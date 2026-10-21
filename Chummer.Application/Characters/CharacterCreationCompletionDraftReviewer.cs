using System.Text.Json;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// One canonical full Draft reviewer. Callers supply the actual live owner lease.
// No staged/relaxed assessment and no substitute owner accessor.
internal static class CharacterCreationCompletionDraftReviewer
{
    internal static CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> Review(
        IWorkspaceStore store, IOwnerContextLease lease, OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId, ICharacterSourceDataResolver sourceResolver,
        ICharacterFileQueries characterQueries, CharacterCreationCompletionReviewRequest proposal,
        CancellationToken cancellationToken, CharacterCreationCampaignExecutionContext? campaign)
    {
        if (lease.Stamp != expectedOwner)
            return DraftRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        // Public Draft review starts its own operation at owner admission. A
        // nested attribute rebase already holds that same synchronous owner lease
        // and has replayed this exact history; join only its call-stack memo.
        // Store/source/campaign re-observation below still runs in full.
        using var validation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        try
        {
            proposal = JsonSerializer.Deserialize<CharacterCreationCompletionReviewRequest>(JsonSerializer.Serialize(proposal))
                ?? throw new InvalidDataException("Completion proposal could not be detached.");
            if (proposal.Selections is null)
                return DraftRejected(CharacterCreationCompletionReviewSchemas.RequestInvalid);
            var admitted = sourceResolver.ForCreationSourceExecution(proposal.RuleSourceExecutionSchema);
            if (admitted is null)
                return DraftRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            using ICharacterSourceDataResolverOperationScope? scope =
                (admitted as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            ICharacterSourceDataResolver resolver = scope ?? admitted;
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                reuseReadObservation: true);
            if (!view.SupportsWorkspaceAuxiliaryStateAtomicCommit)
                return DraftRejected(CharacterCreationAttributesBlockers.PersistenceAuthorityRequired);
            var read = view.Get(workspaceId);
            if (!read.Success || read.Value is not { } original)
                return DraftRejected(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
            if ((proposal.CampaignAuthority is null) != (campaign is null)
                || campaign is not null && (!campaign.AuthorizesOriginal(expectedOwner.Owner.Value, original,
                        proposal.CampaignAuthority, CharacterCreationCampaignPhase.Review, cancellationToken)
                    || proposal.RuleSourceExecutionSchema is null
                    || proposal.CampaignAuthority!.RetainedIdentity.SourceDigest != proposal.RuleSourceCaptureDigest))
                return DraftRejected(CharacterCreationCampaignExecutionContext.Blocker);
            CharacterCreationRuleSourceExecutionContext? ruleSources = null;
            var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(view, resolver, characterQueries,
                new(workspaceId) { IncludeKarmaPurchasePolicy = proposal.Selections.SkillKarmaPurchases is not null },
                proposal, cancellationToken, captured: value => ruleSources = new(value, characterQueries));
            if (loaded.Outcome != CharacterCreationFoundationOutcomes.Success || loaded.Value is not { } inputs)
                return new(loaded.Outcome, null, loaded.Blockers);
            var context = resolver.TryCreateContext(original.Document.Content);
            if (context is null || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(
                    context, original.Document.Content, out var bootstrap))
                return DraftRejected(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid);
            // Use the actual full review and selected witness. No alternate
            // pricing, invented intermediate draft or finalization projection.
            var evaluated = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(
                inputs, proposal, cancellationToken, ruleSources, campaign);
            if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success || evaluated.Value is not { } reviewed)
                return new(evaluated.Outcome, null, evaluated.Blockers);
            var captured = reviewed.Capture(original, bootstrap, cancellationToken);
            if (captured.Outcome != CharacterCreationFoundationOutcomes.Success || captured.Value is not { } witness)
                return new(captured.Outcome, null, captured.Blockers);
    
            // The initial read is deliberately coherent and cached. Re-observe
            // the real store and source after selected replay before publishing.
            var currentView = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId,
                reuseReadObservation: true);
            var current = currentView.Get(workspaceId);
            if (!current.Success || current.Value is not { } observed
                || observed.Id != original.Id || observed.ContentRevision != original.ContentRevision
                || observed.SavedRevision != original.SavedRevision
                || observed.Document.Content != original.Document.Content
                || observed.Document.AuxiliaryStateDigest != original.Document.AuxiliaryStateDigest)
                return DraftRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                    CharacterCreationFoundationOutcomes.Conflict);
            var reloaded = OwnerBoundCharacterCreationCompletionReader.LoadForProposal(currentView, resolver, characterQueries,
                new(workspaceId) { IncludeKarmaPurchasePolicy = proposal.Selections.SkillKarmaPurchases is not null },
                proposal, cancellationToken);
            var currentContext = resolver.TryCreateContext(observed.Document.Content);
            if (reloaded.Outcome != CharacterCreationFoundationOutcomes.Success
                || reloaded.Value?.SnapshotDigest != inputs.SnapshotDigest
                || currentContext is null || !CharacterCreationCompletionBootstrapSnapshot.TryCapture(
                    currentContext, observed.Document.Content, out var currentBootstrap)
                || currentBootstrap.SnapshotDigest != bootstrap.SnapshotDigest)
                return DraftRejected(CharacterCreationCompletionReviewSchemas.SnapshotConflict,
                    CharacterCreationFoundationOutcomes.Conflict);
            cancellationToken.ThrowIfCancellationRequested();
            if (campaign is not null && !campaign.AuthorizesOriginal(expectedOwner.Owner.Value, observed,
                    proposal.CampaignAuthority, CharacterCreationCampaignPhase.Review, cancellationToken))
                return DraftRejected(CharacterCreationCampaignExecutionContext.Blocker);
            var review = reviewed.ReviewSnapshot;
            string authorityDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCompletionDraftReview.SchemaV1,
                inputs.SnapshotDigest, BootstrapDigest = bootstrap.SnapshotDigest, witness.WitnessDigest
            });
            var binding = new CharacterCreationFinalizationBinding(workspaceId, original.ContentRevision,
                original.SavedRevision, CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content),
                original.Document.AuxiliaryStateDigest, inputs.Skills.PrerequisiteDraft.BuildMethod, authorityDigest);
            var result = new CharacterCreationCompletionDraftReview(CharacterCreationCompletionDraftReview.SchemaV1,
                binding, review, witness.WitnessDigest, string.Empty) { OwnerScopeDigest = CharacterCreationFinalizationDigest.Compute(expectedOwner.Owner),
                OwnerStampDigest = CharacterCreationFinalizationDigest.Compute(expectedOwner) };
            result = result with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(result) };
            return new(CharacterCreationFoundationOutcomes.Success, result, []);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException
            or IOException or UnauthorizedAccessException or System.Xml.XmlException or OverflowException or FormatException)
        { return DraftRejected(CharacterCreationFinalizationBlockers.DraftAuthorityInvalid); }
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionDraftReview> DraftRejected(
        string blocker, string outcome = CharacterCreationFoundationOutcomes.Blocked)
        => new(outcome, null, [blocker]);
}
