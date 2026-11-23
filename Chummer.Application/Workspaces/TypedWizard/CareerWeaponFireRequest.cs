// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces.TypedWizard;

internal sealed record CareerWeaponFireEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    CharacterWeaponFireState Weapon);

/// <summary>
/// Exact direct Career Weapons that can be projected with the canonical firing authority.
/// Unsupported, descendant, and ambiguous weapons never become generic targets.
/// </summary>
internal sealed record CareerWeaponFireCatalogEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    IReadOnlyList<CareerWeaponFireEditorState> Weapons);

internal sealed record CareerWeaponFireRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterWeaponFireIdentity Identity,
    string ExpectedNodeRevision,
    CharacterWeaponFireMode Mode,
    bool ConfirmedPartial);

internal sealed record CareerWeaponFireProjection(
    CharacterWeaponFireState State,
    XElement Weapon,
    XElement? Clip,
    XElement? AmmoGear);

internal static class CareerWeaponFireEditorProjector
{
    private const int MaximumDirectWeapons = 512;

    public static CareerWeaponFireEditorState Project(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        Guid weaponId)
    {
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "Dossier revision is unavailable. Reload before firing the Weapon.");
        }

        return new(workspaceId, contentRevision, ProjectValue(xml, weaponId).State);
    }

    public static CareerWeaponFireCatalogEditorState ProjectCatalog(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        if (string.IsNullOrWhiteSpace(workspaceId.Value) || contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "Career Weapon firing requires an exact saved runner identity and revision.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root is { Name.LocalName: "character" }
            ? document.Root
            : throw new InvalidOperationException("Workspace XML must use <character> as the root node.");
        XElement[] containers = root.Elements("weapons").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException(
                "Career Weapon firing requires exactly one <weapons> container.");
        }

        XElement[] directWeapons = containers[0].Elements("weapon").ToArray();
        if (directWeapons.Length > MaximumDirectWeapons)
            throw new InvalidOperationException("The direct Career Weapon catalog exceeds its bound.");

        var identities = new List<Guid>(directWeapons.Length);
        foreach (XElement weapon in directWeapons)
        {
            if (!TryReadGuid(weapon, "guid", out Guid weaponId)
                || weaponId == Guid.Empty
                || identities.Contains(weaponId)
                || root.Descendants("weapon").Count(candidate =>
                    TryReadGuid(candidate, "guid", out Guid candidateId)
                    && candidateId == weaponId) != 1)
            {
                throw new InvalidOperationException(
                    "A direct Career Weapon identity is missing, duplicate, or ambiguous.");
            }
            identities.Add(weaponId);
        }

        var eligible = new List<CareerWeaponFireEditorState>(identities.Count);
        foreach (Guid weaponId in identities)
        {
            try
            {
                CharacterWeaponFireState state = ProjectValue(xml, weaponId).State;
                eligible.Add(new CareerWeaponFireEditorState(workspaceId, contentRevision, state));
            }
            catch (InvalidOperationException)
            {
                // The single-target projector is the authority boundary. A direct Weapon with
                // unsupported modes, bonuses, clips, or ammunition simply has no table action.
            }
        }
        return new CareerWeaponFireCatalogEditorState(
            workspaceId,
            contentRevision,
            eligible);
    }

    internal static CareerWeaponFireProjection ProjectValue(string xml, Guid weaponId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        if (weaponId == Guid.Empty)
        {
            throw new InvalidOperationException("Weapon firing requires a stable Weapon Guid.");
        }

        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root is { Name.LocalName: "character" }
            ? document.Root
            : throw new InvalidOperationException("Workspace XML must use <character> as the root node.");
        XElement weapon = FindWeaponRoot(root, weaponId);
        int activeAmmoSlot = ReadPositiveInteger(weapon, "activeammoslot");
        int baseAmmoSlots = ReadPositiveInteger(weapon, "ammoslots");
        if (activeAmmoSlot > baseAmmoSlots)
        {
            throw new InvalidOperationException(
                "Accessory-provided active Weapon clips require the full Chummer5 runtime.");
        }

        XElement[] clipContainers = weapon.Elements("clips").Take(2).ToArray();
        if (clipContainers.Length > 1)
        {
            throw new InvalidOperationException("Weapon requires at most one <clips> container.");
        }
        XElement[] clips = clipContainers.Length == 0
            ? []
            : clipContainers[0].Elements("clip").ToArray();
        XElement? clip = activeAmmoSlot <= clips.Length ? clips[activeAmmoSlot - 1] : null;
        int savedAmmoRemaining = clip is null ? 0 : ReadNonNegativeInteger(clip, "count");
        Guid ammoGearId = clip is null ? Guid.Empty : ReadGuid(clip, "id", allowEmpty: true);
        XElement? ammoGear = ammoGearId == Guid.Empty ? null : FindAmmoGear(root, ammoGearId);
        decimal? ammoGearQuantity = ammoGear is null ? null : ReadNonNegativeDecimal(ammoGear, "qty");
        // Core validates the linked stack against the loaded clip below. A stack
        // may retain reserve rounds; it must cover, not equal, the loaded count.
        int ammoRemaining = savedAmmoRemaining;

        XElement[] accessoryContainers = weapon.Elements("accessories").Take(2).ToArray();
        if (accessoryContainers.Length > 1)
        {
            throw new InvalidOperationException("Weapon requires at most one <accessories> container.");
        }
        CharacterWeaponFireAccessorySource[] accessories = accessoryContainers.Length == 0
            ? []
            : accessoryContainers[0].Elements("accessory")
                .Select(ProjectAccessory)
                .ToArray();

        var source = new CharacterWeaponFireSource(
            ReadSingle(weapon, "type"),
            ReadSingle(weapon, "ammo"),
            ReadSingle(weapon, "mode"),
            ReadBoolean(weapon, "allowsingleshot"),
            ReadBoolean(weapon, "allowshortburst"),
            ReadBoolean(weapon, "allowlongburst"),
            ReadBoolean(weapon, "allowfullburst"),
            ReadBoolean(weapon, "allowsuppressive"),
            ReadPositiveInteger(weapon, "singleshot"),
            ReadPositiveInteger(weapon, "shortburst"),
            ReadPositiveInteger(weapon, "longburst"),
            ReadPositiveInteger(weapon, "fullburst"),
            ReadPositiveInteger(weapon, "suppressive"),
            accessories);
        bool unsupportedModeSemantics = HasUnsupportedModeSemantics(weapon, ammoGear);
        bool unsafeAmmoDeletion = ammoGear is not null && !CanDeleteAmmoGearExactly(root, ammoGear, ammoGearId);
        if (!CharacterWeaponFireRules.TryCreateState(
                new CharacterWeaponFireIdentity(weaponId, activeAmmoSlot, ammoGearId),
                ReadBoolean(root, "created"),
                ReadDisplayName(weapon),
                ammoRemaining,
                ammoGearQuantity,
                source,
                unsupportedModeSemantics || unsafeAmmoDeletion,
                out CharacterWeaponFireState state))
        {
            throw new InvalidOperationException(
                "The selected direct Weapon is not an exact, supported Career firing target.");
        }

        return new(state, weapon, clip, ammoGear);
    }

    internal static XElement FindWeaponRoot(XElement root, Guid weaponId)
    {
        XElement[] containers = root.Elements("weapons").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException("Weapon firing requires exactly one <weapons> container.");
        }

        XElement[] directMatches = containers[0].Elements("weapon")
            .Where(candidate => TryReadGuid(candidate, "guid", out Guid id) && id == weaponId)
            .Take(2)
            .ToArray();
        int globalMatches = root.Descendants("weapon")
            .Count(candidate => TryReadGuid(candidate, "guid", out Guid id) && id == weaponId);
        return directMatches.Length == 1 && globalMatches == 1
            ? directMatches[0]
            : throw new InvalidOperationException(
                "Weapon Guid identity is missing, ambiguous, or belongs to a descendant or other owner.");
    }

    internal static XElement FindAmmoGear(XElement root, Guid ammoGearId)
    {
        XElement[] containers = root.Elements("gears").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException("Linked Weapon ammo requires exactly one <gears> container.");
        }
        XElement[] matches = containers[0].Descendants("gear")
            .Where(candidate => TryReadGuid(candidate, "guid", out Guid id) && id == ammoGearId)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("Linked Weapon ammo Gear identity is missing or ambiguous.");
    }

    private static CharacterWeaponFireAccessorySource ProjectAccessory(XElement accessory)
        => new(
            ReadBoolean(accessory, "equipped"),
            ReadSingle(accessory, "firemode"),
            ReadSingle(accessory, "firemodereplace"),
            ReadNonNegativeInteger(accessory, "singleshot"),
            ReadNonNegativeInteger(accessory, "shortburst"),
            ReadNonNegativeInteger(accessory, "longburst"),
            ReadNonNegativeInteger(accessory, "fullburst"),
            ReadNonNegativeInteger(accessory, "suppressive"));

    private static bool HasUnsupportedModeSemantics(XElement weapon, XElement? ammoGear)
    {
        bool weaponWireless = ReadBoolean(weapon, "wirelesson");
        if (weaponWireless && HasModeBonus(weapon, "wirelessweaponbonus"))
        {
            return true;
        }

        XElement? accessories = weapon.Elements("accessories").SingleOrDefault();
        if (accessories is not null)
        {
            foreach (XElement accessory in accessories.Elements("accessory"))
            {
                if (ReadBoolean(accessory, "equipped")
                    && weaponWireless
                    && ReadBoolean(accessory, "wirelesson")
                    && HasModeBonus(accessory, "wirelessweaponbonus"))
                {
                    return true;
                }
            }
        }

        return ammoGear is not null
               && ammoGear.DescendantsAndSelf("gear")
                   .Any(gear => HasModeBonus(gear, "weaponbonus")
                                || HasModeBonus(gear, "flechetteweaponbonus"));
    }

    private static bool HasModeBonus(XElement parent, string bonusName)
    {
        XElement[] bonuses = parent.Elements(bonusName).Take(2).ToArray();
        if (bonuses.Length > 1)
        {
            return true;
        }
        return bonuses.Length == 1
               && bonuses[0].DescendantsAndSelf()
                   .Any(value => value.Name.LocalName is "firemode" or "modereplace"
                                 && !string.IsNullOrWhiteSpace(value.Value));
    }

    private static bool CanDeleteAmmoGearExactly(XElement root, XElement gear, Guid gearId)
    {
        if (gear.Descendants("gear").Any())
        {
            return false;
        }
        string category = OptionalSingle(gear, "category");
        if (category is "Foci" or "Metamagic Foci" or "Stacked Focus")
        {
            return false;
        }
        string weaponId = OptionalSingle(gear, "weaponid");
        if (!string.IsNullOrWhiteSpace(weaponId)
            && (!Guid.TryParse(weaponId, out Guid parsedWeaponId) || parsedWeaponId != Guid.Empty))
        {
            return false;
        }

        string id = gearId.ToString("D");
        return !root.Descendants("improvement")
                    .Any(improvement => string.Equals(
                        OptionalSingle(improvement, "sourcename"), id, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            OptionalSingle(improvement, "sourcename"), id + "Wireless", StringComparison.OrdinalIgnoreCase))
               && !root.Descendants("weapon")
                    .Any(candidate => !ReferenceEquals(candidate, gear)
                                      && string.Equals(OptionalSingle(candidate, "parentid"), id,
                                          StringComparison.OrdinalIgnoreCase))
               && !root.Descendants("gearid")
                    .Any(value => string.Equals(value.Value, id, StringComparison.OrdinalIgnoreCase));
    }

    private static string ReadDisplayName(XElement weapon)
    {
        string customName = OptionalSingle(weapon, "customname");
        if (!string.IsNullOrWhiteSpace(customName))
        {
            return customName;
        }
        string name = ReadSingle(weapon, "name");
        return string.IsNullOrWhiteSpace(name) ? "Weapon" : name;
    }

    private static string OptionalSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length switch
        {
            0 => string.Empty,
            1 => matches[0].Value,
            _ => throw new InvalidOperationException($"<{parent.Name.LocalName}> requires at most one <{name}> element.")
        };
    }

    private static string ReadSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0].Value
            : throw new InvalidOperationException(
                $"<{parent.Name.LocalName}> requires exactly one <{name}> element.");
    }

    private static bool ReadBoolean(XElement parent, string name)
        => bool.TryParse(ReadSingle(parent, name), out bool value)
            ? value
            : throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> must be a saved Boolean.");

    private static int ReadPositiveInteger(XElement parent, string name)
    {
        int value = ReadInteger(parent, name);
        return value > 0
            ? value
            : throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> must be positive.");
    }

    private static int ReadNonNegativeInteger(XElement parent, string name)
    {
        int value = ReadInteger(parent, name);
        return value >= 0
            ? value
            : throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> cannot be negative.");
    }

    private static int ReadInteger(XElement parent, string name)
        => int.TryParse(ReadSingle(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> must be a saved integer.");

    private static decimal ReadNonNegativeDecimal(XElement parent, string name)
        => decimal.TryParse(ReadSingle(parent, name), NumberStyles.Number, CultureInfo.InvariantCulture,
                out decimal value)
           && value >= 0m
            ? value
            : throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> must be non-negative.");

    private static Guid ReadGuid(XElement parent, string name, bool allowEmpty)
    {
        if (!TryReadGuid(parent, name, out Guid value) || !allowEmpty && value == Guid.Empty)
        {
            throw new InvalidOperationException($"<{parent.Name.LocalName}> <{name}> must be a saved Guid.");
        }
        return value;
    }

    private static bool TryReadGuid(XElement parent, string name, out Guid value)
    {
        value = Guid.Empty;
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1 && Guid.TryParseExact(matches[0].Value, "D", out value);
    }

}
