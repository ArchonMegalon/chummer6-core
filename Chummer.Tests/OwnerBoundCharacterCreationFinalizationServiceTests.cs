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
[DoNotParallelize]
public sealed class OwnerBoundCharacterCreationFinalizationServiceTests
{
    private static readonly OwnerScope AccountA = new("finalization-account-a");
    private static readonly OwnerScope AccountB = new("finalization-account-b");
    private static ReadyContext s_source = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        // One genuine, nonempty, complete awakened draft graph. Each test gets
        // independent private storage; no test may mutate this source fixture.
        s_source = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 2);
    }

    [ClassCleanup]
    public static void Cleanup() => s_source?.Dispose();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Full_canonical_graph_finalizes_only_the_admitted_partition_and_replays_cold(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var before = fixture.Read(fixture.Owner.Current);
        var unchanged = fixture.CaptureOtherPartitions();
        var observed = new ScopedAtomicStore(fixture);
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        Assert.AreEqual(0, observed.Commits);
        AssertRecordEquals(before, fixture.Read(stamp.Owner));

        var result = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        Assert.IsEmpty(result.Blockers);
        Assert.AreEqual(1, observed.Commits);
        Assert.IsGreaterThanOrEqualTo(12, observed.Reads,
            "Finalization must evaluate its complete graph and reread its receipt through the scoped view.");
        Assert.AreEqual(linked ? 0 : observed.Reads, observed.LocalReads);
        var after = fixture.Read(stamp.Owner);
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.LocalHistory, after.LocalHistory);
        CollectionAssert.AreEqual(before.DelegatedGmHistorySegmentStarts.ToArray(),
            after.DelegatedGmHistorySegmentStarts.ToArray());
        Assert.IsTrue(s_source.Queries.ParseSummary(new(after.Document.Content)).Created);
        Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        AssertJsonEquals(before.Document.AuxiliaryState,
            after.Document.AuxiliaryState.CharacterCreationFinalizationArchive.State);
        Assert.HasCount(1, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!);
        Assert.AreEqual(result.Value, after.Document.AuxiliaryState.CharacterCreationFinalizationReceipts![0].Receipt);
        Assert.IsTrue(after.CanReplayReceipt(result.Value.ContentRevision));
        fixture.AssertOtherPartitionsUnchanged(unchanged);

        var committedBytes = fixture.CapturePartition(stamp.Owner);
        var coldService = Service(fixture, new ScopedAtomicStore(fixture));
        var replay = coldService.Confirm(stamp, command);
        var lookup = coldService.LookupReceipt(stamp, new(fixture.Id, command.IdempotencyKey));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, replay.Outcome, Describe(replay));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, lookup.Outcome, Describe(lookup));
        Assert.AreEqual(result.Value, replay.Value);
        Assert.AreEqual(result.Value, lookup.Value);
        Assert.AreEqual(committedBytes, fixture.CapturePartition(stamp.Owner));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict,
            coldService.Confirm(stamp, command with { PlanDigest = "different-plan" }).Outcome);
        Assert.AreEqual(committedBytes, fixture.CapturePartition(stamp.Owner));
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    [DataRow("foreign-issuer")]
    [DataRow("switched-owner")]
    [DataRow("owner-ABA")]
    [DataRow("unknown")]
    public void All_four_methods_reject_invalid_original_admission_before_any_store_read(string denial)
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var service = Service(fixture, observed);
        var original = fixture.Owner.Capture();
        var command = Review(service, original, fixture.Id);
        OwnerContextStamp rejected = original;
        if (denial == "foreign-issuer") rejected = new TestOwner(AccountA).Capture();
        else if (denial == "switched-owner") fixture.Owner.Transition(AccountB);
        else if (denial == "owner-ABA")
        {
            fixture.Owner.Transition(AccountB);
            fixture.Owner.Transition(AccountA);
        }
        else rejected = default;
        int reads = observed.Reads;
        var before = fixture.CaptureAllPartitions();
        AssertUnavailable(service.Load(rejected, new(fixture.Id)));
        AssertUnavailable(service.Review(rejected, new(command.Binding)));
        AssertUnavailable(service.Confirm(rejected, command));
        AssertUnavailable(service.LookupReceipt(rejected, new(fixture.Id, command.IdempotencyKey)));
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Missing_lease_capability_and_spoofed_local_identity_never_fall_back_to_local_storage()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var command = Review(Service(fixture, observed), fixture.Owner.Capture(), fixture.Id);
        int reads = observed.Reads;
        var currentOnly = new OwnerBoundCharacterCreationFinalizationService(observed,
            new CurrentOnlyOwner(AccountA), s_source.Queries, s_source.Resolver);
        AssertUnavailable(currentOnly.Load(fixture.Owner.Capture(), new(fixture.Id)));
        AssertUnavailable(currentOnly.Confirm(fixture.Owner.Capture(), command));

        var spoof = new TestOwner(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        var spoofed = new OwnerBoundCharacterCreationFinalizationService(observed,
            spoof, s_source.Queries, s_source.Resolver);
        AssertUnavailable(spoofed.Load(spoof.Capture(), new(fixture.Id)));
        AssertUnavailable(spoofed.Confirm(spoof.Capture(), command));
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Missing_scoped_atomic_capability_cannot_be_replaced_by_a_local_only_capability(bool localOnly)
    {
        using var fixture = new Fixture(AccountA);
        var command = Review(Service(fixture, new ScopedAtomicStore(fixture)), fixture.Owner.Capture(), fixture.Id);
        ObservedStore store = localOnly ? new LocalAtomicStore(fixture) : new ObservedStore(fixture);
        var before = fixture.CaptureAllPartitions();
        var result = Service(fixture, store).Confirm(fixture.Owner.Capture(), command);
        Assert.IsNull(result.Value);
        Assert.AreNotEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome);
        Assert.AreNotEqual(CharacterCreationFinalizationOutcomes.Replayed, result.Outcome);
        Assert.IsNotEmpty(result.Blockers);
        Assert.AreEqual(0, store.LocalReads);
        fixture.AssertPartitionsUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Missing_workspace_returns_owner_confined_NotFound_without_local_fallback()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture);
        var service = Service(fixture, observed);
        var result = service.Load(fixture.Owner.Capture(), new(new("absent-workspace")));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.NotFound, result.Outcome);
        Assert.IsNull(result.Value);
        Assert.AreEqual(1, observed.Reads);
        Assert.AreEqual(0, observed.LocalReads);
        Assert.AreEqual(0, observed.Commits);
    }

    [TestMethod]
    public void Known_commit_keeps_receipt_if_reopen_is_unavailable_and_lookup_remains_owner_bound()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture) { FailReadsAfterCommit = true };
        var service = Service(fixture, observed);
        var original = fixture.Owner.Capture();
        var command = Review(service, original, fixture.Id);
        var result = service.Confirm(original, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFinalizationBlockers.PostCommitReopenRequired);
        Assert.AreEqual(1, observed.Commits);
        var before = fixture.CaptureAllPartitions();
        fixture.Owner.Transition(AccountB);
        int reads = observed.Reads;
        AssertUnavailable(service.LookupReceipt(original, new(fixture.Id, command.IdempotencyKey)));
        Assert.AreEqual(reads, observed.Reads);
        fixture.AssertPartitionsUnchanged(before);
        fixture.Owner.Transition(AccountA);
        var current = fixture.Owner.Capture();
        Assert.AreNotEqual(original, current);
        var recovered = Service(fixture, new ScopedAtomicStore(fixture))
            .LookupReceipt(current, new(fixture.Id, command.IdempotencyKey));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, recovered.Outcome, Describe(recovered));
        Assert.AreEqual(result.Value, recovered.Value);
        fixture.AssertPartitionsUnchanged(before);
    }

    [TestMethod]
    public void Commit_that_reports_unavailable_recovers_only_its_scoped_locally_committed_receipt()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedAtomicStore(fixture) { ReportUnavailableAfterCommit = true };
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        var result = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Replayed, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(1, observed.Commits);
        Assert.AreEqual(result.Value, fixture.Read(AccountA).Document.AuxiliaryState
            .CharacterCreationFinalizationReceipts!.Single().Receipt);
        Assert.AreEqual(0, observed.LocalReads);
    }

    [TestMethod]
    public void Actual_same_owner_continuation_restore_does_not_promote_imported_finalization_receipts()
    {
        using var fixture = new Fixture(AccountA);
        var service = Service(fixture, new ScopedAtomicStore(fixture));
        var stamp = fixture.Owner.Capture();
        var command = Review(service, stamp, fixture.Id);
        var committed = service.Confirm(stamp, command);
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, committed.Outcome, Describe(committed));
        Assert.IsNotNull(committed.Value);
        var exported = new WorkspaceContinuationExportService(fixture.Store, fixture.Owner).Export(stamp, fixture.Id);
        Assert.IsTrue(exported.Success, exported.Error);
        Assert.IsNotNull(exported.Value);
        Assert.AreEqual(AccountA.NormalizedValue, exported.Value.Snapshot.OwnerId);

        string targetDirectory = Directory.CreateTempSubdirectory("chummer-finalization-import-").FullName;
        try
        {
            var target = new FileWorkspaceStore(targetDirectory);
            const int maximumBytes = 16 * 1024 * 1024;
            var restore = new WorkspaceContinuationRestoreService(target, fixture.Owner,
                s_source.Resolver, s_source.Queries, Catalog(), maximumBytes);
            using var review = restore.Review(stamp, WorkspaceContinuationCodec.Encode(exported.Value, maximumBytes));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
                JsonSerializer.Serialize(review.Result));
            var restored = restore.Confirm(review, explicitlyConfirmed: true);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restored.Outcome, JsonSerializer.Serialize(restored));
            var coldStore = new FileWorkspaceStore(targetDirectory);
            var current = coldStore.Get(AccountA, fixture.Id).Value!;
            Assert.IsNotNull(current.LocalHistory);
            Assert.AreEqual(current.ContentRevision, current.LocalHistory.ImportedThroughRevision);
            Assert.AreEqual(exported.Value.SnapshotDigest, current.LocalHistory.ImportedSnapshotDigest);
            Assert.IsFalse(current.CanReplayReceipt(committed.Value.ContentRevision));
            AssertJsonEquals(fixture.Read(AccountA).Document, current.Document);
            CollectionAssert.AreEqual(fixture.Read(AccountA).DelegatedGmHistorySegmentStarts.ToArray(),
                current.DelegatedGmHistorySegmentStarts.ToArray());
            var importedService = new OwnerBoundCharacterCreationFinalizationService(coldStore, fixture.Owner,
                s_source.Queries, s_source.Resolver);
            var before = JsonSerializer.Serialize(current);
            var replay = importedService.Confirm(stamp, command);
            var lookup = importedService.LookupReceipt(stamp, new(fixture.Id, command.IdempotencyKey));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, replay.Outcome, Describe(replay));
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Conflict, lookup.Outcome, Describe(lookup));
            Assert.IsNull(replay.Value);
            Assert.IsNull(lookup.Value);
            Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(targetDirectory).Get(AccountA, fixture.Id).Value));
        }
        finally { Directory.Delete(targetDirectory, recursive: true); }
    }

    private static OwnerBoundCharacterCreationFinalizationService Service(Fixture fixture, ObservedStore store)
        => new(store, fixture.Owner, s_source.Queries, new ObservedResolver(fixture, s_source.Resolver));

    private static CharacterCreationFinalizationConfirmRequest Review(
        IOwnerBoundCharacterCreationFinalizationService service, OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        var loaded = service.Load(owner, new(id));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, loaded.Outcome, Describe(loaded));
        Assert.IsNotNull(loaded.Value);
        Assert.HasCount(7, loaded.Value.Steps);
        Assert.IsTrue(loaded.Value.Steps.All(step => step.IsComplete), Describe(loaded));
        var reviewed = service.Review(owner, new(loaded.Value.Binding));
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Available, reviewed.Outcome, Describe(reviewed));
        Assert.IsNotNull(reviewed.Value);
        Assert.IsNotNull(reviewed.Value.Plan);
        Assert.IsTrue(reviewed.Value.CanConfirm, Describe(reviewed));
        return new(loaded.Value.Binding, reviewed.Value.PreviewDigest, reviewed.Value.Plan.PlanDigest,
            "owner-bound-finalization", ExplicitlyConfirmed: true);
    }

    private static void AssertUnavailable<T>(CharacterCreationFinalizationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFinalizationOutcomes.Unavailable, result.Outcome, Describe(result));
        Assert.IsNull(result.Value);
    }

    private static string Describe<T>(CharacterCreationFinalizationResult<T> result) where T : class
        => JsonSerializer.Serialize(result);
    private static void AssertJsonEquals<T>(T expected, T actual)
        => Assert.IsTrue(JsonElement.DeepEquals(JsonSerializer.SerializeToElement(expected),
            JsonSerializer.SerializeToElement(actual)));
    private static void AssertRecordEquals(WorkspaceStoredDocument expected, WorkspaceStoredDocument actual)
        => AssertJsonEquals(expected, actual);

    private static XmlLifeModulesCatalogService Catalog()
    {
        for (DirectoryInfo? path = new(AppContext.BaseDirectory); path is not null; path = path.Parent)
        {
            string candidate = Path.Combine(path.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(candidate)) return new(candidate);
        }
        throw new DirectoryNotFoundException("Canonical Core Life Modules catalog was not staged.");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly Dictionary<OwnerScope, string> _paths = [];
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-finalization-owner-").FullName;
        public FileWorkspaceStore Store { get; }
        public TestOwner Owner { get; }
        public CharacterWorkspaceId Id => s_source.WorkspaceId;

        public Fixture(OwnerScope owner)
        {
            Store = new(Root);
            Owner = new(owner);
            foreach (var partition in new[] { OwnerScope.LocalSingleUser, AccountA, AccountB })
            {
                string[] oldPaths = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories);
                var placeholder = new WorkspaceDocument(
                    "<character><name>Unready shadow</name><metatype>Human</metatype><created>False</created></character>", "sr5");
                var created = partition.IsLocalSingleUser
                    ? Store.CreateWorkspaceDocument(Id, placeholder)
                    : Store.CreateWorkspaceDocument(partition, Id, placeholder);
                Assert.IsTrue(created.Success, created.Error);
                string recordPath = Directory.GetFiles(Root, Id.Value + ".json", SearchOption.AllDirectories)
                    .Except(oldPaths, StringComparer.Ordinal).Single();
                _paths.Add(partition, recordPath);
                if (partition != owner) continue;

                // TEST FIXTURE STORAGE, not authenticated restore or owner migration.
                // The destination was created through its real scoped FileStore API.
                // Install exact bytes from a genuinely committed ReadyContext; never
                // relabel a portable owner/digest or invent rule-valid draft objects.
                string sourcePath = Path.Combine(s_source.Directory, "workspaces", Id.Value + ".json");
                File.Copy(sourcePath, recordPath, overwrite: true);
                File.SetLastWriteTimeUtc(recordPath, File.GetLastWriteTimeUtc(sourcePath));
                CollectionAssert.AreEqual(File.ReadAllBytes(sourcePath), File.ReadAllBytes(recordPath));
                AssertRecordEquals(s_source.Store.Get(Id).Value!, Read(partition));
            }
            Assert.IsNull(Read(owner == AccountA ? AccountB : AccountA)
                .Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        }

        public WorkspaceStoredDocument Read(OwnerScope owner)
        {
            var cold = new FileWorkspaceStore(Root);
            var result = owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(owner, Id);
            Assert.IsTrue(result.Success, result.Error);
            Assert.IsNotNull(result.Value);
            return result.Value;
        }

        public (string Bytes, DateTime Timestamp) CapturePartition(OwnerScope owner)
            => (Convert.ToBase64String(File.ReadAllBytes(_paths[owner])), File.GetLastWriteTimeUtc(_paths[owner]));
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> CaptureAllPartitions()
            => _paths.Keys.ToDictionary(owner => owner, CapturePartition);
        public Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> CaptureOtherPartitions()
            => _paths.Keys.Where(owner => owner != Owner.Current).ToDictionary(owner => owner, CapturePartition);
        public void AssertOtherPartitionsUnchanged(Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> before)
            => AssertPartitionsUnchanged(before);
        public void AssertPartitionsUnchanged(Dictionary<OwnerScope, (string Bytes, DateTime Timestamp)> before)
        {
            foreach (var entry in before) Assert.AreEqual(entry.Value, CapturePartition(entry.Key), entry.Key.Value);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class ObservedResolver(Fixture fixture, ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
        {
            fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(s_source.Store.Get(s_source.WorkspaceId).Value!.Document.Content, characterXml);
            return inner.TryCreateContext(characterXml);
        }
    }

    private class ObservedStore(Fixture fixture) : IWorkspaceStore
    {
        protected Fixture Fixture { get; } = fixture;
        public int Reads { get; private set; }
        public int LocalReads { get; private set; }
        public bool ReadUnavailable { get; set; }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            LocalReads++;
            return Read(OwnerScope.LocalSingleUser, id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => Read(owner, id);
        private WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId id)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner, "Every nested evaluator must use the admitted partition.");
            Reads++;
            return ReadUnavailable ? new(WorkspaceOperationOutcome.Unavailable)
                : owner.IsLocalSingleUser ? new FileWorkspaceStore(Fixture.Root).Get(id)
                : new FileWorkspaceStore(Fixture.Root).Get(owner, id);
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw new AssertFailedException("Unexpected inventory.");
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw new AssertFailedException("Unexpected inventory.");
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => Unexpected();
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => Unexpected();
        protected static WorkspaceStoreMutationResult Unexpected() => throw new AssertFailedException("Unexpected mutation lane.");
    }

    private class LocalAtomicStore(Fixture fixture) : ObservedStore(fixture), IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public int Commits { get; protected set; }
        public bool FailReadsAfterCommit { get; init; }
        public bool ReportUnavailableAfterCommit { get; init; }
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(OwnerScope.LocalSingleUser, id, revision, digest, document);
        protected WorkspaceStoreMutationResult Commit(OwnerScope owner, CharacterWorkspaceId id,
            long revision, string digest, WorkspaceDocument document)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner);
            var result = owner.IsLocalSingleUser
                ? Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, revision, digest, document)
                : Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(owner, id, revision, digest, document);
            if (result.Success)
            {
                Commits++;
                ReadUnavailable = FailReadsAfterCommit;
                if (ReportUnavailableAfterCommit) return new(WorkspaceOperationOutcome.Unavailable);
            }
            return result;
        }
    }

    private sealed class ScopedAtomicStore(Fixture fixture) : LocalAtomicStore(fixture), IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(owner, id, revision, digest, document);
    }

    private sealed class CurrentOnlyOwner(OwnerScope owner) : IOwnerContextAccessor
    {
        public OwnerScope Current => owner;
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
        public void AssertLeaseHeld()
        {
            Assert.IsTrue(Monitor.IsEntered(_gate), "Owner admission must stay on the calling thread.");
            Assert.AreEqual(1, ActiveLeases, "Admission must cover source reads, atomic commit and receipt lookup.");
        }
        public void Transition(OwnerScope owner)
        {
            lock (_gate) { Assert.AreEqual(0, ActiveLeases); _owner = owner; _revision++; }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases, "The finalizer must not reacquire a nested lease.");
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
        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                owner.AssertLeaseHeld();
                _disposed = true;
                owner.ActiveLeases--;
                Monitor.Exit(owner._gate);
            }
        }
    }
}
