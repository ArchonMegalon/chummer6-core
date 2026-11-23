using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static bool TryRetainedFocusValues6511(XElement character, XElement gear, string settings,
        IReadOnlyList<XElement> effects, out IReadOnlyList<CharacterRuleEffectValue> values)
    {
        values = [];
        var profile = ParsePurchaseXml(settings);
        if (profile is not { Name.LocalName: "settings" }) return false;
        profile.Name = "setting"; // Private wrapper adaptation used by the transaction too.
        if (!CharacterCreationFocusKarma1220.TryPriceOwnedOrdinary(profile, character, gear, out _, out int karma)) return false;
        string id = gear.Element("guid")!.Value, name = gear.Element("name")!.Value;
        var rows = new List<CharacterRuleEffectValue>
        {
            new(id, name, CharacterRuleEffectMetric.Rating, "focus-force", CharacterRuleEffectValueState.Known,
                Number: int.Parse(gear.Element("rating")!.Value, System.Globalization.CultureInfo.InvariantCulture)),
            new(id, name, CharacterRuleEffectMetric.Selection, "focus-category", CharacterRuleEffectValueState.Known, Text: gear.Element("category")!.Value),
            new(id, name, CharacterRuleEffectMetric.Selection, "focus-tradition", CharacterRuleEffectValueState.Known, Text: gear.Element("extra")!.Value),
            new(id, name, CharacterRuleEffectMetric.Selected, "focus-bonded", CharacterRuleEffectValueState.Known, Flag: bool.Parse(gear.Element("bonded")!.Value)),
            new(id, name, CharacterRuleEffectMetric.KarmaCost, "focus-binding", CharacterRuleEffectValueState.Known, Number: karma)
        };
        foreach (var effect in effects)
        {
            string qualifier = "focus-effect/" + effect.Element("improvementttype")!.Value + "/" + effect.Element("improvedname")!.Value;
            rows.Add(new(id, name, CharacterRuleEffectMetric.Modifier, qualifier, CharacterRuleEffectValueState.Known,
                Number: decimal.Parse(effect.Element("val")!.Value, System.Globalization.CultureInfo.InvariantCulture)));
            rows.Add(new(id, name, CharacterRuleEffectMetric.Enabled, qualifier, CharacterRuleEffectValueState.Known,
                Flag: effect.Element("enabled")!.Value == "1"));
        }
        values = rows;
        return true;
    }
}
