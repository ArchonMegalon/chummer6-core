using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CancellationToken cancellationToken = default)
        => CommitCompletion(OwnerScope.LocalSingleUser, request, sourceResolver, characterQueries, cancellationToken);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CancellationToken cancellationToken = default)
        => CommitCompletionCore(owner, request, sourceResolver, characterQueries, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        CharacterCreationCompletionApplyRequest request, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => CommitCompletionCore(OwnerScope.LocalSingleUser, request, sourceResolver, characterQueries, cancellationToken, campaign);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletion(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => CommitCompletionCore(owner, request, sourceResolver, characterQueries, cancellationToken, campaign);

    private CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> CommitCompletionCore(
        OwnerScope owner, CharacterCreationCompletionApplyRequest request,
        ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(sourceResolver);
        ArgumentNullException.ThrowIfNull(characterQueries);
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsLocalSingleUser && IsInvalidScopedOwner(owner)
            || !CharacterCreationCompletionTransaction.IsConfirmed(request))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        try { request = CharacterCreationCompletionTransaction.Freeze(request); }
        catch (Exception error) when (error is System.Text.Json.JsonException or ArgumentException or InvalidOperationException)
        { return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
        var id = request.Binding.WorkspaceId;
        string? path = TryGetPath(owner, id);
        if (path is null)
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        try
        {
            if (!TrySecureExistingWorkspaceDirectory(owner, allowLegacyMigration: false))
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            using WorkspaceOperationLease operation = AcquireWorkspaceOperation(path);
            cancellationToken.ThrowIfCancellationRequested();
            var read = ReadWorkspaceUnderLease(owner, id, path, out var delegatedLedger);
            if (!read.Success || read.Value is not { } saved)
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            bool requiresRetention = request.Proposal.RuleSourceExecutionSchema is not null || request.Proposal.CampaignAuthority is not null;
            using var recovery = OpenSavedRuleSources(owner, id, path, saved, requirePublication: requiresRetention);
            using var historyValidation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
            CharacterCreationRuleSourceExecutionContext? captured = null;
            ICharacterCreationRuleSourceRecovery? authorityRecovery = campaign is null ? recovery
                : new CharacterCreationCampaignRecovery(campaign, recovery);
            var prior = CharacterCreationCompletionTransaction.Lookup(saved, request, recovery);
            if (prior is not null) return prior;
            if ((request.Proposal.CampaignAuthority is null) != (campaign is null)
                || campaign is not null && !campaign.AuthorizesApplyBinding(owner.Value, request.Binding,
                    request.Proposal.CampaignAuthority, cancellationToken))
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationCampaignExecutionContext.Blocker);
            if (!CharacterCreationCompletionTransaction.TryBuild(this, owner, saved, sourceResolver, characterQueries,
                    request, cancellationToken, out var replacement, out var receipt, value => captured = value, authorityRecovery, campaign))
                return CharacterCreationCompletionTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            if (request.Proposal.RuleSourceExecutionSchema is not null)
            {
                if (captured is null || recovery is null) throw RetainedRuleSourceRejected();
                recovery.RetainAdmittedCapture(captured, request.Proposal.RuleSourceCaptureDigest!);
            }
            if (campaign is not null)
            {
                if (recovery is null) throw RetainedRuleSourceRejected();
                recovery.RetainCommittedCampaign(campaign.CaptureCommitted(receipt!, cancellationToken));
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
                    // Resolve live sources again after the temporary record is
                    // flushed, under the same actual owner/workspace lease.
                    if (!CharacterCreationCompletionTransaction.TryBuild(this, owner, saved, sourceResolver, characterQueries,
                            request, cancellationToken, out var current, out var finalReceipt, recovery: authorityRecovery, campaign: campaign)
                        || finalReceipt!.ReceiptDigest != receipt.ReceiptDigest
                        || current!.Content != replacement!.Content
                        || current.AuxiliaryStateDigest != replacement.AuxiliaryStateDigest)
                        throw new IOException("Completion source changed before commit.");
                    cancellationToken.ThrowIfCancellationRequested();
                });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or OperationCanceledException)
        {
            // A failed acknowledgement is not permission to replay a mutation.
            // Resolve its outcome only from the already-written durable receipt.
        }
        // Do not check cancellation after rename: the committed result must be
        // reconciled even if the caller departed during the atomic write.
        try
        {
            using var operation = AcquireWorkspaceOperation(path);
            var observed = ReadWorkspaceUnderLease(owner, id, path, out _);
            if (observed.Success && observed.Value is { } workspace)
            {
                using var recovery = OpenSavedRuleSources(owner, id, path, workspace);
                if (CharacterCreationCompletionTransaction.Lookup(workspace, request, recovery) is { } result) return result;
            }
        }
        catch (Exception error) when (IsRetainedRuleSourceFailure(error) || error is System.Text.Json.JsonException) { }
        return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }
}
