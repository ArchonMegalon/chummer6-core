using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Runs existing rules and atomic persistence in one admitted partition.</summary>
public sealed class OwnerBoundCharacterCreationLifestylesService(
    IWorkspaceStore store,
    IOwnerContextAccessor ownerContext,
    ICharacterSourceDataResolver sourceResolver,
    ICharacterFileQueries? completionQueries = null) : IOwnerBoundCharacterCreationLifestylesService,
    IOwnerBoundCharacterCreationLifestylesCompletionReader
{
    public CharacterCreationLifestyleResult<CharacterCreationLifestylesCompletionInputs> LoadCompletionInputs(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationLifestyleOutcomes.Blocked, null,
                [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision]);
        using (lease)
        {
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, request.WorkspaceId,
                reuseReadObservation: true);
            using ICharacterSourceDataResolverOperationScope? scope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            return new CharacterCreationLifestylesService(view, scope ?? sourceResolver, completionQueries)
                .LoadCompletionInputs(request);
        }
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.Load(request));
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationLifestylePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Preview(request));
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationLifestyleConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        return Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> LookupReceipt(
        OwnerContextStamp expectedOwner, CharacterCreationLifestyleReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Invoke(expectedOwner, request.WorkspaceId, service => service.LookupReceipt(request));
    }

    private CharacterCreationLifestyleResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId workspaceId,
        Func<CharacterCreationLifestylesService, CharacterCreationLifestyleResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationLifestyleOutcomes.Blocked, null,
                [CharacterCreationLifestylesBlockers.StaleWorkspaceRevision]);

        using (lease)
        {
            // No ambient fallback and no lease across await. Read, evaluation
            // and the existing atomic commit share this exact owner/workspace.
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, workspaceId);
            using ICharacterSourceDataResolverOperationScope? scope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            return action(new CharacterCreationLifestylesService(view, scope ?? sourceResolver, completionQueries));
        }
    }
}
