using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Workspaces;

public sealed record HeroLabImportSnapshot(
    string SourceFormat,
    string RulesetId,
    string SourceLabel,
    CharacterProfileSection Profile,
    CharacterProgressSection Progress,
    CharacterRulesSection Rules,
    CharacterBuildSection Build,
    CharacterMovementSection Movement,
    CharacterAwakeningSection Awakening,
    CharacterAttributesSection Attributes,
    CharacterAttributeDetailsSection AttributeDetails,
    CharacterSkillsSection Skills,
    CharacterQualitiesSection Qualities,
    CharacterContactsSection Contacts,
    CharacterInventorySection Inventory,
    CharacterGearSection Gear,
    CharacterWeaponsSection Weapons,
    CharacterArmorsSection Armors,
    CharacterCyberwaresSection Cyberwares,
    CharacterVehiclesSection Vehicles,
    CharacterSpellsSection Spells,
    CharacterPowersSection Powers,
    CharacterComplexFormsSection ComplexForms,
    IReadOnlyList<HeroLabAttributeOracle> OracleAttributes,
    IReadOnlyList<HeroLabSkillOracle> OracleSkills,
    IReadOnlyList<HeroLabQualityOracle> OracleQualities,
    IReadOnlyList<HeroLabItemOracle> OracleItems,
    IReadOnlyList<HeroLabContactOracle> OracleContacts);

public sealed record HeroLabAttributeOracle(
    string Name,
    string DisplayName,
    int BaseValue,
    int TotalValue,
    int Minimum,
    int NaturalMaximum,
    int AugmentedMaximum,
    string Category);

public sealed record HeroLabSkillOracle(
    string Name,
    string Category,
    bool IsKnowledge,
    int BaseValue,
    int TotalValue,
    int DicePool,
    string Group,
    bool FromGroup);

public sealed record HeroLabQualityOracle(
    string Name,
    string Category,
    int Rank);

public sealed record HeroLabItemOracle(
    string Bucket,
    string Name,
    int Quantity,
    int Rating,
    string Category,
    bool Natural,
    bool UserAdded,
    bool WirelessPresent);

public sealed record HeroLabContactOracle(
    string Name,
    string Role,
    int Connection,
    int Loyalty);

public static class HeroLabShadowrunImporter
{
    public const string ClassicPortfolioFormatId = "herolab-classic-portfolio";
    public const string OnlineJsonFormatId = "herolab-online-json";

    private static readonly StringComparer Comparer = StringComparer.Ordinal;
    private static readonly IReadOnlyDictionary<string, string> AttributeNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Body"] = "BOD",
        ["Agility"] = "AGI",
        ["Reaction"] = "REA",
        ["Strength"] = "STR",
        ["Willpower"] = "WIL",
        ["Logic"] = "LOG",
        ["Intuition"] = "INT",
        ["Charisma"] = "CHA",
        ["Edge"] = "EDG",
        ["Magic"] = "MAG",
        ["Resonance"] = "RES",
        ["Essence"] = "ESS",
        ["Depth"] = "DEP"
    };

    public static string? DetectRulesetFromClassicPortfolio(byte[] portfolioBytes)
    {
        using ZipArchive archive = OpenPortfolioArchive(portfolioBytes);
        string? portfolioXml = ReadEntryText(archive, "portfolio.xml");
        if (string.IsNullOrWhiteSpace(portfolioXml))
        {
            return null;
        }

        XElement root = XElement.Parse(portfolioXml, LoadOptions.PreserveWhitespace);
        string gameFolder = root.Element("game")?.Attribute("folder")?.Value?.Trim() ?? string.Empty;
        return DetectRulesetFromClassicGameFolder(gameFolder);
    }

    public static string? DetectRulesetFromOnlineJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string gameCode = ReadJsonString(root, "metadata", "gameCode");
        string gameName = ReadJsonString(root, "metadata", "gameName");
        string combined = $"{gameCode} {gameName}";
        if (combined.Contains("shadowrun 6", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("shadowrun sixth", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("sr6", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("sixth world", StringComparison.OrdinalIgnoreCase))
        {
            return RulesetDefaults.Sr6;
        }

        if (combined.Contains("shadowrun 5", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("sr5", StringComparison.OrdinalIgnoreCase))
        {
            return RulesetDefaults.Sr5;
        }

        if (combined.Contains("shadowrun 4", StringComparison.OrdinalIgnoreCase)
            || combined.Contains("sr4", StringComparison.OrdinalIgnoreCase))
        {
            return RulesetDefaults.Sr4;
        }

        return null;
    }

    public static HeroLabImportSnapshot ImportClassicPortfolio(byte[] portfolioBytes, string sourceLabel)
    {
        using ZipArchive archive = OpenPortfolioArchive(portfolioBytes);
        string portfolioXml = ReadRequiredEntryText(archive, "portfolio.xml");
        XElement portfolioRoot = XElement.Parse(portfolioXml, LoadOptions.PreserveWhitespace);
        XElement? portfolioNode = portfolioRoot.Element("portfolio");
        XElement? gameNode = portfolioRoot.Element("game");
        if (portfolioNode is null || gameNode is null)
        {
            throw new InvalidOperationException($"{sourceLabel} must keep Hero Lab classic <game> and <portfolio> nodes.");
        }

        string? rulesetId = DetectRulesetFromClassicGameFolder(gameNode.Attribute("folder")?.Value);
        if (rulesetId is null)
        {
            throw new InvalidOperationException($"{sourceLabel} targets an unsupported Hero Lab classic game folder.");
        }

        List<XElement> heroes = portfolioNode.Elements("hero").ToList();
        if (heroes.Count == 0)
        {
            throw new InvalidOperationException($"{sourceLabel} must contain at least one Hero Lab classic hero.");
        }

        string activeHeroIndex = portfolioNode.Attribute("activehero")?.Value?.Trim() ?? string.Empty;
        XElement selectedHero = heroes.FirstOrDefault(hero => string.Equals(hero.Attribute("heroindex")?.Value?.Trim(), activeHeroIndex, StringComparison.Ordinal))
            ?? heroes[0];
        string heroName = selectedHero.Attribute("heroname")?.Value?.Trim() ?? string.Empty;
        string heroSummary = selectedHero.Attribute("herosummary")?.Value?.Trim() ?? string.Empty;
        string leadFileName = selectedHero.Attribute("leadfile")?.Value?.Trim() ?? string.Empty;

        XElement statblockCharacter = ResolveClassicStatblockCharacter(archive, heroName, heroSummary, sourceLabel);
        XElement? leadHero = string.IsNullOrWhiteSpace(leadFileName)
            ? null
            : LoadClassicLeadHero(archive, leadFileName, heroName);

        string gameName = gameNode.Attribute("game")?.Value?.Trim() ?? string.Empty;
        string appVersion = portfolioRoot.Element("product") is XElement product
            ? JoinNonEmpty(".", product.Attribute("major")?.Value, product.Attribute("minor")?.Value, product.Attribute("patch")?.Value)
            : string.Empty;

        return BuildClassicSnapshot(
            rulesetId,
            sourceLabel,
            gameName,
            appVersion,
            heroSummary,
            selectedHero,
            statblockCharacter,
            leadHero);
    }

    public static HeroLabImportSnapshot ImportOnlineJson(string json, string sourceLabel)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException($"{sourceLabel} must contain Hero Lab Online JSON content.");
        }

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        string? rulesetId = DetectRulesetFromOnlineJson(json);
        if (!string.Equals(rulesetId, RulesetDefaults.Sr6, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{sourceLabel} does not describe a supported Shadowrun Hero Lab Online export.");
        }

        JsonElement actor = ResolveOnlineLeadActor(root, sourceLabel);
        JsonElement items = TryReadJsonProperty(actor, "items", out JsonElement actorItems)
            ? actorItems
            : default;
        string playerName = ReadJsonString(actor, "player");
        string name = ReadJsonString(actor, "name");
        string gameName = ReadJsonString(root, "metadata", "gameName");
        string appVersion = ReadJsonString(root, "metadata", "hloVersion");
        string exportVersion = ReadJsonString(root, "metadata", "exportVersion");
        JsonElement gameValues = TryReadJsonProperty(actor, "gameValues", out JsonElement valuesElement) && valuesElement.ValueKind == JsonValueKind.Object
            ? valuesElement
            : default;

        List<HeroLabAttributeOracle> oracleAttributes = [];
        List<CharacterAttributeSummary> attributeSummaries = [];
        List<CharacterAttributeDetailSummary> attributeDetails = [];
        List<HeroLabSkillOracle> oracleSkills = [];
        List<CharacterSkillSummary> skillSummaries = [];
        List<HeroLabQualityOracle> oracleQualities = [];
        List<CharacterQualitySummary> qualitySummaries = [];
        List<HeroLabContactOracle> oracleContacts = [];
        List<CharacterContactSummary> contactSummaries = [];
        List<HeroLabItemOracle> oracleItems = [];
        List<CharacterWeaponSummary> weapons = [];
        List<CharacterArmorSummary> armors = [];
        List<CharacterCyberwareSummary> cyberwares = [];
        List<CharacterVehicleSummary> vehicles = [];
        List<CharacterSpellSummary> spells = [];
        List<CharacterPowerSummary> powers = [];
        List<CharacterComplexFormSummary> complexForms = [];
        List<CharacterGearSummary> gear = [];

        if (items.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            foreach ((string itemId, JsonElement item) in EnumerateOnlineItems(items))
            {
                ParseOnlineItem(
                    itemId,
                    item,
                    oracleAttributes,
                    attributeSummaries,
                    attributeDetails,
                    oracleSkills,
                    skillSummaries,
                    oracleQualities,
                    qualitySummaries,
                    oracleContacts,
                    contactSummaries,
                    oracleItems,
                    gear,
                    weapons,
                    armors,
                    cyberwares,
                    vehicles,
                    spells,
                    powers,
                    complexForms);
            }
        }

        skillSummaries = CollapseDuplicateSkills(skillSummaries, oracleSkills);

        CharacterInventorySection inventory = BuildInventorySection(oracleItems);
        CharacterProfileSection profile = new(
            Name: name,
            Alias: ReadJsonString(gameValues, "alias"),
            PlayerName: playerName,
            Metatype: ReadJsonString(gameValues, "metatype"),
            Metavariant: ReadJsonString(gameValues, "metavariant"),
            Sex: ReadJsonString(gameValues, "sex"),
            Age: ReadJsonString(gameValues, "age"),
            Height: ReadJsonString(gameValues, "height"),
            Weight: ReadJsonString(gameValues, "weight"),
            Hair: ReadJsonString(gameValues, "hair"),
            Eyes: ReadJsonString(gameValues, "eyes"),
            Skin: ReadJsonString(gameValues, "skin"),
            Concept: ReadJsonString(gameValues, "concept"),
            Description: ReadJsonString(gameValues, "description"),
            Background: ReadJsonString(gameValues, "background"),
            CreatedVersion: exportVersion,
            AppVersion: appVersion,
            BuildMethod: ReadJsonString(gameValues, "buildMethod"),
            GameplayOption: gameName,
            Created: true,
            Adept: ReadJsonBool(gameValues, "adept"),
            Magician: ReadJsonBool(gameValues, "magician"),
            Technomancer: ReadJsonBool(gameValues, "technomancer"),
            AI: ReadJsonBool(gameValues, "ai"),
            MainMugshotIndex: -1,
            MugshotCount: 0);
        CharacterProgressSection progress = new(
            Karma: ReadJsonDecimal(gameValues, "karma"),
            Nuyen: ReadJsonDecimal(gameValues, "nuyen"),
            StartingNuyen: 0m,
            StreetCred: 0,
            Notoriety: 0,
            PublicAwareness: 0,
            BurntStreetCred: 0,
            BuildKarma: 0,
            TotalAttributes: attributeSummaries.Sum(static attribute => attribute.BaseValue),
            TotalSpecial: 0,
            PhysicalCmFilled: 0,
            StunCmFilled: 0,
            TotalEssence: ReadJsonDecimal(gameValues, "essence"),
            InitiateGrade: ReadJsonInt(gameValues, "initiateGrade"),
            SubmersionGrade: ReadJsonInt(gameValues, "submersionGrade"),
            MagEnabled: profile.Magician || profile.Adept,
            ResEnabled: profile.Technomancer,
            DepEnabled: profile.AI);
        CharacterRulesSection rules = new(
            GameEdition: "SR6",
            Settings: OnlineJsonFormatId,
            GameplayOption: gameName,
            GameplayOptionQualityLimit: 0,
            MaxNuyen: 0,
            MaxKarma: 0,
            ContactMultiplier: 0,
            BannedWareGrades: Array.Empty<string>());
        CharacterBuildSection build = new(
            BuildMethod: profile.BuildMethod,
            PriorityMetatype: string.Empty,
            PriorityAttributes: string.Empty,
            PrioritySpecial: string.Empty,
            PrioritySkills: string.Empty,
            PriorityResources: string.Empty,
            PriorityTalent: string.Empty,
            SumToTen: 0,
            Special: 0,
            TotalSpecial: 0,
            TotalAttributes: attributeSummaries.Sum(static attribute => attribute.BaseValue),
            ContactPoints: contactSummaries.Sum(static contact => contact.Connection + contact.Loyalty),
            ContactPointsUsed: contactSummaries.Sum(static contact => contact.Connection + contact.Loyalty));
        CharacterMovementSection movement = new(
            Walk: ReadJsonString(gameValues, "walk"),
            Run: ReadJsonString(gameValues, "run"),
            Sprint: ReadJsonString(gameValues, "sprint"),
            WalkAlt: string.Empty,
            RunAlt: string.Empty,
            SprintAlt: string.Empty,
            PhysicalCmFilled: 0,
            StunCmFilled: 0);
        CharacterAwakeningSection awakening = new(
            MagEnabled: profile.Magician || profile.Adept,
            ResEnabled: profile.Technomancer,
            DepEnabled: profile.AI,
            Adept: profile.Adept,
            Magician: profile.Magician,
            Technomancer: profile.Technomancer,
            AI: profile.AI,
            InitiateGrade: ReadJsonInt(gameValues, "initiateGrade"),
            SubmersionGrade: ReadJsonInt(gameValues, "submersionGrade"),
            Tradition: ReadJsonString(gameValues, "tradition"),
            TraditionName: ReadJsonString(gameValues, "traditionName"),
            TraditionDrain: ReadJsonString(gameValues, "traditionDrain"),
            SpiritCombat: string.Empty,
            SpiritDetection: string.Empty,
            SpiritHealth: string.Empty,
            SpiritIllusion: string.Empty,
            SpiritManipulation: string.Empty,
            Stream: ReadJsonString(gameValues, "stream"),
            StreamDrain: ReadJsonString(gameValues, "streamDrain"),
            CurrentCounterspellingDice: 0,
            SpellLimit: 0,
            CfpLimit: 0,
            AiNormalProgramLimit: 0,
            AiAdvancedProgramLimit: 0);

        return new HeroLabImportSnapshot(
            SourceFormat: OnlineJsonFormatId,
            RulesetId: RulesetDefaults.Sr6,
            SourceLabel: sourceLabel,
            Profile: profile,
            Progress: progress,
            Rules: rules,
            Build: build,
            Movement: movement,
            Awakening: awakening,
            Attributes: new CharacterAttributesSection(attributeSummaries.Count, attributeSummaries),
            AttributeDetails: new CharacterAttributeDetailsSection(attributeDetails.Count, attributeDetails),
            Skills: new CharacterSkillsSection(skillSummaries.Count, skillSummaries.Count(static skill => skill.IsKnowledge), skillSummaries),
            Qualities: new CharacterQualitiesSection(qualitySummaries.Count, qualitySummaries),
            Contacts: new CharacterContactsSection(contactSummaries.Count, contactSummaries),
            Inventory: inventory,
            Gear: new CharacterGearSection(gear.Count, gear),
            Weapons: new CharacterWeaponsSection(weapons.Count, weapons),
            Armors: new CharacterArmorsSection(armors.Count, armors),
            Cyberwares: new CharacterCyberwaresSection(cyberwares.Count, cyberwares),
            Vehicles: new CharacterVehiclesSection(vehicles.Count, vehicles),
            Spells: new CharacterSpellsSection(spells.Count, spells),
            Powers: new CharacterPowersSection(powers.Count, powers),
            ComplexForms: new CharacterComplexFormsSection(complexForms.Count, complexForms),
            OracleAttributes: oracleAttributes,
            OracleSkills: oracleSkills,
            OracleQualities: oracleQualities,
            OracleItems: oracleItems,
            OracleContacts: oracleContacts);
    }

    private static HeroLabImportSnapshot BuildClassicSnapshot(
        string rulesetId,
        string sourceLabel,
        string gameName,
        string appVersion,
        string heroSummary,
        XElement heroNode,
        XElement statblockCharacter,
        XElement? leadHero)
    {
        List<HeroLabAttributeOracle> oracleAttributes = statblockCharacter.Element("attributes")?
            .Elements("attribute")
            .Select(ParseClassicAttributeOracle)
            .Where(static attribute => !string.IsNullOrWhiteSpace(attribute.Name))
            .ToList()
            ?? [];
        List<CharacterAttributeSummary> attributes = oracleAttributes
            .Select(static attribute => new CharacterAttributeSummary(attribute.Name, attribute.BaseValue, attribute.TotalValue))
            .ToList();
        List<CharacterAttributeDetailSummary> attributeDetails = oracleAttributes
            .Select(static attribute => new CharacterAttributeDetailSummary(
                attribute.Name,
                attribute.Minimum,
                attribute.NaturalMaximum,
                attribute.AugmentedMaximum,
                attribute.BaseValue,
                Math.Max(0, attribute.TotalValue - attribute.BaseValue),
                attribute.TotalValue,
                attribute.Category))
            .ToList();

        List<HeroLabSkillOracle> oracleSkills = ParseClassicSkills(statblockCharacter);
        List<CharacterSkillSummary> skills = CollapseDuplicateSkills(
            oracleSkills.Select(static skill => new CharacterSkillSummary(
                Guid: string.Empty,
                Suid: skill.Name,
                Category: skill.Category,
                IsKnowledge: skill.IsKnowledge,
                BaseValue: skill.BaseValue,
                KarmaValue: Math.Max(0, skill.TotalValue - skill.BaseValue),
                Specializations: Array.Empty<string>()))
            .ToList(),
            oracleSkills);

        List<HeroLabQualityOracle> oracleQualities = ParseClassicQualities(statblockCharacter);
        List<CharacterQualitySummary> qualities = oracleQualities
            .Select(static quality => new CharacterQualitySummary(quality.Name, quality.Category, quality.Rank))
            .ToList();

        List<HeroLabContactOracle> oracleContacts = ParseClassicContacts(statblockCharacter);
        List<CharacterContactSummary> contacts = oracleContacts
            .Select(static contact => new CharacterContactSummary(contact.Name, contact.Role, string.Empty, contact.Connection, contact.Loyalty))
            .ToList();

        List<HeroLabItemOracle> oracleItems = ParseClassicItems(statblockCharacter);
        CharacterInventorySection inventory = BuildInventorySection(oracleItems);
        List<CharacterGearSummary> gear = oracleItems
            .Where(static item => string.Equals(item.Bucket, "gear", StringComparison.Ordinal))
            .Select(static item => new CharacterGearSummary(
                Guid: string.Empty,
                Name: item.Name,
                Category: item.Category,
                Rating: item.Rating > 0 ? item.Rating.ToString() : string.Empty,
                Quantity: item.Quantity.ToString(),
                Cost: string.Empty,
                Equipped: false,
                Location: string.Empty))
            .ToList();
        List<CharacterWeaponSummary> weapons = oracleItems
            .Where(static item => string.Equals(item.Bucket, "weapon", StringComparison.Ordinal))
            .Select(static item => new CharacterWeaponSummary(
                Guid: string.Empty,
                Name: item.Name,
                Category: item.Category,
                Type: string.Empty,
                Damage: string.Empty,
                AP: string.Empty,
                Accuracy: string.Empty,
                Mode: string.Empty,
                Ammo: string.Empty,
                Cost: string.Empty,
                Equipped: false))
            .ToList();
        List<CharacterArmorSummary> armors = oracleItems
            .Where(static item => string.Equals(item.Bucket, "armor", StringComparison.Ordinal))
            .Select(static item => new CharacterArmorSummary(
                Guid: string.Empty,
                Name: item.Name,
                Category: item.Category,
                ArmorValue: string.Empty,
                Rating: item.Rating > 0 ? item.Rating.ToString() : string.Empty,
                Cost: string.Empty,
                Equipped: false))
            .ToList();
        List<CharacterCyberwareSummary> cyberwares = oracleItems
            .Where(static item => string.Equals(item.Bucket, "cyberware", StringComparison.Ordinal) || string.Equals(item.Bucket, "bioware", StringComparison.Ordinal))
            .Select(static item => new CharacterCyberwareSummary(
                Guid: string.Empty,
                Name: item.Name,
                Category: item.Category,
                Essence: string.Empty,
                Capacity: string.Empty,
                Rating: item.Rating > 0 ? item.Rating.ToString() : string.Empty,
                Cost: string.Empty,
                Grade: string.Empty,
                Location: string.Empty))
            .ToList();
        List<CharacterVehicleSummary> vehicles = oracleItems
            .Where(static item => string.Equals(item.Bucket, "vehicle", StringComparison.Ordinal))
            .Select(static item => new CharacterVehicleSummary(
                Guid: string.Empty,
                Name: item.Name,
                Category: item.Category,
                Handling: string.Empty,
                Speed: string.Empty,
                Body: string.Empty,
                Armor: string.Empty,
                Sensor: string.Empty,
                Seats: string.Empty,
                Cost: string.Empty,
                ModCount: 0,
                WeaponCount: 0))
            .ToList();

        List<CharacterSpellSummary> spells = ParseClassicSpells(statblockCharacter);
        List<CharacterPowerSummary> powers = ParseClassicPowers(statblockCharacter);
        List<CharacterComplexFormSummary> complexForms = ParseClassicComplexForms(statblockCharacter);

        string displayName = (statblockCharacter.Attribute("name")?.Value?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = heroNode.Attribute("heroname")?.Value?.Trim()
                ?? heroSummary
                ?? sourceLabel;
        }

        SplitClassicName(displayName, out string name, out string alias);
        XElement? personal = statblockCharacter.Element("personal");
        string metatype = statblockCharacter.Element("race")?.Attribute("name")?.Value?.Trim() ?? string.Empty;
        string heritage = statblockCharacter.Element("heritage")?.Attribute("name")?.Value?.Trim() ?? string.Empty;
        string buildMethod = statblockCharacter.Element("creation")?.Element("bp") is not null ? "BP" : "Karma";
        bool magician = ContainsAnyNamed(oracleQualities.Select(static quality => quality.Name), "Magician", "Mystic Adept");
        bool adept = ContainsAnyNamed(oracleQualities.Select(static quality => quality.Name), "Adept", "Mystic Adept");
        bool technomancer = ContainsAnyNamed(oracleQualities.Select(static quality => quality.Name), "Technomancer");
        bool isAi = string.Equals(metatype, "A.I.", StringComparison.OrdinalIgnoreCase)
            || string.Equals(metatype, "AI", StringComparison.OrdinalIgnoreCase);
        int physicalFilled = ParseLeadUsagePool(leadHero, "DmgNet", "5");
        int stunFilled = ParseLeadUsagePool(leadHero, "DmgNet", "6");

        CharacterProfileSection profile = new(
            Name: name,
            Alias: alias,
            PlayerName: statblockCharacter.Attribute("playername")?.Value?.Trim() ?? string.Empty,
            Metatype: metatype,
            Metavariant: string.Empty,
            Sex: personal?.Attribute("gender")?.Value?.Trim() ?? string.Empty,
            Age: personal?.Attribute("age")?.Value?.Trim() ?? string.Empty,
            Height: personal?.Element("charheight")?.Attribute("text")?.Value?.Trim() ?? string.Empty,
            Weight: personal?.Element("charweight")?.Attribute("text")?.Value?.Trim() ?? string.Empty,
            Hair: personal?.Attribute("hair")?.Value?.Trim() ?? string.Empty,
            Eyes: personal?.Attribute("eyes")?.Value?.Trim() ?? string.Empty,
            Skin: personal?.Attribute("skin")?.Value?.Trim() ?? string.Empty,
            Concept: heroSummary ?? string.Empty,
            Description: personal?.Element("description")?.Value?.Trim() ?? string.Empty,
            Background: string.Empty,
            CreatedVersion: appVersion,
            AppVersion: appVersion,
            BuildMethod: buildMethod,
            GameplayOption: gameName,
            Created: ParseDecimal(statblockCharacter.Element("karma")?.Attribute("total")?.Value) > 0m,
            Adept: adept,
            Magician: magician,
            Technomancer: technomancer,
            AI: isAi,
            MainMugshotIndex: -1,
            MugshotCount: heroNode.Elements("userimage").Count());
        CharacterProgressSection progress = new(
            Karma: ParseDecimal(statblockCharacter.Element("karma")?.Attribute("total")?.Value),
            Nuyen: ParseDecimal(statblockCharacter.Element("cash")?.Attribute("total")?.Value),
            StartingNuyen: 0m,
            StreetCred: 0,
            Notoriety: 0,
            PublicAwareness: 0,
            BurntStreetCred: 0,
            BuildKarma: 0,
            TotalAttributes: attributes.Sum(static attribute => attribute.BaseValue),
            TotalSpecial: 0,
            PhysicalCmFilled: physicalFilled,
            StunCmFilled: stunFilled,
            TotalEssence: oracleAttributes.FirstOrDefault(static attribute => string.Equals(attribute.Name, "ESS", StringComparison.Ordinal))?.TotalValue ?? 0m,
            InitiateGrade: 0,
            SubmersionGrade: 0,
            MagEnabled: magician || adept,
            ResEnabled: technomancer,
            DepEnabled: isAi);
        CharacterRulesSection rules = new(
            GameEdition: rulesetId.Equals(RulesetDefaults.Sr4, StringComparison.Ordinal) ? "SR4" : "SR5",
            Settings: ClassicPortfolioFormatId,
            GameplayOption: gameName,
            GameplayOptionQualityLimit: 0,
            MaxNuyen: 0,
            MaxKarma: 0,
            ContactMultiplier: 0,
            BannedWareGrades: Array.Empty<string>());
        CharacterBuildSection build = new(
            BuildMethod: buildMethod,
            PriorityMetatype: string.Empty,
            PriorityAttributes: string.Empty,
            PrioritySpecial: string.Empty,
            PrioritySkills: string.Empty,
            PriorityResources: string.Empty,
            PriorityTalent: string.Empty,
            SumToTen: 0,
            Special: 0,
            TotalSpecial: 0,
            TotalAttributes: attributes.Sum(static attribute => attribute.BaseValue),
            ContactPoints: contacts.Sum(static contact => contact.Connection + contact.Loyalty),
            ContactPointsUsed: contacts.Sum(static contact => contact.Connection + contact.Loyalty));
        CharacterMovementSection movement = new(
            Walk: ReadClassicMovementValue(statblockCharacter, "walk"),
            Run: ReadClassicMovementValue(statblockCharacter, "run"),
            Sprint: ReadClassicMovementValue(statblockCharacter, "sprint"),
            WalkAlt: ReadClassicMovementValue(statblockCharacter, "walkalt"),
            RunAlt: ReadClassicMovementValue(statblockCharacter, "runalt"),
            SprintAlt: ReadClassicMovementValue(statblockCharacter, "sprintalt"),
            PhysicalCmFilled: physicalFilled,
            StunCmFilled: stunFilled);
        CharacterAwakeningSection awakening = new(
            MagEnabled: magician || adept,
            ResEnabled: technomancer,
            DepEnabled: isAi,
            Adept: adept,
            Magician: magician,
            Technomancer: technomancer,
            AI: isAi,
            InitiateGrade: 0,
            SubmersionGrade: 0,
            Tradition: statblockCharacter.Element("magic")?.Element("tradition")?.Value?.Trim() ?? string.Empty,
            TraditionName: statblockCharacter.Element("magic")?.Element("tradition")?.Value?.Trim() ?? string.Empty,
            TraditionDrain: string.Empty,
            SpiritCombat: string.Empty,
            SpiritDetection: string.Empty,
            SpiritHealth: string.Empty,
            SpiritIllusion: string.Empty,
            SpiritManipulation: string.Empty,
            Stream: string.Empty,
            StreamDrain: string.Empty,
            CurrentCounterspellingDice: 0,
            SpellLimit: 0,
            CfpLimit: 0,
            AiNormalProgramLimit: 0,
            AiAdvancedProgramLimit: 0);

        return new HeroLabImportSnapshot(
            SourceFormat: ClassicPortfolioFormatId,
            RulesetId: rulesetId,
            SourceLabel: sourceLabel,
            Profile: profile,
            Progress: progress,
            Rules: rules,
            Build: build,
            Movement: movement,
            Awakening: awakening,
            Attributes: new CharacterAttributesSection(attributes.Count, attributes),
            AttributeDetails: new CharacterAttributeDetailsSection(attributeDetails.Count, attributeDetails),
            Skills: new CharacterSkillsSection(skills.Count, skills.Count(static skill => skill.IsKnowledge), skills),
            Qualities: new CharacterQualitiesSection(qualities.Count, qualities),
            Contacts: new CharacterContactsSection(contacts.Count, contacts),
            Inventory: inventory,
            Gear: new CharacterGearSection(gear.Count, gear),
            Weapons: new CharacterWeaponsSection(weapons.Count, weapons),
            Armors: new CharacterArmorsSection(armors.Count, armors),
            Cyberwares: new CharacterCyberwaresSection(cyberwares.Count, cyberwares),
            Vehicles: new CharacterVehiclesSection(vehicles.Count, vehicles),
            Spells: new CharacterSpellsSection(spells.Count, spells),
            Powers: new CharacterPowersSection(powers.Count, powers),
            ComplexForms: new CharacterComplexFormsSection(complexForms.Count, complexForms),
            OracleAttributes: oracleAttributes,
            OracleSkills: oracleSkills,
            OracleQualities: oracleQualities,
            OracleItems: oracleItems,
            OracleContacts: oracleContacts);
    }

    private static ZipArchive OpenPortfolioArchive(byte[] portfolioBytes)
    {
        MemoryStream stream = new(portfolioBytes, writable: false);
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    private static string? DetectRulesetFromClassicGameFolder(string? folder)
    {
        string normalized = folder?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "shadowrun4" => RulesetDefaults.Sr4,
            "shadowrun5" => RulesetDefaults.Sr5,
            _ => null
        };
    }

    private static string ReadRequiredEntryText(ZipArchive archive, string fileNameSuffix)
    {
        return ReadEntryText(archive, fileNameSuffix)
            ?? throw new InvalidOperationException($"Hero Lab classic portfolio is missing '{fileNameSuffix}'.");
    }

    private static string? ReadEntryText(ZipArchive archive, string fileNameSuffix)
    {
        ZipArchiveEntry? entry = archive.Entries.FirstOrDefault(candidate =>
            candidate.FullName.EndsWith(fileNameSuffix, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            return null;
        }

        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static XElement ResolveClassicStatblockCharacter(ZipArchive archive, string heroName, string heroSummary, string sourceLabel)
    {
        List<XElement> characters = archive.Entries
            .Where(static entry => entry.FullName.StartsWith("statblocks_xml", StringComparison.OrdinalIgnoreCase)
                && entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(ReadClassicStatblockCharacter)
            .Where(static character => character is not null)
            .Cast<XElement>()
            .ToList();

        if (characters.Count == 0)
        {
            characters = archive.Entries
                .Where(static entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Select(ReadClassicStatblockCharacter)
                .Where(static character => character is not null)
                .Cast<XElement>()
                .ToList();
        }

        if (characters.Count == 0)
        {
            throw new InvalidOperationException($"{sourceLabel} must keep at least one Hero Lab classic XML statblock.");
        }

        if (!string.IsNullOrWhiteSpace(heroName))
        {
            XElement? exact = characters.FirstOrDefault(character =>
                string.Equals(character.Attribute("name")?.Value?.Trim(), heroName, StringComparison.Ordinal));
            if (exact is not null)
            {
                return exact;
            }
        }

        if (characters.Count == 1)
        {
            return characters[0];
        }

        if (!string.IsNullOrWhiteSpace(heroSummary))
        {
            XElement? summaryMatch = characters.FirstOrDefault(character =>
                string.Equals(character.Attribute("name")?.Value?.Trim(), heroSummary, StringComparison.OrdinalIgnoreCase));
            if (summaryMatch is not null)
            {
                return summaryMatch;
            }
        }

        return characters[0];
    }

    private static XElement? ReadClassicStatblockCharacter(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using StreamReader reader = new(stream, detectEncodingFromByteOrderMarks: true);
        XDocument document = XDocument.Parse(reader.ReadToEnd(), LoadOptions.PreserveWhitespace);
        return document.Root?.Element("public")?.Element("character")
            ?? document.Root?.Descendants("character").FirstOrDefault();
    }

    private static XElement? LoadClassicLeadHero(ZipArchive archive, string leadFileName, string heroName)
    {
        string? leadXml = ReadEntryText(archive, leadFileName);
        if (string.IsNullOrWhiteSpace(leadXml))
        {
            return null;
        }

        XDocument document = XDocument.Parse(leadXml, LoadOptions.PreserveWhitespace);
        XElement? hero = document.Root?.Element("hero");
        if (hero is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(heroName))
        {
            return hero;
        }

        return string.Equals(hero.Attribute("heroname")?.Value?.Trim(), heroName, StringComparison.Ordinal)
            ? hero
            : hero;
    }

    private static HeroLabAttributeOracle ParseClassicAttributeOracle(XElement attribute)
    {
        string displayName = attribute.Attribute("name")?.Value?.Trim() ?? string.Empty;
        int baseValue = ParseInt(attribute.Attribute("base")?.Value);
        int totalValue = ParseInt(attribute.Attribute("modified")?.Value);
        if (totalValue == 0)
        {
            totalValue = ParseInt(attribute.Attribute("text")?.Value);
        }

        return new HeroLabAttributeOracle(
            Name: NormalizeAttributeName(displayName),
            DisplayName: displayName,
            BaseValue: baseValue,
            TotalValue: totalValue,
            Minimum: ParseInt(attribute.Attribute("minimum")?.Value),
            NaturalMaximum: ParseInt(attribute.Attribute("naturalmaximum")?.Value),
            AugmentedMaximum: ParseInt(attribute.Attribute("augmentedmaximum")?.Value),
            Category: attribute.Attribute("category")?.Value?.Trim() ?? string.Empty);
    }

    private static List<HeroLabSkillOracle> ParseClassicSkills(XElement statblockCharacter)
    {
        XElement? skillsNode = statblockCharacter.Element("skills");
        if (skillsNode is null)
        {
            return [];
        }

        List<HeroLabSkillOracle> skills = [];
        foreach (XElement bucket in skillsNode.Elements())
        {
            bool isKnowledge = string.Equals(bucket.Name.LocalName, "knowledge", StringComparison.OrdinalIgnoreCase)
                || string.Equals(bucket.Name.LocalName, "language", StringComparison.OrdinalIgnoreCase);
            string category = bucket.Name.LocalName;
            foreach (XElement skill in bucket.Elements("skill"))
            {
                string name = skill.Attribute("name")?.Value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                int baseValue = ParseInt(skill.Attribute("base")?.Value);
                int totalValue = ParseInt(skill.Attribute("modified")?.Value);
                if (totalValue == 0)
                {
                    totalValue = ParseInt(skill.Attribute("text")?.Value);
                }

                skills.Add(new HeroLabSkillOracle(
                    Name: name,
                    Category: category,
                    IsKnowledge: isKnowledge,
                    BaseValue: baseValue,
                    TotalValue: totalValue,
                    DicePool: ParseInt(skill.Attribute("dicepool")?.Value),
                    Group: skill.Attribute("group")?.Value?.Trim() ?? string.Empty,
                    FromGroup: ParseBool(skill.Attribute("fromgroup")?.Value)));
            }
        }

        return skills;
    }

    private static List<CharacterSkillSummary> CollapseDuplicateSkills(List<CharacterSkillSummary> skills, List<HeroLabSkillOracle> oracleSkills)
    {
        return skills
            .GroupBy(
                skill => $"{skill.Category}|{skill.Suid}|{skill.IsKnowledge}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                HeroLabSkillOracle? bestOracle = oracleSkills
                    .Where(skill => string.Equals(skill.Name, group.First().Suid, StringComparison.Ordinal)
                        && string.Equals(skill.Category, group.First().Category, StringComparison.Ordinal)
                        && skill.IsKnowledge == group.First().IsKnowledge)
                    .OrderByDescending(static skill => skill.DicePool)
                    .ThenByDescending(static skill => skill.TotalValue)
                    .FirstOrDefault();
                CharacterSkillSummary best = group
                    .OrderByDescending(skill => bestOracle?.DicePool ?? 0)
                    .ThenByDescending(skill => skill.BaseValue + skill.KarmaValue)
                    .First();
                return bestOracle is null
                    ? best
                    : best with
                    {
                        KarmaValue = Math.Max(0, bestOracle.TotalValue - bestOracle.BaseValue)
                    };
            })
            .OrderBy(static skill => skill.Suid, Comparer)
            .ToList();
    }

    private static List<HeroLabQualityOracle> ParseClassicQualities(XElement statblockCharacter)
    {
        XElement? qualitiesNode = statblockCharacter.Element("qualities");
        if (qualitiesNode is null)
        {
            return [];
        }

        return qualitiesNode.Elements()
            .SelectMany(bucket => bucket.Elements("quality").Select(quality => new HeroLabQualityOracle(
                Name: quality.Attribute("name")?.Value?.Trim() ?? string.Empty,
                Category: bucket.Name.LocalName,
                Rank: ParseInt(quality.Attribute("rank")?.Value))))
            .Where(static quality => !string.IsNullOrWhiteSpace(quality.Name))
            .ToList();
    }

    private static List<HeroLabContactOracle> ParseClassicContacts(XElement statblockCharacter)
    {
        return statblockCharacter.Element("contacts")?
            .Elements("contact")
            .Select(contact => new HeroLabContactOracle(
                Name: contact.Attribute("name")?.Value?.Trim() ?? string.Empty,
                Role: contact.Attribute("type")?.Value?.Trim() ?? string.Empty,
                Connection: ParseInt(contact.Attribute("connection")?.Value),
                Loyalty: ParseInt(contact.Attribute("loyalty")?.Value)))
            .Where(static contact => !string.IsNullOrWhiteSpace(contact.Name))
            .ToList()
            ?? [];
    }

    private static List<HeroLabItemOracle> ParseClassicItems(XElement statblockCharacter)
    {
        List<HeroLabItemOracle> items = [];
        XElement? gear = statblockCharacter.Element("gear");
        if (gear is null)
        {
            return items;
        }

        AddClassicItems(items, gear.Element("weapons"), "weapon");
        AddClassicItems(items, gear.Element("armor"), "armor");
        AddClassicItems(items, gear.Element("equipment"), "gear");

        XElement? augmentations = gear.Element("augmentations");
        if (augmentations is not null)
        {
            AddClassicItems(items, augmentations.Element("cyberware"), "cyberware");
            AddClassicItems(items, augmentations.Element("bioware"), "bioware");
        }

        XElement? vehicles = statblockCharacter.Element("vehicles");
        if (vehicles is not null)
        {
            AddClassicItems(items, vehicles, "vehicle");
        }

        return items;
    }

    private static void AddClassicItems(List<HeroLabItemOracle> items, XElement? parent, string bucket)
    {
        if (parent is null)
        {
            return;
        }

        foreach (XElement item in parent.Elements("item"))
        {
            ParseClassicItemRecursive(items, item, bucket);
        }
    }

    private static void ParseClassicItemRecursive(List<HeroLabItemOracle> items, XElement item, string bucket)
    {
        string name = item.Attribute("name")?.Value?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
        {
            items.Add(new HeroLabItemOracle(
                Bucket: bucket,
                Name: name,
                Quantity: Math.Max(1, ParseInt(item.Attribute("quantity")?.Value, fallback: 1)),
                Rating: ParseInt(item.Attribute("rating")?.Value),
                Category: item.Attribute("category")?.Value?.Trim() ?? string.Empty,
                Natural: ParseBool(item.Attribute("natural")?.Value),
                UserAdded: !string.Equals(item.Attribute("useradded")?.Value?.Trim(), "no", StringComparison.OrdinalIgnoreCase),
                WirelessPresent: string.Equals(item.Attribute("wireless")?.Value?.Trim(), "Present", StringComparison.OrdinalIgnoreCase)));
        }

        foreach (XElement nested in item.Elements("item"))
        {
            ParseClassicItemRecursive(items, nested, bucket);
        }
    }

    private static List<CharacterSpellSummary> ParseClassicSpells(XElement statblockCharacter)
    {
        return statblockCharacter
            .Descendants("spell")
            .Select(spell => new CharacterSpellSummary(
                Name: spell.Attribute("name")?.Value?.Trim() ?? string.Empty,
                Category: spell.Attribute("category")?.Value?.Trim() ?? string.Empty,
                Type: spell.Attribute("type")?.Value?.Trim() ?? string.Empty,
                Range: spell.Attribute("range")?.Value?.Trim() ?? string.Empty,
                Duration: spell.Attribute("duration")?.Value?.Trim() ?? string.Empty,
                DrainValue: spell.Attribute("drain")?.Value?.Trim() ?? string.Empty,
                Source: string.Empty))
            .Where(static spell => !string.IsNullOrWhiteSpace(spell.Name))
            .Distinct()
            .ToList();
    }

    private static List<CharacterPowerSummary> ParseClassicPowers(XElement statblockCharacter)
    {
        return statblockCharacter
            .Descendants("power")
            .Select(power => new CharacterPowerSummary(
                Name: power.Attribute("name")?.Value?.Trim() ?? string.Empty,
                Rating: ParseInt(power.Attribute("rating")?.Value),
                Source: string.Empty,
                PointsPerLevel: ParseDecimal(power.Attribute("pointsperlevel")?.Value)))
            .Where(static power => !string.IsNullOrWhiteSpace(power.Name))
            .Distinct()
            .ToList();
    }

    private static List<CharacterComplexFormSummary> ParseClassicComplexForms(XElement statblockCharacter)
    {
        return statblockCharacter
            .Descendants("complexform")
            .Select(form => new CharacterComplexFormSummary(
                Name: form.Attribute("name")?.Value?.Trim() ?? string.Empty,
                Target: form.Attribute("target")?.Value?.Trim() ?? string.Empty,
                Duration: form.Attribute("duration")?.Value?.Trim() ?? string.Empty,
                FadingValue: form.Attribute("fading")?.Value?.Trim() ?? string.Empty,
                Source: string.Empty))
            .Where(static form => !string.IsNullOrWhiteSpace(form.Name))
            .Distinct()
            .ToList();
    }

    private static CharacterInventorySection BuildInventorySection(IReadOnlyList<HeroLabItemOracle> items)
    {
        string[] gearNames = items.Where(static item => string.Equals(item.Bucket, "gear", StringComparison.Ordinal))
            .Select(static item => item.Name)
            .ToArray();
        string[] weaponNames = items.Where(static item => string.Equals(item.Bucket, "weapon", StringComparison.Ordinal))
            .Select(static item => item.Name)
            .ToArray();
        string[] armorNames = items.Where(static item => string.Equals(item.Bucket, "armor", StringComparison.Ordinal))
            .Select(static item => item.Name)
            .ToArray();
        string[] cyberwareNames = items.Where(static item => string.Equals(item.Bucket, "cyberware", StringComparison.Ordinal) || string.Equals(item.Bucket, "bioware", StringComparison.Ordinal))
            .Select(static item => item.Name)
            .ToArray();
        string[] vehicleNames = items.Where(static item => string.Equals(item.Bucket, "vehicle", StringComparison.Ordinal))
            .Select(static item => item.Name)
            .ToArray();

        return new CharacterInventorySection(
            GearCount: gearNames.Length,
            WeaponCount: weaponNames.Length,
            ArmorCount: armorNames.Length,
            CyberwareCount: cyberwareNames.Length,
            VehicleCount: vehicleNames.Length,
            GearNames: gearNames,
            WeaponNames: weaponNames,
            ArmorNames: armorNames,
            CyberwareNames: cyberwareNames,
            VehicleNames: vehicleNames);
    }

    private static void ParseOnlineItem(
        string itemId,
        JsonElement item,
        List<HeroLabAttributeOracle> oracleAttributes,
        List<CharacterAttributeSummary> attributeSummaries,
        List<CharacterAttributeDetailSummary> attributeDetails,
        List<HeroLabSkillOracle> oracleSkills,
        List<CharacterSkillSummary> skillSummaries,
        List<HeroLabQualityOracle> oracleQualities,
        List<CharacterQualitySummary> qualitySummaries,
        List<HeroLabContactOracle> oracleContacts,
        List<CharacterContactSummary> contactSummaries,
        List<HeroLabItemOracle> oracleItems,
        List<CharacterGearSummary> gear,
        List<CharacterWeaponSummary> weapons,
        List<CharacterArmorSummary> armors,
        List<CharacterCyberwareSummary> cyberwares,
        List<CharacterVehicleSummary> vehicles,
        List<CharacterSpellSummary> spells,
        List<CharacterPowerSummary> powers,
        List<CharacterComplexFormSummary> complexForms)
    {
        string name = ReadJsonString(item, "name");
        string compset = ReadJsonString(item, "compset");
        string bucket = DetermineOnlineBucket(itemId, compset);
        string normalizedName = NormalizeAttributeName(name);
        switch (bucket)
        {
            case "attribute":
            {
                int baseValue = ReadJsonInt(item, "baseValue", "base", "scoreBase", "valueBase", "value");
                int totalValue = ReadJsonInt(item, "totalValue", "total", "value", "net", "scoreNet", "valueNet", "modified");
                if (totalValue == 0)
                {
                    totalValue = baseValue;
                }

                HeroLabAttributeOracle oracle = new(
                    Name: normalizedName,
                    DisplayName: name,
                    BaseValue: baseValue,
                    TotalValue: totalValue,
                    Minimum: ReadJsonInt(item, "minimum", "min"),
                    NaturalMaximum: ReadJsonInt(item, "naturalMaximum", "max"),
                    AugmentedMaximum: ReadJsonInt(item, "augmentedMaximum", "augMax"),
                    Category: ReadJsonString(item, "category"));
                if (!string.IsNullOrWhiteSpace(oracle.Name))
                {
                    oracleAttributes.Add(oracle);
                    attributeSummaries.Add(new CharacterAttributeSummary(oracle.Name, oracle.BaseValue, oracle.TotalValue));
                    attributeDetails.Add(new CharacterAttributeDetailSummary(
                        oracle.Name,
                        oracle.Minimum,
                        oracle.NaturalMaximum,
                        oracle.AugmentedMaximum,
                        oracle.BaseValue,
                        Math.Max(0, oracle.TotalValue - oracle.BaseValue),
                        oracle.TotalValue,
                        oracle.Category));
                }

                break;
            }

            case "skill":
            {
                int baseValue = ReadJsonInt(item, "baseValue", "base", "ratingBase", "valueBase", "value");
                int totalValue = ReadJsonInt(item, "totalValue", "total", "value", "net", "ratingNet", "valueNet", "modified");
                if (totalValue == 0)
                {
                    totalValue = baseValue;
                }

                bool isKnowledge = ReadJsonBool(item, "isKnowledge")
                    || compset.Contains("know", StringComparison.OrdinalIgnoreCase)
                    || itemId.StartsWith("kn", StringComparison.OrdinalIgnoreCase)
                    || itemId.StartsWith("lng", StringComparison.OrdinalIgnoreCase);
                HeroLabSkillOracle oracle = new(
                    Name: name,
                    Category: compset,
                    IsKnowledge: isKnowledge,
                    BaseValue: baseValue,
                    TotalValue: totalValue,
                    DicePool: ReadJsonInt(item, "dicePool", "dicepool"),
                    Group: ReadJsonString(item, "group"),
                    FromGroup: ReadJsonBool(item, "fromGroup"));
                if (!string.IsNullOrWhiteSpace(oracle.Name))
                {
                    oracleSkills.Add(oracle);
                    skillSummaries.Add(new CharacterSkillSummary(
                        Guid: string.Empty,
                        Suid: oracle.Name,
                        Category: oracle.Category,
                        IsKnowledge: oracle.IsKnowledge,
                        BaseValue: oracle.BaseValue,
                        KarmaValue: Math.Max(0, oracle.TotalValue - oracle.BaseValue),
                        Specializations: Array.Empty<string>()));
                }

                break;
            }

            case "quality":
            {
                HeroLabQualityOracle oracle = new(
                    Name: name,
                    Category: compset,
                    Rank: Math.Max(1, ReadJsonInt(item, "rank", "rating", "value")));
                if (!string.IsNullOrWhiteSpace(oracle.Name))
                {
                    oracleQualities.Add(oracle);
                    qualitySummaries.Add(new CharacterQualitySummary(oracle.Name, oracle.Category, oracle.Rank));
                }

                break;
            }

            case "contact":
            {
                HeroLabContactOracle oracle = new(
                    Name: name,
                    Role: ReadJsonString(item, "role", "type"),
                    Connection: ReadJsonInt(item, "connection"),
                    Loyalty: ReadJsonInt(item, "loyalty"));
                if (!string.IsNullOrWhiteSpace(oracle.Name))
                {
                    oracleContacts.Add(oracle);
                    contactSummaries.Add(new CharacterContactSummary(oracle.Name, oracle.Role, string.Empty, oracle.Connection, oracle.Loyalty));
                }

                break;
            }

            case "spell":
                if (!string.IsNullOrWhiteSpace(name))
                {
                    spells.Add(new CharacterSpellSummary(name, compset, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty));
                }

                break;

            case "power":
                if (!string.IsNullOrWhiteSpace(name))
                {
                    powers.Add(new CharacterPowerSummary(name, ReadJsonInt(item, "rating"), string.Empty, 0m));
                }

                break;

            case "complexform":
                if (!string.IsNullOrWhiteSpace(name))
                {
                    complexForms.Add(new CharacterComplexFormSummary(name, string.Empty, string.Empty, string.Empty, string.Empty));
                }

                break;
        }

        if (bucket is "gear" or "weapon" or "armor" or "cyberware" or "vehicle")
        {
            HeroLabItemOracle oracle = new(
                Bucket: bucket,
                Name: name,
                Quantity: Math.Max(1, ReadJsonInt(item, ["quantity", "stackQty", "qty", "count"], 1)),
                Rating: ReadJsonInt(item, "rating"),
                Category: compset,
                Natural: ReadJsonBool(item, "natural"),
                UserAdded: !ReadJsonBool(item, "useradded") || ReadJsonString(item, "useradded") is not "no",
                WirelessPresent: ReadJsonBool(item, "wireless"));
            if (!string.IsNullOrWhiteSpace(oracle.Name))
            {
                oracleItems.Add(oracle);
            }

            switch (bucket)
            {
                case "gear":
                    gear.Add(new CharacterGearSummary(string.Empty, name, compset, oracle.Rating > 0 ? oracle.Rating.ToString() : string.Empty, oracle.Quantity.ToString(), string.Empty, false, string.Empty));
                    break;
                case "weapon":
                    weapons.Add(new CharacterWeaponSummary(string.Empty, name, compset, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, false));
                    break;
                case "armor":
                    armors.Add(new CharacterArmorSummary(string.Empty, name, compset, string.Empty, oracle.Rating > 0 ? oracle.Rating.ToString() : string.Empty, string.Empty, false));
                    break;
                case "cyberware":
                    cyberwares.Add(new CharacterCyberwareSummary(string.Empty, name, compset, string.Empty, string.Empty, oracle.Rating > 0 ? oracle.Rating.ToString() : string.Empty, string.Empty, string.Empty, string.Empty));
                    break;
                case "vehicle":
                    vehicles.Add(new CharacterVehicleSummary(string.Empty, name, compset, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, 0));
                    break;
            }
        }

        if (TryReadJsonProperty(item, "items", out JsonElement nestedItems)
            && nestedItems.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
        {
            foreach ((string nestedId, JsonElement nestedItem) in EnumerateOnlineItems(nestedItems))
            {
                ParseOnlineItem(
                    nestedId,
                    nestedItem,
                    oracleAttributes,
                    attributeSummaries,
                    attributeDetails,
                    oracleSkills,
                    skillSummaries,
                    oracleQualities,
                    qualitySummaries,
                    oracleContacts,
                    contactSummaries,
                    oracleItems,
                    gear,
                    weapons,
                    armors,
                    cyberwares,
                    vehicles,
                    spells,
                    powers,
                    complexForms);
            }
        }
    }

    private static JsonElement ResolveOnlineLeadActor(JsonElement root, string sourceLabel)
    {
        if (!TryReadJsonProperty(root, "actors", out JsonElement actors))
        {
            throw new InvalidOperationException($"{sourceLabel} must keep a Hero Lab Online actors collection.");
        }

        if (actors.ValueKind == JsonValueKind.Object)
        {
            if (TryReadJsonProperty(actors, "actor.1", out JsonElement lead) && lead.ValueKind == JsonValueKind.Object)
            {
                return lead;
            }

            JsonProperty first = actors.EnumerateObject().FirstOrDefault();
            if (first.Value.ValueKind == JsonValueKind.Object)
            {
                return first.Value;
            }

            throw new InvalidOperationException($"{sourceLabel} does not contain a usable Hero Lab Online lead actor.");
        }

        if (actors.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement actor in actors.EnumerateArray())
            {
                if (actor.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (ReadJsonBool(actor, "isLead", "lead", "isActive", "active"))
                {
                    return actor;
                }
            }

            JsonElement firstActor = actors.EnumerateArray().FirstOrDefault(static candidate => candidate.ValueKind == JsonValueKind.Object);
            if (firstActor.ValueKind == JsonValueKind.Object)
            {
                return firstActor;
            }
        }

        throw new InvalidOperationException($"{sourceLabel} does not contain a usable Hero Lab Online lead actor.");
    }

    private static IEnumerable<(string ItemId, JsonElement Item)> EnumerateOnlineItems(JsonElement items)
    {
        if (items.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in items.EnumerateObject())
            {
                yield return (property.Name, property.Value);
            }

            yield break;
        }

        if (items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        int index = 0;
        foreach (JsonElement item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            string itemId = ReadJsonString(item, "id");
            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = ReadJsonString(item, "itemId");
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = $"item_{index + 1}";
            }

            yield return (itemId, item);
            index++;
        }
    }

    private static string DetermineOnlineBucket(string itemId, string compset)
    {
        if (itemId.StartsWith("as", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("abil", StringComparison.OrdinalIgnoreCase))
        {
            return "attribute";
        }

        if (itemId.StartsWith("sk", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("skill", StringComparison.OrdinalIgnoreCase))
        {
            return "skill";
        }

        if (itemId.StartsWith("qu", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("qual", StringComparison.OrdinalIgnoreCase))
        {
            return "quality";
        }

        if (itemId.StartsWith("con", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("contact", StringComparison.OrdinalIgnoreCase))
        {
            return "contact";
        }

        if (itemId.StartsWith("wp", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("weapon", StringComparison.OrdinalIgnoreCase))
        {
            return "weapon";
        }

        if (itemId.StartsWith("ar", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("armor", StringComparison.OrdinalIgnoreCase))
        {
            return "armor";
        }

        if (itemId.StartsWith("cw", StringComparison.OrdinalIgnoreCase)
            || itemId.StartsWith("bw", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("augment", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("cyber", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("bio", StringComparison.OrdinalIgnoreCase))
        {
            return "cyberware";
        }

        if (itemId.StartsWith("ve", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("vehicle", StringComparison.OrdinalIgnoreCase))
        {
            return "vehicle";
        }

        if (itemId.StartsWith("sp", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("spell", StringComparison.OrdinalIgnoreCase))
        {
            return "spell";
        }

        if (itemId.StartsWith("pw", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("power", StringComparison.OrdinalIgnoreCase))
        {
            return "power";
        }

        if (itemId.StartsWith("cf", StringComparison.OrdinalIgnoreCase)
            || compset.Contains("complex", StringComparison.OrdinalIgnoreCase))
        {
            return "complexform";
        }

        return "gear";
    }

    private static string ReadClassicMovementValue(XElement statblockCharacter, string name)
    {
        return statblockCharacter
            .Descendants("movement")
            .Elements()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value
            ?.Trim()
            ?? string.Empty;
    }

    private static int ParseLeadUsagePool(XElement? leadHero, string id, string pickIndex)
    {
        if (leadHero is null)
        {
            return 0;
        }

        return leadHero.Elements("usagepool")
            .Where(pool => string.Equals(pool.Attribute("id")?.Value?.Trim(), id, StringComparison.Ordinal)
                && string.Equals(pool.Attribute("pickindex")?.Value?.Trim(), pickIndex, StringComparison.Ordinal))
            .Select(pool => ParseInt(pool.Attribute("quantity")?.Value))
            .FirstOrDefault();
    }

    private static void SplitClassicName(string value, out string name, out string alias)
    {
        int asIndex = value.IndexOf(" as ", StringComparison.OrdinalIgnoreCase);
        if (asIndex > 0)
        {
            name = value[..asIndex].Trim();
            alias = value[(asIndex + 4)..].Trim().Trim('\'', '"');
            return;
        }

        name = value.Trim();
        alias = string.Empty;
    }

    private static bool ContainsAnyNamed(IEnumerable<string> values, params string[] needles)
    {
        return values.Any(value => needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase)));
    }

    private static string NormalizeAttributeName(string value)
    {
        string trimmed = value?.Trim() ?? string.Empty;
        if (AttributeNameMap.TryGetValue(trimmed, out string? mapped))
        {
            return mapped;
        }

        return trimmed.ToUpperInvariant().Replace(" ", string.Empty, StringComparison.Ordinal);
    }

    private static string JoinNonEmpty(string separator, params string?[] parts)
    {
        return string.Join(separator, parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ReadJsonString(JsonElement element, params string[] path)
    {
        JsonElement current = element;
        foreach (string segment in path)
        {
            if (!TryReadJsonProperty(current, segment, out current))
            {
                return string.Empty;
            }
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString()?.Trim() ?? string.Empty
            : current.ToString().Trim();
    }

    private static bool ReadJsonBool(JsonElement element, params string[] candidateNames)
    {
        foreach (string candidateName in candidateNames)
        {
            if (!TryReadJsonProperty(element, candidateName, out JsonElement value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String => ParseBool(value.GetString()),
                JsonValueKind.Number => value.TryGetInt32(out int numeric) && numeric != 0,
                _ => false
            };
        }

        return false;
    }

    private static int ReadJsonInt(JsonElement element, string candidateName, int fallback = 0)
        => ReadJsonInt(element, [candidateName], fallback);

    private static int ReadJsonInt(JsonElement element, params string[] candidateNames)
        => ReadJsonInt(element, candidateNames, 0);

    private static int ReadJsonInt(JsonElement element, string[] candidateNames, int fallback)
    {
        foreach (string candidateName in candidateNames)
        {
            if (!TryReadJsonProperty(element, candidateName, out JsonElement value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out int intValue) => intValue,
                JsonValueKind.Number when value.TryGetDecimal(out decimal decimalValue) => (int)decimalValue,
                JsonValueKind.String => ParseInt(value.GetString(), fallback),
                _ => fallback
            };
        }

        return fallback;
    }

    private static decimal ReadJsonDecimal(JsonElement element, params string[] candidateNames)
    {
        foreach (string candidateName in candidateNames)
        {
            if (!TryReadJsonProperty(element, candidateName, out JsonElement value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetDecimal(out decimal decimalValue) => decimalValue,
                JsonValueKind.String => ParseDecimal(value.GetString()),
                _ => 0m
            };
        }

        return 0m;
    }

    private static bool TryReadJsonProperty(JsonElement element, string candidateName, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(candidateName, out value))
        {
            return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, candidateName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        string normalizedCandidateName = NormalizeJsonPropertyName(candidateName);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (string.Equals(NormalizeJsonPropertyName(property.Name), normalizedCandidateName, StringComparison.Ordinal))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeJsonPropertyName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[value.Length];
        int count = 0;
        foreach (char character in value)
        {
            if (character is '_' or '-' or ' ' or '.')
            {
                continue;
            }

            buffer[count++] = char.ToLowerInvariant(character);
        }

        return new string(buffer[..count]);
    }

    private static int ParseInt(string? value, int fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        string trimmed = value.Trim();
        if (string.Equals(trimmed, "N", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        return int.TryParse(trimmed, out int parsed)
            ? parsed
            : decimal.TryParse(trimmed, out decimal decimalParsed)
                ? (int)decimalParsed
                : fallback;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        return decimal.TryParse(value.Trim(), out decimal parsed)
            ? parsed
            : 0m;
    }

    private static bool ParseBool(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string trimmed = value.Trim();
        return string.Equals(trimmed, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "1", StringComparison.Ordinal);
    }
}
