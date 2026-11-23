using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

// An additive projection over retained profile bytes. V1/V2 observations and
// their identifiers remain frozen; these values cannot confer execution custody.
public enum CharacterCreationEssenceControl
{
    MaximumOnly, ShownValue, Unrounded, UnclampMinimum
}

public sealed record CharacterCreationEssenceSettingDefinition(CharacterCreationEssenceControl Control, string Element);
public sealed record CharacterCreationEssenceSettingValue(CharacterCreationEssenceControl Control,
    bool Value, string SourceValueDigest);
public sealed record CharacterCreationEssenceSettingPolicy(string ProfileXmlDigest,
    IReadOnlyList<CharacterCreationEssenceSettingValue> Settings);

public enum CharacterCreationEssenceSettingBranch
{
    EssenceValidityAndRounding, EnabledSpecialAttributeLoss, NoEnabledSpecialAttributes, ShownValueBypassesMinimumLoss
}

public sealed record CharacterCreationEssenceSettingConsumption(CharacterCreationEssenceSettingValue Setting,
    CharacterCreationEssenceSettingBranch Branch, string InputsDigest, string ResultDigest);
public sealed record CharacterCreationEssenceSettingEvidence(CharacterCreationEssenceSettingPolicy Policy,
    IReadOnlyList<CharacterCreationEssenceSettingConsumption> Settings);

public static class CharacterCreationEssenceSettingDefinitions
{
    public static IReadOnlyList<CharacterCreationEssenceSettingDefinition> All { get; } = Array.AsReadOnly(new[]
    {
        new CharacterCreationEssenceSettingDefinition(CharacterCreationEssenceControl.MaximumOnly, "esslossreducesmaximumonly"),
        new CharacterCreationEssenceSettingDefinition(CharacterCreationEssenceControl.ShownValue, "specialkarmacostbasedonshownvalue"),
        new CharacterCreationEssenceSettingDefinition(CharacterCreationEssenceControl.Unrounded, "donotroundessenceinternally"),
        new CharacterCreationEssenceSettingDefinition(CharacterCreationEssenceControl.UnclampMinimum, "unclampattributeminimum")
    });

    // Absence produces no setting evidence. Existing interpreter defaults remain
    // internal arithmetic inputs, never authority for an absent source setting.
    public static bool TryRead(XElement profile, out CharacterCreationEssenceSettingPolicy? policy)
    {
        policy = null;
        if (profile.Name != "setting" || profile.HasAttributes) return false;
        var settings = new List<CharacterCreationEssenceSettingValue>();
        foreach (var definition in All)
        {
            var rows = profile.Elements().Where(node => node.Name.LocalName == definition.Element).ToArray();
            if (rows.Length == 0) continue;
            if (rows is not [var leaf] || leaf.Name != definition.Element || leaf.HasAttributes
                || leaf.Nodes().ToArray() is not [XText text] || text.GetType() != typeof(XText)
                || text.Value != text.Value.Trim() || !bool.TryParse(text.Value, out bool value)) return false;
            settings.Add(new(definition.Control, value,
                CharacterCreationFinalizationDigest.Compute(leaf.ToString(SaveOptions.DisableFormatting))));
        }
        policy = new(CharacterCreationFinalizationDigest.Compute(profile.ToString(SaveOptions.DisableFormatting)),
            settings.AsReadOnly());
        return true;
    }
}
