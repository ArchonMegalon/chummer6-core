using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Quotes pending talent qualities from effective source rows. Full rows are retained
/// for later effect materialization, not represented as already-applied grants.
/// Only prompt-free talent semantics with an exclusion-only requirement graph are admitted.
/// </summary>
public static class CharacterCreationKarmaTalentAuthority
{
    private static readonly HashSet<string> SourceFields = new(StringComparer.Ordinal)
    {
        "id", "name", "translate", "karma", "category", "contributetolimit", "contributetobp",
        "onlyprioritygiven", "bonus", "forbidden", "source", "page", "altpage", "nameonpage",
        "notes", "altnotes", "notesColor", "nolevels", "implemented"
    };

    public static string ComputeDigest(CharacterCreationKarmaTalentCatalog catalog)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            catalog with { AuthorityDigest = string.Empty });

    public static bool IsOptionId(string? id) => id == CharacterCreationKarmaTalentCatalog.MundaneOptionId
        || Guid.TryParseExact(id, "D", out var parsed) && parsed != Guid.Empty && parsed.ToString("D") == id;

    public static CharacterCreationKarmaTalentOption Mundane(string profileAnchor)
        => new(CharacterCreationKarmaTalentCatalog.MundaneOptionId, "Mundane", 0, null,
            string.Empty, CharacterCreationQualitiesRules.ComputeSourceNodeDigest(string.Empty), true, [], [profileAnchor]);

    public static CharacterCreationKarmaTalentOption? Project(XElement row, int multiplier, bool bookEnabled)
    {
        if (!Guid.TryParseExact(Value(row, "id"), "D", out var id) || id == Guid.Empty)
            return null;
        string xml = row.ToString(SaveOptions.DisableFormatting);
        string name = Value(row, "name");
        string book = Value(row, "source");
        string page = Value(row, "page");
        string? attribute = row.Element("bonus")?.Element("enableattribute")?.Element("name")?.Value;
        bool costValid = int.TryParse(Value(row, "karma"), NumberStyles.None,
            CultureInfo.InvariantCulture, out int baseCost) && multiplier > 0
            && (long)baseCost * multiplier <= int.MaxValue;
        int cost = costValid ? baseCost * multiplier : 0;
        bool supported = costValid && xml.Length <= 32 * 1024
            && row.Name == "quality" && !row.HasAttributes
            && row.Elements().All(child => SourceFields.Contains(child.Name.LocalName)
                && child.Name.Namespace == XNamespace.None)
            && row.Elements().GroupBy(child => child.Name).All(group => group.Count() == 1)
            && row.Elements().Where(child => child.Name != "bonus" && child.Name != "forbidden")
                .All(child => !child.HasAttributes && !child.HasElements)
            && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(book)
            && !string.IsNullOrWhiteSpace(page) && Value(row, "category") == "Positive"
            && IsBoolean(row, "contributetolimit", false, required: true)
            && IsBoolean(row, "contributetobp", true, required: false)
            && IsBoolean(row, "implemented", true, required: false)
            && row.Element("onlyprioritygiven") is { } marker && string.IsNullOrWhiteSpace(marker.Value)
            && attribute is "MAG" or "RES"
            && ValidBonus(row.Element("bonus")) && ValidExclusions(row.Element("forbidden"));
        string[] blockers = !bookEnabled ? [CharacterCreationKarmaTalentCatalog.SourceDisabled]
            : !supported ? [CharacterCreationKarmaTalentCatalog.UnsupportedSource] : [];
        return new(id.ToString("D"), name, cost, attribute, xml,
            CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml), blockers.Length == 0, blockers,
            [$"qualities.xml#quality:{id:D}", $"{book}:{page}"]);
    }

    public static bool IsCompatible(CharacterCreationKarmaTalentOption talent,
        CharacterCreationMetatypeOptionProjection metatype, IReadOnlyList<string>? additionalQualityNames = null)
    {
        if (talent.OptionId == CharacterCreationKarmaTalentCatalog.MundaneOptionId) return true;
        if (!talent.IsEnabled || talent.SourceNodeXml is not { Length: > 0 and <= 32 * 1024 }
            || metatype.GrantedQualities is null || metatype.GrantedQualities.Any(item => item is null)) return false;
        try
        {
            using var text = new StringReader(talent.SourceNodeXml);
            using var reader = XmlReader.Create(text, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024
            });
            var row = XElement.Load(reader);
            if (row.Name != "quality" || !ValidExclusions(row.Element("forbidden"))) return false;
            var exclusions = row.Element("forbidden")?.Element("oneof");
            return exclusions is null || !exclusions.Elements("quality").Any(excluded =>
                metatype.GrantedQualities.Any(quality => quality.Name == excluded.Value)
                || additionalQualityNames?.Contains(excluded.Value, StringComparer.Ordinal) == true);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool ValidBonus(XElement? bonus)
    {
        if (bonus is null || bonus.Attributes().Any(attribute => attribute.Name != "useselected"
                || !bool.TryParse(attribute.Value, out bool value) || value)
            || bonus.Elements("enableattribute").Count() != 1)
            return false;
        foreach (var effect in bonus.Elements())
        {
            if (effect.HasAttributes || effect.Name.Namespace != XNamespace.None) return false;
            switch (effect.Name.LocalName)
            {
                case "enableattribute":
                    if (!Fields(effect, "name") || Value(effect, "name") is not ("MAG" or "RES")) return false;
                    break;
                case "enabletab":
                    if (!effect.HasElements || effect.Elements().Any(child => child.Name != "name"
                        || child.HasAttributes || child.HasElements
                        || child.Value is not ("magician" or "adept" or "technomancer"))) return false;
                    break;
                case "unlockskills":
                    if (effect.HasElements || string.IsNullOrWhiteSpace(effect.Value)) return false;
                    break;
                case "blockspelldescriptor":
                case "limitspellcategory":
                    if (effect.HasElements || string.IsNullOrWhiteSpace(effect.Value)) return false;
                    break;
                case "addgear":
                    if (!Fields(effect, "name", "category") || Value(effect, "name") != "Living Persona"
                        || Value(effect, "category") != "Commlinks") return false;
                    break;
                case "specificskill":
                    if (!Fields(effect, "name", "bonus", "condition")
                        || string.IsNullOrWhiteSpace(Value(effect, "name"))
                        || string.IsNullOrWhiteSpace(Value(effect, "condition"))
                        || !int.TryParse(Value(effect, "bonus"), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out _)) return false;
                    break;
                default: return false;
            }
        }
        return true;
    }

    // Human/Elf racial qualities are checked at selection time. There are no
    // other selections yet; exclusions must also reach later quality evaluation.
    private static bool ValidExclusions(XElement? forbidden)
        => forbidden is null || !forbidden.HasAttributes && forbidden.Elements().Count() == 1
            && forbidden.Element("oneof") is { HasAttributes: false } oneOf
            && oneOf.Elements().All(item => !item.HasAttributes && !item.HasElements
                && (item.Name == "quality" && !string.IsNullOrWhiteSpace(item.Value)
                    || item.Name.LocalName is "magenabled" or "resenabled" or "depenabled"
                        && item.Name.Namespace == XNamespace.None && string.IsNullOrWhiteSpace(item.Value)));

    private static bool Fields(XElement element, params string[] fields)
        => element.Elements().Select(item => item.Name.ToString()).Order(StringComparer.Ordinal)
            .SequenceEqual(fields.Order(StringComparer.Ordinal))
            && element.Elements().All(item => !item.HasAttributes && !item.HasElements);

    private static bool IsBoolean(XElement row, string field, bool expected, bool required)
        => row.Element(field) is { } element ? bool.TryParse(element.Value, out var value) && value == expected : !required;

    private static string Value(XElement row, string field) => row.Element(field)?.Value ?? string.Empty;
}
