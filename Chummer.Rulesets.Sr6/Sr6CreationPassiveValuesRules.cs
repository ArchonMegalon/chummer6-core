using System.Globalization;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Only projects a revalidated saved draft. Never changes historical previews
/// or assumes a purchased item is worn. Conditional and activated powers stay inactive.</summary>
internal static class Sr6CreationPassiveValuesRules
{
    internal static Sr6CreationPassiveValues? Project(Sr6CreationFoundationState state, Sr6CreationNaturalValues? natural)
    {
        if (state.Selection is not { } saved || natural?.Attributes is null) return null;
        var warnings = new List<string>();
        int Power(string id) => saved.AdeptPowers?.Powers.SingleOrDefault(row => row.Option.Id == id)?.Rating ?? 0;
        int Quality(string family) => saved.Qualities?.Values.SingleOrDefault(row => row.FamilyId == family)?.Rating?.Total ?? 0;
        int reflexes = Power("improved-reflexes");
        bool reactionConflict = reflexes > 0 && Power("improved-attribute-reaction") > 0;
        if (reactionConflict) warnings.Add("reaction-power-conflict");
        if (saved.Qualities?.Values.Any(row => row.Id == "combat-paralysis") == true)
            warnings.Add("combat-paralysis-rolled-total");

        var attributes = natural.Attributes.Select(row =>
        {
            int bonus = Power("improved-attribute-" + row.AttributeId.ToLowerInvariant());
            if (row.AttributeId == "Reaction")
            {
                if (reactionConflict) return new Sr6CreationPassiveAttributeValue(row.AttributeId, row.Rating, null, null);
                bonus += reflexes;
            }
            return new Sr6CreationPassiveAttributeValue(row.AttributeId, row.Rating, bonus, row.Rating + bonus);
        }).ToArray();
        int? Rating(string id) => attributes.Single(row => row.AttributeId == id).Rating;
        var skills = natural.Skills?.Select(row =>
        {
            int always = Power("improved-ability-" + row.SkillId.ToLowerInvariant() + "-all");
            int noncombat = Power("improved-ability-" + row.SkillId.ToLowerInvariant() + "-noncombat");
            return new Sr6CreationPassiveSkillValue(row.SkillId, row.Rating, always, noncombat,
                row.Rating + always, row.Rating + always + noncombat);
        }).ToArray();

        var rules = new Sr6DerivedStatsProvider();
        int body = Rating("Body")!.Value, willpower = Rating("Willpower")!.Value;
        int intuition = Rating("Intuition")!.Value, strength = Rating("Strength")!.Value;
        int? reaction = Rating("Reaction");
        int innateTough = saved.Selection.MetatypeId switch { "ork" => 1, "troll" => 2, _ => 0 };
        // Purchased upgrades already include the innate levels. Do not add them twice.
        int builtTough = Math.Max(innateTough, Quality("built-tough"));
        int glassJaw = Quality("glass-jaw"), willToLive = Quality("will-to-live");
        int dermal = saved.Selection.MetatypeId == "troll" ? 1 : 0;
        int armor = Power("mystic-armor"), combatSense = Power("combat-sense");
        var derived = new List<Sr6CreationDerivedValue>();
        Add("physical-monitor", rules.PhysicalConditionMonitor(body) + builtTough,
            $"8 + ceil({body} / 2) + {builtTough}", "40,66,76");
        Add("stun-monitor", rules.StunConditionMonitor(willpower) - glassJaw,
            $"8 + ceil({willpower} / 2) - {glassJaw}", "40,80");
        Add("overflow", body * 2 + willToLive * 2, $"{body} × 2 + {willToLive} × 2", "76,124");
        Add("initiative-base", reaction is { } r ? rules.InitiativeRank(r, intuition) : null,
            $"{reaction} + {intuition}", "41,160");
        Add("initiative-dice", reactionConflict ? null : 1 + reflexes, $"1 + {reflexes}", "41,160");
        Add("unarmored-defense-rating", rules.DefenseRating(body, 0, dermal + armor),
            $"{body} + {dermal} + {armor}", "41,66,74,159");
        Add("defense-dice-pool", reaction + intuition + combatSense,
            $"{reaction} + {intuition} + {combatSense}", "108,159");
        Add("unarmed-attack-rating", reaction is { } attackReaction ? rules.UnarmedAttackRatingClose(strength, attackReaction) : null,
            $"{strength} + {reaction}", "41");
        return new(Array.AsReadOnly(attributes), skills is null ? null : Array.AsReadOnly(skills),
            derived.AsReadOnly(), warnings.AsReadOnly(), Array.AsReadOnly(new[]
            { "sr6_core_de_2024:p40-41,66,74,76,80,108,124,159-160" }));

        void Add(string id, int? value, FormattableString expression, string pages)
            => derived.Add(new(id, value, value is null ? string.Empty
                : expression.ToString(CultureInfo.InvariantCulture) + " = " + value.Value.ToString(CultureInfo.InvariantCulture),
                "sr6_core_de_2024:p" + pages));
    }
}
