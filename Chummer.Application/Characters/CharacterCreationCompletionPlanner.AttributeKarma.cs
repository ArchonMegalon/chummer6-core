using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    // Whole-graph reviews are more expensive than attribute-only probes. This
    // separate fixed bound includes every attempted level, even failed probes.
    public const int MaximumAttributeKarmaCandidateEvaluations = 32;

    private sealed record AttributeKarmaCompletion(
        CharacterCreationAttributeCompletionPlan Attributes,
        CharacterCreationCompletionSelections Selections,
        CharacterCreationCompletionReview Review,
        bool Changed);

    private static bool HasAutomaticAttributePermissions(CharacterCreationAttributesState state,
        IReadOnlyList<string> permitted)
        => permitted.All(id => state.PendingDraft is { } draft
            ? draft.PlayerChosenAttributeIds is { } chosen && !chosen.Contains(id, StringComparer.Ordinal)
            : state.Attributes.Any(row => row.AttributeId == id && row.PriorityPointsSpent == 0 && row.KarmaLevels == 0));

    private static CharacterCreationFoundationResult<AttributeKarmaCompletion> AllocateAttributeKarma(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionPlanRequest request,
        CharacterCreationAttributeCompletionPlan attributes,
        CharacterCreationCompletionSelections choices,
        CharacterCreationCompletionReview review,
        CancellationToken cancellationToken)
    {
        var blockers = attributes.Blockers.ToList();
        var goals = request.Attributes.KarmaTargets;
        int evaluations = 0;
        bool changed = false;
        decimal originalCost = attributes.Preview.CreationKarmaBudget.Used;
        var preview = attributes.Preview;
        var session = CharacterCreationAttributesService.CreateDraftSession(inputs.Skills.Attributes);
        if (session is null)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationAttributesBlockers.AuthorityUnavailable]);
        if (goals is not { Count: > 0 })
            blockers.Add(CharacterCreationAttributeCompletionBlockers.KarmaTargetsRequired);
        else foreach (var goal in goals)
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
                var candidate = choices.AttributeAllocations.ToArray();
                int index = Array.FindIndex(candidate, row => row.AttributeId == goal.AttributeId);
                if (candidate[index].KarmaLevels == int.MaxValue)
                {
                    blockers.Add(CharacterCreationAttributesBlockers.AllocationInvalid);
                    break;
                }
                candidate[index] = candidate[index] with { KarmaLevels = candidate[index].KarmaLevels + 1 };
                evaluations++;
                var proposed = session.Preview(candidate);
                if (Terminal(proposed.Outcome)) return new(proposed.Outcome, null, proposed.Blockers);
                if (proposed.Outcome != CharacterCreationFoundationOutcomes.Success
                    || proposed.Value is not { CanConfirm: true, Blockers.Count: 0 } next)
                {
                    blockers.AddRange(proposed.Blockers);
                    break;
                }
                decimal spend = next.CreationKarmaBudget.Used - originalCost;
                if (spend < 0 || spend > request.Attributes.MaximumAdditionalKarmaSpend)
                {
                    blockers.Add(CharacterCreationAttributeCompletionBlockers.KarmaSpendLimit);
                    break;
                }

                // Always review original observations with the complete proposal.
                // No intermediate draft, plan digest or cached pricing is authority.
                // In particular, CHA can change contact costs and MAG can invalidate
                // a previously complete magic graph. Never re-select magic here.
                var nextChoices = choices with { AttributeAllocations = candidate };
                var evaluated = CharacterCreationCompletionReviewService.Review(inputs,
                    new(request.InputsSnapshotDigest, nextChoices), cancellationToken);
                if (Terminal(evaluated.Outcome)) return new(evaluated.Outcome, null, evaluated.Blockers);
                if (evaluated.Outcome != CharacterCreationFoundationOutcomes.Success
                    || evaluated.Value is not { Blockers.Count: 0 } nextReview)
                {
                    blockers.AddRange(evaluated.Blockers);
                    break;
                }
                // The review has charged Attributes, Qualities, PP, contacts and
                // the full nuyen investment once. A null balance never means zero.
                if (nextReview.Balances.RemainingCreationKarma is not int remaining
                    || remaining < request.KarmaToReserve)
                {
                    blockers.Add(CharacterCreationCompletionPlanBlockers.KarmaReserveRequired);
                    break;
                }
                if (request.NuyenToReserve > 0 && (nextReview.Balances.RemainingNuyen is not decimal nuyen
                    || nuyen < request.NuyenToReserve))
                {
                    blockers.Add(CharacterCreationCompletionPlanBlockers.NuyenReserveRequired);
                    break;
                }
                choices = nextChoices;
                review = nextReview;
                preview = next;
                changed = true;
            }
            if (evaluations >= MaximumAttributeKarmaCandidateEvaluations) break;
        }

        bool unmet = goals?.Any(goal => preview.Attributes.Single(row => row.AttributeId == goal.AttributeId)
            .UnaugmentedCurrent < goal.TargetUnaugmentedRating) == true;
        if (unmet)
        {
            blockers.Add(CharacterCreationAttributeCompletionBlockers.KarmaTargetsUnfulfilled);
            if (evaluations >= MaximumAttributeKarmaCandidateEvaluations)
                blockers.Add(CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
        }
        if (request.Attributes.Targets is { } pointGoals && pointGoals.All(goal => preview.Attributes
                .Single(row => row.AttributeId == goal.AttributeId).UnaugmentedCurrent >= goal.TargetUnaugmentedRating))
            blockers.RemoveAll(value => value == CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled);
        attributes = attributes with
        {
            Allocations = choices.AttributeAllocations, Preview = preview,
            HasChanges = attributes.HasChanges || changed, Evaluations = attributes.Evaluations + evaluations,
            Blockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
        };
        cancellationToken.ThrowIfCancellationRequested();
        return new(attributes.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, new(attributes, choices, review, changed), attributes.Blockers);
    }

    private static void RefreshDependentPlans(CharacterCreationCompletionSelections choices,
        CharacterCreationCompletionReview review, ref CharacterCreationSkillCompletionPlan? skills,
        ref CharacterCreationMagicCompletionPlan? magic, ref CharacterCreationPurchaseCompletionPlan? purchases)
    {
        // Rebind projections to the accepted full review, without rerunning any
        // chooser or claiming a saved draft. Original request bindings stay intact.
        if (skills is not null)
        {
            var skillBlockers = skills.Blockers.ToList();
            if (skills.Request.Targets is null || skills.Request.FillRemainingPoints)
            {
                // Mental attribute increases can grant more knowledge points.
                // Keep that newly unfilled pool visible without silently making
                // another round of skill choices after magic/purchases.
                UpdatePool(CharacterCreationSkillCompletionBlockers.ActivePointsUnallocated,
                    review.Skills!.ActiveSkillPointBudget.Remaining, skills.Request.ActivePointsToReserve);
                UpdatePool(CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated,
                    review.Skills!.SkillGroupPointBudget.Remaining, skills.Request.GroupPointsToReserve);
                UpdatePool(CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated,
                    review.Skills!.KnowledgeSkillPointBudget.Remaining, skills.Request.KnowledgePointsToReserve);
            }
            skills = skills with { AttributeAllocations = choices.AttributeAllocations,
                Preview = review.Skills!, PlanDigest = string.Empty,
                Blockers = skillBlockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
            skills = skills with { PlanDigest = CharacterCreationSkillsDigest.Compute(skills) };

            void UpdatePool(string blocker, decimal remaining, int reserve)
            {
                skillBlockers.RemoveAll(value => value == blocker);
                if (remaining > reserve) skillBlockers.Add(blocker);
            }
        }
        if (magic is not null)
        {
            magic = magic with { AttributeAllocations = choices.AttributeAllocations,
                Preview = review.UpstreamBudget!.Magic, Budget = review.UpstreamBudget,
                IsMagicComplete = review.UpstreamBudget.Magic.Blockers.Count == 0, PlanDigest = string.Empty };
            magic = magic with { PlanDigest = CharacterCreationMagicCompletionRules.ComputePlanDigest(magic) };
        }
        if (purchases is not null)
        {
            purchases = purchases with { Request = purchases.Request with { AttributeAllocations = choices.AttributeAllocations },
                Preview = review.Lifestyles!, PlanDigest = string.Empty };
            purchases = purchases with { PlanDigest = CharacterCreationPurchaseCompletionRules.ComputePlanDigest(purchases) };
        }
    }
}
