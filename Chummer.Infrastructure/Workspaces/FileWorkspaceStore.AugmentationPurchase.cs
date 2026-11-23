using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterAugmentationPurchaseCommitCapability
{
    public CharacterAugmentationPurchaseCommitResult CommitAugmentationPurchase(
        OwnerScope owner, CharacterWorkspaceId workspaceId,
        CharacterAugmentationPurchaseCommand command,
        ICharacterAugmentationPurchaseAuthority authority,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(authority);
        if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)) || command.Binding is null
            || command.Binding.OwnerId != owner.NormalizedValue
            || command.Binding.WorkspaceId != workspaceId.Value)
            return new(true, "The purchase does not belong to this runner and owner.");
        string? path = TryGetPath(owner, workspaceId);
        if (path is null) throw new IOException("The saved runner is unavailable.");
        using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
        var read = ReadWorkspaceUnderLease(owner, workspaceId, path, out var delegatedEditLedger);
        if (!read.Success || read.Value is not { } saved)
            throw new IOException("The saved runner could not be verified.");
        cancellationToken.ThrowIfCancellationRequested();
        if (authority.LookupAugmentation(saved.Document.Content, command).AlreadyApplied)
            return new(false);
        if (saved.ContentRevision != command.ExpectedContentRevision
            || saved.SavedRevision != saved.ContentRevision || saved.ContentRevision == long.MaxValue)
            return new(true, "The saved runner changed. Reload before reviewing again.");

        // Re-evaluate canonical source, quote and runner inside the same lease as
        // the single replacement. No caller-provided candidate XML is accepted.
        var materialized = authority.MaterializeAugmentation(
            saved.Document.Content, saved.ContentRevision, command);
        if (materialized.AlreadyApplied) return new(false);
        if (!materialized.Applied)
            return new(true, materialized.BlockReason ?? "The purchase is unavailable.");
        if (materialized.Receipt is not { } receipt || receipt.Command is null
            || JsonSerializer.Serialize(receipt.Command) != JsonSerializer.Serialize(command)
            || !authority.LookupAugmentation(materialized.CharacterXml, command).AlreadyApplied)
            throw new IOException("The purchase candidate could not be verified.");
        var replacement = saved.Document with
        {
            State = saved.Document.State with { Payload = materialized.CharacterXml }
        };
        long revision = checked(saved.ContentRevision + 1);
        var record = BuildPersistedRecord(replacement, revision, revision, saved.LocalHistory!,
            delegatedEditLedger, saved.DelegatedGmHistorySegmentStarts);
        WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
            beforeTargetReplace: cancellationToken.ThrowIfCancellationRequested,
            admittedAugmentationReceipt: receipt);
        // A materialized candidate or returned write is never UI success.
        // The caller must reopen and perform original-command receipt lookup.
        return new(false);
    }

    private static bool TryReadAugmentationLedger(string? xml, long revision,
        out Dictionary<Guid, string> ledger, string? workspaceId = null)
    {
        ledger = new();
        // Other workspace payload kinds remain opaque. A replacement cannot drop
        // a previous XML ledger: the central transition guard compares both sides.
        if (string.IsNullOrEmpty(xml) || !xml.Contains("augmentationpurchases", StringComparison.Ordinal))
            return true;
        try
        {
            var document = XDocument.Parse(xml);
            var containers = document.Descendants().Where(e => e.Name.LocalName == "augmentationpurchases").ToArray();
            if (containers.Length == 0) return true;
            if (containers.Length != 1 || document.Root?.Name != "character"
                || containers[0].Name != "augmentationpurchases"
                || containers[0].Parent != document.Root || containers[0].HasAttributes)
                return false;
            foreach (var row in containers[0].Elements())
            {
                if (row.Name != "purchase" || row.HasElements || row.Attributes().Count() != 1
                    || !Guid.TryParseExact((string?)row.Attribute("operation"), "D", out var operation)
                    || operation == Guid.Empty || ledger.ContainsKey(operation)) return false;
                var receipt = JsonSerializer.Deserialize<CharacterAugmentationPurchaseReceipt>(row.Value);
                if (receipt?.Command is not { } command || command.Binding is null || command.Selection is null
                    || command.NewInstanceIds is null || receipt.InstanceIds is null
                    || receipt.Schema != CharacterAugmentationPurchaseSchemas.ReceiptV1
                    || receipt.Binding != command.Binding || receipt.Binding.OperationId != operation
                    || workspaceId is not null && receipt.Binding.WorkspaceId != workspaceId
                    || string.IsNullOrWhiteSpace(receipt.Binding.OwnerId) || string.IsNullOrWhiteSpace(receipt.Binding.WorkspaceId)
                    || receipt.PreviousContentRevision < 0 || receipt.PreviousContentRevision == long.MaxValue
                    || receipt.PreviousContentRevision != command.ExpectedContentRevision
                    || receipt.NewContentRevision != receipt.PreviousContentRevision + 1 || receipt.NewContentRevision > revision
                    || receipt.PreviousCharacterDigest != command.ExpectedCharacterDigest
                    || receipt.CatalogDigest != command.ExpectedCatalogDigest || receipt.QuoteDigest != command.ExpectedQuoteDigest
                    || receipt.SettingsProfileId != command.ExpectedSettingsProfileId
                    || receipt.Kind != command.Selection.Kind || !Enum.IsDefined(receipt.Kind)
                    || receipt.SourceId != command.Selection.SourceId || receipt.GradeId != command.Selection.GradeId
                    || receipt.SourceId == Guid.Empty || receipt.GradeId == Guid.Empty || receipt.SourceId == receipt.GradeId
                    || receipt.ExpenseId != command.NewExpenseId || receipt.ExpenseId == Guid.Empty
                    || command.Selection.Quantity < 1 || command.NewInstanceIds.Count != command.Selection.Quantity
                    || !receipt.InstanceIds.SequenceEqual(command.NewInstanceIds)
                    || command.NewInstanceIds.Append(command.NewExpenseId).Any(id => id == Guid.Empty
                        || id == receipt.SourceId || id == receipt.GradeId)
                    || command.NewInstanceIds.Append(command.NewExpenseId).Distinct().Count() != command.NewInstanceIds.Count + 1
                    || command.ExpenseDate.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(receipt.SettingsProfileId)
                    || new[] { receipt.PreviousCharacterDigest, receipt.CatalogDigest, receipt.QuoteDigest, receipt.MaterializedDigest }
                        .Any(d => !CharacterCyberwarePurchaseRules.IsCanonicalDigest(d))
                    || receipt.CommandDigest != AugmentationLedgerDigest(command)
                    || receipt.ReceiptDigest != AugmentationLedgerDigest(receipt with { ReceiptDigest = string.Empty }))
                    return false;
                ledger.Add(operation, row.Value);
            }
            return true;
        }
        catch (Exception error) when (error is XmlException or JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string AugmentationLedgerDigest<T>(T value)
        => CharacterCyberwarePurchaseRules.ComputeCharacterDigest(JsonSerializer.Serialize(value));

    private void ValidateAugmentationWrite(string path, PersistedWorkspaceRecord record,
        WorkspaceWriteDisposition disposition, CharacterAugmentationPurchaseReceipt? admittedReceipt)
    {
        var previous = disposition == WorkspaceWriteDisposition.ReplaceExisting ? ReadPrivateRecord(path) : null;
        if (!TryReadAugmentationLedger(previous is null ? null : ResolveContent(previous), previous?.ContentRevision ?? 0, out var before)
            || !TryReadAugmentationLedger(ResolveContent(record), record.ContentRevision ?? 0, out var after)
            || before.Any(row => !after.TryGetValue(row.Key, out var value) || value != row.Value))
            throw new IOException("The durable augmentation purchase history is invalid or changed.");
        if (admittedReceipt is null)
        {
            if (after.Count != before.Count)
                throw new IOException("Augmentation purchases require the typed workspace transaction.");
            return;
        }
        if (previous is null || before.ContainsKey(admittedReceipt.Binding.OperationId)
            || after.Count != before.Count + 1
            || !after.TryGetValue(admittedReceipt.Binding.OperationId, out var added)
            || added != JsonSerializer.Serialize(admittedReceipt))
            throw new IOException("The augmentation transaction must retain one complete new receipt.");
    }
}
