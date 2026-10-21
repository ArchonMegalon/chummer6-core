using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationLoadoutService
{
    private static CharacterCreationFoundationResult<CharacterCreationLoadoutState> PrepareComposite(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, ICharacterFileQueries? queries)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline, Loadout: not null } inputs)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, loaded.Blockers.Count != 0
                ? loaded.Blockers : [CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable]);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections, CharacterCreationCompositeEditStage.Loadout);
        if (reviewed.Value is not { Lifestyles: not null } review || reviewed.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, reviewed.Blockers);
        return new(CharacterCreationFoundationOutcomes.Success, new(inputs.Loadout, inputs.Lifestyles.Gear.Resources,
            review.Lifestyles.Gear.Resources.Budget) { CompositeSelections = baseline.Selections.Loadout }, []);
    }

    private static CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> PreviewComposite(
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, ICharacterFileQueries? queries,
        CharacterCreationLoadoutPreviewRequest request)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline, Loadout: not null } inputs)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, loaded.Blockers.Count != 0
                ? loaded.Blockers : [CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable]);
        if (request.Binding != inputs.Loadout.Binding || request.SnapshotDigest != inputs.Loadout.SnapshotDigest)
            return Reject<CharacterCreationLoadoutPreview>(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
        if (CharacterCreationLoadoutReceiptLedgerIntegrity.Same(baseline.Selections.Loadout, request.Selections))
            return Reject<CharacterCreationLoadoutPreview>(CharacterCreationGearBlockers.NoChange);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { Loadout = request.Selections }, CharacterCreationCompositeEditStage.Loadout);
        if (reviewed.Value is not { Loadout: not null, Lifestyles: not null } review || reviewed.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, reviewed.Blockers);
        var budget = review.Lifestyles.Gear.Resources.Budget;
        var preview = new CharacterCreationLoadoutPreview(request, review.Loadout, budget.TotalStartingNuyen,
            budget.KnownPurchaseCost, budget.RemainingNuyen, string.Empty)
        { CompositePurchase = CharacterCreationCompositePurchaseEdit.Binding(workspace, baseline, review) };
        preview = preview with { PreviewDigest = CharacterCreationLoadoutDraftRules.PreviewDigest(preview) };
        return new(CharacterCreationFoundationOutcomes.Success, Freeze(preview), []);
    }
}
