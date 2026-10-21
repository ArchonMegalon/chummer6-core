using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // A read-only view of the private canonical execution, never a public receipt factory.
    public bool TryGetExecutedAttributeHouseRule993(string profileId, string profileInputsDigest,
        string profileXmlDigest, string field, string leafXmlDigest, out bool value, out bool noPaidTargets,
        out CharacterCreationAttributesPreview? result, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadAttributeHouseRule993(this, profileId,
            profileInputsDigest, profileXmlDigest, field, leafXmlDigest, out value, out noPaidTargets, out result, out executionDigest);
}
