using System.Text.Json.Serialization;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    [JsonIgnore] public CharacterMatrixInitiativeDiceProjection? MatrixInitiative { get; internal init; }
    private CharacterCreationRuleSourceScopeInvocation? matrixInvocation;
    private CharacterCreationSelectedEffectsConsumer? matrixConsumer;
    private CharacterCreationCompleteAssessment? matrixAuthority;

    internal void BindMatrixInitiative(CharacterCreationSelectedEffectsConsumer consumer, CharacterCreationCompleteAssessment authority)
    {
        matrixInvocation = this;
        matrixConsumer = consumer;
        matrixAuthority = authority;
    }

    public bool TryGetExecutedMatrixInitiative(string profileId, string profileInputsDigest,
        string profileXmlDigest, out CharacterMatrixInitiativeDiceProjection? result)
    {
        result = null;
        if (!ReferenceEquals(matrixInvocation, this) || !HasLifecycleResultV1
            || matrixAuthority is not { } authority || matrixConsumer is null
            || MatrixInitiative is not { Created: false } actual
            || !ReferenceEquals(matrixConsumer.MatrixInitiative(authority), actual)
            || authority.PolicyDigest != PolicyDigest || authority.RequestDigest != RequestDigest
            || actual.GraphDigest != GraphDigest || actual.SettingsProfileId != profileId
            || actual.ProfileInputsDigest != profileInputsDigest || actual.ProfileXmlDigest != profileXmlDigest
            || CreationDraft718?.SettingsProfileId != profileId) return false;
        result = actual;
        return true;
    }
}
