using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>German 2024 core formulas. Casting choices (attribute, element, target,
/// trigger) are not distinct learned spells. Alchemy uses a known spell, not an SR5 duplicate.</summary>
public static class Sr6CreationSpellRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p67-68,134-148,152";

    public static IReadOnlyList<Sr6CreationSpellOption> Catalog()
    {
        var rows = new List<Sr6CreationSpellOption>();
        Add("health", 134, ("decrease-attribute", "Attribut Senken"), ("increase-attribute", "Attribut Steigern"));
        Add("health", 135, ("antidote", "Gegenmittel"), ("heal", "Heilen"), ("cooling-heal", "Kühlendes Heilen"),
            ("neutralizing-heal", "Neutralisierendes Heilen"), ("increase-reflexes", "Reflexe Steigern"));
        Add("health", 136, ("resist-pain", "Schmerzresistenz"), ("stabilize", "Stabilisieren"), ("warming-heal", "Wärmendes Heilen"));
        Add("illusion", 136, ("chaos", "Chaos"), ("mask", "Maske"), ("physical-mask", "Physische Maske"),
            ("agony", "Schmerz"), ("hush", "Schweigen"), ("sensor-sneak", "Sensortäuschung"));
        Add("illusion", 137, ("silence", "Stille"), ("trid-phantasm", "Trideo-Trugbild"), ("phantasm", "Trugbild"),
            ("invisibility", "Unsichtbarkeit"), ("improved-invisibility", "Verbesserte Unsichtbarkeit"), ("confusion", "Verwirrung"));
        Add("combat", 138, ("stunball", "Betäubungsball"), ("stunbolt", "Betäubungsblitz"), ("lightning-bolt", "Blitzstrahl"),
            ("blast", "Druckwelle"), ("ice-spear", "Eisspeer"), ("ice-storm", "Eissturm"), ("powerball", "Energieball"));
        Add("combat", 139, ("powerbolt", "Energieblitz"), ("fireball", "Feuerball"), ("flamethrower", "Flammenstoß"),
            ("ball-lightning", "Kugelblitz"), ("manaball", "Manaball"), ("manabolt", "Manablitz"),
            ("acid-stream", "Säurestrahl"), ("toxic-wave", "Säurewelle"), ("clout", "Stoß"));
        Add("manipulation", 139, ("astral-armor", "Astralpanzerung"));
        Add("manipulation", 140, ("thunderclap", "Donnerknall"), ("darkness", "Dunkelheit"), ("elemental-armor", "Elementarpanzerung"),
            ("vehicle-armor", "Fahrzeugpanzerung"), ("focus-boost", "Fokus-Boost"), ("control-thoughts", "Gedanken Beherrschen"),
            ("control-actions", "Handlungen Beherrschen"));
        Add("manipulation", 141, ("animate-wood", "Holz Beleben"));
        Add("manipulation", 142, ("shape-wood", "Holz Formen"), ("animate-plastic", "Kunststoff Beleben"),
            ("shape-plastic", "Kunststoff Formen"), ("levitate", "Levitieren"), ("light", "Licht"),
            ("mana-barrier", "Manabarriere"), ("animate-metal", "Metall Beleben"), ("shape-metal", "Metall Formen"), ("armor", "Panzerung"));
        Add("manipulation", 143, ("physical-barrier", "Physische Barriere"), ("fling", "Schleuder"),
            ("animate-stone", "Stein Beleben"), ("shape-stone", "Stein Formen"), ("overclock", "Übertakten"), ("reinforce", "Wand Verstärken"));
        Add("detection", 144, ("detect-enemies", "Feinde Entdecken"), ("mind-probe", "Geistessonde"),
            ("analyze-device", "Gerät Analysieren"), ("clairaudience", "Hellhören"), ("clairvoyance", "Hellsicht"),
            ("combat-sense", "Kampfsinn"), ("detect-life", "Leben Entdecken"), ("analyze-magic", "Magie Analysieren"));
        Add("detection", 145, ("detect-magic", "Magie Entdecken"), ("mindlink", "Telepathie"), ("analyze-truth", "Wahrheit Prüfen"));
        Add("ritual", 147, ("remote-sensing", "Fernwahrnehmung"), ("prodigal-spell", "Fernzauber"), ("curse", "Fluch"),
            ("dominion", "Herrschaft"), ("ward", "Hüter"), ("circle-of-healing", "Kreis der Heilung"), ("circle-of-protection", "Schutzkreis"));
        Add("ritual", 148, ("watcher", "Watcher"));
        return rows.AsReadOnly();

        void Add(string category, int page, params (string Id, string Name)[] entries)
        {
            string kind = category == "ritual" ? "ritual" : "spell";
            string anchor = "sr6_core_de_2024:p" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);
            rows.AddRange(entries.Select(entry => new Sr6CreationSpellOption(kind + "-" + entry.Id, entry.Name, kind, category, anchor)));
        }
    }

    private static string? UseId(Sr6CreationFoundationPreview foundation)
        => foundation.Selection.TalentId switch
        {
            "magician" or "mystic-adept" => "sorcery-and-enchanting",
            "aspected-magician" => foundation.Selection.Skills?.AspectedSkillId switch
            { "Sorcery" => "sorcery", "Enchanting" => "enchanting", _ => null },
            _ => null
        };

    public static IReadOnlyList<Sr6CreationSpellOption>? Options(Sr6CreationFoundationPreview foundation)
        => foundation.TalentAllocation is null || UseId(foundation) is not { } use ? null
            : Catalog().Where(row => use != "enchanting" || row.Kind == "spell").ToArray();

    public static CharacterCreationFoundationResult<Sr6CreationSpellPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationSpellSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeSpells(selection, out var frozen))
            return Fail(Sr6CreationSpellBlockers.InvalidSelection);
        if (foundation.TalentAllocation is not { } talent || UseId(foundation) is not { } use)
            return Fail(Sr6CreationSpellBlockers.TalentRequired);
        bool alchemyOnly = use == "enchanting";
        int limit = alchemyOnly ? talent.AlchemicalSpellLimit : talent.SpellOrRitualLimit;
        int freeSlots = alchemyOnly ? talent.FreeAlchemicalSpellSlots : talent.FreeSpellOrRitualSlots;
        if (frozen!.CatalogIds.Count > limit) return Fail(Sr6CreationSpellBlockers.LimitExceeded);
        var catalog = Options(foundation)!;
        var values = new List<Sr6CreationSpellOption>();
        foreach (string id in frozen.CatalogIds)
        {
            var option = catalog.SingleOrDefault(row => row.Id == id);
            if (option is null) return Fail(Sr6CreationSpellBlockers.CatalogUnavailable);
            values.Add(option);
        }
        int free = Math.Min(values.Count, freeSlots);
        int cost = (values.Count - free) * talent.CharacterPointsPerSpellOrForm;
        if (foundation.PointBuy is { } points && cost > points.PointsRemaining)
            return Fail(Sr6CreationPointBuyBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-spells.v1", SourceAnchor,
            Sr6CreationFoundationRules.CoreSourceSha256, Catalog = catalog,
            TalentAuthority = talent.AuthorityDigest, Selection = frozen, use, limit, cost, free
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values.ToArray(), limit, limit - values.Count, free, cost, use, authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationSpellPreview> Fail(string blocker)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
