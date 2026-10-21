namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    // Checked read of a private execution bound to this exact object. No setter,
    // serialized capability or factory accepts the returned values as authority.
    public bool TryGetExecutedProfileBrokenGroup940(string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, out bool usePoints, out bool noTarget,
        out IReadOnlyList<(string TargetId, int GroupRating, bool PointSpecialization)> checks,
        out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadBrokenGroup940(this, profileId,
            profileInputsDigest, profileXmlDigest, leafXmlDigest, out usePoints, out noTarget, out checks,
            out executionDigest);
}
