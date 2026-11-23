using System.Xml.Linq;
using Chummer.Application.Session;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static bool UsesCareerCondition5497(CharacterCurrentRuleEnvironmentContext current)
        => current.Stage == CharacterCurrentRuleStage.Career
            && CurrentCharacterRuleSourceSemantics.IsCareerConditionProfile5497(
                current.SemanticsProfile, current.SemanticsFingerprint);

    private static bool TryObserveVehiclePolicy5497(CharacterCurrentRuleEnvironmentContext current,
        ICharacterSourceDataContext source, XElement profile, out object? observation)
    {
        observation = null;
        if (!UsesCareerCondition5497(current) || !current.IsUnchanged
            || !source.TryResolveCareerSettingsProfile(out var raw, out var state)
            || string.IsNullOrWhiteSpace(state)) return false;
        var settings = ParseCreation1949(raw);
        // The resolver adapts only setting -> settings. Compare every source
        // child, retaining the original capture and the complete raw readback.
        if (!XNode.DeepEquals(CreationComparisonTree1949(settings),
                CreationComparisonTree1949(new XElement("settings", profile.Nodes())))) return false;
        if (!settings.Elements().Any(e => e.Name.LocalName == SessionPlayVehiclePolicySource5400.OverrideElement))
            return true;
        SessionPlayVehiclePolicySource5400.SettingsPolicy5497? policy = null;
        try { policy = SessionPlayVehiclePolicySource5400.InterpretSettings5497(settings); }
        catch (SessionPlayException)
        {
            // The explicit typed capability is unavailable. Keep unrelated
            // retained history readable; the vehicle projector repeats this same
            // interpreter and cannot substitute Standard on this branch.
        }
        if (!source.TryResolveCareerSettingsProfile(out var finalRaw, out var finalState)
            || finalRaw != raw || finalState != state || !current.IsUnchanged) return false;
        observation = new { Schema = "chummer.current-vehicle-policy-consumption.v1", current.BindingDigest,
            Source = raw, SourceState = state, Available = policy is not null, Policy = policy };
        return true;
    }
}
