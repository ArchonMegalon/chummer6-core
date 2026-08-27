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
        "cost", "handling", "pilot", "sensor", "speed", "seats", "addslots", "modslots"
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
        string overlayDigest,
        bool droneMods,
        bool multiplyRestrictedCost,
        decimal restrictedCostMultiplier,
        bool multiplyForbiddenCost,
        decimal forbiddenCostMultiplier,
        IReadOnlyList<XElement> vehicleRows,
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
            || !CharacterVehicleWorkshopRules.IsCanonicalDigest(overlayDigest)
            || restrictedCostMultiplier < 0m
            || forbiddenCostMultiplier < 0m
            || multiplyRestrictedCost && restrictedCostMultiplier <= 0m
            || multiplyForbiddenCost && forbiddenCostMultiplier <= 0m
            || vehicleRows is null
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

        foreach (XElement row in vehicleRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!chassisIds.Add(identity.Id))
                return false;
            chassis.Add(ProjectChassis(row, identity, droneMods));
        }

        foreach (XElement row in modificationRows.OrderBy(RowIdText, StringComparer.Ordinal))
        {
            if (!TryReadEnabledIdentity(row, isSourceEnabled, out SourceIdentity identity, out bool enabled))
                return false;
            if (!enabled)
                continue;
            if (!modificationIds.Add(identity.Id))
                return false;
            modifications.Add(ProjectModification(row, identity));
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
        bool droneMods)
    {
        var issues = DirectFieldIssues(row, ChassisFields);
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
            UnsupportedReason(issues));
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
                string.Empty, string.Empty, false, 0m, false, 0m, false),
            [], [], [], string.Empty);

    private readonly record struct SourceIdentity(Guid Id, string Name, string SourceBook);
}
