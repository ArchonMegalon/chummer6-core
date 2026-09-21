using Chummer.Contracts.LifeModules;

namespace Chummer.Application.LifeModules;

public sealed partial class LifeModuleOriginDossierService
{
    internal LifeModuleOriginDossierResult<LifeModuleDecisionInputResolution> ResolveChoiceInputs(
        OriginStoryArcSeed current, string choiceId, IReadOnlyDictionary<string, string> values)
    {
        if (!TryValidateProjection(current) || _authority is not ILifeModuleDecisionInputAuthority resolver
            || !LifeModuleDecisionInputIntegrity.TryNormalize(values, out var normalized))
            return Blocked<LifeModuleDecisionInputResolution>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IllegalChoice);
        var choice = current.CurrentTurn.LegalChoices.SingleOrDefault(item => item.ChoiceId == choiceId);
        if (choice?.FollowUps is not { Count: > 0 })
            return Blocked<LifeModuleDecisionInputResolution>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.IllegalChoice);
        var result = resolver.ResolveInputs(new(current.CurrentTurn.WorkspaceId,
            current.CurrentTurn.WorkspaceRevision, choiceId, current.CurrentTurn.DecisionDigest,
            choice.DecisionCommandDigest, normalized));
        if (!IsAuthoritySuccess(result.Outcome) || result.Value is null)
            return FromAuthority<LifeModuleDecisionInputResolution, LifeModuleDecisionInputResolution>(result);
        if (!MatchesInputResolution(current, choice, result.Value))
            return Blocked<LifeModuleDecisionInputResolution>(LifeModuleOriginDossierOutcomes.Invalid,
                LifeModuleOriginDossierBlockers.AuthorityInvalid);
        return new(LifeModuleOriginDossierOutcomes.Success, result.Value, []);
    }

    internal static bool MatchesInputResolution(OriginStoryArcSeed current,
        LifeModuleNarrativeChoiceSeed choice, LifeModuleDecisionInputResolution? resolution)
        => choice.FollowUps is { Count: > 0 }
            ? LifeModuleDecisionInputIntegrity.Matches(resolution, current.CurrentTurn.WorkspaceId,
                current.CurrentTurn.WorkspaceRevision, choice.ChoiceId, current.CurrentTurn.DecisionDigest,
                choice.DecisionCommandDigest)
              && resolution!.Values.Keys.All(key => choice.FollowUps.Any(prompt => prompt.PromptId == key))
              && choice.FollowUps.All(prompt =>
                  resolution.Values.TryGetValue(prompt.PromptId, out var value) && !string.IsNullOrWhiteSpace(value)
                      ? prompt.Options.Count == 0 || prompt.Options.Any(option => option.IsEnabled && option.SourceValue == value)
                      : !prompt.IsRequired)
            : resolution is null;
}
