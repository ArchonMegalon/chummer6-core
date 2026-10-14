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
            new CompletionPreviewSession(attributesState.SnapshotDigest, skillsState, attributes), []);
    }

    internal sealed class CompletionPreviewSession(
        string attributesSnapshotDigest,
        CharacterCreationSkillsState skillsState,
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
                skillsState.Authority,
                skillsState.PrerequisiteDraft,
                skillsState.SelectedActiveSkillPoints,
                skillsState.SelectedSkillGroupPoints,
                attributes.Attributes.Single(item => item.AttributeId == "INT").UnaugmentedCurrent,
                attributes.Attributes.Single(item => item.AttributeId == "LOG").UnaugmentedCurrent,
                skillsState.MovementCapability,
                skillAllocations.ToArray(),
                groupAllocations.ToArray(),
                blockers);
            cancellationToken.ThrowIfCancellationRequested();
            string[] normalized = Normalize(blockers);
            var preview = new CharacterCreationAttributeSkillsPreview(
                CharacterCreationAttributeSkillsPreviewSchemas.PreviewV1,
                attributesSnapshotDigest,
                skillsState.SnapshotDigest,
                attributes,
                skillsState.Binding,
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
            // Source anchors/group members and the attribute projection may refer
            // to the private snapshot. A returned proposal must not mutate it.
            preview = JsonSerializer.Deserialize<CharacterCreationAttributeSkillsPreview>(
                JsonSerializer.SerializeToUtf8Bytes(preview))!;
            return new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked, preview, normalized);
        }
    }
}
