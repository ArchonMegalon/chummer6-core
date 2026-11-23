using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterAstralInitiativeDiceProjection? AstralInitiative1249 { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? astralInvocation1249;
    private CharacterCreationSelectedEffectsConsumer? astralConsumer1249;
    private CharacterCreationCompleteAssessment? astralAuthority1249;

    internal void BindAstralInitiative1249(CharacterCreationSelectedEffectsConsumer consumer,
        CharacterCreationCompleteAssessment authority)
    {
        astralInvocation1249 = this;
        astralConsumer1249 = consumer;
        astralAuthority1249 = authority;
    }

    // The version is carried by the private compiler, never by a public diagnostic.
    [JsonIgnore] public bool HasLifecycleResultV1 => ReferenceEquals(astralInvocation1249, this)
        && astralConsumer1249 is { } consumer
        && consumer.LifecycleResultSchema1272 == CharacterCreationCompletionReviewSchemas.LifecycleResultV1
        && astralAuthority1249 is { } authority && consumer.DigestFor(authority) is not null
        && authority.PolicyDigest == PolicyDigest && authority.RequestDigest == RequestDigest;

    // Public diagnostics cannot register a producer. A copied invocation loses
    // this private consumer, while mutation of the original result fails equality.
    public bool TryGetExecutedAstralInitiative1249(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterAstralInitiativeDiceProjection? result)
    {
        result = null;
        if (!HasLifecycleResultV1 || astralAuthority1249 is not { } authority || astralConsumer1249 is null
            || AstralInitiative1249 is not { Created: false } actual
            || !ReferenceEquals(astralConsumer1249.AstralInitiative1249(authority), actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.SettingsProfileId != profileId
            || actual.ProfileInputsDigest != profileInputsDigest || actual.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
