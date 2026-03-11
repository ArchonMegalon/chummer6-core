using Chummer.Contracts.Journal;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Journal;

public interface IJournalProjectionService
{
    JournalProjection BuildProjection(
        string scopeKind,
        string scopeId,
        IReadOnlyList<NoteDocument> notes,
        IReadOnlyList<LedgerEntry> ledgerEntries,
        IReadOnlyList<TimelineEvent> timelineEvents);

    IReadOnlyList<RulesetCapabilityDiagnostic> Validate(JournalProjection projection);
}
