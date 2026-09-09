using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationExportTests
{
    private static readonly OwnerScope OwnerA = new("continuation-owner-a");
    private static readonly OwnerScope OwnerB = new("continuation-owner-b");
    private static readonly CharacterWorkspaceId WorkspaceId = new("continuation-runner");
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Actual_local_creation_finalization_and_career_export_preserves_full_state_after_cold_read(bool awakened)
    {
        // Reuse actual bootstrap, source-backed drafts and governed commits. These
        // branches exercise purchases or magic; no imported synthetic rule state.
        using ReadyContext context = ReadyContext.Create(true,
            includeNonEmptyPurchases: !awakened,
            talentValue: awakened ? "Mystic Adept" : null,
            mysticPowerPoints: awakened ? 2 : 0);
        TestOwner authority = new(OwnerScope.LocalSingleUser);
        var creation = context.Store.Get(context.WorkspaceId).Value!;
        var creationExport = Export(new FileWorkspaceStore(context.Directory), authority, context.WorkspaceId);
        AssertSnapshot(creation, creationExport.Snapshot.Workspace);
        AssertCodecRoundtrip(creationExport);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationQualitiesDraft);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationGearDraft);
        if (awakened)
            Assert.IsNotNull(creation.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft);
        foreach (var replay in context.ReplayChecks)
            replay(new FileWorkspaceStore(context.Directory));

        var loaded = context.Finalizer.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var reviewed = context.Finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
        const string finalizeKey = "continuation-real-finalization";
        var finalized = context.Finalizer.Confirm(new(loaded.Value.Binding,
            reviewed.Value.PreviewDigest, reviewed.Value.Plan!.PlanDigest, finalizeKey,
            ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome,
            string.Join(",", finalized.Blockers));

        // Reputation's current governed mutation is local-only; this test makes
        // no claim that linked-owner reputation mutation has been implemented.
        var reputation = new WorkspaceCharacterCareerReputationService(
            new FileWorkspaceStore(context.Directory), context.Resolver);
        var preview = reputation.Preview(new CharacterCareerReputationRequest(
            context.WorkspaceId, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards,
            new(1, null, null), "First local Career reputation decision"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        var reputationCommand = preview.Preview!.Command with { ExplicitlyConfirmed = true };
        var reputationApplied = reputation.Commit(reputationCommand);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, reputationApplied.Outcome, reputationApplied.Error);

        var rewards = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(context.Directory));
        var rewardPreview = rewards.Preview(new(context.WorkspaceId, Guid.NewGuid(), Guid.NewGuid(),
            8, 12500, new DateTime(2078, 9, 7, 18, 0, 0), "First locally recorded run"));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, rewardPreview.Outcome, rewardPreview.Error);
        var rewardCommand = rewardPreview.Preview!.Command with { ExplicitlyConfirmed = true };
        var rewardApplied = rewards.Commit(rewardCommand);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewardApplied.Outcome, rewardApplied.Error);

        var expected = context.Store.Get(context.WorkspaceId).Value!;
        var exported = Export(new FileWorkspaceStore(context.Directory), authority, context.WorkspaceId);
        AssertSnapshot(expected, exported.Snapshot.Workspace);
        AssertCodecRoundtrip(exported);
        Assert.AreEqual(OwnerScope.LocalSingleUser.NormalizedValue, exported.Snapshot.OwnerId);
        Assert.HasCount(0, exported.Snapshot.DelegatedGmCharacterEdits);
        var auxiliary = exported.Snapshot.Workspace.Document.AuxiliaryState;
        Assert.HasCount(1, auxiliary.CharacterCreationFinalizationReceipts!);
        Assert.HasCount(1, auxiliary.CharacterCareerReputationReceipts!);
        Assert.HasCount(1, auxiliary.CharacterAfterRunRewardReceipts!);
        Assert.IsNotNull(auxiliary.CharacterCreationFinalizationArchive);
        Assert.AreEqual(JsonSerializer.Serialize(creation.Document.AuxiliaryState),
            JsonSerializer.Serialize(auxiliary.CharacterCreationFinalizationArchive.State));
        Assert.AreEqual(creation.Document.AuxiliaryStateDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(auxiliary.CharacterCreationFinalizationArchive.State));
        Assert.AreNotEqual(creationExport.SnapshotDigest, exported.SnapshotDigest);
        Assert.AreNotEqual(exported.SnapshotDigest, WorkspaceContinuationSnapshotDigest.Compute(
            exported.Snapshot with
            {
                Workspace = exported.Snapshot.Workspace with
                {
                    Document = exported.Snapshot.Workspace.Document with
                    {
                        State = exported.Snapshot.Workspace.Document.State with
                        {
                            AuxiliaryState = WorkspaceDocumentAuxiliaryState.Empty
                        }
                    }
                }
            }), "The digest must bind the actual auxiliary archive and Career receipts, not only XML.");
        Assert.AreEqual(exported.SnapshotDigest,
            Export(new FileWorkspaceStore(context.Directory), authority, context.WorkspaceId).SnapshotDigest);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, reputation.Commit(reputationCommand).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, rewards.Commit(rewardCommand).Outcome);
        using ReadyContext reopened = context.Restart();
        Assert.AreEqual(finalized.Value!.ReceiptDigest,
            reopened.Finalizer.LookupReceipt(new(context.WorkspaceId, finalizeKey)).Value!.ReceiptDigest);
        AssertSnapshot(expected, Export(reopened.Store, authority, context.WorkspaceId).Snapshot.Workspace);
    }

    [TestMethod]
    public void Linked_export_retains_real_delegated_audit_and_replay_identity_after_later_owner_write()
    {
        using StoreContext context = new();
        var command = GmCommand();
        var applied = GmService(context.Store).Execute(command);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome, applied.Error);
        Assert.IsNotNull(applied.Receipt);
        var current = context.Store.Get(OwnerA, WorkspaceId).Value!;
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(OwnerA, WorkspaceId,
            current.ContentRevision, Document("Later owner note")).Success);

        var expected = context.Store.Get(OwnerA, WorkspaceId).Value!;
        TestOwner authority = new(OwnerA);
        var exported = Export(new FileWorkspaceStore(context.Directory), authority);
        AssertSnapshot(expected, exported.Snapshot.Workspace);
        Assert.AreEqual(OwnerA.NormalizedValue, exported.Snapshot.OwnerId);
        Assert.HasCount(1, exported.Snapshot.DelegatedGmCharacterEdits);
        Assert.AreEqual(JsonSerializer.Serialize(applied.Receipt),
            JsonSerializer.Serialize(exported.Snapshot.DelegatedGmCharacterEdits.Single()));
        AssertCodecRoundtrip(exported);
        Assert.AreEqual(3L, exported.Snapshot.Workspace.ContentRevision);
        Assert.AreEqual(0L, exported.Snapshot.Workspace.SavedRevision);
        var replay = GmService(new FileWorkspaceStore(context.Directory)).Execute(command);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Replayed, replay.Outcome, replay.Error);
        Assert.AreEqual(applied.Receipt.ReceiptId, replay.Receipt!.ReceiptId);
        Assert.AreEqual(exported.SnapshotDigest,
            Export(new FileWorkspaceStore(context.Directory), authority).SnapshotDigest);
    }

    [TestMethod]
    public void Owner_partitions_and_actual_lease_are_used_without_local_fallback()
    {
        using StoreContext context = new();
        Assert.IsTrue(context.Store.CreateWorkspaceDocument(OwnerB, WorkspaceId, Document("B private note")).Success);
        Assert.IsTrue(context.Store.CreateWorkspaceDocument(WorkspaceId, Document("Local private note")).Success);
        TestOwner authority = new(OwnerA);
        ObservingStore observed = new(context.Store, () =>
        {
            Assert.AreEqual(1, authority.ActiveLeases);
            Assert.IsFalse(authority.CanAcquireFromAnotherThread(), "An owner transition must be excluded while reading.");
        });
        var a = Export(observed, authority);
        Assert.AreEqual(OwnerA, observed.LastOwner);
        Assert.AreEqual(Document("A private note").Content, a.Snapshot.Workspace.Document.Content);
        Assert.AreEqual(0, authority.ActiveLeases);
        authority.Transition(OwnerB);
        var b = Export(observed, authority);
        Assert.AreEqual(OwnerB, observed.LastOwner);
        Assert.AreEqual(Document("B private note").Content, b.Snapshot.Workspace.Document.Content);
        Assert.AreNotEqual(a.SnapshotDigest, b.SnapshotDigest);
        authority.Transition(OwnerScope.LocalSingleUser);
        var local = Export(observed, authority);
        Assert.IsNull(observed.LastOwner, "Trusted local authority must use the explicit local capability overload.");
        Assert.AreEqual(Document("Local private note").Content, local.Snapshot.Workspace.Document.Content);
        Assert.AreEqual(3, observed.ReadCount);
    }

    [TestMethod]
    public void Stale_foreign_unknown_and_ABA_stamps_fail_before_the_store_is_read()
    {
        TestOwner authority = new(OwnerA);
        ObservingStore observed = new(null);
        WorkspaceContinuationExportService service = new(observed, authority);
        var a = authority.Capture();
        authority.Transition(OwnerB);
        AssertUnavailable(service.Export(a, WorkspaceId));
        authority.Transition(OwnerA);
        AssertUnavailable(service.Export(a, WorkspaceId));
        AssertUnavailable(service.Export(authority.Capture() with { AuthorityInstanceId = "another-authority" }, WorkspaceId));
        AssertUnavailable(service.Export(authority.Capture() with { Owner = OwnerB }, WorkspaceId));
        AssertUnavailable(service.Export(default, WorkspaceId));
        authority.Transition(default);
        AssertUnavailable(service.Export(authority.Capture(), WorkspaceId));
        Assert.AreEqual(0, observed.ReadCount);
        Assert.AreEqual(0, authority.ActiveLeases);
    }

    [TestMethod]
    public void Missing_capability_or_value_only_owner_cannot_export_a_public_snapshot_as_continuation()
    {
        TestOwner authority = new(OwnerA);
        AssertUnavailable(new WorkspaceContinuationExportService(new UnsupportedStore(), authority)
            .Export(authority.Capture(), WorkspaceId));
        AssertUnavailable(new WorkspaceContinuationExportService(new DefaultCapabilityStore(), authority)
            .Export(authority.Capture(), WorkspaceId));
        AssertUnavailable(new WorkspaceContinuationExportService(new ObservingStore(null), new ValueOnlyOwner(OwnerA))
            .Export(authority.Capture(), WorkspaceId));
        Assert.AreEqual(0, authority.ActiveLeases);
    }

    [TestMethod]
    public void Missing_foreign_workspace_and_untrusted_local_value_fail_closed()
    {
        using StoreContext context = new();
        TestOwner authority = new(OwnerB);
        var missing = new WorkspaceContinuationExportService(context.Store, authority)
            .Export(authority.Capture(), WorkspaceId);
        Assert.IsFalse(missing.Success);
        Assert.IsNull(missing.Value);
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, missing.Outcome);
        Assert.IsTrue(context.Store.CreateWorkspaceDocument(WorkspaceId, Document("Local private note")).Success);
        authority.Transition(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        AssertUnavailable(new WorkspaceContinuationExportService(context.Store, authority)
            .Export(authority.Capture(), WorkspaceId));
    }

    [TestMethod]
    public async Task Checkpoint_race_exports_one_complete_revision_under_the_workspace_lease()
    {
        using StoreContext context = new();
        var applied = GmService(context.Store).Execute(GmCommand());
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome, applied.Error);
        TestOwner authority = new(OwnerA);
        var before = Export(context.Store, authority);
        using BlockingCheckpoint fault = new();
        FileWorkspaceStore writer = new(context.Directory, fault);
        Task<WorkspaceStoreMutationResult> checkpoint = Task.Run(() => writer.SaveCheckpoint(
            OwnerA, WorkspaceId, before.Snapshot.Workspace.ContentRevision));
        Task<WorkspaceContinuationExport>? export = null;
        using ManualResetEventSlim readAdmitted = new();
        try
        {
            Assert.IsTrue(fault.Entered.Wait(TimeSpan.FromSeconds(10)), "Checkpoint did not reach its durable-write barrier.");
            ObservingStore reader = new(new FileWorkspaceStore(context.Directory), readAdmitted.Set);
            export = Task.Run(() => Export(reader, authority));
            Assert.IsTrue(readAdmitted.Wait(TimeSpan.FromSeconds(10)), "Export did not acquire owner admission.");
            Assert.IsFalse(export.IsCompleted, "Export must wait for the in-progress workspace transaction.");
        }
        finally
        {
            fault.Release.Set();
            await checkpoint.WaitAsync(TimeSpan.FromSeconds(10));
            if (export is not null)
                await export.WaitAsync(TimeSpan.FromSeconds(10));
        }
        Assert.IsTrue(checkpoint.Result.Success, checkpoint.Result.Error);
        Assert.IsNotNull(export);
        var after = export.Result;
        Assert.AreEqual(before.Snapshot.Workspace.ContentRevision, after.Snapshot.Workspace.ContentRevision);
        Assert.AreEqual(after.Snapshot.Workspace.ContentRevision, after.Snapshot.Workspace.SavedRevision);
        // SaveCheckpoint writes a new checkpoint timestamp when SavedRevision
        // advances. Export must return that committed read, not the prior time.
        Assert.AreEqual(new FileWorkspaceStore(context.Directory).Get(OwnerA, WorkspaceId).Value!.LastUpdatedUtc,
            after.Snapshot.Workspace.LastUpdatedUtc);
        Assert.AreEqual(JsonSerializer.Serialize(before.Snapshot.DelegatedGmCharacterEdits),
            JsonSerializer.Serialize(after.Snapshot.DelegatedGmCharacterEdits));
        Assert.AreNotEqual(before.SnapshotDigest, after.SnapshotDigest);
        Assert.AreEqual(after.SnapshotDigest, Export(new FileWorkspaceStore(context.Directory), authority).SnapshotDigest);
        Assert.AreEqual(0, authority.ActiveLeases);
    }

    [TestMethod]
    public void Digest_binds_owner_identity_payload_revisions_and_private_GM_hashes()
    {
        using StoreContext context = new();
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, GmService(context.Store).Execute(GmCommand()).Outcome);
        var exported = Export(context.Store, new TestOwner(OwnerA));
        var snapshot = exported.Snapshot;
        var workspace = snapshot.Workspace;
        var receipt = snapshot.DelegatedGmCharacterEdits.Single();
        Assert.AreEqual(exported.SnapshotDigest, WorkspaceContinuationSnapshotDigest.Compute(snapshot));
        WorkspaceContinuationSnapshot[] mutations =
        [
            snapshot with { OwnerId = OwnerB.NormalizedValue },
            snapshot with { Workspace = workspace with { Id = new("another-runner") } },
            snapshot with { Workspace = workspace with { Document = Document("Different payload") } },
            snapshot with { Workspace = workspace with { ContentRevision = workspace.ContentRevision + 1 } },
            snapshot with { Workspace = workspace with { SavedRevision = workspace.ContentRevision } },
            snapshot with { DelegatedGmCharacterEdits = [] },
            snapshot with { DelegatedGmCharacterEdits = [receipt with { IdempotencyKeySha256 = new string('a', 64) }] },
            snapshot with { DelegatedGmCharacterEdits = [receipt with { CommandSha256 = new string('b', 64) }] }
        ];
        foreach (var mutation in mutations)
            Assert.AreNotEqual(exported.SnapshotDigest, WorkspaceContinuationSnapshotDigest.Compute(mutation));
    }

    [TestMethod]
    [DataRow("unknown")]
    [DataRow("duplicate")]
    [DataRow("invalid-json")]
    [DataRow("future-checkpoint")]
    [DataRow("wrong-ledger-owner")]
    [DataRow("wrong-ledger-workspace")]
    [DataRow("mismatched-ledger-hash")]
    [DataRow("future-ledger-revision")]
    public void Corrupt_records_are_denied_without_rewriting_or_losing_the_evidence(string corruption)
    {
        using StoreContext context = new();
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, GmService(context.Store).Execute(GmCommand()).Outcome);
        string path = context.RecordPath;
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var ledger = record["DelegatedGmCharacterEdits"]![0]!;
        switch (corruption)
        {
            case "unknown": record["UnknownContinuationState"] = new JsonObject { ["mustNotDisappear"] = true }; break;
            case "future-checkpoint": record["SavedRevision"] = 99; break;
            case "wrong-ledger-owner": ledger["Receipt"]!["CharacterOwnerId"] = OwnerB.NormalizedValue; break;
            case "wrong-ledger-workspace": ledger["Receipt"]!["CharacterId"]!["Value"] = "foreign-runner"; break;
            case "mismatched-ledger-hash": ledger["CommandSha256"] = new string('f', 64); break;
            case "future-ledger-revision":
                ledger["Receipt"]!["PreviousRevision"] = 98;
                ledger["Receipt"]!["NewRevision"] = 99;
                break;
        }
        string damaged = record.ToJsonString();
        if (corruption == "duplicate")
            damaged = damaged[..^1] + ",\"ContentRevision\":2}";
        if (corruption == "invalid-json")
            damaged = "{\"ContentRevision\":";
        File.WriteAllText(path, damaged);
        var bytes = File.ReadAllBytes(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        TestOwner authority = new(OwnerA);
        var result = new WorkspaceContinuationExportService(new FileWorkspaceStore(context.Directory), authority)
            .Export(authority.Capture(), WorkspaceId);
        Assert.IsFalse(result.Success, corruption);
        Assert.IsNull(result.Value, corruption);
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, result.Outcome, corruption);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(0, authority.ActiveLeases);
    }

    [TestMethod]
    public void Legacy_record_is_not_silently_migrated_by_export_but_an_explicit_normal_read_can_migrate_it()
    {
        using StoreContext context = new();
        string path = context.RecordPath;
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        Assert.IsTrue(record.Remove("RecordSchemaVersion"));
        record.Remove("LocalHistory");
        record.Remove("AuxiliaryState");
        File.WriteAllText(path, record.ToJsonString());
        byte[] before = File.ReadAllBytes(path);
        var timestamp = File.GetLastWriteTimeUtc(path);
        TestOwner authority = new(OwnerA);
        var denied = new WorkspaceContinuationExportService(new FileWorkspaceStore(context.Directory), authority)
            .Export(authority.Capture(), WorkspaceId);
        Assert.IsFalse(denied.Success);
        Assert.IsNull(denied.Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
        var migrated = new FileWorkspaceStore(context.Directory).Get(OwnerA, WorkspaceId);
        Assert.IsTrue(migrated.Success, migrated.Error);
        var exported = Export(new FileWorkspaceStore(context.Directory), authority);
        AssertSnapshot(migrated.Value!, exported.Snapshot.Workspace);
    }

    [TestMethod]
    public void Export_does_not_move_a_legacy_owner_workspace_directory()
    {
        using StoreContext context = new();
        string canonical = Path.GetDirectoryName(context.RecordPath)!;
        Assert.IsTrue(OwnerScopedStatePath.TryResolveContainedLegacyOwnerDirectory(
            context.Directory, OwnerA, out string legacyOwner));
        System.IO.Directory.CreateDirectory(legacyOwner);
        string legacyWorkspaces = Path.Combine(legacyOwner, "workspaces");
        System.IO.Directory.Move(canonical, legacyWorkspaces);
        string legacyRecord = Path.Combine(legacyWorkspaces, WorkspaceId.Value + ".json");
        byte[] before = File.ReadAllBytes(legacyRecord);
        var timestamp = File.GetLastWriteTimeUtc(legacyRecord);
        TestOwner authority = new(OwnerA);
        var result = new WorkspaceContinuationExportService(new FileWorkspaceStore(context.Directory), authority)
            .Export(authority.Capture(), WorkspaceId);
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Value);
        Assert.IsTrue(System.IO.Directory.Exists(legacyWorkspaces));
        Assert.IsFalse(System.IO.Directory.Exists(canonical));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(legacyRecord));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(legacyRecord));
    }

    [TestMethod]
    public void Export_preserves_stale_workspace_temp_files_instead_of_running_write_recovery()
    {
        using StoreContext context = new();
        string path = context.RecordPath;
        string stale = path + ".tmp.continuation-orphan";
        File.WriteAllText(stale, "interrupted writer evidence");
        byte[] before = File.ReadAllBytes(path);
        byte[] tempBefore = File.ReadAllBytes(stale);
        var timestamp = File.GetLastWriteTimeUtc(stale);
        var exported = Export(new FileWorkspaceStore(context.Directory), new TestOwner(OwnerA));
        Assert.AreEqual(Document("A private note").Content, exported.Snapshot.Workspace.Document.Content);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.IsTrue(File.Exists(stale));
        CollectionAssert.AreEqual(tempBefore, File.ReadAllBytes(stale));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(stale));
    }

    private static void AssertCodecRoundtrip(WorkspaceContinuationExport expected)
    {
        // Transport/content fidelity only: decoding is not restore admission.
        byte[] bytes = WorkspaceContinuationCodec.Encode(expected, int.MaxValue);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, bytes.Length, out var candidate));
        Assert.IsNotNull(candidate);
        Assert.AreEqual(expected.SnapshotDigest, candidate.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(expected.Snapshot), JsonSerializer.Serialize(candidate.Snapshot));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Snapshot.Workspace.Document.AuxiliaryState),
            JsonSerializer.Serialize(candidate.Snapshot.Workspace.Document.AuxiliaryState));
        Assert.AreEqual(expected.Snapshot.Workspace.Document.AuxiliaryStateDigest,
            candidate.Snapshot.Workspace.Document.AuxiliaryStateDigest);
        Assert.AreEqual(JsonSerializer.Serialize(expected.Snapshot.DelegatedGmCharacterEdits),
            JsonSerializer.Serialize(candidate.Snapshot.DelegatedGmCharacterEdits));
        CollectionAssert.AreEqual(bytes, WorkspaceContinuationCodec.Encode(candidate, bytes.Length));
    }

    private static WorkspaceContinuationExport Export(IWorkspaceStore store, TestOwner authority,
        CharacterWorkspaceId? workspaceId = null)
    {
        var result = new WorkspaceContinuationExportService(store, authority)
            .Export(authority.Capture(), workspaceId ?? WorkspaceId);
        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(WorkspaceOperationOutcome.Success, result.Outcome);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void AssertSnapshot(WorkspaceStoredDocument expected, WorkspaceDocumentSnapshot actual)
    {
        Assert.AreEqual(expected.Id, actual.Id);
        Assert.AreEqual(expected.ContentRevision, actual.ContentRevision);
        Assert.AreEqual(expected.SavedRevision, actual.SavedRevision);
        Assert.AreEqual(expected.LastUpdatedUtc, actual.LastUpdatedUtc);
        Assert.AreEqual(JsonSerializer.Serialize(expected.Document), JsonSerializer.Serialize(actual.Document));
        Assert.AreEqual(JsonSerializer.Serialize(expected.Document.AuxiliaryState),
            JsonSerializer.Serialize(actual.Document.AuxiliaryState));
        Assert.AreEqual(expected.Document.AuxiliaryStateDigest, actual.Document.AuxiliaryStateDigest);
    }

    private static void AssertUnavailable(CommandResult<WorkspaceContinuationExport> result)
    {
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Value);
        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, result.Outcome);
    }

    private static WorkspaceDocument Document(string notes) => new(
        $"<character><name>Runner One</name><alias>One</alias><notes>{notes}</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>",
        RulesetDefaults.Sr5);

    private static DelegatedGmCharacterEditCommand GmCommand() => new(
        "campaign-one", "gm@example.com", OwnerA, WorkspaceId, 1, "continuation-gm-edit",
        "Correct campaign-visible note",
        [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath, "GM-visible note")]);

    private static DelegatedGmCharacterEditService GmService(IWorkspaceStore store)
    {
        CharacterFileService files = new();
        Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
            new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
        return new(store, new RulesetWorkspaceCodecResolver([codec]), new GmAuthorizer(), new FixedClock());
    }

    // Only campaign admission is a fixture. The patch interpreter, revision CAS,
    // audit creation, replay checks and durable store are production services.
    private sealed class GmAuthorizer : ICampaignGmCharacterEditAuthorizer
    {
        public CampaignGmCharacterEditAuthorization Authorize(CampaignGmCharacterEditAuthorizationRequest request) => new(
            true, request.CampaignId, request.ActorId, DelegatedGmCharacterEditContract.GameMasterRole,
            DelegatedGmCharacterEditContract.CharacterEditScope, request.CharacterOwner, request.CharacterId,
            "delegation-one", "campaign-owner@example.com", request.CharacterOwner.NormalizedValue,
            "authority-receipt-one", 7, FixedNow.AddMinutes(-5), FixedNow.AddHours(1),
            [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => FixedNow;
    }

    private sealed class StoreContext : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), $"chummer-continuation-{Guid.NewGuid():N}");
        public FileWorkspaceStore Store { get; }
        public string RecordPath => System.IO.Directory.EnumerateFiles(Directory,
            WorkspaceId.Value + ".json", SearchOption.AllDirectories).Single();

        public StoreContext()
        {
            System.IO.Directory.CreateDirectory(Directory);
            Store = new(Directory);
            Assert.IsTrue(Store.CreateWorkspaceDocument(OwnerA, WorkspaceId, Document("A private note")).Success);
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class TestOwner(OwnerScope initial) : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        private OwnerScope _owner = initial;
        private long _revision;
        public int ActiveLeases { get; private set; }
        public OwnerScope Current { get { lock (_gate) return _owner; } }
        public OwnerContextStamp Capture() { lock (_gate) return new(_owner, _issuer, _revision); }
        public void Transition(OwnerScope owner)
        {
            lock (_gate)
            {
                Assert.AreEqual(0, ActiveLeases);
                _owner = owner;
                _revision++;
            }
        }

        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
            {
                Monitor.Exit(_gate);
                lease = null;
                return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }

        public bool CanAcquireFromAnotherThread()
        {
            var task = Task.Run(() =>
            {
                if (!Monitor.TryEnter(_gate)) return false;
                Monitor.Exit(_gate);
                return true;
            });
            Assert.IsTrue(task.Wait(TimeSpan.FromSeconds(10)));
            return task.Result;
        }

        private sealed class Lease(TestOwner authority, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                authority.ActiveLeases--;
                Monitor.Exit(authority._gate);
            }
        }
    }

    private sealed class ValueOnlyOwner(OwnerScope owner) : IOwnerContextAccessor
    {
        public OwnerScope Current => owner;
    }

    private class UnsupportedStore : IWorkspaceStore
    {
        private static Exception Unexpected() => new AssertFailedException("Export must not fall back to the public workspace API.");
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => throw Unexpected();
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw Unexpected();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw Unexpected();
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => throw Unexpected();
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => throw Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document) => throw Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision) => throw Unexpected();
    }

    private sealed class DefaultCapabilityStore : UnsupportedStore, IWorkspaceContinuationReadCapability { }

    private sealed class ObservingStore(FileWorkspaceStore? inner, Action? onRead = null)
        : UnsupportedStore, IWorkspaceContinuationReadCapability
    {
        public bool SupportsWorkspaceContinuationRead => true;
        public int ReadCount { get; private set; }
        public OwnerScope? LastOwner { get; private set; }
        public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(CharacterWorkspaceId id)
        {
            ReadCount++;
            LastOwner = null;
            onRead?.Invoke();
            return inner!.ReadContinuation(id);
        }
        public CommandResult<WorkspaceContinuationSnapshot> ReadContinuation(OwnerScope owner, CharacterWorkspaceId id)
        {
            ReadCount++;
            LastOwner = owner;
            onRead?.Invoke();
            return inner!.ReadContinuation(owner, id);
        }
    }

    private sealed class BlockingCheckpoint : IFileWorkspaceStoreFaultInjector, IDisposable
    {
        public ManualResetEventSlim Entered { get; } = new();
        public ManualResetEventSlim Release { get; } = new();
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed) return;
            Entered.Set();
            Assert.IsTrue(Release.Wait(TimeSpan.FromSeconds(15)), "Checkpoint test barrier was not released.");
        }
        public void Dispose() { Entered.Dispose(); Release.Dispose(); }
    }
}
