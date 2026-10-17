using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    /// <summary>
    /// Evaluate explicit hypothetical choices without saving attributes, replacing
    /// historical skill drafts, or creating receipts. Requires valid original
    /// attributes and skills snapshots from the same saved workspace revision.
    /// Planning before the first saved Attributes draft needs separate authority
    /// preparation; this entry point never invents that missing baseline.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview>
        PreviewWithProposedAttributes(
            CharacterCreationAttributesState attributesState,
            CharacterCreationSkillsState skillsState,
            IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skillAllocations);
        ArgumentNullException.ThrowIfNull(groupAllocations);
        var prepared = PrepareCompletionPreview(attributesState, skillsState, attributeAllocations, cancellationToken);
        return prepared.Value is { } session
            ? session.Preview(skillAllocations, groupAllocations, cancellationToken)
            : new(prepared.Outcome, null, prepared.Blockers);
    }

    /// <summary>
    /// Plan from admitted original inputs even before Attributes is saved.
    /// This is a detached historical projection, not permission to apply it.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview>
        PreviewWithProposedAttributes(
            CharacterCreationSkillsCompletionInputs inputs,
            IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(skillAllocations);
        ArgumentNullException.ThrowIfNull(groupAllocations);
        var prepared = PrepareCompletionPreview(inputs, attributeAllocations, cancellationToken);
        return prepared.Value is { } session
            ? session.Preview(skillAllocations, groupAllocations, cancellationToken)
            : new(prepared.Outcome, null, prepared.Blockers);
    }

    internal static CharacterCreationFoundationResult<CompletionPreviewSession> PrepareCompletionPreview(
        CharacterCreationSkillsCompletionInputs inputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        cancellationToken.ThrowIfCancellationRequested();
        inputs = JsonSerializer.Deserialize<CharacterCreationSkillsCompletionInputs>(
            JsonSerializer.SerializeToUtf8Bytes(inputs))!;
        attributeAllocations = attributeAllocations.ToArray();
        if (!IsValidCompletionInputs(inputs))
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.AuthorityUnavailable);
        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(inputs.Attributes);
        if (attributesSession is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);
        return new(CharacterCreationFoundationOutcomes.Success,
            new CompletionPreviewSession(inputs.Attributes.SnapshotDigest, inputs.SnapshotDigest,
                inputs.Binding, inputs.Authority, inputs.PrerequisiteDraft,
                inputs.SelectedActiveSkillPoints, inputs.SelectedSkillGroupPoints,
                inputs.MovementCapability, inputs.KnowledgePointContributions, attributes), []);
    }

    private static bool IsValidCompletionInputs(CharacterCreationSkillsCompletionInputs inputs)
    {
        if (inputs.Schema != CharacterCreationSkillsCompletionSchemas.InputsV1
            || inputs.Binding is null || inputs.Attributes?.Binding is null
            || inputs.PrerequisiteDraft?.Assignments is null
            || inputs.Allocations is null || inputs.GroupAllocations is null
            || inputs.KnowledgePointContributions is null
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.SnapshotDigest,
                CharacterCreationSkillsDigest.Compute(inputs with { SnapshotDigest = string.Empty }))
            || !CharacterCreationSkillsDraftIntegrity.IsValidAuthority(inputs.Authority)
            || !CharacterCreationSkillsAccessRules.MatchesPrerequisite(inputs.Authority, inputs.PrerequisiteDraft)
            || !MatchesCompletionAttributes(inputs.Binding, inputs.PrerequisiteDraft, inputs.Attributes)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.SkillsAuthorityDigest, inputs.Authority.AuthorityDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.RuntimeDigest, inputs.Authority.RuntimeDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(inputs.Binding.ContributionInputsDigest,
                CharacterCreationSkillsDigest.Compute(inputs.KnowledgePointContributions))
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                inputs.KnowledgePointContributions, inputs.Authority.KnowledgePointContributions)
            || inputs.KnowledgePointContributions.Any(contribution =>
                !CharacterCreationSkillsDigest.EqualsFixedTime(contribution.SourceCharacterXmlDigest, inputs.Binding.RawCharacterXmlDigest))
            || inputs.MovementCapability != ResolveMovementCapability(inputs.PrerequisiteDraft.HeritageSelection?.Movement)
            || inputs.PrerequisiteDraft.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || inputs.PrerequisiteDraft.PriorityTable != "Standard")
            return false;

        var assignments = inputs.PrerequisiteDraft.Assignments.Where(item =>
            item.CategoryId == CharacterCreationPriorityCategoryIds.Skills).Take(2).ToArray();
        if (assignments.Length != 1
            || !CharacterCreationStandardPrioritySkillsRules.TryGetBudget(assignments[0].Rank, out int active, out int groups)
            || active != inputs.SelectedActiveSkillPoints || groups != inputs.SelectedSkillGroupPoints)
            return false;

        if (inputs.SavedSkillsState is { } saved)
            return saved.PendingDraft is { } draft && CreateDraftSession(saved) is not null
                && saved.Binding == inputs.Binding
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.Authority, inputs.Authority)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.PrerequisiteDraft, inputs.PrerequisiteDraft)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(saved.AttributesDraft, inputs.Attributes.PendingDraft)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(draft.Allocations, inputs.Allocations)
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(draft.GroupAllocations, inputs.GroupAllocations);

        if (!CharacterCreationTalentSkillGrants.TryResolve(inputs.PrerequisiteDraft, inputs.Authority, out var grants))
            return false;
        var initialSkills = grants.InitialSkills(inputs.Authority).Select(skill => new CharacterCreationSkillAllocation(
            skill.SourceSkillId, skill.Kind, skill.Rating, skill.SpecializationOptionId, skill.IsNativeLanguage)).ToArray();
        var initialGroups = grants.InitialGroups(inputs.Authority).Select(group =>
            new CharacterCreationSkillGroupAllocation(group.GroupId, group.Rating)).ToArray();
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(initialSkills, inputs.Allocations)
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(initialGroups, inputs.GroupAllocations);
    }

    // The bounded planner validates/clones the original source snapshots once,
    // not once per tentative point. This session has no workspace/write path.
    internal static CharacterCreationFoundationResult<CompletionPreviewSession> PrepareCompletionPreview(
        CharacterCreationAttributesState attributesState,
        CharacterCreationSkillsState skillsState,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attributesState);
        ArgumentNullException.ThrowIfNull(skillsState);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        cancellationToken.ThrowIfCancellationRequested();

        // IReadOnlyList may wrap caller-owned arrays. All validation and evaluation
        // below use the same private copies, including authority/source anchors.
        attributesState = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(attributesState))!;
        skillsState = JsonSerializer.Deserialize<CharacterCreationSkillsState>(
            JsonSerializer.SerializeToUtf8Bytes(skillsState))!;
        attributeAllocations = attributeAllocations.ToArray();

        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(attributesState);
        if (attributesSession is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        if (CreateDraftSession(skillsState) is null)
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.AuthorityUnavailable);

        CharacterCreationAttributesBinding attributesBinding = attributesState.Binding;
        CharacterCreationSkillsBinding skillsBinding = skillsState.Binding;
        if (attributesBinding.WorkspaceId != skillsBinding.WorkspaceId
            || attributesBinding.ContentRevision != skillsBinding.ContentRevision
            || attributesBinding.SavedRevision != skillsBinding.SavedRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.RawCharacterXmlDigest, skillsBinding.RawCharacterXmlDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.AuxiliaryStateDigest, skillsBinding.AuxiliaryStateDigest)
            || attributesBinding.PrerequisiteDraftRevision != skillsBinding.PrerequisiteDraftRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.PrerequisiteDraftDigest, skillsBinding.PrerequisiteDraftDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(
                attributesBinding.PrerequisiteAuthorityDigest, skillsBinding.PrerequisiteAuthorityDigest)
            || attributesState.PendingDraft is null
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                attributesState.PendingDraft, skillsState.AttributesDraft)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                attributesState.PrerequisiteDraft, skillsState.PrerequisiteDraft))
            return Blocked<CompletionPreviewSession>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);

        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);

        return new(CharacterCreationFoundationOutcomes.Success,
            new CompletionPreviewSession(attributesState.SnapshotDigest, skillsState.SnapshotDigest,
                skillsState.Binding, skillsState.Authority, skillsState.PrerequisiteDraft!,
                skillsState.SelectedActiveSkillPoints, skillsState.SelectedSkillGroupPoints,
                skillsState.MovementCapability, skillsState.KnowledgePointContributions, attributes), []);
    }

    internal sealed class CompletionPreviewSession(
        string attributesSnapshotDigest,
        string skillsSnapshotDigest,
        CharacterCreationSkillsBinding binding,
        CharacterCreationSkillsAuthority authority,
        CharacterCreationPrerequisiteDraft prerequisite,
        int selectedActiveSkillPoints,
        int selectedSkillGroupPoints,
        CharacterCreationMovementCapability movement,
        IReadOnlyList<CharacterCreationKnowledgePointContribution> contributions,
        CharacterCreationAttributesPreview attributes)
    {
        internal CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewPurchases(
            CharacterCreationAttributeSkillsPreview baseline,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases,
            CharacterCreationKarmaSkillsPolicy policy, CancellationToken cancellationToken)
        {
            if (purchases.Count == 0) return new(CharacterCreationFoundationOutcomes.Success, baseline, []);
            var blockers = new List<string>();
            var rows = baseline.Skills.ToDictionary(row => (row.Kind, row.SourceSkillId));
            var final = rows.ToDictionary(pair => pair.Key, pair => new CharacterCreationSkillAllocation(
                pair.Value.SourceSkillId, pair.Value.Kind, pair.Value.Rating,
                pair.Value.SpecializationOptionId, pair.Value.IsNativeLanguage));
            var costs = new Dictionary<(string Kind, string Id), (int Levels, int Cost, bool Specialization)>();
            try
            {
                foreach (var purchase in purchases)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var key = (purchase.Kind, purchase.SourceSkillId);
                    var source = (purchase.Kind == CharacterCreationSkillKinds.Active ? authority.ActiveSkills : authority.KnowledgeSkills)
                        .SingleOrDefault(row => row.SourceSkillId == purchase.SourceSkillId);
                    if (source is null || !CharacterCreationSkillsDigest.EqualsFixedTime(source.SourceNodeDigest, purchase.SourceNodeDigest))
                    { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.SourceConflict); continue; }
                    rows.TryGetValue(key, out var original);
                    int lower = original?.Rating ?? 0;
                    if (original?.IsNativeLanguage == true || purchase.Rating < lower
                        || purchase.Rating == lower && purchase.SpecializationOptionId is null
                        || purchase.SpecializationOptionId is not null && original?.SpecializationOptionId is not null)
                    { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid); continue; }
                    // Catalog membership alone does not fund a group. Compensation for
                    // paid group points remains a separate coupled purchase domain;
                    // never split or refund those points for an individual purchase.
                    if (purchase.Kind == CharacterCreationSkillKinds.Active && source.SkillGroup is not null
                        && policy.CompensateSkillGroupKarmaDifference
                        && baseline.SkillGroups.Any(group => group.PointCost > 0
                            && group.MemberSkillSourceIds.Contains(source.SourceSkillId, StringComparer.Ordinal)))
                    { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.GroupCompensationUnsupported); continue; }
                    int cost;
                    bool priced = purchase.Kind == CharacterCreationSkillKinds.Active
                        ? CharacterCreationSkillCostRules.TryActive(lower, purchase.Rating,
                            policy.KarmaNewActiveSkill, policy.KarmaImproveActiveSkill, out cost)
                        : CharacterCreationSkillCostRules.TryKnowledge(lower, purchase.Rating,
                            policy.KarmaNewKnowledgeSkill, policy.KarmaImproveKnowledgeSkill, out cost);
                    if (!priced) { blockers.Add(CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid); continue; }
                    bool specialization = purchase.SpecializationOptionId is not null;
                    if (specialization) cost = checked(cost + (purchase.Kind == CharacterCreationSkillKinds.Active
                        ? policy.KarmaSpecialization : policy.KarmaKnowledgeSpecialization));
                    costs.Add(key, (checked(purchase.Rating - lower), cost, specialization));
                    final[key] = new(purchase.SourceSkillId, purchase.Kind, purchase.Rating,
                        purchase.SpecializationOptionId ?? original?.SpecializationOptionId, false);
                }
                if (blockers.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
                // Reuse all current source/talent/movement/grant/cap/group/spec rules.
                // Only this temporary legality evaluation has an unbounded point pool;
                // the returned budgets and PointCost remain the original paid points.
                var evaluated = EvaluateAllocations(authority, prerequisite, int.MaxValue, selectedSkillGroupPoints,
                    attributes.Attributes.Single(row => row.AttributeId == "INT").UnaugmentedCurrent,
                    attributes.Attributes.Single(row => row.AttributeId == "LOG").UnaugmentedCurrent,
                    movement, final.Values.ToArray(), baseline.SkillGroups.Select(row =>
                        new CharacterCreationSkillGroupAllocation(row.GroupId, row.Rating)).ToArray(), blockers);
                if (blockers.Count != 0) return new(CharacterCreationFoundationOutcomes.Blocked, null, Normalize(blockers));
                var skills = evaluated.Skills.Select(row =>
                {
                    if (!costs.TryGetValue((row.Kind, row.SourceSkillId), out var paid)) return row;
                    rows.TryGetValue((row.Kind, row.SourceSkillId), out var original);
                    return row with { PointCost = original?.PointCost ?? 0, KarmaLevels = paid.Levels,
                        KarmaCost = paid.Cost, KarmaSpecialization = paid.Specialization,
                        SourceAnchorIds = row.SourceAnchorIds.Concat(policy.SourceAnchorIds)
                            .Concat(paid.Specialization ? new[] { (row.Kind == CharacterCreationSkillKinds.Active
                                ? authority.ActiveSkills : authority.KnowledgeSkills).Single(source => source.SourceSkillId == row.SourceSkillId)
                                .Specializations.Single(spec => spec.OptionId == row.SpecializationOptionId).SourceAnchorId } : [])
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() };
                }).ToArray();
                _ = skills.Sum(row => row.KarmaCost); // Reject overflow before exposing an exact quote.
                var result = baseline with { Skills = skills, ProposalDigest = string.Empty };
                result = result with { ProposalDigest = CharacterCreationSkillsDigest.Compute(result) };
                cancellationToken.ThrowIfCancellationRequested();
                return new(CharacterCreationFoundationOutcomes.Success, result, []);
            }
            catch (OverflowException)
            {
                return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillKarmaPurchaseBlockers.PurchaseInvalid);
            }
        }

        internal CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> Preview(
            IReadOnlyList<CharacterCreationSkillAllocation> skillAllocations,
            IReadOnlyList<CharacterCreationSkillGroupAllocation> groupAllocations,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(skillAllocations);
            ArgumentNullException.ThrowIfNull(groupAllocations);
            cancellationToken.ThrowIfCancellationRequested();

            // Do not rebind historical Skills to a pretend saved attribute draft.
            // Core retains the source, talent, movement, grant and overflow rules.
            var blockers = new List<string>();
            var projected = EvaluateAllocations(
                authority,
                prerequisite,
                selectedActiveSkillPoints,
                selectedSkillGroupPoints,
                attributes.Attributes.Single(item => item.AttributeId == "INT").UnaugmentedCurrent,
                attributes.Attributes.Single(item => item.AttributeId == "LOG").UnaugmentedCurrent,
                movement,
                skillAllocations.ToArray(),
                groupAllocations.ToArray(),
                blockers);
            cancellationToken.ThrowIfCancellationRequested();
            string[] normalized = Normalize(blockers);
            var preview = new CharacterCreationAttributeSkillsPreview(
                CharacterCreationAttributeSkillsPreviewSchemas.PreviewV1,
                attributesSnapshotDigest,
                skillsSnapshotDigest,
                attributes,
                binding,
                projected.Skills,
                projected.Groups,
                contributions,
                projected.ActiveBudget,
                projected.GroupBudget,
                projected.KnowledgeBudget,
                projected.KnowledgeOverflow,
                normalized,
                ProposalDigest: string.Empty);
            preview = preview with { ProposalDigest = CharacterCreationSkillsDigest.Compute(preview) };
            // Source anchors/group members and the attribute projection may refer
            // to the private snapshot. A returned proposal must not mutate it.
            preview = JsonSerializer.Deserialize<CharacterCreationAttributeSkillsPreview>(
                JsonSerializer.SerializeToUtf8Bytes(preview))!;
            return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked, preview, normalized);
        }
    }
}
