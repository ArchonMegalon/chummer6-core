using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public enum CharacterSr5HealingActivityTerminalKind
{
    Completed,
    Cancelled
}

/// <summary>
/// Immutable terminal record for one natural-healing activity. The complete reviewed
/// lifecycle is retained so a restart can prove the result without trusting a caller's
/// reconstruction. Reservation and start are review/CAS transitions; only a terminal
/// completion or cancellation changes the workspace and advances the healing calendar.
/// </summary>
public sealed record CharacterSr5HealingActivityLedgerEntry(
    string ContractName,
    CharacterSr5HealingActivityTerminalKind TerminalKind,
    Guid ActivityId,
    Guid TransactionId,
    long ExpectedWorkspaceRevision,
    long CommittedWorkspaceRevision,
    long ExpectedCalendarRevision,
    long CommittedCalendarRevision,
    string IdempotencyKey,
    string CommandDigest,
    string CharacterPayloadDigestBefore,
    string CharacterPayloadDigestAfter,
    CharacterSr5HealingQuote Quote,
    CharacterSr5HealingReservation Reservation,
    CharacterSr5HealingStartedInterval? Started,
    CharacterSr5HealingRollReceipt? Roll,
    CharacterSr5HealingCompletionQuote? CompletionQuote,
    CharacterSr5HealingCompletionCommand? CompletionCommand,
    CharacterSr5HealingCompletionReceipt? CompletionReceipt,
    CharacterSr5HealingCancellationQuote? CancellationQuote,
    CharacterSr5HealingCancellationCommand? CancellationCommand,
    CharacterSr5HealingCancellationReceipt? CancellationReceipt,
    string EntryDigest);

/// <summary>
/// Integrity authority for the workspace-owned SR5 natural-healing activity ledger.
/// Calendar revision 1 is the empty ledger; every terminal append advances it exactly
/// once in the same workspace CAS that stores the character result and receipt.
/// </summary>
public static class CharacterSr5HealingActivityLedgerIntegrity
{
    public const string EntryV1 =
        "chummer.core.sr5-natural-healing-activity-ledger-entry/v1";
    public const long InitialCalendarRevision = 1;
    public const int MaximumEntries = 4096;
    public const string EmptyLedgerDigest =
        "38ae3efb87073df806818dbb302c1763ec022334c6fbbf83018465e654a61a3e";

    public static long GetCalendarRevision(
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? entries)
        => checked(InitialCalendarRevision + (entries?.Count ?? 0));

    public static string ComputeCalendarDigest(
        string? rawCalendarElement,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? entries)
        => Sha256(Canonical(
            "chummer.core.sr5-natural-healing-calendar/v1",
            rawCalendarElement ?? string.Empty,
            entries is { Count: > 0 } ? entries[^1].EntryDigest : EmptyLedgerDigest,
            GetCalendarRevision(entries)));

    public static bool TryCreateCompletionEntry(
        string? characterPayloadBefore,
        string? characterPayloadAfter,
        CharacterSr5HealingQuote? quote,
        CharacterSr5HealingReservation? reservation,
        CharacterSr5HealingStartedInterval? started,
        CharacterSr5HealingRollReceipt? roll,
        CharacterSr5HealingCompletionQuote? completion,
        CharacterSr5HealingCompletionCommand? command,
        CharacterSr5HealingCompletionReceipt? receipt,
        out CharacterSr5HealingActivityLedgerEntry entry)
    {
        entry = null!;
        if (!CharacterSr5DowntimeHealingRules.IsCoherent(quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(reservation, quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(started, quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(roll, started)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(
                completion, quote, started, roll)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(command, completion)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(receipt, completion, command)
            || characterPayloadBefore is null
            || characterPayloadAfter is null)
        {
            return false;
        }

        var unsigned = new CharacterSr5HealingActivityLedgerEntry(
            EntryV1,
            CharacterSr5HealingActivityTerminalKind.Completed,
            command!.ActivityId,
            command.TransactionId,
            command.ExpectedWorkspaceRevision,
            receipt!.AppliedWorkspaceRevision,
            command.ExpectedCalendarRevision,
            receipt.AppliedCalendarRevision,
            command.IdempotencyKey,
            command.CommandDigest,
            PayloadDigest(characterPayloadBefore),
            PayloadDigest(characterPayloadAfter),
            quote!,
            reservation!,
            started,
            roll,
            completion,
            command,
            receipt,
            CancellationQuote: null,
            CancellationCommand: null,
            CancellationReceipt: null,
            EntryDigest: string.Empty);
        entry = unsigned with { EntryDigest = CalculateEntryDigest(unsigned) };
        return IsCoherent(entry.Quote.WorkspaceId, entry);
    }

    public static bool TryCreateCancellationEntry(
        string? characterPayloadBefore,
        string? characterPayloadAfter,
        CharacterSr5HealingQuote? quote,
        CharacterSr5HealingReservation? reservation,
        CharacterSr5HealingStartedInterval? started,
        CharacterSr5HealingCancellationQuote? cancellation,
        CharacterSr5HealingCancellationCommand? command,
        CharacterSr5HealingCancellationReceipt? receipt,
        out CharacterSr5HealingActivityLedgerEntry entry)
    {
        entry = null!;
        bool subjectValid = reservation is not null && started is null
            || reservation is null && started is not null;
        if (!subjectValid
            || !CharacterSr5DowntimeHealingRules.IsCoherent(quote)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(
                cancellation, quote, reservation, started)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(command, cancellation)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(receipt, cancellation, command)
            || characterPayloadBefore is null
            || characterPayloadAfter is null)
        {
            return false;
        }

        CharacterSr5HealingReservation exactReservation = reservation
            ?? started!.Reservation;
        var unsigned = new CharacterSr5HealingActivityLedgerEntry(
            EntryV1,
            CharacterSr5HealingActivityTerminalKind.Cancelled,
            command!.ActivityId,
            command.TransactionId,
            command.ExpectedWorkspaceRevision,
            receipt!.AppliedWorkspaceRevision,
            command.ExpectedCalendarRevision,
            receipt.AppliedCalendarRevision,
            command.IdempotencyKey,
            command.CommandDigest,
            PayloadDigest(characterPayloadBefore),
            PayloadDigest(characterPayloadAfter),
            quote!,
            exactReservation,
            started,
            Roll: null,
            CompletionQuote: null,
            CompletionCommand: null,
            CompletionReceipt: null,
            cancellation,
            command,
            receipt,
            EntryDigest: string.Empty);
        entry = unsigned with { EntryDigest = CalculateEntryDigest(unsigned) };
        return IsCoherent(entry.Quote.WorkspaceId, entry);
    }

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentWorkspaceRevision,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? entries)
    {
        if (entries is null)
        {
            return true;
        }
        if (currentWorkspaceRevision <= 0 || entries.Count > MaximumEntries)
        {
            return false;
        }

        HashSet<Guid> activityIds = [];
        HashSet<Guid> transactionIds = [];
        HashSet<string> idempotencyKeys = new(StringComparer.Ordinal);
        long priorWorkspaceRevision = 0;
        long expectedCalendarRevision = InitialCalendarRevision;
        foreach (CharacterSr5HealingActivityLedgerEntry entry in entries)
        {
            if (!IsCoherent(workspaceId, entry)
                || entry.CommittedWorkspaceRevision > currentWorkspaceRevision
                || entry.CommittedWorkspaceRevision <= priorWorkspaceRevision
                || entry.ExpectedCalendarRevision != expectedCalendarRevision
                || !activityIds.Add(entry.ActivityId)
                || !transactionIds.Add(entry.TransactionId)
                || !idempotencyKeys.Add(entry.IdempotencyKey))
            {
                return false;
            }
            priorWorkspaceRevision = entry.CommittedWorkspaceRevision;
            expectedCalendarRevision = entry.CommittedCalendarRevision;
        }
        return true;
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousWorkspaceRevision,
        long nextWorkspaceRevision,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? current,
        IReadOnlyList<CharacterSr5HealingActivityLedgerEntry>? replacement,
        string? characterPayloadBefore,
        string? characterPayloadAfter)
    {
        if (nextWorkspaceRevision != previousWorkspaceRevision + 1
            || replacement is null
            || replacement.Count != (current?.Count ?? 0) + 1
            || !IsValidLedger(workspaceId, nextWorkspaceRevision, replacement))
        {
            return false;
        }
        if (current is not null
            && !current.Zip(replacement.Take(current.Count)).All(pair =>
                FixedEquals(pair.First.EntryDigest, pair.Second.EntryDigest)))
        {
            return false;
        }

        CharacterSr5HealingActivityLedgerEntry appended = replacement[^1];
        return appended.ExpectedWorkspaceRevision == previousWorkspaceRevision
            && appended.CommittedWorkspaceRevision == nextWorkspaceRevision
            && appended.ExpectedCalendarRevision == GetCalendarRevision(current)
            && appended.CommittedCalendarRevision
                == appended.ExpectedCalendarRevision + 1
            && FixedEquals(
                appended.CharacterPayloadDigestBefore,
                PayloadDigest(characterPayloadBefore))
            && FixedEquals(
                appended.CharacterPayloadDigestAfter,
                PayloadDigest(characterPayloadAfter))
            && HasExactCharacterTransition(
                appended,
                characterPayloadBefore,
                characterPayloadAfter);
    }

    public static bool IsCoherent(
        CharacterWorkspaceId workspaceId,
        CharacterSr5HealingActivityLedgerEntry? entry)
    {
        if (entry is null
            || !string.Equals(entry.ContractName, EntryV1, StringComparison.Ordinal)
            || entry.ActivityId == Guid.Empty
            || entry.TransactionId == Guid.Empty
            || entry.ExpectedWorkspaceRevision <= 0
            || entry.ExpectedWorkspaceRevision == long.MaxValue
            || entry.CommittedWorkspaceRevision != entry.ExpectedWorkspaceRevision + 1
            || entry.ExpectedCalendarRevision <= 0
            || entry.ExpectedCalendarRevision == long.MaxValue
            || entry.CommittedCalendarRevision != entry.ExpectedCalendarRevision + 1
            || !IsDigest(entry.IdempotencyKey)
            || !IsDigest(entry.CommandDigest)
            || !IsDigest(entry.CharacterPayloadDigestBefore)
            || !IsDigest(entry.CharacterPayloadDigestAfter)
            || !IsDigest(entry.EntryDigest)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(entry.Quote)
            || entry.Quote.WorkspaceId != workspaceId
            || entry.Quote.ActivityId != entry.ActivityId
            || entry.Quote.WorkspaceRevision != entry.ExpectedWorkspaceRevision
            || entry.Quote.CalendarRevision != entry.ExpectedCalendarRevision
            || !FixedEquals(
                entry.Quote.ContentDigest,
                entry.CharacterPayloadDigestBefore)
            || !CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.Reservation, entry.Quote))
        {
            return false;
        }

        bool terminalValid = entry.TerminalKind switch
        {
            CharacterSr5HealingActivityTerminalKind.Completed =>
                IsCoherentCompletion(entry),
            CharacterSr5HealingActivityTerminalKind.Cancelled =>
                IsCoherentCancellation(entry),
            _ => false
        };
        return terminalValid
            && FixedEquals(CalculateEntryDigest(entry), entry.EntryDigest);
    }

    private static bool IsCoherentCompletion(
        CharacterSr5HealingActivityLedgerEntry entry)
        => entry.Started is not null
            && entry.Roll is not null
            && entry.CompletionQuote is not null
            && entry.CompletionCommand is not null
            && entry.CompletionReceipt is not null
            && entry.CancellationQuote is null
            && entry.CancellationCommand is null
            && entry.CancellationReceipt is null
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.Started, entry.Quote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(entry.Roll, entry.Started)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CompletionQuote, entry.Quote, entry.Started, entry.Roll)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CompletionCommand, entry.CompletionQuote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CompletionReceipt,
                entry.CompletionQuote,
                entry.CompletionCommand)
            && entry.CompletionCommand.TransactionId == entry.TransactionId
            && FixedEquals(
                entry.CompletionCommand.IdempotencyKey,
                entry.IdempotencyKey)
            && FixedEquals(
                entry.CompletionCommand.CommandDigest,
                entry.CommandDigest)
            && entry.CompletionReceipt.TransactionId == entry.TransactionId
            && entry.CompletionReceipt.ActivityId == entry.ActivityId
            && entry.CompletionReceipt.AppliedWorkspaceRevision
                == entry.CommittedWorkspaceRevision
            && entry.CompletionReceipt.AppliedCalendarRevision
                == entry.CommittedCalendarRevision
            && FixedEquals(
                entry.CompletionReceipt.CommandDigest,
                entry.CommandDigest)
            && FixedEquals(
                entry.CompletionReceipt.ReceiptDigest,
                TerminalReceiptDigest(entry));

    private static bool IsCoherentCancellation(
        CharacterSr5HealingActivityLedgerEntry entry)
    {
        bool reservedCancellation = entry.Started is null
            && entry.CancellationQuote?.Kind
                == CharacterSr5HealingCancellationKind.CancelReservation;
        bool startedCancellation = entry.Started is not null
            && entry.CancellationQuote?.Kind
                == CharacterSr5HealingCancellationKind.InterruptStartedInterval
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.Started, entry.Quote);
        return (reservedCancellation || startedCancellation)
            && entry.Roll is null
            && entry.CompletionQuote is null
            && entry.CompletionCommand is null
            && entry.CompletionReceipt is null
            && entry.CancellationQuote is not null
            && entry.CancellationCommand is not null
            && entry.CancellationReceipt is not null
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CancellationQuote,
                entry.Quote,
                reservedCancellation ? entry.Reservation : null,
                startedCancellation ? entry.Started : null)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CancellationCommand, entry.CancellationQuote)
            && CharacterSr5DowntimeHealingRules.IsCoherent(
                entry.CancellationReceipt,
                entry.CancellationQuote,
                entry.CancellationCommand)
            && entry.CancellationCommand.TransactionId == entry.TransactionId
            && FixedEquals(
                entry.CancellationCommand.IdempotencyKey,
                entry.IdempotencyKey)
            && FixedEquals(
                entry.CancellationCommand.CommandDigest,
                entry.CommandDigest)
            && entry.CancellationReceipt.TransactionId == entry.TransactionId
            && entry.CancellationReceipt.ActivityId == entry.ActivityId
            && entry.CancellationReceipt.AppliedWorkspaceRevision
                == entry.CommittedWorkspaceRevision
            && entry.CancellationReceipt.AppliedCalendarRevision
                == entry.CommittedCalendarRevision
            && FixedEquals(
                entry.CancellationReceipt.CommandDigest,
                entry.CommandDigest)
            && FixedEquals(
                entry.CancellationReceipt.ReceiptDigest,
                TerminalReceiptDigest(entry));
    }

    private static bool HasExactCharacterTransition(
        CharacterSr5HealingActivityLedgerEntry entry,
        string? before,
        string? after)
    {
        if (before is null || after is null)
        {
            return false;
        }
        try
        {
            XDocument beforeDocument = XDocument.Parse(before, LoadOptions.PreserveWhitespace);
            XDocument afterDocument = XDocument.Parse(after, LoadOptions.PreserveWhitespace);
            XElement beforeRoot = beforeDocument.Root!;
            XElement afterRoot = afterDocument.Root!;
            if (beforeRoot.Name.LocalName != "character"
                || afterRoot.Name.LocalName != "character")
            {
                return false;
            }

            string damageName = entry.Quote.Track == CharacterSr5HealingTrack.Stun
                ? "stuncmfilled"
                : "physicalcmfilled";
            XElement[] beforeDamage = beforeRoot.Elements(damageName).Take(2).ToArray();
            XElement[] afterDamage = afterRoot.Elements(damageName).Take(2).ToArray();
            if (beforeDamage.Length != 1
                || afterDamage.Length != 1
                || !int.TryParse(
                    beforeDamage[0].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int damageBefore)
                || !int.TryParse(
                    afterDamage[0].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int damageAfter)
                || damageBefore != entry.Quote.DamageBoxesBefore)
            {
                return false;
            }

            int expectedAfter = entry.TerminalKind
                == CharacterSr5HealingActivityTerminalKind.Completed
                    ? entry.CompletionReceipt!.DamageBoxesAfter
                    : entry.CancellationReceipt!.DamageBoxes;
            if (damageAfter != expectedAfter)
            {
                return false;
            }
            afterDamage[0].Value = beforeDamage[0].Value;
            return XNode.DeepEquals(beforeDocument, afterDocument);
        }
        catch (Exception error) when (error is InvalidOperationException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string TerminalReceiptDigest(
        CharacterSr5HealingActivityLedgerEntry entry)
        => entry.TerminalKind == CharacterSr5HealingActivityTerminalKind.Completed
            ? entry.CompletionReceipt!.ReceiptDigest
            : entry.CancellationReceipt!.ReceiptDigest;

    private static string CalculateEntryDigest(
        CharacterSr5HealingActivityLedgerEntry entry)
        => Sha256(Canonical(
            entry.ContractName,
            entry.TerminalKind,
            entry.ActivityId,
            entry.TransactionId,
            entry.ExpectedWorkspaceRevision,
            entry.CommittedWorkspaceRevision,
            entry.ExpectedCalendarRevision,
            entry.CommittedCalendarRevision,
            entry.IdempotencyKey,
            entry.CommandDigest,
            entry.CharacterPayloadDigestBefore,
            entry.CharacterPayloadDigestAfter,
            entry.Quote.QuoteDigest,
            entry.Reservation.ReservationDigest,
            entry.Started?.StartDigest ?? string.Empty,
            entry.Roll?.RollDigest ?? string.Empty,
            entry.CompletionQuote?.CompletionQuoteDigest ?? string.Empty,
            entry.CompletionCommand?.CommandDigest ?? string.Empty,
            entry.CompletionReceipt?.ReceiptDigest ?? string.Empty,
            entry.CancellationQuote?.CancellationQuoteDigest ?? string.Empty,
            entry.CancellationCommand?.CommandDigest ?? string.Empty,
            entry.CancellationReceipt?.ReceiptDigest ?? string.Empty));

    private static string PayloadDigest(string? value)
        => Sha256(value ?? string.Empty);

    private static bool IsDigest(string? value)
        => value is { Length: CharacterSr5DowntimeHealingRules.DigestLength }
            && value.All(static character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f');

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
}
