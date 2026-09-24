using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class CharacterCreationFoundationLifeModuleDecisionAuthority
{
    public LifeModuleDecisionAuthorityResult<LifeModuleEffectReview> ReviewEffects(LifeModuleDecisionInputRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_foundation is not CharacterCreationFoundationService foundation
            || !TryWorkspaceId(request.WorkspaceId, out var id)
            || !LifeModuleDecisionInputIntegrity.TryNormalize(request.Values, out var values))
            return Invalid<LifeModuleEffectReview>();
        var loaded = Load(request.WorkspaceId);
        if (loaded.Value is not { IsTerminal: false } step || step.WorkspaceRevision != request.WorkspaceRevision
            || step.DecisionDigest != request.DecisionDigest
            || !step.LegalChoices.Any(item => item.ChoiceId == request.ChoiceId
                && item.DecisionCommandDigest == request.DecisionCommandDigest))
            return Blocked<LifeModuleEffectReview>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.DecisionStale);
        var workspace = _workspaceStore.Get(id).Value;
        if (workspace is null || workspace.ContentRevision != request.WorkspaceRevision)
            return Blocked<LifeModuleEffectReview>(LifeModuleOriginDossierOutcomes.Conflict,
                LifeModuleOriginDossierBlockers.WorkspaceStale);
        CharacterCreationFoundationResult<CharacterCreationFoundationEffectCompilation> compiled;
        string previewDigest;
        if (workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances is not { Count: > 0 })
        {
            var initial = foundation.Load(new(id));
            if (initial.Value is not { } state)
                return FromFoundation<CharacterCreationFoundationState, LifeModuleEffectReview>(initial);
            var candidate = BuildCandidates(state).SingleOrDefault(item => item.Choice.ChoiceId == request.ChoiceId
                && item.Choice.DecisionCommandDigest == request.DecisionCommandDigest);
            if (candidate is null) return Invalid<LifeModuleEffectReview>();
            var previewRequest = new CharacterCreationFoundationPreviewRequest(state.Binding,
                candidate.FoundationPreview.RequestedMetatype, candidate.Selection, values);
            var projected = foundation.Preview(previewRequest);
            if (projected.Value is not { CanConfirm: true, CanApply: true } preview || projected.Blockers.Count != 0)
                return FromFoundation<CharacterCreationFoundationPreview, LifeModuleEffectReview>(projected);
            previewDigest = preview.PreviewDigest;
            compiled = foundation.ReviewFoundationEffects(previewRequest, previewDigest);
        }
        else
        {
            var loadedJourney = foundation.ProjectJourney(workspace);
            if (loadedJourney.Value is not { } state)
                return FromFoundation<CharacterCreationLifeModuleJourneyState, LifeModuleEffectReview>(loadedJourney);
            if (request.ChoiceId == FinishSelectionChoiceId)
            {
                var finish = CharacterCreationFoundationService.ProjectFinishSelection(workspace, state);
                if (values.Count != 0 || !finish.CanConfirm
                    || FinishChoice(finish, step.Locale).DecisionCommandDigest != request.DecisionCommandDigest)
                    return Invalid<LifeModuleEffectReview>();
                return Success(LifeModuleEffectReviewIntegrity.Seal(new(request with { Values = values },
                    finish.PreviewDigest, string.Empty, string.Empty, [], string.Empty)));
            }
            var candidate = BuildModuleCandidates(foundation, workspace, state).SingleOrDefault(item =>
                item.Choice.ChoiceId == request.ChoiceId && item.Choice.DecisionCommandDigest == request.DecisionCommandDigest);
            if (candidate is null) return Invalid<LifeModuleEffectReview>();
            var previewRequest = candidate.Preview.Request with { FollowUpValues = values };
            var projected = foundation.ProjectModule(workspace, previewRequest, state);
            if (projected.Value is not { CanConfirm: true } preview || projected.Blockers.Count != 0)
                return FromFoundation<CharacterCreationLifeModulePreview, LifeModuleEffectReview>(projected);
            previewDigest = preview.PreviewDigest;
            compiled = foundation.ReviewModuleEffects(previewRequest, previewDigest);
        }
        if (compiled.Value is not { } compilation || compiled.Blockers.Count != 0)
            return FromFoundation<CharacterCreationFoundationEffectCompilation, LifeModuleEffectReview>(compiled);
        return Success(LifeModuleEffectReviewIntegrity.Seal(new(request with { Values = values }, previewDigest,
            compilation.CompilerRuntimeDigest, compilation.CompilationDigest,
            CharacterCreationFoundationEffectCompiler.ReviewContributions(compilation), string.Empty)));
    }
}
