using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Validation of data only. Callers must separately verify the original execution
// object with its retained resolver; a copied or deserialized result is no proof.
public static class CharacterCreationEssenceSettingEvidenceRules
{
    public static bool Matches(XElement profile, CharacterCreationEssenceExecution1222 execution,
        out CharacterCreationEssenceSettingEvidence? evidence)
    {
        evidence = null;
        if (!execution.Result.Valid || execution.SettingsEvidence is not { } actual
            || !CharacterCreationEssenceSettingDefinitions.TryRead(profile, out var policy)
            || policy!.Settings.Count == 0 || policy.ProfileXmlDigest != execution.ProfileXmlDigest
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, actual.Policy)
            || actual.Settings.Count != policy.Settings.Count) return false;
        string resultDigest = CharacterCreationFinalizationDigest.Compute(execution.Result);
        for (int i = 0; i < policy.Settings.Count; i++)
        {
            var setting = policy.Settings[i];
            var consumed = actual.Settings[i];
            if (consumed.Setting != setting || consumed.ResultDigest != resultDigest
                || !CharacterCreationFinalizationDigest.IsCanonical(consumed.InputsDigest)
                || consumed.Branch != Branch(setting.Control, execution.Result.Attributes.Count, execution.ShownValue)
                || setting.Control == CharacterCreationEssenceControl.MaximumOnly && setting.Value != execution.MaximumOnly
                || setting.Control == CharacterCreationEssenceControl.ShownValue && setting.Value != execution.ShownValue
                || setting.Control == CharacterCreationEssenceControl.Unrounded && setting.Value != execution.Unrounded) return false;
        }
        evidence = actual;
        return true;
    }

    public static CharacterCreationEssenceSettingBranch Branch(CharacterCreationEssenceControl control, int enabledAttributes, bool shownValue)
        => control == CharacterCreationEssenceControl.Unrounded
            ? CharacterCreationEssenceSettingBranch.EssenceValidityAndRounding
            : enabledAttributes == 0 ? CharacterCreationEssenceSettingBranch.NoEnabledSpecialAttributes
            : control == CharacterCreationEssenceControl.MaximumOnly && shownValue
                ? CharacterCreationEssenceSettingBranch.ShownValueBypassesMinimumLoss
                : CharacterCreationEssenceSettingBranch.EnabledSpecialAttributeLoss;
}
