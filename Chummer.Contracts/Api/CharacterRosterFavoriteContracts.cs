namespace Chummer.Contracts.Api;

public sealed record CharacterRosterDocumentIdentity(
    string Locator,
    string DisplayName);

public sealed record CharacterRosterFavoriteState(
    long Revision,
    IReadOnlyList<CharacterRosterDocumentIdentity> Favorites,
    IReadOnlyList<CharacterRosterDocumentIdentity> Recent)
{
    public static CharacterRosterFavoriteState Empty { get; } = new(
        Revision: 0,
        Favorites: Array.Empty<CharacterRosterDocumentIdentity>(),
        Recent: Array.Empty<CharacterRosterDocumentIdentity>());
}

public sealed record CharacterRosterFavoriteMutation(
    CharacterRosterDocumentIdentity Character,
    bool IsFavorite,
    long ExpectedRevision);

public enum CharacterRosterSortTarget
{
    Favorites,
    Recent
}

public sealed record CharacterRosterSortMutation(
    CharacterRosterSortTarget Target,
    long ExpectedRevision);
