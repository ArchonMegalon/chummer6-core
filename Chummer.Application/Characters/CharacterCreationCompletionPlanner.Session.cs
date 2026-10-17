using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    /// <summary>
    /// Admit one detached historical input graph for a bounded proposal search.
    /// Reusing it avoids repeating original-state admission, not candidate rules
    /// or final review. It grants no current owner, workspace or write authority.
    /// Discard the session when the observed inputs change.
    /// </summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSession(
        CharacterCreationCompletionReviewInputs inputs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = Detach(inputs);
        if (!TryOriginalSelections(inputs, out var original))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
        var admission = CharacterCreationCompletionReviewService.Review(inputs,
            new(inputs.SnapshotDigest, original), cancellationToken);
        if (admission.Value is null) return new(admission.Outcome, null, admission.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        // Original readiness blockers are still evaluated by every candidate.
        // Success here means only that the historical inputs were admitted.
        return new(CharacterCreationFoundationOutcomes.Success, new PlanningSession(inputs, original), []);
    }

    public sealed class PlanningSession
    {
        private readonly CharacterCreationCompletionReviewInputs _inputs;
        private readonly CharacterCreationCompletionSelections _original;

        internal PlanningSession(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections original)
        {
            _inputs = inputs;
            _original = original;
        }

        public CharacterCreationFoundationResult<CharacterCreationCompletionPlan> Plan(
            CharacterCreationCompletionPlanRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidRequest(request)) return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationCompletionPlanBlockers.RequestInvalid);
            request = Detach(request);
            if (!Digest(_inputs.SnapshotDigest, request.InputsSnapshotDigest))
                return Reject(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            return PlanAdmitted(_inputs, request, _original, cancellationToken);
        }
    }
}
