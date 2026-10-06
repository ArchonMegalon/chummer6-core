using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CreationReadObservationTests
{
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Load_reads_once_but_a_later_operation_reads_fresh(bool magic, bool local)
    {
        var owners = new Owners(local);
        var store = new CountingStore(owners.Current);
        var resolver = new UnavailableSources();
        var qualities = new OwnerBoundCharacterCreationQualitiesService(store, owners,
            new XmlCharacterFileQueries(new CharacterFileService()), resolver);
        var talents = new OwnerBoundCharacterCreationMagicResonanceService(store, owners, resolver);
        long Load() => magic
            ? talents.Load(owners.Capture(), new(store.Id)).Value!.Binding.ContentRevision
            : qualities.Load(owners.Capture(), new(store.Id)).Value!.Binding.ContentRevision;

        Assert.AreEqual(7L, Load());
        Assert.AreEqual(1, store.Reads, "Dependent projections must share this one fresh store observation.");
        Assert.IsTrue(resolver.Calls > 1, "Do not remove dependent rule/source evaluation.");
        Assert.AreEqual(0, owners.ActiveLeases);
        int sourceCalls = resolver.Calls;
        store.Current = store.Current with { ContentRevision = 8, SavedRevision = 8 };
        Assert.AreEqual(8L, Load());
        Assert.AreEqual(2, store.Reads, "Never retain a snapshot across public operations.");
        Assert.AreEqual(sourceCalls * 2, resolver.Calls);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Snapshot_preserves_history_and_never_bypasses_lease_or_partition(bool local)
    {
        var owners = new Owners(local);
        var store = new CountingStore(owners.Current);
        using var lease = new Lease(owners.Capture());
        var view = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id, true);
        var first = view.Get(store.Id);
        Assert.AreSame(first, view.Get(owners.Current, store.Id));
        Assert.AreSame(store.Current, first.Value);
        Assert.IsFalse(first.Value!.CanReplayReceipt(7), "Imported history must not become local receipt authority.");
        Assert.IsNull(view.Get(new OwnerScope("foreign"), store.Id).Value);
        Assert.IsNull(view.Get(new CharacterWorkspaceId("foreign-workspace")).Value);
        Assert.AreEqual(1, store.Reads);
        lease.Dispose();
        Assert.IsNull(view.Get(store.Id).Value);
        Assert.IsFalse(view.SupportsWorkspaceAuxiliaryStateAtomicCommit);
        Assert.AreEqual(1, store.Reads);
    }

    [TestMethod]
    public void Default_view_and_failed_reads_are_not_cached()
    {
        var owners = new Owners(false);
        var store = new CountingStore(owners.Current);
        using var lease = new Lease(owners.Capture());
        var ordinary = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id);
        Assert.AreEqual(7L, ordinary.Get(store.Id).Value!.ContentRevision);
        store.Current = store.Current with { ContentRevision = 8 };
        Assert.AreEqual(8L, ordinary.Get(store.Id).Value!.ContentRevision);
        Assert.AreEqual(2, store.Reads, "The default mutation/preview view must still reread.");
        var snapshot = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id, true);
        store.Outcome = WorkspaceOperationOutcome.Corrupt;
        Assert.IsNull(snapshot.Get(store.Id).Value);
        store.Outcome = WorkspaceOperationOutcome.Success;
        Assert.AreEqual(8L, snapshot.Get(store.Id).Value!.ContentRevision);
        Assert.AreEqual(4, store.Reads);
    }

    [TestMethod]
    [DataRow(false, 3)]
    [DataRow(true, 2)]
    public void Rule_projection_is_identical_to_the_fresh_read_path(bool magic, int oldReads)
    {
        var owners = new Owners(false);
        var store = new CountingStore(owners.Current);
        using var lease = new Lease(owners.Capture());
        var resolver = new UnavailableSources();
        string Project(bool reuse)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id, reuse);
            if (magic)
                return JsonSerializer.Serialize(new CharacterCreationMagicResonanceService(view, resolver).Load(new(store.Id)));
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var prerequisites = new CharacterCreationPrerequisiteService(view, queries, resolver);
            var attributes = new CharacterCreationAttributesService(view, resolver);
            return JsonSerializer.Serialize(new CharacterCreationQualitiesService(view, resolver,
                prerequisites, attributes).Load(new(store.Id)));
        }
        string expected = Project(false);
        Assert.AreEqual(oldReads, store.Reads);
        int sourceCalls = resolver.Calls;
        Assert.AreEqual(expected, Project(true), "Do not change bindings, missing-authority blockers or rule output.");
        Assert.AreEqual(oldReads + 1, store.Reads);
        Assert.AreEqual(sourceCalls * 2, resolver.Calls);
    }

    [TestMethod]
    public void Cached_observation_does_not_hide_an_owner_transition_away_and_back()
    {
        var owners = new Owners(false);
        var store = new CountingStore(owners.Current);
        using var lease = new Lease(owners.Capture());
        var view = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id, true);
        Assert.IsNotNull(view.Get(store.Id).Value);
        lease.CurrentStamp = owners.Capture() with { Owner = new OwnerScope("other"), TransitionRevision = 2 };
        Assert.IsNull(view.Get(store.Id).Value);
        lease.CurrentStamp = owners.Capture() with { TransitionRevision = 3 };
        Assert.IsNull(view.Get(store.Id).Value);
        Assert.AreEqual(1, store.Reads);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Even_a_rejected_atomic_write_disables_the_read_snapshot(bool local)
    {
        var owners = new Owners(local);
        var store = new CountingStore(owners.Current);
        using var lease = new Lease(owners.Capture());
        var view = new OwnerBoundCreationWorkspaceStore(store, lease, owners.Capture(), store.Id, true);
        var first = view.Get(store.Id).Value!;
        Assert.IsFalse(view.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            store.Id, first.ContentRevision, first.Document.AuxiliaryStateDigest, first.Document).Success);
        store.Current = store.Current with { ContentRevision = 8 };
        Assert.AreEqual(8L, view.Get(store.Id).Value!.ContentRevision);
        store.Current = store.Current with { ContentRevision = 9 };
        Assert.AreEqual(9L, view.Get(store.Id).Value!.ContentRevision);
        Assert.AreEqual(3, store.Reads);
    }

    private sealed class UnavailableSources : ICharacterSourceDataResolver
    {
        public int Calls { get; private set; }
        public ICharacterSourceDataContext? TryCreateContext(string xml) { Calls++; return null; }
    }

    private sealed class Owners(bool local) : IOwnerContextLeaseAccessor
    {
        private readonly OwnerContextStamp _stamp = new(
            local ? OwnerScope.LocalSingleUser : new OwnerScope("read-test-owner"), "read-test-authority", 1);
        public int ActiveLeases { get; private set; }
        public OwnerScope Current => _stamp.Owner;
        public OwnerContextStamp Capture() => _stamp;
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            lease = null;
            if (expected != _stamp || ActiveLeases != 0) return false;
            ActiveLeases++;
            lease = new Lease(_stamp, () => ActiveLeases--);
            return true;
        }
    }

    private sealed class Lease(OwnerContextStamp stamp, Action? released = null) : IOwnerContextLease
    {
        private bool _disposed;
        public OwnerContextStamp CurrentStamp { get; set; } = stamp;
        public OwnerContextStamp Stamp { get { ObjectDisposedException.ThrowIf(_disposed, this); return CurrentStamp; } }
        public void Dispose() { if (_disposed) return; _disposed = true; released?.Invoke(); }
    }

    private sealed class CountingStore(OwnerScope owner) : IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability, IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public CharacterWorkspaceId Id { get; } = new("read-observation-test");
        public int Reads { get; private set; }
        public WorkspaceOperationOutcome Outcome { get; set; } = WorkspaceOperationOutcome.Success;
        public WorkspaceStoredDocument Current { get; set; } = new(new("read-observation-test"),
            new WorkspaceDocument("<character><name>Read test</name><created>False</created><buildmethod>LifeModule</buildmethod></character>", "sr5"),
            7, 7, DateTimeOffset.UnixEpoch)
        {
            LocalHistory = new("11111111111111111111111111111111", 7, new string('a', 64))
        };
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            Assert.IsTrue(owner.IsLocalSingleUser, "No ambient fallback for a linked owner.");
            return Get(owner, id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope requested, CharacterWorkspaceId id)
        {
            Assert.AreEqual(owner, requested);
            Assert.AreEqual(Id, id);
            Reads++;
            return new(Outcome, Outcome == WorkspaceOperationOutcome.Success ? Current : null);
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => [];
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope requested) => [];
        private static WorkspaceStoreMutationResult Denied() => new(WorkspaceOperationOutcome.Unavailable);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope requested, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope requested, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope requested, CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult Delete(OwnerScope requested, CharacterWorkspaceId id, long revision) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document) => Denied();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope requested, CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document) => Denied();
    }
}
