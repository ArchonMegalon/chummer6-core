using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationRoleCompletionPlanner
{
    public const string PhysicalSourceUnavailable3847 = "creation-physical-loadout-source-or-mechanics-unavailable";
    public const string PhysicalSkillUnavailable3847 = "creation-physical-loadout-learned-skill-or-target-unavailable";
    public const string PhysicalLocked3847 = "creation-physical-loadout-existing-equipment-locked";
    public const string PhysicalAmmoUnavailable3847 = "creation-physical-loadout-owned-ammunition-exhausted-or-ambiguous";
    public const string PhysicalLoadUnavailable3847 = "creation-physical-loadout-native-round-allocation-unavailable";
    public const string PhysicalReadinessUnavailable3847 = "creation-physical-loadout-requested-readiness-unavailable";
    public const string PhysicalMultipleRoutines3847 = "creation-physical-loadout-simultaneous-construction-unsupported";

    private static CharacterCreationFoundationResult<CharacterCreationRoleCompletionCandidate> PlanPhysicalLoadout3847(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationRoleCompletionRequest request,
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationStartingNuyenSource startingCashSource, CharacterCreationStartingCashChoice? cashChoice,
        CharacterCreationRuleSourceExecutionContext ruleSources, CharacterCreationCompletionBaseline basis,
        CharacterCreationCompleteRuleSourceExecution source, CancellationToken cancellationToken,
        CharacterCreationCampaignExecutionContext? campaign)
    {
        var routines = request.Brief.Routines.Where(row => row.Kind == CharacterCreationRoleRoutineKind.PhysicalAttack
            && row.Action?.PhysicalAttack is null).ToArray();
        // Sequence/multiple-kit allocation needs its own shared round demand model.
        // This ordinary single-routine increment never pretends that one cartridge
        // satisfies several simultaneous actions.
        if (routines.Length != 1) return Refuse(PhysicalMultipleRoutines3847);
        int evaluated = basis.CandidateEvaluations;
        int remaining = request.MaximumCandidateEvaluations - evaluated;
        if (remaining < 1) return Refuse(CharacterCreationCompletionBaselineBlockers.SearchLimitReached);
        var candidates = source.PhysicalCandidates3847(routines[0], basis, remaining, out string unavailable);
        if (candidates.Count == 0) return Refuse(unavailable);
        IReadOnlyList<string> blockers = [PhysicalReadinessUnavailable3847];
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // One bounded full evaluation per equipment alternative. All domains
            // retain the successful baseline's choices and the same single wallet.
            var planned = CharacterCreationCompletionPlanner.PlanCompleteBaseline(inputs,
                new(candidate.Plan, 1, false), original, bootstrap, startingCashSource, cashChoice,
                ruleSources, cancellationToken, campaign);
            evaluated++;
            if (planned.Outcome is CharacterCreationFoundationOutcomes.Invalid or CharacterCreationFoundationOutcomes.Conflict)
                return new(planned.Outcome, null, planned.Blockers);
            blockers = planned.Blockers;
            if (planned.Outcome != CharacterCreationFoundationOutcomes.Success || planned.Value is not { } complete) continue;
            var current = ruleSources.Resolve(inputs, complete.Plan.Review.Request, cancellationToken);
            if (current is null) return new(CharacterCreationFoundationOutcomes.Conflict, null, [SourceUnavailable]);
            var bound = current.BindPhysical3847(routines[0], candidate.Load, complete.Finalization.CharacterXml);
            if (bound is null) { blockers = [PhysicalSourceUnavailable3847]; continue; }
            var constructed = request with { Brief = request.Brief with
            {
                Plan = complete.Plan.Request,
                Routines = request.Brief.Routines.Select(row => row.Id == bound.Id ? bound : row).ToArray()
            } };
            complete = complete with { CandidateEvaluations = evaluated };
            var assessment = current.AssessRoleObjectives(constructed, complete, cancellationToken);
            var physical = assessment.Routines.Single(row => row.RoutineId == bound.Id).Findings
                .Where(row => row.Code.StartsWith("physical-attack-", StringComparison.Ordinal)).ToArray();
            if (physical.Length != 5 || physical.Any(row => row.State != CharacterCreationRoleFindingState.AssessedPass))
            { blockers = [PhysicalReadinessUnavailable3847]; continue; }
            return new(CharacterCreationFoundationOutcomes.Success, new(constructed, complete, assessment), []);
        }
        return new(CharacterCreationFoundationOutcomes.Blocked, null,
            blockers.Append(PhysicalReadinessUnavailable3847).Distinct(StringComparer.Ordinal).ToArray());

        static CharacterCreationFoundationResult<CharacterCreationRoleCompletionCandidate> Refuse(string code)
            => new(CharacterCreationFoundationOutcomes.Blocked, null, [code]);
    }
}
