using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded, read-only additions using Core's ordinary skill evaluator. Preference
/// order is explicit input, not AI advice or an archetype optimization claim.
/// This component has no workspace, provider, owner lease or write capability.
/// </summary>
public static class CharacterCreationSkillCompletionPlanner
{
    public const int MaximumCandidateEvaluations = 256;
    public const int MaximumTargets = 128;

    public static CharacterCreationFoundationResult<CharacterCreationSkillCompletionPlan> Plan(
        CharacterCreationAttributesState attributesState,
        CharacterCreationSkillsState skillsState,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationSkillCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attributesState);
        ArgumentNullException.ThrowIfNull(skillsState);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Binding is null || request.PermittedTargetOrder is null
            || request.PermittedTargetOrder.Count > MaximumTargets
            || request.PermittedTargetOrder.Any(target => target is null)
            || request.ActivePointsToReserve < 0 || request.GroupPointsToReserve < 0
            || request.KnowledgePointsToReserve < 0)
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        request = request with { PermittedTargetOrder = request.PermittedTargetOrder.ToArray() };
        attributeAllocations = attributeAllocations.ToArray();
        attributesState = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(attributesState))!;
        skillsState = JsonSerializer.Deserialize<CharacterCreationSkillsState>(
            JsonSerializer.SerializeToUtf8Bytes(skillsState))!;
        if (request.Binding != skillsState.Binding
            || !CharacterCreationSkillsDigest.EqualsFixedTime(request.AttributesSnapshotDigest, attributesState.SnapshotDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(request.SkillsSnapshotDigest, skillsState.SnapshotDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationSkillCompletionBlockers.SnapshotConflict);

        var prepared = CharacterCreationSkillsService.PrepareCompletionPreview(
            attributesState, skillsState, attributeAllocations, cancellationToken);
        if (prepared.Value is not { } session)
            return new(prepared.Outcome, null, prepared.Blockers);
        if (skillsState.PrerequisiteDraft!.BuildMethod is not
            (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);

        var permitted = request.PermittedTargetOrder.Select(target => (target.Kind, target.SourceId)).ToHashSet();
        if (permitted.Count != request.PermittedTargetOrder.Count
            || request.PermittedTargetOrder.Any(target => string.IsNullOrWhiteSpace(target.SourceId)
                || !IsKnownTarget(skillsState.Authority, target)))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        // Core projections include free source grants, but not implicit group
        // member allocations. Preserve both kinds exactly, including identity
        // choices and specializations; never trade them away to fit a proposal.
        var allocations = skillsState.Skills.Select(skill => new CharacterCreationSkillAllocation(
            skill.SourceSkillId, skill.Kind, skill.Rating, skill.SpecializationOptionId, skill.IsNativeLanguage)).ToArray();
        var groups = skillsState.SkillGroups.Select(group =>
            new CharacterCreationSkillGroupAllocation(group.GroupId, group.Rating)).ToArray();
        var initial = session.Preview(allocations, groups, cancellationToken);
        if (initial.Value is not { } preview || !AllowsPlanning(preview.Blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, initial.Blockers);
        if (!PreservesReserves(preview, request))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        int baselineOverflow = preview.KnowledgePointOverflowToActive;
        int evaluations = 1;
        bool changed = false;
        bool workLimitReached = false;
        bool groupMemberLocked = false;
        foreach (var target in request.PermittedTargetOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isGroup = target.Kind == CharacterCreationSkillCompletionKinds.Group;
            if (isGroup && Remaining(preview, target.Kind) > Reserve(request, target.Kind)
                && skillsState.Authority.SkillGroups.Single(group => group.GroupId == target.SourceId)
                    .MemberSkillSourceIds.Any(id => !permitted.Contains((CharacterCreationSkillKinds.Active, id))))
            {
                groupMemberLocked = true;
                continue;
            }
            while (Remaining(preview, target.Kind) > Reserve(request, target.Kind))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (evaluations > MaximumCandidateEvaluations)
                {
                    workLimitReached = true;
                    break;
                }
                var candidateSkills = allocations.ToArray();
                var candidateGroups = groups.ToArray();
                if (isGroup)
                {
                    int index = Array.FindIndex(candidateGroups, group => group.GroupId == target.SourceId);
                    int rating = index < 0 ? 0 : candidateGroups[index].Rating;
                    if (rating == int.MaxValue) break;
                    var next = new CharacterCreationSkillGroupAllocation(target.SourceId, rating + 1);
                    if (index < 0) candidateGroups = [.. candidateGroups, next];
                    else candidateGroups[index] = next;
                }
                else
                {
                    int index = Array.FindIndex(candidateSkills, skill =>
                        skill.Kind == target.Kind && skill.SourceSkillId == target.SourceId);
                    var current = index < 0
                        ? new CharacterCreationSkillAllocation(target.SourceId, target.Kind, 0, null, false)
                        : candidateSkills[index];
                    // Native language has no numeric rating and is a story/
                    // identity choice. Permission to spend points does not change it.
                    if (current.IsNativeLanguage || current.Rating is not int rating || rating == int.MaxValue)
                        break;
                    var next = current with { Rating = rating + 1 };
                    if (index < 0) candidateSkills = [.. candidateSkills, next];
                    else candidateSkills[index] = next;
                }

                var candidate = session.Preview(candidateSkills, candidateGroups, cancellationToken);
                evaluations++;
                if (candidate.Value is not { } proposed || !AllowsPlanning(proposed.Blockers)
                    || !PreservesReserves(proposed, request)
                    || proposed.KnowledgePointOverflowToActive > baselineOverflow)
                    break;
                allocations = candidateSkills;
                groups = candidateGroups;
                preview = proposed;
                changed = true;
            }
            if (workLimitReached) break;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var blockers = preview.Blockers.ToList();
        if (preview.ActiveSkillPointBudget.Remaining > request.ActivePointsToReserve)
            blockers.Add(CharacterCreationSkillCompletionBlockers.ActivePointsUnallocated);
        if (preview.SkillGroupPointBudget.Remaining > request.GroupPointsToReserve)
        {
            blockers.Add(CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated);
            if (groupMemberLocked) blockers.Add(CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
        }
        if (preview.KnowledgeSkillPointBudget.Remaining > request.KnowledgePointsToReserve)
            blockers.Add(CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated);
        if (workLimitReached) blockers.Add(CharacterCreationSkillCompletionBlockers.WorkLimitReached);
        var plan = new CharacterCreationSkillCompletionPlan(request, attributeAllocations, allocations, groups,
            preview, changed, evaluations, blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            PlanDigest: string.Empty);
        plan = plan with { PlanDigest = CharacterCreationSkillsDigest.Compute(plan) };
        return new(plan.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, plan, plan.Blockers);
    }

    private static bool AllowsPlanning(IReadOnlyList<string> blockers) =>
        blockers.All(blocker => blocker == CharacterCreationSkillsBlockers.NativeLanguageRequired);

    private static bool IsKnownTarget(CharacterCreationSkillsAuthority authority, CharacterCreationSkillCompletionTarget target) =>
        target.Kind switch
        {
            CharacterCreationSkillKinds.Active => authority.ActiveSkills.Any(skill => skill.SourceSkillId == target.SourceId),
            CharacterCreationSkillKinds.Knowledge => authority.KnowledgeSkills.Any(skill => skill.SourceSkillId == target.SourceId),
            CharacterCreationSkillCompletionKinds.Group => authority.SkillGroups.Any(group => group.GroupId == target.SourceId),
            _ => false
        };

    private static bool PreservesReserves(CharacterCreationAttributeSkillsPreview preview, CharacterCreationSkillCompletionRequest request) =>
        preview.ActiveSkillPointBudget.Remaining >= request.ActivePointsToReserve
        && preview.SkillGroupPointBudget.Remaining >= request.GroupPointsToReserve
        && preview.KnowledgeSkillPointBudget.Remaining >= request.KnowledgePointsToReserve;

    private static decimal Remaining(CharacterCreationAttributeSkillsPreview preview, string kind) => kind switch
    {
        CharacterCreationSkillKinds.Active => preview.ActiveSkillPointBudget.Remaining,
        CharacterCreationSkillKinds.Knowledge => preview.KnowledgeSkillPointBudget.Remaining,
        _ => preview.SkillGroupPointBudget.Remaining
    };

    private static int Reserve(CharacterCreationSkillCompletionRequest request, string kind) => kind switch
    {
        CharacterCreationSkillKinds.Active => request.ActivePointsToReserve,
        CharacterCreationSkillKinds.Knowledge => request.KnowledgePointsToReserve,
        _ => request.GroupPointsToReserve
    };

    private static CharacterCreationFoundationResult<CharacterCreationSkillCompletionPlan> Reject(string outcome, string blocker) =>
        new(outcome, null, [blocker]);
}
