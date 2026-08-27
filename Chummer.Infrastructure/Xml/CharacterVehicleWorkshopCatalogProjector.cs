using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

/// <summary>
/// Lossless, bounded projection of the effective SR5 vehicles catalog into the
/// typed workshop contract. Source rows remain visible when they cannot yet be
/// persisted exactly, but are explicitly marked Unsupported and cannot be quoted.
/// </summary>
internal static class CharacterVehicleWorkshopCatalogProjector
{
    private static readonly IReadOnlySet<string> ChassisFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "page", "source", "accel", "armor", "avail", "body", "category",
        "cost", "handling", "pilot", "sensor", "speed", "seats", "addslots", "modslots", "gears", "mods"
    };

    private static readonly IReadOnlySet<string> FactoryGearSourceFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "category", "rating", "minrating", "source", "page", "avail",
        "capacity", "armorcapacity", "cost", "weight", "costfor", "addoncategory",
        "requireparent", "ratinglabel"
    };

    private static readonly IReadOnlySet<string> FactoryGearInstructionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "rating", "capacity", "maxrating"
    };

    private static readonly IReadOnlySet<string> FactoryGearInstructionAttributes = new HashSet<string>(StringComparer.Ordinal)
    {
        "rating", "qty", "consumecapacity"
    };

    private static readonly IReadOnlySet<string> FactoryModificationSourceFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "page", "source", "avail", "category", "cost", "rating", "slots",
        "limit", "capacity", "ratinglabel", "conditionmonitor", "weaponmountcategories",
        "ammoreplace", "ammobonus", "ammobonuspercent", "useownattributesforweapon"
    };

    private static readonly IReadOnlySet<string> FactoryModificationInstructionFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "rating"
    };

    private static readonly IReadOnlySet<string> ModificationFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "page", "source", "avail", "category", "cost", "rating", "minrating",
        "slots", "capacity"
    };

    private static readonly IReadOnlySet<string> WeaponMountFields = new HashSet<string>(StringComparer.Ordinal)
    {
        "id", "name", "page", "source", "avail", "category", "cost", "slots", "capacity"
    };

    internal static bool TryProject(
        string profileId,
        string profileDigest,
        string vehiclesDigest,
        string weaponsDigest,
        string gearDigest,
        string overlayDigest,
        bool droneMods,
        bool multiplyRestrictedCost,
        decimal restrictedCostMultiplier,
        bool multiplyForbiddenCost,
        decimal forbiddenCostMultiplier,
        IReadOnlyList<XElement> vehicleRows,
        IReadOnlyList<XElement> gearRows,
        IReadOnlyList<XElement> modificationRows,
        IReadOnlyList<XElement> weaponMountRows,
        Func<string, bool> isSourceEnabled,
        out CharacterVehicleWorkshopCatalog catalog)
    {
        catalog = EmptyCatalog();
        if (string.IsNullOrWhiteSpace(profileId)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(profileDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(vehiclesDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(weaponsDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(gearDigest)
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(overlayDigest)
            || restrictedCostMultiplier < 0m
            || forbiddenCostMultiplier < 0m
            || multiplyRestrictedCost && restrictedCostMultiplier <= 0m
            || multiplyForbiddenCost && forbiddenCostMultiplier <= 0m
            || vehicleRows is null
            || gearRows is null
            || modificationRows is null
            || weaponMountRows is null
            || isSourceEnabled is null)
        {
            return false;
        }

        var chassis = new List<CharacterVehicleWorkshopChassisEntry>();
        var modifications = new List<CharacterVehicleWorkshopModificationEntry>();
        var components = new List<CharacterVehicleWeaponMountComponentEntry>();
        var chassisIds = new HashSet<Guid>();
        var modificationIds = new HashSet<Guid>();
        var componentIds = new HashSet<Guid>();
        var factoryGearSources = new List<FactoryGearSourceRow>();
        var factoryGearSourceIds = new HashSet<Guid>();
        var factoryModificationSources = new List<FactoryModificationSourceRow>();

        foreach (XElement row in gearRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!factoryGearSourceIds.Add(identity.Id))
                return false;
            factoryGearSources.Add(new FactoryGearSourceRow(
                identity,
                row,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(
                    row.ToString(SaveOptions.DisableFormatting))));
        }

        foreach (XElement row in modificationRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!modificationIds.Add(identity.Id))
                return false;
            factoryModificationSources.Add(new FactoryModificationSourceRow(
                identity,
                row,
                CharacterVehicleWorkshopRules.ComputeCharacterDigest(
                    row.ToString(SaveOptions.DisableFormatting))));
            modifications.Add(ProjectModification(row, identity));
        }

        foreach (XElement row in vehicleRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!chassisIds.Add(identity.Id))
                return false;
            chassis.Add(ProjectChassis(row, identity, droneMods, factoryGearSources, factoryModificationSources));
        }

        foreach (XElement row in weaponMountRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!componentIds.Add(identity.Id))
                return false;
            components.Add(ProjectWeaponMount(row, identity));
        }

        var binding = new CharacterVehicleWorkshopSourceBinding(
            CharacterVehicleWorkshopRules.RulesetId,
            profileId,
            CharacterVehicleWorkshopRules.SemanticsVersion,
            profileDigest,
            vehiclesDigest,
            weaponsDigest,
            gearDigest,
            overlayDigest,
            multiplyRestrictedCost,
            restrictedCostMultiplier,
            multiplyForbiddenCost,
            forbiddenCostMultiplier,
            Exact: true);
        var unsigned = new CharacterVehicleWorkshopCatalog(
            binding,
            chassis.OrderBy(item => item.SourceId.Value).ToArray(),
            modifications.OrderBy(item => item.SourceId.Value).ToArray(),
            components.OrderBy(item => item.SourceId.Value).ToArray(),
            DeclaredCatalogDigest: string.Empty);
        catalog = unsigned with
        {
            DeclaredCatalogDigest = CharacterVehicleWorkshopRules.ComputeCatalogDigest(unsigned)
        };
        return true;
    }

    private static CharacterVehicleWorkshopChassisEntry ProjectChassis(
        XElement row,
        SourceIdentity identity,
        bool droneMods,
        IReadOnlyList<FactoryGearSourceRow> factoryGearSources,
        IReadOnlyList<FactoryModificationSourceRow> factoryModificationSources)
    {
        var issues = DirectFieldIssues(row, ChassisFields);
        IReadOnlyList<CharacterVehicleWorkshopFactoryGearEntry> factoryGears = ProjectFactoryGears(
            row,
            new CharacterVehicleChassisSourceId(identity.Id),
            factoryGearSources,
            issues);
        IReadOnlyList<CharacterVehicleWorkshopFactoryModificationEntry> factoryModifications =
            ProjectFactoryModifications(
                row,
                new CharacterVehicleChassisSourceId(identity.Id),
                factoryModificationSources,
                issues);
        string category = RequiredText(row, "category", issues);
        string page = RequiredText(row, "page", issues);
        int body = RequiredNonNegativeInt(row, "body", issues);
        int handling = 0;
        int offRoadHandling = 0;
        ReadPair(row, "handling", issues, out handling, out offRoadHandling);
        int acceleration = 0;
        int offRoadAcceleration = 0;
        ReadPair(row, "accel", issues, out acceleration, out offRoadAcceleration);
        int speed = 0;
        int offRoadSpeed = 0;
        ReadPair(row, "speed", issues, out speed, out offRoadSpeed);
        int pilot = RequiredNonNegativeInt(row, "pilot", issues);
        int seats = RequiredNonNegativeInt(row, "seats", issues);
        int armor = RequiredNonNegativeInt(row, "armor", issues);
        int sensor = RequiredNonNegativeInt(row, "sensor", issues);
        decimal cost = RequiredNonNegativeDecimal(row, "cost", issues);
        CharacterVehicleWorkshopAvailability availability = RequiredAvailability(row, "avail", issues);
        if (availability.AddToParent)
            issues.Add("A chassis availability value cannot be additive.");

        int addSlots = OptionalNonNegativeInt(row, "addslots", 0, issues);
        int explicitDroneSlots = OptionalNonNegativeInt(row, "modslots", body, issues);
        int modificationSlots = 0;
        try
        {
            modificationSlots = category.StartsWith("Drones:", StringComparison.Ordinal) && droneMods
                ? explicitDroneSlots
                : checked(Math.Max(body, 4) + addSlots);
        }
        catch (OverflowException)
        {
            issues.Add("The source chassis slot arithmetic exceeds the supported integer range.");
        }

        CharacterVehicleWorkshopProjectionStatus status = Status(issues);
        return new CharacterVehicleWorkshopChassisEntry(
            new CharacterVehicleChassisSourceId(identity.Id),
            category.StartsWith("Drones:", StringComparison.Ordinal)
                ? CharacterVehicleChassisKind.Drone
                : CharacterVehicleChassisKind.Vehicle,
            CharacterVehicleChassisPosture.Stock,
            identity.Name,
            category,
            handling,
            offRoadHandling,
            acceleration,
            offRoadAcceleration,
            speed,
            offRoadSpeed,
            pilot,
            body,
            seats,
            armor,
            sensor,
            modificationSlots,
            ModificationCapacity: 0,
            cost,
            availability,
            identity.SourceBook,
            page,
            GmAuthorityDigest: string.Empty,
            status,
            UnsupportedReason(issues),
            factoryGears,
            factoryModifications);
    }

    private static IReadOnlyList<CharacterVehicleWorkshopFactoryGearEntry> ProjectFactoryGears(
        XElement vehicleRow,
        CharacterVehicleChassisSourceId chassisSourceId,
        IReadOnlyList<FactoryGearSourceRow> sourceRows,
        ICollection<string> chassisIssues)
    {
        XElement[] containers = vehicleRow.Elements("gears").Take(2).ToArray();
        if (containers.Length == 0)
            return [];
        if (containers.Length != 1
            || containers[0].HasAttributes
            || containers[0].Elements().Any(child => child.Name.LocalName != "gear")
            || containers[0].Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            chassisIssues.Add("The factory gear container is ambiguous or contains unsupported content.");
            return [];
        }

        CharacterVehicleWorkshopFactoryGearEntry[] projected = containers[0]
            .Elements("gear")
            .Select((instruction, ordinal) => ProjectFactoryGear(
                instruction,
                chassisSourceId,
                ordinal,
                sourceRows))
            .ToArray();
        foreach (CharacterVehicleWorkshopFactoryGearEntry item in projected.Where(item =>
                     item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported))
        {
            chassisIssues.Add($"Factory gear #{item.Ordinal + 1} ({item.Name}) is unsupported: {item.UnsupportedReason}");
        }
        return projected;
    }

    private static CharacterVehicleWorkshopFactoryGearEntry ProjectFactoryGear(
        XElement instruction,
        CharacterVehicleChassisSourceId chassisSourceId,
        int ordinal,
        IReadOnlyList<FactoryGearSourceRow> sourceRows)
    {
        var issues = new List<string>();
        string instructionDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(
            instruction.ToString(SaveOptions.DisableFormatting));
        string[] unsupportedAttributes = instruction.Attributes()
            .Where(attribute => !FactoryGearInstructionAttributes.Contains(attribute.Name.LocalName))
            .Select(attribute => attribute.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedAttributes.Length != 0)
        {
            issues.Add("Factory instruction attributes require prompts or unprojected behavior: "
                + string.Join(", ", unsupportedAttributes) + ".");
        }
        string[] unsupportedFields = instruction.Elements()
            .Where(element => !FactoryGearInstructionFields.Contains(element.Name.LocalName))
            .Select(element => element.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedFields.Length != 0)
        {
            issues.Add("Factory instruction fields are not projected losslessly: "
                + string.Join(", ", unsupportedFields) + ".");
        }
        string[] duplicateFields = instruction.Elements()
            .Where(element => FactoryGearInstructionFields.Contains(element.Name.LocalName))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateFields.Length != 0)
        {
            issues.Add("Factory instruction fields are ambiguous because they occur more than once: "
                + string.Join(", ", duplicateFields) + ".");
        }

        string reference = ReadFactoryGearReference(instruction, issues);
        FactoryGearSourceRow[] matches = Guid.TryParse(reference, out Guid requestedId)
            ? sourceRows.Where(row => row.Identity.Id == requestedId).Take(2).ToArray()
            : sourceRows.Where(row => string.Equals(row.Identity.Name, reference, StringComparison.Ordinal)).Take(2).ToArray();
        FactoryGearSourceRow? source = matches.Length == 1 ? matches[0] : null;
        if (source is null)
            issues.Add("The factory gear reference does not resolve to one enabled effective gear.xml row.");

        CharacterVehicleFactoryGearSourceId sourceId = new(source?.Identity.Id ?? Guid.Empty);
        CharacterVehicleFactoryGearProjectionId projectionId =
            CharacterVehicleWorkshopRules.DeriveFactoryGearProjectionId(
                chassisSourceId,
                sourceId,
                ordinal,
                instructionDigest);
        string name = source?.Identity.Name ?? reference;
        string category = string.Empty;
        string capacity = string.Empty;
        string armorCapacity = string.Empty;
        string minimumRatingText = string.Empty;
        string maximumRatingText = string.Empty;
        int rating = 0;
        decimal quantity = 1m;
        CharacterVehicleWorkshopAvailability availability =
            new(0, CharacterVehicleWorkshopLegality.Legal, false);
        string weight = string.Empty;
        string sourceBook = source?.Identity.SourceBook ?? string.Empty;
        string page = string.Empty;
        bool consumeCapacity = false;
        string sourceNodeDigest = source?.NodeDigest ?? string.Empty;

        if (source is not null)
        {
            AddFactoryGearSourceIssues(source.Row, issues);
            category = RequiredText(source.Row, "category", issues);
            page = RequiredText(source.Row, "page", issues);
            availability = RequiredAvailability(source.Row, "avail", issues);
            if (!TryReadScalar(source.Row, "rating", out string maximumText)
                || !int.TryParse(maximumText, NumberStyles.None, CultureInfo.InvariantCulture, out int maximumRating)
                || maximumRating < 0)
            {
                issues.Add("The factory gear maximum rating is not one fixed non-negative integer.");
                maximumRating = 0;
            }
            maximumRatingText = maximumRating == 0
                ? string.Empty
                : maximumRating.ToString(CultureInfo.InvariantCulture);
            int effectiveMinimum = maximumRating > 0 ? 1 : 0;
            XElement[] minimumNodes = source.Row.Elements("minrating").Take(2).ToArray();
            if (minimumNodes.Length == 1
                && TryReadScalarElement(minimumNodes[0], out string minimumText)
                && int.TryParse(minimumText, NumberStyles.None, CultureInfo.InvariantCulture, out int minimumRating)
                && minimumRating >= 0
                && (maximumRating == 0 || minimumRating <= maximumRating))
            {
                minimumRatingText = minimumText;
                effectiveMinimum = minimumRating == 0 && maximumRating > 0 ? 1 : minimumRating;
            }
            else if (minimumNodes.Length != 0)
            {
                issues.Add("The factory gear minimum rating is not one fixed value inside its source range.");
            }

            int requestedRating = ReadFactoryInstructionRating(instruction, issues);
            int effectiveMaximum = maximumRating == 0 ? int.MaxValue : maximumRating;
            rating = Math.Max(Math.Min(requestedRating, effectiveMaximum), effectiveMinimum);
            quantity = ReadFactoryInstructionQuantity(instruction, issues);
            consumeCapacity = ReadFactoryConsumeCapacity(instruction, issues);
            string rawCapacity = ReadOptionalCapacity(source.Row, "capacity", issues);
            string rawArmorCapacity = ReadOptionalCapacity(source.Row, "armorcapacity", issues);
            XElement[] capacityOverrides = instruction.Elements("capacity").Take(2).ToArray();
            if (capacityOverrides.Length == 1
                && TryReadScalarElement(capacityOverrides[0], out string overrideText)
                && IsFixedCapacity(overrideText))
            {
                rawCapacity = overrideText;
            }
            else if (capacityOverrides.Length != 0)
            {
                issues.Add("The factory capacity override is not one fixed capacity value.");
            }
            capacity = consumeCapacity ? rawCapacity : ZeroConsumedCapacity(rawCapacity);
            armorCapacity = consumeCapacity ? rawArmorCapacity : ZeroConsumedCapacity(rawArmorCapacity);
            weight = ReadOptionalFixedDecimalText(source.Row, "weight", issues);

            if (!TryReadScalar(source.Row, "cost", out string cost)
                || (!decimal.TryParse(cost, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fixedCost)
                    || fixedCost < 0m)
                && !TryReadRatingFactor(cost, out _))
            {
                issues.Add("The factory gear cost would require an interactive or unbounded evaluation before it is zeroed.");
            }
            XElement[] costForNodes = source.Row.Elements("costfor").Take(2).ToArray();
            if (costForNodes.Length == 1
                && (!TryReadScalarElement(costForNodes[0], out string costForText)
                    || !decimal.TryParse(costForText, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal costFor)
                    || costFor != 1m)
                || costForNodes.Length > 1)
            {
                issues.Add("Factory gear with a non-unit source cost-for quantity is not projected yet.");
            }
            if (string.Equals(name, "Custom Item", StringComparison.Ordinal))
                issues.Add("Factory Custom Item naming requires an interactive selection.");
        }

        CharacterVehicleWorkshopProjectionStatus status = Status(issues);
        return new CharacterVehicleWorkshopFactoryGearEntry(
            projectionId,
            chassisSourceId,
            ordinal,
            sourceId,
            name,
            category,
            capacity,
            armorCapacity,
            minimumRatingText,
            maximumRatingText,
            rating,
            quantity,
            availability,
            weight,
            sourceBook,
            page,
            consumeCapacity,
            sourceNodeDigest,
            instructionDigest,
            status,
            UnsupportedReason(issues));
    }

    private static string ReadFactoryGearReference(XElement instruction, ICollection<string> issues)
    {
        XElement[] ids = instruction.Elements("id").Take(2).ToArray();
        XElement[] names = instruction.Elements("name").Take(2).ToArray();
        if (ids.Length + names.Length > 1)
            issues.Add("The factory gear reference is ambiguous.");
        if (ids.Length + names.Length == 1)
        {
            if (instruction.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
                issues.Add("The factory gear reference mixes direct text and named fields.");
            XElement reference = ids.Concat(names).Single();
            if (TryReadScalarElement(reference, out string value) && ValidText(value))
                return value;
            issues.Add("The factory gear reference text is missing or unsafe.");
            return string.Empty;
        }
        if (!instruction.HasElements)
        {
            string value = instruction.Value.Trim();
            if (ValidText(value))
                return value;
        }
        issues.Add("The factory gear instruction has no unique id, name, or direct-text reference.");
        return string.Empty;
    }

    private static void AddFactoryGearSourceIssues(XElement row, ICollection<string> issues)
    {
        if (row.HasAttributes)
            issues.Add("The effective factory gear source row has unprojected attributes.");
        string[] unsupported = row.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !FactoryGearSourceFields.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length != 0)
        {
            issues.Add("Factory gear source fields require prompts, children, weapons, bonuses, or matrix behavior: "
                + string.Join(", ", unsupported) + ".");
        }
        string[] duplicates = row.Elements()
            .Where(element => FactoryGearSourceFields.Contains(element.Name.LocalName)
                && element.Name.LocalName != "addoncategory")
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            issues.Add("Factory gear source fields are ambiguous because they occur more than once: "
                + string.Join(", ", duplicates) + ".");
        }
        if (row.Elements("addoncategory").Any(element =>
                !TryReadScalarElement(element, out string value) || !ValidText(value)))
        {
            issues.Add("A factory gear addon category contains unsafe or structured content.");
        }
        if (row.Elements("requireparent").Any(element => element.HasAttributes || element.HasElements
                || !string.IsNullOrWhiteSpace(element.Value)))
        {
            issues.Add("The factory gear require-parent marker is not the inert empty form.");
        }
    }

    private static int ReadFactoryInstructionRating(XElement instruction, ICollection<string> issues)
    {
        XAttribute? attribute = instruction.Attribute("rating");
        XElement[] elements = instruction.Elements("rating").Take(2).ToArray();
        if (attribute is not null && elements.Length != 0)
            issues.Add("The factory gear rating is declared both as an attribute and an element.");
        string text = attribute?.Value
            ?? (elements.Length == 1 && TryReadScalarElement(elements[0], out string elementText)
                ? elementText
                : "0");
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= 0)
            return value;
        issues.Add("The factory gear rating is not one fixed non-negative integer.");
        return 0;
    }

    private static decimal ReadFactoryInstructionQuantity(XElement instruction, ICollection<string> issues)
    {
        XAttribute? attribute = instruction.Attribute("qty");
        if (attribute is null)
            return 1m;
        if (decimal.TryParse(attribute.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && value > 0m)
        {
            return value;
        }
        issues.Add("The factory gear quantity is not one fixed positive number.");
        return 1m;
    }

    private static bool ReadFactoryConsumeCapacity(XElement instruction, ICollection<string> issues)
    {
        XAttribute? attribute = instruction.Attribute("consumecapacity");
        if (attribute is null)
            return false;
        if (bool.TryParse(attribute.Value, out bool value))
            return value;
        issues.Add("The factory gear consume-capacity marker is not a fixed boolean.");
        return false;
    }

    private static string ReadOptionalCapacity(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return string.Empty;
        if (nodes.Length == 1
            && TryReadScalarElement(nodes[0], out string text)
            && IsFixedCapacity(text))
        {
            return text;
        }
        issues.Add($"The factory gear {field} is not one fixed capacity value.");
        return string.Empty;
    }

    private static string ReadOptionalFixedDecimalText(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return string.Empty;
        if (nodes.Length == 1
            && TryReadScalarElement(nodes[0], out string text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && value >= 0m)
        {
            return text;
        }
        issues.Add($"The factory gear {field} is not one fixed non-negative number.");
        return string.Empty;
    }

    private static bool IsFixedCapacity(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int scalar) && scalar >= 0)
            return true;
        if (IsBracketedNonNegativeInt(value))
            return true;
        int slash = value.IndexOf("/[", StringComparison.Ordinal);
        return slash > 0
            && int.TryParse(value[..slash], NumberStyles.None, CultureInfo.InvariantCulture, out int prefix)
            && prefix >= 0
            && IsBracketedNonNegativeInt(value[(slash + 1)..]);
    }

    private static bool IsBracketedNonNegativeInt(string value)
        => value.Length >= 3
            && value[0] == '['
            && value[^1] == ']'
            && int.TryParse(value[1..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            && parsed >= 0;

    private static string ZeroConsumedCapacity(string value)
    {
        int slash = value.IndexOf("/[", StringComparison.Ordinal);
        return slash < 0 ? "[0]" : value[..slash] + "/[0]";
    }

    private static IReadOnlyList<CharacterVehicleWorkshopFactoryModificationEntry> ProjectFactoryModifications(
        XElement vehicleRow,
        CharacterVehicleChassisSourceId chassisSourceId,
        IReadOnlyList<FactoryModificationSourceRow> sourceRows,
        ICollection<string> chassisIssues)
    {
        XElement[] containers = vehicleRow.Elements("mods").Take(2).ToArray();
        if (containers.Length == 0)
            return [];
        if (containers.Length != 1
            || containers[0].HasAttributes
            || containers[0].Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            chassisIssues.Add("The factory modification container is ambiguous or contains unsupported content.");
            return [];
        }

        string[] unsupportedChildren = containers[0].Elements()
            .Where(child => child.Name.LocalName is not ("name" or "mod" or "addslots"))
            .Select(child => child.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedChildren.Length != 0)
        {
            chassisIssues.Add("The factory modification container has unsupported instruction kinds: "
                + string.Join(", ", unsupportedChildren) + ".");
        }
        if (containers[0].Elements("addslots").Any())
        {
            chassisIssues.Add("A nested factory add-slots instruction is not projected by this increment.");
        }

        CharacterVehicleWorkshopFactoryModificationEntry[] projected = containers[0]
            .Elements()
            .Where(instruction => instruction.Name.LocalName is "name" or "mod")
            .Select((instruction, ordinal) => ProjectFactoryModification(
                instruction,
                chassisSourceId,
                ordinal,
                sourceRows))
            .ToArray();
        foreach (CharacterVehicleWorkshopFactoryModificationEntry item in projected.Where(item =>
                     item.ProjectionStatus == CharacterVehicleWorkshopProjectionStatus.Unsupported))
        {
            chassisIssues.Add($"Factory modification #{item.Ordinal + 1} ({item.Name}) is unsupported: {item.UnsupportedReason}");
        }
        return projected;
    }

    private static CharacterVehicleWorkshopFactoryModificationEntry ProjectFactoryModification(
        XElement instruction,
        CharacterVehicleChassisSourceId chassisSourceId,
        int ordinal,
        IReadOnlyList<FactoryModificationSourceRow> sourceRows)
    {
        var issues = new List<string>();
        string instructionDigest = CharacterVehicleWorkshopRules.ComputeCharacterDigest(
            instruction.ToString(SaveOptions.DisableFormatting));
        string reference = ReadFactoryModificationReference(instruction, issues);
        FactoryModificationSourceRow[] matches = Guid.TryParse(reference, out Guid requestedId)
            ? sourceRows.Where(row => row.Identity.Id == requestedId).Take(2).ToArray()
            : sourceRows.Where(row => string.Equals(row.Identity.Name, reference, StringComparison.Ordinal)).Take(2).ToArray();
        FactoryModificationSourceRow? source = matches.Length == 1 ? matches[0] : null;
        if (source is null)
            issues.Add("The factory modification reference does not resolve to one enabled effective vehicles.xml modification row.");

        CharacterVehicleFactoryModificationSourceId sourceId = new(source?.Identity.Id ?? Guid.Empty);
        CharacterVehicleFactoryModificationInstructionId instructionId = CharacterVehicleWorkshopRules
            .DeriveFactoryModificationInstructionId(chassisSourceId, sourceId, ordinal, instructionDigest);
        string name = source?.Identity.Name ?? reference;
        string category = string.Empty;
        string limit = string.Empty;
        string slots = string.Empty;
        string capacity = string.Empty;
        int rating = 0;
        string maximumRating = string.Empty;
        string ratingLabel = "String_Rating";
        int conditionMonitor = 0;
        CharacterVehicleWorkshopAvailability availability = new(0, CharacterVehicleWorkshopLegality.Legal, false);
        string cost = string.Empty;
        string extra = string.Empty;
        string sourceBook = source?.Identity.SourceBook ?? string.Empty;
        string page = string.Empty;
        string subsystems = string.Empty;
        string weaponMountCategories = string.Empty;
        decimal ammoBonus = 0m;
        decimal ammoBonusPercent = 0m;
        string ammoReplace = string.Empty;
        bool useOwnAttributesForWeapon = false;
        string sourceNodeDigest = source?.NodeDigest ?? string.Empty;

        if (source is not null)
        {
            AddFactoryModificationSourceIssues(source.Row, issues);
            category = RequiredText(source.Row, "category", issues);
            page = RequiredText(source.Row, "page", issues);
            availability = RequiredAvailability(source.Row, "avail", issues);
            limit = ReadOptionalSafeText(source.Row, "limit", string.Empty, issues);
            slots = ReadRequiredFixedNonNegativeText(source.Row, "slots", issues);
            capacity = ReadOptionalFixedCapacityText(source.Row, "capacity", issues);
            cost = ReadRequiredFixedNonNegativeDecimalText(source.Row, "cost", issues);
            maximumRating = ReadRequiredFixedNonNegativeText(source.Row, "rating", issues);
            if (!int.TryParse(maximumRating, NumberStyles.None, CultureInfo.InvariantCulture, out int maximum))
                maximum = 0;
            int requested = ReadFactoryModificationRating(instruction, issues);
            rating = maximum == 0 ? 0 : Math.Min(Math.Max(requested, 1), maximum);
            ratingLabel = ReadOptionalSafeText(source.Row, "ratinglabel", "String_Rating", issues);
            conditionMonitor = ReadOptionalNonNegativeInt(source.Row, "conditionmonitor", issues);
            weaponMountCategories = ReadOptionalSafeText(source.Row, "weaponmountcategories", string.Empty, issues);
            ammoReplace = ReadOptionalSafeText(source.Row, "ammoreplace", string.Empty, issues);
            ammoBonus = ReadOptionalNonNegativeDecimal(source.Row, "ammobonus", issues);
            ammoBonusPercent = ReadOptionalNonNegativeDecimal(source.Row, "ammobonuspercent", issues);
            useOwnAttributesForWeapon = ReadOptionalBoolean(source.Row, "useownattributesforweapon", issues);
        }

        CharacterVehicleWorkshopProjectionStatus status = Status(issues);
        return new CharacterVehicleWorkshopFactoryModificationEntry(
            instructionId,
            chassisSourceId,
            ordinal,
            sourceId,
            name,
            category,
            limit,
            slots,
            capacity,
            rating,
            maximumRating,
            ratingLabel,
            conditionMonitor,
            availability,
            cost,
            extra,
            sourceBook,
            page,
            subsystems,
            weaponMountCategories,
            ammoBonus,
            ammoBonusPercent,
            ammoReplace,
            useOwnAttributesForWeapon,
            sourceNodeDigest,
            instructionDigest,
            status,
            UnsupportedReason(issues));
    }

    private static string ReadFactoryModificationReference(XElement instruction, ICollection<string> issues)
    {
        if (instruction.Name.LocalName == "name")
        {
            string[] unsupportedAttributes = instruction.Attributes()
                .Where(attribute => attribute.Name.LocalName != "rating")
                .Select(attribute => attribute.Name.LocalName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (unsupportedAttributes.Length != 0)
            {
                issues.Add("Factory modification attributes require prompts or unprojected behavior: "
                    + string.Join(", ", unsupportedAttributes) + ".");
            }
            if (instruction.HasElements || !ValidText(instruction.Value))
            {
                issues.Add("The factory modification name instruction is structured, missing, or unsafe.");
                return string.Empty;
            }
            return instruction.Value;
        }

        if (instruction.HasAttributes
            || instruction.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)))
        {
            issues.Add("The structured factory modification instruction has attributes or direct text.");
        }
        string[] unsupportedFields = instruction.Elements()
            .Where(element => !FactoryModificationInstructionFields.Contains(element.Name.LocalName))
            .Select(element => element.Name.LocalName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupportedFields.Length != 0)
        {
            issues.Add("Nested factory modification fields are not projected losslessly: "
                + string.Join(", ", unsupportedFields) + ".");
        }
        string[] duplicateFields = instruction.Elements()
            .Where(element => FactoryModificationInstructionFields.Contains(element.Name.LocalName))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicateFields.Length != 0)
        {
            issues.Add("Factory modification instruction fields are ambiguous because they occur more than once: "
                + string.Join(", ", duplicateFields) + ".");
        }
        XElement[] names = instruction.Elements("name").Take(2).ToArray();
        if (names.Length == 1 && TryReadScalarElement(names[0], out string value) && ValidText(value))
            return value;
        issues.Add("The structured factory modification instruction has no unique safe name.");
        return string.Empty;
    }

    private static void AddFactoryModificationSourceIssues(XElement row, ICollection<string> issues)
    {
        if (row.HasAttributes)
            issues.Add("The effective factory modification source row has unprojected attributes.");
        string[] unsupported = row.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !FactoryModificationSourceFields.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (unsupported.Length != 0)
        {
            issues.Add("Factory modification source fields require prompts, nested children, bonuses, dynamic evaluation, or unprojected behavior: "
                + string.Join(", ", unsupported) + ".");
        }
        string[] duplicates = row.Elements()
            .Where(element => FactoryModificationSourceFields.Contains(element.Name.LocalName))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        if (duplicates.Length != 0)
        {
            issues.Add("Factory modification source fields are ambiguous because they occur more than once: "
                + string.Join(", ", duplicates) + ".");
        }
    }

    private static int ReadFactoryModificationRating(XElement instruction, ICollection<string> issues)
    {
        string text = "0";
        if (instruction.Name.LocalName == "name")
        {
            XAttribute? attribute = instruction.Attribute("rating");
            if (attribute is not null)
                text = attribute.Value;
        }
        else
        {
            XElement[] ratings = instruction.Elements("rating").Take(2).ToArray();
            if (ratings.Length == 1 && TryReadScalarElement(ratings[0], out string value))
                text = value;
            else if (ratings.Length > 1)
                issues.Add("The factory modification rating instruction is ambiguous.");
        }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int rating) && rating >= 0)
            return rating;
        issues.Add("The factory modification rating is not one fixed non-negative integer.");
        return 0;
    }

    private static string ReadRequiredFixedNonNegativeText(
        XElement row,
        string field,
        ICollection<string> issues)
    {
        if (TryReadScalar(row, field, out string text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= 0)
        {
            return text;
        }
        issues.Add($"The factory modification {field} is not one fixed non-negative integer.");
        return string.Empty;
    }

    private static string ReadRequiredFixedNonNegativeDecimalText(
        XElement row,
        string field,
        ICollection<string> issues)
    {
        if (TryReadScalar(row, field, out string text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && value >= 0m)
        {
            return text;
        }
        issues.Add($"The factory modification {field} is not one fixed non-negative number.");
        return string.Empty;
    }

    private static string ReadOptionalFixedCapacityText(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return string.Empty;
        if (nodes.Length == 1 && TryReadScalarElement(nodes[0], out string text) && IsFixedCapacity(text))
            return text;
        issues.Add($"The factory modification {field} is not one fixed capacity value.");
        return string.Empty;
    }

    private static string ReadOptionalSafeText(
        XElement row,
        string field,
        string fallback,
        ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return fallback;
        if (nodes.Length == 1 && TryReadScalarElement(nodes[0], out string value)
            && value.IndexOfAny(['\0', '\r', '\n']) < 0)
        {
            return value;
        }
        issues.Add($"The optional factory modification {field} text is ambiguous, structured, or unsafe.");
        return fallback;
    }

    private static int ReadOptionalNonNegativeInt(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return 0;
        if (nodes.Length == 1 && TryReadScalarElement(nodes[0], out string text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value) && value >= 0)
        {
            return value;
        }
        issues.Add($"The optional factory modification {field} is not one fixed non-negative integer.");
        return 0;
    }

    private static decimal ReadOptionalNonNegativeDecimal(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return 0m;
        if (nodes.Length == 1 && TryReadScalarElement(nodes[0], out string text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) && value >= 0m)
        {
            return value;
        }
        issues.Add($"The optional factory modification {field} is not one fixed non-negative number.");
        return 0m;
    }

    private static bool ReadOptionalBoolean(XElement row, string field, ICollection<string> issues)
    {
        XElement[] nodes = row.Elements(field).Take(2).ToArray();
        if (nodes.Length == 0)
            return false;
        if (nodes.Length == 1 && TryReadScalarElement(nodes[0], out string text)
            && bool.TryParse(text, out bool value))
        {
            return value;
        }
        issues.Add($"The optional factory modification {field} is not one fixed boolean.");
        return false;
    }

    private static CharacterVehicleWorkshopModificationEntry ProjectModification(
        XElement row,
        SourceIdentity identity)
    {
        var issues = DirectFieldIssues(row, ModificationFields);
        string category = RequiredText(row, "category", issues);
        string page = RequiredText(row, "page", issues);
        CharacterVehicleWorkshopAvailability availability = RequiredAvailability(row, "avail", issues);

        int minimumRating = 0;
        int maximumRating = 0;
        if (!TryReadScalar(row, "rating", out string ratingText)
            || !int.TryParse(ratingText, NumberStyles.None, CultureInfo.InvariantCulture, out maximumRating)
            || maximumRating < 0)
        {
            issues.Add("The modification rating is not a fixed non-negative integer.");
            maximumRating = 0;
        }
        else if (maximumRating > 0)
        {
            minimumRating = OptionalNonNegativeInt(row, "minrating", 1, issues);
            if (minimumRating < 1 || minimumRating > maximumRating)
            {
                issues.Add("The modification minimum rating is outside its fixed source range.");
                minimumRating = Math.Min(1, maximumRating);
            }
        }
        else if (row.Elements("minrating").Any())
        {
            issues.Add("A rating-zero modification must not declare a separate minimum rating.");
        }

        ReadAffineDecimal(row, "cost", issues, out decimal baseCost, out decimal costPerRating);
        ReadAffineInt(row, "slots", issues, out int baseSlots, out int slotsPerRating);
        ReadOptionalAffineInt(row, "capacity", issues, out int baseCapacity, out int capacityPerRating);
        CharacterVehicleWorkshopProjectionStatus status = Status(issues);
        return new CharacterVehicleWorkshopModificationEntry(
            new CharacterVehicleModificationSourceId(identity.Id),
            identity.Name,
            category,
            minimumRating,
            maximumRating,
            baseCost,
            costPerRating,
            baseSlots,
            slotsPerRating,
            baseCapacity,
            capacityPerRating,
            availability,
            identity.SourceBook,
            page,
            AllowedChassis: [],
            status,
            UnsupportedReason(issues));
    }

    private static CharacterVehicleWeaponMountComponentEntry ProjectWeaponMount(
        XElement row,
        SourceIdentity identity)
    {
        var issues = DirectFieldIssues(row, WeaponMountFields);
        string page = RequiredText(row, "page", issues);
        string category = RequiredText(row, "category", issues);
        CharacterVehicleWeaponMountComponentKind kind = category switch
        {
            "Size" => CharacterVehicleWeaponMountComponentKind.Size,
            "Visibility" => CharacterVehicleWeaponMountComponentKind.Visibility,
            "Flexibility" => CharacterVehicleWeaponMountComponentKind.Flexibility,
            "Control" => CharacterVehicleWeaponMountComponentKind.Control,
            _ => CharacterVehicleWeaponMountComponentKind.Size
        };
        if (category is not ("Size" or "Visibility" or "Flexibility" or "Control"))
            issues.Add("The weapon-mount category is not Size, Visibility, Flexibility, or Control.");
        decimal cost = RequiredNonNegativeDecimal(row, "cost", issues);
        int slots = RequiredNonNegativeInt(row, "slots", issues);
        int capacity = OptionalNonNegativeInt(row, "capacity", 0, issues);
        CharacterVehicleWorkshopAvailability availability = RequiredAvailability(row, "avail", issues);
        CharacterVehicleWorkshopProjectionStatus status = Status(issues);
        return new CharacterVehicleWeaponMountComponentEntry(
            new CharacterVehicleWeaponMountComponentSourceId(identity.Id),
            kind,
            identity.Name,
            cost,
            slots,
            capacity,
            availability,
            identity.SourceBook,
            page,
            AllowedChassis: [],
            RequiredComponents: [],
            ForbiddenComponents: [],
            status,
            UnsupportedReason(issues));
    }

    private static List<string> DirectFieldIssues(XElement row, IReadOnlySet<string> supportedFields)
    {
        string[] unsupported = row.Elements()
            .Select(element => element.Name.LocalName)
            .Where(name => !supportedFields.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] duplicates = row.Elements()
            .Where(element => supportedFields.Contains(element.Name.LocalName))
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .Where(group => group.Count() != 1)
            .Select(group => group.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var issues = new List<string>();
        if (unsupported.Length != 0)
        {
            issues.Add("Source fields are not yet projected losslessly: "
                + string.Join(", ", unsupported) + ".");
        }
        if (duplicates.Length != 0)
        {
            issues.Add("Source fields are ambiguous because they occur more than once: "
                + string.Join(", ", duplicates) + ".");
        }
        return issues;
    }

    private static void ReadPair(
        XElement row,
        string field,
        ICollection<string> issues,
        out int primary,
        out int alternate)
    {
        primary = 0;
        alternate = 0;
        if (!TryReadScalar(row, field, out string text))
        {
            issues.Add($"The source {field} value is missing or ambiguous.");
            return;
        }
        string[] pieces = text.Split('/', StringSplitOptions.None);
        if (pieces.Length is < 1 or > 2
            || pieces.Any(piece => !int.TryParse(
                piece,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int parsed) || parsed < 0))
        {
            issues.Add($"The source {field} value is not a fixed non-negative value or pair.");
            return;
        }
        primary = int.Parse(pieces[0], NumberStyles.None, CultureInfo.InvariantCulture);
        alternate = pieces.Length == 2
            ? int.Parse(pieces[1], NumberStyles.None, CultureInfo.InvariantCulture)
            : primary;
    }

    private static int RequiredNonNegativeInt(XElement row, string field, ICollection<string> issues)
    {
        if (TryReadScalar(row, field, out string text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= 0)
        {
            return value;
        }
        issues.Add($"The source {field} value is not one fixed non-negative integer.");
        return 0;
    }

    private static int OptionalNonNegativeInt(
        XElement row,
        string field,
        int fallback,
        ICollection<string> issues)
    {
        XElement[] matches = row.Elements(field).Take(2).ToArray();
        if (matches.Length == 0)
            return fallback;
        if (matches.Length == 1
            && TryReadScalarElement(matches[0], out string text)
            && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= 0)
        {
            return value;
        }
        issues.Add($"The optional source {field} value is not one fixed non-negative integer.");
        return fallback;
    }

    private static decimal RequiredNonNegativeDecimal(
        XElement row,
        string field,
        ICollection<string> issues)
    {
        if (TryReadScalar(row, field, out string text)
            && decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value)
            && value >= 0m)
        {
            return value;
        }
        issues.Add($"The source {field} value is not one fixed non-negative number.");
        return 0m;
    }

    private static string RequiredText(XElement row, string field, ICollection<string> issues)
    {
        if (TryReadScalar(row, field, out string text) && ValidText(text))
            return text;
        issues.Add($"The source {field} text is missing, ambiguous, or unsafe.");
        return string.Empty;
    }

    private static CharacterVehicleWorkshopAvailability RequiredAvailability(
        XElement row,
        string field,
        ICollection<string> issues)
    {
        if (!TryReadScalar(row, field, out string text))
        {
            issues.Add("The source availability is missing or ambiguous.");
            return new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false);
        }
        bool addToParent = text.StartsWith('+');
        string remainder = addToParent ? text[1..] : text;
        CharacterVehicleWorkshopLegality legality = remainder.EndsWith('F')
            ? CharacterVehicleWorkshopLegality.Forbidden
            : remainder.EndsWith('R')
                ? CharacterVehicleWorkshopLegality.Restricted
                : CharacterVehicleWorkshopLegality.Legal;
        if (legality != CharacterVehicleWorkshopLegality.Legal)
            remainder = remainder[..^1];
        if (int.TryParse(remainder, NumberStyles.None, CultureInfo.InvariantCulture, out int value)
            && value >= 0)
        {
            return new CharacterVehicleWorkshopAvailability(value, legality, addToParent);
        }
        issues.Add("The source availability is not a fixed non-negative value with an optional R/F suffix.");
        return new CharacterVehicleWorkshopAvailability(0, CharacterVehicleWorkshopLegality.Legal, false);
    }

    private static void ReadAffineDecimal(
        XElement row,
        string field,
        ICollection<string> issues,
        out decimal baseValue,
        out decimal perRating)
    {
        baseValue = 0m;
        perRating = 0m;
        if (!TryReadScalar(row, field, out string text))
        {
            issues.Add($"The source {field} expression is missing or ambiguous.");
            return;
        }
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal fixedValue)
            && fixedValue >= 0m)
        {
            baseValue = fixedValue;
            return;
        }
        if (TryReadRatingFactor(text, out decimal factor))
        {
            perRating = factor;
            return;
        }
        issues.Add($"The source {field} expression is not fixed or a bounded Rating multiplier.");
    }

    private static void ReadAffineInt(
        XElement row,
        string field,
        ICollection<string> issues,
        out int baseValue,
        out int perRating)
    {
        baseValue = 0;
        perRating = 0;
        if (!TryReadScalar(row, field, out string text))
        {
            issues.Add($"The source {field} expression is missing or ambiguous.");
            return;
        }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int fixedValue)
            && fixedValue >= 0)
        {
            baseValue = fixedValue;
            return;
        }
        if (TryReadRatingFactor(text, out decimal factor)
            && decimal.Truncate(factor) == factor
            && factor <= int.MaxValue)
        {
            perRating = (int)factor;
            return;
        }
        issues.Add($"The source {field} expression is not fixed or a bounded integer Rating multiplier.");
    }

    private static void ReadOptionalAffineInt(
        XElement row,
        string field,
        ICollection<string> issues,
        out int baseValue,
        out int perRating)
    {
        if (!row.Elements(field).Any())
        {
            baseValue = 0;
            perRating = 0;
            return;
        }
        ReadAffineInt(row, field, issues, out baseValue, out perRating);
    }

    private static bool TryReadRatingFactor(string text, out decimal factor)
    {
        factor = 0m;
        string[] pieces = text.Split('*', StringSplitOptions.TrimEntries);
        if (pieces.Length != 2)
            return false;
        string number = string.Equals(pieces[0], "Rating", StringComparison.Ordinal)
            ? pieces[1]
            : string.Equals(pieces[1], "Rating", StringComparison.Ordinal)
                ? pieces[0]
                : string.Empty;
        return number.Length != 0
            && decimal.TryParse(number, NumberStyles.Number, CultureInfo.InvariantCulture, out factor)
            && factor >= 0m;
    }

    private static bool TryReadEnabledIdentity(
        XElement row,
        Func<string, bool> isSourceEnabled,
        out SourceIdentity identity,
        out bool enabled)
    {
        identity = default;
        enabled = false;
        if (!TryReadScalar(row, "source", out string sourceBook))
            return false;
        enabled = isSourceEnabled(sourceBook);
        if (!enabled)
            return true;
        if (!TryReadScalar(row, "id", out string idText)
            || !Guid.TryParseExact(idText, "D", out Guid id)
            || id == Guid.Empty
            || !TryReadScalar(row, "name", out string name)
            || !ValidText(name))
        {
            return false;
        }
        identity = new SourceIdentity(id, name, sourceBook);
        return true;
    }

    private static string RowIdText(XElement row)
        => row.Elements("id").Take(1).Select(element => element.Value.Trim()).FirstOrDefault() ?? string.Empty;

    private static bool TryReadScalar(XElement row, string field, out string value)
    {
        value = string.Empty;
        XElement[] matches = row.Elements(field).Take(2).ToArray();
        return matches.Length == 1 && TryReadScalarElement(matches[0], out value);
    }

    private static bool TryReadScalarElement(XElement element, out string value)
    {
        value = element.Value;
        return !element.HasAttributes
            && !element.HasElements
            && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static bool ValidText(string value)
        => !string.IsNullOrWhiteSpace(value)
            && string.Equals(value, value.Trim(), StringComparison.Ordinal)
            && value.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static CharacterVehicleWorkshopProjectionStatus Status(IReadOnlyCollection<string> issues)
        => issues.Count == 0
            ? CharacterVehicleWorkshopProjectionStatus.Exact
            : CharacterVehicleWorkshopProjectionStatus.Unsupported;

    private static string UnsupportedReason(IEnumerable<string> issues)
        => string.Join(" ", issues.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal));

    private static CharacterVehicleWorkshopCatalog EmptyCatalog()
        => new(
            new CharacterVehicleWorkshopSourceBinding(
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, false, 0m, false, 0m, false),
            [], [], [], string.Empty);

    private readonly record struct SourceIdentity(Guid Id, string Name, string SourceBook);

    private sealed record FactoryGearSourceRow(
        SourceIdentity Identity,
        XElement Row,
        string NodeDigest);

    private sealed record FactoryModificationSourceRow(
        SourceIdentity Identity,
        XElement Row,
        string NodeDigest);
}
