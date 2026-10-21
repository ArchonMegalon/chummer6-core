using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Detached diagnostics only. The result cannot register an invocation.
    public bool TryGetExecutedProfileQualityKarmaLimit947(string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, out CharacterCreationQualitiesCompletionEvaluation? evaluation,
        out bool noTarget, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947.TryRead947(this,
            profileId, profileInputsDigest, profileXmlDigest, leafXmlDigest, out evaluation, out noTarget, out executionDigest);
}
