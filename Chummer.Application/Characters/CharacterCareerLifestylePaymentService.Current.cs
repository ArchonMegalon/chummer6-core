using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCareerLifestylePaymentService
{
    public CharacterCareerLifestylePaymentPreviewResult PreviewCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentRequest request, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.PreviewCurrentCareerLifestylePayment(owners, expectedOwner, ownerLease, request, _sources, cancellationToken)
            : new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
    public CharacterCareerLifestylePaymentResult CommitCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentCommand command, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.CommitCurrentCareerLifestylePayment(owners, expectedOwner, ownerLease, command, _sources, cancellationToken)
            : new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
    public CharacterCareerLifestylePaymentResult LookupCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest, CancellationToken cancellationToken = default)
        => _store is ICharacterCareerLifestyleCurrentCapability current
            ? current.LookupCurrentCareerLifestylePayment(owners, expectedOwner, ownerLease, workspaceId, operationId, commandDigest, _sources, cancellationToken)
            : new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");
}
