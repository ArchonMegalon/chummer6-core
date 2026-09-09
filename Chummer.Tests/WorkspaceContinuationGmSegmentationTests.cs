using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationGmSegmentationTests
{
    private static readonly OwnerScope Owner = new("gm-segmentation-owner");
    private static readonly CharacterWorkspaceId Id = new("gm-segmentation-runner");
    private const int MaximumBytes = 1024 * 1024;

    [TestMethod]
    public void Imported_future_history_allows_genuine_local_authority_then_preserves_every_execution_segment()
    {
        using StoreContext context = new();
        var importedCommand = context.Apply("foreign-key", "Foreign note");
        var imported = context.Snapshot();
        Assert.AreEqual(2030, imported.DelegatedGmCharacterEdits[0].AppliedAtUtc.Year);
        Assert.AreEqual(20L, imported.DelegatedGmCharacterEdits[0].AuthorityRevision);

        context.StartNewAuthority();
        var localCommand = context.Command("local-key", "Local note");
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Unavailable, context.Service().Execute(localCommand).Outcome,
            "A native execution history must not reset its clock or delegation bindings.");
        AssertSnapshotEqual(imported, context.Snapshot());

        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, context.Service().Execute(localCommand).Outcome);
        var firstLocal = context.Snapshot();
        CollectionAssert.AreEqual(new[] { 1 }, firstLocal.DelegatedGmHistorySegmentStarts.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(imported.DelegatedGmCharacterEdits[0]),
            JsonSerializer.Serialize(firstLocal.DelegatedGmCharacterEdits[0]));
        Assert.IsFalse(DelegatedGmCharacterEditLedgerValidator.IsValidLedger(Owner, Id,
            firstLocal.Workspace.ContentRevision, Ledger(firstLocal)));
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(Owner, firstLocal));
        AssertRoundtrip(firstLocal);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Replayed, context.Service().Execute(localCommand).Outcome);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Conflict, context.Service().Execute(importedCommand).Outcome);

        context.Clock.Now = context.Clock.Now.AddMinutes(-1);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Unavailable,
            context.Service().Execute(context.Command("local-clock-rollback", "Rejected clock")).Outcome);
        context.Clock.Now = context.Clock.Now.AddMinutes(1);
        context.Authority.Revision = 6;
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Unavailable,
            context.Service().Execute(context.Command("local-authority-rollback", "Rejected authority")).Outcome);
        AssertSnapshotEqual(firstLocal, context.Snapshot());
        context.Authority.Revision = 7;
        context.Apply("second-local-key", "Second local note");
        var twoLocal = context.Snapshot();
        CollectionAssert.AreEqual(new[] { 1 }, twoLocal.DelegatedGmHistorySegmentStarts.ToArray());

        // Reimporting an already segmented history preserves every old grouping;
        // only its subsequent real local append opens another execution segment.
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        context.Clock.Now = context.Clock.Now.AddYears(-1);
        context.Authority.Revision = 2;
        context.Authority.CampaignOwner = "third-authority-owner";
        var thirdEpochCommand = context.Apply("third-epoch-key", "Third epoch note");
        var thirdEpoch = context.Snapshot();
        CollectionAssert.AreEqual(new[] { 1, 3 }, thirdEpoch.DelegatedGmHistorySegmentStarts.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(twoLocal.DelegatedGmCharacterEdits),
            JsonSerializer.Serialize(thirdEpoch.DelegatedGmCharacterEdits.Take(3).ToArray()));
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Replayed, context.Service().Execute(thirdEpochCommand).Outcome);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Conflict, context.Service().Execute(localCommand).Outcome);
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(Owner, thirdEpoch));
        AssertRoundtrip(thirdEpoch);

        long revision = thirdEpoch.Workspace.ContentRevision;
        Assert.IsTrue(context.Store.SaveCheckpoint(Owner, Id, revision).Success);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocument(Owner, Id, revision, Document("Owner dirty edit")).Success);
        Assert.IsTrue(context.Store.ReplaceWorkspaceDocumentAndCheckpoint(Owner, Id, revision + 1, Document("Owner checkpoint")).Success);
        var afterWrites = context.Snapshot();
        CollectionAssert.AreEqual(new[] { 1, 3 }, afterWrites.DelegatedGmHistorySegmentStarts.ToArray());
        Assert.AreEqual(JsonSerializer.Serialize(thirdEpoch.DelegatedGmCharacterEdits),
            JsonSerializer.Serialize(afterWrites.DelegatedGmCharacterEdits));
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(Owner, afterWrites));
        AssertRoundtrip(afterWrites);
    }

    [TestMethod]
    public void Portable_boundary_cannot_reset_the_store_local_authority_epoch()
    {
        using StoreContext context = new();
        context.Apply("first", "First");
        context.Apply("second", "Second");
        var snapshot = context.Snapshot();
        var second = snapshot.DelegatedGmCharacterEdits[1] with
        {
            AppliedAtUtc = snapshot.DelegatedGmCharacterEdits[0].AppliedAtUtc.AddYears(-1),
            AuthorityRevision = 7,
            GrantedByCampaignOwnerId = "foreign-binding"
        };
        var portable = snapshot with
        {
            DelegatedGmCharacterEdits = [snapshot.DelegatedGmCharacterEdits[0], second],
            DelegatedGmHistorySegmentStarts = [1]
        };
        Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidSegmentedLedger(Owner, Id,
            snapshot.Workspace.ContentRevision, Ledger(portable), portable.DelegatedGmHistorySegmentStarts));
        AssertRoundtrip(portable);
        Assert.IsTrue(context.Read().CanReplayReceipt(second.NewRevision));
        var record = context.Record();
        record["DelegatedGmHistorySegmentStarts"] = new JsonArray(1);
        record["DelegatedGmCharacterEdits"]![1]!["Receipt"] = JsonSerializer.SerializeToNode(second);
        context.WriteRecord(record);
        context.AssertCorruptWithoutWrites();
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("missing")]
    [DataRow("zero")]
    [DataRow("end")]
    [DataRow("negative")]
    [DataRow("duplicate")]
    [DataRow("unsorted")]
    [DataRow("local-extra")]
    [DataRow("missing-local-start")]
    public void Invalid_persisted_boundaries_fail_closed_without_rewriting(string damage)
    {
        using StoreContext context = new();
        context.Apply("foreign", "Foreign");
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        context.Apply("local-one", "Local one");
        context.Apply("local-two", "Local two");
        var record = context.Record();
        record["DelegatedGmHistorySegmentStarts"] = damage switch
        {
            "null" or "missing" => null,
            "zero" => new JsonArray(0),
            "end" => new JsonArray(3),
            "negative" => new JsonArray(-1),
            "duplicate" => new JsonArray(1, 1),
            "unsorted" => new JsonArray(2, 1),
            "local-extra" => new JsonArray(1, 2),
            "missing-local-start" => new JsonArray(),
            _ => throw new AssertFailedException(damage)
        };
        if (damage == "missing") record.Remove("DelegatedGmHistorySegmentStarts");
        context.WriteRecord(record);
        context.AssertCorruptWithoutWrites();
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("workspace")]
    [DataRow("overlap")]
    [DataRow("reversed")]
    [DataRow("key")]
    [DataRow("receipt")]
    public void Segment_boundaries_do_not_relax_global_identity_uniqueness_or_revision_checks(string damage)
    {
        using StoreContext context = new();
        context.Apply("foreign", "Foreign");
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        context.StartNewAuthority();
        context.Apply("local", "Local");
        var snapshot = context.Snapshot();
        var entries = Ledger(snapshot);
        Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidSegmentedLedger(Owner, Id, 3, entries, [1]));
        var second = entries[1].Receipt;
        second = damage switch
        {
            "owner" => second with { CharacterOwnerId = "another-owner", GrantedByCharacterOwnerId = "another-owner" },
            "workspace" => second with { CharacterId = new("another-runner") },
            "overlap" => second with { PreviousRevision = 1, NewRevision = 2 },
            "key" => second with { IdempotencyKeySha256 = entries[0].IdempotencyKeySha256 },
            "receipt" => second with { ReceiptId = entries[0].Receipt.ReceiptId },
            "reversed" => second,
            _ => throw new AssertFailedException(damage)
        };
        if (damage == "key")
            second = second with { ReceiptId = ReceiptId(second) };
        entries[1] = new(second.IdempotencyKeySha256, second.CommandSha256, second);
        if (damage == "reversed") Array.Reverse(entries);
        Assert.IsFalse(DelegatedGmCharacterEditLedgerValidator.IsValidSegmentedLedger(Owner, Id, 3, entries, [1]));
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("null")]
    [DataRow("fraction")]
    [DataRow("duplicate")]
    [DataRow("old-contract")]
    [DataRow("changed-digest-input")]
    public void Version_two_codec_requires_exact_segment_metadata(string damage)
    {
        using StoreContext context = new();
        context.Apply("first", "First");
        context.Apply("second", "Second");
        var snapshot = context.Snapshot() with { DelegatedGmHistorySegmentStarts = [1] };
        AssertRoundtrip(snapshot);
        Assert.AreNotEqual(WorkspaceContinuationSnapshotDigest.Compute(snapshot),
            WorkspaceContinuationSnapshotDigest.Compute(snapshot with { DelegatedGmHistorySegmentStarts = [] }));
        var wire = JsonNode.Parse(WorkspaceContinuationCodec.Encode(
            new(snapshot, WorkspaceContinuationSnapshotDigest.Compute(snapshot)), MaximumBytes))!.AsObject();
        var captured = wire["Snapshot"]!.AsObject();
        switch (damage)
        {
            case "missing": captured.Remove("DelegatedGmHistorySegmentStarts"); break;
            case "null": captured["DelegatedGmHistorySegmentStarts"] = null; break;
            case "fraction": captured["DelegatedGmHistorySegmentStarts"] = new JsonArray(1.5); break;
            case "duplicate": captured["DelegatedGmHistorySegmentStarts"] = new JsonArray(1, 1); break;
            case "old-contract": wire["ContractName"] = "chummer.workspace-continuation-snapshot/v1"; break;
            case "changed-digest-input": captured["DelegatedGmHistorySegmentStarts"] = new JsonArray(); break;
        }
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(Encoding.UTF8.GetBytes(wire.ToJsonString()),
            MaximumBytes, out var decoded));
        Assert.IsNull(decoded);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Version_three_migration_preserves_local_history_and_derives_only_existing_local_suffix(bool localSuffix)
    {
        using StoreContext context = new();
        context.Apply("foreign", "Foreign");
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        if (localSuffix) context.Apply("local", "Local");
        var before = context.Snapshot();
        var history = context.Read().LocalHistory;
        var record = context.Record();
        record["RecordSchemaVersion"] = 3;
        record.Remove("DelegatedGmHistorySegmentStarts");
        context.WriteRecord(record, before.Workspace.LastUpdatedUtc.UtcDateTime);
        byte[] legacy = File.ReadAllBytes(context.Path);
        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable,
            new FileWorkspaceStore(context.Directory).ReadContinuation(Owner, Id).Outcome);
        CollectionAssert.AreEqual(legacy, File.ReadAllBytes(context.Path));
        Assert.AreEqual(history, context.Read().LocalHistory);
        AssertSnapshotEqual(before, context.Snapshot());
        Assert.AreEqual(4, context.Record()["RecordSchemaVersion"]!.GetValue<int>());
    }

    [TestMethod]
    public void Legacy_whole_ledger_corruption_is_not_blessed_by_migration_segmentation()
    {
        using StoreContext context = new();
        context.Apply("foreign", "Foreign");
        WorkspaceImportedHistoryTestFixture.MarkImported(context.Directory, Id, Owner);
        context.StartNewAuthority();
        context.Apply("local", "Local");
        var record = context.Record();
        record["RecordSchemaVersion"] = 3;
        record.Remove("DelegatedGmHistorySegmentStarts");
        context.WriteRecord(record);
        context.AssertCorruptWithoutWrites();
    }

    private static DelegatedGmCharacterEditLedgerEntry[] Ledger(WorkspaceContinuationSnapshot snapshot) =>
        snapshot.DelegatedGmCharacterEdits.Select(receipt =>
            new DelegatedGmCharacterEditLedgerEntry(receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt)).ToArray();

    private static string ReceiptId(DelegatedGmCharacterEditAuditReceipt receipt) => "gm-edit-" +
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(receipt.CommandSha256 + "\n"
            + receipt.DelegationId + "\n" + receipt.AuthorityReceiptId + "\n" + receipt.IdempotencyKeySha256)))[..24];

    private static void AssertSnapshotEqual(WorkspaceContinuationSnapshot expected, WorkspaceContinuationSnapshot actual) =>
        Assert.AreEqual(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));

    private static void AssertRoundtrip(WorkspaceContinuationSnapshot snapshot)
    {
        byte[] bytes = WorkspaceContinuationCodec.Encode(new(snapshot,
            WorkspaceContinuationSnapshotDigest.Compute(snapshot)), MaximumBytes);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out var decoded));
        AssertSnapshotEqual(snapshot, decoded!.Snapshot);
        CollectionAssert.AreEqual(bytes, WorkspaceContinuationCodec.Encode(decoded, MaximumBytes));
    }

    private static WorkspaceDocument Document(string note) => new(
        $"<character><name>Runner</name><alias>One</alias><notes>{note}</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>", "sr5");

    private sealed class StoreContext : IDisposable
    {
        public string Directory { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chummer-gm-segment-" + Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Directory.GetFiles(Directory, Id.Value + ".json", SearchOption.AllDirectories).Single();
        public FileWorkspaceStore Store { get; }
        public MutableClock Clock { get; } = new();
        public MutableAuthority Authority { get; }
        public StoreContext()
        {
            Store = new(Directory);
            Authority = new(Clock);
            Assert.IsTrue(Store.CreateWorkspaceDocument(Owner, Id, Document("Original")).Success);
        }
        public WorkspaceStoredDocument Read()
        {
            var result = new FileWorkspaceStore(Directory).Get(Owner, Id);
            Assert.IsTrue(result.Success, result.Error);
            return result.Value!;
        }
        public WorkspaceContinuationSnapshot Snapshot()
        {
            var result = new FileWorkspaceStore(Directory).ReadContinuation(Owner, Id);
            Assert.IsTrue(result.Success, result.Error);
            return result.Value!;
        }
        public DelegatedGmCharacterEditCommand Command(string key, string note) => new(
            "segment-campaign", "gm@example.com", Owner, Id, Read().ContentRevision, key, "Update visible notes",
            [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath, note)]);
        public DelegatedGmCharacterEditCommand Apply(string key, string note)
        {
            var command = Command(key, note);
            var result = Service().Execute(command);
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, result.Outcome, result.Error);
            return command;
        }
        public DelegatedGmCharacterEditService Service()
        {
            CharacterFileService files = new();
            Sr5WorkspaceCodec codec = new(new XmlCharacterFileQueries(files),
                new XmlCharacterSectionQueries(new CharacterSectionService()), new XmlCharacterMetadataCommands(files));
            return new(new FileWorkspaceStore(Directory), new RulesetWorkspaceCodecResolver([codec]), Authority, Clock);
        }
        public void StartNewAuthority()
        {
            Clock.Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
            Authority.Revision = 7;
            Authority.CampaignOwner = "new-campaign-owner";
        }
        public JsonObject Record() => JsonNode.Parse(File.ReadAllText(Path))!.AsObject();
        public void WriteRecord(JsonObject record, DateTime? timestamp = null)
        {
            File.WriteAllText(Path, record.ToJsonString());
            if (timestamp is { } time) File.SetLastWriteTimeUtc(Path, time);
        }
        public void AssertCorruptWithoutWrites()
        {
            byte[] before = File.ReadAllBytes(Path);
            DateTime timestamp = File.GetLastWriteTimeUtc(Path);
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, new FileWorkspaceStore(Directory).Get(Owner, Id).Outcome);
            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, new FileWorkspaceStore(Directory).ReadContinuation(Owner, Id).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Path));
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(Path));
        }
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class MutableAuthority(MutableClock clock) : ICampaignGmCharacterEditAuthorizer
    {
        public long Revision { get; set; } = 20;
        public string CampaignOwner { get; set; } = "old-campaign-owner";
        public CampaignGmCharacterEditAuthorization Authorize(CampaignGmCharacterEditAuthorizationRequest request) => new(
            true, request.CampaignId, request.ActorId, DelegatedGmCharacterEditContract.GameMasterRole,
            DelegatedGmCharacterEditContract.CharacterEditScope, request.CharacterOwner, request.CharacterId,
            "reused-delegation-id", CampaignOwner, request.CharacterOwner.NormalizedValue, "reused-authority-id", Revision,
            clock.Now.AddMinutes(-5), clock.Now.AddHours(1), [DelegatedGmCharacterEditContract.ProfileNotesPath]);
    }
}
