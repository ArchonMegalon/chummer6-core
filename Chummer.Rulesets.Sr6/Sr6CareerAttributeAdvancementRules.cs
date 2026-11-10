using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// Bounded authority: re-evaluate Core's finalization archive, then replay natural
/// purchases. Imported XML, new effects and special attributes need their own authority.
/// </summary>
public static class Sr6CareerAttributeAdvancementRules
{
    private sealed record Authority(XElement Root, Sr6CreationFoundationState Foundation,
        Sr6CreationNaturalValues Natural, Sr6CreationPassiveValues Passive, string RuleDigest,
        IReadOnlyList<string> Sources);

    public static Sr6CareerAttributeResult Quote(WorkspaceStoredDocument saved, OwnerScope owner,
        string attributeId, string runtimeDigest)
    {
        if (attributeId is not ("Body" or "Agility" or "Reaction" or "Strength" or "Willpower" or "Logic" or "Intuition" or "Charisma"))
            return Unavailable("sr6_attribute_special_authority_unavailable");
        if (!TryResolve(saved, owner, runtimeDigest, out var authority, out string error)) return Unavailable(error);
        var row = authority!.Natural.Attributes!.Single(x => x.AttributeId == attributeId);
        var passive = authority.Passive.Attributes.Single(x => x.AttributeId == attributeId);
        int karma = int.Parse(authority.Root.Element("karma")!.Value, CultureInfo.InvariantCulture);
        int cost = new Sr6AdvancementProvider().AttributeCost(row.Rating + 1);
        string? blocker = row.Rating >= row.Maximum ? "sr6_attribute_natural_limit"
            : karma < cost ? "sr6_attribute_insufficient_karma" : null;
        var sources = authority.Sources
            .Concat(authority.Foundation.Selection?.Qualities?.Values.Where(x => x.AttributeId == attributeId)
                .Select(x => x.SourceAnchorId) ?? [])
            .Concat(authority.Foundation.Selection?.AdeptPowers?.Powers.Select(x => x.Option.SourceAnchorId) ?? [])
            .Distinct(StringComparer.Ordinal).ToArray();
        var quote = new Sr6CareerAttributeQuote(Sr6CareerAttributeSchemas.QuoteV1, owner.NormalizedValue,
            saved.Id, saved.ContentRevision,
            Sr6CareerAttributeIntegrity.Digest(new { saved.Document.Content, saved.Document.AuxiliaryStateDigest }),
            authority.RuleDigest, attributeId, row.Rating, row.Rating + 1, passive.PermanentBonus!.Value,
            row.Maximum, karma, cost, blocker, sources, "");
        quote = quote with { BindingDigest = Sr6CareerAttributeIntegrity.BindingDigest(quote) };
        return new(Sr6CareerAttributeOutcome.Available, quote);
    }

    public static Sr6CareerAttributeResult Prepare(WorkspaceStoredDocument saved, OwnerScope owner,
        Sr6CareerAttributeCommand command, string runtimeDigest, out WorkspaceDocument? replacement)
    {
        replacement = null;
        if (!Sr6CareerAttributeIntegrity.IsConfirmed(command) || command.Quote.OwnerId != owner.NormalizedValue
            || command.Quote.WorkspaceId != saved.Id)
            return new(Sr6CareerAttributeOutcome.Conflict, Error: "sr6_attribute_command_invalid");
        var prior = Sr6CareerAttributeIntegrity.Lookup(saved, command);
        if (prior.Outcome != Sr6CareerAttributeOutcome.NotFound) return prior;
        if (saved.ContentRevision != command.Quote.WorkspaceRevision || saved.SavedRevision != saved.ContentRevision)
            return new(Sr6CareerAttributeOutcome.Conflict, Error: "sr6_attribute_revision_conflict");
        var current = Quote(saved, owner, command.Quote.AttributeId, runtimeDigest);
        if (current.Quote is not { } quote) return current;
        if (quote.BindingDigest != command.Quote.BindingDigest)
            return new(Sr6CareerAttributeOutcome.Conflict, Error: "sr6_attribute_quote_changed");
        if (!quote.CanAdvance) return new(Sr6CareerAttributeOutcome.Blocked, quote, Error: quote.Blocker);
        if ((saved.Document.AuxiliaryState.Sr6CareerAttributeReceipts?.Count ?? 0) >= 4096)
            return Unavailable("sr6_attribute_history_capacity");
        if (!TryResolve(saved, owner, runtimeDigest, out var authority, out string error)) return Unavailable(error);
        var root = authority!.Root;
        if (root.Element("expenses")?.Elements().Any(x =>
            Guid.TryParse(x.Element("guid")?.Value, out var id) && id == command.OperationId) == true)
            return new(Sr6CareerAttributeOutcome.IdempotencyConflict, Error: "sr6_attribute_expense_identity_reserved");
        var natural = Increment(authority.Natural, quote.AttributeId);
        Project(root, authority.Foundation, natural);
        root.Element("karma")!.Value = (quote.AvailableKarma - quote.KarmaCost).ToString(CultureInfo.InvariantCulture);
        if (root.Element("expenses") is null) root.Add(new XElement("expenses"));
        root.Element("expenses")!.Add(Sr6CareerAttributeIntegrity.Expense(command));
        var receipt = new Sr6CareerAttributeReceipt(command, saved.ContentRevision + 1,
            quote.AvailableKarma - quote.KarmaCost, command.OperationId, -quote.KarmaCost, "");
        receipt = receipt with { ReceiptDigest = Sr6CareerAttributeIntegrity.ReceiptDigest(receipt) };
        replacement = saved.Document with
        {
            State = saved.Document.State with
            {
                Payload = root.ToString(SaveOptions.DisableFormatting),
                AuxiliaryState = saved.Document.AuxiliaryState with
                { Sr6CareerAttributeReceipts = [.. saved.Document.AuxiliaryState.Sr6CareerAttributeReceipts ?? [], receipt] }
            }
        };
        return new(Sr6CareerAttributeOutcome.Applied, quote, receipt);
    }

    private static bool TryResolve(WorkspaceStoredDocument saved, OwnerScope owner, string runtimeDigest,
        out Authority? authority, out string error)
    {
        authority = null;
        error = "sr6_attribute_natural_authority_unavailable";
        var state = saved.Document.AuxiliaryState;
        if (saved.Document.RulesetId != RulesetDefaults.Sr6 || saved.ContentRevision != saved.SavedRevision
            || state.Sr6CreationFinalizationArchive is not { } archive
            || !Sr6CreationFinalizationIntegrity.IsValidArchive(saved.Id, saved.ContentRevision, state)
            || !Sr6CareerAttributeIntegrity.IsValidLedger(saved.Id, saved.ContentRevision, state)) return false;
        var original = new WorkspaceStoredDocument(saved.Id,
            new WorkspaceDocument(new WorkspaceDocumentState(RulesetDefaults.Sr6, 1, "workspace", archive.OriginalDocument.Content)
                { AuxiliaryState = archive.OriginalState }),
            archive.Review.Binding.ContentRevision, archive.Review.Binding.SavedRevision, saved.LastUpdatedUtc);
        // Load re-evaluates every saved choice against the current bootstrap/rule environment.
        var foundation = Sr6CreationFoundationRules.Load(original).Value;
        var reviewed = Sr6CreationFinalizationRules.Review(original).Value;
        if (foundation?.DraftSummary?.NaturalValues is not { Attributes: not null } natural
            || foundation.DraftSummary.PassiveValues is not { } passive
            || reviewed is not { CanFinalize: true } || reviewed.ReviewDigest != archive.Review.ReviewDigest)
            return false;
        try
        {
            var root = XDocument.Parse(saved.Document.Content).Root;
            var expected = XDocument.Parse(reviewed.Document.Content).Root;
            if (!HasRequiredContainers(root) || !HasRequiredContainers(expected))
            {
                error = "sr6_attribute_required_xml_unavailable";
                return false;
            }
            if (root.Elements("karma").Count() != 1 || root.Elements("expenses").Count() > 1
                || !int.TryParse(root.Element("karma")!.Value, NumberStyles.None, CultureInfo.InvariantCulture, out int karma)
                || karma < 0) return false;
            foreach (var receipt in state.Sr6CareerAttributeReceipts ?? [])
            {
                var q = receipt.Command.Quote;
                var row = natural.Attributes!.SingleOrDefault(x => x.AttributeId == q.AttributeId);
                if (q.OwnerId != owner.NormalizedValue || row is null || row.Rating != q.NaturalRating
                    || row.Maximum != q.NaturalMaximum || q.TargetNaturalRating > row.Maximum) return false;
                var expenses = root.Element("expenses")?.Elements().Where(x =>
                    Guid.TryParse(x.Element("guid")?.Value, out var id) && id == receipt.ExpenseId).ToArray() ?? [];
                if (expenses.Length != 1 || !XNode.DeepEquals(expenses[0], Sr6CareerAttributeIntegrity.Expense(receipt.Command)))
                    return false;
                natural = Increment(natural, q.AttributeId);
            }
            if (state.Sr6CareerAttributeReceipts is { Count: > 0 }) Project(expected, foundation, natural);
            // Normal workspace earnings/expenses can change; rule-bearing XML cannot silently
            // override the re-evaluated archive. In particular no raw maximum or quality name is authority.
            var comparison = new XElement(root);
            comparison.Element("karma")!.Value = expected.Element("karma")!.Value;
            comparison.Element("expenses")?.Remove();
            expected.Element("expenses")?.Remove();
            // Legacy views sometimes materialize totalvalue as an effective value. It is display
            // only: accept exactly the Core natural or Core effective value, never infer allocation.
            passive = Sr6CreationPassiveValuesRules.Project(foundation, natural)!;
            foreach (var attribute in comparison.Element("attributes")!.Elements("attribute"))
            {
                string id = attribute.Element("sr6id")?.Value ?? "";
                var row = natural.Attributes!.SingleOrDefault(x => x.AttributeId == id);
                var effect = passive.Attributes.SingleOrDefault(x => x.AttributeId == id);
                if (row is null || effect?.PermanentBonus is not >= 0 or > 4 || effect.Rating is null) return false;
                if (!int.TryParse(attribute.Element("totalvalue")?.Value, out int total)
                    || (total != row.Rating && total != effect.Rating)) return false;
                attribute.Element("totalvalue")!.Value = row.Rating.ToString(CultureInfo.InvariantCulture);
                // Augmented ceiling is legacy display metadata only; it grants no natural rank.
                attribute.Element("metatypeaugmax")?.Remove();
            }
            if (!XNode.DeepEquals(comparison, expected)) return false;
            authority = new(root, foundation, natural, passive,
                Sr6CareerAttributeIntegrity.Digest(new { runtimeDigest, foundation.Binding.AuthorityDigest, reviewed.ReviewDigest }),
                reviewed.SourceAnchorIds);
            return true;
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException or FormatException or OverflowException)
        { return false; }
    }

    private static bool HasRequiredContainers([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] XElement? root)
    {
        if (root is null || root.Name != "character"
            || new[] { "attributes", "sr6creation", "karma", "physicalcm", "stuncm" }
                .Any(name => root.Elements(name).Count() != 1)) return false;
        var projection = root.Element("sr6creation")!;
        return projection.Elements("passiveattributes").Count() == 1
            && projection.Elements("derived").Count() == 1;
    }

    private static Sr6CreationNaturalValues Increment(Sr6CreationNaturalValues natural, string id) => natural with
    {
        Attributes = natural.Attributes!.Select(x => x.AttributeId == id
            ? x with { Rating = x.Rating + 1, KarmaIncrease = x.KarmaIncrease + 1 } : x).ToArray()
    };

    private static void Project(XElement root, Sr6CreationFoundationState foundation, Sr6CreationNaturalValues natural)
    {
        foreach (var row in natural.Attributes!)
        {
            var attribute = root.Element("attributes")!.Elements("attribute").Single(x => x.Element("sr6id")?.Value == row.AttributeId);
            attribute.Element("karma")!.Value = row.KarmaIncrease.ToString(CultureInfo.InvariantCulture);
            // Keep native SR6 XML convention: totalvalue is natural; passiveattributes carries effects.
            attribute.Element("totalvalue")!.Value = row.Rating.ToString(CultureInfo.InvariantCulture);
        }
        var passive = Sr6CreationPassiveValuesRules.Project(foundation, natural)!;
        var projection = root.Element("sr6creation")!;
        projection.Element("passiveattributes")!.ReplaceWith(new XElement("passiveattributes", passive.Attributes.Select(row =>
            new XElement("attribute", new XAttribute("id", row.AttributeId), new XElement("natural", row.NaturalRating),
                new XElement("bonus", row.PermanentBonus), new XElement("effective", row.Rating)))));
        projection.Element("derived")!.ReplaceWith(new XElement("derived", passive.Derived.Select(row =>
            new XElement("stat", new XAttribute("id", row.Id), row.Value is null ? null : new XElement("value", row.Value),
                new XElement("calculation", row.Calculation), new XElement("source", row.SourceAnchorId)))));
        root.Element("physicalcm")!.Value = passive.Derived.Single(x => x.Id == "physical-monitor").Value!.Value.ToString(CultureInfo.InvariantCulture);
        root.Element("stuncm")!.Value = passive.Derived.Single(x => x.Id == "stun-monitor").Value!.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static Sr6CareerAttributeResult Unavailable(string error) => new(Sr6CareerAttributeOutcome.Unavailable, Error: error);
}
