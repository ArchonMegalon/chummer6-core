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
            || request.NormalPointsToReserve < 0 || request.SpecialPointsToReserve < 0)
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationAttributeCompletionBlockers.RequestInvalid);

        // Both input collections and the rules session are detached. Never read
        // the caller's mutable arrays again once planning starts.
        request = request with { PermittedAttributeOrder = request.PermittedAttributeOrder.ToArray() };
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
                || !attributes.Any(item => item.AttributeId == id && item.IsEnabled)))
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
        foreach (string id in request.PermittedAttributeOrder)
        {
            int index = Array.FindIndex(allocations, item => item.AttributeId == id);
            bool normal = attributes[index].Category == CharacterCreationAttributeCategories.Normal;
            int reserve = normal ? request.NormalPointsToReserve : request.SpecialPointsToReserve;
            while ((normal ? preview.NormalPointBudget : preview.SpecialPointBudget).Remaining > reserve)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evaluations > MaximumCandidateEvaluations)
                {
                    workLimitReached = true;
                    break;
                }
                if (allocations[index].PriorityPoints == int.MaxValue)
                    break;

                var candidate = allocations.ToArray();
                candidate[index] = candidate[index] with { PriorityPoints = candidate[index].PriorityPoints + 1 };
                var result = session.Preview(candidate);
                evaluations++;
                if (result.Outcome != CharacterCreationFoundationOutcomes.Success
                    || result.Value is not { CanConfirm: true, Blockers.Count: 0 } proposed
                    || proposed.NormalPointBudget.Remaining < request.NormalPointsToReserve
                    || proposed.SpecialPointBudget.Remaining < request.SpecialPointsToReserve
                    // Priority points can reprice existing Karma levels. Keeping
                    // KarmaLevels alone would not preserve the player's spending.
                    || proposed.CreationKarmaBudget.Used > originalKarmaUsed)
                    break;

                allocations = candidate;
                preview = proposed;
                changed = true;
            }
            if (workLimitReached)
                break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var blockers = new List<string>();
        if (preview.NormalPointBudget.Remaining > request.NormalPointsToReserve)
            blockers.Add(CharacterCreationAttributeCompletionBlockers.NormalPointsUnallocated);
        if (preview.SpecialPointBudget.Remaining > request.SpecialPointsToReserve)
            blockers.Add(CharacterCreationAttributeCompletionBlockers.SpecialPointsUnallocated);
        if (workLimitReached)
            blockers.Add(CharacterCreationAttributeCompletionBlockers.WorkLimitReached);
        var plan = new CharacterCreationAttributeCompletionPlan(
            request, allocations, preview, changed, evaluations, blockers.ToArray());
        return new(blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, plan, plan.Blockers);
    }

    private static CharacterCreationFoundationResult<CharacterCreationAttributeCompletionPlan> Reject(
        string outcome, string blocker) => new(outcome, null, [blocker]);
}
