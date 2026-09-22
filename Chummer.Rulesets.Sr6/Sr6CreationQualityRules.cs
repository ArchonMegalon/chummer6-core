using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Creation purchase authority, not runtime activation of situational effects.</summary>
public static class Sr6CreationQualityRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p68-81";
    public const string MetatypeUpgradeSourceAnchor = "sr6_official_faq:racial-quality-upgrades";

    public static IReadOnlyList<Sr6CreationQualityOption> Options(Sr6CreationFoundationPreview foundation)
    {
        var values = new List<Sr6CreationQualityOption>();
        void Add(string id, string name, int cost, int page)
            => values.Add(new(id, name, id, cost, "sr6_core_de_2024:p" + page));
        Add("analytical-mind", "Analytischer Geist", 3, 74);
        Add("astral-chameleon", "Astrales Chamäleon", 9, 74);
        Add("ambidextrous", "Beidhändigkeit", 4, 74);
        Add("first-impression", "Erster Eindruck", 12, 74);
        Add("photographic-memory", "Fotografisches Gedächtnis", 12, 74);
        Add("long-reach", "Große Reichweite", 12, 74);
        Add("double-jointed", "Gummigelenke", 12, 75);
        Add("hardening", "Härtung", 10, 75);
        Add("high-pain-tolerance", "Hohe Schmerztoleranz", 7, 75);
        Add("catlike", "Katzenhaft", 12, 75);
        Add("magic-resistance", "Magieresistenz", 8, 75);
        Add("gearhead", "Meistermechaniker", 10, 75);
        Add("human-looking", "Menschliches Aussehen", 8, 75);
        Add("guts", "Mut", 12, 75);
        Add("pathogen-resistance", "Pathogenresistenz", 12, 75);
        Add("quick-healer", "Schnellheilung", 8, 76);
        Add("jury-rigger", "Technisches Improvisationstalent", 12, 76);
        Add("toxin-resistance", "Toxinresistenz", 12, 76);
        Add("blandness", "Unauffälligkeit", 8, 76);
        Add("strong-willed", "Willensstark", 12, 77);
        Add("toughness", "Zähigkeit", 12, 77);
        Add("ar-vertigo", "AR-Desorientierung", -10, 78);
        Add("astral-beacon", "Astrales Leuchtfeuer", -10, 78);
        Add("distinctive-style", "Auffälliger Stil", -6, 78);
        Add("notoriety", "Berüchtigt", -8, 78);
        Add("elf-poser", "Elfenposer", -6, 79);
        Add("scorched", "Gezeichnet", -6, 79);
        Add("gremlins", "Gremlins", -6, 80);
        Add("shaky-hands", "Händezittern", -4, 80);
        Add("implant-rejection", "Immunabstoßung", -8, 80);
        Add("combat-paralysis", "Kampflähmung", -8, 80);
        Add("low-pain-tolerance", "Niedrige Schmerztoleranz", -10, 80);
        Add("ork-poser", "Orkposer", -6, 80);
        Add("insomnia", "Schlaflosigkeit", -4, 80);
        Add("weak-immune-system", "Schwaches Immunsystem", -8, 80);
        Add("simsense-vertigo", "Simsinn-Desorientierung", -6, 81);
        Add("uneducated", "Ungebildet", -6, 81);
        Add("uncouth", "Ungehobelt", -6, 81);
        Add("bad-luck", "Unglück", -10, 81);
        foreach (string id in Sr6CreationAttributeIds.Ordered.Take(8))
            values.Add(new("exceptional-" + id, "Außergewöhnliches Attribut", "exceptional-" + id,
                12, "sr6_core_de_2024:p74", AttributeId: id));
        foreach (string id in Sr6CreationSkillIds.Ordered)
            values.Add(new("aptitude-" + id, "Talentiert", "aptitude", 12, "sr6_core_de_2024:p76", SkillId: id));
        AddLevels("focused-concentration", "Erhöhte Konzentrationsfähigkeit", 3, 12, 74);
        int builtToughInnate = foundation.Selection.MetatypeId switch { "ork" => 1, "troll" => 2, _ => 0 };
        AddLevels("built-tough", "Robust Gebaut", 4, 4, 76, builtToughInnate);
        AddLevels("will-to-live", "Überlebenswille", 3, 8, 76);
        // Ten is the largest purchasable reduction under supported natural creation
        // caps (Willpower <= 8). The actual final Willpower is checked after Karma.
        AddLevels("glass-jaw", "Glaskinn", 10, -4, 80);
        AddLevels("dependents", "Verpflichtungen", 3, -4, 81);
        return values.Select(row =>
        {
            bool allowed = row.Id switch
            {
                "human-looking" => foundation.Selection.MetatypeId is "elf" or "ork" or "dwarf",
                "elf-poser" => foundation.Selection.MetatypeId is "human" or "ork",
                "ork-poser" => foundation.Selection.MetatypeId is "human" or "elf",
                "implant-rejection" => foundation.BaseMagic == 0 && foundation.BaseResonance == 0,
                // Dwarfs already receive this unranked advantage for free (p66).
                "toxin-resistance" => foundation.Selection.MetatypeId != "dwarf",
                _ => true
            };
            return row with { Available = allowed, UnavailableReason = allowed ? null : Sr6CreationQualityBlockers.Unavailable };
        }).ToArray();

        void AddLevels(string family, string name, int maximum, int cost, int page, int innate = 0)
        {
            for (int rating = innate + 1; rating <= maximum; rating++)
                values.Add(new(family + "-" + rating, name, family, (rating - innate) * cost,
                    "sr6_core_de_2024:p" + page) { Rating = new(rating, innate, rating - innate, cost) });
        }
    }

    public static CharacterCreationFoundationResult<Sr6CreationQualityPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationQualitySelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeQualities(selection, out var frozen) || frozen is null)
            return Fail(Sr6CreationQualityBlockers.InvalidSelection);
        var catalog = Options(foundation);
        var values = new List<Sr6CreationQualityOption>();
        foreach (string id in frozen.OptionIds)
        {
            var option = catalog.SingleOrDefault(row => row.Id == id);
            if (option is null) return Fail(Sr6CreationQualityBlockers.InvalidSelection);
            if (!option.Available) return Fail(option.UnavailableReason!);
            values.Add(option);
        }
        if (values.Select(row => row.FamilyId).Distinct(StringComparer.Ordinal).Count() != values.Count)
            return Fail(Sr6CreationQualityBlockers.Conflict);
        // A permanent distinctive appearance explicitly removes Blandness (p76).
        if (frozen.OptionIds.Contains("blandness", StringComparer.Ordinal)
            && frozen.OptionIds.Contains("distinctive-style", StringComparer.Ordinal))
            return Fail(Sr6CreationQualityBlockers.Conflict);
        int cost = values.Where(row => row.KarmaCost > 0).Sum(row => row.KarmaCost);
        int bonus = -values.Where(row => row.KarmaCost < 0).Sum(row => row.KarmaCost);
        int net = bonus - cost;
        if (net > Sr6QualityProvider.MaximumNetBonusKarmaFromQualities) return Fail(Sr6CreationQualityBlockers.LimitExceeded);
        if (50 + net < 0) return Fail(Sr6CreationQualityBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-qualities.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, foundation.Selection.MetatypeId, foundation.Selection.TalentId,
            Values = values, MaximumChoices = 6, MaximumNetBonus = 20, BaseCustomizationKarma = 50
        });
        bool metatypeUpgrade = values.Any(row => row.Rating is { Innate: > 0 });
        if (metatypeUpgrade)
            authority = Sr6CreationFoundationIntegrity.Digest(new
            {
                Schema = "chummer.sr6.creation-quality-upgrade.v1", PurchaseAuthority = authority,
                MetatypeUpgradeSourceAnchor, EachUpgradeCountsAsOneChoice = true, ChargeOnlyAddedLevels = true
            });
        return new(CharacterCreationFoundationOutcomes.Success, new(values.ToArray(), cost, bonus, net,
            50 + net, 6, 20, authority, metatypeUpgrade ? [SourceAnchor, MetatypeUpgradeSourceAnchor] : [SourceAnchor]), []);
    }

    /// <summary>Run after pool and Karma evaluation, never against client-provided ratings.</summary>
    public static string? ValidateDependentRatings(Sr6CreationFoundationPreview foundation)
    {
        var glassJaw = foundation.Qualities?.Values.SingleOrDefault(row => row.FamilyId == "glass-jaw");
        if (glassJaw?.Rating is not { } rating) return null;
        if (foundation.Attributes is null) return Sr6CreationQualityBlockers.AttributesRequired;
        int willpower = Sr6CreationKarmaRules.AttributeRating(foundation, "Willpower");
        int stunBoxes = new Sr6DerivedStatsProvider().StunConditionMonitor(willpower);
        return stunBoxes - rating.Total < 2 ? Sr6CreationQualityBlockers.RatingUnavailable : null;
    }

    public static int AttributeMaximumBonus(Sr6CreationFoundationPreview foundation, string id)
        => foundation.Qualities?.Values.Any(row => row.AttributeId == id) == true ? 1 : 0;

    public static int SkillMaximumBonus(Sr6CreationFoundationPreview foundation, string id)
        => foundation.Qualities?.Values.Any(row => row.SkillId == id) == true ? 1 : 0;

    private static CharacterCreationFoundationResult<Sr6CreationQualityPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
