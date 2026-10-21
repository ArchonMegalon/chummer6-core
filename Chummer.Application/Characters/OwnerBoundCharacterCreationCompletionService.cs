using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationCompletionService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver, ICharacterFileQueries characterQueries)
    : IOwnerBoundCharacterCreationCompletionService
{
    public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCash(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken = default)
        => LoadStartingCashCore(expectedOwner, workspaceId, proposal, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCashWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => LoadStartingCashCore(expectedOwner, workspaceId, proposal, cancellationToken, campaign);

    private CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadStartingCashCore(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken,
        CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                CharacterCreationFoundationOutcomes.Conflict);
        using (lease)
        {
            // Keep only this synchronous operation's validated history available
            // from the first store read through preview and the final reread.
            // Each public call still reopens and validates retained source bytes.
            using var validation = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
            try
            {
                proposal = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(proposal))
                    ?? throw new InvalidDataException("Completion proposal could not be detached.");
                var admitted = sourceResolver.ForCreationSourceExecution(proposal.RuleSourceExecutionSchema);
                if (admitted is null) return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
                using ICharacterSourceDataResolverOperationScope? scope =
                    (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
                ICharacterSourceDataResolver resolver = scope ?? admitted;
                var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                    reuseReadObservation: true);
                var read = view.Get(workspaceId);
                if (!read.Success || read.Value is not { } original)
                    return CashRejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
                CharacterCreationRuleSourceExecutionContext? ruleSources = null;
                var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(
                    view, resolver, characterQueries, new(workspaceId)
                        { IncludeKarmaPurchasePolicy = proposal.Selections.SkillKarmaPurchases is not null },
                    proposal, cancellationToken, captured: value => ruleSources = new(value, characterQueries));
                if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs)
                    return new(loaded.Outcome, null, loaded.Blockers);
                if ((proposal.CampaignAuthority is null) != (campaign is null)
                    || campaign is not null && !campaign.AuthorizesOriginal(expectedOwner.Owner.Value, original,
                        proposal.CampaignAuthority, CharacterCreationCampaignPhase.Review, cancellationToken))
                    return CashRejected(CharacterCreationCampaignExecutionContext.Blocker);
                var evaluated = CharacterCreationCompletionReviewService.Review(inputs, proposal, cancellationToken, ruleSources, campaign);
                if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Value is not { Lifestyles: { } lifestyles } review
                    || review.Blockers.Count != 0)
                    return new(evaluated.Outcome, null, evaluated.Blockers);

                // This is the proposed, evaluated purchase—not the original
                // saved draft. No synthetic draft or dice choice is needed.
                Guid? sourceId = null;
                if (lifestyles.StartingLifestyleId is Guid selected)
                {
                    var rows = lifestyles.Lifestyles.Where(row => row.Configuration.LifestyleId == selected).Take(2).ToArray();
                    if (rows.Length != 1) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                    var options = lifestyles.Authority.LifestyleOptions.Where(row =>
                        row.OptionId == rows[0].Configuration.BaseLifestyleOptionId).Take(2).ToArray();
                    if (options.Length != 1) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                    sourceId = options[0].SourceId;
                }
                else if (lifestyles.Lifestyles.Count != 0)
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                var context = resolver.TryCreateContext(original.Document.Content);
                if (context is null) return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                if (!Resolve(out var source) || source is null
                    || !CharacterCreationFinalizationStartingCashRules.SourcesMatch(source, lifestyles.Authority))
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                cancellationToken.ThrowIfCancellationRequested();
                // The cached view admitted one coherent observation. Recheck
                // the real store before publishing source terms after the work.
                var current = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId).Get(workspaceId);
                if (!current.Success || current.Value is not { } after
                    || after.Id != original.Id || after.ContentRevision != original.ContentRevision
                    || after.SavedRevision != original.SavedRevision
                    || after.Document.Content != original.Document.Content
                    || after.Document.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest)
                    return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                        CharacterCreationFoundationOutcomes.Conflict);
                if (!Resolve(out var observed) || observed?.AuthorityDigest != source.AuthorityDigest)
                    return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable);
                if (inputs.EffectivePolicy is not null
                    && OwnerBoundCharacterCreationCompletionReader.LoadForProposal(
                        new WorkspaceContinuationReadView(after), resolver, characterQueries,
                        new(workspaceId) { IncludeKarmaPurchasePolicy = proposal.Selections.SkillKarmaPurchases is not null },
                        proposal, cancellationToken).Outcome != CharacterCreationFoundationOutcomes.Success)
                    return CashRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                        CharacterCreationFoundationOutcomes.Conflict);
                cancellationToken.ThrowIfCancellationRequested();
                if (campaign is not null && !campaign.AuthorizesOriginal(expectedOwner.Owner.Value, original,
                        proposal.CampaignAuthority, CharacterCreationCampaignPhase.Review, cancellationToken))
                    return CashRejected(CharacterCreationCampaignExecutionContext.Blocker);
                return new(CharacterCreationFoundationOutcomes.Success, source, []);

                bool Resolve(out CharacterCreationStartingNuyenSource? value)
                    => sourceId is Guid id ? context.TryResolveCreationPriorityStartingNuyen(id, out value)
                        : context.TryResolveCreationDefaultStartingNuyen(out value);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
                or IOException or UnauthorizedAccessException or System.Xml.XmlException or OverflowException)
            { return CashRejected(CharacterCreationFinalizationBlockers.StartingCashUnavailable); }
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> ReviewDraft(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken = default)
        => ReviewDraftCore(expectedOwner, workspaceId, proposal, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> ReviewDraftWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationCampaignExecutionContext campaign,
        CancellationToken cancellationToken = default)
        => campaign.Phase == CharacterCreationCampaignPhase.Review
            ? ReviewDraftCore(expectedOwner, workspaceId, proposal, cancellationToken, campaign)
            : DraftRejected(CharacterCreationCampaignExecutionContext.Blocker);

    private CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> ReviewDraftCore(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CancellationToken cancellationToken,
        CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return DraftRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                CharacterCreationFoundationOutcomes.Conflict);
        using (lease)
        {
            // Each public review owns a fresh memo, even when invoked from a
            // callback inside another operation on this thread.
            using var validation = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
            return CharacterCreationCompletionDraftReviewer.Review(store, lease, expectedOwner, workspaceId,
                sourceResolver, characterQueries, proposal, cancellationToken, campaign);
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ApplyDraft(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftApplyRequest request,
        CancellationToken cancellationToken = default)
        => ApplyDraftCore(expectedOwner, request, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ApplyDraftWithCampaign(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftApplyRequest request,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => ApplyDraftCore(expectedOwner, request, cancellationToken, campaign);

    private CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> ApplyDraftCore(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftApplyRequest request,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        // Capture every caller-owned collection before owner admission or any
        // resolver callback. All later validation/dispatch uses these exact values.
        try { request = CharacterCreationCompletionDraftTransaction.Freeze(request); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or IOException)
        { return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
        if (!CharacterCreationCompletionDraftTransaction.IsConfirmed(request) || request.Review.OwnerStampDigest != CharacterCreationFinalizationDigest.Compute(expectedOwner)
            || request.Review.OwnerScopeDigest != CharacterCreationFinalizationDigest.Compute(expectedOwner.Owner))
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        using var validation = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        {
            var admitted = sourceResolver.ForCreationSourceExecution(request.Review.Review.Request.RuleSourceExecutionSchema);
            // The atomic store reconciles exact retained receipts before live admission.
            // A refused schema still reaches that lookup; TryBuild refuses fresh execution.
            using ICharacterSourceDataResolverOperationScope? scope =
                (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver operationResolver = scope ?? admitted ?? sourceResolver;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.Review.Binding.WorkspaceId);
            if (campaign is not null)
                return ((ICharacterCreationCompletionDraftAtomicCommitCapability)view).CommitCompletionDraft(
                    expectedOwner.Owner, request, operationResolver, characterQueries, campaign, cancellationToken);
            return ((ICharacterCreationCompletionDraftAtomicCommitCapability)view).CommitCompletionDraft(
                expectedOwner.Owner, request, operationResolver, characterQueries, cancellationToken);
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> LookupDraft(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionDraftReceiptLookup request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        using var validation = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
        if (!CharacterCreationCompletionDraftTransaction.IsLookup(request)
            || (expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId);
            return view.LookupCompletionDraftReceipt(expectedOwner.Owner, request, cancellationToken);
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> DraftRejected(
        string blocker, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, [blocker]);

    public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Review(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken = default)
        => ReviewCore(expectedOwner, workspaceId, proposal, startingCash, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> ReviewWithCampaign(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => campaign.Phase == CharacterCreationCampaignPhase.Review
            ? ReviewCore(expectedOwner, workspaceId, proposal, startingCash, cancellationToken, campaign)
            : Reject([CharacterCreationCampaignExecutionContext.Blocker]);

    private CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> ReviewCore(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId workspaceId,
        CharacterCreationCompletionReviewRequest proposal, CharacterCreationStartingCashChoice startingCash,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        ArgumentNullException.ThrowIfNull(startingCash);
        cancellationToken.ThrowIfCancellationRequested();
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return Reject([CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        using (lease)
        {
            using var validation = CharacterCreationCompletionDraftTransaction.BeginValidationOperation();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                reuseReadObservation: true);
            var read = view.Get(workspaceId);
            if (!read.Success || read.Value is not { } workspace)
                return Reject([CharacterCreationFinalizationBlockers.WorkspaceUnavailable]);
            try
            {
                proposal = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(proposal))
                    ?? throw new InvalidDataException("Completion proposal could not be detached.");
                if (!CharacterCreationCompletionTransaction.TryAdmit(view, expectedOwner.Owner, workspace,
                        sourceResolver, characterQueries, proposal, startingCash, cancellationToken,
                        out _, out var projection, out var blockers, campaign: campaign)) return Reject(blockers);
                cancellationToken.ThrowIfCancellationRequested();
                return new(CharacterCreationFoundationOutcomes.Success, new(proposal, projection!), []);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
            { return Reject([CharacterCreationFinalizationBlockers.DraftAuthorityInvalid]); }
        }
    }

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> Apply(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken = default)
        => ApplyCore(expectedOwner, request, cancellationToken, null);

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ApplyWithCampaign(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CharacterCreationCampaignExecutionContext campaign, CancellationToken cancellationToken = default)
        => ApplyCore(expectedOwner, request, cancellationToken, campaign);

    private CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ApplyCore(
        OwnerContextStamp expectedOwner, CharacterCreationCompletionApplyRequest request,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationCompletionTransaction.IsConfirmed(request))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return CharacterCreationCompletionTransaction.Blocked(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        using (lease)
        {
            // Keep source drift poisoned across initial admission and the check after temp-file flush.
            var admitted = sourceResolver.ForCreationSourceExecution(request.Proposal.RuleSourceExecutionSchema);
            // Receipt recovery uses retained authority even if this live resolver refuses the schema.
            // The transaction must still admit the requested schema before any fresh source read.
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver operationResolver = sourceScope ?? admitted ?? sourceResolver;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.Binding.WorkspaceId);
            if (campaign is not null)
                return ((ICharacterCreationCompletionAtomicCommitCapability)view).CommitCompletion(request,
                    operationResolver, characterQueries, campaign, cancellationToken);
            return ((ICharacterCreationCompletionAtomicCommitCapability)view).CommitCompletion(request,
                operationResolver, characterQueries, cancellationToken);
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionApplyReview> Reject(IReadOnlyList<string> blockers)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, blockers.Count == 0
            ? [CharacterCreationFinalizationBlockers.DraftAuthorityInvalid] : blockers.ToArray());

    private static CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> CashRejected(
        string blocker, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, [blocker]);
}
