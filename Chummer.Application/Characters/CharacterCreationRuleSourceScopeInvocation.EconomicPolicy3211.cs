using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedEconomicSetting3211(CharacterCreationObservedSetting key,
        CharacterCreationSettingConsumer consumer, string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, object sourceAuthority, out string? result, out bool inactive)
        => CharacterCreationCompleteRuleSourceExecution.TryReadEconomicPolicy3211(this, key, consumer,
            profileId, profileInputsDigest, profileXmlDigest, leafXmlDigest, sourceAuthority, out result, out inactive);
}
