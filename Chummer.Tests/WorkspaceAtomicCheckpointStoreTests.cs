using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceAtomicCheckpointStoreTests
{
    [TestMethod]
    public void File_store_atomically_replaces_and_checkpoints_the_new_revision()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            WorkspaceStoreEntry created = Create(store, Document("created"));

            WorkspaceStoreMutationResult committed = store.ReplaceWorkspaceDocumentAndCheckpoint(
                created.Id,
                expectedContentRevision: 1,
                Document("checkpointed replacement"));

            AssertMutation(committed, 2, 2);
            AssertRead(store.Get(created.Id), 2, 2, "checkpointed replacement");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_stale_atomic_replace_and_checkpoint_performs_no_write()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            WorkspaceStoreEntry created = Create(store, Document("created"));
            AssertMutation(
                store.ReplaceWorkspaceDocument(created.Id, 1, Document("winner")),
                2,
                0);
            string targetPath = GetTargetPath(stateDirectory, created.Id);
            byte[] before = File.ReadAllBytes(targetPath);
            DateTime beforeLastWriteUtc = File.GetLastWriteTimeUtc(targetPath);
            RecordingFaultInjector recorder = new();
            FileWorkspaceStore staleStore = new(stateDirectory, recorder);

            WorkspaceStoreMutationResult stale = staleStore.ReplaceWorkspaceDocumentAndCheckpoint(
                created.Id,
                expectedContentRevision: 1,
                Document("stale"));

            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, stale.Outcome);
            Assert.AreEqual(0, recorder.StageCallCount);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            Assert.AreEqual(beforeLastWriteUtc, File.GetLastWriteTimeUtc(targetPath));
            AssertRead(store.Get(created.Id), 2, 0, "winner");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_atomic_replace_and_checkpoint_write_fault_rolls_back()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore normalStore = new(stateDirectory);
            WorkspaceStoreEntry created = Create(normalStore, Document("original"));
            string targetPath = GetTargetPath(stateDirectory, created.Id);
            byte[] before = File.ReadAllBytes(targetPath);
            FileWorkspaceStore failingStore = new(
                stateDirectory,
                new ThrowingIOExceptionFaultInjector(
                    FileWorkspaceStoreFaultStage.AfterTempFileFlushed));

            WorkspaceStoreMutationResult failed = failingStore.ReplaceWorkspaceDocumentAndCheckpoint(
                created.Id,
                expectedContentRevision: 1,
                Document("rejected replacement"));

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, failed.Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            AssertRead(new FileWorkspaceStore(stateDirectory).Get(created.Id), 1, 0, "original");
            Assert.IsFalse(Directory.EnumerateFiles(
                Path.GetDirectoryName(targetPath)!,
                $"{Path.GetFileName(targetPath)}.tmp.*").Any());
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_reopen_preserves_atomic_replacement_document_and_equal_revisions()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore firstStore = new(stateDirectory);
            WorkspaceStoreEntry created = Create(firstStore, Document("created"));
            WorkspaceDocument replacement = Document("reopen equality");

            AssertMutation(
                firstStore.ReplaceWorkspaceDocumentAndCheckpoint(
                    created.Id,
                    expectedContentRevision: 1,
                    replacement),
                2,
                2);

            WorkspaceStoreReadResult reopened = new FileWorkspaceStore(stateDirectory).Get(created.Id);
            Assert.IsTrue(reopened.Success, reopened.Error);
            Assert.AreEqual(replacement, reopened.Value?.Document);
            Assert.AreEqual(2L, reopened.Value?.ContentRevision);
            Assert.AreEqual(reopened.Value?.ContentRevision, reopened.Value?.SavedRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Workspace_store_default_atomic_replace_and_checkpoint_fails_closed()
    {
        IWorkspaceStore store = new DefaultAtomicCheckpointWorkspaceStore();

        WorkspaceStoreMutationResult result = store.ReplaceWorkspaceDocumentAndCheckpoint(
            new CharacterWorkspaceId("unsupported"),
            expectedContentRevision: 1,
            Document("must not commit"));

        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, result.Outcome);
        Assert.IsFalse(result.Success);
    }

    private static WorkspaceStoreEntry Create(IWorkspaceStore store, WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = store.CreateWorkspaceDocument(document);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Entry);
        return result.Entry.Value;
    }

    private static void AssertMutation(
        WorkspaceStoreMutationResult result,
        long contentRevision,
        long savedRevision)
    {
        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(contentRevision, result.Entry?.ContentRevision);
        Assert.AreEqual(savedRevision, result.Entry?.SavedRevision);
    }

    private static void AssertRead(
        WorkspaceStoreReadResult result,
        long contentRevision,
        long savedRevision,
        string expectedContent)
    {
        Assert.IsTrue(result.Success, result.Error);
        Assert.AreEqual(contentRevision, result.Value?.ContentRevision);
        Assert.AreEqual(savedRevision, result.Value?.SavedRevision);
        StringAssert.Contains(result.Value?.Document.Content ?? string.Empty, expectedContent);
    }

    private static WorkspaceDocument Document(string name)
    {
        return new WorkspaceDocument(
            $"<character><name>{name}</name></character>",
            RulesetId: "sr5");
    }

    private static string CreateTempStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetTargetPath(string stateDirectory, CharacterWorkspaceId id)
    {
        return Path.Combine(stateDirectory, "workspaces", $"{id.Value}.json");
    }

    private sealed class RecordingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        public int StageCallCount { get; private set; }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            StageCallCount++;
        }
    }

    private sealed class ThrowingIOExceptionFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _stage;

        public ThrowingIOExceptionFaultInjector(FileWorkspaceStoreFaultStage stage)
        {
            _stage = stage;
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == _stage)
            {
                throw new IOException("Injected workspace write failure.");
            }
        }
    }

    private sealed class DefaultAtomicCheckpointWorkspaceStore : IWorkspaceStore
    {
        private static WorkspaceStoreMutationResult UnsupportedMutation()
            => new(WorkspaceOperationOutcome.Unavailable);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document)
            => UnsupportedMutation();

        public IReadOnlyList<WorkspaceStoreEntry> List() => [];

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => [];

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => new(WorkspaceOperationOutcome.Unavailable);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => new(WorkspaceOperationOutcome.Unavailable);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnsupportedMutation();

        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => UnsupportedMutation();
    }
}
