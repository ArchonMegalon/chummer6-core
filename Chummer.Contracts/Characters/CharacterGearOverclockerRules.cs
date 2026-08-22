using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterGearOverclockerPhase
{
    Career
}

public enum CharacterGearOverclockerTarget
{
    None,
    Attack,
    Sleaze,
    DataProcessing,
    Firewall
}

/// <summary>
/// Stable direct-parent path to one Gear node in character/gears. Mutable
/// labels and collection indexes are deliberately excluded.
/// </summary>
public sealed record CharacterGearOverclockerIdentity(IReadOnlyList<Guid> GearPath);

public sealed record CharacterGearOverclockerEconomics(decimal NuyenDelta, int KarmaDelta);

public sealed record CharacterGearOverclockerState(
    CharacterGearOverclockerIdentity Identity,
    string DisplayPath,
    CharacterGearOverclockerPhase Phase,
    CharacterGearOverclockerTarget Attribute,
    CharacterGearOverclockerEconomics Economics,
    string Revision);

/// <summary>
/// Exact authority for CharacterCareer.cboGearOverclocker. The legacy editor
/// applies only to Career Cyberdeck Gear while the Overclocker improvement is
/// active. It changes one Matrix bonus selection and creates no transaction.
/// </summary>
public static class CharacterGearOverclockerRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterGearOverclockerIdentity? identity,
        bool created,
        bool hasActiveOverclocker,
        string? category,
        string? displayPath,
        string? savedAttribute,
        out CharacterGearOverclockerState state)
    {
        state = Unavailable();
        if (!created
            || !hasActiveOverclocker
            || !string.Equals(category, "Cyberdecks", StringComparison.Ordinal)
            || !IsValidIdentity(identity)
            || displayPath is null
            || !TryParseAttribute(savedAttribute, out CharacterGearOverclockerTarget attribute))
        {
            return false;
        }

        var economics = new CharacterGearOverclockerEconomics(0m, 0);
        state = new CharacterGearOverclockerState(
            identity!,
            displayPath,
            CharacterGearOverclockerPhase.Career,
            attribute,
            economics,
            CalculateRevision(identity!, attribute));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterGearOverclockerState? current,
        string? expectedRevision,
        CharacterGearOverclockerTarget attribute)
        => current is not null
            && current.Phase == CharacterGearOverclockerPhase.Career
            && current.Economics is { NuyenDelta: 0m, KarmaDelta: 0 }
            && IsDefinedAttribute(attribute)
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Attribute != attribute;

    public static bool IsValidIdentity(CharacterGearOverclockerIdentity? identity)
        => identity?.GearPath is { Count: > 0 } path
            && path.All(id => id != Guid.Empty)
            && path.Distinct().Count() == path.Count;

    public static bool IdentityEquals(
        CharacterGearOverclockerIdentity? left,
        CharacterGearOverclockerIdentity? right)
        => left?.GearPath is not null
            && right?.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    public static string ToSavedValue(CharacterGearOverclockerTarget attribute)
        => attribute switch
        {
            CharacterGearOverclockerTarget.None => "None",
            CharacterGearOverclockerTarget.Attack => "Attack",
            CharacterGearOverclockerTarget.Sleaze => "Sleaze",
            CharacterGearOverclockerTarget.DataProcessing => "Data Processing",
            CharacterGearOverclockerTarget.Firewall => "Firewall",
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };

    public static bool TryParseAttribute(
        string? savedValue,
        out CharacterGearOverclockerTarget attribute)
    {
        attribute = savedValue switch
        {
            null or "" or "None" => CharacterGearOverclockerTarget.None,
            "Attack" => CharacterGearOverclockerTarget.Attack,
            "Sleaze" => CharacterGearOverclockerTarget.Sleaze,
            "Data Processing" => CharacterGearOverclockerTarget.DataProcessing,
            "Firewall" => CharacterGearOverclockerTarget.Firewall,
            _ => (CharacterGearOverclockerTarget)(-1)
        };
        return IsDefinedAttribute(attribute);
    }

    private static bool IsDefinedAttribute(CharacterGearOverclockerTarget attribute)
        => attribute is CharacterGearOverclockerTarget.None
            or CharacterGearOverclockerTarget.Attack
            or CharacterGearOverclockerTarget.Sleaze
            or CharacterGearOverclockerTarget.DataProcessing
            or CharacterGearOverclockerTarget.Firewall;

    private static string CalculateRevision(
        CharacterGearOverclockerIdentity identity,
        CharacterGearOverclockerTarget attribute)
    {
        var payload = new StringBuilder();
        payload.Append(CharacterGearOverclockerPhase.Career).Append('\0');
        foreach (Guid id in identity.GearPath)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        payload.Append(ToSavedValue(attribute));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterGearOverclockerState Unavailable()
        => new(
            new CharacterGearOverclockerIdentity(Array.Empty<Guid>()),
            string.Empty,
            CharacterGearOverclockerPhase.Career,
            CharacterGearOverclockerTarget.None,
            new CharacterGearOverclockerEconomics(0m, 0),
            string.Empty);
}
