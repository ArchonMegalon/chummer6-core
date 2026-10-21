using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore : ICharacterCreationCompletionDraftAtomicCommitCapability
{
    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> CommitCompletionDraft(
        OwnerScope owner, CharacterCreationCompletionDraftApplyRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CancellationToken cancellationToken = default)
        => CommitCompletionDraftCore(owner, request, resolver, queries, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> CommitCompletionDraft(
        OwnerScope owner, CharacterCreationCompletionDraftApplyRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CommitCompletionDraftCore(owner, request, resolver, queries, cancellationToken, campaign);

    private CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> CommitCompletionDraftCore(
        OwnerScope owner, CharacterCreationCompletionDraftApplyRequest request,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries, CancellationToken cancellationToken,
        CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(queries);
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationCompletionDraftTransaction.IsConfirmed(request))
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        try { request = CharacterCreationCompletionDraftTransaction.Freeze(request); }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
        var id = request.Review.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(request);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            cancellationToken.ThrowIfCancellationRequested();
            // Internal fault seam under the real operation lease, before the persisted read.
            _faultInjector.OnStage(FileWorkspaceStoreFaultStage.BeforeCompletionDraftRead, path, path);
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            var proposal = request.Review.Review.Request;
            bool requiresRetention = proposal.RuleSourceExecutionSchema is not null || proposal.CampaignAuthority is not null;
            using var recovery = OpenSavedRuleSources(owner, id, path, saved, requirePublication: requiresRetention);
            using var historyValidation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            CharacterCreationRuleSourceExecutionContext? captured = null;
            if (CharacterCreationCompletionDraftTransaction.Lookup(saved, lookup, recovery) is { } replay) return replay;
            // Committed recovery is private and exact; only a new write needs live Apply.
            if ((proposal.CampaignAuthority is null) != (campaign is null)
                || campaign is not null && !campaign.AuthorizesApplyBinding(owner.Value, request.Review.Binding,
                    proposal.CampaignAuthority, cancellationToken))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);
            if (!CharacterCreationCompletionDraftTransaction.TryBuild(this, owner, saved, resolver, queries,
                    request, campaign, cancellationToken, out var replacement, out var receipt, value => captured = value, recovery))
                return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            if (proposal.RuleSourceExecutionSchema is not null)
            {
                if (captured is null || recovery is null) throw RetainedRuleSourceRejected();
                recovery.RetainAdmittedCapture(captured, proposal.RuleSourceCaptureDigest!);
            }
            if (campaign is not null)
            {
                if (recovery is null) throw RetainedRuleSourceRejected();
                recovery.RetainCommittedDraftCampaign(campaign.CaptureCommitted(receipt!, cancellationToken));
            }
            var record = BuildPersistedRecord(replacement!, receipt!.ContentRevision, receipt.SavedRevision,
                saved.LocalHistory!, delegatedLedger, saved.DelegatedGmHistorySegmentStarts);
            if (recovery is not null)
                record = record with { RetainedRuleSources = BuildRetainedManifest(owner, id,
                    saved.LocalHistory!, replacement!, recovery) };
            cancellationToken.ThrowIfCancellationRequested();
            WriteRecordAtomically(path, record, WorkspaceWriteDisposition.ReplaceExisting,
                beforeTargetReplace: () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!CharacterCreationCompletionDraftTransaction.TryBuild(this, owner, saved, resolver, queries,
                            request, campaign, cancellationToken, out var current, out var checkedReceipt, recovery: recovery)
                        || checkedReceipt!.ReceiptDigest != receipt.ReceiptDigest
                        || current!.Content != replacement!.Content
                        || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                        throw new IOException("Composite Creation authority changed before commit.");
                    cancellationToken.ThrowIfCancellationRequested();
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException
            || campaign is not null && error is OperationCanceledException)
        {
            // Only exact durable lookup can settle an unknown acknowledgement.
        }
        try
        {
            using var operation = AcquireWorkspaceOperation(path);
            var observed = ReadWorkspaceUnderLease(owner, id, path);
            if (observed.Success && observed.Value is { } workspace)
            {
                using var recovery = OpenSavedRuleSources(owner, id, path, workspace);
                if (CharacterCreationCompletionDraftTransaction.Lookup(workspace, lookup, recovery) is { } result) return result;
            }
        }
        catch (Exception error) when (IsRetainedRuleSourceFailure(error) || error is System.Text.Json.JsonException) { }
        return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
