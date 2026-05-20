#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileHubReviewStoreTests
{
    [TestMethod]
    public void File_store_upsert_normalizes_and_persists_owner_review()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubReviewStore store = new(stateDirectory.Path);
        OwnerScope owner = new(" Alice ");

        HubReviewRecord persisted = store.Upsert(
            owner,
            CreateRecord(
                projectKind: " RulePack ",
                projectId: "  campaign.shadowops  ",
                rulesetId: " SR5 ",
                recommendationState: " Recommended ",
                reviewText: "  Great pack  "));

        HubReviewRecord? loaded = store.Get(new OwnerScope("alice"), "rulepack", "campaign.shadowops", RulesetDefaults.Sr5);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("rulepack", persisted.ProjectKind);
        Assert.AreEqual("campaign.shadowops", persisted.ProjectId);
        Assert.AreEqual(RulesetDefaults.Sr5, persisted.RulesetId);
        Assert.AreEqual("alice", persisted.OwnerId);
        Assert.AreEqual("recommended", persisted.RecommendationState);
        Assert.AreEqual("Great pack", persisted.ReviewText);
        Assert.AreEqual(persisted, loaded);
    }

    [TestMethod]
    public void File_store_list_filters_reviews_by_normalized_kind_item_and_ruleset()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubReviewStore store = new(stateDirectory.Path);
        OwnerScope alice = new("alice");
        OwnerScope bob = new("bob");

        store.Upsert(alice, CreateRecord(projectKind: "rulepack", projectId: "alpha", rulesetId: RulesetDefaults.Sr5));
        store.Upsert(alice, CreateRecord(projectKind: "rulepack", projectId: "beta", rulesetId: RulesetDefaults.Sr6));
        store.Upsert(alice, CreateRecord(projectKind: "runtime-lock", projectId: "beta", rulesetId: RulesetDefaults.Sr5));
        store.Upsert(bob, CreateRecord(projectKind: "rulepack", projectId: "alpha", rulesetId: RulesetDefaults.Sr5));

        HubReviewRecord[] filtered = store.List(new OwnerScope(" alice "), " rulepack ", " beta ", " sr6 ").ToArray();

        Assert.HasCount(1, filtered);
        Assert.AreEqual("rulepack", filtered[0].ProjectKind);
        Assert.AreEqual("beta", filtered[0].ProjectId);
        Assert.AreEqual(RulesetDefaults.Sr6, filtered[0].RulesetId);
        Assert.AreEqual("alice", filtered[0].OwnerId);
    }

    [TestMethod]
    public void File_store_list_all_includes_local_and_owner_scoped_reviews()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubReviewStore store = new(stateDirectory.Path);

        store.Upsert(OwnerScope.LocalSingleUser, CreateRecord(projectKind: "rulepack", projectId: "alpha", rulesetId: RulesetDefaults.Sr5));
        store.Upsert(new OwnerScope("alice"), CreateRecord(projectKind: "rulepack", projectId: "beta", rulesetId: RulesetDefaults.Sr6));
        store.Upsert(new OwnerScope("bob"), CreateRecord(projectKind: "runtime-lock", projectId: "gamma", rulesetId: RulesetDefaults.Sr5));

        HubReviewRecord[] allRulepacks = store.ListAll(" rulepack ").ToArray();

        Assert.HasCount(2, allRulepacks);
        CollectionAssert.AreEquivalent(
            new[] { "alpha", "beta" },
            allRulepacks.Select(record => record.ProjectId).ToArray());
    }

    [TestMethod]
    public void File_store_upsert_replaces_existing_review_instead_of_appending_duplicate()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubReviewStore store = new(stateDirectory.Path);
        OwnerScope owner = new("alice");

        HubReviewRecord original = CreateRecord(
            reviewId: "review-1",
            projectKind: "rulepack",
            projectId: "campaign.shadowops",
            rulesetId: RulesetDefaults.Sr5,
            recommendationState: HubRecommendationStates.Neutral,
            reviewText: "First");
        HubReviewRecord updated = original with
        {
            RecommendationState = HubRecommendationStates.Recommended,
            ReviewText = "Second",
            UpdatedAtUtc = original.UpdatedAtUtc.AddMinutes(5)
        };

        store.Upsert(owner, original);
        store.Upsert(owner, updated);

        HubReviewRecord[] records = store.List(owner).ToArray();

        Assert.HasCount(1, records);
        Assert.AreEqual(HubRecommendationStates.Recommended, records[0].RecommendationState);
        Assert.AreEqual("Second", records[0].ReviewText);
        Assert.AreEqual(original.ReviewId, records[0].ReviewId);
    }

    [TestMethod]
    public void File_store_returns_empty_for_missing_owner_and_missing_state_file()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubReviewStore store = new(stateDirectory.Path);

        Assert.IsEmpty(store.List(new OwnerScope("missing")));
        Assert.IsEmpty(store.ListAll());
        Assert.IsNull(store.Get(new OwnerScope("missing"), "rulepack", "alpha", RulesetDefaults.Sr5));
    }

    private static HubReviewRecord CreateRecord(
        string reviewId = "review-1",
        string projectKind = "rulepack",
        string projectId = "campaign.shadowops",
        string rulesetId = "sr5",
        string recommendationState = "recommended",
        string? reviewText = "Looks good")
    {
        DateTimeOffset createdAtUtc = new(2026, 5, 20, 10, 0, 0, TimeSpan.Zero);
        return new HubReviewRecord(
            ReviewId: reviewId,
            ProjectKind: projectKind,
            ProjectId: projectId,
            RulesetId: rulesetId,
            OwnerId: "ignored-owner",
            RecommendationState: recommendationState,
            CreatedAtUtc: createdAtUtc,
            UpdatedAtUtc: createdAtUtc.AddMinutes(1),
            Stars: 4,
            ReviewText: reviewText,
            UsedAtTable: true);
    }

    private sealed class TemporaryStateDirectory : IDisposable
    {
        public TemporaryStateDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chummer-hub-review-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
