using Chummer.Contracts.Journal;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Journal;

public sealed class DefaultJournalProjectionService : IJournalProjectionService
{
    public JournalProjection BuildProjection(
        string scopeKind,
        string scopeId,
        IReadOnlyList<NoteDocument> notes,
        IReadOnlyList<LedgerEntry> ledgerEntries,
        IReadOnlyList<TimelineEvent> timelineEvents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(ledgerEntries);
        ArgumentNullException.ThrowIfNull(timelineEvents);

        string normalizedScopeKind = Normalize(scopeKind);
        string normalizedScopeId = scopeId.Trim();

        NoteDocument[] orderedNotes = notes
            .Where(note => MatchesScope(note.ScopeKind, note.ScopeId, normalizedScopeKind, normalizedScopeId))
            .Select(static note => note with
            {
                Blocks = note.Blocks
                    .OrderBy(static block => block.CreatedAtUtc)
                    .ThenBy(static block => block.BlockId, StringComparer.Ordinal)
                    .ToArray()
            })
            .OrderBy(static note => note.UpdatedAtUtc)
            .ThenBy(static note => note.NoteId, StringComparer.Ordinal)
            .ToArray();
        LedgerEntry[] orderedLedgerEntries = ledgerEntries
            .Where(entry => MatchesScope(entry.ScopeKind, entry.ScopeId, normalizedScopeKind, normalizedScopeId))
            .OrderBy(static entry => entry.OccurredAtUtc)
            .ThenBy(static entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();
        TimelineEvent[] orderedTimelineEvents = timelineEvents
            .Where(entry => MatchesScope(entry.ScopeKind, entry.ScopeId, normalizedScopeKind, normalizedScopeId))
            .OrderBy(static entry => entry.StartsAtUtc)
            .ThenBy(static entry => entry.EventId, StringComparer.Ordinal)
            .ToArray();

        return new JournalProjection(
            ScopeKind: normalizedScopeKind,
            ScopeId: normalizedScopeId,
            Notes: orderedNotes,
            LedgerEntries: orderedLedgerEntries,
            TimelineEvents: orderedTimelineEvents);
    }

    public IReadOnlyList<RulesetCapabilityDiagnostic> Validate(JournalProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        List<RulesetCapabilityDiagnostic> diagnostics = [];
        string normalizedScopeKind = Normalize(projection.ScopeKind);
        string normalizedScopeId = projection.ScopeId.Trim();

        AppendDuplicateIdDiagnostics(
            diagnostics,
            projection.Notes.Select(static note => note.NoteId),
            "journal.note.duplicate-id");
        AppendDuplicateIdDiagnostics(
            diagnostics,
            projection.LedgerEntries.Select(static entry => entry.EntryId),
            "journal.ledger.duplicate-id");
        AppendDuplicateIdDiagnostics(
            diagnostics,
            projection.TimelineEvents.Select(static entry => entry.EventId),
            "journal.timeline.duplicate-id");

        HashSet<string> noteIds = projection.Notes.Select(static note => note.NoteId).ToHashSet(StringComparer.Ordinal);
        HashSet<string> ledgerEntryIds = projection.LedgerEntries.Select(static entry => entry.EntryId).ToHashSet(StringComparer.Ordinal);

        foreach (NoteDocument note in projection.Notes)
        {
            if (!MatchesScope(note.ScopeKind, note.ScopeId, normalizedScopeKind, normalizedScopeId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.note.scope-mismatch",
                    subjectId: note.NoteId,
                    ("scopeKind", note.ScopeKind),
                    ("scopeId", note.ScopeId)));
            }
        }

        foreach (LedgerEntry entry in projection.LedgerEntries)
        {
            if (!MatchesScope(entry.ScopeKind, entry.ScopeId, normalizedScopeKind, normalizedScopeId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.ledger.scope-mismatch",
                    subjectId: entry.EntryId,
                    ("scopeKind", entry.ScopeKind),
                    ("scopeId", entry.ScopeId)));
            }

            if (entry.NoteId is not null && !noteIds.Contains(entry.NoteId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.ledger.note-missing",
                    subjectId: entry.EntryId,
                    ("noteId", entry.NoteId)));
            }
        }

        foreach (TimelineEvent entry in projection.TimelineEvents)
        {
            if (!MatchesScope(entry.ScopeKind, entry.ScopeId, normalizedScopeKind, normalizedScopeId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.timeline.scope-mismatch",
                    subjectId: entry.EventId,
                    ("scopeKind", entry.ScopeKind),
                    ("scopeId", entry.ScopeId)));
            }

            if (entry.NoteId is not null && !noteIds.Contains(entry.NoteId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.timeline.note-missing",
                    subjectId: entry.EventId,
                    ("noteId", entry.NoteId)));
            }

            if (entry.LedgerEntryId is not null && !ledgerEntryIds.Contains(entry.LedgerEntryId))
            {
                diagnostics.Add(Diagnostic(
                    "journal.timeline.ledger-missing",
                    subjectId: entry.EventId,
                    ("ledgerEntryId", entry.LedgerEntryId)));
            }

            if (entry.EndsAtUtc is DateTimeOffset endsAtUtc && endsAtUtc < entry.StartsAtUtc)
            {
                diagnostics.Add(Diagnostic(
                    "journal.timeline.invalid-range",
                    subjectId: entry.EventId,
                    ("startsAtUtc", entry.StartsAtUtc),
                    ("endsAtUtc", endsAtUtc)));
            }
        }

        return diagnostics
            .OrderBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Message, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendDuplicateIdDiagnostics(
        List<RulesetCapabilityDiagnostic> diagnostics,
        IEnumerable<string> ids,
        string code)
    {
        foreach (IGrouping<string, string> duplicate in ids
                     .Where(static id => !string.IsNullOrWhiteSpace(id))
                     .GroupBy(static id => id, StringComparer.Ordinal)
                     .Where(static group => group.Count() > 1)
                     .OrderBy(static group => group.Key, StringComparer.Ordinal))
        {
            diagnostics.Add(Diagnostic(code, duplicate.Key, ("id", duplicate.Key)));
        }
    }

    private static RulesetCapabilityDiagnostic Diagnostic(
        string code,
        string subjectId,
        params (string Name, object? Value)[] parameters)
    {
        RulesetExplainParameter[] explainParameters =
        [
            Param("subjectId", subjectId),
            .. parameters.Select(static parameter => Param(parameter.Name, parameter.Value))
        ];

        return new RulesetCapabilityDiagnostic(
            Code: code,
            Message: code,
            Severity: RulesetCapabilityDiagnosticSeverities.Warning,
            MessageKey: code,
            MessageParameters: explainParameters);
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));

    private static bool MatchesScope(string scopeKind, string scopeId, string expectedScopeKind, string expectedScopeId)
        => string.Equals(Normalize(scopeKind), expectedScopeKind, StringComparison.Ordinal)
           && string.Equals(scopeId.Trim(), expectedScopeId, StringComparison.Ordinal);

    private static string Normalize(string value)
        => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
}
