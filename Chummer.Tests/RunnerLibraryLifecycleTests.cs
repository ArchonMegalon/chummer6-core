using System.Text;
using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RunnerLibraryLifecycleTests
{
    private static readonly OwnerScope Owner = OwnerScope.LocalSingleUser;

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Lifecycle_is_typed_recoverable_and_does_not_change_runner_content(bool fileBacked)
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked);
        CharacterWorkspaceId runnerId = new("runner_alpha");
        CharacterWorkspaceId unrelatedId = new("runner_unrelated");
        fixture.Create(runnerId, "alpha payload");
        fixture.Create(unrelatedId, "unrelated payload");
        RunnerLibraryItem initial = fixture.Item(runnerId);
        byte[]? runnerBytesBefore = fixture.ReadWorkspaceBytes(runnerId);
        WorkspaceStoredDocument unrelatedBefore = fixture.Read(unrelatedId);
        byte[]? unrelatedBytesBefore = fixture.ReadWorkspaceBytes(unrelatedId);

        RunnerLibraryMutationResult renamed = fixture.Service.Rename(
            Owner,
            new RenameRunnerCommand(
                runnerId,
                initial.LifecycleRevision,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "Nightshade",
                "rename-1"));
        AssertApplied(renamed, RunnerLibraryLifecycle.Active, 2);
        Assert.AreEqual(runnerId, renamed.Item!.Id);

        RunnerLibraryMutationResult archived = fixture.Service.Archive(
            Owner,
            new ArchiveRunnerCommand(
                runnerId,
                2,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "archive-1"));
        AssertApplied(archived, RunnerLibraryLifecycle.Archived, 3);

        RunnerLibraryMutationResult deleted = fixture.Service.Delete(
            Owner,
            new DeleteRunnerCommand(
                runnerId,
                3,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "delete-1"));
        AssertApplied(deleted, RunnerLibraryLifecycle.Deleted, 4);
        Assert.AreEqual(RunnerLibraryLifecycle.Archived, deleted.Item!.LifecycleBeforeDelete);
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, fixture.Store.Get(runnerId).Outcome);
        if (fileBacked)
        {
            CollectionAssert.AreEqual(
                runnerBytesBefore,
                fixture.ReadWorkspaceBytes(runnerId),
                "Runner Library Delete must preserve workspace bytes for recovery.");
        }
        Assert.IsFalse(fixture.Service.List(Owner, new RunnerLibraryListQuery()).Items
            .Any(item => item.Id == runnerId));

        RunnerLibraryMutationResult restoredDeleted = fixture.Service.RestoreDeleted(
            Owner,
            new RestoreDeletedRunnerCommand(
                runnerId,
                4,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "restore-delete-1"));
        AssertApplied(restoredDeleted, RunnerLibraryLifecycle.Archived, 5);

        RunnerLibraryMutationResult restoredArchived = fixture.Service.RestoreArchived(
            Owner,
            new RestoreArchivedRunnerCommand(
                runnerId,
                5,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "restore-archive-1"));
        AssertApplied(restoredArchived, RunnerLibraryLifecycle.Active, 6);
        Assert.AreEqual("Nightshade", restoredArchived.Item!.DisplayName);
        Assert.AreEqual(initial.ContentRevision, restoredArchived.Item.ContentRevision);
        Assert.AreEqual(initial.SavedRevision, restoredArchived.Item.SavedRevision);
        Assert.AreEqual("alpha payload", fixture.Read(runnerId).Document.Content);
        RunnerLibraryListResult filtered = fixture.Service.List(
            Owner,
            new RunnerLibraryListQuery(
                RunnerLibraryLifecycleFilter.Active,
                NameContains: "shade"));
        Assert.AreEqual(runnerId, filtered.Items.Single().Id);

        WorkspaceStoredDocument unrelatedAfter = fixture.Read(unrelatedId);
        Assert.AreEqual(unrelatedBefore.ContentRevision, unrelatedAfter.ContentRevision);
        Assert.AreEqual(unrelatedBefore.SavedRevision, unrelatedAfter.SavedRevision);
        Assert.AreEqual(unrelatedBefore.LastUpdatedUtc, unrelatedAfter.LastUpdatedUtc);
        CollectionAssert.AreEqual(unrelatedBytesBefore, fixture.ReadWorkspaceBytes(unrelatedId));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Stale_lifecycle_content_revision_or_digest_fails_closed(bool fileBacked)
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked);
        CharacterWorkspaceId id = new("runner_cas");
        fixture.Create(id, "cas payload");
        RunnerLibraryItem initial = fixture.Item(id);

        RunnerLibraryMutationResult staleRevision = fixture.Service.Rename(
            Owner,
            new RenameRunnerCommand(
                id,
                initial.LifecycleRevision,
                initial.ContentRevision + 1,
                initial.ContentDigestSha256,
                "Wrong revision",
                "stale-content-revision"));
        Assert.AreEqual(RunnerLibraryOperationOutcome.Conflict, staleRevision.Outcome);

        RunnerLibraryMutationResult staleDigest = fixture.Service.Archive(
            Owner,
            new ArchiveRunnerCommand(
                id,
                initial.LifecycleRevision,
                initial.ContentRevision,
                new string('a', 64),
                "stale-content-digest"));
        Assert.AreEqual(RunnerLibraryOperationOutcome.Conflict, staleDigest.Outcome);

        RunnerLibraryMutationResult applied = fixture.Service.Rename(
            Owner,
            new RenameRunnerCommand(
                id,
                initial.LifecycleRevision,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "Winner",
                "winner"));
        AssertApplied(applied, RunnerLibraryLifecycle.Active, 2);

        RunnerLibraryMutationResult staleLifecycle = fixture.Service.Archive(
            Owner,
            new ArchiveRunnerCommand(
                id,
                initial.LifecycleRevision,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "stale-lifecycle"));
        Assert.AreEqual(RunnerLibraryOperationOutcome.Conflict, staleLifecycle.Outcome);
        Assert.AreEqual("Winner", fixture.Item(id).DisplayName);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Idempotent_replay_returns_original_receipt_and_key_reuse_conflicts(bool fileBacked)
    {
        DateTimeOffset committedAt = new(2026, 8, 25, 12, 34, 56, TimeSpan.Zero);
        using StoreFixture fixture = StoreFixture.Create(
            fileBacked,
            new FixedTimeProvider(committedAt));
        CharacterWorkspaceId id = new("runner_replay");
        fixture.Create(id, "replay payload");
        RunnerLibraryItem initial = fixture.Item(id);
        RenameRunnerCommand command = new(
            id,
            initial.LifecycleRevision,
            initial.ContentRevision,
            initial.ContentDigestSha256,
            "Replay Name",
            "stable-key");

        RunnerLibraryMutationResult first = fixture.Service.Rename(Owner, command);
        RunnerLibraryMutationResult replay = fixture.Service.Rename(Owner, command);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Applied, first.Outcome);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(first.Receipt, replay.Receipt);
        Assert.AreEqual(committedAt, replay.Receipt!.CommittedAtUtc);

        RunnerLibraryMutationResult conflict = fixture.Service.Rename(
            Owner,
            command with { DisplayName = "Different payload" });
        Assert.AreEqual(RunnerLibraryOperationOutcome.Conflict, conflict.Outcome);
        Assert.AreEqual("Replay Name", fixture.Item(id).DisplayName);
        Assert.AreEqual(2L, fixture.Item(id).LifecycleRevision);

        RunnerLibraryItem renamed = fixture.Item(id);
        Assert.AreEqual(
            RunnerLibraryOperationOutcome.Applied,
            fixture.Service.Archive(
                Owner,
                new ArchiveRunnerCommand(
                    id,
                    renamed.LifecycleRevision,
                    renamed.ContentRevision,
                    renamed.ContentDigestSha256,
                    "archive-after-rename"))
                .Outcome);
        RunnerLibraryMutationResult historicalReplay = fixture.Service.Rename(Owner, command);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Replayed, historicalReplay.Outcome);
        Assert.AreEqual(first.Receipt, historicalReplay.Receipt);
        Assert.AreEqual(
            RunnerLibraryLifecycle.Archived,
            historicalReplay.Item!.Lifecycle,
            "Replay receipts are historical; Item is deliberately the current projection.");
        Assert.AreEqual(3L, historicalReplay.CurrentLifecycleRevision);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Store_rejects_noncanonical_command_digest_without_a_write(bool fileBacked)
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked);
        CharacterWorkspaceId id = new("runner_forged");
        fixture.Create(id, "forged payload");
        RunnerLibraryItem initial = fixture.Item(id);
        string keyDigest = RunnerLibraryCanonical.ComputeIdempotencyKeyDigest("forged-key");
        RunnerLibraryStoreMutation forged = new(
            RunnerLibraryMutationKind.Rename,
            id,
            null,
            initial.LifecycleRevision,
            initial.ContentRevision,
            initial.ContentDigestSha256,
            "Forged",
            keyDigest,
            new string('f', 64));

        RunnerLibraryMutationResult result = ((IRunnerLibraryStore)fixture.Store)
            .ApplyRunnerLibraryMutation(Owner, forged);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Invalid, result.Outcome);
        Assert.AreEqual(id.Value, fixture.Item(id).DisplayName);
        Assert.AreEqual(1L, fixture.Item(id).LifecycleRevision);

        RunnerLibraryStoreMutation canonical = forged with
        {
            CommandDigestSha256 = RunnerLibraryCanonical.ComputeCommandDigest(
                forged.Kind,
                forged.RunnerId,
                forged.NewRunnerId,
                forged.ExpectedLifecycleRevision,
                forged.ExpectedContentRevision,
                forged.ExpectedContentDigestSha256,
                forged.DisplayName,
                forged.IdempotencyKeyDigestSha256)
        };
        RunnerLibraryStoreState corruptCurrent =
            RunnerLibraryStoreStateMachine.CreateLegacy(id, DateTimeOffset.UtcNow) with
            {
                LifecycleRevision = 0
            };
        Assert.IsFalse(RunnerLibraryStoreStateMachine.TryApply(
            id,
            corruptCurrent,
            canonical,
            initial.ContentRevision,
            initial.ContentDigestSha256,
            DateTimeOffset.UtcNow,
            out _,
            out _,
            out string? corruptError));
        StringAssert.Contains(corruptError, "corrupt");

        DateTimeOffset nonUtc = new(2026, 8, 25, 14, 0, 0, TimeSpan.FromHours(2));
        RunnerLibraryStoreState normalized =
            RunnerLibraryStoreStateMachine.CreateLegacy(id, nonUtc);
        Assert.AreEqual(TimeSpan.Zero, normalized.LastLifecycleUpdatedUtc.Offset);
        Assert.IsFalse(RunnerLibraryStoreStateMachine.IsValid(
            id,
            normalized with { LastLifecycleUpdatedUtc = nonUtc }));

        Assert.IsFalse(RunnerLibraryStoreStateMachine.TryApply(
            id,
            normalized,
            canonical,
            initial.ContentRevision + 1,
            initial.ContentDigestSha256,
            DateTimeOffset.UtcNow,
            out _,
            out _,
            out string? innerRevisionError));
        StringAssert.Contains(innerRevisionError, "expected snapshot");
        Assert.IsFalse(RunnerLibraryStoreStateMachine.TryApply(
            id,
            normalized,
            canonical,
            initial.ContentRevision,
            new string('b', 64),
            DateTimeOffset.UtcNow,
            out _,
            out _,
            out string? innerDigestError));
        StringAssert.Contains(innerDigestError, "expected snapshot");

        CharacterWorkspaceId duplicateId = new("runner_inner_cas_copy");
        string duplicateKeyDigest = RunnerLibraryCanonical.ComputeIdempotencyKeyDigest(
            "inner-duplicate-cas");
        RunnerLibraryStoreMutation duplicateMutation = new(
            RunnerLibraryMutationKind.Duplicate,
            id,
            duplicateId,
            normalized.LifecycleRevision,
            initial.ContentRevision,
            initial.ContentDigestSha256,
            "Inner copy",
            duplicateKeyDigest,
            RunnerLibraryCanonical.ComputeCommandDigest(
                RunnerLibraryMutationKind.Duplicate,
                id,
                duplicateId,
                normalized.LifecycleRevision,
                initial.ContentRevision,
                initial.ContentDigestSha256,
                "Inner copy",
                duplicateKeyDigest));
        Assert.IsFalse(RunnerLibraryStoreStateMachine.TryCreateDuplicate(
            id,
            duplicateId,
            normalized.DisplayName,
            normalized.Lifecycle,
            normalized.LifecycleBeforeDelete,
            "Inner copy",
            normalized.LifecycleRevision,
            normalized.Provenance,
            initial.ContentRevision + 1,
            initial.ContentDigestSha256,
            duplicateMutation,
            DateTimeOffset.UtcNow,
            out _,
            out _));
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Two_same_cas_writers_have_exactly_one_winner(bool fileBacked)
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked);
        CharacterWorkspaceId id = new("runner_race");
        fixture.Create(id, "race payload");
        RunnerLibraryItem initial = fixture.Item(id);
        using Barrier barrier = new(2);

        Task<RunnerLibraryMutationResult> first = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return fixture.Service.Rename(
                Owner,
                new RenameRunnerCommand(
                    id,
                    initial.LifecycleRevision,
                    initial.ContentRevision,
                    initial.ContentDigestSha256,
                    "First",
                    "race-first"));
        });
        Task<RunnerLibraryMutationResult> second = Task.Run(() =>
        {
            barrier.SignalAndWait();
            return fixture.Service.Rename(
                Owner,
                new RenameRunnerCommand(
                    id,
                    initial.LifecycleRevision,
                    initial.ContentRevision,
                    initial.ContentDigestSha256,
                    "Second",
                    "race-second"));
        });

        RunnerLibraryMutationResult[] results = await Task.WhenAll(first, second);
        Assert.AreEqual(1, results.Count(result => result.Outcome == RunnerLibraryOperationOutcome.Applied));
        Assert.AreEqual(1, results.Count(result => result.Outcome == RunnerLibraryOperationOutcome.Conflict));
        Assert.AreEqual(2L, fixture.Item(id).LifecycleRevision);
        Assert.AreEqual(1L, fixture.Read(id).ContentRevision);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Duplicate_is_a_deep_copy_with_provenance_and_source_side_replay(bool fileBacked)
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked);
        CharacterWorkspaceId sourceId = new("runner_source");
        CharacterWorkspaceId duplicateId = new("runner_copy");
        fixture.Create(sourceId, "duplicate payload");
        RunnerLibraryItem source = fixture.Item(sourceId);
        DuplicateRunnerCommand command = new(
            sourceId,
            duplicateId,
            source.LifecycleRevision,
            source.ContentRevision,
            source.ContentDigestSha256,
            "Copy",
            "duplicate-key");

        RunnerLibraryMutationResult created = fixture.Service.Duplicate(Owner, command);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Applied, created.Outcome);
        Assert.AreEqual(duplicateId, created.Item!.Id);
        Assert.AreEqual(sourceId, created.Item.Provenance!.SourceRunnerId);
        Assert.AreEqual(source.ContentRevision, created.Item.Provenance.SourceContentRevision);
        Assert.AreEqual(source.ContentDigestSha256, created.Item.Provenance.SourceContentDigestSha256);
        WorkspaceStoredDocument sourceDocument = fixture.Read(sourceId);
        WorkspaceStoredDocument duplicateDocument = fixture.Read(duplicateId);
        Assert.AreEqual(sourceDocument.Document, duplicateDocument.Document);
        Assert.AreNotSame(sourceDocument.Document, duplicateDocument.Document);
        if (fileBacked)
        {
            RunnerLibraryStoreState persistedSource = JsonSerializer.Deserialize<RunnerLibraryStoreState>(
                File.ReadAllText(fixture.RunnerStatePath(sourceId)))!;
            RunnerLibraryStoreState persistedTarget = JsonSerializer.Deserialize<RunnerLibraryStoreState>(
                File.ReadAllText(fixture.RunnerStatePath(duplicateId)))!;
            Assert.AreEqual(
                persistedSource.MutationLedger.Single().Receipt,
                persistedTarget.MutationLedger.Single().Receipt);
        }

        fixture.Restart();
        RunnerLibraryMutationResult replay = fixture.Service.Duplicate(Owner, command);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(created.Receipt, replay.Receipt);

        WorkspaceStoreMutationResult deletedSource = fixture.Store.Delete(
            sourceId,
            sourceDocument.ContentRevision);
        Assert.IsTrue(deletedSource.Success, deletedSource.Error);
        Assert.IsFalse(fixture.Service.List(
                Owner,
                new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All))
            .Items.Any(item => item.Id == sourceId));
        Assert.AreEqual("duplicate payload", fixture.Read(duplicateId).Document.Content);
        Assert.AreEqual(sourceId, fixture.Item(duplicateId).Provenance!.SourceRunnerId);
    }

    [TestMethod]
    public void File_store_restarts_with_exact_receipt_and_order_independent_json_projection()
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked: true);
        CharacterWorkspaceId id = new("runner_restart");
        fixture.Create(id, "restart payload");
        RunnerLibraryItem initial = fixture.Item(id);
        RenameRunnerCommand command = new(
            id,
            initial.LifecycleRevision,
            initial.ContentRevision,
            initial.ContentDigestSha256,
            "Restarted",
            "restart-key");
        RunnerLibraryMutationResult applied = fixture.Service.Rename(Owner, command);
        string statePath = fixture.RunnerStatePath(id);

        using JsonDocument parsed = JsonDocument.Parse(File.ReadAllText(statePath));
        string reordered = "{" + string.Join(",", parsed.RootElement
            .EnumerateObject()
            .Reverse()
            .Select(property => JsonSerializer.Serialize(property.Name) + ":" + property.Value.GetRawText())) + "}";
        File.WriteAllText(statePath, reordered);

        fixture.Restart();
        RunnerLibraryMutationResult replay = fixture.Service.Rename(Owner, command);
        Assert.AreEqual(RunnerLibraryOperationOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(applied.Receipt, replay.Receipt);
        Assert.AreEqual(applied.Receipt!.ReceiptDigestSha256, replay.Receipt!.ReceiptDigestSha256);
    }

    [TestMethod]
    public void File_store_recovers_duplicate_across_both_cross_file_commit_boundaries()
    {
        foreach (FileWorkspaceStoreFaultStage stage in new[]
                 {
                     FileWorkspaceStoreFaultStage.AfterDuplicateWorkspaceCreatedBeforeLibraryState,
                     FileWorkspaceStoreFaultStage.AfterDuplicateLifecycleStateCreatedBeforeSourceReceipt,
                     FileWorkspaceStoreFaultStage.AfterDuplicateSourceReceiptCreatedBeforePendingCleanup
                 })
        {
            string root = StoreFixture.CreateTempStateDirectory();
            try
            {
                DuplicateCrashFaultInjector injector = new(stage);
                FileWorkspaceStore crashingStore = new(root, injector);
                RunnerLibraryService crashingService = new(crashingStore);
                CharacterWorkspaceId sourceId = new("runner_crash_source");
                CharacterWorkspaceId targetId = new("runner_crash_target");
                Assert.IsTrue(crashingStore.CreateWorkspaceDocument(
                    sourceId,
                    Document("crash payload")).Success);
                RunnerLibraryItem source = Single(crashingService.List(
                    Owner,
                    new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All)),
                    sourceId);
                DuplicateRunnerCommand command = new(
                    sourceId,
                    targetId,
                    source.LifecycleRevision,
                    source.ContentRevision,
                    source.ContentDigestSha256,
                    "Recovered copy",
                    "duplicate-crash-key");

                RunnerLibraryMutationResult interrupted = crashingService.Duplicate(Owner, command);
                Assert.AreEqual(RunnerLibraryOperationOutcome.Unavailable, interrupted.Outcome);

                FileWorkspaceStore interimStore = new(root);
                RunnerLibraryService interimService = new(interimStore);
                Assert.AreEqual(
                    RunnerLibraryOperationOutcome.Applied,
                    interimService.Archive(
                        Owner,
                        new ArchiveRunnerCommand(
                            sourceId,
                            source.LifecycleRevision,
                            source.ContentRevision,
                            source.ContentDigestSha256,
                            "archive-after-interrupted-duplicate"))
                        .Outcome,
                    "A later source mutation must not prevent duplicate receipt reconciliation.");

                FileWorkspaceStore restartedStore = new(root);
                RunnerLibraryService restartedService = new(restartedStore);
                RunnerLibraryMutationResult recovered = restartedService.Duplicate(Owner, command);
                Assert.AreEqual(
                    stage == FileWorkspaceStoreFaultStage.AfterDuplicateWorkspaceCreatedBeforeLibraryState
                        ? RunnerLibraryOperationOutcome.Applied
                        : RunnerLibraryOperationOutcome.Replayed,
                    recovered.Outcome);
                Assert.AreEqual("crash payload", restartedStore.Get(targetId).Value!.Document.Content);
                RunnerLibraryMutationResult replay = restartedService.Duplicate(Owner, command);
                Assert.AreEqual(RunnerLibraryOperationOutcome.Replayed, replay.Outcome);
                Assert.AreEqual(recovered.Receipt, replay.Receipt);
                Assert.AreEqual(
                    RunnerLibraryLifecycle.Archived,
                    Single(
                        restartedService.List(
                            Owner,
                            new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All)),
                        sourceId)
                    .Lifecycle);
                Assert.IsFalse(File.Exists(Path.Combine(
                    root,
                    "workspaces",
                    targetId.Value + ".runner-library.pending.json")));

                RunnerLibraryStoreState persistedSource =
                    JsonSerializer.Deserialize<RunnerLibraryStoreState>(File.ReadAllText(
                        Path.Combine(root, "workspaces", sourceId.Value + ".runner-library.json")))!;
                RunnerLibraryStoreState persistedTarget =
                    JsonSerializer.Deserialize<RunnerLibraryStoreState>(File.ReadAllText(
                        Path.Combine(root, "workspaces", targetId.Value + ".runner-library.json")))!;
                Assert.AreEqual(
                    persistedSource.MutationLedger.Single(entry =>
                        entry.Receipt.Kind == RunnerLibraryMutationKind.Duplicate).Receipt,
                    persistedTarget.MutationLedger.Single().Receipt);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Corrupt_pending_with_committed_target_fails_closed_without_source_write()
    {
        string root = StoreFixture.CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(
                root,
                new DuplicateCrashFaultInjector(
                    FileWorkspaceStoreFaultStage
                        .AfterDuplicateLifecycleStateCreatedBeforeSourceReceipt));
            RunnerLibraryService service = new(store);
            CharacterWorkspaceId sourceId = new("runner_pending_source");
            CharacterWorkspaceId targetId = new("runner_pending_target");
            Assert.IsTrue(store.CreateWorkspaceDocument(
                sourceId,
                Document("pending payload")).Success);
            RunnerLibraryItem source = Single(
                service.List(
                    Owner,
                    new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All)),
                sourceId);
            DuplicateRunnerCommand command = new(
                sourceId,
                targetId,
                source.LifecycleRevision,
                source.ContentRevision,
                source.ContentDigestSha256,
                "Pending target",
                "pending-corruption-key");
            Assert.AreEqual(
                RunnerLibraryOperationOutcome.Unavailable,
                service.Duplicate(Owner, command).Outcome);

            string sourceStatePath = Path.Combine(
                root,
                "workspaces",
                sourceId.Value + ".runner-library.json");
            string targetStatePath = Path.Combine(
                root,
                "workspaces",
                targetId.Value + ".runner-library.json");
            string pendingPath = Path.Combine(
                root,
                "workspaces",
                targetId.Value + ".runner-library.pending.json");
            byte[] sourceBytes = File.ReadAllBytes(sourceStatePath);
            byte[] targetBytes = File.ReadAllBytes(targetStatePath);
            byte[] validPendingBytes = File.ReadAllBytes(pendingPath);
            File.WriteAllText(pendingPath, "{ corrupt pending");
            byte[] corruptPendingBytes = File.ReadAllBytes(pendingPath);

            FileWorkspaceStore restartedStore = new(root);
            RunnerLibraryMutationResult retry =
                new RunnerLibraryService(restartedStore).Duplicate(Owner, command);
            Assert.AreEqual(RunnerLibraryOperationOutcome.Corrupt, retry.Outcome);
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(sourceStatePath));
            CollectionAssert.AreEqual(targetBytes, File.ReadAllBytes(targetStatePath));
            CollectionAssert.AreEqual(corruptPendingBytes, File.ReadAllBytes(pendingPath));

            System.Text.Json.Nodes.JsonObject mismatchedPending =
                System.Text.Json.Nodes.JsonNode.Parse(
                    Encoding.UTF8.GetString(validPendingBytes))!.AsObject();
            mismatchedPending["TargetRunnerId"] = JsonSerializer.SerializeToNode(
                new CharacterWorkspaceId("different_pending_target"));
            File.WriteAllText(pendingPath, mismatchedPending.ToJsonString());
            byte[] mismatchedPendingBytes = File.ReadAllBytes(pendingPath);
            RunnerLibraryMutationResult mismatchedRetry =
                new RunnerLibraryService(new FileWorkspaceStore(root)).Duplicate(Owner, command);
            Assert.AreEqual(RunnerLibraryOperationOutcome.Corrupt, mismatchedRetry.Outcome);
            CollectionAssert.AreEqual(sourceBytes, File.ReadAllBytes(sourceStatePath));
            CollectionAssert.AreEqual(targetBytes, File.ReadAllBytes(targetStatePath));
            CollectionAssert.AreEqual(mismatchedPendingBytes, File.ReadAllBytes(pendingPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void Duplicate_preflights_source_ledger_capacity_before_target_commit()
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked: true);
        CharacterWorkspaceId sourceId = new("runner_full_ledger_source");
        CharacterWorkspaceId targetId = new("runner_full_ledger_target");
        fixture.Create(sourceId, "full ledger payload");
        RunnerLibraryItem source = fixture.Item(sourceId);
        RunnerLibraryStoreState initialState = JsonSerializer.Deserialize<RunnerLibraryStoreState>(
            File.ReadAllText(fixture.RunnerStatePath(sourceId)))!;
        System.Collections.Immutable.ImmutableArray<RunnerLibraryMutationLedgerEntry>.Builder ledger =
            System.Collections.Immutable.ImmutableArray.CreateBuilder<RunnerLibraryMutationLedgerEntry>(
                RunnerLibraryStoreStateMachine.MaximumMutationLedgerEntries);
        string beforeDisplayName = initialState.DisplayName;
        long beforeLifecycleRevision = initialState.LifecycleRevision;
        DateTimeOffset committedAtUtc = initialState.LastLifecycleUpdatedUtc;

        for (int index = 0;
             index < RunnerLibraryStoreStateMachine.MaximumMutationLedgerEntries;
             index++)
        {
            string displayName = $"Ledger runner {index}";
            string keyDigest = RunnerLibraryCanonical.ComputeIdempotencyKeyDigest(
                $"ledger-capacity-{index}");
            string commandDigest = RunnerLibraryCanonical.ComputeCommandDigest(
                RunnerLibraryMutationKind.Rename,
                sourceId,
                null,
                beforeLifecycleRevision,
                source.ContentRevision,
                source.ContentDigestSha256,
                displayName,
                keyDigest);
            long afterLifecycleRevision = beforeLifecycleRevision + 1;
            committedAtUtc = committedAtUtc.AddTicks(1);
            RunnerLibraryMutationReceipt unsigned = new(
                RunnerLibraryCanonical.ReceiptSchema,
                RunnerLibraryMutationKind.Rename,
                sourceId,
                null,
                keyDigest,
                commandDigest,
                RunnerLibraryCanonical.ComputeStateDigest(
                    sourceId,
                    beforeDisplayName,
                    RunnerLibraryLifecycle.Active,
                    null,
                    beforeLifecycleRevision,
                    source.ContentDigestSha256,
                    null),
                RunnerLibraryCanonical.ComputeStateDigest(
                    sourceId,
                    displayName,
                    RunnerLibraryLifecycle.Active,
                    null,
                    afterLifecycleRevision,
                    source.ContentDigestSha256,
                    null),
                beforeDisplayName,
                displayName,
                RunnerLibraryLifecycle.Active,
                RunnerLibraryLifecycle.Active,
                null,
                null,
                beforeLifecycleRevision,
                afterLifecycleRevision,
                null,
                null,
                source.ContentRevision,
                source.ContentDigestSha256,
                committedAtUtc,
                string.Empty);
            RunnerLibraryMutationReceipt receipt = unsigned with
            {
                ReceiptDigestSha256 = RunnerLibraryCanonical.ComputeReceiptDigest(unsigned)
            };
            ledger.Add(new RunnerLibraryMutationLedgerEntry(keyDigest, commandDigest, receipt));
            beforeDisplayName = displayName;
            beforeLifecycleRevision = afterLifecycleRevision;
        }

        RunnerLibraryStoreState state = new(
            beforeDisplayName,
            RunnerLibraryLifecycle.Active,
            null,
            beforeLifecycleRevision,
            committedAtUtc,
            null,
            ledger.MoveToImmutable());
        Assert.IsTrue(RunnerLibraryStoreStateMachine.IsValid(sourceId, state));

        File.WriteAllText(fixture.RunnerStatePath(sourceId), JsonSerializer.Serialize(state));
        RunnerLibraryItem exhausted = fixture.Item(sourceId);
        byte[] sourceBytes = File.ReadAllBytes(fixture.RunnerStatePath(sourceId));
        RunnerLibraryMutationResult duplicate = fixture.Service.Duplicate(
            Owner,
            new DuplicateRunnerCommand(
                sourceId,
                targetId,
                exhausted.LifecycleRevision,
                exhausted.ContentRevision,
                exhausted.ContentDigestSha256,
                "Must not commit",
                "duplicate-full-ledger"));

        Assert.AreEqual(RunnerLibraryOperationOutcome.Unavailable, duplicate.Outcome);
        CollectionAssert.AreEqual(
            sourceBytes,
            File.ReadAllBytes(fixture.RunnerStatePath(sourceId)));
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.Root!,
            "workspaces",
            targetId.Value + ".json")));
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.Root!,
            "workspaces",
            targetId.Value + ".runner-library.json")));
        Assert.IsFalse(File.Exists(Path.Combine(
            fixture.Root!,
            "workspaces",
            targetId.Value + ".runner-library.pending.json")));
    }

    [TestMethod]
    public void Corrupt_tombstone_fails_closed_without_affecting_unrelated_runner()
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked: true);
        CharacterWorkspaceId corruptId = new("runner_corrupt");
        CharacterWorkspaceId unrelatedId = new("runner_safe");
        fixture.Create(corruptId, "corrupt payload");
        fixture.Create(unrelatedId, "safe payload");
        File.Delete(fixture.RunnerStatePath(unrelatedId));
        Assert.IsFalse(File.Exists(fixture.RunnerStatePath(unrelatedId)));
        RunnerLibraryItem initial = fixture.Item(corruptId);
        Assert.AreEqual(
            RunnerLibraryOperationOutcome.Applied,
            fixture.Service.Archive(
                Owner,
                new ArchiveRunnerCommand(
                    corruptId,
                    initial.LifecycleRevision,
                    initial.ContentRevision,
                    initial.ContentDigestSha256,
                    "archive-corrupt"))
                .Outcome);
        Assert.AreEqual(
            RunnerLibraryOperationOutcome.Applied,
            fixture.Service.Delete(
                Owner,
                new DeleteRunnerCommand(
                    corruptId,
                    initial.LifecycleRevision + 1,
                    initial.ContentRevision,
                    initial.ContentDigestSha256,
                    "delete-corrupt"))
                .Outcome);

        RunnerLibraryStoreState state = JsonSerializer.Deserialize<RunnerLibraryStoreState>(
            File.ReadAllText(fixture.RunnerStatePath(corruptId)))!;
        RunnerLibraryMutationReceipt firstReceipt = state.MutationLedger[0].Receipt;
        RunnerLibraryMutationReceipt tamperedUnsigned = firstReceipt with
        {
            AfterDisplayName = "tampered intermediate state",
            ReceiptDigestSha256 = string.Empty
        };
        RunnerLibraryMutationReceipt tampered = tamperedUnsigned with
        {
            ReceiptDigestSha256 = RunnerLibraryCanonical.ComputeReceiptDigest(tamperedUnsigned)
        };
        File.WriteAllText(
            fixture.RunnerStatePath(corruptId),
            JsonSerializer.Serialize(state with
            {
                MutationLedger = state.MutationLedger.SetItem(
                    0,
                    state.MutationLedger[0] with { Receipt = tampered })
            }));
        RunnerLibraryListResult listed = fixture.Service.List(
            Owner,
            new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All));
        Assert.AreEqual(RunnerLibraryOperationOutcome.Corrupt, listed.Outcome);
        Assert.IsFalse(
            File.Exists(fixture.RunnerStatePath(unrelatedId)),
            "A failed list must not migrate an unrelated legacy projection.");
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, fixture.Store.Get(corruptId).Outcome);
        Assert.AreEqual("safe payload", fixture.Read(unrelatedId).Document.Content);
        Assert.AreEqual(1L, fixture.Read(unrelatedId).ContentRevision);
    }

    [TestMethod]
    public void Legacy_active_only_records_project_deterministically_and_migrate_once_on_mutation()
    {
        using StoreFixture fixture = StoreFixture.Create(fileBacked: true);
        CharacterWorkspaceId id = new("runner_legacy");
        fixture.Create(id, "legacy payload");
        Assert.IsTrue(File.Exists(fixture.RunnerStatePath(id)),
            "A confirmed workspace create must include its durable lifecycle sidecar.");
        File.Delete(fixture.RunnerStatePath(id));
        Assert.IsFalse(File.Exists(fixture.RunnerStatePath(id)));

        RunnerLibraryItem first = fixture.Item(id);
        Assert.IsFalse(
            File.Exists(fixture.RunnerStatePath(id)),
            "Runner Library list must remain read-only for legacy projections.");
        RunnerLibraryItem second = fixture.Item(id);
        Assert.AreEqual(first, second);
        Assert.IsFalse(File.Exists(fixture.RunnerStatePath(id)));
        Assert.AreEqual(id.Value, first.DisplayName);
        Assert.AreEqual(1L, first.LifecycleRevision);

        RunnerLibraryMutationResult renamed = fixture.Service.Rename(
            Owner,
            new RenameRunnerCommand(
                id,
                first.LifecycleRevision,
                first.ContentRevision,
                first.ContentDigestSha256,
                "Migrated",
                "migrate-key"));
        Assert.AreEqual(RunnerLibraryOperationOutcome.Applied, renamed.Outcome);
        Assert.IsTrue(File.Exists(fixture.RunnerStatePath(id)));
        string migratedBytes = File.ReadAllText(fixture.RunnerStatePath(id));
        fixture.Restart();
        Assert.AreEqual(renamed.Item, fixture.Item(id));
        Assert.AreEqual(migratedBytes, File.ReadAllText(fixture.RunnerStatePath(id)));
    }

    private static WorkspaceDocument Document(string content)
    {
        return new WorkspaceDocument(content, "sr5", WorkspaceDocumentFormat.Json);
    }

    private static RunnerLibraryItem Single(
        RunnerLibraryListResult result,
        CharacterWorkspaceId id)
    {
        Assert.AreEqual(RunnerLibraryOperationOutcome.Success, result.Outcome, result.Error);
        return result.Items.Single(item => item.Id == id);
    }

    private static void AssertApplied(
        RunnerLibraryMutationResult result,
        RunnerLibraryLifecycle lifecycle,
        long lifecycleRevision)
    {
        Assert.AreEqual(RunnerLibraryOperationOutcome.Applied, result.Outcome, result.Error);
        Assert.IsNotNull(result.Item);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(lifecycle, result.Item.Lifecycle);
        Assert.AreEqual(lifecycleRevision, result.Item.LifecycleRevision);
        Assert.AreEqual(lifecycleRevision, result.CurrentLifecycleRevision);
    }

    private sealed class StoreFixture : IDisposable
    {
        private readonly bool _fileBacked;
        private readonly TimeProvider? _timeProvider;

        private StoreFixture(
            bool fileBacked,
            string? root,
            TimeProvider? timeProvider,
            IWorkspaceStore store)
        {
            _fileBacked = fileBacked;
            Root = root;
            _timeProvider = timeProvider;
            Store = store;
            Service = new RunnerLibraryService(store);
        }

        public string? Root { get; }

        public IWorkspaceStore Store { get; private set; }

        public RunnerLibraryService Service { get; private set; }

        public static StoreFixture Create(bool fileBacked, TimeProvider? timeProvider = null)
        {
            string? root = fileBacked ? CreateTempStateDirectory() : null;
            IWorkspaceStore store = fileBacked
                ? new FileWorkspaceStore(
                    root,
                    FileWorkspaceStoreFaultInjector.None,
                    timeProvider: timeProvider)
                : new InMemoryWorkspaceStore(timeProvider);
            return new StoreFixture(fileBacked, root, timeProvider, store);
        }

        public static string CreateTempStateDirectory()
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                "chummer-runner-library-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        public void Create(CharacterWorkspaceId id, string content)
        {
            WorkspaceStoreMutationResult result = Store.CreateWorkspaceDocument(id, Document(content));
            Assert.IsTrue(result.Success, result.Error);
        }

        public RunnerLibraryItem Item(CharacterWorkspaceId id)
        {
            return Single(
                Service.List(Owner, new RunnerLibraryListQuery(RunnerLibraryLifecycleFilter.All)),
                id);
        }

        public WorkspaceStoredDocument Read(CharacterWorkspaceId id)
        {
            WorkspaceStoreReadResult result = Store.Get(id);
            Assert.IsTrue(result.Success, result.Error);
            return result.Value!;
        }

        public byte[]? ReadWorkspaceBytes(CharacterWorkspaceId id)
        {
            return _fileBacked
                ? File.ReadAllBytes(Path.Combine(Root!, "workspaces", id.Value + ".json"))
                : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Read(id).Document));
        }

        public string RunnerStatePath(CharacterWorkspaceId id)
        {
            return Path.Combine(Root!, "workspaces", id.Value + ".runner-library.json");
        }

        public void Restart()
        {
            if (!_fileBacked)
            {
                return;
            }

            Store = new FileWorkspaceStore(
                Root,
                FileWorkspaceStoreFaultInjector.None,
                timeProvider: _timeProvider);
            Service = new RunnerLibraryService(Store);
        }

        public void Dispose()
        {
            if (Root is not null && Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    private sealed class DuplicateCrashFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _faultStage;
        private int _thrown;

        public DuplicateCrashFaultInjector(FileWorkspaceStoreFaultStage faultStage)
        {
            _faultStage = faultStage;
        }

        public void OnStage(
            FileWorkspaceStoreFaultStage stage,
            string targetPath,
            string tempPath)
        {
            if (stage == _faultStage
                && Interlocked.Exchange(ref _thrown, 1) == 0)
            {
                throw new IOException("Simulated process interruption after duplicate record commit.");
            }
        }
    }
}
