using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterSr5NaturalRecoveryService
{
    Sr5RecoveryScreen? Read(OwnerScope owner, CharacterWorkspaceId workspace);
    Sr5RecoveryResult Preview(OwnerScope owner, Sr5RecoveryRequest request);
    Sr5RecoveryResult Execute(OwnerScope owner, Sr5RecoveryCommand command, CancellationToken token = default);
    Sr5RecoveryResult Recover(OwnerScope owner, CharacterWorkspaceId workspace, Guid operationId, string digest);
}

public interface ICharacterSr5NaturalRecoveryStore
{
    bool SupportsNaturalRecovery { get; }
    string NaturalRecoveryRuntimeDigest { get; }
    Sr5RecoveryResult ExecuteNaturalRecovery(OwnerScope owner, Sr5RecoveryCommand command,
        ICharacterSourceDataResolver sources, CancellationToken token);
}
