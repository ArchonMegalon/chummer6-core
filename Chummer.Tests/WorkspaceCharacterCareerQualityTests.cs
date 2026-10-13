using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceCharacterCareerQualityTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Quality_character_expense_and_receipt_commit_together_and_cold_replay_once(bool linked)
    {
        using var f = new Fixture(linked: linked);
        var plan = f.Plan();
        var result = f.Service.Commit(f.Owners.Capture(), plan);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, result.Outcome, result.Error);
        var saved = f.Saved();
        Assert.AreEqual(2L, saved.ContentRevision);
        Assert.AreEqual(2L, saved.SavedRevision);
        var xml = XElement.Parse(saved.Document.Content);
        Assert.AreEqual("92", xml.Element("karma")!.Value);
        Assert.HasCount(1, xml.Element("qualities")!.Elements().ToArray());
        Assert.HasCount(1, xml.Element("improvements")!.Elements().ToArray());
        Assert.HasCount(1, xml.Element("expenses")!.Elements().ToArray());
        Assert.AreEqual("keep me", xml.Element("notes")!.Value);
        Assert.HasCount(1, saved.Document.AuxiliaryState.CharacterCareerQualityTransactions!);
        string before = JsonSerializer.Serialize(saved);
        int opens = f.Authority.OpenCount;
        f.Authority.Current = false;
        var cold = new OwnerBoundCharacterCareerQualityService(new FileWorkspaceStore(f.Path), f.Owners, f.Authority);
        var replay = cold.Commit(f.Owners.Capture(), plan);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, replay.Outcome, replay.Error);
        Assert.AreEqual(JsonSerializer.Serialize(result.Entry), JsonSerializer.Serialize(replay.Entry));
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(opens, f.Authority.OpenCount, "A durable replay must not reopen mutable source authority.");
        Assert.AreEqual(0, f.Owners.ActiveLeases);
        Assert.AreEqual(f.Authority.OpenCount, f.Authority.DisposeCount);
        if (linked) Assert.IsFalse(new FileWorkspaceStore(f.Path).Get(f.Id).Success);
    }

    [TestMethod]
    [DataRow(false, CharacterCareerQualityOperation.RemoveLevel)]
    [DataRow(false, CharacterCareerQualityOperation.RemoveAllLevels)]
    [DataRow(true, CharacterCareerQualityOperation.RemoveLevel)]
    [DataRow(true, CharacterCareerQualityOperation.RemoveAllLevels)]
    public void Removal_retains_exact_removed_fragments_and_history_without_replaying_an_old_acquisition(
        bool negative, CharacterCareerQualityOperation operation)
    {
        using var f = new Fixture(negative: negative);
        var first = f.Plan(instance: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(first).Outcome);
        var second = f.Plan(instance: Guid.Parse("11111111-1111-1111-1111-111111111111"));
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(second).Outcome);
        var before = XElement.Parse(f.Saved().Document.Content);
        var removal = f.Plan(operation, first.Identity.InternalId);
        var result = f.Commit(removal);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, result.Outcome, result.Error);
        int count = operation == CharacterCareerQualityOperation.RemoveLevel ? 1 : 2;
        Assert.HasCount(count * 2, result.Entry!.RemovedNodes);
        foreach (var fragment in result.Entry.RemovedNodes)
            Assert.IsTrue(XNode.DeepEquals(before.Element(fragment.Container)!.Elements().ElementAt(fragment.Index),
                XElement.Parse(fragment.Xml)));
        var saved = f.Saved();
        var xml = XElement.Parse(saved.Document.Content);
        Assert.HasCount(2 - count, xml.Element("qualities")!.Elements().ToArray());
        Assert.HasCount(2 - count, xml.Element("improvements")!.Elements().ToArray());
        Assert.AreEqual(negative ? (100 - 8 * count).ToString() : "84", xml.Element("karma")!.Value);
        Assert.AreEqual("keep me", xml.Element("notes")!.Value);
        Assert.HasCount(3, saved.Document.AuxiliaryState.CharacterCareerQualityTransactions!);
        string unchanged = JsonSerializer.Serialize(saved);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, f.Commit(first).Outcome);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, f.Commit(removal).Outcome);
        Assert.AreEqual(unchanged, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Source_authority_drift_at_replace_boundary_leaves_no_partial_mutation()
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        var plan = f.Plan();
        string before = JsonSerializer.Serialize(f.Saved());
        fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) f.Authority.Current = false; };
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Unavailable, f.Commit(plan).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
        Assert.HasCount(0, Directory.GetFiles(f.Path, "*.tmp.*", SearchOption.AllDirectories));
        Assert.AreEqual(f.Authority.OpenCount, f.Authority.DisposeCount);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Cancellation_at_replace_boundary_reconciles_actual_durable_outcome(bool afterReplace)
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        using var cancellation = new CancellationTokenSource();
        var plan = f.Plan();
        fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                cancellation.Cancel();
        };
        var result = f.Service.Commit(f.Owners.Capture(), plan, cancellation.Token);
        Assert.AreEqual(afterReplace ? CharacterCareerQualityCommitOutcome.Applied : CharacterCareerQualityCommitOutcome.Unavailable,
            result.Outcome, result.Error);
        Assert.AreEqual(afterReplace ? 2L : 1L, f.Saved().ContentRevision);
        fault.Action = null;
        Assert.AreEqual(afterReplace ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Applied,
            f.Commit(plan).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Diagnostic_hook_failure_after_replace_does_not_undo_a_known_commit()
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        var plan = f.Plan();
        fault.Action = stage => { if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) throw new IOException("synthetic lost response"); };
        var result = f.Commit(plan);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, result.Outcome, result.Error);
        fault.Action = null;
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, f.Commit(plan).Outcome);
        Assert.HasCount(1, f.Saved().Document.AuxiliaryState.CharacterCareerQualityTransactions!);
        Assert.AreEqual("92", XElement.Parse(f.Saved().Document.Content).Element("karma")!.Value);
    }

    [TestMethod]
    public void Concurrent_identical_commits_share_the_workspace_lease_and_competing_revision_conflicts()
    {
        using var f = new Fixture();
        var first = f.Plan();
        var other = f.Plan();
        var cold = new FileWorkspaceStore(f.Path);
        // These direct store calls test the partition writer's shared disk lease;
        // production entry is the separately tested owner-bound service.
        var results = Task.WhenAll(Task.Run(() => f.Store.CommitCareerQuality(f.Owner, first, f.Authority)),
            Task.Run(() => cold.CommitCareerQuality(f.Owner, first, f.Authority))).GetAwaiter().GetResult();
        Assert.AreEqual(1, results.Count(row => row.Outcome == CharacterCareerQualityCommitOutcome.Applied));
        Assert.AreEqual(1, results.Count(row => row.Outcome == CharacterCareerQualityCommitOutcome.Replayed));
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Commit(other).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    [DataRow("aba")]
    [DataRow("foreign")]
    [DataRow("forged-local")]
    [DataRow("authority")]
    public void Wrong_owner_or_transition_is_rejected_before_source_admission(string kind)
    {
        using var f = new Fixture();
        var plan = f.Plan();
        var stamp = f.Owners.Capture();
        switch (kind)
        {
            case "aba": f.Owners.Set(new("other-account")); f.Owners.Set(f.Owner); break;
            case "foreign": stamp = stamp with { Owner = new("other-account") }; break;
            case "forged-local": stamp = stamp with { Owner = new(f.Owner.Value) }; break;
            case "authority": stamp = stamp with { AuthorityInstanceId = Guid.NewGuid().ToString("N") }; break;
        }
        string before = JsonSerializer.Serialize(f.Saved());
        int opens = f.Authority.OpenCount;
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Service.Commit(stamp, plan).Outcome);
        Assert.AreEqual(opens, f.Authority.OpenCount);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(0, f.Owners.ActiveLeases);
    }

    [TestMethod]
    public void Generic_auxiliary_writer_cannot_create_remove_or_replace_quality_history()
    {
        using var f = new Fixture();
        var plan = f.Plan();
        var before = f.Saved();
        using var admission = f.Authority.Open(f.Owner, before, plan.Identity, plan.Operation, f.Store.CareerQualityRuntimeFingerprint);
        Assert.IsTrue(CharacterCareerQualityTransaction.TryBuild(f.Owner, before, plan, admission!,
            f.Store.CareerQualityRuntimeFingerprint, out var replacement, out _));
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, 1,
            before.Document.AuxiliaryStateDigest, replacement!).Success);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(plan).Outcome);
        var committed = f.Saved();
        var stripped = committed.Document with { State = committed.Document.State with
        { AuxiliaryState = committed.Document.AuxiliaryState with { CharacterCareerQualityTransactions = null } } };
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, 2,
            committed.Document.AuxiliaryStateDigest, stripped).Success);
        Assert.AreEqual(JsonSerializer.Serialize(committed), JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Imported_history_cannot_replay_a_locally_addressed_quality_transaction()
    {
        using var f = new Fixture();
        var plan = f.Plan();
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(plan).Outcome);
        WorkspaceImportedHistoryTestFixture.MarkImported(f.Path, f.Id);
        string before = JsonSerializer.Serialize(f.Saved());
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.IdempotencyConflict, f.Commit(plan).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Unchanged_auxiliary_state_does_not_gain_a_serialized_null_quality_ledger()
    {
        using var f = new Fixture();
        Assert.IsNull(f.Saved().Document.AuxiliaryState.CharacterCareerQualityTransactions);
        Assert.DoesNotContain(JsonSerializer.Serialize(f.Saved().Document.AuxiliaryState), "CharacterCareerQualityTransactions");
        Assert.DoesNotContain(JsonSerializer.Serialize(f.Saved().Document.AuxiliaryState), "CharacterCareerQualityCorrections");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Durable_readback_rejects_simulated_success_and_recovers_a_lost_adapter_response(bool actuallyWritten)
    {
        using var f = new Fixture();
        var plan = f.Plan();
        var adapter = new ObservingStore(f.Store) { SimulateUnwrittenSuccess = !actuallyWritten, LoseResponse = actuallyWritten };
        var service = new OwnerBoundCharacterCareerQualityService(adapter, f.Owners, f.Authority);
        var result = service.Commit(f.Owners.Capture(), plan);
        Assert.AreEqual(actuallyWritten ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Unavailable,
            result.Outcome, result.Error);
        Assert.AreEqual(actuallyWritten ? 2L : 1L, f.Saved().ContentRevision);
        if (!actuallyWritten) Assert.IsNull(result.Entry);
        adapter.SimulateUnwrittenSuccess = adapter.LoseResponse = false;
        Assert.AreEqual(actuallyWritten ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Applied,
            service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.HasCount(1, f.Saved().Document.AuxiliaryState.CharacterCareerQualityTransactions!);
    }

    [TestMethod]
    public void Identical_workspace_id_in_two_accounts_never_changes_the_other_partition()
    {
        using var f = new Fixture(linked: true);
        var other = new OwnerScope("another-synthetic-owner");
        Assert.IsTrue(f.Store.CreateWorkspaceDocument(other, f.Id, f.Saved().Document).Success);
        Assert.IsTrue(f.Store.SaveCheckpoint(other, f.Id, 1).Success);
        var before = JsonSerializer.Serialize(f.Store.Get(other, f.Id).Value);
        var plan = f.Plan();
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(plan).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(f.Path).Get(other, f.Id).Value));
        f.Owners.Set(other);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Commit(plan).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(new FileWorkspaceStore(f.Path).Get(other, f.Id).Value));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("identity")]
    [DataRow("transaction")]
    [DataRow("runtime")]
    public void Invalid_plan_never_reaches_atomic_writer(string damage)
    {
        using var f = new Fixture();
        var plan = f.Plan();
        plan = damage switch
        {
            "null" => null!,
            "identity" => plan with { Identity = null! },
            "transaction" => plan with { TransactionId = Guid.Empty },
            "runtime" => plan with { ExpectedRuntimeFingerprint = "not-a-digest" },
            _ => throw new InvalidOperationException()
        };
        var adapter = new ObservingStore(f.Store);
        var service = new OwnerBoundCharacterCareerQualityService(adapter, f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, service.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(0, adapter.Commits);
        Assert.AreEqual(0, adapter.Reads);
    }

    [TestMethod]
    public void Structurally_valid_forged_cost_is_rejected_by_fresh_authority_before_any_write()
    {
        using var f = new Fixture();
        var plan = f.Plan() with { SavedCharacterKarma = 100 };
        Assert.IsTrue(CharacterCareerQualityTransaction.IsValidPlan(plan), "Shape alone is not cost authority.");
        string before = JsonSerializer.Serialize(f.Saved());
        int opens = f.Authority.OpenCount;
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Commit(plan).Outcome);
        Assert.IsGreaterThan(opens, f.Authority.OpenCount);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    [DataRow("payload")]
    [DataRow("receipt")]
    [DataRow("chain")]
    public void Corrupt_cold_state_cannot_replay_a_quality_commit(string damage)
    {
        using var f = new Fixture();
        var plan = f.Plan();
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(plan).Outcome);
        var saved = f.Saved();
        var entry = saved.Document.AuxiliaryState.CharacterCareerQualityTransactions!.Single();
        var document = saved.Document;
        if (damage == "payload") document = document with { State = document.State with
        { Payload = document.Content.Replace("<karma>92</karma>", "<karma>100</karma>", StringComparison.Ordinal) } };
        else
        {
            entry = damage == "receipt" ? entry with { Receipt = entry.Receipt with { CharacterKarmaAfter = 100 } }
                : entry with { PreviousEntryDigest = new('a', 64) };
            entry = entry with { EntryDigest = CharacterCareerQualityTransaction.EntryDigest(entry) };
            document = document with { State = document.State with
            { AuxiliaryState = document.AuxiliaryState with { CharacterCareerQualityTransactions = [entry] } } };
        }
        // Private disk fault injection, never a production mutation API.
        string path = Directory.GetFiles(f.Path, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["Envelope"] = JsonSerializer.SerializeToNode(document.PayloadEnvelope);
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(document.AuxiliaryState);
        File.WriteAllText(path, record.ToJsonString());
        string corrupted = File.ReadAllText(path);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Corrupt, f.Commit(plan).Outcome);
        Assert.AreEqual(corrupted, File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow(false, CharacterCareerQualityOperation.AcquireLevel)]
    [DataRow(true, CharacterCareerQualityOperation.AcquireLevel)]
    [DataRow(false, CharacterCareerQualityOperation.RemoveLevel)]
    [DataRow(true, CharacterCareerQualityOperation.RemoveLevel)]
    [DataRow(false, CharacterCareerQualityOperation.RemoveAllLevels)]
    [DataRow(true, CharacterCareerQualityOperation.RemoveAllLevels)]
    public void Correction_restores_exact_saved_nodes_notes_and_first_level_ownership_and_cold_replays_once(
        bool negative, CharacterCareerQualityOperation operation)
    {
        using var f = new Fixture(negative: negative, firstLevel: true);
        var first = f.Plan(instance: Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"));
        if (operation != CharacterCareerQualityOperation.AcquireLevel)
        {
            Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(first).Outcome);
            Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(f.Plan()).Outcome);
            var saved = f.Saved();
            var xml = XElement.Parse(saved.Document.Content);
            xml.Element("qualities")!.Elements().First().SetElementValue("notes", "  preserved personal note é😀  ");
            var annotated = saved.Document with { State = saved.Document.State with { Payload = xml.ToString() } };
            Assert.IsTrue(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, annotated).Success);
            Assert.IsTrue(f.Store.SaveCheckpoint(f.Id, saved.ContentRevision + 1).Success);
        }
        var before = f.Saved();
        var plan = operation == CharacterCareerQualityOperation.AcquireLevel ? first : f.Plan(operation, first.Identity.InternalId);
        var result = f.Commit(plan);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, result.Outcome, result.Error);
        var post = f.Saved();
        var correction = Correction(post, result.Entry!);
        Assert.IsTrue(CharacterCareerQualityGraph.TryApplyCorrection(post.Document.Content, result.Entry!, correction,
            out var payload, out var restored, out var expense));
        Assert.IsTrue(XNode.DeepEquals(XElement.Parse(before.Document.Content), XElement.Parse(payload!)));
        Assert.AreEqual(result.Entry!.Receipt.CharacterKarmaBefore, restored!.AvailableKarma);
        Assert.AreEqual(0, expense!.MatchingEntryCount);
        Assert.AreEqual(correction.TargetWorkspaceRevision, restored.Binding.WorkspaceRevision);
        Assert.AreEqual(JsonSerializer.Serialize(post), JsonSerializer.Serialize(f.Saved()), "Pure inverse must not claim persistence.");
        int admissions = f.Authority.OpenCount;
        f.Authority.Current = false;
        var applied = f.Service.Correct(f.Owners.Capture(), correction);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, applied.Outcome, applied.Error);
        var durable = f.Saved();
        Assert.AreEqual(correction.TargetWorkspaceRevision, durable.ContentRevision);
        Assert.AreEqual(durable.ContentRevision, durable.SavedRevision);
        Assert.IsTrue(XNode.DeepEquals(XElement.Parse(before.Document.Content), XElement.Parse(durable.Document.Content)));
        Assert.AreEqual(JsonSerializer.Serialize(post.Document.AuxiliaryState.CharacterCareerQualityTransactions),
            JsonSerializer.Serialize(durable.Document.AuxiliaryState.CharacterCareerQualityTransactions));
        Assert.HasCount(1, durable.Document.AuxiliaryState.CharacterCareerQualityCorrections!);
        var cold = new OwnerBoundCharacterCareerQualityService(new FileWorkspaceStore(f.Path), f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, cold.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, cold.Commit(f.Owners.Capture(), plan).Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(durable), JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(admissions, f.Authority.OpenCount, "Undo must use the retained transaction, not catalog defaults.");
        Assert.AreEqual(0, f.Owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Correction_cancellation_reconciles_actual_replace_outcome(bool afterReplace)
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        using var cancellation = new CancellationTokenSource();
        fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                cancellation.Cancel();
        };
        var observed = f.Service.Correct(f.Owners.Capture(), correction, cancellation.Token);
        Assert.AreEqual(afterReplace ? CharacterCareerQualityCommitOutcome.Applied : CharacterCareerQualityCommitOutcome.Unavailable,
            observed.Outcome, observed.Error);
        Assert.AreEqual(afterReplace ? 3L : 2L, f.Saved().ContentRevision);
        fault.Action = null;
        Assert.AreEqual(afterReplace ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Applied,
            f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(3L, f.Saved().ContentRevision);
        Assert.HasCount(1, f.Saved().Document.AuxiliaryState.CharacterCareerQualityCorrections!);
        Assert.HasCount(0, Directory.GetFiles(f.Path, "*.tmp.*", SearchOption.AllDirectories));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Correction_readback_rejects_simulated_success_and_recovers_a_lost_adapter_response(bool actuallyWritten)
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        var adapter = new ObservingStore(f.Store) { SimulateUnwrittenSuccess = !actuallyWritten, LoseResponse = actuallyWritten };
        var service = new OwnerBoundCharacterCareerQualityService(adapter, f.Owners, f.Authority);
        var observed = service.Correct(f.Owners.Capture(), correction);
        Assert.AreEqual(actuallyWritten ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Unavailable,
            observed.Outcome, observed.Error);
        Assert.AreEqual(actuallyWritten ? 3L : 2L, f.Saved().ContentRevision);
        if (!actuallyWritten) Assert.IsNull(observed.Entry);
        adapter.SimulateUnwrittenSuccess = adapter.LoseResponse = false;
        Assert.AreEqual(actuallyWritten ? CharacterCareerQualityCommitOutcome.Replayed : CharacterCareerQualityCommitOutcome.Applied,
            service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.HasCount(1, f.Saved().Document.AuxiliaryState.CharacterCareerQualityCorrections!);
    }

    [TestMethod]
    public void Correction_owner_transition_and_identical_foreign_workspace_cannot_cross_partitions()
    {
        using var f = new Fixture(linked: true);
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        var other = new OwnerScope("other-synthetic-owner");
        Assert.IsTrue(f.Store.CreateWorkspaceDocument(other, f.Id, new WorkspaceDocument(
            "<character><notes>untouched other account</notes></character>", "sr5")).Success);
        Assert.IsTrue(f.Store.SaveCheckpoint(other, f.Id, 1).Success);
        string foreign = JsonSerializer.Serialize(f.Store.Get(other, f.Id).Value);
        var stamp = f.Owners.Capture();
        f.Owners.Set(other);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        f.Owners.Set(f.Owner);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Service.Correct(stamp, correction).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(foreign, JsonSerializer.Serialize(f.Store.Get(other, f.Id).Value));
        Assert.IsFalse(f.Store.Get(f.Id).Success);
        Assert.AreEqual(0, f.Owners.ActiveLeases);
    }

    [TestMethod]
    public void Generic_writer_cannot_forge_or_drop_a_correction_and_old_correction_cannot_change_newer_state()
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var before = f.Saved();
        var correction = Correction(before, result.Entry!);
        var competing = Correction(before, result.Entry!);
        Assert.IsTrue(CharacterCareerQualityTransaction.TryBuildCorrection(f.Owner, before, correction,
            f.Store.CareerQualityRuntimeFingerprint, out var replacement, out _));
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, before.ContentRevision,
            before.Document.AuxiliaryStateDigest, replacement!).Success);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.IdempotencyConflict, f.Service.Correct(f.Owners.Capture(), competing).Outcome);
        var corrected = f.Saved();
        var stripped = corrected.Document with { State = corrected.Document.State with
        { AuxiliaryState = corrected.Document.AuxiliaryState with { CharacterCareerQualityCorrections = null } } };
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, corrected.ContentRevision,
            corrected.Document.AuxiliaryStateDigest, stripped).Success);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(f.Plan()).Outcome);
        var newer = f.Saved();
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Replayed, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(newer), JsonSerializer.Serialize(f.Saved()));
        var second = Correction(newer, newer.Document.AuxiliaryState.CharacterCareerQualityTransactions![^1]);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Service.Correct(f.Owners.Capture(), second).Outcome);
        Assert.HasCount(2, f.Saved().Document.AuxiliaryState.CharacterCareerQualityCorrections!);
        Assert.AreEqual(5L, f.Saved().ContentRevision);
        Assert.AreEqual("100", XElement.Parse(f.Saved().Document.Content).Element("karma")!.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Imported_history_cannot_admit_or_replay_a_local_correction(bool alreadyCorrected)
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        if (alreadyCorrected)
            Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        WorkspaceImportedHistoryTestFixture.MarkImported(f.Path, f.Id);
        string before = JsonSerializer.Serialize(f.Saved());
        Assert.AreEqual(alreadyCorrected ? CharacterCareerQualityCommitOutcome.IdempotencyConflict : CharacterCareerQualityCommitOutcome.Conflict,
            f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Correction_cannot_rewind_a_later_character_edit()
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var saved = f.Saved();
        var correction = Correction(saved, result.Entry!);
        var edit = saved.Document with { State = saved.Document.State with
        { Payload = saved.Document.Content.Replace("keep me", "keep later change", StringComparison.Ordinal) } };
        Assert.IsTrue(f.Store.ReplaceWorkspaceDocument(f.Id, saved.ContentRevision, edit).Success);
        Assert.IsTrue(f.Store.SaveCheckpoint(f.Id, saved.ContentRevision + 1).Success);
        string before = JsonSerializer.Serialize(f.Saved());
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Concurrent_corrections_claim_one_identity_and_cannot_repeat_the_refund()
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        var competing = Correction(f.Saved(), result.Entry!);
        var cold = new FileWorkspaceStore(f.Path);
        var results = Task.WhenAll(Task.Run(() => f.Store.CorrectCareerQuality(f.Owner, correction)),
            Task.Run(() => cold.CorrectCareerQuality(f.Owner, correction))).GetAwaiter().GetResult();
        Assert.AreEqual(1, results.Count(row => row.Outcome == CharacterCareerQualityCommitOutcome.Applied));
        Assert.AreEqual(1, results.Count(row => row.Outcome == CharacterCareerQualityCommitOutcome.Replayed));
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.IdempotencyConflict, f.Service.Correct(f.Owners.Capture(), competing).Outcome);
        Assert.AreEqual(3L, f.Saved().ContentRevision);
        Assert.AreEqual("100", XElement.Parse(f.Saved().Document.Content).Element("karma")!.Value);
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("identity")]
    [DataRow("karma")]
    [DataRow("runtime")]
    public void Invalid_correction_never_reaches_the_atomic_writer(string damage)
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        correction = damage switch
        {
            "null" => null!,
            "identity" => correction with { Identity = null! },
            "karma" => correction with { SavedCharacterKarma = 101 },
            "runtime" => correction with { ExpectedRuntimeFingerprint = "not-a-digest" },
            _ => throw new InvalidOperationException()
        };
        var adapter = new ObservingStore(f.Store);
        var service = new OwnerBoundCharacterCareerQualityService(adapter, f.Owners, f.Authority);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Conflict, service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(0, adapter.Commits);
        Assert.AreEqual(0, adapter.Reads);
    }

    [TestMethod]
    [DataRow("payload")]
    [DataRow("chain")]
    [DataRow("missing-original")]
    public void Corrupt_correction_cold_state_fails_closed_without_replay(string damage)
    {
        using var f = new Fixture();
        var result = f.Commit(f.Plan());
        var correction = Correction(f.Saved(), result.Entry!);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        var document = f.Saved().Document;
        if (damage == "payload") document = document with { State = document.State with
        { Payload = document.Content.Replace("<karma>100</karma>", "<karma>101</karma>", StringComparison.Ordinal) } };
        else if (damage == "missing-original") document = document with { State = document.State with
        { AuxiliaryState = document.AuxiliaryState with { CharacterCareerQualityTransactions = null } } };
        else
        {
            var entry = document.AuxiliaryState.CharacterCareerQualityCorrections!.Single() with { PreviousEntryDigest = new('a', 64) };
            entry = entry with { EntryDigest = CharacterCareerQualityTransaction.CorrectionEntryDigest(entry) };
            document = document with { State = document.State with
            { AuxiliaryState = document.AuxiliaryState with { CharacterCareerQualityCorrections = [entry] } } };
        }
        string path = Directory.GetFiles(f.Path, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
        var record = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        record["Envelope"] = JsonSerializer.SerializeToNode(document.PayloadEnvelope);
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(document.AuxiliaryState);
        File.WriteAllText(path, record.ToJsonString());
        string corrupted = File.ReadAllText(path);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Corrupt, f.Service.Correct(f.Owners.Capture(), correction).Outcome);
        Assert.AreEqual(corrupted, File.ReadAllText(path));
    }

    [TestMethod]
    [DataRow("state")]
    [DataRow("wrong-id")]
    [DataRow("malformed-fragment")]
    [DataRow("wrong-container")]
    [DataRow("wrong-position")]
    [DataRow("missing-fragment")]
    public void Correction_graph_rejects_changed_state_or_invalid_retained_fragments(string damage)
    {
        using var f = new Fixture();
        var first = f.Plan();
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, f.Commit(first).Outcome);
        var removal = f.Plan(CharacterCareerQualityOperation.RemoveLevel, first.Identity.InternalId);
        var result = f.Commit(removal);
        Assert.AreEqual(CharacterCareerQualityCommitOutcome.Applied, result.Outcome);
        var post = f.Saved();
        var entry = result.Entry!;
        var correction = Correction(post, entry);
        string xml = post.Document.Content;
        if (damage == "state") xml = xml.Replace("keep me", "changed", StringComparison.Ordinal);
        else
        {
            var fragments = entry.RemovedNodes.ToArray();
            fragments[0] = damage switch
            {
                "wrong-id" => fragments[0] with { Xml = fragments[0].Xml.Replace(first.Identity.InternalId.ToString("D"), Guid.NewGuid().ToString("D")) },
                "malformed-fragment" => fragments[0] with { Xml = "<!DOCTYPE quality [<!ENTITY x SYSTEM 'file:///unavailable'>]><quality>&x;</quality>" },
                "wrong-container" => fragments[0] with { Container = "expenses" },
                "wrong-position" => fragments[0] with { Index = 999 },
                "missing-fragment" => fragments[0],
                _ => throw new InvalidOperationException()
            };
            entry = entry with { RemovedNodes = damage == "missing-fragment" ? [] : fragments };
            entry = entry with { EntryDigest = CharacterCareerQualityTransaction.EntryDigest(entry) };
        }
        Assert.IsFalse(CharacterCareerQualityGraph.TryApplyCorrection(xml, entry, correction, out _, out _, out _));
    }

    private static CharacterCareerQualityCorrectionPlan Correction(WorkspaceStoredDocument saved, CharacterCareerQualityTransactionEntry entry)
    {
        Assert.IsTrue(CharacterCareerQualityGraph.TryObserve(saved.Document.Content, entry.Source, entry.Plan.Identity,
            entry.ResolvedArmCount, entry.Quote.Binding with { WorkspaceRevision = saved.ContentRevision,
                SavedRevision = saved.SavedRevision }, entry.Quote.RuleDigest, entry.Plan.TransactionId, out var state, out var expense));
        Assert.IsTrue(CharacterCareerQualityRules.TryPlanCorrection(entry.Receipt, state, expense,
            Guid.NewGuid(), "Correct the last selection", false, false, entry.Receipt.ReceiptDigest, out var correction));
        return correction;
    }

    private sealed class ObservingStore(FileWorkspaceStore inner) : IWorkspaceStore, ICharacterCareerQualityAtomicCommitCapability
    {
        public bool SimulateUnwrittenSuccess, LoseResponse;
        public int Reads, Commits;
        public string CareerQualityRuntimeFingerprint => inner.CareerQualityRuntimeFingerprint;
        public CharacterCareerQualityCommitResult CommitCareerQuality(OwnerScope owner, CharacterCareerQualityPlan plan,
            ICharacterCareerQualityAuthorityResolver authority, CancellationToken token = default)
        {
            Commits++;
            if (SimulateUnwrittenSuccess)
            {
                var saved = (owner.IsLocalSingleUser ? inner.Get(new(plan.WorkspaceId)) : inner.Get(owner, new(plan.WorkspaceId))).Value!;
                using var admission = authority.Open(owner, saved, plan.Identity, plan.Operation, CareerQualityRuntimeFingerprint);
                Assert.IsTrue(CharacterCareerQualityTransaction.TryBuild(owner, saved, plan, admission!, CareerQualityRuntimeFingerprint,
                    out _, out var entry));
                return new(CharacterCareerQualityCommitOutcome.Applied, entry, plan.TargetWorkspaceRevision);
            }
            var result = inner.CommitCareerQuality(owner, plan, authority, token);
            if (LoseResponse) throw new IOException("synthetic lost adapter response");
            return result;
        }
        public CharacterCareerQualityCorrectionResult CorrectCareerQuality(OwnerScope owner,
            CharacterCareerQualityCorrectionPlan correction, CancellationToken token = default)
        {
            Commits++;
            if (SimulateUnwrittenSuccess)
            {
                var saved = (owner.IsLocalSingleUser ? inner.Get(new(correction.WorkspaceId)) : inner.Get(owner, new(correction.WorkspaceId))).Value!;
                Assert.IsTrue(CharacterCareerQualityTransaction.TryBuildCorrection(owner, saved, correction,
                    CareerQualityRuntimeFingerprint, out _, out var entry));
                return new(CharacterCareerQualityCommitOutcome.Applied, entry, correction.TargetWorkspaceRevision);
            }
            var result = inner.CorrectCareerQuality(owner, correction, token);
            if (LoseResponse) throw new IOException("synthetic lost correction response");
            return result;
        }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) { Reads++; return inner.Get(id); }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) { Reads++; return inner.Get(owner, id); }
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => inner.CreateWorkspaceDocument(document);
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => inner.CreateWorkspaceDocument(owner, document);
        public IReadOnlyList<WorkspaceStoreEntry> List() => inner.List();
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => inner.List(owner);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(id, revision, document);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => inner.ReplaceWorkspaceDocument(owner, id, revision, document);
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(id, revision);
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.SaveCheckpoint(owner, id, revision);
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => inner.Delete(id, revision);
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => inner.Delete(owner, id, revision);
    }

    private sealed class Fault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage>? Action;
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath) => Action?.Invoke(stage);
    }

    private sealed class Fixture : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("chummer-career-quality-persistence-").FullName;
        public CharacterWorkspaceId Id { get; } = new("career-quality-persistence");
        public OwnerScope Owner { get; }
        public FileWorkspaceStore Store { get; }
        public CharacterCreationAttributesServiceTests.AllocationOwners Owners { get; } = new();
        public TestAuthority Authority { get; }
        public OwnerBoundCharacterCareerQualityService Service { get; }
        public Fixture(IFileWorkspaceStoreFaultInjector? fault = null, bool linked = false, bool negative = false, bool firstLevel = false)
        {
            Owner = linked ? new("synthetic-linked-owner") : OwnerScope.LocalSingleUser;
            Owners.Set(Owner);
            Store = fault is null ? new(Path) : new(Path, fault);
            Authority = new(negative, firstLevel);
            Service = new(Store, Owners, Authority);
            var document = new WorkspaceDocument("<character><created>True</created><karma>100</karma><qualities/><improvements/><expenses/><notes>keep me</notes></character>", "sr5");
            Assert.IsTrue((linked ? Store.CreateWorkspaceDocument(Owner, Id, document) : Store.CreateWorkspaceDocument(Id, document)).Success);
            Assert.IsTrue((linked ? Store.SaveCheckpoint(Owner, Id, 1) : Store.SaveCheckpoint(Id, 1)).Success);
        }
        public WorkspaceStoredDocument Saved()
        {
            var cold = new FileWorkspaceStore(Path);
            var result = Owner.IsLocalSingleUser ? cold.Get(Id) : cold.Get(Owner, Id);
            Assert.IsTrue(result.Success, result.Error);
            return result.Value!;
        }
        public CharacterCareerQualityPlan Plan(CharacterCareerQualityOperation operation = CharacterCareerQualityOperation.AcquireLevel,
            Guid? instance = null)
        {
            using var admission = Authority.Open(Owner, Saved(), new(instance ?? Guid.NewGuid(), Authority.Source.SourceId),
                operation, Store.CareerQualityRuntimeFingerprint);
            Assert.IsTrue(CharacterCareerQualityRules.TryCreateQuote(admission!.Input, out var quote));
            Assert.IsTrue(quote.CanApply, quote.Blocker.ToString());
            Assert.IsTrue(CharacterCareerQualityRules.TryPlan(quote, quote.LogicalRevision, quote.SourceRevision, quote.RuleDigest,
                quote.Binding.RuntimeFingerprint, quote.Binding.ContentDigest, quote.Binding.WorkspaceRevision,
                quote.Binding.SavedRevision, true, false, Guid.NewGuid(), new DateTime(2081, 5, 12, 14, 30, 0), out var plan));
            return plan;
        }
        public CharacterCareerQualityCommitResult Commit(CharacterCareerQualityPlan plan) => Service.Commit(Owners.Capture(), plan);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // Synthetic authority deliberately isolates persistence. This fixture is not
    // a production GM/eligibility resolver and must never be registered in the app.
    private sealed class TestAuthority : ICharacterCareerQualityAuthorityResolver
    {
        private static readonly string Digest = CharacterCareerQualityGraph.Hash("synthetic quality authority");
        public CharacterCareerQualitySource Source { get; }
        public bool Current = true;
        public int OpenCount, DisposeCount;
        public TestAuthority(bool negative, bool firstLevel)
        {
            var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            Source = new(id, "Synthetic quality", "SR5", $"<quality><id>{id:D}</id><name>Synthetic quality</name>"
                + $"<category>{(negative ? "Negative" : "Positive")}</category><karma>{(negative ? -4 : 4)}</karma>"
                + "<limit>5</limit><bonus><ambidextrous/></bonus>"
                + (firstLevel ? "<firstlevelbonus><erased/></firstlevelbonus>" : "")
                + "<source>SR5</source><page>1</page></quality>",
                "sha256:" + Digest, "sha256:" + Digest, "sha256:" + Digest);
        }
        public ICharacterCareerQualityAdmission? Open(OwnerScope owner, WorkspaceStoredDocument saved,
            CharacterCareerQualityIdentity identity, CharacterCareerQualityOperation operation, string runtimeFingerprint)
        {
            Assert.IsTrue(CharacterCareerQualityGraph.TryProject(saved.Document.Content, Source, identity, operation, null, out var graph));
            var input = new CharacterCareerQualityInput(operation, identity, true, "sr5", true, true,
                operation == CharacterCareerQualityOperation.AcquireLevel, operation != CharacterCareerQualityOperation.AcquireLevel,
                true, false, false, 0, graph!.KarmaBefore, graph.Extra, graph.SourceName, graph.Definition, new(1, false, false),
                new(true, true, true, true, true, true, [], [], Digest), graph.Effects, graph.InstancesBefore,
                new(owner.NormalizedValue, saved.Id.Value, saved.ContentRevision, saved.SavedRevision, runtimeFingerprint, Digest),
                graph.SourceState, "synthetic-rule-profile");
            Interlocked.Increment(ref OpenCount);
            return new Admission(this, input);
        }
        private sealed class Admission(TestAuthority authority, CharacterCareerQualityInput input) : ICharacterCareerQualityAdmission
        {
            private bool disposed;
            public CharacterCareerQualityInput Input => input;
            public CharacterCareerQualitySource Source => authority.Source;
            public int? ResolvedArmCount => null;
            public bool IsCurrent() => !disposed && authority.Current;
            public void Dispose() { if (!disposed) { disposed = true; Interlocked.Increment(ref authority.DisposeCount); } }
        }
    }
}
