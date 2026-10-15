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
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationSkillCompletionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (!IsValidRequest(request))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        request = request with
        {
            PermittedTargetOrder = request.PermittedTargetOrder.ToArray(),
            Targets = request.Targets?.ToArray()
        };
        attributeAllocations = attributeAllocations.ToArray();
        inputs = JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(
            JsonSerializer.SerializeToUtf8Bytes(inputs))!;
        if (request.Binding != inputs.Binding
            || inputs.Attributes is null
            || !CharacterCreationSkillsDigest.EqualsFixedTime(request.AttributesSnapshotDigest, inputs.Attributes.SnapshotDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(request.SkillsSnapshotDigest, inputs.SnapshotDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationSkillCompletionBlockers.SnapshotConflict);

        var prepared = CharacterCreationSkillsService.PrepareCompletionPreview(inputs, attributeAllocations, ct);
        if (prepared.Value is not { } session)
            return new(prepared.Outcome, null, prepared.Blockers);
        return PlanPrepared(inputs.Authority, inputs.PrerequisiteDraft.BuildMethod, inputs.MovementCapability, session,
            inputs.Allocations, inputs.GroupAllocations, attributeAllocations, request, ct);
    }

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
        if (!IsValidRequest(request))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        request = request with
        {
            PermittedTargetOrder = request.PermittedTargetOrder.ToArray(),
            Targets = request.Targets?.ToArray()
        };
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
        return PlanPrepared(skillsState.Authority, skillsState.PrerequisiteDraft!.BuildMethod, skillsState.MovementCapability, session,
            skillsState.Skills.Select(skill => new CharacterCreationSkillAllocation(
                skill.SourceSkillId, skill.Kind, skill.Rating, skill.SpecializationOptionId, skill.IsNativeLanguage)),
            skillsState.SkillGroups.Select(group => new CharacterCreationSkillGroupAllocation(group.GroupId, group.Rating)),
            attributeAllocations, request, cancellationToken);
    }

    private static CharacterCreationFoundationResult<CharacterCreationSkillCompletionPlan> PlanPrepared(
        CharacterCreationSkillsAuthority authority,
        string buildMethod,
        CharacterCreationMovementCapability movementCapability,
        CharacterCreationSkillsService.CompletionPreviewSession session,
        IEnumerable<CharacterCreationSkillAllocation> baselineAllocations,
        IEnumerable<CharacterCreationSkillGroupAllocation> baselineGroups,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CharacterCreationSkillCompletionRequest request,
        CancellationToken cancellationToken)
    {
        if (buildMethod is not
            (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen))
            return Reject(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationPrerequisiteBlockers.BuildMethodUnsupported);

        var permitted = request.PermittedTargetOrder.Select(target => (target.Kind, target.SourceId)).ToHashSet();
        if (permitted.Count != request.PermittedTargetOrder.Count
            || request.PermittedTargetOrder.Any(target => string.IsNullOrWhiteSpace(target.SourceId)
                || !IsKnownTarget(authority, target))
            || request.Targets is { } targets &&
                (targets.Select(target => (target.Kind, target.SourceId)).Distinct().Count() != targets.Count
                || targets.Any(target => !permitted.Contains((target.Kind, target.SourceId)))))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        // Core projections include free source grants, but not implicit group
        // member allocations. Preserve both kinds exactly, including identity
        // choices and specializations; never trade them away to fit a proposal.
        var allocations = baselineAllocations.ToArray();
        var groups = baselineGroups.ToArray();
        var initial = session.Preview(allocations, groups, cancellationToken);
        if (initial.Value is not { } preview || !AllowsPlanning(preview.Blockers))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, initial.Blockers);
        if (!PreservesReserves(preview, request))
            return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillCompletionBlockers.RequestInvalid);

        int evaluations = 1;
        bool changed = false;
        bool workLimitReached = false;
        bool groupMemberLocked = false;
        if (request.NativeLanguageSourceId is { } nativeId)
        {
            if (string.IsNullOrWhiteSpace(nativeId)
                || !authority.KnowledgeSkills.Any(skill => skill.SourceSkillId == nativeId && skill.CanBeNativeLanguage))
                return Reject(CharacterCreationFoundationOutcomes.Invalid,
                    CharacterCreationSkillCompletionBlockers.RequestInvalid);
            var existing = allocations.FirstOrDefault(skill =>
                skill.Kind == CharacterCreationSkillKinds.Knowledge && skill.SourceSkillId == nativeId);
            if (existing is not null && (!existing.IsNativeLanguage || existing.Rating is not null
                    || existing.SpecializationOptionId is not null))
                return Reject(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillCompletionBlockers.NativeLanguageChoiceConflict);
            if (existing is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CharacterCreationSkillAllocation[] candidate = [.. allocations,
                    new(nativeId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
                var native = session.Preview(candidate, groups, cancellationToken);
                evaluations++;
                if (native.Value is not { } proposed || !AllowsPlanning(proposed.Blockers)
                    || !PreservesReserves(proposed, request))
                    return new(CharacterCreationFoundationOutcomes.Blocked, null, native.Blockers);
                allocations = candidate;
                preview = proposed;
                changed = true;
            }
        }
        int baselineOverflow = preview.KnowledgePointOverflowToActive;
        IEnumerable<(string Kind, string SourceId, int? Rating)> allocationOrder = request.Targets is null
            ? request.FillRemainingPoints ? []
                : request.PermittedTargetOrder.Select(target => (target.Kind, target.SourceId, (int?)null))
            : request.Targets.Select(target => (target.Kind, target.SourceId, (int?)target.TargetRating));
        foreach (var target in allocationOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target.Rating is int desired && MeetsTarget(preview, authority, movementCapability,
                    target.Kind, target.SourceId, desired))
                continue;
            bool isGroup = target.Kind == CharacterCreationSkillCompletionKinds.Group;
            if (isGroup && Remaining(preview, target.Kind) > Reserve(request, target.Kind)
                && authority.SkillGroups.Single(group => group.GroupId == target.SourceId)
                    .MemberSkillSourceIds.Any(id => !permitted.Contains((CharacterCreationSkillKinds.Active, id))))
            {
                groupMemberLocked = true;
                continue;
            }
            while (Remaining(preview, target.Kind) > Reserve(request, target.Kind)
                && (target.Rating is null || !MeetsTarget(preview, authority, movementCapability,
                    target.Kind, target.SourceId, target.Rating.Value)))
            {
                if (!TryIncrement(target.Kind, target.SourceId))
                    break;
            }
            if (workLimitReached) break;
        }

        if (request.FillRemainingPoints)
        {
            // Group permissions include their canonical members, but that is
            // not a request to buy/break those members independently. Attempt
            // group increments before other entries and keep positive groups.
            var fillOrder = request.PermittedTargetOrder.OrderBy(target =>
                target.Kind == CharacterCreationSkillCompletionKinds.Group ? 0 : 1).ToArray();
            bool progress;
            do
            {
                progress = false;
                foreach (var target in fillOrder)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (workLimitReached) break;
                    if (Remaining(preview, target.Kind) <= Reserve(request, target.Kind)) continue;
                    if (target.Kind == CharacterCreationSkillKinds.Active && preview.SkillGroups.Any(group =>
                            group.Rating > 0 && group.MemberSkillSourceIds.Contains(target.SourceId, StringComparer.Ordinal)))
                        continue;
                    if (TryIncrement(target.Kind, target.SourceId)) progress = true;
                }
            } while (progress && !workLimitReached);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var blockers = preview.Blockers.ToList();
        if (request.Targets is null || request.FillRemainingPoints)
        {
            if (preview.ActiveSkillPointBudget.Remaining > request.ActivePointsToReserve)
                blockers.Add(CharacterCreationSkillCompletionBlockers.ActivePointsUnallocated);
            if (preview.SkillGroupPointBudget.Remaining > request.GroupPointsToReserve)
            {
                blockers.Add(CharacterCreationSkillCompletionBlockers.GroupPointsUnallocated);
                if (groupMemberLocked) blockers.Add(CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
            }
            if (preview.KnowledgeSkillPointBudget.Remaining > request.KnowledgePointsToReserve)
                blockers.Add(CharacterCreationSkillCompletionBlockers.KnowledgePointsUnallocated);
        }
        if (request.Targets is not null)
        {
            if (request.Targets.Any(target => !MeetsTarget(preview, authority, movementCapability,
                    target.Kind, target.SourceId, target.TargetRating)))
                blockers.Add(CharacterCreationSkillCompletionBlockers.TargetsUnfulfilled);
            if (groupMemberLocked && (!request.FillRemainingPoints || request.Targets.Any(target =>
                    target.Kind == CharacterCreationSkillCompletionKinds.Group
                    && !MeetsTarget(preview, authority, movementCapability, target.Kind, target.SourceId, target.TargetRating)
                    && authority.SkillGroups.Single(group => group.GroupId == target.SourceId)
                        .MemberSkillSourceIds.Any(id => !permitted.Contains((CharacterCreationSkillKinds.Active, id))))))
                blockers.Add(CharacterCreationSkillCompletionBlockers.GroupMemberLocked);
        }
        if (workLimitReached) blockers.Add(CharacterCreationSkillCompletionBlockers.WorkLimitReached);
        var plan = new CharacterCreationSkillCompletionPlan(request, attributeAllocations, allocations, groups,
            preview, changed, evaluations, blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            PlanDigest: string.Empty);
        plan = plan with { PlanDigest = CharacterCreationSkillsDigest.Compute(plan) };
        return new(plan.Blockers.Count == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, plan, plan.Blockers);

        bool TryIncrement(string kind, string sourceId)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool isGroup = kind == CharacterCreationSkillCompletionKinds.Group;
            if (isGroup && authority.SkillGroups.Single(group => group.GroupId == sourceId)
                    .MemberSkillSourceIds.Any(id => !permitted.Contains((CharacterCreationSkillKinds.Active, id))))
            {
                groupMemberLocked = true;
                return false;
            }
            if (request.FillRemainingPoints || request.Targets is not null
                    ? evaluations >= MaximumCandidateEvaluations
                    : evaluations > MaximumCandidateEvaluations)
            {
                workLimitReached = true;
                return false;
            }
            var candidateSkills = allocations.ToArray();
            var candidateGroups = groups.ToArray();
            if (isGroup)
            {
                int index = Array.FindIndex(candidateGroups, group => group.GroupId == sourceId);
                int rating = index < 0 ? 0 : candidateGroups[index].Rating;
                if (rating == int.MaxValue) return false;
                var next = new CharacterCreationSkillGroupAllocation(sourceId, rating + 1);
                if (index < 0) candidateGroups = [.. candidateGroups, next];
                else candidateGroups[index] = next;
            }
            else
            {
                int index = Array.FindIndex(candidateSkills, skill => skill.Kind == kind && skill.SourceSkillId == sourceId);
                var current = index < 0
                    ? new CharacterCreationSkillAllocation(sourceId, kind, 0, null, false)
                    : candidateSkills[index];
                // A native language remains an identity choice, never a rating.
                if (current.IsNativeLanguage || current.Rating is not int rating || rating == int.MaxValue)
                    return false;
                var next = current with { Rating = rating + 1 };
                if (index < 0) candidateSkills = [.. candidateSkills, next];
                else candidateSkills[index] = next;
            }
            var candidate = session.Preview(candidateSkills, candidateGroups, cancellationToken);
            evaluations++;
            if (candidate.Value is not { } proposed || !AllowsPlanning(proposed.Blockers)
                || !PreservesReserves(proposed, request)
                || proposed.KnowledgePointOverflowToActive > baselineOverflow)
                return false;
            allocations = candidateSkills;
            groups = candidateGroups;
            preview = proposed;
            changed = true;
            return true;
        }
    }

    private static bool IsValidRequest(CharacterCreationSkillCompletionRequest request) =>
        request.Binding is not null && request.PermittedTargetOrder is not null
        && request.PermittedTargetOrder.Count <= MaximumTargets
        && !request.PermittedTargetOrder.Any(target => target is null)
        && (request.Targets is null || request.Targets.Count <= MaximumTargets
            && request.Targets.All(target => target is not null && target.TargetRating >= 0
                && !string.IsNullOrWhiteSpace(target.SourceId)))
        && request.ActivePointsToReserve >= 0 && request.GroupPointsToReserve >= 0
        && request.KnowledgePointsToReserve >= 0;

    private static bool AllowsPlanning(IReadOnlyList<string> blockers) =>
        blockers.All(blocker => blocker == CharacterCreationSkillsBlockers.NativeLanguageRequired);

    private static bool MeetsTarget(CharacterCreationAttributeSkillsPreview preview,
        CharacterCreationSkillsAuthority authority, CharacterCreationMovementCapability movement,
        string kind, string sourceId, int desired)
        => EvaluateTarget(preview, authority, movement, new(kind, sourceId, desired)).IsSatisfied;

    /// <summary>Observes one numeric goal using the same source/grant/group and
    /// movement rules as planning. The caller supplies an already evaluated Core
    /// preview and its actual authority; this does not admit that input as current
    /// source, ownership or mutation authority, and never spends any points.</summary>
    public static CharacterCreationSkillCompletionTargetEvaluation EvaluateTarget(
        CharacterCreationAttributeSkillsPreview preview, CharacterCreationSkillsAuthority authority,
        CharacterCreationMovementCapability movement, CharacterCreationSkillCompletionRatingTarget target)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(movement);
        ArgumentNullException.ThrowIfNull(target);
        if (target.TargetRating < 0 || string.IsNullOrWhiteSpace(target.SourceId))
            throw new ArgumentException("A target requires a source identity and nonnegative numeric goal.", nameof(target));
        if (!IsKnownTarget(authority, new(target.Kind, target.SourceId)))
            return new(target, false, null, false);
        int? effective = EffectiveRating();
        return new(target, true, effective, effective is int rating && rating >= target.TargetRating);

        int? EffectiveRating()
        {
            if (target.Kind == CharacterCreationSkillCompletionKinds.Group)
                return preview.SkillGroups.SingleOrDefault(group => group.GroupId == target.SourceId)?.Rating ?? 0;
            var skill = preview.Skills.SingleOrDefault(row => row.Kind == target.Kind && row.SourceSkillId == target.SourceId);
            // Native is never a numeric rating, even when a caller asks for zero.
            if (skill is not null) return skill.EffectiveRating;
            if (target.Kind != CharacterCreationSkillKinds.Active) return 0;
            var source = authority.ActiveSkills.Single(item => item.SourceSkillId == target.SourceId);
            // Group projections retain disabled canonical members for authority
            // matching. They must not imply an effective skill the runner cannot use.
            if (!CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, target.SourceId)
                || source.RequiresGroundMovement && !movement.Ground
                || source.RequiresSwimMovement && !movement.Swim
                || source.RequiresFlyMovement && !movement.Fly)
                return 0;
            return preview.SkillGroups.Where(group => group.IsEnabled
                    && group.MemberSkillSourceIds.Contains(target.SourceId, StringComparer.Ordinal))
                .Select(group => group.Rating).DefaultIfEmpty(0).Max();
        }
    }

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
