using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterArmorTreeNodeKind
{
    Armor,
    ArmorMod,
    Gear
}

public sealed record CharacterArmorTreeNodeIdentity(
    CharacterArmorTreeNodeKind Kind,
    Guid ArmorId,
    Guid? ArmorModId,
    IReadOnlyList<Guid> GearPath);

public sealed record CharacterArmorTreeFlagState(
    CharacterArmorTreeNodeIdentity Identity,
    string DisplayPath,
    bool Stolen,
    bool DiscountedCost,
    string Revision);

/// <summary>
/// Exact authority for the two creation-mode flags exposed for the selected
/// Chummer5 armor-tree node. Armor, ArmorMod, and Gear all implement the same
/// legacy stolen and Black Market Discount contracts; hierarchy is identity,
/// not an eligibility rule.
/// </summary>
public static class CharacterArmorTreeFlagRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterArmorTreeNodeIdentity? identity,
        bool created,
        string? displayPath,
        bool stolen,
        bool discountedCost,
        out CharacterArmorTreeFlagState state)
    {
        state = Unavailable();
        if (created || !IsValidIdentity(identity) || displayPath is null)
        {
            return false;
        }

        state = new CharacterArmorTreeFlagState(
            identity!,
            displayPath,
            stolen,
            discountedCost,
            CalculateRevision(identity!, stolen, discountedCost));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterArmorTreeFlagState? current,
        string? expectedRevision,
        bool stolen,
        bool discountedCost)
        => current is not null
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && (current.Stolen != stolen || current.DiscountedCost != discountedCost);

    public static bool IdentityEquals(
        CharacterArmorTreeNodeIdentity? left,
        CharacterArmorTreeNodeIdentity? right)
        => left is not null
            && right is not null
            && left.Kind == right.Kind
            && left.ArmorId == right.ArmorId
            && left.ArmorModId == right.ArmorModId
            && left.GearPath is not null
            && right.GearPath is not null
            && left.GearPath.SequenceEqual(right.GearPath);

    public static bool IsValidIdentity(CharacterArmorTreeNodeIdentity? identity)
    {
        if (identity is null
            || identity.ArmorId == Guid.Empty
            || identity.GearPath is null
            || identity.ArmorModId == Guid.Empty
            || identity.GearPath.Any(id => id == Guid.Empty))
        {
            return false;
        }

        bool shapeValid = identity.Kind switch
        {
            CharacterArmorTreeNodeKind.Armor => identity.ArmorModId is null
                && identity.GearPath.Count == 0,
            CharacterArmorTreeNodeKind.ArmorMod => identity.ArmorModId is not null
                && identity.GearPath.Count == 0,
            CharacterArmorTreeNodeKind.Gear => identity.GearPath.Count != 0,
            _ => false
        };
        if (!shapeValid)
        {
            return false;
        }

        var unique = new HashSet<Guid> { identity.ArmorId };
        if (identity.ArmorModId is Guid modId && !unique.Add(modId))
        {
            return false;
        }
        return identity.GearPath.All(unique.Add);
    }

    private static string CalculateRevision(
        CharacterArmorTreeNodeIdentity identity,
        bool stolen,
        bool discountedCost)
    {
        var payload = new StringBuilder();
        payload.Append(identity.Kind)
            .Append('\0')
            .Append(identity.ArmorId.ToString("D"))
            .Append('\0')
            .Append(identity.ArmorModId?.ToString("D") ?? string.Empty);
        foreach (Guid gearId in identity.GearPath)
        {
            payload.Append('\0').Append(gearId.ToString("D"));
        }
        payload.Append('\0').Append(stolen ? '1' : '0')
            .Append('\0').Append(discountedCost ? '1' : '0');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterArmorTreeFlagState Unavailable()
        => new(
            new CharacterArmorTreeNodeIdentity(
                CharacterArmorTreeNodeKind.Armor,
                Guid.Empty,
                null,
                Array.Empty<Guid>()),
            string.Empty,
            false,
            false,
            string.Empty);
}
