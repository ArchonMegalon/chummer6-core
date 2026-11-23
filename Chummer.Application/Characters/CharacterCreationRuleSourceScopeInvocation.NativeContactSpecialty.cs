using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterCreationNativeContactSpecialtyProjection? NativeContactSpecialty { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? nativeContactSpecialtyInvocation;
    private CharacterCreationSelectedEffectsConsumer? nativeContactSpecialtyConsumer;
    private CharacterCreationCompleteAssessment? nativeContactSpecialtyAuthority;
    private string? nativeContactSpecialtyDigest;

    internal void BindNativeContactSpecialty(CharacterCreationSelectedEffectsConsumer consumer, CharacterCreationCompleteAssessment authority)
    {
        nativeContactSpecialtyInvocation = this;
        nativeContactSpecialtyConsumer = consumer;
        nativeContactSpecialtyAuthority = authority;
        nativeContactSpecialtyDigest = CharacterCreationFinalizationDigest.Compute(NativeContactSpecialty);
    }

    public bool TryGetExecutedNativeContactSpecialty(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterCreationNativeContactSpecialtyProjection? result)
    {
        result = null;
        if (!ReferenceEquals(nativeContactSpecialtyInvocation, this) || !HasLifecycleResultV1
            || nativeContactSpecialtyAuthority is not { } authority || nativeContactSpecialtyConsumer is null
            || NativeContactSpecialty is not { Created: false } actual
            || !ReferenceEquals(nativeContactSpecialtyConsumer.NativeContactSpecialty(authority), actual)
            || nativeContactSpecialtyDigest != CharacterCreationFinalizationDigest.Compute(actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.Policy.SettingsProfileId != profileId
            || actual.Policy.ProfileInputsDigest != profileInputsDigest || actual.Policy.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
