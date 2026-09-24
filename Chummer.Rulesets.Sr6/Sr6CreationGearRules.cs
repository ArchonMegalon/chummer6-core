using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using System.Globalization;

namespace Chummer.Rulesets.Sr6;

/// <summary>SR6 core-book purchase basket. No SR5 prices, active equipment effects or finalization.</summary>
public static class Sr6CreationGearRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p69-70,244,247,249-259,266,268-269,273,283";
    public const int MaximumAvailability = 7;
    public const decimal MaximumCarryOver = 5000m;

    public static IReadOnlyList<Sr6CreationGearOption> Catalog(string metatype)
    {
        var rows = new List<Sr6CreationGearOption>();
        Add("combat-axe", "Kampfaxt", "melee", 4, "legal", 500, 249);
        Add("combat-knife", "Kampfmesser", "melee", 2, "legal", 220, 249);
        Add("katana", "Katana", "melee", 3, "legal", 350, 249);
        Add("knife", "Messer", "melee", 1, "legal", 20, 249);
        Add("sword", "Schwert", "melee", 3, "legal", 320, 249);
        Add("polearm", "Stangenwaffe", "melee", 2, "legal", 210, 249);
        Add("survival-knife", "Überlebensmesser", "melee", 2, "legal", 220, 249);
        Add("forearm-snap-blades", "Unterarmschnappklingen", "melee", 3, "legal", 185, 249);
        Add("stun-baton", "Betäubungsschlagstock", "melee", 2, "legal", 600, 249);
        Add("club", "Knüppel", "melee", 1, "legal", 65, 249);
        Add("staff", "Stab", "melee", 1, "legal", 150, 249);
        Add("extendable-baton", "Teleskopschlagstock", "melee", 2, "legal", 50, 249);
        Add("extendable-staff", "Teleskopstab", "melee", 2, "legal", 250, 249);
        Add("sap", "Totschläger", "melee", 1, "legal", 75, 249);
        Add("bullwhip", "Bullenpeitsche", "melee", 4, "legal", 255, 250);
        Add("monofilament-whip", "Monofilamentpeitsche", "melee", 6, "illegal", 1300, 250);
        Add("bike-chain", "Motorradkette", "melee", 1, "legal", 15, 250);
        Add("knucks", "Schlagring", "melee", 1, "legal", 100, 250);
        Add("shock-gloves", "Schockhandschuhe", "melee", 4, "legal", 790, 250);
        Add("light-crossbow", "Armbrust, leicht", "projectile", 3, "legal", 150, 250);
        Add("medium-crossbow", "Armbrust, mittel", "projectile", 3, "licensed", 290, 250);
        Add("heavy-crossbow", "Armbrust, schwer", "projectile", 4, "licensed", 425, 250);
        Add("shuriken", "Shuriken", "projectile", 2, "legal", 160, 250);
        Add("throwing-knife", "Wurfmesser", "projectile", 2, "legal", 155, 250);
        Add("defiance-super-shock", "Defiance Super Shock", "firearm", 1, "legal", 340, 251);
        Add("yamaha-pulsar-i", "Yamaha Pulsar I", "firearm", 1, "legal", 325, 251);
        Add("yamaha-pulsar-ii", "Yamaha Pulsar II", "firearm", 1, "legal", 350, 251);
        Add("fichetti-tiffani-needler", "Fichetti Tiffani Needler", "firearm", 2, "legal", 435, 251);
        Add("streetline-special", "Streetline Special", "firearm", 2, "legal", 200, 251);
        Add("walther-palm-pistol", "Walther Palm Pistol", "firearm", 2, "legal", 345, 251);
        Add("ares-light-fire-70", "Ares Light Fire 70", "firearm", 3, "licensed", 350, 252);
        Add("ares-light-fire-75", "Ares Light Fire 75", "firearm", 3, "licensed", 400, 252);
        Add("beretta-101t", "Beretta 101T", "firearm", 2, "licensed", 260, 252);
        Add("beretta-201t", "Beretta 201T", "firearm", 3, "licensed", 460, 252);
        Add("colt-america-l36", "Colt America L36", "firearm", 2, "licensed", 230, 252);
        Add("fichetti-security-600", "Fichetti Security 600", "firearm", 3, "licensed", 390, 252);
        Add("ruger-redhawk", "Ruger Redhawk", "firearm", 2, "licensed", 250, 252);
        Add("ares-crusader-ii", "Ares Crusader II", "firearm", 4, "licensed", 520, 253);
        Add("ceska-black-scorpion", "Ceska Black Scorpion", "firearm", 3, "licensed", 510, 253);
        Add("steyr-tmp", "Steyr TMP", "firearm", 3, "licensed", 690, 253);
        Add("ares-predator-vi", "Ares Predator VI", "firearm", 2, "licensed", 750, 254);
        Add("ares-viper-slivergun", "Ares Viper Slivergun", "firearm", 4, "licensed", 610, 254);
        Add("browning-ultra-power", "Browning Ultra Power", "firearm", 2, "licensed", 315, 254);
        Add("colt-government-2076", "Colt Government 2076", "firearm", 3, "licensed", 275, 254);
        Add("colt-manhunter", "Colt Manhunter", "firearm", 3, "licensed", 500, 254);
        Add("ruger-super-warhawk", "Ruger Super Warhawk", "firearm", 3, "licensed", 400, 254);
        Add("colt-cobra-tz100", "Colt Cobra TZ-100", "firearm", 2, "licensed", 730, 255);
        Add("colt-cobra-tz110", "Colt Cobra TZ-110", "firearm", 2, "licensed", 785, 255);
        Add("colt-cobra-tz120", "Colt Cobra TZ-120", "firearm", 3, "licensed", 840, 255);
        Add("fn-p93-praetor", "FN P93 Praetor", "firearm", 4, "licensed", 925, 255);
        Add("hk227", "HK-227", "firearm", 3, "licensed", 825, 255);
        Add("ingram-smartgun-xi", "Ingram Smartgun XI", "firearm", 3, "licensed", 750, 255);
        Add("sck-model-100", "SCK Modell 100", "firearm", 3, "licensed", 725, 255);
        Add("uzi-v", "Uzi V", "firearm", 2, "licensed", 455, 255);
        Add("defiance-t250", "Defiance T-250", "firearm", 2, "licensed", 330, 255);
        Add("defiance-t250-short", "Defiance T-250 kurz", "firearm", 2, "licensed", 330, 255);
        Add("mossberg-cmdt", "Mossberg CMDT", "firearm", 4, "licensed", 700, 255);
        Add("pjss-model-55", "PJSS Modell 55", "firearm", 5, "licensed", 825, 255);
        Add("remington-roomsweeper", "Remington Roomsweeper", "firearm", 2, "licensed", 325, 255);
        Add("remington-900", "Remington 900", "firearm", 3, "licensed", 12000, 256);
        Add("ruger-101", "Ruger 101", "firearm", 2, "licensed", 11100, 256);
        Add("ak97", "AK-97", "firearm", 2, "licensed", 2100, 256);
        Add("ares-alpha", "Ares Alpha", "firearm", 5, "licensed", 3400, 256);
        Add("colt-m23", "Colt M23", "firearm", 2, "licensed", 2100, 256);
        Add("fn-har", "FN HAR", "firearm", 3, "licensed", 2100, 256);
        Add("yamaha-raiden", "Yamaha Raiden", "firearm", 5, "licensed", 3200, 256);
        Add("ares-desert-strike", "Ares Desert Strike", "firearm", 4, "illegal", 11000, 257);
        Add("cavalier-crockett-ebr", "Cavalier Arms Crockett EBR", "firearm", 5, "illegal", 9050, 257);
        Add("ranger-arms-sm6", "Ranger Arms SM-6", "firearm", 5, "illegal", 13200, 257);
        Add("barret-model-122", "Barret Modell 122", "firearm", 6, "illegal", 15200, 257);
        Add("ingram-valiant", "Ingram Valiant", "firearm", 4, "licensed", 4175, 258);
        Add("stoner-ares-m202", "Stoner-Ares M202", "firearm", 4, "licensed", 6900, 258);
        Add("rpk-hmg", "RPK SMG", "firearm", 5, "licensed", 8000, 258);
        Add("panther-xxl", "Panther XXL", "firearm", 6, "illegal", 10000, 258);
        Add("ares-super-squirt", "Ares S-III Super Squirt", "firearm", 3, "licensed", 560, 258);
        Add("parashield-dart-pistol", "Parashield Pfeilpistole", "firearm", 2, "legal", 510, 258);
        Add("parashield-dart-rifle", "Parashield Pfeilgewehr", "firearm", 3, "legal", 710, 258);
        Add("ares-antioch-ii", "Ares Antioch II", "launcher", 3, "illegal", 5900, 259);
        Add("armtech-mgl6", "ArmTech MGL-6", "launcher", 4, "illegal", 1800, 259);
        Add("armtech-mgl12", "ArmTech MGL-12", "launcher", 4, "illegal", 5000, 259);
        Add("aztechnology-striker", "Aztechnology Striker", "launcher", 5, "illegal", 7000, 259);
        Add("onotari-interceptor", "Onotari Interceptor", "launcher", 5, "illegal", 9000, 259);
        Add("actioneer-suit", "Actioneer Geschäftsanzug", "armor", 2, "legal", 1500, 266, fitted: true);
        Add("chameleon-suit", "Chamäleonanzug", "armor", 4, "illegal", 2000, 266, fitted: true);
        Add("full-body-armor", "Ganzkörperpanzerung", "armor", 4, "licensed", 2000, 266, fitted: true);
        // The full-body armor's matching helmet is a linked accessory, not a standalone armor option here.
        Add("lined-coat", "Gefütterter Mantel", "armor", 2, "legal", 900, 266, fitted: true);
        Add("synthleather-jacket", "Kunstlederjacke", "armor", 1, "legal", 300, 266, fitted: true);
        Add("armor-jacket", "Panzerjacke", "armor", 2, "legal", 1000, 266, fitted: true);
        Add("armor-clothing", "Panzerkleidung", "armor", 2, "legal", 500, 266, fitted: true);
        Add("armor-vest", "Panzerweste", "armor", 2, "legal", 750, 266, fitted: true);
        Add("urban-explorer", "Urban Explorer Overall", "armor", 2, "legal", 800, 266, fitted: true);
        Add("meta-link", "Meta Link", "commlink", 2, "legal", 100, 268, rating: 1);
        Add("sony-emperor", "Sony Emperor", "commlink", 2, "legal", 700, 268, rating: 2);
        Add("renraku-sensei", "Renraku Sensei", "commlink", 2, "legal", 1000, 268, rating: 3);
        Add("erika-elite", "Erika Elite", "commlink", 2, "legal", 2500, 268, rating: 4);
        Add("hermes-ikon", "Hermes Ikon", "commlink", 3, "legal", 5000, 268, rating: 5);
        Add("transys-avalon", "Transys Avalon", "commlink", 3, "legal", 8000, 268, rating: 6);
        Add("erika-mcd6", "Erika MCD-6", "cyberdeck", 3, "illegal", 24750, 268, rating: 1);
        Add("spinrad-falcon", "Spinrad Falcon", "cyberdeck", 3, "illegal", 61500, 268, rating: 2);
        Add("mct360", "MCT 360", "cyberdeck", 3, "illegal", 95000, 268, rating: 3);
        Add("renraku-kitsune", "Renraku Kitsune", "cyberdeck", 4, "illegal", 107000, 268, rating: 4);
        Add("shiawase-cyber6", "Shiawase Cyber-6", "cyberdeck", 5, "illegal", 172500, 268, rating: 5);
        Add("fairlight-excalibur", "Fairlight Excalibur", "cyberdeck", 6, "illegal", 410600, 268, rating: 6);
        Add("rcc-scrap", "Aus Schrott gebastelt", "rcc", 1, "licensed", 1400, 269, rating: 1);
        Add("allegiance-control-center", "Allegiance Control Center", "rcc", 3, "licensed", 8000, 269, rating: 2);
        Add("essy-dronemaster", "Essy Motors DroneMaster", "rcc", 3, "licensed", 16000, 269, rating: 3);
        Add("horizon-overseer", "Horizon Overseer", "rcc", 4, "licensed", 32000, 269, rating: 4);
        Add("maersk-spider", "Mærsk Spider", "rcc", 5, "licensed", 34000, 269, rating: 4);
        Add("vulcan-liegelord", "Vulcan Liegelord", "rcc", 5, "licensed", 66000, 269, rating: 5);
        Add("proteus-poseidon", "Proteus Poseidon", "rcc", 6, "licensed", 68000, 269, rating: 5);
        Add("transys-eidolon", "Transys Eidolon", "rcc", 7, "licensed", 75000, 269, rating: 6);
        Add("ares-red-dog", "Ares Red Dog", "rcc", 8, "licensed", 95000, 269, rating: 6);
        Add("aztechnology-tlaloc", "Aztechnology Tlaloc", "rcc", 9, "licensed", 140000, 269, rating: 6);
        Add("credstick-standard", "Credstick: Standard", "credstick", 1, "legal", 5, 273);
        Add("credstick-silver", "Credstick: Silber", "credstick", 1, "legal", 20, 273);
        Add("credstick-gold", "Credstick: Gold", "credstick", 2, "legal", 100, 273);
        Add("credstick-platinum", "Credstick: Platin", "credstick", 3, "legal", 500, 273);
        Add("credstick-ebony", "Credstick: Ebenholz", "credstick", 5, "legal", 1000, 273);
        Add("disposable-syringe", "Einweg-Spritze", "medical", 2, "legal", 10, 283);
        Add("first-aid-kit", "Erste-Hilfe-Set", "medical", 1, "legal", 50, 283);
        Add("medkit-supplies", "Medkit-Nachfüllpack", "medical", 1, "legal", 100, 283);
        Add("antidote-patch", "Antidot-Patch", "medical", 3, "legal", 150, 283);
        Add("trauma-patch", "Trauma-Patch", "medical", 3, "legal", 500, 283);
        Add("biomonitor", "Vitalmonitor", "medical", 2, "legal", 300, 283);
        for (int rating = 1; rating <= 6; rating++)
        {
            Add("medkit-" + rating, "Medkit", "medical", 3, "legal", rating * 250, 283, rating: rating);
            Add("stim-patch-" + rating, "Stim-Patch", "medical", 3, "legal", rating * 25, 283, rating: rating);
        }
        for (int rating = 1; rating <= 12; rating++)
            Add("tranq-patch-" + rating, "Tranq-Patch", "medical", 3, "legal", rating * rating * 10, 283, rating: rating);
        return rows.AsReadOnly();

        void Add(string id, string name, string category, int availability, string legality, decimal price,
            int page, bool fitted = false, int? rating = null)
        {
            int surcharge = metatype == "troll" || metatype == "dwarf" && fitted ? 10 : 0;
            rows.Add(new(id, name, category, availability, legality, price, surcharge, price * (100 + surcharge) / 100m,
                rating, availability <= MaximumAvailability, availability <= MaximumAvailability ? null : Sr6CreationGearBlockers.AvailabilityExceeded,
                "sr6_core_de_2024:p" + page.ToString(CultureInfo.InvariantCulture)));
        }
    }

    public static CharacterCreationFoundationResult<Sr6CreationGearPreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationGearSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeGear(selection, out var frozen))
            return Fail(Sr6CreationGearBlockers.InvalidSelection);
        var catalog = Catalog(foundation.Selection.MetatypeId);
        var values = new List<Sr6CreationGearValue>();
        foreach (var choice in frozen!.Items)
        {
            var option = catalog.SingleOrDefault(row => row.Id == choice.CatalogId);
            if (option is null) return Fail(Sr6CreationGearBlockers.CatalogUnavailable);
            if (!option.Available) return Fail(Sr6CreationGearBlockers.AvailabilityExceeded);
            values.Add(new(choice, option, choice.Quantity * option.UnitPrice));
        }
        decimal budget = foundation.Karma?.ResourcesNuyen ?? foundation.Budget.ResourcesNuyen;
        decimal spent = values.Sum(row => row.TotalPrice);
        if (spent > budget) return Fail(Sr6CreationGearBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-gear.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, foundation.Selection.MetatypeId, Catalog = catalog,
            KarmaAuthorityDigest = foundation.Karma?.AuthorityDigest, budget, MaximumAvailability, MaximumCarryOver
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(values.ToArray(), budget, spent, budget - spent, MaximumCarryOver,
                Math.Max(0, budget - spent - MaximumCarryOver), values.Any(row => row.Option.Legality != "legal"),
                authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationGearPreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
