using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterVehicleEquipmentInstalledPhase
{
    Creation,
    Career
}

public enum CharacterVehicleEquipmentNodeKind
{
    WeaponMount,
    VehicleMod,
    Weapon,
    WeaponAccessory
}

public sealed record CharacterVehicleEquipmentPathSegment(
    CharacterVehicleEquipmentNodeKind Kind,
    Guid Id);

/// <summary>
/// Stable identity for one ICanEquip node in a saved Vehicle tree. The path is
/// a direct parent-to-child chain below VehicleId; mutable labels are never identity.
/// </summary>
public sealed record CharacterVehicleEquipmentInstalledIdentity(
    Guid VehicleId,
    IReadOnlyList<CharacterVehicleEquipmentPathSegment> Path);

/// <summary>
/// Exact saved facts used by the four legacy enable branches. A VehicleMod
/// with sensor side effects remains visible but is deliberately fail-closed.
/// </summary>
public sealed record CharacterVehicleEquipmentInstalledProvenance(
    bool? IncludedInVehicle,
    string SavedParentId,
    Guid? ParentWeaponId,
    bool EquippedOnlyMutationExact);

public sealed record CharacterVehicleEquipmentInstalledEconomics(
    decimal NuyenDelta,
    int KarmaDelta);

public sealed record CharacterVehicleEquipmentInstalledState(
    CharacterVehicleEquipmentInstalledIdentity Identity,
    string DisplayPath,
    CharacterVehicleEquipmentInstalledPhase Phase,
    bool Installed,
    bool LegacyEnabled,
    CharacterVehicleEquipmentInstalledProvenance Provenance,
    CharacterVehicleEquipmentInstalledEconomics Economics,
    string Revision)
{
    public bool CanChangeInstalled => LegacyEnabled && Provenance.EquippedOnlyMutationExact;
}

/// <summary>
/// Exact saved-data authority for CharacterCreate/CharacterCareer.
/// chkVehicleWeaponAccessoryInstalled. The shared Chummer5 handler calls
/// ICanEquip.SetEquippedAsync for WeaponMount, VehicleMod, Weapon, and
/// WeaponAccessory and charges no Nuyen or Karma in either phase.
/// </summary>
public static class CharacterVehicleEquipmentInstalledRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterVehicleEquipmentInstalledIdentity? identity,
        bool created,
        string? displayPath,
        bool installed,
        CharacterVehicleEquipmentInstalledProvenance? provenance,
        out CharacterVehicleEquipmentInstalledState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || displayPath is null
            || !IsValidProvenance(identity!, provenance))
        {
            return false;
        }

        CharacterVehicleEquipmentInstalledPhase phase = created
            ? CharacterVehicleEquipmentInstalledPhase.Career
            : CharacterVehicleEquipmentInstalledPhase.Creation;
        bool legacyEnabled = CalculateLegacyEnabled(identity!, provenance!);
        var economics = new CharacterVehicleEquipmentInstalledEconomics(0m, 0);
        state = new CharacterVehicleEquipmentInstalledState(
            identity!,
            displayPath,
            phase,
            installed,
            legacyEnabled,
            provenance!,
            economics,
            CalculateRevision(identity!, phase, installed, legacyEnabled, provenance!));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterVehicleEquipmentInstalledState? current,
        string? expectedRevision,
        bool installed)
        => current is not null
            && current.CanChangeInstalled
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Installed != installed
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 };

    public static bool IsValidIdentity(CharacterVehicleEquipmentInstalledIdentity? identity)
    {
        if (identity is null
            || identity.VehicleId == Guid.Empty
            || identity.Path is not { Count: > 0 }
            || identity.Path.Any(segment => segment.Id == Guid.Empty))
        {
            return false;
        }

        var unique = new HashSet<Guid> { identity.VehicleId };
        if (!identity.Path.All(segment => unique.Add(segment.Id)))
        {
            return false;
        }

        if (identity.Path[0].Kind is not (
                CharacterVehicleEquipmentNodeKind.WeaponMount
                or CharacterVehicleEquipmentNodeKind.VehicleMod
                or CharacterVehicleEquipmentNodeKind.Weapon))
        {
            return false;
        }

        for (int index = 1; index < identity.Path.Count; index++)
        {
            CharacterVehicleEquipmentNodeKind parent = identity.Path[index - 1].Kind;
            CharacterVehicleEquipmentNodeKind child = identity.Path[index].Kind;
            bool validHop = parent switch
            {
                CharacterVehicleEquipmentNodeKind.WeaponMount => child is
                    CharacterVehicleEquipmentNodeKind.VehicleMod
                    or CharacterVehicleEquipmentNodeKind.Weapon,
                CharacterVehicleEquipmentNodeKind.VehicleMod => child ==
                    CharacterVehicleEquipmentNodeKind.Weapon,
                CharacterVehicleEquipmentNodeKind.Weapon => child is
                    CharacterVehicleEquipmentNodeKind.Weapon
                    or CharacterVehicleEquipmentNodeKind.WeaponAccessory,
                _ => false
            };
            if (!validHop)
            {
                return false;
            }
        }
        return true;
    }

    public static bool IdentityEquals(
        CharacterVehicleEquipmentInstalledIdentity? left,
        CharacterVehicleEquipmentInstalledIdentity? right)
        => left is not null
            && right is not null
            && left.VehicleId == right.VehicleId
            && left.Path is not null
            && right.Path is not null
            && left.Path.SequenceEqual(right.Path);

    private static bool IsValidProvenance(
        CharacterVehicleEquipmentInstalledIdentity identity,
        CharacterVehicleEquipmentInstalledProvenance? provenance)
    {
        if (provenance is null || provenance.SavedParentId is null)
        {
            return false;
        }

        CharacterVehicleEquipmentNodeKind kind = identity.Path[^1].Kind;
        Guid? expectedParentWeaponId = identity.Path.Count > 1
            && identity.Path[^2].Kind == CharacterVehicleEquipmentNodeKind.Weapon
                ? identity.Path[^2].Id
                : null;
        return kind switch
        {
            CharacterVehicleEquipmentNodeKind.WeaponMount =>
                provenance.IncludedInVehicle is not null
                && provenance.SavedParentId.Length == 0
                && provenance.ParentWeaponId is null
                && provenance.EquippedOnlyMutationExact,
            CharacterVehicleEquipmentNodeKind.VehicleMod =>
                provenance.IncludedInVehicle is not null
                && provenance.SavedParentId.Length == 0
                && provenance.ParentWeaponId is null,
            CharacterVehicleEquipmentNodeKind.Weapon =>
                provenance.IncludedInVehicle is null
                && provenance.ParentWeaponId == expectedParentWeaponId
                && provenance.EquippedOnlyMutationExact,
            CharacterVehicleEquipmentNodeKind.WeaponAccessory =>
                provenance.IncludedInVehicle is null
                && provenance.SavedParentId.Length == 0
                && provenance.ParentWeaponId is null
                && provenance.EquippedOnlyMutationExact,
            _ => false
        };
    }

    private static bool CalculateLegacyEnabled(
        CharacterVehicleEquipmentInstalledIdentity identity,
        CharacterVehicleEquipmentInstalledProvenance provenance)
        => identity.Path[^1].Kind switch
        {
            CharacterVehicleEquipmentNodeKind.WeaponMount or
                CharacterVehicleEquipmentNodeKind.VehicleMod =>
                    provenance.IncludedInVehicle == false,
            CharacterVehicleEquipmentNodeKind.Weapon =>
                (provenance.ParentWeaponId is null
                    || !string.Equals(
                        provenance.SavedParentId,
                        provenance.ParentWeaponId.Value.ToString("D"),
                        StringComparison.Ordinal))
                && !string.Equals(
                    provenance.SavedParentId,
                    identity.VehicleId.ToString("D"),
                    StringComparison.Ordinal),
            CharacterVehicleEquipmentNodeKind.WeaponAccessory => true,
            _ => false
        };

    private static string CalculateRevision(
        CharacterVehicleEquipmentInstalledIdentity identity,
        CharacterVehicleEquipmentInstalledPhase phase,
        bool installed,
        bool legacyEnabled,
        CharacterVehicleEquipmentInstalledProvenance provenance)
    {
        var payload = new StringBuilder(identity.VehicleId.ToString("D"));
        foreach (CharacterVehicleEquipmentPathSegment segment in identity.Path)
        {
            payload.Append('\0').Append((int)segment.Kind)
                .Append('\0').Append(segment.Id.ToString("D"));
        }
        payload.Append('\0').Append((int)phase)
            .Append('\0').Append(installed ? '1' : '0')
            .Append('\0').Append(legacyEnabled ? '1' : '0')
            .Append('\0').Append(provenance.IncludedInVehicle?.ToString() ?? string.Empty)
            .Append('\0').Append(provenance.SavedParentId)
            .Append('\0').Append(provenance.ParentWeaponId?.ToString("D") ?? string.Empty)
            .Append('\0').Append(provenance.EquippedOnlyMutationExact ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterVehicleEquipmentInstalledState Unavailable()
        => new(
            new CharacterVehicleEquipmentInstalledIdentity(Guid.Empty, Array.Empty<CharacterVehicleEquipmentPathSegment>()),
            string.Empty,
            CharacterVehicleEquipmentInstalledPhase.Creation,
            false,
            false,
            new CharacterVehicleEquipmentInstalledProvenance(null, string.Empty, null, false),
            new CharacterVehicleEquipmentInstalledEconomics(0m, 0),
            string.Empty);
}
