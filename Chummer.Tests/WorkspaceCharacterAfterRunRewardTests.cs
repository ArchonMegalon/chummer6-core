using System.Xml.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceCharacterAfterRunRewardTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("after-run-reward-tests");
    private static readonly Guid OperationId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid RewardId = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly DateTime ExpenseDate = new(2078, 9, 6, 18, 30, 0);

    [TestMethod]
    public void Oversized_reason_is_rejected_before_store_reads_or_writes_at_both_boundaries()
    {
        using var fixture = new Fixture();
        var command = Command(new WorkspaceCharacterAfterRunRewardService(fixture.Store));
        var store = new FaultingStore(fixture.Store);
        var service = new WorkspaceCharacterAfterRunRewardService(store);
        // JSON escaping would expand this further if input admission ran after hashing.
        string oversized = new('<', 1_000_000);
        var preview = service.Preview(Request() with { Reason = oversized });
        var commit = service.Commit(command with { Reason = oversized });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, preview.Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, commit.Outcome);
        Assert.AreEqual("reward_command_invalid", preview.Error);
        Assert.AreEqual("reward_command_invalid", commit.Error);
        Assert.IsNull(preview.CurrentWorkspaceRevision);
        Assert.IsNull(commit.CurrentWorkspaceRevision);
        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, store.SuccessfulCommits);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("utc")]
    [DataRow("local")]
    [DataRow("subsecond")]
    public void Noncanonical_expense_dates_are_rejected_without_silent_conversion_or_store_access(string kind)
    {
        using var fixture = new Fixture();
        var command = Command(new WorkspaceCharacterAfterRunRewardService(fixture.Store));
        DateTime date = kind switch
        {
            "utc" => DateTime.SpecifyKind(ExpenseDate, DateTimeKind.Utc),
            "local" => DateTime.SpecifyKind(ExpenseDate, DateTimeKind.Local),
            _ => ExpenseDate.AddTicks(1)
        };
        var store = new FaultingStore(fixture.Store);
        var service = new WorkspaceCharacterAfterRunRewardService(store);
        var preview = service.Preview(Request() with { ExpenseDateLocal = date });
        var commit = service.Commit(command with { ExpenseDateLocal = date });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, preview.Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, commit.Outcome);
        Assert.AreEqual("reward_command_invalid", preview.Error);
        Assert.AreEqual("reward_command_invalid", commit.Error);
        Assert.AreEqual(0, store.ReadCount);
        Assert.AreEqual(0, store.SuccessfulCommits);
    }

    [TestMethod]
    [DoNotParallelize]
    public void Journaled_command_and_preview_digests_survive_timezone_changes_and_cold_replay()
    {
        using var fixture = new Fixture();
        string? originalTimezone = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", "UTC");
            TimeZoneInfo.ClearCachedData();
            var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
            var preview = service.Preview(Request()).Preview!;
            var command = preview.Command with { ExplicitlyConfirmed = true };
            string journal = JsonSerializer.Serialize(command);
            string previewJournal = JsonSerializer.Serialize(preview);
            string digest = command.CommandDigest();
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(command).Outcome);
            foreach (string timezone in new[] { "Pacific/Honolulu", "Europe/Vienna", "UTC" })
            {
                Environment.SetEnvironmentVariable("TZ", timezone);
                TimeZoneInfo.ClearCachedData();
                Assert.AreEqual(timezone, TimeZoneInfo.Local.Id);
                var recovered = JsonSerializer.Deserialize<CharacterAfterRunRewardCommand>(journal)!;
                var recoveredPreview = JsonSerializer.Deserialize<CharacterAfterRunRewardPreview>(previewJournal)!;
                Assert.AreEqual(DateTimeKind.Unspecified, recovered.ExpenseDateLocal.Kind);
                Assert.AreEqual(ExpenseDate, recovered.ExpenseDateLocal);
                Assert.AreEqual(digest, recovered.CommandDigest());
                string? recoveredFingerprint = recovered.ExpectedPreviewDigest;
                Assert.AreEqual(preview.PreviewDigest, recoveredFingerprint);
                Assert.IsTrue(CharacterAfterRunRewardProjector.IsCoherentPreview(recoveredPreview));
                var cold = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(fixture.DirectoryPath));
                Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, cold.Commit(recovered).Outcome);
                Assert.IsTrue(cold.Read(WorkspaceId).Snapshot!.Expenses.All(expense =>
                    expense.ExpenseDateLocal == ExpenseDate && expense.ExpenseDateLocal.Kind == DateTimeKind.Unspecified));
            }
            Assert.AreEqual(2L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", originalTimezone);
            TimeZoneInfo.ClearCachedData();
        }
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Reward_composition_resolves_one_service_over_the_configured_store(bool headless, bool atomic)
    {
        using var fixture = new Fixture();
        IWorkspaceStore store = atomic ? fixture.Store : new InMemoryWorkspaceStore();
        if (!atomic)
        {
            Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, Document()).Success);
            Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
        }
        var services = new ServiceCollection();
        if (headless)
            services.AddChummerHeadlessCore(fixture.DirectoryPath, fixture.DirectoryPath);
        else
            services.AddCharacterAfterRunRewardPersistence();
        services.Replace(ServiceDescriptor.Singleton(store));
        services.AddCharacterAfterRunRewardPersistence();
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<ICharacterAfterRunRewardService>();
        Assert.IsInstanceOfType<WorkspaceCharacterAfterRunRewardService>(service);
        Assert.AreSame(store, provider.GetRequiredService<IWorkspaceStore>());
        Assert.AreEqual(1, provider.GetServices<ICharacterAfterRunRewardService>().Count());
        Assert.AreSame(service, provider.GetRequiredService<ICharacterAfterRunRewardService>());
        var preview = service.Preview(Request());
        Assert.AreEqual(atomic ? CharacterAfterRunRewardOutcome.Available : CharacterAfterRunRewardOutcome.Unavailable,
            preview.Outcome, preview.Error);
        var command = atomic
            ? preview.Preview!.Command with { ExplicitlyConfirmed = true }
            : Command(new WorkspaceCharacterAfterRunRewardService(fixture.Store));
        Assert.AreEqual(atomic ? CharacterAfterRunRewardOutcome.Applied : CharacterAfterRunRewardOutcome.Unavailable,
            service.Commit(command).Outcome);
        Assert.AreEqual(atomic ? 38 : 30, service.Read(WorkspaceId).Snapshot!.AvailableKarma);
        Assert.AreEqual(atomic ? 2L : 1L, store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Reward_composition_preserves_an_explicit_host_registration()
    {
        using var fixture = new Fixture();
        ICharacterAfterRunRewardService registered = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var services = new ServiceCollection();
        services.AddSingleton(registered);
        services.AddCharacterAfterRunRewardPersistence();
        using var provider = services.BuildServiceProvider();
        Assert.AreSame(registered, provider.GetRequiredService<ICharacterAfterRunRewardService>());
        Assert.AreEqual(1, provider.GetServices<ICharacterAfterRunRewardService>().Count());
    }

    [TestMethod]
    public void Strict_empty_ledger_is_available_with_authoritative_saved_revision()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);

        var result = service.Read(WorkspaceId);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, result.Outcome);
        Assert.IsNotNull(result.Snapshot);
        Assert.AreEqual(30, result.Snapshot.AvailableKarma);
        Assert.AreEqual(1000m, result.Snapshot.AvailableNuyen);
        Assert.AreEqual(1L, result.Snapshot.ContentRevision);
        Assert.AreEqual(1L, result.Snapshot.SavedRevision);
        Assert.AreEqual(0, result.Snapshot.Expenses.Count);
        Assert.AreEqual(64, result.Snapshot.SourceDigest.Length);
        Assert.AreEqual(fixture.Store.Get(WorkspaceId).Value!.Document.AuxiliaryStateDigest,
            result.Snapshot.AuxiliaryStateDigest);
    }

    [TestMethod]
    public void Unconfirmed_preview_binds_core_outcome_without_writes_reservations_or_receipts()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var request = Request();
        var result = service.Preview(request);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, result.Outcome);
        var preview = result.Preview!;
        Assert.IsFalse(preview.Command.ExplicitlyConfirmed);
        Assert.AreEqual(30, preview.KarmaBefore);
        Assert.AreEqual(38, preview.KarmaAfter);
        Assert.AreEqual(1000m, preview.NuyenBefore);
        Assert.AreEqual(13500m, preview.NuyenAfter);
        Assert.AreEqual(request.OperationId, preview.Command.OperationId);
        Assert.AreEqual(request.RewardId, preview.Command.RewardId);
        Assert.AreEqual(request.Reason, preview.Command.Reason);
        Assert.IsFalse(preview.KarmaAlreadyRecorded);
        Assert.IsFalse(preview.NuyenAlreadyRecorded);
        string? commandFingerprint = preview.Command.ExpectedPreviewDigest;
        Assert.AreEqual(preview.PreviewDigest, commandFingerprint);
        Assert.AreEqual(CharacterAfterRunRewardProjector.ComputePreviewDigest(preview), preview.PreviewDigest);
        Assert.IsTrue(CharacterAfterRunRewardProjector.IsCoherentPreview(preview));
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        Assert.IsNull(fixture.Store.Get(WorkspaceId).Value!.Document.AuxiliaryState.CharacterAfterRunRewardReceipts);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Commit(preview.Command).Outcome);

        // This is the exact payload the host must journal before calling Commit.
        var confirmed = preview.Command with { ExplicitlyConfirmed = true };
        Assert.AreNotEqual(preview.Command.CommandDigest(), confirmed.CommandDigest());
        var roundTripped = JsonSerializer.Deserialize<CharacterAfterRunRewardCommand>(JsonSerializer.Serialize(confirmed))!;
        Assert.AreEqual(confirmed.CommandDigest(), roundTripped.CommandDigest());
        Assert.AreEqual(CharacterAfterRunRewardOutcome.NotFound,
            service.Lookup(WorkspaceId, confirmed.OperationId, confirmed.CommandDigest()).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(roundTripped).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Preview(request).Outcome);
    }

    [TestMethod]
    public void Changed_inputs_cannot_use_an_older_preview_fingerprint()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        foreach (var changed in new[]
                 {
                     command with { KarmaAmount = 9 },
                     command with { NuyenAmount = 12501 },
                     command with { Reason = "Different reviewed explanation" },
                     command with { ExpenseDateLocal = ExpenseDate.AddDays(1) },
                     command with { OperationId = Guid.NewGuid() },
                     command with { RewardId = Guid.NewGuid() },
                     command with { ExpectedPreviewDigest = null }
                 })
        {
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Commit(changed).Outcome);
            Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        }
        var fresh = Command(service, request => request with { KarmaAmount = 9 });
        Assert.AreNotEqual(command.ExpectedPreviewDigest, fresh.ExpectedPreviewDigest);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(fresh).Outcome);
        Assert.AreEqual(39, service.Read(WorkspaceId).Snapshot!.AvailableKarma);
    }

    [TestMethod]
    public void Explicit_no_award_is_previewed_confirmed_and_replayable_without_character_changes()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var request = Request() with { Kind = CharacterAfterRunRewardKind.NoAward, KarmaAmount = 0, NuyenAmount = 0 };
        var preview = service.Preview(request);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, preview.Outcome);
        Assert.AreEqual(CharacterAfterRunRewardKind.NoAward, preview.Preview!.Kind);
        Assert.AreEqual(30, preview.Preview.KarmaAfter);
        Assert.AreEqual(1000m, preview.Preview.NuyenAfter);
        Assert.IsNull(preview.Preview.KarmaExpenseId);
        Assert.IsNull(preview.Preview.NuyenExpenseId);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        var command = preview.Preview.Command with { ExplicitlyConfirmed = true };

        var result = service.Commit(command);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, result.Outcome);
        Assert.AreEqual(CharacterAfterRunRewardKind.NoAward, result.Receipt!.Kind);
        Assert.IsNull(result.Receipt.KarmaExpenseId);
        Assert.IsNull(result.Receipt.NuyenExpenseId);
        var coldStore = new FileWorkspaceStore(fixture.DirectoryPath);
        var saved = coldStore.Get(WorkspaceId).Value!;
        Assert.AreEqual(Document().Content, saved.Document.Content);
        Assert.AreEqual(2L, saved.ContentRevision);
        Assert.AreEqual(2L, saved.SavedRevision);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
        var cold = new WorkspaceCharacterAfterRunRewardService(coldStore);
        Assert.AreEqual(0, cold.Read(WorkspaceId).Snapshot!.Expenses.Count);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, cold.Commit(command).Outcome);
        Assert.AreEqual(2L, coldStore.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Default_zero_reward_and_mixed_no_award_inputs_are_not_silent_acknowledgements()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        foreach (var request in new[]
                 {
                     Request() with { KarmaAmount = 0, NuyenAmount = 0 },
                     Request() with { Kind = CharacterAfterRunRewardKind.NoAward },
                     Request() with { Kind = CharacterAfterRunRewardKind.NoAward, KarmaAmount = 0, NuyenAmount = 0, ExistingKarmaExpenseId = Guid.NewGuid() }
                 })
        {
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Preview(request).Outcome);
        }
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Atomic_bundle_awards_both_balances_and_exactly_two_expenses_in_one_saved_revision()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);

        var result = service.Commit(command);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, result.Outcome);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(30, result.Receipt.KarmaBefore);
        Assert.AreEqual(38, result.Receipt.KarmaAfter);
        Assert.AreEqual(1000m, result.Receipt.NuyenBefore);
        Assert.AreEqual(13500m, result.Receipt.NuyenAfter);
        Assert.AreEqual(2L, result.Receipt.CommittedWorkspaceRevision);
        var saved = fixture.Store.Get(WorkspaceId).Value!;
        Assert.AreEqual(2L, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        XElement root = XDocument.Parse(saved.Document.Content).Root!;
        Assert.AreEqual("38", root.Element("karma")!.Value);
        Assert.AreEqual("13500", root.Element("nuyen")!.Value);
        Assert.AreEqual("Keep unrelated runner state", root.Element("notes")!.Value);
        Assert.AreEqual(2, root.Element("expenses")!.Elements("expense").Count());
        Assert.AreNotEqual(result.Receipt.KarmaExpenseId, result.Receipt.NuyenExpenseId);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
    }

    [TestMethod]
    public void Cold_store_replays_original_receipt_even_after_later_saved_edit()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        var applied = service.Commit(command);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, applied.Outcome);
        var saved = fixture.Store.Get(WorkspaceId).Value!;
        var replacement = saved.Document with
        {
            State = saved.Document.State with
            {
                Payload = saved.Document.Content.Replace("Keep unrelated runner state", "Later manual note", StringComparison.Ordinal)
            }
        };
        Assert.IsTrue(fixture.Store.ReplaceWorkspaceDocumentAndCheckpoint(WorkspaceId, 2, replacement).Success);
        var reopenedStore = new FileWorkspaceStore(fixture.DirectoryPath);
        var reopened = new WorkspaceCharacterAfterRunRewardService(reopenedStore);

        var lookup = reopened.Lookup(WorkspaceId, command.OperationId, command.CommandDigest());
        var replay = reopened.Commit(command);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, lookup.Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(JsonSerializer.Serialize(applied.Receipt), JsonSerializer.Serialize(replay.Receipt));
        Assert.AreEqual(applied.Receipt!.ReceiptDigest, lookup.Receipt!.ReceiptDigest);
        Assert.AreEqual(3L, reopenedStore.Get(WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(2, reopened.Read(WorkspaceId).Snapshot!.Expenses.Count);
    }

    [TestMethod]
    public void Different_command_or_new_operation_for_same_reward_cannot_duplicate_awards()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(command).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.IdempotencyConflict,
            service.Commit(command with { KarmaAmount = 9 }).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict,
            service.Preview(Request() with { OperationId = Guid.NewGuid() }).Outcome);
        Assert.AreEqual(2L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("throw")]
    [DataRow("empty")]
    [DataRow("cancel")]
    public void Lost_commit_response_or_postcommit_cancellation_recovers_complete_bundle(string mode)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var wrapper = new FaultingStore(fixture.Store)
        {
            AfterCommit = result =>
            {
                if (mode == "throw") throw new IOException("Simulated lost response after durable commit.");
                if (mode == "cancel") cancellation.Cancel();
                return new(WorkspaceOperationOutcome.Unavailable);
            }
        };
        var service = new WorkspaceCharacterAfterRunRewardService(wrapper);
        var command = Command(service);

        var result = service.Commit(command, cancellation.Token);

        Assert.IsTrue(result.Outcome is CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed);
        Assert.AreEqual(1, wrapper.SuccessfulCommits);
        Assert.AreEqual(38, result.Receipt!.KarmaAfter);
        Assert.AreEqual(13500m, result.Receipt.NuyenAfter);
        var cold = new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(fixture.DirectoryPath));
        var recovered = cold.Lookup(WorkspaceId, command.OperationId, command.CommandDigest());
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed, recovered.Outcome);
        Assert.AreEqual(result.Receipt.ReceiptDigest, recovered.Receipt!.ReceiptDigest);
        Assert.AreEqual(2, cold.Read(WorkspaceId).Snapshot!.Expenses.Count);
    }

    [TestMethod]
    public void Failure_before_atomic_replacement_leaves_no_partial_award_or_receipt()
    {
        using var fixture = new Fixture();
        var before = fixture.Store.Get(WorkspaceId).Value!;
        var failingStore = new FileWorkspaceStore(fixture.DirectoryPath, new BeforeReplaceFault());
        var service = new WorkspaceCharacterAfterRunRewardService(failingStore);
        var command = Command(service);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Unavailable, service.Commit(command).Outcome);

        var coldStore = new FileWorkspaceStore(fixture.DirectoryPath);
        Assert.AreEqual(before.Document, coldStore.Get(WorkspaceId).Value!.Document);
        Assert.AreEqual(1L, coldStore.Get(WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.NotFound,
            new WorkspaceCharacterAfterRunRewardService(coldStore)
                .Lookup(WorkspaceId, command.OperationId, command.CommandDigest()).Outcome);
    }

    [TestMethod]
    public void Precommit_cancellation_never_writes()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        try
        {
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Unavailable,
                service.Commit(command, cancellation.Token).Outcome);
        }
        catch (OperationCanceledException)
        {
            // A pre-write cancellation may use the normal .NET cancellation channel.
        }
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        Assert.IsNull(fixture.Store.Get(WorkspaceId).Value!.Document.AuxiliaryState.CharacterAfterRunRewardReceipts);
    }

    [TestMethod]
    [DataRow("<expenses /><expenses />")]
    [DataRow("<expenses><expense /></expenses>")]
    [DataRow("<expenses><expense><guid>not-a-guid</guid></expense></expenses>")]
    public void Ambiguous_or_malformed_expenses_are_corrupt_not_an_empty_baseline(string expenses)
    {
        using var fixture = new Fixture(Document() with
        {
            State = Document().State with { Payload = Document().Content.Replace("<expenses />", expenses, StringComparison.Ordinal) }
        });
        var result = new WorkspaceCharacterAfterRunRewardService(fixture.Store).Read(WorkspaceId);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt, result.Outcome);
        Assert.IsNull(result.Snapshot);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Missing_expense_container_is_valid_empty_and_created_once()
    {
        using var fixture = new Fixture(Document() with
        {
            State = Document().State with { Payload = Document().Content.Replace("<expenses />", "", StringComparison.Ordinal) }
        });
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(Command(service)).Outcome);
        Assert.AreEqual(1, XDocument.Parse(fixture.Store.Get(WorkspaceId).Value!.Document.Content).Root!.Elements("expenses").Count());
    }

    [TestMethod]
    [DataRow(0, 12500, 30, 13500, 1)]
    [DataRow(8, 0, 38, 1000, 1)]
    public void Single_component_reward_has_no_synthetic_zero_expense(int karma, int nuyen, int expectedKarma, int expectedNuyen, int expenseCount)
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service, request => request with { KarmaAmount = karma, NuyenAmount = nuyen });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(command).Outcome);
        var snapshot = service.Read(WorkspaceId).Snapshot!;
        Assert.AreEqual(expectedKarma, snapshot.AvailableKarma);
        Assert.AreEqual((decimal)expectedNuyen, snapshot.AvailableNuyen);
        Assert.AreEqual(expenseCount, snapshot.Expenses.Count);
    }

    [TestMethod]
    [DataRow(0, 0)]
    [DataRow(-1, 12500)]
    [DataRow(8, -1)]
    [DataRow(10000000, 12500)]
    [DataRow(8, 10000000)]
    public void Invalid_reward_amounts_reject_entire_bundle(int karma, int nuyen)
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var result = service.Commit(Command(service) with { KarmaAmount = karma, NuyenAmount = nuyen });
        Assert.IsFalse(result.Outcome is CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Stale_or_dirty_workspace_never_reprepares_against_changed_balances()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        Assert.IsTrue(fixture.Store.ReplaceWorkspaceDocument(WorkspaceId, 1, Document()).Success);
        Assert.AreNotEqual(CharacterAfterRunRewardOutcome.Available, service.Read(WorkspaceId).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Commit(command).Outcome);
        Assert.IsTrue(fixture.Store.SaveCheckpoint(WorkspaceId, 2).Success);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Commit(command).Outcome);
        Assert.AreEqual(30, service.Read(WorkspaceId).Snapshot!.AvailableKarma);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Existing_recorded_gain_selection_and_mixed_bundle_do_not_duplicate_rewards(bool bothRecorded)
    {
        Guid karmaId = Guid.Parse("aaaaaaaa-1111-4111-8111-111111111111");
        Guid nuyenId = Guid.Parse("bbbbbbbb-2222-4222-8222-222222222222");
        XDocument xml = XDocument.Parse(Document().Content);
        xml.Root!.Element("karma")!.Value = "38";
        xml.Root.Element("expenses")!.Add(ManualGain(karmaId, "Karma", 8));
        if (bothRecorded)
        {
            xml.Root.Element("nuyen")!.Value = "13500";
            xml.Root.Element("expenses")!.Add(ManualGain(nuyenId, "Nuyen", 12500));
        }
        using var fixture = new Fixture(Document() with { State = Document().State with { Payload = xml.ToString() } });
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service, request => request with
        {
            ExistingKarmaExpenseId = karmaId,
            ExistingNuyenExpenseId = bothRecorded ? nuyenId : null,
            Reason = "Associate already recorded run rewards"
        });

        var result = service.Commit(command);

        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, result.Outcome);
        Assert.AreEqual(38, result.Receipt!.KarmaBefore);
        Assert.AreEqual(38, result.Receipt.KarmaAfter);
        Assert.AreEqual(13500m, result.Receipt.NuyenAfter);
        Assert.AreEqual(karmaId, result.Receipt.KarmaExpenseId);
        Assert.AreEqual(bothRecorded ? 2 : 1, result.Receipt.Before.SelectedExpenses.Count);
        if (bothRecorded) Assert.AreEqual(nuyenId, result.Receipt.NuyenExpenseId);
        var snapshot = service.Read(WorkspaceId).Snapshot!;
        Assert.AreEqual(2, snapshot.Expenses.Count);
        Assert.AreEqual("Earlier manually recorded gain", snapshot.Expenses.Single(e => e.ExpenseId == karmaId).Reason);
        var duplicate = Request() with
        {
            OperationId = Guid.NewGuid(), RewardId = Guid.NewGuid(),
            ExistingKarmaExpenseId = result.Receipt.KarmaExpenseId,
            ExistingNuyenExpenseId = result.Receipt.NuyenExpenseId
        };
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, service.Preview(duplicate).Outcome);
        Assert.AreEqual(2L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("amount")]
    [DataRow("type")]
    [DataRow("refund")]
    [DataRow("undo")]
    public void Selected_expense_requires_exact_positive_gain_fields(string mutation)
    {
        Guid id = Guid.NewGuid();
        XElement expense = ManualGain(id, "Karma", 8);
        if (mutation == "amount") expense.Element("amount")!.Value = "7";
        if (mutation == "type") expense.Element("type")!.Value = "Nuyen";
        if (mutation == "refund") expense.Element("refund")!.Value = "True";
        if (mutation == "undo") expense.Element("undo")!.Element("karmatype")!.Value = "ManualSubtract";
        XDocument xml = XDocument.Parse(Document().Content);
        xml.Root!.Element("expenses")!.Add(expense);
        using var fixture = new Fixture(Document() with { State = Document().State with { Payload = xml.ToString() } });
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var result = service.Preview(Request() with { ExistingKarmaExpenseId = id });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Conflict, result.Outcome);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("balance")]
    [DataRow("unrelated")]
    [DataRow("metadata")]
    [DataRow("sibling")]
    public void Direct_atomic_store_rejects_smuggled_payload_envelope_or_sibling_lane(string mutation)
    {
        using var fixture = new Fixture();
        var wrapper = new FaultingStore(fixture.Store)
        {
            TransformReplacement = replacement => mutation switch
            {
                "balance" => replacement with { State = replacement.State with { Payload = replacement.Content.Replace("<karma>38</karma>", "<karma>999</karma>", StringComparison.Ordinal) } },
                "unrelated" => replacement with { State = replacement.State with { Payload = replacement.Content.Replace("Keep unrelated runner state", "Smuggled edit", StringComparison.Ordinal) } },
                "metadata" => replacement with { State = replacement.State with { SchemaVersion = 2 } },
                "sibling" => replacement with { State = replacement.State with { AuxiliaryState = replacement.AuxiliaryState with { CharacterCreationFinalizationReceipts = [] } } },
                _ => throw new InvalidOperationException()
            }
        };
        var service = new WorkspaceCharacterAfterRunRewardService(wrapper);
        var result = service.Commit(Command(service));
        Assert.IsFalse(result.Outcome is CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed);
        Assert.AreEqual(0, wrapper.SuccessfulCommits);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(Document(), fixture.Store.Get(WorkspaceId).Value!.Document);
    }

    [TestMethod]
    public void Generic_or_auxiliary_replacement_cannot_remove_reward_history()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(Command(service)).Outcome);
        var saved = fixture.Store.Get(WorkspaceId).Value!;
        var stripped = saved.Document with
        {
            State = saved.Document.State with
            {
                AuxiliaryState = saved.Document.AuxiliaryState with { CharacterAfterRunRewardReceipts = null }
            }
        };
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndCheckpoint(WorkspaceId, 2, stripped).Success);
        Assert.IsFalse(fixture.Store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            WorkspaceId, 2, saved.Document.AuxiliaryStateDigest, stripped).Success);
        Assert.AreEqual(2L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Store_without_explicit_atomic_capability_never_composes_separate_reward_writes()
    {
        var store = new InMemoryWorkspaceStore();
        Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, Document()).Success);
        Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
        var service = new WorkspaceCharacterAfterRunRewardService(store);
        using var fixture = new Fixture();
        var command = Command(new WorkspaceCharacterAfterRunRewardService(fixture.Store));
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Unavailable, service.Commit(command).Outcome);
        Assert.AreEqual(1L, store.Get(WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(Document(), store.Get(WorkspaceId).Value!.Document);
    }

    [TestMethod]
    public void Unconfirmed_command_or_wrong_source_or_auxiliary_binding_never_writes()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        foreach (var invalid in new[]
                 {
                     command with { ExplicitlyConfirmed = false },
                     command with { ExpectedSourceDigest = new string('a', 64) },
                     command with { ExpectedAuxiliaryStateDigest = new string('b', 64) }
                 })
        {
            var result = service.Commit(invalid);
            Assert.IsFalse(result.Outcome is CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed);
        }
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Duplicate_saved_expense_identity_is_corrupt()
    {
        XDocument xml = XDocument.Parse(Document().Content);
        Guid id = Guid.NewGuid();
        xml.Root!.Element("expenses")!.Add(ManualGain(id, "Karma", 8), ManualGain(id, "Nuyen", 12500));
        using var fixture = new Fixture(Document() with { State = Document().State with { Payload = xml.ToString() } });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt,
            new WorkspaceCharacterAfterRunRewardService(fixture.Store).Read(WorkspaceId).Outcome);
    }

    [TestMethod]
    [DataRow("karma")]
    [DataRow("nuyen")]
    public void Missing_balance_is_not_defaulted_to_authoritative_zero(string name)
    {
        XDocument xml = XDocument.Parse(Document().Content);
        xml.Root!.Element(name)!.Remove();
        using var fixture = new Fixture(Document() with { State = Document().State with { Payload = xml.ToString() } });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt,
            new WorkspaceCharacterAfterRunRewardService(fixture.Store).Read(WorkspaceId).Outcome);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("balance")]
    [DataRow("unrelated")]
    public void Recomputed_receipt_hash_does_not_authorize_a_different_character_transition(string mutation)
    {
        using var fixture = new Fixture();
        var wrapper = new FaultingStore(fixture.Store)
        {
            TransformReplacement = replacement =>
            {
                string forgedPayload = mutation == "balance"
                    ? replacement.Content.Replace("<karma>38</karma>", "<karma>999</karma>", StringComparison.Ordinal)
                    : replacement.Content.Replace("Keep unrelated runner state", "Smuggled rehashed edit", StringComparison.Ordinal);
                Assert.AreNotEqual(replacement.Content, forgedPayload);
                var original = replacement.AuxiliaryState.CharacterAfterRunRewardReceipts!.Single();
                var forged = original with
                {
                    CharacterPayloadDigestAfter = CharacterAfterRunRewardProjector.PayloadDigest(forgedPayload)
                };
                forged = forged with { ReceiptDigest = CharacterAfterRunRewardProjector.ReceiptDigest(forged) };
                return replacement with
                {
                    State = replacement.State with
                    {
                        Payload = forgedPayload,
                        AuxiliaryState = replacement.AuxiliaryState with { CharacterAfterRunRewardReceipts = new[] { forged } }
                    }
                };
            }
        };
        var service = new WorkspaceCharacterAfterRunRewardService(wrapper);
        var result = service.Commit(Command(service));
        Assert.IsFalse(result.Outcome is CharacterAfterRunRewardOutcome.Applied or CharacterAfterRunRewardOutcome.Replayed);
        Assert.AreEqual(0, wrapper.SuccessfulCommits);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Simultaneous_commits_use_store_cas_without_replay_or_duplicate_reward(bool sameOperation)
    {
        using var fixture = new Fixture();
        using var barrier = new Barrier(2);
        var wrapper = new FaultingStore(fixture.Store)
        {
            BeforeCommit = () => Assert.IsTrue(barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "Both calls must reach the same pre-CAS boundary.")
        };
        var service = new WorkspaceCharacterAfterRunRewardService(wrapper);
        var command = Command(service);
        var secondCommand = sameOperation ? command : Command(service, request => request with { OperationId = Guid.NewGuid() });
        CharacterAfterRunRewardResult? first = null;
        CharacterAfterRunRewardResult? second = null;
        Exception? firstError = null;
        Exception? secondError = null;
        var firstThread = new Thread(() =>
        {
            try { first = service.Commit(command); }
            catch (Exception error) { firstError = error; }
        });
        var secondThread = new Thread(() =>
        {
            try { second = service.Commit(secondCommand); }
            catch (Exception error) { secondError = error; }
        });
        firstThread.Start();
        secondThread.Start();
        Assert.IsTrue(firstThread.Join(TimeSpan.FromSeconds(20)));
        Assert.IsTrue(secondThread.Join(TimeSpan.FromSeconds(20)));
        Assert.IsNull(firstError);
        Assert.IsNull(secondError);
        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        CollectionAssert.AreEquivalent(
            new[] { CharacterAfterRunRewardOutcome.Applied, sameOperation ? CharacterAfterRunRewardOutcome.Replayed : CharacterAfterRunRewardOutcome.Conflict },
            new[] { first.Outcome, second.Outcome });
        Assert.AreEqual(1, wrapper.SuccessfulCommits);
        var coldStore = new FileWorkspaceStore(fixture.DirectoryPath);
        var snapshot = new WorkspaceCharacterAfterRunRewardService(coldStore).Read(WorkspaceId).Snapshot!;
        Assert.AreEqual(38, snapshot.AvailableKarma);
        Assert.AreEqual(13500m, snapshot.AvailableNuyen);
        Assert.AreEqual(2, snapshot.Expenses.Count);
        Assert.AreEqual(2L, coldStore.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Contradictory_saved_revision_cannot_be_used_as_durable_receipt_proof()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var command = Command(service);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, service.Commit(command).Outcome);
        var contradictory = new FaultingStore(fixture.Store)
        {
            TransformRead = read => read.Value is null ? read : read with
            {
                Value = read.Value with { SavedRevision = 1 }
            }
        };
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt,
            new WorkspaceCharacterAfterRunRewardService(contradictory)
                .Lookup(WorkspaceId, command.OperationId, command.CommandDigest()).Outcome);
    }

    [TestMethod]
    public void Generated_payload_cannot_cross_reader_limit_after_an_admitted_source()
    {
        const int maximumCharacters = (int)CharacterAfterRunRewardProjector.MaximumCharacterXmlLength;
        const string originalNotes = "Keep unrelated runner state";
        string source = Document().Content.Replace(originalNotes,
            new string('x', maximumCharacters - 1024 - Document().Content.Length + originalNotes.Length),
            StringComparison.Ordinal);
        Assert.AreEqual(maximumCharacters - 1024, source.Length);
        var document = Document() with { State = Document().State with { Payload = source } };
        var saved = new WorkspaceStoredDocument(WorkspaceId, document, 1, 1, DateTimeOffset.UnixEpoch);
        Assert.IsTrue(CharacterAfterRunRewardProjector.TryRead(saved, out _, out _));
        using var fixture = new Fixture();
        var wrapper = new FaultingStore(fixture.Store)
        {
            TransformRead = _ => new WorkspaceStoreReadResult(WorkspaceOperationOutcome.Success, saved)
        };
        var result = new WorkspaceCharacterAfterRunRewardService(wrapper)
            .Preview(Request() with { Reason = new string('R', 2048) });
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Unavailable, result.Outcome);
        Assert.IsNull(result.Preview);
        Assert.AreEqual("reward_output_size_exceeded", result.Error);
        Assert.AreEqual(0, wrapper.SuccessfulCommits);
        Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Reward_receipts_do_not_copy_the_growing_unrelated_expense_ledger()
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var sizes = new List<int>();
        for (int index = 0; index < 20; index++)
        {
            var command = Command(service, request => request with { OperationId = Guid.NewGuid(), RewardId = Guid.NewGuid() });
            var result = service.Commit(command);
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, result.Outcome);
            Assert.AreEqual(0, result.Receipt!.Before.SelectedExpenses.Count);
            sizes.Add(JsonSerializer.Serialize(result.Receipt).Length);
        }
        Assert.IsTrue(sizes.Max() - sizes.Min() < 100, "Unrelated historical expenses must not accumulate inside each receipt.");
        Assert.AreEqual(40, service.Read(WorkspaceId).Snapshot!.Expenses.Count);
        Assert.AreEqual(20, fixture.Store.Get(WorkspaceId).Value!.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
    }

    [TestMethod]
    [DataRow("digest")]
    [DataRow("duplicate")]
    [DataRow("null")]
    [DataRow("rehash_balance")]
    [DataRow("duplicate_reward")]
    [DataRow("duplicate_association")]
    public void Corrupt_reward_receipt_history_is_never_empty_or_replayable(string mutation)
    {
        using var fixture = new Fixture();
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var firstCommand = Command(service);
        var first = service.Commit(firstCommand).Receipt!;
        var firstKarma = service.Read(WorkspaceId).Snapshot!.Expenses.Single(e => e.ExpenseId == first.KarmaExpenseId);
        var secondCommand = Command(service, request => request with { OperationId = Guid.NewGuid(), RewardId = Guid.NewGuid() });
        var second = service.Commit(secondCommand).Receipt!;
        CharacterAfterRunRewardReceipt[] corrupt;
        if (mutation == "null") corrupt = [null!];
        else if (mutation == "duplicate") corrupt = [first, first];
        else
        {
            var forged = second;
            if (mutation == "digest") forged = forged with { ReceiptDigest = new string('a', 64) };
            if (mutation == "rehash_balance") forged = forged with { KarmaAfter = 999 };
            if (mutation == "duplicate_reward")
            {
                var changed = secondCommand with { RewardId = first.RewardId };
                forged = forged with { RewardId = first.RewardId, Command = changed, CommandDigest = changed.CommandDigest() };
            }
            if (mutation == "duplicate_association")
            {
                var changed = secondCommand with { ExistingKarmaExpenseId = first.KarmaExpenseId };
                forged = forged with
                {
                    KarmaExpenseId = first.KarmaExpenseId,
                    KarmaAfter = forged.KarmaBefore,
                    Command = changed,
                    CommandDigest = changed.CommandDigest(),
                    Before = forged.Before with { SelectedExpenses = new[] { firstKarma } }
                };
            }
            if (mutation is "duplicate_reward" or "duplicate_association")
            {
                // Make the forged receipt internally self-consistent so this
                // regression reaches global identity guards, not a stale hash.
                var forgedPreview = new CharacterAfterRunRewardPreview(
                    forged.Command with { ExplicitlyConfirmed = false, ExpectedPreviewDigest = null },
                    forged.KarmaBefore, forged.KarmaAfter, forged.NuyenBefore, forged.NuyenAfter,
                    forged.KarmaExpenseId, forged.NuyenExpenseId,
                    forged.Command.ExistingKarmaExpenseId.HasValue,
                    forged.Command.ExistingNuyenExpenseId.HasValue, string.Empty);
                var rebound = forged.Command with
                {
                    ExpectedPreviewDigest = CharacterAfterRunRewardProjector.ComputePreviewDigest(forgedPreview)
                };
                forged = forged with { Command = rebound, CommandDigest = rebound.CommandDigest() };
            }
            if (mutation != "digest") forged = forged with
            {
                ReceiptDigest = CharacterAfterRunRewardReceiptLedgerIntegrity.ComputeReceiptDigest(forged)
            };
            if (mutation is "duplicate_reward" or "duplicate_association")
                Assert.IsTrue(CharacterAfterRunRewardReceiptLedgerIntegrity.IsCoherent(WorkspaceId, forged));
            corrupt = [first, forged];
        }
        Assert.IsFalse(CharacterAfterRunRewardReceiptLedgerIntegrity.IsValidLedger(WorkspaceId, 3, corrupt));
        var wrapper = new FaultingStore(fixture.Store)
        {
            TransformRead = read => read.Value is null ? read : read with
            {
                Value = read.Value with
                {
                    Document = read.Value.Document with
                    {
                        State = read.Value.Document.State with
                        {
                            AuxiliaryState = read.Value.Document.AuxiliaryState with { CharacterAfterRunRewardReceipts = corrupt }
                        }
                    }
                }
            }
        };
        var corruptedService = new WorkspaceCharacterAfterRunRewardService(wrapper);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt, corruptedService.Read(WorkspaceId).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt,
            corruptedService.Lookup(WorkspaceId, firstCommand.OperationId, firstCommand.CommandDigest()).Outcome);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Corrupt, corruptedService.Commit(firstCommand).Outcome);
        Assert.AreEqual(3L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    public void Pure_association_capacity_rejection_preserves_complete_existing_evidence_and_workspace()
    {
        // '<' exercises the actual default JSON escaping budget (six UTF-8
        // bytes per character) without lowering the existing manual reason limit.
        string longReason = new('<', CharacterCareerManualKarmaRules.MaximumReasonLength);
        Guid[] ids = Enumerable.Range(0, 12).Select(_ => Guid.NewGuid()).ToArray();
        XDocument xml = XDocument.Parse(Document().Content);
        xml.Root!.Element("karma")!.Value = "126";
        foreach (Guid id in ids)
        {
            XElement expense = ManualGain(id, "Karma", 8);
            expense.Element("reason")!.Value = longReason;
            xml.Root.Element("expenses")!.Add(expense);
        }
        var document = Document() with { State = Document().State with { Payload = xml.ToString() } };
        using var fixture = new Fixture(document);
        var service = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        int applied = 0;
        bool rejected = false;
        foreach (Guid id in ids)
        {
            var before = fixture.Store.Get(WorkspaceId).Value!;
            var request = Request() with
            {
                OperationId = Guid.NewGuid(), RewardId = Guid.NewGuid(),
                NuyenAmount = 0, ExistingKarmaExpenseId = id, Reason = longReason
            };
            var result = service.Preview(request);
            if (result.Outcome == CharacterAfterRunRewardOutcome.Available)
            {
                Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied,
                    service.Commit(result.Preview!.Command with { ExplicitlyConfirmed = true }).Outcome);
                applied++;
                Assert.AreEqual(document.Content, fixture.Store.Get(WorkspaceId).Value!.Document.Content);
                continue;
            }
            Assert.AreEqual(CharacterAfterRunRewardOutcome.Unavailable, result.Outcome);
            Assert.AreEqual("reward_receipt_capacity_exhausted", result.Error);
            var after = new FileWorkspaceStore(fixture.DirectoryPath).Get(WorkspaceId).Value!;
            Assert.AreEqual(before.ContentRevision, after.ContentRevision);
            Assert.AreEqual(before.SavedRevision, after.SavedRevision);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
            Assert.AreEqual(before.Document.Content, after.Document.Content);
            Assert.AreEqual(applied, after.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
            Assert.IsTrue(JsonSerializer.SerializeToUtf8Bytes(after.Document.AuxiliaryState.CharacterAfterRunRewardReceipts).Length
                <= CharacterAfterRunRewardReceiptLedgerIntegrity.MaximumLedgerUtf8Bytes);
            Assert.AreEqual(longReason, after.Document.AuxiliaryState.CharacterAfterRunRewardReceipts[0].Command.Reason);
            rejected = true;
            break;
        }
        Assert.IsTrue(applied > 0 && applied < ids.Length);
        Assert.IsTrue(rejected, "Pure associations must hit the auxiliary byte cap even though character XML never grows.");
    }

    [TestMethod]
    public void Creation_or_non_sr5_workspace_cannot_receive_reward_bundle()
    {
        foreach (var invalidDocument in new[]
                 {
                     Document() with { State = Document().State with { Payload = Document().Content.Replace("<created>True</created>", "<created>False</created>", StringComparison.Ordinal) } },
                     Document() with { State = Document().State with { RulesetId = "sr6" } }
                 })
        {
            using var fixture = new Fixture(invalidDocument);
            var result = new WorkspaceCharacterAfterRunRewardService(fixture.Store).Read(WorkspaceId);
            Assert.AreNotEqual(CharacterAfterRunRewardOutcome.Available, result.Outcome);
            Assert.AreEqual(1L, fixture.Store.Get(WorkspaceId).Value!.ContentRevision);
        }
    }

    [TestMethod]
    public void Rewards_then_existing_contact_settlement_finish_at_27_karma_13500_nuyen_with_three_expenses()
    {
        using var fixture = new Fixture();
        var rewards = new WorkspaceCharacterAfterRunRewardService(fixture.Store);
        var rewardCommand = Command(rewards);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Applied, rewards.Commit(rewardCommand).Outcome);
        var input = CharacterAfterRunSettlementRulesTests.Input();
        var settlement = new CharacterAfterRunSettlementService(
            new WorkspaceCharacterAfterRunSettlementWorkspace(fixture.Store, new SettlementSource(input)));
        var binding = settlement.Quote(new CharacterAfterRunSettlementQuoteRequest(WorkspaceId, input.Identity)).Binding!;
        Assert.IsNotNull(binding);
        var command = new CharacterAfterRunSettlementCommand(
            CharacterAfterRunSettlementServiceSchemas.CommandV1, WorkspaceId, binding.WorkspaceRevision,
            binding.Identity, binding.Quote.SourceDigest, binding.Quote.CustomDataDigest,
            binding.Quote.GmPolicyDigest, binding.Quote.RuntimeDigest, binding.Quote.LogicalDigest,
            binding.BindingDigest, Guid.NewGuid(), ExplicitlyConfirmed: true);

        var result = settlement.Settle(command);

        Assert.AreEqual(CharacterAfterRunSettlementServiceOutcome.Applied, result.Outcome);
        Assert.AreEqual(27, result.Receipt!.KarmaAfter);
        var saved = new FileWorkspaceStore(fixture.DirectoryPath).Get(WorkspaceId).Value!;
        Assert.AreEqual(3L, saved.ContentRevision);
        Assert.AreEqual(3L, saved.SavedRevision);
        var xml = XDocument.Parse(saved.Document.Content);
        Assert.AreEqual("27", xml.Root!.Element("karma")!.Value);
        Assert.AreEqual("13500", xml.Root.Element("nuyen")!.Value);
        Assert.AreEqual(3, xml.Root.Element("expenses")!.Elements("expense").Count());
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunRewardReceipts!.Count);
        Assert.AreEqual(1, saved.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts!.Count);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Replayed,
            new WorkspaceCharacterAfterRunRewardService(new FileWorkspaceStore(fixture.DirectoryPath)).Commit(rewardCommand).Outcome);
    }

    private static XElement ManualGain(Guid id, string type, int amount) => new("expense",
        new XElement("guid", id.ToString("D")), new XElement("date", "2078-09-05T12:00:00"),
        new XElement("amount", amount), new XElement("reason", "Earlier manually recorded gain"),
        new XElement("type", type), new XElement("refund", "False"), new XElement("forcecareervisible", "False"),
        new XElement("undo", new XElement("karmatype", type == "Karma" ? "ManualAdd" : "ImproveAttribute"),
            new XElement("nuyentype", type == "Nuyen" ? "ManualAdd" : "AddCyberware"),
            new XElement("objectid"), new XElement("qty", "0"), new XElement("extra")));

    private static CharacterAfterRunRewardPreviewRequest Request()
        => new(WorkspaceId, OperationId, RewardId, 8, 12500, ExpenseDate, "After Run reward");

    private static CharacterAfterRunRewardCommand Command(WorkspaceCharacterAfterRunRewardService service,
        Func<CharacterAfterRunRewardPreviewRequest, CharacterAfterRunRewardPreviewRequest>? transform = null)
    {
        var request = Request();
        var result = service.Preview(transform?.Invoke(request) ?? request);
        Assert.AreEqual(CharacterAfterRunRewardOutcome.Available, result.Outcome, result.Error);
        Assert.IsNotNull(result.Preview);
        Assert.IsFalse(result.Preview.Command.ExplicitlyConfirmed);
        return result.Preview.Command with { ExplicitlyConfirmed = true };
    }

    private static WorkspaceDocument Document() => new(
        """
        <character>
          <created>True</created><karma>30</karma><nuyen>1000</nuyen>
          <streetcred>10</streetcred><notoriety>4</notoriety><publicawareness>6</publicawareness>
          <contacts /><expenses /><notes>Keep unrelated runner state</notes>
        </character>
        """, "sr5");

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Directory.CreateTempSubdirectory("chummer-after-run-reward-tests-").FullName;
        public FileWorkspaceStore Store { get; }

        public Fixture(WorkspaceDocument? document = null)
        {
            Store = new FileWorkspaceStore(DirectoryPath);
            Assert.IsTrue(Store.CreateWorkspaceDocument(WorkspaceId, document ?? Document()).Success);
            Assert.IsTrue(Store.SaveCheckpoint(WorkspaceId, 1).Success);
        }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }

    private sealed class BeforeReplaceFault : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
                throw new IOException("Simulated process loss before replacement.");
        }
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

    private sealed class FaultingStore(IWorkspaceStore inner) : IWorkspaceStore, IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        private int _readCount;
        public int ReadCount => _readCount;
        public Action? BeforeCommit { get; init; }
        public Func<WorkspaceStoreMutationResult, WorkspaceStoreMutationResult>? AfterCommit { get; init; }
        public Func<WorkspaceDocument, WorkspaceDocument>? TransformReplacement { get; init; }
        public Func<WorkspaceStoreReadResult, WorkspaceStoreReadResult>? TransformRead { get; init; }
        public int SuccessfulCommits { get; private set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            Interlocked.Increment(ref _readCount);
            var read = inner.Get(id);
            return TransformRead?.Invoke(read) ?? read;
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id) => inner.Get(owner, id);
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            CharacterWorkspaceId id, long revision, string digest, WorkspaceDocument document)
        {
            BeforeCommit?.Invoke();
            var result = inner.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id, revision, digest,
                TransformReplacement?.Invoke(document) ?? document);
            if (!result.Success) return result;
            SuccessfulCommits++;
            return AfterCommit?.Invoke(result) ?? result;
        }
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
}
