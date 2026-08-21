using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public sealed record CharacterWeaponActiveCommlinkSemantics(
    Guid WeaponId,
    Guid MatrixOwnerId,
    string MatrixOwnerKind,
    bool ActiveCommlink,
    bool IsCommlink);

public static class CharacterWeaponActiveCommlinkRules
{
    private static readonly string[] MatrixDeviceElementNames =
    [
        "gear", "armor", "weapon", "cyberware", "vehicle"
    ];

    public static bool TryProject(
        XElement character,
        XElement weapon,
        out CharacterWeaponActiveCommlinkSemantics semantics)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(weapon);
        semantics = new CharacterWeaponActiveCommlinkSemantics(
            Guid.Empty,
            Guid.Empty,
            string.Empty,
            ActiveCommlink: false,
            IsCommlink: false);

        if (!TryReadStableGuid(weapon, out Guid weaponId)
            || !TryReadActiveCommlinkState(character, weapon, out bool activeCommlink)
            || string.IsNullOrWhiteSpace(ReadValue(weapon, "parentid"))
            || !CharacterWeaponMatrixParentResolver.TryResolveOwner(
                character,
                weapon,
                out CharacterMatrixOwner owner)
            || owner.Kind == CharacterMatrixOwnerKind.Weapon
            || !TryReadStableGuid(owner.Item, out Guid ownerId)
            || !CharacterWeaponHomeNodeRules.TryEvaluateOwnerIsCommlink(
                character,
                owner,
                out bool isCommlink))
        {
            return false;
        }

        semantics = new CharacterWeaponActiveCommlinkSemantics(
            weaponId,
            ownerId,
            owner.Kind.ToString(),
            activeCommlink,
            isCommlink);
        return true;
    }

    public static IEnumerable<XElement> EnumerateSavedActiveCommlinks(XElement character)
        => character.DescendantsAndSelf()
            .Where(item => MatrixDeviceElementNames.Contains(item.Name.LocalName, StringComparer.Ordinal))
            .SelectMany(item => item.Elements("active"));

    private static bool TryReadActiveCommlinkState(
        XElement character,
        XElement weapon,
        out bool activeCommlink)
    {
        activeCommlink = false;
        XElement[] targetValues = weapon.Elements("active").Take(2).ToArray();
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
        foreach (XElement node in EnumerateSavedActiveCommlinks(character))
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

    private static bool TryReadStableGuid(XElement item, out Guid id)
        => Guid.TryParseExact(ReadValue(item, "guid"), "D", out id) && id != Guid.Empty;

    private static string ReadValue(XElement item, string name)
        => item.Element(name)?.Value.Trim() ?? string.Empty;
}
