using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded, read-only Priority/Sum-to-Ten attribute proposal using the ordinary
/// Core evaluator. There is no workspace, provider, owner lease or write path.
/// Preference order is explicit input, not a claim of archetype optimization.
/// </summary>
public static class CharacterCreationAttributeCompletionPlanner
{
    public const int MaximumCandidateEvaluations = 256;

    public static CharacterCreationFoundationResult<CharacterCreationAttributeCompletionPlan> Plan(
        CharacterCreationAttributesState state,
        CharacterCreationAttributeCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Binding is null || request.PermittedAttributeOrder is null
            || request.PermittedAttributeOrder.Count > 13
            || request.Targets is { } requestedTargets && (requestedTargets.Count > 13
                || requestedTargets.Any(target => target is null || target.TargetUnaugmentedRating < 0))
            || request.KarmaTargets is { } karmaTargets && (karmaTargets.Count > 13
                || karmaTargets.Any(target => target is null || target.TargetUnaugmentedRating < 0))
            || request.MaximumAdditionalKarmaSpend < 0
            || request.NormalPointsToReserve < 0 || request.SpecialPointsToReserve < 0)
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributeCompletionBlockers.RequestInvalid);

        // Both input collections and the rules session are detached. Never read
        // the caller's mutable arrays again once planning starts.
        request = request with
        {
            PermittedAttributeOrder = request.PermittedAttributeOrder.ToArray(),
            Targets = request.Targets?.ToArray(),
            KarmaTargets = request.KarmaTargets?.ToArray()
        };
        state = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(state))!;
        if (request.Binding != state.Binding
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                request.SnapshotDigest, state.SnapshotDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributeCompletionBlockers.SnapshotConflict);

        var session = CharacterCreationAttributesService.CreateDraftSession(state);
        if (session is null)
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        if (state.PrerequisiteDraft!.BuildMethod is not
            (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);

        var attributes = state.Attributes;
        if (attributes is null || attributes.Count > 13
            || attributes.Any(item => item is null)
            || attributes.Select(item => item.AttributeId).Distinct(StringComparer.Ordinal).Count() != attributes.Count)
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        if (request.PermittedAttributeOrder.Distinct(StringComparer.Ordinal).Count()
                != request.PermittedAttributeOrder.Count
            || request.PermittedAttributeOrder.Any(id => string.IsNullOrWhiteSpace(id)
                || !attributes.Any(item => item.AttributeId == id && item.IsEnabled))
            || request.Targets is { } targets &&
                (targets.Select(target => target.AttributeId).Distinct(StringComparer.Ordinal).Count() != targets.Count
                || targets.Any(target => string.IsNullOrWhiteSpace(target.AttributeId)
                    || !request.PermittedAttributeOrder.Contains(target.AttributeId, StringComparer.Ordinal)))
            || request.KarmaTargets is { } karmaGoals &&
                (karmaGoals.Select(target => target.AttributeId).Distinct(StringComparer.Ordinal).Count() != karmaGoals.Count
                || karmaGoals.Any(target => string.IsNullOrWhiteSpace(target.AttributeId)
                    || !request.PermittedAttributeOrder.Contains(target.AttributeId, StringComparer.Ordinal))))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributeCompletionBlockers.RequestInvalid);

        var allocations = attributes.Select(item => new CharacterCreationAttributeAllocation(
            item.AttributeId, item.PriorityPointsSpent, item.KarmaLevels)).ToArray();
        var initial = session.Preview(allocations);
        if (initial.Value is not { } preview
            || preview.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, initial.Blockers);
        if (request.NormalPointsToReserve > preview.NormalPointBudget.Remaining
            || request.SpecialPointsToReserve > preview.SpecialPointBudget.Remaining)
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributeCompletionBlockers.RequestInvalid);

        int evaluations = 1;
        bool changed = false;
        bool workLimitReached = false;
        decimal originalKarmaUsed = preview.CreationKarmaBudget.Used;
        var goals = request.Targets?.ToDictionary(target => target.AttributeId,
            target => target.TargetUnaugmentedRating, StringComparer.Ordinal);
        IEnumerable<string> allocationOrder = request.Targets is null
            ? request.FillRemainingPoints ? [] : request.PermittedAttributeOrder
            : request.Targets.Select(target => target.AttributeId);
        foreach (string id in allocationOrder)
        {
            int index = Array.FindIndex(allocations, item => item.AttributeId == id);
            bool normal = attributes[index].Category == CharacterCreationAttributeCategories.Normal;
            int reserve = normal ? request.NormalPointsToReserve : request.SpecialPointsToReserve;
            while ((normal ? preview.NormalPointBudget : preview.SpecialPointBudget).Remaining > reserve
                && (goals is null || preview.Attributes.Single(item => item.AttributeId == id).UnaugmentedCurrent < goals[id]))
            {
                if (!TryIncrement(index))
                    break;
            }
            if (workLimitReached)
                break;
        }

        if (request.FillRemainingPoints)
        {
            bool progress;
            do
            {
                progress = false;
                foreach (string id in request.PermittedAttributeOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (workLimitReached) break;
                    int index = Array.FindIndex(allocations, item => item.AttributeId == id);
                    bool normal = attributes[index].Category == CharacterCreationAttributeCategories.Normal;
                    int reserve = normal ? request.NormalPointsToReserve : request.SpecialPointsToReserve;
                    if ((normal ? preview.NormalPointBudget : preview.SpecialPointBudget).Remaining > reserve
                        && TryIncrement(index))
                        progress = true;
                }
            } while (progress && !workLimitReached);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var blockers = new List<string>();
        if (request.Targets is null || request.FillRemainingPoints)
        {
            if (preview.NormalPointBudget.Remaining > request.NormalPointsToReserve)
                blockers.Add(CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
            if (preview.SpecialPointBudget.Remaining > request.SpecialPointsToReserve)
                blockers.Add(CharacterCreationAttributeCompletionBlockers.SpecialPointsUnallocated);
        }
        if (request.Targets is not null && request.Targets.Any(target => preview.Attributes.Single(item =>
                     item.AttributeId == target.AttributeId).UnaugmentedCurrent < target.TargetUnaugmentedRating))
            blockers.Add(CharacterCreationAttributeCompletionBlockers.TargetsUnfulfilled);
        if (workLimitReached)
            blockers.Add(CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
        var plan = new CharacterCreationAttributeCompletionPlan(
            request, allocations, preview, changed, evaluations, blockers.ToArray());
        return new(blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, plan, plan.Blockers);

        bool TryIncrement(int index)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (request.FillRemainingPoints || goals is not null
                    ? evaluations >= MaximumCandidateEvaluations
                    : evaluations > MaximumCandidateEvaluations)
            {
                workLimitReached = true;
                return false;
            }
            if (allocations[index].PriorityPoints == int.MaxValue) return false;
            var candidate = allocations.ToArray();
            candidate[index] = candidate[index] with { PriorityPoints = candidate[index].PriorityPoints + 1 };
            var result = session.Preview(candidate);
            evaluations++;
            if (result.Outcome != CharacterCreationFoundationOutcomes.Success
                || result.Value is not { CanConfirm: true, Blockers.Count: 0 } proposed
                || proposed.NormalPointBudget.Remaining < request.NormalPointsToReserve
                || proposed.SpecialPointBudget.Remaining < request.SpecialPointsToReserve
                // Adding priority points must not reprice the retained Karma levels.
                || proposed.CreationKarmaBudget.Used > originalKarmaUsed)
                return false;
            allocations = candidate;
            preview = proposed;
            changed = true;
            return true;
        }
    }

    private static CharacterCreationFoundationResult<CharacterCreationAttributeCompletionPlan> Reject(
        string outcome, string blocker) => new(outcome, null, [blocker]);
}
