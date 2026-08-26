using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Safe fallback for workspace-store compositions that cannot atomically replace
/// and checkpoint a saved character.
/// </summary>
public sealed class UnavailableCharacterAfterRunSettlementWorkspace :
    ICharacterAfterRunSettlementWorkspace
{
    private const string Error =
        "An atomic After Run settlement workspace authority is not configured.";

    public CharacterAfterRunSettlementWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterAfterRunSettlementIdentity identity)
        => new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterAfterRunSettlementWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
        => new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterAfterRunSettlementWorkspaceCommitResult Commit(
        CharacterAfterRunSettlementWorkspaceCommitRequest request)
        => new(CharacterAfterRunSettlementWorkspaceOutcome.Unavailable, Error: Error);
}
