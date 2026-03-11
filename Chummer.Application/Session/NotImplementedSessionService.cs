using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed class NotImplementedSessionService : ISessionService
{
    public SessionApiResult<SessionCharacterCatalog> ListCharacters(OwnerScope owner)
        => NotImplemented<SessionCharacterCatalog>(owner, SessionApiOperations.ListCharacters);

    public SessionApiResult<SessionDashboardProjection> GetCharacterProjection(OwnerScope owner, string characterId)
        => NotImplemented<SessionDashboardProjection>(owner, SessionApiOperations.GetCharacterProjection, characterId);

    public SessionApiResult<SessionOverlaySnapshot> ApplyCharacterPatches(OwnerScope owner, string characterId, SessionPatchRequest? request)
        => NotImplemented<SessionOverlaySnapshot>(owner, SessionApiOperations.ApplyCharacterPatches, characterId);

    public SessionApiResult<SessionSyncReceipt> SyncCharacterLedger(OwnerScope owner, string characterId, SessionSyncBatch? batch)
        => NotImplemented<SessionSyncReceipt>(owner, SessionApiOperations.SyncCharacterLedger, characterId);

    public SessionApiResult<SessionProfileCatalog> ListProfiles(OwnerScope owner)
        => NotImplemented<SessionProfileCatalog>(owner, SessionApiOperations.ListProfiles);

    public SessionApiResult<SessionRuntimeStatusProjection> GetRuntimeState(OwnerScope owner, string characterId)
        => NotImplemented<SessionRuntimeStatusProjection>(owner, SessionApiOperations.GetRuntimeState, characterId);

    public SessionApiResult<SessionRuntimeBundleIssueReceipt> GetRuntimeBundle(OwnerScope owner, string characterId)
        => NotImplemented<SessionRuntimeBundleIssueReceipt>(owner, SessionApiOperations.GetRuntimeBundle, characterId);

    public SessionApiResult<SessionRuntimeBundleRefreshReceipt> RefreshRuntimeBundle(OwnerScope owner, string characterId)
        => NotImplemented<SessionRuntimeBundleRefreshReceipt>(owner, SessionApiOperations.RefreshRuntimeBundle, characterId);

    public SessionApiResult<SessionProfileSelectionReceipt> SelectProfile(OwnerScope owner, string characterId, SessionProfileSelectionRequest? request)
        => NotImplemented<SessionProfileSelectionReceipt>(owner, SessionApiOperations.SelectProfile, characterId);

    public SessionApiResult<RulePackCatalog> ListRulePacks(OwnerScope owner)
        => NotImplemented<RulePackCatalog>(owner, SessionApiOperations.ListRulePacks);

    public SessionApiResult<SessionOverlaySnapshot> UpdatePins(OwnerScope owner, SessionPinUpdateRequest? request)
        => NotImplemented<SessionOverlaySnapshot>(owner, SessionApiOperations.UpdatePins, request?.BaseCharacterVersion.CharacterId);

    private static SessionApiResult<T> NotImplemented<T>(OwnerScope owner, string operation, string? characterId = null)
        => SessionApiResult<T>.FromNotImplemented(
            new SessionNotImplementedReceipt(
                Error: "session_not_implemented",
                Operation: operation,
                Message: "The dedicated session/mobile surface is not implemented yet.",
                CharacterId: string.IsNullOrWhiteSpace(characterId) ? null : characterId,
                OwnerId: owner.NormalizedValue));
}
