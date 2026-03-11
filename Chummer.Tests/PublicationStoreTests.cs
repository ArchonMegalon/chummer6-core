#nullable enable annotations

using System;
using System.IO;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class PublicationStoreTests
{
    [TestMethod]
    public void File_rulepack_publication_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRulePackPublicationStore store = new(stateDirectory);
            RulePackPublicationRecord aliceRecord = new(
                PackId: "house-rules",
                Version: "1.0.0",
                RulesetId: RulesetDefaults.Sr5,
                Publication: new RulePackPublicationMetadata(
                    OwnerId: "alice",
                    Visibility: ArtifactVisibilityModes.Private,
                    PublicationStatus: RulePackPublicationStatuses.Draft,
                    Review: new RulePackReviewDecision(RulePackReviewStates.PendingReview),
                    Shares: [],
                    PublisherId: "ShadowOps"));

            store.Upsert(new OwnerScope("alice"), aliceRecord);

            RulePackPublicationRecord? reloaded = store.Get(new OwnerScope("alice"), "house-rules", "1.0.0", RulesetDefaults.Sr5);
            RulePackPublicationRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), "house-rules", "1.0.0", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("alice", reloaded.Publication.OwnerId);
            Assert.AreEqual(ArtifactVisibilityModes.Private, reloaded.Publication.Visibility);
            Assert.AreEqual("shadowops", reloaded.Publication.PublisherId);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_ruleprofile_publication_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuleProfilePublicationStore store = new(stateDirectory);
            RuleProfilePublicationRecord aliceRecord = new(
                ProfileId: "local.sr5.current-overlays",
                RulesetId: RulesetDefaults.Sr5,
                Publication: new RuleProfilePublicationMetadata(
                    OwnerId: "alice",
                    Visibility: ArtifactVisibilityModes.CampaignShared,
                    PublicationStatus: RuleProfilePublicationStatuses.Published,
                    Review: new RulePackReviewDecision(RulePackReviewStates.Approved),
                    Shares: [],
                    PublisherId: "ShadowOps"));

            store.Upsert(new OwnerScope("alice"), aliceRecord);

            RuleProfilePublicationRecord? reloaded = store.Get(new OwnerScope("alice"), "local.sr5.current-overlays", RulesetDefaults.Sr5);
            RuleProfilePublicationRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), "local.sr5.current-overlays", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("alice", reloaded.Publication.OwnerId);
            Assert.AreEqual(ArtifactVisibilityModes.CampaignShared, reloaded.Publication.Visibility);
            Assert.AreEqual("shadowops", reloaded.Publication.PublisherId);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-publication-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
