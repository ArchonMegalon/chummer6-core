using Chummer.Contracts.Api;

namespace Chummer.Application.Tools;

/// <summary>
/// Exact Chummer5 CharacterRoster favorite/MRU rules, expressed without registry or UI dependencies.
/// </summary>
public static class CharacterRosterFavoriteRules
{
    public const int MaximumEntries = 10;

    public static bool IsFavorite(
        CharacterRosterFavoriteState state,
        CharacterRosterDocumentIdentity character)
    {
        CharacterRosterFavoriteState normalized = ValidateAndNormalize(state);
        CharacterRosterDocumentIdentity identity = NormalizeIdentity(character);
        return normalized.Favorites.Any(item => SameLocator(item, identity));
    }

    public static CharacterRosterFavoriteState Apply(
        CharacterRosterFavoriteState current,
        CharacterRosterFavoriteMutation mutation)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(mutation);
        if (current.Revision < 0)
            throw new InvalidDataException("Roster favorite revision cannot be negative.");
        if (mutation.ExpectedRevision != current.Revision)
        {
            throw new InvalidOperationException(
                $"Roster favorites changed at revision {current.Revision}; expected {mutation.ExpectedRevision}.");
        }

        CharacterRosterDocumentIdentity character = NormalizeIdentity(mutation.Character);
        List<CharacterRosterDocumentIdentity> favorites = NormalizeCollection(current.Favorites, "favorites");
        List<CharacterRosterDocumentIdentity> recent = NormalizeCollection(current.Recent, "recent");

        bool alreadyFavorite = favorites.Any(item => SameLocator(item, character));
        if (alreadyFavorite == mutation.IsFavorite)
            return new CharacterRosterFavoriteState(current.Revision, favorites, recent);

        favorites.RemoveAll(item => SameLocator(item, character));
        if (mutation.IsFavorite)
        {
            favorites.Add(character);
            favorites.Sort(CompareFavorites);
            Trim(favorites);
        }
        else
        {
            recent.RemoveAll(item => SameLocator(item, character));
            recent.Insert(0, character);
            Trim(recent);
        }

        return new CharacterRosterFavoriteState(current.Revision + 1, favorites, recent);
    }

    public static CharacterRosterFavoriteState ValidateAndNormalize(CharacterRosterFavoriteState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.Revision < 0)
            throw new InvalidDataException("Roster favorite revision cannot be negative.");

        List<CharacterRosterDocumentIdentity> favorites = NormalizeCollection(state.Favorites, "favorites");
        List<CharacterRosterDocumentIdentity> recent = NormalizeCollection(state.Recent, "recent");
        if (favorites.Count > MaximumEntries || recent.Count > MaximumEntries)
            throw new InvalidDataException($"Roster favorite and recent collections are limited to {MaximumEntries} entries.");
        return new CharacterRosterFavoriteState(state.Revision, favorites, recent);
    }

    private static CharacterRosterDocumentIdentity NormalizeIdentity(CharacterRosterDocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        string locator = identity.Locator?.Trim() ?? string.Empty;
        string displayName = identity.DisplayName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(locator))
            throw new ArgumentException("A character document locator is required.", nameof(identity));
        if (!Uri.TryCreate(locator, UriKind.Absolute, out _) && !Path.IsPathFullyQualified(locator))
            throw new ArgumentException("Character document locator must be an absolute URI or file path.", nameof(identity));
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException("A character display name is required.", nameof(identity));
        return new CharacterRosterDocumentIdentity(locator, displayName);
    }

    private static List<CharacterRosterDocumentIdentity> NormalizeCollection(
        IReadOnlyList<CharacterRosterDocumentIdentity>? items,
        string label)
    {
        if (items is null)
            throw new InvalidDataException($"Roster {label} collection is required.");
        List<CharacterRosterDocumentIdentity> normalized = [];
        foreach (CharacterRosterDocumentIdentity item in items)
        {
            CharacterRosterDocumentIdentity candidate;
            try
            {
                candidate = NormalizeIdentity(item);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException($"Roster {label} contains an invalid identity.", exception);
            }
            if (normalized.Any(existing => SameLocator(existing, candidate)))
                throw new InvalidDataException($"Roster {label} contains a duplicate locator.");
            normalized.Add(candidate);
        }
        return normalized;
    }

    private static bool SameLocator(CharacterRosterDocumentIdentity left, CharacterRosterDocumentIdentity right)
    {
        if (Uri.TryCreate(left.Locator, UriKind.Absolute, out Uri? leftUri)
            && Uri.TryCreate(right.Locator, UriKind.Absolute, out Uri? rightUri))
        {
            if (leftUri.IsFile && rightUri.IsFile)
                return string.Equals(leftUri.LocalPath, rightUri.LocalPath, StringComparison.OrdinalIgnoreCase);
            return leftUri.Equals(rightUri);
        }
        return string.Equals(left.Locator, right.Locator, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareFavorites(CharacterRosterDocumentIdentity left, CharacterRosterDocumentIdentity right)
    {
        int byName = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        return byName != 0 ? byName : StringComparer.OrdinalIgnoreCase.Compare(left.Locator, right.Locator);
    }

    private static void Trim(List<CharacterRosterDocumentIdentity> items)
    {
        if (items.Count > MaximumEntries)
            items.RemoveRange(MaximumEntries, items.Count - MaximumEntries);
    }
}
