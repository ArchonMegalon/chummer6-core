using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Supplies edition-owned inputs for an empty pending draft. Persistence and
/// owner admission remain in CharacterCreationBootstrapService. Implementations
/// must not use another edition's settings, catalog, or default metatype.
/// </summary>
public interface IRulesetCharacterCreationBootstrapProvider
{
    string RulesetId { get; }

    bool TryPrepareBinding(CharacterWorkspaceId workspaceId, WorkspaceDocument document,
        out CharacterCreationBootstrapBinding binding, out IReadOnlyList<string> sourceAnchorIds,
        out IReadOnlyList<string> blockers);
}
