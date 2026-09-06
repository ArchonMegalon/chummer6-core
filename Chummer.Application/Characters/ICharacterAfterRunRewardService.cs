using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Local offline manual reward authority over one saved workspace. No server,
/// actor, signed GM approval, or authenticated run authority is implied.
/// Preview reads and quotes unconfirmed intent without writing or reserving.
/// Show its exact outcomes, obtain confirmation, and change only the returned
/// Command.ExplicitlyConfirmed flag; Commit independently rechecks its binding.
/// Hosts must durably persist caller-owned logical RewardId, OperationId and the
/// complete command before Commit. Retry or query the same command and identities
/// after an unknown result; do not allocate new IDs or prepare new balances.
/// Exactly-once handling prevents repeats of those identities, not deliberate
/// new manual grants with a new RewardId. Historical replay is evidence of the
/// original commit and does not claim its expenses remain unchanged today.
/// </summary>
public interface ICharacterAfterRunRewardService
{
    CharacterAfterRunRewardReadResult Read(CharacterWorkspaceId workspaceId);

    CharacterAfterRunRewardPreviewResult Preview(CharacterAfterRunRewardPreviewRequest request);

    CharacterAfterRunRewardResult Commit(
        CharacterAfterRunRewardCommand command,
        CancellationToken cancellationToken = default);

    CharacterAfterRunRewardResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid operationId,
        string commandDigest);
}
