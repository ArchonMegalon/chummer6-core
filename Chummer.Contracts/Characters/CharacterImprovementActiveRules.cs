using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable saved identity for one Chummer5 Improvement. SourceName is the
/// legacy Improvement.InternalId; the remaining semantic fields distinguish
/// the several effects that can be emitted by the same source object.
/// </summary>
public sealed record CharacterImprovementIdentity(
    string SourceName,
    string ImprovementType,
    string ImprovementSource,
    string ImprovedName,
    string UniqueName,
    string Target,
    string CustomId,
    string CustomGroup);

public sealed record CharacterImprovementActiveState(
    CharacterImprovementIdentity Identity,
    string DisplayName,
    bool Enabled,
    string Revision);

/// <summary>
/// Core authority for CharacterCareer.chkImprovementActive. The legacy
/// checkbox is available only for a directly selected Improvement and maps
/// its boolean state to Improvement.Enabled.
/// </summary>
public static class CharacterImprovementActiveRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterImprovementIdentity? identity,
        bool created,
        string? displayName,
        bool enabled,
        out CharacterImprovementActiveState state)
    {
        state = Unavailable();
        if (!created || !IsValidIdentity(identity) || displayName is null)
        {
            return false;
        }

        state = new CharacterImprovementActiveState(
            identity!,
            displayName,
            enabled,
            CalculateRevision(identity!, enabled));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterImprovementActiveState? current,
        string? expectedRevision,
        bool enabled)
        => current is not null
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Enabled != enabled;

    public static bool IsValidIdentity(CharacterImprovementIdentity? identity)
        => identity is not null
            && !string.IsNullOrWhiteSpace(identity.SourceName)
            && !string.IsNullOrWhiteSpace(identity.ImprovementType)
            && !string.IsNullOrWhiteSpace(identity.ImprovementSource)
            && identity.ImprovedName is not null
            && identity.UniqueName is not null
            && identity.Target is not null
            && identity.CustomId is not null
            && identity.CustomGroup is not null;

    public static bool IdentityEquals(
        CharacterImprovementIdentity? left,
        CharacterImprovementIdentity? right)
        => left is not null
            && right is not null
            && string.Equals(left.SourceName, right.SourceName, StringComparison.Ordinal)
            && string.Equals(left.ImprovementType, right.ImprovementType, StringComparison.Ordinal)
            && string.Equals(left.ImprovementSource, right.ImprovementSource, StringComparison.Ordinal)
            && string.Equals(left.ImprovedName, right.ImprovedName, StringComparison.Ordinal)
            && string.Equals(left.UniqueName, right.UniqueName, StringComparison.Ordinal)
            && string.Equals(left.Target, right.Target, StringComparison.Ordinal)
            && string.Equals(left.CustomId, right.CustomId, StringComparison.Ordinal)
            && string.Equals(left.CustomGroup, right.CustomGroup, StringComparison.Ordinal);

    private static string CalculateRevision(CharacterImprovementIdentity identity, bool enabled)
    {
        string payload = string.Join('\0',
            identity.SourceName,
            identity.ImprovementType,
            identity.ImprovementSource,
            identity.ImprovedName,
            identity.UniqueName,
            identity.Target,
            identity.CustomId,
            identity.CustomGroup,
            enabled ? "1" : "0");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static CharacterImprovementActiveState Unavailable()
        => new(
            new CharacterImprovementIdentity(
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty),
            string.Empty,
            false,
            string.Empty);
}
