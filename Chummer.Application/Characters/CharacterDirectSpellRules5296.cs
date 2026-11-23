using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

// Extracted from the existing ordinary Direct Combat evaluator 3470. Shared by
// that distribution and local play; no name/UUID-to-rule table or caller bonus.
internal static class CharacterDirectSpellRules5296
{
    internal static bool TryReadSource(XElement source, out int adjustment, out string defense)
    {
        adjustment = 0; defense = "";
        if ((string?)source.Element("descriptor") != "Direct" || (string?)source.Element("range") != "LOS"
            || !TryReadOrdinaryCombatSource(source, out adjustment)) return false;
        defense = (string?)source.Element("type") switch { "M" => "WIL", "P" => "BOD", _ => "" };
        return defense.Length != 0;
    }

    private static bool TryReadOrdinaryCombatSource(XElement source, out int adjustment)
    {
        adjustment = 0;
        string[] fields = ["id", "name", "page", "source", "category", "damage", "descriptor", "duration", "dv", "range", "type", "useskill"];
        if (source.Name != "spell" || source.HasAttributes
            || source.Elements().Any(e => !fields.Contains(e.Name.LocalName, StringComparer.Ordinal) || e.HasAttributes || e.HasElements)
            || source.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || (string?)source.Element("category") != "Combat" || (string?)source.Element("duration") != "I"
            || (string?)source.Element("damage") is not ("P" or "S")
            || (string?)source.Element("useskill") is not (null or "" or "Spellcasting")) return false;
        string dv = ((string?)source.Element("dv") ?? "").Replace(" ", "", StringComparison.Ordinal);
        return dv == "F" || dv.Length >= 3 && dv[0] == 'F' && dv[1] is '+' or '-'
            && int.TryParse(dv.AsSpan(1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out adjustment)
            && adjustment is >= -100 and <= 100;
    }

    // SR5 pp. 283-284: ordinary non-elemental indirect LOS / LOS (A).
    // Reuse the strict field and drain grammar, without calling an indirect cast direct.
    internal static bool TryReadIndirectSource(XElement source, out int adjustment, out string kind)
    {
        adjustment = 0; kind = "";
        var descriptors = ((string?)source.Element("descriptor") ?? "").Split(',').Select(s => s.Trim()).ToArray();
        bool area = descriptors.Contains("Area", StringComparer.Ordinal);
        if ((string?)source.Element("source") != "SR5" || descriptors.Length != (area ? 2 : 1) || descriptors.Distinct(StringComparer.Ordinal).Count() != descriptors.Length
            || !descriptors.Contains("Indirect", StringComparer.Ordinal)
            || descriptors.Any(d => d is not ("Indirect" or "Area"))
            || (string?)source.Element("type") != "P"
            || (string?)source.Element("range") != (area ? "LOS (A)" : "LOS")) return false;
        if (!TryReadOrdinaryCombatSource(source, out adjustment)) return false;
        kind = area ? "indirect-area" : "indirect-single";
        return true;
    }

    // The retained catalog says only "Elemental". The publisher's individual
    // entries supply the element. Bind that evidence to catalog identity AND all
    // consumed structural fields, never a display name or a saved-character flag.
    // Drain remains the actual retained (possibly amended) expression.
    internal static bool TryReadElementalSource5361(XElement source, out int adjustment, out string kind, out string element)
    {
        adjustment = 0; kind = ""; element = "";
        if (!Guid.TryParse((string?)source.Element("id"), out var id)) return false;
        var rule = id.ToString("D") switch
        {
            "c78d91cc-fa02-48c3-a243-28823a2038ef" => (Element: "acid", Area: false, Page: "283", Book: "SR5"),
            "87cb3685-22e8-46fa-890f-f3cfef10a71f" => (Element: "acid", Area: true, Page: "283", Book: "SR5"),
            "02de7f2a-3c8e-48a5-b7d1-0bf9854271aa" => (Element: "fire", Area: false, Page: "284", Book: "SR5"),
            "9ce1e699-3c30-4184-858b-d5539935ef96" => (Element: "fire", Area: true, Page: "284", Book: "SR5"),
            "b39ec682-8409-4546-be00-7615583e6541" => (Element: "electricity", Area: false, Page: "284", Book: "SR5"),
            "9b5c1538-d66b-4b71-9953-78817aaef6e4" => (Element: "electricity", Area: true, Page: "284", Book: "SR5"),
            // SG pp.103-104 explicitly invoke SR5 Cold damage. Ice Storm also
            // creates Ice Sheet; its distinct outcome is retained by Core below.
            "bba17f26-7b83-4904-a5fa-a21ef7cacaa7" => (Element: "cold", Area: false, Page: "103", Book: "SG"),
            "2c2ad94e-3dd5-40e3-a4e0-74668dae1b48" => (Element: "cold", Area: true, Page: "103", Book: "SG"),
            _ => (Element: "", Area: false, Page: "", Book: "")
        };
        var descriptors = ((string?)source.Element("descriptor") ?? "").Split(',').Select(s => s.Trim()).ToArray();
        if (rule.Element.Length == 0 || (string?)source.Element("source") != rule.Book
            || (string?)source.Element("page") != rule.Page || (string?)source.Element("type") != "P"
            || (string?)source.Element("damage") != "P" || (string?)source.Element("range") != (rule.Area ? "LOS (A)" : "LOS")
            || descriptors.Length != (rule.Area ? 3 : 2) || descriptors.Distinct(StringComparer.Ordinal).Count() != descriptors.Length
            || !descriptors.Contains("Indirect") || !descriptors.Contains("Elemental")
            || descriptors.Any(d => d is not ("Indirect" or "Elemental" or "Area"))
            || descriptors.Contains("Area") != rule.Area || !TryReadOrdinaryCombatSource(source, out adjustment)) return false;
        element = rule.Element; kind = rule.Area ? "indirect-area" : "indirect-single"; return true;
    }

    // Spell.Skill resolves an explicit per-spell override first, then the exact
    // matching category's useskill. Empty/missing metadata supplies no default.
    // Shared by play mutation admission and the existing 3470 evaluator.
    internal static bool TryResolveSkill(XElement source, ICharacterSourceDataContext context,
        out string skill, out string evidence)
    {
        skill = (string?)source.Element("useskill") ?? "";
        evidence = "";
        if (skill.Length == 0)
        {
            if (context is not ICharacterSheetOwnerSourceContext categories
                || !categories.TryResolveSheetSpellCategory((string?)source.Element("category") ?? "", out var receipt)
                || receipt is null) return false;
            var category = XElement.Parse(receipt.SourceXml);
            if (category.Name != "category" || category.HasElements
                || category.Value != (string?)source.Element("category")) return false;
            skill = (string?)category.Attribute("useskill") ?? "";
            evidence = Chummer.Contracts.Characters.CharacterCreationFinalizationDigest.Compute(receipt);
        }
        else evidence = Chummer.Contracts.Characters.CharacterCreationFinalizationDigest.Compute(source.ToString(SaveOptions.DisableFormatting));
        // Other skills require their own attribute/pool/defaulting consumer.
        return skill == "Spellcasting";
    }

    internal static int DrainValue(int force, int adjustment) => Math.Max(2, checked(force + adjustment));
    internal static int LimitedHits(int hits, int force) => Math.Min(hits, force);
    internal static int NetHits(int limited, int opposition) => Math.Max(0, limited - opposition);
    internal static bool PhysicalDrain(int limited, int magic) => limited > magic;
    internal static int UnresistedDrain(int drain, int hits) => Math.Max(0, drain - hits);
}
