using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterWeaponHomeNodeSemantics(
    Guid WeaponId,
    Guid MatrixOwnerId,
    string MatrixOwnerKind,
    bool Visible,
    bool Enabled,
    bool HomeNode,
    bool IsCommlink,
    int DeviceRating,
    int ProgramLimit,
    int DepTotal);

public static class CharacterWeaponHomeNodeRules
{
    private static readonly string[] MatrixOwnerElementNames =
    [
        "gear", "armor", "weapon", "cyberware", "vehicle"
    ];

    public static bool TryProject(
        XElement character,
        XElement weapon,
        out CharacterWeaponHomeNodeSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(weapon);
        semantics = new CharacterWeaponHomeNodeSemantics(
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            Visible: false,
            Enabled: false,
            HomeNode: false,
            IsCommlink: false,
            DeviceRating: 0,
            ProgramLimit: 0,
            DepTotal: 0);

        if (!TryReadStableGuid(weapon, out Guid weaponId)
            || !TryReadHomeNodeState(character, weapon, out bool homeNode)
            || !TryReadIsAi(character, out bool isAi))
        {
            return false;
        }

        if (!isAi)
        {
            semantics = semantics with
            {
                WeaponId = weaponId,
                HomeNode = homeNode
            };
            return true;
        }

        if (!TryReadAttributeTotal(character, "DEP", out int depTotal)
            || string.IsNullOrWhiteSpace(ReadValue(weapon, "parentid"))
            || !CharacterWeaponMatrixParentResolver.TryResolveOwner(character, weapon, out CharacterMatrixOwner owner)
            || owner.Kind == CharacterMatrixOwnerKind.Weapon
            || !TryReadStableGuid(owner.Item, out Guid ownerId)
            || !TryEvaluateOwner(
                character,
                owner,
                out bool isCommlink,
                out int deviceRating,
                out int programLimit))
        {
            return false;
        }

        int requiredProgramLimit = depTotal > deviceRating ? 2 : 1;
        semantics = new CharacterWeaponHomeNodeSemantics(
            WeaponId: weaponId,
            MatrixOwnerId: ownerId,
            MatrixOwnerKind: owner.Kind.ToString(),
            Visible: true,
            Enabled: isCommlink && programLimit >= requiredProgramLimit,
            HomeNode: homeNode,
            IsCommlink: isCommlink,
            DeviceRating: deviceRating,
            ProgramLimit: programLimit,
            DepTotal: depTotal);
        return true;
    }

    private static bool TryEvaluateOwner(
        XElement character,
        CharacterMatrixOwner owner,
        out bool isCommlink,
        out int deviceRating,
        out int programLimit)
    {
        isCommlink = false;
        deviceRating = 0;
        programLimit = 0;
        if (!TryReadSavedAttributeTotals(character, out IReadOnlyDictionary<string, int> savedAttributes)
            || !TryReadOverclockerEnabled(character, out bool overclockerEnabled))
        {
            return false;
        }

        return owner.Kind switch
        {
            CharacterMatrixOwnerKind.Gear => TryEvaluateGear(
                owner.Item,
                savedAttributes,
                overclockerEnabled,
                out isCommlink,
                out deviceRating,
                out programLimit),
            CharacterMatrixOwnerKind.Armor => TryEvaluateArmor(
                owner.Item,
                savedAttributes,
                overclockerEnabled,
                out isCommlink,
                out deviceRating,
                out programLimit),
            CharacterMatrixOwnerKind.Cyberware => TryEvaluateCyberware(
                owner.Item,
                savedAttributes,
                overclockerEnabled,
                out isCommlink,
                out deviceRating,
                out programLimit),
            CharacterMatrixOwnerKind.Vehicle => TryEvaluateVehicle(
                owner.Item,
                savedAttributes,
                overclockerEnabled,
                out isCommlink,
                out deviceRating,
                out programLimit),
            _ => false
        };
    }

    private static bool TryEvaluateGear(
        XElement gear,
        IReadOnlyDictionary<string, int> savedAttributes,
        bool overclockerEnabled,
        out bool isCommlink,
        out int deviceRating,
        out int programLimit)
    {
        isCommlink = ReadValue(gear, "canformpersona").Contains("Self", StringComparison.Ordinal)
            || (gear.Element("children")?.Elements("gear") ?? [])
                .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
        deviceRating = 0;
        programLimit = 0;
        if (string.Equals(ReadValue(gear, "name"), "Living Persona", StringComparison.Ordinal)
            || !TryReadOptionalInt(gear, "rating", out int rating))
        {
            return false;
        }

        string deviceExpression = ReadValue(gear, "devicerating");
        if (string.IsNullOrWhiteSpace(deviceExpression))
        {
            deviceExpression = isCommlink ? "2" : "0";
        }
        if (!TryEvaluateExpression(deviceExpression, rating, savedAttributes, out int baseDeviceRating)
            || !TryApplyOverclocker(
                baseDeviceRating,
                ReadValue(gear, "overclocked"),
                "Device Rating",
                overclockerEnabled,
                out deviceRating))
        {
            return false;
        }

        string programExpression = ReadValue(gear, "programlimit");
        if (string.IsNullOrWhiteSpace(programExpression))
        {
            programExpression = isCommlink
                ? FirstNonBlank(ReadValue(gear, "devicerating"), "2")
                : "0";
        }
        return TryEvaluateExpression(programExpression, rating, savedAttributes, out int baseProgramLimit)
            && TryApplyOverclocker(
                baseProgramLimit,
                ReadValue(gear, "overclocked"),
                "Program Limit",
                overclockerEnabled,
                out programLimit);
    }

    private static bool TryEvaluateArmor(
        XElement armor,
        IReadOnlyDictionary<string, int> savedAttributes,
        bool overclockerEnabled,
        out bool isCommlink,
        out int deviceRating,
        out int programLimit)
    {
        isCommlink = ReadValue(armor, "canformpersona").Contains("Self", StringComparison.Ordinal)
            || (armor.Element("gears")?.Elements("gear") ?? [])
                .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
        deviceRating = 0;
        programLimit = 0;
        if (!TryReadOptionalInt(armor, "rating", out int rating))
        {
            return false;
        }

        string deviceExpression = FirstNonBlank(ReadValue(armor, "devicerating"), "2");
        if (!TryEvaluateExpression(deviceExpression, rating, savedAttributes, out int baseDeviceRating)
            || !TryApplyOverclocker(
                baseDeviceRating,
                ReadValue(armor, "overclocked"),
                "Device Rating",
                overclockerEnabled,
                out deviceRating))
        {
            return false;
        }

        string programExpression = ReadValue(armor, "programlimit");
        if (string.IsNullOrWhiteSpace(programExpression))
        {
            programExpression = isCommlink
                ? FirstNonBlank(ReadValue(armor, "devicerating"), "2")
                : "0";
        }
        return TryEvaluateExpression(programExpression, rating, savedAttributes, out int baseProgramLimit)
            && TryApplyOverclocker(
                baseProgramLimit,
                ReadValue(armor, "overclocked"),
                "Program Limit",
                overclockerEnabled,
                out programLimit);
    }

    private static bool TryEvaluateCyberware(
        XElement cyberware,
        IReadOnlyDictionary<string, int> savedAttributes,
        bool overclockerEnabled,
        out bool isCommlink,
        out int deviceRating,
        out int programLimit)
    {
        isCommlink = false;
        deviceRating = 0;
        programLimit = 0;
        if (!TryReadOptionalInt(cyberware, "rating", out int rating)
            || string.IsNullOrWhiteSpace(ReadValue(cyberware, "devicerating"))
            || !TryEvaluateExpression(
                ReadValue(cyberware, "devicerating"),
                rating,
                savedAttributes,
                out int baseDeviceRating)
            || !TryApplyOverclocker(
                baseDeviceRating,
                ReadValue(cyberware, "overclocked"),
                "Device Rating",
                overclockerEnabled,
                out deviceRating))
        {
            return false;
        }

        bool childFormsParent = (cyberware.Element("gears")?.Elements("gear") ?? [])
                .Concat(cyberware.Element("children")?.Elements("cyberware") ?? [])
                .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
        isCommlink = ReadValue(cyberware, "canformpersona").Contains("Self", StringComparison.Ordinal)
            || childFormsParent && deviceRating > 0;
        string programExpression = ReadValue(cyberware, "programlimit");
        if (string.IsNullOrWhiteSpace(programExpression))
        {
            programExpression = isCommlink ? ReadValue(cyberware, "devicerating") : "0";
        }
        return TryEvaluateExpression(programExpression, rating, savedAttributes, out int baseProgramLimit)
            && TryApplyOverclocker(
                baseProgramLimit,
                ReadValue(cyberware, "overclocked"),
                "Program Limit",
                overclockerEnabled,
                out programLimit);
    }

    private static bool TryEvaluateVehicle(
        XElement vehicle,
        IReadOnlyDictionary<string, int> savedAttributes,
        bool overclockerEnabled,
        out bool isCommlink,
        out int deviceRating,
        out int programLimit)
    {
        isCommlink = false;
        deviceRating = 0;
        programLimit = 0;
        // Chummer5 resolves vehicle-mod Matrix bonuses from source data when they are not
        // embedded in the save. Without that authority the phone must fail closed.
        if (vehicle.Element("mods")?.Elements("mod").Any() == true)
        {
            return false;
        }

        string deviceExpression = FirstNonBlank(
            ReadValue(vehicle, "devicerating"),
            ReadValue(vehicle, "pilot"));
        if (string.IsNullOrWhiteSpace(deviceExpression)
            || !TryEvaluateExpression(deviceExpression, rating: 0, savedAttributes, out int baseDeviceRating)
            || !TryApplyOverclocker(
                baseDeviceRating,
                ReadValue(vehicle, "overclocked"),
                "Device Rating",
                overclockerEnabled,
                out deviceRating))
        {
            return false;
        }

        isCommlink = (vehicle.Element("gears")?.Elements("gear") ?? [])
                .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal))
            && deviceRating > 0;
        string programExpression = FirstNonBlank(
            ReadValue(vehicle, "programlimit"),
            ReadValue(vehicle, "devicerating"),
            ReadValue(vehicle, "pilot"));
        return TryEvaluateExpression(programExpression, rating: 0, savedAttributes, out int baseProgramLimit)
            && TryApplyOverclocker(
                baseProgramLimit,
                ReadValue(vehicle, "overclocked"),
                "Program Limit",
                overclockerEnabled,
                out programLimit);
    }

    private static bool TryEvaluateExpression(
        string expression,
        int rating,
        IReadOnlyDictionary<string, int> savedAttributes,
        out int value)
    {
        value = 0;
        return !expression.Contains("FixedValues", StringComparison.Ordinal)
            && !expression.Contains("{Children ", StringComparison.Ordinal)
            && !expression.Contains("{Parent ", StringComparison.Ordinal)
            && !expression.Contains("{Gear ", StringComparison.Ordinal)
            && CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
                expression,
                rating,
                savedAttributes,
                out value);
    }

    private static bool TryApplyOverclocker(
        int baseValue,
        string overclockedAttribute,
        string expectedAttribute,
        bool overclockerEnabled,
        out int value)
    {
        value = baseValue;
        if (!overclockerEnabled
            || !string.Equals(overclockedAttribute, expectedAttribute, StringComparison.Ordinal))
        {
            return true;
        }
        if (baseValue == int.MaxValue)
        {
            return false;
        }
        value++;
        return true;
    }

    private static bool TryReadIsAi(XElement character, out bool isAi)
    {
        isAi = false;
        if (!TryReadOptionalBool(character, "depenabled", out bool depEnabled))
        {
            return false;
        }
        if (!depEnabled)
        {
            return true;
        }

        XElement[] bodies = (character.Element("attributes")?.Elements("attribute") ?? [])
            .Where(attribute => string.Equals(ReadValue(attribute, "name"), "BOD", StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (bodies.Length > 1)
        {
            return false;
        }
        if (bodies.Length == 0)
        {
            isAi = true;
            return true;
        }
        if (!TryReadOptionalInt(bodies[0], "metatypemax", out int metatypeMaximum))
        {
            return false;
        }
        isAi = metatypeMaximum == 0;
        return true;
    }

    private static bool TryReadAttributeTotal(XElement character, string name, out int total)
    {
        total = 0;
        XElement[] matches = (character.Element("attributes")?.Elements("attribute") ?? [])
            .Where(attribute => string.Equals(ReadValue(attribute, "name"), name, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            && int.TryParse(
                ReadValue(matches[0], "totalvalue"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out total);
    }

    private static bool TryReadSavedAttributeTotals(
        XElement character,
        out IReadOnlyDictionary<string, int> totals)
    {
        Dictionary<string, int> parsed = new(StringComparer.Ordinal);
        foreach (XElement attribute in character.Element("attributes")?.Elements("attribute") ?? [])
        {
            string name = ReadValue(attribute, "name");
            string totalText = ReadValue(attribute, "totalvalue");
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(totalText))
            {
                continue;
            }
            if (!int.TryParse(totalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)
                || !parsed.TryAdd(name, total))
            {
                totals = new Dictionary<string, int>();
                return false;
            }
        }
        totals = parsed;
        return true;
    }

    private static bool TryReadOverclockerEnabled(XElement character, out bool enabled)
    {
        enabled = false;
        if (!TryReadOptionalBool(character, "created", out bool careerMode))
        {
            return false;
        }
        foreach (XElement improvement in character.Element("improvements")?.Elements("improvement") ?? [])
        {
            if (!string.Equals(
                    ReadValue(improvement, "improvementttype"),
                    "Overclocker",
                    StringComparison.Ordinal)
                || ReadLegacyIntegerFlag(improvement, "enabled", 1) <= 0
                || ReadLegacyIntegerFlag(improvement, "addtorating", 0) > 0)
            {
                continue;
            }
            string condition = ReadValue(improvement, "condition");
            if (string.IsNullOrEmpty(condition)
                || string.Equals(condition, careerMode ? "career" : "create", StringComparison.Ordinal))
            {
                enabled = true;
                return true;
            }
        }
        return true;
    }

    private static int ReadLegacyIntegerFlag(XElement item, string name, int defaultValue)
    {
        string value = ReadValue(item, name);
        if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out int parsed))
        {
            return parsed;
        }
        return bool.TryParse(value, out bool boolean) ? boolean ? 1 : 0 : defaultValue;
    }

    private static bool TryReadHomeNodeState(XElement character, XElement weapon, out bool homeNode)
    {
        homeNode = false;
        XElement[] targetValues = weapon.Elements("homenode").Take(2).ToArray();
        if (targetValues.Length > 1
            || targetValues.Length == 1 && !bool.TryParse(targetValues[0].Value, out homeNode))
        {
            return false;
        }

        int selectedCount = 0;
        foreach (XElement node in EnumerateSavedHomeNodes(character))
        {
            if (!bool.TryParse(node.Value, out bool selected))
            {
                return false;
            }
            if (selected && ++selectedCount > 1)
            {
                return false;
            }
        }
        return true;
    }

    public static IEnumerable<XElement> EnumerateSavedHomeNodes(XElement character)
        => character.DescendantsAndSelf()
            .Where(item => MatrixOwnerElementNames.Contains(item.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(item => item.Elements("homenode"));

    private static bool TryReadStableGuid(XElement item, out Guid id)
        => Guid.TryParseExact(ReadValue(item, "guid"), "D", out id) && id != Guid.Empty;

    private static bool TryReadOptionalInt(XElement item, string name, out int value)
    {
        string raw = ReadValue(item, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = 0;
            return true;
        }
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalBool(XElement item, string name, out bool value)
    {
        string raw = ReadValue(item, name);
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = false;
            return true;
        }
        return bool.TryParse(raw, out value);
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string ReadValue(XElement item, string name)
        => item.Element(name)?.Value.Trim() ?? string.Empty;
}
