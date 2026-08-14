using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

public enum CharacterMatrixOwnerKind
{
    Gear,
    Armor,
    Weapon,
    Cyberware,
    Vehicle
}

public sealed record CharacterMatrixOwner(
    CharacterMatrixOwnerKind Kind,
    XElement Item);

public static class CharacterWeaponMatrixParentResolver
{
    public static bool TryResolveOwner(
        XElement character,
        XElement weapon,
        out CharacterMatrixOwner owner)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(weapon);
        return TryResolveOwner(
            character,
            weapon,
            new HashSet<XElement>(ReferenceEqualityComparer.Instance),
            out owner);
    }

    private static bool TryResolveOwner(
        XElement character,
        XElement weapon,
        ISet<XElement> visitedWeapons,
        out CharacterMatrixOwner owner)
    {
        owner = new CharacterMatrixOwner(CharacterMatrixOwnerKind.Weapon, weapon);
        if (!visitedWeapons.Add(weapon))
        {
            return false;
        }

        string parentId = ReadValue(weapon, "parentid");
        if (string.IsNullOrEmpty(parentId))
        {
            return true;
        }

        CharacterMatrixOwner[] matches = EnumerateOwners(character)
            .Where(candidate => string.Equals(
                ReadValue(candidate.Item, "guid"),
                parentId,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length == 0)
        {
            // Chummer5 falls back to the weapon's own Matrix state when ParentID is stale.
            return true;
        }
        if (matches.Length != 1)
        {
            return false;
        }

        CharacterMatrixOwner match = matches[0];
        return match.Kind != CharacterMatrixOwnerKind.Weapon
            ? Assign(match, out owner)
            : TryResolveOwner(character, match.Item, visitedWeapons, out owner);
    }

    private static bool Assign(CharacterMatrixOwner value, out CharacterMatrixOwner owner)
    {
        owner = value;
        return true;
    }

    private static IEnumerable<CharacterMatrixOwner> EnumerateOwners(XElement character)
    {
        foreach (XElement gear in EnumerateGearContainer(character.Element("gears")))
        {
            yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
        }

        foreach (XElement armor in character.Element("armors")?.Elements("armor") ?? [])
        {
            yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Armor, armor);
            foreach (XElement gear in EnumerateGearContainer(armor.Element("gears"))
                         .Concat(EnumerateGearContainer(armor.Element("children"))))
            {
                yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
            }
            foreach (XElement armorMod in armor.Element("armormods")?.Elements("armormod") ?? [])
            {
                foreach (XElement gear in EnumerateGearContainer(armorMod.Element("gears")))
                {
                    yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
                }
            }
        }

        foreach (CharacterMatrixOwner owner in EnumerateWeaponContainer(character.Element("weapons")))
        {
            yield return owner;
        }

        foreach (CharacterMatrixOwner owner in EnumerateCyberwareContainer(character.Element("cyberwares")))
        {
            yield return owner;
        }

        foreach (XElement vehicle in character.Element("vehicles")?.Elements("vehicle") ?? [])
        {
            yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Vehicle, vehicle);
            foreach (XElement gear in EnumerateGearContainer(vehicle.Element("gears")))
            {
                yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
            }
            foreach (CharacterMatrixOwner owner in EnumerateWeaponContainer(vehicle.Element("weapons")))
            {
                yield return owner;
            }
            foreach (XElement vehicleMod in vehicle.Element("mods")?.Elements("mod") ?? [])
            {
                foreach (CharacterMatrixOwner owner in EnumerateVehicleModOwners(vehicleMod))
                {
                    yield return owner;
                }
            }
            foreach (XElement mount in vehicle.Element("weaponmounts")?.Elements("weaponmount") ?? [])
            {
                foreach (CharacterMatrixOwner owner in EnumerateWeaponContainer(mount.Element("weapons")))
                {
                    yield return owner;
                }
                foreach (XElement mountMod in mount.Element("mods")?.Elements("mod") ?? [])
                {
                    foreach (CharacterMatrixOwner owner in EnumerateVehicleModOwners(mountMod))
                    {
                        yield return owner;
                    }
                }
            }
        }
    }

    private static IEnumerable<CharacterMatrixOwner> EnumerateVehicleModOwners(XElement vehicleMod)
    {
        foreach (CharacterMatrixOwner owner in EnumerateWeaponContainer(vehicleMod.Element("weapons")))
        {
            yield return owner;
        }
        foreach (CharacterMatrixOwner owner in EnumerateCyberwareContainer(vehicleMod.Element("cyberwares")))
        {
            yield return owner;
        }
    }

    private static IEnumerable<CharacterMatrixOwner> EnumerateWeaponContainer(XElement? container)
    {
        foreach (XElement weapon in container?.Elements("weapon") ?? [])
        {
            yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Weapon, weapon);
            foreach (XElement accessory in weapon.Element("accessories")?.Elements("accessory") ?? [])
            {
                foreach (XElement gear in EnumerateGearContainer(accessory.Element("gears")))
                {
                    yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
                }
            }
            foreach (CharacterMatrixOwner owner in EnumerateWeaponContainer(weapon.Element("underbarrel")))
            {
                yield return owner;
            }
        }
    }

    private static IEnumerable<CharacterMatrixOwner> EnumerateCyberwareContainer(XElement? container)
    {
        foreach (XElement cyberware in container?.Elements("cyberware") ?? [])
        {
            yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Cyberware, cyberware);
            foreach (XElement gear in EnumerateGearContainer(cyberware.Element("gears")))
            {
                yield return new CharacterMatrixOwner(CharacterMatrixOwnerKind.Gear, gear);
            }
            foreach (CharacterMatrixOwner owner in EnumerateCyberwareContainer(cyberware.Element("children")))
            {
                yield return owner;
            }
        }
    }

    private static IEnumerable<XElement> EnumerateGearContainer(XElement? container)
    {
        foreach (XElement gear in container?.Elements("gear") ?? [])
        {
            yield return gear;
            foreach (XElement child in EnumerateGearContainer(gear.Element("children")))
            {
                yield return child;
            }
        }
    }

    private static string ReadValue(XElement element, string name)
        => element.Element(name)?.Value.Trim() ?? string.Empty;
}
