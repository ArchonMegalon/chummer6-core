using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationResourcesService
{
    private CharacterCreationResourcesResult<CharacterCreationResourcesState> LoadComposite(WorkspaceStoredDocument workspace)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _sourceData, _completionQueries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            return new(CharacterCreationResourcesOutcomes.Blocked, null, loaded.Blockers);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections, CharacterCreationCompositeEditStage.Resources);
        if (reviewed.Value is not { Lifestyles: { } lifestyles } review || reviewed.Blockers.Count != 0)
            return new(CharacterCreationResourcesOutcomes.Blocked, null, reviewed.Blockers);
        var context = CompositeContext(workspace, inputs, review);
        var state = new CharacterCreationResourcesState(CharacterCreationResourcesSchemas.StateV1,
            CharacterCreationWizardStepIds.Resources, context.Binding, context.Authority, context.Prerequisite,
            context.PendingDraft, context.Options, lifestyles.Gear.Resources.Budget, [],
            _workspaceStore is ICharacterCreationCompositeStageAtomicCommitCapability { SupportsCompositeStageAtomicCommit: true }, string.Empty);
        state = state with { SnapshotDigest = CharacterCreationResourcesRules.ComputeStateDigest(state) };
        return new(CharacterCreationResourcesOutcomes.Available, state, []);
    }

    private AuthorityContext CompositeContext(WorkspaceStoredDocument workspace,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review)
    {
        var context = BuildContext(workspace);
        var resources = inputs.Lifestyles.Gear.Resources;
        var budget = review.Lifestyles!.Gear.Resources.Budget;
        int availableKarma = checked((review.Balances.RemainingCreationKarma ?? 0) + review.Request.Selections.TotalKarmaInvestment);
        return context with
        {
            AvailableKarma = availableKarma,
            Options = BuildOptions(resources.Authority, resources.PriorityOption,
                availableKarma, resources.PrerequisiteDraft.CreationKarmaTotal),
            Budget = budget, KnownPurchaseCost = budget.KnownPurchaseCost, PurchaseAuthorityExact = budget.IsExact
        };
    }

    private PreviewEvaluation EvaluateCompositePreview(WorkspaceStoredDocument workspace,
        CharacterCreationResourcesPreviewRequest request)
    {
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, _sourceData, _completionQueries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs)
            return new(new(CharacterCreationResourcesOutcomes.Blocked, null, loaded.Blockers), workspace);
        var option = FindAllocationOption(inputs.Lifestyles.Gear.Resources.Authority,
            inputs.Lifestyles.Gear.Resources.PriorityOption, int.MaxValue, request.OptionId);
        if (option is null)
            return new(Blocked<CharacterCreationResourcesPreview>(CharacterCreationResourcesOutcomes.Invalid,
                CharacterCreationResourcesBlockers.InvalidOption), workspace);
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { TotalKarmaInvestment = option.KarmaInvestment }, CharacterCreationCompositeEditStage.Resources);
        if (reviewed.Value is not { Lifestyles: not null } shared)
            return new(new(CharacterCreationResourcesOutcomes.Blocked, null, reviewed.Blockers), workspace);
        var current = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections, CharacterCreationCompositeEditStage.Resources);
        if (current.Value is not { Lifestyles: not null } before || current.Blockers.Count != 0)
            return new(new(CharacterCreationResourcesOutcomes.Blocked, null, current.Blockers), workspace);
        var context = CompositeContext(workspace, inputs, before);
        var evaluated = EvaluatePreview(context, request, effectiveKarmaInvestment: baseline.Selections.TotalKarmaInvestment);
        if (evaluated.Result.Value is not { } preview) return evaluated;
        var blockers = preview.Blockers.Where(row => row != CharacterCreationResourcesBlockers.NoChange).Concat(reviewed.Blockers).ToList();
        if (option.KarmaInvestment == baseline.Selections.TotalKarmaInvestment) blockers.Add(CharacterCreationResourcesBlockers.NoChange);
        var normalized = Normalize(blockers);
        preview = preview with { Blockers = normalized, CanConfirm = normalized.Length == 0, BudgetAfter = shared.Lifestyles!.Gear.Resources.Budget,
            CompositeReviewDigest = shared.ReviewDigest };
        preview = preview with { PreviewDigest = CharacterCreationResourcesRules.ComputePreviewDigest(preview) };
        return new(new(normalized.Length == 0 ? CharacterCreationResourcesOutcomes.Available : CharacterCreationResourcesOutcomes.Blocked,
            preview, normalized), workspace);
    }
}
