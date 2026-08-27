using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Exact saved-character adapter for SR5 natural healing. It derives character
/// facts from the current .chum5 XML, validates non-mutating lifecycle transitions
/// against the current workspace/calendar pair, and writes terminal activity plus
/// character result and receipt through one durable auxiliary-state checkpoint CAS.
/// </summary>
public sealed class WorkspaceCharacterSr5DowntimeHealingWorkspace :
    ICharacterSr5DowntimeHealingWorkspace
{
    private const long MaximumCharacterXmlLength = 67_108_864;
    private static readonly ConcurrentDictionary<string, object> WorkspaceGates =
        new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions ComparisonJsonOptions = new();

    private readonly IWorkspaceStore _store;

    public WorkspaceCharacterSr5DowntimeHealingWorkspace(IWorkspaceStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public CharacterSr5HealingWorkspaceReadResult Read(
        CharacterSr5HealingWorkspaceReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidReadRequest(request))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_read_request");
        }

        lock (Gate(request.WorkspaceId))
        {
            WorkspaceStoreReadResult read = ReadStore(request.WorkspaceId);
            if (!TryRequireSavedSr5Document(
                    read,
                    out WorkspaceStoredDocument saved,
                    out WorkspaceFailure failure))
            {
                return new(failure.Outcome, failure.Revision, Error: failure.Error);
            }

            try
            {
                IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger =
                    ReadLedger(saved);
                ProjectionResult projected = ProjectInput(
                    saved,
                    ledger,
                    request.Track,
                    request.ActivityId,
                    request.EarliestStartUtc);
                return projected.Input is not null
                    ? new(
                        CharacterSr5HealingWorkspaceOutcome.Available,
                        saved.ContentRevision,
                        CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger),
                        projected.Input)
                    : new(
                        projected.Outcome,
                        saved.ContentRevision,
                        CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger),
                        Error: projected.Error);
            }
            catch (Exception error) when (IsProjectionFailure(error))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "sr5_healing_workspace_projection_corrupt");
            }
        }
    }

    public CharacterSr5HealingWorkspaceReserveResult Reserve(
        CharacterSr5HealingWorkspaceReserveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CharacterSr5DowntimeHealingRules.IsCoherent(request.Quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Plan, request.Quote))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_reservation_request");
        }

        lock (Gate(request.Quote.WorkspaceId))
        {
            CurrentProjection current = ReadCurrentProjection(request.Quote);
            if (current.Input is null || current.Quote is null)
            {
                return new(
                    current.Outcome,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    Error: current.Error);
            }
            if (!CanonicalEquals(current.Quote, request.Quote)
                || !CharacterSr5DowntimeHealingRules.TryReserve(
                    current.Quote,
                    request.Plan,
                    request.ExpectedQuoteDigest,
                    request.ExpectedPlanDigest,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    request.ReservedAtUtc,
                    request.ExplicitlyConfirmed,
                    out CharacterSr5HealingReservation reservation))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    Error: "stale_or_forged_sr5_healing_reservation");
            }
            return new(
                CharacterSr5HealingWorkspaceOutcome.Reserved,
                current.WorkspaceRevision,
                current.CalendarRevision,
                reservation);
        }
    }

    public CharacterSr5HealingWorkspaceStartResult Start(
        CharacterSr5HealingWorkspaceStartRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!CharacterSr5DowntimeHealingRules.IsCoherent(request.Quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Reservation, request.Quote))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_start_request");
        }

        lock (Gate(request.Quote.WorkspaceId))
        {
            CurrentProjection current = ReadCurrentProjection(request.Quote);
            if (current.Input is null || current.Quote is null)
            {
                return new(
                    current.Outcome,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    Error: current.Error);
            }
            if (!CanonicalEquals(current.Quote, request.Quote)
                || !CharacterSr5DowntimeHealingRules.TryStart(
                    current.Quote,
                    request.Reservation,
                    request.ExpectedReservationDigest,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    request.StartedAtUtc,
                    request.ExplicitlyConfirmed,
                    out CharacterSr5HealingStartedInterval started))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    current.WorkspaceRevision,
                    current.CalendarRevision,
                    Error: "stale_or_forged_sr5_healing_start");
            }
            return new(
                CharacterSr5HealingWorkspaceOutcome.Started,
                current.WorkspaceRevision,
                current.CalendarRevision,
                started);
        }
    }

    public CharacterSr5HealingWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest)
    {
        if (!IsValidWorkspaceId(workspaceId)
            || transactionId == Guid.Empty
            || !IsDigest(idempotencyKey)
            || !IsDigest(commandDigest))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_outcome_lookup");
        }

        lock (Gate(workspaceId))
        {
            return LookupUnderGate(
                workspaceId,
                transactionId,
                idempotencyKey,
                commandDigest);
        }
    }

    public CharacterSr5HealingWorkspaceCommitResult CommitCompletion(
        CharacterSr5HealingWorkspaceCompletionCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidCompletionRequest(request))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_completion_commit");
        }

        lock (Gate(request.Command.WorkspaceId))
        {
            CharacterSr5HealingWorkspaceLookupResult prior = LookupUnderGate(
                request.Command.WorkspaceId,
                request.Command.TransactionId,
                request.Command.IdempotencyKey,
                request.Command.CommandDigest);
            if (prior.Outcome == CharacterSr5HealingWorkspaceOutcome.Replayed)
            {
                return ReplayCommit(prior);
            }
            if (prior.Outcome != CharacterSr5HealingWorkspaceOutcome.NotFound)
            {
                return CommitFromLookup(prior);
            }
            return CommitCompletionUnderGate(request);
        }
    }

    public CharacterSr5HealingWorkspaceCommitResult CommitCancellation(
        CharacterSr5HealingWorkspaceCancellationCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidCancellationRequest(request))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "invalid_sr5_healing_cancellation_commit");
        }

        lock (Gate(request.Command.WorkspaceId))
        {
            CharacterSr5HealingWorkspaceLookupResult prior = LookupUnderGate(
                request.Command.WorkspaceId,
                request.Command.TransactionId,
                request.Command.IdempotencyKey,
                request.Command.CommandDigest);
            if (prior.Outcome == CharacterSr5HealingWorkspaceOutcome.Replayed)
            {
                return ReplayCommit(prior);
            }
            if (prior.Outcome != CharacterSr5HealingWorkspaceOutcome.NotFound)
            {
                return CommitFromLookup(prior);
            }
            return CommitCancellationUnderGate(request);
        }
    }

    private CharacterSr5HealingWorkspaceCommitResult CommitCompletionUnderGate(
        CharacterSr5HealingWorkspaceCompletionCommitRequest request)
    {
        WorkspaceStoreReadResult read = ReadStore(request.Command.WorkspaceId);
        if (!TryRequireSavedSr5Document(
                read,
                out WorkspaceStoredDocument saved,
                out WorkspaceFailure failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger =
                ReadLedger(saved);
            long calendarRevision =
                CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger);
            if (saved.ContentRevision != request.Command.ExpectedWorkspaceRevision
                || calendarRevision != request.Command.ExpectedCalendarRevision)
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "stale_sr5_healing_workspace_or_calendar_revision");
            }
            if (ledger.Count >= CharacterSr5HealingActivityLedgerIntegrity.MaximumEntries
                || ledger.Any(entry => entry.ActivityId == request.Command.ActivityId))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_activity_already_terminal_or_ledger_full");
            }

            ProjectionResult projected = ProjectInput(
                saved,
                ledger,
                request.Quote.Track,
                request.Quote.ActivityId,
                request.Quote.EarliestStartUtc);
            if (projected.Input is null
                || !CharacterSr5DowntimeHealingRules.TryCreateQuote(
                    projected.Input,
                    out CharacterSr5HealingQuote current)
                || !CanonicalEquals(current, request.Quote))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "stale_or_forged_sr5_healing_completion_projection");
            }

            XDocument document = ParseDocument(saved.Document.Content);
            SetDamage(document, current.Track, request.Command.SavedDamageBoxes);
            int observedDamage = ReadDamage(document, current.Track);
            long committedRevision = checked(saved.ContentRevision + 1);
            long committedCalendarRevision = checked(calendarRevision + 1);
            if (!CharacterSr5DowntimeHealingRules.TryCreateCompletionReceipt(
                    request.CompletionQuote,
                    request.Command,
                    committedRevision,
                    committedCalendarRevision,
                    observedDamage,
                    out CharacterSr5HealingCompletionReceipt receipt))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_completion_receipt_creation_failed");
            }

            string payloadAfter = Serialize(document);
            if (!CharacterSr5HealingActivityLedgerIntegrity.TryCreateCompletionEntry(
                    saved.Document.Content,
                    payloadAfter,
                    current,
                    request.Reservation,
                    request.Started,
                    request.Roll,
                    request.CompletionQuote,
                    request.Command,
                    receipt,
                    out CharacterSr5HealingActivityLedgerEntry ledgerEntry))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_completion_ledger_entry_invalid");
            }
            return CommitReplacement(
                saved,
                ledger,
                ledgerEntry,
                payloadAfter,
                request.Command.TransactionId,
                request.Command.IdempotencyKey,
                request.Command.CommandDigest);
        }
        catch (OverflowException)
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_revision_exhausted");
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_completion_projection_corrupt");
        }
    }

    private CharacterSr5HealingWorkspaceCommitResult CommitCancellationUnderGate(
        CharacterSr5HealingWorkspaceCancellationCommitRequest request)
    {
        WorkspaceStoreReadResult read = ReadStore(request.Command.WorkspaceId);
        if (!TryRequireSavedSr5Document(
                read,
                out WorkspaceStoredDocument saved,
                out WorkspaceFailure failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger =
                ReadLedger(saved);
            long calendarRevision =
                CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger);
            if (saved.ContentRevision != request.Command.ExpectedWorkspaceRevision
                || calendarRevision != request.Command.ExpectedCalendarRevision)
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "stale_sr5_healing_workspace_or_calendar_revision");
            }
            if (ledger.Count >= CharacterSr5HealingActivityLedgerIntegrity.MaximumEntries
                || ledger.Any(entry => entry.ActivityId == request.Command.ActivityId))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_activity_already_terminal_or_ledger_full");
            }

            ProjectionResult projected = ProjectInput(
                saved,
                ledger,
                request.Quote.Track,
                request.Quote.ActivityId,
                request.Quote.EarliestStartUtc);
            if (projected.Input is null
                || !CharacterSr5DowntimeHealingRules.TryCreateQuote(
                    projected.Input,
                    out CharacterSr5HealingQuote current)
                || !CanonicalEquals(current, request.Quote))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "stale_or_forged_sr5_healing_cancellation_projection");
            }

            int observedDamage = ReadDamage(
                ParseDocument(saved.Document.Content),
                current.Track);
            long committedRevision = checked(saved.ContentRevision + 1);
            long committedCalendarRevision = checked(calendarRevision + 1);
            if (!CharacterSr5DowntimeHealingRules.TryCreateCancellationReceipt(
                    request.CancellationQuote,
                    request.Command,
                    committedRevision,
                    committedCalendarRevision,
                    observedDamage,
                    out CharacterSr5HealingCancellationReceipt receipt)
                || !CharacterSr5HealingActivityLedgerIntegrity.TryCreateCancellationEntry(
                    saved.Document.Content,
                    saved.Document.Content,
                    current,
                    request.Reservation,
                    request.Started,
                    request.CancellationQuote,
                    request.Command,
                    receipt,
                    out CharacterSr5HealingActivityLedgerEntry ledgerEntry))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_cancellation_receipt_or_ledger_invalid");
            }
            return CommitReplacement(
                saved,
                ledger,
                ledgerEntry,
                saved.Document.Content,
                request.Command.TransactionId,
                request.Command.IdempotencyKey,
                request.Command.CommandDigest);
        }
        catch (OverflowException)
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_revision_exhausted");
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_cancellation_projection_corrupt");
        }
    }

    private CharacterSr5HealingWorkspaceCommitResult CommitReplacement(
        WorkspaceStoredDocument saved,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger,
        CharacterSr5HealingActivityLedgerEntry ledgerEntry,
        string payloadAfter,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest)
    {
        CharacterSr5HealingActivityLedgerEntry[] replacementLedger =
            [.. ledger, ledgerEntry];
        WorkspaceDocument replacement = new(
            saved.Document.State with
            {
                Payload = payloadAfter,
                AuxiliaryState = saved.Document.AuxiliaryState with
                {
                    CharacterSr5HealingActivities = replacementLedger
                }
            },
            saved.Document.Format);

        WorkspaceStoreMutationResult committed;
        try
        {
            if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
                { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomicStore)
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Unavailable,
                    saved.ContentRevision,
                    ledgerEntry.ExpectedCalendarRevision,
                    Error: "sr5_healing_atomic_auxiliary_state_commit_unavailable");
            }
            committed = atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                saved.Id,
                saved.ContentRevision,
                saved.Document.AuxiliaryStateDigest,
                replacement);
        }
        catch (Exception error) when (IsStoreFailure(error))
        {
            return RecoverUnknownCommit(
                saved.Id,
                transactionId,
                idempotencyKey,
                commandDigest,
                error.Message);
        }

        if (committed.Success
            && committed.Entry is { } entry
            && entry.ContentRevision == ledgerEntry.CommittedWorkspaceRevision
            && entry.SavedRevision == ledgerEntry.CommittedWorkspaceRevision)
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Applied,
                entry.ContentRevision,
                ledgerEntry.CommittedCalendarRevision,
                ledgerEntry.IdempotencyKey,
                ledgerEntry.CommandDigest,
                ledgerEntry);
        }

        CharacterSr5HealingWorkspaceCommitResult recovered = RecoverUnknownCommit(
            saved.Id,
            transactionId,
            idempotencyKey,
            commandDigest,
            committed.Error);
        if (recovered.Outcome is CharacterSr5HealingWorkspaceOutcome.Replayed
            or CharacterSr5HealingWorkspaceOutcome.IdempotencyConflict
            or CharacterSr5HealingWorkspaceOutcome.Corrupt
            or CharacterSr5HealingWorkspaceOutcome.Missing)
        {
            return recovered;
        }
        return committed.Outcome switch
        {
            WorkspaceOperationOutcome.Conflict => new(
                CharacterSr5HealingWorkspaceOutcome.Conflict,
                recovered.CurrentWorkspaceRevision,
                recovered.CurrentCalendarRevision,
                Error: committed.Error ?? "sr5_healing_workspace_revision_conflict"),
            WorkspaceOperationOutcome.Missing => new(
                CharacterSr5HealingWorkspaceOutcome.Missing,
                Error: committed.Error ?? "sr5_healing_workspace_missing"),
            WorkspaceOperationOutcome.Corrupt => new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                recovered.CurrentWorkspaceRevision,
                recovered.CurrentCalendarRevision,
                Error: committed.Error ?? "sr5_healing_workspace_corrupt"),
            _ => recovered
        };
    }

    private CharacterSr5HealingWorkspaceCommitResult RecoverUnknownCommit(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest,
        string? error)
    {
        CharacterSr5HealingWorkspaceLookupResult lookup = LookupUnderGate(
            workspaceId,
            transactionId,
            idempotencyKey,
            commandDigest);
        if (lookup.Outcome == CharacterSr5HealingWorkspaceOutcome.Replayed)
        {
            return ReplayCommit(lookup);
        }
        if (lookup.Outcome is CharacterSr5HealingWorkspaceOutcome.IdempotencyConflict
            or CharacterSr5HealingWorkspaceOutcome.Corrupt
            or CharacterSr5HealingWorkspaceOutcome.Missing)
        {
            return CommitFromLookup(lookup);
        }
        return new(
            CharacterSr5HealingWorkspaceOutcome.Indeterminate,
            lookup.CurrentWorkspaceRevision,
            lookup.CurrentCalendarRevision,
            Error: error ?? lookup.Error ?? "sr5_healing_atomic_commit_outcome_unknown");
    }

    private CharacterSr5HealingWorkspaceLookupResult LookupUnderGate(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest)
    {
        WorkspaceStoreReadResult read = ReadStore(workspaceId);
        if (!TryRequireSavedSr5Document(
                read,
                out WorkspaceStoredDocument saved,
                out WorkspaceFailure failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            _ = ParseDocument(saved.Document.Content);
            IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger =
                ReadLedger(saved);
            long calendarRevision =
                CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger);
            CharacterSr5HealingActivityLedgerEntry[] matches = ledger.Where(entry =>
                    entry.TransactionId == transactionId
                    || FixedEquals(entry.IdempotencyKey, idempotencyKey))
                .Take(2)
                .ToArray();
            if (matches.Length == 0)
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.NotFound,
                    saved.ContentRevision,
                    calendarRevision);
            }
            if (matches.Length != 1)
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: "sr5_healing_ledger_identity_ambiguous");
            }
            CharacterSr5HealingActivityLedgerEntry match = matches[0];
            if (match.TransactionId != transactionId
                || !FixedEquals(match.IdempotencyKey, idempotencyKey)
                || !FixedEquals(match.CommandDigest, commandDigest))
            {
                return new(
                    CharacterSr5HealingWorkspaceOutcome.IdempotencyConflict,
                    saved.ContentRevision,
                    calendarRevision,
                    match.IdempotencyKey,
                    match.CommandDigest,
                    Error: "sr5_healing_transaction_or_idempotency_claimed");
            }
            return new(
                CharacterSr5HealingWorkspaceOutcome.Replayed,
                saved.ContentRevision,
                calendarRevision,
                match.IdempotencyKey,
                match.CommandDigest,
                match);
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_activity_ledger_corrupt");
        }
    }

    private CurrentProjection ReadCurrentProjection(CharacterSr5HealingQuote quote)
    {
        WorkspaceStoreReadResult read = ReadStore(quote.WorkspaceId);
        if (!TryRequireSavedSr5Document(
                read,
                out WorkspaceStoredDocument saved,
                out WorkspaceFailure failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger =
                ReadLedger(saved);
            long calendarRevision =
                CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger);
            ProjectionResult projected = ProjectInput(
                saved,
                ledger,
                quote.Track,
                quote.ActivityId,
                quote.EarliestStartUtc);
            if (projected.Input is null
                || !CharacterSr5DowntimeHealingRules.TryCreateQuote(
                    projected.Input,
                    out CharacterSr5HealingQuote current))
            {
                return new(
                    projected.Outcome,
                    saved.ContentRevision,
                    calendarRevision,
                    Error: projected.Error ?? "sr5_healing_quote_projection_invalid");
            }
            return new(
                CharacterSr5HealingWorkspaceOutcome.Available,
                saved.ContentRevision,
                calendarRevision,
                projected.Input,
                current);
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "sr5_healing_quote_projection_corrupt");
        }
    }

    private static ProjectionResult ProjectInput(
        WorkspaceStoredDocument saved,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger,
        CharacterSr5HealingTrack track,
        Guid activityId,
        DateTimeOffset earliestStartUtc)
    {
        if (ledger.Any(entry => entry.ActivityId == activityId))
        {
            return new(
                CharacterSr5HealingWorkspaceOutcome.Conflict,
                Error: "sr5_healing_activity_already_terminal");
        }
        XDocument document = ParseDocument(saved.Document.Content);
        XElement root = RequireCharacterRoot(document);
        int body = ReadAttributeTotal(root, "BOD");
        int willpower = ReadAttributeTotal(root, "WIL");
        int damage = ReadDamage(document, track);
        bool created = ReadRequiredBool(root, "created");
        string rawCalendar = ReadRawCalendar(root);
        long calendarRevision =
            CharacterSr5HealingActivityLedgerIntegrity.GetCalendarRevision(ledger);
        string calendarDigest =
            CharacterSr5HealingActivityLedgerIntegrity.ComputeCalendarDigest(
                rawCalendar,
                ledger);
        string ruleId = track == CharacterSr5HealingTrack.Stun
            ? CharacterSr5DowntimeHealingRules.StunRuleId
            : CharacterSr5DowntimeHealingRules.PhysicalRuleId;
        var input = new CharacterSr5HealingAuthorityInput(
            saved.Id,
            saved.ContentRevision,
            calendarRevision,
            activityId,
            created,
            saved.Document.RulesetId,
            track,
            damage,
            body,
            willpower,
            Array.Empty<CharacterSr5HealingDicePoolModifier>(),
            earliestStartUtc,
            RuntimeFingerprint(saved.Document),
            Sha256(Canonical(
                "chummer.core.sr5-natural-healing-source/v1",
                CharacterSr5DowntimeHealingRules.SourceId,
                ruleId)),
            PayloadDigest(saved.Document.Content),
            calendarDigest);
        return CharacterSr5DowntimeHealingRules.TryCreateQuote(input, out _)
            ? new(CharacterSr5HealingWorkspaceOutcome.Available, input)
            : new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                Error: "sr5_healing_exact_workspace_projection_invalid");
    }

    private static IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ReadLedger(
        WorkspaceStoredDocument saved)
    {
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? persisted =
            saved.Document.AuxiliaryState.CharacterSr5HealingActivities;
        if (!CharacterSr5HealingActivityLedgerIntegrity.IsValidLedger(
                saved.Id,
                saved.ContentRevision,
                persisted))
        {
            throw new InvalidOperationException("sr5_healing_activity_ledger_corrupt");
        }
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry> ledger = persisted ?? [];
        if (ledger.Count > 0
            && ledger[^1].CommittedWorkspaceRevision == saved.ContentRevision
            && !FixedEquals(
                ledger[^1].CharacterPayloadDigestAfter,
                PayloadDigest(saved.Document.Content)))
        {
            throw new InvalidOperationException("sr5_healing_latest_payload_binding_invalid");
        }
        return ledger;
    }

    private WorkspaceStoreReadResult ReadStore(CharacterWorkspaceId workspaceId)
    {
        try
        {
            return _store.Get(workspaceId);
        }
        catch (Exception error) when (IsStoreFailure(error))
        {
            return new(
                WorkspaceOperationOutcome.Unavailable,
                Error: "sr5_healing_workspace_read_unavailable");
        }
    }

    private static bool TryRequireSavedSr5Document(
        WorkspaceStoreReadResult read,
        out WorkspaceStoredDocument saved,
        out WorkspaceFailure failure)
    {
        saved = null!;
        failure = default;
        if (!read.Success || read.Value is not { } value)
        {
            failure = new(
                read.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing
                        => CharacterSr5HealingWorkspaceOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt
                        => CharacterSr5HealingWorkspaceOutcome.Corrupt,
                    WorkspaceOperationOutcome.Conflict
                        => CharacterSr5HealingWorkspaceOutcome.Conflict,
                    _ => CharacterSr5HealingWorkspaceOutcome.Unavailable
                },
                Error: read.Error ?? "sr5_healing_workspace_unavailable");
            return false;
        }
        if (value.ContentRevision <= 0
            || value.ContentRevision == long.MaxValue
            || value.SavedRevision != value.ContentRevision
            || value.Document.Format != WorkspaceDocumentFormat.NativeXml
            || value.Document.SchemaVersion <= 0
            || !string.Equals(
                value.Document.RulesetId,
                CharacterSr5DowntimeHealingRules.RulesetId,
                StringComparison.Ordinal)
            || !string.Equals(value.Document.PayloadKind, "workspace", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(value.Document.Content)
            || value.Document.Content.Length > MaximumCharacterXmlLength)
        {
            failure = new(
                CharacterSr5HealingWorkspaceOutcome.Corrupt,
                value.ContentRevision,
                "sr5_healing_saved_workspace_invalid");
            return false;
        }
        saved = value;
        return true;
    }

    private static XDocument ParseDocument(string content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacterXmlLength,
            IgnoreWhitespace = false
        };
        using var text = new StringReader(content);
        using XmlReader reader = XmlReader.Create(text, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        if (document.Declaration?.Standalone is { Length: > 0 }
            && document.Declaration.Standalone is not "yes" and not "no")
        {
            throw new InvalidOperationException("invalid_xml_declaration");
        }
        return document;
    }

    private static XElement RequireCharacterRoot(XDocument document)
    {
        if (document.Root is not { } root
            || root.Name.LocalName != "character"
            || root.Name.Namespace != XNamespace.None)
        {
            throw new InvalidOperationException("invalid_character_root");
        }
        return root;
    }

    private static int ReadAttributeTotal(XElement root, string name)
    {
        XElement[] containers = root.Elements("attributes").Take(2).ToArray();
        if (containers.Length != 1)
        {
            throw new InvalidOperationException("attributes_projection_not_unique");
        }
        XElement[] matches = containers[0].Elements("attribute")
            .Where(attribute => string.Equals(
                ReadSingleValue(attribute, "name"),
                name,
                StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("healing_attribute_not_unique");
        }
        int value = ReadRequiredNonNegativeInt(matches[0], "totalvalue");
        if (value <= 0 || value > CharacterSr5DowntimeHealingRules.MaximumAttribute)
        {
            throw new InvalidOperationException("healing_attribute_out_of_range");
        }
        return value;
    }

    private static int ReadDamage(
        XDocument document,
        CharacterSr5HealingTrack track)
        => ReadRequiredNonNegativeInt(
            RequireCharacterRoot(document),
            track == CharacterSr5HealingTrack.Stun
                ? "stuncmfilled"
                : "physicalcmfilled",
            CharacterSr5DowntimeHealingRules.MaximumDamageBoxes);

    private static void SetDamage(
        XDocument document,
        CharacterSr5HealingTrack track,
        int value)
    {
        XElement root = RequireCharacterRoot(document);
        string name = track == CharacterSr5HealingTrack.Stun
            ? "stuncmfilled"
            : "physicalcmfilled";
        XElement element = RequireSingle(root, name);
        element.Value = value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool ReadRequiredBool(XElement parent, string name)
        => ReadSingleValue(parent, name) switch
        {
            "True" => true,
            "False" => false,
            _ => throw new InvalidOperationException("invalid_boolean_projection")
        };

    private static int ReadRequiredNonNegativeInt(
        XElement parent,
        string name,
        int maximum = int.MaxValue)
    {
        string text = ReadSingleValue(parent, name);
        if (!int.TryParse(
                text,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int value)
            || value < 0
            || value > maximum)
        {
            throw new InvalidOperationException("invalid_integer_projection");
        }
        return value;
    }

    private static XElement RequireSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException("workspace_element_not_unique");
    }

    private static string ReadSingleValue(XElement parent, string name)
        => RequireSingle(parent, name).Value.Trim();

    private static string ReadRawCalendar(XElement root)
    {
        XElement[] matches = root.Elements("calendar").Take(2).ToArray();
        return matches.Length switch
        {
            0 => string.Empty,
            1 => matches[0].ToString(SaveOptions.DisableFormatting),
            _ => throw new InvalidOperationException("calendar_projection_not_unique")
        };
    }

    private static string Serialize(XDocument document)
        => document.ToString(SaveOptions.DisableFormatting);

    private static string RuntimeFingerprint(WorkspaceDocument document)
        => Sha256(Canonical(
            "chummer.core.sr5-natural-healing-workspace-runtime/v1",
            document.RulesetId,
            document.SchemaVersion,
            document.PayloadKind,
            CharacterSr5HealingActivityLedgerIntegrity.EntryV1));

    private static bool IsValidReadRequest(CharacterSr5HealingWorkspaceReadRequest request)
        => IsValidWorkspaceId(request.WorkspaceId)
            && Enum.IsDefined(request.Track)
            && request.ActivityId != Guid.Empty
            && request.EarliestStartUtc.Offset == TimeSpan.Zero;

    private static bool IsValidCompletionRequest(
        CharacterSr5HealingWorkspaceCompletionCommitRequest request)
        => CharacterSr5DowntimeHealingRules.IsCoherent(request.Quote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Reservation, request.Quote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Started, request.Quote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Roll, request.Started)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.CompletionQuote,
                request.Quote,
                request.Started,
                request.Roll)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Command, request.CompletionQuote);

    private static bool IsValidCancellationRequest(
        CharacterSr5HealingWorkspaceCancellationCommitRequest request)
        => CharacterSr5DowntimeHealingRules.IsCoherent(request.Quote)
            && (request.Reservation is not null && request.Started is null
                || request.Reservation is null && request.Started is not null)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.CancellationQuote,
                request.Quote,
                request.Reservation,
                request.Started)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                request.Command,
                request.CancellationQuote);

    private static bool IsValidWorkspaceId(CharacterWorkspaceId value)
        => !string.IsNullOrWhiteSpace(value.Value)
            && value.Value.Length
                <= CharacterSr5DowntimeHealingRules.MaximumWorkspaceIdLength;

    private static bool IsDigest(string? value)
        => value is { Length: CharacterSr5DowntimeHealingRules.DigestLength }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static object Gate(CharacterWorkspaceId workspaceId)
        => WorkspaceGates.GetOrAdd(workspaceId.Value, static _ => new object());

    private static CharacterSr5HealingWorkspaceCommitResult ReplayCommit(
        CharacterSr5HealingWorkspaceLookupResult lookup)
        => new(
            CharacterSr5HealingWorkspaceOutcome.Replayed,
            lookup.CurrentWorkspaceRevision,
            lookup.CurrentCalendarRevision,
            lookup.ExistingIdempotencyKey,
            lookup.ExistingCommandDigest,
            lookup.Entry,
            lookup.Error);

    private static CharacterSr5HealingWorkspaceCommitResult CommitFromLookup(
        CharacterSr5HealingWorkspaceLookupResult lookup)
        => new(
            lookup.Outcome,
            lookup.CurrentWorkspaceRevision,
            lookup.CurrentCalendarRevision,
            lookup.ExistingIdempotencyKey,
            lookup.ExistingCommandDigest,
            lookup.Entry,
            lookup.Error);

    private static bool CanonicalEquals<T>(T left, T right)
    {
        byte[] leftBytes = JsonSerializer.SerializeToUtf8Bytes(
            left,
            ComparisonJsonOptions);
        byte[] rightBytes = JsonSerializer.SerializeToUtf8Bytes(
            right,
            ComparisonJsonOptions);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string PayloadDigest(string value) => Sha256(value);

    private static string Canonical(params object?[] values)
        => string.Join('\0', values.Select(static value =>
        {
            string text = Convert.ToString(value, CultureInfo.InvariantCulture)
                ?? string.Empty;
            return string.Concat(
                text.Length.ToString(CultureInfo.InvariantCulture),
                ":",
                text);
        }));

    private static string Sha256(string value)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool IsProjectionFailure(Exception error)
        => error is XmlException
            or InvalidOperationException
            or FormatException
            or OverflowException
            or JsonException;

    private static bool IsStoreFailure(Exception error)
        => error is IOException
            or UnauthorizedAccessException
            or InvalidOperationException;

    private readonly record struct ProjectionResult(
        CharacterSr5HealingWorkspaceOutcome Outcome,
        CharacterSr5HealingAuthorityInput? Input = null,
        string? Error = null);

    private readonly record struct CurrentProjection(
        CharacterSr5HealingWorkspaceOutcome Outcome,
        long WorkspaceRevision = 0,
        long CalendarRevision = 0,
        CharacterSr5HealingAuthorityInput? Input = null,
        CharacterSr5HealingQuote? Quote = null,
        string? Error = null);

    private readonly record struct WorkspaceFailure(
        CharacterSr5HealingWorkspaceOutcome Outcome,
        long Revision = 0,
        string? Error = null);
}
