using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed partial class FileSystemCharacterCyberwarePurchaseAuthority : ICharacterCareerImplantCommerceAuthority
{
    public CharacterCareerImplantCommercePreparation ReadInstalled(string characterXml, long contentRevision)
    {
        var preparation = Prepare(characterXml, contentRevision, legacyV1: false, installedCommerce: true); // Purchase and v1 receipt authority stay unchanged.
        var blockers = preparation.Blockers.ToList();
        var rows = new List<CharacterCareerImplantCommerceRow>();
        var items = new List<CharacterCareerImplantCommerceItem>();
        var effectAuthorities = new List<string>();
        CharacterAugmentationPurchasePreparation? bioware = null;
        IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> bookkeepingSources = [];
        try
        {
            var root = XDocument.Parse(characterXml).Root!;
            if (root.Elements("cyberwares").Count() != 1 || root.Element("cyberwares")!.HasAttributes
                || root.Element("cyberwares")!.Elements().Any(e => e.Name != "cyberware")
                || root.Elements("expenses").Count() > 1)
                throw new InvalidDataException("Implant or expense containers are ambiguous.");
            var wares = root.Element("cyberwares")!.Elements("cyberware").ToArray();
            if (wares.Length > 4096) throw new InvalidDataException("Installed implant catalog is too large.");
            var ids = wares.Select(w => ReadOptionalScalar(w, "guid")).ToArray();
            if (ids.Any(id => !Guid.TryParseExact(id, "D", out var value) || value == Guid.Empty)
                || ids.Select(Guid.Parse).Distinct().Count() != ids.Length)
                throw new InvalidDataException("Installed implants need unique stable instance GUIDs.");
            // The purchase authority supplies all-sources/profile identity, exclusions and grade legality.
            var context = _sourceData.TryCreateContext(characterXml);
            var effectSources = ReadCommerceEffectSources();
            // Resolve Bioware through its own pinned catalog/profile and mixed-Essence
            // authority. Never obtain its grades or multipliers from cyberware.xml.
            if (wares.Any(w => ReadOptionalScalar(w, "improvementsource") == "Bioware"))
                bioware = PrepareAugmentation(characterXml, contentRevision, CharacterAugmentationKind.Bioware);
            var biowarePreparation = bioware is null ? null : ToCyberwarePreparation(bioware);
            XElement[] biowareSources = bioware?.SourceXmlDigest == BiowareXmlSha256
                ? ReadCommerceEffectSources("Bioware") : [];
            bookkeepingSources = ReadCommerceBookkeepingSources(context);
            if (preparation.EssenceHoleRating > 0 && preparation.EssenceAntiHoleRating > 0)
                throw new InvalidDataException("Simultaneous positive Essence Hole and Anti-Hole balances are inconsistent.");
            foreach (var ware in wares)
            {
                string sourceId = ReadOptionalScalar(ware, "sourceid");
                if (sourceId is EssenceHoleSourceId or EssenceAntiHoleSourceId)
                {
                    ValidateCommerceBookkeeping(ware, RequireCommerceBookkeepingSource(bookkeepingSources, sourceId));
                    continue;
                }
                if (ReadOptionalScalar(ware, "name") is "Essence Hole" or "Essence Antihole"
                    || Guid.TryParse(sourceId, out var bookkeepingId)
                        && (bookkeepingId == Guid.Parse(EssenceHoleSourceId) || bookkeepingId == Guid.Parse(EssenceAntiHoleSourceId)))
                    throw new InvalidDataException("Essence bookkeeping has a wrong or noncanonical source identity.");
                Guid id = Guid.Parse(ReadOptionalScalar(ware, "guid"));
                string name = ReadOptionalScalar(ware, "name");
                string? error = null;
                CharacterCareerImplantCommerceItem? item = null;
                string kind = ReadOptionalScalar(ware, "improvementsource");
                var itemPreparation = kind == "Bioware" ? biowarePreparation : preparation;
                try
                {
                    if (kind is not ("Cyberware" or "Bioware"))
                        throw new InvalidDataException("The installed implant needs an exact Cyberware or Bioware source kind.");
                    if (itemPreparation is null || !itemPreparation.Exact)
                        throw new InvalidDataException(itemPreparation?.Blockers.FirstOrDefault() ?? "The installed source authority is unavailable.");
                    var entry = itemPreparation.Entries.SingleOrDefault(e => e.SourceId.Value.ToString("D") == sourceId)
                        ?? throw new InvalidDataException(kind == "Bioware"
                            ? itemPreparation.Exclusions.SingleOrDefault(e => e.SourceId.Value.ToString("D") == sourceId)?.Reason
                                ?? "Installed Bioware source is absent from the exact current purchase catalog."
                            : "Installed source is excluded or not in the exact current purchase catalog.");
                    var grade = entry.Grades.SingleOrDefault(g => g.Name == ReadOptionalScalar(ware, "grade"))
                        ?? throw new InvalidDataException("Installed grade is unavailable under the selected source/profile.");
                    if (!TryReadInteger(ware, "rating", out int rating) || rating < entry.MinimumRating || rating > entry.MaximumRating
                        || !TryReadInteger(ware, "essdiscount", out int discount) || discount != 0
                        || !TryReadBoolean(ware, "discountedcost", out bool discounted) || discounted)
                        throw new InvalidDataException("Rating must be exact; discounted/Essence-discounted implants are unsupported.");
                    var selection = new CharacterCyberwarePurchaseSelection(entry.SourceId, grade.Id, rating, 0, false, 0m, false);
                    var expected = CreateSavedCyberware(entry, grade.Name, selection, new(id));
                    var effectSource = (kind == "Bioware" ? biowareSources : effectSources)
                        .Single(row => ReadOptionalScalar(row, "id") == sourceId);
                    if (kind == "Bioware")
                    {
                        ValidateCommerceBiowareSource(root, ware, effectSource, effectSources, biowareSources);
                        expected.SetElementValue("improvementsource", "Bioware");
                        expected.SetElementValue("isgeneware", "False");
                        // Match MaterializeAugmentation, including its empty bonus shape.
                        expected.Element("bonus")!.ReplaceWith(effectSource.Element("bonus") is XElement bioBonus
                            ? new XElement(bioBonus) : new XElement("bonus"));
                    }
                    // Preserve the purchase writer's explicit empty bonus when the source has none.
                    // XNode.DeepEquals distinguishes it from a self-closing XElement("bonus").
                    if (effectSource.Element("bonus") is XElement bonus)
                        expected.Element("bonus")!.ReplaceWith(new XElement(bonus));
                    string effectAuthority = ValidateCommerceEffects(root, ware, effectSource, rating);
                    if (effectAuthority.Length > 0) effectAuthorities.Add(effectAuthority);
                    // Require the canonical purchase shape plus the exact source bonus.
                    if (!CommerceTemplateEquals(ware, expected))
                        throw new InvalidDataException($"Only exact top-level canonical purchase-shaped {kind} is supported; choices, children, custom costs and generated state are excluded.");
                    if (kind == "Cyberware" && (context is null || !context.TryResolveCyberwareCommerceSource(sourceId, name, "Cyberware", out var source)
                        || source.EssenceModifierPostExpression != "{Modifier}"
                        || source.EssenceDecimals != preparation.Settings.EssenceDecimals
                        || source.DoNotRoundEssenceInternally != preparation.Settings.DoNotRoundEssenceInternally))
                        throw new InvalidDataException("The exact installed source/Essence rule environment is unavailable.");
                    // Preserve purchase receipts and their expense links, but refuse every other live reference.
                    foreach (var node in root.Descendants().Where(e => !e.HasElements && Guid.TryParse(e.Value, out var v) && v == id))
                    {
                        if (node.Parent == ware && node.Name == "guid") continue;
                        if (node.Name == "sourcename" && node.Parent?.Name == "improvement"
                            && node.Parent.Parent == root.Element("improvements")) continue;
                        if (node.Name == "objectid" && node.Parent?.Name == "undo" && node.Parent.Parent?.Name == "expense"
                            && node.Parent.Parent.Parent == root.Element("expenses")
                            && ReadOptionalScalar(node.Parent, "nuyentype") == "AddCyberware") continue;
                        throw new InvalidDataException("The implant has an external saved reference.");
                    }
                    if (root.Descendants().Attributes().Any(a => Guid.TryParse(a.Value, out var v) && v == id))
                        throw new InvalidDataException("The implant has an external attribute reference.");
                    var snapshot = new CharacterCyberwareCommerceSnapshot(id, entry.Name, "", entry.CapacityExpression,
                        rating, entry.MinimumRating, entry.MaximumRating, grade.Id.Value.ToString("D"), grade.Name,
                        entry.CostExpression, entry.EssenceExpression, false, false, 0m, 1m, 0m,
                        itemPreparation.Settings.EssenceDecimals, itemPreparation.Settings.DoNotRoundEssenceInternally,
                        itemPreparation.AvailableNuyen, itemPreparation.EssenceHoleRating, itemPreparation.EssenceAntiHoleRating,
                        entry.Grades.Select(g => new CharacterCyberwareGradeOption(g.Id.Value.ToString("D"), g.Name,
                            g.CostMultiplier, g.EssenceMultiplier)).ToArray());
                    item = new(id, entry.SourceId.Value, entry.Name, ware.ToString(SaveOptions.DisableFormatting), snapshot);
                    items.Add(item);
                }
                catch (Exception ex) when (CommerceDataFailure(ex)) { error = ex.Message; }
                var actions = new List<CharacterCareerImplantCommerceAction>();
                if (item is not null)
                {
                    // Catalog support describes the structural action, not affordability at the default refund.
                    // Only these boolean probes ignore funds; Items retains the actual balance for every Preview/commit.
                    var eligibility = item with { Snapshot = item.Snapshot with { AvailableNuyen = decimal.MaxValue } };
                    var sale = CommerceQuote(eligibility, "sell", CharacterCyberwareCommerceRules.DefaultRefundPercentage, bookkeepingSources, out var saleError);
                    actions.Add(new("sell", "Sell", sale is not null, saleError));
                    foreach (var grade in item.Snapshot.GradeOptions.Where(g => g.Id != item.Snapshot.CurrentGradeId))
                    {
                        string action = "grade:" + grade.Id;
                        var upgrade = CommerceQuote(eligibility, action, CharacterCyberwareCommerceRules.DefaultRefundPercentage, bookkeepingSources, out var upgradeError);
                        bool supported = upgrade is not null && CommerceReplacementPurchaseMatches(itemPreparation!, item, upgrade, out upgradeError);
                        actions.Add(new(action, "Upgrade to " + grade.Name, supported, upgradeError));
                    }
                    for (int rating = item.Snapshot.CurrentRating; rating < item.Snapshot.MaximumRating;)
                    {
                        string value = (++rating).ToString(CultureInfo.InvariantCulture);
                        string action = "rating:" + value;
                        var upgrade = CommerceQuote(eligibility, action, CharacterCyberwareCommerceRules.DefaultRefundPercentage,
                            bookkeepingSources, out var upgradeError);
                        bool supported = upgrade is not null && CommerceReplacementPurchaseMatches(itemPreparation!, item, upgrade, out upgradeError);
                        actions.Add(new(action, "Upgrade to rating " + value + " (" + item.Snapshot.CurrentGradeName + ")",
                            supported, supported ? null : upgradeError));
                    }
                }
                rows.Add(new(id, name, actions, error is null ? [] : [error]));
            }
        }
        catch (Exception ex) when (CommerceDataFailure(ex)) { blockers.Add(ex.Message); items.Clear(); }
        string catalogDigest = bioware is null ? preparation.CatalogDigest
            : CharacterCareerImplantCommerceTransaction.Digest(new { preparation.CatalogDigest, Bioware = bioware.CatalogDigest });
        string sourceDigest = bioware is null ? preparation.CyberwareXmlDigest
            : CharacterCareerImplantCommerceTransaction.Digest(new { Cyberware = preparation.CyberwareXmlDigest, Bioware = bioware.SourceXmlDigest });
        return new(blockers.Count == 0, blockers.ToArray(), effectAuthorities.Count == 0 ? catalogDigest
            : CharacterCareerImplantCommerceTransaction.Digest(new { CatalogDigest = catalogDigest, effectAuthorities }),
            preparation.SettingsProfileId, sourceDigest, rows.ToArray(), items.ToArray())
        { BookkeepingSources = bookkeepingSources };
    }

    public CharacterCareerImplantCommerceQuote? Quote(CharacterCareerImplantCommercePreparation preparation,
        CharacterCareerImplantCommerceRequest request, out string error)
    {
        error = "The installed commerce request is unavailable.";
        if (!preparation.Exact) { error = preparation.Blockers.FirstOrDefault() ?? error; return null; }
        var item = preparation.Items.SingleOrDefault(i => i.InstanceId == request.InstanceId);
        if (item is null) return null;
        // Rating actions are admitted only after the source-bound purchase authority has also
        // checked both ratings, availability, selected settings, price and Essence. Every commit
        // regenerates this catalog; a caller cannot introduce an unreviewed rating through the ID.
        if (request.ActionId != "sell"
            && !preparation.Rows.Any(row => row.InstanceId == request.InstanceId
                && row.Actions.Any(action => action.Id == request.ActionId && action.Supported)))
        { error = "This rating replacement is unavailable under the exact purchase rules and effect authority."; return null; }
        var canonical = CommerceQuote(item, request.ActionId, request.RefundPercentage, preparation.BookkeepingSources, out error);
        if (canonical is null) return null;
        try
        {
            var replacement = request.ActionId == "sell" ? null : XElement.Parse(item.InstalledXml);
            if (replacement is not null)
            {
                replacement.Element("grade")!.Value = canonical.GradeName;
                // ReadInstalled compared every mechanical field with CreateSavedCyberware at the
                // original rating. This source-bound template varies only rating/grade.
                // Keep the planner's RatingReplayRequired flag and all unrelated saved fields.
                if (request.ActionId.StartsWith("rating:", StringComparison.Ordinal))
                    replacement.Element("rating")!.Value = canonical.Rating.ToString(CultureInfo.InvariantCulture);
            }
            var expense = new XElement("expense", Scalar("guid", request.ExpenseId.ToString("D")),
                Scalar("date", request.ExpenseDate.UtcDateTime.ToString("s", CultureInfo.InvariantCulture)),
                Scalar("amount", canonical.NuyenDelta.ToString(CultureInfo.InvariantCulture)),
                Scalar("reason", (request.ActionId == "sell" ? "Sold " : "Upgraded ")
                    + ReadOptionalScalar(XElement.Parse(item.InstalledXml), "improvementsource") + " " + item.Name),
                Scalar("type", "Nuyen"), Scalar("refund", "False"), Scalar("forcecareervisible", "False"));
            // Commerce has no generic purchase-undo authority. Prior purchase history stays intact.
            return new(item, canonical, replacement?.ToString(SaveOptions.DisableFormatting),
                expense.ToString(SaveOptions.DisableFormatting), item.Snapshot.AvailableNuyen,
                checked(item.Snapshot.AvailableNuyen + canonical.NuyenDelta),
                (request.ActionId.StartsWith("rating:", StringComparison.Ordinal)
                    ? "Replace the selected implant at the quoted higher rating and the same grade; retain its source, instance identity and notes. Apply the quoted Essence Hole/Anti-Hole balances. "
                    : "Remove the selected implant or change only its grade; apply the quoted Essence Hole/Anti-Hole balances, creating the reviewed canonical object when needed. ")
                + "Total Essence, Magic and Resonance remain unchanged. Remove only this implant's canonical effects on sale; recalculate them at the replacement rating. Other improvements remain unchanged. Commerce undo is unavailable.")
            {
                CreatedEssenceHoleXml = CreateCommerceBookkeepingXml(preparation.BookkeepingSources, EssenceHoleSourceId,
                    item.Snapshot.EssenceHoleRating, canonical.NewEssenceHoleRating, request),
                CreatedEssenceAntiHoleXml = CreateCommerceBookkeepingXml(preparation.BookkeepingSources, EssenceAntiHoleSourceId,
                    item.Snapshot.EssenceAntiHoleRating, canonical.NewEssenceAntiHoleRating, request)
            };
        }
        catch (Exception ex) when (CommerceDataFailure(ex)) { error = ex.Message; return null; }
    }

    private bool CommerceReplacementPurchaseMatches(CharacterCyberwarePurchasePreparation preparation,
        CharacterCareerImplantCommerceItem item, CharacterCyberwareCommerceQuote commerce, out string error)
    {
        // Probe price/legality, not affordability: the commerce planner checks the actual funds
        // after trade-in. Purchase must not consume an Essence Hole or add a second expense here.
        var pricing = preparation with { AvailableNuyen = decimal.MaxValue };
        var selection = new CharacterCyberwarePurchaseSelection(new(item.SourceId),
            new(Guid.Parse(item.Snapshot.CurrentGradeId)), item.Snapshot.CurrentRating, 0, false, 0m, false);
        var current = Quote(pricing, selection);
        var replacement = Quote(pricing, selection with { Rating = commerce.Rating, GradeId = new(Guid.Parse(commerce.GradeId)) });
        error = !current.Exact ? current.BlockReason : !replacement.Exact ? replacement.BlockReason
            : "Rating replacement requires matching canonical purchase price and Essence under the selected rules.";
        if (!current.Exact || !replacement.Exact || current.BaseCost != commerce.CurrentTotalCost
            || replacement.ChargedCost != commerce.NewTotalCost
            || current.InstalledEssence != commerce.CurrentEssence || replacement.InstalledEssence != commerce.NewEssence)
            return false;
        error = string.Empty;
        return true;
    }

    private static CharacterCyberwareCommerceQuote? CommerceQuote(CharacterCareerImplantCommerceItem item,
        string action, decimal refund, IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> sources, out string error)
    {
        error = "Only sale, a different canonical grade, or a higher canonical rating at the same grade is supported.";
        var quote = CharacterCareerImplantCommerceTransaction.QuoteCanonical(item.Snapshot, action, refund);
        if (quote is null) return null;
        if (!quote.Exact)
        {
            // Ask the same planner about a zero initial balance only after resolving real source authority.
            // No changed prices, rounding, Essence arithmetic or broader effect support.
            if (item.Snapshot.EssenceHoleRating is null && sources.Any(e => e.SourceId.Value.ToString("D") == EssenceHoleSourceId))
                quote = CharacterCareerImplantCommerceTransaction.QuoteCanonical(item.Snapshot, action, refund, createHole: true)!;
            if (!quote.Exact && item.Snapshot.EssenceAntiHoleRating is null
                && sources.Any(e => e.SourceId.Value.ToString("D") == EssenceAntiHoleSourceId))
                quote = CharacterCareerImplantCommerceTransaction.QuoteCanonical(item.Snapshot, action, refund, createAntiHole: true)!;
        }
        if (!quote.Exact)
        {
            error = quote.BlockReason;
            if (error.Contains("source-generated objects", StringComparison.Ordinal))
            {
                string sourceId = error.Contains("Anti-Hole", StringComparison.Ordinal) ? EssenceAntiHoleSourceId : EssenceHoleSourceId;
                error = $"Required canonical Essence bookkeeping source {sourceId} is unavailable or disabled in the current character source/profile context (cyberware.xml).";
            }
            return null;
        }
        foreach (var plan in new[] { (EssenceHoleSourceId, quote.NewEssenceHoleRating), (EssenceAntiHoleSourceId, quote.NewEssenceAntiHoleRating) })
        {
            if (plan.Item2 is not > 0) continue;
            var source = sources.SingleOrDefault(e => e.SourceId.Value.ToString("D") == plan.Item1);
            if (source is null || plan.Item2 > source.MaximumRating)
            { error = "Canonical Essence bookkeeping source/rating is unavailable (cyberware.xml)."; return null; }
        }
        // Exact centi-Essence compensation permits no inferred special-attribute restoration/loss.
        if (!CharacterCareerImplantCommerceTransaction.PreservesEssence(item.Snapshot, quote))
        { error = "Additional Essence loss or sub-centi-Essence rounding needs the full special-attribute authority."; return null; }
        error = string.Empty;
        return quote;
    }

    public string Materialize(string characterXml, CharacterCareerImplantCommerceQuote quote)
    {
        var document = XDocument.Parse(characterXml);
        var root = document.Root!;
        var target = root.Elements("cyberwares").Single().Elements("cyberware")
            .Single(w => ReadOptionalScalar(w, "guid") == quote.Item.InstanceId.ToString("D"));
        if (target.ToString(SaveOptions.DisableFormatting) != quote.Item.InstalledXml
            || !TryReadDecimal(root, "nuyen", out decimal nuyen) || nuyen != quote.NuyenBefore)
            throw new InvalidDataException("The selected implant or funds changed.");
        var expense = XElement.Parse(quote.ExpenseXml);
        if (ContainsGuid(root, Guid.Parse(expense.Element("guid")!.Value)))
            throw new InvalidDataException("The expense identity already exists.");
        var sources = ReadCommerceBookkeepingSources(_sourceData.TryCreateContext(characterXml));
        if (!TryReadEssenceHole(root, EssenceHoleSourceId, out int? hole) || hole != quote.Item.Snapshot.EssenceHoleRating
            || !TryReadEssenceHole(root, EssenceAntiHoleSourceId, out int? antiHole) || antiHole != quote.Item.Snapshot.EssenceAntiHoleRating)
            throw new InvalidDataException("Essence bookkeeping changed after review.");
        ReconcileCommerceEffects(root, target, quote);
        if (quote.ReplacementImplantXml is null) target.Remove();
        else target.ReplaceWith(XElement.Parse(quote.ReplacementImplantXml));
        SetCommerceBookkeeping(root, EssenceHoleSourceId, quote.Canonical.NewEssenceHoleRating,
            quote.CreatedEssenceHoleXml, sources, Guid.Parse(expense.Element("guid")!.Value));
        SetCommerceBookkeeping(root, EssenceAntiHoleSourceId, quote.Canonical.NewEssenceAntiHoleRating,
            quote.CreatedEssenceAntiHoleXml, sources, Guid.Parse(expense.Element("guid")!.Value));
        root.Elements("nuyen").Single().Value = quote.NuyenAfter.ToString(CultureInfo.InvariantCulture);
        GetOrCreateUniqueContainer(root, "expenses").Add(expense);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void SetCommerceBookkeeping(XElement root, string sourceId, int? rating,
        string? createdXml, IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> sources, Guid expenseId)
    {
        var wares = root.Element("cyberwares")!;
        var ware = wares.Elements("cyberware").SingleOrDefault(w => ReadOptionalScalar(w, "sourceid") == sourceId);
        if (rating is null)
        {
            if (ware is not null || createdXml is not null) throw new InvalidDataException("Unexpected Essence bookkeeping.");
            return;
        }
        if (ware is null)
        {
            if (rating <= 0 || createdXml is null) throw new InvalidDataException("Missing reviewed Essence bookkeeping creation.");
            var entry = RequireCommerceBookkeepingSource(sources, sourceId);
            Guid id = CharacterCareerImplantCommerceTransaction.BookkeepingInstanceId(expenseId, sourceId);
            var expected = CreateCommerceBookkeeping(entry, rating.Value, id);
            if (createdXml != expected.ToString(SaveOptions.DisableFormatting) || CommerceIdentityExists(root, id))
                throw new InvalidDataException("Created Essence bookkeeping XML or identity is not the authenticated reviewed materialization.");
            wares.Add(expected);
            return;
        }
        if (createdXml is not null) throw new InvalidDataException("Existing Essence bookkeeping cannot be recreated.");
        ValidateCommerceBookkeeping(ware, RequireCommerceBookkeepingSource(sources, sourceId));
        if (rating == 0) ware.Remove();
        else ware.Elements("rating").Single().Value = rating.Value.ToString(CultureInfo.InvariantCulture);
    }

    private IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> ReadCommerceBookkeepingSources(ICharacterSourceDataContext? context)
    {
        byte[] bytes = _readAllBytes(Path.Combine(_overlays.GetCatalog().BaseDataPath, "cyberware.xml"));
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != CharacterCyberwarePurchaseLegacyAuthority.CyberwareXmlSha256)
            throw new InvalidDataException("Canonical Essence bookkeeping source changed: cyberware.xml.");
        using var stream = new MemoryStream(bytes, false);
        var sourceRoot = XDocument.Load(stream).Root!;
        var entries = new List<CharacterCyberwarePurchaseCatalogEntry>();
        foreach (string sourceId in new[] { EssenceHoleSourceId, EssenceAntiHoleSourceId })
        {
            var row = sourceRoot.Element("cyberwares")!.Elements("cyberware").Single(e => ReadOptionalScalar(e, "id") == sourceId);
            string name = ReadOptionalScalar(row, "name");
            if (context is null || !context.TryResolveCyberwareCommerceSource(sourceId, name, "Cyberware", out var resolved)
                || resolved.SourceId != sourceId || resolved.Name != name || resolved.Source != ReadOptionalScalar(row, "source")
                || resolved.CostExpression != ReadOptionalScalar(row, "cost") || resolved.EssenceExpression != ReadOptionalScalar(row, "ess")
                || resolved.CapacityExpression != ReadOptionalScalar(row, "capacity") || resolved.MinimumRatingExpression != ReadOptionalScalar(row, "minrating")
                || resolved.MaximumRatingExpression != ReadOptionalScalar(row, "rating") || resolved.ForcedGrade != ReadOptionalScalar(row, "forcegrade")
                || resolved.SourceEntryUsesGeneratedOrImprovementSemantics || resolved.EssenceModifierPostExpression != "{Modifier}"
                || resolved.BannedGrades.Count != 0) continue;
            var gradeRow = sourceRoot.Element("grades")!.Elements("grade").Single(g => ReadOptionalScalar(g, "name") == resolved.ForcedGrade);
            var grade = resolved.Grades.SingleOrDefault(g => g.Id == ReadOptionalScalar(gradeRow, "id"));
            if (grade is null || grade.SpecialSemantics || grade.Name != resolved.ForcedGrade || grade.Name != "None"
                || !TryReadDecimal(gradeRow, "cost", out decimal cost) || grade.CostMultiplier != cost
                || !TryReadDecimal(gradeRow, "ess", out decimal essence) || grade.EssenceMultiplier != essence
                || grade.Source != ReadOptionalScalar(gradeRow, "source") || !TryReadInteger(gradeRow, "avail", out int availability)) continue;
            entries.Add(new(new(Guid.Parse(sourceId)), name, ReadOptionalScalar(row, "category"),
                resolved.EssenceExpression, resolved.CapacityExpression, ReadOptionalScalar(row, "avail"), resolved.CostExpression,
                resolved.Source, ReadOptionalScalar(row, "page"), false, resolved.ForcedGrade, [],
                [new(new(Guid.Parse(grade.Id)), grade.Name, cost, essence, availability)],
                0, int.Parse(resolved.MaximumRatingExpression, CultureInfo.InvariantCulture)));
        }
        return entries;
    }

    private static CharacterCyberwarePurchaseCatalogEntry RequireCommerceBookkeepingSource(
        IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> sources, string sourceId)
        => sources.SingleOrDefault(e => e.SourceId.Value.ToString("D") == sourceId)
            ?? throw new InvalidDataException($"Canonical Essence bookkeeping source {sourceId} is unavailable or disabled in the current character source/profile context (cyberware.xml).");

    private static string? CreateCommerceBookkeepingXml(IReadOnlyList<CharacterCyberwarePurchaseCatalogEntry> sources,
        string sourceId, int? before, int? after, CharacterCareerImplantCommerceRequest request)
    {
        if (before.HasValue || after is null or 0) return null;
        Guid id = CharacterCareerImplantCommerceTransaction.BookkeepingInstanceId(request.ExpenseId, sourceId);
        if (id == Guid.Empty || id == request.InstanceId || id == request.ExpenseId || id == request.OperationId)
            throw new InvalidDataException("The reviewed bookkeeping identity conflicts with the operation.");
        return CreateCommerceBookkeeping(RequireCommerceBookkeepingSource(sources, sourceId), after.Value, id)
            .ToString(SaveOptions.DisableFormatting);
    }

    private static XElement CreateCommerceBookkeeping(CharacterCyberwarePurchaseCatalogEntry entry, int rating, Guid id)
    {
        if (rating <= 0 || rating > entry.MaximumRating) throw new InvalidDataException("Essence bookkeeping rating is outside canonical source bounds.");
        var grade = entry.Grades.Single();
        var saved = CreateSavedCyberware(entry, grade.Name,
            new(entry.SourceId, grade.Id, rating, 0, false, 0m, false), new(id));
        // Cyberware.Create/Save preserves these constructor defaults when the source omits them.
        // Ordinary purchase serialization stays untouched; these are the exact two pinned bookkeeping rows.
        saved.Element("limbslotcount")!.Value = "1";
        saved.Element("minagility")!.Value = "3";
        saved.Element("minstrength")!.Value = "3";
        saved.Element("minrating")!.Value = string.Empty;
        saved.Element("ratinglabel")!.Value = "String_Rating";
        return saved;
    }

    private static void ValidateCommerceBookkeeping(XElement ware, CharacterCyberwarePurchaseCatalogEntry entry)
    {
        if (!TryReadInteger(ware, "rating", out int rating) || rating < 0 || rating > entry.MaximumRating
            || !Guid.TryParseExact(ReadOptionalScalar(ware, "guid"), "D", out Guid id) || id == Guid.Empty
            || ReadOptionalScalar(ware, "name") != entry.Name || ReadOptionalScalar(ware, "sourceid") != entry.SourceId.Value.ToString("D")
            || ware.HasAttributes || ware.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1))
            throw new InvalidDataException("Essence bookkeeping is ambiguous or inconsistent with its canonical source.");
        var expected = CreateCommerceBookkeeping(entry, Math.Max(1, rating), id);
        expected.Element("rating")!.Value = rating.ToString(CultureInfo.InvariantCulture);
        // Preserve the base's previously admitted minimal/purchase-normalized serialization too.
        var previous = CreateSavedCyberware(entry with { MinimumRating = 1 }, entry.ForcedGrade,
            new(entry.SourceId, entry.Grades.Single().Id, rating, 0, false, 0m, false), new(id));
        if (!MatchesBookkeepingFields(ware, expected) && !MatchesBookkeepingFields(ware, previous))
            throw new InvalidDataException("Essence bookkeeping has unsupported saved effects or expressions.");
    }

    private static bool MatchesBookkeepingFields(XElement ware, XElement expected)
        => ware.Elements().All(e => e.Name.LocalName is "notes" or "notesColor" or "sortorder"
            ? !e.HasElements && !e.HasAttributes && e.Name.Namespace == XNamespace.None
            : XNode.DeepEquals(e, expected.Element(e.Name)));

    private static bool CommerceIdentityExists(XElement root, Guid id)
        => root.Descendants().Any(e => !e.HasElements && Guid.TryParse(e.Value, out Guid value) && value == id)
            || root.DescendantsAndSelf().Attributes().Any(a => Guid.TryParse(a.Value, out Guid value) && value == id);

    private static bool CommerceTemplateEquals(XElement actual, XElement expected)
    {
        var copy = new XElement(actual);
        foreach (string cosmetic in new[] { "notes", "notesColor", "sortorder" })
        {
            var fields = copy.Elements(cosmetic).ToArray();
            if (fields.Length != 1 || fields[0].HasElements || fields[0].HasAttributes) return false;
            fields[0].Value = expected.Element(cosmetic)!.Value;
        }
        return XNode.DeepEquals(copy, expected);
    }

    private static void ValidateCommerceBiowareSource(XElement root, XElement ware, XElement source,
        XElement[] cyberwareSources, XElement[] biowareSources)
    {
        if (source.Element("isgeneware") is XElement geneware && geneware.Value != bool.FalseString)
            throw new InvalidDataException("Geneware needs its own nonretroactive Essence and replacement authority.");
        string? blocker = BiowareSourceBlocker(source);
        if (blocker is not null) throw new InvalidDataException(blocker);
        TryReadRatingBounds(source, out int minimum, out int maximum);
        for (int rating = minimum; rating <= maximum; rating++)
            if (!TryCommerceEffects(source, rating, out _, out blocker)) throw new InvalidDataException(blocker);
        // Removing a prerequisite cannot silently invalidate another installed implant.
        // Complex dependencies remain explicit until their native reconciliation exists.
        string id = ReadOptionalScalar(source, "id"), name = ReadOptionalScalar(source, "name");
        foreach (var other in root.Element("cyberwares")!.Elements("cyberware").Where(w => w != ware))
        {
            string otherKind = ReadOptionalScalar(other, "improvementsource");
            var sources = otherKind == "Bioware" ? biowareSources : cyberwareSources;
            var otherSource = sources.SingleOrDefault(s => ReadOptionalScalar(s, "id") == ReadOptionalScalar(other, "sourceid"));
            if (otherSource?.Element("required")?.Descendants("bioware").Any(e => e.HasElements || e.HasAttributes
                || e.Value == name || e.Value.Equals(id, StringComparison.OrdinalIgnoreCase)) == true)
                throw new InvalidDataException("Another installed implant depends on this Bioware; reconcile that prerequisite before commerce.");
        }
    }

    private static bool CommerceDataFailure(Exception ex) => ex is XmlException or InvalidDataException
        or InvalidOperationException or ArgumentException or FormatException or OverflowException or IOException or UnauthorizedAccessException;
}
