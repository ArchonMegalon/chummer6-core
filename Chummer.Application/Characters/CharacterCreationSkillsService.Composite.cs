using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    private CharacterCreationFoundationResult<CharacterCreationSkillsState> LoadCompositeSkills(WorkspaceStoredDocument workspace)
    {
        var loaded = LoadCompletionInputs(new(workspace.Id));
        if (loaded.Value is not { CompositeBaseline: { } composite } inputs)
            return new(loaded.Outcome, null, loaded.Blockers);
        var choices = composite.Selections;
        var evaluated = PreviewCompositeSkillPurchases(inputs, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases);
        if (evaluated.Value is not { } projection || evaluated.Blockers.Count != 0)
            return new(evaluated.Outcome, null, evaluated.Blockers);
        var pending = workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft;
        if (pending?.BaseContentRevision < composite.ContentRevision) pending = null;
        bool canSave = _store is IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true };
        var state = CompositeSkillsState(inputs, pending, projection, canSave);
        return new(CharacterCreationFoundationOutcomes.Success, state, state.Blockers);
    }

    private static CharacterCreationSkillsState CompositeSkillsState(
        CharacterCreationSkillsCompletionInputs inputs, CharacterCreationSkillsDraft? pending,
        CharacterCreationAttributeSkillsPreview projection, bool canSave)
    {
        var state = new CharacterCreationSkillsState(CharacterCreationSkillsSchemas.SnapshotV1,
            inputs.Binding, inputs.Authority, inputs.PrerequisiteDraft, inputs.Attributes.PendingDraft,
            pending, projection.Skills, projection.SkillGroups, inputs.KnowledgePointContributions,
            projection.ActiveSkillPointBudget, projection.SkillGroupPointBudget, projection.KnowledgeSkillPointBudget,
            inputs.Attributes.Attributes.Single(row => row.AttributeId == "INT").UnaugmentedCurrent,
            inputs.Attributes.Attributes.Single(row => row.AttributeId == "LOG").UnaugmentedCurrent,
            canSave ? [] : [CharacterCreationSkillsBlockers.PersistenceAuthorityRequired], canSave, string.Empty)
        {
            CompositeInputs = inputs, SelectedActiveSkillPoints = inputs.SelectedActiveSkillPoints,
            SelectedSkillGroupPoints = inputs.SelectedSkillGroupPoints, MovementCapability = inputs.MovementCapability
        };
        return state with { SnapshotDigest = CharacterCreationSkillsDigest.Compute(state) };
    }

    // The ordinary receipt remains a real point-funded edit. Paid ranks are never
    // copied back from effective rows into Allocations; the composite retains them.
    private static (CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Result,
        CharacterCreationSkillsDraft? Draft) EvaluateCompositeSkills(
        CharacterCreationSkillsState state, CharacterCreationSkillsPreviewRequest request,
        List<string> blockers, CharacterCreationSkillsDraft? persistedDraft)
    {
        var inputs = state.CompositeInputs!;
        var choices = inputs.CompositeBaseline!.Selections;
        var projected = PreviewCompositeSkillPurchases(inputs, choices.AttributeAllocations,
            request.Allocations, request.GroupAllocations, choices.SkillKarmaPurchases);
        if (projected.Value is not { } paid || projected.Blockers.Count != 0)
            return (new(projected.Outcome, null, Normalize(blockers.Concat(projected.Blockers))), null);
        // An ordinary point edit cannot spend additional shared Karma until the
        // other stages participate in full source-admitted rebasing. Reducing a
        // paid target's cost is safe; fresh Finish independently recalculates it.
        if (paid.Skills.Sum(row => row.KarmaCost) > state.Skills.Sum(row => row.KarmaCost))
            blockers.Add(CharacterCreationCompositeBaselineReader.RebaseRequired);
        var point = EvaluateAllocations(state.Authority, state.PrerequisiteDraft,
            state.SelectedActiveSkillPoints, state.SelectedSkillGroupPoints,
            inputs.Attributes.Attributes, state.MovementCapability,
            request.Allocations, request.GroupAllocations, blockers);
        var originalBlockers = new List<string>();
        var original = EvaluateAllocations(state.Authority, state.PrerequisiteDraft,
            state.SelectedActiveSkillPoints, state.SelectedSkillGroupPoints,
            inputs.Attributes.Attributes, state.MovementCapability,
            choices.SkillAllocations, choices.GroupAllocations, originalBlockers);
        if (originalBlockers.Count != 0)
            blockers.Add(CharacterCreationSkillsBlockers.DraftInvalid);
        if (SameComposite(point.Skills, original.Skills) && SameComposite(point.Groups, original.Groups))
            blockers.Add(CharacterCreationSkillsBlockers.DraftDuplicate);
        if (persistedDraft?.DraftRevision == long.MaxValue)
            blockers.Add(CharacterCreationSkillsBlockers.DraftConflict);
        var draft = blockers.Count == 0 ? BuildDraft(state, point, persistedDraft) : null;
        var normalized = Normalize(blockers);
        var preview = new CharacterCreationSkillsPreview(CharacterCreationSkillsSchemas.PreviewV1,
            state.Binding, paid.Skills, paid.SkillGroups, state.KnowledgePointContributions,
            paid.ActiveSkillPointBudget, paid.SkillGroupPointBudget, paid.KnowledgeSkillPointBudget,
            point.KnowledgeOverflow, normalized, true, draft is not null && normalized.Length == 0, string.Empty);
        preview = preview with { PreviewDigest = CharacterCreationSkillsDigest.Compute(preview) };
        return (new(normalized.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, preview, normalized), draft);
    }

    internal static bool ValidateCompositePending(WorkspaceStoredDocument workspace,
        CharacterCreationSkillsCompletionInputs inputs)
    {
        if (inputs.CompositeBaseline is not { } composite) return false;
        var pending = workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft;
        // Older ordinary history was fully replayed as the composite's origin.
        if (pending is null || pending.BaseContentRevision < composite.ContentRevision) return true;
        if (inputs.Attributes.PendingDraft is not { } attributes
            || !CharacterCreationSkillsDraftIntegrity.IsStructurallyValidPending(pending,
                workspace.Id, workspace.ContentRevision, inputs.Binding.RawCharacterXmlDigest,
                inputs.PrerequisiteDraft, attributes, inputs.Authority, inputs.Binding.ContributionInputsDigest))
            return false;
        var blockers = new List<string>();
        var point = EvaluateAllocations(inputs.Authority, inputs.PrerequisiteDraft,
            inputs.SelectedActiveSkillPoints, inputs.SelectedSkillGroupPoints,
            inputs.Attributes.Attributes,
            inputs.MovementCapability, pending.Allocations, pending.GroupAllocations, blockers);
        if (blockers.Count != 0) return false;
        // Build using only the freshly evaluated point projection, then compare
        // the complete logical draft including authority and upstream bindings.
        var pointState = new CharacterCreationSkillsState(CharacterCreationSkillsSchemas.SnapshotV1,
            inputs.Binding, inputs.Authority, inputs.PrerequisiteDraft, attributes, pending,
            point.Skills, point.Groups, inputs.KnowledgePointContributions,
            point.ActiveBudget, point.GroupBudget, point.KnowledgeBudget,
            inputs.Attributes.Attributes.Single(row => row.AttributeId == "INT").UnaugmentedCurrent,
            inputs.Attributes.Attributes.Single(row => row.AttributeId == "LOG").UnaugmentedCurrent,
            [], false, string.Empty)
        {
            SelectedActiveSkillPoints = inputs.SelectedActiveSkillPoints,
            SelectedSkillGroupPoints = inputs.SelectedSkillGroupPoints,
            MovementCapability = inputs.MovementCapability
        };
        return CharacterCreationSkillsDraftIntegrity.HasSameLogicalPayload(
            pending, BuildDraft(pointState, point, pending));
    }

    private static bool IsValidCompositeSkillsState(CharacterCreationSkillsState state)
    {
        if (state.CompositeInputs is not { CompositeBaseline: { } composite } inputs
            || !IsValidCompletionInputs(inputs) || state.Binding != inputs.Binding
            || !SameComposite(state.Authority, inputs.Authority)
            || !SameComposite(state.PrerequisiteDraft, inputs.PrerequisiteDraft)
            || !SameComposite(state.AttributesDraft, inputs.Attributes.PendingDraft))
            return false;
        var choices = composite.Selections;
        var result = PreviewCompositeSkillPurchases(inputs, choices.AttributeAllocations,
            choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases);
        if (result.Value is not { } projected || result.Blockers.Count != 0) return false;
        var expected = CompositeSkillsState(inputs, state.PendingDraft, projected, state.CanEdit);
        return SameComposite(state, expected);
    }

    private static bool SameComposite<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
