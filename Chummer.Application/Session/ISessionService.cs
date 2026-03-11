using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public interface ISessionService
{
    SessionApiResult<SessionCharacterCatalog> ListCharacters(OwnerScope owner);

    SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId);

    SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request);

    SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch);

    SessionApiResult<SessionProfileCatalog> ListProfiles(OwnerScope owner);

    SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState(OwnerScope owner, string characterId);

    SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle(OwnerScope owner, string characterId);

    SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle(OwnerScope owner, string characterId);

    SessionApiResult<SessionProfileSelectionReceipt> SelectProfile(OwnerScope owner, string characterId, SessionProfileSelectionRequest? request);

    SessionApiResult<RulePackCatalog> ListRulePacks(OwnerScope owner);

    SessionApiResult<SessionOverlaySnapshot> UpdatePins(OwnerScope owner, SessionPinUpdateRequest? request);
}
