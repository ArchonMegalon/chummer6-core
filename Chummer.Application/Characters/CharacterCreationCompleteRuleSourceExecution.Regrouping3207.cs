using Chummer.Contracts.Characters;
using D3207 = Chummer.Application.Characters.CharacterCreationSourceOperationDisposition;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private readonly HashSet<string> inactiveRegroupingSettings3207 = new(StringComparer.Ordinal);
    internal IReadOnlySet<string> InactiveRegroupingSettings3207 => inactiveRegroupingSettings3207;

    // The existing bridge still requires the original current-source report and
    // exact selected-profile/leaf occurrence. This adds its private branch proof.
    private bool MatchesRegroupingResult3207(CharacterCreationCompleteAssessment authority, ProfileSettingCapability1203 capability,
        CharacterCreationRuleSourceScopeInvocation invocation,
        IReadOnlyList<CharacterCreationSourceOperation> operations, out bool inactive)
    {
        inactive = false;
        if (capability.Field != "allowskillregrouping") return true;
        if (!CharacterCreationCompleteProfileSettingsExecution.MatchesSkillGroupAuthority3207(invocation, authority)
            || !invocation.TryGetExecutedSkillGroupSetting3207(Inputs.EffectivePolicy!.SettingsProfileId,
                Inputs.EffectivePolicy.ProfileInputsDigest, capability.ProfileDigest, capability.Field, capability.LeafDigest,
                out _, out inactive, out _)) return false;
        var disposition = inactive ? D3207.NotSelected : D3207.Compiled;
        return operations.All(operation => operation.Disposition == disposition);
    }
}
