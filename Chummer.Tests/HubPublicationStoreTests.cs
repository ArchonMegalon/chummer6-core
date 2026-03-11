#nullable enable annotations

using System;
using System.IO;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class HubPublicationStoreTests
{
    [TestMethod]
    public void File_hub_draft_store_persists_owner_scoped_draft_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileHubDraftStore store = new(stateDirectory);
            HubDraftRecord record = new(
                DraftId: "draft-1",
                ProjectKind: HubCatalogItemKinds.RulePack,
                ProjectId: "campaign.shadowops",
                RulesetId: RulesetDefaults.Sr5,
                Title: "Campaign ShadowOps",
                OwnerId: "alice",
                PublisherId: "shadowops",
                State: HubPublicationStates.Draft,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:05:00+00:00"),
                Summary: "Street-level campaign",
                Description: "Campaign-specific SR5 publication draft.");

            store.Upsert(new OwnerScope("alice"), record);

            HubDraftRecord? reloaded = store.Get(new OwnerScope("alice"), HubCatalogItemKinds.RulePack, "campaign.shadowops", RulesetDefaults.Sr5);
            HubDraftRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), HubCatalogItemKinds.RulePack, "campaign.shadowops", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Campaign ShadowOps", reloaded.Title);
            Assert.AreEqual("Street-level campaign", reloaded.Summary);
            Assert.AreEqual("Campaign-specific SR5 publication draft.", reloaded.Description);
            Assert.AreEqual("shadowops", reloaded.PublisherId);
            Assert.AreEqual(HubPublicationStates.Draft, reloaded.State);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_hub_moderation_case_store_persists_owner_scoped_case_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileHubModerationCaseStore store = new(stateDirectory);
            HubModerationCaseRecord record = new(
                CaseId: "case-1",
                DraftId: "draft-1",
                ProjectKind: HubCatalogItemKinds.RuleProfile,
                ProjectId: "campaign.sr5.runtime",
                RulesetId: RulesetDefaults.Sr5,
                Title: "Campaign Runtime",
                OwnerId: "alice",
                PublisherId: "shadowops",
                State: HubModerationStates.PendingReview,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:10:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:11:00+00:00"),
                Summary: "Ready for review");

            store.Upsert(new OwnerScope("alice"), record);

            HubModerationCaseRecord? reloaded = store.Get(new OwnerScope("alice"), HubCatalogItemKinds.RuleProfile, "campaign.sr5.runtime", RulesetDefaults.Sr5);
            HubModerationCaseRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), HubCatalogItemKinds.RuleProfile, "campaign.sr5.runtime", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Campaign Runtime", reloaded.Title);
            Assert.AreEqual("shadowops", reloaded.PublisherId);
            Assert.AreEqual(HubModerationStates.PendingReview, reloaded.State);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_hub_publication_stores_delete_owner_scoped_draft_and_moderation_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileHubDraftStore draftStore = new(stateDirectory);
            FileHubModerationCaseStore moderationStore = new(stateDirectory);
            OwnerScope owner = new("alice");
            HubDraftRecord draft = new(
                DraftId: "draft-1",
                ProjectKind: HubCatalogItemKinds.RulePack,
                ProjectId: "campaign.shadowops",
                RulesetId: RulesetDefaults.Sr5,
                Title: "Campaign ShadowOps",
                OwnerId: owner.NormalizedValue,
                PublisherId: "shadowops",
                State: HubPublicationStates.Submitted,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:05:00+00:00"));
            HubModerationCaseRecord moderation = new(
                CaseId: "case-1",
                DraftId: draft.DraftId,
                ProjectKind: draft.ProjectKind,
                ProjectId: draft.ProjectId,
                RulesetId: draft.RulesetId,
                Title: draft.Title,
                OwnerId: owner.NormalizedValue,
                PublisherId: draft.PublisherId,
                State: HubModerationStates.PendingReview,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:10:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:11:00+00:00"));

            draftStore.Upsert(owner, draft);
            moderationStore.Upsert(owner, moderation);

            Assert.IsTrue(draftStore.Delete(owner, draft.DraftId));
            Assert.IsTrue(moderationStore.DeleteByDraftId(owner, draft.DraftId));
            Assert.IsNull(draftStore.Get(owner, draft.DraftId));
            Assert.IsNull(moderationStore.GetByDraftId(owner, draft.DraftId));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-hub-publication-store-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
