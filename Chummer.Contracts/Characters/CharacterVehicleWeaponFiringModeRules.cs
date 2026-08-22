using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterVehicleWeaponFiringModePhase { Creation, Career }

public enum CharacterVehicleWeaponFiringMode
{
    DogBrain,
    GunneryCommandDevice,
    RemoteOperated,
    ManualOperation,
    Skill
}

public sealed record CharacterVehicleWeaponFiringModeIdentity(Guid VehicleId, Guid WeaponId);
public sealed record CharacterVehicleWeaponFiringModeEconomics(decimal NuyenDelta, int KarmaDelta);
public sealed record CharacterVehicleWeaponFiringModeProvenance(string RangeType, string Ammo);
public sealed record CharacterVehicleWeaponFiringModeState(
    CharacterVehicleWeaponFiringModeIdentity Identity,
    string DisplayName,
    CharacterVehicleWeaponFiringModePhase Phase,
    CharacterVehicleWeaponFiringMode FiringMode,
    CharacterVehicleWeaponFiringModeProvenance Provenance,
    CharacterVehicleWeaponFiringModeEconomics Economics,
    string Revision);

/// <summary>
/// Exact saved-data authority for cboVehicleWeaponFiringMode on one direct Weapon
/// in a Vehicle's own weapons collection. Weapon mounts, underbarrel weapons and
/// all other descendant paths remain outside this bounded authority.
/// </summary>
public static class CharacterVehicleWeaponFiringModeRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterVehicleWeaponFiringModeIdentity? identity,
        bool created,
        string? displayName,
        string? savedFiringMode,
        string? rangeType,
        string? ammo,
        out CharacterVehicleWeaponFiringModeState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || string.IsNullOrWhiteSpace(displayName)
            || !IsLegacyEditorVisible(rangeType, ammo)
            || !TryParseSavedValue(savedFiringMode, out CharacterVehicleWeaponFiringMode mode))
        {
            return false;
        }

        CharacterVehicleWeaponFiringModePhase phase = created
            ? CharacterVehicleWeaponFiringModePhase.Career
            : CharacterVehicleWeaponFiringModePhase.Creation;
        var provenance = new CharacterVehicleWeaponFiringModeProvenance(rangeType!, ammo!);
        state = new CharacterVehicleWeaponFiringModeState(
            identity!, displayName, phase, mode, provenance,
            new CharacterVehicleWeaponFiringModeEconomics(0m, 0),
            CalculateRevision(identity!, phase, mode, provenance));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterVehicleWeaponFiringModeState? current,
        string? expectedRevision,
        CharacterVehicleWeaponFiringMode requestedMode)
        => current is not null
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsLegacyEditorVisible(current.Provenance.RangeType, current.Provenance.Ammo)
            && IsDefined(requestedMode)
            && requestedMode != current.FiringMode
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal);

    public static bool IsValidIdentity(CharacterVehicleWeaponFiringModeIdentity? identity)
        => identity is { VehicleId: var vehicleId, WeaponId: var weaponId }
            && vehicleId != Guid.Empty
            && weaponId != Guid.Empty
            && vehicleId != weaponId;

    /// <summary>
    /// Mirrors the legacy visibility rules: ranged weapons expose the picker;
    /// melee weapons expose it only when their raw Ammo string is not "0".
    /// </summary>
    public static bool IsLegacyEditorVisible(string? rangeType, string? ammo)
        => string.Equals(rangeType, "Ranged", StringComparison.Ordinal)
            || string.Equals(rangeType, "Melee", StringComparison.Ordinal)
                && !string.Equals(ammo, "0", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(ammo);

    public static bool TryParseSavedValue(
        string? value,
        out CharacterVehicleWeaponFiringMode firingMode)
    {
        firingMode = CharacterVehicleWeaponFiringMode.DogBrain;
        if (string.IsNullOrWhiteSpace(value)
            || !Enum.TryParse(value.Trim(), ignoreCase: true, out CharacterVehicleWeaponFiringMode parsed)
            || !IsDefined(parsed))
        {
            return false;
        }

        firingMode = parsed;
        return true;
    }

    public static string SavedValue(CharacterVehicleWeaponFiringMode firingMode)
        => IsDefined(firingMode)
            ? firingMode.ToString()
            : throw new ArgumentOutOfRangeException(nameof(firingMode));

    private static bool IsDefined(CharacterVehicleWeaponFiringMode value)
        => value is CharacterVehicleWeaponFiringMode.DogBrain
            or CharacterVehicleWeaponFiringMode.GunneryCommandDevice
            or CharacterVehicleWeaponFiringMode.RemoteOperated
            or CharacterVehicleWeaponFiringMode.ManualOperation
            or CharacterVehicleWeaponFiringMode.Skill;

    private static string CalculateRevision(
        CharacterVehicleWeaponFiringModeIdentity identity,
        CharacterVehicleWeaponFiringModePhase phase,
        CharacterVehicleWeaponFiringMode mode,
        CharacterVehicleWeaponFiringModeProvenance provenance)
    {
        string payload = string.Join("\0", "vehicle-weapon-firing-mode/v1",
            identity.VehicleId.ToString("D"), identity.WeaponId.ToString("D"), phase,
            SavedValue(mode), provenance.RangeType, provenance.Ammo);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static CharacterVehicleWeaponFiringModeState Unavailable() => new(
        new CharacterVehicleWeaponFiringModeIdentity(Guid.Empty, Guid.Empty), string.Empty,
        CharacterVehicleWeaponFiringModePhase.Creation,
        CharacterVehicleWeaponFiringMode.DogBrain,
        new CharacterVehicleWeaponFiringModeProvenance(string.Empty, string.Empty),
        new CharacterVehicleWeaponFiringModeEconomics(0m, 0), string.Empty);
}
