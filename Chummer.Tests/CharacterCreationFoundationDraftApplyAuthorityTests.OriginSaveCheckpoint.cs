using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Unaccepted_origin_opening_survives_save_only_and_cold_restore(bool saveBeforeOpening)
    {
        string directory = CreateTempDirectory();
        try
        {
            CharacterWorkspaceId id = new("origin-save-only");
            FileWorkspaceStore store = new(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(
                id, new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
            Assert.AreEqual(0L, store.Get(id).Value!.SavedRevision);

            LifeModuleOriginDossierInteractionService CreateInteraction(FileWorkspaceStore currentStore) =>
                new(new LifeModuleOriginDossierService(
                    new CharacterCreationFoundationLifeModuleDecisionAuthority(
                        currentStore, CreateService(currentStore),
                        new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE")));

            if (saveBeforeOpening)
                Assert.IsTrue(store.SaveCheckpoint(id, 1).Success);
            var opening = CreateInteraction(store).Start(id.Value);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, opening.Outcome,
                string.Join(", ", opening.Blockers));
            Assert.IsNotNull(opening.Value);
            Assert.IsNull(opening.Value.PendingPreview);
            Assert.IsEmpty(opening.Value.Projection.VisibleChapters);
            Assert.AreEqual(OriginLtdProvenanceStates.NotRequested, opening.Value.LtdProvenance.State);
            if (!saveBeforeOpening)
                Assert.IsTrue(store.SaveCheckpoint(id, 1).Success);

            FileWorkspaceStore reopened = new(directory);
            Assert.AreEqual(1L, reopened.Get(id).Value!.ContentRevision);
            Assert.AreEqual(1L, reopened.Get(id).Value!.SavedRevision);
            byte[] savedBytes = File.ReadAllBytes(WorkspacePath(directory, id));
            var restarted = CreateInteraction(reopened);
            if (!saveBeforeOpening)
            {
                var strict = restarted.Restore(opening.Value);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Conflict, strict.Outcome,
                    "A command's old admission digest must not become valid merely because a view can reopen.");
            }
            var restored = restarted.Reopen(opening.Value);
            CollectionAssert.AreEqual(savedBytes, File.ReadAllBytes(WorkspacePath(directory, id)),
                "Restoring an unaccepted opening must not mutate the runner.");
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, restored.Outcome,
                string.Join(", ", restored.Blockers));
            Assert.IsNotNull(restored.Value);
            Assert.AreEqual(restarted.Start(id.Value).Value!.CheckpointDigest, restored.Value.CheckpointDigest);
            Assert.AreEqual(restored.Value.CheckpointDigest,
                CreateInteraction(new(directory)).Reopen(restored.Value).Value!.CheckpointDigest);

            var choice = restored.Value.Projection.CurrentTurn.LegalChoices.First(item => item.FollowUps is null);
            var prepared = restarted.Prepare(restored.Value, choice.ChoiceId);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, prepared.Outcome);
            Assert.IsNotNull(prepared.Value?.PendingPreview);
            var committed = restarted.Confirm(prepared.Value, prepared.Value.PendingPreview.PreviewDigest,
                "origin-save-only-confirm", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, committed.Outcome,
                string.Join(", ", committed.Blockers));
            Assert.AreEqual(2L, reopened.Get(id).Value!.ContentRevision);
            byte[] committedBytes = File.ReadAllBytes(WorkspacePath(directory, id));
            var retry = CreateInteraction(new(directory)).Confirm(prepared.Value,
                prepared.Value.PendingPreview.PreviewDigest, "origin-save-only-confirm", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, retry.Outcome);
            Assert.AreEqual(committed.Value!.Checkpoint.CheckpointDigest, retry.Value!.Checkpoint.CheckpointDigest);
            CollectionAssert.AreEqual(committedBytes, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Saved_origin_opening_rebinds_only_under_the_current_owner_transition(bool linked)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("origin-save-owner");
            var scope = linked ? new OwnerScope("origin-save-account-a") : OwnerScope.LocalSingleUser;
            var owner = new AvailabilityOwner(scope);
            var stamp = owner.Capture();
            var store = new FileWorkspaceStore(directory);
            var document = new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5);
            Assert.IsTrue((linked ? store.CreateWorkspaceDocument(scope, id, document)
                : store.CreateWorkspaceDocument(id, document)).Success);
            OwnerBoundLifeModuleOriginService Service(FileWorkspaceStore current) => new(current, owner,
                new XmlCharacterFileQueries(new CharacterFileService()),
                new FileSystemCharacterSourceDataResolver(CreateOverlays()), CreateCatalog());
            var service = Service(store);
            var opening = service.Start(stamp, id.Value);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, opening.Outcome);
            Assert.IsNotNull(opening.Value);
            Assert.IsTrue((linked ? store.SaveCheckpoint(scope, id, 1) : store.SaveCheckpoint(id, 1)).Success);
            string Snapshot() => System.Text.Json.JsonSerializer.Serialize(
                linked ? store.Get(scope, id).Value : store.Get(id).Value);
            string saved = Snapshot();
            var reopened = Service(new(directory));
            var fresh = reopened.Reopen(stamp, opening.Value);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, fresh.Outcome,
                string.Join(", ", fresh.Blockers));
            Assert.AreEqual(scope.NormalizedValue, fresh.Value!.OwnerId);
            owner.Transition(new("origin-save-account-b"));
            Assert.IsNull(reopened.Reopen(stamp, opening.Value).Value);
            Assert.IsNull(reopened.Reopen(owner.Capture(), opening.Value).Value);
            owner.Transition(scope);
            Assert.IsNull(reopened.Reopen(stamp, opening.Value).Value,
                "A→B→A must not revive the old admission stamp.");
            Assert.AreEqual(fresh.Value.CheckpointDigest,
                reopened.Reopen(owner.Capture(), opening.Value).Value!.CheckpointDigest);
            Assert.AreEqual(saved, Snapshot());
            Assert.AreEqual(0, owner.ActiveLeases);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
