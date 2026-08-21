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
