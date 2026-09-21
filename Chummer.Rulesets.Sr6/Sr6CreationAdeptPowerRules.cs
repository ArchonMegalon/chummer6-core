using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Known powers only, before equipment/quality modifiers and runtime effects.
/// Integer quarter-points avoid floating-point budget drift.</summary>
public static class Sr6CreationAdeptPowerRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p95,158-160";
    public const string CombatSkillAnchor = "sr6_official_faq:improved-ability-combat-skills";
    public const string CombatSkillSource = "https://shadowrunsixthworld.com/shadowrun-sixth-world-faq/";

    public static IReadOnlyList<Sr6CreationAdeptPowerOption>? Options(Sr6CreationFoundationPreview foundation)
    {
        if (foundation.Selection.TalentId is not ("adept" or "mystic-adept")
            || foundation.TalentAllocation is not { } talent || foundation.Attributes is null) return null;
        int magic = talent.Magic;
        var rows = new List<Sr6CreationAdeptPowerOption>();
        Add("adrenaline-boost", "Adrenalinschub", 158, 1, magic);
        Add("astral-perception", "Astrale Wahrnehmung", 158, 4, 1);
        foreach (string id in new[] { "Body", "Agility", "Reaction", "Strength" })
            Add("attribute-boost-" + id.ToLowerInvariant(), "Attributsschub", 158, 1, magic, "attribute", id);
        Add("rapid-healing", "Beschleunigte Heilung", 158, 2, magic);
        Add("enhanced-accuracy", "Erhöhte Präzision", 158, 2, 1);
        Add("danger-sense", "Gefahrensinn", 158, 2, 1);
        foreach (string id in new[] { "sight", "hearing", "touch", "smell", "taste" })
            Add("enhanced-sense-" + id, "Geschärfter Sinn", 159, 1, 1, "sense", id);
        Add("combat-sense", "Kampfsinn", 159, 2, magic);
        Add("kinesics", "Körpersprache", 159, 1, 1);
        Add("critical-strike", "Kritischer Schlag", 159, 4, magic);
        Add("magic-resistance", "Magieresistenz", 159, 2, 1);
        Add("mystic-armor", "Mystische Panzerung", 159, 1, magic);
        Add("direction-sense", "Richtungssinn", 159, 1, 1);
        Add("pain-resistance", "Schmerzresistenz", 159, 1, magic);
        Add("traceless-walk", "Spurloser Schritt", 159, 2, 1);
        Add("voice-control", "Stimmkontrolle", 159, 2, 1);
        Add("killing-hands", "Todeskralle", 159, 2, 1);
        foreach (string id in Sr6CreationSkillIds.Ordered)
        {
            // The German profile excludes Magic-linked skills; Tasking is unavailable to adepts.
            if (id is "Sorcery" or "Conjuring" or "Enchanting" or "Tasking") continue;
            int natural = foundation.Skills?.Values.SingleOrDefault(row => row.SkillId == id)?.Rating ?? 0;
            int maximum = Math.Min(magic, Math.Min(4, (natural + 1) / 2));
            if (id == "Astral" && !HasAstralPerception(foundation.Selection)) maximum = 0;
            bool combat = id is "CloseCombat" or "ExoticWeapons" or "Firearms";
            bool mixed = id is "Astral" or "Athletics" or "Cracking" or "Engineering" or "Piloting";
            string key = "improved-ability-" + id.ToLowerInvariant();
            Add(key + "-all", "Verbesserte Fertigkeit", 159, combat || mixed ? 4 : 2, maximum, "skill", id);
            if (mixed) Add(key + "-noncombat", "Verbesserte Fertigkeit", 159, 2, maximum, "skill", id, "noncombat");
        }
        Add("improved-reflexes", "Verbesserte Reflexe", 160, 4, Math.Min(4, magic));
        Add("improved-perception", "Verbesserte Wahrnehmung", 160, 2, 1);
        foreach (string id in new[] { "Body", "Agility", "Reaction", "Strength" })
        {
            int natural = foundation.Attributes.Values.Single(row => row.AttributeId == id).Value;
            Add("improved-attribute-" + id.ToLowerInvariant(), "Verbessertes Körperliches Attribut", 160,
                4, Math.Min(magic, Math.Min(4, (natural + 1) / 2)), "attribute", id);
        }
        Add("wall-running", "Wandlaufen", 160, 2, 1);
        return rows.ToArray();

        void Add(string id, string name, int page, int cost, int maximum, string? kind = null, string? subject = null, string use = "all")
            => rows.Add(new(id, name, kind, subject, use, cost, maximum,
                "sr6_core_de_2024:p" + page.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    // Shape is frozen before skill evaluation; final power legality is checked in the same preview.
    public static bool HasAstralPerception(Sr6CreationFoundationSelection selection)
        => selection.AdeptPowers?.Choices.Any(row => row.CatalogId == "astral-perception" && row.Rating == 1) == true;

    public static CharacterCreationFoundationResult<Sr6CreationAdeptPowerPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationAdeptPowerSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeAdeptPowers(selection, out var frozen))
            return Fail(Sr6CreationAdeptPowerBlockers.InvalidSelection);
        if (Options(foundation) is not { } options) return Fail(Sr6CreationAdeptPowerBlockers.TalentRequired);
        var values = new List<Sr6CreationAdeptPowerValue>();
        var skills = new HashSet<string>(StringComparer.Ordinal);
        foreach (var choice in frozen!.Choices)
        {
            var option = options.SingleOrDefault(row => row.Id == choice.CatalogId);
            if (option is null) return Fail(Sr6CreationAdeptPowerBlockers.CatalogUnavailable);
            if (choice.Rating > option.MaximumRating) return Fail(Sr6CreationAdeptPowerBlockers.RatingExceeded);
            if (option.SubjectKind == "skill" && !skills.Add(option.SubjectId!))
                return Fail(Sr6CreationAdeptPowerBlockers.InvalidSelection);
            values.Add(new(option, choice.Rating, choice.Rating * option.QuarterPointsPerRating));
        }
        int budget = foundation.TalentAllocation!.PowerPointBudget * 4;
        int spent = values.Sum(row => row.QuarterPointsSpent);
        if (spent > budget) return Fail(Sr6CreationAdeptPowerBlockers.BudgetExceeded);
        string[] warnings = values.Any(row => row.Option.Id == "improved-reflexes")
            ? ["improved-reflexes-no-stacking"] : [];
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-adept-powers.v1", SourceAnchor, CombatSkillAnchor, CombatSkillSource,
            Sr6CreationFoundationRules.CoreSourceSha256, Options = options,
            TalentAuthority = foundation.TalentAllocation.AuthorityDigest,
            AttributeAuthority = foundation.Attributes!.AuthorityDigest, SkillAuthority = foundation.Skills?.AuthorityDigest,
            Selection = frozen, budget, spent, warnings
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values.ToArray(), budget, spent, budget - spent, warnings, authority, [SourceAnchor, CombatSkillAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationAdeptPowerPreview> Fail(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
