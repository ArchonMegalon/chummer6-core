namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedProfileMagic3200(string profileId, string profileInputsDigest, string profileXmlDigest,
        string field, string leafXmlDigest, out string? value, out bool inactive, out string? talentKind, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadMagic3200(this, profileId, profileInputsDigest,
            profileXmlDigest, field, leafXmlDigest, out value, out inactive, out talentKind, out executionDigest);
}
