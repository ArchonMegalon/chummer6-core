using Chummer.Application.Owners;
using Chummer.Contracts.Workspaces;
namespace Chummer.Application.Workspaces;
public sealed partial class WorkspaceService : IWorkspaceWizardMutationCapability
{
    public WorkspaceWizardAdvancementCatalog ReadWizardAdvancement(IOwnerContextAccessor owners, OwnerContextStamp owner,
        IOwnerContextLease lease, CharacterWorkspaceId id, long revision, bool attributes, CancellationToken cancellationToken = default)
        => _workspaceStore is IWorkspaceWizardMutationStoreCapability typed
            ? typed.ReadWizardAdvancement(owners, owner, lease, id, revision, attributes, cancellationToken)
            : new(WorkspaceWizardOutcome.Rejected,id,revision,[],[],0,[],0,"Typed advancement is unavailable.");
    public WorkspaceWizardResult PreviewWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp owner,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default)
        => _workspaceStore is IWorkspaceWizardMutationStoreCapability typed
            ? typed.PreviewWizardMutation(owners, owner, lease, request, cancellationToken) : WizardUnavailable();
    public WorkspaceWizardResult CommitWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp owner,
        IOwnerContextLease lease, WorkspaceWizardCommand command, CancellationToken cancellationToken = default)
        => _workspaceStore is IWorkspaceWizardMutationStoreCapability typed
            ? typed.CommitWizardMutation(owners, owner, lease, command, cancellationToken) : WizardUnavailable();
    public WorkspaceWizardResult LookupWizardMutation(IOwnerContextAccessor owners, OwnerContextStamp owner,
        IOwnerContextLease lease, WorkspaceWizardRequest request, CancellationToken cancellationToken = default)
        => _workspaceStore is IWorkspaceWizardMutationStoreCapability typed
            ? typed.LookupWizardMutation(owners, owner, lease, request, cancellationToken) : WizardUnavailable();
    private static WorkspaceWizardResult WizardUnavailable() => new(WorkspaceWizardOutcome.Rejected,
        Error: "Typed wizard authority is unavailable.");
}
