using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterImprovementNotesState(
    CharacterImprovementIdentity Identity,
    string DisplayName,
    string Notes,
    string NotesColor,
    string Revision);

/// <summary>
/// Exact authority for CharacterCareer.tsImprovementNotes. Chummer5 exposes
/// the notes editor only for a directly selected Improvement and commits the
/// note text and legacy HTML notes color together.
/// </summary>
public static class CharacterImprovementNotesRules
{
    public const int RevisionHexLength = 64;

    public static bool TryCreateState(
        CharacterImprovementIdentity? identity,
        bool created,
        string? displayName,
        string? notes,
        string? notesColor,
        out CharacterImprovementNotesState state)
    {
        state = Unavailable();
        if (!created
            || !CharacterImprovementActiveRules.IsValidIdentity(identity)
            || displayName is null
            || notes is null
            || !IsValidLegacyHtmlColor(notesColor))
        {
            return false;
        }

        state = new CharacterImprovementNotesState(
            identity!,
            displayName,
            notes,
            notesColor!,
            CalculateRevision(identity!, notes, notesColor!));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterImprovementNotesState? current,
        string? expectedRevision,
        string? notes,
        string? notesColor)
        => current is not null
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && notes is not null
            && CanSetLegacyHtmlColor(current.NotesColor, notesColor)
            && (!string.Equals(current.Notes, notes, StringComparison.Ordinal)
                || !string.Equals(current.NotesColor, notesColor, StringComparison.Ordinal));

    public static bool IsValidLegacyHtmlColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }
        if (value.Length == 7 && value[0] == '#')
        {
            return value.AsSpan(1).ToString().All(Uri.IsHexDigit);
        }
        return value.All(static character =>
            character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
    }

    /// <summary>
    /// Arbitrary colors are persisted in Chummer5's canonical #RRGGBB form.
    /// A saved legacy name may be preserved during a notes-only edit, but the
    /// phone must not invent an unvalidated ColorTranslator name.
    /// </summary>
    public static bool CanSetLegacyHtmlColor(string current, string? requested)
        => requested is { Length: 7 } && requested[0] == '#'
            ? requested.AsSpan(1).ToString().All(Uri.IsHexDigit)
            : IsValidLegacyHtmlColor(requested)
                && string.Equals(current, requested, StringComparison.Ordinal);

    private static string CalculateRevision(
        CharacterImprovementIdentity identity,
        string notes,
        string notesColor)
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
            notes,
            notesColor);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static CharacterImprovementNotesState Unavailable()
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
            string.Empty,
            string.Empty,
            string.Empty);
}
