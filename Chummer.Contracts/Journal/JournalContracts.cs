using Chummer.Contracts.Owners;

namespace Chummer.Contracts.Journal;

public static class JournalScopeKinds
{
    public const string Character = "character";
    public const string Session = "session";
    public const string Campaign = "campaign";
    public const string Asset = "asset";
}

public static class NoteBlockKinds
{
    public const string Paragraph = "paragraph";
    public const string Checklist = "checklist";
    public const string Quote = "quote";
    public const string Code = "code";
}

public static class LedgerEntryKinds
{
    public const string Karma = "karma";
    public const string Nuyen = "nuyen";
    public const string Expense = "expense";
    public const string Income = "income";
    public const string Training = "training";
    public const string Custom = "custom";
}

public static class TimelineEventKinds
{
    public const string Session = "session";
    public const string Downtime = "downtime";
    public const string Training = "training";
    public const string Milestone = "milestone";
    public const string Reminder = "reminder";
    public const string Note = "note";
}

public sealed record NoteBlock(
    string BlockId,
    string Kind,
    string Content,
    DateTimeOffset CreatedAtUtc);

public sealed record NoteDocument(
    string NoteId,
    OwnerScope Owner,
    string ScopeKind,
    string ScopeId,
    string Title,
    IReadOnlyList<NoteBlock> Blocks,
    DateTimeOffset UpdatedAtUtc);

public sealed record LedgerEntry(
    string EntryId,
    OwnerScope Owner,
    string ScopeKind,
    string ScopeId,
    string Kind,
    decimal Amount,
    string Currency,
    string Label,
    DateTimeOffset OccurredAtUtc,
    string? NoteId = null);

public sealed record TimelineEvent(
    string EventId,
    OwnerScope Owner,
    string ScopeKind,
    string ScopeId,
    string Kind,
    string Title,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset? EndsAtUtc = null,
    string? NoteId = null,
    string? LedgerEntryId = null);

public sealed record JournalProjection(
    string ScopeKind,
    string ScopeId,
    IReadOnlyList<NoteDocument> Notes,
    IReadOnlyList<LedgerEntry> LedgerEntries,
    IReadOnlyList<TimelineEvent> TimelineEvents);
