using Chummer.Contracts.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCareerLifestylePaymentService
{
    CharacterCareerLifestylePaymentPreviewResult Preview(OwnerScope owner, CharacterCareerLifestylePaymentRequest request);
    CharacterCareerLifestylePaymentResult Commit(OwnerScope owner, CharacterCareerLifestylePaymentCommand command,
        CancellationToken cancellationToken = default);
    CharacterCareerLifestylePaymentResult Lookup(OwnerScope owner, CharacterWorkspaceId workspaceId,
        Guid operationId, string commandDigest);
    CharacterCareerLifestylePaymentPreviewResult PreviewCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentRequest request, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

    CharacterCareerLifestylePaymentResult CommitCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterCareerLifestylePaymentCommand command, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

    CharacterCareerLifestylePaymentResult LookupCurrent(IOwnerContextAccessor owners, OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease,
        CharacterWorkspaceId workspaceId, Guid operationId, string commandDigest, CancellationToken cancellationToken = default)
        => new(CharacterCareerLifestylePaymentOutcome.Unavailable, Error: "lifestyle_current_route_unavailable");

}

/// <summary>Trusted executable store seam. Revalidate under the common workspace lease; atomically save payload and receipt.</summary>
public interface ICharacterCareerLifestylePaymentAtomicCommitCapability
{
    string CareerLifestylePaymentRuntimeDigest { get; }
    CharacterCareerLifestylePaymentResult CommitCareerLifestylePayment(OwnerScope owner,
        CharacterCareerLifestylePaymentCommand command, ICharacterSourceDataResolver sourceResolver,
        CancellationToken cancellationToken = default);
}
