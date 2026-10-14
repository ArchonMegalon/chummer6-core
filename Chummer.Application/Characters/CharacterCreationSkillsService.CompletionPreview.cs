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
        ArgumentNullException.ThrowIfNull(attributesState);
        ArgumentNullException.ThrowIfNull(skillsState);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(skillAllocations);
        ArgumentNullException.ThrowIfNull(groupAllocations);
        cancellationToken.ThrowIfCancellationRequested();

        // IReadOnlyList may wrap caller-owned arrays. All validation and evaluation
        // below use the same private copies, including authority/source anchors.
        attributesState = JsonSerializer.Deserialize<CharacterCreationAttributesState>(
            JsonSerializer.SerializeToUtf8Bytes(attributesState))!;
        skillsState = JsonSerializer.Deserialize<CharacterCreationSkillsState>(
            JsonSerializer.SerializeToUtf8Bytes(skillsState))!;
        attributeAllocations = attributeAllocations.ToArray();
        skillAllocations = skillAllocations.ToArray();
        groupAllocations = groupAllocations.ToArray();

        var attributesSession = CharacterCreationAttributesService.CreateDraftSession(attributesState);
        if (attributesSession is null)
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationAttributesBlockers.AuthorityUnavailable);
        if (CreateDraftSession(skillsState) is null)
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
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
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationAttributeSkillsPreviewSchemas.SnapshotConflict);

        cancellationToken.ThrowIfCancellationRequested();
        var attributesResult = attributesSession.Preview(attributeAllocations);
        if (attributesResult.Value is not { } attributes
            || attributes.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, attributesResult.Blockers);

        // Do not rebind the historical Skills state to a pretend saved attribute
        // draft. Only the pure evaluation inputs change. Core retains the talent,
        // movement, source contribution, group and overflow rules unchanged.
        var blockers = new List<string>();
        var projected = EvaluateAllocations(
            skillsState.Authority,
            skillsState.PrerequisiteDraft,
            skillsState.SelectedActiveSkillPoints,
            skillsState.SelectedSkillGroupPoints,
            attributes.Attributes.Single(item => item.AttributeId == "INT").UnaugmentedCurrent,
            attributes.Attributes.Single(item => item.AttributeId == "LOG").UnaugmentedCurrent,
            skillsState.MovementCapability,
            skillAllocations,
            groupAllocations,
            blockers);
        cancellationToken.ThrowIfCancellationRequested();
        string[] normalized = Normalize(blockers);
        var preview = new CharacterCreationAttributeSkillsPreview(
            CharacterCreationAttributeSkillsPreviewSchemas.PreviewV1,
            attributesState.SnapshotDigest,
            skillsState.SnapshotDigest,
            attributes,
            skillsBinding,
            projected.Skills,
            projected.Groups,
            skillsState.KnowledgePointContributions,
            projected.ActiveBudget,
            projected.GroupBudget,
            projected.KnowledgeBudget,
            projected.KnowledgeOverflow,
            normalized,
            ProposalDigest: string.Empty);
        preview = preview with { ProposalDigest = CharacterCreationSkillsDigest.Compute(preview) };
        return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, preview, normalized);
    }
}
