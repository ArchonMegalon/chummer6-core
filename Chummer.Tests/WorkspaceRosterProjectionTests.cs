using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceRosterProjectionTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Service_uses_one_projection_without_List_Get_replay_and_preserves_rows(bool linked)
    {
        using Fixture fixture = new();
        OwnerScope owner = linked ? new("roster-owner") : OwnerScope.LocalSingleUser;
        var older = linked ? fixture.Store.CreateWorkspaceDocument(owner, Document("Older"))
            : fixture.Store.CreateWorkspaceDocument(Document("Older"));
        var newer = linked ? fixture.Store.CreateWorkspaceDocument(owner, Document("Newer"))
            : fixture.Store.CreateWorkspaceDocument(Document("Newer"));
        Assert.IsTrue(older.Success);
        Assert.IsTrue(newer.Success);
        Assert.IsTrue((linked ? fixture.Store.SaveCheckpoint(owner, newer.Entry!.Value.Id, 1)
            : fixture.Store.SaveCheckpoint(newer.Entry!.Value.Id, 1)).Success);
        foreach (string path in Directory.EnumerateFiles(fixture.Root, "*.json", SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(path, Path.GetFileNameWithoutExtension(path) == older.Entry!.Value.Id.Value
                ? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                : new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var originalBytes = Directory.EnumerateFiles(fixture.Root, "*.json", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllText);
        WorkspaceService legacy = Service(new LegacyStore(fixture.Store));
        ProjectionOnlyStore projected = new(fixture.Store);
        WorkspaceService service = Service(projected);

        foreach (int? limit in new int?[] { null, 1, 0, -5 })
        {
            var expected = legacy.List(owner, limit);
            var actual = service.List(owner, limit);
            CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
            Assert.AreEqual(newer.Entry.Value.Id, actual[0].Id);
            Assert.IsTrue(actual[0].HasSavedWorkspace);
            Assert.AreEqual(1L, actual[0].SavedRevision);
        }
        Assert.AreEqual(4, projected.ProjectionCalls);
        Assert.AreEqual(linked ? owner : (OwnerScope?)null, projected.LastOwner);
        foreach (var (path, bytes) in originalBytes)
            Assert.AreEqual(bytes, File.ReadAllText(path), "Roster projection changed a saved runner.");
    }

    [TestMethod]
    public void Each_call_and_new_store_observe_replace_checkpoint_and_delete_without_cached_state()
    {
        using Fixture fixture = new();
        var created = fixture.Store.CreateWorkspaceDocument(Document("Before"));
        Assert.IsTrue(created.Success);
        var id = created.Entry!.Value.Id;
        WorkspaceService service = Service(fixture.Store);
        Assert.AreEqual("Before", service.List().Single().Summary.Name);
        Assert.IsTrue(fixture.Store.ReplaceWorkspaceDocument(id, 1, Document("After")).Success);
        Assert.IsTrue(fixture.Store.SaveCheckpoint(id, 2).Success);
        var current = service.List().Single();
        Assert.AreEqual("After", current.Summary.Name);
        Assert.AreEqual(2L, current.ContentRevision);
        Assert.AreEqual(2L, current.SavedRevision);
        Assert.AreEqual(current, Service(new FileWorkspaceStore(fixture.Root)).List().Single());
        Assert.IsTrue(fixture.Store.Delete(id, 2).Success);
        Assert.HasCount(0, service.List());
    }

    [TestMethod]
    public void Corrupt_and_invalid_revision_records_are_not_projected()
    {
        using Fixture fixture = new();
        var valid = fixture.Store.CreateWorkspaceDocument(Document("Valid"));
        Assert.IsTrue(valid.Success);
        string directory = Path.Combine(fixture.Root, "workspaces");
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{invalid json");
        JsonNode damaged = JsonNode.Parse(File.ReadAllText(fixture.PathFor(valid.Entry!.Value.Id)))!;
        damaged["SavedRevision"] = 900;
        File.WriteAllText(Path.Combine(directory, "bad-revision.json"), damaged.ToJsonString());
        int projections = 0;
        var rows = fixture.Store.ListProjected(stored => { projections++; return stored.Id; });
        Assert.AreEqual(1, projections);
        CollectionAssert.AreEqual(new[] { valid.Entry.Value.Id }, rows.ToArray());
        Assert.HasCount(1, Service(fixture.Store).List());
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, fixture.Store.Get(new("bad-revision")).Outcome);
    }

    [TestMethod]
    public void Scoped_projection_isolated_and_invalid_owners_never_fall_back_to_local()
    {
        using Fixture fixture = new();
        OwnerScope a = new("roster-a"), b = new("roster-b");
        var local = fixture.Store.CreateWorkspaceDocument(Document("Local"));
        var first = fixture.Store.CreateWorkspaceDocument(a, Document("A"));
        var second = fixture.Store.CreateWorkspaceDocument(b, Document("B"));
        Assert.IsTrue(local.Success && first.Success && second.Success);
        Assert.AreEqual(local.Entry!.Value.Id, fixture.Store.ListProjected(value => value.Id).Single());
        Assert.AreEqual(first.Entry!.Value.Id, fixture.Store.ListProjected(a, value => value.Id).Single());
        Assert.AreEqual(second.Entry!.Value.Id, fixture.Store.ListProjected(b, value => value.Id).Single());
        foreach (OwnerScope invalid in new[] { default(OwnerScope), new OwnerScope(""), new OwnerScope("local-single-user") })
        {
            Assert.HasCount(0, fixture.Store.ListProjected(invalid, value => value.Id));
            Assert.HasCount(0, Service(fixture.Store).List(invalid));
        }
    }

    [TestMethod]
    public void Generated_normal_read_does_not_weaken_the_separate_strict_continuation_reader()
    {
        using Fixture fixture = new();
        var created = fixture.Store.CreateWorkspaceDocument(Document("Strict export"));
        Assert.IsTrue(created.Success);
        var id = created.Entry!.Value.Id;
        Assert.IsTrue(fixture.Store.ReadContinuation(id).Success);
        string path = fixture.PathFor(id);
        JsonNode record = JsonNode.Parse(File.ReadAllText(path))!;
        record["AuxiliaryState"] = new JsonObject { ["FutureUnknownState"] = "not exportable" };
        string bytes = record.ToJsonString();
        File.WriteAllText(path, bytes);

        // Ordinary local reads preserve the prior additive-field semantics.
        Assert.IsTrue(fixture.Store.Get(id).Success);
        Assert.AreEqual("Strict export", Service(fixture.Store).List().Single().Summary.Name);
        // Export must still reject unknown auxiliary fields rather than discard
        // them through the ordinary reader's generated converter.
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, fixture.Store.ReadContinuation(id).Outcome);
        Assert.AreEqual(bytes, File.ReadAllText(path));
    }

    [TestMethod]
    public void Older_projection_failure_only_throws_when_selected_by_limit()
    {
        using Fixture fixture = new();
        var old = fixture.Store.CreateWorkspaceDocument(Document("Old"));
        var current = fixture.Store.CreateWorkspaceDocument(Document("Current"));
        Assert.IsTrue(old.Success && current.Success);
        File.SetLastWriteTimeUtc(fixture.PathFor(old.Entry!.Value.Id), DateTime.UtcNow.AddDays(-5));
        string Project(WorkspaceStoredDocument value) => value.Id == old.Entry.Value.Id
            ? throw new FormatException("Old projection unavailable") : "Current";
        Assert.AreEqual("Current", fixture.Store.ListProjected(Project, 1).Single());
        Assert.ThrowsExactly<FormatException>(() => fixture.Store.ListProjected(Project));
        Assert.ThrowsExactly<FormatException>(() => fixture.Store.ListProjected(Project, 0));
        Assert.ThrowsExactly<FormatException>(() => fixture.Store.ListProjected(Project, -1));
    }

    [TestMethod]
    public void Legacy_migration_keeps_revision_timestamp_and_reopens_after_projection()
    {
        using Fixture fixture = new();
        CharacterWorkspaceId id = new("legacy-roster");
        string path = fixture.PathFor(id);
        File.WriteAllText(path, """
            {"Content":"<character><name>Legacy</name></character>","Format":"NativeXml","RulesetId":"sr5"}
            """);
        DateTime timestamp = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        var row = Service(fixture.Store).List().Single();
        Assert.AreEqual("Legacy", row.Summary.Name);
        Assert.AreEqual(1L, row.ContentRevision);
        Assert.AreEqual(1L, row.SavedRevision);
        Assert.AreEqual(new DateTimeOffset(timestamp), row.LastUpdatedUtc);
        Assert.AreEqual(row, Service(new FileWorkspaceStore(fixture.Root)).List().Single());
    }

    [TestMethod]
    public void Projection_runs_after_file_lease_release_and_summary_failure_keeps_fallback()
    {
        using Fixture fixture = new();
        var created = fixture.Store.CreateWorkspaceDocument(new WorkspaceDocument("not xml", RulesetId: RulesetDefaults.Sr5));
        Assert.IsTrue(created.Success);
        FileWorkspaceStore other = new(fixture.Root, FileWorkspaceStoreFaultInjector.None, TimeSpan.FromMilliseconds(100));
        var outcome = fixture.Store.ListProjected(value => other.Get(value.Id).Outcome).Single();
        Assert.AreEqual(WorkspaceOperationOutcome.Success, outcome);
        var row = Service(fixture.Store).List().Single();
        Assert.AreEqual($"Workspace {created.Entry!.Value.Id.Value}", row.Summary.Name);
    }

    private static WorkspaceDocument Document(string name) => new(
        $"<character><name>{name}</name><metatype>Human</metatype><buildmethod>Priority</buildmethod></character>",
        RulesetId: RulesetDefaults.Sr5);

    private static WorkspaceService Service(IWorkspaceStore store)
    {
        CharacterFileService files = new();
        Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
            new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
        return new(store, new RulesetWorkspaceCodecResolver([codec]), new WorkspaceImportRulesetDetector());
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "chummer-roster-tests", Guid.NewGuid().ToString("N"));
        public FileWorkspaceStore Store { get; }
        public Fixture() => Store = new(Root);
        public string PathFor(CharacterWorkspaceId id) => Path.Combine(Root, "workspaces", id.Value + ".json");
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private class LegacyStore(FileWorkspaceStore store) : IWorkspaceStore
    {
        protected FileWorkspaceStore inner { get; } = store;
        public virtual IReadOnlyList<WorkspaceStoreEntry> List() => inner.List();
        public virtual IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => inner.List(owner);
        public virtual WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => inner.Get(id);
        public virtual WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => inner.Get(owner, id);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => inner.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => inner.CreateWorkspaceDocument(owner, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(owner, id, revision, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(id, revision);
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(owner, id, revision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => inner.Delete(id, revision);
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.Delete(owner, id, revision);
    }

    private sealed class ProjectionOnlyStore(FileWorkspaceStore store) : LegacyStore(store), IWorkspaceStoreProjection
    {
        public int ProjectionCalls { get; private set; }
        public OwnerScope? LastOwner { get; private set; }
        public override IReadOnlyList<WorkspaceStoreEntry> List() => throw new AssertFailedException("Duplicate roster enumeration");
        public override IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw new AssertFailedException("Duplicate scoped enumeration");
        public override WorkspaceStoreReadResult Get(CharacterWorkspaceId id) => throw new AssertFailedException("Duplicate document read");
        public override WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => throw new AssertFailedException("Duplicate scoped read");
        public IReadOnlyList<T> ListProjected<T>(Func<WorkspaceStoredDocument, T> project, int? maxCount = null)
        {
            ProjectionCalls++;
            LastOwner = null;
            return inner.ListProjected(project, maxCount);
        }
        public IReadOnlyList<T> ListProjected<T>(OwnerScope owner, Func<WorkspaceStoredDocument, T> project, int? maxCount = null)
        {
            ProjectionCalls++;
            LastOwner = owner;
            return inner.ListProjected(owner, project, maxCount);
        }
    }
}
