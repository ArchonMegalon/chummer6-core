using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Optional source-bound reads from the caller's already captured document.</summary>
public interface ICharacterSheetDerivedQueries
{
    CharacterSheetDerivedSection ParseSheetDerived(CharacterDocument document);

    CharacterSheetDerivedSection ParseSheetDerived(Chummer.Application.Workspaces.WorkspaceStoredDocument observation)
        => CharacterSheetDerivedSection.Unavailable("The workspace-bound sheet read authority is unavailable.");
}
