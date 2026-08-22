using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterGearOverclockerPhase
{
    Career
}

public enum CharacterGearOverclockerAttribute
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
    CharacterGearOverclockerAttribute Attribute,
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
            || !TryParseAttribute(savedAttribute, out CharacterGearOverclockerAttribute attribute))
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
        CharacterGearOverclockerAttribute attribute)
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

    public static string ToSavedValue(CharacterGearOverclockerAttribute attribute)
        => attribute switch
        {
            CharacterGearOverclockerAttribute.None => "None",
            CharacterGearOverclockerAttribute.Attack => "Attack",
            CharacterGearOverclockerAttribute.Sleaze => "Sleaze",
            CharacterGearOverclockerAttribute.DataProcessing => "Data Processing",
            CharacterGearOverclockerAttribute.Firewall => "Firewall",
            _ => throw new ArgumentOutOfRangeException(nameof(attribute))
        };

    public static bool TryParseAttribute(
        string? savedValue,
        out CharacterGearOverclockerAttribute attribute)
    {
        attribute = savedValue switch
        {
            null or "" or "None" => CharacterGearOverclockerAttribute.None,
            "Attack" => CharacterGearOverclockerAttribute.Attack,
            "Sleaze" => CharacterGearOverclockerAttribute.Sleaze,
            "Data Processing" => CharacterGearOverclockerAttribute.DataProcessing,
            "Firewall" => CharacterGearOverclockerAttribute.Firewall,
            _ => (CharacterGearOverclockerAttribute)(-1)
        };
        return IsDefinedAttribute(attribute);
    }

    private static bool IsDefinedAttribute(CharacterGearOverclockerAttribute attribute)
        => attribute is CharacterGearOverclockerAttribute.None
            or CharacterGearOverclockerAttribute.Attack
            or CharacterGearOverclockerAttribute.Sleaze
            or CharacterGearOverclockerAttribute.DataProcessing
            or CharacterGearOverclockerAttribute.Firewall;

    private static string CalculateRevision(
        CharacterGearOverclockerIdentity identity,
        CharacterGearOverclockerAttribute attribute)
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
            CharacterGearOverclockerAttribute.None,
            new CharacterGearOverclockerEconomics(0m, 0),
            string.Empty);
}
