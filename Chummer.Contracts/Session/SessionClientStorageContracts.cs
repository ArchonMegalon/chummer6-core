using Chummer.Contracts.Content;

namespace Chummer.Contracts.Session;

public static class SessionClientCacheAreas
{
    public const string CharacterCatalog = "character-catalog";
    public const string ProfileCatalog = "profile-catalog";
    public const string RulePackCatalog = "rulepack-catalog";
    public const string RuntimeState = "runtime-state";
    public const string RuntimeBundle = "runtime-bundle";
    public const string Ledger = "ledger";
    public const string Replica = "replica";
}

public static class SessionClientCacheKeys
{
    public const string Global = "global";
}

public static class SessionClientStorageBackends
{
    public const string IndexedDb = "indexeddb";
    public const string Opfs = "opfs";
    public const string CacheApi = "cache-api";
}

public sealed record CachedClientPayload<T>(
    string CacheArea,
    string CacheKey,
    T Payload,
    DateTimeOffset CachedAtUtc,
    string StorageBackend = SessionClientStorageBackends.IndexedDb);

public sealed record ClientStorageQuotaEstimate(
    long? UsageBytes,
    long? QuotaBytes,
    bool IndexedDbAvailable,
    bool OpfsAvailable,
    bool PersistenceSupported,
    bool IsPersistent,
    DateTimeOffset CapturedAtUtc);

public interface ISessionOfflineCacheService
{
    Task<CachedClientPayload<SessionCharacterCatalog>?> GetCharacterCatalogAsync(CancellationToken ct = default);

    Task<CachedClientPayload<SessionCharacterCatalog>> CacheCharacterCatalogAsync(SessionCharacterCatalog catalog, CancellationToken ct = default);

    Task<CachedClientPayload<SessionProfileCatalog>?> GetProfileCatalogAsync(CancellationToken ct = default);

    Task<CachedClientPayload<SessionProfileCatalog>> CacheProfileCatalogAsync(SessionProfileCatalog catalog, CancellationToken ct = default);

    Task<CachedClientPayload<RulePackCatalog>?> GetRulePackCatalogAsync(CancellationToken ct = default);

    Task<CachedClientPayload<RulePackCatalog>> CacheRulePackCatalogAsync(RulePackCatalog catalog, CancellationToken ct = default);

    Task<CachedClientPayload<SessionRuntimeStatusProjection>?> GetRuntimeStateAsync(string characterId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionRuntimeStatusProjection>> CacheRuntimeStateAsync(
        string characterId,
        SessionRuntimeStatusProjection runtimeState,
        CancellationToken ct = default);

    Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetRuntimeBundleAsync(string characterId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> CacheRuntimeBundleAsync(
        string characterId,
        SessionRuntimeBundleIssueReceipt receipt,
        CancellationToken ct = default);

    Task<CachedClientPayload<SessionLedger>?> GetLedgerAsync(string overlayId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionLedger>> CacheLedgerAsync(SessionLedger ledger, CancellationToken ct = default);

    Task RemoveLedgerAsync(string overlayId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionReplicaState>?> GetReplicaStateAsync(string overlayId, CancellationToken ct = default);

    Task<CachedClientPayload<SessionReplicaState>> CacheReplicaStateAsync(SessionReplicaState state, CancellationToken ct = default);

    Task RemoveReplicaStateAsync(string overlayId, CancellationToken ct = default);

    Task<ClientStorageQuotaEstimate> GetStorageQuotaAsync(CancellationToken ct = default);
}
