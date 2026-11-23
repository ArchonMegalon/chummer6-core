using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterCreationNativeSettingsProjection? NativeSettings1420 { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? nativeSettingsInvocation1420;
    private CharacterCreationSelectedEffectsConsumer? nativeSettingsConsumer1420;
    private CharacterCreationCompleteAssessment? nativeSettingsAuthority1420;

    internal void BindNativeSettings1420(CharacterCreationSelectedEffectsConsumer consumer, CharacterCreationCompleteAssessment authority)
    {
        nativeSettingsInvocation1420 = this;
        nativeSettingsConsumer1420 = consumer;
        nativeSettingsAuthority1420 = authority;
    }

    public bool TryGetExecutedNativeSettings1420(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterCreationNativeSettingsProjection? result)
    {
        result = null;
        if (!ReferenceEquals(nativeSettingsInvocation1420, this) || !HasLifecycleResultV1
            || nativeSettingsAuthority1420 is not { } authority || nativeSettingsConsumer1420 is null
            || NativeSettings1420 is not { Created: false } actual
            || !ReferenceEquals(nativeSettingsConsumer1420.NativeSettings1420(authority), actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.SettingsProfileId != profileId
            || actual.ProfileInputsDigest != profileInputsDigest || actual.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
