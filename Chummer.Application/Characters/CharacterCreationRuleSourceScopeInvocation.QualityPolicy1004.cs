using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Detached diagnostics only; no registration or transferable execution authority.
    public bool TryGetExecutedProfileQualityPolicy1004(string profileId, string profileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest,
        out CharacterCreationQualitiesCompletionEvaluation? evaluation,
        out CharacterCreationQualityCostPolicy? costPolicy, out bool value, out bool noTarget, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947.TryReadQualityPolicy1004(
            this, profileId, profileInputsDigest, profileXmlDigest, field, leafXmlDigest,
            out evaluation, out costPolicy, out value, out noTarget, out executionDigest);
}
