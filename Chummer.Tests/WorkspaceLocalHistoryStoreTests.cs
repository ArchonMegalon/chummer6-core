using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceLocalHistoryStoreTests
{
    private static readonly OwnerScope Owner = new("local-history-owner");
    private static readonly CharacterWorkspaceId Id = new("local-history-runner");

    [TestMethod]
    public void Local_incarnation_survives_writes_checkpoints_and_cold_reads_but_not_delete_recreate()
    {
        using StoreContext context = new();
        var initial = context.Read();
        WorkspaceLocalHistory history = initial.LocalHistory!;
        Assert.IsNotNull(history);
        Assert.IsTrue(history.IsValid(initial.ContentRevision));
        Assert.AreEqual(0L, history.ImportedThroughRevision);
        Assert.IsNull(history.ImportedSnapshotDigest);
        Assert.AreEqual(3, JsonNode.Parse(File.ReadAllText(context.Path))!["RecordSchemaVersion"]!.GetValue<int>());
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(Owner, Id, 1, Document("dirty")).Success);
        Assert.AreEqual(history, context.Read().LocalHistory);
        Assert.IsTrue(context.Store.SaveCheckpoint(Owner, Id, 2).Success);
        Assert.AreEqual(history, context.Read().LocalHistory);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocumentAndCheckpoint(Owner, Id, 2, Document("saved")).Success);
        Assert.AreEqual(history, context.Read().LocalHistory);
        var beforeDelete = context.Read();
        Assert.IsTrue(context.Store.Delete(Owner, Id, beforeDelete.ContentRevision).Success);
        Assert.IsTrue(context.Store.CreateWorkspaceDocument(Owner, Id, Document("new incarnation")).Success);
        Assert.AreNotEqual(history.IncarnationId, context.Read().LocalHistory!.IncarnationId);
    }

    [TestMethod]
    public void Version_two_migration_preserves_real_bootstrap_auxiliary_revisions_and_timestamp()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsNotNull(before.Document.AuxiliaryState.CharacterCreationBootstrapBinding);
        string path = System.IO.Path.Combine(context.Directory, "workspaces", context.WorkspaceId.Value + ".json");
        JsonObject record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["RecordSchemaVersion"] = 2;
        record.Remove("LocalHistory");
        File.WriteAllText(path, record.ToJsonString());
        File.SetLastWriteTimeUtc(path, before.LastUpdatedUtc.UtcDateTime);
        byte[] legacy = File.ReadAllBytes(path);
        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable,
            new FileWorkspaceStore(context.Directory).ReadContinuation(context.WorkspaceId).Outcome);
        CollectionAssert.AreEqual(legacy, File.ReadAllBytes(path), "Export must never migrate.");

        var read = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId);
        Assert.IsTrue(read.Success, read.Error);
        var after = read.Value!;
        Assert.AreEqual(JsonSerializer.Serialize(before.Document), JsonSerializer.Serialize(after.Document));
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.LastUpdatedUtc, after.LastUpdatedUtc);
        Assert.IsTrue(after.LocalHistory!.IsValid(after.ContentRevision));
        Assert.AreEqual(0L, after.LocalHistory.ImportedThroughRevision);
        Assert.IsTrue(new FileWorkspaceStore(context.Directory).ReadContinuation(context.WorkspaceId).Success);
        Assert.AreEqual(after.LocalHistory,
            new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!.LocalHistory);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("missing-floor")]
    [DataRow("missing-digest")]
    [DataRow("bad-incarnation")]
    [DataRow("negative-floor")]
    [DataRow("future-floor")]
    [DataRow("missing-import-digest")]
    [DataRow("unexpected-local-digest")]
    [DataRow("unknown-field")]
    [DataRow("downgraded-record")]
    [DataRow("missing-revisions")]
    [DataRow("version-two-missing-revisions")]
    public void Invalid_local_provenance_fails_closed_without_rewriting(string damage)
    {
        using StoreContext context = new();
        JsonObject record = JsonNode.Parse(File.ReadAllText(context.Path))!.AsObject();
        JsonObject history = record["LocalHistory"]!.AsObject();
        switch (damage)
        {
            case "missing": record.Remove("LocalHistory"); break;
            case "missing-floor": history.Remove("ImportedThroughRevision"); break;
            case "missing-digest": history.Remove("ImportedSnapshotDigest"); break;
            case "bad-incarnation": history["IncarnationId"] = Guid.Empty.ToString("N"); break;
            case "negative-floor": history["ImportedThroughRevision"] = -1; break;
            case "future-floor": history["ImportedThroughRevision"] = 2; history["ImportedSnapshotDigest"] = new string('a', 64); break;
            case "missing-import-digest": history["ImportedThroughRevision"] = 1; break;
            case "unexpected-local-digest": history["ImportedSnapshotDigest"] = new string('a', 64); break;
            case "unknown-field": history["ReplayAuthorized"] = true; break;
            case "downgraded-record": record["RecordSchemaVersion"] = 2; break;
            case "missing-revisions": record.Remove("ContentRevision"); record.Remove("SavedRevision"); break;
            case "version-two-missing-revisions":
                record["RecordSchemaVersion"] = 2; record.Remove("LocalHistory");
                record.Remove("ContentRevision"); record.Remove("SavedRevision"); break;
        }
        File.WriteAllText(context.Path, record.ToJsonString());
        byte[] before = File.ReadAllBytes(context.Path);
        DateTime timestamp = File.GetLastWriteTimeUtc(context.Path);
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt,
            new FileWorkspaceStore(context.Directory).Get(Owner, Id).Outcome, damage);
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt,
            new FileWorkspaceStore(context.Directory).ReadContinuation(Owner, Id).Outcome, damage);
        Assert.IsFalse(context.Store.ReplaceWorkspaceDocument(Owner, Id, 1, Document("overwrite")).Success);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(context.Path));
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(context.Path));
    }

    [TestMethod]
    public void Imported_GM_keys_are_reserved_not_replayed_while_later_local_keys_remain_replayable()
    {
        using StoreContext context = new();
        var firstCommand = Command(1, "first-key", "first edit");
        var first = context.Service().Execute(firstCommand);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, first.Outcome, first.Error);
        var before = context.Store.ReadContinuation(Owner, Id).Value!;
        string digest = WorkspaceContinuationSnapshotDigest.Compute(before);
        WorkspaceLocalHistory local = context.Read().LocalHistory!;

        // Private persisted-state fixture ONLY. No production restore setter or
        // uploaded JSON may manufacture store-local provenance.
        context.SetImportedHistory(new(local.IncarnationId, 2, digest));
        var history = context.Read().LocalHistory!;
        var receipt = before.DelegatedGmCharacterEdits.Single();
        var lookup = context.Store.LookupDelegatedGmCharacterEdit(Owner, Id,
            receipt.IdempotencyKeySha256, receipt.CommandSha256);
        Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict, lookup.Outcome);
        Assert.IsNull(lookup.Receipt);
        var replay = context.Service().Execute(firstCommand);
        Assert.AreNotEqual(DelegatedGmCharacterEditOutcome.Replayed, replay.Outcome);
        Assert.AreNotEqual(DelegatedGmCharacterEditOutcome.Applied, replay.Outcome);
        var apply = context.Store.ApplyDelegatedGmCharacterEdit(Owner, Id, 2, Document("replayed overwrite"),
            new(receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt));
        Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict, apply.Outcome);
        Assert.AreEqual(2L, context.Read().ContentRevision);

        var secondCommand = Command(2, "second-key", "new local edit");
        var second = context.Service().Execute(secondCommand);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, second.Outcome, second.Error);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Replayed, context.Service().Execute(secondCommand).Outcome);
        Assert.AreEqual(history, context.Read().LocalHistory);
        Assert.IsTrue(context.Store.SaveCheckpoint(Owner, Id, 3).Success);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocumentAndCheckpoint(Owner, Id, 3, Document("owner edit")).Success);
        Assert.AreEqual(history, context.Read().LocalHistory);
        var exported = new FileWorkspaceStore(context.Directory).ReadContinuation(Owner, Id).Value!;
        Assert.HasCount(2, exported.DelegatedGmCharacterEdits);
        Assert.AreEqual(JsonSerializer.Serialize(receipt), JsonSerializer.Serialize(exported.DelegatedGmCharacterEdits[0]));
        string wire = Encoding.UTF8.GetString(WorkspaceContinuationCodec.Encode(
            new(exported, WorkspaceContinuationSnapshotDigest.Compute(exported)), 1024 * 1024));
        Assert.IsFalse(wire.Contains(history.IncarnationId, StringComparison.Ordinal));
        Assert.IsFalse(wire.Contains("LocalHistory", StringComparison.Ordinal));
        Assert.IsFalse(wire.Contains("ImportedThroughRevision", StringComparison.Ordinal));
        Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict,
            context.Store.LookupDelegatedGmCharacterEdit(Owner, Id, receipt.IdempotencyKeySha256, receipt.CommandSha256).Outcome);
    }

    private static DelegatedGmCharacterEditCommand Command(long revision, string key, string note) => new(
        "history-campaign", "gm@example.com", Owner, Id, revision, key, "Update visible notes",
        [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath, note)]);

    private static WorkspaceDocument Document(string note) => new(
        $"<character><name>Runner</name><alias>One</alias><notes>{note}</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>", "sr5");

    private sealed class StoreContext : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chummer-local-history-" + Guid.NewGuid().ToString("N"));
        public FileWorkspaceStore Store { get; }
        public string Path => System.IO.Directory.GetFiles(Directory, Id.Value + ".json", SearchOption.AllDirectories).Single();
        public StoreContext()
        {
            Store = new(Directory);
            Assert.IsTrue(Store.CreateWorkspaceDocument(Owner, Id, Document("original")).Success);
        }
        public WorkspaceStoredDocument Read()
        {
            var read = new FileWorkspaceStore(Directory).Get(Owner, Id);
            Assert.IsTrue(read.Success, read.Error);
            return read.Value!;
        }
        public void SetImportedHistory(WorkspaceLocalHistory history)
        {
            var record = JsonNode.Parse(File.ReadAllText(Path))!.AsObject();
            record["LocalHistory"] = JsonSerializer.SerializeToNode(history);
            File.WriteAllText(Path, record.ToJsonString());
        }
        public DelegatedGmCharacterEditService Service()
        {
            CharacterFileService files = new();
            Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
            return new(new FileWorkspaceStore(Directory), new RulesetWorkspaceCodecResolver([codec]), new GmAuthorizer(), new GmClock());
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }
    private sealed class GmAuthorizer : ICampaignGmCharacterEditAuthorizer
    {
        public CampaignGmCharacterEditAuthorization Authorize(CampaignGmCharacterEditAuthorizationRequest request) => new(
            true, request.CampaignId, request.ActorId, DelegatedGmCharacterEditContract.GameMasterRole,
            DelegatedGmCharacterEditContract.CharacterEditScope, request.CharacterOwner, request.CharacterId,
            "history-delegation", "campaign-owner", request.CharacterOwner.NormalizedValue, "history-authority", 7,
            GmClock.Now.AddMinutes(-5), GmClock.Now.AddHours(1), [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }
    private sealed class GmClock : TimeProvider
    {
        public static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
