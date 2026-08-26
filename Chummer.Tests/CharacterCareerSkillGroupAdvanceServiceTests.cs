using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.DependencyInjection;
using Chummer.Infrastructure.Workspaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCareerSkillGroupAdvanceServiceTests
{
    private static readonly CharacterWorkspaceId WorkspaceId = new("workspace-17");
    private static readonly CharacterCareerSkillGroupIdentity Identity = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly Guid TransactionId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [TestMethod]
    public void Quote_is_bound_to_authoritative_projection_workspace_and_revision()
    {
        var workspace = new AtomicWorkspace(Input(), revision: 17);
        var service = new CharacterCareerSkillGroupAdvanceService(workspace);

        CharacterCareerSkillGroupQuoteResult result = service.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, Identity));

        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Available,
            result.Outcome);
        Assert.IsNotNull(result.Binding);
        CharacterCareerSkillGroupQuoteBinding binding = result.Binding!;
        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceSchemas.QuoteV1,
            binding.ContractName);
        Assert.AreEqual(WorkspaceId, binding.WorkspaceId);
        Assert.AreEqual(17L, binding.WorkspaceRevision);
        Assert.AreEqual(Identity, binding.Identity);
        Assert.AreEqual(20, binding.Quote.KarmaCost);
        Assert.IsTrue(
            CharacterCareerSkillGroupAdvanceServiceIntegrity.IsCanonicalDigest(
                binding.BindingDigest));

        workspace.Input = workspace.Input with { AvailableKarma = 19 };
        CharacterCareerSkillGroupQuoteBinding rebound = service.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, Identity)).Binding!;
        Assert.AreNotEqual(binding.BindingDigest, rebound.BindingDigest);
        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceBlocker.InsufficientKarma,
            rebound.Quote.Blocker);
    }

    [TestMethod]
    public void Advance_is_atomic_digest_bound_and_replays_before_stale_revision()
    {
        var workspace = new AtomicWorkspace(Input(), revision: 17);
        var service = new CharacterCareerSkillGroupAdvanceService(workspace);
        CharacterCareerSkillGroupAdvanceCommand command = Command(service);

        CharacterCareerSkillGroupAdvanceResult applied = service.Advance(command);

        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            applied.Outcome);
        Assert.AreEqual(18L, applied.CurrentWorkspaceRevision);
        Assert.AreEqual(20, applied.Receipt!.CharacterKarmaAfter);
        Assert.AreEqual(2, applied.Receipt.GroupKarmaAfter);
        Assert.AreEqual(TransactionId, applied.Receipt.ExpenseId);
        Assert.IsTrue(
            CharacterCareerSkillGroupAdvanceServiceIntegrity.TryComputeResultDigest(
                applied with { ResultDigest = string.Empty },
                out string expectedResultDigest));
        Assert.AreEqual(expectedResultDigest, applied.ResultDigest);
        Assert.AreEqual(1, workspace.ApplyCount);

        CharacterCareerSkillGroupAdvanceResult replayed = service.Advance(command);

        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Replayed,
            replayed.Outcome,
            "Lookup must resolve the receipt before the now-stale expected revision.");
        Assert.AreEqual(applied.Receipt, replayed.Receipt);
        Assert.AreEqual(1, workspace.ApplyCount);
    }

    [TestMethod]
    public void Reused_transaction_with_different_command_is_idempotency_conflict()
    {
        var workspace = new AtomicWorkspace(Input(), revision: 17);
        var service = new CharacterCareerSkillGroupAdvanceService(workspace);
        CharacterCareerSkillGroupAdvanceCommand command = Command(service);
        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Applied,
            service.Advance(command).Outcome);

        CharacterCareerSkillGroupAdvanceResult collision = service.Advance(command with
        {
            ExpenseDateLocal = command.ExpenseDateLocal.AddMinutes(1)
        });

        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.IdempotencyConflict,
            collision.Outcome);
        Assert.AreEqual(1, workspace.ApplyCount);
    }

    [TestMethod]
    public void Tampered_binding_revision_and_missing_confirmation_fail_before_commit()
    {
        foreach (Func<CharacterCareerSkillGroupAdvanceCommand,
                     CharacterCareerSkillGroupAdvanceCommand> tamper in new Func<
                     CharacterCareerSkillGroupAdvanceCommand,
                     CharacterCareerSkillGroupAdvanceCommand>[]
                 {
                     command => command with
                     {
                         ExpectedBindingDigest = new string('0', 64)
                     },
                     command => command with
                     {
                         ExpectedRuleDigest = new string('1', 64)
                     },
                     command => command with
                     {
                         ExpectedWorkspaceRevision = command.ExpectedWorkspaceRevision + 1
                     },
                     command => command with { ExplicitlyConfirmed = false }
                 })
        {
            var workspace = new AtomicWorkspace(Input(), revision: 17);
            var service = new CharacterCareerSkillGroupAdvanceService(workspace);
            CharacterCareerSkillGroupAdvanceResult result = service.Advance(
                tamper(Command(service)));

            Assert.IsTrue(
                result.Outcome is CharacterCareerSkillGroupAdvanceServiceOutcome.Conflict
                    or CharacterCareerSkillGroupAdvanceServiceOutcome.Blocked);
            Assert.AreEqual(0, workspace.ApplyCount);
            Assert.AreEqual(17L, workspace.Revision);
        }
    }

    [TestMethod]
    public void Invalid_backend_receipt_and_unconfigured_backend_fail_closed()
    {
        var hostile = new AtomicWorkspace(Input(), revision: 17)
        {
            ForgeCommitReceipt = true
        };
        var hostileService = new CharacterCareerSkillGroupAdvanceService(hostile);
        CharacterCareerSkillGroupAdvanceResult forged = hostileService.Advance(
            Command(hostileService));
        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Corrupt,
            forged.Outcome);
        Assert.IsNull(forged.Receipt);

        var unavailableService = new CharacterCareerSkillGroupAdvanceService(
            new UnavailableCharacterCareerSkillGroupAdvanceWorkspace());
        CharacterCareerSkillGroupQuoteResult unavailable = unavailableService.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, Identity));
        Assert.AreEqual(
            CharacterCareerSkillGroupAdvanceServiceOutcome.Unavailable,
            unavailable.Outcome);
        Assert.IsNull(unavailable.Binding);
    }

    [TestMethod]
    public void Headless_composition_defaults_closed_and_preserves_host_backend()
    {
        var defaultServices = new ServiceCollection();
        defaultServices.AddChummerHeadlessCore(
            AppContext.BaseDirectory,
            AppContext.BaseDirectory);
        using (ServiceProvider defaultProvider =
               defaultServices.BuildServiceProvider())
        {
            Assert.IsInstanceOfType<
                UnavailableCharacterCareerSkillGroupAdvanceWorkspace>(
                defaultProvider.GetRequiredService<
                    ICharacterCareerSkillGroupAdvanceWorkspace>());
            Assert.IsInstanceOfType<CharacterCareerSkillGroupAdvanceService>(
                defaultProvider.GetRequiredService<
                    ICharacterCareerSkillGroupAdvanceService>());
        }

        var backend = new AtomicWorkspace(Input(), revision: 17);
        var configuredServices = new ServiceCollection();
        configuredServices.AddSingleton<
            ICharacterCareerSkillGroupAdvanceWorkspace>(backend);
        configuredServices.AddChummerHeadlessCore(
            AppContext.BaseDirectory,
            AppContext.BaseDirectory);
        using ServiceProvider configuredProvider =
            configuredServices.BuildServiceProvider();
        Assert.AreSame(
            backend,
            configuredProvider.GetRequiredService<
                ICharacterCareerSkillGroupAdvanceWorkspace>());
    }

    private static CharacterCareerSkillGroupAdvanceCommand Command(
        ICharacterCareerSkillGroupAdvanceService service)
    {
        CharacterCareerSkillGroupQuoteBinding binding = service.Quote(
            new CharacterCareerSkillGroupQuoteRequest(WorkspaceId, Identity)).Binding!;
        return new CharacterCareerSkillGroupAdvanceCommand(
            CharacterCareerSkillGroupAdvanceServiceSchemas.CommandV1,
            WorkspaceId,
            binding.WorkspaceRevision,
            Identity,
            binding.Quote.LogicalRevision,
            binding.Quote.SourceRevision,
            binding.Quote.RuleDigest,
            binding.BindingDigest,
            TransactionId,
            new DateTime(2081, 5, 12, 14, 30, 0, DateTimeKind.Unspecified),
            ExplicitlyConfirmed: true);
    }

    private static CharacterCareerSkillGroupAdvanceInput Input()
        => new(
            Identity,
            Created: true,
            RulesetId: CharacterCareerSkillGroupAdvanceRules.RulesetId,
            TargetOwnedByCharacter: true,
            MemberProjectionIsExact: true,
            Name: "Stealth",
            BasePoints: 2,
            KarmaPoints: 1,
            RatingMaximum: 6,
            AvailableKarma: 40,
            Disabled: false,
            Broken: false,
            new CharacterCareerSkillGroupAdvanceSettings(5, 5),
            Members:
            [
                new(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    3,
                    true,
                    "Physical Active"),
                new(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    3,
                    true,
                    "Physical Active")
            ],
            Modifiers: [],
            RawSourceState: "<skills><skill>Stealth member source</skill></skills>",
            RawRuleState: "settings:v1");

    private sealed class AtomicWorkspace :
        ICharacterCareerSkillGroupAdvanceWorkspace
    {
        private readonly Dictionary<Guid, LedgerEntry> _ledger = [];

        public AtomicWorkspace(
            CharacterCareerSkillGroupAdvanceInput input,
            long revision)
        {
            Input = input;
            Revision = revision;
        }

        public CharacterCareerSkillGroupAdvanceInput Input { get; set; }

        public long Revision { get; private set; }

        public int ApplyCount { get; private set; }

        public bool ForgeCommitReceipt { get; init; }

        public CharacterCareerSkillGroupWorkspaceReadResult Read(
            CharacterWorkspaceId workspaceId,
            CharacterCareerSkillGroupIdentity identity)
            => workspaceId == WorkspaceId && identity == Identity
                ? new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Available,
                    Revision,
                    Input)
                : new(CharacterCareerSkillGroupWorkspaceOutcome.Missing);

        public CharacterCareerSkillGroupWorkspaceLookupResult Lookup(
            CharacterWorkspaceId workspaceId,
            Guid transactionId,
            string commandDigest)
        {
            if (workspaceId != WorkspaceId)
            {
                return new(CharacterCareerSkillGroupWorkspaceOutcome.Missing);
            }
            if (!_ledger.TryGetValue(transactionId, out LedgerEntry? entry))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.NotFound,
                    Revision);
            }
            return string.Equals(
                    entry.CommandDigest,
                    commandDigest,
                    StringComparison.Ordinal)
                ? new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Replayed,
                    Revision,
                    entry.CommandDigest,
                    entry.ReviewedQuote,
                    entry.Receipt)
                : new(
                    CharacterCareerSkillGroupWorkspaceOutcome.IdempotencyConflict,
                    Revision,
                    entry.CommandDigest);
        }

        public CharacterCareerSkillGroupWorkspaceCommitResult Commit(
            CharacterCareerSkillGroupWorkspaceCommitRequest request)
        {
            CharacterCareerSkillGroupWorkspaceLookupResult replay = Lookup(
                request.WorkspaceId,
                request.Plan.TransactionId,
                request.CommandDigest);
            if (replay.Outcome != CharacterCareerSkillGroupWorkspaceOutcome.NotFound)
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
                    CharacterCareerSkillGroupWorkspaceOutcome.Conflict,
                    Revision,
                    Error: "stale_workspace_revision");
            }

            CharacterCareerSkillGroupAdvanceInput postInput = Input with
            {
                KarmaPoints = request.Plan.SavedGroupKarmaPoints,
                AvailableKarma = request.Plan.SavedCharacterKarma,
                Members = Input.Members.Select(member => member.Enabled
                    ? member with
                    {
                        TotalBaseRating = member.TotalBaseRating + 1
                    }
                    : member).ToArray()
            };
            if (!CharacterCareerSkillGroupAdvanceRules.TryCreateQuote(
                    postInput,
                    out CharacterCareerSkillGroupAdvanceQuote postQuote))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    Revision);
            }
            var expense = new CharacterCareerSkillGroupExpenseObservation(
                MatchingEntryCount: 1,
                request.Plan.ExpenseId,
                request.Plan.ExpenseDateLocal,
                request.Plan.ExpenseAmount,
                request.Plan.ExpenseReason,
                ExpenseType: "Karma",
                Refund: false,
                ForceCareerVisible: true,
                request.Plan.KarmaUndoType,
                request.Plan.NuyenUndoType,
                request.Plan.UndoObjectId,
                request.Plan.UndoQuantity,
                request.Plan.UndoExtra);
            if (!CharacterCareerSkillGroupAdvanceRules.TryCreateReceipt(
                    request.Plan.TransactionId,
                    request.ReviewedQuote,
                    request.Plan,
                    postQuote,
                    expense,
                    out CharacterCareerSkillGroupAdvanceReceipt receipt))
            {
                return new(
                    CharacterCareerSkillGroupWorkspaceOutcome.Corrupt,
                    Revision);
            }

            Input = postInput;
            Revision++;
            ApplyCount++;
            _ledger.Add(
                request.Plan.TransactionId,
                new LedgerEntry(request.CommandDigest, request.ReviewedQuote, receipt));
            CharacterCareerSkillGroupAdvanceReceipt returned = ForgeCommitReceipt
                ? receipt with { ExpenseAmount = receipt.ExpenseAmount + 1 }
                : receipt;
            return new(
                CharacterCareerSkillGroupWorkspaceOutcome.Applied,
                Revision,
                request.CommandDigest,
                request.ReviewedQuote,
                returned);
        }

        private sealed record LedgerEntry(
            string CommandDigest,
            CharacterCareerSkillGroupAdvanceQuote ReviewedQuote,
            CharacterCareerSkillGroupAdvanceReceipt Receipt);
    }
}
