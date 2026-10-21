using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationCompletionDraftReceiptLookupCapability
{
    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> LookupCompletionDraftReceipt(
        OwnerScope owner, CharacterCreationCompletionDraftReceiptLookup request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if ((!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner))
            || !CharacterCreationCompletionDraftTransaction.IsLookup(request))
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

        // The lookup and receipt contain only immutable scalar values. No caller
        // document, resolver, recovery handle or mutable collection enters this read.
        var id = request.WorkspaceId;
        try
        {
            string? path = TryGetPath(owner, id);
            if (path is null || !TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using var operation = AcquireWorkspaceOperation(path, recoverStaleTempFiles: false);
            cancellationToken.ThrowIfCancellationRequested();

            // Read one fresh record without the ordinary Get path's migration or
            // validation cache. Lookup never rewrites a checkpoint or invents local
            // provenance for an older record that did not persist it.
            var record = ReadPrivateRecord(path);
            if (!TryMaterializeRecord(record, out var document, out long contentRevision, out long savedRevision, out _)
                || record.RecordSchemaVersion is null or < 3
                || record.LocalHistory is not { } history || !history.IsValid(contentRevision)
                || !TryMaterializeDelegatedEditLedger(owner, id, contentRevision, record.DelegatedGmCharacterEdits,
                    record.RecordSchemaVersion >= 4 ? record.DelegatedGmHistorySegmentStarts : [], out var ledger))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            if (history.LocalAdoption is { } adoption
                && (adoption.WorkspaceId != id || adoption.OwnerId != owner.NormalizedValue))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

            IReadOnlyList<int> segmentStarts;
            if (record.RecordSchemaVersion >= 4)
                segmentStarts = record.DelegatedGmHistorySegmentStarts!;
            else
            {
                if (record.DelegatedGmHistorySegmentStarts is not null)
                    return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
                int firstLocal = FindFirstLocalDelegatedEdit(ledger, history);
                segmentStarts = firstLocal > 0 && firstLocal < ledger.Count ? [firstLocal] : [];
            }
            if (!HasValidLocalDelegatedEditSegments(ledger, segmentStarts, history))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

            // Opening the private manifest rechecks source bytes, owner, workspace
            // and incarnation before semantic replay, even on repeated lookups.
            using var recovery = OpenRecordRuleSources(owner, id, record);
            using var validation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            if (!IsValidAuxiliaryState(id, contentRevision, document.AuxiliaryState, recovery)
                || !Sr6CreationFinalizationIntegrity.IsValidCurrentDocument(document, contentRevision)
                || !CharacterCreationCompletionTransaction.IsValidCurrentDocument(document, contentRevision, recovery)
                || !CharacterCreationCompletionDraftTransaction.IsValidCurrentDocument(document, contentRevision))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);

            var workspace = new WorkspaceStoredDocument(id, document, contentRevision, savedRevision,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero))
            {
                LocalHistory = history,
                DelegatedGmHistorySegmentStarts = segmentStarts
            };
            var result = CharacterCreationCompletionDraftTransaction.Lookup(workspace, request, recovery)
                ?? CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (Exception error) when (IsRetainedRuleSourceFailure(error) || error is System.Text.Json.JsonException)
        {
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        }
    }
}
