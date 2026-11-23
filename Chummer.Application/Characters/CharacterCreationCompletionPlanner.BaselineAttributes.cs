using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    // Feasibility staging only. In particular, do not demand a complete magic or
    // knowledge graph before changing the attributes that determine its budget.
    // The caller still runs every dependent planner and the complete final review.
    private static CharacterCreationFoundationResult<CharacterCreationAttributeCompletionPlan> StageBaselineAttributeFloors(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionPlanRequest request,
        CharacterCreationAttributeCompletionPlan attributes, CancellationToken cancellationToken)
    {
        var session = CharacterCreationAttributesService.CreateDraftSession(inputs.Skills.Attributes);
        if (session is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationAttributesBlockers.AuthorityUnavailable]);
        var goals = request.Attributes.KarmaTargets;
        if (goals is not { Count: > 0 })
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationAttributeCompletionBlockers.KarmaTargetsRequired]);

        var blockers = attributes.Blockers.ToList();
        var allocations = attributes.Allocations.ToArray();
        var preview = attributes.Preview;
        decimal originalCost = preview.CreationKarmaBudget.Used;
        int evaluations = 0;
        bool changed = false;
        foreach (var goal in goals)
        {
            while (preview.Attributes.Single(row => row.AttributeId == goal.AttributeId).UnaugmentedCurrent
                < goal.TargetUnaugmentedRating)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evaluations >= MaximumAttributeKarmaCandidateEvaluations)
                {
                    blockers.Add(CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
                    break;
                }
                var candidate = allocations.ToArray();
                int index = Array.FindIndex(candidate, row => row.AttributeId == goal.AttributeId);
                if (index < 0 || candidate[index].KarmaLevels == int.MaxValue)
                {
                    blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                    break;
                }
                candidate[index] = candidate[index] with { KarmaLevels = candidate[index].KarmaLevels + 1 };
                evaluations++;
                var evaluated = session.Preview(candidate);
                if (Terminal(evaluated.Outcome)) return new(evaluated.Outcome, null, evaluated.Blockers);
                if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success
                    || evaluated.Value is not { CanConfirm: true, Blockers.Count: 0 } next)
                {
                    blockers.AddRange(evaluated.Blockers);
                    break;
                }
                decimal spend = next.CreationKarmaBudget.Used - originalCost;
                if (spend < 0 || spend > request.Attributes.MaximumAdditionalKarmaSpend)
                {
                    blockers.Add(CharacterCreationAttributeCompletionBlockers.KarmaSpendLimit);
                    break;
                }
                allocations = candidate;
                preview = next;
                changed = true;
            }
            if (evaluations >= MaximumAttributeKarmaCandidateEvaluations) break;
        }

        if (goals.Any(goal => preview.Attributes.Single(row => row.AttributeId == goal.AttributeId)
                .UnaugmentedCurrent < goal.TargetUnaugmentedRating))
            blockers.Add(CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled);
        if (request.Attributes.Targets is { } pointGoals && pointGoals.All(goal => preview.Attributes
                .Single(row => row.AttributeId == goal.AttributeId).UnaugmentedCurrent >= goal.TargetUnaugmentedRating))
            blockers.RemoveAll(value => value == CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled);

        attributes = attributes with
        {
            Allocations = allocations, Preview = preview, HasChanges = attributes.HasChanges || changed,
            Evaluations = attributes.Evaluations + evaluations,
            Blockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(attributes.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, attributes, attributes.Blockers);
    }
}
