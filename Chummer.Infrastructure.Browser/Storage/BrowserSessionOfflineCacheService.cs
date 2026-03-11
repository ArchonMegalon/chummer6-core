using Chummer.Application.Session;
using Chummer.Contracts.Content;
using Chummer.Contracts.Session;

namespace Chummer.Infrastructure.Browser.Storage;

public sealed class BrowserSessionOfflineCacheService : ISessionOfflineCacheService
{
    private readonly IBrowseCacheStore _browseCacheStore;
    private readonly ISessionRuntimeBundleCacheStore _runtimeBundleCacheStore;
    private readonly ISessionLedgerStore _ledgerStore;
    private readonly ISessionReplicaStore _replicaStore;
    private readonly IClientStorageQuotaService _quotaService;

    public BrowserSessionOfflineCacheService(
        IBrowseCacheStore browseCacheStore,
        ISessionRuntimeBundleCacheStore runtimeBundleCacheStore,
        ISessionLedgerStore ledgerStore,
        ISessionReplicaStore replicaStore,
        IClientStorageQuotaService quotaService)
    {
        _browseCacheStore = browseCacheStore;
        _runtimeBundleCacheStore = runtimeBundleCacheStore;
        _ledgerStore = ledgerStore;
        _replicaStore = replicaStore;
        _quotaService = quotaService;
    }

    public Task<CachedClientPayload<SessionCharacterCatalog>?> GetCharacterCatalogAsync(CancellationToken ct = default)
        => _browseCacheStore.GetAsync<SessionCharacterCatalog>(SessionClientCacheAreas.CharacterCatalog, SessionClientCacheKeys.Global, ct);

    public Task<CachedClientPayload<SessionCharacterCatalog>> CacheCharacterCatalogAsync(SessionCharacterCatalog catalog, CancellationToken ct = default)
        => _browseCacheStore.UpsertAsync(SessionClientCacheAreas.CharacterCatalog, SessionClientCacheKeys.Global, catalog, ct);

    public Task<CachedClientPayload<SessionProfileCatalog>?> GetProfileCatalogAsync(CancellationToken ct = default)
        => _browseCacheStore.GetAsync<SessionProfileCatalog>(SessionClientCacheAreas.ProfileCatalog, SessionClientCacheKeys.Global, ct);

    public Task<CachedClientPayload<SessionProfileCatalog>> CacheProfileCatalogAsync(SessionProfileCatalog catalog, CancellationToken ct = default)
        => _browseCacheStore.UpsertAsync(SessionClientCacheAreas.ProfileCatalog, SessionClientCacheKeys.Global, catalog, ct);

    public Task<CachedClientPayload<RulePackCatalog>?> GetRulePackCatalogAsync(CancellationToken ct = default)
        => _browseCacheStore.GetAsync<RulePackCatalog>(SessionClientCacheAreas.RulePackCatalog, SessionClientCacheKeys.Global, ct);

    public Task<CachedClientPayload<RulePackCatalog>> CacheRulePackCatalogAsync(RulePackCatalog catalog, CancellationToken ct = default)
        => _browseCacheStore.UpsertAsync(SessionClientCacheAreas.RulePackCatalog, SessionClientCacheKeys.Global, catalog, ct);

    public Task<CachedClientPayload<SessionRuntimeStatusProjection>?> GetRuntimeStateAsync(string characterId, CancellationToken ct = default)
        => _browseCacheStore.GetAsync<SessionRuntimeStatusProjection>(SessionClientCacheAreas.RuntimeState, NormalizeCharacterId(characterId), ct);

    public Task<CachedClientPayload<SessionRuntimeStatusProjection>> CacheRuntimeStateAsync(
        string characterId,
        SessionRuntimeStatusProjection runtimeState,
        CancellationToken ct = default)
        => _browseCacheStore.UpsertAsync(SessionClientCacheAreas.RuntimeState, NormalizeCharacterId(characterId), runtimeState, ct);

    public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetRuntimeBundleAsync(string characterId, CancellationToken ct = default)
        => _runtimeBundleCacheStore.GetAsync(NormalizeCharacterId(characterId), ct);

    public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> CacheRuntimeBundleAsync(
        string characterId,
        SessionRuntimeBundleIssueReceipt receipt,
        CancellationToken ct = default)
        => _runtimeBundleCacheStore.UpsertAsync(NormalizeCharacterId(characterId), receipt, ct);

    public Task<CachedClientPayload<SessionLedger>?> GetLedgerAsync(string overlayId, CancellationToken ct = default)
        => _ledgerStore.GetAsync(NormalizeOverlayId(overlayId), ct);

    public Task<CachedClientPayload<SessionLedger>> CacheLedgerAsync(SessionLedger ledger, CancellationToken ct = default)
        => _ledgerStore.UpsertAsync(ledger with { OverlayId = NormalizeOverlayId(ledger.OverlayId) }, ct);

    public Task RemoveLedgerAsync(string overlayId, CancellationToken ct = default)
        => _ledgerStore.RemoveAsync(NormalizeOverlayId(overlayId), ct);

    public Task<CachedClientPayload<SessionReplicaState>?> GetReplicaStateAsync(string overlayId, CancellationToken ct = default)
        => _replicaStore.GetAsync(NormalizeOverlayId(overlayId), ct);

    public Task<CachedClientPayload<SessionReplicaState>> CacheReplicaStateAsync(SessionReplicaState state, CancellationToken ct = default)
        => _replicaStore.UpsertAsync(state with { OverlayId = NormalizeOverlayId(state.OverlayId) }, ct);

    public Task RemoveReplicaStateAsync(string overlayId, CancellationToken ct = default)
        => _replicaStore.RemoveAsync(NormalizeOverlayId(overlayId), ct);

    public Task<ClientStorageQuotaEstimate> GetStorageQuotaAsync(CancellationToken ct = default)
        => _quotaService.GetEstimateAsync(ct);

    private static string NormalizeCharacterId(string characterId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(characterId);
        return characterId.Trim();
    }

    private static string NormalizeOverlayId(string overlayId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(overlayId);
        return overlayId.Trim();
    }
}
