using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

public sealed class CharacterSectionService : ICharacterSectionService
{
    private readonly ICharacterSourceDataResolver? _sourceDataResolver;

    private readonly record struct CharacterMatrixImprovementBasis(
        bool OverclockerEnabled,
        bool LivingPersonaDeviceRatingExact,
        string LivingPersonaDeviceRatingSuffix,
        bool LivingPersonaConditionMonitorExact,
        string LivingPersonaConditionMonitorExpression,
        IReadOnlyDictionary<string, int> SavedAttributeTotals);

    public CharacterSectionService(ICharacterSourceDataResolver? sourceDataResolver = null)
    {
        _sourceDataResolver = sourceDataResolver;
    }

    public CharacterAttributesSection ParseAttributes(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        bool created = ParseBool(ReadValue(character, "created"));
        int availableKarma = Decimal.ToInt32(decimal.Truncate(ParseDecimal(ReadValue(character, "karma"))));
        IReadOnlyList<CharacterAttributeSummary> attributes = character
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => BuildAttributeSummary(attribute, created, availableKarma))
            .ToArray()
            ?? Array.Empty<CharacterAttributeSummary>();

        return new CharacterAttributesSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    public CharacterAttributeDetailsSection ParseAttributeDetails(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        bool created = ParseBool(ReadValue(character, "created"));
        int availableKarma = Decimal.ToInt32(decimal.Truncate(ParseDecimal(ReadValue(character, "karma"))));
        IReadOnlyList<CharacterAttributeDetailSummary> attributes = character
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => BuildAttributeDetailSummary(attribute, created, availableKarma))
            .ToArray()
            ?? Array.Empty<CharacterAttributeDetailSummary>();

        return new CharacterAttributeDetailsSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    private static CharacterAttributeSummary BuildAttributeSummary(XElement attribute, bool created, int availableKarma)
    {
        string name = ReadValue(attribute, "name");
        int baseValue = ParseInt(ReadValue(attribute, "base"));
        int karmaValue = ParseInt(ReadValue(attribute, "karma"));
        int totalValue = ParseInt(FirstNonBlank(ReadValue(attribute, "totalvalue"), ReadValue(attribute, "value")));
        if (karmaValue == 0 && totalValue >= baseValue)
        {
            karmaValue = totalValue - baseValue;
        }

        int metatypeMin = ParseInt(ReadValue(attribute, "metatypemin"));
        int metatypeMax = Math.Max(metatypeMin, ParseInt(ReadValue(attribute, "metatypemax")));
        int metatypeAugMax = Math.Max(metatypeMax, ParseInt(ReadValue(attribute, "metatypeaugmax")));
        int priorityMaximum = Math.Max(baseValue, metatypeMax);
        int karmaMaximum = Math.Max(0, metatypeAugMax - baseValue);
        int upgradeKarmaCost = ComputeCareerAttributeUpgradeCost(totalValue, metatypeAugMax);

        return new CharacterAttributeSummary(
            Name: name,
            BaseValue: baseValue,
            TotalValue: totalValue)
        {
            KarmaValue = karmaValue,
            MetatypeMin = metatypeMin,
            MetatypeMax = metatypeMax,
            MetatypeAugMax = metatypeAugMax,
            PriorityMaximum = priorityMaximum,
            KarmaMaximum = karmaMaximum,
            BaseUnlocked = !created,
            Created = created,
            AvailableKarma = availableKarma,
            UpgradeKarmaCost = upgradeKarmaCost,
            CanCareerUpgrade = created && upgradeKarmaCost > 0 && availableKarma >= upgradeKarmaCost
        };
    }

    private static CharacterAttributeDetailSummary BuildAttributeDetailSummary(XElement attribute, bool created, int availableKarma)
    {
        string name = ReadValue(attribute, "name");
        int metatypeMin = ParseInt(ReadValue(attribute, "metatypemin"));
        int metatypeMax = Math.Max(metatypeMin, ParseInt(ReadValue(attribute, "metatypemax")));
        int metatypeAugMax = Math.Max(metatypeMax, ParseInt(ReadValue(attribute, "metatypeaugmax")));
        int baseValue = ParseInt(ReadValue(attribute, "base"));
        int karmaValue = ParseInt(ReadValue(attribute, "karma"));
        int totalValue = ParseInt(ReadValue(attribute, "totalvalue"));
        if (karmaValue == 0 && totalValue >= baseValue)
        {
            karmaValue = totalValue - baseValue;
        }

        int priorityMaximum = Math.Max(baseValue, metatypeMax);
        int karmaMaximum = Math.Max(0, metatypeAugMax - baseValue);
        int upgradeKarmaCost = ComputeCareerAttributeUpgradeCost(totalValue, metatypeAugMax);

        return new CharacterAttributeDetailSummary(
            Name: name,
            MetatypeMin: metatypeMin,
            MetatypeMax: metatypeMax,
            MetatypeAugMax: metatypeAugMax,
            BaseValue: baseValue,
            KarmaValue: karmaValue,
            TotalValue: totalValue,
            MetatypeCategory: ReadValue(attribute, "metatypecategory"))
        {
            PriorityMaximum = priorityMaximum,
            KarmaMaximum = karmaMaximum,
            BaseUnlocked = !created,
            Created = created,
            AvailableKarma = availableKarma,
            UpgradeKarmaCost = upgradeKarmaCost,
            CanCareerUpgrade = created && upgradeKarmaCost > 0 && availableKarma >= upgradeKarmaCost
        };
    }

    private static int ComputeCareerAttributeUpgradeCost(int currentValue, int totalMaximum)
    {
        if (currentValue >= totalMaximum)
        {
            return -1;
        }

        int nextRank = Math.Max(1, currentValue + 1);
        return nextRank * 5;
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
        string alias = ReadValue(character, "alias");
        string name = ReadValue(character, "name");

        bool ambidextrous = character
            .Element("improvements")?
            .Elements("improvement")
            .Any(improvement =>
                string.Equals(
                    ReadValue(improvement, "improvementttype"),
                    "Ambidextrous",
                    StringComparison.OrdinalIgnoreCase)
                && ReadLegacyImprovementIntegerFlag(improvement, "enabled", defaultValue: 1) > 0)
            ?? false;
        return new CharacterProfileSection(
            Name: string.IsNullOrWhiteSpace(name) ? alias : name,
            Alias: alias,
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
            MugshotCount: mugshotCount)
        {
            CharacterNotes = ReadValue(character, "notes"),
            GameNotes = ReadValue(character, "gamenotes"),
            GroupNotes = ReadValue(character, "groupnotes"),
            PrimaryArm = FirstNonBlank(ReadValue(character, "primaryarm"), "Right"),
            Ambidextrous = ambidextrous
        };
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
            DepEnabled: ParseBool(ReadValue(character, "depenabled")))
        {
            AstralReputation = ParseInt(ReadValue(character, "baseastralreputation")),
            WildReputation = ParseInt(ReadValue(character, "basewildreputation")),
            CurrentLiftCarryHits = ParseInt(ReadValue(character, "currentliftcarryhits"))
        };
    }

    public CharacterProgressSection ParseKarmaSummary(string xml) => ParseProgress(xml);

    public CharacterConditionMonitorSection ParseConditionMonitor(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        return new CharacterConditionMonitorSection(
            PhysicalTrack: ParseInt(ReadValue(character, "physicalcm")),
            PhysicalFilled: ParseInt(ReadValue(character, "physicalcmfilled")),
            PhysicalOverflow: ParseInt(ReadValue(character, "physicalcmoverflow")),
            PhysicalThresholdOffset: ParseInt(ReadValue(character, "physicalcmthresholdoffset")),
            PhysicalNaturalRecovery: ReadValue(character, "physicalcmnaturalrecovery"),
            StunTrack: ParseInt(ReadValue(character, "stuncm")),
            StunFilled: ParseInt(ReadValue(character, "stuncmfilled")),
            StunThresholdOffset: ParseInt(ReadValue(character, "stuncmthresholdoffset")),
            StunNaturalRecovery: ReadValue(character, "stuncmnaturalrecovery"),
            PhysicalActsAsCore: ParseBool(ReadValue(character, "physicalcmiscorecm")),
            StunActsAsMatrix: ParseBool(ReadValue(character, "stuncmismatrixcm")),
            Created: ParseBool(ReadValue(character, "created")));
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

    public CharacterSpellDefenseSection ParseSpellDefense(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        int counterspellingDice = ParseInt(ReadValue(character, "currentcounterspellingdice"));
        int bod = ReadAttributeTotalValue(character, "BOD");
        int agi = ReadAttributeTotalValue(character, "AGI");
        int rea = ReadAttributeTotalValue(character, "REA");
        int str = ReadAttributeTotalValue(character, "STR");
        int cha = ReadAttributeTotalValue(character, "CHA");
        int inti = ReadAttributeTotalValue(character, "INT");
        int log = ReadAttributeTotalValue(character, "LOG");
        int wil = ReadAttributeTotalValue(character, "WIL");
        int armor = EstimateArmorRating(character);
        CharacterSpellDefenseMetricSummary[] metrics =
        [
            CreateSpellDefenseMetric(character, "indirect-dodge", "Indirect Dodge", "indirectdefenseresist", counterspellingDice, "Dodge", rea + inti),
            CreateSpellDefenseMetric(character, "indirect-soak", "Indirect Soak", "indirectsoakresist", counterspellingDice, "Body + armor + damage resistance", bod + armor),
            CreateSpellDefenseMetric(character, "direct-soak-mana", "Direct Soak (Mana)", "directmanaresist", counterspellingDice, "Willpower", wil),
            CreateSpellDefenseMetric(character, "direct-soak-physical", "Direct Soak (Physical)", "directphysicalresist", counterspellingDice, "Body", bod),
            CreateSpellDefenseMetric(character, "detection", "Detection", "detectionspellresist", counterspellingDice, "Logic + Willpower", log + wil),
            CreateSpellDefenseMetric(character, "decrease-bod", "Decrease BOD", "decreasebodresist", counterspellingDice, "Body + Willpower", bod + wil),
            CreateSpellDefenseMetric(character, "decrease-agi", "Decrease AGI", "decreaseagiresist", counterspellingDice, "Agility + Willpower", agi + wil),
            CreateSpellDefenseMetric(character, "decrease-rea", "Decrease REA", "decreaserearesist", counterspellingDice, "Reaction + Willpower", rea + wil),
            CreateSpellDefenseMetric(character, "decrease-str", "Decrease STR", "decreasestrresist", counterspellingDice, "Strength + Willpower", str + wil),
            CreateSpellDefenseMetric(character, "decrease-cha", "Decrease CHA", "decreasecharesist", counterspellingDice, "Charisma + Willpower", cha + wil),
            CreateSpellDefenseMetric(character, "decrease-int", "Decrease INT", "decreaseintresist", counterspellingDice, "Intuition + Willpower", inti + wil),
            CreateSpellDefenseMetric(character, "decrease-log", "Decrease LOG", "decreaselogresist", counterspellingDice, "Logic + Willpower", log + wil),
            CreateSpellDefenseMetric(character, "decrease-wil", "Decrease WIL", "decreasewilresist", counterspellingDice, "Willpower + Willpower", wil + wil),
            CreateSpellDefenseMetric(character, "illusion-mana", "Illusion (Mana)", "illusionmanaresist", counterspellingDice, "Logic + Willpower", log + wil),
            CreateSpellDefenseMetric(character, "illusion-physical", "Illusion (Physical)", "illusionphysicalresist", counterspellingDice, "Logic + Intuition", log + inti),
            CreateSpellDefenseMetric(character, "manipulation-mental", "Manipulation (Mental)", "manipulationmentalresist", counterspellingDice, "Logic + Willpower", log + wil),
            CreateSpellDefenseMetric(character, "manipulation-physical", "Manipulation (Physical)", "manipulationphysicalresist", counterspellingDice, "Body + Strength", bod + str)
        ];

        return new CharacterSpellDefenseSection(
            Count: metrics.Length,
            CurrentCounterspellingDice: counterspellingDice,
            Metrics: metrics);
    }

    public CharacterGearSection ParseGear(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        bool careerEditable = ParseBool(ReadValue(character, "created"));
        CharacterMatrixImprovementBasis improvementBasis = BuildCharacterMatrixImprovementBasis(
            character,
            careerEditable);
        XElement[] topLevelGear = character.Element("gears")?.Elements("gear").ToArray() ?? [];
        IReadOnlyDictionary<XElement, CharacterGearQuantitySemantics> quantitySemantics
            = BuildGearQuantitySemantics(xml, topLevelGear, careerEditable);
        List<CharacterGearSummary> gear = [];
        foreach (XElement item in topLevelGear)
        {
            FlattenGearSummary(
                item,
                gear,
                careerEditable,
                parentGuid: string.Empty,
                parentName: string.Empty,
                hierarchyPath: string.Empty,
                depth: 0,
                improvementBasis,
                quantitySemantics);
        }

        return new CharacterGearSection(
            Count: gear.Count,
            Gear: gear);
    }

    private static void FlattenGearSummary(
        XElement item,
        List<CharacterGearSummary> gear,
        bool careerEditable,
        string parentGuid,
        string parentName,
        string hierarchyPath,
        int depth,
        CharacterMatrixImprovementBasis improvementBasis,
        IReadOnlyDictionary<XElement, CharacterGearQuantitySemantics> quantitySemantics)
    {
        string guid = ReadValue(item, "guid");
        string name = ReadValue(item, "name");
        string path = string.IsNullOrWhiteSpace(hierarchyPath)
            ? name
            : string.IsNullOrWhiteSpace(name) ? hierarchyPath : $"{hierarchyPath} / {name}";
        XElement[] children = item.Element("children")?.Elements("gear").ToArray() ?? [];
        bool maximumExact = TryCalculateGearMatrixMaximum(item, improvementBasis, out int maximum);
        if (!string.IsNullOrWhiteSpace(name))
        {
            gear.Add(new CharacterGearSummary(
                Guid: guid,
                Name: name,
                Category: ReadValue(item, "category"),
                Rating: ReadValue(item, "rating"),
                Quantity: ReadValue(item, "qty"),
                Cost: ReadValue(item, "cost"),
                Equipped: ParseBool(ReadValue(item, "equipped")),
                Location: ReadValue(item, "location"),
                Source: ReadValue(item, "source"),
                Notes: ReadValue(item, "notes"),
                CustomName: ReadValue(item, "extra"),
                WirelessEnabled: ParseBool(ReadValue(item, "wirelesson")),
                HomeNode: ParseBool(ReadValue(item, "homenode")),
                ParentGuid: parentGuid,
                ParentName: parentName,
                HierarchyPath: path,
                Depth: depth,
                ChildCount: children.Length,
                MatrixDamage: ParseInt(ReadValue(item, "matrixcmfilled")),
                MatrixConditionMaximum: maximumExact ? maximum : 0,
                MatrixConditionMaximumExact: maximumExact,
                CareerEditable: careerEditable)
            {
                QuantitySemantics = quantitySemantics.GetValueOrDefault(item)
            });
        }

        foreach (XElement child in children)
        {
            FlattenGearSummary(
                child,
                gear,
                careerEditable,
                guid,
                name,
                path,
                depth + 1,
                improvementBasis,
                quantitySemantics);
        }
    }

    private IReadOnlyDictionary<XElement, CharacterGearQuantitySemantics> BuildGearQuantitySemantics(
        string xml,
        IReadOnlyList<XElement> topLevelGear,
        bool careerEditable)
    {
        if (!careerEditable || topLevelGear.Count == 0)
        {
            return new Dictionary<XElement, CharacterGearQuantitySemantics>();
        }

        int? maximumNuyenDecimals = _sourceDataResolver?.TryCreateContext(xml)
            is { } sourceData
            && sourceData.TryResolveMaxNuyenDecimals(out int decimals)
                ? decimals
                : null;
        Dictionary<string, int> identityCounts = topLevelGear
            .Select(item => ReadValue(item, "guid"))
            .Where(static value => Guid.TryParseExact(value, "D", out Guid parsed) && parsed != Guid.Empty)
            .GroupBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);
        Dictionary<XElement, CharacterGearMergeIdentity> mergeIdentities = [];
        foreach (XElement item in topLevelGear)
        {
            if (TryBuildGearMergeIdentity(item, out CharacterGearMergeIdentity? identity))
            {
                mergeIdentities[item] = identity;
            }
        }

        Dictionary<XElement, CharacterGearQuantitySemantics> result = [];
        foreach (XElement item in topLevelGear)
        {
            string guid = ReadValue(item, "guid");
            string name = ReadValue(item, "name");
            string category = ReadValue(item, "category");
            if (!identityCounts.TryGetValue(guid, out int identityCount)
                || identityCount != 1
                || !decimal.TryParse(
                    ReadValue(item, "qty"),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal quantity)
                || !CharacterGearQuantityRules.TryResolvePrecision(
                    name,
                    category,
                    maximumNuyenDecimals,
                    out int decimalPlaces,
                    out decimal minimumIncrement)
                || !CharacterGearQuantityRules.IsValidAmount(quantity, minimumIncrement)
                || !mergeIdentities.TryGetValue(item, out CharacterGearMergeIdentity? identity))
            {
                continue;
            }

            bool purchaseCostExact = TryCalculateGearPurchaseUnitCost(item, out decimal purchaseUnitCost);
            string[] mergeCandidates = topLevelGear
                .Where(candidate => !ReferenceEquals(candidate, item)
                    && mergeIdentities.TryGetValue(candidate, out CharacterGearMergeIdentity? candidateIdentity)
                    && CharacterGearQuantityRules.AreIdenticalForMerge(identity, candidateIdentity))
                .Select(candidate => ReadValue(candidate, "guid"))
                .Where(candidateGuid => identityCounts.TryGetValue(candidateGuid, out int count) && count == 1)
                .OrderBy(static candidateGuid => candidateGuid, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            result[item] = new CharacterGearQuantitySemantics(
                Quantity: quantity,
                DecimalPlaces: decimalPlaces,
                MinimumIncrement: minimumIncrement,
                PurchaseUnitCost: purchaseCostExact ? purchaseUnitCost : 0m,
                PurchaseUnitCostExact: purchaseCostExact,
                MergeCandidateGuids: mergeCandidates);
        }

        return result;
    }

    private static bool TryBuildGearMergeIdentity(
        XElement gear,
        out CharacterGearMergeIdentity? identity)
    {
        identity = null;
        if (!int.TryParse(
                ReadValue(gear, "rating"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int rating))
        {
            return false;
        }

        List<CharacterGearMergeChildIdentity> children = [];
        foreach (XElement child in gear.Element("children")?.Elements("gear") ?? [])
        {
            if (!decimal.TryParse(
                    ReadValue(child, "qty"),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out decimal quantity)
                || quantity <= 0m
                || !TryBuildGearMergeIdentity(child, out CharacterGearMergeIdentity? childIdentity))
            {
                return false;
            }

            children.Add(new CharacterGearMergeChildIdentity(quantity, childIdentity!));
        }

        identity = new CharacterGearMergeIdentity(
            Name: ReadValue(gear, "name"),
            Category: ReadValue(gear, "category"),
            Rating: rating,
            Extra: ReadValue(gear, "extra"),
            GearName: ReadValue(gear, "gearname"),
            Notes: ReadValue(gear, "notes"),
            Children: children);
        return true;
    }

    private static bool TryCalculateGearPurchaseUnitCost(XElement gear, out decimal cost)
    {
        cost = 0m;
        return TryBuildGearCostSnapshot(gear, out CharacterGearCostSnapshot? snapshot)
            && CharacterGearQuantityRules.TryCalculatePurchaseUnitCost(snapshot!, out cost);
    }

    private static bool TryBuildGearCostSnapshot(
        XElement gear,
        out CharacterGearCostSnapshot? snapshot)
    {
        snapshot = null;
        if (!int.TryParse(
                ReadValue(gear, "rating"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int rating)
            || !TryReadPositiveDecimal(gear, "costfor", 1m, out decimal costFor)
            || !TryReadPositiveDecimal(gear, "qty", 1m, out decimal savedQuantity)
            || !TryParseOptionalBool(ReadValue(gear, "discountedcost"), out bool discounted))
        {
            return false;
        }

        int childMultiplier = 1;
        string childMultiplierValue = ReadValue(gear, "childcostmultiplier");
        if (!string.IsNullOrWhiteSpace(childMultiplierValue)
            && (!int.TryParse(
                    childMultiplierValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out childMultiplier)
                || childMultiplier <= 0))
        {
            return false;
        }

        List<CharacterGearCostSnapshot> children = [];
        foreach (XElement child in gear.Element("children")?.Elements("gear") ?? [])
        {
            if (!TryBuildGearCostSnapshot(child, out CharacterGearCostSnapshot? childSnapshot))
            {
                return false;
            }
            children.Add(childSnapshot!);
        }

        snapshot = new CharacterGearCostSnapshot(
            Rating: rating,
            Quantity: savedQuantity,
            CostExpression: ReadValue(gear, "cost"),
            CostFor: costFor,
            DiscountedCost: discounted,
            ChildCostMultiplier: childMultiplier,
            Children: children);
        return true;
    }

    private static bool TryReadPositiveDecimal(
        XElement item,
        string elementName,
        decimal fallback,
        out decimal value)
    {
        string raw = ReadValue(item, elementName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = fallback;
            return true;
        }

        return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value)
            && value > 0m;
    }

    private static bool TryCalculateGearMatrixMaximum(
        XElement gear,
        CharacterMatrixImprovementBasis improvementBasis,
        out int maximum)
    {
        maximum = 0;
        if (!TryParseOptionalInt(ReadValue(gear, "rating"), out int rating))
        {
            return false;
        }

        string deviceRatingExpression = ReadValue(gear, "devicerating");
        if (string.IsNullOrWhiteSpace(deviceRatingExpression))
        {
            bool isCommlink = ReadValue(gear, "canformpersona").Contains("Self", StringComparison.Ordinal)
                || (gear.Element("children")?.Elements("gear") ?? [])
                    .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
            deviceRatingExpression = isCommlink ? "2" : "0";
        }
        if (string.Equals(ReadValue(gear, "name"), "Living Persona", StringComparison.Ordinal))
        {
            if (!improvementBasis.LivingPersonaDeviceRatingExact)
            {
                return false;
            }
            deviceRatingExpression += improvementBasis.LivingPersonaDeviceRatingSuffix;
        }
        if (!CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                deviceRatingExpression,
                rating,
                improvementBasis.SavedAttributeTotals,
                out int deviceRating))
        {
            return false;
        }

        if (string.Equals(ReadValue(gear, "overclocked"), "Device Rating", StringComparison.Ordinal)
            && improvementBasis.OverclockerEnabled)
        {
            if (deviceRating == int.MaxValue)
            {
                return false;
            }
            deviceRating++;
        }

        if (!TryCalculateGearTotalBonusMatrixBoxes(gear, improvementBasis, out int bonusMatrixBoxes))
        {
            return false;
        }
        return CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum(
            deviceRating,
            bonusMatrixBoxes,
            out maximum);
    }

    private static bool TryCalculateGearTotalBonusMatrixBoxes(
        XElement gear,
        CharacterMatrixImprovementBasis improvementBasis,
        out int total)
    {
        total = 0;
        if (!TryParseOptionalInt(ReadValue(gear, "matrixcmbonus"), out int ownBonus))
        {
            return false;
        }

        long calculated = ownBonus;
        if (string.Equals(ReadValue(gear, "name"), "Living Persona", StringComparison.Ordinal))
        {
            if (!improvementBasis.LivingPersonaConditionMonitorExact)
            {
                return false;
            }
            string expression = improvementBasis.LivingPersonaConditionMonitorExpression;
            if (!string.IsNullOrEmpty(expression))
            {
                if (!TryParseOptionalInt(ReadValue(gear, "rating"), out int rating)
                    || !CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                        expression,
                        rating,
                        improvementBasis.SavedAttributeTotals,
                        out int livingPersonaBonus))
                {
                    return false;
                }
                calculated += livingPersonaBonus;
            }
        }
        foreach (XElement child in gear.Element("children")?.Elements("gear") ?? [])
        {
            if (!TryParseOptionalBool(ReadValue(child, "equipped"), out bool equipped))
            {
                return false;
            }
            if (!equipped)
            {
                continue;
            }
            if (!TryCalculateGearTotalBonusMatrixBoxes(child, improvementBasis, out int childBonus))
            {
                return false;
            }
            calculated += childBonus;
        }

        if (calculated is < int.MinValue or > int.MaxValue)
        {
            return false;
        }
        total = (int)calculated;
        return true;
    }

    public CharacterWeaponsSection ParseWeapons(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        ICharacterSourceDataContext? sourceData = _sourceDataResolver?.TryCreateContext(xml);
        bool careerEditable = ParseBool(ReadValue(character, "created"));
        CharacterMatrixImprovementBasis improvementBasis = BuildCharacterMatrixImprovementBasis(
            character,
            careerEditable);
        IReadOnlyList<CharacterWeaponSummary> weapons = character
            .Element("weapons")?
            .Elements("weapon")
            .Select(item => BuildWeaponSummary(
                character,
                item,
                careerEditable,
                improvementBasis,
                sourceData))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterWeaponSummary>();

        return new CharacterWeaponsSection(
            Count: weapons.Count,
            Weapons: weapons);
    }

    private static CharacterWeaponSummary BuildWeaponSummary(
        XElement character,
        XElement item,
        bool careerEditable,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData)
    {
        bool ownerExact = CharacterWeaponMatrixParentResolver.TryResolveOwner(
            character,
            item,
            out CharacterMatrixOwner owner);
        int maximum = 0;
        bool maximumExact = ownerExact
            && TryCalculateMatrixOwnerMaximum(
                owner,
                improvementBasis,
                sourceData,
                out maximum);
        return new CharacterWeaponSummary(
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
            Equipped: ParseBool(ReadValue(item, "equipped")),
            Source: ReadValue(item, "source"),
            Notes: ReadValue(item, "notes"),
            CustomName: ReadValue(item, "extra"),
            WirelessEnabled: ParseBool(ReadValue(item, "wirelesson")),
            MatrixDamage: ParseInt(ReadValue(ownerExact ? owner.Item : item, "matrixcmfilled")),
            MatrixConditionMaximum: maximumExact ? maximum : 0,
            MatrixConditionMaximumExact: maximumExact,
            CareerEditable: careerEditable);
    }

    private static bool TryCalculateMatrixOwnerMaximum(
        CharacterMatrixOwner owner,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData,
        out int maximum)
        => owner.Kind switch
        {
            CharacterMatrixOwnerKind.Gear => TryCalculateGearMatrixMaximum(
                owner.Item,
                improvementBasis,
                out maximum),
            CharacterMatrixOwnerKind.Armor => TryCalculateArmorMatrixMaximum(
                owner.Item,
                improvementBasis,
                out maximum),
            CharacterMatrixOwnerKind.Weapon => TryCalculateWeaponOwnMatrixMaximum(
                owner.Item,
                improvementBasis,
                out maximum),
            CharacterMatrixOwnerKind.Cyberware => TryCalculateCyberwareMatrixMaximum(
                owner.Item,
                improvementBasis,
                sourceData,
                out maximum),
            CharacterMatrixOwnerKind.Vehicle => TryCalculateVehicleMatrixMaximum(
                owner.Item,
                improvementBasis,
                sourceData,
                out maximum),
            _ => AssignUnavailableMaximum(out maximum)
        };

    private static bool AssignUnavailableMaximum(out int maximum)
    {
        maximum = 0;
        return false;
    }

    private static bool TryCalculateWeaponOwnMatrixMaximum(
        XElement weapon,
        CharacterMatrixImprovementBasis improvementBasis,
        out int maximum)
    {
        maximum = 0;
        if (!TryParseOptionalInt(ReadValue(weapon, "rating"), out int rating))
        {
            return false;
        }

        string deviceRatingExpression = ReadValue(weapon, "devicerating");
        int deviceRating;
        if (string.IsNullOrWhiteSpace(deviceRatingExpression))
        {
            deviceRating = 2;
        }
        else if (!CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            deviceRatingExpression,
            rating,
            improvementBasis.SavedAttributeTotals,
            out deviceRating))
        {
            return false;
        }

        if (string.Equals(ReadValue(weapon, "overclocked"), "Device Rating", StringComparison.Ordinal)
            && improvementBasis.OverclockerEnabled)
        {
            if (deviceRating == int.MaxValue)
            {
                return false;
            }
            deviceRating++;
        }

        return CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum(
            deviceRating,
            totalBonusMatrixBoxes: 0,
            out maximum);
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
                        Equipped: ParseBool(ReadValue(accessory, "equipped")),
                        IncludedInWeapon: ParseBool(ReadValue(accessory, "included")),
                        Category: ReadValue(accessory, "category"),
                        Source: ReadValue(accessory, "source"),
                        Notes: ReadValue(accessory, "notes"),
                        CustomName: ReadValue(accessory, "extra"),
                        Location: ReadValue(accessory, "location"),
                        WirelessEnabled: ParseBool(ReadValue(accessory, "wirelesson"))))
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
        bool careerEditable = ParseBool(ReadValue(character, "created"));
        CharacterMatrixImprovementBasis improvementBasis = BuildCharacterMatrixImprovementBasis(
            character,
            careerEditable);
        IReadOnlyList<CharacterArmorSummary> armors = character
            .Element("armors")?
            .Elements("armor")
            .Select(item => BuildArmorSummary(item, careerEditable, improvementBasis))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterArmorSummary>();

        return new CharacterArmorsSection(
            Count: armors.Count,
            Armors: armors);
    }

    private static CharacterArmorSummary BuildArmorSummary(
        XElement item,
        bool careerEditable,
        CharacterMatrixImprovementBasis improvementBasis)
    {
        bool maximumExact = TryCalculateArmorMatrixMaximum(item, improvementBasis, out int maximum);
        bool armorDamageExact = TryParseOptionalInt(ReadValue(item, "damage"), out int armorDamage)
            && TryCalculateArmorDamageMaximum(item, out int armorDamageMaximum);
        bool equippedExact = bool.TryParse(ReadValue(item, "equipped"), out bool equipped);
        return new CharacterArmorSummary(
            Guid: ReadValue(item, "guid"),
            Name: ReadValue(item, "name"),
            Category: ReadValue(item, "category"),
            ArmorValue: ReadValue(item, "armor"),
            Rating: ReadValue(item, "rating"),
            Cost: ReadValue(item, "cost"),
            Equipped: equippedExact && equipped,
            Source: ReadValue(item, "source"),
            Notes: ReadValue(item, "notes"),
            CustomName: ReadValue(item, "extra"),
            WirelessEnabled: ParseBool(ReadValue(item, "wirelesson")),
            MatrixDamage: ParseInt(ReadValue(item, "matrixcmfilled")),
            MatrixConditionMaximum: maximumExact ? maximum : 0,
            MatrixConditionMaximumExact: maximumExact,
            ActiveCommlink: ParseBool(ReadValue(item, "active")),
            IsCommlink: IsArmorCommlink(item),
            HomeNode: ParseBool(ReadValue(item, "homenode")),
            ArmorDamage: armorDamageExact ? armorDamage : 0,
            ArmorDamageMaximum: armorDamageExact ? armorDamageMaximum : 0,
            ArmorDamageMaximumExact: armorDamageExact,
            EquippedExact: equippedExact,
            CareerEditable: careerEditable);
    }

    private static bool TryCalculateArmorDamageMaximum(XElement armor, out int maximum)
    {
        maximum = 0;
        if (!TryParseOptionalInt(ReadValue(armor, "rating"), out int rating))
        {
            return false;
        }

        CharacterArmorDamageModifierBasis[] modifiers = armor
            .Element("armormods")?
            .Elements("armormod")
            .Select(modifier =>
            {
                bool armorExact = TryParseOptionalInt(ReadValue(modifier, "armor"), out int armorValue);
                bool equippedExact = TryParseOptionalBool(ReadValue(modifier, "equipped"), out bool equipped);
                return new CharacterArmorDamageModifierBasis(
                    armorValue,
                    equipped,
                    armorExact && equippedExact);
            })
            .ToArray()
            ?? [];
        return CharacterArmorDamageRules.TryCalculateMaximum(
            ReadValue(armor, "armor"),
            ReadValue(armor, "armoroverride"),
            rating,
            modifiers,
            out maximum);
    }

    private static bool IsArmorCommlink(XElement armor)
        => ReadValue(armor, "canformpersona").Contains("Self", StringComparison.Ordinal)
            || armor.Element("gears")?.Elements("gear").Any(
                gear => ReadValue(gear, "canformpersona").Contains("Parent", StringComparison.Ordinal)) == true;

    private static bool TryCalculateArmorMatrixMaximum(
        XElement armor,
        CharacterMatrixImprovementBasis improvementBasis,
        out int maximum)
    {
        maximum = 0;
        if (!TryParseOptionalInt(ReadValue(armor, "matrixcmbonus"), out int ownBonus))
        {
            return false;
        }

        string deviceRatingText = ReadValue(armor, "devicerating");
        int deviceRating;
        if (string.IsNullOrWhiteSpace(deviceRatingText))
        {
            deviceRating = 2;
        }
        else if (!int.TryParse(deviceRatingText, out deviceRating))
        {
            return false;
        }

        if (string.Equals(ReadValue(armor, "overclocked"), "Device Rating", StringComparison.Ordinal)
            && improvementBasis.OverclockerEnabled)
        {
            if (deviceRating == int.MaxValue)
            {
                return false;
            }
            deviceRating++;
        }

        long conditionBonus = ownBonus;
        foreach (XElement gear in (armor.Element("gears")?.Elements("gear") ?? [])
                     .Concat(armor.Element("children")?.Elements("gear") ?? []))
        {
            if (!TryParseOptionalBool(ReadValue(gear, "equipped"), out bool equipped))
            {
                return false;
            }
            if (!equipped)
            {
                continue;
            }
            if (!TryCalculateGearTotalBonusMatrixBoxes(gear, improvementBasis, out int gearBonus))
            {
                return false;
            }
            conditionBonus += gearBonus;
        }

        if (conditionBonus is < int.MinValue or > int.MaxValue)
        {
            return false;
        }
        return CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum(
            deviceRating,
            (int)conditionBonus,
            out maximum);
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
                        Equipped: ParseBool(ReadValue(mod, "equipped")),
                        Source: ReadValue(mod, "source"),
                        Notes: ReadValue(mod, "notes"),
                        CustomName: ReadValue(mod, "extra"),
                        Location: ReadValue(mod, "location"),
                        WirelessEnabled: ParseBool(ReadValue(mod, "wirelesson"))))
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
        ICharacterSourceDataContext? sourceData = _sourceDataResolver?.TryCreateContext(xml);
        bool careerEditable = ParseBool(ReadValue(character, "created"));
        CharacterMatrixImprovementBasis improvementBasis = BuildCharacterMatrixImprovementBasis(
            character,
            careerEditable);
        List<CharacterCyberwareSummary> cyberwares = [];
        foreach (XElement item in character.Element("cyberwares")?.Elements("cyberware") ?? [])
        {
            FlattenCyberwareSummary(
                item,
                cyberwares,
                parentGuid: string.Empty,
                parentName: string.Empty,
                hierarchyPath: string.Empty,
                depth: 0,
                careerEditable,
                improvementBasis,
                sourceData);
        }

        return new CharacterCyberwaresSection(
            Count: cyberwares.Count,
            Cyberwares: cyberwares);
    }

    public CharacterVehiclesSection ParseVehicles(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        ICharacterSourceDataContext? sourceData = _sourceDataResolver?.TryCreateContext(xml);
        bool careerEditable = ParseBool(ReadValue(character, "created"));
        CharacterMatrixImprovementBasis improvementBasis = BuildCharacterMatrixImprovementBasis(
            character,
            careerEditable);
        IReadOnlyList<CharacterVehicleSummary> vehicles = character
            .Element("vehicles")?
            .Elements("vehicle")
            .Select(item => BuildVehicleSummary(item, careerEditable, improvementBasis, sourceData))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .ToArray()
            ?? Array.Empty<CharacterVehicleSummary>();

        return new CharacterVehiclesSection(
            Count: vehicles.Count,
            Vehicles: vehicles);
    }

    private static CharacterVehicleSummary BuildVehicleSummary(
        XElement item,
        bool careerEditable,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData)
    {
        CharacterVehicleConditionModifierBasis[] modifiers = item.Element("mods")?
            .Elements("mod")
            .Select(modifier => BuildVehicleConditionModifierBasis(modifier, sourceData))
            .ToArray()
            ?? [];
        bool bodyExact = int.TryParse(ReadValue(item, "body"), out int baseBody);
        int physicalMaximum = 0;
        bool maximumExact = bodyExact
            && CharacterVehicleConditionMonitorCalculator.TryCalculatePhysicalMaximum(
                ReadValue(item, "category"),
                baseBody,
                modifiers,
                out physicalMaximum);
        bool matrixMaximumExact = TryCalculateVehicleMatrixMaximum(
            item,
            improvementBasis,
            sourceData,
            out int matrixMaximum);
        CharacterLocationSummary[] locations = item.Element("locations")?
            .Elements("location")
            .Select(location => new CharacterLocationSummary(
                Guid: ReadValue(location, "guid"),
                Name: ReadValue(location, "name"),
                Notes: ReadValue(location, "notes")))
            .ToArray()
            ?? [];

        return new CharacterVehicleSummary(
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
            ModCount: modifiers.Length,
            WeaponCount: item.Element("weapons")?.Elements("weapon").Count() ?? 0,
            Source: ReadValue(item, "source"),
            Notes: ReadValue(item, "notes"),
            CustomName: ReadValue(item, "extra"),
            PhysicalDamage: ParseInt(ReadValue(item, "physicalcmfilled")),
            PhysicalConditionMaximum: maximumExact ? physicalMaximum : 0,
            PhysicalConditionMaximumExact: maximumExact,
            CareerEditable: careerEditable,
            MatrixDamage: ParseInt(ReadValue(item, "matrixcmfilled")),
            MatrixConditionMaximum: matrixMaximumExact ? matrixMaximum : 0,
            MatrixConditionMaximumExact: matrixMaximumExact,
            HomeNode: ParseBool(ReadValue(item, "homenode")),
            LocationCount: locations.Length,
            Locations: locations);
    }

    private static bool TryCalculateVehicleMatrixMaximum(
        XElement vehicle,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData,
        out int maximum)
    {
        maximum = 0;
        string deviceRatingText = ReadValue(vehicle, "devicerating");
        if (string.IsNullOrWhiteSpace(deviceRatingText))
        {
            deviceRatingText = ReadValue(vehicle, "pilot");
        }
        if (!int.TryParse(deviceRatingText, out int baseDeviceRating))
        {
            return false;
        }

        long deviceRatingBonus = 0;
        long conditionBonus = 0;
        foreach (XElement modifier in vehicle.Element("mods")?.Elements("mod") ?? [])
        {
            if (!TryParseOptionalBool(ReadValue(modifier, "wirelesson"), out bool wirelessEnabled)
                || !TryReadEffectiveVehicleModBonuses(
                    modifier,
                    sourceData,
                    requireWireless: wirelessEnabled,
                    out CharacterVehicleModSourceBonuses sourceBonuses))
            {
                return false;
            }

            XElement? bonus = modifier.Element("bonus");
            int regularDeviceRating = ParseInt(
                bonus?.Element("devicerating")?.Value ?? sourceBonuses.DeviceRatingExpression);
            int regularConditionBonus = ParseInt(
                bonus?.Element("matrixcmbonus")?.Value ?? sourceBonuses.MatrixConditionExpression);
            deviceRatingBonus += regularDeviceRating;
            conditionBonus += regularConditionBonus;
            if (wirelessEnabled)
            {
                XElement? wirelessBonus = modifier.Element("wirelessbonus");
                int wirelessDeviceRating = ParseInt(
                    wirelessBonus?.Element("devicerating")?.Value
                    ?? sourceBonuses.WirelessDeviceRatingExpression);
                int wirelessConditionBonus = ParseInt(
                    wirelessBonus?.Element("matrixcmbonus")?.Value
                    ?? sourceBonuses.WirelessMatrixConditionExpression);
                deviceRatingBonus += wirelessDeviceRating;
                conditionBonus += wirelessConditionBonus;
            }
        }

        foreach (XElement gear in vehicle.Element("gears")?.Elements("gear") ?? [])
        {
            if (!TryParseOptionalBool(ReadValue(gear, "equipped"), out bool equipped))
            {
                return false;
            }
            if (equipped)
            {
                if (!TryCalculateGearTotalBonusMatrixBoxes(
                    gear,
                    improvementBasis,
                    out int gearConditionBonus))
                {
                    return false;
                }
                conditionBonus += gearConditionBonus;
            }
        }

        long totalDeviceRating = baseDeviceRating + deviceRatingBonus;
        if (string.Equals(
                ReadValue(vehicle, "overclocked"),
                "Device Rating",
                StringComparison.Ordinal)
            && improvementBasis.OverclockerEnabled)
        {
            totalDeviceRating++;
        }
        if (totalDeviceRating is < int.MinValue or > int.MaxValue
            || conditionBonus is < int.MinValue or > int.MaxValue)
        {
            return false;
        }
        return CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum(
            (int)totalDeviceRating,
            (int)conditionBonus,
            out maximum);
    }

    private static CharacterVehicleConditionModifierBasis BuildVehicleConditionModifierBasis(
        XElement modifier,
        ICharacterSourceDataContext? sourceData)
    {
        bool includedExact = TryParseOptionalBool(ReadValue(modifier, "included"), out bool included);
        bool equippedExact = TryParseOptionalBool(ReadValue(modifier, "equipped"), out bool equipped);
        bool conditionExact = TryParseOptionalInt(ReadValue(modifier, "conditionmonitor"), out int conditionBonus);
        bool ratingExact = TryParseOptionalInt(ReadValue(modifier, "rating"), out int rating);
        bool modifierExact = includedExact && equippedExact && conditionExact;
        int? effectiveBodyBonus = included || !equipped
            ? 0
            : ratingExact && TryReadEffectiveVehicleBodyBonus(modifier, rating, sourceData, out int bodyBonus)
                ? bodyBonus
                : null;
        if (!included && equipped && effectiveBodyBonus is null)
        {
            modifierExact = false;
        }
        return new CharacterVehicleConditionModifierBasis(
            IncludedInVehicle: included,
            Equipped: equipped,
            ConditionMonitorBonus: conditionBonus,
            EffectiveBodyBonus: effectiveBodyBonus,
            Exact: modifierExact);
    }

    private static bool TryReadEffectiveVehicleBodyBonus(
        XElement modifier,
        int rating,
        ICharacterSourceDataContext? sourceData,
        out int bodyBonus)
    {
        bodyBonus = 0;
        XElement? bonus = modifier.Element("bonus");
        if (!TryParseOptionalBool(ReadValue(modifier, "wirelesson"), out bool wirelessEnabled)
            || !TryReadEffectiveVehicleModBonuses(
                modifier,
                sourceData,
                requireWireless: wirelessEnabled,
                out CharacterVehicleModSourceBonuses sourceBonuses)
            || !TryResolveOptionalVehicleBodyExpression(
                bonus?.Element("body")?.Value ?? sourceBonuses.BodyExpression,
                rating,
                out int regularBonus))
        {
            return false;
        }
        bodyBonus = regularBonus;

        if (!wirelessEnabled)
        {
            return true;
        }

        XElement? wirelessBonus = modifier.Element("wirelessbonus");
        if (!TryResolveOptionalVehicleBodyExpression(
                wirelessBonus?.Element("body")?.Value ?? sourceBonuses.WirelessBodyExpression,
                rating,
                out int wirelessBodyBonus))
        {
            return false;
        }
        try
        {
            bodyBonus = checked(bodyBonus + wirelessBodyBonus);
            return true;
        }
        catch (OverflowException)
        {
            bodyBonus = 0;
            return false;
        }
    }

    private static bool TryReadEffectiveVehicleModBonuses(
        XElement modifier,
        ICharacterSourceDataContext? sourceData,
        bool requireWireless,
        out CharacterVehicleModSourceBonuses bonuses)
    {
        bonuses = CharacterVehicleModSourceBonuses.Empty;
        bool needsRegularSource = modifier.Element("bonus") is null;
        bool needsWirelessSource = requireWireless && modifier.Element("wirelessbonus") is null;
        if (!needsRegularSource && !needsWirelessSource)
        {
            return true;
        }

        return sourceData?.TryResolveVehicleModBonuses(
            ReadValue(modifier, "sourceid"),
            ReadValue(modifier, "name"),
            out bonuses) == true;
    }

    private static bool TryResolveOptionalVehicleBodyExpression(
        string? expression,
        int rating,
        out int bonus)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            bonus = 0;
            return true;
        }

        return CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            expression,
            rating,
            out bonus);
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
                        Equipped: ParseBool(ReadValue(mod, "equipped")),
                        Source: ReadValue(mod, "source"),
                        Notes: ReadValue(mod, "notes"),
                        CustomName: ReadValue(mod, "extra"),
                        Location: ReadValue(mod, "location"),
                        WirelessEnabled: ParseBool(ReadValue(mod, "wirelesson"))))
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
                    .ToArray() ?? Array.Empty<string>(),
                Name: FirstNonBlank(ReadValue(skill, "name"), ReadValue(skill, "suid")),
                Notes: ReadValue(skill, "notes"),
                CustomName: ReadValue(skill, "extra")))
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
                BP: ParseInt(ReadValue(quality, "bp")),
                Guid: ReadValue(quality, "guid"),
                Notes: ReadValue(quality, "notes"),
                CustomName: ReadValue(quality, "extra")))
            .ToArray()
            ?? Array.Empty<CharacterQualitySummary>();

        return new CharacterQualitiesSection(
            Count: qualities.Count,
            Qualities: qualities);
    }

    public CharacterContactsSection ParseContacts(string xml)
        => ParseContactsByType(xml, static type => type == ContactRecordType.Contact);

    public CharacterContactsSection ParseRelationships(string xml)
        => ParseContactsByType(xml, static _ => true);

    public CharacterContactsSection ParseEnemies(string xml)
        => ParseContactsByType(xml, static type => type == ContactRecordType.Enemy);

    public CharacterContactsSection ParsePets(string xml)
        => ParseContactsByType(xml, static type => type == ContactRecordType.Pet);

    private CharacterContactsSection ParseContactsByType(string xml, Func<ContactRecordType, bool> includeContact)
    {
        XElement character = LoadCharacterRoot(xml);
        IReadOnlyList<CharacterContactSummary> contacts = character
            .Element("contacts")?
            .Elements("contact")
            .Where(contact => includeContact(ParseContactRecordType(ReadValue(contact, "type"))))
            .Select(contact => ParseContactSummary(character, contact))
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
                Source: ReadValue(spell, "source"),
                Guid: ReadValue(spell, "guid"),
                Notes: ReadValue(spell, "notes"),
                CustomName: ReadValue(spell, "extra")))
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
                PointsPerLevel: ParseDecimal(ReadValue(power, "pointsperlevel")),
                Guid: ReadValue(power, "guid"),
                Notes: ReadValue(power, "notes"),
                CustomName: ReadValue(power, "extra")))
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
                Source: ReadValue(form, "source"),
                Guid: ReadValue(form, "guid"),
                Notes: ReadValue(form, "notes"),
                CustomName: ReadValue(form, "extra")))
            .ToArray()
            ?? Array.Empty<CharacterComplexFormSummary>();

        return new CharacterComplexFormsSection(
            Count: complexForms.Count,
            ComplexForms: complexForms);
    }

    public CharacterSpiritsSection ParseSpirits(string xml)
    {
        XElement character = LoadCharacterRoot(xml);
        bool created = ParseBool(ReadValue(character, "created"));
        IReadOnlyList<CharacterSpiritSummary> spirits = character
            .Element("spirits")?
            .Elements("spirit")
            .Select(spirit => BuildSpiritSummary(character, spirit, created))
            .Where(spirit => !string.IsNullOrWhiteSpace(spirit.Name))
            .ToArray()
            ?? Array.Empty<CharacterSpiritSummary>();

        return new CharacterSpiritsSection(
            Count: spirits.Count,
            Spirits: spirits)
        {
            Created = created
        };
    }

    private static CharacterSpiritSummary BuildSpiritSummary(
        XElement character,
        XElement spirit,
        bool created)
    {
        int force = ParseInt(ReadValue(spirit, "force"));
        bool forceMaximumExact = TryCalculateSpiritForceMaximum(
            character,
            spirit,
            created,
            out int forceMaximum);
        string linkedFileName = ReadValue(spirit, "file");
        string linkedRelativeFileName = ReadValue(spirit, "relative");
        bool linked = !string.IsNullOrWhiteSpace(linkedFileName)
            || !string.IsNullOrWhiteSpace(linkedRelativeFileName);
        XElement? linkedIdentity = spirit.Element("chummercomplete")?.Element("linkedcharacter");
        string linkedName = linkedIdentity?.Element("name")?.Value.Trim() ?? string.Empty;
        string linkedDisplayName = linkedIdentity?.Element("displayname")?.Value.Trim() ?? string.Empty;
        string pathDisplayName = Path.GetFileName(FirstNonBlank(linkedFileName, linkedRelativeFileName))
            ?? string.Empty;
        return new CharacterSpiritSummary(
            Name: ReadSpiritName(spirit),
            Force: force,
            Services: ParseInt(ReadValue(spirit, "services")),
            Bound: ParseBool(ReadValue(spirit, "bound")),
            Guid: ReadValue(spirit, "guid"),
            Notes: ReadValue(spirit, "notes"),
            CustomName: ReadValue(spirit, "extra"))
        {
            EntityType = NormalizeSpiritEntityType(ReadValue(spirit, "type")),
            CritterName = ReadValue(spirit, "crittername"),
            CritterNameEditableExact = string.IsNullOrWhiteSpace(ReadValue(spirit, "file"))
                && string.IsNullOrWhiteSpace(ReadValue(spirit, "relative")),
            LinkedCharacter = new CharacterLinkedAssociationSummary(
                IsLinked: linked,
                IdentityResolved: linked && !string.IsNullOrWhiteSpace(linkedName),
                FileName: linkedFileName,
                RelativeFileName: linkedRelativeFileName,
                DisplayName: FirstNonBlank(
                    linkedDisplayName,
                    pathDisplayName,
                    linked ? "Linked runner" : string.Empty)),
            ForceMaximum = forceMaximumExact ? forceMaximum : 0,
            ForceMaximumExact = forceMaximumExact,
            ForceEditable = created && forceMaximumExact && force is >= 0 && force <= forceMaximum
        };
    }

    /// <summary>
    /// Mirrors Character.MaxSpiritForce and Character.MaxSpriteLevel without guessing an
    /// external character-settings profile. Sprite ceilings are self-contained. Spirit
    /// ceilings are exact only when the profile choice is persisted alongside the runner, or
    /// when MAG.Value and MAG.TotalValue are equal so either legacy setting gives the same
    /// result.
    /// </summary>
    private static bool TryCalculateSpiritForceMaximum(
        XElement character,
        XElement spirit,
        bool created,
        out int maximum)
    {
        maximum = 0;
        string entityType = NormalizeSpiritEntityType(ReadValue(spirit, "type"));
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return false;
        }

        int basis;
        if (string.Equals(entityType, "Sprite", StringComparison.Ordinal))
        {
            if (!ParseBool(ReadValue(character, "resenabled"))
                || !TryReadCharacterAttributeValue(character, "RES", "totalvalue", out basis))
            {
                return false;
            }
        }
        else
        {
            if (!ParseBool(ReadValue(character, "magenabled"))
                || !TryReadCharacterAttributeValue(character, "MAG", "value", out int magicValue)
                || !TryReadCharacterAttributeValue(character, "MAG", "totalvalue", out int magicTotalValue))
            {
                return false;
            }

            string savedSetting = ReadValue(character, "spiritforcebasedontotalmag");
            if (bool.TryParse(savedSetting, out bool useTotalMagic))
            {
                basis = useTotalMagic ? magicTotalValue : magicValue;
            }
            else if (magicValue == magicTotalValue)
            {
                basis = magicValue;
            }
            else
            {
                return false;
            }
        }

        if (basis <= 0)
        {
            maximum = 0;
            return true;
        }

        if (created)
        {
            if (basis > int.MaxValue / 2)
            {
                return false;
            }
            basis *= 2;
        }

        maximum = basis;
        return true;
    }

    private static bool TryReadCharacterAttributeValue(
        XElement character,
        string attributeName,
        string propertyName,
        out int value)
    {
        value = 0;
        XElement? attribute = character.Element("attributes")?
            .Elements("attribute")
            .FirstOrDefault(candidate => string.Equals(
                ReadValue(candidate, "name"),
                attributeName,
                StringComparison.OrdinalIgnoreCase));
        return attribute is not null
            && int.TryParse(
                ReadValue(attribute, propertyName),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static string NormalizeSpiritEntityType(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "SPIRIT" => "Spirit",
            "SPRITE" => "Sprite",
            _ => string.Empty
        };

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
                Source: ReadValue(program, "source"),
                Guid: ReadValue(program, "guid"),
                Notes: ReadValue(program, "notes"),
                CustomName: ReadValue(program, "extra")))
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
                Schooling: ParseBool(ReadValue(grade, "schooling")),
                Guid: ReadValue(grade, "guid"),
                Reward: ReadValue(grade, "reward"),
                Notes: ReadValue(grade, "notes")))
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
                Rating: ParseInt(ReadValue(power, "rating")),
                Guid: ReadValue(power, "guid"),
                Notes: ReadValue(power, "notes"),
                CustomName: ReadValue(power, "extra")))
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
        IReadOnlyList<string> selectedSources = character
            .Element("sources")?
            .Elements("source")
            .Select(source => CanonicalizeSourceCode(source.Value))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();

        IReadOnlyDictionary<string, int> referenceCounts = character
            .Descendants("source")
            .Where(sourceNode => !string.Equals(sourceNode.Parent?.Name.LocalName, "sources", StringComparison.OrdinalIgnoreCase))
            .Select(sourceNode => CanonicalizeSourceCode(sourceNode.Value))
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .GroupBy(source => source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<CharacterSourcebookSummary> sourcebooks = selectedSources
            .Concat(referenceCounts.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.OrdinalIgnoreCase)
            .Select(source =>
            {
                bool selected = selectedSources.Contains(source, StringComparer.OrdinalIgnoreCase);
                int itemReferenceCount = referenceCounts.TryGetValue(source, out int count) ? count : 0;

                return new CharacterSourcebookSummary(
                    Code: source,
                    ItemReferenceCount: itemReferenceCount,
                    SelectedForCharacter: selected,
                    MissingFromSelectedList: itemReferenceCount > 0 && !selected,
                    SelectionOnly: selected && itemReferenceCount == 0);
            })
            .ToArray();

        return new CharacterSourcesSection(
            Count: selectedSources.Count,
            Sources: selectedSources,
            ReferencedSourceCount: referenceCounts.Count,
            Sourcebooks: sourcebooks);
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
                Enabled: ReadLegacyImprovementIntegerFlag(improvement, "enabled", defaultValue: 1) > 0))
            .ToArray()
            ?? Array.Empty<CharacterImprovementSummary>();

        return new CharacterImprovementsSection(
            Count: improvements.Count,
            EnabledCount: improvements.Count(improvement => improvement.Enabled),
            Improvements: improvements);
    }

    private static CharacterMatrixImprovementBasis BuildCharacterMatrixImprovementBasis(
        XElement character,
        bool careerMode)
    {
        XElement[] improvements = character
            .Element("improvements")?
            .Elements("improvement")
            .ToArray()
            ?? [];
        bool overclockerEnabled = improvements.Any(improvement =>
            string.Equals(
                ReadValue(improvement, "improvementttype"),
                "Overclocker",
                StringComparison.Ordinal)
            && IsApplicableValueImprovement(improvement, careerMode));
        bool deviceRatingExact = TryReadLivingPersonaImprovementExpression(
            improvements,
            "LivingPersonaDeviceRating",
            careerMode,
            out string deviceRatingSuffix);
        bool conditionMonitorExact = TryReadLivingPersonaImprovementExpression(
            improvements,
            "LivingPersonaMatrixCM",
            careerMode,
            out string conditionMonitorExpression);

        return new CharacterMatrixImprovementBasis(
            OverclockerEnabled: overclockerEnabled,
            LivingPersonaDeviceRatingExact: deviceRatingExact,
            LivingPersonaDeviceRatingSuffix: deviceRatingSuffix,
            LivingPersonaConditionMonitorExact: conditionMonitorExact,
            LivingPersonaConditionMonitorExpression: conditionMonitorExpression,
            SavedAttributeTotals: ReadSavedAttributeTotals(character));
    }

    private static IReadOnlyDictionary<string, int> ReadSavedAttributeTotals(XElement character)
    {
        var totals = new Dictionary<string, int>(StringComparer.Ordinal);
        var unavailable = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement attribute in character.Element("attributes")?.Elements("attribute") ?? [])
        {
            string name = ReadValue(attribute, "name");
            if (string.IsNullOrEmpty(name) || unavailable.Contains(name))
            {
                continue;
            }

            if (!int.TryParse(
                    ReadValue(attribute, "totalvalue"),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int total)
                || !totals.TryAdd(name, total))
            {
                totals.Remove(name);
                unavailable.Add(name);
            }
        }
        return totals;
    }

    private static bool TryReadLivingPersonaImprovementExpression(
        IEnumerable<XElement> improvements,
        string improvementType,
        bool careerMode,
        out string expression)
    {
        List<CharacterMatrixImprovementFragment> fragments = [];
        foreach (XElement improvement in improvements)
        {
            if (!string.Equals(
                    ReadValue(improvement, "improvementttype"),
                    improvementType,
                    StringComparison.Ordinal)
                || !IsApplicableValueImprovement(improvement, careerMode))
            {
                continue;
            }

            string valueText = ReadValue(improvement, "val");
            if (!string.IsNullOrEmpty(valueText)
                && !decimal.TryParse(
                    valueText,
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                expression = string.Empty;
                return false;
            }

            fragments.Add(new CharacterMatrixImprovementFragment(
                Expression: ReadValue(improvement, "improvedname"),
                Value: string.IsNullOrEmpty(valueText)
                    ? 0m
                    : decimal.Parse(valueText, NumberStyles.Number, CultureInfo.InvariantCulture),
                UniqueName: ReadValue(improvement, "unique"),
                Custom: ParseBool(ReadValue(improvement, "custom"))));
        }

        if (!CharacterMatrixImprovementSelector.TrySelectExpressions(fragments, out IReadOnlyList<string> selected))
        {
            expression = string.Empty;
            return false;
        }
        foreach (string fragment in selected)
        {
            if (!string.IsNullOrEmpty(fragment) && fragment[0] is not ('+' or '-'))
            {
                expression = string.Empty;
                return false;
            }
        }

        expression = string.Concat(selected);
        return true;
    }

    private static bool IsApplicableValueImprovement(XElement improvement, bool careerMode)
    {
        if (ReadLegacyImprovementIntegerFlag(improvement, "enabled", defaultValue: 1) <= 0
            || ReadLegacyImprovementIntegerFlag(improvement, "addtorating", defaultValue: 0) > 0)
        {
            return false;
        }

        string condition = ReadValue(improvement, "condition");
        return string.IsNullOrEmpty(condition)
            || string.Equals(
                condition,
                careerMode ? "career" : "create",
                StringComparison.Ordinal);
    }

    private static int ReadLegacyImprovementIntegerFlag(
        XElement improvement,
        string nodeName,
        int defaultValue)
    {
        string value = ReadValue(improvement, nodeName);
        if (int.TryParse(
                value,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed))
        {
            return parsed;
        }
        return bool.TryParse(value, out bool boolean)
            ? boolean ? 1 : 0
            : defaultValue;
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
                Quantity: ParseDecimal(ReadValue(drug, "qty")),
                Guid: ReadValue(drug, "guid"),
                Notes: ReadValue(drug, "notes"),
                CustomName: ReadValue(drug, "extra")))
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

    private static void FlattenCyberwareSummary(
        XElement item,
        ICollection<CharacterCyberwareSummary> cyberwares,
        string parentGuid,
        string parentName,
        string hierarchyPath,
        int depth,
        bool careerEditable,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData)
    {
        string name = ReadValue(item, "name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        string guid = ReadValue(item, "guid");
        List<XElement> children = EnumerateCyberwareChildren(item).ToList();
        string mountSlot = ReadFirstNonEmptyValue(item, "plugsintomodularmount", "plugintomodularmount", "modularmount");
        string nextHierarchyPath = string.IsNullOrWhiteSpace(hierarchyPath)
            ? name
            : $"{hierarchyPath} > {name}";
        bool maximumExact = TryCalculateCyberwareMatrixMaximum(
            item,
            improvementBasis,
            sourceData,
            out int maximum);

        cyberwares.Add(new CharacterCyberwareSummary(
            Guid: guid,
            Name: name,
            Category: ReadValue(item, "category"),
            Essence: ReadValue(item, "ess"),
            Capacity: ReadValue(item, "capacity"),
            Rating: ReadValue(item, "rating"),
            Cost: ReadValue(item, "cost"),
            Grade: ReadValue(item, "grade"),
            Location: ReadValue(item, "location"),
            ParentGuid: parentGuid,
            ParentName: parentName,
            MountSlot: mountSlot,
            HierarchyPath: nextHierarchyPath,
            Depth: depth,
            ChildCount: children.Count,
            IsModular: !string.IsNullOrWhiteSpace(mountSlot)
                || ParseBool(ReadValue(item, "hasmodularmount"))
                || name.Contains("Modular", StringComparison.OrdinalIgnoreCase),
            Source: ReadValue(item, "source"),
            Notes: ReadValue(item, "notes"),
            CustomName: ReadValue(item, "extra"),
            Equipped: ParseBool(ReadValue(item, "equipped")),
            WirelessEnabled: ParseBool(ReadValue(item, "wirelesson")),
            HomeNode: ParseBool(ReadValue(item, "homenode")),
            MatrixDamage: ParseInt(ReadValue(item, "matrixcmfilled")),
            MatrixConditionMaximum: maximumExact ? maximum : 0,
            MatrixConditionMaximumExact: maximumExact,
            CareerEditable: careerEditable));

        foreach (XElement child in children)
        {
            FlattenCyberwareSummary(
                child,
                cyberwares,
                parentGuid: guid,
                parentName: name,
                hierarchyPath: nextHierarchyPath,
                depth: depth + 1,
                careerEditable,
                improvementBasis,
                sourceData);
        }
    }

    private static bool TryCalculateCyberwareMatrixMaximum(
        XElement cyberware,
        CharacterMatrixImprovementBasis improvementBasis,
        ICharacterSourceDataContext? sourceData,
        out int maximum)
    {
        maximum = 0;
        if (!TryParseOptionalInt(ReadValue(cyberware, "rating"), out int rating))
        {
            return false;
        }

        string deviceRatingExpression = ReadValue(cyberware, "devicerating");
        int deviceRating;
        if (string.IsNullOrWhiteSpace(deviceRatingExpression))
        {
            if (sourceData?.TryResolveCyberwareGradeDeviceRating(
                    ReadValue(cyberware, "grade"),
                    ReadValue(cyberware, "improvementsource"),
                    out deviceRating) != true)
            {
                return false;
            }
        }
        else if (!CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                     deviceRatingExpression,
                     rating,
                     improvementBasis.SavedAttributeTotals,
                     out deviceRating))
        {
            return false;
        }

        if (!TryCalculateCyberwareTotalBonusMatrixBoxes(
                cyberware,
                improvementBasis,
                out int bonusMatrixBoxes))
        {
            return false;
        }

        if (string.Equals(ReadValue(cyberware, "overclocked"), "Device Rating", StringComparison.Ordinal)
            && improvementBasis.OverclockerEnabled)
        {
            if (deviceRating == int.MaxValue)
            {
                return false;
            }
            deviceRating++;
        }

        return CharacterMatrixConditionMonitorCalculator.TryCalculateMaximum(
            deviceRating,
            bonusMatrixBoxes,
            out maximum);
    }

    private static bool TryCalculateCyberwareTotalBonusMatrixBoxes(
        XElement cyberware,
        CharacterMatrixImprovementBasis improvementBasis,
        out int total)
    {
        long calculated = 0;
        foreach (XElement child in EnumerateCyberwareChildren(cyberware))
        {
            if (!TryCalculateCyberwareTotalBonusMatrixBoxes(
                child,
                improvementBasis,
                out int childBonus))
            {
                total = 0;
                return false;
            }
            calculated += childBonus;
        }

        foreach (XElement gear in cyberware.Element("gears")?.Elements("gear") ?? [])
        {
            if (!TryParseOptionalBool(ReadValue(gear, "equipped"), out bool equipped))
            {
                total = 0;
                return false;
            }
            if (!equipped)
            {
                continue;
            }
            if (!TryCalculateGearTotalBonusMatrixBoxes(gear, improvementBasis, out int gearBonus))
            {
                total = 0;
                return false;
            }
            calculated += gearBonus;
        }

        if (calculated is < int.MinValue or > int.MaxValue)
        {
            total = 0;
            return false;
        }
        total = (int)calculated;
        return true;
    }

    private static IEnumerable<XElement> EnumerateCyberwareChildren(XElement item)
    {
        return item.Elements("cyberware")
            .Concat(item.Element("children")?.Elements("cyberware") ?? Array.Empty<XElement>())
            .Concat(item.Element("cyberwares")?.Elements("cyberware") ?? Array.Empty<XElement>());
    }

    private static string ReadFirstNonEmptyValue(XElement parent, params string[] nodeNames)
    {
        foreach (string nodeName in nodeNames)
        {
            string value = ReadValue(parent, nodeName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string CanonicalizeSourceCode(string value)
    {
        return (value ?? string.Empty).Trim().ToUpperInvariant();
    }

    private static CharacterSpellDefenseMetricSummary CreateSpellDefenseMetric(
        XElement character,
        string id,
        string label,
        string nodeName,
        int counterspellingDice,
        string formula,
        int fallbackBaseValue)
    {
        int baseValue = TryReadIntValue(character, nodeName, out int parsedValue)
            ? parsedValue
            : fallbackBaseValue;
        return new CharacterSpellDefenseMetricSummary(
            Id: id,
            Label: label,
            BaseValue: baseValue,
            CounterspellingDice: counterspellingDice,
            TotalValue: baseValue + counterspellingDice,
            Formula: formula);
    }

    private static CharacterContactSummary ParseContactSummary(XElement character, XElement contact)
    {
        ContactRecordType recordType = ParseContactRecordType(ReadValue(contact, "type"));
        CharacterContactEditSemantics? contactSemantics = null;
        CharacterPetEditSemantics? petSemantics = null;
        bool exact = recordType == ContactRecordType.Pet
            ? CharacterPetEditSemanticsResolver.TryResolve(contact, out petSemantics)
            : CharacterContactEditSemanticsResolver.TryResolve(character, contact, out contactSemantics);
        int connection = exact && contactSemantics is not null
            ? contactSemantics.Connection
            : Math.Max(1, ParseInt(ReadValue(contact, "connection")));
        int loyalty = exact && contactSemantics is not null
            ? contactSemantics.Loyalty
            : Math.Max(1, ParseInt(ReadValue(contact, "loyalty")));
        string linkedFileName = ReadValue(contact, "file");
        string linkedRelativeFileName = ReadValue(contact, "relative");
        bool linked = !string.IsNullOrWhiteSpace(linkedFileName)
            || !string.IsNullOrWhiteSpace(linkedRelativeFileName);
        XElement? linkedIdentity = contact.Element("chummercomplete")?.Element("linkedcharacter");
        string linkedName = linkedIdentity?.Element("name")?.Value.Trim() ?? string.Empty;
        bool linkedIdentityResolved = linked && !string.IsNullOrWhiteSpace(linkedName);
        string savedMetatype = ReadValue(contact, "metatype");
        string savedGender = FirstNonBlank(ReadValue(contact, "gender"), ReadValue(contact, "sex"));
        string savedAge = ReadValue(contact, "age");
        string linkedDisplayName = linkedIdentity?.Element("displayname")?.Value.Trim() ?? string.Empty;
        string linkedMetatype = linkedIdentity?.Element("metatype")?.Value.Trim() ?? string.Empty;
        string linkedGender = linkedIdentity?.Element("gender")?.Value.Trim() ?? string.Empty;
        string linkedAge = linkedIdentity?.Element("age")?.Value.Trim() ?? string.Empty;
        string pathDisplayName = Path.GetFileName(FirstNonBlank(linkedFileName, linkedRelativeFileName))
            ?? string.Empty;
        return new CharacterContactSummary(
            Name: linkedIdentityResolved ? linkedName : ReadValue(contact, "name"),
            Role: ReadValue(contact, "role"),
            Location: ReadValue(contact, "location"),
            Connection: connection,
            Loyalty: loyalty,
            Guid: ReadValue(contact, "guid"),
            Notes: ReadValue(contact, "notes"),
            CustomName: ReadValue(contact, "extra"),
            Metatype: linkedIdentityResolved ? FirstNonBlank(linkedMetatype, savedMetatype) : savedMetatype,
            Gender: linkedIdentityResolved ? FirstNonBlank(linkedGender, savedGender) : savedGender,
            Age: linkedIdentityResolved ? FirstNonBlank(linkedAge, savedAge) : savedAge,
            ContactType: ReadValue(contact, "contacttype"),
            PreferredPayment: ReadValue(contact, "preferredpayment"),
            HobbiesVice: ReadValue(contact, "hobbiesvice"),
            PersonalLife: ReadValue(contact, "personallife"),
            GroupName: ReadValue(contact, "groupname"),
            IsGroup: contactSemantics?.IsGroup ?? ParseBool(ReadValue(contact, "group")),
            Free: contactSemantics?.Free ?? ParseBool(ReadValue(contact, "free")),
            Family: contactSemantics?.Family ?? ParseBool(ReadValue(contact, "family")),
            Blackmail: contactSemantics?.Blackmail ?? ParseBool(ReadValue(contact, "blackmail")),
            ConnectionMaximum: contactSemantics?.ConnectionMaximum ?? 0,
            IdentityEditable: exact && (petSemantics?.IdentityEditable ?? contactSemantics?.IdentityEditable ?? false),
            ConnectionEditable: exact && contactSemantics?.ConnectionEditable == true,
            LoyaltyEditable: exact && contactSemantics?.LoyaltyEditable == true,
            GroupEditable: exact && contactSemantics?.GroupEditable == true,
            FreeEditable: exact && contactSemantics?.FreeEditable == true,
            FamilyEditable: exact && contactSemantics?.FamilyEditable == true,
            BlackmailEditable: exact && contactSemantics?.BlackmailEditable == true,
            CanDelete: exact && (petSemantics?.CanDelete ?? contactSemantics?.CanDelete ?? false),
            EditSemanticsExact: exact,
            LinkedCharacter: new CharacterLinkedAssociationSummary(
                IsLinked: linked,
                IdentityResolved: linkedIdentityResolved,
                FileName: linkedFileName,
                RelativeFileName: linkedRelativeFileName,
                DisplayName: FirstNonBlank(
                    linkedDisplayName,
                    pathDisplayName,
                    linked ? "Linked runner" : string.Empty)));
    }

    private static ContactRecordType ParseContactRecordType(string value)
        => value.Trim().ToUpperInvariant() switch
        {
            "PET" => ContactRecordType.Pet,
            "CONTACT" or "" => ContactRecordType.Contact,
            _ => ContactRecordType.Enemy
        };

    private static int ReadAttributeTotalValue(XElement character, string attributeName)
        => (character.Element("attributes")?.Elements("attribute") ?? Enumerable.Empty<XElement>())
            .Where(attribute => string.Equals(ReadValue(attribute, "name"), attributeName, StringComparison.OrdinalIgnoreCase))
            .Select(attribute => ParseInt(ReadValue(attribute, "totalvalue")))
            .FirstOrDefault();

    private static bool TryReadIntValue(XElement parent, string nodeName, out int value)
    {
        XElement? element = parent.Element(nodeName);
        if (element is null)
        {
            value = 0;
            return false;
        }

        value = ParseInt(element.Value.Trim());
        return true;
    }

    private static int EstimateArmorRating(XElement character)
    {
        XElement? armors = character.Element("armors");
        if (armors is null)
        {
            return 0;
        }

        int highestBaseArmor = 0;
        int stackedArmorDelta = 0;

        foreach (XElement armor in armors.Elements("armor"))
        {
            if (!ParseBool(ReadValue(armor, "equipped")))
            {
                continue;
            }

            string armorValue = ReadValue(armor, "armor");
            if (string.IsNullOrWhiteSpace(armorValue) || !TryParseSignedInteger(armorValue, out int parsedArmor))
            {
                continue;
            }

            if (armorValue.StartsWith('+') || armorValue.StartsWith('-'))
            {
                stackedArmorDelta += parsedArmor;
            }
            else
            {
                highestBaseArmor = Math.Max(highestBaseArmor, parsedArmor);
            }
        }

        return Math.Max(0, highestBaseArmor + stackedArmorDelta);
    }

    private static bool TryParseSignedInteger(string value, out int parsed)
        => int.TryParse(value.Trim(), out parsed);

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

    private static bool TryParseOptionalInt(string value, out int parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = 0;
            return true;
        }

        return int.TryParse(value, out parsed);
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static bool ParseBool(string value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static bool TryParseOptionalBool(string value, out bool parsed)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsed = false;
            return true;
        }

        return bool.TryParse(value, out parsed);
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

    private enum ContactRecordType
    {
        Contact,
        Pet,
        Enemy
    }
}
