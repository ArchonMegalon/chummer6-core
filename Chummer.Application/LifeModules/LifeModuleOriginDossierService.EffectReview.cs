using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class LifeModuleOriginDossierService
{
    internal LifeModuleOriginDossierResult<LifeModuleEffectReview> ResolveEffectReview(
        OriginStoryArcSeed current, string choiceId, LifeModuleDecisionInputResolution? inputs)
    {
        if (_authority is not ILifeModuleDecisionEffectReviewAuthority reviewer)
            return new(LifeModuleOriginDossierOutcomes.Missing, null, []);
        if (!TryValidateProjection(current))
            return Blocked<LifeModuleEffectReview>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.ProjectionInvalid);
        var choice = current.CurrentTurn.LegalChoices.SingleOrDefault(item => item.ChoiceId == choiceId);
        if (choice is null || !MatchesInputResolution(current, choice, inputs))
            return Blocked<LifeModuleEffectReview>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IllegalChoice);
        var request = new LifeModuleDecisionInputRequest(current.CurrentTurn.WorkspaceId,
            current.CurrentTurn.WorkspaceRevision, choiceId, current.CurrentTurn.DecisionDigest,
            choice.DecisionCommandDigest, inputs?.Values ?? new Dictionary<string, string>(StringComparer.Ordinal));
        var reviewed = reviewer.ReviewEffects(request);
        if (!IsAuthoritySuccess(reviewed.Outcome) || reviewed.Value is not { } review)
            return FromAuthority<LifeModuleEffectReview, LifeModuleEffectReview>(reviewed);
        if (!LifeModuleEffectReviewIntegrity.IsValid(review)
            || !Chummer.Application.Characters.CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(request, review.Request))
            return Blocked<LifeModuleEffectReview>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        return new(LifeModuleOriginDossierOutcomes.Success, review, []);
    }
}
