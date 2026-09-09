using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceImportedWizardReplayTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Imported_current_and_archived_purchases_and_finalization_keep_history_without_local_replay(bool finalized)
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var resources = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var gear = new CharacterCreationGearService(context.Store, context.Resolver);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available,
            resources.LookupReceipt(new(context.WorkspaceId, "resources-finalization-test")).Outcome);
        Assert.AreEqual(CharacterCreationGearOutcomes.Available,
            gear.LookupReceipt(new(context.WorkspaceId, "gear-finalization-test")).Outcome);
        CharacterCreationFinalizationConfirmRequest? confirmation = null;
        if (finalized)
        {
            var loaded = context.Finalizer.Load(new(context.WorkspaceId));
            Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
            var review = context.Finalizer.Review(new(loaded.Value.Binding));
            Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
            confirmation = new(loaded.Value.Binding, review.Value.PreviewDigest, review.Value.Plan!.PlanDigest,
                "imported-history-finalize", ExplicitlyConfirmed: true);
            var applied = context.Finalizer.Confirm(confirmation);
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, context.Finalizer.Confirm(confirmation).Outcome);
        }
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, context.WorkspaceId);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value);
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Conflict,
            resources.LookupReceipt(new(context.WorkspaceId, "resources-finalization-test")).Outcome);
        Assert.AreEqual(CharacterCreationGearOutcomes.Conflict,
            gear.LookupReceipt(new(context.WorkspaceId, "gear-finalization-test")).Outcome);
        if (confirmation is not null)
        {
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict,
                context.Finalizer.LookupReceipt(new(context.WorkspaceId, confirmation.IdempotencyKey)).Outcome);
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, context.Finalizer.Confirm(confirmation).Outcome);
        }
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Receipt_classification_rejects_invalid_metadata_and_out_of_range_revisions()
    {
        var workspace = new WorkspaceStoredDocument(new("runner"), new("<character/>", "sr5"), 5, 5, DateTimeOffset.UnixEpoch);
        Assert.IsTrue(workspace.CanReplayReceipt(5), "Pre-continuation store behavior remains compatible.");
        foreach (long revision in new[] { -1L, 0L, 6L, long.MaxValue })
            Assert.IsFalse(workspace.CanReplayReceipt(revision));
        var history = new WorkspaceLocalHistory(Guid.NewGuid().ToString("N"), 3, new string('a', 64));
        var imported = workspace with { LocalHistory = history };
        Assert.IsFalse(imported.CanReplayReceipt(1));
        Assert.IsFalse(imported.CanReplayReceipt(3));
        Assert.IsTrue(imported.CanReplayReceipt(4));
        Assert.IsTrue(imported.CanReplayReceipt(5));
        foreach (var invalid in new[]
                 {
                     history with { ImportedThroughRevision = -1 },
                     history with { ImportedThroughRevision = 6 },
                     history with { ImportedSnapshotDigest = null },
                     history with { IncarnationId = "untrusted" }
                 })
            Assert.IsFalse((workspace with { LocalHistory = invalid }).CanReplayReceipt(5));
    }
}
