using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Closed quality operations under the caller's live owner lease.
/// The store observes the workspace before choosing retained or ordinary sources.
/// No current source resolver or arbitrary replacement document escapes.</summary>
public interface ICharacterCareerQualityCurrentCapability
{
    CharacterCareerQualityWorkspaceSnapshot? ReadCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterWorkspaceId workspace,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default);
    CharacterCareerQualityCommitResult RecoverCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityPlan original,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default);
    CharacterCareerQualityCommitResult CommitCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityPlan plan,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default);
    CharacterCareerQualityCorrectionResult CorrectCurrentCareerQuality(IOwnerContextAccessor owners,
        OwnerContextStamp expectedOwner, IOwnerContextLease ownerLease, CharacterCareerQualityCorrectionPlan correction,
        ICharacterCareerQualityAuthorityResolver authority, CancellationToken cancellationToken = default);
}
