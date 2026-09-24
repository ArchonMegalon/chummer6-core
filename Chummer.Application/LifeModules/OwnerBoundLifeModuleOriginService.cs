using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.LifeModules;

public interface IOwnerBoundLifeModuleOriginService
{
    bool IsCurrent(OwnerContextStamp owner);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(OwnerContextStamp owner, string workspaceId);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null);
    LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string previewDigest, string idempotencyKey, bool explicitlyConfirmed);
}

/// <summary>
/// Admits each synchronous Origin operation for one exact owner transition and
/// workspace. No lease, evaluator, or mutable owner context escapes the call.
/// </summary>
public sealed class OwnerBoundLifeModuleOriginService(
    IWorkspaceStore store, IOwnerContextAccessor owners, ICharacterFileQueries characterFiles,
    ICharacterSourceDataResolver sourceResolver, ILifeModulesCatalogService catalog) : IOwnerBoundLifeModuleOriginService
{
    public bool IsCurrent(OwnerContextStamp owner)
    {
        if (!OwnerContextAdmission.TryAcquire(owners, owner, out var lease)) return false;
        using (lease) return !owner.Owner.UsesLocalSingleUserValue || owner.Owner.IsLocalSingleUser;
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Start(
        OwnerContextStamp owner, string workspaceId)
    {
        var result = Invoke(owner, workspaceId, service => service.Start(workspaceId));
        return result.Value is { } checkpoint && !MatchesOwner(owner, checkpoint)
            ? Denied<LifeModuleOriginDossierDraftCheckpoint>() : result;
    }

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Restore(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Restore(checkpoint))
            : Denied<LifeModuleOriginDossierDraftCheckpoint>();

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierDraftCheckpoint> Prepare(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string choiceId, IReadOnlyDictionary<string, string>? followUpValues = null)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Prepare(checkpoint, choiceId, followUpValues))
            : Denied<LifeModuleOriginDossierDraftCheckpoint>();

    public LifeModuleOriginDossierResult<LifeModuleOriginDossierInteractionAdvance> Confirm(
        OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint,
        string previewDigest, string idempotencyKey, bool explicitlyConfirmed)
        => MatchesOwner(owner, checkpoint)
            ? Invoke(owner, checkpoint.WorkspaceId, service => service.Confirm(
                checkpoint, previewDigest, idempotencyKey, explicitlyConfirmed))
            : Denied<LifeModuleOriginDossierInteractionAdvance>();

    private static bool MatchesOwner(OwnerContextStamp owner, LifeModuleOriginDossierDraftCheckpoint checkpoint)
        => checkpoint.OwnerId == owner.Owner.NormalizedValue;

    private LifeModuleOriginDossierResult<T> Invoke<T>(OwnerContextStamp owner, string workspaceId,
        Func<LifeModuleOriginDossierInteractionService, LifeModuleOriginDossierResult<T>> action) where T : class
    {
        if (string.IsNullOrWhiteSpace(workspaceId)
            || (owner.Owner.UsesLocalSingleUserValue && !owner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(owners, owner, out var lease))
            return Denied<T>();
        using (lease)
        {
            using ICharacterSourceDataResolverOperationScope? sourceScope =
                (sourceResolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            var view = new OwnerBoundCreationWorkspaceStore(store, lease, owner, new CharacterWorkspaceId(workspaceId));
            var foundation = new CharacterCreationFoundationService(view, characterFiles,
                sourceScope ?? sourceResolver, catalog, new CharacterCreationFoundationDraftApplyAuthority(view));
            var authority = new CharacterCreationFoundationLifeModuleDecisionAuthority(
                view, foundation, characterFiles, owner.Owner.NormalizedValue);
            return action(new(new(authority)));
        }
    }

    private static LifeModuleOriginDossierResult<T> Denied<T>() where T : class
        => new(LifeModuleOriginDossierOutcomes.Blocked, null, [LifeModuleOriginDossierBlockers.AuthorityInvalid]);
}
