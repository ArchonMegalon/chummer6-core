using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>Every purchase/read, including local and historical recovery, requires a fresh issuing owner lease.</summary>
public sealed class WorkspaceCharacterCareerGearPurchaseService(IWorkspaceStore store,
    ICharacterSourceDataResolver sources, IOwnerContextAccessor owners) : ICharacterCareerGearPurchaseService
{
    private T WithOwner<T>(OwnerScope owner, Func<FileWorkspaceStore, IOwnerContextLeaseAccessor, IOwnerContextLease, T> action, T refused)
    {
        if (!CharacterCareerGearPurchaseTransaction.IsValidOwner(owner)
            || store is not FileWorkspaceStore files || owners is not IOwnerContextLeaseAccessor authority) return refused;
        var stamp = authority.Capture();
        if (!stamp.IsValid || stamp.Owner != owner || stamp.Owner.IsLocalSingleUser != owner.IsLocalSingleUser
            || !authority.TryAcquire(stamp, out var lease) || lease is null) return refused;
        using (lease)
        {
            if (lease.Stamp != stamp || authority.Capture() != stamp) return refused;
            return action(files, authority, lease);
        }
    }
    public CharacterCareerGearPurchaseCatalogResult QueryCatalog(OwnerScope owner, CharacterCareerGearPurchaseCatalogQuery query)
        => WithOwner(owner, (files, authority, lease) => files.QueryCurrentCareerGearPurchase(authority, lease, sources, query),
            new CharacterCareerGearPurchaseCatalogResult(CharacterCareerGearPurchaseOutcome.Conflict, null, null, ["gear_owner_changed"]));
    public CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection selection)
        => selection is null ? new(CharacterCareerGearPurchaseOutcome.Conflict) : PreviewCore(owner, request, selection);
    public CharacterCareerGearPurchasePreviewResult Preview(OwnerScope owner, CharacterCareerGearPurchaseRequest request)
        => PreviewCore(owner, request, null);
    private CharacterCareerGearPurchasePreviewResult PreviewCore(OwnerScope owner, CharacterCareerGearPurchaseRequest request,
        CharacterCareerGearPurchaseCatalogSelection? selection)
        => WithOwner(owner, (files, authority, lease) => files.PreviewCurrentCareerGearPurchase(authority, lease, sources, request, selection),
            new CharacterCareerGearPurchasePreviewResult(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_owner_changed"));
    public CharacterCareerGearPurchaseResult Commit(OwnerScope owner, CharacterCareerGearPurchaseCommand command,
        CancellationToken cancellationToken = default)
        => WithOwner(owner, (files, authority, lease) => files.CommitCurrentCareerGearPurchase(authority, lease, sources, command, cancellationToken),
            new CharacterCareerGearPurchaseResult(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_owner_changed"));
    public CharacterCareerGearPurchaseResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest)
        => WithOwner(owner, (files, authority, lease) => files.LookupCurrentCareerGearPurchase(authority, lease, workspaceId, operationId, commandDigest),
            new CharacterCareerGearPurchaseResult(CharacterCareerGearPurchaseOutcome.Conflict, Error: "gear_owner_changed"));
}
