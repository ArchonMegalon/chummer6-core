using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed record CharacterCareerCustomDrugPurchaseSnapshot(CharacterCareerCustomDrugPurchaseBinding Binding,
    CharacterCustomDrugPreparation Authority, string SettingsXml, string SettingsAuthority,
    CharacterCustomDrugSelection Recipe, string SavedDrugXmlBefore, decimal QuantityBefore, IReadOnlyList<Guid> InstanceIds);

/// <summary>Bounded additional-dose transaction. Pricing is exclusively CharacterCustomDrugRules.QuantityPurchase.</summary>
public static class CharacterCareerCustomDrugPurchaseTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public const int MaximumContentLength = 67_108_864;
    private const int MaximumRecipeLength = 1_048_576;
    private const string Prefix = "chummer.core.sr5-career-custom-drug-purchase-";
    private static string LedgerRoot => Hash(Prefix + "ledger/v1");
    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);
    public static string CommandDigest(CharacterCareerCustomDrugPurchaseCommand command)
        => Hash(Prefix + "command/v1\0" + Json(command));
    public static string ReceiptDigest(CharacterCareerCustomDrugPurchaseReceipt receipt)
        => Hash(Prefix + "receipt/v1\0" + Json(receipt with { ReceiptDigest = "" }));
    public static bool IsValidOwner(OwnerScope owner) => owner.NormalizedValue is { Length: > 0 and <= 512 }
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsValidRequest(CharacterCareerCustomDrugPurchaseRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.TargetDrugId != Guid.Empty
            && request.AdditionalQuantity is > 0m and <= 1_000_000m
            && decimal.Round(request.AdditionalQuantity, 2) == request.AdditionalQuantity
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal.Ticks % TimeSpan.TicksPerSecond == 0;
    public static bool IsBoundCommand(CharacterCareerCustomDrugPurchaseCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } b
            && b.OwnerId is { Length: > 0 and <= 512 } && new OwnerScope(b.OwnerId).NormalizedValue == b.OwnerId
            && b.WorkspaceId == command.Request.WorkspaceId && b.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(b.SourceDigest) && IsDigest(b.AuxiliaryStateDigest) && IsDigest(b.RuleStateDigest)
            && IsDigest(b.CatalogDigest) && IsDigest(b.RulesDigest) && IsDigest(b.RecipeDigest) && IsDigest(b.RuntimeDigest)
            && b.SettingsProfileId is { Length: > 0 and <= 4096 };
    public static bool IsConfirmedCommand(CharacterCareerCustomDrugPurchaseCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    private static string SourceDigest(WorkspaceDocument document) => Hash(Prefix + "source/v1\0" + Json(new
    { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind, PayloadDigest = Hash(document.Content) }));
    private static CharacterCustomDrugCatalogAuthority Catalog(CharacterCustomDrugPreparation p)
        => new(p.Exact, p.Blockers, "sr5", p.SettingsProfileId, p.CatalogDigest, p.RulesDigest, p.Policy, p.Grades, p.Components);
    private static string RuleDigest(CharacterCustomDrugPreparation p, string settings, string authority)
        => Hash(Prefix + "rules/v1\0" + Json(new { Catalog = Catalog(p), settings, authority }));
    private static bool AcceptedPolicy(CharacterCustomDrugCalculationPolicy policy)
        => policy.QuantityDecimalPlaces == 2 && policy.MaximumQuantity == 1_000_000m;
    private static bool ValidStack(decimal value, CharacterCustomDrugCalculationPolicy policy)
        => value >= 0m && value <= policy.MaximumQuantity && decimal.Round(value, policy.QuantityDecimalPlaces) == value;
    private static CharacterCareerCustomDrugPurchaseCatalogQuantity Bounds(decimal before, CharacterCustomDrugCalculationPolicy policy)
    {
        decimal maximum = policy.MaximumQuantity - before;
        return new(CharacterCareerCustomDrugPurchaseQuantityUnit.Dose, policy.QuantityDecimalPlaces,
            0.01m, maximum, 0.01m, Math.Min(1m, maximum), policy.MaximumQuantity);
    }

    private static bool TryWorkspace(OwnerScope owner, WorkspaceStoredDocument saved, string runtime,
        out XDocument? xml, out decimal nuyen, out Guid[] identities, out string error)
    {
        xml = null; nuyen = 0m; identities = []; error = "custom_drug_workspace_invalid";
        if (!IsValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision || !IsDigest(runtime)
            || saved.Document.Format != WorkspaceDocumentFormat.NativeXml || saved.Document.RulesetId != "sr5"
            || saved.Document.SchemaVersion != 1 || saved.Document.PayloadKind is not (null or "" or "workspace" or "sr5/chum5-xml")) return false;
        xml = ReadXml(saved.Document.Content); var root = xml.Root!;
        if (!bool.TryParse(Scalar(root, "created"), out bool created) || !created) return false;
        nuyen = Decimal(root, "nuyen"); if (nuyen < 0m) return false;
        var drugs = Container(root, "drugs", true)!;
        if (drugs.Elements().Any(e => e.Name != "drug") || drugs.Elements().Count() > 4096) return false;
        var ids = drugs.Elements("drug").Select(e => Guid.Parse(Scalar(e, "guid"))).ToArray();
        error = "custom_drug_target_identity_invalid";
        if (ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length) return false;
        var allIds = root.Descendants("guid").Select(e => Guid.TryParse(e.Value, out var id) ? id : Guid.Empty).ToArray();
        identities = allIds;
        if (ids.Any(id => allIds.Count(value => value == id) != 1)) return false;
        var expenses = Container(root, "expenses", false);
        if (expenses is not null)
        {
            error = "custom_drug_expenses_invalid";
            if (expenses.Elements().Count() >= 100_000 || expenses.Elements().Any(e => e.Name != "expense")) return false;
            var expenseIds = expenses.Elements().Select(e => Guid.Parse(Scalar(e, "guid"))).ToArray();
            if (expenseIds.Any(id => id == Guid.Empty) || expenseIds.Distinct().Count() != expenseIds.Length) return false;
        }
        error = string.Empty; return true;
    }

    private static bool TryAuthority(ICharacterSourceDataContext? context, WorkspaceStoredDocument saved, decimal nuyen,
        out CharacterCustomDrugPreparation? preparation, out string settings, out string authority, out string error)
    {
        preparation = null; settings = authority = string.Empty; error = "custom_drug_source_unavailable";
        if (context is null || !context.TryResolveCareerSettingsProfile(out settings, out authority)
            || settings is not { Length: > 0 and <= 1_048_576 } || authority is not { Length: > 0 and <= 2_097_152 }
            || !context.TryResolveCustomDrugCatalog(out var catalog) || !CharacterCustomDrugRules.IsValidCatalogAuthority(catalog)) return false;
        error = "custom_drug_quantity_policy_changed";
        if (!AcceptedPolicy(catalog.Policy)) return false;
        preparation = CharacterCustomDrugRules.BindPreparation(catalog, CharacterCustomDrugContext.Career,
            CharacterCustomDrugQuotePurpose.QuantityPurchase, saved.ContentRevision,
            CharacterCustomDrugRules.ComputeCharacterDigest(saved.Document.Content), nuyen);
        error = string.Empty; return preparation.Exact;
    }

    private static bool TryRecipe(XElement drug, CharacterCustomDrugPreparation p,
        ICharacterCareerCustomDrugPurchaseComponentValidator? validator, IReadOnlyList<Guid>? identities,
        out CharacterCustomDrugSelection? recipe, out decimal quantity, out string error)
    {
        recipe = null; quantity = 0m; error = "custom_drug_recipe_shape_unsupported";
        if (drug.Name != "drug" || drug.HasAttributes || Scalar(drug, "category") != "Custom Drug"
            || Guid.Parse(Scalar(drug, "sourceid")) != Guid.Empty || Guid.Parse(Scalar(drug, "guid")) == Guid.Empty
            || !bool.TryParse(Scalar(drug, "stolen"), out bool stolen) || stolen) return false;
        quantity = Decimal(drug, "quantity");
        error = "custom_drug_stack_quantity_out_of_bounds";
        if (!ValidStack(quantity, p.Policy) || quantity >= p.Policy.MaximumQuantity) return false;
        var grades = p.Grades.Where(g => g.Name == Scalar(drug, "grade")).ToArray();
        error = "custom_drug_grade_unavailable_or_ambiguous";
        if (grades.Length != 1) return false;
        var container = Container(drug, "drugcomponents", true)!;
        if (container.Elements().Any(e => e.Name != "drugcomponent")) return false;
        var components = container.Elements("drugcomponent").ToArray();
        error = "custom_drug_components_invalid";
        if (components.Length == 0 || components.Length > p.Policy.MaximumComponents) return false;
        var selected = new List<CharacterCustomDrugComponentSelection>(); var instances = new HashSet<Guid>();
        foreach (var component in components)
        {
            var id = new CharacterCustomDrugComponentId(Guid.Parse(Scalar(component, "sourceid")));
            Guid instance = Guid.Parse(Scalar(component, "guid"));
            if (instance == Guid.Empty || !instances.Add(instance) || instance == Guid.Parse(Scalar(drug, "guid"))
                || identities is not null && identities.Count(value => value == instance) != 1) return false;
            int level = int.Parse(Scalar(component, "level"), CultureInfo.InvariantCulture);
            var matches = p.Components.Where(c => c.Id == id).ToArray();
            error = "custom_drug_component_source_mismatch";
            if (matches.Length != 1 || !matches[0].Effects.Any(e => e.Level == level)) return false;
            // New admission checks the entire canonical saved component, including cached price and effects.
            // Historical lookup has immutable evidence and does not consult today's encoder or catalog.
            if (validator is not null && !validator.MatchesSavedComponent(
                component.ToString(SaveOptions.DisableFormatting), matches[0], level, instance)) return false;
            if (Decimal(component, "cost") != matches[0].CostPerLevel) return false;
            selected.Add(new(id, level));
        }
        recipe = new(Scalar(drug, "name"), grades[0].Id, 1m, false, false, 0m, selected.ToArray());
        var definition = CharacterCustomDrugRules.Quote(p with { Purpose = CharacterCustomDrugQuotePurpose.RecipeDefinition }, recipe);
        error = definition.BlockReason;
        if (!definition.Exact) return false;
        // Canonical recipes have no drug-level cached cost. If imported, it must agree exactly.
        if (Container(drug, "cost", false) is not null && Decimal(drug, "cost") != definition.UnitCost)
        { error = "custom_drug_saved_price_mismatch"; return false; }
        error = string.Empty; return true;
    }

    private static CharacterCareerCustomDrugPurchaseBinding Binding(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCustomDrugPreparation p, string settings, string authority, string recipe, string runtime)
        => new(owner.NormalizedValue, saved.Id, saved.ContentRevision, SourceDigest(saved.Document), saved.Document.AuxiliaryStateDigest,
            RuleDigest(p, settings, authority), p.CatalogDigest, p.RulesDigest, p.SettingsProfileId, Hash(recipe), runtime);

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, Guid target,
        ICharacterSourceDataResolver sources, ICharacterCareerCustomDrugPurchaseComponentValidator validator, string runtime,
        out CharacterCareerCustomDrugPurchaseSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "custom_drug_input_unresolved";
        try
        {
            if (target == Guid.Empty || !TryWorkspace(owner, saved, runtime, out var xml, out var nuyen, out var identities, out error)) return false;
            var context = sources.TryCreateContext(saved.Document.Content);
            if (!TryAuthority(context, saved, nuyen, out var p, out var settings, out var authority, out error)) return false;
            var drug = xml!.Root!.Element("drugs")!.Elements("drug").SingleOrDefault(e => Guid.Parse(Scalar(e, "guid")) == target);
            if (drug is null) { error = "custom_drug_target_not_found"; return false; }
            if (!TryRecipe(drug, p!, validator, identities, out var recipe, out var quantity, out error)) return false;
            string drugXml = drug.ToString(SaveOptions.DisableFormatting);
            if (drugXml.Length > MaximumRecipeLength) return false;
            var binding = Binding(owner, saved, p!, settings, authority, drugXml, runtime);
            if (!TryAuthority(context, saved, nuyen, out var after, out var settingsAfter, out var authorityAfter, out error)
                || RuleDigest(after!, settingsAfter, authorityAfter) != binding.RuleStateDigest)
            { error = "custom_drug_source_changed"; return false; }
            snapshot = new(binding, p!, settings, authority, recipe!, drugXml, quantity, Array.AsReadOnly(identities));
            return true;
        }
        catch (Exception ex) when (ReadFailure(ex)) { error = "custom_drug_input_unresolved"; return false; }
    }

    internal static bool TryReadCatalog(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        ICharacterCareerCustomDrugPurchaseComponentValidator validator, string runtime,
        out CharacterCareerCustomDrugPurchaseCatalog? catalog, out string error)
    {
        catalog = null; error = "custom_drug_catalog_unresolved";
        try
        {
            if (!TryWorkspace(owner, saved, runtime, out var xml, out var nuyen, out var identities, out error)) return false;
            if (saved.Document.AuxiliaryState.CharacterCareerCustomDrugPurchaseReceipts is { Count: >= MaximumReceipts })
            { error = "custom_drug_receipt_capacity_exhausted"; return false; }
            var context = sources.TryCreateContext(saved.Document.Content);
            if (!TryAuthority(context, saved, nuyen, out var p, out var settings, out var authority, out error)) return false;
            var rows = new List<CharacterCareerCustomDrugPurchaseCatalogRow>();
            foreach (var drug in xml!.Root!.Element("drugs")!.Elements("drug"))
            {
                Guid id = Guid.Parse(Scalar(drug, "guid"));
                bool supported; decimal quantity = 0m; string blocker;
                try { supported = TryRecipe(drug, p!, validator, identities, out _, out quantity, out blocker); }
                catch (Exception ex) when (ReadFailure(ex)) { supported = false; blocker = "custom_drug_recipe_shape_unsupported"; }
                string recipeXml = drug.ToString(SaveOptions.DisableFormatting);
                if (recipeXml.Length > MaximumRecipeLength) { supported = false; blocker = "custom_drug_recipe_size_limit"; }
                rows.Add(new(id, drug.Element("name")?.Value ?? "", drug.Element("grade")?.Value ?? "", quantity, supported,
                    supported ? Bounds(quantity, p!.Policy) : null, supported ? [] : [blocker], supported
                        ? new(id, Binding(owner, saved, p!, settings, authority, recipeXml, runtime)) : null));
            }
            var binding = Binding(owner, saved, p!, settings, authority, "", runtime);
            if (!TryAuthority(context, saved, nuyen, out var after, out var settingsAfter, out var authorityAfter, out error)
                || RuleDigest(after!, settingsAfter, authorityAfter) != binding.RuleStateDigest)
            { error = "custom_drug_source_changed"; return false; }
            catalog = new(binding, Array.AsReadOnly(rows.ToArray())); error = string.Empty; return true;
        }
        catch (Exception ex) when (ReadFailure(ex)) { error = "custom_drug_catalog_unresolved"; return false; }
    }

    internal static bool IsWorkspaceCurrent(WorkspaceStoredDocument saved, CharacterCareerCustomDrugPurchaseBinding binding)
        => saved.Id == binding.WorkspaceId && saved.ContentRevision == binding.WorkspaceRevision
            && saved.SavedRevision == saved.ContentRevision && saved.Document.AuxiliaryStateDigest == binding.AuxiliaryStateDigest
            && SourceDigest(saved.Document) == binding.SourceDigest;
    public static bool IsSourceCurrent(OwnerScope owner, WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        ICharacterCareerCustomDrugPurchaseComponentValidator validator, Guid target, CharacterCareerCustomDrugPurchaseBinding binding, string runtime)
        => TryRead(owner, saved, target, sources, validator, runtime, out var current, out _) && current!.Binding == binding;
    private static Guid ExpenseIdentity(CharacterCareerCustomDrugPurchaseCommand command)
        => new(Convert.FromHexString(Hash(Prefix + "expense/v1\0" + Json(new
        { command.Binding.OwnerId, command.Request.WorkspaceId, command.Request.OperationId }))[..32]));

    public static bool TryPreview(CharacterCareerCustomDrugPurchaseSnapshot snapshot, CharacterCareerCustomDrugPurchaseRequest request,
        out CharacterCareerCustomDrugPurchasePreview? preview, out string error)
    {
        preview = null; error = "custom_drug_request_conflict";
        try
        {
            var command = new CharacterCareerCustomDrugPurchaseCommand(request, snapshot.Binding);
            if (!IsBoundCommand(command) || Guid.Parse(Scalar(XElement.Parse(snapshot.SavedDrugXmlBefore), "guid")) != request.TargetDrugId
                || snapshot.Binding.RecipeDigest != Hash(snapshot.SavedDrugXmlBefore)
                || snapshot.Binding.RuleStateDigest != RuleDigest(snapshot.Authority, snapshot.SettingsXml, snapshot.SettingsAuthority)) return false;
            decimal after = checked(snapshot.QuantityBefore + request.AdditionalQuantity);
            if (!ValidStack(after, snapshot.Authority.Policy)) { error = "custom_drug_resulting_quantity_out_of_bounds"; return false; }
            var recipe = snapshot.Recipe with { Quantity = request.AdditionalQuantity };
            var price = CharacterCustomDrugRules.Quote(snapshot.Authority, recipe);
            if (!price.Exact) { error = price.BlockReason; return false; }
            Guid expense = ExpenseIdentity(command);
            if (snapshot.InstanceIds.Contains(expense)) { error = "custom_drug_expense_identity_conflict"; return false; }
            var quote = new CharacterCareerCustomDrugPurchaseQuote(snapshot.Authority, snapshot.SettingsXml, snapshot.SettingsAuthority,
                recipe, snapshot.SavedDrugXmlBefore, price, snapshot.QuantityBefore, after, snapshot.Authority.AvailableNuyen,
                checked(snapshot.Authority.AvailableNuyen + price.NuyenDelta), expense);
            preview = CreatePreview(command, quote); error = string.Empty; return true;
        }
        catch (Exception ex) when (ReadFailure(ex)) { error = "custom_drug_quote_unresolved"; return false; }
    }
    private static CharacterCareerCustomDrugPurchasePreview CreatePreview(CharacterCareerCustomDrugPurchaseCommand command,
        CharacterCareerCustomDrugPurchaseQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash(Prefix + "preview/v1\0" + Json(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerCustomDrugPurchaseReceipt? receipt)
    {
        try
        {
            if (receipt is null || !IsConfirmedCommand(receipt.Command) || receipt.Quote is not { } q
                || !IsDigest(receipt.CommandDigest) || !IsDigest(receipt.CharacterPayloadDigestAfter)
                || !IsDigest(receipt.PreviousReceiptDigest) || !IsDigest(receipt.ReceiptDigest)
                || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1
                || q.SavedDrugXmlBefore is not { Length: > 0 and <= MaximumRecipeLength }
                || q.SettingsXml is not { Length: > 0 and <= 1_048_576 } || q.SettingsAuthority is not { Length: > 0 and <= 2_097_152 }
                || !CharacterCustomDrugRules.IsValidPreparation(q.Authority)
                || !CharacterCustomDrugRules.IsValidCatalogAuthority(Catalog(q.Authority)) || !AcceptedPolicy(q.Authority.Policy)
                || q.Authority.Context != CharacterCustomDrugContext.Career || q.Authority.Purpose != CharacterCustomDrugQuotePurpose.QuantityPurchase
                || q.Authority.ContentRevision != receipt.Command.Binding.WorkspaceRevision
                || q.Authority.AvailableNuyen != q.NuyenBefore) return false;
            var b = receipt.Command.Binding; var request = receipt.Command.Request;
            if (b.CatalogDigest != q.Authority.CatalogDigest || b.RulesDigest != q.Authority.RulesDigest
                || b.SettingsProfileId != q.Authority.SettingsProfileId || b.RecipeDigest != Hash(q.SavedDrugXmlBefore)
                || b.RuleStateDigest != RuleDigest(q.Authority, q.SettingsXml, q.SettingsAuthority)) return false;
            var drug = XElement.Parse(q.SavedDrugXmlBefore);
            if (Guid.Parse(Scalar(drug, "guid")) != request.TargetDrugId
                || !TryRecipe(drug, q.Authority, null, null, out var recipe, out var quantity, out _)
                || quantity != q.QuantityBefore || Json(recipe! with { Quantity = request.AdditionalQuantity }) != Json(q.Recipe)
                || q.QuantityAfter != checked(quantity + request.AdditionalQuantity) || !ValidStack(q.QuantityAfter, q.Authority.Policy)
                || q.ExpenseId != ExpenseIdentity(receipt.Command)) return false;
            // Pure historical verification against retained evidence; no live source, profile, or runtime lookup.
            var expected = CharacterCustomDrugRules.Quote(q.Authority, q.Recipe);
            return expected.Exact && Json(expected) == Json(q.Price) && q.NuyenAfter == checked(q.NuyenBefore + expected.NuyenDelta)
                && receipt.CommandDigest == CommandDigest(receipt.Command)
                && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, q).PreviewDigest
                && receipt.ReceiptDigest == ReceiptDigest(receipt);
        }
        catch (Exception ex) when (ReadFailure(ex)) { return false; }
    }
    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, IReadOnlyList<CharacterCareerCustomDrugPurchaseReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = []; HashSet<Guid> expenses = [];
        string previous = LedgerRoot; long lastRevision = 0, bytes = 2; string? owner = null;
        foreach (var receipt in receipts)
        {
            if (!IsCoherent(receipt) || receipt.Command.Request.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId)
                || !expenses.Add(receipt.Quote.ExpenseId)) return false;
            owner ??= receipt.Command.Binding.OwnerId;
            if (owner != receipt.Command.Binding.OwnerId) return false;
            // Distinct operations may legitimately purchase more doses for the SAME drug identity.
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = receipt.CommittedWorkspaceRevision; previous = receipt.ReceiptDigest;
        }
        return true;
    }
    public static bool IsValidHistory(WorkspaceStoredDocument saved)
        => IsValidHistory(saved.Id, saved.Document, saved.ContentRevision, saved.SavedRevision);
    public static bool IsValidHistory(CharacterWorkspaceId id, WorkspaceDocument document, long revision, long savedRevision)
    {
        var history = document.AuxiliaryState.CharacterCareerCustomDrugPurchaseReceipts;
        return IsValidLedger(id, revision, history)
            && (history is not { Count: > 0 } || savedRevision >= history[^1].CommittedWorkspaceRevision
                && (revision != history[^1].CommittedWorkspaceRevision || Hash(document.Content) == history[^1].CharacterPayloadDigestAfter));
    }
    public static CharacterCareerCustomDrugPurchaseResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operation, string digest)
    {
        if (!IsValidOwner(owner) || operation == Guid.Empty || !IsDigest(digest) || !IsValidHistory(saved))
            return new(CharacterCareerCustomDrugPurchaseOutcome.Corrupt, Error: "custom_drug_history_invalid");
        var history = saved.Document.AuxiliaryState.CharacterCareerCustomDrugPurchaseReceipts;
        if (history?.Any(r => r.Command.Binding.OwnerId != owner.NormalizedValue) == true)
            return new(CharacterCareerCustomDrugPurchaseOutcome.Conflict, Error: "custom_drug_owner_mismatch");
        var receipt = history?.SingleOrDefault(r => r.Command.Request.OperationId == operation);
        if (receipt is null) return new(CharacterCareerCustomDrugPurchaseOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision) && receipt.CommandDigest == digest
            ? new(CharacterCareerCustomDrugPurchaseOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerCustomDrugPurchaseOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision, Error: "custom_drug_operation_conflict");
    }
    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved, CharacterCareerCustomDrugPurchaseSnapshot snapshot,
        CharacterCareerCustomDrugPurchaseCommand command, out WorkspaceDocument? replacement, out CharacterCareerCustomDrugPurchaseReceipt? receipt)
    {
        replacement = null; receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || !IsWorkspaceCurrent(saved, command.Binding) || snapshot.Binding != command.Binding
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerCustomDrugPurchaseReceipts;
        if (history is { Count: >= MaximumReceipts } || history?.Any(r => r.Command.Request.OperationId == command.Request.OperationId
            || r.Command.Binding.OwnerId != owner.NormalizedValue) == true) return false;
        try
        {
            var xml = ReadXml(saved.Document.Content); var root = xml.Root!;
            var drug = root.Element("drugs")!.Elements("drug").Single(e => Guid.Parse(Scalar(e, "guid")) == command.Request.TargetDrugId);
            if (drug.ToString(SaveOptions.DisableFormatting) != snapshot.SavedDrugXmlBefore) return false;
            drug.Element("quantity")!.Value = preview.Quote.QuantityAfter.ToString(CultureInfo.InvariantCulture);
            root.Element("nuyen")!.Value = preview.Quote.NuyenAfter.ToString(CultureInfo.InvariantCulture);
            var expenses = root.Element("expenses"); if (expenses is null) { expenses = new XElement("expenses"); root.Add(expenses); }
            expenses.Add(new XElement("expense", new XElement("guid", preview.Quote.ExpenseId.ToString("D")),
                new XElement("date", command.Request.ExpenseDateLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement("type", "Nuyen"), new XElement("amount", preview.Quote.Price.NuyenDelta.ToString(CultureInfo.InvariantCulture)),
                new XElement("reason", "Purchase custom drug doses: " + preview.Quote.Price.Name),
                new XElement("refund", "False"), new XElement("forcecareervisible", "False")));
            // No undo descriptor: this slice does not authorize generic purchase undo.
            string payload = xml.ToString(SaveOptions.DisableFormatting); if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerCustomDrugPurchaseReceipt(command, preview.Quote, CommandDigest(command),
                saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with { Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerCustomDrugPurchaseReceipts = Array.AsReadOnly(appended) } } };
            receipt = candidate; return true;
        }
        catch (Exception ex) when (ReadFailure(ex)) { return false; }
    }

    private static bool ReadFailure(Exception ex) => ex is XmlException or IOException or UnauthorizedAccessException
        or FormatException or OverflowException or ArgumentException or InvalidOperationException or NullReferenceException;
    private static XDocument ReadXml(string content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > MaximumContentLength) throw new InvalidDataException();
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var xml = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (xml.Root?.Name != "character" || xml.Root.HasAttributes) throw new InvalidDataException();
        return xml;
    }
    private static XElement? Container(XElement parent, string name, bool required)
    {
        var nodes = parent.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1 || required && nodes.Length != 1 || nodes.Any(e => e.HasAttributes)) throw new InvalidDataException();
        return nodes.SingleOrDefault();
    }
    private static string Scalar(XElement parent, string name)
    {
        var node = Container(parent, name, true)!; if (node.HasElements) throw new InvalidDataException(); return node.Value;
    }
    private static decimal Decimal(XElement parent, string name)
        => decimal.Parse(Scalar(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
}
