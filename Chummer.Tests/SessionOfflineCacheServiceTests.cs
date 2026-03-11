#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Session;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;
using Chummer.Infrastructure.Browser.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class SessionOfflineCacheServiceTests
{
    [TestMethod]
    public async Task Browser_session_offline_cache_service_roundtrips_cached_catalogs_and_runtime_state()
    {
        BrowserSessionOfflineCacheService service = CreateService();
        SessionCharacterCatalog characterCatalog = new([
            new("char-1", "Apex Predator", RulesetDefaults.Sr5, "runtime-1")
        ]);
        SessionProfileCatalog profileCatalog = new([
            new("official.sr5.core", "Official SR5 Core", RulesetDefaults.Sr5, "runtime-1", "stable")
        ], "official.sr5.core");
        RulePackCatalog rulePackCatalog = new([
            new RulePackManifest(
                PackId: "official.sr5.core",
                Version: "1.0.0",
                Title: "Official SR5 Core",
                Author: "Chummer",
                Description: "Core runtime pack",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "1.0.0",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: ArtifactVisibilityModes.Private,
                TrustTier: ArtifactTrustTiers.Official,
                Assets: [],
                Capabilities: [],
                ExecutionPolicies: [])
        ]);
        SessionRuntimeStatusProjection runtimeState = new(
            CharacterId: "char-1",
            SelectionState: SessionRuntimeSelectionStates.Selected,
            ProfileId: "official.sr5.core",
            ProfileTitle: "Official SR5 Core",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-1",
            SessionReady: true,
            BundleFreshness: SessionRuntimeBundleFreshnessStates.Current,
            RequiresBundleRefresh: false);

        CachedClientPayload<SessionCharacterCatalog> cachedCharacters = await service.CacheCharacterCatalogAsync(characterCatalog);
        CachedClientPayload<SessionProfileCatalog> cachedProfiles = await service.CacheProfileCatalogAsync(profileCatalog);
        CachedClientPayload<RulePackCatalog> cachedRulePacks = await service.CacheRulePackCatalogAsync(rulePackCatalog);
        CachedClientPayload<SessionRuntimeStatusProjection> cachedRuntimeState = await service.CacheRuntimeStateAsync("char-1", runtimeState);

        Assert.AreEqual(SessionClientCacheAreas.CharacterCatalog, cachedCharacters.CacheArea);
        Assert.AreEqual(SessionClientCacheAreas.ProfileCatalog, cachedProfiles.CacheArea);
        Assert.AreEqual(SessionClientCacheAreas.RulePackCatalog, cachedRulePacks.CacheArea);
        Assert.AreEqual(SessionClientCacheAreas.RuntimeState, cachedRuntimeState.CacheArea);

        CachedClientPayload<SessionCharacterCatalog>? loadedCharacters = await service.GetCharacterCatalogAsync();
        CachedClientPayload<SessionProfileCatalog>? loadedProfiles = await service.GetProfileCatalogAsync();
        CachedClientPayload<RulePackCatalog>? loadedRulePacks = await service.GetRulePackCatalogAsync();
        CachedClientPayload<SessionRuntimeStatusProjection>? loadedRuntimeState = await service.GetRuntimeStateAsync("char-1");

        Assert.IsNotNull(loadedCharacters);
        Assert.IsNotNull(loadedProfiles);
        Assert.IsNotNull(loadedRulePacks);
        Assert.IsNotNull(loadedRuntimeState);
        Assert.AreEqual("Apex Predator", loadedCharacters.Payload.Characters[0].DisplayName);
        Assert.AreEqual("official.sr5.core", loadedProfiles.Payload.ActiveProfileId);
        Assert.AreEqual("official.sr5.core", loadedRulePacks.Payload.InstalledRulePacks[0].PackId);
        Assert.AreEqual(SessionRuntimeSelectionStates.Selected, loadedRuntimeState.Payload.SelectionState);
    }

    [TestMethod]
    public async Task Browser_session_offline_cache_service_roundtrips_bundle_receipts_and_quota_estimate()
    {
        ClientStorageQuotaEstimate estimate = new(
            UsageBytes: 1_024,
            QuotaBytes: 8_192,
            IndexedDbAvailable: true,
            OpfsAvailable: true,
            PersistenceSupported: true,
            IsPersistent: false,
            CapturedAtUtc: DateTimeOffset.UtcNow);
        BrowserSessionOfflineCacheService service = CreateService(quotaService: new StubClientStorageQuotaService(estimate));
        SessionRuntimeBundleIssueReceipt receipt = new(
            Outcome: SessionRuntimeBundleIssueOutcomes.Issued,
            Bundle: new SessionRuntimeBundle(
                BundleId: "bundle-1",
                BaseCharacterVersion: new("char-1", "ver-1", RulesetDefaults.Sr5, "runtime-1"),
                EngineApiVersion: "1.0.0",
                SignedAtUtc: DateTimeOffset.UtcNow,
                Signature: "sig-1",
                QuickActions: [],
                Trackers:
                [
                    new TrackerDefinition(
                        TrackerId: "stun",
                        Category: TrackerCategories.Condition,
                        Label: "Stun",
                        DefaultValue: 0,
                        MinimumValue: 0,
                        MaximumValue: 10,
                        Thresholds: [])
                ],
                ReducerBindings: new Dictionary<string, string> { ["session.quick-actions"] = "official.sr5.core:quick-actions" }),
            SignatureEnvelope: new SessionRuntimeBundleSignatureEnvelope(
                BundleId: "bundle-1",
                KeyId: "key-1",
                Signature: "sig-1",
                SignedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(7)),
            DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
            Diagnostics: [new SessionRuntimeBundleTrustDiagnostic(SessionRuntimeBundleTrustStates.Trusted, "Trusted bundle", "key-1", "runtime-1")]);

        CachedClientPayload<SessionRuntimeBundleIssueReceipt> cachedReceipt = await service.CacheRuntimeBundleAsync("char-1", receipt);
        CachedClientPayload<SessionRuntimeBundleIssueReceipt>? loadedReceipt = await service.GetRuntimeBundleAsync("char-1");
        ClientStorageQuotaEstimate loadedEstimate = await service.GetStorageQuotaAsync();

        Assert.AreEqual(SessionClientCacheAreas.RuntimeBundle, cachedReceipt.CacheArea);
        Assert.IsNotNull(loadedReceipt);
        Assert.AreEqual("bundle-1", loadedReceipt.Payload.Bundle.BundleId);
        Assert.AreEqual(SessionRuntimeBundleTrustStates.Trusted, loadedReceipt.Payload.Diagnostics[0].State);
        Assert.AreEqual(estimate.QuotaBytes, loadedEstimate.QuotaBytes);
        Assert.IsTrue(loadedEstimate.IndexedDbAvailable);
        Assert.IsTrue(loadedEstimate.OpfsAvailable);
    }

    [TestMethod]
    public async Task Browser_session_offline_cache_service_roundtrips_and_removes_ledger_and_replica_state()
    {
        BrowserSessionOfflineCacheService service = CreateService();
        CharacterVersionReference baseCharacterVersion = new("char-1", "ver-1", RulesetDefaults.Sr5, "runtime-1");
        SessionLedger ledger = new(
            OverlayId: "offline:char-1",
            BaseCharacterVersion: baseCharacterVersion,
            Events:
            [
                new SessionEventEnvelope(
                    EventId: "event-1",
                    OverlayId: "offline:char-1",
                    BaseCharacterVersion: baseCharacterVersion,
                    DeviceId: "device-1",
                    ActorId: "actor-1",
                    Sequence: 1,
                    EventType: SessionEventTypes.TrackerIncrement,
                    Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
                    {
                        ["trackerId"] = RulesetCapabilityBridge.FromObject("stun"),
                        ["delta"] = RulesetCapabilityBridge.FromObject(1)
                    },
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            NextSequence: 2);
        SessionReplicaState replicaState = new(
            OverlayId: "offline:char-1",
            BaseCharacterVersion: baseCharacterVersion,
            RuntimeFingerprint: "runtime-1",
            ReplicaId: "browser-local",
            ClockSummary:
            [
                new SessionReplicaClock("browser-local", 2, DateTimeOffset.UtcNow)
            ],
            Values:
            [
                new SessionReplicaValue("tracker:stun", SessionReplicaValueKinds.PnCounter, "{\"value\":1}")
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            PendingOperationCount: 1);

        CachedClientPayload<SessionLedger> cachedLedger = await service.CacheLedgerAsync(ledger);
        CachedClientPayload<SessionReplicaState> cachedReplica = await service.CacheReplicaStateAsync(replicaState);

        Assert.AreEqual(SessionClientCacheAreas.Ledger, cachedLedger.CacheArea);
        Assert.AreEqual(SessionClientCacheAreas.Replica, cachedReplica.CacheArea);

        CachedClientPayload<SessionLedger>? loadedLedger = await service.GetLedgerAsync("offline:char-1");
        CachedClientPayload<SessionReplicaState>? loadedReplica = await service.GetReplicaStateAsync("offline:char-1");

        Assert.IsNotNull(loadedLedger);
        Assert.IsNotNull(loadedReplica);
        Assert.HasCount(1, loadedLedger.Payload.Events);
        Assert.AreEqual(1, loadedReplica.Payload.PendingOperationCount);

        await service.RemoveLedgerAsync("offline:char-1");
        await service.RemoveReplicaStateAsync("offline:char-1");

        Assert.IsNull(await service.GetLedgerAsync("offline:char-1"));
        Assert.IsNull(await service.GetReplicaStateAsync("offline:char-1"));
    }

    private static BrowserSessionOfflineCacheService CreateService(
        IBrowseCacheStore? browseCacheStore = null,
        ISessionRuntimeBundleCacheStore? runtimeBundleCacheStore = null,
        ISessionLedgerStore? ledgerStore = null,
        ISessionReplicaStore? replicaStore = null,
        IClientStorageQuotaService? quotaService = null)
        => new(
            browseCacheStore ?? new InMemoryBrowseCacheStore(),
            runtimeBundleCacheStore ?? new InMemorySessionRuntimeBundleCacheStore(),
            ledgerStore ?? new InMemorySessionLedgerStore(),
            replicaStore ?? new InMemorySessionReplicaStore(),
            quotaService ?? new StubClientStorageQuotaService(
                new ClientStorageQuotaEstimate(
                    UsageBytes: 512,
                    QuotaBytes: 4_096,
                    IndexedDbAvailable: true,
                    OpfsAvailable: false,
                    PersistenceSupported: true,
                    IsPersistent: false,
                    CapturedAtUtc: DateTimeOffset.UtcNow)));

    private sealed class InMemoryBrowseCacheStore : IBrowseCacheStore
    {
        private readonly Dictionary<string, object> _records = new(StringComparer.Ordinal);

        public Task<CachedClientPayload<T>?> GetAsync<T>(string cacheArea, string cacheKey, CancellationToken ct = default)
        {
            _records.TryGetValue(BuildKey(cacheArea, cacheKey), out object? record);
            return Task.FromResult(record as CachedClientPayload<T>);
        }

        public Task<CachedClientPayload<T>> UpsertAsync<T>(string cacheArea, string cacheKey, T payload, CancellationToken ct = default)
        {
            CachedClientPayload<T> record = new(cacheArea, cacheKey, payload, DateTimeOffset.UtcNow);
            _records[BuildKey(cacheArea, cacheKey)] = record;
            return Task.FromResult(record);
        }

        public Task RemoveAsync(string cacheArea, string cacheKey, CancellationToken ct = default)
        {
            _records.Remove(BuildKey(cacheArea, cacheKey));
            return Task.CompletedTask;
        }

        private static string BuildKey(string cacheArea, string cacheKey)
            => $"{cacheArea}::{cacheKey}";
    }

    private sealed class InMemorySessionRuntimeBundleCacheStore : ISessionRuntimeBundleCacheStore
    {
        private readonly Dictionary<string, CachedClientPayload<SessionRuntimeBundleIssueReceipt>> _records = new(StringComparer.Ordinal);

        public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>?> GetAsync(string characterId, CancellationToken ct = default)
        {
            _records.TryGetValue(characterId, out CachedClientPayload<SessionRuntimeBundleIssueReceipt>? record);
            return Task.FromResult(record);
        }

        public Task<CachedClientPayload<SessionRuntimeBundleIssueReceipt>> UpsertAsync(
            string characterId,
            SessionRuntimeBundleIssueReceipt receipt,
            CancellationToken ct = default)
        {
            CachedClientPayload<SessionRuntimeBundleIssueReceipt> record = new(
                SessionClientCacheAreas.RuntimeBundle,
                characterId,
                receipt,
                DateTimeOffset.UtcNow);
            _records[characterId] = record;
            return Task.FromResult(record);
        }

        public Task RemoveAsync(string characterId, CancellationToken ct = default)
        {
            _records.Remove(characterId);
            return Task.CompletedTask;
        }
    }

    private sealed class StubClientStorageQuotaService : IClientStorageQuotaService
    {
        private readonly ClientStorageQuotaEstimate _estimate;

        public StubClientStorageQuotaService(ClientStorageQuotaEstimate estimate)
        {
            _estimate = estimate;
        }

        public Task<ClientStorageQuotaEstimate> GetEstimateAsync(CancellationToken ct = default)
            => Task.FromResult(_estimate);
    }

    private sealed class InMemorySessionLedgerStore : ISessionLedgerStore
    {
        private readonly Dictionary<string, CachedClientPayload<SessionLedger>> _records = new(StringComparer.Ordinal);

        public Task<CachedClientPayload<SessionLedger>?> GetAsync(string overlayId, CancellationToken ct = default)
        {
            _records.TryGetValue(overlayId, out CachedClientPayload<SessionLedger>? record);
            return Task.FromResult(record);
        }

        public Task<CachedClientPayload<SessionLedger>> UpsertAsync(SessionLedger ledger, CancellationToken ct = default)
        {
            CachedClientPayload<SessionLedger> record = new(
                SessionClientCacheAreas.Ledger,
                ledger.OverlayId,
                ledger,
                DateTimeOffset.UtcNow);
            _records[ledger.OverlayId] = record;
            return Task.FromResult(record);
        }

        public Task RemoveAsync(string overlayId, CancellationToken ct = default)
        {
            _records.Remove(overlayId);
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySessionReplicaStore : ISessionReplicaStore
    {
        private readonly Dictionary<string, CachedClientPayload<SessionReplicaState>> _records = new(StringComparer.Ordinal);

        public Task<CachedClientPayload<SessionReplicaState>?> GetAsync(string overlayId, CancellationToken ct = default)
        {
            _records.TryGetValue(overlayId, out CachedClientPayload<SessionReplicaState>? record);
            return Task.FromResult(record);
        }

        public Task<CachedClientPayload<SessionReplicaState>> UpsertAsync(SessionReplicaState state, CancellationToken ct = default)
        {
            CachedClientPayload<SessionReplicaState> record = new(
                SessionClientCacheAreas.Replica,
                state.OverlayId,
                state,
                DateTimeOffset.UtcNow);
            _records[state.OverlayId] = record;
            return Task.FromResult(record);
        }

        public Task RemoveAsync(string overlayId, CancellationToken ct = default)
        {
            _records.Remove(overlayId);
            return Task.CompletedTask;
        }
    }
}
