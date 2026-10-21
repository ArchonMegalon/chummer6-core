using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCareerPowerGraph
{
    private static XElement[] CompileEffects(XElement source, CharacterCareerPowerIdentity identity, int rating)
    {
        var bonus = Single(source, "bonus", false);
        Need(!bonus.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)), "power_bonus_invalid");
        var result = new List<XElement>();
        bool usesChoice = false;
        foreach (var node in bonus.Elements())
        {
            Need(!node.HasAttributes && node.Name.NamespaceName.Length == 0, "power_effect_attributes_unavailable");
            string type = node.Name.LocalName switch {
                "dodge" => "Dodge", "armor" => "Armor", "judgeintentionsdefense" => "JudgeIntentionsDefense",
                "damageresistance" => "DamageResistance", "composure" => "Composure",
                "memory" => "Memory", "physicalcmrecovery" => "PhysicalCMRecovery",
                "stuncmrecovery" => "StunCMRecovery", _ => "" };
            string name = "";
            decimal value = 0, augmented = 0;
            if (type.Length > 0)
            {
                Need(!node.HasElements, "power_scalar_effect_invalid");
                value = Expression(node.Value, rating);
            }
            else if (node.Name == "specificskill")
            {
                Need(node.Elements().All(e => e.Name == "name" || e.Name == "bonus"), "power_skill_effect_unavailable");
                type = "Skill"; name = Value(node, "name"); value = Expression(Value(node, "bonus"), rating);
            }
            else if (node.Name == "specificattribute" || node.Name == "selectattribute")
            {
                bool select = node.Name == "selectattribute";
                Need(node.Elements().All(e => e.Name == (select ? "attribute" : "name") || e.Name == "val"),
                    "power_attribute_effect_unavailable");
                name = select ? identity.Extra : Value(node, "name");
                Need(name is "BOD" or "AGI" or "REA" or "STR" or "CHA" or "INT" or "LOG" or "WIL",
                    "power_attribute_choice_unavailable");
                if (select)
                {
                    Need(!usesChoice && node.Elements("attribute").Any(e => !e.HasElements && !e.HasAttributes && e.Value == name),
                        "power_attribute_choice_invalid");
                    usesChoice = true;
                }
                type = "Attribute"; augmented = Expression(Value(node, "val"), rating);
            }
            else throw new InvalidDataException("power_effect_unavailable:" + node.Name.LocalName);
            Need(name.Length <= 1024 && value >= 0 && augmented >= 0, "power_effect_value_invalid");
            result.Add(Improvement(identity.InternalId, type, name, value, augmented));
        }
        Need(usesChoice || identity.Extra.Length == 0, "power_unbound_choice");
        return result.ToArray();
    }

    private static decimal Expression(string text, int rating)
    {
        if (text == "Rating") return rating;
        Need(text == text.Trim() && text.Length <= 20
            && decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _), "power_expression_unavailable");
        return decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    private static XElement Improvement(Guid instance, string type, string name, decimal value, decimal augmented) =>
        new("improvement", new XElement("target", ""), new XElement("improvedname", name),
            new XElement("sourcename", instance.ToString("D")), new XElement("min", 0), new XElement("max", 0),
            new XElement("aug", augmented), new XElement("augmax", 0), new XElement("val", value),
            new XElement("rating", 1), new XElement("exclude", ""), new XElement("condition", ""),
            new XElement("improvementttype", type), new XElement("improvementsource", "Power"),
            new XElement("custom", false), new XElement("customname", ""), new XElement("customid", ""),
            new XElement("customgroup", ""), new XElement("addtorating", false), new XElement("enabled", true),
            new XElement("order", 0), new XElement("notes", ""), new XElement("notesColor", "#000000"));

    // Ignore presentation-only notes/order/color, normalize native boolean and
    // decimal encodings; reject every additional behavioral field.
    private static string EffectKey(XElement effect)
    {
        string[] fields = ["target", "improvedname", "sourcename", "min", "max", "aug", "augmax", "val", "rating",
            "exclude", "condition", "improvementttype", "improvementsource", "custom", "customname",
            "customid", "customgroup", "addtorating", "enabled", "order", "notes", "notesColor", "unique"];
        Need(effect.Name == "improvement" && !effect.HasAttributes
            && effect.Elements().All(e => fields.Contains(e.Name.LocalName))
            && effect.Elements().GroupBy(e => e.Name).All(g => g.Count() == 1)
            && Value(effect, "unique", "") == "" && Value(effect, "condition") == ""
            && Value(effect, "exclude") == "" && Value(effect, "target") == ""
            && !Bool(effect, "custom") && !Bool(effect, "addtorating") && Bool(effect, "enabled"),
            "power_saved_effect_semantics_unavailable");
        return JsonSerializer.Serialize(new { Source = Value(effect, "improvementsource"),
            Id = GuidValue(effect, "sourcename"), Type = Value(effect, "improvementttype"),
            Name = Value(effect, "improvedname"), Min = Decimal(effect, "min"), Max = Decimal(effect, "max"),
            Aug = Decimal(effect, "aug"), AugMax = Decimal(effect, "augmax"),
            Val = Decimal(effect, "val"), Rating = Int(effect, "rating") });
    }
    private static bool EquivalentEffects(IEnumerable<XElement> actual, IEnumerable<XElement> expected) =>
        actual.Select(EffectKey).Order(StringComparer.Ordinal).SequenceEqual(
            expected.Select(EffectKey).Order(StringComparer.Ordinal));

    private static void RecalculateAttributeCaches(XElement root, XElement effects, IEnumerable<PowerState> states,
        XElement metatype)
    {
        var affected = states.SelectMany(s => CompileEffects(s.Source, s.Identity, s.Cost.TotalRating))
            .Where(e => Value(e, "improvementttype") == "Attribute").Select(e => Value(e, "improvedname")).Distinct();
        foreach (var name in affected)
        {
            var attribute = Single(root, "attributes").Elements("attribute").Single(e => Value(e, "name") == name);
            string prefix = name.ToLowerInvariant();
            int minimum = Int(metatype, prefix + "min"), maximum = Int(metatype, prefix + "max");
            int absoluteMaximum = Int(metatype, prefix + "aug");
            int allocated = Int(attribute, "base"), karma = Int(attribute, "karma");
            // Saved bounds are consistency checks, never authority for a larger purchase.
            Need(Int(attribute, "metatypemin") == minimum && Int(attribute, "metatypemax") == maximum
                && Int(attribute, "metatypeaugmax") == absoluteMaximum
                && minimum >= 0 && minimum <= maximum && maximum <= absoluteMaximum
                && allocated >= 0 && karma >= 0, "power_attribute_cap_unavailable");
            int natural = checked(minimum + allocated + karma);
            Need(natural <= maximum, "power_attribute_cap_unavailable");
            decimal augmentation = 0;
            foreach (var effect in effects.Elements().Where(e => Bool(e, "enabled")
                && (Value(e, "improvedname") == name || Value(e, "improvedname") == ""
                    || Value(e, "improvedname") == name + "Base")
                && (Value(e, "improvementttype").StartsWith("Attribute", StringComparison.Ordinal)
                    || Value(e, "improvementttype") == "ReplaceAttribute")))
            {
                Need(Value(effect, "improvementttype") == "Attribute" && Value(effect, "condition") == ""
                    && Decimal(effect, "min") == 0 && Decimal(effect, "max") == 0 && Decimal(effect, "augmax") == 0
                    && Value(effect, "improvedname") == name && Value(effect, "exclude") == ""
                    && Value(effect, "target") == "" && Value(effect, "unique", "") == ""
                    && Int(effect, "rating") == 1 && Decimal(effect, "aug") >= 0
                    && Decimal(effect, "val") == 0 && !Bool(effect, "custom") && !Bool(effect, "addtorating"),
                    "power_attribute_cache_semantics_unavailable");
                augmentation += Decimal(effect, "aug");
            }
            decimal total = natural + augmentation;
            // Native AttributeModifiers clamps the delta before TotalValue clamps the absolute value.
            // Reject a purchase that would be truncated on native reload.
            Need(augmentation <= absoluteMaximum - maximum
                && total >= 0 && total == decimal.Truncate(total) && total <= absoluteMaximum,
                "power_attribute_cap_unavailable");
            attribute.SetElementValue("totalvalue", total.ToString(CultureInfo.InvariantCulture));
        }
    }
}
