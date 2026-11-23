using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCareerLifestyleMaintenanceService
{
    public CharacterCareerLifestyleMaintenanceLoadResult LoadCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.LoadCurrentCareerLifestyleMaintenance(owners, expectedOwner, ownerLease, workspaceId, _sources, cancellationToken)
            : new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
    public CharacterCareerLifestyleMaintenancePreviewResult PreviewCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceRequest request, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.PreviewCurrentCareerLifestyleMaintenance(owners, expectedOwner, ownerLease, request, _sources, cancellationToken)
            : new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
    public CharacterCareerLifestyleMaintenanceResult CommitCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand command, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.CommitCurrentCareerLifestyleMaintenance(owners, expectedOwner, ownerLease, command, _sources, cancellationToken)
            : new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
    public CharacterCareerLifestyleMaintenanceResult LookupCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand original, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.LookupCurrentCareerLifestyleMaintenance(owners, expectedOwner, ownerLease, original, _sources, cancellationToken)
            : new(CharacterCareerLifestyleMaintenanceOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
}
