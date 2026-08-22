using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public sealed record CharacterMartialArtNotesIdentity(
    Guid MartialArtId,
    Guid? TechniqueId)
{
    public bool IsTechnique => TechniqueId.HasValue;
}

public sealed record CharacterMartialArtNotesEconomics(
    int KarmaDelta,
    decimal NuyenDelta);

public sealed record CharacterMartialArtNotesState(
    CharacterMartialArtNotesIdentity Identity,
    bool Created,
    string MartialArtName,
    string TargetName,
    string Notes,
    string NotesColor,
    string Revision,
    CharacterMartialArtNotesEconomics Economics);

/// <summary>
/// Exact authority for CharacterCreate/CharacterCareer.tsMartialArtsNotes.
/// Both modes edit notes and legacy HTML color together with no economics.
/// </summary>
public static class CharacterMartialArtNotesRules
{
    public const int RevisionHexLength = 64;

    public static bool IsValidIdentity(CharacterMartialArtNotesIdentity? identity)
        => identity is not null
            && identity.MartialArtId != Guid.Empty
            && identity.TechniqueId != Guid.Empty;

    public static bool TryCreateState(
        CharacterMartialArtNotesIdentity? identity,
        bool created,
        string? martialArtName,
        string? targetName,
        string? notes,
        string? notesColor,
        out CharacterMartialArtNotesState state)
    {
        state = Unavailable();
        if (identity is null
            || identity.MartialArtId == Guid.Empty
            || identity.TechniqueId == Guid.Empty
            || martialArtName is null
            || targetName is null
            || notes is null
            || !CharacterImprovementNotesRules.IsValidLegacyHtmlColor(notesColor))
        {
            return false;
        }

        state = new CharacterMartialArtNotesState(
            identity,
            created,
            martialArtName,
            targetName,
            notes,
            notesColor!,
            CalculateRevision(identity, created, martialArtName, targetName, notes, notesColor!),
            new CharacterMartialArtNotesEconomics(0, 0m));
        return true;
    }

    public static bool TryValidateMutation(
        CharacterMartialArtNotesState? current,
        CharacterMartialArtNotesIdentity? identity,
        string? expectedRevision,
        string? notes,
        string? notesColor)
        => current is not null
            && identity is not null
            && current.Identity == identity
            && expectedRevision is { Length: RevisionHexLength }
            && string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal)
            && notes is not null
            && CharacterImprovementNotesRules.CanSetLegacyHtmlColor(current.NotesColor, notesColor)
            && current.Economics is { KarmaDelta: 0, NuyenDelta: 0m }
            && (!string.Equals(current.Notes, notes, StringComparison.Ordinal)
                || !string.Equals(current.NotesColor, notesColor, StringComparison.Ordinal));

    private static string CalculateRevision(
        CharacterMartialArtNotesIdentity identity,
        bool created,
        string martialArtName,
        string targetName,
        string notes,
        string notesColor)
    {
        string payload = string.Join('\0',
            identity.MartialArtId.ToString("D"),
            identity.TechniqueId?.ToString("D") ?? string.Empty,
            created.ToString(),
            martialArtName,
            targetName,
            notes,
            notesColor);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))
            .ToLowerInvariant();
    }

    private static CharacterMartialArtNotesState Unavailable()
        => new(
            new CharacterMartialArtNotesIdentity(Guid.Empty, null),
            false,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new CharacterMartialArtNotesEconomics(0, 0m));
}
