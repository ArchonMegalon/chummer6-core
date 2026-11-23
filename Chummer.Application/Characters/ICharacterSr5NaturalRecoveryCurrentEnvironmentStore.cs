using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// Observations and mutations stay inside the store's admitted retained-source scope.
// No resolver/capture returned by the callback is a transferable write capability.
public interface ICharacterSr5NaturalRecoveryCurrentEnvironmentStore
{
    bool SupportsRetainedNaturalRecovery { get; }
    TResult ReadNaturalRecovery<TResult>(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, ICharacterSourceDataResolver sources,
        Func<WorkspaceStoredDocument, ICharacterSourceDataResolver, string, string?, TResult> project);
    Sr5RecoveryResult LookupNaturalRecovery(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        CharacterWorkspaceId workspace, Guid operationId, string digest);
    Sr5RecoveryResult ExecuteNaturalRecovery(IOwnerContextLeaseAccessor owners, IOwnerContextLease lease,
        Sr5RecoveryCommand command, ICharacterSourceDataResolver sources, CancellationToken token);
}
