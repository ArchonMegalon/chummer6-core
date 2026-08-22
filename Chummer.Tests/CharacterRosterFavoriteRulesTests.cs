using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Infrastructure.Files;

namespace Chummer.Tests;

[TestFixture]
public sealed class CharacterRosterFavoriteRulesTests
{
    [Test]
    public void Toggle_matches_Chummer5_sorted_favorites_and_front_of_MRU_rules()
    {
        CharacterRosterDocumentIdentity zed = new("content://runner/zed", "Zed");
        CharacterRosterDocumentIdentity alpha = new("content://runner/alpha", "Alpha");

        CharacterRosterFavoriteState initial = new(0, [], [zed]);
        CharacterRosterFavoriteState first = CharacterRosterFavoriteRules.Apply(
            initial,
            new CharacterRosterFavoriteMutation(zed, IsFavorite: true, ExpectedRevision: 0));
        CharacterRosterFavoriteState second = CharacterRosterFavoriteRules.Apply(
            first,
            new CharacterRosterFavoriteMutation(alpha, IsFavorite: true, ExpectedRevision: 1));
        CharacterRosterFavoriteState third = CharacterRosterFavoriteRules.Apply(
            second,
            new CharacterRosterFavoriteMutation(alpha, IsFavorite: false, ExpectedRevision: 2));

        CollectionAssert.AreEqual(new[] { "Zed" }, third.Favorites.Select(item => item.DisplayName).ToArray());
        CollectionAssert.AreEqual(new[] { "Alpha", "Zed" }, third.Recent.Select(item => item.DisplayName).ToArray());
        Assert.AreEqual(3, third.Revision);
    }

    [Test]
    public void Apply_rejects_stale_revision_without_returning_mutated_state()
    {
        CharacterRosterFavoriteMutation stale = new(
            new CharacterRosterDocumentIdentity("content://runner/alpha", "Alpha"),
            IsFavorite: true,
            ExpectedRevision: 4);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => CharacterRosterFavoriteRules.Apply(CharacterRosterFavoriteState.Empty, stale))!;
        StringAssert.Contains("expected 4", error.Message);
    }

    [Test]
    public void Sort_matches_Chummer5_locator_order_and_changes_only_selected_collection()
    {
        CharacterRosterDocumentIdentity favoriteZed = new("content://runner/zed", "Alpha display");
        CharacterRosterDocumentIdentity favoriteAlpha = new("content://runner/alpha", "Zulu display");
        CharacterRosterDocumentIdentity recentYankee = new("content://runner/yankee", "Beta display");
        CharacterRosterDocumentIdentity recentBravo = new("content://runner/bravo", "Omega display");
        CharacterRosterFavoriteState initial = new(
            7,
            [favoriteZed, favoriteAlpha],
            [recentYankee, recentBravo]);

        CharacterRosterFavoriteState favoritesSorted = CharacterRosterFavoriteRules.ApplySort(
            initial,
            new CharacterRosterSortMutation(CharacterRosterSortTarget.Favorites, ExpectedRevision: 7));
        CollectionAssert.AreEqual(
            new[] { favoriteAlpha.Locator, favoriteZed.Locator },
            favoritesSorted.Favorites.Select(item => item.Locator).ToArray());
        CollectionAssert.AreEqual(initial.Recent, favoritesSorted.Recent);
        Assert.AreEqual(8, favoritesSorted.Revision);

        CharacterRosterFavoriteState recentSorted = CharacterRosterFavoriteRules.ApplySort(
            favoritesSorted,
            new CharacterRosterSortMutation(CharacterRosterSortTarget.Recent, ExpectedRevision: 8));
        CollectionAssert.AreEqual(favoritesSorted.Favorites, recentSorted.Favorites);
        CollectionAssert.AreEqual(
            new[] { recentBravo.Locator, recentYankee.Locator },
            recentSorted.Recent.Select(item => item.Locator).ToArray());
        Assert.AreEqual(9, recentSorted.Revision);
    }

    [Test]
    public void Sort_fails_closed_for_stale_revision_and_unknown_target()
    {
        CharacterRosterFavoriteState state = new(
            2,
            [new CharacterRosterDocumentIdentity("content://runner/zed", "Zed")],
            []);

        Assert.Throws<InvalidOperationException>(() => CharacterRosterFavoriteRules.ApplySort(
            state,
            new CharacterRosterSortMutation(CharacterRosterSortTarget.Favorites, ExpectedRevision: 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CharacterRosterFavoriteRules.ApplySort(
            state,
            new CharacterRosterSortMutation((CharacterRosterSortTarget)99, ExpectedRevision: 2)));
    }

    [Test]
    public void Sorted_state_uses_atomic_revision_store_and_backup_recovery()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-roster-sort-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileCharacterRosterFavoriteStore store = new(directory);
            CharacterRosterFavoriteState unsorted = new(
                1,
                [
                    new CharacterRosterDocumentIdentity("content://runner/zed", "Alpha display"),
                    new CharacterRosterDocumentIdentity("content://runner/alpha", "Zulu display")
                ],
                []);
            store.Save(0, unsorted);
            CharacterRosterFavoriteState sorted = CharacterRosterFavoriteRules.ApplySort(
                store.Load(),
                new CharacterRosterSortMutation(CharacterRosterSortTarget.Favorites, ExpectedRevision: 1));
            store.Save(1, sorted);

            Assert.AreEqual(2, store.Load().Revision);
            Assert.AreEqual("content://runner/alpha", store.Load().Favorites[0].Locator);
            Assert.Throws<InvalidOperationException>(() => store.Save(1, sorted));

            string primary = Directory.GetFiles(directory, "roster-favorites.json", SearchOption.AllDirectories).Single();
            File.WriteAllText(primary, "{broken");
            CharacterRosterFavoriteState recovered = store.Load();
            Assert.AreEqual(1, recovered.Revision);
            Assert.AreEqual("content://runner/zed", recovered.Favorites[0].Locator);
            StringAssert.DoesNotContain("{broken", File.ReadAllText(primary));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void File_store_is_atomic_revision_checked_and_recovers_from_backup()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-roster-favorites-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileCharacterRosterFavoriteStore store = new(directory);
            CharacterRosterDocumentIdentity runner = new("content://runner/alpha", "Alpha");
            CharacterRosterFavoriteState favorite = CharacterRosterFavoriteRules.Apply(
                store.Load(),
                new CharacterRosterFavoriteMutation(runner, IsFavorite: true, ExpectedRevision: 0));
            store.Save(0, favorite);
            CharacterRosterFavoriteState recent = CharacterRosterFavoriteRules.Apply(
                store.Load(),
                new CharacterRosterFavoriteMutation(runner, IsFavorite: false, ExpectedRevision: 1));
            store.Save(1, recent);

            Assert.Throws<InvalidOperationException>(() => store.Save(0, favorite));
            string primary = Directory.GetFiles(directory, "roster-favorites.json", SearchOption.AllDirectories).Single();
            File.WriteAllText(primary, "{broken");

            CharacterRosterFavoriteState recovered = store.Load();
            Assert.AreEqual(1, recovered.Revision);
            Assert.AreEqual("Alpha", recovered.Favorites.Single().DisplayName);
            StringAssert.DoesNotContain("{broken", File.ReadAllText(primary));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
