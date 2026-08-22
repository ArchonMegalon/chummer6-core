using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

/// <summary>
/// Stable identity for one position in Chummer5's ordered mugshot collection.
/// Mugshots do not carry GUIDs in the native file, so both the exact zero-based
/// position and the decoded image digest are required.
/// </summary>
public sealed record CharacterMugshotIdentity(
    int ZeroBasedIndex,
    string ImageSha256);

public sealed record CharacterCareerMugshotState(
    IReadOnlyList<CharacterMugshotIdentity> Mugshots,
    int MainMugshotIndex,
    int DefaultSelectedOneBasedIndex,
    string Revision)
{
    public bool HasMugshots => Mugshots.Count > 0;
}

/// <summary>
/// Exact authority for CharacterCareer.nudMugshotIndex and
/// CharacterCareer.chkIsMainMugshot plus the collection mutation performed by
/// CharacterCareer.cmdDeleteMugshot. The spinner is a transient 1-based
/// selector with wraparound (or zero for an empty collection); the checkbox
/// writes a 0-based main index or -1, while deletion preserves exact ordered
/// identity and applies Chummer5's main-index adjustment rules.
/// </summary>
public static class CharacterCareerMugshotRules
{
    public const int Sha256HexLength = 64;

    public static bool TryCreateIdentity(
        int zeroBasedIndex,
        ReadOnlySpan<byte> imageBytes,
        out CharacterMugshotIdentity identity)
    {
        identity = new CharacterMugshotIdentity(-1, string.Empty);
        if (zeroBasedIndex < 0 || imageBytes.IsEmpty)
        {
            return false;
        }

        identity = new CharacterMugshotIdentity(
            zeroBasedIndex,
            Convert.ToHexString(SHA256.HashData(imageBytes)).ToLowerInvariant());
        return true;
    }

    public static bool TryCreateState(
        bool created,
        IReadOnlyList<CharacterMugshotIdentity>? mugshots,
        int mainMugshotIndex,
        out CharacterCareerMugshotState state)
    {
        state = Unavailable();
        if (!created || mugshots is null || !HasExactOrderedIdentity(mugshots))
        {
            return false;
        }
        if (mainMugshotIndex < -1 || mainMugshotIndex >= mugshots.Count)
        {
            return false;
        }

        int selected = mugshots.Count == 0
            ? 0
            : Math.Max(mainMugshotIndex, 0) + 1;
        CharacterMugshotIdentity[] exact = mugshots.ToArray();
        state = new CharacterCareerMugshotState(
            Array.AsReadOnly(exact),
            mainMugshotIndex,
            selected,
            CalculateRevision(exact, mainMugshotIndex));
        return true;
    }

    public static int WrapSelection(
        CharacterCareerMugshotState state,
        int requestedOneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Mugshots.Count == 0)
        {
            return 0;
        }
        if (requestedOneBasedIndex < 1)
        {
            return state.Mugshots.Count;
        }
        if (requestedOneBasedIndex > state.Mugshots.Count)
        {
            return 1;
        }
        return requestedOneBasedIndex;
    }

    public static bool IsSelectedMain(
        CharacterCareerMugshotState state,
        int selectedOneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Mugshots.Count > 0
            && WrapSelection(state, selectedOneBasedIndex) - 1 == state.MainMugshotIndex;
    }

    public static CharacterMugshotIdentity? ResolveSelection(
        CharacterCareerMugshotState state,
        int selectedOneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        int selected = WrapSelection(state, selectedOneBasedIndex);
        return selected == 0 ? null : state.Mugshots[selected - 1];
    }

    public static bool TryValidateMainMutation(
        CharacterCareerMugshotState? current,
        CharacterMugshotIdentity? selectedIdentity,
        string? expectedRevision,
        bool isMain)
    {
        if (current is null
            || selectedIdentity is null
            || expectedRevision is not { Length: Sha256HexLength }
            || !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            || !IsValidIdentity(selectedIdentity)
            || selectedIdentity.ZeroBasedIndex >= current.Mugshots.Count
            || current.Mugshots[selectedIdentity.ZeroBasedIndex] != selectedIdentity)
        {
            return false;
        }

        bool currentlyMain = current.MainMugshotIndex == selectedIdentity.ZeroBasedIndex;
        return currentlyMain != isMain;
    }

    public static int ApplyMainMutation(
        CharacterCareerMugshotState current,
        CharacterMugshotIdentity selectedIdentity,
        string expectedRevision,
        bool isMain)
    {
        if (!TryValidateMainMutation(current, selectedIdentity, expectedRevision, isMain))
        {
            throw new InvalidOperationException(
                "The selected mugshot, main state, collection order, or local revision changed; reopen before saving.");
        }
        return isMain ? selectedIdentity.ZeroBasedIndex : -1;
    }

    public static bool TryValidateDelete(
        CharacterCareerMugshotState? current,
        CharacterMugshotIdentity? selectedIdentity,
        string? expectedRevision)
    {
        if (current is null
            || selectedIdentity is null
            || expectedRevision is not { Length: Sha256HexLength }
            || !string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            || !IsValidIdentity(selectedIdentity)
            || selectedIdentity.ZeroBasedIndex >= current.Mugshots.Count)
        {
            return false;
        }

        return current.Mugshots[selectedIdentity.ZeroBasedIndex] == selectedIdentity;
    }

    public static int ApplyDeleteMainIndex(
        CharacterCareerMugshotState current,
        CharacterMugshotIdentity selectedIdentity,
        string expectedRevision)
    {
        if (!TryValidateDelete(current, selectedIdentity, expectedRevision))
        {
            throw new InvalidOperationException(
                "The selected mugshot, collection order, or local revision changed; reopen before deleting.");
        }

        if (selectedIdentity.ZeroBasedIndex == current.MainMugshotIndex)
        {
            return -1;
        }
        return selectedIdentity.ZeroBasedIndex < current.MainMugshotIndex
            ? current.MainMugshotIndex - 1
            : current.MainMugshotIndex;
    }

    private static bool HasExactOrderedIdentity(IReadOnlyList<CharacterMugshotIdentity> mugshots)
    {
        for (int index = 0; index < mugshots.Count; index++)
        {
            CharacterMugshotIdentity? identity = mugshots[index];
            if (!IsValidIdentity(identity) || identity.ZeroBasedIndex != index)
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidIdentity(CharacterMugshotIdentity? identity)
        => identity is { ZeroBasedIndex: >= 0 }
            && identity.ImageSha256 is { Length: Sha256HexLength }
            && identity.ImageSha256.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string CalculateRevision(
        CharacterMugshotIdentity[] mugshots,
        int mainMugshotIndex)
    {
        var payload = new StringBuilder();
        payload.Append(mainMugshotIndex).Append('\0').Append(mugshots.Length).Append('\0');
        foreach (CharacterMugshotIdentity mugshot in mugshots)
        {
            payload.Append(mugshot.ZeroBasedIndex).Append('\0')
                .Append(mugshot.ImageSha256).Append('\0');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ToString())))
            .ToLowerInvariant();
    }

    private static CharacterCareerMugshotState Unavailable()
        => new(Array.Empty<CharacterMugshotIdentity>(), -1, 0, string.Empty);
}
