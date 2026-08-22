using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public enum CharacterVehicleActiveCommlinkPhase { Creation, Career }

public sealed record CharacterVehicleActiveCommlinkEconomics(decimal NuyenDelta, int KarmaDelta);

public sealed record CharacterVehicleActiveCommlinkSemantics(
    Guid VehicleId,
    CharacterVehicleActiveCommlinkPhase Phase,
    bool ActiveCommlink,
    bool IsCommlink,
    bool Visible,
    bool Enabled,
    CharacterVehicleActiveCommlinkEconomics Economics);

/// <summary>
/// Exact saved-data authority for the top-level Vehicle selected by the legacy
/// chkVehicleActiveCommlink handler. The desktop handler accepts any
/// IHasMatrixAttributes tree node; this bounded authority deliberately rejects
/// descendants until each descendant kind has its own typed identity contract.
/// </summary>
public static class CharacterVehicleActiveCommlinkRules
{
    private static readonly string[] MatrixDeviceElementNames =
    [
        "gear", "armor", "weapon", "cyberware", "vehicle"
    ];

    public static bool TryProject(
        XElement character,
        XElement vehicle,
        bool created,
        out CharacterVehicleActiveCommlinkSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(vehicle);
        semantics = Unavailable();

        if (!IsTopLevelVehicle(character, vehicle)
            || !TryReadUniqueStableGuid(character, vehicle, out Guid vehicleId)
            || !TryReadActiveCommlinkState(character, vehicle, out bool activeCommlink)
            || !CharacterWeaponHomeNodeRules.TryEvaluateOwnerIsCommlink(
                character,
                new CharacterMatrixOwner(CharacterMatrixOwnerKind.Vehicle, vehicle),
                out bool isCommlink))
        {
            return false;
        }

        CharacterVehicleActiveCommlinkPhase phase = created
            ? CharacterVehicleActiveCommlinkPhase.Career
            : CharacterVehicleActiveCommlinkPhase.Creation;
        semantics = new CharacterVehicleActiveCommlinkSemantics(
            vehicleId,
            phase,
            activeCommlink,
            isCommlink,
            Visible: isCommlink,
            Enabled: isCommlink,
            new CharacterVehicleActiveCommlinkEconomics(0m, 0));
        return true;
    }

    public static IEnumerable<XElement> EnumerateSavedActiveCommlinks(XElement character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.DescendantsAndSelf()
            .Where(item => MatrixDeviceElementNames.Contains(item.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(item => item.Elements("active"));
    }

    private static bool IsTopLevelVehicle(XElement character, XElement vehicle)
        => vehicle.Name.LocalName == "vehicle"
            && vehicle.Parent is { Name.LocalName: "vehicles" } vehicles
            && ReferenceEquals(vehicles.Parent, character)
            && character.Name.LocalName == "character";

    private static bool TryReadUniqueStableGuid(
        XElement character,
        XElement vehicle,
        out Guid vehicleId)
    {
        vehicleId = Guid.Empty;
        if (!Guid.TryParseExact(ReadValue(vehicle, "guid"), "D", out Guid candidate)
            || candidate == Guid.Empty)
        {
            return false;
        }

        int matchingIds = character.DescendantsAndSelf()
            .Where(item => item.Name.LocalName == "vehicle")
            .Count(item => Guid.TryParseExact(ReadValue(item, "guid"), "D", out Guid parsed)
                && parsed == candidate);
        if (matchingIds != 1)
        {
            return false;
        }

        vehicleId = candidate;
        return true;
    }

    private static bool TryReadActiveCommlinkState(
        XElement character,
        XElement vehicle,
        out bool activeCommlink)
    {
        activeCommlink = false;
        XElement[] targetValues = vehicle.Elements("active").Take(2).ToArray();
        if (targetValues.Length > 1
            || targetValues.Length == 1 && !bool.TryParse(targetValues[0].Value, out activeCommlink))
        {
            return false;
        }

        XElement[] matrixDevices = character.DescendantsAndSelf()
            .Where(item => MatrixDeviceElementNames.Contains(item.Name.LocalName, StringComparer.Ordinal))
            .ToArray();
        if (matrixDevices.Any(device => device.Elements("active").Take(2).Count() > 1))
        {
            return false;
        }

        int selectedCount = 0;
        foreach (XElement active in EnumerateSavedActiveCommlinks(character))
        {
            if (!bool.TryParse(active.Value, out bool selected)
                || selected && ++selectedCount > 1)
            {
                return false;
            }
        }
        return true;
    }

    private static CharacterVehicleActiveCommlinkSemantics Unavailable() => new(
        Guid.Empty,
        CharacterVehicleActiveCommlinkPhase.Creation,
        ActiveCommlink: false,
        IsCommlink: false,
        Visible: false,
        Enabled: false,
        new CharacterVehicleActiveCommlinkEconomics(0m, 0));

    private static string ReadValue(XElement item, string name)
        => item.Element(name)?.Value.Trim() ?? string.Empty;
}
