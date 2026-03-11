using Chummer.Contracts.Characters;

namespace Chummer.Contracts.Session;

public static class SessionApiOperations
{
    public const string ListCharacters = "list-characters";
    public const string GetCharacterProjection = "get-character-projection";
    public const string ApplyCharacterPatches = "apply-character-patches";
    public const string SyncCharacterLedger = "sync-character-ledger";
    public const string ListProfiles = "list-profiles";
    public const string GetRuntimeState = "get-runtime-state";
    public const string GetRuntimeBundle = "get-runtime-bundle";
    public const string RefreshRuntimeBundle = "refresh-runtime-bundle";
    public const string SelectProfile = "select-profile";
    public const string ListRulePacks = "list-rulepacks";
    public const string UpdatePins = "update-pins";
}

public static class SessionProfileSelectionOutcomes
{
    public const string Selected = "selected";
    public const string Deferred = "deferred";
    public const string Blocked = "blocked";
}

public sealed record SessionCharacterListItem(
    string CharacterId,
    string DisplayName,
    string RulesetId,
    string RuntimeFingerprint);

public sealed record SessionCharacterCatalog(
    IReadOnlyList<SessionCharacterListItem> Characters);

public sealed record SessionProfileListItem(
    string ProfileId,
    string Title,
    string RulesetId,
    string RuntimeFingerprint,
    string UpdateChannel,
    bool SessionReady = true,
    string? Audience = null);

public sealed record SessionProfileCatalog(
    IReadOnlyList<SessionProfileListItem> Profiles,
    string? ActiveProfileId = null);

public sealed record SessionPatchRequest(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionEventEnvelope> Events);

public sealed record SessionPinUpdateRequest(
    string OverlayId,
    CharacterVersionReference BaseCharacterVersion,
    IReadOnlyList<SessionQuickActionPin> Pins);

public sealed record SessionProfileSelectionRequest(
    string ProfileId);

public sealed record SessionProfileSelectionReceipt(
    string CharacterId,
    string ProfileId,
    string RuntimeFingerprint,
    string Outcome,
    bool RequiresBundleRefresh = false,
    string? DeferredReason = null);

public sealed record SessionNotImplementedReceipt(
    string Error,
    string Operation,
    string Message,
    string? CharacterId = null,
    string? OwnerId = null);

public sealed record SessionApiResult<T>(
    T? Payload = default,
    SessionNotImplementedReceipt? NotImplemented = null)
{
    public bool IsImplemented => NotImplemented is null;

    public static SessionApiResult<T> Implemented(T payload)
        => new(payload, null);

    public static SessionApiResult<T> FromNotImplemented(SessionNotImplementedReceipt receipt)
        => new(default, receipt);
}
