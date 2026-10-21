namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Read-only access to an in-process canonical execution; no serialized receipt can recreate it.
    public bool TryGetExecutedProfileSkillCost972(string profileId, string profileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest, out int value, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadSkillCost972(this, profileId,
            profileInputsDigest, profileXmlDigest, field, leafXmlDigest, out value, out executionDigest);
}
