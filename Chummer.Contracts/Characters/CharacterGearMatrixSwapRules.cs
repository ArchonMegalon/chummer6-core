using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterGearMatrixSwapPhase { Creation, Career }

public enum CharacterGearMatrixStat { Attack, Sleaze, DataProcessing, Firewall }

public sealed record CharacterGearMatrixSwapIdentity(IReadOnlyList<Guid> GearPath);
public sealed record CharacterGearMatrixSwapEconomics(decimal NuyenDelta, int KarmaDelta);
public sealed record CharacterGearMatrixSwapState(
    CharacterGearMatrixSwapIdentity Identity,
    string DisplayPath,
    CharacterGearMatrixSwapPhase Phase,
    string Attack,
    string Sleaze,
    string DataProcessing,
    string Firewall,
    CharacterGearMatrixSwapEconomics Economics,
    string Revision);

/// <summary>Shared exact raw-value swap seam used by each legacy Gear Matrix combo.</summary>
public static class CharacterGearMatrixSwapRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearMatrixSwapIdentity? identity, bool created, bool canSwapAttributes,
        string? displayPath, string? attack, string? sleaze, string? dataProcessing, string? firewall,
        out CharacterGearMatrixSwapState state)
    {
        state = Unavailable();
        if (!canSwapAttributes || !IsValidIdentity(identity) || string.IsNullOrEmpty(displayPath)
            || string.IsNullOrEmpty(attack) || string.IsNullOrEmpty(sleaze)
            || string.IsNullOrEmpty(dataProcessing) || string.IsNullOrEmpty(firewall)) return false;
        CharacterGearMatrixSwapPhase phase = created ? CharacterGearMatrixSwapPhase.Career : CharacterGearMatrixSwapPhase.Creation;
        state = new(identity!, displayPath, phase, attack, sleaze, dataProcessing, firewall,
            new CharacterGearMatrixSwapEconomics(0m, 0),
            CalculateRevision(identity!, phase, attack, sleaze, dataProcessing, firewall));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearMatrixSwapState? current, string? expectedRevision,
        CharacterGearMatrixStat changedAttribute, CharacterGearMatrixStat targetAttribute)
        => current is not null
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsDefined(changedAttribute) && IsDefined(targetAttribute)
            && changedAttribute != targetAttribute
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && !string.Equals(Read(current, changedAttribute), Read(current, targetAttribute), StringComparison.Ordinal);

    public static bool IsValidIdentity(CharacterGearMatrixSwapIdentity? identity)
        => identity?.GearPath is { Count: > 0 } path && path.All(id => id != Guid.Empty)
            && path.Distinct().Count() == path.Count;

    public static bool IdentityEquals(CharacterGearMatrixSwapIdentity? left, CharacterGearMatrixSwapIdentity? right)
        => left?.GearPath is not null && right?.GearPath is not null && left.GearPath.SequenceEqual(right.GearPath);

    public static string ElementName(CharacterGearMatrixStat attribute) => attribute switch
    {
        CharacterGearMatrixStat.Attack => "attack",
        CharacterGearMatrixStat.Sleaze => "sleaze",
        CharacterGearMatrixStat.DataProcessing => "dataprocessing",
        CharacterGearMatrixStat.Firewall => "firewall",
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    public static string Read(CharacterGearMatrixSwapState state, CharacterGearMatrixStat attribute) => attribute switch
    {
        CharacterGearMatrixStat.Attack => state.Attack,
        CharacterGearMatrixStat.Sleaze => state.Sleaze,
        CharacterGearMatrixStat.DataProcessing => state.DataProcessing,
        CharacterGearMatrixStat.Firewall => state.Firewall,
        _ => throw new ArgumentOutOfRangeException(nameof(attribute))
    };

    private static bool IsDefined(CharacterGearMatrixStat value)
        => value is CharacterGearMatrixStat.Attack or CharacterGearMatrixStat.Sleaze
            or CharacterGearMatrixStat.DataProcessing or CharacterGearMatrixStat.Firewall;

    private static string CalculateRevision(CharacterGearMatrixSwapIdentity identity, CharacterGearMatrixSwapPhase phase,
        string attack, string sleaze, string dataProcessing, string firewall)
    {
        var payload = new StringBuilder().Append(phase).Append('\0');
        foreach (Guid id in identity.GearPath) payload.Append(id.ToString("D")).Append('\0');
        payload.Append(attack).Append('\0').Append(sleaze).Append('\0').Append(dataProcessing).Append('\0').Append(firewall);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString()))).ToLowerInvariant();
    }

    private static CharacterGearMatrixSwapState Unavailable() => new(
        new CharacterGearMatrixSwapIdentity([]), string.Empty, CharacterGearMatrixSwapPhase.Creation,
        string.Empty, string.Empty, string.Empty, string.Empty, new CharacterGearMatrixSwapEconomics(0m, 0), string.Empty);
}
