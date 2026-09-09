using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationRestoreTests
{
    private const int MaximumBytes = 16 * 1024 * 1024;

    [TestMethod]
    public void Real_complete_creation_restores_dirty_then_finalizes_locally_and_restores_full_Career_archive()
    {
        using ReadyContext source = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        var original = source.Store.Get(source.WorkspaceId).Value!;
        // An actual ordinary edit leaves the existing confirmed draft graph dirty.
        Assert.IsTrue(source.Store.ReplaceWorkspaceDocument(source.WorkspaceId,
            original.ContentRevision, original.Document).Success);
        var creation = Export(source.Store, source.WorkspaceId);
        Assert.IsTrue(creation.Snapshot.Workspace.SavedRevision < creation.Snapshot.Workspace.ContentRevision);
        Assert.IsTrue(creation.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationGearDraft!.Budget.BasketCost > 0m);
        string sourceBefore = JsonSerializer.Serialize(source.Store.Get(source.WorkspaceId).Value);

        using Target target = new();
        var service = Service(source, target);
        using var review = Review(service, target.Owner, creation);
        var applied = service.Confirm(review, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, applied.Outcome, Describe(applied));
        AssertRestored(target, creation, applied.Receipt!);
        Assert.AreEqual(sourceBefore, JsonSerializer.Serialize(source.Store.Get(source.WorkspaceId).Value));
        var stored = target.Read(source.WorkspaceId);
        Assert.AreEqual(creation.Snapshot.Workspace.SavedRevision, stored.SavedRevision,
            "Durably writing the imported record must not silently checkpoint dirty character state.");
        Assert.AreEqual(creation.Snapshot.Workspace.ContentRevision, stored.LocalHistory!.ImportedThroughRevision);

        // This is a new local decision after an explicit checkpoint, not replay
        // of the imported Creation confirmations.
        Assert.IsTrue(target.Store.SaveCheckpoint(source.WorkspaceId, stored.ContentRevision).Success);
        var finalizer = ReadyContext.BuildFinalizer(target.Store, source.Queries, source.Resolver);
        var loaded = finalizer.Load(new(source.WorkspaceId));
        Assert.IsNotNull(loaded.Value, string.Join(",", loaded.Blockers));
        var preview = finalizer.Review(new(loaded.Value.Binding));
        Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
        var finalized = finalizer.Confirm(new(loaded.Value.Binding, preview.Value.PreviewDigest,
            preview.Value.Plan!.PlanDigest, "restore-local-finalization", ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, finalized.Outcome, string.Join(",", finalized.Blockers));
        var reputation = new WorkspaceCharacterCareerReputationService(target.Store, source.Resolver);
        var reputationPreview = reputation.Preview(new(source.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(1), "New local Career decision"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, reputationPreview.Outcome, reputationPreview.Error);
        var command = reputationPreview.Preview!.Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, reputation.Commit(command).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, reputation.Commit(command).Outcome);
        var career = Export(target.Store, source.WorkspaceId);
        var auxiliary = career.Snapshot.Workspace.Document.AuxiliaryState;
        Assert.IsNotNull(auxiliary.CharacterCreationFinalizationArchive);
        Assert.AreEqual(JsonSerializer.Serialize(creation.Snapshot.Workspace.Document.AuxiliaryState),
            JsonSerializer.Serialize(auxiliary.CharacterCreationFinalizationArchive.State));
        Assert.HasCount(1, auxiliary.CharacterCreationFinalizationReceipts!);
        Assert.HasCount(1, auxiliary.CharacterCareerReputationReceipts!);

        using Target second = new();
        var secondService = Service(source, second);
        using var secondReview = Review(secondService, second.Owner, career);
        var secondApplied = secondService.Confirm(secondReview, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, secondApplied.Outcome, Describe(secondApplied));
        AssertRestored(second, career, secondApplied.Receipt!);
        var secondReputation = new WorkspaceCharacterCareerReputationService(second.Store, source.Resolver);
        Assert.AreEqual(CharacterCareerReputationOutcome.IdempotencyConflict, secondReputation.Commit(command).Outcome);
        var next = secondReputation.Preview(new(source.WorkspaceId, Guid.NewGuid(),
            CharacterCareerReputationOperation.AdjustManualAwards, new(1), "Second device local decision"));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, next.Outcome, next.Error);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied,
            secondReputation.Commit(next.Preview!.Command with { ExplicitlyConfirmed = true }).Outcome);
        Assert.AreEqual(secondApplied.Receipt, second.Read(source.WorkspaceId).LocalHistory!.LastRestore);
    }

    [TestMethod]
    public void Real_bootstrap_only_restore_retains_binding_without_inventing_future_drafts()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        using Target target = new();
        var exported = Export(source.Store, source.WorkspaceId);
        var service = Service(source, target);
        using var review = Review(service, target.Owner, exported);
        Assert.IsFalse(review.Result.Target!.Exists);
        Assert.AreNotEqual(Guid.Empty, review.OperationId);
        Assert.IsNotNull(review.AdmissionDigest);
        var result = service.Confirm(review, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, Describe(result));
        AssertRestored(target, exported, result.Receipt!);
        var auxiliary = target.Read(source.WorkspaceId).Document.AuxiliaryState;
        Assert.IsNotNull(auxiliary.CharacterCreationBootstrapBinding);
        Assert.IsNull(auxiliary.CharacterCreationPrerequisiteDraft);
        Assert.IsNull(auxiliary.CharacterCreationFinalizationArchive);
        Assert.AreEqual(0L, target.Read(source.WorkspaceId).SavedRevision);
    }

    [TestMethod]
    [DataRow("none")]
    [DataRow("checkpoint")]
    [DataRow("delete-recreate")]
    [DataRow("competing-create")]
    [DataRow("absent-create-delete")]
    public void Target_compare_exchange_checks_checkpoint_incarnation_and_absence(string race)
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        AdvanceSource(source, 3);
        var candidate = Export(source.Store, source.WorkspaceId);
        using Target target = new();
        if (race is not ("competing-create" or "absent-create-delete"))
            Assert.IsTrue(target.Store.CreateWorkspaceDocument(source.WorkspaceId, PlainDocument()).Success);
        var service = Service(source, target);
        using var review = Review(service, target.Owner, candidate);
        var expected = review.Result.Target!;
        switch (race)
        {
            case "checkpoint":
                Assert.IsTrue(target.Store.SaveCheckpoint(source.WorkspaceId, 1).Success);
                Assert.AreEqual(expected.ContentRevision, target.Read(source.WorkspaceId).ContentRevision);
                break;
            case "delete-recreate":
                Assert.IsTrue(target.Store.Delete(source.WorkspaceId, 1).Success);
                Assert.IsTrue(target.Store.CreateWorkspaceDocument(source.WorkspaceId, PlainDocument()).Success);
                Assert.AreEqual(expected.ContentRevision, target.Read(source.WorkspaceId).ContentRevision);
                Assert.AreEqual(expected.SavedRevision, target.Read(source.WorkspaceId).SavedRevision);
                Assert.AreNotEqual(expected.IncarnationId, target.Read(source.WorkspaceId).LocalHistory!.IncarnationId);
                break;
            case "competing-create":
                Assert.IsTrue(target.Store.CreateWorkspaceDocument(source.WorkspaceId, PlainDocument()).Success);
                break;
            case "absent-create-delete":
                Assert.IsTrue(target.Store.CreateWorkspaceDocument(source.WorkspaceId, PlainDocument()).Success);
                Assert.IsTrue(target.Store.Delete(source.WorkspaceId, 1).Success);
                Assert.AreEqual(WorkspaceOperationOutcome.Missing, target.Store.Get(source.WorkspaceId).Outcome);
                break;
        }
        string before = JsonSerializer.Serialize(target.Store.Get(source.WorkspaceId));
        var result = service.Confirm(review, explicitlyConfirmed: true);
        if (race == "none")
        {
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, Describe(result));
            AssertRestored(target, candidate, result.Receipt!);
            Assert.AreNotEqual(expected.IncarnationId, target.Read(source.WorkspaceId).LocalHistory!.IncarnationId);
        }
        else
        {
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Conflict, result.Outcome, Describe(result));
            Assert.AreEqual(before, JsonSerializer.Serialize(target.Store.Get(source.WorkspaceId)));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed,
                service.Confirm(review, explicitlyConfirmed: true).Outcome);
        }
    }

    [TestMethod]
    public void Same_state_is_not_replay_and_nonadvancing_replacements_are_refused_without_writes()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        using Target target = new();
        var initial = Export(source.Store, source.WorkspaceId);
        var service = Service(source, target);
        using var review = Review(service, target.Owner, initial);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, service.Confirm(review, true).Outcome);
        string before = JsonSerializer.Serialize(target.Read(source.WorkspaceId));
        using var identical = service.Review(target.Owner.Capture(), Encode(initial));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.AlreadyCurrent, identical.Result.Outcome);
        Assert.IsNull(identical.Result.Receipt);
        Assert.AreEqual(Guid.Empty, identical.OperationId);
        Assert.AreEqual(before, JsonSerializer.Serialize(target.Read(source.WorkspaceId)));
        var changed = initial.Snapshot with { Workspace = initial.Snapshot.Workspace with
        {
            LastUpdatedUtc = initial.Snapshot.Workspace.LastUpdatedUtc.AddSeconds(1)
        } };
        using var equalRevision = service.Review(target.Owner.Capture(), Encode(WithDigest(changed)));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Conflict, equalRevision.Result.Outcome);
        var current = target.Read(source.WorkspaceId);
        Assert.IsTrue(target.Store.ReplaceWorkspaceDocument(source.WorkspaceId, current.ContentRevision, current.Document).Success);
        string later = JsonSerializer.Serialize(target.Read(source.WorkspaceId));
        using var rollback = service.Review(target.Owner.Capture(), Encode(initial));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Conflict, rollback.Result.Outcome);
        Assert.AreEqual(later, JsonSerializer.Serialize(target.Read(source.WorkspaceId)));
    }

    [TestMethod]
    public void Consumed_create_review_and_lookup_only_recovery_cannot_resurrect_after_delete()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        using Target target = new();
        var service = Service(source, target);
        using var review = Review(service, target.Owner, Export(source.Store, source.WorkspaceId));
        _ = service.Confirm(review, explicitlyConfirmed: true); // The host loses its response.
        var committed = target.Read(source.WorkspaceId);
        Assert.IsTrue(target.Store.Delete(source.WorkspaceId, committed.ContentRevision).Success);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, service.Confirm(review, true).Outcome);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing,
            Service(source, target).Recover(target.Owner.Capture(), source.WorkspaceId,
                review.OperationId, review.AdmissionDigest!).Outcome);
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, target.Store.Get(source.WorkspaceId).Outcome);
    }

    [TestMethod]
    public void Lost_success_recovers_only_from_local_receipt_even_after_later_owner_edits_and_source_loss()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var faults = new RestoreFault();
        using Target target = new(faults);
        var resolver = new ObservedResolver(source.Resolver, target.Owner);
        var service = Service(source, target, resolver);
        using var review = Review(service, target.Owner, Export(source.Store, source.WorkspaceId));
        faults.Callback = (stage, _, _) =>
        {
            Assert.AreEqual(1, target.Owner.ActiveLeases);
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced)
                throw new IOException("Lost adapter observation after the real atomic commit.");
        };
        _ = service.Confirm(review, explicitlyConfirmed: true);
        faults.Callback = null;
        var committed = target.Read(source.WorkspaceId);
        Assert.IsNotNull(committed.LocalHistory!.LastRestore);
        Assert.IsTrue(target.Store.ReplaceWorkspaceDocument(source.WorkspaceId, committed.ContentRevision, committed.Document).Success);
        string before = JsonSerializer.Serialize(target.Read(source.WorkspaceId));
        resolver.Missing = true;
        int reads = resolver.Reads;
        var recovered = Service(source, target, resolver).Recover(target.Owner.Capture(), source.WorkspaceId,
            review.OperationId, review.AdmissionDigest!);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Recovered, recovered.Outcome, Describe(recovered));
        Assert.AreEqual(committed.LocalHistory.LastRestore, recovered.Receipt);
        Assert.IsTrue(recovered.Target!.ContentRevision > recovered.Receipt!.ContentRevision);
        Assert.AreEqual(reads, resolver.Reads, "Recovery is local proof lookup, never a second source evaluation or write.");
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing,
            service.Recover(target.Owner.Capture(), source.WorkspaceId, Guid.NewGuid(), review.AdmissionDigest!).Outcome);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing,
            service.Recover(target.Owner.Capture(), source.WorkspaceId, review.OperationId, new string('f', 64)).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(target.Read(source.WorkspaceId)));
    }

    [TestMethod]
    public void Source_loss_after_temp_flush_aborts_before_target_replace_under_the_actual_owner_lease()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        AdvanceSource(source, 2);
        var faults = new RestoreFault();
        using Target target = new(faults);
        Assert.IsTrue(target.Store.CreateWorkspaceDocument(source.WorkspaceId, PlainDocument()).Success);
        var resolver = new ObservedResolver(source.Resolver, target.Owner);
        var service = Service(source, target, resolver);
        using var review = Review(service, target.Owner, Export(source.Store, source.WorkspaceId));
        byte[] before = File.ReadAllBytes(target.RecordPath(source.WorkspaceId));
        DateTime timestamp = File.GetLastWriteTimeUtc(target.RecordPath(source.WorkspaceId));
        bool flushed = false;
        faults.Callback = (stage, _, _) =>
        {
            Assert.AreEqual(1, target.Owner.ActiveLeases);
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                flushed = true;
                resolver.Missing = true;
            }
        };
        var result = service.Confirm(review, explicitlyConfirmed: true);
        Assert.IsTrue(flushed, "The adverse source change must occur after durable temporary serialization.");
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Conflict, result.Outcome, Describe(result));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(target.RecordPath(source.WorkspaceId)));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(target.RecordPath(source.WorkspaceId)));
        Assert.AreEqual(0, target.Owner.ActiveLeases);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing,
            service.Recover(target.Owner.Capture(), source.WorkspaceId, review.OperationId, review.AdmissionDigest!).Outcome);
    }

    [TestMethod]
    [DataRow("foreign-issuer")]
    [DataRow("owner-ABA")]
    [DataRow("foreign-review")]
    [DataRow("not-explicit")]
    [DataRow("disposed")]
    public void Reviews_require_their_actual_owner_issuer_and_explicit_confirmation(string denial)
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        using Target target = new();
        var exported = Export(source.Store, source.WorkspaceId);
        var service = Service(source, target);
        using var review = Review(service, target.Owner, exported);
        WorkspaceContinuationRestoreResult result;
        if (denial == "foreign-issuer")
        {
            using var rejected = service.Review(new TestOwner(OwnerScope.LocalSingleUser).Capture(), Encode(exported));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Rejected, rejected.Result.Outcome);
            result = service.Confirm(rejected, true);
        }
        else if (denial == "owner-ABA")
        {
            target.Owner.Transition(new("another-owner"));
            target.Owner.Transition(OwnerScope.LocalSingleUser);
            result = service.Confirm(review, true);
        }
        else if (denial == "foreign-review") result = Service(source, target).Confirm(review, true);
        else if (denial == "not-explicit") result = service.Confirm(review, false);
        else { review.Dispose(); result = service.Confirm(review, true); }
        Assert.IsTrue(result.Outcome is WorkspaceContinuationRestoreOutcome.Rejected or WorkspaceContinuationRestoreOutcome.ReviewConsumed,
            Describe(result));
        Assert.IsNull(result.Receipt);
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, target.Store.Get(source.WorkspaceId).Outcome);
        Assert.AreEqual(0, target.Owner.ActiveLeases);
    }

    [TestMethod]
    public void NonUTC_timestamp_is_rejected_without_representing_equal_instants_as_equal_snapshot_digests()
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        using Target target = new();
        var exported = Export(source.Store, source.WorkspaceId);
        var changed = exported.Snapshot with { Workspace = exported.Snapshot.Workspace with
        {
            LastUpdatedUtc = exported.Snapshot.Workspace.LastUpdatedUtc.ToOffset(TimeSpan.FromHours(2))
        } };
        var shifted = WithDigest(changed);
        Assert.AreNotEqual(exported.SnapshotDigest, shifted.SnapshotDigest);
        using var review = Service(source, target).Review(target.Owner.Capture(), Encode(shifted));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Rejected, review.Result.Outcome);
        CollectionAssert.Contains(review.Result.Blockers!.ToArray(), "continuation-timestamp-representation-unsupported");
        Assert.AreEqual(WorkspaceOperationOutcome.Missing, target.Store.Get(source.WorkspaceId).Outcome);
    }

    private static WorkspaceContinuationRestoreService Service(ReadyContext source, Target target,
        ICharacterSourceDataResolver? resolver = null) => new(target.Store, target.Owner,
            resolver ?? new ObservedResolver(source.Resolver, target.Owner), source.Queries, Catalog(), MaximumBytes);

    private static WorkspaceContinuationRestoreReview Review(WorkspaceContinuationRestoreService service,
        TestOwner owner, WorkspaceContinuationExport exported)
    {
        var review = service.Review(owner.Capture(), Encode(exported));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome, Describe(review.Result));
        return review;
    }

    private static WorkspaceContinuationExport Export(FileWorkspaceStore store, CharacterWorkspaceId id)
    {
        var owner = new TestOwner(OwnerScope.LocalSingleUser);
        var result = new WorkspaceContinuationExportService(store, owner).Export(owner.Capture(), id);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void AssertRestored(Target target, WorkspaceContinuationExport exported, WorkspaceContinuationRestoreReceipt receipt)
    {
        Assert.IsNotNull(receipt);
        var cold = Export(new FileWorkspaceStore(target.Directory), exported.Snapshot.Workspace.Id);
        Assert.AreEqual(exported.SnapshotDigest, cold.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(exported.Snapshot), JsonSerializer.Serialize(cold.Snapshot));
        var current = target.Read(exported.Snapshot.Workspace.Id);
        Assert.AreEqual(receipt, current.LocalHistory!.LastRestore);
        Assert.AreEqual(exported.Snapshot.Workspace.ContentRevision, current.LocalHistory.ImportedThroughRevision);
        Assert.AreEqual(exported.SnapshotDigest, current.LocalHistory.ImportedSnapshotDigest);
        Assert.AreEqual(receipt.IncarnationId, current.LocalHistory.IncarnationId);
        Assert.IsTrue(current.LocalHistory.IsValid(current.ContentRevision));
        Assert.AreEqual(0, target.Owner.ActiveLeases);
    }

    private static void AdvanceSource(ReadyContext source, int revision)
    {
        var current = source.Store.Get(source.WorkspaceId).Value!;
        while (current.ContentRevision < revision)
        {
            Assert.IsTrue(source.Store.ReplaceWorkspaceDocument(source.WorkspaceId,
                current.ContentRevision, current.Document).Success);
            current = source.Store.Get(source.WorkspaceId).Value!;
        }
    }

    private static WorkspaceDocument PlainDocument() => new(
        "<character><name>Existing target</name><metatype>Human</metatype><created>False</created></character>", "sr5");
    private static WorkspaceContinuationExport WithDigest(WorkspaceContinuationSnapshot snapshot) =>
        new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot));
    private static byte[] Encode(WorkspaceContinuationExport exported) => WorkspaceContinuationCodec.Encode(exported, MaximumBytes);
    private static string Describe(WorkspaceContinuationRestoreResult result) => JsonSerializer.Serialize(result);
    private static XmlLifeModulesCatalogService Catalog()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
        }
        throw new DirectoryNotFoundException("Canonical Life Modules data is missing.");
    }

    private sealed class Target : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("chummer-continuation-restore-").FullName;
        public FileWorkspaceStore Store { get; }
        public TestOwner Owner { get; } = new(OwnerScope.LocalSingleUser);
        public Target(IFileWorkspaceStoreFaultInjector? fault = null) => Store = fault is null
            ? new(Directory) : new(Directory, fault);
        public WorkspaceStoredDocument Read(CharacterWorkspaceId id)
        {
            var read = new FileWorkspaceStore(Directory).Get(id);
            Assert.IsTrue(read.Success, read.Error);
            return read.Value!;
        }
        public string RecordPath(CharacterWorkspaceId id) => Path.Combine(Directory, "workspaces", id.Value + ".json");
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class ObservedResolver(ICharacterSourceDataResolver inner, TestOwner owner) : ICharacterSourceDataResolver
    {
        public bool Missing { get; set; }
        public int Reads { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            Assert.AreEqual(1, owner.ActiveLeases, "Every live restore source read must retain the actual owner lease.");
            Reads++;
            return Missing ? null : inner.TryCreateContext(xml);
        }
    }

    private sealed class RestoreFault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage, string, string>? Callback { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string temporaryPath) =>
            Callback?.Invoke(stage, targetPath, temporaryPath);
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
            lock (_gate) { Assert.AreEqual(0, ActiveLeases); _owner = owner; _revision++; }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases, "Restore must not assume reentrant owner admission.");
            if (!expected.IsValid || expected != new OwnerContextStamp(_owner, _issuer, _revision))
            {
                Monitor.Exit(_gate); lease = null; return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }
        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true; owner.ActiveLeases--; Monitor.Exit(owner._gate);
            }
        }
    }
}
