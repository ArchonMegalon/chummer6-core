using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// Learned formula statistics from the German 2024 Core tables, pp.134–145
/// (the source SHA-256 is Sr6CreationFoundationRules.CoreSourceSha256).
/// These are base statistics, not a cast, adjusted drain, or an applied effect.
/// Kept outside the saved option/preview contract so existing draft digests remain valid.
/// </summary>
internal static class Sr6CreationSpellProfiles
{
    private sealed record Statistics(string Category, int Page, string Type, string Range,
        string Duration, int Drain, string? Damage = null, string? Combat = null, string? Element = null);

    // Source codes: B/BF/BF(F) -> T/LOS/LOS(A), A/S/P -> S/I/P,
    // K/B damage -> P/S. L denotes source ZB and deliberately remains blocked:
    // a generic sustained spell would lose the required maximum-duration rule.
    private static readonly IReadOnlyDictionary<string, Statistics> Profiles =
        new Dictionary<string, Statistics>(StringComparer.Ordinal)
    {
        ["spell-decrease-attribute"] = new("health", 134, "P", "T", "S", 3),
        ["spell-increase-attribute"] = new("health", 134, "P", "T", "S", 3),
        ["spell-antidote"] = new("health", 135, "P", "T", "P", 5),
        ["spell-heal"] = new("health", 135, "P", "T", "P", 3),
        ["spell-cooling-heal"] = new("health", 135, "P", "T", "P", 5),
        ["spell-neutralizing-heal"] = new("health", 135, "P", "T", "P", 5),
        ["spell-increase-reflexes"] = new("health", 135, "P", "T", "S", 5),
        ["spell-resist-pain"] = new("health", 136, "M", "T", "S", 3),
        ["spell-stabilize"] = new("health", 136, "M", "T", "P", 3),
        ["spell-warming-heal"] = new("health", 136, "P", "T", "P", 5),
        ["spell-chaos"] = new("illusion", 136, "P", "LOS", "S", 4),
        ["spell-mask"] = new("illusion", 136, "M", "T", "S", 3),
        ["spell-physical-mask"] = new("illusion", 136, "P", "T", "S", 4),
        ["spell-agony"] = new("illusion", 136, "M", "LOS", "S", 3),
        ["spell-hush"] = new("illusion", 136, "M", "T", "S", 3),
        ["spell-sensor-sneak"] = new("illusion", 136, "P", "T", "S", 2),
        ["spell-silence"] = new("illusion", 137, "P", "T", "S", 4),
        ["spell-trid-phantasm"] = new("illusion", 137, "P", "LOS (A)", "S", 4),
        ["spell-phantasm"] = new("illusion", 137, "M", "LOS (A)", "S", 3),
        ["spell-invisibility"] = new("illusion", 137, "M", "T", "S", 3),
        ["spell-improved-invisibility"] = new("illusion", 137, "P", "T", "S", 4),
        ["spell-confusion"] = new("illusion", 137, "M", "LOS", "S", 3),
        ["spell-stunball"] = new("combat", 138, "M", "LOS (A)", "I", 4, "S", "direct"),
        ["spell-stunbolt"] = new("combat", 138, "M", "LOS", "I", 3, "S", "direct"),
        ["spell-lightning-bolt"] = new("combat", 138, "P", "LOS", "I", 5, "P", "indirect", "electricity"),
        ["spell-blast"] = new("combat", 138, "P", "LOS (A)", "I", 4, "S", "indirect"),
        ["spell-ice-spear"] = new("combat", 138, "P", "LOS", "I", 5, "P", "indirect", "cold"),
        ["spell-ice-storm"] = new("combat", 138, "P", "LOS (A)", "I", 6, "P", "indirect", "cold"),
        ["spell-powerball"] = new("combat", 138, "P", "LOS (A)", "I", 5, "P", "direct"),
        ["spell-powerbolt"] = new("combat", 139, "P", "LOS", "I", 4, "P", "direct"),
        ["spell-fireball"] = new("combat", 139, "P", "LOS (A)", "I", 6, "P", "indirect", "fire"),
        ["spell-flamethrower"] = new("combat", 139, "P", "LOS", "I", 5, "P", "indirect", "fire"),
        ["spell-ball-lightning"] = new("combat", 139, "P", "LOS (A)", "I", 6, "P", "indirect", "electricity"),
        ["spell-manaball"] = new("combat", 139, "M", "LOS (A)", "I", 5, "P", "direct"),
        ["spell-manabolt"] = new("combat", 139, "M", "LOS", "I", 4, "P", "direct"),
        ["spell-acid-stream"] = new("combat", 139, "P", "LOS", "I", 5, "P", "indirect", "acid"),
        ["spell-toxic-wave"] = new("combat", 139, "P", "LOS (A)", "I", 6, "P", "indirect", "acid"),
        ["spell-clout"] = new("combat", 139, "P", "LOS", "I", 3, "S", "indirect"),
        ["spell-astral-armor"] = new("manipulation", 139, "M", "T", "S", 3),
        ["spell-thunderclap"] = new("manipulation", 140, "P", "LOS (A)", "S", 3),
        ["spell-darkness"] = new("manipulation", 140, "P", "LOS (A)", "S", 3),
        ["spell-elemental-armor"] = new("manipulation", 140, "M", "T", "S", 5),
        ["spell-vehicle-armor"] = new("manipulation", 140, "P", "T", "S", 6),
        ["spell-focus-boost"] = new("manipulation", 140, "M", "T", "L", 7),
        ["spell-control-thoughts"] = new("manipulation", 140, "M", "LOS", "L", 4),
        ["spell-control-actions"] = new("manipulation", 140, "M", "LOS", "L", 4),
        ["spell-animate-wood"] = new("manipulation", 141, "P", "LOS", "L", 4),
        ["spell-shape-wood"] = new("manipulation", 142, "P", "LOS", "S", 3),
        ["spell-animate-plastic"] = new("manipulation", 142, "P", "LOS", "L", 3),
        ["spell-shape-plastic"] = new("manipulation", 142, "P", "LOS", "S", 2),
        ["spell-levitate"] = new("manipulation", 142, "P", "LOS", "S", 6),
        ["spell-light"] = new("manipulation", 142, "P", "LOS (A)", "S", 3),
        ["spell-mana-barrier"] = new("manipulation", 142, "M", "LOS (A)", "S", 5),
        ["spell-animate-metal"] = new("manipulation", 142, "P", "LOS", "L", 6),
        ["spell-shape-metal"] = new("manipulation", 142, "P", "LOS", "S", 5),
        ["spell-armor"] = new("manipulation", 142, "P", "T", "S", 4),
        ["spell-physical-barrier"] = new("manipulation", 143, "P", "LOS (A)", "S", 6),
        ["spell-fling"] = new("manipulation", 143, "P", "LOS", "I", 5),
        ["spell-animate-stone"] = new("manipulation", 143, "P", "LOS", "L", 5),
        ["spell-shape-stone"] = new("manipulation", 143, "P", "LOS", "S", 4),
        ["spell-overclock"] = new("manipulation", 143, "P", "LOS", "S", 4),
        ["spell-reinforce"] = new("manipulation", 143, "P", "LOS (A)", "S", 4),
        ["spell-detect-enemies"] = new("detection", 144, "M", "T", "S", 3),
        ["spell-mind-probe"] = new("detection", 144, "P", "T", "S", 5),
        ["spell-analyze-device"] = new("detection", 144, "P", "T", "S", 2),
        ["spell-clairaudience"] = new("detection", 144, "M", "T", "S", 3),
        ["spell-clairvoyance"] = new("detection", 144, "M", "T", "S", 3),
        ["spell-combat-sense"] = new("detection", 144, "M", "T", "S", 3),
        ["spell-detect-life"] = new("detection", 144, "M", "T", "S", 3),
        ["spell-analyze-magic"] = new("detection", 144, "P", "T", "S", 3),
        ["spell-detect-magic"] = new("detection", 145, "M", "T", "S", 4),
        ["spell-mindlink"] = new("detection", 145, "M", "T", "S", 3),
        ["spell-analyze-truth"] = new("detection", 145, "M", "T", "S", 3),
    };

    internal static IReadOnlyList<XElement> Materialize(Sr6CreationSpellOption option, out bool complete)
    {
        var canonical = Sr6CreationSpellRules.Catalog().SingleOrDefault(row => row.Id == option.Id);
        string? missing = null;
        Statistics? stats = null;
        if (canonical is null || canonical != option)
            missing = "canonical-spell-identity";
        else if (option.Kind != "spell")
            missing = "ritual-runtime-profile";
        else if (!Profiles.TryGetValue(option.Id, out stats))
            missing = "spell-runtime-profile";
        else if (stats.Duration == "L")
            missing = "maximum-duration-rule";
        else if (stats.Category != option.CategoryId
            || option.SourceAnchorId != "sr6_core_de_2024:p" + stats.Page.ToString(CultureInfo.InvariantCulture)
            || stats.Type is not ("M" or "P") || stats.Range is not ("T" or "LOS" or "LOS (A)")
            || stats.Duration is not ("I" or "S" or "P") || stats.Drain < 0
            || (option.CategoryId == "combat" && (stats.Damage is not ("P" or "S")
                || stats.Combat is not ("direct" or "indirect")))
            || (option.CategoryId != "combat" && (stats.Damage is not null || stats.Combat is not null
                || stats.Element is not null))
            || stats.Element is not (null or "electricity" or "cold" or "fire" or "acid")
            || (stats.Element is not null && (stats.Combat != "indirect" || stats.Damage != "P")))
            missing = "invalid-runtime-profile";

        complete = missing is null;
        var profile = new XElement("sr6spellprofile", new XAttribute("schema", "1"),
            new XAttribute("scope", "base-formula-statistics"),
            new XElement("source", option.SourceAnchorId),
            new XElement("sourcesha256", Sr6CreationFoundationRules.CoreSourceSha256));
        var result = new List<XElement> { new("sr6runtimestatsavailable", complete), profile };
        if (!complete)
        {
            profile.Add(new XElement("missing", missing));
            return result;
        }

        // CharacterSectionService.ParseSpells consumes these established fields.
        // Never serialize a partial profile as usable shared spell statistics.
        Statistics known = stats!;
        result.AddRange([new("type", known.Type), new("range", known.Range),
            new("duration", known.Duration), new("dv", known.Drain)]);
        if (known.Combat is not null)
        {
            result.Add(new("damage", known.Damage));
            profile.Add(new XElement("combatkind", known.Combat));
        }
        if (known.Element is not null) profile.Add(new XElement("damageelement", known.Element));
        return result;
    }
}
