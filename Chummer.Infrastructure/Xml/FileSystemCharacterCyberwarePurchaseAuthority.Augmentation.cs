using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Additive Career augmentation authority. The original Cyberware/v1 implementation is
/// deliberately unchanged. This partial materializes a single atomic workspace candidate;
/// it neither saves a workspace nor claims that a candidate has been committed to storage.
/// </summary>
public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority : ICharacterAugmentationPurchaseAuthority
{
    public const string BiowareXmlSha256 = "9139c0c726c960a6d51ad60539acb8ec3e59433945b65aaeaf8e37a60da73cdd";
    private const string AugmentationReceiptMissing = "No durable augmentation purchase receipt exists for this operation.";

    public CharacterAugmentationPurchasePreparation PrepareAugmentation(
        string characterXml, long contentRevision, CharacterAugmentationKind kind)
    {
        if (kind == CharacterAugmentationKind.Cyberware)
        {
            var old = Prepare(characterXml, contentRevision);
            return new(old.Exact, old.Blockers, kind, contentRevision, old.CharacterDigest, old.CatalogDigest,
                old.SettingsProfileId, old.CyberwareXmlDigest, old.AvailableNuyen, old.ExCon,
                old.EssenceHoleRating, old.EssenceAntiHoleRating, old.Settings,
                old.Entries.Select(e => FromCyberwareEntry(e, kind, string.Empty)).ToArray(),
                old.Exclusions.Select(e => new CharacterAugmentationPurchaseExclusion(e.SourceId.Value, e.Name, e.Reason)).ToArray(),
                characterXml, string.Empty);
        }

        var blockers = new List<string>();
        var entries = new List<CharacterAugmentationPurchaseCatalogEntry>();
        var exclusions = new List<CharacterAugmentationPurchaseExclusion>();
        var settings = new CharacterCyberwarePurchaseSettings(false, false, 0m, false, 0m, 0, false, []);
        string profileId = string.Empty, profileXml = string.Empty, profileAuthority = string.Empty;
        string sourceDigest = string.Empty;
        decimal nuyen = 0m;
        decimal? essence = null, maximum = null;
        bool exCon = false;
        int? hole = null, antiHole = null;
        if (kind != CharacterAugmentationKind.Bioware)
            blockers.Add("Unknown augmentation kind.");
        var document = TryParseCharacter(characterXml, blockers);
        XElement? root = document?.Root;
        if (contentRevision < 0) blockers.Add("Character content revision must be non-negative.");
        if (root is not null)
        {
            if (!TryReadBoolean(root, "created", out bool created) || !created)
                blockers.Add(CharacterCyberwarePurchaseBlockers.NotCareer);
            if (!TryReadScalar(root, "settings", out profileId) || string.IsNullOrWhiteSpace(profileId))
                blockers.Add("The character has no unique settings profile identity.");
            if (!TryReadDecimal(root, "nuyen", out nuyen) || nuyen < 0m)
                blockers.Add("The character has no unique non-negative Nuyen balance.");
            if (!TryReadBoolean(root, "excon", out exCon))
                blockers.Add("The character has no unique Ex-Con state.");
            foreach (string container in new[] { "cyberwares", "improvements", "expenses", "limitmodifiers", "augmentationpurchases", "attributes", "qualities" })
                if (root.Elements(container).Take(2).Count() > 1)
                    blockers.Add($"The saved {container} container is ambiguous.");
            if (!TryReadEssenceHole(root, EssenceHoleSourceId, out hole)
                || !TryReadEssenceHole(root, EssenceAntiHoleSourceId, out antiHole))
                blockers.Add("Essence Hole saved identity or rating is ambiguous.");
            if (HasSelectedAugmentationCustomData(root))
                blockers.Add("Selected custom data directories require an effective Bioware catalog authority.");
        }

        try
        {
            var catalog = _overlays.GetCatalog();
            if (catalog.Overlays.Any(o => o.Enabled))
                blockers.Add(CharacterCyberwarePurchaseBlockers.OverlaysUnsupported);
            var context = root is null ? null : _sourceData.TryCreateContext(characterXml);
            if (context is null || !context.TryResolveCareerSettingsProfile(out profileXml, out profileAuthority)
                || string.IsNullOrWhiteSpace(profileAuthority))
                blockers.Add("The exact effective Career settings profile is unavailable.");
            else
            {
                var profile = XElement.Parse(profileXml);
                if (!TryReadAugmentationSettings(profile, out settings))
                    blockers.Add("The exact Bioware purchase settings are incomplete or unsupported.");
                if (HasSelectedAugmentationCustomData(profile))
                    blockers.Add("Custom profile data directories are not a pinned Bioware source authority.");
                byte[] bytes = _readAllBytes(Path.Combine(catalog.BaseDataPath, "bioware.xml"));
                sourceDigest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!FixedEquals(sourceDigest, BiowareXmlSha256))
                    blockers.Add("bioware.xml does not match the canonical Bioware source snapshot.");
                else
                {
                    using var stream = new MemoryStream(bytes, writable: false);
                    var source = XDocument.Load(stream).Root!;
                    foreach (XElement row in source.Element("biowares")!.Elements("bioware"))
                    {
                        Guid id = Guid.Parse(ReadOptionalScalar(row, "id"));
                        string name = ReadOptionalScalar(row, "name");
                        string? reason = BiowareSourceBlocker(row);
                        if (!context.TryIsBookEnabled(ReadOptionalScalar(row, "source"), out bool enabled) || !enabled)
                            reason = "The Bioware source book is not enabled in the exact character profile.";
                        var grades = new List<CharacterAugmentationPurchaseGrade>();
                        foreach (XElement grade in source.Element("grades")!.Elements("grade"))
                        {
                            string gradeName = ReadOptionalScalar(grade, "name");
                            string forced = ReadOptionalScalar(row, "forcegrade");
                            if (!context.TryIsBookEnabled(ReadOptionalScalar(grade, "source"), out bool gradeEnabled) || !gradeEnabled
                                || settings.BannedGrades.Contains(gradeName, StringComparer.Ordinal)
                                || row.Element("bannedgrades")?.Elements("grade").Any(g => g.Value == gradeName) == true
                                || forced.Length != 0 && forced != gradeName
                                || forced.Length == 0 && gradeName == "None"
                                // The Burnout's Way grade needs its quality and adept discount authority.
                                || gradeName == "Standard (Burnout's Way)") continue;
                            if (!TryReadDecimal(grade, "cost", out decimal cost) || cost < 0m
                                || !TryReadDecimal(grade, "ess", out decimal ess) || ess < 0m
                                || !TryReadInteger(grade, "avail", out int avail))
                            {
                                reason = "The canonical Bioware grade has unsupported numeric semantics.";
                                continue;
                            }
                            grades.Add(new(Guid.Parse(ReadOptionalScalar(grade, "id")), gradeName, cost, ess, avail));
                        }
                        if (grades.Count == 0) reason ??= "No enabled, unbanned Bioware grade satisfies the source constraints.";
                        if (reason is not null) { exclusions.Add(new(id, name, reason)); continue; }
                        TryReadRatingBounds(row, out int min, out int max);
                        string category = ReadOptionalScalar(row, "category");
                        var blackMarketNames = root?.Element("improvements")?.Elements("improvement")
                            .Where(i => ReadOptionalScalar(i, "improvementttype") == "BlackMarketDiscount"
                                && ReadOptionalScalar(i, "enabled") is "1" or "True")
                            .Select(i => ReadOptionalScalar(i, "improvedname")).ToHashSet(StringComparer.Ordinal) ?? [];
                        bool blackMarket = source.Element("categories")!.Elements("category").Any(c => c.Value == category
                            && ((string?)c.Attribute("blackmarket") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Any(blackMarketNames.Contains));
                        entries.Add(new(kind, id, name, category, ReadOptionalScalar(row, "ess"), ReadOptionalScalar(row, "capacity"),
                            ReadOptionalScalar(row, "avail"), ReadOptionalScalar(row, "cost"), ReadOptionalScalar(row, "source"),
                            ReadOptionalScalar(row, "page"), blackMarket, ReadOptionalScalar(row, "forcegrade"),
                            row.Element("bannedgrades")?.Elements("grade").Select(g => g.Value).ToArray() ?? [],
                            grades.OrderBy(g => g.Id).ToArray(), min, max, row.ToString(SaveOptions.DisableFormatting)));
                    }
                    if (root is not null)
                    {
                        string? stateBlocker = TryCalculateExistingAugmentationEssence(root, settings, source,
                            catalog.BaseDataPath, out decimal currentEssence, out decimal maximumEssence);
                        if (stateBlocker is not null) blockers.Add(stateBlocker);
                        else { essence = currentEssence; maximum = maximumEssence; }
                    }
                }
            }
        }
        catch (Exception ex) when (IsAugmentationDataFailure(ex))
        {
            blockers.Add("The canonical Bioware source/profile or saved character state could not be read exactly.");
        }
        if (entries.Count == 0) blockers.Add("No Bioware entries are available under the supported source/profile semantics.");
        entries.Sort((a, b) => a.SourceId.CompareTo(b.SourceId));
        exclusions.Sort((a, b) => a.SourceId.CompareTo(b.SourceId));
        string digest = AugmentationDigest(new { Schema = "sr5-bioware-catalog/v1", Source = sourceDigest,
            Cyberware = CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256, profileId, profileXml, profileAuthority, settings, entries, exclusions });
        return new(blockers.Count == 0, Normalize(blockers.Select(b => b.Replace("Cyberware", "Bioware", StringComparison.Ordinal))), kind, contentRevision,
            CharacterCyberwarePurchaseRules.ComputeCharacterDigest(characterXml), digest, profileId, sourceDigest, nuyen, exCon,
            hole, antiHole, settings, entries.ToArray(), exclusions.ToArray(), characterXml, profileXml, essence, maximum);
    }

    public CharacterAugmentationPurchaseQuote QuoteAugmentation(
        CharacterAugmentationPurchasePreparation preparation, CharacterAugmentationPurchaseSelection selection)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(selection);
        CharacterAugmentationPurchaseQuote Deny(string? reason) => new(false, reason, selection, string.Empty, 0m, 0m, 0m,
            preparation.EssenceHoleRating, preparation.EssenceAntiHoleRating, preparation.CurrentEssence, preparation.CurrentEssence, [], string.Empty);
        if (!preparation.Exact) return Deny(preparation.Blockers.FirstOrDefault() ?? "Augmentation preparation is not exact.");
        if (selection.Kind != preparation.Kind || selection.Quantity < 1)
            return Deny("Augmentation kind must match its catalog and quantity must be a positive integer.");
        var matches = preparation.Entries.Where(e => e.Kind == selection.Kind && e.SourceId == selection.SourceId).Take(2).ToArray();
        if (matches.Length != 1) return Deny("The selected augmentation source identity is absent or ambiguous.");
        var entry = matches[0];
        if (selection.Kind == CharacterAugmentationKind.Cyberware && selection.Quantity != 1)
            return Deny("The existing Cyberware authority supports one instance per command.");
        try
        {
            var pricing = ToCyberwarePreparation(preparation) with { AvailableNuyen = preparation.AvailableNuyen / selection.Quantity };
            var unit = CharacterCyberwarePurchaseRules.Quote(pricing, ToCyberwareSelection(selection));
            if (!unit.Exact) return Deny(selection.Kind == CharacterAugmentationKind.Bioware
                ? unit.BlockReason.Replace("Cyberware", "Bioware", StringComparison.Ordinal) : unit.BlockReason);
            decimal charged = checked(unit.ChargedCost * selection.Quantity);
            decimal essence = checked(unit.InstalledEssence * selection.Quantity);
            int consumed = checked(CharacterCyberwarePurchaseRules.StandardRound(unit.InstalledEssence * 100m) * selection.Quantity);
            int? hole = preparation.EssenceHoleRating is int previousHole ? Math.Max(0, previousHole - consumed) : null;
            decimal? nextEssence = preparation.CurrentEssence - essence + (preparation.EssenceHoleRating.GetValueOrDefault() - hole.GetValueOrDefault()) / 100m;
            var effects = new List<CharacterAugmentationPurchaseEffect>();
            if (selection.Kind == CharacterAugmentationKind.Bioware)
            {
                var source = XElement.Parse(entry.SourceXml);
                var character = XDocument.Parse(preparation.CharacterXml).Root!;
                string? admission = BiowareCharacterAdmission(source, character, selection.Quantity);
                if (admission is not null) return Deny(admission);
                if (!TryProjectBiowareEffects(source.Element("bonus"), selection.Rating, out var projected, out string? effectBlocker))
                    return Deny(effectBlocker);
                effects.AddRange(projected);
                if (nextEssence is null || nextEssence <= 0m)
                    return Deny("The purchase would leave non-positive Essence or its remaining Essence cannot be established.");
                string? special = ProjectCareerSpecialAttributeLoss(character, XElement.Parse(preparation.SettingsXml),
                    preparation.MaximumEssence!.Value, nextEssence.Value, effects);
                if (special is not null) return Deny(special);
            }
            var unsigned = new CharacterAugmentationPurchaseQuote(true, null, selection, unit.GradeName, charged, -charged,
                essence, hole, preparation.EssenceAntiHoleRating, preparation.CurrentEssence, nextEssence, effects.ToArray(), string.Empty);
            return unsigned with { QuoteDigest = AugmentationDigest(new { preparation.CharacterDigest, preparation.ContentRevision,
                preparation.CatalogDigest, preparation.SettingsProfileId, Quote = unsigned }) };
        }
        catch (Exception ex) when (IsAugmentationDataFailure(ex))
        {
            return Deny("Augmentation quote arithmetic or saved/source expressions exceed the exact supported range.");
        }
    }

    public CharacterAugmentationPurchaseResult MaterializeAugmentation(
        string characterXml, long currentContentRevision, CharacterAugmentationPurchaseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string? identity = AugmentationCommandIdentityBlocker(command);
        if (identity is not null) return AugmentationBlocked(characterXml, identity);
        var recovered = LookupAugmentation(characterXml, command);
        if (recovered.AlreadyApplied || recovered.BlockReason != AugmentationReceiptMissing) return recovered;
        var preparation = PrepareAugmentation(characterXml, currentContentRevision, command.Selection.Kind);
        var quote = QuoteAugmentation(preparation, command.Selection);
        if (currentContentRevision != command.ExpectedContentRevision || currentContentRevision == long.MaxValue)
            return AugmentationBlocked(characterXml, CharacterCyberwarePurchaseBlockers.StaleRevision);
        if (!FixedEquals(command.ExpectedCharacterDigest, preparation.CharacterDigest))
            return AugmentationBlocked(characterXml, CharacterCyberwarePurchaseBlockers.StaleCharacter);
        if (!FixedEquals(command.ExpectedCatalogDigest, preparation.CatalogDigest)
            || command.ExpectedSettingsProfileId != preparation.SettingsProfileId)
            return AugmentationBlocked(characterXml, "The augmentation source catalog or settings profile has changed.");
        if (!quote.Allowed) return AugmentationBlocked(characterXml, quote.BlockReason ?? "The augmentation purchase is blocked.");
        if (!FixedEquals(command.ExpectedQuoteDigest, quote.QuoteDigest))
            return AugmentationBlocked(characterXml, CharacterCyberwarePurchaseBlockers.StaleQuote);
        try
        {
            var document = XDocument.Parse(characterXml);
            var root = document.Root!;
            if (command.NewInstanceIds.Append(command.NewExpenseId).Any(id => ContainsGuid(root, id)))
                return AugmentationBlocked(characterXml, "An augmentation instance or expense identity already exists.");
            var entry = preparation.Entries.Single(e => e.SourceId == command.Selection.SourceId);
            if (command.Selection.Kind == CharacterAugmentationKind.Cyberware)
            {
                var oldPreparation = ToCyberwarePreparation(preparation);
                var oldSelection = ToCyberwareSelection(command.Selection);
                var oldQuote = Quote(oldPreparation, oldSelection);
                var result = Commit(characterXml, currentContentRevision, new(command.ExpectedContentRevision,
                    command.ExpectedCharacterDigest, command.ExpectedCatalogDigest, oldQuote.QuoteDigest, oldSelection,
                    new(command.NewInstanceIds[0]), command.NewExpenseId, command.ExpenseDate));
                if (!result.Committed) return AugmentationBlocked(characterXml, result.BlockReason);
                document = XDocument.Parse(result.CharacterXml);
                root = document.Root!;
            }
            else
            {
                root.Element("nuyen")!.Value = (preparation.AvailableNuyen + quote.NuyenDelta).ToString(CultureInfo.InvariantCulture);
                ApplyEssenceHole(root, quote.NewEssenceHoleRating);
                foreach (Guid instanceId in command.NewInstanceIds)
                {
                    XElement saved = CreateSavedCyberware(ToCyberwareEntry(entry), quote.GradeName,
                        ToCyberwareSelection(command.Selection), new(instanceId));
                    saved.SetElementValue("improvementsource", "Bioware");
                    var source = XElement.Parse(entry.SourceXml);
                    saved.SetElementValue("isgeneware", source.Element("isgeneware") is null ? "False" : "True");
                    saved.Element("bonus")!.ReplaceWith(source.Element("bonus") is XElement bonus ? new XElement(bonus) : new XElement("bonus"));
                    GetOrCreateUniqueContainer(root, "cyberwares").Add(saved);
                    MaterializeBiowareEffects(root, entry.Name, instanceId, quote.Effects.Where(e => e.ImprovementSource == "Bioware"));
                }
                // Character effects are one replacement for the transaction, not one per instance.
                var characterEffects = quote.Effects.Where(e => e.ImprovementSource == "EssenceLoss").ToArray();
                if (characterEffects.Length != 0)
                {
                    var improvements = GetOrCreateUniqueContainer(root, "improvements");
                    improvements.Elements("improvement").Where(i => ReadOptionalScalar(i, "improvementsource") == "EssenceLoss").Remove();
                    foreach (var effect in characterEffects)
                        improvements.Add(SaveAugmentationImprovement(effect, string.Empty));
                }
                XElement expense = CreateExpense(ToCyberwareEntry(entry), quote.NuyenDelta,
                    new(command.NewInstanceIds[0]), command.NewExpenseId, command.ExpenseDate);
                expense.SetElementValue("reason", $"Purchased Bioware {entry.Name}");
                // Native's legacy one-item undo cannot undo a multi-instance atomic receipt.
                // The typed transaction receipt is the recovery authority; do not leave a partial native undo.
                if (command.NewInstanceIds.Count != 1) expense.Element("undo")!.Remove();
                GetOrCreateUniqueContainer(root, "expenses").Add(expense);
            }
            string materialized = AugmentationDigest(new { Xml = document.ToString(SaveOptions.DisableFormatting) });
            var receipt = new CharacterAugmentationPurchaseReceipt(CharacterAugmentationPurchaseSchemas.ReceiptV1, command.Binding,
                command.Selection.Kind, command.Selection.SourceId, command.Selection.GradeId, preparation.SettingsProfileId,
                currentContentRevision, checked(currentContentRevision + 1), preparation.CharacterDigest, preparation.CatalogDigest,
                quote.QuoteDigest, AugmentationDigest(command), command.NewInstanceIds.ToArray(), command.NewExpenseId,
                quote.NuyenDelta, materialized, string.Empty)
            {
                Command = command with { NewInstanceIds = command.NewInstanceIds.ToArray() }
            };
            receipt = receipt with { ReceiptDigest = AugmentationDigest(receipt) };
            GetOrCreateUniqueContainer(root, "augmentationpurchases").Add(new XElement("purchase",
                new XAttribute("operation", command.Binding.OperationId.ToString("D")), JsonSerializer.Serialize(receipt)));
            return new(true, false, null, document.ToString(SaveOptions.DisableFormatting), receipt);
        }
        catch (Exception ex) when (IsAugmentationDataFailure(ex))
        {
            return AugmentationBlocked(characterXml, "The augmentation could not be materialized atomically from the exact saved state.");
        }
    }

    public CharacterAugmentationPurchaseResult LookupAugmentation(string characterXml, CharacterAugmentationPurchaseCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string? identity = AugmentationCommandIdentityBlocker(command);
        if (identity is not null) return AugmentationBlocked(characterXml, identity);
        try
        {
            var document = XDocument.Parse(characterXml);
            if (document.Root?.Name != "character") return AugmentationBlocked(characterXml, "A unique character root is required for receipt recovery.");
            var containers = document.Root.Elements("augmentationpurchases").Take(2).ToArray();
            if (containers.Length > 1) return AugmentationBlocked(characterXml, "The durable augmentation receipt container is ambiguous.");
            if (containers.Length == 1)
            {
                var operations = new HashSet<Guid>();
                foreach (var row in containers[0].Elements())
                    if (row.Name != "purchase" || row.HasElements || row.Attributes().Count() != 1
                        || !Guid.TryParseExact((string?)row.Attribute("operation"), "D", out Guid operation)
                        || operation == Guid.Empty || !operations.Add(operation))
                        return AugmentationBlocked(characterXml, "The durable augmentation receipt inventory is malformed or ambiguous.");
            }
            var rows = containers.SingleOrDefault()?.Elements("purchase")
                .Where(e => Guid.TryParse((string?)e.Attribute("operation"), out Guid id) && id == command.Binding.OperationId).Take(2).ToArray() ?? [];
            if (rows.Length == 0) return AugmentationBlocked(characterXml, AugmentationReceiptMissing);
            if (rows.Length != 1 || rows[0].HasElements) return AugmentationBlocked(characterXml, "The operation receipt is ambiguous or malformed.");
            var receipt = JsonSerializer.Deserialize<CharacterAugmentationPurchaseReceipt>(rows[0].Value);
            if (receipt is null || receipt.Schema != CharacterAugmentationPurchaseSchemas.ReceiptV1
                || receipt.Binding != command.Binding || receipt.Kind != command.Selection.Kind
                || receipt.SourceId != command.Selection.SourceId || receipt.GradeId != command.Selection.GradeId
                || receipt.SettingsProfileId != command.ExpectedSettingsProfileId
                || receipt.PreviousContentRevision != command.ExpectedContentRevision
                || receipt.PreviousContentRevision == long.MaxValue || receipt.NewContentRevision != receipt.PreviousContentRevision + 1
                || receipt.PreviousCharacterDigest != command.ExpectedCharacterDigest || receipt.CatalogDigest != command.ExpectedCatalogDigest
                || receipt.QuoteDigest != command.ExpectedQuoteDigest || receipt.ExpenseId != command.NewExpenseId
                || receipt.InstanceIds is null || !receipt.InstanceIds.SequenceEqual(command.NewInstanceIds)
                || receipt.Command is null || !FixedEquals(receipt.CommandDigest, AugmentationDigest(receipt.Command))
                || !FixedEquals(receipt.CommandDigest, AugmentationDigest(command))
                || !FixedEquals(receipt.ReceiptDigest, AugmentationDigest(receipt with { ReceiptDigest = string.Empty })))
                return AugmentationBlocked(characterXml, "The operation receipt does not match the exact owner/workspace/source/profile/revision/command binding.");
            return new(false, true, null, characterXml, receipt);
        }
        catch (Exception ex) when (IsAugmentationDataFailure(ex))
        {
            return AugmentationBlocked(characterXml, "The durable augmentation receipt could not be decoded exactly.");
        }
    }

    public CharacterAugmentationPurchaseResult LookupAugmentation(string characterXml, CharacterAugmentationPurchaseBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (string.IsNullOrWhiteSpace(binding.OwnerId) || string.IsNullOrWhiteSpace(binding.WorkspaceId) || binding.OperationId == Guid.Empty)
            return AugmentationBlocked(characterXml, "Cold recovery requires the authenticated owner/workspace/operation binding.");
        try
        {
            var document = XDocument.Parse(characterXml);
            var containers = document.Root?.Elements("augmentationpurchases").Take(2).ToArray() ?? [];
            if (document.Root?.Name != "character" || containers.Length > 1)
                return AugmentationBlocked(characterXml, "The character or durable receipt container is ambiguous.");
            var rows = containers.SingleOrDefault()?.Elements("purchase")
                .Where(e => Guid.TryParse((string?)e.Attribute("operation"), out Guid id) && id == binding.OperationId).Take(2).ToArray() ?? [];
            if (rows.Length == 0) return AugmentationBlocked(characterXml, AugmentationReceiptMissing);
            if (rows.Length != 1 || rows[0].HasElements) return AugmentationBlocked(characterXml, "The durable operation receipt is malformed or ambiguous.");
            var receipt = JsonSerializer.Deserialize<CharacterAugmentationPurchaseReceipt>(rows[0].Value);
            if (receipt?.Command is null || receipt.Binding != binding || receipt.Command.Binding != binding)
                return AugmentationBlocked(characterXml, "The durable operation does not belong to this owner/workspace binding.");
            return LookupAugmentation(characterXml, receipt.Command);
        }
        catch (Exception ex) when (IsAugmentationDataFailure(ex))
        {
            return AugmentationBlocked(characterXml, "The durable operation provenance could not be decoded exactly.");
        }
    }

    private static string? AugmentationCommandIdentityBlocker(CharacterAugmentationPurchaseCommand command)
    {
        if (command.Binding is null || string.IsNullOrWhiteSpace(command.Binding.OwnerId) || string.IsNullOrWhiteSpace(command.Binding.WorkspaceId)
            || command.Binding.OperationId == Guid.Empty || command.Selection is null || command.Selection.SourceId == Guid.Empty
            || command.Selection.GradeId == Guid.Empty || command.Selection.SourceId == command.Selection.GradeId
            || !Enum.IsDefined(command.Selection.Kind) || command.Selection.Quantity < 1
            || command.NewInstanceIds is null || command.NewInstanceIds.Count != command.Selection.Quantity || command.NewExpenseId == Guid.Empty
            || command.NewInstanceIds.Append(command.NewExpenseId).Any(id => id == Guid.Empty
                || id == command.Selection.SourceId || id == command.Selection.GradeId)
            || command.NewInstanceIds.Append(command.NewExpenseId).Distinct().Count() != command.NewInstanceIds.Count + 1
            || command.ExpectedContentRevision < 0 || command.ExpenseDate.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(command.ExpectedSettingsProfileId)
            || new[] { command.ExpectedCharacterDigest, command.ExpectedCatalogDigest, command.ExpectedQuoteDigest }
                .Any(d => !CharacterCyberwarePurchaseRules.IsCanonicalDigest(d)))
            return "The augmentation command requires exact owner, workspace, operation, source, profile, revision, quote, instance and expense identities.";
        return null;
    }

    private static CharacterAugmentationPurchaseResult AugmentationBlocked(string xml, string? reason) => new(false, false, reason, xml, null);
    private static string AugmentationDigest<T>(T value) => CharacterCyberwarePurchaseRules.ComputeCharacterDigest(JsonSerializer.Serialize(value));
    private static bool IsAugmentationDataFailure(Exception exception) => IsReadFailure(exception)
        || exception is System.Xml.XmlException or JsonException or FormatException or OverflowException or ArgumentException or InvalidOperationException or InvalidDataException;

    private static CharacterCyberwarePurchaseSelection ToCyberwareSelection(CharacterAugmentationPurchaseSelection s)
        => new(new(s.SourceId), new(s.GradeId), s.Rating, s.EssenceDiscountPercent, s.BlackMarketDiscount, s.MarkupPercent, s.FreeCost);

    private static CharacterAugmentationPurchaseCatalogEntry FromCyberwareEntry(CharacterCyberwarePurchaseCatalogEntry e,
        CharacterAugmentationKind kind, string sourceXml) => new(kind, e.SourceId.Value, e.Name, e.Category, e.EssenceExpression,
        e.CapacityExpression, e.AvailabilityExpression, e.CostExpression, e.SourceBook, e.Page, e.BlackMarketEligible,
        e.ForcedGrade, e.BannedGrades, e.Grades.Select(g => new CharacterAugmentationPurchaseGrade(g.Id.Value, g.Name, g.CostMultiplier,
            g.EssenceMultiplier, g.AvailabilityModifier)).ToArray(), e.MinimumRating, e.MaximumRating, sourceXml);

    private static CharacterCyberwarePurchaseCatalogEntry ToCyberwareEntry(CharacterAugmentationPurchaseCatalogEntry e)
        => new(new(e.SourceId), e.Name, e.Category, e.EssenceExpression, e.CapacityExpression, e.AvailabilityExpression,
            e.CostExpression, e.SourceBook, e.Page, e.BlackMarketEligible, e.ForcedGrade, e.BannedGrades,
            e.Grades.Select(g => new CharacterCyberwarePurchaseGrade(new(g.Id), g.Name, g.CostMultiplier, g.EssenceMultiplier, g.AvailabilityModifier)).ToArray(),
            e.MinimumRating, e.MaximumRating);

    private static CharacterCyberwarePurchasePreparation ToCyberwarePreparation(CharacterAugmentationPurchasePreparation p)
        => new(p.Exact, p.Blockers, p.ContentRevision, p.CharacterDigest, p.CatalogDigest, p.SettingsProfileId, p.SourceXmlDigest,
            p.AvailableNuyen, p.ExCon, p.EssenceHoleRating, p.EssenceAntiHoleRating, p.Settings,
            p.Entries.Select(ToCyberwareEntry).ToArray(), p.Exclusions.Select(e => new CharacterCyberwarePurchaseCatalogExclusion(new(e.SourceId), e.Name, e.Reason)).ToArray());

    private static bool TryReadAugmentationSettings(XElement profile, out CharacterCyberwarePurchaseSettings settings)
    {
        settings = new(false, false, 0m, false, 0m, 0, false, []);
        if (!TryReadBoolean(profile, "allowcyberwareessdiscounts", out bool discounts)
            || !TryReadBoolean(profile, "multiplyrestrictedcost", out bool restricted)
            || !TryReadDecimal(profile, "restrictedcostmultiplier", out decimal restrictedMultiplier) || restrictedMultiplier < 0m
            || !TryReadBoolean(profile, "multiplyforbiddencost", out bool forbidden)
            || !TryReadDecimal(profile, "forbiddencostmultiplier", out decimal forbiddenMultiplier) || forbiddenMultiplier < 0m
            || !TryReadBoolean(profile, "donotroundessenceinternally", out bool unrounded)
            || !TryReadScalar(profile, "essenceformat", out string format) || !TryGetDecimalPlaces(format, out int places)
            || !TryReadBoolean(profile, "esslossreducesmaximumonly", out _)
            || !TryReadBoolean(profile, "specialkarmacostbasedonshownvalue", out _)
            || ReadOptionalScalar(profile, "essencemodifierpostexpression") is not ("" or "{Modifier}")) return false;
        settings = new(discounts, restricted, restrictedMultiplier, forbidden, forbiddenMultiplier, places, unrounded,
            profile.Element("bannedwaregrades")?.Elements("grade").Select(g => g.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray() ?? []);
        return true;
    }

    private static bool HasSelectedAugmentationCustomData(XElement root)
    {
        var containers = root.Elements("customdatadirectorynames").Take(2).ToArray();
        if (containers.Length > 1) return true;
        // CharacterSettings.Save retains externally registered but disabled directories.
        // Those are not selected overlays. Legacy directoryname entries imply selection.
        return containers.SingleOrDefault()?.Elements().Any(e => e.Name != "customdatadirectoryname"
            || !TryReadBoolean(e, "enabled", out bool enabled) || enabled) == true;
    }
}
