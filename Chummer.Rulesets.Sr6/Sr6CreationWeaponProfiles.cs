using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>
/// German 2024 Core pp.245,247-259. These are catalog profiles, not SR5 weapon
/// records or an SR6 combat resolver. Included accessories are not applied again
/// to printed ratings. No ammunition, wireless activation, or equipped state is granted.
/// </summary>
internal static class Sr6CreationWeaponProfiles
{
    internal static Sr6CreationWeaponProfile? Create(string id)
    {
        Sr6CreationWeaponAttackProfile? attack = id switch
        {
            "combat-axe" => Melee(5, "physical", 9, 249),
            "combat-knife" => Melee(3, "physical", 8, 249),
            "katana" => Melee(4, "physical", 10, 249),
            "knife" => Melee(2, "physical", 6, 249),
            "sword" => Melee(3, "physical", 9, 249),
            "polearm" => Melee(4, "physical", 8, 249),
            "survival-knife" => Melee(3, "physical", 8, 249),
            "forearm-snap-blades" => Melee(3, "physical", 6, 249),
            "stun-baton" => Melee(5, "stun", 6, 249, electrical: true),
            "club" => Melee(3, "stun", 6, 249),
            "staff" or "extendable-staff" => Melee(4, "stun", 8, 249),
            "extendable-baton" => Melee(2, "stun", 5, 249),
            "sap" => Melee(2, "stun", 6, 249),
            "bullwhip" => Melee(1, "physical", 6, 250, whip: true),
            "monofilament-whip" => Melee(6, "physical", 14, 250, whip: true),
            "bike-chain" => Melee(2, "stun", 5, 250),
            "knucks" => Melee(3, "physical", 6, 250),
            "shock-gloves" => Melee(4, "stun", 5, 250, electrical: true),
            "light-crossbow" => Projectile(2, new(6, 8, 2, null, null), 250, 4),
            "medium-crossbow" => Projectile(3, new(2, 10, 4, 2, null), 250, 4),
            "heavy-crossbow" => Projectile(4, new(2, 8, 6, 4, null), 250, 4),
            "shuriken" => Projectile(2, new(9, 11, 5, null, null), 250),
            "throwing-knife" => Projectile(2, new(10, 9, 3, null, null), 250),
            "defiance-super-shock" => Gun(6, "SS", new(10, 6, null, null, null), 4, "internal", 251)
                with { DamageKind = "stun", Electrical = true, MaximumRangeMeters = 20 },
            "yamaha-pulsar-i" or "yamaha-pulsar-ii" => Gun(4, "SS", new(9, 9, null, null, null), 4, "internal", 251)
                with { DamageKind = "stun", Electrical = true },
            "fichetti-tiffani-needler" => Gun(3, "SS", new(10, 6, 2, null, null), 4, "clip", 251),
            "streetline-special" => Gun(2, "SS", new(8, 8, null, null, null), 6, "clip", 251),
            "walther-palm-pistol" => Gun(2, "SS/BF", new(12, 7, null, null, null), 6, "break-action", 251),
            "ares-light-fire-70" or "ares-light-fire-75" => Gun(2, "SA", new(10, 7, 6, null, null), 16, "clip", 252),
            "beretta-101t" => Gun(2, "SA", new(9, 8, 6, null, null), 21, "clip", 252),
            "beretta-201t" => Gun(2, "SA/FA", new(9, 8, 6, null, null), 21, "clip", 252),
            "colt-america-l36" => Gun(2, "SA", new(8, 8, 6, null, null), 11, "clip", 252),
            "fichetti-security-600" => Gun(2, "SA", new(10, 9, 6, null, null), 30, "clip", 252),
            "ruger-redhawk" => Gun(3, "SA/BF", new(7, 10, 7, null, null), 8, "cylinder", 252),
            "ares-crusader-ii" => Gun(2, "SA/BF", new(9, 9, 7, null, null), 40, "clip", 253),
            "ceska-black-scorpion" => Gun(2, "SA/BF", new(10, 9, 8, null, null), 35, "clip", 253),
            "steyr-tmp" => Gun(2, "SA/FA", new(8, 8, 6, null, null), 30, "clip", 253),
            "ares-predator-vi" => Gun(3, "SA/BF", new(10, 10, 8, null, null), 15, "clip", 254),
            "ares-viper-slivergun" => Gun(3, "SA/BF", new(12, 8, 6, null, null), 30, "clip", 254),
            "browning-ultra-power" => Gun(3, "SA", new(10, 9, 6, null, null), 10, "clip", 254),
            "colt-government-2076" => Gun(3, "SA", new(10, 8, 6, null, null), 14, "clip", 254),
            "colt-manhunter" => Gun(3, "SA", new(11, 9, 7, null, null), 14, "clip", 254),
            "ruger-super-warhawk" => Gun(4, "SA", new(8, 11, 8, null, null), 6, "cylinder", 254),
            "colt-cobra-tz100" => Gun(3, "SA/BF", new(9, 9, 6, null, null), 32, "clip", 255),
            "colt-cobra-tz110" or "colt-cobra-tz120" => Gun(3, "SA/BF", new(10, 10, 7, null, null), 32, "clip", 255),
            "fn-p93-praetor" => Gun(4, "SA/BF/FA", new(9, 12, 7, null, null), 50, "clip", 255),
            "hk227" => Gun(3, "SA/BF", new(10, 11, 8, null, null), 28, "clip", 255),
            "ingram-smartgun-xi" => Gun(3, "SA/BF", new(11, 9, 6, null, null), 32, "clip", 255),
            "sck-model-100" => Gun(3, "SA/BF", new(10, 10, 7, null, null), 30, "clip", 255),
            "uzi-v" => Gun(3, "SA/BF/FA", new(8, 8, 7, null, null), 24, "clip", 255),
            "defiance-t250" => Gun(4, "SS/SA", new(7, 10, 6, null, null), 5, "internal", 255),
            "defiance-t250-short" => Gun(3, "SS/SA", new(8, 8, 4, null, null), 5, "internal", 255),
            "mossberg-cmdt" => Gun(4, "SA/BF", new(4, 11, 7, null, null), 10, "clip", 255)
                with { Magazines = [new(10, "clip"), new(24, "drum")] },
            "pjss-model-55" => Gun(4, "SA/short-burst", new(3, 12, 8, null, null), 2, "break-action", 255),
            "remington-roomsweeper" => Gun(5, "SA", new(9, 8, 4, null, null), 8, "internal", 255),
            "remington-900" => Gun(5, "SS", new(2, 7, 10, 12, 11), 5, "internal", 256),
            "ruger-101" => Gun(5, "SA", new(2, 6, 10, 12, 11), 8, "internal", 256),
            "ak97" => Gun(5, "SA/BF/FA", new(4, 11, 9, 7, 1), 38, "clip", 256),
            "ares-alpha" => Gun(4, "SA/BF/FA", new(4, 10, 9, 7, 2), 42, "clip", 256),
            "colt-m23" => Gun(4, "SA/BF/FA", new(5, 8, 8, 8, 1), 40, "clip", 256),
            "fn-har" => Gun(5, "SA/BF/FA", new(3, 11, 10, 6, 1), 35, "clip", 256),
            "yamaha-raiden" => Gun(4, "SA/BF/FA", new(4, 11, 10, 7, 2), 60, "clip", 256),
            "ares-desert-strike" => Gun(5, "SA", new(3, 10, 10, 10, 10), 14, "clip", 257),
            "cavalier-crockett-ebr" => Gun(5, "SA/BF", new(3, 8, 11, 8, 8), 20, "clip", 257),
            "ranger-arms-sm6" => Gun(5, "SA", new(3, 6, 9, 11, 12), 15, "clip", 257),
            "barret-model-122" => Gun(6, "SA", new(1, 8, 11, 16, 14), 10, "clip", 257),
            "ingram-valiant" => MachineGun(4, new(2, 11, 12, 7, 3)),
            "stoner-ares-m202" => MachineGun(5, new(1, 10, 11, 7, 6)),
            "rpk-hmg" => MachineGun(6, new(1, 10, 12, 8, 7)),
            "panther-xxl" => Gun(7, "SA", new(1, 9, 12, 8, 6), 15, "clip", 258),
            "ares-super-squirt" => Payload("contact-toxin", new(8, 12, 9, null, null), 20, "clip", 258)
                with { DamageValue = 0, DamageKind = "none" },
            "parashield-dart-pistol" => Payload("injection-toxin", new(9, 10, 8, null, null), 5, "clip", 258)
                with { DamageValue = 1, DamageKind = "physical" },
            "parashield-dart-rifle" => Payload("injection-toxin", new(5, 8, 11, 3, null), 6, "internal", 258)
                with { DamageValue = 1, DamageKind = "physical" },
            "ares-antioch-ii" => Payload("grenade", new(null, 6, 8, 6, 5), 8, "internal", 259),
            "armtech-mgl6" => Payload("grenade", new(null, 8, 8, 3, null), 6, "clip", 259),
            "armtech-mgl12" => Payload("grenade", new(null, 8, 9, 6, 2), 12, "clip", 259),
            "aztechnology-striker" => Payload("rocket", new(null, 4, 10, 9, 6), 1, "muzzle-loading", 259),
            "onotari-interceptor" => Payload("rocket", new(null, 5, 9, 10, 8), 2, "muzzle-loading", 259),
            _ => null
        };
        if (attack is null) return null;
        var attacks = new List<Sr6CreationWeaponAttackProfile> { attack };
        if (id is "knife" or "combat-knife" or "survival-knife")
            attacks.Add(Projectile(attack.DamageValue!.Value, new(attack.AttackRatings.Close, id == "knife" ? 1 : 2, null, null, null), 249)
                with { Id = "thrown", MaximumRangeMeters = 20 });
        if (id == "ares-alpha")
            attacks.Add(Payload("grenade", new(4, 10, 6, 2, null), 6, "clip", 256) with { Id = "underbarrel-grenade" });
        if (id == "yamaha-raiden")
        {
            attacks.Add(Payload("grenade", new(4, 11, 7, 1, null), 4, "clip", 256) with { Id = "underbarrel-grenade" });
            attacks.Add(Gun(4, "SS/SA", new(7, 9, 8, null, null), 2, "break-action", 256) with { Id = "underbarrel-shotgun" });
        }
        var accessories = Accessories(id);
        var traits = Traits(id, attack.SourceAnchorId);
        if (attacks.Any(row => row.SkillId == "ExoticWeapons")) traits.Add(new("exotic-specialization-required", null, "sr6_core_de_2024:p97"));
        return new(attacks.AsReadOnly(), accessories, id == "stoner-ares-m202" ? 3 : id == "rpk-hmg" ? 5 : null, traits.AsReadOnly());
    }

    private static Sr6CreationWeaponAttackProfile Melee(int damage, string kind, int rating, int page, bool electrical = false, bool whip = false)
        => new("primary", whip ? "ExoticWeapons" : "CloseCombat", whip ? "whips" : null,
            damage, kind, electrical, null, new(rating, null, null, null, null), whip ? "Reaction" : "Strength", [], [], null, Anchor(page));
    private static Sr6CreationWeaponAttackProfile Projectile(int damage, Sr6CreationWeaponAttackRatings ratings, int page, int? capacity = null)
        => new("primary", "Athletics", null, damage, "physical", false, null, ratings, null, [],
            capacity is { } count ? [new(count, "internal")] : [], null, Anchor(page));
    private static Sr6CreationWeaponAttackProfile Gun(int damage, string modes, Sr6CreationWeaponAttackRatings ratings, int capacity, string feed, int page)
        => new("primary", "Firearms", null, damage, "physical", false, null, ratings, null, modes.Split('/'), [new(capacity, feed)], null, Anchor(page));
    private static Sr6CreationWeaponAttackProfile MachineGun(int damage, Sr6CreationWeaponAttackRatings ratings)
        => Gun(damage, "SA/BF/FA", ratings, 50, "clip", 258) with { Magazines = [new(50, "clip"), new(100, "belt")] };
    private static Sr6CreationWeaponAttackProfile Payload(string kind, Sr6CreationWeaponAttackRatings ratings, int capacity, string feed, int page)
        => new("primary", "ExoticWeapons", kind is "grenade" or "rocket" ? "launchers" : "airguns",
            null, "payload", false, kind, ratings, null, ["SS"], [new(capacity, feed)], null, Anchor(page));
    private static string Anchor(int page) => "sr6_core_de_2024:p" + page.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static IReadOnlyList<string> Accessories(string id) => id switch
    {
        "survival-knife" => ["gps", "mini-multitool", "micro-lighter", "illuminated-blade"],
        "ares-light-fire-70" or "steyr-tmp" or "browning-ultra-power" or "colt-government-2076" or "mossberg-cmdt" => ["laser-sight"],
        "ares-light-fire-75" => ["laser-sight", "smartgun"],
        "beretta-201t" => ["detachable-stock"],
        "fichetti-security-600" => ["detachable-stock", "laser-sight"],
        "ares-crusader-ii" or "ingram-smartgun-xi" => id == "ingram-smartgun-xi"
            ? ["gas-vent", "smartgun", "silencer"] : ["gas-vent", "smartgun"],
        "ceska-black-scorpion" or "colt-cobra-tz100" => ["folding-stock"],
        "ares-predator-vi" => ["smartgun", "selectable-ammunition-magazine"],
        "ares-viper-slivergun" => ["silencer"],
        "colt-manhunter" or "panther-xxl" or "ares-antioch-ii" or "onotari-interceptor" or "ares-alpha" => ["smartgun"],
        "colt-cobra-tz110" or "uzi-v" => ["folding-stock", "laser-sight"],
        "colt-cobra-tz120" => ["folding-stock", "laser-sight", "gas-vent"],
        "fn-p93-praetor" => ["fixed-stock", "laser-sight", "three-mode-flashlight"],
        "hk227" => ["retractable-stock", "smartgun", "silencer"],
        "sck-model-100" => ["smartgun", "folding-stock"],
        "pjss-model-55" => ["fixed-stock", "shock-pad"],
        "remington-900" or "parashield-dart-rifle" => ["scope"],
        "ruger-101" => ["scope", "fixed-stock", "shock-pad"],
        "fn-har" => ["laser-sight", "gas-vent"],
        "yamaha-raiden" => ["silencer", "smartgun"],
        "ares-desert-strike" or "cavalier-crockett-ebr" => ["fixed-stock", "shock-pad", "detachable-scope"],
        "ranger-arms-sm6" => ["silencer", "scope", "smartgun", "fixed-stock", "shock-pad"],
        "barret-model-122" => ["silencer", "smartgun", "folding-bipod"],
        "ingram-valiant" => ["fixed-stock", "shock-pad", "laser-sight", "gas-vent"],
        "rpk-hmg" => ["detachable-tripod"],
        _ => []
    };

    private static List<Sr6CreationEquipmentTrait> Traits(string id, string source)
    {
        var rows = new List<Sr6CreationEquipmentTrait>();
        void Add(string trait, int? value = null, int? page = null) => rows.Add(new(trait, value, page is { } p ? Anchor(p) : source));
        switch (id)
        {
            case "survival-knife": Add("wireless-held-location-vitals", page: 248); break;
            case "forearm-snap-blades": Add("minor-deploy-retract", page: 248); Add("wireless-deploy-extra-minor", page: 248); break;
            case "stun-baton": case "shock-gloves":
                int chargePage = id == "stun-baton" ? 248 : 250;
                Add("charge-capacity", 10, chargePage); Add("wired-charge-seconds", 10, chargePage); Add("wireless-charge-minutes", 30, chargePage);
                if (id == "shock-gloves") { Add("activation-minor"); Add("wireless-activation-extra-minor"); }
                break;
            case "extendable-baton": case "extendable-staff":
                Add("minor-deploy-retract"); Add("wireless-deploy-extra-minor");
                if (id == "extendable-baton") { Add("retracted-concealability", 4); Add("extended-concealability", 2); }
                break;
            case "sap": Add("concealability", 4); break;
            case "bullwhip": case "bike-chain": Add("trip-extra-minor", page: 249); break;
            case "monofilament-whip":
                Add("critical-glitch-self-damage", 6, 249); Add("wireless-attack-rating-bonus", 2, 249);
                Add("wireless-glitch-safe-retract", page: 249); break;
            case "light-crossbow": Add("hands-required", 1); break;
            case "medium-crossbow": case "heavy-crossbow": Add("hands-required", 2); break;
            case "shuriken": case "throwing-knife": Add("wireless-embedded-smartlink-dice", 1, 251); break;
            case "defiance-super-shock": Add("contact-taser-use"); Add("wired-hit-sustain-major"); Add("top-mount-only"); break;
            case "yamaha-pulsar-i": Add("top-mount-only"); break;
            case "yamaha-pulsar-ii": Add("top-mount-only"); Add("contact-attack-as-club"); break;
            case "fichetti-tiffani-needler": Add("caseless-flechette-only"); Add("wireless-visual-concealment-bonus", 1); Add("no-accessory-mounts"); break;
            case "streetline-special": Add("mad-threshold-bonus", 1); Add("no-accessory-mounts"); break;
            case "walther-palm-pistol": Add("no-accessory-mounts"); break;
            case "ares-light-fire-75": Add("active-smartgun-additional-rating", 1); break;
            case "ares-viper-slivergun": Add("unique-flechette-only"); break;
            case "defiance-t250": case "defiance-t250-short":
                Add("pump-clear-jam-minor", page: 256); if (id == "defiance-t250-short") Add("concealability", 3, 256); break;
            case "pjss-model-55": Add("short-burst-two-barrels", page: 256); break;
            case "remington-roomsweeper": Add("heavy-pistol-flechette-compatible", page: 256); break;
            case "remington-900": Add("no-underbarrel-mount"); break;
            case "colt-m23": Add("underbarrel-mounts", 3); break;
            case "ranger-arms-sm6": Add("assembly-firearms-logic-extended", 6); break;
            case "ares-super-squirt": Add("contact-toxin-dmso"); Add("barrel-underbarrel-mounts-only"); break;
            case "parashield-dart-pistol": Add("injection-dart-only"); Add("top-mount-only"); break;
            case "parashield-dart-rifle": Add("injection-dart-only"); Add("top-underbarrel-mounts-only"); break;
            case "armtech-mgl6": case "armtech-mgl12": Add("wireless-minimum-detonation-distance", 5); break;
            case "aztechnology-striker": Add("wireless-local-matrix-dice-nonstacking", 1); break;
            case "onotari-interceptor": Add("separate-launch-tubes", 2); Add("dual-launch-override-risk", 6); break;
        }
        return rows;
    }
}
