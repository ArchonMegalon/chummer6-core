using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterCreationNativeLoadoutProjection1432? NativeLoadout1432 { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? nativeLoadoutInvocation1432;
    private CharacterCreationSelectedEffectsConsumer? nativeLoadoutConsumer1432;
    private CharacterCreationCompleteAssessment? nativeLoadoutAuthority1432;

    internal void BindNativeLoadout1432(CharacterCreationSelectedEffectsConsumer consumer, CharacterCreationCompleteAssessment authority)
    {
        nativeLoadoutInvocation1432 = this;
        nativeLoadoutConsumer1432 = consumer;
        nativeLoadoutAuthority1432 = authority;
    }

    public bool TryGetExecutedNativeLoadout1432(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterCreationNativeLoadoutProjection1432? result)
    {
        result = null;
        if (!ReferenceEquals(nativeLoadoutInvocation1432, this) || !HasLifecycleResultV1
            || nativeLoadoutAuthority1432 is not { } authority || nativeLoadoutConsumer1432 is null
            || NativeLoadout1432 is not { Created: false } actual
            || !ReferenceEquals(nativeLoadoutConsumer1432.NativeLoadout1432(authority), actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.SettingsProfileId != profileId
            || actual.ProfileInputsDigest != profileInputsDigest || actual.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
