using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCareerFocusBondingRules
{
    internal static bool Allows(CharacterCareerFocusBondingState state, bool bonded)
    {
        // Canonical BeforeCheck returns before both limit checks when unbonding.
        if (!bonded || state.Bonded || state.IgnoreRules) return true;
        long count = (long)state.BondedCount + 1, force = (long)state.TotalForce + state.Force;
        return count <= state.Magic && force <= (long)state.Magic * 5
            && (state.AdeptMagic is not int adept || count <= adept && force <= (long)adept * 5);
    }
}

public static partial class CharacterCareerFocusBondingTransaction
{
    private sealed record Inventory(XElement[] Gear, XElement[] Foci, int Count, int Force);
    private static Inventory ReadInventory(XElement root)
    {
        var gear = new List<XElement>();
        foreach (string container in new[] { "gears", "armors", "weapons", "cyberwares", "vehicles" })
        {
            var box = Node(root, container);
            if (box is not null) gear.AddRange(box.Descendants().Where(x => x.Name.LocalName == "gear"));
        }
        Need(gear.All(x => x.Name == "gear" && !x.HasAttributes), "focus_bonding_inventory_invalid");
        var ids = new HashSet<Guid>();
        foreach (var item in gear)
            Need(ids.Add(Identity(item)) && root.Descendants("guid").Count(x => Guid.TryParse(x.Value, out var id) && id == Identity(item)) == 1,
                "focus_bonding_duplicate_identity");
        var foci = Node(root, "foci")?.Elements().ToArray() ?? [];
        var claimed = new HashSet<Guid>(); int count = 0, force = 0;
        foreach (var focus in foci)
        {
            Need(focus.Name == "focus" && !focus.HasAttributes && focus.Elements().Count() == 2
                && ids.Add(Identity(focus)), "focus_bonding_binding_invalid");
            Guid id = GuidValue(focus, "gearid");
            var item = gear.Single(x => Identity(x) == id);
            Need(claimed.Add(id) && Scalar(item, "category") is "Foci" or "Metamagic Foci"
                && Flag(item, "bonded") && Integer(item, "rating") > 0, "focus_bonding_binding_invalid");
            count = checked(count + 1); force = checked(force + Integer(item, "rating"));
        }
        // Stack operations are excluded. Existing stacks still participate in the
        // canonical count/total-Force check; they are never priced or mutated here.
        foreach (var stack in Node(root, "stackedfoci")?.Elements() ?? [])
        {
            Need(stack.Name == "stackedfocus" && !stack.HasAttributes && ids.Add(Identity(stack)), "focus_bonding_stack_invalid");
            Guid ownerId = GuidValue(stack, "gearid"); var owner = gear.Single(x => Identity(x) == ownerId);
            Need(claimed.Add(ownerId) && Scalar(owner, "category") == "Stacked Focus", "focus_bonding_stack_invalid");
            int total = 0; var components = Box(stack, "gears").Elements().ToArray();
            Need(components.Length >= 2, "focus_bonding_stack_invalid");
            foreach (var component in components)
            {
                Need(component.Name == "gear" && !component.HasAttributes && ids.Add(Identity(component))
                    && Scalar(component, "category") is "Foci" or "Metamagic Foci" && Integer(component, "rating") > 0,
                    "focus_bonding_stack_invalid");
                total = checked(total + Integer(component, "rating"));
            }
            if (Flag(stack, "bonded")) { count = checked(count + 1); force = checked(force + total); }
        }
        foreach (var item in gear)
        {
            string category = Scalar(item, "category", "");
            if (category is "Foci" or "Metamagic Foci")
                Need(Integer(item, "rating") > 0 && Flag(item, "bonded") == claimed.Contains(Identity(item)), "focus_bonding_orphan_binding");
            if (category == "Stacked Focus") Need(claimed.Contains(Identity(item)), "focus_bonding_orphan_stack");
        }
        return new(gear.ToArray(), foci, count, force);
    }

    private static bool SupportedName(string name)
        => name is "Alchemical Focus" or "Disenchanting Focus" or "Power Focus" or "Centering Focus"
            or "Flexible Signature Focus" or "Masking Focus" or "Spell Shaping Focus"
            || SpellcastingFocusCategory7471(name) is not null
            || new[] { "Counterspelling Focus", "Ritual Spellcasting Focus", "Sustaining Focus" }
                .Any(type => new[] { "Combat", "Detection", "Health", "Illusion", "Manipulation" }.Any(category => name == type + ", " + category));

    internal const string OrdinarySpellcastingFocusCondition7471 = "not(/spell/alchemical)";
    private static string? SpellcastingFocusCategory7471(string name) => name switch
    {
        "Spellcasting Focus, Combat" => "Combat",
        "Spellcasting Focus, Detection" => "Detection",
        "Spellcasting Focus, Health" => "Health",
        "Spellcasting Focus, Illusion" => "Illusion",
        "Spellcasting Focus, Manipulation" => "Manipulation",
        _ => null
    };

    private static XElement ExpectedBonus(string name)
    {
        Need(SupportedName(name), "focus_bonding_effect_type_unsupported");
        var bonus = new XElement("bonus");
        if (name is "Alchemical Focus" or "Disenchanting Focus") bonus.Add(new XElement("specificskill",
            new XElement("name", name == "Alchemical Focus" ? "Alchemy" : "Disenchanting"), new XElement("bonus", "Rating")));
        if (name == "Power Focus") bonus.Add(new XElement("skillattribute", new XAttribute("precedence", "0"),
            new XElement("name", "MAG"), new XElement("bonus", "Rating")));
        if (SpellcastingFocusCategory7471(name) is { } category)
        {
            bonus.Add(new XAttribute("unique", category.ToLowerInvariant() + "spellfocus"));
            bonus.Add(new XElement("spellcategory", new XElement("name", category), new XElement("val", "Rating"),
                new XElement("condition", OrdinarySpellcastingFocusCondition7471)));
        }
        bonus.Add(new XElement("selecttradition")); return bonus;
    }

    private static XElement[] CompileFocus(XElement gear, XElement source)
    {
        const string sourceFields = "id name category rating minrating ratinglabel source page avail cost bonus translate altpage notes altnotes notesColor";
        Need(source.Elements().All(x => sourceFields.Split(' ').Contains(x.Name.LocalName) && x.Name.Namespace == XNamespace.None)
            && source.Elements().GroupBy(x => x.Name).All(x => x.Count() == 1)
            && source.Elements().Where(x => x.Name != "bonus").All(x => !x.HasAttributes && !x.HasElements),
            "focus_bonding_source_semantics_unsupported");
        string name = Scalar(source, "name");
        Need(SupportedName(name) && Scalar(gear, "name") == name && Scalar(source, "category") == "Foci"
            && Scalar(gear, "category") == "Foci" && GuidValue(source, "id") == GuidValue(gear, "sourceid")
            && Scalar(source, "source") == Scalar(gear, "source"), "focus_bonding_source_identity_invalid");
        int min = Integer(source, "minrating", 1), max = Integer(source, "rating"), rating = Integer(gear, "rating");
        Need(min >= 1 && max >= min && rating >= min && rating <= max, "focus_bonding_rating_invalid");
        Need(Scalar(gear, "extra").Length is > 0 and <= 256 && Scalar(gear, "extra").Trim() == Scalar(gear, "extra"),
            "focus_bonding_tradition_selection_required");
        const string gearFields = "sourceid guid name category capacity armorcapacity minrating maxrating rating qty avail costfor cost weight extra bonded equipped wirelesson stolen weaponguid bonus wirelessbonus weaponbonus flechetteweaponbonus source page isflechetteammo ammoforweapontype canformpersona devicerating gearname forcedvalue matrixcmfilled matrixcmbonus parentid allowrename childcostmultiplier childavailmodifier children location notes notesColor discountedcost programlimit overclocked attack sleaze dataprocessing firewall attributearray modattack modsleaze moddataprocessing modfirewall modattributearray canswapattributes active homenode sortorder grade";
        Need(gear.Elements().All(x => x.Name.Namespace == XNamespace.None && gearFields.Split(' ').Contains(x.Name.LocalName)
            && (x.Name == "bonus" || !x.HasAttributes && !x.HasElements))
            && gear.Elements().GroupBy(x => x.Name).All(x => x.Count() == 1), "focus_bonding_owned_shape_unsupported");
        foreach (string empty in new[] { "children", "wirelessbonus", "weaponbonus", "flechetteweaponbonus", "parentid", "grade", "forcedvalue" })
            Need(Scalar(gear, empty, "").Length == 0, "focus_bonding_owned_semantics_unsupported");
        Need(Scalar(gear, "weaponguid", "") is "" or "00000000-0000-0000-0000-000000000000"
            && Number(gear, "qty") == 1 && Number(gear, "costfor", 1) == 1, "focus_bonding_owned_semantics_unsupported");
        // Both field allowlists enforce a single unqualified bonus. Read its
        // attributes through exact grammar equality, not the scalar Node helper.
        var expected = ExpectedBonus(name); var actual = source.Element("bonus");
        Need(actual is not null && XmlKey(actual) == XmlKey(expected), "focus_bonding_source_effect_unsupported");
        // Gear.Load falls back to effective source when the saved node has no
        // inner text; Gear.Save emits an empty node for selecttradition alone.
        var savedBonus = gear.Element("bonus");
        Need(savedBonus is null || !savedBonus.HasAttributes && string.IsNullOrWhiteSpace(savedBonus.Value) && !savedBonus.HasElements
            || XmlKey(savedBonus) == XmlKey(expected), "focus_bonding_saved_bonus_drift");
        return BuiltinEffects(gear);
    }

    private static XElement[] BuiltinEffects(XElement gear)
    {
        string name = Scalar(gear, "name"); var result = new List<XElement>();
        Guid id = Identity(gear); int rating = Integer(gear, "rating"); bool enabled = Flag(gear, "equipped");
        if (name is "Alchemical Focus" or "Disenchanting Focus")
            result.Add(FocusImprovement(id, "Skill", name == "Alchemical Focus" ? "Alchemy" : "Disenchanting", rating, enabled));
        if (name == "Power Focus") result.Add(FocusImprovement(id, "SkillAttribute", "MAG", rating, enabled, "precedence0"));
        string unique = "";
        if (SpellcastingFocusCategory7471(name) is { } category)
        {
            unique = category.ToLowerInvariant() + "spellfocus";
            result.Add(FocusImprovement(id, "SpellCategory", category, rating, enabled, unique,
                OrdinarySpellcastingFocusCondition7471));
        }
        // Canonical selecttradition consumes saved Extra as ForcedValue and creates a Tradition improvement.
        // It inherits the enclosing bonus unique, just like spellcategory.
        result.Add(FocusImprovement(id, "Tradition", Scalar(gear, "extra"), 0, enabled, unique));
        return result.ToArray();
    }

    private static XElement FocusImprovement(Guid id, string type, string name, int value, bool enabled, string unique = "", string condition = "")
    {
        var row = new XElement("improvement"); if (unique.Length != 0) row.Add(new XElement("unique", unique));
        row.Add(new XElement("target"), new XElement("improvedname", name), new XElement("sourcename", id.ToString("D")),
            new XElement("min", 0), new XElement("max", 0), new XElement("aug", 0), new XElement("augmax", 0),
            new XElement("val", value), new XElement("rating", 1), new XElement("exclude"), new XElement("condition", condition),
            new XElement("improvementttype", type), new XElement("improvementsource", "Gear"), new XElement("custom", "False"),
            new XElement("customname"), new XElement("customid"), new XElement("customgroup"), new XElement("addtorating", 0),
            new XElement("enabled", enabled ? 1 : 0), new XElement("order", 0), new XElement("notes"), new XElement("notesColor", "#000000"));
        return row;
    }
    private static XElement[] OwnedEffects(XElement root, Guid gearId)
    {
        var rows = Box(root, "improvements").Elements().ToArray();
        Need(rows.All(x => x.Name == "improvement" && !x.HasAttributes), "focus_bonding_effect_graph_invalid");
        Need(!rows.Any(x => Scalar(x, "improvementsource", "") == "Gear"
            && Scalar(x, "sourcename", "").StartsWith(gearId.ToString("D"), StringComparison.OrdinalIgnoreCase)
            && !Guid.TryParse(Scalar(x, "sourcename"), out _)), "focus_bonding_owned_effect_suffix_unsupported");
        return rows.Where(x => Scalar(x, "improvementsource", "") == "Gear"
            && Guid.TryParse(Scalar(x, "sourcename", ""), out var id) && id == gearId).ToArray();
    }
    private static string EffectKey(XElement row)
    {
        const string fields = "unique target improvedname sourcename min max aug augmax val rating exclude condition improvementttype improvementsource custom customname customid customgroup addtorating enabled order notes notesColor";
        Need(row.Name == "improvement" && !row.HasAttributes && row.Elements().All(x => !x.HasAttributes && !x.HasElements
            && x.Name.Namespace == XNamespace.None && fields.Split(' ').Contains(x.Name.LocalName))
            && row.Elements().GroupBy(x => x.Name).All(x => x.Count() == 1), "focus_bonding_effect_semantics_unsupported");
        return JsonSerializer.Serialize(new { Source = Scalar(row, "improvementsource"), Id = GuidValue(row, "sourcename"),
            Type = Scalar(row, "improvementttype"), Name = Scalar(row, "improvedname"), Unique = Scalar(row, "unique", ""),
            Target = Scalar(row, "target"), Min = Number(row, "min"), Max = Number(row, "max"), Aug = Number(row, "aug"),
            AugMax = Number(row, "augmax"), Value = Number(row, "val"), Rating = Number(row, "rating"), Exclude = Scalar(row, "exclude"),
            Condition = Scalar(row, "condition"), Custom = Flag(row, "custom"), CustomName = Scalar(row, "customname"),
            CustomId = Scalar(row, "customid"), CustomGroup = Scalar(row, "customgroup"), AddToRating = NumericFlag(row, "addtorating"),
            Enabled = NumericFlag(row, "enabled") });
    }
    private static bool EquivalentEffects(IEnumerable<XElement> actual, IEnumerable<XElement> expected)
        => actual.Select(EffectKey).Order(StringComparer.Ordinal).SequenceEqual(expected.Select(EffectKey).Order(StringComparer.Ordinal));
    private static decimal Number(XElement parent, string name, decimal? fallback = null)
    {
        Need(decimal.TryParse(Scalar(parent, name, fallback?.ToString(CultureInfo.InvariantCulture)), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var result), "focus_bonding_number_invalid"); return result;
    }
    private static bool NumericFlag(XElement row, string name)
    {
        string value = Scalar(row, name);
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number > 0;
        if (bool.TryParse(value, out var flag)) return flag;
        throw new InvalidDataException("focus_bonding_effect_flag_invalid");
    }
    private static string XmlKey(XElement node)
    {
        var copy = new XElement(node);
        foreach (var text in copy.DescendantNodes().OfType<XText>().Where(x => string.IsNullOrWhiteSpace(x.Value)).ToArray()) text.Remove();
        return copy.ToString(SaveOptions.DisableFormatting);
    }
}
