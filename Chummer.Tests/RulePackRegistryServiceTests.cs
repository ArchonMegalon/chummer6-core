#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RulePackRegistryServiceTests
{
    [TestMethod]
    public void Overlay_registry_service_projects_overlays_into_rulepack_registry_entries()
    {
        ContentOverlayCatalog catalog = new(
            BaseDataPath: "/app/data",
            BaseLanguagePath: "/app/lang",
            Overlays:
            [
                new ContentOverlayPack(
                    Id: "house-rules",
                    Name: "House Rules",
                    RootPath: "/packs/house-rules",
                    DataPath: "/packs/house-rules/data",
                    LanguagePath: "/packs/house-rules/lang",
                    Priority: 50,
                    Enabled: true,
                    Mode: ContentOverlayModes.MergeCatalog,
                    Description: "Campaign overlay.")
            ]);
        OverlayRulePackRegistryService service = new(
            new RulePackManifestStoreStub(),
            new ContentOverlayCatalogServiceStub(catalog),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(),
            new RulePackInstallStateStoreStub());

        IReadOnlyList<RulePackRegistryEntry> entries = service.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.HasCount(1, entries);
        Assert.AreEqual("house-rules", entries[0].Manifest.PackId);
        Assert.AreEqual(RulesetDefaults.Sr5, entries[0].Manifest.Targets[0]);
        Assert.AreEqual(RulePackPublicationStatuses.Published, entries[0].Publication.PublicationStatus);
        Assert.AreEqual(RulePackReviewStates.NotRequired, entries[0].Publication.Review.State);
        Assert.AreEqual(ArtifactInstallStates.Installed, entries[0].Install.State);
        Assert.AreEqual(RegistryEntrySourceKinds.OverlayCatalogBridge, entries[0].SourceKind);
    }

    [TestMethod]
    public void Overlay_registry_service_prefers_owner_backed_publication_metadata_when_present()
    {
        ContentOverlayCatalog catalog = new(
            BaseDataPath: "/app/data",
            BaseLanguagePath: "/app/lang",
            Overlays:
            [
                new ContentOverlayPack(
                    Id: "house-rules",
                    Name: "House Rules",
                    RootPath: "/packs/house-rules",
                    DataPath: "/packs/house-rules/data",
                    LanguagePath: "/packs/house-rules/lang",
                    Priority: 50,
                    Enabled: true,
                    Mode: ContentOverlayModes.MergeCatalog,
                    Description: "Campaign overlay.")
            ]);
        OverlayRulePackRegistryService service = new(
            new RulePackManifestStoreStub(),
            new ContentOverlayCatalogServiceStub(catalog),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(
            [
                new RulePackPublicationRecord(
                    PackId: "house-rules",
                    Version: "overlay-v1",
                    RulesetId: RulesetDefaults.Sr5,
                    Publication: new RulePackPublicationMetadata(
                        OwnerId: "alice",
                        Visibility: ArtifactVisibilityModes.Private,
                        PublicationStatus: RulePackPublicationStatuses.Draft,
                        Review: new RulePackReviewDecision(RulePackReviewStates.PendingReview),
                        Shares:
                        [
                            new RulePackShareGrant(
                                SubjectKind: RulePackShareSubjectKinds.User,
                                SubjectId: "bob",
                                AccessLevel: RulePackShareAccessLevels.View)
                        ]))
            ]),
            new RulePackInstallStateStoreStub(
            [
                new RulePackInstallRecord(
                    PackId: "house-rules",
                    Version: "overlay-v1",
                    RulesetId: RulesetDefaults.Sr5,
                    Install: new ArtifactInstallState(
                        State: ArtifactInstallStates.Pinned,
                        InstalledAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:runtime"))
            ]));

        RulePackRegistryEntry entry = service.List(new OwnerScope("alice"), RulesetDefaults.Sr5).Single();

        Assert.AreEqual("alice", entry.Publication.OwnerId);
        Assert.AreEqual(ArtifactVisibilityModes.Private, entry.Publication.Visibility);
        Assert.AreEqual(RulePackPublicationStatuses.Draft, entry.Publication.PublicationStatus);
        Assert.AreEqual(RulePackReviewStates.PendingReview, entry.Publication.Review.State);
        Assert.HasCount(1, entry.Publication.Shares);
        Assert.AreEqual(ArtifactInstallStates.Pinned, entry.Install.State);
        Assert.AreEqual("workspace-1", entry.Install.InstalledTargetId);
        Assert.AreEqual(RegistryEntrySourceKinds.OverlayCatalogBridge, entry.SourceKind);
    }

    [TestMethod]
    public void Overlay_registry_service_returns_null_for_unknown_pack()
    {
        OverlayRulePackRegistryService service = new(
            new RulePackManifestStoreStub(),
            new ContentOverlayCatalogServiceStub(new ContentOverlayCatalog("/app/data", "/app/lang", [])),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(),
            new RulePackInstallStateStoreStub());

        RulePackRegistryEntry? entry = service.Get(OwnerScope.LocalSingleUser, "missing-pack", RulesetDefaults.Sr5);

        Assert.IsNull(entry);
    }

    [TestMethod]
    public void Overlay_registry_service_merges_owner_backed_manifests_and_prefers_persisted_entries_on_key_collisions()
    {
        ContentOverlayCatalog catalog = new(
            BaseDataPath: "/app/data",
            BaseLanguagePath: "/app/lang",
            Overlays:
            [
                new ContentOverlayPack(
                    Id: "house-rules",
                    Name: "House Rules",
                    RootPath: "/packs/house-rules",
                    DataPath: "/packs/house-rules/data",
                    LanguagePath: "/packs/house-rules/lang",
                    Priority: 50,
                    Enabled: true,
                    Mode: ContentOverlayModes.MergeCatalog,
                    Description: "Campaign overlay.")
            ]);
        OverlayRulePackRegistryService service = new(
            new RulePackManifestStoreStub(
            [
                new RulePackManifestRecord(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "overlay-v1",
                        Title: "Persisted House Rules",
                        Author: "alice",
                        Description: "Owner-backed registry copy.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.Private,
                        TrustTier: ArtifactTrustTiers.Private,
                        Assets: [],
                        Capabilities: [],
                        ExecutionPolicies: [])),
                new RulePackManifestRecord(
                    new RulePackManifest(
                        PackId: "gm-tools",
                        Version: "1.0.0",
                        Title: "GM Tools",
                        Author: "alice",
                        Description: "Extra owner-backed pack.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets: [],
                        Capabilities: [],
                        ExecutionPolicies: []))
            ]),
            new ContentOverlayCatalogServiceStub(catalog),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(),
            new RulePackInstallStateStoreStub());

        IReadOnlyList<RulePackRegistryEntry> entries = service.List(new OwnerScope("alice"), RulesetDefaults.Sr5);

        Assert.HasCount(2, entries);
        RulePackRegistryEntry persistedCollision = entries.Single(entry => string.Equals(entry.Manifest.PackId, "house-rules", StringComparison.Ordinal));
        RulePackRegistryEntry persistedOnly = entries.Single(entry => string.Equals(entry.Manifest.PackId, "gm-tools", StringComparison.Ordinal));
        Assert.AreEqual("Persisted House Rules", persistedCollision.Manifest.Title);
        Assert.AreEqual("alice", persistedCollision.Publication.OwnerId);
        Assert.AreEqual(RulePackPublicationStatuses.Draft, persistedCollision.Publication.PublicationStatus);
        Assert.AreEqual(ArtifactInstallStates.Available, persistedCollision.Install.State);
        Assert.AreEqual(RegistryEntrySourceKinds.PersistedManifest, persistedCollision.SourceKind);
        Assert.AreEqual("GM Tools", persistedOnly.Manifest.Title);
        Assert.AreEqual(ArtifactVisibilityModes.LocalOnly, persistedOnly.Publication.Visibility);
        Assert.AreEqual(RegistryEntrySourceKinds.PersistedManifest, persistedOnly.SourceKind);
    }

    [TestMethod]
    public void Overlay_registry_service_returns_entries_in_deterministic_pack_order()
    {
        ContentOverlayCatalog overlaysA = new(
            BaseDataPath: "/app/data",
            BaseLanguagePath: "/app/lang",
            Overlays:
            [
                new ContentOverlayPack("zeta-pack", "Zeta Pack", "/packs/zeta", "/packs/zeta/data", "/packs/zeta/lang", 10, true, ContentOverlayModes.MergeCatalog, "Zeta overlay."),
                new ContentOverlayPack("alpha-pack", "Alpha Pack", "/packs/alpha", "/packs/alpha/data", "/packs/alpha/lang", 90, true, ContentOverlayModes.MergeCatalog, "Alpha overlay.")
            ]);
        ContentOverlayCatalog overlaysB = new(
            BaseDataPath: "/app/data",
            BaseLanguagePath: "/app/lang",
            Overlays:
            [
                new ContentOverlayPack("alpha-pack", "Alpha Pack", "/packs/alpha", "/packs/alpha/data", "/packs/alpha/lang", 90, true, ContentOverlayModes.MergeCatalog, "Alpha overlay."),
                new ContentOverlayPack("zeta-pack", "Zeta Pack", "/packs/zeta", "/packs/zeta/data", "/packs/zeta/lang", 10, true, ContentOverlayModes.MergeCatalog, "Zeta overlay.")
            ]);

        OverlayRulePackRegistryService serviceA = new(
            new RulePackManifestStoreStub(),
            new ContentOverlayCatalogServiceStub(overlaysA),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(),
            new RulePackInstallStateStoreStub());
        OverlayRulePackRegistryService serviceB = new(
            new RulePackManifestStoreStub(),
            new ContentOverlayCatalogServiceStub(overlaysB),
            new RulesetSelectionPolicyStub(),
            new RulePackPublicationStoreStub(),
            new RulePackInstallStateStoreStub());

        CollectionAssert.AreEqual(
            serviceA.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5).Select(static entry => entry.Manifest.PackId).ToArray(),
            serviceB.List(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5).Select(static entry => entry.Manifest.PackId).ToArray());
    }

    private sealed class ContentOverlayCatalogServiceStub : IContentOverlayCatalogService
    {
        private readonly ContentOverlayCatalog _catalog;

        public ContentOverlayCatalogServiceStub(ContentOverlayCatalog catalog)
        {
            _catalog = catalog;
        }

        public ContentOverlayCatalog GetCatalog() => _catalog;

        public IReadOnlyList<string> GetDataDirectories() => [_catalog.BaseDataPath];

        public IReadOnlyList<string> GetLanguageDirectories() => [_catalog.BaseLanguagePath];

        public string ResolveDataFile(string fileName) => Path.Combine(_catalog.BaseDataPath, fileName);
    }

    private sealed class RulesetSelectionPolicyStub : IRulesetSelectionPolicy
    {
        public string GetDefaultRulesetId() => RulesetDefaults.Sr5;
    }

    private sealed class RulePackManifestStoreStub : IRulePackManifestStore
    {
        private readonly IReadOnlyList<RulePackManifestRecord> _records;

        public RulePackManifestStoreStub(IReadOnlyList<RulePackManifestRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RulePackManifestRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => record.Manifest.Targets.Contains(normalizedRulesetId, StringComparer.Ordinal)).ToArray();
        }

        public RulePackManifestRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
        {
            return List(owner, rulesetId).FirstOrDefault(
                record => string.Equals(record.Manifest.PackId, packId, StringComparison.Ordinal)
                    && string.Equals(record.Manifest.Version, version, StringComparison.Ordinal));
        }

        public RulePackManifestRecord Upsert(OwnerScope owner, RulePackManifestRecord record)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RulePackPublicationStoreStub : IRulePackPublicationStore
    {
        private readonly IReadOnlyList<RulePackPublicationRecord> _records;

        public RulePackPublicationStoreStub(IReadOnlyList<RulePackPublicationRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RulePackPublicationRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RulePackPublicationRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
        {
            return _records.FirstOrDefault(
                record => string.Equals(record.PackId, packId, StringComparison.Ordinal)
                    && string.Equals(record.Version, version, StringComparison.Ordinal)
                    && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RulePackPublicationRecord Upsert(OwnerScope owner, RulePackPublicationRecord record)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RulePackInstallStateStoreStub : IRulePackInstallStateStore
    {
        private readonly IReadOnlyList<RulePackInstallRecord> _records;

        public RulePackInstallStateStoreStub(IReadOnlyList<RulePackInstallRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RulePackInstallRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RulePackInstallRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
        {
            return _records.FirstOrDefault(
                record => string.Equals(record.PackId, packId, StringComparison.Ordinal)
                    && string.Equals(record.Version, version, StringComparison.Ordinal)
                    && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RulePackInstallRecord Upsert(OwnerScope owner, RulePackInstallRecord record)
        {
            throw new NotSupportedException();
        }
    }
}
