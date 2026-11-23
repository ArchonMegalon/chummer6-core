using Chummer.Application.Session;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed partial class FileWorkspaceStore
{
    private ISessionPlayStore? _settlementJournal5712;

    /// <summary>Composition only; shares the existing journal. No alternate store or transport.</summary>
    public FileWorkspaceStore WithSessionPlaySettlementJournal5712(ISessionPlayStore? journal)
    {
        if (_settlementJournal5712 is not null && !ReferenceEquals(_settlementJournal5712, journal))
            throw new InvalidOperationException("The workspace Play journal is already bound.");
        _settlementJournal5712 = journal; return this;
    }

    // Lock ordering is identical to settlement: journal first, canonical workspace second.
    private T WithSessionPlayExport5712<T>(OwnerScope owner, CharacterWorkspaceId id, Func<ISessionPlayReader?, T> capture)
        => _settlementJournal5712 is null ? capture(null) : _settlementJournal5712.Inspect(owner, id.Value, capture);

    private static void RequireSettledSessionPlay5712(ISessionPlayReader? reader, WorkspaceDocumentAuxiliaryState? state)
    {
        if (SessionAmmunitionSettlementRules5712.RequiresSettlement(reader,
            SessionAmmunitionSettlementRules5712.Identity(state?.SessionPlaySettlement5712)))
            throw new InvalidOperationException("play-ammunition-needs-after-run-settlement-before-export");
    }
}
