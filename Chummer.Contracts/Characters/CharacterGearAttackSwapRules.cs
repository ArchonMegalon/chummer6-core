using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterGearAttackSwapPhase
{
    Creation,
    Career
}

public enum CharacterGearAttackSwapTarget
{
    Sleaze,
    DataProcessing,
    Firewall
}

public sealed record CharacterGearAttackSwapIdentity(IReadOnlyList<Guid> GearPath);

public sealed record CharacterGearAttackSwapEconomics(decimal NuyenDelta, int KarmaDelta);

public sealed record CharacterGearAttackSwapState(
    CharacterGearAttackSwapIdentity Identity,
    string DisplayPath,
    CharacterGearAttackSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterGearAttackSwapEconomics Economics,
    string Revision);

/// <summary>
/// Exact authority for CharacterCreate/CharacterCareer.cboGearAttack. The
/// legacy combo swaps the saved raw Attack string with one other base Matrix
/// attribute. Matrix bonuses are display-only and no transaction is created.
/// </summary>
public static class CharacterGearAttackSwapRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearAttackSwapIdentity? identity,
        bool created,
        bool canSwapAttributes,
        string? displayPath,
        string? attack,
        string? sleaze,
        string? dataProcessing,
        string? firewall,
        out CharacterGearAttackSwapState state)
    {
        state = Unavailable();
        if (!canSwapAttributes
            || !IsValidIdentity(identity)
            || string.IsNullOrEmpty(displayPath)
            || string.IsNullOrEmpty(attack)
            || string.IsNullOrEmpty(sleaze)
            || string.IsNullOrEmpty(dataProcessing)
            || string.IsNullOrEmpty(firewall))
        {
            return false;
        }

        CharacterGearAttackSwapPhase phase = created
            ? CharacterGearAttackSwapPhase.Career
            : CharacterGearAttackSwapPhase.Creation;
        var economics = new CharacterGearAttackSwapEconomics(0m, 0);
        state = new CharacterGearAttackSwapState(
            identity!, displayPath, phase, attack, sleaze, dataProcessing, firewall,
            economics,
            CalculateRevision(identity!, phase, attack, sleaze, dataProcessing, firewall));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearAttackSwapState? current,
        string? expectedRevision,
        CharacterGearAttackSwapTarget target)
        => current is not null
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsDefinedTarget(target)
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && !string.Equals(current.Attack, ReadTarget(current, target), StringComparison.Ordinal);

    public static bool IsValidIdentity(CharacterGearAttackSwapIdentity? identity)
        => identity?.GearPath is { Count: > 0 } path
            && path.All(id => id != Guid.Empty)
            && path.Distinct().Count() == path.Count;

    public static bool IdentityEquals(
        CharacterGearAttackSwapIdentity? left,
        CharacterGearAttackSwapIdentity? right)
        => left?.GearPath is not null
            && right?.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    public static string TargetElement(CharacterGearAttackSwapTarget target)
        => target switch
        {
            CharacterGearAttackSwapTarget.Sleaze => "sleaze",
            CharacterGearAttackSwapTarget.DataProcessing => "dataprocessing",
            CharacterGearAttackSwapTarget.Firewall => "firewall",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    public static string ReadTarget(
        CharacterGearAttackSwapState state,
        CharacterGearAttackSwapTarget target)
        => target switch
        {
            CharacterGearAttackSwapTarget.Sleaze => state.Sleaze,
            CharacterGearAttackSwapTarget.DataProcessing => state.DataProcessing,
            CharacterGearAttackSwapTarget.Firewall => state.Firewall,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };

    private static bool IsDefinedTarget(CharacterGearAttackSwapTarget target)
        => target is CharacterGearAttackSwapTarget.Sleaze
            or CharacterGearAttackSwapTarget.DataProcessing
            or CharacterGearAttackSwapTarget.Firewall;

    private static string CalculateRevision(
        CharacterGearAttackSwapIdentity identity,
        CharacterGearAttackSwapPhase phase,
        string attack,
        string sleaze,
        string dataProcessing,
        string firewall)
    {
        var payload = new StringBuilder();
        payload.Append(phase).Append('\0');
        foreach (Guid id in identity.GearPath)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        payload.Append(attack).Append('\0')
            .Append(sleaze).Append('\0')
            .Append(dataProcessing).Append('\0')
            .Append(firewall);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterGearAttackSwapState Unavailable()
        => new(
            new CharacterGearAttackSwapIdentity(Array.Empty<Guid>()),
            string.Empty,
            CharacterGearAttackSwapPhase.Creation,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new CharacterGearAttackSwapEconomics(0m, 0),
            string.Empty);
}
