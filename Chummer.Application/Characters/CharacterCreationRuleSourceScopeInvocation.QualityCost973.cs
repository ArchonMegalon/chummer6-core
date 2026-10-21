using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Detached diagnostics; no public registration path.
    public bool TryGetExecutedProfileQualityCost973(string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, out CharacterCreationQualitiesCompletionEvaluation? evaluation,
        out CharacterCreationQualityCostPolicy? costPolicy, out bool noTarget, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947.TryReadCost973(this,
            profileId, profileInputsDigest, profileXmlDigest, leafXmlDigest,
            out evaluation, out costPolicy, out noTarget, out executionDigest);
}
