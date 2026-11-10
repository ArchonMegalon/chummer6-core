using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Rulesets.Sr6;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    // All atomic writers, including restore, must preserve the committed prefix.
    // A format fence is not permission for a newer generic writer to erase history.
    private static void RequirePreservedSr6CareerAttributeHistory(
        PersistedWorkspaceRecord? previous, PersistedWorkspaceRecord replacement)
    {
        if (previous?.AuxiliaryState?.Sr6CareerAttributeReceipts is not { } receipts) return;
        if (previous.RecordSchemaVersion != Sr6CareerAttributeRecordSchemaVersion
            || replacement.RecordSchemaVersion != Sr6CareerAttributeRecordSchemaVersion
            || replacement.AuxiliaryState?.Sr6CareerAttributeReceipts is not { } next
            || next.Count < receipts.Count
            || Sr6CareerAttributeIntegrity.Digest(receipts)
                != Sr6CareerAttributeIntegrity.Digest(next.Take(receipts.Count).ToArray()))
            throw new IOException("SR6 advancement history cannot be removed or rewritten.");
    }

    public string Sr6CareerAttributeRuntimeDigest => Sr6CareerAttributeIntegrity.Digest(new
    {
        Schema = Sr6CareerAttributeSchemas.QuoteV1,
        Rules = typeof(Sr6CareerAttributeAdvancementRules).Module.ModuleVersionId,
        Application = typeof(Sr6CareerAttributeIntegrity).Module.ModuleVersionId,
        Contracts = typeof(Sr6CareerAttributeQuote).Module.ModuleVersionId,
        Store = typeof(FileWorkspaceStore).Module.ModuleVersionId
    });

    public Sr6CareerAttributeResult CommitSr6CareerAttribute(OwnerScope owner, Sr6CareerAttributeCommand command)
    {
        if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            || !Sr6CareerAttributeIntegrity.TryFreezeCommand(command, out command) || command.Quote.OwnerId != owner.NormalizedValue)
            return new(Sr6CareerAttributeOutcome.Conflict, Error: "sr6_attribute_command_invalid");
        var id = command.Quote.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null) return new(Sr6CareerAttributeOutcome.Missing);
        bool committed = false;
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return new(Sr6CareerAttributeOutcome.Unavailable, Error: "sr6_attribute_workspace_unavailable");
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return new(Sr6CareerAttributeOutcome.Unavailable, Error: "sr6_attribute_workspace_unavailable");
            var result = Sr6CareerAttributeAdvancementRules.Prepare(saved, owner, command,
                Sr6CareerAttributeRuntimeDigest, out var replacement);
            if (result.Outcome != Sr6CareerAttributeOutcome.Applied || replacement is null) return result;
            var receipt = result.Receipt!;
            if (!IsValidAuxiliaryState(id, receipt.CommittedRevision, replacement.AuxiliaryState))
                return new(Sr6CareerAttributeOutcome.Corrupt, Error: "sr6_attribute_auxiliary_invalid");
            var record = BuildPersistedRecord(replacement, receipt.CommittedRevision, receipt.CommittedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    var rechecked = Sr6CareerAttributeAdvancementRules.Prepare(saved, owner, command,
                        Sr6CareerAttributeRuntimeDigest, out var current);
                    if (rechecked.Outcome != Sr6CareerAttributeOutcome.Applied || current is null
                        || current.Content != replacement.Content || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                        throw new IOException("sr6_attribute_authority_changed_before_replace");
                });
            committed = true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The replace may already be durable. Reconcile this identity; never issue a new debit.
        }
        var observed = owner.IsLocalSingleUser ? Get(id) : Get(owner, id);
        if (observed.Success && observed.Value is { } currentSaved)
        {
            var recovered = Sr6CareerAttributeIntegrity.Lookup(currentSaved, command);
            if (recovered.Outcome != Sr6CareerAttributeOutcome.NotFound)
                return committed && recovered.Outcome == Sr6CareerAttributeOutcome.Replayed
                    ? recovered with { Outcome = Sr6CareerAttributeOutcome.Applied } : recovered;
        }
        return new(Sr6CareerAttributeOutcome.Unavailable, Error: "sr6_attribute_commit_unknown_retry_same_operation");
    }
}
