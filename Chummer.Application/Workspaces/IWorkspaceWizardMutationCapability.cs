using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;
namespace Chummer.Application.Workspaces;

public interface IWorkspaceWizardMutationCapability
{
    WorkspaceWizardAdvancementCatalog ReadWizardAdvancement(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IOwnerContextLease lease, CharacterWorkspaceId id, long revision, bool attributes, CancellationToken cancellationToken = default);
    WorkspaceWizardResult PreviewWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default);
    WorkspaceWizardResult CommitWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IOwnerContextLease lease, WorkspaceWizardCommand command, CancellationToken cancellationToken = default);
    WorkspaceWizardResult LookupWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default);
}
// The service delegates only closed commands; no replacement or mutation callback.
public interface IWorkspaceWizardMutationStoreCapability : IWorkspaceWizardMutationCapability { }
