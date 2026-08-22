using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationMugshotState(
    IReadOnlyList<CharacterMugshotIdentity> Mugshots,
    int MainMugshotIndex,
    int DefaultSelectedOneBasedIndex,
    string Revision)
{
    public bool HasMugshots => Mugshots.Count > 0;
}

/// <summary>
/// Exact authority for CharacterCreate.nudMugshotIndex and
/// CharacterCreate.chkIsMainMugshot. Creation uses the same transient
/// one-based selector as Career, but this phase-explicit contract accepts only
/// an uncreated runner. The checkbox persists the selected zero-based main
/// index, or -1 when the selected main portrait is cleared.
/// </summary>
public static class CharacterCreationMugshotRules
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
        out CharacterCreationMugshotState state)
    {
        state = Unavailable();
        if (created || mugshots is null || !HasExactOrderedIdentity(mugshots))
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
        state = new CharacterCreationMugshotState(
            Array.AsReadOnly(exact),
            mainMugshotIndex,
            selected,
            CalculateRevision(exact, mainMugshotIndex));
        return true;
    }

    public static int WrapSelection(
        CharacterCreationMugshotState state,
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
        CharacterCreationMugshotState state,
        int selectedOneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Mugshots.Count > 0
            && WrapSelection(state, selectedOneBasedIndex) - 1 == state.MainMugshotIndex;
    }

    public static CharacterMugshotIdentity? ResolveSelection(
        CharacterCreationMugshotState state,
        int selectedOneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        int selected = WrapSelection(state, selectedOneBasedIndex);
        return selected == 0 ? null : state.Mugshots[selected - 1];
    }

    public static bool TryValidateMainMutation(
        CharacterCreationMugshotState? current,
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
        CharacterCreationMugshotState current,
        CharacterMugshotIdentity selectedIdentity,
        string expectedRevision,
        bool isMain)
    {
        if (!TryValidateMainMutation(current, selectedIdentity, expectedRevision, isMain))
        {
            throw new InvalidOperationException(
                "The selected Creation mugshot, main state, collection order, or local revision changed; reopen before saving.");
        }
        return isMain ? selectedIdentity.ZeroBasedIndex : -1;
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

    private static CharacterCreationMugshotState Unavailable()
        => new(Array.Empty<CharacterMugshotIdentity>(), -1, 0, string.Empty);
}
