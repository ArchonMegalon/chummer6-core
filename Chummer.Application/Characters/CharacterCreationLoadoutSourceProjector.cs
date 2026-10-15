using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Source-faithful, prompt-free Weapon/Armor Create+Save subset.
/// This does not add ammunition, attachments, improvements or implied loaded clips.</summary>
public static class CharacterCreationLoadoutSourceProjector
{
    public static bool IsSourceProjectable(string kind, string sourceNodeXml, string sourceCategoryXml)
        => CharacterCreationLoadoutRules.IsSourceProjectable(kind, sourceNodeXml, sourceCategoryXml);

    public static bool TryCreateOption(string kind, XElement source, XElement? category,
        bool sourceEnabled, int maximumAvailability, out CharacterCreationLoadoutCatalogOption? option)
    {
        option = null;
        if (source is null || source.Name != kind || maximumAvailability < 0
            || kind is not (CharacterCreationLoadoutKinds.Weapon or CharacterCreationLoadoutKinds.Armor)
            || !Guid.TryParse(Value(source, "id"), out Guid id) || id == Guid.Empty
            || string.IsNullOrWhiteSpace(Value(source, "name")) || string.IsNullOrWhiteSpace(Value(source, "category"))) return false;
        string xml = source.ToString(SaveOptions.DisableFormatting);
        string categoryXml = (category ?? new XElement("category")).ToString(SaveOptions.DisableFormatting);
        bool fixedCost = CharacterCreationLoadoutRules.TryReadFixedCost(Value(source, "cost"), out decimal cost);
        bool fixedAvailability = CharacterCreationLoadoutRules.TryReadAvailability(Value(source, "avail"),
            out int availability, out string legality);
        var blockers = new List<string>();
        if (!sourceEnabled) blockers.Add(CharacterCreationLoadoutBlockers.SourceDisabled);
        if (fixedAvailability && availability > maximumAvailability) blockers.Add(CharacterCreationLoadoutBlockers.AvailabilityExceeded);
        if (!fixedCost || !fixedAvailability || !IsSourceProjectable(kind, xml, categoryXml))
            blockers.Add(CharacterCreationLoadoutBlockers.UnsupportedSemantics);
        var candidate = new CharacterCreationLoadoutCatalogOption($"{kind}:{id:D}", kind, id,
            Value(source, "name"), Value(source, "category"), fixedCost ? cost : 0m,
            fixedAvailability ? availability : 0, legality, Value(source, "source"), Value(source, "page"),
            blockers.Count == 0, fixedCost, fixedAvailability, blockers.Order(StringComparer.Ordinal).ToArray(),
            [$"{(kind == CharacterCreationLoadoutKinds.Weapon ? "weapons.xml" : "armor.xml")}#{kind}:{id:D}"],
            xml, CharacterCreationLoadoutRules.ComputeSourceNodeDigest(xml), categoryXml,
            CharacterCreationLoadoutRules.ComputeSourceCategoryDigest(categoryXml), string.Empty);
        candidate = candidate with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(candidate) };
        if (!CharacterCreationLoadoutRules.IsValidOption(candidate)) return false;
        option = candidate;
        return true;
    }

    public static bool TryBuildGraph(IReadOnlyList<CharacterCreationLoadoutLine>? lines,
        string reviewDigest, out XElement[] weapons, out XElement[] armor,
        IReadOnlyList<CharacterCreationLoadoutInstance>? retainedInstances = null)
    {
        weapons = []; armor = [];
        if (!CharacterCreationFinalizationDigest.IsCanonical(reviewDigest)
            || lines is not { Count: <= CharacterCreationLoadoutRules.MaximumBasketLines }
            || lines.Any(line => !CharacterCreationLoadoutRules.IsValidLine(line))
            || lines.Select(line => line.OptionId).Distinct(StringComparer.Ordinal).Count() != lines.Count
            || lines.Count(line => line.Kind == CharacterCreationLoadoutKinds.Armor) > 1) return false;
        if (retainedInstances is not null && (retainedInstances.Count > lines.Count
            || retainedInstances.Any(row => row is null || row.InstanceIds is null
                || row.InstanceIds.Any(id => id == Guid.Empty)
                || !lines.Any(line => line.OptionId == row.OptionId && line.Quantity == row.InstanceIds.Count))
            || retainedInstances.Select(row => row.OptionId).Distinct(StringComparer.Ordinal).Count() != retainedInstances.Count
            || retainedInstances.SelectMany(row => row.InstanceIds).Distinct().Count() != retainedInstances.Sum(row => row.InstanceIds.Count)))
            return false;
        var weaponNodes = new List<XElement>();
        var armorNodes = new List<XElement>();
        foreach (CharacterCreationLoadoutLine line in lines.OrderBy(item => item.OptionId, StringComparer.Ordinal))
        {
            // IsValidLine replays the bounded, DTD-free parser and source semantics above.
            XElement source = XElement.Parse(line.SourceNodeXml);
            XElement category = XElement.Parse(line.SourceCategoryXml);
            for (int index = 0; index < line.Quantity; index++)
            {
                string instanceId = retainedInstances?.SingleOrDefault(row => row.OptionId == line.OptionId)?.InstanceIds[index].ToString("D")
                    ?? StableGuid($"creation-loadout:{line.Kind}:{line.OptionId}:{line.SourceNodeDigest}:{line.SourceCategoryDigest}:{reviewDigest}:{index}");
                XElement saved = Common(source, line, instanceId);
                if (line.Kind == CharacterCreationLoadoutKinds.Weapon)
                {
                    AddWeaponFields(saved, source, category);
                    weaponNodes.Add(saved);
                }
                else
                {
                    AddArmorFields(saved, source);
                    armorNodes.Add(saved);
                }
            }
        }
        weapons = weaponNodes.ToArray(); armor = armorNodes.ToArray();
        return true;
    }

    private static XElement Common(XElement source, CharacterCreationLoadoutLine line, string id)
        => new(line.Kind,
            new XElement("sourceid", line.SourceId.ToString("D", CultureInfo.InvariantCulture)),
            new XElement("guid", id), new XElement("name", line.Name), new XElement("category", line.Category),
            new XElement("avail", Value(source, "avail")), new XElement("cost", Value(source, "cost")),
            new XElement("weight", Value(source, "weight")), new XElement("source", line.SourceBook),
            new XElement("page", line.Page), new XElement("rating", "0"), new XElement("maxrating", string.Empty),
            new XElement("equipped", "True"),
            // Weapon.Create reads the source flag; Armor.Create does not and
            // newly purchased armor retains its false creation default.
            new XElement("stolen", line.Kind == CharacterCreationLoadoutKinds.Weapon
                ? Boolean(source, "stolen", false) : "False"),
            new XElement("wirelesson", "False"), new XElement("location", string.Empty),
            new XElement("notes", source.Element("altnotes")?.Value ?? Value(source, "notes")),
            new XElement("notesColor", source.Element("notesColor")?.Value ?? "#000000"),
            new XElement("discountedcost", "False"), new XElement("sortorder", "0"),
            new XElement("active", "False"), new XElement("homenode", "False"),
            new XElement("devicerating", string.Empty), new XElement("programlimit", string.Empty),
            new XElement("overclocked", "None"), new XElement("attack", string.Empty), new XElement("sleaze", string.Empty),
            new XElement("dataprocessing", string.Empty), new XElement("firewall", string.Empty),
            new XElement("attributearray", string.Empty), new XElement("modattack", string.Empty),
            new XElement("modsleaze", string.Empty), new XElement("moddataprocessing", string.Empty),
            new XElement("modfirewall", string.Empty), new XElement("modattributearray", string.Empty),
            new XElement("canswapattributes", "False"), new XElement("matrixcmfilled", "0"),
            new XElement("wirelessbonus", string.Empty));

    private static void AddWeaponFields(XElement saved, XElement source, XElement category)
    {
        foreach (string field in new[] { "type", "spec", "spec2", "reach", "damage", "ap", "mode", "rc", "ammo",
                     "ammocategory", "accuracy", "conceal", "useskill", "useskillspec", "range", "alternaterange" })
            saved.Add(new XElement(field, Value(source, field)));
        saved.Add(new XElement("cyberware", "False"), new XElement("ammoslots", "1"),
            new XElement("sizecategory", Value(source, "sizecategory")), new XElement("firingmode", "DogBrain"),
            new XElement("minrating", string.Empty), new XElement("activeammoslot", "1"),
            new XElement("rangemultiply", source.Element("range")?.Attribute("multiply")?.Value ?? "1"),
            new XElement("singleshot", "1"), new XElement("shortburst", "3"), new XElement("longburst", "6"),
            new XElement("fullburst", "10"), new XElement("suppressive", "20"),
            new XElement("allowsingleshot", "True"), new XElement("allowshortburst", "True"),
            new XElement("allowlongburst", "True"), new XElement("allowfullburst", "True"),
            new XElement("allowsuppressive", "True"), new XElement("parentid", string.Empty),
            new XElement("allowaccessory", Boolean(source, "allowaccessory", true)),
            new XElement("weaponname", string.Empty), new XElement("included", "False"),
            new XElement("requireammo", "True"), new XElement("mount", string.Empty),
            new XElement("extramount", string.Empty), new XElement("weaponslots", string.Join('/',
                source.Element("accessorymounts")?.Elements("mount").Select(mount => mount.Value) ?? [])),
            new XElement("doubledcostweaponslots", string.Empty), new XElement("wirelessweaponbonus", string.Empty),
            new XElement("weapontype", Value(source, "weapontype") is { Length: > 0 } weaponType
                ? weaponType : category.Attribute("type")!.Value));
    }

    private static void AddArmorFields(XElement saved, XElement source)
    {
        foreach (string field in new[] { "armor", "armorcapacity" }) saved.Add(new XElement(field, Value(source, field)));
        string armorOverride = Value(source, "armoroverride");
        saved.Add(new XElement("armoroverride", armorOverride == "0" ? string.Empty : armorOverride),
            new XElement("armorname", string.Empty), new XElement("matrixcmbonus", "0"),
            new XElement("canformpersona", string.Empty), new XElement("extra", string.Empty),
            new XElement("damage", "0"), new XElement("ratinglabel", source.Element("ratinglabel")?.Value ?? "String_Rating"),
            new XElement("encumbrance", Boolean(source, "encumbrance", true)), new XElement("armormods"),
            new XElement("bonus", string.Empty));
    }

    private static string Value(XElement source, string name)
        => source.Elements(name).ToArray() is [var value] && !value.HasElements ? value.Value : string.Empty;
    private static string Boolean(XElement source, string name, bool fallback)
        => (source.Element(name) is { } value ? bool.Parse(value.Value) : fallback) ? "True" : "False";
    private static string StableGuid(string input)
        => new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(input)).AsSpan(0, 16)).ToString("D", CultureInfo.InvariantCulture);
}
