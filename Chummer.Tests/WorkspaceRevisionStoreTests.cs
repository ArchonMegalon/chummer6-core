using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceRevisionStoreTests
{
    [TestMethod]
    public void File_store_preserves_content_and_checkpoint_revisions_across_restarts()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore firstStore = new(stateDirectory);
            WorkspaceStoreEntry created = Create(firstStore, Document("created"));
            Assert.AreEqual(1L, created.ContentRevision);
            Assert.AreEqual(0L, created.SavedRevision);

            FileWorkspaceStore secondStore = new(stateDirectory);
            WorkspaceStoreReadResult restarted = secondStore.Get(created.Id);
            AssertRead(restarted, 1, 0, "created");

            WorkspaceStoreMutationResult replaced = secondStore.ReplaceWorkspaceDocument(
                created.Id,
                expectedContentRevision: 1,
                Document("replaced"));
            AssertMutation(replaced, 2, 0);

            WorkspaceStoreMutationResult checkpoint = secondStore.SaveCheckpoint(
                created.Id,
                expectedContentRevision: 2);
            AssertMutation(checkpoint, 2, 2);

            FileWorkspaceStore thirdStore = new(stateDirectory);
            AssertRead(thirdStore.Get(created.Id), 2, 2, "replaced");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_migrates_legacy_records_to_checkpointed_revision_one()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("legacyrevision");
            string path = GetTargetPath(stateDirectory, id);
            File.WriteAllText(
                path,
                """
                {
                  "Content": "<character><name>Legacy</name></character>",
                  "Format": "NativeXml",
                  "RulesetId": "SR5"
                }
                """);

            AssertRead(store.Get(id), 1, 1, "Legacy");

            using JsonDocument migrated = JsonDocument.Parse(File.ReadAllText(path));
            Assert.AreEqual(1L, migrated.RootElement.GetProperty("ContentRevision").GetInt64());
            Assert.AreEqual(1L, migrated.RootElement.GetProperty("SavedRevision").GetInt64());
            Assert.IsTrue(migrated.RootElement.TryGetProperty("Envelope", out _));
            Assert.IsFalse(migrated.RootElement.TryGetProperty("Content", out _));

            FileWorkspaceStore restarted = new(stateDirectory);
            AssertRead(restarted.Get(id), 1, 1, "Legacy");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_legacy_migration_preserves_the_logical_last_updated_timestamp()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("legacytimestamp");
            string path = GetTargetPath(stateDirectory, id);
            File.WriteAllText(path, """
                {
                  "Content": "<character><name>Old legacy</name></character>",
                  "RulesetId": "sr5"
                }
                """);
            DateTime oldTimestampUtc = DateTime.UtcNow.AddDays(-30);
            File.SetLastWriteTimeUtc(path, oldTimestampUtc);

            WorkspaceStoreReadResult migrated = store.Get(id);

            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsLessThan(DateTimeOffset.UtcNow.AddDays(-20), migrated.Value!.LastUpdatedUtc);
            Assert.IsTrue(
                Math.Abs((File.GetLastWriteTimeUtc(path) - oldTimestampUtc).TotalSeconds) <= 2,
                "Legacy migration changed the logical workspace timestamp.");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_uses_nonblank_legacy_content_when_envelope_payload_is_whitespace()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("mixedfallback");
            File.WriteAllText(GetTargetPath(stateDirectory, id), """
                {
                  "Format": "NativeXml",
                  "Envelope": {
                    "RulesetId": "sr5",
                    "SchemaVersion": 1,
                    "PayloadKind": "workspace",
                    "Payload": "   "
                  },
                  "Content": "<character><name>Legacy fallback</name></character>",
                  "ContentRevision": 2,
                  "SavedRevision": 1
                }
                """);

            AssertRead(store.Get(id), 2, 1, "Legacy fallback");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_mixed_record_prefers_a_nonblank_envelope_payload()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = new("mixedprecedence");
            File.WriteAllText(GetTargetPath(stateDirectory, id), """
                {
                  "Format": "NativeXml",
                  "Envelope": {
                    "RulesetId": "sr5",
                    "SchemaVersion": 1,
                    "PayloadKind": "workspace",
                    "Payload": "<character><name>Envelope winner</name></character>"
                  },
                  "Content": "<character><name>Legacy loser</name></character>",
                  "RulesetId": "sr6",
                  "ContentRevision": 2,
                  "SavedRevision": 1
                }
                """);

            WorkspaceStoreReadResult read = store.Get(id);
            AssertRead(read, 2, 1, "Envelope winner");
            Assert.IsFalse(read.Value!.Document.Content.Contains("Legacy loser", StringComparison.Ordinal));
            Assert.AreEqual(RulesetDefaults.Sr5, read.Value.Document.RulesetId);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_rejects_undefined_numeric_format_blank_state_and_corrupt_revisions()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            Dictionary<string, string> corruptRecords = new(StringComparer.Ordinal)
            {
                ["undefinedformat"] = PersistedJson("999", "sr5", "<character />", "2", "1"),
                ["blankruleset"] = PersistedJson("NativeXml", "   ", "opaque-payload", "2", "1"),
                ["blankcontent"] = PersistedJson("NativeXml", "sr5", "   ", "2", "1"),
                ["partialrevision"] = PersistedJson("NativeXml", "sr5", "<character />", "2", null),
                ["reversedrevision"] = PersistedJson("NativeXml", "sr5", "<character />", "2", "3"),
                ["zerocontentrevision"] = PersistedJson("NativeXml", "sr5", "<character />", "0", "0"),
                ["negativesavedrevision"] = PersistedJson("NativeXml", "sr5", "<character />", "1", "-1")
            };

            foreach ((string idValue, string json) in corruptRecords)
            {
                CharacterWorkspaceId id = new(idValue);
                File.WriteAllText(GetTargetPath(stateDirectory, id), json);
                Assert.AreEqual(
                    WorkspaceOperationOutcome.Corrupt,
                    store.Get(id).Outcome,
                    idValue);
            }
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_distinguishes_missing_corrupt_and_unavailable_reads()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Missing,
                store.Get(new CharacterWorkspaceId("missing")).Outcome);

            CharacterWorkspaceId corruptId = new("corrupt");
            File.WriteAllText(GetTargetPath(stateDirectory, corruptId), "{broken-json");
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, store.Get(corruptId).Outcome);

            if (!OperatingSystem.IsWindows())
            {
                WorkspaceStoreEntry linked = Create(store, Document("linked"));
                string targetPath = GetTargetPath(stateDirectory, linked.Id);
                string victimPath = Path.Combine(stateDirectory, "unavailable-victim");
                File.WriteAllText(victimPath, "victim");
                File.Delete(targetPath);
                File.CreateSymbolicLink(targetPath, victimPath);

                Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, store.Get(linked.Id).Outcome);
                Assert.AreEqual("victim", File.ReadAllText(victimPath));
            }
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_rejects_stale_replace_and_delete_without_clobbering_winner()
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
            WorkspaceStoreMutationResult staleReplace = store.ReplaceWorkspaceDocument(
                created.Id,
                1,
                Document("stale"));
            WorkspaceStoreMutationResult staleDelete = store.Delete(created.Id, 1);

            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, staleReplace.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Conflict, staleDelete.Outcome);
            AssertRead(store.Get(created.Id), 2, 0, "winner");
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task File_store_allows_exactly_one_concurrent_cas_winner()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore creator = new(stateDirectory);
            WorkspaceStoreEntry created = Create(creator, Document("created"));
            FileWorkspaceStore firstStore = new(stateDirectory);
            FileWorkspaceStore secondStore = new(stateDirectory);
            ManualResetEventSlim start = new(initialState: false);

            Task<WorkspaceStoreMutationResult> first = Task.Run(() =>
            {
                start.Wait();
                return firstStore.ReplaceWorkspaceDocument(created.Id, 1, Document("first"));
            });
            Task<WorkspaceStoreMutationResult> second = Task.Run(() =>
            {
                start.Wait();
                return secondStore.ReplaceWorkspaceDocument(created.Id, 1, Document("second"));
            });
            start.Set();

            WorkspaceStoreMutationResult[] results = await Task.WhenAll(first, second);

            Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Success));
            Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Conflict));
            WorkspaceStoreReadResult final = creator.Get(created.Id);
            Assert.AreEqual(2L, final.Value?.ContentRevision);
            Assert.IsTrue(
                final.Value?.Document.Content.Contains("first", StringComparison.Ordinal) == true
                || final.Value?.Document.Content.Contains("second", StringComparison.Ordinal) == true);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task In_memory_store_allows_exactly_one_concurrent_cas_winner()
    {
        InMemoryWorkspaceStore store = new();
        WorkspaceStoreEntry created = Create(store, Document("created"));
        ManualResetEventSlim start = new(initialState: false);
        Task<WorkspaceStoreMutationResult> first = Task.Run(() =>
        {
            start.Wait();
            return store.ReplaceWorkspaceDocument(created.Id, 1, Document("first"));
        });
        Task<WorkspaceStoreMutationResult> second = Task.Run(() =>
        {
            start.Wait();
            return store.ReplaceWorkspaceDocument(created.Id, 1, Document("second"));
        });
        start.Set();

        WorkspaceStoreMutationResult[] results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Success));
        Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Conflict));
        Assert.AreEqual(2L, store.Get(created.Id).Value?.ContentRevision);
    }

    [TestMethod]
    public async Task File_store_allows_exactly_one_concurrent_conditional_create_winner()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore firstStore = new(stateDirectory);
            FileWorkspaceStore secondStore = new(stateDirectory);
            OwnerScope owner = new("roaming-owner@example.com");
            CharacterWorkspaceId id = new("sharedroamingworkspace");
            ManualResetEventSlim start = new(initialState: false);
            Task<WorkspaceStoreMutationResult> first = Task.Run(() =>
            {
                start.Wait();
                return firstStore.CreateWorkspaceDocument(owner, id, Document("first"));
            });
            Task<WorkspaceStoreMutationResult> second = Task.Run(() =>
            {
                start.Wait();
                return secondStore.CreateWorkspaceDocument(owner, id, Document("second"));
            });
            start.Set();

            WorkspaceStoreMutationResult[] results = await Task.WhenAll(first, second);

            Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Success));
            Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Conflict));
            WorkspaceStoreReadResult final = firstStore.Get(owner, id);
            Assert.AreEqual(1L, final.Value?.ContentRevision);
            Assert.IsTrue(
                final.Value?.Document.Content.Contains("first", StringComparison.Ordinal) == true
                || final.Value?.Document.Content.Contains("second", StringComparison.Ordinal) == true);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task In_memory_store_allows_exactly_one_concurrent_conditional_create_winner()
    {
        InMemoryWorkspaceStore store = new();
        OwnerScope owner = new("roaming-owner@example.com");
        CharacterWorkspaceId id = new("sharedroamingworkspace");
        ManualResetEventSlim start = new(initialState: false);
        Task<WorkspaceStoreMutationResult> first = Task.Run(() =>
        {
            start.Wait();
            return store.CreateWorkspaceDocument(owner, id, Document("first"));
        });
        Task<WorkspaceStoreMutationResult> second = Task.Run(() =>
        {
            start.Wait();
            return store.CreateWorkspaceDocument(owner, id, Document("second"));
        });
        start.Set();

        WorkspaceStoreMutationResult[] results = await Task.WhenAll(first, second);

        Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Success));
        Assert.AreEqual(1, results.Count(result => result.Outcome == WorkspaceOperationOutcome.Conflict));
        Assert.AreEqual(1L, store.Get(owner, id).Value?.ContentRevision);
    }

    [TestMethod]
    public void Conditional_create_rejects_ids_outside_the_workspace_id_grammar()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            CharacterWorkspaceId invalid = new("../escape");
            WorkspaceStoreMutationResult fileResult = new FileWorkspaceStore(stateDirectory)
                .CreateWorkspaceDocument(invalid, Document("file"));
            WorkspaceStoreMutationResult memoryResult = new InMemoryWorkspaceStore()
                .CreateWorkspaceDocument(invalid, Document("memory"));

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, fileResult.Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, memoryResult.Outcome);
            Assert.IsFalse(File.Exists(Path.Combine(stateDirectory, "escape.json")));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Workspace_service_propagates_revisions_and_save_advances_checkpoint()
    {
        InMemoryWorkspaceStore store = new();
        WorkspaceService service = CreateWorkspaceService(store);
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Imported"),
            RulesetDefaults.Sr5));

        Assert.AreEqual(1L, imported.ContentRevision);
        Assert.AreEqual(0L, imported.SavedRevision);
        WorkspaceListItem listedBeforeSave = service.List().Single(item => item.Id == imported.Id);
        Assert.AreEqual(1L, listedBeforeSave.ContentRevision);
        Assert.AreEqual(0L, listedBeforeSave.SavedRevision);
        Assert.IsFalse(listedBeforeSave.HasSavedWorkspace);

        CommandResult<WorkspaceSaveReceipt> saved = service.Save(imported.Id, imported.ContentRevision);
        Assert.IsTrue(saved.Success);
        Assert.AreEqual(1L, saved.Value?.ContentRevision);
        Assert.AreEqual(1L, saved.Value?.SavedRevision);

        CommandResult<WorkspaceMetadataResult> updated = service.UpdateMetadata(
            imported.Id,
            expectedContentRevision: 1,
            new UpdateWorkspaceMetadata("Updated", "Alias", "Notes"));
        Assert.IsTrue(updated.Success);
        Assert.AreEqual(2L, updated.Value?.ContentRevision);
        Assert.AreEqual(1L, updated.Value?.SavedRevision);

        CommandResult<WorkspaceSaveReceipt> staleSave = service.Save(imported.Id, 1);
        Assert.AreEqual(WorkspaceOperationOutcome.Conflict, staleSave.Outcome);
        CommandResult<WorkspaceSaveReceipt> currentSave = service.Save(imported.Id, 2);
        Assert.IsTrue(currentSave.Success);
        Assert.AreEqual(2L, currentSave.Value?.SavedRevision);
    }

    [TestMethod]
    public void Workspace_service_import_does_not_create_when_export_projection_fails()
    {
        InMemoryWorkspaceStore store = new();
        FaultingWorkspaceCodec codec = new(CreateSr5Codec())
        {
            ThrowOnBuildExportBundle = true
        };
        WorkspaceService service = CreateWorkspaceService(store, codec);

        Assert.ThrowsExactly<InvalidOperationException>(() => service.Import(new WorkspaceImportDocument(
            CharacterXml("Rejected import"),
            RulesetDefaults.Sr5)));

        Assert.HasCount(0, store.List());
    }

    [TestMethod]
    public void Workspace_service_metadata_null_projection_does_not_commit()
    {
        InMemoryWorkspaceStore store = new();
        FaultingWorkspaceCodec codec = new(CreateSr5Codec());
        WorkspaceService service = CreateWorkspaceService(store, codec);
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Original"),
            RulesetDefaults.Sr5));
        WorkspaceStoredDocument before = store.Get(imported.Id).Value!;
        codec.ReturnNullProfile = true;

        CommandResult<WorkspaceMetadataResult> result = service.UpdateMetadata(
            imported.Id,
            imported.ContentRevision,
            new UpdateWorkspaceMetadata("Rejected", "Rejected", "Rejected"));

        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, result.Outcome);
        WorkspaceStoredDocument after = store.Get(imported.Id).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
    }

    [TestMethod]
    public void Workspace_service_metadata_projection_throw_does_not_commit()
    {
        InMemoryWorkspaceStore store = new();
        FaultingWorkspaceCodec codec = new(CreateSr5Codec());
        WorkspaceService service = CreateWorkspaceService(store, codec);
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Original"),
            RulesetDefaults.Sr5));
        WorkspaceStoredDocument before = store.Get(imported.Id).Value!;
        codec.ThrowOnProfileProjection = true;

        Assert.ThrowsExactly<InvalidOperationException>(() => service.UpdateMetadata(
            imported.Id,
            imported.ContentRevision,
            new UpdateWorkspaceMetadata("Rejected", "Rejected", "Rejected")));

        WorkspaceStoredDocument after = store.Get(imported.Id).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
    }

    [TestMethod]
    public void Workspace_service_save_does_not_checkpoint_when_receipt_projection_fails()
    {
        InMemoryWorkspaceStore store = new();
        FaultingWorkspaceCodec codec = new(CreateSr5Codec());
        WorkspaceService service = CreateWorkspaceService(store, codec);
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Unsaved"),
            RulesetDefaults.Sr5));
        codec.ReturnNullExportBundle = true;

        Assert.ThrowsExactly<NullReferenceException>(() => service.Save(
            imported.Id,
            imported.ContentRevision));

        WorkspaceStoredDocument after = store.Get(imported.Id).Value!;
        Assert.AreEqual(imported.ContentRevision, after.ContentRevision);
        Assert.AreEqual(0L, after.SavedRevision);
    }

    [TestMethod]
    public void Compatibility_metadata_shim_does_not_clobber_a_concurrent_winner()
    {
        InterleavingWorkspaceStore store = new();
        WorkspaceService service = CreateWorkspaceService(store);
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Imported"),
            RulesetDefaults.Sr5));
        store.ArmWinner(new WorkspaceDocument(CharacterXml("Winner"), RulesetId: RulesetDefaults.Sr5));

#pragma warning disable CS0618
        CommandResult<Chummer.Contracts.Characters.CharacterProfileSection> result = service.UpdateMetadata(
            imported.Id,
            new UpdateWorkspaceMetadata("Stale", "Stale", "Stale"));
#pragma warning restore CS0618

        Assert.IsFalse(result.Success);
        Assert.AreEqual(WorkspaceOperationOutcome.Conflict, result.Outcome);
        WorkspaceStoreReadResult final = store.Get(imported.Id);
        Assert.AreEqual(2L, final.Value?.ContentRevision);
        StringAssert.Contains(final.Value?.Document.Content ?? string.Empty, "Winner");
        Assert.IsFalse((final.Value?.Document.Content ?? string.Empty).Contains("Stale", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Workspace_service_owner_scoped_sentinels_cannot_reach_local_state()
    {
        WorkspaceService service = CreateWorkspaceService(new InMemoryWorkspaceStore());
        WorkspaceImportResult imported = service.Import(new WorkspaceImportDocument(
            CharacterXml("Local"),
            RulesetDefaults.Sr5));
        OwnerScope[] invalidOwners =
        [
            default,
            new OwnerScope(string.Empty),
            new OwnerScope("   "),
            new OwnerScope(" LOCAL-SINGLE-USER ")
        ];

        foreach (OwnerScope invalidOwner in invalidOwners)
        {
            Assert.AreEqual(0, service.List(invalidOwner).Count);
            Assert.IsNull(service.GetSummary(invalidOwner, imported.Id));
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                service.UpdateMetadata(
                    invalidOwner,
                    imported.Id,
                    imported.ContentRevision,
                    new UpdateWorkspaceMetadata("Blocked", "Blocked", "Blocked")).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                service.Save(invalidOwner, imported.Id, imported.ContentRevision).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                service.Close(invalidOwner, imported.Id, imported.ContentRevision).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                service.Download(invalidOwner, imported.Id).Outcome);
        }

        Assert.IsNotNull(service.GetSummary(imported.Id));
        Assert.AreEqual(1L, service.List().Single(item => item.Id == imported.Id).ContentRevision);
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
            RulesetId: RulesetDefaults.Sr5);
    }

    private static string CharacterXml(string name)
    {
        return $"<character><name>{name}</name><alias>{name}</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>";
    }

    private static string PersistedJson(
        string format,
        string rulesetId,
        string payload,
        string contentRevision,
        string? savedRevision)
    {
        Dictionary<string, object?> record = new(StringComparer.Ordinal)
        {
            ["Format"] = format,
            ["Envelope"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["RulesetId"] = rulesetId,
                ["SchemaVersion"] = 1,
                ["PayloadKind"] = "workspace",
                ["Payload"] = payload
            },
            ["ContentRevision"] = long.Parse(contentRevision)
        };
        if (savedRevision is not null)
        {
            record["SavedRevision"] = long.Parse(savedRevision);
        }

        return JsonSerializer.Serialize(record);
    }

    private static WorkspaceService CreateWorkspaceService(
        IWorkspaceStore store,
        IRulesetWorkspaceCodec? codec = null)
    {
        codec ??= CreateSr5Codec();
        return new WorkspaceService(
            store,
            new RulesetWorkspaceCodecResolver([codec]),
            new WorkspaceImportRulesetDetector());
    }

    private static Sr5WorkspaceCodec CreateSr5Codec()
    {
        CharacterFileService characterFileService = new();
        XmlCharacterFileQueries fileQueries = new(characterFileService);
        XmlCharacterSectionQueries sectionQueries = new(new CharacterSectionService());
        XmlCharacterMetadataCommands metadataCommands = new(characterFileService);
        return new Sr5WorkspaceCodec(fileQueries, sectionQueries, metadataCommands);
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

    private sealed class InterleavingWorkspaceStore : IWorkspaceStore
    {
        private readonly InMemoryWorkspaceStore _inner = new();
        private WorkspaceDocument? _winner;
        private int _armed;

        public void ArmWinner(WorkspaceDocument winner)
        {
            _winner = winner;
            Volatile.Write(ref _armed, 1);
        }

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(CharacterWorkspaceId id, WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(id, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, WorkspaceDocument document)
            => _inner.CreateWorkspaceDocument(owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List() => _inner.List();

        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => _inner.List(owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => GetCore(OwnerScope.LocalSingleUser, id, trustedLocalScope: true);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => GetCore(owner, id, trustedLocalScope: false);

        private WorkspaceStoreReadResult GetCore(
            OwnerScope owner,
            CharacterWorkspaceId id,
            bool trustedLocalScope)
        {
            WorkspaceStoreReadResult stale = trustedLocalScope
                ? _inner.Get(id)
                : _inner.Get(owner, id);
            if (Interlocked.Exchange(ref _armed, 0) == 1
                && stale.Value is WorkspaceStoredDocument current
                && _winner is WorkspaceDocument winner)
            {
                WorkspaceStoreMutationResult won = trustedLocalScope
                    ? _inner.ReplaceWorkspaceDocument(id, current.ContentRevision, winner)
                    : _inner.ReplaceWorkspaceDocument(owner, id, current.ContentRevision, winner);
                Assert.IsTrue(won.Success, won.Error);
            }

            return stale;
        }

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision, WorkspaceDocument document)
            => _inner.ReplaceWorkspaceDocument(owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.SaveCheckpoint(id, expectedContentRevision);

        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.SaveCheckpoint(owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.Delete(id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
            => _inner.Delete(owner, id, expectedContentRevision);
    }

    private sealed class FaultingWorkspaceCodec : IRulesetWorkspaceCodec
    {
        private readonly IRulesetWorkspaceCodec _inner;

        public FaultingWorkspaceCodec(IRulesetWorkspaceCodec inner)
        {
            _inner = inner;
        }

        public bool ThrowOnBuildExportBundle { get; set; }

        public bool ReturnNullExportBundle { get; set; }

        public bool ReturnNullProfile { get; set; }

        public bool ThrowOnProfileProjection { get; set; }

        public string RulesetId => _inner.RulesetId;

        public int SchemaVersion => _inner.SchemaVersion;

        public string PayloadKind => _inner.PayloadKind;

        public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
            => _inner.WrapImport(rulesetId, document);

        public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
            => _inner.ParseSummary(envelope);

        public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
        {
            if (string.Equals(sectionId, "profile", StringComparison.Ordinal))
            {
                if (ThrowOnProfileProjection)
                {
                    throw new InvalidOperationException("Injected profile projection failure.");
                }

                if (ReturnNullProfile)
                {
                    return null!;
                }
            }

            return _inner.ParseSection(sectionId, envelope);
        }

        public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
            => _inner.Validate(envelope);

        public WorkspacePayloadEnvelope UpdateMetadata(
            WorkspacePayloadEnvelope envelope,
            UpdateWorkspaceMetadata command)
            => _inner.UpdateMetadata(envelope, command);

        public WorkspaceDownloadReceipt BuildDownload(
            CharacterWorkspaceId id,
            WorkspacePayloadEnvelope envelope,
            WorkspaceDocumentFormat format)
            => _inner.BuildDownload(id, envelope, format);

        public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
        {
            if (ThrowOnBuildExportBundle)
            {
                throw new InvalidOperationException("Injected export projection failure.");
            }

            return ReturnNullExportBundle
                ? null!
                : _inner.BuildExportBundle(envelope);
        }
    }
}
