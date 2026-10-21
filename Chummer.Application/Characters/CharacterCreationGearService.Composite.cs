using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationGearService
{
    private CharacterCreationGearResult<CharacterCreationGearState> LoadComposite(WorkspaceStoredDocument workspace)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _sourceData, _completionQueries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            return new(CharacterCreationGearOutcomes.Blocked, null, loaded.Blockers);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections, CharacterCreationCompositeEditStage.Gear);
        if (reviewed.Value is not { Lifestyles: not null } review || reviewed.Blockers.Count != 0)
            return new(CharacterCreationGearOutcomes.Blocked, null, reviewed.Blockers);
        var state = CreateState(CompositeContext(workspace, review)) with
        { CompositeSelections = baseline.Selections.Gear,
            CanEdit = _workspaceStore is ICharacterCreationCompositeStageAtomicCommitCapability { SupportsCompositeStageAtomicCommit: true } };
        state = state with { SnapshotDigest = CharacterCreationGearRules.ComputeStateDigest(state) };
        return new(CharacterCreationGearOutcomes.Available, state, []);
    }

    private AuthorityContext CompositeContext(WorkspaceStoredDocument workspace, CharacterCreationCompletionReview review)
    {
        var context = BuildContext(workspace);
        return context with
        {
            Budget = review.Lifestyles!.Gear.Budget,
            Blockers = context.Blockers.Where(row => row != CharacterCreationGearBlockers.ResourcesDraftRequired).ToArray()
        };
    }

    private PreviewEvaluation EvaluateCompositePreview(WorkspaceStoredDocument workspace, CharacterCreationGearPreviewRequest request)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _sourceData, _completionQueries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            return new(new(CharacterCreationGearOutcomes.Blocked, null, loaded.Blockers), workspace);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { Gear = request.Basket }, CharacterCreationCompositeEditStage.Gear);
        if (reviewed.Value is not { Lifestyles: not null } shared)
            return new(new(CharacterCreationGearOutcomes.Blocked, null, reviewed.Blockers), workspace);
        var current = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections, CharacterCreationCompositeEditStage.Gear);
        if (current.Value is not { Lifestyles: not null } before || current.Blockers.Count != 0)
            return new(new(CharacterCreationGearOutcomes.Blocked, null, current.Blockers), workspace);
        return EvaluatePreview(CompositeContext(workspace, before), workspace, request, shared, baseline);
    }
}
