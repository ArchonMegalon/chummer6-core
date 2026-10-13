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
        public Fixture(IFileWorkspaceStoreFaultInjector? fault = null, bool linked = false, bool negative = false)
        {
            Owner = linked ? new("synthetic-linked-owner") : OwnerScope.LocalSingleUser;
            Owners.Set(Owner);
            Store = fault is null ? new(Path) : new(Path, fault);
            Authority = new(negative);
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
        public TestAuthority(bool negative)
        {
            var id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            Source = new(id, "Synthetic quality", "SR5", $"<quality><id>{id:D}</id><name>Synthetic quality</name>"
                + $"<category>{(negative ? "Negative" : "Positive")}</category><karma>{(negative ? -4 : 4)}</karma>"
                + "<limit>5</limit><bonus><ambidextrous/></bonus><source>SR5</source><page>1</page></quality>",
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
