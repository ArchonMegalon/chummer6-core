using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private sealed record AttributeKarmaLeaf3201(int Value, string XmlDigest);

    private static AttributeKarmaLeaf3201? ReadAttributeKarmaLeaf3201(XElement profile,
        CharacterCreationAttributePolicy spending)
    {
        if (profile.Name != "setting" || profile.HasAttributes
            || profile.Descendants().Where(node => node.Name.LocalName == "karmacost").ToArray() is not [var costs]
            || costs.Parent != profile || costs.Name != "karmacost" || costs.HasAttributes
            || profile.Descendants().Where(node => node.Name.LocalName == "karmaattribute").ToArray() is not [var leaf]
            || leaf.Parent != costs || leaf.Name != "karmaattribute" || leaf.HasAttributes
            || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || !int.TryParse(leaf.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            || value <= 0 || value != spending.KarmaAttribute) return null;
        return new(value, CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting)));
    }

    private static bool MatchesAttributeKarmaObservation3201(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationSettingObservation observation, AttributeKarmaLeaf3201 leaf)
    {
        if (observation.Key != CharacterCreationObservedSetting.AttributeKarma
            || observation.State != CharacterCreationSettingObservationState.Present
            || policy.SettingObservations is not { } observations
            || observations.Settings.Count(row => row == observation) != 1
            || observation.SourceValueDigest != leaf.XmlDigest) return false;
        var definition = CharacterCreationSettingDefinitions.ForSchema(observations.Schema)?.SingleOrDefault(row => row.Key == CharacterCreationObservedSetting.AttributeKarma);
        return definition is not null && observation.ValueDigest == CharacterCreationSettingDefinitions.ValueDigest(
            definition.Kind, leaf.Value.ToString(CultureInfo.InvariantCulture));
    }

    private static bool SupportsAttributeKarma3201(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer, CharacterCreationSelectedConsumerPolicy selected)
        => consumer == CharacterCreationSettingConsumer.Attributes
            && TrySource(policy, source, out var profile)
            && selected.Attributes.Spending is { } spending
            && spending.SettingsProfileId == policy.SettingsProfileId
            && spending.RawProfileInputsDigest == policy.ProfileInputsDigest
            && ReadAttributeKarmaLeaf3201(profile!, spending) is { } leaf
            && MatchesAttributeKarmaObservation3201(policy, observation, leaf);

    internal static bool TryReadAttributeKarma3201(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string leafXmlDigest,
        out int value, out CharacterCreationAttributesPreview? result, out string? executionDigest)
    {
        value = 0; result = null; executionDigest = null;
        // A DTO copy cannot acquire this table entry. The existing binding checks
        // the canonical full/selected replay, state, policy, request and candidate.
        if (!attributeHouseRuleInvocations993.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (completed.Karma is not { } leaf || profileId != completed.Policy.SettingsProfileId
            || profileInputsDigest != completed.Policy.RawProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || leafXmlDigest != leaf.XmlDigest
            || bound.InvocationDigest != SkillCostInvocationBinding972(invocation)) return false;
        value = leaf.Value;
        result = completed.Result;
        // A zero sum is never evidence. This retained result follows the actual
        // successful allocation invocation, including the positive-rate guard.
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-attribute-Karma-price", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, completed.StateDigest, completed.SkillsDigest,
            completed.PolicyContentDigest, completed.Policy, completed.ProfileXmlDigest, Leaf = leaf, Result = result
        });
        return true;
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedProfileAttributeKarma3201(string profileId, string profileInputsDigest,
        string profileXmlDigest, string leafXmlDigest, out int value,
        out CharacterCreationAttributesPreview? result, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadAttributeKarma3201(this, profileId,
            profileInputsDigest, profileXmlDigest, leafXmlDigest, out value, out result, out executionDigest);
}
