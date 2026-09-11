using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
[DoNotParallelize]
public sealed class OwnerBoundCharacterCreationPrerequisiteServiceTests
{
    private static readonly OwnerScope AccountA = new("prerequisite-account-a");
    private static readonly OwnerScope AccountB = new("prerequisite-account-b");
    private static readonly string[] SourceFiles = ["settings.xml", "priorities.xml", "metatypes.xml", "skills.xml"];
    private static readonly ICharacterFileQueries Queries = new XmlCharacterFileQueries(new CharacterFileService());
    private static string s_root = null!;
    private static ICharacterSourceDataResolver s_resolver = null!;
    private static WorkspaceStoredDocument s_bootstrap = null!;

    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        s_root = Directory.CreateTempSubdirectory("chummer-prerequisite-owner-source-").FullName;
        try
        {
            DirectoryInfo? root = new(AppDomain.CurrentDomain.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Chummer", "data", "settings.xml")))
                root = root.Parent;
            Assert.IsNotNull(root, "The actual canonical Core data tree must be available.");
            CopySources(Path.Combine(root.FullName, "Chummer"), s_root);
            s_resolver = Resolver(s_root);
            var store = new FileWorkspaceStore(Path.Combine(s_root, "bootstrap-store"));
            var codec = new Sr5WorkspaceCodec(Queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(s_resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(store,
                new RulesetWorkspaceCodecResolver([codec]), Queries, s_resolver);
            var result = bootstrap.Create(new(CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
                "Scoped prerequisite runner", "Owner fixture", CharacterCreationBuildMethods.Priority,
                CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, result.Outcome, Describe(result));
            Assert.IsNotNull(result.Value);
            var read = store.Get(result.Value.WorkspaceId);
            Assert.IsTrue(read.Success, read.Error);
            s_bootstrap = read.Value!;
            Assert.AreEqual(1L, s_bootstrap.ContentRevision);
            Assert.AreEqual(0L, s_bootstrap.SavedRevision);
        }
        catch
        {
            Directory.Delete(s_root, recursive: true);
            throw;
        }
    }

    [ClassCleanup]
    public static void Cleanup()
    {
        if (Directory.Exists(s_root)) Directory.Delete(s_root, recursive: true);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Actual_prerequisite_checkpoint_changes_only_the_admitted_partition(bool linked)
    {
        using var fixture = new Fixture(linked ? AccountA : OwnerScope.LocalSingleUser);
        var observed = new ScopedStore(fixture);
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var before = fixture.Capture();
        var original = fixture.Read(stamp.Owner);
        var request = Prepare(service, stamp, fixture.Id);
        fixture.AssertUnchanged(before);
        Assert.AreEqual(0, observed.Commits);

        var result = service.Confirm(stamp, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, Describe(result));
        Assert.IsNotNull(result.Value);
        Assert.IsEmpty(result.Blockers);
        Assert.AreEqual(1, observed.Commits);
        Assert.IsGreaterThanOrEqualTo(3, observed.Reads);
        Assert.AreEqual(linked ? 0 : observed.Reads, observed.LocalReads);
        var after = fixture.Read(stamp.Owner);
        Assert.AreEqual(original.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(original.Document.Content, after.Document.Content);
        Assert.AreEqual(original.LocalHistory, after.LocalHistory);
        Assert.IsFalse(result.Value.CharacterDocumentChanged);
        Assert.AreEqual(after.Id, result.Value.WorkspaceId);
        Assert.AreEqual(original.ContentRevision, result.Value.PreviousContentRevision);
        Assert.AreEqual(after.ContentRevision, result.Value.ContentRevision);
        Assert.AreEqual(after.SavedRevision, result.Value.SavedRevision);
        Assert.IsNotNull(after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft);
        Assert.AreEqual(result.Value.DraftDigest, after.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft.DraftDigest);
        Assert.AreEqual(result.Value.AuthorityDigest, request.Binding.AuthorityDigest);
        fixture.AssertUnchanged(before, except: stamp.Owner);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);

        var committed = fixture.Capture();
        var stale = service.Confirm(stamp, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome, Describe(stale));
        Assert.IsNull(stale.Value);
        Assert.AreEqual(1, observed.Commits, "This API must not invent replay or retry semantics.");
        fixture.AssertUnchanged(committed);
    }

    [TestMethod]
    [DataRow("foreign-issuer")]
    [DataRow("owner-B")]
    [DataRow("owner-ABA")]
    [DataRow("unknown")]
    public void All_three_methods_reject_stale_original_owner_before_any_read(string scenario)
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedStore(fixture);
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var request = Prepare(service, stamp, fixture.Id);
        if (scenario == "foreign-issuer") stamp = new TestOwner(AccountA).Capture();
        else if (scenario == "unknown") stamp = default;
        else
        {
            fixture.Owner.Transition(AccountB);
            if (scenario == "owner-ABA") fixture.Owner.Transition(AccountA);
        }
        var before = fixture.Capture();
        int reads = observed.Reads;
        AssertUnavailable(service.Load(stamp, new(fixture.Id)));
        AssertUnavailable(service.Preview(stamp, PreviewRequest(request)));
        AssertUnavailable(service.Confirm(stamp, request));
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
        fixture.AssertUnchanged(before);
    }

    [TestMethod]
    public void Missing_lease_and_forged_local_alias_never_fall_back_to_local_storage()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedStore(fixture);
        var request = Prepare(Service(fixture, observed), fixture.Owner.Capture(), fixture.Id);
        var before = fixture.Capture();
        int reads = observed.Reads;
        var currentOnly = new OwnerBoundCharacterCreationPrerequisiteService(observed,
            new CurrentOnlyOwner(AccountA), Queries, s_resolver);
        var forged = new TestOwner(new OwnerScope(OwnerScope.LocalSingleUser.Value));
        var forgedService = new OwnerBoundCharacterCreationPrerequisiteService(observed, forged, Queries, s_resolver);
        foreach (var pair in new[] { (currentOnly, fixture.Owner.Capture()), (forgedService, forged.Capture()) })
        {
            AssertUnavailable(pair.Item1.Load(pair.Item2, new(fixture.Id)));
            AssertUnavailable(pair.Item1.Preview(pair.Item2, PreviewRequest(request)));
            AssertUnavailable(pair.Item1.Confirm(pair.Item2, request));
        }
        Assert.AreEqual(reads, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
        Assert.AreEqual(0, forged.ActiveLeases);
        fixture.AssertUnchanged(before);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void A_local_atomic_capability_cannot_stand_in_for_missing_owner_atomic_commit(bool localCapability)
    {
        using var fixture = new Fixture(AccountA);
        var request = Prepare(Service(fixture, new ScopedStore(fixture)), fixture.Owner.Capture(), fixture.Id);
        ReadStore store = localCapability ? new LocalAtomicStore(fixture) : new ReadStore(fixture);
        var before = fixture.Capture();
        var service = Service(fixture, store);
        var preview = service.Preview(fixture.Owner.Capture(), PreviewRequest(request));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, preview.Outcome, Describe(preview));
        CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationPrerequisiteBlockers.PersistenceAuthorityRequired);
        // Existing Confirm recomputes the preview, including its capability blockers.
        // That preview differs from the previously valid one; do not rewrite its outcome.
        var result = service.Confirm(fixture.Owner.Capture(), request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, result.Outcome, Describe(result));
        Assert.IsNull(result.Value);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationPrerequisiteBlockers.PreviewDigestMismatch);
        Assert.AreEqual(0, store.LocalReads);
        fixture.AssertUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void A_missing_linked_workspace_cannot_read_the_matching_valid_local_workspace()
    {
        using var fixture = new Fixture(AccountA);
        Assert.IsTrue(fixture.Store.Delete(AccountA, fixture.Id, 1).Success);
        var observed = new ScopedStore(fixture);
        var before = fixture.Capture();
        var result = Service(fixture, observed).Load(fixture.Owner.Capture(), new(fixture.Id));
        Assert.IsNull(result.Value);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome);
        Assert.AreEqual(1, observed.Reads);
        Assert.AreEqual(0, observed.LocalReads);
        fixture.AssertUnchanged(before);
    }

    [TestMethod]
    public void Saved_revision_drift_rejects_both_preview_and_confirm_without_auxiliary_write()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedStore(fixture);
        var service = Service(fixture, observed);
        var stamp = fixture.Owner.Capture();
        var request = Prepare(service, stamp, fixture.Id);
        Assert.IsTrue(fixture.Store.SaveCheckpoint(AccountA, fixture.Id, 1).Success);
        var before = fixture.Capture();
        var preview = service.Preview(stamp, PreviewRequest(request));
        var confirm = service.Confirm(stamp, request);
        foreach (var blockers in new[] { preview.Blockers, confirm.Blockers })
            CollectionAssert.Contains(blockers.ToArray(), CharacterCreationPrerequisiteBlockers.StaleWorkspaceRevision);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, preview.Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, confirm.Outcome);
        Assert.IsNull(confirm.Value);
        Assert.AreEqual(0, observed.Commits);
        fixture.AssertUnchanged(before);
    }

    [TestMethod]
    public void Real_source_byte_drift_invalidates_a_valid_original_preview_without_writing()
    {
        using var fixture = new Fixture(AccountA);
        string source = Path.Combine(fixture.Root, "private-source");
        CopySources(s_root, source);
        var observed = new ScopedStore(fixture);
        var service = Service(fixture, observed, Resolver(source));
        var stamp = fixture.Owner.Capture();
        var request = Prepare(service, stamp, fixture.Id);
        var before = fixture.Capture();
        string path = Path.Combine(source, "data", "priorities.xml");
        File.AppendAllText(path, "\n<!-- private test source drift -->\n");
        var result = service.Confirm(stamp, request);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, Describe(result));
        Assert.IsNull(result.Value);
        Assert.IsNotEmpty(result.Blockers);
        Assert.AreEqual(0, observed.Commits);
        fixture.AssertUnchanged(before);
        Assert.AreEqual(0, fixture.Owner.ActiveLeases);
    }

    [TestMethod]
    public void Shared_view_rejects_foreign_workspace_owner_and_expired_lease()
    {
        using var fixture = new Fixture(AccountA);
        var observed = new ScopedStore(fixture);
        var stamp = fixture.Owner.Capture();
        Assert.IsTrue(fixture.Owner.TryAcquire(stamp, out var lease));
        Assert.IsNotNull(lease);
        var view = new OwnerBoundCreationWorkspaceStore(observed, lease, stamp, fixture.Id);
        using (lease)
        {
            Assert.IsFalse(view.Get(new CharacterWorkspaceId("foreign-id")).Success);
            Assert.IsFalse(view.Get(AccountB, fixture.Id).Success);
            Assert.IsFalse(view.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(AccountB,
                fixture.Id, 1, s_bootstrap.Document.AuxiliaryStateDigest, s_bootstrap.Document).Success);
            Assert.IsFalse(view.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(new("foreign-id"),
                1, s_bootstrap.Document.AuxiliaryStateDigest, s_bootstrap.Document).Success);
        }
        Assert.IsFalse(view.Get(fixture.Id).Success);
        Assert.IsFalse(view.SupportsWorkspaceAuxiliaryStateAtomicCommit);
        Assert.AreEqual(0, observed.Reads);
        Assert.AreEqual(0, observed.Commits);
    }

    private static CharacterCreationPrerequisiteConfirmRequest Prepare(
        IOwnerBoundCharacterCreationPrerequisiteService service, OwnerContextStamp stamp, CharacterWorkspaceId id)
    {
        var loaded = service.Load(stamp, new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, Describe(loaded));
        Assert.IsNotNull(loaded.Value);
        var state = loaded.Value;
        var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
        { ["heritage"] = "E", ["talent"] = "B", ["attributes"] = "A", ["skills"] = "C", ["resources"] = "D" };
        var heritage = state.Authority.Options.Single(x => x.CategoryId == "heritage" && x.Rank == "E")
            .HeritageOptions.First(x => x.MetatypeName == "Human" && x.MetavariantSourceId is null && x.IsEnabled);
        var talent = state.Authority.Options.Single(x => x.CategoryId == "talent" && x.Rank == "B")
            .TalentOptions.First(x => x.Value == "Magician" && x.IsEnabled);
        var previewRequest = new CharacterCreationPrerequisitePreviewRequest(state.Binding, ranks)
        {
            HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId,
            TalentActiveSkillSelectionIds = talent.ActiveSkillGrant?.Options.Where(x => x.IsEnabled)
                .Take(talent.ActiveSkillGrant.Quantity).Select(x => x.SelectionId).ToArray() ?? [],
            TalentSkillGroupSelectionIds = talent.SkillGroupGrant?.Options
                .Take(talent.SkillGroupGrant.Quantity).Select(x => x.SelectionId).ToArray() ?? []
        };
        var preview = service.Preview(stamp, previewRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, Describe(preview));
        Assert.IsNotNull(preview.Value);
        Assert.IsTrue(preview.Value.CanConfirm);
        Assert.IsEmpty(preview.Blockers);
        Assert.IsEmpty(preview.Value.Blockers);
        return new(preview.Value.Binding, ranks, preview.Value.PreviewDigest, true)
        {
            HeritageSelectionId = previewRequest.HeritageSelectionId,
            TalentSelectionId = previewRequest.TalentSelectionId,
            TalentActiveSkillSelectionIds = previewRequest.TalentActiveSkillSelectionIds,
            TalentSkillGroupSelectionIds = previewRequest.TalentSkillGroupSelectionIds
        };
    }

    private static CharacterCreationPrerequisitePreviewRequest PreviewRequest(CharacterCreationPrerequisiteConfirmRequest request)
        => new(request.Binding, request.PriorityAssignments)
        {
            HeritageSelectionId = request.HeritageSelectionId, TalentSelectionId = request.TalentSelectionId,
            TalentActiveSkillSelectionIds = request.TalentActiveSkillSelectionIds,
            TalentSkillGroupSelectionIds = request.TalentSkillGroupSelectionIds
        };

    private static OwnerBoundCharacterCreationPrerequisiteService Service(Fixture fixture, IWorkspaceStore store,
        ICharacterSourceDataResolver? resolver = null) => new(store, fixture.Owner, Queries,
            new ObservedResolver(fixture.Owner, resolver ?? s_resolver));
    private static ICharacterSourceDataResolver Resolver(string root)
        => new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
    private static void CopySources(string from, string to)
    {
        Directory.CreateDirectory(Path.Combine(to, "data"));
        foreach (string name in SourceFiles) File.Copy(Path.Combine(from, "data", name), Path.Combine(to, "data", name));
    }
    private static string Describe<T>(T value) => JsonSerializer.Serialize(value);
    private static void AssertUnavailable<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome, Describe(result));
        Assert.IsNull(result.Value);
        CollectionAssert.AreEqual(new[] { CharacterCreationPrerequisiteBlockers.WorkspaceUnavailable }, result.Blockers.ToArray());
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-prerequisite-owner-").FullName;
        public FileWorkspaceStore Store { get; }
        public TestOwner Owner { get; }
        public CharacterWorkspaceId Id => s_bootstrap.Id;
        public Fixture(OwnerScope owner)
        {
            Owner = new(owner);
            Store = new(Root);
            // TEST construction, not authenticated restore: create the same genuine
            // bootstrap document through each real typed atomic store capability.
            foreach (var partition in new[] { OwnerScope.LocalSingleUser, AccountA, AccountB })
            {
                var result = partition.IsLocalSingleUser
                    ? Store.CreateCharacterCreationBootstrapWorkspaceDocument(Id, s_bootstrap.Document)
                    : Store.CreateCharacterCreationBootstrapWorkspaceDocument(partition, Id, s_bootstrap.Document);
                Assert.IsTrue(result.Success, result.Error);
                var read = Read(partition);
                Assert.AreEqual(Describe(s_bootstrap.Document), Describe(read.Document));
                Assert.AreEqual(s_bootstrap.ContentRevision, read.ContentRevision);
                Assert.AreEqual(s_bootstrap.SavedRevision, read.SavedRevision);
            }
        }
        public WorkspaceStoredDocument Read(OwnerScope owner)
        {
            var cold = new FileWorkspaceStore(Root);
            var read = owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(owner, Id);
            Assert.IsTrue(read.Success, read.Error);
            return read.Value!;
        }
        public Dictionary<OwnerScope, string> Capture() => new[] { OwnerScope.LocalSingleUser, AccountA, AccountB }
            .ToDictionary(owner => owner, owner =>
            {
                var cold = new FileWorkspaceStore(Root);
                return Describe(owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(owner, Id));
            });
        public void AssertUnchanged(Dictionary<OwnerScope, string> before, OwnerScope? except = null)
        {
            var after = Capture();
            foreach (var item in before)
                if (item.Key != except) Assert.AreEqual(item.Value, after[item.Key], item.Key.Value);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    // Observers delegate every read and commit to the actual FileWorkspaceStore.
    // Only the owner authority is a controlled host fixture, not deployed credentials.
    private sealed class ObservedResolver(TestOwner owner, ICharacterSourceDataResolver inner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            owner.AssertLeaseHeld();
            return inner.TryCreateContext(xml);
        }
    }
    private class ReadStore(Fixture fixture) : IWorkspaceStore
    {
        protected Fixture Fixture { get; } = fixture;
        public int Reads { get; private set; }
        public int LocalReads { get; private set; }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            LocalReads++;
            return Read(OwnerScope.LocalSingleUser, id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => Read(owner, id);
        private WorkspaceStoreReadResult Read(OwnerScope owner, CharacterWorkspaceId id)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner);
            Assert.AreEqual(Fixture.Id, id);
            Reads++;
            return owner.IsLocalSingleUser ? Fixture.Store.Get(id) : Fixture.Store.Get(owner, id);
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
        private static WorkspaceStoreMutationResult Unexpected() => throw new AssertFailedException("Unexpected mutation lane.");
    }
    private class LocalAtomicStore(Fixture fixture) : ReadStore(fixture), IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public int Commits { get; private set; }
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(OwnerScope.LocalSingleUser, id, revision, digest, document);
        protected WorkspaceStoreMutationResult Commit(OwnerScope owner, CharacterWorkspaceId id,
            long revision, string digest, WorkspaceDocument document)
        {
            Fixture.Owner.AssertLeaseHeld();
            Assert.AreEqual(Fixture.Owner.Current, owner);
            Assert.AreEqual(Fixture.Id, id);
            var result = owner.IsLocalSingleUser
                ? Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, revision, digest, document)
                : Fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(owner, id, revision, digest, document);
            if (result.Success) Commits++;
            return result;
        }
    }
    private sealed class ScopedStore(Fixture fixture) : LocalAtomicStore(fixture), IOwnerScopedWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public bool SupportsOwnerScopedWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            OwnerScope owner, CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
            => Commit(owner, id, revision, digest, document);
    }
    private sealed class CurrentOnlyOwner(OwnerScope owner) : IOwnerContextAccessor { public OwnerScope Current => owner; }
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
            Assert.IsTrue(Monitor.IsEntered(_gate));
            Assert.AreEqual(1, ActiveLeases);
        }
        public void Transition(OwnerScope owner)
        {
            lock (_gate) { Assert.AreEqual(0, ActiveLeases); _owner = owner; _revision++; }
        }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases);
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
