using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterGearEquipmentPhase
{
    Creation,
    Career
}

/// <summary>
/// Stable identity for one node in the saved Gear tree. Every Guid is one
/// direct parent-to-child hop; mutable labels are never identity.
/// </summary>
public sealed record CharacterGearEquipmentIdentity(IReadOnlyList<Guid> GearPath);

public sealed record CharacterGearEquipmentEconomics(
    decimal NuyenDelta,
    int KarmaDelta);

public sealed record CharacterGearEquipmentState(
    CharacterGearEquipmentIdentity Identity,
    string DisplayPath,
    CharacterGearEquipmentPhase Phase,
    bool Equipped,
    bool CanChangeEquip,
    CharacterGearEquipmentEconomics Economics,
    string Revision);

/// <summary>
/// Authority for CharacterCreate/CharacterCareer.chkGearEquipped. The legacy
/// handler changes the saved equipped Boolean when the Gear is neither
/// included in a parent nor loaded into a weapon clip. This state edit has no
/// Nuyen or Karma transaction in either phase.
/// </summary>
public static class CharacterGearEquipmentRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearEquipmentIdentity? identity,
        bool created,
        bool includedInParent,
        bool loadedIntoClip,
        string? displayPath,
        bool equipped,
        out CharacterGearEquipmentState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity) || displayPath is null)
        {
            return false;
        }

        CharacterGearEquipmentPhase phase = created
            ? CharacterGearEquipmentPhase.Career
            : CharacterGearEquipmentPhase.Creation;
        bool canChangeEquip = !includedInParent && !loadedIntoClip;
        var economics = new CharacterGearEquipmentEconomics(0m, 0);
        state = new CharacterGearEquipmentState(
            identity!,
            displayPath,
            phase,
            equipped,
            canChangeEquip,
            economics,
            CalculateRevision(identity!, phase, equipped, canChangeEquip));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearEquipmentState? current,
        string? expectedRevision,
        bool equipped)
        => current is not null
            && current.CanChangeEquip
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Equipped != equipped
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 };

    public static bool IsValidIdentity(CharacterGearEquipmentIdentity? identity)
        => identity?.GearPath is { Count: > 0 } path
            && path.All(id => id != Guid.Empty)
            && path.Distinct().Count() == path.Count;

    public static bool IdentityEquals(
        CharacterGearEquipmentIdentity? left,
        CharacterGearEquipmentIdentity? right)
        => left?.GearPath is not null
            && right?.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    private static string CalculateRevision(
        CharacterGearEquipmentIdentity identity,
        CharacterGearEquipmentPhase phase,
        bool equipped,
        bool canChangeEquip)
    {
        var payload = new StringBuilder();
        foreach (Guid id in identity.GearPath)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        payload.Append((int)phase).Append('\0')
            .Append(equipped ? '1' : '0').Append('\0')
            .Append(canChangeEquip ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterGearEquipmentState Unavailable()
        => new(
            new CharacterGearEquipmentIdentity(Array.Empty<Guid>()),
            string.Empty,
            CharacterGearEquipmentPhase.Creation,
            false,
            false,
            new CharacterGearEquipmentEconomics(0m, 0),
            string.Empty);
}
