using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceAuxiliaryStateStoreTests
{
    [TestMethod]
    public void File_store_migrates_old_record_to_empty_typed_auxiliary_state()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("old-record");
            string targetPath = GetTargetPath(stateDirectory, id);
            Dictionary<string, object?> oldRecord = new(StringComparer.Ordinal)
            {
                ["Format"] = WorkspaceDocumentFormat.NativeXml.ToString(),
                ["Envelope"] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["RulesetId"] = "sr5",
                    ["SchemaVersion"] = 1,
                    ["PayloadKind"] = "workspace",
                    ["Payload"] = CharacterXml("Legacy")
                },
                ["ContentRevision"] = 4,
                ["SavedRevision"] = 3
            };
            File.WriteAllText(targetPath, JsonSerializer.Serialize(oldRecord));

            WorkspaceStoreReadResult migrated = store.Get(id);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.AreEqual(4L, migrated.Value?.ContentRevision);
            Assert.AreEqual(3L, migrated.Value?.SavedRevision);
            Assert.IsTrue(migrated.Value?.Document.AuxiliaryState.IsEmpty);
            string migratedJson = File.ReadAllText(targetPath);
            StringAssert.Contains(migratedJson, "\"RecordSchemaVersion\":2");
            Assert.IsFalse(migratedJson.Contains("AuxiliaryState", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Creation_authority_commit_round_trips_on_reopen_without_leaking_into_xml_download()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore firstStore = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-round-trip");
            WorkspaceDocument original = Document("Original");
            Create(firstStore, id, original);
            CharacterCreationFoundationDraftLedger draft = Draft(id);
            WorkspaceDocument withDraft = WithDraft(original, draft);

            WorkspaceStoreMutationResult committed =
                firstStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 1,
                    original.AuxiliaryStateDigest,
                    withDraft);

            AssertMutation(committed, contentRevision: 2, savedRevision: 2);
            WorkspaceStoreReadResult reopened = new FileWorkspaceStore(stateDirectory).Get(id);
            Assert.IsTrue(reopened.Success, reopened.Error);
            Assert.AreEqual(
                draft.DraftDigest,
                reopened.Value?.Document.AuxiliaryState.CharacterCreationFoundationDraft?.DraftDigest);
            Assert.AreEqual(withDraft.AuxiliaryStateDigest, reopened.Value?.Document.AuxiliaryStateDigest);
            Assert.AreEqual(original.Content, reopened.Value?.Document.Content);

            WorkspaceDownloadReceipt download = CreateSr5Codec().BuildDownload(
                id,
                reopened.Value!.Document.PayloadEnvelope,
                WorkspaceDocumentFormat.NativeXml);
            string downloadedXml = Encoding.UTF8.GetString(Convert.FromBase64String(download.ContentBase64));
            Assert.AreEqual(original.Content, downloadedXml);
            Assert.IsFalse(downloadedXml.Contains("draft", StringComparison.OrdinalIgnoreCase));
            Assert.IsFalse(downloadedXml.Contains(draft.Selection.ModuleId, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Generic_or_stale_authority_replacement_cannot_change_auxiliary_state_and_writes_nothing()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-cas");
            WorkspaceDocument original = Document("Original");
            Create(store, id, original);
            WorkspaceDocument withDraft = WithDraft(original, Draft(id));
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 1,
                    original.AuxiliaryStateDigest,
                    withDraft),
                contentRevision: 2,
                savedRevision: 2);
            string targetPath = GetTargetPath(stateDirectory, id);
            byte[] before = File.ReadAllBytes(targetPath);
            DateTime beforeLastWriteUtc = File.GetLastWriteTimeUtc(targetPath);
            RecordingFaultInjector recorder = new();
            FileWorkspaceStore guardedStore = new(stateDirectory, recorder);

            WorkspaceStoreMutationResult generic = guardedStore.ReplaceWorkspaceDocument(
                id,
                expectedContentRevision: 2,
                Document("Forged removal"));
            WorkspaceStoreMutationResult genericCheckpoint =
                guardedStore.ReplaceWorkspaceDocumentAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    Document("Forged checkpoint removal"));
            WorkspaceStoreMutationResult staleAuxiliary =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    original.AuxiliaryStateDigest,
                    WithDraft(Document("Forged update"), Draft(id) with { DraftRevision = 2 }));
            WorkspaceStoreMutationResult forgedBinding =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    withDraft.AuxiliaryStateDigest,
                    WithDraft(
                        Document("Forged binding"),
                        Draft(new CharacterWorkspaceId("different-workspace"))));

            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, generic.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, genericCheckpoint.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, staleAuxiliary.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, forgedBinding.Outcome);
            Assert.AreEqual(0, recorder.StageCallCount);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            Assert.AreEqual(beforeLastWriteUtc, File.GetLastWriteTimeUtc(targetPath));
            WorkspaceStoreReadResult unchanged = guardedStore.Get(id);
            Assert.AreEqual(2L, unchanged.Value?.ContentRevision);
            Assert.AreEqual(withDraft.AuxiliaryStateDigest, unchanged.Value?.Document.AuxiliaryStateDigest);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Metadata_update_and_checkpoint_preserve_auxiliary_state()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-metadata");
            WorkspaceDocument original = Document("Original");
            Create(store, id, original);
            CharacterCreationFoundationDraftLedger draft = Draft(id);
            WorkspaceDocument withDraft = WithDraft(original, draft);
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 1,
                    original.AuxiliaryStateDigest,
                    withDraft),
                contentRevision: 2,
                savedRevision: 2);
            WorkspaceService service = CreateWorkspaceService(store);

            CommandResult<WorkspaceMetadataResult> updated = service.UpdateMetadata(
                id,
                expectedContentRevision: 2,
                new UpdateWorkspaceMetadata("Renamed", null, null));

            Assert.IsTrue(updated.Success, updated.Error);
            Assert.AreEqual(3L, updated.Value?.ContentRevision);
            Assert.AreEqual(2L, updated.Value?.SavedRevision);
            AssertMutation(store.SaveCheckpoint(id, expectedContentRevision: 3), 3, 3);
            WorkspaceStoreReadResult reopened = new FileWorkspaceStore(stateDirectory).Get(id);
            Assert.AreEqual(
                draft.DraftDigest,
                reopened.Value?.Document.AuxiliaryState.CharacterCreationFoundationDraft?.DraftDigest);
            Assert.AreEqual(withDraft.AuxiliaryStateDigest, reopened.Value?.Document.AuxiliaryStateDigest);
            StringAssert.Contains(reopened.Value?.Document.Content ?? string.Empty, "Renamed");
            Assert.AreEqual(3L, reopened.Value?.ContentRevision);
            Assert.AreEqual(3L, reopened.Value?.SavedRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Independent_creation_draft_lanes_advance_atomically_without_rewriting_the_sibling()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-lane-composition");
            WorkspaceDocument original = Document("Original");
            Create(store, id, original);
            CharacterCreationFoundationDraftLedger foundation = Draft(id);
            WorkspaceDocument foundationDocument = WithDraft(original, foundation);
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    1,
                    original.AuxiliaryStateDigest,
                    foundationDocument),
                2,
                2);

            CharacterCreationPrerequisiteDraft prerequisite = PrerequisiteDraft(id, baseRevision: 2);
            WorkspaceDocument composed = foundationDocument with
            {
                State = foundationDocument.State with
                {
                    AuxiliaryState = foundationDocument.AuxiliaryState with
                    {
                        CharacterCreationPrerequisiteDraft = prerequisite
                    }
                }
            };
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    2,
                    foundationDocument.AuxiliaryStateDigest,
                    composed),
                3,
                3);

            CharacterCreationFoundationDraftLedger advancedFoundation = foundation with
            {
                DraftRevision = 2,
                BaseContentRevision = 3,
                DraftDigest = "sha256:" + new string('e', 64)
            };
            WorkspaceDocument advanced = composed with
            {
                State = composed.State with
                {
                    AuxiliaryState = composed.AuxiliaryState with
                    {
                        CharacterCreationFoundationDraft = advancedFoundation
                    }
                }
            };
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    3,
                    composed.AuxiliaryStateDigest,
                    advanced),
                4,
                4);

            WorkspaceDocument foundationCleared = advanced with
            {
                State = advanced.State with
                {
                    AuxiliaryState = advanced.AuxiliaryState with
                    {
                        CharacterCreationFoundationDraft = null
                    }
                }
            };
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    4,
                    advanced.AuxiliaryStateDigest,
                    foundationCleared),
                5,
                5);

            WorkspaceStoredDocument reopened = new FileWorkspaceStore(stateDirectory).Get(id).Value!;
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationFoundationDraft);
            Assert.AreEqual(
                prerequisite.DraftDigest,
                reopened.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft?.DraftDigest);

            WorkspaceDocument prerequisiteCleared = foundationCleared with
            {
                State = foundationCleared.State with
                {
                    AuxiliaryState = foundationCleared.AuxiliaryState with
                    {
                        CharacterCreationPrerequisiteDraft = null
                    }
                }
            };
            AssertMutation(
                store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    5,
                    foundationCleared.AuxiliaryStateDigest,
                    prerequisiteCleared),
                6,
                6);
            Assert.IsTrue(
                new FileWorkspaceStore(stateDirectory).Get(id).Value!.Document.AuxiliaryState.IsEmpty);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Creation_authority_write_fault_rolls_back_document_auxiliary_state_and_checkpoint()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-fault");
            WorkspaceDocument original = Document("Original");
            Create(store, id, original);
            string targetPath = GetTargetPath(stateDirectory, id);
            byte[] before = File.ReadAllBytes(targetPath);
            FileWorkspaceStore failingStore = new(
                stateDirectory,
                new ThrowingIOExceptionFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));

            WorkspaceStoreMutationResult failed =
                failingStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 1,
                    original.AuxiliaryStateDigest,
                    WithDraft(Document("Rejected"), Draft(id)));

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, failed.Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));
            WorkspaceStoreReadResult reopened = new FileWorkspaceStore(stateDirectory).Get(id);
            Assert.AreEqual(1L, reopened.Value?.ContentRevision);
            Assert.AreEqual(0L, reopened.Value?.SavedRevision);
            Assert.AreEqual(original, reopened.Value?.Document);
            Assert.IsTrue(reopened.Value?.Document.AuxiliaryState.IsEmpty);
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
    public void Workspace_store_default_creation_authority_commit_fails_closed()
    {
        IWorkspaceStore store = new InMemoryWorkspaceStore();
        CharacterWorkspaceId id = new("unsupported-authority");
        WorkspaceDocument original = Document("Original");
        Create(store, id, original);

        WorkspaceStoreMutationResult result =
            store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id,
                expectedContentRevision: 1,
                original.AuxiliaryStateDigest,
                WithDraft(original, Draft(id)));

        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, result.Outcome);
        WorkspaceStoreReadResult unchanged = store.Get(id);
        Assert.AreEqual(1L, unchanged.Value?.ContentRevision);
        Assert.IsTrue(unchanged.Value?.Document.AuxiliaryState.IsEmpty);
    }

    [TestMethod]
    public void First_creation_authority_commit_requires_first_draft_revision_and_current_base_binding()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("draft-first-binding");
            WorkspaceDocument original = Document("Original");
            Create(store, id, original);
            WorkspaceDocument revised = Document("Revised before first draft");
            AssertMutation(
                store.ReplaceWorkspaceDocument(id, expectedContentRevision: 1, revised),
                contentRevision: 2,
                savedRevision: 0);
            CharacterCreationFoundationDraftLedger canonical = Draft(id) with
            {
                BaseContentRevision = 2
            };
            StringAssert.StartsWith(canonical.BaseRawCharacterXmlDigest, "sha256:");
            StringAssert.StartsWith(canonical.SourceDigest, "sha256:");
            StringAssert.StartsWith(canonical.DraftDigest, "sha256:");
            Assert.AreEqual(64, revised.AuxiliaryStateDigest.Length);
            Assert.IsFalse(revised.AuxiliaryStateDigest.StartsWith("sha256:", StringComparison.Ordinal));
            string targetPath = GetTargetPath(stateDirectory, id);
            byte[] before = File.ReadAllBytes(targetPath);
            RecordingFaultInjector recorder = new();
            FileWorkspaceStore guardedStore = new(stateDirectory, recorder);

            WorkspaceStoreMutationResult prefixedOuterDigest =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    "sha256:" + revised.AuxiliaryStateDigest,
                    WithDraft(revised, canonical));
            WorkspaceStoreMutationResult unqualifiedFoundationDigest =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    revised.AuxiliaryStateDigest,
                    WithDraft(
                        revised,
                        canonical with { BaseRawCharacterXmlDigest = new string('a', 64) }));
            WorkspaceStoreMutationResult skippedDraftRevision =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    revised.AuxiliaryStateDigest,
                    WithDraft(revised, canonical with { DraftRevision = 2 }));
            WorkspaceStoreMutationResult staleBaseRevision =
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    revised.AuxiliaryStateDigest,
                    WithDraft(revised, canonical with { BaseContentRevision = 1 }));

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, prefixedOuterDigest.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, unqualifiedFoundationDigest.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, skippedDraftRevision.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, staleBaseRevision.Outcome);
            Assert.AreEqual(0, recorder.StageCallCount);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(targetPath));

            AssertMutation(
                guardedStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                    id,
                    expectedContentRevision: 2,
                    revised.AuxiliaryStateDigest,
                    WithDraft(revised, canonical)),
                contentRevision: 3,
                savedRevision: 3);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static CharacterCreationFoundationDraftLedger Draft(CharacterWorkspaceId id)
    {
        return new CharacterCreationFoundationDraftLedger(
            Schema: CharacterCreationFoundationSchemas.DraftLedgerV1,
            WorkspaceId: id,
            DraftRevision: 1,
            BaseContentRevision: 1,
            BaseRawCharacterXmlDigest: "sha256:" + new string('a', 64),
            SourceDigest: "sha256:" + new string('b', 64),
            RequestedMetatype: "Human",
            Selection: new CharacterCreationFoundationSelection("nationality.module.1", null),
            RequirementEvaluations: [],
            ProjectedEffects: [],
            FollowUpValues: new Dictionary<string, string>(StringComparer.Ordinal),
            SourceAnchorIds: ["lifemodules.xml#nationality.module.1"],
            CompilationStatus: CharacterCreationFoundationDraftStatuses.PendingFinalization,
            CharacterEffectsApplied: false,
            DraftDigest: "sha256:" + new string('c', 64));
    }

    private static CharacterCreationPrerequisiteDraft PrerequisiteDraft(
        CharacterWorkspaceId id,
        long baseRevision)
    {
        CharacterCreationPriorityAssignment[] assignments =
            CharacterCreationPriorityCategoryIds.Ordered.Select((category, order) =>
                new CharacterCreationPriorityAssignment(
                    order,
                    category,
                    ((char)('A' + order)).ToString(),
                    $"00000000-0000-0000-0000-{order + 1:000000000000}",
                    "sha256:" + new string((char)('a' + order), 64),
                    4 - order,
                    category == CharacterCreationPriorityCategoryIds.Attributes ? 16 : null,
                    [$"priorities.xml#test:{category}"]))
            .ToArray();
        var draft = new CharacterCreationPrerequisiteDraft(
            CharacterCreationPrerequisiteSchemas.DraftV1,
            id,
            1,
            baseRevision,
            "sha256:" + new string('a', 64),
            "sha256:" + new string('b', 64),
            CharacterCreationBuildMethods.Priority,
            "223a11ff-80e0-428b-89a9-6ef1c243b8b6",
            "Standard",
            ["A", "B", "C", "D", "E"],
            10,
            assignments,
            25,
            0,
            ["settings.xml#setting:test", "priorities.xml"],
            "sha256:" + new string('d', 64))
        {
            HeritageSelection = new CharacterCreationPriorityHeritageSelection(
                "human",
                CharacterCreationPriorityChildKinds.Metatype,
                assignments[0].SourceId,
                "a53d885d-a4a4-443d-b6a6-b0a55b0a96c7",
                null,
                "Human",
                null,
                1,
                0,
                false,
                CharacterCreationPrerequisiteServiceTests.HumanAttributes(),
                "sha256:" + new string('e', 64),
                "sha256:" + new string('f', 64),
                ["priorities.xml#human", "metatypes.xml#human"]),
            TalentSelection = new CharacterCreationPriorityTalentSelection(
                "mundane",
                assignments[1].SourceId,
                "Mundane",
                "Mundane",
                0,
                null,
                null,
                null,
                [],
                "sha256:" + new string('9', 64),
                ["priorities.xml#mundane"]),
            EffectiveNormalAttributePoints = 16,
            TotalSpecialAttributePoints = 1
        };
        return draft;
    }

    private static WorkspaceDocument WithDraft(
        WorkspaceDocument document,
        CharacterCreationFoundationDraftLedger draft)
    {
        return document with
        {
            State = document.State with
            {
                AuxiliaryState = new WorkspaceDocumentAuxiliaryState(draft)
            }
        };
    }

    private static WorkspaceStoreEntry Create(
        IWorkspaceStore store,
        CharacterWorkspaceId id,
        WorkspaceDocument document)
    {
        WorkspaceStoreMutationResult result = store.CreateWorkspaceDocument(id, document);
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

    private static WorkspaceDocument Document(string name)
    {
        return new WorkspaceDocument(CharacterXml(name), RulesetId: "sr5");
    }

    private static string CharacterXml(string name)
    {
        return $"<character><name>{name}</name><alias>{name}</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>False</created></character>";
    }

    private static WorkspaceService CreateWorkspaceService(IWorkspaceStore store)
    {
        Sr5WorkspaceCodec codec = CreateSr5Codec();
        return new WorkspaceService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            new WorkspaceImportRulesetDetector());
    }

    private static Sr5WorkspaceCodec CreateSr5Codec()
    {
        CharacterFileService characterFileService = new();
        return new Sr5WorkspaceCodec(
            new XmlCharacterFileQueries(characterFileService),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(characterFileService));
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
}
