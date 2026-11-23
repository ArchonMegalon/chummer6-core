using Chummer.Contracts.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerLifestyleMaintenanceService
{
    CharacterCareerLifestyleMaintenanceLoadResult Load(OwnerScope owner, CharacterWorkspaceId workspaceId);
    CharacterCareerLifestyleMaintenancePreviewResult Preview(OwnerScope owner, CharacterCareerLifestyleMaintenanceRequest request);
    CharacterCareerLifestyleMaintenanceResult Commit(OwnerScope owner, CharacterCareerLifestyleMaintenanceCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerLifestyleMaintenanceResult Lookup(OwnerScope owner, CharacterCareerLifestyleMaintenanceCommand original);
    CharacterCareerLifestyleMaintenanceLoadResult LoadCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

    CharacterCareerLifestyleMaintenancePreviewResult PreviewCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceRequest request, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

    CharacterCareerLifestyleMaintenanceResult CommitCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand command, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

    CharacterCareerLifestyleMaintenanceResult LookupCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand original, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

}

/// <summary>Trusted executable store seam. Revalidate under the common workspace lease; atomically save payload and receipt.</summary>
public interface ICharacterCareerLifestyleMaintenanceAtomicCommitCapability
{
    string CareerLifestyleMaintenanceRuntimeDigest { get; }
    CharacterCareerLifestyleMaintenanceResult CommitCareerLifestyleMaintenance(OwnerScope owner,
        CharacterCareerLifestyleMaintenanceCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}
