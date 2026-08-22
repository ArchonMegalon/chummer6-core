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
        Assert.IsFalse(ApplicationDeleteConfirmationState.Default.CustomDateTimeFormats);
        Assert.AreEqual(string.Empty, ApplicationDeleteConfirmationState.Default.CustomDateFormat);
        Assert.AreEqual(string.Empty, ApplicationDeleteConfirmationState.Default.CustomTimeFormat);
        Assert.IsTrue(ApplicationDeleteConfirmationState.Default.DatesIncludeTime);
        Assert.IsFalse(ApplicationDeleteConfirmationState.Default.HideMasterIndex);
        Assert.IsFalse(ApplicationDeleteConfirmationState.Default.HideCharacterRoster);
        Assert.IsTrue(ApplicationDeleteConfirmationState.Default.SearchInCategoryOnly);
        Assert.IsFalse(ApplicationDeleteConfirmationState.Default.AllowEasterEggs);
        Assert.IsFalse(ApplicationDeleteConfirmationState.Default.LiveUpdateCleanCharacterFiles);
        Assert.AreEqual(0, ApplicationDeleteConfirmationState.Default.Revision);
        Assert.AreEqual("confirmdelete", ApplicationDeleteConfirmationRules.LegacyIdentity);
        Assert.AreEqual("confirmkarmaexpense", ApplicationDeleteConfirmationRules.LegacyKarmaExpenseIdentity);
        Assert.AreEqual("usecustomdatetime", ApplicationDeleteConfirmationRules.LegacyCustomDateTimeFormatsIdentity);
        Assert.AreEqual("customdateformat", ApplicationDeleteConfirmationRules.LegacyCustomDateFormatIdentity);
        Assert.AreEqual("customtimeformat", ApplicationDeleteConfirmationRules.LegacyCustomTimeFormatIdentity);
        Assert.AreEqual("datesincludetime", ApplicationDeleteConfirmationRules.LegacyDatesIncludeTimeIdentity);
        Assert.AreEqual("hidemasterindex", ApplicationDeleteConfirmationRules.LegacyHideMasterIndexIdentity);
        Assert.AreEqual("hidecharacterroster", ApplicationDeleteConfirmationRules.LegacyHideCharacterRosterIdentity);
        Assert.AreEqual("searchincategoryonly", ApplicationDeleteConfirmationRules.LegacySearchInCategoryOnlyIdentity);
        Assert.AreEqual("alloweastereggs", ApplicationDeleteConfirmationRules.LegacyAllowEasterEggsIdentity);
        Assert.AreEqual("prefernightlybuilds", ApplicationDeleteConfirmationRules.LegacyPreferNightlyBuildsIdentity);
        Assert.AreEqual(
            "liveupdatecleancharacterfiles",
            ApplicationDeleteConfirmationRules.LegacyLiveUpdateCleanCharacterFilesIdentity);
    }

    [Test]
    public void Update_defaults_match_Chummer5_application_version_build_semantics()
    {
        ApplicationDeleteConfirmationState milestone =
            ApplicationDeleteConfirmationState.ForApplicationVersion(new Version(5, 225, 0, 17));
        ApplicationDeleteConfirmationState nightly =
            ApplicationDeleteConfirmationState.ForApplicationVersion(new Version(5, 225, 42, 17));

        Assert.IsFalse(milestone.PreferNightlyBuilds);
        Assert.IsTrue(nightly.PreferNightlyBuilds);
        Assert.IsFalse(milestone.LiveUpdateCleanCharacterFiles);
        Assert.IsFalse(nightly.LiveUpdateCleanCharacterFiles);
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
            Assert.IsFalse(migrated.CustomDateTimeFormats);
            Assert.AreEqual(string.Empty, migrated.CustomDateFormat);
            Assert.AreEqual(string.Empty, migrated.CustomTimeFormat);
            Assert.IsTrue(migrated.DatesIncludeTime);
            Assert.IsFalse(migrated.HideMasterIndex);
            Assert.IsFalse(migrated.HideCharacterRoster);
            Assert.IsTrue(migrated.SearchInCategoryOnly);
            Assert.IsFalse(migrated.AllowEasterEggs);
            Assert.IsFalse(migrated.LiveUpdateCleanCharacterFiles);
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

            File.WriteAllText(
                path,
                "{\"Revision\":0,\"ConfirmDelete\":true,\"HideMasterIndex\":\"false\"}");
            File.Delete(path + ".bak");
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());

            File.WriteAllText(
                path,
                "{\"Revision\":0,\"ConfirmDelete\":true,\"SearchInCategoryOnly\":\"true\"}");
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());

            File.WriteAllText(
                path,
                "{\"Revision\":0,\"ConfirmDelete\":true,\"PreferNightlyBuilds\":\"false\"}");
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());

            File.WriteAllText(
                path,
                "{\"Revision\":0,\"ConfirmDelete\":true,\"LiveUpdateCleanCharacterFiles\":\"false\"}");
            Assert.Throws<InvalidDataException>(() => new FileApplicationDeleteConfirmationStore(directory).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Date_time_snapshot_has_typed_identities_exact_phase_rules_and_one_revision()
    {
        ApplicationDeleteConfirmationState enabled = ApplicationDeleteConfirmationRules.ApplyDateTimeSnapshot(
            ApplicationDeleteConfirmationState.Default,
            DateTimeMutation(
                useCustom: true,
                dateFormat: "yyyy-MM-dd",
                timeFormat: "HH:mm:ss",
                datesIncludeTime: false,
                expectedRevision: 0));

        Assert.AreEqual(1, enabled.Revision);
        Assert.IsTrue(enabled.CustomDateTimeFormats);
        Assert.AreEqual("yyyy-MM-dd", enabled.CustomDateFormat);
        Assert.AreEqual("HH:mm:ss", enabled.CustomTimeFormat);
        Assert.IsFalse(enabled.DatesIncludeTime);

        ApplicationDeleteConfirmationState disabled = ApplicationDeleteConfirmationRules.ApplyDateTimeSnapshot(
            enabled,
            DateTimeMutation(
                useCustom: false,
                dateFormat: "M/d/yyyy",
                timeFormat: "h:mm tt",
                datesIncludeTime: true,
                expectedRevision: 1));
        Assert.AreEqual(2, disabled.Revision);
        Assert.IsFalse(disabled.CustomDateTimeFormats);
        Assert.AreEqual("yyyy-MM-dd", disabled.CustomDateFormat, "Disabled-phase culture previews are not persisted.");
        Assert.AreEqual("HH:mm:ss", disabled.CustomTimeFormat, "Disabled-phase culture previews are not persisted.");
        Assert.IsTrue(disabled.DatesIncludeTime);

        Assert.Throws<ArgumentException>(() => ApplicationDeleteConfirmationRules.ApplyDateTimeSnapshot(
            disabled,
            DateTimeMutation(
                useCustom: true,
                dateFormat: "d",
                timeFormat: "t",
                datesIncludeTime: true,
                expectedRevision: 2) with
            {
                CustomDateFormat = new(ApplicationSettingIdentity.CustomTimeFormat, "d")
            }));
    }

    [Test]
    public void Date_time_preview_matches_legacy_error_and_keeps_raw_format()
    {
        DateTime sample = new(2060, 12, 31, 23, 45, 0, DateTimeKind.Utc);
        ApplicationDateTimeFormatPreview valid = ApplicationDeleteConfirmationRules.PreviewDateTimeFormat(
            ApplicationSettingIdentity.CustomDateFormat,
            customDateTimeFormats: true,
            customFormat: "yyyy-MM-dd",
            cultureDefaultFormat: "d",
            sample: sample,
            formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsTrue(valid.IsValid);
        Assert.AreEqual(ApplicationDateTimeFormatPhase.Custom, valid.Phase);
        Assert.AreEqual("2060-12-31", valid.Sample);

        ApplicationDateTimeFormatPreview invalid = ApplicationDeleteConfirmationRules.PreviewDateTimeFormat(
            ApplicationSettingIdentity.CustomTimeFormat,
            customDateTimeFormats: true,
            customFormat: "%",
            cultureDefaultFormat: "t",
            sample: sample,
            formatProvider: System.Globalization.CultureInfo.InvariantCulture);
        Assert.IsFalse(invalid.IsValid);
        Assert.AreEqual("%", invalid.Format);
        Assert.AreEqual("Error", invalid.Sample);
    }

    [Test]
    public void File_store_round_trips_date_time_snapshot_and_recovers_previous_atomic_commit()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-date-time-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileApplicationDeleteConfirmationStore store = new(directory);
            ApplicationDeleteConfirmationState first = ApplicationDeleteConfirmationRules.ApplyDateTimeSnapshot(
                store.Load(),
                DateTimeMutation(true, "yyyy-MM-dd", "HH:mm:ss", false, 0));
            store.Save(0, first);
            ApplicationDeleteConfirmationState restarted = new FileApplicationDeleteConfirmationStore(directory).Load();
            Assert.AreEqual(first, restarted);

            ApplicationDeleteConfirmationState second = ApplicationDeleteConfirmationRules.ApplyDateTimeSnapshot(
                restarted,
                DateTimeMutation(true, "yyyyMMdd", "HHmm", true, 1));
            store.Save(1, second);
            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{");
            Assert.AreEqual(first, new FileApplicationDeleteConfirmationStore(directory).Load());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Whole_page_snapshot_types_and_atomically_persists_both_index_visibility_values()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chummer-index-visibility-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileApplicationDeleteConfirmationStore store = new(directory);
            ApplicationDeleteConfirmationState first = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                store.Load(),
                SettingsMutation(
                    hideMasterIndex: true,
                    hideCharacterRoster: true,
                    expectedRevision: 0));
            store.Save(0, first);

            ApplicationDeleteConfirmationState restarted =
                new FileApplicationDeleteConfirmationStore(directory).Load();
            Assert.AreEqual(1, restarted.Revision);
            Assert.IsTrue(restarted.HideMasterIndex);
            Assert.IsTrue(restarted.HideCharacterRoster);

            ApplicationDeleteConfirmationState second = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                restarted,
                SettingsMutation(
                    hideMasterIndex: false,
                    hideCharacterRoster: true,
                    expectedRevision: 1));
            store.Save(1, second);
            Assert.AreEqual(2, second.Revision);
            Assert.IsFalse(second.HideMasterIndex);
            Assert.IsTrue(second.HideCharacterRoster, "The two legacy booleans are independent.");

            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{");
            Assert.AreEqual(first, new FileApplicationDeleteConfirmationStore(directory).Load());

            ApplicationSettingsSnapshotMutation wrongIdentity = SettingsMutation(
                hideMasterIndex: false,
                hideCharacterRoster: false,
                expectedRevision: first.Revision) with
            {
                HideMasterIndex = new(ApplicationSettingIdentity.HideCharacterRoster, false)
            };
            Assert.Throws<ArgumentException>(() => ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                first,
                wrongIdentity));
            Assert.Throws<InvalidOperationException>(() => ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                first,
                SettingsMutation(true, true, expectedRevision: 0)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Whole_page_snapshot_types_and_atomically_persists_both_selection_behavior_values()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chummer-selection-behavior-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            FileApplicationDeleteConfirmationStore store = new(directory);
            ApplicationDeleteConfirmationState first = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                store.Load(),
                SettingsMutation(
                    hideMasterIndex: false,
                    hideCharacterRoster: false,
                    expectedRevision: 0,
                    searchInCategoryOnly: false,
                    allowEasterEggs: true));
            store.Save(0, first);

            ApplicationDeleteConfirmationState restarted =
                new FileApplicationDeleteConfirmationStore(directory).Load();
            Assert.AreEqual(1, restarted.Revision);
            Assert.IsFalse(restarted.SearchInCategoryOnly);
            Assert.IsTrue(restarted.AllowEasterEggs);

            ApplicationDeleteConfirmationState second = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                restarted,
                SettingsMutation(
                    hideMasterIndex: false,
                    hideCharacterRoster: false,
                    expectedRevision: 1,
                    searchInCategoryOnly: true,
                    allowEasterEggs: true));
            store.Save(1, second);
            Assert.IsTrue(second.SearchInCategoryOnly);
            Assert.IsTrue(second.AllowEasterEggs, "The two legacy booleans are independent.");

            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{");
            Assert.AreEqual(first, new FileApplicationDeleteConfirmationStore(directory).Load());

            ApplicationSettingsSnapshotMutation wrongIdentity = SettingsMutation(
                hideMasterIndex: false,
                hideCharacterRoster: false,
                expectedRevision: first.Revision) with
            {
                SearchInCategoryOnly = new(ApplicationSettingIdentity.AllowEasterEggs, true)
            };
            Assert.Throws<ArgumentException>(() => ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                first,
                wrongIdentity));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Whole_page_snapshot_persists_independent_update_values_with_build_aware_migration()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "chummer-update-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Version nightlyVersion = new(5, 225, 42, 17);
        try
        {
            string path = Path.Combine(directory, "application-delete-confirmation.json");
            File.WriteAllText(path, "{\"Revision\":0,\"ConfirmDelete\":true}");
            FileApplicationDeleteConfirmationStore store = new(directory, nightlyVersion);
            ApplicationDeleteConfirmationState migrated = store.Load();
            Assert.IsTrue(migrated.PreferNightlyBuilds);
            Assert.IsFalse(migrated.LiveUpdateCleanCharacterFiles);

            ApplicationDeleteConfirmationState first = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                migrated,
                SettingsMutation(
                    hideMasterIndex: false,
                    hideCharacterRoster: false,
                    expectedRevision: 0,
                    preferNightlyBuilds: false,
                    liveUpdateCleanCharacterFiles: true));
            store.Save(0, first);
            ApplicationDeleteConfirmationState restarted =
                new FileApplicationDeleteConfirmationStore(directory, nightlyVersion).Load();
            Assert.IsFalse(restarted.PreferNightlyBuilds);
            Assert.IsTrue(restarted.LiveUpdateCleanCharacterFiles);

            ApplicationDeleteConfirmationState second = ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                restarted,
                SettingsMutation(
                    hideMasterIndex: false,
                    hideCharacterRoster: false,
                    expectedRevision: 1,
                    preferNightlyBuilds: true,
                    liveUpdateCleanCharacterFiles: true));
            store.Save(1, second);
            Assert.IsTrue(second.PreferNightlyBuilds);
            Assert.IsTrue(
                second.LiveUpdateCleanCharacterFiles,
                "The two legacy update booleans are independent.");

            File.WriteAllText(path, "{");
            Assert.AreEqual(
                first,
                new FileApplicationDeleteConfirmationStore(directory, nightlyVersion).Load());

            ApplicationSettingsSnapshotMutation wrongIdentity = SettingsMutation(
                hideMasterIndex: false,
                hideCharacterRoster: false,
                expectedRevision: first.Revision) with
            {
                PreferNightlyBuilds = new(
                    ApplicationSettingIdentity.LiveUpdateCleanCharacterFiles,
                    false)
            };
            Assert.Throws<ArgumentException>(() => ApplicationDeleteConfirmationRules.ApplySettingsSnapshot(
                first,
                wrongIdentity));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ApplicationDateTimeSettingsMutation DateTimeMutation(
        bool useCustom,
        string dateFormat,
        string timeFormat,
        bool datesIncludeTime,
        long expectedRevision)
        => new(
            new(ApplicationSettingIdentity.CustomDateTimeFormats, useCustom),
            new(ApplicationSettingIdentity.CustomDateFormat, dateFormat),
            new(ApplicationSettingIdentity.CustomTimeFormat, timeFormat),
            new(ApplicationSettingIdentity.DatesIncludeTime, datesIncludeTime),
            expectedRevision);

    private static ApplicationSettingsSnapshotMutation SettingsMutation(
        bool hideMasterIndex,
        bool hideCharacterRoster,
        long expectedRevision,
        bool searchInCategoryOnly = true,
        bool allowEasterEggs = false,
        bool preferNightlyBuilds = false,
        bool liveUpdateCleanCharacterFiles = false)
        => new(
            ConfirmDelete: true,
            ConfirmKarmaExpense: true,
            CustomDateTimeFormats: new(ApplicationSettingIdentity.CustomDateTimeFormats, false),
            CustomDateFormat: new(ApplicationSettingIdentity.CustomDateFormat, string.Empty),
            CustomTimeFormat: new(ApplicationSettingIdentity.CustomTimeFormat, string.Empty),
            DatesIncludeTime: new(ApplicationSettingIdentity.DatesIncludeTime, true),
            HideMasterIndex: new(ApplicationSettingIdentity.HideMasterIndex, hideMasterIndex),
            HideCharacterRoster: new(ApplicationSettingIdentity.HideCharacterRoster, hideCharacterRoster),
            SearchInCategoryOnly: new(ApplicationSettingIdentity.SearchInCategoryOnly, searchInCategoryOnly),
            AllowEasterEggs: new(ApplicationSettingIdentity.AllowEasterEggs, allowEasterEggs),
            PreferNightlyBuilds: new(ApplicationSettingIdentity.PreferNightlyBuilds, preferNightlyBuilds),
            LiveUpdateCleanCharacterFiles: new(
                ApplicationSettingIdentity.LiveUpdateCleanCharacterFiles,
                liveUpdateCleanCharacterFiles),
            ExpectedRevision: expectedRevision);
}
