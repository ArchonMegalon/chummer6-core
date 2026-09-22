using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

public sealed class Sr6CreationFoundationService(IWorkspaceStore store, IOwnerContextAccessor ownerContext)
    : ISr6CreationFoundationService
{
    public CharacterCreationFoundationResult<Sr6CreationFoundationState> Load(OwnerContextStamp owner, CharacterWorkspaceId id)
    {
        if (!TryAcquire(owner, out var lease)) return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        using (lease)
        {
            var read = owner.Owner.IsLocalSingleUser ? store.Get(id) : store.Get(owner.Owner, id);
            return read.Value is { } saved && read.Success ? Sr6CreationFoundationRules.Load(saved)
                : Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationState>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        }
    }

    public CharacterCreationFoundationResult<Sr6CreationFoundationPreview> Preview(OwnerContextStamp owner,
        Sr6CreationFoundationBinding binding, Sr6CreationFoundationSelection selection)
    {
        if (!TryAcquire(owner, out var lease)) return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        using (lease)
        {
            if (!Sr6CreationFoundationIntegrity.ValidBinding(binding))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.StaleBinding);
            var read = owner.Owner.IsLocalSingleUser ? store.Get(binding.WorkspaceId) : store.Get(owner.Owner, binding.WorkspaceId);
            if (!read.Success || read.Value is not { } saved || Sr6CreationFoundationRules.Load(saved).Value is not { } current
                || current.Binding != binding)
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationPreview>(Sr6CreationFoundationBlockers.StaleBinding);
            return Sr6CreationFoundationRules.Preview(saved.Document.AuxiliaryState.CharacterCreationBootstrapBinding!, binding, selection);
        }
    }

    public CharacterCreationFoundationResult<Sr6CreationCharacterProjection> ProjectCharacter(OwnerContextStamp owner,
        Sr6CreationFoundationBinding binding)
    {
        if (!TryAcquire(owner, out var lease))
            return Sr6CreationFoundationRules.Blocked<Sr6CreationCharacterProjection>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        using (lease)
        {
            if (!Sr6CreationFoundationIntegrity.ValidBinding(binding))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationCharacterProjection>(Sr6CreationFoundationBlockers.StaleBinding);
            var read = owner.Owner.IsLocalSingleUser ? store.Get(binding.WorkspaceId) : store.Get(owner.Owner, binding.WorkspaceId);
            if (!read.Success || read.Value is not { } saved)
                return Sr6CreationFoundationRules.Blocked<Sr6CreationCharacterProjection>(Sr6CreationFoundationBlockers.StaleBinding);
            var result = Sr6CreationCharacterProjector.Project(saved);
            return result.Value is { } projection && projection.Binding != binding
                ? Sr6CreationFoundationRules.Blocked<Sr6CreationCharacterProjection>(Sr6CreationFoundationBlockers.StaleBinding)
                : result;
        }
    }

    public CharacterCreationFoundationResult<Sr6CreationFoundationCommit> Confirm(OwnerContextStamp owner,
        Sr6CreationFoundationConfirmRequest request)
    {
        if (!TryAcquire(owner, out var lease)) return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.WorkspaceUnavailable);
        using (lease)
        {
            if (!Sr6CreationFoundationIntegrity.TryFreezeRequest(request, out request))
                return Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.ConfirmationRequired);
            return store is ISr6CreationFoundationAtomicCommitCapability atomic
                ? atomic.CommitSr6Foundation(owner.Owner, request)
                : Sr6CreationFoundationRules.Blocked<Sr6CreationFoundationCommit>(Sr6CreationFoundationBlockers.PersistenceUnavailable);
        }
    }

    private bool TryAcquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
    {
        lease = null;
        return expected.IsValid && ownerContext is IOwnerContextLeaseAccessor authority
            && authority.TryAcquire(expected, out lease);
    }
}
