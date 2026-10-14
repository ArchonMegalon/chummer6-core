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
