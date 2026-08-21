using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterGearActiveCommlinkSemantics(
    Guid GearId,
    bool ActiveCommlink,
    bool IsCommlink);

public static class CharacterGearActiveCommlinkRules
{
    private static readonly string[] MatrixDeviceElementNames =
    [
        "gear", "armor", "weapon", "cyberware", "vehicle"
    ];

    public static bool TryProject(
        XElement character,
        XElement gear,
        out CharacterGearActiveCommlinkSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(gear);
        semantics = new CharacterGearActiveCommlinkSemantics(
            Guid.Empty,
            ActiveCommlink: false,
            IsCommlink: false);

        if (!TryReadUniqueStableGuid(character, gear, out Guid gearId)
            || !TryReadActiveCommlinkState(character, gear, out bool activeCommlink))
        {
            return false;
        }

        bool isCommlink = ReadValue(gear, "canformpersona").Contains("Self", StringComparison.Ordinal)
            || (gear.Element("children")?.Elements("gear") ?? [])
                .Any(child => ReadValue(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
        semantics = new CharacterGearActiveCommlinkSemantics(
            gearId,
            activeCommlink,
            isCommlink);
        return true;
    }

    private static bool TryReadUniqueStableGuid(XElement character, XElement gear, out Guid gearId)
    {
        gearId = Guid.Empty;
        if (!Guid.TryParseExact(ReadValue(gear, "guid"), "D", out Guid candidate)
            || candidate == Guid.Empty)
        {
            return false;
        }

        int matchingIds = character.DescendantsAndSelf()
            .Where(item => item.Name.LocalName == "gear")
            .Count(item => Guid.TryParseExact(ReadValue(item, "guid"), "D", out Guid parsed)
                && parsed == candidate);
        if (matchingIds != 1)
        {
            return false;
        }

        gearId = candidate;
        return true;
    }

    private static bool TryReadActiveCommlinkState(
        XElement character,
        XElement gear,
        out bool activeCommlink)
    {
        activeCommlink = false;
        XElement[] targetValues = gear.Elements("active").Take(2).ToArray();
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

    public static IEnumerable<XElement> EnumerateSavedActiveCommlinks(XElement character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.DescendantsAndSelf()
            .Where(item => MatrixDeviceElementNames.Contains(item.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(item => item.Elements("active"));
    }

    private static string ReadValue(XElement item, string name)
        => item.Element(name)?.Value.Trim() ?? string.Empty;
}
