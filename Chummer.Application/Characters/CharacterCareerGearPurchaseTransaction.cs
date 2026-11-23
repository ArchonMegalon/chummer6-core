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

public sealed record CharacterCareerGearPurchaseSnapshot(
    CharacterCareerGearPurchaseBinding Binding, CharacterCareerGearPurchaseSource Source,
    string SettingsXml, string SettingsAuthority, decimal Nuyen,
    IReadOnlyList<Guid> ExpenseIds, IReadOnlyList<Guid> GearIds);

/// <summary>Pure preparation and durable evidence checks. Only the dedicated leased store may persist a payment.</summary>
public static class CharacterCareerGearPurchaseTransaction
{
    public const int MaximumReceipts = 1024;
    public const int MaximumLedgerBytes = 4 * 1024 * 1024;
    public const int MaximumContentLength = 67_108_864;
    private static string LedgerRoot => Hash("chummer.core.sr5-career-gear-purchase-ledger/v1");

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool IsDigest(string? value) => value is { Length: 64 }
        && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string CommandDigest(CharacterCareerGearPurchaseCommand command)
        => Hash("chummer.core.sr5-career-gear-purchase-command/v1\0" + JsonSerializer.Serialize(command));
    public static string ReceiptDigest(CharacterCareerGearPurchaseReceipt receipt)
        => Hash("chummer.core.sr5-career-gear-purchase-receipt/v1\0" + JsonSerializer.Serialize(receipt with { ReceiptDigest = "" }));

    public static bool IsValidOwner(OwnerScope owner) => owner.NormalizedValue is { Length: > 0 and <= 512 }
        && (!owner.UsesLocalSingleUserValue || owner.IsLocalSingleUser);
    public static bool IsValidRequest(CharacterCareerGearPurchaseRequest? request)
        => request is not null && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(request.WorkspaceId)
            && request.OperationId != Guid.Empty && request.SourceId != Guid.Empty
            && request.Rating >= 0 && request.Quantity > 0m && request.Quantity <= CharacterGearQuantityRules.MaximumQuantity
            && CharacterCareerGearPurchaseRules.IsMarkupValid(request.MarkupPercent)
            && request.ParentGearId != Guid.Empty && request.StackTargetId != Guid.Empty
            && (request.Ammunition is null || request.Ammunition.WeaponCategory is { Length: > 0 and <= 1024 } category
                && category == category.Trim() && !category.Any(char.IsControl) && IsDigest(request.Ammunition.AuthorityDigest))
            && request.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
            && request.ExpenseDateLocal.Ticks % TimeSpan.TicksPerSecond == 0;

    public static bool IsBoundCommand(CharacterCareerGearPurchaseCommand? command)
        => command is not null && IsValidRequest(command.Request) && command.Binding is { } binding
            && binding.OwnerId is { Length: > 0 and <= 512 }
            && new OwnerScope(binding.OwnerId).NormalizedValue == binding.OwnerId
            && binding.WorkspaceId == command.Request.WorkspaceId
            && binding.WorkspaceRevision is > 0 and < long.MaxValue
            && IsDigest(binding.SourceDigest) && IsDigest(binding.AuxiliaryStateDigest)
            && IsDigest(binding.RuleStateDigest) && IsDigest(binding.RuntimeDigest);

    public static bool IsConfirmedCommand(CharacterCareerGearPurchaseCommand? command)
        => IsBoundCommand(command) && command!.ExplicitlyConfirmed && IsDigest(command.ExpectedPreviewDigest);

    public static bool TryRead(OwnerScope owner, WorkspaceStoredDocument saved, Guid sourceId,
        ICharacterSourceDataResolver sources, string runtimeDigest,
        out CharacterCareerGearPurchaseSnapshot? snapshot, out string error)
    {
        snapshot = null; error = "gear_workspace_invalid";
        if (!IsValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || sourceId == Guid.Empty || !IsDigest(runtimeDigest)) return false;
        if (!TryReadWorkspace(owner, saved, runtimeDigest, out decimal nuyen, out var expenseIds, out var gearIds, out error)) return false;
        var document = saved.Document;
        try
        {
            error = "gear_source_unavailable";
            var context = sources.TryCreateContext(document.Content);
            if (!TryReadRuleState(context, sourceId, out var source, out var settings, out var authority, out var rules)) return false;
            var binding = new CharacterCareerGearPurchaseBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                SourceDigest(document), document.AuxiliaryStateDigest, rules, runtimeDigest);
            if (!TryReadRuleState(context, sourceId, out _, out _, out _, out var after) || after != rules)
            { error = "gear_source_changed"; return false; }
            snapshot = new(binding, source!, settings, authority, nuyen, Array.AsReadOnly(expenseIds), Array.AsReadOnly(gearIds));
            error = string.Empty; return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "gear_input_unresolved"; return false; }
    }

    private static bool TryReadWorkspace(OwnerScope owner, WorkspaceStoredDocument saved, string runtimeDigest,
        out decimal nuyen, out Guid[] expenseIds, out Guid[] gearIds, out string error)
    {
        nuyen = 0m;
        expenseIds = gearIds = [];
        error = "gear_workspace_invalid";
        if (!IsValidOwner(owner) || !CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(saved.Id)
            || saved.ContentRevision is <= 0 or long.MaxValue || saved.SavedRevision != saved.ContentRevision
            || !IsDigest(runtimeDigest)) return false;
        var document = saved.Document;
        error = "gear_requires_saved_sr5_career";
        if (document.Format != WorkspaceDocumentFormat.NativeXml || document.RulesetId != "sr5"
            || document.SchemaVersion != 1 || document.PayloadKind is not ("workspace" or "sr5/chum5-xml")) return false;
        try
        {
            var root = ReadXml(document.Content).Root!;
            if (!bool.TryParse(Scalar(root, "created"), out bool career) || !career) return false;
            nuyen = Decimal(root, "nuyen");
            if (nuyen < 0m) return false;
            var gears = Container(root, "gears", required: false);
            if (gears is not null && gears.Elements().Any(row => row.Name != "gear")) return false;
            // Inspect identities only; existing children, effects and opaque inventory content are retained verbatim.
            var gearNodes = new List<XElement>();
            var pending = new Queue<XElement>(gears?.Elements("gear") ?? []);
            while (pending.TryDequeue(out var gear))
            {
                if (gearNodes.Count >= 100_000) return false;
                gearNodes.Add(gear);
                var children = Container(gear, "children", required: false);
                if (children?.Elements().Any(row => row.Name != "gear") == true) return false;
                foreach (var child in children?.Elements("gear") ?? []) pending.Enqueue(child);
            }
            if (gearNodes.Count >= 100_000) return false;
            gearIds = gearNodes.Select(row => Guid.Parse(Scalar(row, "guid"))).ToArray();
            if (gearIds.Any(id => id == Guid.Empty) || gearIds.Distinct().Count() != gearIds.Length) return false;
            var expenses = Container(root, "expenses", required: false);
            if (expenses is not null && expenses.Elements().Any(row => row.Name != "expense")) return false;
            var expenseRows = expenses?.Elements("expense").ToArray() ?? [];
            if (expenseRows.Length >= 100_000) return false;
            expenseIds = expenseRows.Select(row => Guid.Parse(Scalar(row, "guid"))).ToArray();
            if (expenseIds.Any(id => id == Guid.Empty) || expenseIds.Distinct().Count() != expenseIds.Length) return false;
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "gear_input_unresolved"; return false; }
    }

    internal static bool TryReadCatalog(OwnerScope owner, WorkspaceStoredDocument saved,
        ICharacterSourceDataResolver resolver, string runtimeDigest,
        out CharacterCareerGearPurchaseCatalog? catalog, out string error)
    {
        catalog = null;
        if (!TryReadWorkspace(owner, saved, runtimeDigest, out _, out _, out _, out error)) return false;
        if (saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts is { Count: >= MaximumReceipts })
        { error = "gear_receipt_capacity_exhausted"; return false; }
        try
        {
            var context = resolver.TryCreateContext(saved.Document.Content);
            if (!TryReadCatalogInputs(context, out var sources, out string settings, out string authority, out error)) return false;
            if (sources.Count == 0) { error = "gear_catalog_empty"; return false; }
            var rows = new List<CharacterCareerGearPurchaseCatalogRow>();
            var rules = new List<string>();
            string payloadDigest = SourceDigest(saved.Document);
            int ratingChoices = 0;
            foreach (var source in sources)
            {
                string ruleDigest = RuleDigest(source, settings, authority);
                var binding = new CharacterCareerGearPurchaseBinding(owner.NormalizedValue, saved.Id, saved.ContentRevision,
                    payloadDigest, saved.Document.AuxiliaryStateDigest, ruleDigest, runtimeDigest);
                var row = CharacterCreationLegacySourceProjector.DescribeCareerGearPurchaseSource(source, settings, binding);
                ratingChoices += row.Ratings?.Values.Count ?? 0;
                if (ratingChoices > 16_384) { error = "gear_catalog_rating_budget_exceeded"; return false; }
                rows.Add(row);
                rules.Add(ruleDigest);
            }
            // Re-read the same context: source capture must detect drift during enumeration/projection.
            if (!TryReadCatalogInputs(context, out var after, out string settingsAfter, out string authorityAfter, out _)
                || settings != settingsAfter || authority != authorityAfter || !sources.Select(item => RuleDigest(item, settings, authority))
                    .SequenceEqual(after.Select(item => RuleDigest(item, settingsAfter, authorityAfter))))
            { error = "gear_source_changed"; return false; }
            string digest = Hash("chummer.core.sr5-career-gear-catalog/v1\0" + JsonSerializer.Serialize(rules));
            catalog = new(new(owner.NormalizedValue, saved.Id, saved.ContentRevision, payloadDigest,
                saved.Document.AuxiliaryStateDigest, digest, runtimeDigest), Array.AsReadOnly(rows.ToArray()));
            error = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { error = "gear_catalog_input_unresolved"; return false; }
    }

    private static bool TryReadCatalogInputs(ICharacterSourceDataContext? context,
        out IReadOnlyList<CharacterCareerGearPurchaseSource> sources, out string settings, out string authority, out string error)
    {
        sources = [];
        settings = authority = string.Empty;
        error = "gear_source_unavailable";
        if (context is null || !context.TryResolveCareerSettingsProfile(out settings, out authority)
            || settings is not { Length: > 0 and <= 1_048_576 } || string.IsNullOrWhiteSpace(settings)
            || authority is not { Length: > 0 and <= 2_097_152 } || string.IsNullOrWhiteSpace(authority)) return false;
        error = "gear_catalog_source_unavailable";
        if (!context.TryResolveCareerGearPurchaseCatalog(out var resolved) || resolved is null) return false;
        if (resolved.Count > 4096) { error = "gear_catalog_row_limit"; return false; }
        if (resolved.Any(source => !IsValidSource(source)) || resolved.Select(source => source.SourceId).Distinct().Count() != resolved.Count)
            return false;
        if (resolved.Sum(source => (long)source.SourceXml.Length) > 16 * 1024 * 1024)
        { error = "gear_catalog_source_size_limit"; return false; }
        if (resolved.Count > 0 && resolved.Any(source => source.CatalogDigest != resolved[0].CatalogDigest
            || source.ProfileDigest != resolved[0].ProfileDigest || source.CustomDataDigest != resolved[0].CustomDataDigest))
        { error = "gear_catalog_source_identity_mismatch"; return false; }
        sources = Array.AsReadOnly(resolved.OrderBy(source => source.SourceId)
            .Select(source => CharacterCreationLegacySourceProjector.BindCareerAmmunitionSource(source, context)).ToArray());
        error = string.Empty;
        return true;
    }

    internal static bool IsWorkspaceCurrent(WorkspaceStoredDocument saved, CharacterCareerGearPurchaseBinding binding)
        => saved.Id == binding.WorkspaceId && saved.ContentRevision == binding.WorkspaceRevision
            && saved.SavedRevision == saved.ContentRevision && saved.Document.AuxiliaryStateDigest == binding.AuxiliaryStateDigest
            && SourceDigest(saved.Document) == binding.SourceDigest;

    public static bool IsSourceCurrent(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        Guid sourceId, CharacterCareerGearPurchaseBinding binding)
        => SourceDigest(saved.Document) == binding.SourceDigest
            && TryReadRuleState(sources.TryCreateContext(saved.Document.Content), sourceId, out _, out _, out _, out var rules)
            && rules == binding.RuleStateDigest;

    private static bool TryReadRuleState(ICharacterSourceDataContext? context, Guid sourceId,
        out CharacterCareerGearPurchaseSource? source, out string settings, out string authority, out string digest)
    {
        source = null; settings = authority = digest = string.Empty;
        if (context is null || !context.TryResolveCareerSettingsProfile(out settings, out authority)
            || string.IsNullOrWhiteSpace(settings) || string.IsNullOrWhiteSpace(authority)
            || !context.TryResolveCareerGearPurchaseSource(sourceId, out source) || !IsValidSource(source)
            || source!.SourceId != sourceId) return false;
        source = CharacterCreationLegacySourceProjector.BindCareerAmmunitionSource(source, context);
        digest = RuleDigest(source, settings, authority);
        return true;
    }

    private static bool IsValidSource(CharacterCareerGearPurchaseSource? source)
        => source is not null && source.SourceId != Guid.Empty && source.Name is { Length: > 0 and <= 1024 }
            && source.SourceBook is { Length: > 0 and <= 1024 }
            && source.SourceXml is { Length: > 0 and <= CharacterCreationGearRules.MaximumSourceNodeLength }
            && IsSourceDigest(source.CatalogDigest) && IsSourceDigest(source.ProfileDigest) && IsSourceDigest(source.CustomDataDigest);

    private static bool IsSourceDigest(string? value) => IsDigest(value)
        || value is { Length: 71 } && value.StartsWith("sha256:", StringComparison.Ordinal) && IsDigest(value[7..]);

    private static string RuleDigest(CharacterCareerGearPurchaseSource source, string settings, string authority)
        => Hash("chummer.core.sr5-career-gear-purchase-rules/v1\0" + JsonSerializer.Serialize(new { source, settings, authority }));

    private static string SourceDigest(WorkspaceDocument document)
        => Hash("chummer.core.sr5-career-gear-purchase-source/v1\0" + JsonSerializer.Serialize(new
        { document.Format, document.RulesetId, document.SchemaVersion, document.PayloadKind, PayloadDigest = Hash(document.Content) }));

    public static bool TryPreview(CharacterCareerGearPurchaseSnapshot snapshot,
        CharacterCareerGearPurchaseRequest request, out CharacterCareerGearPurchasePreview? preview, out string error)
    {
        preview = null; error = "gear_request_conflict";
        var command = new CharacterCareerGearPurchaseCommand(request, snapshot.Binding);
        if (!IsBoundCommand(command) || snapshot.Source.SourceId != request.SourceId
            || snapshot.Binding.RuleStateDigest != RuleDigest(snapshot.Source, snapshot.SettingsXml, snapshot.SettingsAuthority)) return false;
        if (!TryQuote(command, snapshot.Source, snapshot.SettingsXml, snapshot.SettingsAuthority, snapshot.Nuyen, out var quote, out error)) return false;
        if (snapshot.ExpenseIds.Contains(quote!.ExpenseId) || snapshot.GearIds.Contains(quote.GearId))
        { error = "gear_instance_identity_conflict"; return false; }
        preview = CreatePreview(command, quote); error = string.Empty; return true;
    }

    private static Guid Identity(CharacterCareerGearPurchaseCommand command, string kind)
        => new(Convert.FromHexString(Hash("chummer.core.sr5-career-gear-purchase-" + kind + "/v1\0"
            + JsonSerializer.Serialize(new { command.Binding.OwnerId, command.Request.WorkspaceId, command.Request.OperationId }))[..32]));

    private static bool TryQuote(CharacterCareerGearPurchaseCommand command, CharacterCareerGearPurchaseSource source,
        string settings, string authority, decimal nuyen, out CharacterCareerGearPurchaseQuote? quote, out string error)
    {
        quote = null; error = "gear_quote_unavailable";
        if (!IsValidSource(source) || nuyen < 0m || settings is not { Length: > 0 and <= 1_048_576 }
            || authority is not { Length: > 0 and <= 2_097_152 }) return false;
        Guid gear = Identity(command, "item"), expense = Identity(command, "expense");
        if (!CharacterCreationLegacySourceProjector.TryBuildCareerPurchasedGear(source, command.Request, settings, gear,
            out string xml, out decimal unit, out decimal total, out int availability, out string legality, out error)) return false;
        if (total > nuyen) { error = "gear_insufficient_funds"; return false; }
        quote = new(source, settings, authority, nuyen, unit, total, nuyen - total, availability, legality, gear, expense, xml);
        error = string.Empty; return true;
    }

    private static CharacterCareerGearPurchasePreview CreatePreview(CharacterCareerGearPurchaseCommand command,
        CharacterCareerGearPurchaseQuote quote)
    {
        var unsigned = command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null };
        string digest = Hash("chummer.core.sr5-career-gear-purchase-preview/v1\0" + JsonSerializer.Serialize(new { Command = unsigned, Quote = quote }));
        return new(unsigned with { ExpectedPreviewDigest = digest }, quote, digest);
    }

    public static bool IsCoherent(CharacterCareerGearPurchaseReceipt? receipt)
    {
        if (receipt is null || !IsConfirmedCommand(receipt.Command) || receipt.Quote is not { } quote
            || !IsValidSource(quote.Source) || !IsDigest(receipt.CommandDigest)
            || !IsDigest(receipt.CharacterPayloadDigestAfter) || !IsDigest(receipt.PreviousReceiptDigest)
            || !IsDigest(receipt.ReceiptDigest) || receipt.CommittedWorkspaceRevision != receipt.Command.Binding.WorkspaceRevision + 1
            || !TryQuote(receipt.Command, quote.Source, quote.SettingsXml, quote.SettingsAuthority, quote.NuyenBefore, out var expected, out _))
            return false;
        return quote == expected && receipt.Command.Binding.RuleStateDigest == RuleDigest(quote.Source, quote.SettingsXml, quote.SettingsAuthority)
            && receipt.CommandDigest == CommandDigest(receipt.Command)
            && receipt.Command.ExpectedPreviewDigest == CreatePreview(receipt.Command, expected!).PreviewDigest
            && receipt.ReceiptDigest == ReceiptDigest(receipt);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision,
        IReadOnlyList<CharacterCareerGearPurchaseReceipt>? receipts)
    {
        if (receipts is null) return true;
        if (revision <= 0 || receipts.Count > MaximumReceipts) return false;
        HashSet<Guid> operations = [];
        HashSet<Guid> expenses = [];
        HashSet<Guid> items = [];
        string previous = LedgerRoot;
        long lastRevision = 0, bytes = 2;
        foreach (var receipt in receipts)
        {
            if (!IsCoherent(receipt) || receipt.Command.Request.WorkspaceId != id
                || receipt.CommittedWorkspaceRevision > revision || receipt.CommittedWorkspaceRevision <= lastRevision
                || receipt.PreviousReceiptDigest != previous || !operations.Add(receipt.Command.Request.OperationId)
                || !expenses.Add(receipt.Quote.ExpenseId) || !items.Add(receipt.Quote.GearId)) return false;
            bytes += JsonSerializer.SerializeToUtf8Bytes(receipt).Length + 1;
            if (bytes > MaximumLedgerBytes) return false;
            lastRevision = receipt.CommittedWorkspaceRevision;
            previous = receipt.ReceiptDigest;
        }
        return true;
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var history = saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts;
        return IsValidLedger(saved.Id, saved.ContentRevision, history)
            && (history is not { Count: > 0 } || (saved.SavedRevision >= history[^1].CommittedWorkspaceRevision
                && (saved.ContentRevision != history[^1].CommittedWorkspaceRevision
                    || Hash(saved.Document.Content) == history[^1].CharacterPayloadDigestAfter)));
    }

    public static CharacterCareerGearPurchaseResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved,
        Guid operationId, string commandDigest)
    {
        if (!IsValidOwner(owner) || !IsValidHistory(saved))
            return new(CharacterCareerGearPurchaseOutcome.Corrupt, Error: "gear_history_invalid");
        var receipt = saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts?
            .SingleOrDefault(row => row.Command.Request.OperationId == operationId);
        if (receipt is null) return new(CharacterCareerGearPurchaseOutcome.NotFound, CurrentWorkspaceRevision: saved.ContentRevision);
        return saved.CanReplayReceipt(receipt.CommittedWorkspaceRevision)
            && receipt.Command.Binding.OwnerId == owner.NormalizedValue && receipt.CommandDigest == commandDigest
            ? new(CharacterCareerGearPurchaseOutcome.Replayed, receipt, saved.ContentRevision)
            : new(CharacterCareerGearPurchaseOutcome.IdempotencyConflict, CurrentWorkspaceRevision: saved.ContentRevision,
                Error: "gear_operation_conflict");
    }

    public static bool TryBuild(OwnerScope owner, WorkspaceStoredDocument saved,
        CharacterCareerGearPurchaseSnapshot snapshot, CharacterCareerGearPurchaseCommand command,
        out WorkspaceDocument? replacement, out CharacterCareerGearPurchaseReceipt? receipt)
    {
        replacement = null;
        receipt = null;
        if (!IsValidOwner(owner) || !IsConfirmedCommand(command) || command.Binding.OwnerId != owner.NormalizedValue
            || !IsValidHistory(saved) || saved.Id != command.Request.WorkspaceId
            || saved.ContentRevision != command.Binding.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision
            || saved.Document.AuxiliaryStateDigest != command.Binding.AuxiliaryStateDigest
            || SourceDigest(saved.Document) != command.Binding.SourceDigest
            || !TryPreview(snapshot, command.Request, out var preview, out _)
            || preview!.Command with { ExplicitlyConfirmed = true } != command) return false;
        var history = saved.Document.AuxiliaryState.CharacterCareerGearPurchaseReceipts;
        if (history is { Count: >= MaximumReceipts }
            || history?.Any(row => row.Command.Request.OperationId == command.Request.OperationId) == true) return false;
        try
        {
            XDocument document = ReadXml(saved.Document.Content);
            XElement root = document.Root!;
            var gears = root.Element("gears");
            if (gears is null) { gears = new XElement("gears"); root.Add(gears); }
            gears.Add(XElement.Parse(preview.Quote.GearXml));
            root.Element("nuyen")!.Value = preview.Quote.NuyenAfter.ToString(CultureInfo.InvariantCulture);
            var expenses = root.Element("expenses");
            if (expenses is null) { expenses = new XElement("expenses"); root.Add(expenses); }
            expenses.Add(new XElement("expense",
                new XElement("guid", preview.Quote.ExpenseId.ToString("D")),
                new XElement("date", command.Request.ExpenseDateLocal.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)),
                new XElement("type", "Nuyen"), new XElement("amount", (-preview.Quote.TotalCost).ToString(CultureInfo.InvariantCulture)),
                new XElement("reason", "Purchase gear: " + preview.Quote.Source.Name),
                new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
                new XElement("undo", new XElement("karmatype", "ImproveAttribute"),
                    new XElement("nuyentype", "AddGear"), new XElement("objectid", preview.Quote.GearId.ToString("D")),
                    new XElement("qty", command.Request.Quantity.ToString(CultureInfo.InvariantCulture)), new XElement("extra", string.Empty))));
            string payload = document.ToString(SaveOptions.DisableFormatting);
            if (payload.Length > MaximumContentLength) return false;
            var candidate = new CharacterCareerGearPurchaseReceipt(command, preview.Quote, CommandDigest(command),
                saved.ContentRevision + 1, Hash(payload), history is { Count: > 0 } ? history[^1].ReceiptDigest : LedgerRoot, "");
            candidate = candidate with { ReceiptDigest = ReceiptDigest(candidate) };
            var appended = (history ?? []).Append(candidate).ToArray();
            if (!IsValidLedger(saved.Id, candidate.CommittedWorkspaceRevision, appended)) return false;
            replacement = saved.Document with { State = saved.Document.State with
            {
                Payload = payload,
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerGearPurchaseReceipts = Array.AsReadOnly(appended) }
            } };
            receipt = candidate;
            return true;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or FormatException or OverflowException
            or ArgumentException or InvalidOperationException) { return false; }
    }

    private static XDocument ReadXml(string content)
    {
        if (string.IsNullOrEmpty(content) || content.Length > MaximumContentLength) throw new InvalidDataException();
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaximumContentLength });
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Root?.Name != "character" || document.Root.HasAttributes) throw new InvalidDataException();
        return document;
    }

    private static XElement? Container(XElement parent, string name, bool required)
    {
        var nodes = parent.Elements(name).Take(2).ToArray();
        if (nodes.Length > 1 || (required && nodes.Length != 1) || nodes.Any(node => node.HasAttributes))
            throw new InvalidDataException();
        return nodes.SingleOrDefault();
    }

    private static string Scalar(XElement parent, string name, bool required = true)
    {
        var node = Container(parent, name, required);
        if (node?.HasElements == true) throw new InvalidDataException();
        return node?.Value ?? string.Empty;
    }

    private static decimal Decimal(XElement parent, string name)
        => decimal.Parse(Scalar(parent, name), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture);
}
