using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Safe default for hosts that have not supplied an atomic Career mutation backend.
/// It prevents a DI composition from silently falling back to presentation-side writes.
/// </summary>
public sealed class UnavailableCharacterCareerSkillGroupAdvanceWorkspace :
    ICharacterCareerSkillGroupAdvanceWorkspace
{
    private const string Error =
        "An atomic Career skill-group workspace authority is not configured.";

    public CharacterCareerSkillGroupWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterCareerSkillGroupIdentity identity)
        => new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterCareerSkillGroupWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
        => new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterCareerSkillGroupWorkspaceCommitResult Commit(
        CharacterCareerSkillGroupWorkspaceCommitRequest request)
        => new(CharacterCareerSkillGroupWorkspaceOutcome.Unavailable, Error: Error);
}
