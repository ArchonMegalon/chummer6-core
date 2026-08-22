using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterMartialArtDeleteIdentity(
    Guid MartialArtId,
    Guid? TechniqueId)
{
    public bool IsTechnique => TechniqueId.HasValue;
}

public sealed record CharacterMartialArtDeleteEconomics(
    int KarmaDelta,
    decimal NuyenDelta);

public sealed record CharacterMartialArtDeleteState(
    CharacterMartialArtDeleteIdentity Identity,
    bool Created,
    string MartialArtName,
    string TargetName,
    bool MartialArtIsQuality,
    int CascadeTechniqueCount,
    string Revision,
    CharacterMartialArtDeleteEconomics Economics)
{
    public bool CanDelete => Identity.IsTechnique || !MartialArtIsQuality;
}

/// <summary>
/// Exact authority for CharacterCreate/CharacterCareer.cmdDeleteMartialArt.
/// Both modes require confirmation and delete with no Karma or Nuyen refund.
/// </summary>
public static class CharacterMartialArtDeleteRules
{
    public const int RevisionHexLength = 64;

    public static bool IsValidIdentity(CharacterMartialArtDeleteIdentity? identity)
        => identity is not null
            && identity.MartialArtId != Guid.Empty
            && identity.TechniqueId != Guid.Empty;

    public static bool TryCreateState(
        CharacterMartialArtDeleteIdentity? identity,
        bool created,
        string? martialArtName,
        string? targetName,
        bool martialArtIsQuality,
        int cascadeTechniqueCount,
        string? targetState,
        string? improvementState,
        out CharacterMartialArtDeleteState state)
    {
        state = Unavailable();
        if (!IsValidIdentity(identity)
            || martialArtName is null
            || targetName is null
            || cascadeTechniqueCount < 0
            || (identity!.IsTechnique && cascadeTechniqueCount != 0)
            || targetState is null
            || improvementState is null)
        {
            return false;
        }

        state = new CharacterMartialArtDeleteState(
            identity,
            created,
            martialArtName,
            targetName,
            martialArtIsQuality,
            cascadeTechniqueCount,
            CalculateRevision(
                identity,
                created,
                martialArtName,
                targetName,
                martialArtIsQuality,
                cascadeTechniqueCount,
                targetState,
                improvementState),
            new CharacterMartialArtDeleteEconomics(0, 0m));
        return true;
    }

    public static bool CanDelete(
        CharacterMartialArtDeleteState? current,
        CharacterMartialArtDeleteIdentity? identity,
        string? expectedRevision,
        bool confirmed)
        => confirmed
            && current is not null
            && identity is not null
            && IsValidIdentity(identity)
            && current.Identity == identity
            && current.CanDelete
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Economics is { KarmaDelta: 0, NuyenDelta: 0m };

    private static string CalculateRevision(
        CharacterMartialArtDeleteIdentity identity,
        bool created,
        string martialArtName,
        string targetName,
        bool martialArtIsQuality,
        int cascadeTechniqueCount,
        string targetState,
        string improvementState)
    {
        string payload = string.Join('\0',
            identity.MartialArtId.ToString("D"),
            identity.TechniqueId?.ToString("D") ?? string.Empty,
            created.ToString(),
            martialArtName,
            targetName,
            martialArtIsQuality.ToString(),
            cascadeTechniqueCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            targetState,
            improvementState);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static CharacterMartialArtDeleteState Unavailable()
        => new(
            new CharacterMartialArtDeleteIdentity(Guid.Empty, null),
            false,
            string.Empty,
            string.Empty,
            false,
            0,
            string.Empty,
            new CharacterMartialArtDeleteEconomics(0, 0m));
}
