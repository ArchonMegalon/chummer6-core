using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterCreationNativeMagicProjection1430? NativeMagic1430 { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? nativeMagicInvocation1430;
    private CharacterCreationSelectedEffectsConsumer? nativeMagicConsumer1430;
    private CharacterCreationCompleteAssessment? nativeMagicAuthority1430;

    internal void BindNativeMagic1430(CharacterCreationSelectedEffectsConsumer consumer, CharacterCreationCompleteAssessment authority)
    {
        nativeMagicInvocation1430 = this;
        nativeMagicConsumer1430 = consumer;
        nativeMagicAuthority1430 = authority;
    }

    public bool TryGetExecutedNativeMagic1430(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterCreationNativeMagicProjection1430? result)
    {
        result = null;
        if (!ReferenceEquals(nativeMagicInvocation1430, this) || !HasLifecycleResultV1
            || nativeMagicAuthority1430 is not { } authority || nativeMagicConsumer1430 is null
            || NativeMagic1430 is not { Created: false } actual
            || !ReferenceEquals(nativeMagicConsumer1430.NativeMagic1430(authority), actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.Policy.SettingsProfileId != profileId
            || actual.Policy.ProfileInputsDigest != profileInputsDigest || actual.Policy.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
