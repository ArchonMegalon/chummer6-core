using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class CharacterSectionService : ICharacterSectionService
{
    public CharacterAttributesSection ParseAttributes(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterAttributeSummary> attributes = character
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => new CharacterAttributeSummary(
                Name: ReadValue(attribute, "name"),
                BaseValue: ParseInt(ReadValue(attribute, "base")),
                TotalValue: ParseInt(ReadValue(attribute, "totalvalue"))))
            .ToArray()
            ?? Array.Empty<CharacterAttributeSummary>();

        return new CharacterAttributesSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    public CharacterAttributeDetailsSection ParseAttributeDetails(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterAttributeDetailSummary> attributes = character
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => new CharacterAttributeDetailSummary(
                Name: ReadValue(attribute, "name"),
                MetatypeMin: ParseInt(ReadValue(attribute, "metatypemin")),
                MetatypeMax: ParseInt(ReadValue(attribute, "metatypemax")),
                MetatypeAugMax: ParseInt(ReadValue(attribute, "metatypeaugmax")),
                BaseValue: ParseInt(ReadValue(attribute, "base")),
                KarmaValue: ParseInt(ReadValue(attribute, "karma")),
                TotalValue: ParseInt(ReadValue(attribute, "totalvalue")),
                MetatypeCategory: ReadValue(attribute, "metatypecategory")))
            .ToArray()
            ?? Array.Empty<CharacterAttributeDetailSummary>();

        return new CharacterAttributeDetailsSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    public CharacterInventorySection ParseInventory(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<string> gears = ReadItemNames(character, "gears", "gear");
        IReadOnlyList<string> weapons = ReadItemNames(character, "weapons", "weapon");
        IReadOnlyList<string> armors = ReadItemNames(character, "armors", "armor");
        IReadOnlyList<string> cyberwares = ReadItemNames(character, "cyberwares", "cyberware");
        IReadOnlyList<string> vehicles = ReadItemNames(character, "vehicles", "vehicle");

        return new CharacterInventorySection(
            GearCount: gears.Count,
            WeaponCount: weapons.Count,
            ArmorCount: armors.Count,
            CyberwareCount: cyberwares.Count,
            VehicleCount: vehicles.Count,
            GearNames: gears,
            WeaponNames: weapons,
            ArmorNames: armors,
            CyberwareNames: cyberwares,
            VehicleNames: vehicles);
    }

    public CharacterProfileSection ParseProfile(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        int mugshotCount = character.Element("mugshots")?.Elements("mugshot").Count() ?? 0;

        return new CharacterProfileSection(
            Name: ReadValue(character, "name"),
            Alias: ReadValue(character, "alias"),
            PlayerName: ReadValue(character, "playername"),
            Metatype: ReadValue(character, "metatype"),
            Metavariant: ReadValue(character, "metavariant"),
            Sex: ReadValue(character, "sex"),
            Age: ReadValue(character, "age"),
            Height: ReadValue(character, "height"),
            Weight: ReadValue(character, "weight"),
            Hair: ReadValue(character, "hair"),
            Eyes: ReadValue(character, "eyes"),
            Skin: ReadValue(character, "skin"),
            Concept: ReadValue(character, "concept"),
            Description: ReadValue(character, "description"),
            Background: ReadValue(character, "background"),
            CreatedVersion: ReadValue(character, "createdversion"),
            AppVersion: ReadValue(character, "appversion"),
            BuildMethod: ReadValue(character, "buildmethod"),
            GameplayOption: ReadValue(character, "gameplayoption"),
            Created: ParseBool(ReadValue(character, "created")),
            Adept: ParseBool(ReadValue(character, "adept")),
            Magician: ParseBool(ReadValue(character, "magician")),
            Technomancer: ParseBool(ReadValue(character, "technomancer")),
            AI: ParseBool(ReadValue(character, "ai")),
            MainMugshotIndex: ParseInt(ReadValue(character, "mainmugshotindex")),
            MugshotCount: mugshotCount);
    }

    public CharacterProgressSection ParseProgress(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        return new CharacterProgressSection(
            Karma: ParseDecimal(ReadValue(character, "karma")),
            Nuyen: ParseDecimal(ReadValue(character, "nuyen")),
            StartingNuyen: ParseDecimal(ReadValue(character, "startingnuyen")),
            StreetCred: ParseInt(ReadValue(character, "streetcred")),
            Notoriety: ParseInt(ReadValue(character, "notoriety")),
            PublicAwareness: ParseInt(ReadValue(character, "publicawareness")),
            BurntStreetCred: ParseInt(ReadValue(character, "burntstreetcred")),
            BuildKarma: ParseInt(ReadValue(character, "buildkarma")),
            TotalAttributes: ParseInt(ReadValue(character, "totalattributes")),
            TotalSpecial: ParseInt(ReadValue(character, "totalspecial")),
            PhysicalCmFilled: ParseInt(ReadValue(character, "physicalcmfilled")),
            StunCmFilled: ParseInt(ReadValue(character, "stuncmfilled")),
            TotalEssence: ParseDecimal(ReadValue(character, "totaless")),
            InitiateGrade: ParseInt(ReadValue(character, "initiategrade")),
            SubmersionGrade: ParseInt(ReadValue(character, "submersiongrade")),
            MagEnabled: ParseBool(ReadValue(character, "magenabled")),
            ResEnabled: ParseBool(ReadValue(character, "resenabled")),
            DepEnabled: ParseBool(ReadValue(character, "depenabled")));
    }

    public CharacterRulesSection ParseRules(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<string> bannedWareGrades = character
            .Element("bannedwaregrades")?
            .Elements("grade")
            .Select(node => node.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray()
            ?? Array.Empty<string>();

        return new CharacterRulesSection(
            GameEdition: ReadValue(character, "gameedition"),
            Settings: ReadValue(character, "settings"),
            GameplayOption: ReadValue(character, "gameplayoption"),
            GameplayOptionQualityLimit: ParseInt(ReadValue(character, "gameplayoptionqualitylimit")),
            MaxNuyen: ParseInt(ReadValue(character, "maxnuyen")),
            MaxKarma: ParseInt(ReadValue(character, "maxkarma")),
            ContactMultiplier: ParseInt(ReadValue(character, "contactmultiplier")),
            BannedWareGrades: bannedWareGrades);
    }

    public CharacterBuildSection ParseBuild(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        return new CharacterBuildSection(
            BuildMethod: ReadValue(character, "buildmethod"),
            PriorityMetatype: ReadValue(character, "prioritymetatype"),
            PriorityAttributes: ReadValue(character, "priorityattributes"),
            PrioritySpecial: ReadValue(character, "priorityspecial"),
            PrioritySkills: ReadValue(character, "priorityskills"),
            PriorityResources: ReadValue(character, "priorityresources"),
            PriorityTalent: ReadValue(character, "prioritytalent"),
            SumToTen: ParseInt(ReadValue(character, "sumtoten")),
            Special: ParseInt(ReadValue(character, "special")),
            TotalSpecial: ParseInt(ReadValue(character, "totalspecial")),
            TotalAttributes: ParseInt(ReadValue(character, "totalattributes")),
            ContactPoints: ParseInt(ReadValue(character, "contactpoints")),
            ContactPointsUsed: ParseInt(ReadValue(character, "contactpointsused")));
    }

    public CharacterMovementSection ParseMovement(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        return new CharacterMovementSection(
            Walk: ReadValue(character, "walk"),
            Run: ReadValue(character, "run"),
            Sprint: ReadValue(character, "sprint"),
            WalkAlt: ReadValue(character, "walkalt"),
            RunAlt: ReadValue(character, "runalt"),
            SprintAlt: ReadValue(character, "sprintalt"),
            PhysicalCmFilled: ParseInt(ReadValue(character, "physicalcmfilled")),
            StunCmFilled: ParseInt(ReadValue(character, "stuncmfilled")));
    }

    public CharacterAwakeningSection ParseAwakening(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        return new CharacterAwakeningSection(
            MagEnabled: ParseBool(ReadValue(character, "magenabled")),
            ResEnabled: ParseBool(ReadValue(character, "resenabled")),
            DepEnabled: ParseBool(ReadValue(character, "depenabled")),
            Adept: ParseBool(ReadValue(character, "adept")),
            Magician: ParseBool(ReadValue(character, "magician")),
            Technomancer: ParseBool(ReadValue(character, "technomancer")),
            AI: ParseBool(ReadValue(character, "ai")),
            InitiateGrade: ParseInt(ReadValue(character, "initiategrade")),
            SubmersionGrade: ParseInt(ReadValue(character, "submersiongrade")),
            Tradition: ReadValue(character, "tradition"),
            TraditionName: ReadValue(character, "traditionname"),
            TraditionDrain: ReadValue(character, "traditiondrain"),
            SpiritCombat: ReadValue(character, "spiritcombat"),
            SpiritDetection: ReadValue(character, "spiritdetection"),
            SpiritHealth: ReadValue(character, "spirithealth"),
            SpiritIllusion: ReadValue(character, "spiritillusion"),
            SpiritManipulation: ReadValue(character, "spiritmanipulation"),
            Stream: ReadValue(character, "stream"),
            StreamDrain: ReadValue(character, "streamdrain"),
            CurrentCounterspellingDice: ParseInt(ReadValue(character, "currentcounterspellingdice")),
            SpellLimit: ParseInt(ReadValue(character, "spelllimit")),
            CfpLimit: ParseInt(ReadValue(character, "cfplimit")),
            AiNormalProgramLimit: ParseInt(ReadValue(character, "ainormalprogramlimit")),
            AiAdvancedProgramLimit: ParseInt(ReadValue(character, "aiadvancedprogramlimit")));
    }

    public CharacterGearSection ParseGear(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterGearSummary> gear = character
            .Element("gears")?
            .Elements("gear")
            .Select(item => new CharacterGearSummary(
                Guid: ReadValue(item, "guid"),
                Name: ReadValue(item, "name"),
                Category: ReadValue(item, "category"),
                Rating: ReadValue(item, "rating"),
                Quantity: ReadValue(item, "qty"),
                Cost: ReadValue(item, "cost"),
                Equipped: ParseBool(ReadValue(item, "equipped")),
                Location: ReadValue(item, "location")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterGearSummary>();

        return new CharacterGearSection(
            Count: gear.Count,
            Gear: gear);
    }

    public CharacterWeaponsSection ParseWeapons(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterWeaponSummary> weapons = character
            .Element("weapons")?
            .Elements("weapon")
            .Select(item => new CharacterWeaponSummary(
                Guid: ReadValue(item, "guid"),
                Name: ReadValue(item, "name"),
                Category: ReadValue(item, "category"),
                Type: ReadValue(item, "type"),
                Damage: ReadValue(item, "damage"),
                AP: ReadValue(item, "ap"),
                Accuracy: ReadValue(item, "accuracy"),
                Mode: ReadValue(item, "mode"),
                Ammo: ReadValue(item, "ammo"),
                Cost: ReadValue(item, "cost"),
                Equipped: ParseBool(ReadValue(item, "equipped"))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterWeaponSummary>();

        return new CharacterWeaponsSection(
            Count: weapons.Count,
            Weapons: weapons);
    }

    public CharacterWeaponAccessoriesSection ParseWeaponAccessories(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterWeaponAccessorySummary> accessories = character
            .Element("weapons")?
            .Elements("weapon")
            .SelectMany(weapon =>
            {
                string weaponGuid = ReadValue(weapon, "guid");
                string weaponName = ReadValue(weapon, "name");
                return weapon.Element("accessories")?
                    .Elements("accessory")
                    .Select(accessory => new CharacterWeaponAccessorySummary(
                        WeaponGuid: weaponGuid,
                        WeaponName: weaponName,
                        AccessoryGuid: ReadValue(accessory, "guid"),
                        Name: ReadValue(accessory, "name"),
                        Mount: ReadValue(accessory, "mount"),
                        ExtraMount: ReadValue(accessory, "extramount"),
                        Rating: ReadValue(accessory, "rating"),
                        Cost: ReadValue(accessory, "cost"),
                        Equipped: ParseBool(ReadValue(accessory, "equipped"))))
                    ?? Array.Empty<CharacterWeaponAccessorySummary>();
            })
            .Where(accessory => !string.IsNullOrWhiteSpace(accessory.Name))
            .ToArray()
            ?? Array.Empty<CharacterWeaponAccessorySummary>();

        return new CharacterWeaponAccessoriesSection(
            Count: accessories.Count,
            Accessories: accessories);
    }

    public CharacterArmorsSection ParseArmors(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterArmorSummary> armors = character
            .Element("armors")?
            .Elements("armor")
            .Select(item => new CharacterArmorSummary(
                Guid: ReadValue(item, "guid"),
                Name: ReadValue(item, "name"),
                Category: ReadValue(item, "category"),
                ArmorValue: ReadValue(item, "armor"),
                Rating: ReadValue(item, "rating"),
                Cost: ReadValue(item, "cost"),
                Equipped: ParseBool(ReadValue(item, "equipped"))))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterArmorSummary>();

        return new CharacterArmorsSection(
            Count: armors.Count,
            Armors: armors);
    }

    public CharacterArmorModsSection ParseArmorMods(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterArmorModSummary> armorMods = character
            .Element("armors")?
            .Elements("armor")
            .SelectMany(armor =>
            {
                string armorGuid = ReadValue(armor, "guid");
                string armorName = ReadValue(armor, "name");
                return armor.Element("armormods")?
                    .Elements("armormod")
                    .Select(mod => new CharacterArmorModSummary(
                        ArmorGuid: armorGuid,
                        ArmorName: armorName,
                        ModGuid: ReadValue(mod, "guid"),
                        Name: ReadValue(mod, "name"),
                        Category: ReadValue(mod, "category"),
                        Rating: ReadValue(mod, "rating"),
                        Cost: ReadValue(mod, "cost"),
                        Equipped: ParseBool(ReadValue(mod, "equipped"))))
                    ?? Array.Empty<CharacterArmorModSummary>();
            })
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Name))
            .ToArray()
            ?? Array.Empty<CharacterArmorModSummary>();

        return new CharacterArmorModsSection(
            Count: armorMods.Count,
            ArmorMods: armorMods);
    }

    public CharacterCyberwaresSection ParseCyberwares(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterCyberwareSummary> cyberwares = character
            .Element("cyberwares")?
            .Elements("cyberware")
            .Select(item => new CharacterCyberwareSummary(
                Guid: ReadValue(item, "guid"),
                Name: ReadValue(item, "name"),
                Category: ReadValue(item, "category"),
                Essence: ReadValue(item, "ess"),
                Capacity: ReadValue(item, "capacity"),
                Rating: ReadValue(item, "rating"),
                Cost: ReadValue(item, "cost"),
                Grade: ReadValue(item, "grade"),
                Location: ReadValue(item, "location")))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterCyberwareSummary>();

        return new CharacterCyberwaresSection(
            Count: cyberwares.Count,
            Cyberwares: cyberwares);
    }

    public CharacterVehiclesSection ParseVehicles(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterVehicleSummary> vehicles = character
            .Element("vehicles")?
            .Elements("vehicle")
            .Select(item => new CharacterVehicleSummary(
                Guid: ReadValue(item, "guid"),
                Name: ReadValue(item, "name"),
                Category: ReadValue(item, "category"),
                Handling: ReadValue(item, "handling"),
                Speed: ReadValue(item, "speed"),
                Body: ReadValue(item, "body"),
                Armor: ReadValue(item, "armor"),
                Sensor: ReadValue(item, "sensor"),
                Seats: ReadValue(item, "seats"),
                Cost: ReadValue(item, "cost"),
                ModCount: item.Element("mods")?.Elements("mod").Count() ?? 0,
                WeaponCount: item.Element("weapons")?.Elements("weapon").Count() ?? 0))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterVehicleSummary>();

        return new CharacterVehiclesSection(
            Count: vehicles.Count,
            Vehicles: vehicles);
    }

    public CharacterVehicleModsSection ParseVehicleMods(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterVehicleModSummary> vehicleMods = character
            .Element("vehicles")?
            .Elements("vehicle")
            .SelectMany(vehicle =>
            {
                string vehicleGuid = ReadValue(vehicle, "guid");
                string vehicleName = ReadValue(vehicle, "name");
                return vehicle.Element("mods")?
                    .Elements("mod")
                    .Select(mod => new CharacterVehicleModSummary(
                        VehicleGuid: vehicleGuid,
                        VehicleName: vehicleName,
                        ModGuid: ReadValue(mod, "guid"),
                        Name: ReadValue(mod, "name"),
                        Category: ReadValue(mod, "category"),
                        Slots: ReadValue(mod, "slots"),
                        Rating: ReadValue(mod, "rating"),
                        Cost: ReadValue(mod, "cost"),
                        Equipped: ParseBool(ReadValue(mod, "equipped"))))
                    ?? Array.Empty<CharacterVehicleModSummary>();
            })
            .Where(mod => !string.IsNullOrWhiteSpace(mod.Name))
            .ToArray()
            ?? Array.Empty<CharacterVehicleModSummary>();

        return new CharacterVehicleModsSection(
            Count: vehicleMods.Count,
            VehicleMods: vehicleMods);
    }

    public CharacterSkillsSection ParseSkills(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterSkillSummary> skills = character
            .Element("newskills")?
            .Element("skills")?
            .Elements("skill")
            .Select(skill => new CharacterSkillSummary(
                Guid: ReadValue(skill, "guid"),
                Suid: ReadValue(skill, "suid"),
                Category: ReadValue(skill, "skillcategory"),
                IsKnowledge: ParseBool(ReadValue(skill, "isknowledge")),
                BaseValue: ParseInt(ReadValue(skill, "base")),
                KarmaValue: ParseInt(ReadValue(skill, "karma")),
                Specializations: skill.Element("specs")?
                    .Elements("spec")
                    .Select(spec => ReadValue(spec, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray() ?? Array.Empty<string>()))
            .ToArray()
            ?? Array.Empty<CharacterSkillSummary>();

        int knowledgeCount = skills.Count(skill => skill.IsKnowledge);
        return new CharacterSkillsSection(
            Count: skills.Count,
            KnowledgeCount: knowledgeCount,
            Skills: skills);
    }

    public CharacterQualitiesSection ParseQualities(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterQualitySummary> qualities = character
            .Element("qualities")?
            .Elements("quality")
            .Select(quality => new CharacterQualitySummary(
                Name: ReadValue(quality, "name"),
                Source: ReadValue(quality, "source"),
                BP: ParseInt(ReadValue(quality, "bp"))))
            .ToArray()
            ?? Array.Empty<CharacterQualitySummary>();

        return new CharacterQualitiesSection(
            Count: qualities.Count,
            Qualities: qualities);
    }

    public CharacterContactsSection ParseContacts(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterContactSummary> contacts = character
            .Element("contacts")?
            .Elements("contact")
            .Select(contact => new CharacterContactSummary(
                Name: ReadValue(contact, "name"),
                Role: ReadValue(contact, "role"),
                Location: ReadValue(contact, "location"),
                Connection: ParseInt(ReadValue(contact, "connection")),
                Loyalty: ParseInt(ReadValue(contact, "loyalty"))))
            .ToArray()
            ?? Array.Empty<CharacterContactSummary>();

        return new CharacterContactsSection(
            Count: contacts.Count,
            Contacts: contacts);
    }

    public CharacterSpellsSection ParseSpells(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterSpellSummary> spells = character
            .Element("spells")?
            .Elements("spell")
            .Select(spell => new CharacterSpellSummary(
                Name: ReadValue(spell, "name"),
                Category: ReadValue(spell, "category"),
                Type: ReadValue(spell, "type"),
                Range: ReadValue(spell, "range"),
                Duration: ReadValue(spell, "duration"),
                DrainValue: ReadValue(spell, "dv"),
                Source: ReadValue(spell, "source")))
            .ToArray()
            ?? Array.Empty<CharacterSpellSummary>();

        return new CharacterSpellsSection(
            Count: spells.Count,
            Spells: spells);
    }

    public CharacterPowersSection ParsePowers(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterPowerSummary> powers = character
            .Element("powers")?
            .Elements("power")
            .Select(power => new CharacterPowerSummary(
                Name: ReadValue(power, "name"),
                Rating: ParseInt(ReadValue(power, "rating")),
                Source: ReadValue(power, "source"),
                PointsPerLevel: ParseDecimal(ReadValue(power, "pointsperlevel"))))
            .ToArray()
            ?? Array.Empty<CharacterPowerSummary>();

        return new CharacterPowersSection(
            Count: powers.Count,
            Powers: powers);
    }

    public CharacterComplexFormsSection ParseComplexForms(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterComplexFormSummary> complexForms = character
            .Element("complexforms")?
            .Elements("complexform")
            .Select(form => new CharacterComplexFormSummary(
                Name: ReadValue(form, "name"),
                Target: ReadValue(form, "target"),
                Duration: ReadValue(form, "duration"),
                FadingValue: ReadValue(form, "fv"),
                Source: ReadValue(form, "source")))
            .ToArray()
            ?? Array.Empty<CharacterComplexFormSummary>();

        return new CharacterComplexFormsSection(
            Count: complexForms.Count,
            ComplexForms: complexForms);
    }

    public CharacterSpiritsSection ParseSpirits(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterSpiritSummary> spirits = character
            .Element("spirits")?
            .Elements("spirit")
            .Select(spirit => new CharacterSpiritSummary(
                Name: ReadSpiritName(spirit),
                Force: ParseInt(ReadValue(spirit, "force")),
                Services: ParseInt(ReadValue(spirit, "services")),
                Bound: ParseBool(ReadValue(spirit, "bound"))))
            .Where(spirit => !string.IsNullOrWhiteSpace(spirit.Name))
            .ToArray()
            ?? Array.Empty<CharacterSpiritSummary>();

        return new CharacterSpiritsSection(
            Count: spirits.Count,
            Spirits: spirits);
    }

    public CharacterFociSection ParseFoci(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterFocusSummary> foci = character
            .Element("foci")?
            .Elements("focus")
            .Select(focus => new CharacterFocusSummary(
                Guid: ReadValue(focus, "guid"),
                GearId: ReadValue(focus, "gearid")))
            .ToArray()
            ?? Array.Empty<CharacterFocusSummary>();

        return new CharacterFociSection(
            Count: foci.Count,
            Foci: foci);
    }

    public CharacterAiProgramsSection ParseAiPrograms(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterAiProgramSummary> aiPrograms = character
            .Element("aiprograms")?
            .Elements()
            .Select(program => new CharacterAiProgramSummary(
                Name: ReadValue(program, "name"),
                Rating: ReadValue(program, "rating"),
                Source: ReadValue(program, "source")))
            .Where(program => !string.IsNullOrWhiteSpace(program.Name))
            .ToArray()
            ?? Array.Empty<CharacterAiProgramSummary>();

        return new CharacterAiProgramsSection(
            Count: aiPrograms.Count,
            AiPrograms: aiPrograms);
    }

    public CharacterMartialArtsSection ParseMartialArts(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterMartialArtSummary> martialArts = character
            .Element("martialarts")?
            .Elements("martialart")
            .Select(martialArt => new CharacterMartialArtSummary(
                Name: ReadValue(martialArt, "name"),
                Source: ReadValue(martialArt, "source"),
                Rating: ParseInt(ReadValue(martialArt, "rating")),
                Techniques: martialArt
                    .Element("martialarttechniques")?
                    .Elements("martialarttechnique")
                    .Select(technique => ReadValue(technique, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray()
                    ?? Array.Empty<string>()))
            .ToArray()
            ?? Array.Empty<CharacterMartialArtSummary>();

        return new CharacterMartialArtsSection(
            Count: martialArts.Count,
            MartialArts: martialArts);
    }

    public CharacterLimitModifiersSection ParseLimitModifiers(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterLimitModifierSummary> modifiers = character
            .Element("limitmodifiers")?
            .Elements("limitmodifier")
            .Select(modifier => new CharacterLimitModifierSummary(
                Name: ReadValue(modifier, "name"),
                Limit: ReadValue(modifier, "limit"),
                Condition: ReadValue(modifier, "condition"),
                Bonus: ParseInt(ReadValue(modifier, "bonus"))))
            .ToArray()
            ?? Array.Empty<CharacterLimitModifierSummary>();

        return new CharacterLimitModifiersSection(
            Count: modifiers.Count,
            LimitModifiers: modifiers);
    }

    public CharacterLifestylesSection ParseLifestyles(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterLifestyleSummary> lifestyles = character
            .Element("lifestyles")?
            .Elements("lifestyle")
            .Select(lifestyle => new CharacterLifestyleSummary(
                Name: ReadValue(lifestyle, "name"),
                BaseLifestyle: ReadValue(lifestyle, "baselifestyle"),
                Source: ReadValue(lifestyle, "source"),
                Cost: ParseDecimal(ReadValue(lifestyle, "cost")),
                Months: ParseInt(ReadValue(lifestyle, "months"))))
            .ToArray()
            ?? Array.Empty<CharacterLifestyleSummary>();

        return new CharacterLifestylesSection(
            Count: lifestyles.Count,
            Lifestyles: lifestyles);
    }

    public CharacterMetamagicsSection ParseMetamagics(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterMetamagicSummary> metamagics = character
            .Element("metamagics")?
            .Elements("metamagic")
            .Select(metamagic => new CharacterMetamagicSummary(
                Name: ReadValue(metamagic, "name"),
                Source: ReadValue(metamagic, "source"),
                Grade: ParseInt(ReadValue(metamagic, "grade")),
                PaidWithKarma: ParseBool(ReadValue(metamagic, "paidwithkarma"))))
            .ToArray()
            ?? Array.Empty<CharacterMetamagicSummary>();

        return new CharacterMetamagicsSection(
            Count: metamagics.Count,
            Metamagics: metamagics);
    }

    public CharacterArtsSection ParseArts(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterArtSummary> arts = character
            .Element("arts")?
            .Elements("art")
            .Select(art => new CharacterArtSummary(
                Name: ReadValue(art, "name"),
                Source: ReadValue(art, "source"),
                Grade: ParseInt(ReadValue(art, "grade"))))
            .ToArray()
            ?? Array.Empty<CharacterArtSummary>();

        return new CharacterArtsSection(
            Count: arts.Count,
            Arts: arts);
    }

    public CharacterInitiationGradesSection ParseInitiationGrades(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterInitiationGradeSummary> initiationGrades = character
            .Element("initiationgrades")?
            .Elements("initiationgrade")
            .Select(grade => new CharacterInitiationGradeSummary(
                Grade: ParseInt(ReadValue(grade, "grade")),
                Res: ParseBool(ReadValue(grade, "res")),
                Group: ParseBool(ReadValue(grade, "group")),
                Ordeal: ParseBool(ReadValue(grade, "ordeal")),
                Schooling: ParseBool(ReadValue(grade, "schooling"))))
            .ToArray()
            ?? Array.Empty<CharacterInitiationGradeSummary>();

        return new CharacterInitiationGradesSection(
            Count: initiationGrades.Count,
            InitiationGrades: initiationGrades);
    }

    public CharacterCritterPowersSection ParseCritterPowers(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterCritterPowerSummary> critterPowers = character
            .Element("critterpowers")?
            .Elements("critterpower")
            .Select(power => new CharacterCritterPowerSummary(
                Name: ReadValue(power, "name"),
                Category: ReadValue(power, "category"),
                Type: ReadValue(power, "type"),
                Action: ReadValue(power, "action"),
                Range: ReadValue(power, "range"),
                Duration: ReadValue(power, "duration"),
                Source: ReadValue(power, "source"),
                Rating: ParseInt(ReadValue(power, "rating"))))
            .ToArray()
            ?? Array.Empty<CharacterCritterPowerSummary>();

        return new CharacterCritterPowersSection(
            Count: critterPowers.Count,
            CritterPowers: critterPowers);
    }

    public CharacterMentorSpiritsSection ParseMentorSpirits(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterMentorSpiritSummary> mentorSpirits = character
            .Element("mentorspirits")?
            .Elements("mentorspirit")
            .Select(mentor => new CharacterMentorSpiritSummary(
                Name: ReadValue(mentor, "name"),
                MentorType: ReadValue(mentor, "mentortype"),
                Source: ReadValue(mentor, "source"),
                Advantage: ReadValue(mentor, "advantage"),
                Disadvantage: ReadValue(mentor, "disadvantage")))
            .ToArray()
            ?? Array.Empty<CharacterMentorSpiritSummary>();

        return new CharacterMentorSpiritsSection(
            Count: mentorSpirits.Count,
            MentorSpirits: mentorSpirits);
    }

    public CharacterExpensesSection ParseExpenses(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterExpenseSummary> expenses = character
            .Element("expenses")?
            .Elements("expense")
            .Select(expense => new CharacterExpenseSummary(
                Date: ReadValue(expense, "date"),
                Amount: ParseDecimal(ReadValue(expense, "amount")),
                Reason: ReadValue(expense, "reason"),
                Type: ReadValue(expense, "type"),
                Refund: ParseBool(ReadValue(expense, "refund"))))
            .ToArray()
            ?? Array.Empty<CharacterExpenseSummary>();

        decimal totalKarma = expenses
            .Where(expense => string.Equals(expense.Type, "Karma", StringComparison.OrdinalIgnoreCase))
            .Sum(expense => expense.Amount);
        decimal totalNuyen = expenses
            .Where(expense => string.Equals(expense.Type, "Nuyen", StringComparison.OrdinalIgnoreCase))
            .Sum(expense => expense.Amount);

        return new CharacterExpensesSection(
            Count: expenses.Count,
            TotalKarma: totalKarma,
            TotalNuyen: totalNuyen,
            Expenses: expenses);
    }

    public CharacterSourcesSection ParseSources(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<string> sources = character
            .Element("sources")?
            .Elements("source")
            .Select(source => source.Value.Trim())
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();

        return new CharacterSourcesSection(
            Count: sources.Count,
            Sources: sources);
    }

    public CharacterLocationsSection ParseGearLocations(string xml) => ParseLocationsSection(xml, "gearlocations");

    public CharacterLocationsSection ParseArmorLocations(string xml) => ParseLocationsSection(xml, "armorlocations");

    public CharacterLocationsSection ParseWeaponLocations(string xml) => ParseLocationsSection(xml, "weaponlocations");

    public CharacterLocationsSection ParseVehicleLocations(string xml) => ParseLocationsSection(xml, "vehiclelocations");

    public CharacterCalendarSection ParseCalendar(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        XElement? calendar = character.Element("calendar");
        if (calendar == null)
            return new CharacterCalendarSection(0, Array.Empty<CharacterCalendarEntrySummary>());

        IReadOnlyList<CharacterCalendarEntrySummary> entries = calendar
            .Descendants()
            .Where(node => string.Equals(node.Name.LocalName, "entry", StringComparison.Ordinal))
            .Select(entry => new CharacterCalendarEntrySummary(
                Date: ReadValue(entry, "date"),
                Name: ReadValue(entry, "name"),
                Notes: ReadValue(entry, "notes")))
            .ToArray();

        return new CharacterCalendarSection(
            Count: entries.Count,
            Entries: entries);
    }

    public CharacterImprovementsSection ParseImprovements(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterImprovementSummary> improvements = character
            .Element("improvements")?
            .Elements("improvement")
            .Select(improvement => new CharacterImprovementSummary(
                ImprovedName: ReadValue(improvement, "improvedname"),
                ImprovementType: ReadValue(improvement, "improvementttype"),
                ImprovementSource: ReadValue(improvement, "improvementsource"),
                Rating: ParseInt(ReadValue(improvement, "rating")),
                Enabled: ParseBool(ReadValue(improvement, "enabled"))))
            .ToArray()
            ?? Array.Empty<CharacterImprovementSummary>();

        return new CharacterImprovementsSection(
            Count: improvements.Count,
            EnabledCount: improvements.Count(improvement => improvement.Enabled),
            Improvements: improvements);
    }

    public CharacterCustomDataDirectoryNamesSection ParseCustomDataDirectoryNames(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<string> names = character
            .Element("customdatadirectorynames")?
            .Elements("directoryname")
            .Select(name => name.Value.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();

        return new CharacterCustomDataDirectoryNamesSection(
            Count: names.Count,
            DirectoryNames: names);
    }

    public CharacterDrugsSection ParseDrugs(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterDrugSummary> drugs = character
            .Element("drugs")?
            .Elements("drug")
            .Select(drug => new CharacterDrugSummary(
                Name: ReadValue(drug, "name"),
                Category: ReadValue(drug, "category"),
                Source: ReadValue(drug, "source"),
                Rating: ParseInt(ReadValue(drug, "rating")),
                Quantity: ParseDecimal(ReadValue(drug, "qty"))))
            .ToArray()
            ?? Array.Empty<CharacterDrugSummary>();

        return new CharacterDrugsSection(
            Count: drugs.Count,
            Drugs: drugs);
    }

    private static IReadOnlyList<string> ReadItemNames(XElement character, string sectionName, string nodeName)
    {
        return character.Element(sectionName)?
            .Elements(nodeName)
            .Select(item => ReadValue(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static XElement LoadCharacterRoot(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        if (document.Root == null || !string.Equals(document.Root.Name.LocalName, "character", StringComparison.Ordinal))
            throw new InvalidOperationException("Root node must be <character>.");
        return document.Root;
    }

    private static string ReadValue(XElement parent, string nodeName)
    {
        return (parent.Element(nodeName)?.Value ?? string.Empty).Trim();
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out int parsed) ? parsed : 0;
    }

    private static bool ParseBool(string value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, out decimal parsed) ? parsed : 0m;
    }

    private static string ReadSpiritName(XElement spirit)
    {
        string name = ReadValue(spirit, "name");
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return spirit.Value.Trim();
    }

    private CharacterLocationsSection ParseLocationsSection(string xml, string sectionName)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterLocationSummary> locations = character
            .Element(sectionName)?
            .Elements("location")
            .Select(location => new CharacterLocationSummary(
                Guid: ReadValue(location, "guid"),
                Name: ReadValue(location, "name"),
                Notes: ReadValue(location, "notes")))
            .ToArray()
            ?? Array.Empty<CharacterLocationSummary>();

        return new CharacterLocationsSection(
            Count: locations.Count,
            Locations: locations);
    }
}
