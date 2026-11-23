using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerLifestyleCurrentCapability
{
    CharacterCareerLifestyleMaintenanceLoadResult LoadCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestyleMaintenancePreviewResult PreviewCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceRequest request, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestyleMaintenanceResult CommitCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestyleMaintenanceResult LookupCurrentCareerLifestyleMaintenance(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestyleMaintenanceCommand original, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestylePaymentPreviewResult PreviewCurrentCareerLifestylePayment(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentRequest request, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestylePaymentResult CommitCurrentCareerLifestylePayment(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentCommand command, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
    CharacterCareerLifestylePaymentResult LookupCurrentCareerLifestylePayment(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest, ICharacterSourceDataResolver sources, CancellationToken cancellationToken = default);
}
