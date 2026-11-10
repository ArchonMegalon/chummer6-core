using System.Xml.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Contracts.Owners;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6CareerAttributeAdvancementTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void Natural_limit_is_not_the_augmented_ceiling(int bonus)
    {
        using var f = new Fixture(6, bonus: bonus);
        f.Edit(root => root.Element("attributes")!.Elements().Single(x => x.Element("sr6id")?.Value == "Body")
            .Add(new XElement("metatypeaugmax", 10)));
        var quote = f.Quote();
        Assert.AreEqual(6, quote.NaturalMaximum);
        Assert.AreEqual("sr6_attribute_natural_limit", quote.Blocker);
        string before = f.Read().Document.Content;
        Assert.AreEqual(Sr6CareerAttributeOutcome.Blocked, f.Advance(f.Command(quote)).Outcome);
        Assert.AreEqual(before, f.Read().Document.Content);
        Assert.IsNull(f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts);
    }

    [TestMethod]
    public void Purchase_debits_exact_quote_adds_one_expense_and_reopens()
    {
        using var f = new Fixture(5);
        var quote = f.Quote();
        Assert.AreEqual(30, quote.KarmaCost);
        Assert.AreEqual(5, quote.NaturalRating);
        var command = f.Command(quote);
        var result = f.Advance(command);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, result.Outcome, result.Error);
        Assert.AreEqual(70, result.Receipt!.KarmaAfter);
        Assert.AreEqual(-30, result.Receipt.ExpenseAmount);
        var cold = new FileWorkspaceStore(f.Directory);
        var service = new Sr6CareerAttributeAdvancementService(cold, f.Owner);
        var read = cold.Get(f.Id).Value!;
        Assert.AreEqual(read.ContentRevision, read.SavedRevision);
        Assert.AreEqual(quote.WorkspaceRevision + 1, read.ContentRevision);
        Assert.AreEqual(6, service.Quote(f.Owner.Capture(), f.Id, "Body").Quote!.NaturalRating);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, service.Advance(f.Owner.Capture(), command).Outcome);
        Assert.HasCount(1, XDocument.Parse(read.Document.Content).Root!.Element("expenses")!.Elements().ToArray());
        Assert.HasCount(1, read.Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
        Assert.AreEqual(70, int.Parse(XDocument.Parse(read.Document.Content).Root!.Element("karma")!.Value));
    }

    [TestMethod]
    public void Approved_adept_bonus_is_not_purchased_natural_rank()
    {
        using var f = new Fixture(5, bonus: 3);
        f.Edit(root => root.Element("attributes")!.Elements().Single(x => x.Element("sr6id")?.Value == "Body")
            .Element("totalvalue")!.Value = "8");
        var quote = f.Quote();
        Assert.AreEqual(5, quote.NaturalRating);
        Assert.AreEqual(3, quote.AugmentationBonus);
        Assert.AreEqual(8, quote.EffectiveRating);
        Assert.AreEqual(30, quote.KarmaCost);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(f.Command(quote)).Outcome);
        var after = f.Quote();
        Assert.AreEqual(6, after.NaturalRating);
        Assert.AreEqual(9, after.EffectiveRating);
        Assert.IsFalse(after.CanAdvance);
    }

    [TestMethod]
    public void Approved_exceptional_attribute_rebuilds_natural_maximum()
    {
        using var f = new Fixture(6, exceptional: true);
        var quote = f.Quote();
        Assert.AreEqual(7, quote.NaturalMaximum);
        Assert.AreEqual(35, quote.KarmaCost);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), "sr6_core_de_2024:p74");
        var result = f.Advance(f.Command(quote));
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, result.Outcome, result.Error);
        Assert.AreEqual(65, result.Receipt!.KarmaAfter);
    }

    [TestMethod]
    public void Unproven_raw_limit_or_quality_does_not_grant_authority()
    {
        using var f = new Fixture(6);
        f.Edit(root => root.Element("attributes")!.Elements().Single(x => x.Element("sr6id")?.Value == "Body")
            .Element("metatypemax")!.Value = "7");
        Assert.AreEqual(Sr6CareerAttributeOutcome.Unavailable, f.Service.Quote(f.Owner.Capture(), f.Id, "Body").Outcome);
    }

    [TestMethod]
    public void Insufficient_karma_keeps_cost_but_cannot_debit()
    {
        using var f = new Fixture(5, karma: 29);
        var quote = f.Quote();
        Assert.AreEqual(30, quote.KarmaCost);
        Assert.AreEqual("sr6_attribute_insufficient_karma", quote.Blocker);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Blocked, f.Advance(f.Command(quote)).Outcome);
        Assert.IsNull(f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts);
    }

    [TestMethod]
    public void Stale_workspace_rule_binding_and_foreign_owner_cannot_spend()
    {
        using var f = new Fixture(5);
        var quote = f.Quote();
        var changedRule = quote with { RuleDigest = Sr6CareerAttributeIntegrity.Digest("changed-rule") };
        changedRule = changedRule with { BindingDigest = Sr6CareerAttributeIntegrity.BindingDigest(changedRule) };
        Assert.AreEqual(Sr6CareerAttributeOutcome.Conflict, f.Advance(f.Command(changedRule)).Outcome);
        var foreign = quote with { OwnerId = "another-owner" };
        foreign = foreign with { BindingDigest = Sr6CareerAttributeIntegrity.BindingDigest(foreign) };
        Assert.AreEqual(Sr6CareerAttributeOutcome.Conflict, f.Advance(f.Command(foreign)).Outcome);
        f.Edit(root => root.Element("karma")!.Value = "99");
        Assert.AreEqual(Sr6CareerAttributeOutcome.Conflict, f.Advance(f.Command(quote)).Outcome);
        Assert.IsNull(f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts);
    }

    [TestMethod]
    public void Same_operation_with_changed_intent_is_reserved()
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        Assert.AreEqual(Sr6CareerAttributeOutcome.IdempotencyConflict,
            f.Advance(command with { ExpenseDateLocal = command.ExpenseDateLocal.AddDays(1) }).Outcome);
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Uncertain_replace_reconciles_same_identity_without_second_debit(bool afterReplace)
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        f.Fault.Stage = afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed;
        var first = f.Advance(command);
        // The existing atomic writer deliberately swallows a diagnostic hook failure
        // after rename because it already knows that the flushed record committed.
        Assert.AreEqual(afterReplace ? Sr6CareerAttributeOutcome.Applied : Sr6CareerAttributeOutcome.Unavailable, first.Outcome);
        f.Fault.Stage = null;
        Assert.AreEqual(afterReplace ? Sr6CareerAttributeOutcome.Replayed : Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
        Assert.AreEqual("70", XDocument.Parse(f.Read().Document.Content).Root!.Element("karma")!.Value);
    }

    [TestMethod]
    public void Lost_response_reopens_and_replays_original_operation()
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        // Simulate a transport dropping the response after the actual store operation.
        _ = f.Store.CommitSr6CareerAttribute(f.Owner.Capture().Owner, command);
        var cold = new Sr6CareerAttributeAdvancementService(new FileWorkspaceStore(f.Directory), f.Owner);
        var found = cold.Lookup(f.Owner.Capture(), command);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, found.Outcome, found.Error);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, cold.Advance(f.Owner.Capture(), command).Outcome);
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
        Assert.AreEqual("70", XDocument.Parse(f.Read().Document.Content).Root!.Element("karma")!.Value);
    }

    [TestMethod]
    public void Special_attribute_and_import_without_archive_are_unavailable()
    {
        using var f = new Fixture(5);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Unavailable, f.Service.Quote(f.Owner.Capture(), f.Id, "Edge").Outcome);
        var imported = f.Store.CreateWorkspaceDocument(new WorkspaceDocument("<character><gameedition>SR6</gameedition><created>True</created></character>", RulesetDefaults.Sr6));
        Assert.AreEqual(Sr6CareerAttributeOutcome.Unavailable,
            f.Service.Quote(f.Owner.Capture(), imported.Entry!.Value.Id, "Body").Outcome);
    }

    [TestMethod]
    public void Both_xml_views_disable_unproven_sr6_upgrade_and_keep_natural_allocation()
    {
        const string xml = "<character><gameedition>SR6</gameedition><created>True</created><karma>100</karma><attributes><attribute><name>BOD</name><base>5</base><karma>0</karma><totalvalue>8</totalvalue><metatypemax>6</metatypemax><metatypeaugmax>10</metatypeaugmax></attribute></attributes></character>";
        var service = new CharacterSectionService();
        var summary = service.ParseAttributes(xml).Attributes.Single();
        var detail = service.ParseAttributeDetails(xml).Attributes.Single();
        Assert.AreEqual(0, summary.KarmaValue);
        Assert.AreEqual(0, detail.KarmaValue);
        Assert.AreEqual(1, summary.KarmaMaximum);
        Assert.AreEqual(1, detail.KarmaMaximum);
        Assert.AreEqual(-1, summary.UpgradeKarmaCost);
        Assert.AreEqual(-1, detail.UpgradeKarmaCost);
        Assert.IsFalse(summary.CanCareerUpgrade);
        Assert.IsFalse(detail.CanCareerUpgrade);
    }

    [TestMethod]
    public void Competing_commands_use_one_atomic_revision()
    {
        using var f = new Fixture(5);
        var quote = f.Quote();
        var commands = new[] { f.Command(quote), f.Command(quote) };
        var results = new Sr6CareerAttributeResult[2];
        Parallel.For(0, 2, i => results[i] = f.Advance(commands[i]));
        Assert.AreEqual(1, results.Count(x => x.Outcome == Sr6CareerAttributeOutcome.Applied));
        Assert.AreEqual(1, results.Count(x => x.Outcome == Sr6CareerAttributeOutcome.Conflict));
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
        Assert.AreEqual("70", XDocument.Parse(f.Read().Document.Content).Root!.Element("karma")!.Value);
    }

    [TestMethod]
    public void Generic_auxiliary_writer_cannot_remove_durable_operation_identity()
    {
        using var f = new Fixture(5);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(f.Command(f.Quote())).Outcome);
        var saved = f.Read();
        var replacement = saved.Document with { State = saved.Document.State with
            { AuxiliaryState = saved.Document.AuxiliaryState with { Sr6CareerAttributeReceipts = null } } };
        var attempted = f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, saved.ContentRevision,
            saved.Document.AuxiliaryStateDigest, replacement);
        Assert.IsFalse(attempted.Success);
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
    }

    [TestMethod]
    public void Existing_sr5_xml_projection_is_unchanged()
    {
        const string xml = "<character><gameedition>SR5</gameedition><created>True</created><karma>100</karma><attributes><attribute><name>BOD</name><base>3</base><karma>1</karma><totalvalue>4</totalvalue><metatypemax>6</metatypemax><metatypeaugmax>10</metatypeaugmax></attribute></attributes></character>";
        var service = new CharacterSectionService();
        var summary = service.ParseAttributes(xml).Attributes.Single();
        var detail = service.ParseAttributeDetails(xml).Attributes.Single();
        Assert.AreEqual(25, summary.UpgradeKarmaCost);
        Assert.AreEqual(25, detail.UpgradeKarmaCost);
        Assert.IsTrue(summary.CanCareerUpgrade);
        Assert.IsTrue(detail.CanCareerUpgrade);
    }

    [TestMethod]
    [DataRow("attributes")]
    [DataRow("sr6creation")]
    [DataRow("sr6creation/passiveattributes")]
    [DataRow("sr6creation/derived")]
    [DataRow("karma")]
    [DataRow("physicalcm")]
    [DataRow("stuncm")]
    public void Missing_required_xml_returns_unavailable_without_mutation(string elementPath)
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        f.Edit(root =>
        {
            XElement element = root;
            foreach (string name in elementPath.Split('/')) element = element.Element(name)!;
            element.Remove();
        });
        byte[] before = File.ReadAllBytes(f.RecordPath);
        var saved = f.Read();
        var result = f.Service.Quote(f.Owner.Capture(), f.Id, "Body");
        Assert.AreEqual(Sr6CareerAttributeOutcome.Unavailable, result.Outcome, result.Error);
        Assert.AreEqual("sr6_attribute_required_xml_unavailable", result.Error);
        // A current-revision command must also fail during reconstruction, not throw.
        var quote = command.Quote with { WorkspaceRevision = saved.ContentRevision };
        quote = quote with { BindingDigest = Sr6CareerAttributeIntegrity.BindingDigest(quote) };
        var applied = f.Advance(command with { Quote = quote });
        Assert.AreEqual(Sr6CareerAttributeOutcome.Unavailable, applied.Outcome, applied.Error);
        Assert.IsNull(f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
    }

    [TestMethod]
    public void Receipt_fence_is_rejected_by_the_pre_patch_store_version_guard()
    {
        using var f = new Fixture(5);
        Assert.IsTrue(PrePatchVersionGuardAccepts(File.ReadAllText(f.RecordPath)));
        var command = f.Command(f.Quote());
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        string protectedRecord = File.ReadAllText(f.RecordPath);
        Assert.IsFalse(PrePatchVersionGuardAccepts(protectedRecord));
        // Demonstrate the old failure: the SAME receipt-bearing payload passes
        // the old version guard if written with the previous ordinary format.
        var downgraded = JsonNode.Parse(protectedRecord)!;
        downgraded["RecordSchemaVersion"] = 4;
        Assert.IsTrue(PrePatchVersionGuardAccepts(downgraded.ToJsonString()));
        // Version 5 is also understood by the old implementation (retained sources).
        downgraded["RecordSchemaVersion"] = 5;
        Assert.IsTrue(PrePatchVersionGuardAccepts(downgraded.ToJsonString()));
        var cold = new Sr6CareerAttributeAdvancementService(new FileWorkspaceStore(f.Directory), f.Owner);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, cold.Lookup(f.Owner.Capture(), command).Outcome);
    }

    [TestMethod]
    [DataRow("save")]
    [DataRow("replace")]
    [DataRow("checkpoint")]
    [DataRow("replace-checkpoint")]
    [DataRow("auxiliary-checkpoint")]
    public void Generic_writer_paths_keep_the_receipt_fence_and_replay(string writer)
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        var saved = f.Read();
        switch (writer)
        {
            case "save":
#pragma warning disable CS0618
                f.Store.Save(f.Id, saved.Document);
#pragma warning restore CS0618
                break;
            case "replace":
                Assert.IsTrue(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, saved.Document).Success);
                break;
            case "checkpoint":
                Assert.IsTrue(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, saved.Document).Success);
                Assert.IsTrue(f.Store.SaveCheckpoint(f.Id, f.Read().ContentRevision).Success);
                break;
            case "replace-checkpoint":
                Assert.IsTrue(f.Store.ReplaceWorkspaceDocumentAndCheckpoint(f.Id, saved.ContentRevision, saved.Document).Success);
                break;
            case "auxiliary-checkpoint":
                // This API requires an admitted auxiliary transition; a no-op
                // already fails closed in the pre-patch implementation.
                byte[] before = File.ReadAllBytes(f.RecordPath);
                var noOp = f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id,
                    saved.ContentRevision, saved.Document.AuxiliaryStateDigest, saved.Document);
                Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, noOp.Outcome, noOp.Error);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
                break;
        }
        Assert.IsFalse(PrePatchVersionGuardAccepts(File.ReadAllText(f.RecordPath)));
        Assert.HasCount(1, f.Read().Document.AuxiliaryState.Sr6CareerAttributeReceipts!);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, f.Service.Lookup(f.Owner.Capture(), command).Outcome);
    }

    [TestMethod]
    public void Receipt_backup_restore_and_import_overwrite_paths_fail_closed()
    {
        using var f = new Fixture(5);
        var prior = f.Read();
        var command = f.Command(f.Quote());
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        byte[] before = File.ReadAllBytes(f.RecordPath);
        // Existing-ID imports cannot replace a protected record with receipt-free XML.
        Assert.AreEqual(WorkspaceOperationOutcome.Conflict, f.Store.CreateWorkspaceDocument(f.Id,
            new WorkspaceDocument(prior.Document.Content, RulesetDefaults.Sr6)).Outcome);
        Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, f.Store.ReadContinuation(f.Id).Outcome);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Unavailable,
            f.Store.InspectRestoreTarget(OwnerScope.LocalSingleUser, f.Id).Outcome);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
        // A byte-preserving file backup keeps the fence and durable operation ID.
        string backup = Path.Combine(f.Directory, "backup");
        Directory.CreateDirectory(Path.Combine(backup, "workspaces"));
        string backupPath = Path.Combine(backup, "workspaces", f.Id.Value + ".json");
        File.Copy(f.RecordPath, backupPath);
        Assert.IsFalse(PrePatchVersionGuardAccepts(File.ReadAllText(backupPath)));
        var cold = new Sr6CareerAttributeAdvancementService(new FileWorkspaceStore(backup), f.Owner);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, cold.Lookup(f.Owner.Capture(), command).Outcome);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Atomic_writer_rejects_receipt_loss_or_rewrite_before_replacement(bool rewrite)
    {
        using var f = new Fixture(5);
        var command = f.Command(f.Quote());
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(command).Outcome);
        byte[] before = File.ReadAllBytes(f.RecordPath);
        var candidate = JsonNode.Parse(before)!;
        if (rewrite)
            candidate["AuxiliaryState"]!["Sr6CareerAttributeReceipts"]![0]!["KarmaAfter"] = 999;
        else
        {
            candidate["AuxiliaryState"]!.AsObject().Remove("Sr6CareerAttributeReceipts");
            candidate["RecordSchemaVersion"] = 4;
        }
        // Exercise the shared atomic writer directly, as a restore writer would,
        // independently of the public generic auxiliary-state admission checks.
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic;
        var recordType = typeof(FileWorkspaceStore).GetNestedType("PersistedWorkspaceRecord", flags)!;
        var dispositionType = typeof(FileWorkspaceStore).GetNestedType("WorkspaceWriteDisposition", flags)!;
        var writer = typeof(FileWorkspaceStore).GetMethod("WriteRecordAtomically", flags | System.Reflection.BindingFlags.Instance)!;
        var record = JsonSerializer.Deserialize(candidate.ToJsonString(), recordType)!;
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => writer.Invoke(f.Store,
            [f.RecordPath, record, Enum.Parse(dispositionType, "ReplaceExisting"), null, null]));
        Assert.IsInstanceOfType<IOException>(error.InnerException);
        Assert.AreEqual("SR6 advancement history cannot be removed or rewritten.", error.InnerException!.Message);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
        Assert.AreEqual(Sr6CareerAttributeOutcome.Replayed, f.Service.Lookup(f.Owner.Capture(), command).Outcome);
    }

    [TestMethod]
    [DataRow(5)]
    [DataRow(6)]
    public void Special_record_versions_require_their_protected_state(int version)
    {
        using var f = new Fixture(5);
        var record = JsonNode.Parse(File.ReadAllText(f.RecordPath))!;
        record["RecordSchemaVersion"] = version;
        File.WriteAllText(f.RecordPath, record.ToJsonString());
        byte[] before = File.ReadAllBytes(f.RecordPath);
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, f.Store.Get(f.Id).Outcome);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(4)]
    [DataRow(5)]
    [DataRow(7)]
    public void Unknown_or_downgraded_receipt_formats_are_not_rewritten(int version)
    {
        using var f = new Fixture(5);
        Assert.AreEqual(Sr6CareerAttributeOutcome.Applied, f.Advance(f.Command(f.Quote())).Outcome);
        var saved = f.Read();
        var record = JsonNode.Parse(File.ReadAllText(f.RecordPath))!;
        record["RecordSchemaVersion"] = version;
        File.WriteAllText(f.RecordPath, record.ToJsonString());
        byte[] before = File.ReadAllBytes(f.RecordPath);
        Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, f.Store.Get(f.Id).Outcome);
        Assert.IsFalse(f.Store.SaveCheckpoint(f.Id, saved.ContentRevision).Success);
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, saved.Document).Success);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(f.RecordPath));
    }

    [TestMethod]
    [DataRow("sr5")]
    [DataRow("sr6")]
    public void Receipt_free_records_keep_the_old_format_and_legacy_migration(string ruleset)
    {
        string directory = Path.Combine(Path.GetTempPath(), "sr6-format-control-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileWorkspaceStore(directory);
            var created = store.CreateWorkspaceDocument(new WorkspaceDocument("<character><name>Control</name></character>", ruleset));
            Assert.IsTrue(created.Success, created.Error);
            var id = created.Entry!.Value.Id;
            string path = Path.Combine(directory, "workspaces", id.Value + ".json");
            Assert.IsTrue(PrePatchVersionGuardAccepts(File.ReadAllText(path)));
            Assert.AreEqual(4, JsonNode.Parse(File.ReadAllText(path))!["RecordSchemaVersion"]!.GetValue<int>());
            var legacy = JsonNode.Parse(File.ReadAllText(path))!;
            legacy["RecordSchemaVersion"] = 2;
            legacy.AsObject().Remove("LocalHistory");
            legacy.AsObject().Remove("DelegatedGmHistorySegmentStarts");
            File.WriteAllText(path, legacy.ToJsonString());
            var migrated = store.Get(id);
            Assert.IsTrue(migrated.Success, migrated.Error);
            Assert.IsTrue(store.SaveCheckpoint(id, migrated.Value!.ContentRevision).Success);
            Assert.IsTrue(PrePatchVersionGuardAccepts(File.ReadAllText(path)));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    // Frozen exact version predicate from base 52168e1d74663f968366134f9c52dbd1ab069993:
    // FileWorkspaceStore.cs / TryMaterializeRecord; upper bound from
    // FileWorkspaceStore.RetainedRuleSources.cs. This intentionally does not use
    // the new production constant. The audit verifies these against git show.
    private static bool PrePatchVersionGuardAccepts(string json)
    {
        const int RetainedWorkspaceRecordSchemaVersion = 5;
        var record = JsonSerializer.Deserialize<PrePatchVersionRecord>(json)!;
        if (record.RecordSchemaVersion is int recordSchemaVersion
            && (recordSchemaVersion < 1 || recordSchemaVersion > RetainedWorkspaceRecordSchemaVersion))
            return false;
        return true;
    }

    private sealed record PrePatchVersionRecord(int? RecordSchemaVersion);

    private sealed class Fault : IFileWorkspaceStoreFaultInjector
    {
        public FileWorkspaceStoreFaultStage? Stage { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        { if (stage == Stage) throw new IOException("Injected natural advancement persistence failure"); }
    }

    private sealed class RejectSr5 : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml) => throw new AssertFailedException("SR5 resolver used for SR6");
    }

    private sealed class Fixture : IDisposable
    {
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "sr6-natural-" + Guid.NewGuid().ToString("N"));
        public string RecordPath => Path.Combine(Directory, "workspaces", Id.Value + ".json");
        public FileWorkspaceStore Store { get; }
        public Fault Fault { get; } = new();
        public LocalOwnerContextAccessor Owner { get; } = new();
        public CharacterWorkspaceId Id { get; }
        public Sr6CareerAttributeAdvancementService Service { get; }

        public Fixture(int body, int karma = 100, int bonus = 0, bool exceptional = false)
        {
            Store = new(Directory, Fault);
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var codec = new Sr6WorkspaceCodec(queries, new XmlCharacterSectionQueries(new CharacterSectionService()),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var bootstrap = new CharacterCreationBootstrapService(Store, new RulesetWorkspaceCodecResolver([codec]), queries,
                new RejectSr5(), rulesetBootstrapProviders: [new Sr6CharacterCreationBootstrapProvider()]);
            Sr6CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId("Priority", out var profile);
            var created = new OwnerBoundCharacterCreationBootstrapService(bootstrap, Owner).Create(Owner.Capture(),
                new(CharacterCreationBootstrapSchemas.RequestV1, CharacterCreationBootstrapStages.AwaitingFoundationSelection,
                    RulesetDefaults.Sr6, "Natural advancement", "Test", "Priority", profile));
            Assert.IsNotNull(created.Value, string.Join(",", created.Blockers));
            Id = created.Value.WorkspaceId;
            var creation = new Sr6CreationFoundationService(Store, Owner);
            var binding = creation.Load(Owner.Capture(), Id).Value!.Binding;
            var selection = new Sr6CreationFoundationSelection("human", bonus > 0 ? "adept" : "mundane",
                [new(CharacterCreationPriorityCategoryIds.Heritage, bonus > 0 ? "E" : "D"),
                 new(CharacterCreationPriorityCategoryIds.Talent, bonus > 0 ? "B" : "E"),
                 new(CharacterCreationPriorityCategoryIds.Attributes, "A"),
                 new(CharacterCreationPriorityCategoryIds.Skills, bonus > 0 ? "C" : "B"),
                 new(CharacterCreationPriorityCategoryIds.Resources, bonus > 0 ? "D" : "C")])
            {
                Attributes = new(Sr6CreationAttributeIds.Ordered.Select(id => new Sr6CreationAttributeSpend(id, id == "Body" ? body - 1 : 0, 0)).ToArray()),
                Skills = new([new("Athletics", 1, [])]), Karma = new([], [], 0),
                Knowledge = new("German", [new(Guid.NewGuid(), "Seattle")], []), Contacts = new([]), Gear = new([]), Lifestyle = new("street", 1),
                Qualities = exceptional ? new(["exceptional-Body"]) : null,
                TalentAllocation = bonus > 0 ? new(0) : null,
                AdeptPowers = bonus > 0 ? new([new("improved-attribute-body", bonus)]) : null
            };
            var preview = creation.Preview(Owner.Capture(), binding, selection);
            Assert.IsNotNull(preview.Value, string.Join(",", preview.Blockers));
            var confirmed = creation.Confirm(Owner.Capture(), new(binding, selection, preview.Value.PreviewDigest, Guid.NewGuid(), true));
            Assert.IsNotNull(confirmed.Value, string.Join(",", confirmed.Blockers));
            binding = creation.Load(Owner.Capture(), Id).Value!.Binding;
            var review = creation.ReviewFinalization(Owner.Capture(), binding);
            Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
            Assert.IsTrue(review.Value.CanFinalize, string.Join(",", review.Value.Blockers));
            var finalized = creation.ConfirmFinalization(Owner.Capture(), new(binding, review.Value.ReviewDigest, Guid.NewGuid(), true, true));
            Assert.IsNotNull(finalized.Value, string.Join(",", finalized.Blockers));
            Edit(root => root.Element("karma")!.Value = karma.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Service = new(Store, Owner);
        }

        public WorkspaceStoredDocument Read() => Store.Get(Id).Value!;
        public void Edit(Action<XElement> edit)
        {
            var saved = Read();
            var root = XDocument.Parse(saved.Document.Content).Root!;
            edit(root);
            var result = Store.ReplaceWorkspaceDocumentAndCheckpoint(Id, saved.ContentRevision,
                saved.Document with { State = saved.Document.State with { Payload = root.ToString(SaveOptions.DisableFormatting) } });
            Assert.IsTrue(result.Success, result.Error);
        }
        public Sr6CareerAttributeQuote Quote()
        {
            var result = Service.Quote(Owner.Capture(), Id, "Body");
            Assert.AreEqual(Sr6CareerAttributeOutcome.Available, result.Outcome, result.Error);
            return result.Quote!;
        }
        public Sr6CareerAttributeCommand Command(Sr6CareerAttributeQuote quote) => new(quote, Guid.NewGuid(), new DateTime(2080, 1, 2), true);
        public Sr6CareerAttributeResult Advance(Sr6CareerAttributeCommand command) => Service.Advance(Owner.Capture(), command);
        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
