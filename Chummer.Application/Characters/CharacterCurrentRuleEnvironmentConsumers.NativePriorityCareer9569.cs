using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    // Shared calculation dispatch only. The native and imported proof factories
    // remain separate; each must establish its own complete saved owner graph.
    private static bool UsesOwnedPriorityCareer9569(CharacterCurrentRuleEnvironmentContext current)
        => UsesImportedPriorityCareer5667(current) || UsesNativePriorityCareer9569(current);

    // The successor preserves native lifestyle and selected quality-prompt
    // behavior; it does not silently fall back to older purchase grammars.
    private static bool UsesNativeLifecycleMechanics9569(CharacterCurrentRuleEnvironmentContext current)
        => current.SemanticsProfile == CurrentCharacterRuleSourceSemantics.ProfileV7
            && current.SemanticsFingerprint == CurrentCharacterRuleSourceSemantics.FingerprintV7
            || UsesNativePriorityCareer9569(current);

    private static bool UsesNativePriorityCareer9569(CharacterCurrentRuleEnvironmentContext current)
    {
        if (!CurrentCharacterRuleSourceSemantics.IsNativePriorityCareerProfile9569(
                current.SemanticsProfile, current.SemanticsFingerprint)
            || current.Stage != CharacterCurrentRuleStage.Career
            || current.Current.Document.RulesetId != "sr5") return false;
        XElement root = ParseCreation1949(current.CharacterXml);
        return ScalarCreation1949(root, "buildmethod") == CharacterCreationBuildMethods.Priority
            && root.Elements(CharacterCreationLifestyleMechanicsRules.CapabilityElement).ToArray() is [var marker]
            && !marker.HasAttributes && !marker.HasElements
            && marker.Value == CharacterCreationLifestyleMechanicsRules.Capability;
    }
}
