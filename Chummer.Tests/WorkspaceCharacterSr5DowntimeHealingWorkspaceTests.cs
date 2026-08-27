using System.Text.Json;
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
public sealed class WorkspaceCharacterSr5DowntimeHealingWorkspaceTests
{
    private static readonly CharacterWorkspaceId WorkspaceId =
        new("sr5-healing-workspace-1");
    private static readonly DateTimeOffset Start =
        new(2081, 5, 12, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Read_reserve_and_start_project_exact_saved_sr5_without_mutating()
    {
        var store = new DurableTestWorkspaceStore(WorkspaceId, Document());
        var workspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(store);

        HealingFlow flow = Begin(workspace, CharacterSr5HealingTrack.Physical);

        Assert.AreEqual(3, flow.Quote.Body);
        Assert.AreEqual(4, flow.Quote.Willpower);
        Assert.AreEqual(5, flow.Quote.DamageBoxesBefore);
        Assert.AreEqual(6, flow.Quote.DicePool);
        Assert.AreEqual(1L, flow.Quote.WorkspaceRevision);
        Assert.AreEqual(1L, flow.Quote.CalendarRevision);
        Assert.AreEqual(1L, store.Current.ContentRevision);
        Assert.IsNull(store.Current.Document.AuxiliaryState.CharacterSr5HealingActivities);
    }

    [TestMethod]
    public void Completion_applies_once_and_reopened_lookup_replays_before_revision_cas()
    {
        var store = new DurableTestWorkspaceStore(WorkspaceId, Document());
        var workspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(store);
        CompletionFlow flow = Completion(workspace);

        CharacterSr5HealingWorkspaceCommitResult applied =
            workspace.CommitCompletion(flow.Request);

        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Applied, applied.Outcome);
        Assert.AreEqual(2L, applied.CurrentWorkspaceRevision);
        Assert.AreEqual(2L, applied.CurrentCalendarRevision);
        Assert.AreEqual(3, applied.Entry!.CompletionReceipt!.DamageBoxesAfter);
        Assert.AreEqual(
            "3",
            XDocument.Parse(store.Current.Document.Content)
                .Root!.Element("physicalcmfilled")!.Value);
        Assert.AreEqual(
            1,
            store.Current.Document.AuxiliaryState.CharacterSr5HealingActivities!.Count);

        var reopenedStore = DurableTestWorkspaceStore.Reopen(store.Export());
        var reopened = new WorkspaceCharacterSr5DowntimeHealingWorkspace(reopenedStore);
        CharacterSr5HealingWorkspaceLookupResult lookup = reopened.Lookup(
            WorkspaceId,
            flow.Command.TransactionId,
            flow.Command.IdempotencyKey,
            flow.Command.CommandDigest);
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Replayed, lookup.Outcome);
        Assert.AreEqual(
            applied.Entry.EntryDigest,
            lookup.Entry!.EntryDigest);

        CharacterSr5HealingWorkspaceCommitResult replay =
            reopened.CommitCompletion(flow.Request);
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Replayed, replay.Outcome);
        Assert.AreEqual(2L, reopenedStore.Current.ContentRevision);
    }

    [TestMethod]
    public void Lost_ack_recovers_durable_completion_receipt()
    {
        var store = new DurableTestWorkspaceStore(WorkspaceId, Document())
        {
            ThrowAfterNextSuccessfulCommit = true
        };
        var workspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(store);
        CompletionFlow flow = Completion(workspace);

        CharacterSr5HealingWorkspaceCommitResult recovered =
            workspace.CommitCompletion(flow.Request);

        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Replayed, recovered.Outcome);
        Assert.IsNotNull(recovered.Entry?.CompletionReceipt);
        Assert.AreEqual(1, store.SuccessfulCommitCount);
        Assert.AreEqual(2L, store.Current.ContentRevision);
    }

    [TestMethod]
    public void File_store_persists_reopens_and_replays_completion()
    {
        string directory = TemporaryDirectory();
        try
        {
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, Document()).Success);
            Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
            var workspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(store);
            CompletionFlow flow = Completion(workspace);

            CharacterSr5HealingWorkspaceCommitResult applied =
                workspace.CommitCompletion(flow.Request);

            Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Applied, applied.Outcome);
            var reopenedStore = new FileWorkspaceStore(directory);
            var reopened = new WorkspaceCharacterSr5DowntimeHealingWorkspace(reopenedStore);
            CharacterSr5HealingWorkspaceLookupResult lookup = reopened.Lookup(
                WorkspaceId,
                flow.Command.TransactionId,
                flow.Command.IdempotencyKey,
                flow.Command.CommandDigest);
            Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Replayed, lookup.Outcome);
            Assert.AreEqual(2L, reopenedStore.Get(WorkspaceId).Value!.SavedRevision);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void File_store_cancellation_advances_calendar_with_zero_refund_and_no_damage_change()
    {
        string directory = TemporaryDirectory();
        try
        {
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(WorkspaceId, Document()).Success);
            Assert.IsTrue(store.SaveCheckpoint(WorkspaceId, 1).Success);
            var workspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(store);
            HealingFlow flow = Begin(workspace, CharacterSr5HealingTrack.Stun);
            Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationQuote(
                flow.Quote,
                flow.Reservation,
                started: null,
                requestedAtUtc: Start.AddMinutes(-1),
                out CharacterSr5HealingCancellationQuote quote));
            Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCancellationCommand(
                quote,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                quote.CancellationQuoteDigest,
                quote.SubjectDigest,
                explicitlyConfirmed: true,
                out CharacterSr5HealingCancellationCommand command));

            CharacterSr5HealingWorkspaceCommitResult applied =
                workspace.CommitCancellation(new(
                    flow.Quote,
                    flow.Reservation,
                    Started: null,
                    quote,
                    command));

            Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Applied, applied.Outcome);
            Assert.AreEqual(2L, applied.CurrentCalendarRevision);
            Assert.AreEqual(0, applied.Entry!.CancellationReceipt!.RefundNuyen);
            Assert.AreEqual(0, applied.Entry.CancellationReceipt.RetainedNuyen);
            Assert.AreEqual(
                "3",
                XDocument.Parse(store.Get(WorkspaceId).Value!.Document.Content)
                    .Root!.Element("stuncmfilled")!.Value);
            Assert.AreEqual(
                CharacterSr5HealingWorkspaceOutcome.Replayed,
                new WorkspaceCharacterSr5DowntimeHealingWorkspace(
                        new FileWorkspaceStore(directory))
                    .Lookup(
                        WorkspaceId,
                        command.TransactionId,
                        command.IdempotencyKey,
                        command.CommandDigest)
                    .Outcome);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Workspace_and_auxiliary_calendar_conflicts_fail_without_damage_mutation()
    {
        var staleStore = new DurableTestWorkspaceStore(WorkspaceId, Document());
        var staleWorkspace = new WorkspaceCharacterSr5DowntimeHealingWorkspace(staleStore);
        CompletionFlow staleFlow = Completion(staleWorkspace);
        staleStore.AdvanceUnrelatedWorkspaceRevision();

        CharacterSr5HealingWorkspaceCommitResult stale =
            staleWorkspace.CommitCompletion(staleFlow.Request);
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Conflict, stale.Outcome);
        Assert.AreEqual("5", Damage(staleStore));

        var calendarStore = new DurableTestWorkspaceStore(WorkspaceId, Document())
        {
            RejectNextAtomicAuxiliaryCas = true
        };
        var calendarWorkspace =
            new WorkspaceCharacterSr5DowntimeHealingWorkspace(calendarStore);
        CompletionFlow calendarFlow = Completion(calendarWorkspace);

        CharacterSr5HealingWorkspaceCommitResult calendar =
            calendarWorkspace.CommitCompletion(calendarFlow.Request);
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Conflict, calendar.Outcome);
        Assert.AreEqual("5", Damage(calendarStore));
        Assert.AreEqual(1L, calendarStore.Current.ContentRevision);
    }

    [TestMethod]
    public void Foreign_workspace_receipt_and_transaction_collision_fail_closed()
    {
        var firstStore = new DurableTestWorkspaceStore(WorkspaceId, Document());
        var first = new WorkspaceCharacterSr5DowntimeHealingWorkspace(firstStore);
        CompletionFlow flow = Completion(first);
        Assert.AreEqual(
            CharacterSr5HealingWorkspaceOutcome.Applied,
            first.CommitCompletion(flow.Request).Outcome);

        var foreignId = new CharacterWorkspaceId("sr5-healing-workspace-foreign");
        WorkspaceDocument foreignDocument = new(
            firstStore.Current.Document.State,
            firstStore.Current.Document.Format);
        var foreignStore = new DurableTestWorkspaceStore(
            foreignId,
            foreignDocument,
            contentRevision: 2);
        var foreign = new WorkspaceCharacterSr5DowntimeHealingWorkspace(foreignStore);
        CharacterSr5HealingWorkspaceLookupResult foreignLookup = foreign.Lookup(
            foreignId,
            flow.Command.TransactionId,
            flow.Command.IdempotencyKey,
            flow.Command.CommandDigest);
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Corrupt, foreignLookup.Outcome);

        CharacterSr5HealingWorkspaceLookupResult collision = first.Lookup(
            WorkspaceId,
            flow.Command.TransactionId,
            new string('0', 64),
            new string('1', 64));
        Assert.AreEqual(
            CharacterSr5HealingWorkspaceOutcome.IdempotencyConflict,
            collision.Outcome);
        Assert.IsNull(collision.Entry);
    }

    private static HealingFlow Begin(
        ICharacterSr5DowntimeHealingWorkspace workspace,
        CharacterSr5HealingTrack track)
    {
        Guid activityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        CharacterSr5HealingWorkspaceReadResult read = workspace.Read(
            new(WorkspaceId, track, activityId, Start));
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Available, read.Outcome);
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateQuote(
            read.Input,
            out CharacterSr5HealingQuote quote));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreatePlan(
            quote,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Start,
            out CharacterSr5HealingPlan plan));
        CharacterSr5HealingWorkspaceReserveResult reserve = workspace.Reserve(new(
            quote,
            plan,
            quote.QuoteDigest,
            plan.PlanDigest,
            Start.AddMinutes(-5),
            ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Reserved, reserve.Outcome);
        CharacterSr5HealingWorkspaceStartResult started = workspace.Start(new(
            quote,
            reserve.Reservation!,
            reserve.Reservation!.ReservationDigest,
            Start,
            ExplicitlyConfirmed: true));
        Assert.AreEqual(CharacterSr5HealingWorkspaceOutcome.Started, started.Outcome);
        return new(quote, reserve.Reservation, started.Started!);
    }

    private static CompletionFlow Completion(
        ICharacterSr5DowntimeHealingWorkspace workspace)
    {
        HealingFlow flow = Begin(workspace, CharacterSr5HealingTrack.Physical);
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateRollReceipt(
            flow.Started,
            flow.Started.StartDigest,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            flow.Started.EligibleCompletionUtc,
            [5, 6, 2, 1, 3, 4],
            out CharacterSr5HealingRollReceipt roll));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionQuote(
            flow.Quote,
            flow.Started,
            roll,
            out CharacterSr5HealingCompletionQuote completion));
        Assert.IsTrue(CharacterSr5DowntimeHealingRules.TryCreateCompletionCommand(
            flow.Quote,
            flow.Started,
            completion,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            flow.Quote.QuoteDigest,
            flow.Started.StartDigest,
            completion.CompletionQuoteDigest,
            explicitlyConfirmed: true,
            out CharacterSr5HealingCompletionCommand command));
        return new(
            command,
            new(
                flow.Quote,
                flow.Reservation,
                flow.Started,
                roll,
                completion,
                command));
    }

    private static WorkspaceDocument Document()
        => new(
            new WorkspaceDocumentState(
                CharacterSr5DowntimeHealingRules.RulesetId,
                1,
                "workspace",
                """
                <character>
                  <created>True</created>
                  <physicalcmfilled>5</physicalcmfilled>
                  <stuncmfilled>3</stuncmfilled>
                  <attributes>
                    <attribute><name>BOD</name><totalvalue>3</totalvalue></attribute>
                    <attribute><name>WIL</name><totalvalue>4</totalvalue></attribute>
                  </attributes>
                  <calendar />
                </character>
                """),
            WorkspaceDocumentFormat.NativeXml);

    private static string Damage(DurableTestWorkspaceStore store)
        => XDocument.Parse(store.Current.Document.Content)
            .Root!.Element("physicalcmfilled")!.Value;

    private static string TemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chummer-sr5-healing-workspace-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed record HealingFlow(
        CharacterSr5HealingQuote Quote,
        CharacterSr5HealingReservation Reservation,
        CharacterSr5HealingStartedInterval Started);

    private sealed record CompletionFlow(
        CharacterSr5HealingCompletionCommand Command,
        CharacterSr5HealingWorkspaceCompletionCommitRequest Request);

    private sealed class DurableTestWorkspaceStore :
        IWorkspaceStore,
        IWorkspaceAuxiliaryStateAtomicCommitCapability
    {
        public DurableTestWorkspaceStore(
            CharacterWorkspaceId id,
            WorkspaceDocument document,
            long contentRevision = 1)
        {
            Current = new(
                id,
                document,
                contentRevision,
                contentRevision,
                DateTimeOffset.UtcNow);
        }

        public WorkspaceStoredDocument Current { get; private set; }
        public bool ThrowAfterNextSuccessfulCommit { get; set; }
        public bool RejectNextAtomicAuxiliaryCas { get; set; }
        public int SuccessfulCommitCount { get; private set; }
        public bool SupportsWorkspaceAuxiliaryStateAtomicCommit => true;

        public string Export() => JsonSerializer.Serialize(new DurableSnapshot(
            Current.Id.Value,
            Current.Document.RulesetId,
            Current.Document.SchemaVersion,
            Current.Document.PayloadKind,
            Current.Document.Content,
            Current.Document.Format,
            Current.Document.AuxiliaryState,
            Current.ContentRevision));

        public static DurableTestWorkspaceStore Reopen(string json)
        {
            DurableSnapshot current = JsonSerializer.Deserialize<DurableSnapshot>(json)!;
            WorkspaceDocument document = new(
                new WorkspaceDocumentState(
                    current.RulesetId,
                    current.SchemaVersion,
                    current.PayloadKind,
                    current.Content)
                {
                    AuxiliaryState = current.AuxiliaryState
                },
                current.Format);
            return new(
                new CharacterWorkspaceId(current.WorkspaceId),
                document,
                current.ContentRevision);
        }

        public void AdvanceUnrelatedWorkspaceRevision()
        {
            XDocument document = XDocument.Parse(Current.Document.Content);
            document.Root!.Add(new XElement("notes", "changed elsewhere"));
            long revision = Current.ContentRevision + 1;
            Current = Current with
            {
                Document = new(
                    Current.Document.State with
                    {
                        Payload = document.ToString(SaveOptions.DisableFormatting)
                    },
                    Current.Document.Format),
                ContentRevision = revision,
                SavedRevision = revision
            };
        }

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => id == Current.Id
                ? new(WorkspaceOperationOutcome.Success, Current)
                : new(WorkspaceOperationOutcome.Missing);

        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
            => Get(id);

        public WorkspaceStoreMutationResult
            ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                CharacterWorkspaceId id,
                long expectedContentRevision,
                string expectedAuxiliaryStateDigest,
                WorkspaceDocument document)
        {
            if (id != Current.Id)
                return new(WorkspaceOperationOutcome.Missing);
            if (RejectNextAtomicAuxiliaryCas)
            {
                RejectNextAtomicAuxiliaryCas = false;
                return new(
                    WorkspaceOperationOutcome.Conflict,
                    Entry(),
                    "simulated_calendar_auxiliary_cas_conflict");
            }
            if (expectedContentRevision != Current.ContentRevision
                || !string.Equals(
                    expectedAuxiliaryStateDigest,
                    Current.Document.AuxiliaryStateDigest,
                    StringComparison.Ordinal))
            {
                return new(WorkspaceOperationOutcome.Conflict, Entry());
            }
            long revision = Current.ContentRevision + 1;
            Current = new(
                Current.Id,
                document,
                revision,
                revision,
                DateTimeOffset.UtcNow);
            SuccessfulCommitCount++;
            if (ThrowAfterNextSuccessfulCommit)
            {
                ThrowAfterNextSuccessfulCommit = false;
                throw new IOException("simulated_ack_loss_after_durable_write");
            }
            return new(WorkspaceOperationOutcome.Success, Entry());
        }

        private WorkspaceStoreEntry Entry()
            => new(
                Current.Id,
                Current.LastUpdatedUtc,
                Current.ContentRevision,
                Current.SavedRevision);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => Unsupported();
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            OwnerScope owner,
            WorkspaceDocument document) => Unsupported();
        public IReadOnlyList<WorkspaceStoreEntry> List() => [Entry()];
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => List();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document) => Unsupported();
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document) => Unsupported();
        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision) => Unsupported();
        public WorkspaceStoreMutationResult SaveCheckpoint(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision) => Unsupported();
        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision) => Unsupported();
        public WorkspaceStoreMutationResult Delete(
            OwnerScope owner,
            CharacterWorkspaceId id,
            long expectedContentRevision) => Unsupported();

        private static WorkspaceStoreMutationResult Unsupported()
            => new(WorkspaceOperationOutcome.Unavailable);

        private sealed record DurableSnapshot(
            string WorkspaceId,
            string RulesetId,
            int SchemaVersion,
            string PayloadKind,
            string Content,
            WorkspaceDocumentFormat Format,
            WorkspaceDocumentAuxiliaryState AuxiliaryState,
            long ContentRevision);
    }
}
