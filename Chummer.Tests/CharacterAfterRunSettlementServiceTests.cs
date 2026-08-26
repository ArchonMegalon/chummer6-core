using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterAfterRunSettlementServiceTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("workspace-41");
    private static readonly Guid TransactionId =
        Guid.Parse("44444444-4444-4444-4444-444444444444");

    [TestMethod]
    public void Quote_binds_authoritative_projection_to_workspace_revision()
    {
        var workspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41);
        var service = new CharacterAfterRunSettlementService(workspace);

        CharacterAfterRunSettlementQuoteResult result = service.Quote(
            new CharacterAfterRunSettlementQuoteRequest(
                WorkspaceId,
                workspace.Input.Identity));

        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Available,
            result.Outcome);
        Assert.IsNotNull(result.Binding);
        Assert.AreEqual(41L, result.Binding!.WorkspaceRevision);
        Assert.AreEqual(11, result.Binding.Quote.ContactKarmaCost);
        Assert.IsTrue(CharacterAfterRunSettlementRules.IsCanonicalDigest(
            result.Binding.BindingDigest));

        workspace.Input = workspace.Input with { HeatDelta = 3 };
        CharacterAfterRunSettlementQuoteBinding rebound = service.Quote(
            new CharacterAfterRunSettlementQuoteRequest(
                WorkspaceId,
                workspace.Input.Identity)).Binding!;
        Assert.AreNotEqual(result.Binding.BindingDigest, rebound.BindingDigest);
    }

    [TestMethod]
    public void Settlement_is_atomic_and_replays_before_stale_revision()
    {
        var workspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41);
        var service = new CharacterAfterRunSettlementService(workspace);
        CharacterAfterRunSettlementCommand command = Command(service, workspace);

        CharacterAfterRunSettlementResult applied = service.Settle(command);

        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Applied,
            applied.Outcome);
        Assert.AreEqual(42L, applied.CurrentWorkspaceRevision);
        Assert.AreEqual(19, applied.Receipt!.KarmaAfter);
        Assert.AreEqual(3, applied.Receipt.HeatAfter);
        Assert.AreEqual(2, applied.Receipt.AddedContacts.Count);
        Assert.AreEqual(1, workspace.ApplyCount);
        Assert.IsTrue(
            CharacterAfterRunSettlementServiceIntegrity.TryComputeResultDigest(
                applied with { ResultDigest = string.Empty },
                out string expectedResultDigest));
        Assert.AreEqual(expectedResultDigest, applied.ResultDigest);

        CharacterAfterRunSettlementResult replayed = service.Settle(command);
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Replayed,
            replayed.Outcome);
        Assert.AreEqual(applied.Receipt, replayed.Receipt);
        Assert.AreEqual(1, workspace.ApplyCount);
    }

    [TestMethod]
    public void Transaction_collision_and_stale_or_tampered_binding_fail_closed()
    {
        var workspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41);
        var service = new CharacterAfterRunSettlementService(workspace);
        CharacterAfterRunSettlementCommand command = Command(service, workspace);
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Applied,
            service.Settle(command).Outcome);

        CharacterAfterRunSettlementResult collision = service.Settle(command with
        {
            ExplicitlyConfirmed = false
        });
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.IdempotencyConflict,
            collision.Outcome);

        foreach (Func<CharacterAfterRunSettlementCommand,
                     CharacterAfterRunSettlementCommand> tamper in new Func<
                     CharacterAfterRunSettlementCommand,
                     CharacterAfterRunSettlementCommand>[]
                 {
                     value => value with
                     {
                         ExpectedBindingDigest = new string('0', 64)
                     },
                     value => value with
                     {
                         ExpectedCustomDataDigest = new string('1', 64)
                     },
                     value => value with
                     {
                         ExpectedWorkspaceRevision =
                             value.ExpectedWorkspaceRevision + 1
                     },
                     value => value with { ExplicitlyConfirmed = false }
                 })
        {
            var isolated = new AtomicWorkspace(
                CharacterAfterRunSettlementRulesTests.Input(),
                revision: 41);
            var isolatedService = new CharacterAfterRunSettlementService(isolated);
            CharacterAfterRunSettlementResult result = isolatedService.Settle(
                tamper(Command(isolatedService, isolated)));
            Assert.IsTrue(result.Outcome
                is CharacterAfterRunSettlementServiceOutcome.Conflict
                or CharacterAfterRunSettlementServiceOutcome.Blocked);
            Assert.AreEqual(0, isolated.ApplyCount);
        }
    }

    [TestMethod]
    public void Indeterminate_commit_recovers_exact_written_receipt_by_lookup()
    {
        var workspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41)
        {
            ReturnIndeterminateAfterWrite = true
        };
        var service = new CharacterAfterRunSettlementService(workspace);

        CharacterAfterRunSettlementResult result = service.Settle(
            Command(service, workspace));

        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Replayed,
            result.Outcome);
        Assert.IsNotNull(result.Receipt);
        Assert.AreEqual(42L, result.CurrentWorkspaceRevision);
        Assert.AreEqual(1, workspace.ApplyCount);
        Assert.AreEqual(2, workspace.LookupCount,
            "The service must resolve an unknown write via the durable ledger.");
    }

    [TestMethod]
    public void Indeterminate_without_durable_receipt_remains_unavailable()
    {
        var workspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41)
        {
            ReturnIndeterminateWithoutWrite = true
        };
        var service = new CharacterAfterRunSettlementService(workspace);

        CharacterAfterRunSettlementResult result = service.Settle(
            Command(service, workspace));

        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Unavailable,
            result.Outcome);
        Assert.IsNull(result.Receipt);
        Assert.AreEqual("commit_outcome_unresolved", result.Blockers.Single());
        Assert.AreEqual(0, workspace.ApplyCount);
    }

    [TestMethod]
    public void Forged_receipt_and_unapproved_projection_fail_closed()
    {
        var forgedWorkspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input(),
            revision: 41)
        {
            ForgeReturnedReceipt = true
        };
        var forgedService = new CharacterAfterRunSettlementService(forgedWorkspace);
        CharacterAfterRunSettlementResult forged = forgedService.Settle(
            Command(forgedService, forgedWorkspace));
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Corrupt,
            forged.Outcome);
        Assert.IsNull(forged.Receipt);

        var pendingWorkspace = new AtomicWorkspace(
            CharacterAfterRunSettlementRulesTests.Input() with
            {
                OwnerReview = null
            },
            revision: 41);
        var pendingService = new CharacterAfterRunSettlementService(
            pendingWorkspace);
        CharacterAfterRunSettlementQuoteBinding pendingBinding = pendingService.Quote(
            new CharacterAfterRunSettlementQuoteRequest(
                WorkspaceId,
                pendingWorkspace.Input.Identity)).Binding!;
        CharacterAfterRunSettlementResult blocked = pendingService.Settle(
            Command(pendingBinding));
        Assert.AreEqual(
            CharacterAfterRunSettlementServiceOutcome.Blocked,
            blocked.Outcome);
        Assert.AreEqual(0, pendingWorkspace.ApplyCount);
    }

    private static CharacterAfterRunSettlementCommand Command(
        ICharacterAfterRunSettlementService service,
        AtomicWorkspace workspace)
        => Command(service.Quote(new CharacterAfterRunSettlementQuoteRequest(
            WorkspaceId,
            workspace.Input.Identity)).Binding!);

    private static CharacterAfterRunSettlementCommand Command(
        CharacterAfterRunSettlementQuoteBinding binding)
        => new(
            CharacterAfterRunSettlementServiceSchemas.CommandV1,
            binding.WorkspaceId,
            binding.WorkspaceRevision,
            binding.Identity,
            binding.Quote.SourceDigest,
            binding.Quote.CustomDataDigest,
            binding.Quote.GmPolicyDigest,
            binding.Quote.RuntimeDigest,
            binding.Quote.LogicalDigest,
            binding.BindingDigest,
            TransactionId,
            ExplicitlyConfirmed: true);

    private sealed class AtomicWorkspace :
        ICharacterAfterRunSettlementWorkspace
    {
        private readonly Dictionary<Guid, LedgerEntry> _ledger = [];

        public AtomicWorkspace(
            CharacterAfterRunSettlementInput input,
            long revision)
        {
            Input = input;
            Revision = revision;
        }

        public CharacterAfterRunSettlementInput Input { get; set; }

        public long Revision { get; private set; }

        public int ApplyCount { get; private set; }

        public int LookupCount { get; private set; }

        public bool ReturnIndeterminateAfterWrite { get; init; }

        public bool ReturnIndeterminateWithoutWrite { get; init; }

        public bool ForgeReturnedReceipt { get; init; }

        public CharacterAfterRunSettlementWorkspaceReadResult Read(
            CharacterWorkspaceId workspaceId,
            CharacterAfterRunSettlementIdentity identity)
            => workspaceId == WorkspaceId && identity == Input.Identity
                ? new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Available,
                    Revision,
                    Input)
                : new(CharacterAfterRunSettlementWorkspaceOutcome.Missing);

        public CharacterAfterRunSettlementWorkspaceLookupResult Lookup(
            CharacterWorkspaceId workspaceId,
            Guid transactionId,
            string commandDigest)
        {
            LookupCount++;
            if (workspaceId != WorkspaceId)
                return new(CharacterAfterRunSettlementWorkspaceOutcome.Missing);
            if (!_ledger.TryGetValue(transactionId, out LedgerEntry? entry))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.NotFound,
                    Revision);
            }
            return string.Equals(
                    entry.CommandDigest,
                    commandDigest,
                    StringComparison.Ordinal)
                ? new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Replayed,
                    Revision,
                    entry.CommandDigest,
                    entry.ReviewedQuote,
                    entry.Receipt)
                : new(
                    CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict,
                    Revision,
                    entry.CommandDigest);
        }

        public CharacterAfterRunSettlementWorkspaceCommitResult Commit(
            CharacterAfterRunSettlementWorkspaceCommitRequest request)
        {
            CharacterAfterRunSettlementWorkspaceLookupResult replay = LookupWithoutCount(
                request.WorkspaceId,
                request.Plan.TransactionId,
                request.CommandDigest);
            if (replay.Outcome != CharacterAfterRunSettlementWorkspaceOutcome.NotFound)
            {
                return new(
                    replay.Outcome,
                    replay.CurrentWorkspaceRevision,
                    replay.ExistingCommandDigest,
                    replay.ReviewedQuote,
                    replay.Receipt,
                    replay.Error);
            }
            if (request.ExpectedWorkspaceRevision != Revision)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                    Revision,
                    Error: "stale_workspace_revision");
            }
            if (ReturnIndeterminateWithoutWrite)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate,
                    Revision,
                    Error: "storage_ack_lost");
            }

            CharacterAfterRunSettlementObservation observation =
                CharacterAfterRunSettlementRulesTests.Observation(request.Plan);
            if (!CharacterAfterRunSettlementRules.TryCreateReceipt(
                    request.Plan.TransactionId,
                    request.ReviewedQuote,
                    request.Plan,
                    observation,
                    out CharacterAfterRunSettlementReceipt receipt))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    Revision,
                    Error: "receipt_creation_failed");
            }

            Revision++;
            ApplyCount++;
            Input = Input with { ProposalAlreadySettled = true };
            _ledger.Add(
                request.Plan.TransactionId,
                new LedgerEntry(
                    request.CommandDigest,
                    request.ReviewedQuote,
                    receipt));
            if (ReturnIndeterminateAfterWrite)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate,
                    Revision,
                    Error: "storage_ack_lost");
            }

            CharacterAfterRunSettlementReceipt returned = ForgeReturnedReceipt
                ? receipt with { HeatAfter = receipt.HeatAfter + 1 }
                : receipt;
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Applied,
                Revision,
                request.CommandDigest,
                request.ReviewedQuote,
                returned);
        }

        private CharacterAfterRunSettlementWorkspaceLookupResult LookupWithoutCount(
            CharacterWorkspaceId workspaceId,
            Guid transactionId,
            string commandDigest)
        {
            if (workspaceId != WorkspaceId)
                return new(CharacterAfterRunSettlementWorkspaceOutcome.Missing);
            if (!_ledger.TryGetValue(transactionId, out LedgerEntry? entry))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.NotFound,
                    Revision);
            }
            return string.Equals(
                    entry.CommandDigest,
                    commandDigest,
                    StringComparison.Ordinal)
                ? new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Replayed,
                    Revision,
                    entry.CommandDigest,
                    entry.ReviewedQuote,
                    entry.Receipt)
                : new(
                    CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict,
                    Revision,
                    entry.CommandDigest);
        }

        private sealed record LedgerEntry(
            string CommandDigest,
            CharacterAfterRunSettlementQuote ReviewedQuote,
            CharacterAfterRunSettlementReceipt Receipt);
    }
}
