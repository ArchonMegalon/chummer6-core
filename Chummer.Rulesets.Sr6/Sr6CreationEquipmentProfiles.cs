using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// Projects the verified purchase ledger without changing its historical bytes.
/// Values are from the German 2024 Core tables/text pp.245-269, not SR5 data.
/// Unlisted items deliberately remain unresolved. Nothing is equipped or activated.
/// </summary>
internal static class Sr6CreationEquipmentProfiles
{
    internal static IReadOnlyList<Sr6CreationEquipmentProfile> Project(Sr6CreationFoundationState state)
        => (state.Selection?.Gear?.Items ?? []).Select(ProjectItem).ToArray();

    private static Sr6CreationEquipmentProfile ProjectItem(Sr6CreationGearValue item)
    {
        Sr6CreationArmorProfile? armor = item.Option.CategoryId == "armor" ? item.Choice.CatalogId switch
        {
            "actioneer-suit" => new(2, 6), "chameleon-suit" => new(2, 4),
            "full-body-armor" => new(5, 10), "lined-coat" => new(3, 7),
            "synthleather-jacket" => new(1, 3), "armor-jacket" => new(4, 8),
            "armor-clothing" => new(2, 4), "armor-vest" => new(3, 6),
            "urban-explorer" => new(3, 6), _ => null
        } : null;
        Sr6CreationMatrixDeviceProfile? matrix = item.Option.CategoryId switch
        {
            "commlink" => item.Choice.CatalogId switch
            {
                "meta-link" => Commlink(1, 1, 0, 0), "sony-emperor" => Commlink(2, 1, 1, 1),
                "renraku-sensei" => Commlink(3, 2, 0, 1), "erika-elite" => Commlink(4, 2, 1, 2),
                "hermes-ikon" => Commlink(5, 3, 0, 2), "transys-avalon" => Commlink(6, 3, 1, 3), _ => null
            },
            "cyberdeck" => item.Choice.CatalogId switch
            {
                "erika-mcd6" => Deck(1, 4, 3, 2), "spinrad-falcon" => Deck(2, 5, 4, 4),
                "mct360" => Deck(3, 6, 5, 6), "renraku-kitsune" => Deck(4, 7, 6, 8),
                "shiawase-cyber6" => Deck(5, 8, 7, 10), "fairlight-excalibur" => Deck(6, 9, 8, 12), _ => null
            },
            "rcc" => item.Choice.CatalogId switch
            {
                "rcc-scrap" => Rcc(1, 3, 2), "allegiance-control-center" => Rcc(2, 3, 3),
                "essy-dronemaster" => Rcc(3, 4, 4), "horizon-overseer" => Rcc(4, 5, 4),
                "maersk-spider" => Rcc(4, 4, 5), "vulcan-liegelord" => Rcc(5, 6, 5),
                "proteus-poseidon" => Rcc(5, 5, 6), "transys-eidolon" => Rcc(6, 6, 5),
                "ares-red-dog" => Rcc(6, 7, 6), "aztechnology-tlaloc" => Rcc(6, 8, 7), _ => null
            },
            _ => null
        };
        var traits = new List<Sr6CreationEquipmentTrait>();
        if (armor is not null)
        {
            traits.Add(new("armor-does-not-stack", null, "sr6_core_de_2024:p265"));
            switch (item.Choice.CatalogId)
            {
                case "actioneer-suit": traits.Add(new("included-concealed-holster", null, "sr6_core_de_2024:p265")); break;
                case "chameleon-suit":
                    traits.Add(new("active-suit-stealth-edge", 1, "sr6_core_de_2024:p265"));
                    traits.Add(new("wireless-suit-defense-bonus", 2, "sr6_core_de_2024:p265")); break;
                case "lined-coat": traits.Add(new("concealed-item-edge", 1, "sr6_core_de_2024:p265")); break;
                case "urban-explorer":
                    traits.Add(new("included-music-player", null, "sr6_core_de_2024:p266"));
                    traits.Add(new("included-biomonitor", null, "sr6_core_de_2024:p266")); break;
            }
        }
        if (matrix is not null && item.Option.CategoryId == "cyberdeck")
            traits.Add(new("included-hot-sim-module", null, "sr6_core_de_2024:p268"));
        // Do not derive device stats from a rating in an unsupported catalog row.
        if (matrix is not null && matrix.DeviceRating != item.Option.Rating)
            throw new InvalidOperationException("SR6 device profile/catalog rating mismatch.");
        var weapon = Sr6CreationWeaponProfiles.Create(item.Choice.CatalogId);
        if (weapon is not null) traits.AddRange(weapon.Traits);
        var anchors = new[] { item.Option.SourceAnchorId }.Concat(traits.Select(row => row.SourceAnchorId))
            .Concat(item.Option.CategoryId == "rcc" ? ["sr6_core_de_2024:p268-269"] : [])
            .Concat(weapon is null ? [] : weapon.Attacks.Select(row => row.SourceAnchorId)
                .Concat(["sr6_core_de_2024:p97,107,245,247-259"]))
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(item.Choice.Id, item.Choice.CatalogId, item.Option.SourceName, item.Choice.Quantity,
            armor, matrix, traits.AsReadOnly(), anchors, Sr6CreationFoundationRules.CoreSourceSha256) { Weapon = weapon };
    }

    private static Sr6CreationMatrixDeviceProfile Commlink(int rating, int data, int firewall, int programs)
        => new(rating, null, null, data, firewall, programs, data, null, null);
    private static Sr6CreationMatrixDeviceProfile Deck(int rating, int attack, int sleaze, int programs)
        => new(rating, attack, sleaze, null, null, programs, null, null, null);
    private static Sr6CreationMatrixDeviceProfile Rcc(int rating, int data, int firewall)
        => new(rating, null, null, data, firewall, null, rating * 3, rating, data);

    internal static XElement ToXml(Sr6CreationEquipmentProfile profile)
    {
        var root = new XElement("sr6equipmentprofile", new XAttribute("schema", "chummer.sr6.equipment-profile.v1"),
            new XElement("sourcesha256", profile.SourceSha256),
            new XElement("sources", profile.SourceAnchorIds.Select(anchor => new XElement("anchor", anchor))));
        if (profile.Armor is { } armor)
            root.Add(new XElement("armor", new XElement("defenseratingbonus", armor.DefenseRatingBonus),
                new XElement("modificationcapacity", armor.ModificationCapacity)));
        if (profile.Matrix is { } device)
        {
            var matrix = new XElement("matrix", new XElement("devicerating", device.DeviceRating));
            foreach (var (name, value) in new (string, int?)[] { ("attack", device.Attack), ("sleaze", device.Sleaze),
                ("dataprocessing", device.DataProcessing), ("firewall", device.Firewall),
                ("activeprogramslots", device.ActiveProgramSlots), ("maximumslaves", device.MaximumSlaves),
                ("noisereduction", device.NoiseReduction), ("sharedprogramslots", device.SharedProgramSlots) })
                if (value is { } known) matrix.Add(new XElement(name, known));
            root.Add(matrix);
        }
        if (profile.Weapon is { } weapon)
        {
            var value = new XElement("weapon", new XAttribute("scope", "catalog-statistics"),
                new XElement("ammunitionincluded", false),
                weapon.MinimumCarryStrength is { } strength ? new XElement("minimumcarrystrength", strength) : null,
                new XElement("includedaccessories", weapon.IncludedAccessoryIds.Select(id => new XElement("accessory", new XAttribute("id", id)))));
            foreach (var attack in weapon.Attacks)
            {
                var ratings = attack.AttackRatings;
                value.Add(new XElement("attack", new XAttribute("id", attack.Id), new XAttribute("source", attack.SourceAnchorId),
                    new XElement("skill", attack.SkillId),
                    attack.RequiredWeaponSpecialization is { } specialty ? new XElement("requiredweaponspecialization", specialty) : null,
                    new XElement("damage", new XAttribute("kind", attack.DamageKind), new XAttribute("electrical", attack.Electrical),
                        attack.DamageValue is { } damage ? new XAttribute("base", damage) : null,
                        attack.PayloadKind is { } payload ? new XAttribute("payload", payload) : null),
                    new XElement("attackratings", attack.AttackRatingAttribute is { } attribute ? new XAttribute("addattribute", attribute) : null,
                        new[] { ("close", ratings.Close), ("near", ratings.Near), ("medium", ratings.Medium), ("far", ratings.Far), ("extreme", ratings.Extreme) }
                            .Select(pair => new XElement("range", new XAttribute("id", pair.Item1), new XAttribute("available", pair.Item2.HasValue),
                                pair.Item2 is { } rating ? new XAttribute("base", rating) : null))),
                    new XElement("firemodes", attack.FireModes.Select(mode => new XElement("mode", mode))),
                    new XElement("magazineoptions", attack.Magazines.Select(magazine => new XElement("magazine",
                        new XAttribute("capacity", magazine.Capacity), new XAttribute("feed", magazine.FeedType)))),
                    attack.MaximumRangeMeters is { } range ? new XElement("maximumrangemeters", range) : null));
            }
            root.Add(value);
        }
        root.Add(new XElement("traits", profile.Traits.Select(row => new XElement("trait", new XAttribute("id", row.Id),
            row.Value is { } amount ? new XAttribute("value", amount) : null, new XAttribute("source", row.SourceAnchorId)))));
        return root;
    }
}
