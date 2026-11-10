using System.Globalization;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Historical shape and replay only; current rule authority lives in Rulesets.Sr6.</summary>
public static class Sr6CareerAttributeIntegrity
{
    public static string Digest<T>(T value) => Sr6CreationFoundationIntegrity.Digest(value);
    public static string BindingDigest(Sr6CareerAttributeQuote quote) => Digest(quote with { BindingDigest = "" });
    public static string ReceiptDigest(Sr6CareerAttributeReceipt receipt) => Digest(receipt with { ReceiptDigest = "" });

    public static bool IsConfirmed(Sr6CareerAttributeCommand? command) => command is
        { ExplicitlyConfirmed: true, Quote: { } q }
        && command.OperationId != Guid.Empty && command.ExpenseDateLocal.Kind == DateTimeKind.Unspecified
        && q.Schema == Sr6CareerAttributeSchemas.QuoteV1 && !string.IsNullOrWhiteSpace(q.OwnerId)
        && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(q.WorkspaceId)
        && q.WorkspaceRevision > 0 && q.WorkspaceRevision < long.MaxValue
        && q.AttributeId is "Body" or "Agility" or "Reaction" or "Strength" or "Willpower" or "Logic" or "Intuition" or "Charisma"
        && q.NaturalRating is >= 1 and < 100 && q.TargetNaturalRating == q.NaturalRating + 1
        && q.AugmentationBonus is >= 0 and <= 4 && q.NaturalMaximum is >= 1 and < 100
        && q.AvailableKarma >= 0 && q.KarmaCost == q.TargetNaturalRating * 5
        && q.SourceAnchorIds is { Count: > 0 and <= 128 } && q.SourceAnchorIds.All(x => !string.IsNullOrWhiteSpace(x))
        && CharacterCreationBootstrapBindingDigest.IsCanonical(q.SourceDigest)
        && CharacterCreationBootstrapBindingDigest.IsCanonical(q.RuleDigest)
        && BindingDigest(q) == q.BindingDigest;

    public static bool TryFreezeCommand(Sr6CareerAttributeCommand? input, out Sr6CareerAttributeCommand command)
    {
        command = null!;
        if (!IsConfirmed(input)) return false;
        try
        {
            command = JsonSerializer.Deserialize<Sr6CareerAttributeCommand>(JsonSerializer.Serialize(input))!;
            return IsConfirmed(command);
        }
        catch (JsonException) { return false; }
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, WorkspaceDocumentAuxiliaryState state)
    {
        if (state.Sr6CareerAttributeReceipts is not { } entries) return true;
        if (state.Sr6CreationFinalizationArchive is not { } archive || entries.Count is 0 or > 4096) return false;
        long previous = archive.Receipt.ContentRevision;
        var operations = new HashSet<Guid>();
        string? owner = null;
        foreach (var entry in entries)
        {
            if (entry is null || !IsConfirmed(entry.Command)) return false;
            var q = entry.Command.Quote;
            owner ??= q.OwnerId;
            if (q.WorkspaceId != id || q.OwnerId != owner || !q.CanAdvance
                || q.TargetNaturalRating > q.NaturalMaximum || q.AvailableKarma < q.KarmaCost
                || entry.CommittedRevision != q.WorkspaceRevision + 1 || q.WorkspaceRevision < previous
                || entry.CommittedRevision > revision || !operations.Add(entry.Command.OperationId)
                || entry.ExpenseId != entry.Command.OperationId || entry.ExpenseAmount != -q.KarmaCost
                || entry.KarmaAfter != q.AvailableKarma - q.KarmaCost || ReceiptDigest(entry) != entry.ReceiptDigest)
                return false;
            previous = entry.CommittedRevision;
        }
        return true;
    }

    public static Sr6CareerAttributeResult Lookup(WorkspaceStoredDocument saved, Sr6CareerAttributeCommand command)
    {
        if (!IsValidLedger(saved.Id, saved.ContentRevision, saved.Document.AuxiliaryState))
            return new(Sr6CareerAttributeOutcome.Corrupt, Error: "sr6_attribute_history_invalid");
        var receipt = saved.Document.AuxiliaryState.Sr6CareerAttributeReceipts?
            .SingleOrDefault(x => x.Command.OperationId == command.OperationId);
        if (receipt is null) return new(Sr6CareerAttributeOutcome.NotFound);
        if (Digest(receipt.Command) != Digest(command) || !saved.CanReplayReceipt(receipt.CommittedRevision))
            return new(Sr6CareerAttributeOutcome.IdempotencyConflict, Error: "sr6_attribute_operation_reserved");
        try
        {
            var root = XDocument.Parse(saved.Document.Content).Root;
            var expenses = root?.Elements("expenses").SelectMany(x => x.Elements()).Where(x =>
                Guid.TryParse(x.Element("guid")?.Value, out var id) && id == receipt.ExpenseId).ToArray() ?? [];
            if (expenses.Length != 1 || !XNode.DeepEquals(expenses[0], Expense(receipt.Command)))
                return new(Sr6CareerAttributeOutcome.Corrupt, Error: "sr6_attribute_expense_missing_or_changed");
        }
        catch (XmlException) { return new(Sr6CareerAttributeOutcome.Corrupt, Error: "sr6_attribute_document_invalid"); }
        return new(Sr6CareerAttributeOutcome.Replayed, receipt.Command.Quote, receipt);
    }

    public static XElement Expense(Sr6CareerAttributeCommand command) => new("expense",
        new XElement("guid", command.OperationId.ToString("D")),
        new XElement("date", XmlConvert.ToString(command.ExpenseDateLocal, XmlDateTimeSerializationMode.Unspecified)),
        new XElement("amount", (-command.Quote.KarmaCost).ToString(CultureInfo.InvariantCulture)),
        new XElement("reason", "SR6 natural attribute: " + command.Quote.AttributeId + " " + command.Quote.TargetNaturalRating),
        new XElement("type", "Karma"), new XElement("refund", false), new XElement("forcecareervisible", true));
}
