using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceCharacterCareerReputationTests
{
    [TestMethod]
    public void Imported_reputation_receipts_cannot_recover_or_replay_a_local_commit()
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, f.Service.Commit(command).Outcome);
        var history = WorkspaceImportedHistoryTestFixture.MarkImported(f.StorePath, f.Id);
        string before = JsonSerializer.Serialize(f.Saved());
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.StorePath), f.Resolver);
        Assert.AreEqual(CharacterCareerReputationOutcome.IdempotencyConflict, cold.Commit(command).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.IdempotencyConflict,
            new FileWorkspaceStore(f.StorePath).CommitCareerReputation(command, f.Resolver).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
        var next = cold.Preview(new(f.Id, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards, new(1)));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, next.Outcome, next.Error);
        var nextCommand = next.Preview!.Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, cold.Commit(nextCommand).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, cold.Commit(nextCommand).Outcome);
        Assert.AreEqual(history, f.Saved().LocalHistory);
        Assert.HasCount(2, f.Saved().Document.AuxiliaryState.CharacterCareerReputationReceipts!);
    }

    [TestMethod]
    public void Confirmed_manual_awards_save_one_revision_preserve_unrelated_state_and_cold_replay()
    {
        using var f = new Fixture();
        var before = f.Saved();
        var preview = f.Preview(new CharacterCareerReputationAdjustment(2, -1, 3));
        Assert.IsFalse(preview.Command.ExplicitlyConfirmed);
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(CharacterCareerReputationOutcome.Conflict, f.Service.Commit(preview.Command).Outcome);
        var command = preview.Command with { ExplicitlyConfirmed = true };
        var result = f.Service.Commit(command);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, result.Outcome, result.Error);
        var saved = f.Saved();
        Assert.AreEqual(2L, saved.ContentRevision);
        Assert.AreEqual(2L, saved.SavedRevision);
        var root = XElement.Parse(saved.Document.Content);
        Assert.AreEqual("3", root.Element("streetcred")!.Value);
        Assert.AreEqual("1", root.Element("notoriety")!.Value);
        Assert.AreEqual("4", root.Element("publicawareness")!.Value);
        Assert.AreEqual("0", root.Element("burntstreetcred")!.Value);
        foreach (string field in new[] { "expenses", "notes", "contacts", "karma", "nuyen", "improvements" })
            Assert.IsTrue(XNode.DeepEquals(XElement.Parse(before.Document.Content).Element(field), root.Element(field)), field);
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.StorePath), f.Resolver);
        var repeated = cold.Commit(command);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, repeated.Outcome, repeated.Error);
        Assert.AreEqual(result.Receipt, repeated.Receipt);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterCareerReputationReceipts!.Count);
    }

    [TestMethod]
    public void Burn_changes_only_counter_not_manual_notoriety_or_karma_and_recomputes_effective_totals()
    {
        using var f = new Fixture();
        var result = f.Service.Preview(new(f.Id, Guid.NewGuid(), CharacterCareerReputationOperation.BurnStreetCred));
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, result.Outcome, result.Error);
        Assert.AreEqual(4, result.Preview!.Quote.Before.TotalStreetCred);
        Assert.AreEqual(2, result.Preview.Quote.After.TotalStreetCred);
        Assert.AreEqual(1, result.Preview.Quote.After.TotalNotoriety);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied,
            f.Service.Commit(result.Preview.Command with { ExplicitlyConfirmed = true }).Outcome);
        var root = XElement.Parse(f.Saved().Document.Content);
        Assert.AreEqual("2", root.Element("burntstreetcred")!.Value);
        Assert.AreEqual("2", root.Element("notoriety")!.Value);
        Assert.AreEqual("130", root.Element("karma")!.Value);
    }

    [TestMethod]
    [DataRow("runtime")]
    [DataRow("source")]
    [DataRow("profile")]
    [DataRow("auxiliary")]
    [DataRow("snapshot")]
    [DataRow("preview")]
    [DataRow("request")]
    public void Forged_bound_command_cannot_write(string part)
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        string hash = new('a', 64);
        command = part switch
        {
            "runtime" => command with { Binding = command.Binding with { RuntimeDigest = hash } },
            "source" => command with { Binding = command.Binding with { SourceDigest = hash } },
            "profile" => command with { Binding = command.Binding with { RuleStateDigest = hash } },
            "auxiliary" => command with { Binding = command.Binding with { AuxiliaryStateDigest = hash } },
            "snapshot" => command with { Binding = command.Binding with { SnapshotDigest = hash } },
            "preview" => command with { ExpectedPreviewDigest = hash },
            "request" => command with { Request = command.Request with { Adjustment = new(2) } },
            _ => throw new InvalidOperationException()
        };
        string before = JsonSerializer.Serialize(f.Saved());
        Assert.AreEqual(CharacterCareerReputationOutcome.Conflict, f.Service.Commit(command).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    public void Rehashed_receipt_and_replacement_cannot_use_generic_auxiliary_writer()
    {
        using var f = new Fixture();
        var saved = f.Saved();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var snapshot = f.Service.Read(f.Id).Snapshot!;
        Assert.IsTrue(CharacterCareerReputationTransaction.TryBuild(saved, snapshot, command,
            f.Store.CareerReputationRuntimeDigest, out var replacement, out _));
        var forged = f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, 1,
            saved.Document.AuxiliaryStateDigest, replacement!);
        Assert.IsFalse(forged.Success);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, f.Service.Commit(command).Outcome);
    }

    [TestMethod]
    public void Concurrent_same_command_commits_once_and_different_same_revision_command_conflicts()
    {
        using var f = new Fixture();
        var one = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var other = f.Preview(new(2)).Command with { ExplicitlyConfirmed = true };
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.StorePath), f.Resolver);
        var results = Task.WhenAll(Task.Run(() => f.Service.Commit(one)), Task.Run(() => cold.Commit(one))).GetAwaiter().GetResult();
        Assert.AreEqual(1, results.Count(result => result.Outcome == CharacterCareerReputationOutcome.Applied));
        Assert.AreEqual(1, results.Count(result => result.Outcome == CharacterCareerReputationOutcome.Replayed));
        Assert.AreEqual(CharacterCareerReputationOutcome.Conflict, cold.Commit(other).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    public void Same_operation_changed_command_is_idempotency_conflict_even_after_source_policy_changes()
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, f.Service.Commit(command).Outcome);
        File.Delete(f.ProfilePath);
        Assert.AreEqual(CharacterCareerReputationOutcome.Replayed, f.Service.Commit(command).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.IdempotencyConflict,
            f.Service.Commit(command with { Request = command.Request with { Reason = "different" } }).Outcome);
    }

    [TestMethod]
    public void Profile_change_after_temp_flush_is_fenced_and_temporary_file_is_removed()
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        string before = JsonSerializer.Serialize(f.Saved());
        fault.Action = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                File.AppendAllText(f.ProfilePath, "\n");
        };
        Assert.AreEqual(CharacterCareerReputationOutcome.Unavailable, f.Service.Commit(command).Outcome);
        Assert.AreEqual(before, JsonSerializer.Serialize(f.Saved()));
        Assert.AreEqual(0, Directory.GetFiles(f.StorePath, "*.tmp.*", SearchOption.AllDirectories).Length);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Cancellation_at_write_boundary_cannot_turn_a_committed_change_into_an_unobserved_retry(bool afterReplace)
    {
        var fault = new Fault();
        using var f = new Fixture(fault);
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        using var cancellation = new CancellationTokenSource();
        fault.Action = stage =>
        {
            if (stage == (afterReplace ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed))
                cancellation.Cancel();
        };
        var result = f.Service.Commit(command, cancellation.Token);
        Assert.AreEqual(afterReplace ? CharacterCareerReputationOutcome.Applied : CharacterCareerReputationOutcome.Unavailable, result.Outcome);
        Assert.AreEqual(afterReplace ? 2L : 1L, f.Saved().ContentRevision);
        fault.Action = null;
        Assert.AreEqual(afterReplace ? CharacterCareerReputationOutcome.Replayed : CharacterCareerReputationOutcome.Applied,
            f.Service.Commit(command).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Durable_receipt_not_adapter_response_decides_whether_the_command_was_applied(bool actuallyWritten)
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var adapter = new ObservingStore(f.Store) { SimulateUnwrittenSuccess = !actuallyWritten, LoseWrittenResponse = actuallyWritten };
        var service = new WorkspaceCharacterCareerReputationService(adapter, f.Resolver);
        var result = service.Commit(command);
        Assert.AreEqual(actuallyWritten ? CharacterCareerReputationOutcome.Replayed : CharacterCareerReputationOutcome.Unavailable,
            result.Outcome, result.Error);
        Assert.AreEqual(actuallyWritten ? 2L : 1L, f.Saved().ContentRevision);
        Assert.AreEqual(1, adapter.CommitCount);
        if (!actuallyWritten) Assert.IsNull(result.Receipt);
        adapter.SimulateUnwrittenSuccess = adapter.LoseWrittenResponse = false;
        Assert.AreEqual(actuallyWritten ? CharacterCareerReputationOutcome.Replayed : CharacterCareerReputationOutcome.Applied,
            service.Commit(command).Outcome);
        Assert.AreEqual(2L, f.Saved().ContentRevision);
    }

    [TestMethod]
    [DataRow("reason")]
    [DataRow("null-reason")]
    [DataRow("null-request")]
    [DataRow("null-binding")]
    [DataRow("operation")]
    [DataRow("empty-id")]
    [DataRow("workspace")]
    public void Invalid_command_is_rejected_before_store_access_or_hash_allocation(string kind)
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        command = kind switch
        {
            "reason" => command with { Request = command.Request with { Reason = new('<', 1_000_000) } },
            "null-reason" => command with { Request = command.Request with { Reason = null! } },
            "null-request" => command with { Request = null! },
            "null-binding" => command with { Binding = null! },
            "operation" => command with { Request = command.Request with { Operation = (CharacterCareerReputationOperation)999 } },
            "empty-id" => command with { Request = command.Request with { OperationId = Guid.Empty } },
            "workspace" => command with { Request = command.Request with { WorkspaceId = new("../foreign") } },
            _ => throw new InvalidOperationException()
        };
        var store = new ObservingStore(f.Store);
        var service = new WorkspaceCharacterCareerReputationService(store, f.Resolver);
        Assert.AreEqual(CharacterCareerReputationOutcome.Conflict, service.Commit(command).Outcome);
        // Preview takes a request, not a pre-existing binding; a missing
        // command binding does not make that otherwise valid request invalid.
        if (kind != "null-binding")
            Assert.AreEqual(CharacterCareerReputationOutcome.Conflict, service.Preview(command.Request).Outcome);
        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, store.CommitCount);
    }

    [TestMethod]
    public void Precancelled_command_never_reads_or_mutates_store()
    {
        using var f = new Fixture();
        var command = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var store = new ObservingStore(f.Store);
        var service = new WorkspaceCharacterCareerReputationService(store, f.Resolver);
        using var token = new CancellationTokenSource();
        token.Cancel();
        Assert.ThrowsExactly<OperationCanceledException>(() => service.Commit(command, token.Token));
        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, store.CommitCount);
    }

    [TestMethod]
    public void Headless_DI_uses_the_configured_store_and_fails_closed_without_dedicated_capability()
    {
        using var f = new Fixture();
        var services = new ServiceCollection();
        services.AddChummerHeadlessCore(f.Root, f.Root);
        services.Replace(ServiceDescriptor.Singleton<IWorkspaceStore>(f.Store));
        services.Replace(ServiceDescriptor.Singleton(f.Resolver));
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICharacterCareerReputationService>();
        Assert.AreSame(f.Store, provider.GetRequiredService<IWorkspaceStore>());
        Assert.AreSame(service, provider.GetRequiredService<ICharacterCareerReputationService>());
        Assert.AreEqual(1, provider.GetServices<ICharacterCareerReputationService>().Count());
        var request = new CharacterCareerReputationRequest(f.Id, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards, new(1));
        var preview = service.Preview(request);
        Assert.AreEqual(CharacterCareerReputationOutcome.Available, preview.Outcome, preview.Error);
        var memory = new InMemoryWorkspaceStore();
        Assert.IsTrue(memory.CreateWorkspaceDocument(f.Id, f.Saved().Document).Success);
        Assert.IsTrue(memory.SaveCheckpoint(f.Id, 1).Success);
        var unsupported = new WorkspaceCharacterCareerReputationService(memory, f.Resolver);
        Assert.AreEqual(CharacterCareerReputationOutcome.Unavailable, unsupported.Preview(request).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.Unavailable,
            unsupported.Commit(preview.Preview!.Command with { ExplicitlyConfirmed = true }).Outcome);
        Assert.AreEqual(1L, memory.Get(f.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Reward_reputation_contact_settlement_and_later_reward_preserve_each_others_receipts()
    {
        using var f = new Fixture();
        var rewards = new WorkspaceCharacterAfterRunRewardService(f.Store);
        var award = rewards.Preview(new(f.Id, Guid.NewGuid(), Guid.NewGuid(), 8, 12500,
            new DateTime(2078, 9, 7, 19, 0, 0), "Run reward"));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, award.Outcome, award.Error);
        var rewardCommand = award.Preview!.Command with { ExplicitlyConfirmed = true };
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewards.Commit(rewardCommand).Outcome);
        var reputationCommand = f.Preview(new(1, -1, 2)).Command with { ExplicitlyConfirmed = true };
        var reputation = f.Service.Commit(reputationCommand);
        Assert.AreEqual(CharacterCareerReputationOutcome.Applied, reputation.Outcome, reputation.Error);
        var input = CharacterAfterRunSettlementRulesTests.Input();
        var settlement = new CharacterAfterRunSettlementService(
            new WorkspaceCharacterAfterRunSettlementWorkspace(f.Store, new SettlementSource(input)));
        var binding = settlement.Quote(new(f.Id, input.Identity)).Binding!;
        Assert.IsNotNull(binding);
        var settlementCommand = new CharacterAfterRunSettlementCommand(CharacterAfterRunSettlementServiceSchemas.CommandV1,
            f.Id, binding.WorkspaceRevision, binding.Identity, binding.Quote.SourceDigest, binding.Quote.CustomDataDigest,
            binding.Quote.GmPolicyDigest, binding.Quote.RuntimeDigest, binding.Quote.LogicalDigest, binding.BindingDigest,
            Guid.NewGuid(), ExplicitlyConfirmed: true);
        Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Applied, settlement.Settle(settlementCommand).Outcome);
        var later = rewards.Preview(new(f.Id, Guid.NewGuid(), Guid.NewGuid(), 2, 0,
            new DateTime(2078, 9, 8, 19, 0, 0), "Later reward"));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, later.Outcome, later.Error);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied,
            rewards.Commit(later.Preview!.Command with { ExplicitlyConfirmed = true }).Outcome);
        var saved = f.Saved();
        Assert.AreEqual(5L, saved.SavedRevision);
        Assert.AreEqual(129, int.Parse(XElement.Parse(saved.Document.Content).Element("karma")!.Value));
        Assert.AreEqual(2, XElement.Parse(saved.Document.Content).Element("contacts")!.Elements("contact").Count());
        Assert.AreEqual(2, saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts!.Count);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterCareerReputationReceipts!.Count);
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.StorePath), f.Resolver);
        Assert.AreEqual(reputation.Receipt, cold.Commit(reputationCommand).Receipt);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, rewards.Commit(rewardCommand).Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(f.Saved()));
        var replacement = saved.Document with { State = saved.Document.State with
        { AuxiliaryState = saved.Document.AuxiliaryState with { CharacterCareerReputationReceipts = null } } };
        Assert.IsFalse(f.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(f.Id, 5,
            saved.Document.AuxiliaryStateDigest, replacement).Success);
        Assert.AreEqual(JsonSerializer.Serialize(saved), JsonSerializer.Serialize(f.Saved()));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("digest")]
    [DataRow("chain")]
    [DataRow("order")]
    [DataRow("duplicate")]
    [DataRow("quote")]
    [DataRow("payload")]
    [DataRow("checkpoint")]
    public void Corrupt_durable_history_never_becomes_replay_success_or_empty_history(string kind)
    {
        using var f = new Fixture();
        var one = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var first = f.Service.Commit(one).Receipt!;
        var two = f.Preview(new(1)).Command with { ExplicitlyConfirmed = true };
        var second = f.Service.Commit(two).Receipt!;
        CharacterCareerReputationReceipt[] history;
        if (kind == "null") history = [null!];
        else if (kind == "order") history = [second, first];
        else if (kind == "duplicate") history = [first, first];
        else
        {
            var changed = kind switch
            {
                "digest" => second with { ReceiptDigest = new('a', 64) },
                "chain" => second with { PreviousReceiptDigest = CharacterCareerReputationTransaction.LedgerRoot },
                "quote" => second with { Quote = second.Quote with
                    { After = second.Quote.After with { TotalStreetCred = 999 } } },
                "payload" => second with { CharacterPayloadDigestAfter = new('a', 64) },
                "checkpoint" => second,
                _ => throw new InvalidOperationException()
            };
            if (kind != "digest") changed = changed with { ReceiptDigest = CharacterCareerReputationTransaction.ReceiptDigest(changed) };
            history = [first, changed];
        }
        string file = Directory.GetFiles(f.StorePath, "*.json", SearchOption.AllDirectories).Single();
        var json = JsonNode.Parse(File.ReadAllText(file))!;
        json["AuxiliaryState"]!["CharacterCareerReputationReceipts"] = JsonSerializer.SerializeToNode(history);
        if (kind == "checkpoint") json["SavedRevision"] = 1;
        File.WriteAllText(file, json.ToJsonString());
        string before = File.ReadAllText(file);
        var cold = new WorkspaceCharacterCareerReputationService(new FileWorkspaceStore(f.StorePath), f.Resolver);
        Assert.AreEqual(CharacterCareerReputationOutcome.Corrupt, cold.Lookup(f.Id, two.Request.OperationId,
            CharacterCareerReputationTransaction.CommandDigest(two)).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.Corrupt, cold.Read(f.Id).Outcome);
        Assert.AreEqual(CharacterCareerReputationOutcome.Corrupt, cold.Commit(two).Outcome);
        Assert.AreEqual(before, File.ReadAllText(file));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Coherent_synthetic_history_has_independent_count_and_utf8_bounds(bool escapedReason)
    {
        // Pure hostile-admission test, not a way to authorize historical writes.
        // Entries are individually coherent; no authority is inferred from hashes.
        using var f = new Fixture();
        var original = f.Service.Read(f.Id).Snapshot!;
        var history = new List<CharacterCareerReputationReceipt>();
        string previous = CharacterCareerReputationTransaction.LedgerRoot;
        for (int index = 0; index < CharacterCareerReputationTransaction.MaximumReceipts + 1; index++)
        {
            var snapshot = original with { ContentRevision = index + 1, SavedRevision = index + 1 };
            var request = new CharacterCareerReputationRequest(f.Id, Guid.NewGuid(),
                CharacterCareerReputationOperation.AdjustManualAwards, new(1), escapedReason ? new('<', 512) : "");
            Assert.IsTrue(CharacterCareerReputationTransaction.TryPreview(snapshot, request,
                f.Store.CareerReputationRuntimeDigest, out var preview));
            var command = preview!.Command with { ExplicitlyConfirmed = true };
            var receipt = new CharacterCareerReputationReceipt(command, preview.Quote,
                CharacterCareerReputationTransaction.CommandDigest(command), index + 2,
                new('a', 64), previous, "");
            receipt = receipt with { ReceiptDigest = CharacterCareerReputationTransaction.ReceiptDigest(receipt) };
            Assert.IsTrue(CharacterCareerReputationTransaction.IsCoherent(receipt));
            history.Add(receipt);
            previous = receipt.ReceiptDigest;
        }
        var atCountLimit = history.Take(CharacterCareerReputationTransaction.MaximumReceipts).ToArray();
        int actualUtf8 = JsonSerializer.SerializeToUtf8Bytes(atCountLimit).Length;
        Assert.AreEqual(escapedReason, actualUtf8 > CharacterCareerReputationTransaction.MaximumLedgerBytes);
        Assert.AreEqual(!escapedReason, CharacterCareerReputationTransaction.IsValidLedger(f.Id, 1025, atCountLimit));
        Assert.IsFalse(CharacterCareerReputationTransaction.IsValidLedger(f.Id, 1026, history));
        Assert.AreEqual(1L, f.Saved().ContentRevision);
        Assert.IsNull(f.Saved().Document.AuxiliaryState.CharacterCareerReputationReceipts);
    }

    private sealed class SettlementSource(CharacterAfterRunSettlementInput input) : ICharacterAfterRunSettlementProposalProjectionSource
    {
        public CharacterAfterRunSettlementProposalProjectionResult Read(CharacterAfterRunSettlementProposalProjectionRequest request)
            => new(CharacterAfterRunSettlementProposalProjectionOutcome.Available, request.WorkspaceId,
                request.WorkspaceRevision, request.CharacterProjectionDigest,
                new CharacterAfterRunSettlementProposalProjection(input.Identity, input.TargetOwnedByCharacter,
                    input.ProjectionIsExact, input.RunCompleted, input.ExpectedGmActorId, input.ExpectedOwnerActorId,
                    input.CurrentHeat, input.HeatDelta, input.StreetCredDelta, input.NotorietyDelta,
                    input.PublicAwarenessDelta, input.Settings, input.ContactProposals, input.GmReview,
                    input.OwnerReview, input.RawSourceState, input.RawCustomDataState, input.RawGmPolicyState,
                    input.RawRuntimeState));
    }

    private sealed class ObservingStore(FileWorkspaceStore inner) : IWorkspaceStore, ICharacterCareerReputationAtomicCommitCapability
    {
        public int ReadCount { get; private set; }
        public int CommitCount { get; private set; }
        public bool SimulateUnwrittenSuccess { get; set; }
        public bool LoseWrittenResponse { get; set; }
        public string CareerReputationRuntimeDigest => inner.CareerReputationRuntimeDigest;
        public CharacterCareerReputationResult CommitCareerReputation(CharacterCareerReputationCommand command,
            ICharacterSourceDataResolver resolver, CancellationToken token = default)
        {
            CommitCount++;
            if (SimulateUnwrittenSuccess)
            {
                var saved = inner.Get(command.Request.WorkspaceId).Value!;
                Assert.IsTrue(CharacterCareerReputationProjector.TryRead(saved, resolver, out var snapshot, out _));
                Assert.IsTrue(CharacterCareerReputationTransaction.TryBuild(saved, snapshot!, command,
                    CareerReputationRuntimeDigest, out _, out var receipt));
                return new(CharacterCareerReputationOutcome.Applied, receipt, receipt!.CommittedWorkspaceRevision);
            }
            var result = inner.CommitCareerReputation(command, resolver, token);
            if (LoseWrittenResponse) throw new IOException("Simulated lost adapter response after durable commit.");
            return result;
        }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id) { ReadCount++; return inner.Get(id); }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => inner.Get(owner, id);
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
        public Action<FileWorkspaceStoreFaultStage>? Action { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath) => Action?.Invoke(stage);
    }

    private sealed class Fixture : IDisposable
    {
        private const string ProfileId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-reputation-persistence-").FullName;
        public string StorePath => Path.Combine(Root, "workspaces");
        public string ProfilePath => Path.Combine(Root, "data", "settings.xml");
        public CharacterWorkspaceId Id { get; } = new("reputation-persistence-test");
        public FileWorkspaceStore Store { get; }
        public ICharacterSourceDataResolver Resolver { get; }
        public WorkspaceCharacterCareerReputationService Service { get; }

        public Fixture(IFileWorkspaceStoreFaultInjector? fault = null)
        {
            Directory.CreateDirectory(Path.Combine(Root, "data"));
            string source = FindCoreRoot();
            File.Copy(Path.Combine(source, "Chummer", "data", "settings.xml"), ProfilePath);
            File.Copy(Path.Combine(source, "Chummer", "data", "books.xml"), Path.Combine(Root, "data", "books.xml"));
            Store = fault is null ? new FileWorkspaceStore(StorePath) : new FileWorkspaceStore(StorePath, fault);
            Resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(Root, Root, null));
            Service = new(Store, Resolver);
            var document = new WorkspaceDocument($"""
                <character><settings>{ProfileId}</settings><created>True</created><karma>130</karma><nuyen>1000</nuyen>
                <streetcred>1</streetcred><notoriety>2</notoriety><publicawareness>1</publicawareness><burntstreetcred>0</burntstreetcred>
                <expenses><expense><guid>11111111-1111-4111-8111-111111111111</guid><date>2078-09-07T18:00:00</date>
                <type>Karma</type><amount>30</amount><refund>False</refund><forcecareervisible>False</forcecareervisible></expense></expenses>
                <improvements/><contacts/><notes>Keep all unrelated state</notes></character>
                """, "sr5");
            Assert.IsTrue(Store.CreateWorkspaceDocument(Id, document).Success);
            Assert.IsTrue(Store.SaveCheckpoint(Id, 1).Success);
        }

        public WorkspaceStoredDocument Saved() => new FileWorkspaceStore(StorePath).Get(Id).Value!;
        public CharacterCareerReputationPreview Preview(CharacterCareerReputationAdjustment adjustment)
        {
            var result = Service.Preview(new(Id, Guid.NewGuid(), CharacterCareerReputationOperation.AdjustManualAwards, adjustment, "After Run consequence"));
            Assert.AreEqual(CharacterCareerReputationOutcome.Available, result.Outcome, result.Error);
            return result.Preview!;
        }
        private static string FindCoreRoot()
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "Chummer", "data", "settings.xml"))) return directory.FullName;
            throw new InvalidOperationException("Exact Core content tree is unavailable.");
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
