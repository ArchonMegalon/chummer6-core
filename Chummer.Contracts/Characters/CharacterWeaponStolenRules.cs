using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterWeaponStolenNodeKind
{
    Weapon,
    WeaponAccessory,
    Gear
}

public enum CharacterWeaponStolenPhase
{
    Creation
}

public sealed record CharacterWeaponStolenHop(
    CharacterWeaponStolenNodeKind Kind,
    Guid Id);

/// <summary>
/// Stable typed path through one saved Weapon tree. Labels and collection
/// positions are deliberately excluded from identity.
/// </summary>
public sealed record CharacterWeaponStolenIdentity(
    IReadOnlyList<CharacterWeaponStolenHop> Path);

public sealed record CharacterWeaponStolenEconomics(
    decimal NuyenDelta,
    int KarmaDelta);

public sealed record CharacterWeaponStolenState(
    CharacterWeaponStolenIdentity Identity,
    string DisplayPath,
    CharacterWeaponStolenPhase Phase,
    bool Stolen,
    CharacterWeaponStolenEconomics Economics,
    string Revision);

/// <summary>
/// Exact authority for CharacterCreate.chkWeaponStolen. The legacy control
/// accepts Weapon, WeaponAccessory, and accessory Gear nodes while an active
/// creation Nuyen/Stolen improvement applies. It changes cost partitioning,
/// but creates no Nuyen or Karma transaction.
/// </summary>
public static class CharacterWeaponStolenRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterWeaponStolenIdentity? identity,
        bool created,
        bool hasStolenNuyenImprovement,
        string? displayPath,
        bool stolen,
        out CharacterWeaponStolenState state)
    {
        state = Unavailable();
        if (created
            || !hasStolenNuyenImprovement
            || !IsValidIdentity(identity)
            || displayPath is null)
        {
            return false;
        }

        var economics = new CharacterWeaponStolenEconomics(0m, 0);
        state = new CharacterWeaponStolenState(
            identity!,
            displayPath,
            CharacterWeaponStolenPhase.Creation,
            stolen,
            economics,
            CalculateRevision(identity!, stolen));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterWeaponStolenState? current,
        string? expectedRevision,
        bool stolen)
        => current is not null
            && current.Phase == CharacterWeaponStolenPhase.Creation
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Stolen != stolen;

    public static bool IdentityEquals(
        CharacterWeaponStolenIdentity? left,
        CharacterWeaponStolenIdentity? right)
        => left?.Path is not null
            && right?.Path is not null
            && left.Path.SequenceEqual(right.Path);

    public static bool IsValidIdentity(CharacterWeaponStolenIdentity? identity)
    {
        if (identity?.Path is not { Count: > 0 } path)
        {
            return false;
        }

        CharacterWeaponStolenHop root = path[0];
        if (root.Kind != CharacterWeaponStolenNodeKind.Weapon || root.Id == Guid.Empty)
        {
            return false;
        }

        var ids = new HashSet<Guid> { root.Id };
        for (int index = 1; index < path.Count; index++)
        {
            CharacterWeaponStolenHop previous = path[index - 1];
            CharacterWeaponStolenHop current = path[index];
            if (current.Id == Guid.Empty || !ids.Add(current.Id))
            {
                return false;
            }

            bool validTransition = previous.Kind switch
            {
                CharacterWeaponStolenNodeKind.Weapon => current.Kind is
                    CharacterWeaponStolenNodeKind.Weapon or
                    CharacterWeaponStolenNodeKind.WeaponAccessory,
                CharacterWeaponStolenNodeKind.WeaponAccessory =>
                    current.Kind == CharacterWeaponStolenNodeKind.Gear,
                CharacterWeaponStolenNodeKind.Gear =>
                    current.Kind == CharacterWeaponStolenNodeKind.Gear,
                _ => false
            };
            if (!validTransition)
            {
                return false;
            }
        }
        return true;
    }

    private static string CalculateRevision(
        CharacterWeaponStolenIdentity identity,
        bool stolen)
    {
        var payload = new StringBuilder();
        payload.Append(CharacterWeaponStolenPhase.Creation).Append('\0');
        foreach (CharacterWeaponStolenHop hop in identity.Path)
        {
            payload.Append(hop.Kind).Append('\0')
                .Append(hop.Id.ToString("D")).Append('\0');
        }
        payload.Append(stolen ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterWeaponStolenState Unavailable()
        => new(
            new CharacterWeaponStolenIdentity(Array.Empty<CharacterWeaponStolenHop>()),
            string.Empty,
            CharacterWeaponStolenPhase.Creation,
            false,
            new CharacterWeaponStolenEconomics(0m, 0),
            string.Empty);
}
