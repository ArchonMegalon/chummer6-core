using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterImprovementGroupInsertionIdentity(
    string Name,
    int ExpectedAppendIndex);

public sealed record CharacterImprovementGroupAddEconomics(
    int KarmaDelta,
    decimal NuyenDelta);

public sealed record CharacterImprovementGroupAddState(
    IReadOnlyList<string> Groups,
    string Revision,
    CharacterImprovementGroupAddEconomics Economics);

/// <summary>
/// Exact authority for CharacterCareer's Add Improvement Group action.
/// Chummer5 appends the dialog value without trimming or duplicate rejection.
/// </summary>
public static class CharacterImprovementGroupAddRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        bool created,
        IReadOnlyList<string>? groups,
        out CharacterImprovementGroupAddState state)
    {
        state = Unavailable();
        if (!created || groups is null || groups.Any(group => group is null))
        {
            return false;
        }

        string[] snapshot = groups.ToArray();
        state = new CharacterImprovementGroupAddState(
            snapshot,
            CalculateRevision(snapshot),
            new CharacterImprovementGroupAddEconomics(0, 0m));
        return true;
    }

    public static bool TryCreateIdentity(
        CharacterImprovementGroupAddState? current,
        string? name,
        out CharacterImprovementGroupInsertionIdentity identity)
    {
        identity = new CharacterImprovementGroupInsertionIdentity(string.Empty, -1);
        if (current is null || !IsValidNewName(name))
        {
            return false;
        }

        identity = new CharacterImprovementGroupInsertionIdentity(
            name!,
            current.Groups.Count);
        return true;
    }

    public static bool TryValidateMutation(
        CharacterImprovementGroupAddState? current,
        CharacterImprovementGroupInsertionIdentity? identity,
        string? expectedRevision)
        => current is not null
            && identity is not null
            && IsValidNewName(identity.Name)
            && identity.ExpectedAppendIndex == current.Groups.Count
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && current.Economics is { KarmaDelta: 0, NuyenDelta: 0m };

    public static bool IsValidNewName(string? name)
        => name is { Length: > 0 };

    private static string CalculateRevision(string[] groups)
    {
        var payload = new StringBuilder();
        payload.Append(groups.Length).Append('\0');
        foreach (string group in groups)
        {
            payload.Append(group.Length).Append(':').Append(group).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterImprovementGroupAddState Unavailable()
        => new(
            Array.Empty<string>(),
            string.Empty,
            new CharacterImprovementGroupAddEconomics(0, 0m));
}
