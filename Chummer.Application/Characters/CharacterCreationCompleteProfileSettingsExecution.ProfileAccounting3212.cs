using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private sealed record SecondMaximumLeaf3212(bool AllowSecond, int Maximum, bool Shadowed,
        string XmlDigest, string? PrimaryXmlDigest);

    // Retained only inside the existing successful canonical attribute execution.
    // Keep the actual Boolean and primary selection, not merely an equal count.
    private static SecondMaximumLeaf3212? ReadSecondMaximum3212(XElement profile,
        CharacterCreationAttributePolicy policy)
    {
        var primary = profile.Elements().Where(node => node.Name.LocalName == "maxnumbermaxattributescreate").ToArray();
        if (profile.Elements().Where(node => node.Name.LocalName == "allow2ndmaxattribute").ToArray() is not [var legacy]
            || legacy.Name != "allow2ndmaxattribute" || legacy.HasAttributes
            || legacy.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
            || legacy.Value != legacy.Value.Trim() || !bool.TryParse(legacy.Value, out bool allowSecond)
            || primary.Length > 1 || primary.Any(node => node.Name != "maxnumbermaxattributescreate"
                || node.HasAttributes || node.Nodes().ToArray() is not [XText value] || value.GetType() != typeof(XText))
            || CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(profile) is not int maximum
            || maximum != policy.MaxNumberMaxAttributesCreate
            || primary.Length == 0 && maximum != (allowSecond ? 2 : 1)) return null;
        return new(allowSecond, maximum, primary.Length == 1,
            CharacterCreationFinalizationDigest.Compute(legacy.ToString(SaveOptions.DisableFormatting)),
            primary.Length == 1 ? CharacterCreationFinalizationDigest.Compute(primary[0].ToString(SaveOptions.DisableFormatting)) : null);
    }

    internal static bool TryReadSecondMaximum3212(CharacterCreationRuleSourceScopeInvocation invocation,
        string profileId, string profileInputsDigest, string profileXmlDigest, string rawCharacterDigest,
        string leafXmlDigest, out bool allowSecond, out int maximum, out bool shadowed,
        out CharacterCreationAttributesPreview? result, out string? executionDigest)
    {
        allowSecond = false; maximum = 0; shadowed = false; result = null; executionDigest = null;
        if (!CharacterCreationCompleteRuleSourceExecution.HasCurrentProfileInvocation3212(invocation,
                profileId, profileInputsDigest, profileXmlDigest, rawCharacterDigest)
            || !attributeHouseRuleInvocations993.TryGetValue(invocation, out var bound)) return false;
        var completed = bound.Completed;
        if (completed.SecondMaximum is not { } leaf || profileId != completed.Policy.SettingsProfileId
            || profileInputsDigest != completed.Policy.RawProfileInputsDigest
            || profileXmlDigest != completed.ProfileXmlDigest || leafXmlDigest != leaf.XmlDigest
            || bound.InvocationDigest != SkillCostInvocationBinding972(invocation)) return false;
        allowSecond = leaf.AllowSecond; maximum = leaf.Maximum; shadowed = leaf.Shadowed;
        result = completed.Result;
        executionDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Operation = "canonical-maximum-attribute-count-legacy3212", bound.AuthorityDigest, bound.InputsDigest,
            bound.InvocationDigest, completed.StateDigest, completed.SkillsDigest,
            completed.PolicyContentDigest, completed.Policy, completed.ProfileXmlDigest, Leaf = leaf, Result = result
        });
        return true;
    }
}

public sealed partial class CharacterCreationRuleSourceScopeInvocation
{
    public bool TryGetExecutedProfileSecondMaximum3212(string profileId, string profileInputsDigest,
        string profileXmlDigest, string rawCharacterDigest, string leafXmlDigest,
        out bool allowSecond, out int maximum, out bool shadowed,
        out CharacterCreationAttributesPreview? result, out string? executionDigest)
        => CharacterCreationCompleteProfileSettingsExecution.TryReadSecondMaximum3212(this,
            profileId, profileInputsDigest, profileXmlDigest, rawCharacterDigest, leafXmlDigest,
            out allowSecond, out maximum, out shadowed, out result, out executionDigest);
}
