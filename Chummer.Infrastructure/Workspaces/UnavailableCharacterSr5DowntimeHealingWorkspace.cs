using Chummer.Application.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

public sealed class UnavailableCharacterSr5DowntimeHealingWorkspace :
    ICharacterSr5DowntimeHealingWorkspace
{
    private const string Error =
        "sr5_healing_atomic_workspace_persistence_unavailable";

    public CharacterSr5HealingWorkspaceReadResult Read(
        CharacterSr5HealingWorkspaceReadRequest request)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterSr5HealingWorkspaceReserveResult Reserve(
        CharacterSr5HealingWorkspaceReserveRequest request)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterSr5HealingWorkspaceStartResult Start(
        CharacterSr5HealingWorkspaceStartRequest request)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterSr5HealingWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string idempotencyKey,
        string commandDigest)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterSr5HealingWorkspaceCommitResult CommitCompletion(
        CharacterSr5HealingWorkspaceCompletionCommitRequest request)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);

    public CharacterSr5HealingWorkspaceCommitResult CommitCancellation(
        CharacterSr5HealingWorkspaceCancellationCommitRequest request)
        => new(CharacterSr5HealingWorkspaceOutcome.Unavailable, Error: Error);
}
