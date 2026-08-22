using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterFreeSpriteConversionIdentity(
    Guid CritterPowerId,
    Guid SourceId,
    int ExpectedAppendIndex);

public sealed record CharacterFreeSpriteConversionEconomics(
    int KarmaDelta,
    decimal NuyenDelta);

public sealed record CharacterFreeSpriteConversionState(
    bool Created,
    string MetatypeCategory,
    IReadOnlyList<Guid> CritterPowerIds,
    string Revision,
    bool CanConvert,
    CharacterFreeSpriteConversionEconomics Economics);

/// <summary>
/// Exact authority for CharacterCreate/CharacterCareer Convert to Free Sprite.
/// The legacy handlers are identical: a non-Free Sprite receives Denial without
/// counting toward its Critter Power limit, then becomes a Free Sprite.
/// </summary>
public static class CharacterFreeSpriteConversionRules
{
    public const int RevisionHexLength = 64;
    public const string FreeSpriteCategory = "Free Sprite";
    public const string DenialName = "Denial";
    public const string DenialCategory = "Emergent";
    public const string DenialSource = "UN";
    public const string DenialPage = "160";
    public const string DenialDefaultNotesColor = "Chocolate";
    public static readonly Guid DenialSourceId =
        Guid.Parse("c2899500-5932-4c39-81a8-fa64b08fa916");

    public static bool TryCreateState(
        bool created,
        string? metatypeCategory,
        IReadOnlyList<Guid>? critterPowerIds,
        out CharacterFreeSpriteConversionState state)
    {
        state = Unavailable();
        if (metatypeCategory is null || critterPowerIds is null)
        {
            return false;
        }

        HashSet<Guid> identities = [];
        foreach (Guid id in critterPowerIds)
        {
            if (id == Guid.Empty || !identities.Add(id))
            {
                return false;
            }
        }

        Guid[] snapshot = critterPowerIds.ToArray();
        bool isSprite = metatypeCategory.EndsWith("Sprites", StringComparison.Ordinal)
            && !string.Equals(metatypeCategory, FreeSpriteCategory, StringComparison.Ordinal);
        state = new CharacterFreeSpriteConversionState(
            created,
            metatypeCategory,
            snapshot,
            CalculateRevision(created, metatypeCategory, snapshot),
            isSprite,
            new CharacterFreeSpriteConversionEconomics(0, 0m));
        return true;
    }

    public static bool TryCreateIdentity(
        CharacterFreeSpriteConversionState? current,
        Guid critterPowerId,
        out CharacterFreeSpriteConversionIdentity identity)
    {
        identity = new CharacterFreeSpriteConversionIdentity(Guid.Empty, Guid.Empty, -1);
        if (current is null
            || !current.CanConvert
            || critterPowerId == Guid.Empty
            || current.CritterPowerIds.Contains(critterPowerId))
        {
            return false;
        }

        identity = new CharacterFreeSpriteConversionIdentity(
            critterPowerId,
            DenialSourceId,
            current.CritterPowerIds.Count);
        return true;
    }

    public static bool TryValidateMutation(
        CharacterFreeSpriteConversionState? current,
        CharacterFreeSpriteConversionIdentity? identity,
        string? expectedRevision)
        => current is { CanConvert: true }
            && identity is not null
            && identity.CritterPowerId != Guid.Empty
            && identity.SourceId == DenialSourceId
            && identity.ExpectedAppendIndex == current.CritterPowerIds.Count
            && !current.CritterPowerIds.Contains(identity.CritterPowerId)
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Economics is { KarmaDelta: 0, NuyenDelta: 0m };

    private static string CalculateRevision(
        bool created,
        string category,
        IReadOnlyList<Guid> ids)
    {
        var payload = new StringBuilder();
        payload.Append(created).Append('\0')
            .Append(category.Length).Append(':').Append(category).Append('\0')
            .Append(ids.Count).Append('\0');
        foreach (Guid id in ids)
        {
            payload.Append(id.ToString("D")).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterFreeSpriteConversionState Unavailable()
        => new(
            false,
            string.Empty,
            Array.Empty<Guid>(),
            string.Empty,
            false,
            new CharacterFreeSpriteConversionEconomics(0, 0m));
}
