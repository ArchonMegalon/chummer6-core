using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable identity for one node in the saved top-level Gear tree. Each Guid
/// is one direct parent-to-child hop; labels are deliberately not identity.
/// </summary>
public sealed record CharacterGearStolenIdentity(IReadOnlyList<Guid> GearPath);

public sealed record CharacterGearStolenState(
    CharacterGearStolenIdentity Identity,
    string DisplayPath,
    bool Stolen,
    string Revision);

/// <summary>
/// Core authority for CharacterCreate.chkGearStolen. The legacy checkbox is
/// creation-only and is exposed only while an active, non-rating Nuyen/Stolen
/// Improvement applies. Presentation resolves that saved eligibility before
/// asking these rules to project or mutate a node.
/// </summary>
public static class CharacterGearStolenRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearStolenIdentity? identity,
        bool created,
        bool hasStolenNuyenImprovement,
        string? displayPath,
        bool stolen,
        out CharacterGearStolenState state)
    {
        state = Unavailable();
        if (created
            || !hasStolenNuyenImprovement
            || !IsValidIdentity(identity)
            || displayPath is null)
        {
            return false;
        }

        state = new CharacterGearStolenState(
            identity!,
            displayPath,
            stolen,
            CalculateRevision(identity!, stolen));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearStolenState? current,
        string? expectedRevision,
        bool stolen)
        => current is not null
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Stolen != stolen;

    public static bool IsValidIdentity(CharacterGearStolenIdentity? identity)
        => identity?.GearPath is { Count: > 0 } path
            && path.All(id => id != Guid.Empty)
            && path.Distinct().Count() == path.Count;

    public static bool IdentityEquals(
        CharacterGearStolenIdentity? left,
        CharacterGearStolenIdentity? right)
        => left?.GearPath is not null
            && right?.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    private static string CalculateRevision(
        CharacterGearStolenIdentity identity,
        bool stolen)
    {
        var payload = new StringBuilder();
        foreach (Guid id in identity.GearPath)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        payload.Append(stolen ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterGearStolenState Unavailable()
        => new(
            new CharacterGearStolenIdentity(Array.Empty<Guid>()),
            string.Empty,
            false,
            string.Empty);
}
