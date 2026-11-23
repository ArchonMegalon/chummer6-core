using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Application.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionService
{
    SessionApiResult<SessionCharacterCatalog> ListCharacters(OwnerScope owner);

    SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId);

    SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request);

    SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch);

    SessionApiResult<SessionDashboardProjection> GetCharacterProjectionWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId)
        => GetCharacterProjection(owner.Owner, characterId);

    SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatchesWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId, SessionPatchRequest request)
        => ApplyCharacterPatches(owner.Owner, characterId, request);

    SessionApiResult<SessionPlaySustainingReview> PreviewCharacterSustainingWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId, SessionPatchRequest request)
        => PreviewCharacterSustaining(owner.Owner, characterId, request);

    SessionApiResult<SessionPlaySustainingReview> PreviewCharacterSustaining(OwnerScope owner, string characterId, SessionPatchRequest request)
        => SessionApiResult<SessionPlaySustainingReview>.Failed(SessionPlayFailures.Unavailable("sustaining-preview-unavailable"));
    SessionApiResult<SessionPlayCastingReview> PreviewCharacterCastingWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId, SessionPatchRequest request)
        => PreviewCharacterCasting(owner.Owner, characterId, request);

    SessionApiResult<SessionPlayCastingReview> PreviewCharacterCasting(OwnerScope owner, string characterId, SessionPatchRequest request)
        => SessionApiResult<SessionPlayCastingReview>.Failed(SessionPlayFailures.Unavailable("casting-preview-unavailable"));
    SessionApiResult<SessionPlayElementalReview> PreviewCharacterElementalFollowupWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId, SessionPatchRequest request)
        => PreviewCharacterElementalFollowup(owner.Owner, characterId, request);

    SessionApiResult<SessionPlayElementalReview> PreviewCharacterElementalFollowup(OwnerScope owner, string characterId, SessionPatchRequest request)
        => SessionApiResult<SessionPlayElementalReview>.Failed(SessionPlayFailures.Unavailable("elemental-preview-unavailable"));
    SessionApiResult<SessionPlayOperationLookup> InspectCharacterOperation(OwnerScope owner, string characterId, SessionPatchRequest request)
        => SessionApiResult<SessionPlayOperationLookup>.Failed(SessionPlayFailures.Unavailable("read-only-inspection-unavailable"));

    SessionApiResult<SessionPlayOperationResult> RecoverCharacterOperationWithOwnerLease(IOwnerContextLeaseAccessor owners, OwnerContextStamp owner, IOwnerContextLease lease, string characterId, string operationId)
        => RecoverCharacterOperation(owner.Owner, characterId, operationId);

    SessionApiResult<SessionPlayOperationResult> RecoverCharacterOperation(OwnerScope owner, string characterId, string operationId)
        => SessionApiResult<SessionPlayOperationResult>.Failed(SessionPlayFailures.Unavailable("operation-recovery-unavailable"));

    SessionApiResult<SessionProfileCatalog> ListProfiles(OwnerScope owner);

    SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState(OwnerScope owner, string characterId);

    SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle(OwnerScope owner, string characterId);

    SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle(OwnerScope owner, string characterId);

    SessionApiResult<SessionProfileSelectionReceipt> SelectProfile(OwnerScope owner, string characterId, SessionProfileSelectionRequest? request);

    SessionApiResult<RulePackCatalog> ListRulePacks(OwnerScope owner);

    SessionApiResult<SessionOverlaySnapshot> UpdatePins(OwnerScope owner, SessionPinUpdateRequest? request);
}
