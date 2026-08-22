using System.Text.Json;
using Chummer.Application.Tools;
using Chummer.Contracts.Api;
using Chummer.Infrastructure.Files;

namespace Chummer.Tests;

[TestFixture]
public sealed class ApplicationDeleteConfirmationRulesTests
{
    [Test]
    public void Default_matches_Chummer5_confirmdelete_true()
    {
        Assert.IsTrue(ApplicationDeleteConfirmationState.Default.ConfirmDelete);
        Assert.IsTrue(ApplicationDeleteConfirmationState.Default.ConfirmKarmaExpense);
        Assert.AreEqual(0, ApplicationDeleteConfirmationState.Default.Revision);
        Assert.AreEqual("confirmdelete", ApplicationDeleteConfirmationRules.LegacyIdentity);
        Assert.AreEqual("confirmkarmaexpense", ApplicationDeleteConfirmationRules.LegacyKarmaExpenseIdentity);
    }

    [Test]
    public void Apply_requires_exact_identity_and_revision()
    {
        ApplicationDeleteConfirmationState updated = ApplicationDeleteConfirmationRules.Apply(
            ApplicationDeleteConfirmationState.Default,
            new ApplicationDeleteConfirmationMutation(
                ApplicationSettingIdentity.ConfirmDelete,
                Value: false,
                ExpectedRevision: 0));

        Assert.IsFalse(updated.ConfirmDelete);
        Assert.IsTrue(updated.ConfirmKarmaExpense);
        Assert.AreEqual(1, updated.Revision);
        Assert.Throws<InvalidOperationException>(() => ApplicationDeleteConfirmationRules.Apply(
            updated,
            new ApplicationDeleteConfirmationMutation(
                ApplicationSettingIdentity.ConfirmDelete,
                Value: true,
                ExpectedRevision: 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApplicationDeleteConfirmationRules.Apply(
            updated,
            new ApplicationDeleteConfirmationMutation(
                (ApplicationSettingIdentity)99,
                Value: true,
                ExpectedRevision: 1)));
    }

    [Test]
    public void ApplySnapshot_commits_both_confirmation_drafts_once_with_one_revision_CAS()
    {
        ApplicationDeleteConfirmationState updated = ApplicationDeleteConfirmationRules.ApplySnapshot(
            ApplicationDeleteConfirmationState.Default,
            new ApplicationConfirmationSettingsMutation(
                ConfirmDelete: false,
                ConfirmKarmaExpense: false,
                ExpectedRevision: 0));

        Assert.AreEqual(1, updated.Revision);
        Assert.IsFalse(updated.ConfirmDelete);
        Assert.IsFalse(updated.ConfirmKarmaExpense);
        Assert.Throws<InvalidOperationException>(() => ApplicationDeleteConfirmationRules.ApplySnapshot(
            updated,
            new ApplicationConfirmationSettingsMutation(true, true, ExpectedRevision: 0)));
    }

    [Test]
    public void File_store_is_revision_safe_restart_safe_and_recovers_newest_valid_commit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-delete-confirmation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileApplicationDeleteConfirmationStore store = new(directory);
            Assert.IsTrue(store.Load().ConfirmDelete);

            ApplicationDeleteConfirmationState first = ApplicationDeleteConfirmationRules.Apply(
                store.Load(),
                new ApplicationDeleteConfirmationMutation(
                    ApplicationSettingIdentity.ConfirmDelete,
                    Value: false,
                    ExpectedRevision: 0));
            store.Save(0, first);
            Assert.IsFalse(new FileApplicationDeleteConfirmationStore(directory).Load().ConfirmDelete);
            Assert.IsTrue(new FileApplicationDeleteConfirmationStore(directory).Load().ConfirmKarmaExpense);

            ApplicationDeleteConfirmationState second = ApplicationDeleteConfirmationRules.Apply(
                first,
                new ApplicationDeleteConfirmationMutation(
                    ApplicationSettingIdentity.ConfirmDelete,
                    Value: true,
                    ExpectedRevision: 1));
            store.Save(1, second);
            Assert.Throws<InvalidOperationException>(() => store.Save(1, second));

            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{");
            ApplicationDeleteConfirmationState recovered = new FileApplicationDeleteConfirmationStore(directory).Load();
            Assert.AreEqual(1, recovered.Revision);
            Assert.IsFalse(recovered.ConfirmDelete);

            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(
                new ApplicationDeleteConfirmationState(3, ConfirmDelete: true)));
            File.WriteAllBytes(path + ".bak", JsonSerializer.SerializeToUtf8Bytes(
                new ApplicationDeleteConfirmationState(4, ConfirmDelete: false)));
            ApplicationDeleteConfirmationState newest = new FileApplicationDeleteConfirmationStore(directory).Load();
            Assert.AreEqual(4, newest.Revision);
            Assert.IsFalse(newest.ConfirmDelete);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void File_store_migrates_legacy_missing_karma_confirmation_to_true()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-delete-confirmation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{\"Revision\":9,\"ConfirmDelete\":false}");

            ApplicationDeleteConfirmationState migrated = new FileApplicationDeleteConfirmationStore(directory).Load();

            Assert.AreEqual(9, migrated.Revision);
            Assert.IsFalse(migrated.ConfirmDelete);
            Assert.IsTrue(migrated.ConfirmKarmaExpense);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void File_store_fails_closed_when_both_copies_are_invalid_or_ambiguous()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-delete-confirmation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{");
            File.WriteAllText(path + ".bak", "[");
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());

            File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(
                new ApplicationDeleteConfirmationState(7, ConfirmDelete: true)));
            File.WriteAllBytes(path + ".bak", JsonSerializer.SerializeToUtf8Bytes(
                new ApplicationDeleteConfirmationState(7, ConfirmDelete: false)));
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
