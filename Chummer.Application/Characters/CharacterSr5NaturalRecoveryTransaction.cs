using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Rules = Chummer.Contracts.Characters.CharacterSr5DowntimeHealingRules;

namespace Chummer.Application.Characters;

public static class CharacterSr5NaturalRecoveryTransaction
{
    public const int MaximumOperations = 256;
    public const int MaximumLedgerBytes = 2 * 1024 * 1024;
    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Digest<T>(T value) => Hash(JsonSerializer.Serialize(value));
    public static string LedgerDigest(Sr5RecoveryLedger? ledger) => Digest(ledger ?? new Sr5RecoveryLedger([]));
    public static string OperationDigest(Sr5RecoveryOperation op) => Digest(op with { Digest = "" });
    public static string ReviewDigest(Sr5RecoveryCommand command) => Digest(command with { ExplicitlyConfirmed = false, ReviewDigest = "" });
    public static string OtherAuxiliaryDigest(WorkspaceDocument doc) =>
        WorkspaceDocumentAuxiliaryStateDigest.Compute(doc.AuxiliaryState with { Sr5NaturalRecovery = null });
    public static bool ValidOwner(OwnerScope owner) => CharacterCareerLifestylePaymentTransaction.IsValidOwner(owner);
    private static void Require(bool value) { if (!value) throw new InvalidDataException("Recovery authority is unavailable or changed."); }
    private static bool IsDigest(string? value) => CharacterCareerLifestylePaymentTransaction.IsDigest(value);
    public static bool ValidRequest(Sr5RecoveryRequest? r) => r is not null
        && CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(r.WorkspaceId)
        && r.OperationId != Guid.Empty && r.ActivityId != Guid.Empty && r.OperationId != r.ActivityId
        && Enum.IsDefined(r.Action) && Enum.IsDefined(r.Track)
        && r.StartsAtUtc.Offset == TimeSpan.Zero && r.GameTimeUtc.Offset == TimeSpan.Zero
        && r.StartsAtUtc.Ticks % TimeSpan.TicksPerSecond == 0 && r.GameTimeUtc.Ticks % TimeSpan.TicksPerSecond == 0;
    public static bool ValidCommand(Sr5RecoveryCommand? c) => c is not null && ValidRequest(c.Request)
        && c.ExplicitlyConfirmed && c.WorkspaceRevision is > 0 and < long.MaxValue
        && c.OwnerId is { Length: > 0 and <= 512 }
        // Serialized owner identity is not a trusted-local OwnerScope. The live
        // caller is admitted separately; never reconstruct trusted-local authority.
        && new OwnerScope(c.OwnerId).NormalizedValue == c.OwnerId
        && IsDigest(c.AuxiliaryDigest) && IsDigest(c.LedgerDigest) && IsDigest(c.QuoteDigest) && IsDigest(c.ActivityDigest)
        && (c.CurrentEnvironmentDigest is null || IsDigest(c.CurrentEnvironmentDigest))
        && c.ReviewDigest == ReviewDigest(c);

    public static IReadOnlyList<Sr5RecoveryActivity> Activities(Sr5RecoveryLedger? ledger) =>
        (ledger?.Operations ?? []).GroupBy(x => x.Activity.Quote.ActivityId).Select(g => g.Last().Activity).ToArray();
    public static bool Terminal(Sr5RecoveryActivity a) => a.Phase is Sr5RecoveryPhase.Completed or Sr5RecoveryPhase.Cancelled;
    public static long CalendarRevision(Sr5RecoveryLedger? ledger) => 1 + Activities(ledger).Count(Terminal);

    public static bool ValidActivity(Sr5RecoveryActivity? a)
    {
        if (a is null || !Rules.IsCoherent(a.Quote) || !a.Quote.CanPlan || !IsDigest(a.OtherAuxiliaryDigest)
            || !Rules.IsCoherent(a.Reservation, a.Quote) || !Enum.IsDefined(a.Phase)) return false;
        if (a.Phase == Sr5RecoveryPhase.Reserved)
            return a.Started is null && a.Roll is null && a.Completion is null && a.Completed is null && a.Cancellation is null && a.Cancelled is null;
        if (a.Phase == Sr5RecoveryPhase.Cancelled)
            return Rules.IsCoherent(a.Cancellation, a.Quote, a.Started is null ? a.Reservation : null, a.Started)
                && a.Cancelled is { } cancel && Rules.TryCreateCancellationCommand(a.Cancellation, cancel.TransactionId,
                    a.Cancellation!.CancellationQuoteDigest, a.Cancellation.SubjectDigest, true, out var command)
                && Rules.IsCoherent(cancel, a.Cancellation, command) && a.Completed is null;
        if (!Rules.IsCoherent(a.Started, a.Quote) || a.Cancellation is not null || a.Cancelled is not null) return false;
        if (a.Phase is Sr5RecoveryPhase.Started or Sr5RecoveryPhase.RollUnresolved)
            return a.Roll is null && a.Completion is null && a.Completed is null;
        if (!Rules.IsCoherent(a.Roll, a.Started) || !Rules.IsCoherent(a.Completion, a.Quote, a.Started, a.Roll)) return false;
        if (a.Phase == Sr5RecoveryPhase.Rolled) return a.Completed is null;
        return a.Completed is { } receipt && Rules.TryCreateCompletionCommand(a.Quote, a.Started, a.Completion,
            receipt.TransactionId, a.Quote.QuoteDigest, a.Started!.StartDigest, a.Completion!.CompletionQuoteDigest,
            true, out var complete) && Rules.IsCoherent(receipt, a.Completion, complete);
    }

    public static bool IsValidLedger(CharacterWorkspaceId id, long revision, Sr5RecoveryLedger? ledger)
    {
        if (ledger is null) return true;
        try
        {
            if (ledger.Operations is not { Count: > 0 and <= MaximumOperations }
                || JsonSerializer.SerializeToUtf8Bytes(ledger).Length > MaximumLedgerBytes) return false;
            var seen = new HashSet<Guid>(); var latest = new Dictionary<Guid, Sr5RecoveryOperation>();
            string previous = Hash("sr5-natural-recovery-ledger/v1"); long lastRevision = 0;
            var prefix = new List<Sr5RecoveryOperation>();
            foreach (var op in ledger.Operations)
            {
                if (!ValidCommand(op.Command) || !ValidActivity(op.Activity) || op.Command.Request.WorkspaceId != id
                    || op.Activity.Quote.WorkspaceId != id || op.Command.Request.ActivityId != op.Activity.Quote.ActivityId
                    || op.Command.Request.Track != op.Activity.Quote.Track
                    || op.Command.Request.StartsAtUtc != op.Activity.Reservation.Plan.StartsAtUtc
                    || op.Command.QuoteDigest != op.Activity.Quote.QuoteDigest
                    || op.RecordedRevision != op.Command.WorkspaceRevision + (Terminal(op.Activity) ? 1 : 0)
                    || op.Command.WorkspaceRevision != op.Activity.Quote.WorkspaceRevision
                    || op.RecordedRevision < lastRevision || op.RecordedRevision > revision
                    || op.PreviousDigest != previous || op.CommandDigest != Digest(op.Command)
                    || op.Digest != OperationDigest(op) || !IsDigest(op.PayloadDigest)
                    || !seen.Add(op.Command.Request.OperationId)
                    || op.Command.LedgerDigest != LedgerDigest(new(prefix.ToArray()))) return false;
                latest.TryGetValue(op.Activity.Quote.ActivityId, out var prior);
                if (op.Command.Request.Action == Sr5RecoveryAction.Reserve)
                {
                    if (prior is not null || op.Activity.Phase != Sr5RecoveryPhase.Reserved || op.Command.ActivityDigest != Digest<Sr5RecoveryActivity?>(null)
                        || op.Activity.Reservation.Plan.ReservationId != op.Command.Request.OperationId
                        || op.Activity.Reservation.ReservedAtUtc != op.Command.Request.GameTimeUtc
                        || op.Activity.Quote.CalendarRevision != 1 + latest.Values.Count(x => Terminal(x.Activity))
                        || latest.Values.Any(x => !Terminal(x.Activity))) return false;
                }
                else
                {
                    if (prior is null || prior.Command.OwnerId != op.Command.OwnerId
                        || Digest(prior.Activity) != op.Command.ActivityDigest
                        || !Allowed(prior.Activity.Phase, op.Command.Request.Action)
                        || Digest(prior.Activity.Quote) != Digest(op.Activity.Quote)
                        || prior.Activity.Reservation != op.Activity.Reservation
                        || prior.Activity.OtherAuxiliaryDigest != op.Activity.OtherAuxiliaryDigest
                        || prior.Activity.Started is not null && prior.Activity.Started != op.Activity.Started
                        || op.Command.Request.Action != Sr5RecoveryAction.Roll && Digest(prior.Activity.Roll) != Digest(op.Activity.Roll)
                        || op.Command.Request.Action != Sr5RecoveryAction.Roll && prior.Activity.Completion != op.Activity.Completion) return false;
                    if (op.Command.Request.Action == Sr5RecoveryAction.Start && op.Activity.Phase != Sr5RecoveryPhase.Started
                        || op.Command.Request.Action == Sr5RecoveryAction.Roll && op.Activity.Phase is not (Sr5RecoveryPhase.RollUnresolved or Sr5RecoveryPhase.Rolled)
                        || op.Command.Request.Action == Sr5RecoveryAction.Complete && op.Activity.Phase != Sr5RecoveryPhase.Completed
                        || op.Command.Request.Action == Sr5RecoveryAction.Cancel && op.Activity.Phase != Sr5RecoveryPhase.Cancelled) return false;
                    if (op.Activity.Roll is { } roll && op.Command.Request.Action == Sr5RecoveryAction.Roll
                        && (roll.RollId != op.Command.Request.OperationId || roll.CompletedAtUtc != op.Command.Request.GameTimeUtc)
                        || op.Activity.Completed is { } completed && completed.TransactionId != op.Command.Request.OperationId
                        || op.Activity.Cancelled is { } cancelled && (cancelled.TransactionId != op.Command.Request.OperationId
                            || op.Activity.Cancellation!.RequestedAtUtc != op.Command.Request.GameTimeUtc)) return false;
                }
                latest[op.Activity.Quote.ActivityId] = op; previous = op.Digest; lastRevision = op.RecordedRevision; prefix.Add(op);
            }
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NullReferenceException or OverflowException)
        { return false; }
    }

    public static bool IsValidHistory(WorkspaceStoredDocument saved)
    {
        var ledger = saved.Document.AuxiliaryState.Sr5NaturalRecovery;
        return IsValidLedger(saved.Id, saved.ContentRevision, ledger)
            && (ledger is null || saved.ContentRevision != ledger.Operations[^1].RecordedRevision
                || Hash(saved.Document.Content) == ledger.Operations[^1].PayloadDigest);
    }
    public static Sr5RecoveryResult Lookup(OwnerScope owner, WorkspaceStoredDocument saved, Guid operation, string digest)
    {
        if (!ValidOwner(owner) || !IsDigest(digest) || !IsValidHistory(saved)) return new(Sr5RecoveryOutcome.Conflict);
        var entry = saved.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations.SingleOrDefault(o => o.Command.Request.OperationId == operation);
        if (entry is null) return new(Sr5RecoveryOutcome.NotFound);
        if (entry.Command.OwnerId != owner.NormalizedValue || entry.CommandDigest != digest || !saved.CanReplayReceipt(entry.RecordedRevision))
            return new(Sr5RecoveryOutcome.Conflict);
        return new(entry.Activity.Phase == Sr5RecoveryPhase.RollUnresolved ? Sr5RecoveryOutcome.Unknown : Sr5RecoveryOutcome.Recovered, entry);
    }
    private static bool Allowed(Sr5RecoveryPhase phase, Sr5RecoveryAction action) => action switch
    {
        Sr5RecoveryAction.Start => phase == Sr5RecoveryPhase.Reserved,
        Sr5RecoveryAction.Roll => phase == Sr5RecoveryPhase.Started,
        Sr5RecoveryAction.Complete => phase == Sr5RecoveryPhase.Rolled,
        Sr5RecoveryAction.Cancel => phase is Sr5RecoveryPhase.Reserved or Sr5RecoveryPhase.Started or Sr5RecoveryPhase.Rolled,
        _ => false
    };

    public static XDocument ReadXml(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 67_108_864 });
        var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        Require(doc.Root is { Name.LocalName: "character", HasAttributes: false } && doc.Root.Name.NamespaceName.Length == 0);
        return doc;
    }
    private static XElement? One(XElement root, string name)
    { var all = root.Elements().Where(e => e.Name.LocalName == name).ToArray(); Require(all.Length <= 1 && all.All(e => e.Name == name)); return all.SingleOrDefault(); }
    private static string Scalar(XElement root, string name, string? absent = null)
    {
        var e = One(root, name); if (e is null && absent is not null) return absent;
        Require(e is { HasAttributes: false, HasElements: false }); return e!.Value;
    }
    private static bool Flag(XElement root, string name) => bool.Parse(Scalar(root, name, "false"));
    private static int Number(XElement root, string name) => int.Parse(Scalar(root, name), CultureInfo.InvariantCulture);

    // No display-track alias or saved total is authority. The existing source-backed
    // derived query supplies the complete source-owned condition and recovery channels.
    public static (int Stun, int Physical, int StunCapacity, int PhysicalCapacity, int Body, int Willpower, CharacterSheetConditionValues Condition, string Source, CharacterCareerCalendarState Calendar)
        ReadAuthority(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources)
    {
        Require(saved.ContentRevision > 0 && saved.ContentRevision < long.MaxValue && saved.SavedRevision == saved.ContentRevision
            && saved.Document.Format == WorkspaceDocumentFormat.NativeXml && saved.Document.RulesetId == "sr5"
            // The public legacy constructor and persisted legacy records use "workspace".
            && saved.Document.SchemaVersion == 1 && saved.Document.PayloadKind is "workspace" or "native-xml" or "sr5/chum5-xml");
        var root = ReadXml(saved.Document.Content).Root!;
        Require(Flag(root, "created") && Scalar(root, "gameedition") == "SR5"
            && !Flag(root, "physicalcmiscorecm") && !Flag(root, "stuncmismatrixcm")
            // Windows saves cumulative Physical damage and may omit this legacy field.
            // Explicit overflow remains refused; source-derived capacity is checked below.
            && int.Parse(Scalar(root, "physicalcmoverflow", "0"), CultureInfo.InvariantCulture) == 0);
        int stun = Number(root, "stuncmfilled"), physical = Number(root, "physicalcmfilled");
        Require(stun >= 0 && physical >= 0);
        var context = sources.TryCreateContext(saved.Document.Content);
        Require(context is not null && context.TryIsBookEnabled("SR5", out bool enabled) && enabled);
        // The sheet resolves complete selected-metatype/variant and generated-owner
        // closures. The older creation/quality resolver can legitimately refuse those
        // same sources, so it cannot be a second admission gate for recovery.
        var derived = CharacterSheetDerivedQuery.Parse(saved, sources);
        Require(derived.UnavailableReason is null && derived.Condition is not null
            && CharacterCreationFinalizationDigest.IsCanonical(derived.AuthorityDigest));
        string sourceDigest = derived.AuthorityDigest!;
        var condition = derived.Condition!;
        int stunCapacity = condition.StunMaximum, physicalCapacity = condition.PhysicalMaximum;
        Require(stunCapacity is > 0 and <= Rules.MaximumDamageBoxes && physicalCapacity is > 0 and <= Rules.MaximumDamageBoxes
            && stun <= stunCapacity && physical <= physicalCapacity);
        int body = derived.Attributes.Single(a => a.Name == "BOD").Total, will = derived.Attributes.Single(a => a.Name == "WIL").Total;
        var box = One(root, "calendar");
        Require(box is { HasAttributes: false } && box.Elements().All(e => e.Name == "week"));
        Require(RulesCalendar(box!, out var calendar) && calendar.Weeks.Count > 0);
        var final = CharacterSheetDerivedQuery.Parse(saved, sources);
        Require(final.UnavailableReason is null && final.Condition == condition
            && final.AuthorityDigest == sourceDigest
            && context!.TryIsBookEnabled("SR5", out bool finalBookEnabled) && finalBookEnabled);
        return (stun, physical, stunCapacity, physicalCapacity, body, will, condition,
            Digest(new { sourceDigest, derived.AuthorityDigest, condition }), calendar);
    }
    private static bool RulesCalendar(XElement box, out CharacterCareerCalendarState calendar) =>
        CharacterCareerCalendarRules.TryCreateCalendar(true, CharacterCareerCalendarRules.PinnedSourceAuthority,
            box.Elements().Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray(), out calendar);
    public static IReadOnlyList<Sr5RecoveryCalendarWindow> Windows(CharacterCareerCalendarState calendar) => calendar.Weeks.Select(w =>
    {
        Require(CharacterCareerCalendarRules.TrySelectStart(true, CharacterCareerCalendarRules.PinnedSourceAuthority,
            w.Year, w.Week, out var span));
        return new Sr5RecoveryCalendarWindow(w.Identity.WeekId,
            new DateTimeOffset(span.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            new DateTimeOffset(span.EndDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
    }).OrderBy(w => w.Start).ToArray();
    private static string CalendarDigest(CharacterCareerCalendarState calendar, Sr5RecoveryLedger? ledger, Guid activity) =>
        Digest(new { calendar, Activities = Activities(ledger).Where(a => a.Quote.ActivityId != activity).ToArray() });
    private static bool Fits(CharacterCareerCalendarState calendar, Sr5RecoveryLedger? ledger, DateTimeOffset start, DateTimeOffset end)
    {
        var cursor = start;
        foreach (var window in Windows(calendar)) if (window.Start <= cursor && window.End > cursor) cursor = window.End;
        if (cursor < end) return false;
        foreach (var a in Activities(ledger))
        {
            var from = a.Reservation.Plan.StartsAtUtc;
            var to = a.Reservation.Plan.CompletesAtUtc;
            if (a.Cancelled is not null) to = from + a.Cancelled.Elapsed;
            if (to > from && from < end && to > start) return false;
        }
        return true;
    }
    public static CharacterSr5HealingQuote Quote(WorkspaceStoredDocument saved, ICharacterSourceDataResolver sources,
        string runtime, Guid activityId, CharacterSr5HealingTrack track, DateTimeOffset earliest)
    {
        var a = ReadAuthority(saved, sources); var ledger = saved.Document.AuxiliaryState.Sr5NaturalRecovery;
        int canonicalPool = track == CharacterSr5HealingTrack.Stun ? a.Condition.StunRecoveryPool : a.Condition.PhysicalRecoveryPool;
        int basePool = track == CharacterSr5HealingTrack.Stun ? checked(a.Body + a.Willpower) : checked(2 * a.Body);
        CharacterSr5HealingDicePoolModifier[] modifiers = [new("source-derived-recovery", checked(canonicalPool - basePool), a.Source)];
        Require(Rules.TryCreateQuote(new(saved.Id, saved.ContentRevision, CalendarRevision(ledger), activityId, true, "sr5", track,
            track == CharacterSr5HealingTrack.Stun ? a.Stun : a.Physical, a.Body, a.Willpower, modifiers, earliest,
            runtime, a.Source, Hash(saved.Document.Content), CalendarDigest(a.Calendar, ledger, activityId)), out var quote));
        return quote;
    }
    public static Sr5RecoveryReview Review(OwnerScope owner, WorkspaceStoredDocument saved, Sr5RecoveryRequest request,
        ICharacterSourceDataResolver sources, string runtime, string? currentEnvironmentDigest = null)
    {
        Require(ValidOwner(owner) && ValidRequest(request) && request.WorkspaceId == saved.Id && IsValidHistory(saved));
        var ledger = saved.Document.AuxiliaryState.Sr5NaturalRecovery;
        Require((ledger?.Operations.Count ?? 0) < MaximumOperations && ledger?.Operations.Any(x => x.Command.Request.OperationId == request.OperationId) != true);
        var activity = Activities(ledger).SingleOrDefault(a => a.Quote.ActivityId == request.ActivityId);
        string priorDigest = Digest(activity);
        CharacterSr5HealingCompletionQuote? completion = null; CharacterSr5HealingCancellationQuote? cancellation = null;
        if (request.Action == Sr5RecoveryAction.Reserve)
        {
            Require(activity is null && !Activities(ledger).Any(a => !Terminal(a)));
            var quote = Quote(saved, sources, runtime, request.ActivityId, request.Track, request.GameTimeUtc);
            Require(quote.CanPlan && Rules.TryCreatePlan(quote, request.OperationId, request.StartsAtUtc, out _));
            Rules.TryCreatePlan(quote, request.OperationId, request.StartsAtUtc, out var plan);
            Require(Fits(ReadAuthority(saved, sources).Calendar, ledger, plan.StartsAtUtc, plan.CompletesAtUtc));
            Require(Rules.TryReserve(quote, plan, quote.QuoteDigest, plan.PlanDigest, saved.ContentRevision,
                CalendarRevision(ledger), request.GameTimeUtc, true, out var reservation));
            activity = new(quote, reservation, Sr5RecoveryPhase.Reserved, OtherAuxiliaryDigest(saved.Document));
        }
        else
        {
            Require(activity is not null && Allowed(activity.Phase, request.Action)
                && activity.Quote.Track == request.Track && activity.Reservation.Plan.StartsAtUtc == request.StartsAtUtc
                && saved.CanReplayReceipt(activity.Quote.WorkspaceRevision)
                && ledger!.Operations.First(x => x.Activity.Quote.ActivityId == request.ActivityId).Command.OwnerId == owner.NormalizedValue
                && activity.OtherAuxiliaryDigest == OtherAuxiliaryDigest(saved.Document));
            var current = Quote(saved, sources, runtime, request.ActivityId, request.Track, activity!.Quote.EarliestStartUtc);
            Require(current.QuoteDigest == activity.Quote.QuoteDigest);
            switch (request.Action)
            {
                case Sr5RecoveryAction.Start:
                    Require(Rules.TryStart(current, activity.Reservation, activity.Reservation.ReservationDigest,
                        saved.ContentRevision, CalendarRevision(ledger), request.GameTimeUtc, true, out _)); break;
                case Sr5RecoveryAction.Roll:
                    // This workflow records exactly one scheduled interval.
                    Require(request.GameTimeUtc == activity.Started!.EligibleCompletionUtc); break;
                case Sr5RecoveryAction.Complete:
                    Require(Rules.TryCreateCompletionQuote(current, activity.Started, activity.Roll, out completion) && completion.CanComplete
                        && request.GameTimeUtc == activity.Roll!.CompletedAtUtc); break;
                case Sr5RecoveryAction.Cancel:
                    Require(request.GameTimeUtc <= activity.Reservation.Plan.CompletesAtUtc
                        && request.GameTimeUtc >= (activity.Roll?.CompletedAtUtc
                            ?? activity.Started?.StartedAtUtc ?? activity.Reservation.ReservedAtUtc)
                        && Rules.TryCreateCancellationQuote(current, activity.Started is null ? activity.Reservation : null,
                            activity.Started, request.GameTimeUtc, out cancellation)); break;
            }
        }
        var command = new Sr5RecoveryCommand(request, owner.NormalizedValue, saved.ContentRevision, saved.Document.AuxiliaryStateDigest,
            LedgerDigest(ledger), activity.Quote.QuoteDigest, priorDigest, false)
            { CurrentEnvironmentDigest = currentEnvironmentDigest };
        command = command with { ReviewDigest = ReviewDigest(command) };
        return new(command, activity, completion, cancellation);
    }

    public static (WorkspaceDocument Document, Sr5RecoveryOperation Operation) Apply(WorkspaceStoredDocument saved,
        Sr5RecoveryReview review, Sr5RecoveryCommand command)
    {
        Require(ValidCommand(command) && command == review.Command with { ExplicitlyConfirmed = true });
        var a = review.Activity; var r = command.Request; long revision = saved.ContentRevision; string payload = saved.Document.Content;
        switch (r.Action)
        {
            case Sr5RecoveryAction.Start:
                Require(Rules.TryStart(a.Quote, a.Reservation, a.Reservation.ReservationDigest, revision,
                    a.Quote.CalendarRevision, r.GameTimeUtc, true, out var started));
                a = a with { Phase = Sr5RecoveryPhase.Started, Started = started }; break;
            case Sr5RecoveryAction.Roll:
                a = a with { Phase = Sr5RecoveryPhase.RollUnresolved }; break;
            case Sr5RecoveryAction.Complete:
                Require(Rules.TryCreateCompletionCommand(a.Quote, a.Started, review.Completion, r.OperationId,
                    a.Quote.QuoteDigest, a.Started!.StartDigest, review.Completion!.CompletionQuoteDigest, true, out var complete));
                Require(Rules.TryCreateCompletionReceipt(review.Completion, complete, revision + 1,
                    a.Quote.CalendarRevision + 1, complete.SavedDamageBoxes, out var receipt));
                var xml = ReadXml(payload);
                xml.Root!.Element(a.Quote.Track == CharacterSr5HealingTrack.Stun ? "stuncmfilled" : "physicalcmfilled")!.Value =
                    complete.SavedDamageBoxes.ToString(CultureInfo.InvariantCulture);
                payload = xml.ToString(SaveOptions.DisableFormatting); revision++;
                a = a with { Phase = Sr5RecoveryPhase.Completed, Completed = receipt }; break;
            case Sr5RecoveryAction.Cancel:
                Require(Rules.TryCreateCancellationCommand(review.Cancellation, r.OperationId,
                    review.Cancellation!.CancellationQuoteDigest, review.Cancellation.SubjectDigest, true, out var cancel));
                Require(Rules.TryCreateCancellationReceipt(review.Cancellation, cancel, revision + 1,
                    a.Quote.CalendarRevision + 1, a.Quote.DamageBoxesBefore, out var cancellationReceipt));
                revision++; a = a with { Phase = Sr5RecoveryPhase.Cancelled, Cancellation = review.Cancellation, Cancelled = cancellationReceipt }; break;
        }
        var history = saved.Document.AuxiliaryState.Sr5NaturalRecovery?.Operations ?? [];
        var unsigned = new Sr5RecoveryOperation(command, Digest(command), a, revision, Hash(payload),
            history.Count == 0 ? Hash("sr5-natural-recovery-ledger/v1") : history[^1].Digest, "");
        var op = unsigned with { Digest = OperationDigest(unsigned) };
        var ledger = new Sr5RecoveryLedger(history.Append(op).ToArray());
        Require(IsValidLedger(saved.Id, revision, ledger));
        return (saved.Document with { State = saved.Document.State with { Payload = payload,
            AuxiliaryState = saved.Document.AuxiliaryState with { Sr5NaturalRecovery = ledger } } }, op);
    }

    public static (WorkspaceDocument Document, Sr5RecoveryOperation Operation) FinishRoll(WorkspaceDocument intent,
        Sr5RecoveryOperation fence, IReadOnlyList<int> dice)
    {
        Require(fence.Activity.Phase == Sr5RecoveryPhase.RollUnresolved && dice.Count == fence.Activity.Quote.DicePool);
        var a = fence.Activity;
        Require(Rules.TryCreateRollReceipt(a.Started, a.Started!.StartDigest, fence.Command.Request.OperationId,
            fence.Command.Request.GameTimeUtc, dice, out var roll));
        Require(Rules.TryCreateCompletionQuote(a.Quote, a.Started, roll, out var completion));
        var result = fence with { Activity = a with { Phase = Sr5RecoveryPhase.Rolled, Roll = roll, Completion = completion }, Digest = "" };
        result = result with { Digest = OperationDigest(result) };
        var operations = intent.AuxiliaryState.Sr5NaturalRecovery!.Operations.ToArray();
        Require(operations[^1].Digest == fence.Digest); operations[^1] = result;
        var ledger = new Sr5RecoveryLedger(operations);
        Require(IsValidLedger(a.Quote.WorkspaceId, fence.RecordedRevision, ledger));
        return (intent with { State = intent.State with { AuxiliaryState = intent.AuxiliaryState with { Sr5NaturalRecovery = ledger } } }, result);
    }
}
