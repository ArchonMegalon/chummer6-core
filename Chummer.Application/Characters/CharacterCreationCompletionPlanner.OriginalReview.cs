using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    /// <summary>Review the actual original selections once. The returned local
    /// object records only historical original-state admission; it grants no
    /// current source, campaign, owner or write authority.</summary>
    public static CharacterCreationFoundationResult<ReviewedOriginalState> ReviewOriginalForPlanning(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default, CharacterCreationRuleSourceExecutionContext? ruleSources = null,
        CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
            || request.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || !CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(inputs.Skills?.Authority,
                out var authority, cancellationToken))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
        inputs = Detach(inputs with { Skills = inputs.Skills! with { Authority = authority } });
        request = Detach(request);
        if (!Digest(inputs.SnapshotDigest, request.InputsSnapshotDigest)
            || !Digest(inputs.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)))
            return new(CharacterCreationFoundationOutcomes.Conflict, null,
                [CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        if (!TryOriginalSelections(inputs, out var original) || !Same(original, request.Selections))
            return new(CharacterCreationFoundationOutcomes.Invalid, null,
                [CharacterCreationCompletionPlanBlockers.RequestInvalid]);

        // Do not accept a caller-supplied review or assessment as admission.
        // This is the same real original review used by CreateSessionCore.
        var review = CharacterCreationCompletionReviewService.Review(inputs, request, cancellationToken, ruleSources, campaign);
        if (review.Value is null) return new(review.Outcome, null, review.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        return new(review.Outcome, new ReviewedOriginalState(inputs.SnapshotDigest, original, request, review),
            review.Blockers.ToArray());
    }

    public sealed class ReviewedOriginalState
    {
        private readonly string inputsDigest;
        private readonly CharacterCreationCompletionSelections original;
        private readonly CharacterCreationCompletionReviewRequest originalRequest;
        private readonly CharacterCreationFoundationResult<CharacterCreationCompletionReview> originalReview;

        internal ReviewedOriginalState(string inputsDigest, CharacterCreationCompletionSelections original,
            CharacterCreationCompletionReviewRequest request,
            CharacterCreationFoundationResult<CharacterCreationCompletionReview> review)
        {
            this.inputsDigest = inputsDigest;
            this.original = Detach(original);
            originalRequest = Detach(request);
            originalReview = Detach(review);
        }

        // Display diagnostics are detached from every retained admission field.
        public CharacterCreationFoundationResult<CharacterCreationCompletionReview> OriginalReview => Detach(originalReview);
        public override string ToString() => nameof(ReviewedOriginalState);

        public CharacterCreationFoundationResult<CharacterCreationCompletionPlan> PlanWithFreshExecution(
            CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionPlanRequest request,
            CharacterCreationRuleSourceExecutionContext? ruleSources, CancellationToken cancellationToken = default,
            CharacterCreationCampaignExecutionContext? campaign = null)
        {
            ArgumentNullException.ThrowIfNull(inputs);
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(inputs.Skills?.Authority,
                    out var authority, cancellationToken))
                return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
            inputs = Detach(inputs with { Skills = inputs.Skills! with { Authority = authority } });
            // Recompute the entire supplied graph. Its advertised snapshot alone
            // cannot reuse original-state admission after a nested mutation.
            if (!Digest(inputsDigest, inputs.SnapshotDigest)
                || !Digest(inputsDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)))
                return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationCompletionReviewSchemas.SnapshotConflict);

            // Fixed eligibility is CURRENT too, including the live Preparation
            // observation. Only the earlier original review is reused. No old
            // source context or campaign execution is stored in this object.
            var blockers = CompleteCandidateBlockers(inputs, originalRequest, cancellationToken, campaign, ruleSources);
            if (blockers.Count != 0)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, blockers);
            var current = new PlanningSession(inputs, original, originalRequest.PolicyRequirement,
                originalRequest.EffectivePolicyDigest, originalRequest.PolicyConsumptionSchema,
                originalRequest.CompleteAssessmentSchema, originalRequest, ruleSources, campaign);
            // Existing request/version/permission checks and full candidate Core
            // review remain in the normal PlanningSession path.
            return current.Plan(request, cancellationToken);
        }
    }
}
