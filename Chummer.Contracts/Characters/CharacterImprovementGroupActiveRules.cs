using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterImprovementGroupKind
{
    Ungrouped,
    Named
}

public sealed record CharacterImprovementGroupIdentity(
    CharacterImprovementGroupKind Kind,
    string Name);

public sealed record CharacterImprovementGroupMemberState(
    CharacterImprovementIdentity Identity,
    bool Enabled);

public sealed record CharacterImprovementGroupActiveState(
    CharacterImprovementGroupIdentity Identity,
    string DisplayName,
    IReadOnlyList<CharacterImprovementGroupMemberState> Members,
    string Revision)
{
    public int EnabledCount => Members.Count(member => member.Enabled);

    public int DisabledCount => Members.Count - EnabledCount;
}

/// <summary>
/// Exact authority for CharacterCareer's Enable All and Disable All buttons
/// on a selected level-zero custom Improvement root.
/// </summary>
public static class CharacterImprovementGroupActiveRules
{
    public const string UngroupedLegacyNodeId = "Node_SelectedImprovements";
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterImprovementGroupIdentity? identity,
        bool created,
        string? displayName,
        IReadOnlyList<CharacterImprovementGroupMemberState>? members,
        out CharacterImprovementGroupActiveState state)
    {
        state = Unavailable();
        if (!created
            || !IsValidIdentity(identity)
            || displayName is null
            || members is null
            || members.Any(member =>
                !CharacterImprovementActiveRules.IsValidIdentity(member.Identity))
            || members.Select(member => member.Identity).Distinct().Count() != members.Count)
        {
            return false;
        }

        CharacterImprovementGroupMemberState[] snapshot = members.ToArray();
        state = new CharacterImprovementGroupActiveState(
            identity!,
            displayName,
            snapshot,
            CalculateRevision(identity!, snapshot));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterImprovementGroupActiveState? current,
        string? expectedRevision,
        bool enabled)
        => current is not null
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Members.Any(member => member.Enabled != enabled);

    public static bool IsValidIdentity(CharacterImprovementGroupIdentity? identity)
        => identity is not null
            && identity.Name is not null
            && identity.Kind switch
            {
                CharacterImprovementGroupKind.Ungrouped => identity.Name.Length == 0,
                CharacterImprovementGroupKind.Named => !string.IsNullOrWhiteSpace(identity.Name)
                    && !string.Equals(
                        identity.Name,
                        UngroupedLegacyNodeId,
                        StringComparison.Ordinal),
                _ => false
            };

    public static bool IdentityEquals(
        CharacterImprovementGroupIdentity? left,
        CharacterImprovementGroupIdentity? right)
        => left is not null
            && right is not null
            && left.Kind == right.Kind
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal);

    public static bool Includes(
        CharacterImprovementGroupIdentity identity,
        bool custom,
        string? customGroup)
        => custom
            && IsValidIdentity(identity)
            && string.Equals(
                customGroup ?? string.Empty,
                identity.Kind == CharacterImprovementGroupKind.Ungrouped
                    ? string.Empty
                    : identity.Name,
                StringComparison.Ordinal);

    private static string CalculateRevision(
        CharacterImprovementGroupIdentity identity,
        IReadOnlyList<CharacterImprovementGroupMemberState> members)
    {
        var payload = new StringBuilder();
        payload.Append(identity.Kind).Append('\0').Append(identity.Name);
        foreach (CharacterImprovementGroupMemberState member in members)
        {
            CharacterImprovementIdentity item = member.Identity;
            payload.Append('\0').Append(item.SourceName)
                .Append('\0').Append(item.ImprovementType)
                .Append('\0').Append(item.ImprovementSource)
                .Append('\0').Append(item.ImprovedName)
                .Append('\0').Append(item.UniqueName)
                .Append('\0').Append(item.Target)
                .Append('\0').Append(item.CustomId)
                .Append('\0').Append(item.CustomGroup)
                .Append('\0').Append(member.Enabled ? '1' : '0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterImprovementGroupActiveState Unavailable()
        => new(
            new CharacterImprovementGroupIdentity(
                CharacterImprovementGroupKind.Ungrouped,
                string.Empty),
            string.Empty,
            Array.Empty<CharacterImprovementGroupMemberState>(),
            string.Empty);
}
