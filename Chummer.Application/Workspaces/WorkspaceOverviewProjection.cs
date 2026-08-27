using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// An overview and canonical validation projected from one immutable stored
/// workspace snapshot.
/// </summary>
public sealed record WorkspaceOverviewProjection(
    WorkspaceDocumentSnapshot Workspace,
    CharacterOverviewProjection Overview,
    CharacterValidationResult Validation);
