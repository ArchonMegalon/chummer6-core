#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RuleProfileRegistryServiceTests
{
    [TestMethod]
    public void Default_registry_service_projects_core_and_overlay_profiles_from_rulesets_and_rulepacks()
    {
        RulesetPluginRegistry pluginRegistry =
            new([
                new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5),
                new StubRulesetPlugin(RulesetDefaults.Sr6, "Shadowrun Sixth Edition", schemaVersion: 6)
            ]);
        DefaultRuleProfileRegistryService service = new(
            pluginRegistry,
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "1.0.0",
                        Title: "House Rules",
                        Author: "GM",
                        Description: "Campaign overlay.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets:
                        [
                            new RulePackAssetDescriptor(
                                Kind: RulePackAssetKinds.Xml,
                                Mode: RulePackAssetModes.MergeCatalog,
                                RelativePath: "data/qualities.xml",
                                Checksum: "sha256:abc")
                        ],
                        Capabilities:
                        [
                            new RulePackCapabilityDescriptor(
                                CapabilityId: RulePackCapabilityIds.ContentCatalog,
                                AssetKind: RulePackAssetKinds.Xml,
                                AssetMode: RulePackAssetModes.MergeCatalog)
                        ],
                        ExecutionPolicies: []),
                    new RulePackPublicationMetadata(
                        OwnerId: "system",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileManifestStoreStub(),
            new RuleProfilePublicationStoreStub(),
            new RuleProfileInstallStateStoreStub(),
            new DefaultRuntimeFingerprintService());

        IReadOnlyList<RuleProfileRegistryEntry> entries = service.List(OwnerScope.LocalSingleUser);

        Assert.HasCount(3, entries);
        Assert.IsNotNull(entries.SingleOrDefault(entry => string.Equals(entry.Manifest.ProfileId, "official.sr5.core", StringComparison.Ordinal)));
        RuleProfileRegistryEntry? overlayProfile = entries.SingleOrDefault(entry => string.Equals(entry.Manifest.ProfileId, "local.sr5.current-overlays", StringComparison.Ordinal));
        Assert.IsNotNull(overlayProfile);
        Assert.HasCount(1, overlayProfile.Manifest.RulePacks);
        Assert.AreEqual("house-rules", overlayProfile.Manifest.RulePacks[0].RulePack.Id);
        Assert.AreEqual(RuleProfileCatalogKinds.Personal, overlayProfile.Manifest.CatalogKind);
        Assert.AreEqual(ArtifactInstallStates.Available, overlayProfile.Install.State);
        Assert.AreEqual(RegistryEntrySourceKinds.BuiltInCoreProfile, entries.Single(entry => string.Equals(entry.Manifest.ProfileId, "official.sr5.core", StringComparison.Ordinal)).SourceKind);
        Assert.AreEqual(RegistryEntrySourceKinds.OverlayDerivedProfile, overlayProfile.SourceKind);
    }

    [TestMethod]
    public void Default_registry_service_returns_null_for_unknown_profile()
    {
        DefaultRuleProfileRegistryService service = new(
            new RulesetPluginRegistry([new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5)]),
            new RulePackRegistryServiceStub([]),
            new RuleProfileManifestStoreStub(),
            new RuleProfilePublicationStoreStub(),
            new RuleProfileInstallStateStoreStub(),
            new DefaultRuntimeFingerprintService());

        RuleProfileRegistryEntry? entry = service.Get(OwnerScope.LocalSingleUser, "missing-profile", RulesetDefaults.Sr5);

        Assert.IsNull(entry);
    }

    [TestMethod]
    public void Default_registry_service_runtime_fingerprint_tracks_rulepack_asset_checksums()
    {
        DefaultRuleProfileRegistryService checksumA = CreateServiceWithRulePackChecksum("sha256:abc");
        DefaultRuleProfileRegistryService checksumB = CreateServiceWithRulePackChecksum("sha256:def");

        string fingerprintA = checksumA.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!.Manifest.RuntimeLock.RuntimeFingerprint;
        string fingerprintB = checksumB.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!.Manifest.RuntimeLock.RuntimeFingerprint;

        Assert.AreNotEqual(fingerprintA, fingerprintB);
    }

    [TestMethod]
    public void Default_registry_service_builds_deterministic_overlay_runtime_from_equivalent_rulepack_sets()
    {
        DefaultRuleProfileRegistryService serviceA = CreateServiceWithRulePacks(
        [
            CreateRulePack("zeta-pack", "1.0.0", RulePackCapabilityIds.ValidateCharacter),
            CreateRulePack("alpha-pack", "1.0.0", RulePackCapabilityIds.ValidateCharacter, RulePackCapabilityIds.ContentCatalog)
        ]);
        DefaultRuleProfileRegistryService serviceB = CreateServiceWithRulePacks(
        [
            CreateRulePack("alpha-pack", "1.0.0", RulePackCapabilityIds.ContentCatalog, RulePackCapabilityIds.ValidateCharacter),
            CreateRulePack("zeta-pack", "1.0.0", RulePackCapabilityIds.ValidateCharacter)
        ]);

        RuleProfileRegistryEntry overlayA = serviceA.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!;
        RuleProfileRegistryEntry overlayB = serviceB.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!;

        CollectionAssert.AreEqual(
            overlayA.Manifest.RulePacks.Select(static pack => $"{pack.RulePack.Id}@{pack.RulePack.Version}").ToArray(),
            overlayB.Manifest.RulePacks.Select(static pack => $"{pack.RulePack.Id}@{pack.RulePack.Version}").ToArray());
        CollectionAssert.AreEqual(
            overlayA.Manifest.RuntimeLock.RulePacks.Select(static pack => $"{pack.Id}@{pack.Version}").ToArray(),
            overlayB.Manifest.RuntimeLock.RulePacks.Select(static pack => $"{pack.Id}@{pack.Version}").ToArray());
        CollectionAssert.AreEqual(
            overlayA.Manifest.RuntimeLock.ProviderBindings.Select(static binding => $"{binding.Key}={binding.Value}").ToArray(),
            overlayB.Manifest.RuntimeLock.ProviderBindings.Select(static binding => $"{binding.Key}={binding.Value}").ToArray());
        Assert.AreEqual(overlayA.Manifest.RuntimeLock.RuntimeFingerprint, overlayB.Manifest.RuntimeLock.RuntimeFingerprint);
    }

    [TestMethod]
    public void Default_registry_service_provider_bindings_are_deterministic_across_discovery_order_for_shared_capabilities()
    {
        DefaultRuleProfileRegistryService serviceA = CreateServiceWithRulePacks(
        [
            CreateRulePack("zeta-pack", "1.0.0", RulePackCapabilityIds.ValidateCharacter, RulePackCapabilityIds.DeriveInitiative),
            CreateRulePack("alpha-pack", "1.0.0", RulePackCapabilityIds.ValidateCharacter, RulePackCapabilityIds.ContentCatalog)
        ]);
        DefaultRuleProfileRegistryService serviceB = CreateServiceWithRulePacks(
        [
            CreateRulePack("alpha-pack", "1.0.0", RulePackCapabilityIds.ContentCatalog, RulePackCapabilityIds.ValidateCharacter),
            CreateRulePack("zeta-pack", "1.0.0", RulePackCapabilityIds.DeriveInitiative, RulePackCapabilityIds.ValidateCharacter)
        ]);

        RuleProfileRegistryEntry overlayA = serviceA.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!;
        RuleProfileRegistryEntry overlayB = serviceB.Get(OwnerScope.LocalSingleUser, "local.sr5.current-overlays", RulesetDefaults.Sr5)!;

        string validateBindingA = overlayA.Manifest.RuntimeLock.ProviderBindings[RulePackCapabilityIds.ValidateCharacter];
        string validateBindingB = overlayB.Manifest.RuntimeLock.ProviderBindings[RulePackCapabilityIds.ValidateCharacter];

        CollectionAssert.AreEqual(
            overlayA.Manifest.RuntimeLock.ProviderBindings.Select(static binding => $"{binding.Key}={binding.Value}").ToArray(),
            overlayB.Manifest.RuntimeLock.ProviderBindings.Select(static binding => $"{binding.Key}={binding.Value}").ToArray());
        Assert.AreEqual("zeta-pack/validate.character", validateBindingA);
        Assert.AreEqual(validateBindingA, validateBindingB);
    }

    [TestMethod]
    public void Default_registry_service_prefers_owner_backed_profile_publication_metadata_when_present()
    {
        DefaultRuleProfileRegistryService service = new(
            new RulesetPluginRegistry([new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5)]),
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "1.0.0",
                        Title: "House Rules",
                        Author: "GM",
                        Description: "Campaign overlay.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets: [],
                        Capabilities: [],
                        ExecutionPolicies: []),
                    new RulePackPublicationMetadata(
                        OwnerId: "alice",
                        Visibility: ArtifactVisibilityModes.Private,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileManifestStoreStub(),
            new RuleProfilePublicationStoreStub(
            [
                new RuleProfilePublicationRecord(
                    ProfileId: "local.sr5.current-overlays",
                    RulesetId: RulesetDefaults.Sr5,
                    Publication: new RuleProfilePublicationMetadata(
                        OwnerId: "alice",
                        Visibility: ArtifactVisibilityModes.CampaignShared,
                        PublicationStatus: RuleProfilePublicationStatuses.Draft,
                        Review: new RulePackReviewDecision(RulePackReviewStates.PendingReview),
                        Shares:
                        [
                            new RulePackShareGrant(
                                SubjectKind: RulePackShareSubjectKinds.Campaign,
                                SubjectId: "campaign-7",
                                AccessLevel: RulePackShareAccessLevels.Install)
                        ]))
            ]),
            new RuleProfileInstallStateStoreStub(
            [
                new RuleProfileInstallRecord(
                    ProfileId: "local.sr5.current-overlays",
                    RulesetId: RulesetDefaults.Sr5,
                    Install: new ArtifactInstallState(
                        State: ArtifactInstallStates.Pinned,
                        InstalledAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:runtime"))
            ]),
            new DefaultRuntimeFingerprintService());

        RuleProfileRegistryEntry entry = service.Get(new OwnerScope("alice"), "local.sr5.current-overlays", RulesetDefaults.Sr5)!;

        Assert.AreEqual("alice", entry.Publication.OwnerId);
        Assert.AreEqual(ArtifactVisibilityModes.CampaignShared, entry.Publication.Visibility);
        Assert.AreEqual(RuleProfilePublicationStatuses.Draft, entry.Publication.PublicationStatus);
        Assert.AreEqual(RulePackReviewStates.PendingReview, entry.Publication.Review.State);
        Assert.HasCount(1, entry.Publication.Shares);
        Assert.AreEqual(ArtifactInstallStates.Pinned, entry.Install.State);
        Assert.AreEqual("workspace-1", entry.Install.InstalledTargetId);
        Assert.AreEqual(RegistryEntrySourceKinds.OverlayDerivedProfile, entry.SourceKind);
    }

    [TestMethod]
    public void Default_registry_service_merges_owner_backed_profile_manifests_and_prefers_persisted_entries_on_key_collisions()
    {
        RulesetPluginRegistry pluginRegistry =
            new([new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5)]);
        ResolvedRuntimeLock persistedRuntimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-5",
                    Title: "SR5 Base Content",
                    Description: "Persisted runtime bundle.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "sha256:persisted");
        DefaultRuleProfileRegistryService service = new(
            pluginRegistry,
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "1.0.0",
                        Title: "House Rules",
                        Author: "GM",
                        Description: "Campaign overlay.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets: [],
                        Capabilities: [],
                        ExecutionPolicies: []),
                    new RulePackPublicationMetadata(
                        OwnerId: "alice",
                        Visibility: ArtifactVisibilityModes.Private,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileManifestStoreStub(
            [
                new RuleProfileManifestRecord(
                    new RuleProfileManifest(
                        ProfileId: "local.sr5.current-overlays",
                        Title: "Persisted Overlay Runtime",
                        Description: "Owner-backed runtime projection.",
                        RulesetId: RulesetDefaults.Sr5,
                        Audience: RuleProfileAudienceKinds.Advanced,
                        CatalogKind: RuleProfileCatalogKinds.Personal,
                        RulePacks:
                        [
                            new RuleProfilePackSelection(
                                RulePack: new ArtifactVersionReference("house-rules", "1.0.0"),
                                Required: true,
                                EnabledByDefault: true)
                        ],
                        DefaultToggles: [],
                        RuntimeLock: persistedRuntimeLock,
                        UpdateChannel: RuleProfileUpdateChannels.Preview,
                        Notes: "Persisted owner copy.")),
                new RuleProfileManifestRecord(
                    new RuleProfileManifest(
                        ProfileId: "campaign.seattle.runtime",
                        Title: "Seattle Campaign Runtime",
                        Description: "Extra owner-backed profile.",
                        RulesetId: RulesetDefaults.Sr5,
                        Audience: RuleProfileAudienceKinds.Campaign,
                        CatalogKind: RuleProfileCatalogKinds.Personal,
                        RulePacks: [],
                        DefaultToggles: [],
                        RuntimeLock: persistedRuntimeLock,
                        UpdateChannel: RuleProfileUpdateChannels.CampaignPinned,
                        Notes: "Campaign shared profile.")) 
            ]),
            new RuleProfilePublicationStoreStub(),
            new RuleProfileInstallStateStoreStub(),
            new DefaultRuntimeFingerprintService());

        IReadOnlyList<RuleProfileRegistryEntry> entries = service.List(new OwnerScope("alice"), RulesetDefaults.Sr5);

        Assert.HasCount(3, entries);
        RuleProfileRegistryEntry persistedCollision = entries.Single(entry => string.Equals(entry.Manifest.ProfileId, "local.sr5.current-overlays", StringComparison.Ordinal));
        RuleProfileRegistryEntry persistedOnly = entries.Single(entry => string.Equals(entry.Manifest.ProfileId, "campaign.seattle.runtime", StringComparison.Ordinal));
        Assert.AreEqual("Persisted Overlay Runtime", persistedCollision.Manifest.Title);
        Assert.AreEqual("sha256:persisted", persistedCollision.Manifest.RuntimeLock.RuntimeFingerprint);
        Assert.AreEqual("alice", persistedCollision.Publication.OwnerId);
        Assert.AreEqual(RuleProfilePublicationStatuses.Draft, persistedCollision.Publication.PublicationStatus);
        Assert.AreEqual(ArtifactInstallStates.Available, persistedCollision.Install.State);
        Assert.AreEqual(RegistryEntrySourceKinds.PersistedManifest, persistedCollision.SourceKind);
        Assert.AreEqual("Seattle Campaign Runtime", persistedOnly.Manifest.Title);
        Assert.AreEqual(ArtifactVisibilityModes.LocalOnly, persistedOnly.Publication.Visibility);
        Assert.AreEqual(RegistryEntrySourceKinds.PersistedManifest, persistedOnly.SourceKind);
    }

    private static DefaultRuleProfileRegistryService CreateServiceWithRulePackChecksum(string checksum)
    {
        return new DefaultRuleProfileRegistryService(
            new RulesetPluginRegistry([new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5)]),
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "1.0.0",
                        Title: "House Rules",
                        Author: "GM",
                        Description: "Campaign overlay.",
                        Targets: [RulesetDefaults.Sr5],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        TrustTier: ArtifactTrustTiers.LocalOnly,
                        Assets:
                        [
                            new RulePackAssetDescriptor(
                                Kind: RulePackAssetKinds.Xml,
                                Mode: RulePackAssetModes.MergeCatalog,
                                RelativePath: "data/qualities.xml",
                                Checksum: checksum)
                        ],
                        Capabilities:
                        [
                            new RulePackCapabilityDescriptor(
                                CapabilityId: RulePackCapabilityIds.ContentCatalog,
                                AssetKind: RulePackAssetKinds.Xml,
                                AssetMode: RulePackAssetModes.MergeCatalog)
                        ],
                        ExecutionPolicies: []),
                    new RulePackPublicationMetadata(
                        OwnerId: "system",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileManifestStoreStub(),
            new RuleProfilePublicationStoreStub(),
            new RuleProfileInstallStateStoreStub(),
            new DefaultRuntimeFingerprintService());
    }

    private static DefaultRuleProfileRegistryService CreateServiceWithRulePacks(IReadOnlyList<RulePackRegistryEntry> rulePacks)
    {
        return new DefaultRuleProfileRegistryService(
            new RulesetPluginRegistry([new StubRulesetPlugin(RulesetDefaults.Sr5, "Shadowrun Fifth Edition", schemaVersion: 5)]),
            new RulePackRegistryServiceStub(rulePacks),
            new RuleProfileManifestStoreStub(),
            new RuleProfilePublicationStoreStub(),
            new RuleProfileInstallStateStoreStub(),
            new DefaultRuntimeFingerprintService());
    }

    private static RulePackRegistryEntry CreateRulePack(string packId, string version, params string[] capabilityIds)
    {
        return new RulePackRegistryEntry(
            new RulePackManifest(
                PackId: packId,
                Version: version,
                Title: $"{packId} title",
                Author: "GM",
                Description: "Campaign overlay.",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "rulepack-v1",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: ArtifactVisibilityModes.LocalOnly,
                TrustTier: ArtifactTrustTiers.LocalOnly,
                Assets:
                [
                    new RulePackAssetDescriptor(
                        Kind: RulePackAssetKinds.Xml,
                        Mode: RulePackAssetModes.MergeCatalog,
                        RelativePath: $"data/{packId}.xml",
                        Checksum: $"sha256:{packId}")
                ],
                Capabilities: capabilityIds
                    .Select(static capabilityId => new RulePackCapabilityDescriptor(
                        CapabilityId: capabilityId,
                        AssetKind: RulePackAssetKinds.Xml,
                        AssetMode: RulePackAssetModes.MergeCatalog))
                    .ToArray(),
                ExecutionPolicies: []),
            new RulePackPublicationMetadata(
                OwnerId: "system",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Installed));
    }

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _entries
                : _entries.Where(entry => entry.Manifest.Targets.Contains(normalizedRulesetId, StringComparer.Ordinal)).ToArray();
        }

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null)
        {
            return List(owner, rulesetId)
                .FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));
        }
    }

    private sealed class RuleProfilePublicationStoreStub : IRuleProfilePublicationStore
    {
        private readonly IReadOnlyList<RuleProfilePublicationRecord> _records;

        public RuleProfilePublicationStoreStub(IReadOnlyList<RuleProfilePublicationRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RuleProfilePublicationRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RuleProfilePublicationRecord? Get(OwnerScope owner, string profileId, string rulesetId)
        {
            return _records.FirstOrDefault(
                record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                    && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RuleProfilePublicationRecord Upsert(OwnerScope owner, RuleProfilePublicationRecord record)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RuleProfileManifestStoreStub : IRuleProfileManifestStore
    {
        private readonly IReadOnlyList<RuleProfileManifestRecord> _records;

        public RuleProfileManifestStoreStub(IReadOnlyList<RuleProfileManifestRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RuleProfileManifestRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => string.Equals(record.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RuleProfileManifestRecord? Get(OwnerScope owner, string profileId, string rulesetId)
        {
            return List(owner, rulesetId).FirstOrDefault(
                record => string.Equals(record.Manifest.ProfileId, profileId, StringComparison.Ordinal));
        }

        public RuleProfileManifestRecord Upsert(OwnerScope owner, RuleProfileManifestRecord record)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class RuleProfileInstallStateStoreStub : IRuleProfileInstallStateStore
    {
        private readonly IReadOnlyList<RuleProfileInstallRecord> _records;

        public RuleProfileInstallStateStoreStub(IReadOnlyList<RuleProfileInstallRecord>? records = null)
        {
            _records = records ?? [];
        }

        public IReadOnlyList<RuleProfileInstallRecord> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _records
                : _records.Where(record => string.Equals(record.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RuleProfileInstallRecord? Get(OwnerScope owner, string profileId, string rulesetId)
        {
            return _records.FirstOrDefault(
                record => string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                    && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RuleProfileInstallRecord Upsert(OwnerScope owner, RuleProfileInstallRecord record)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubRulesetPlugin : IRulesetPlugin
    {
        public StubRulesetPlugin(string id, string displayName, int schemaVersion)
        {
            Id = new RulesetId(id);
            DisplayName = displayName;
            Serializer = new StubSerializer(Id, schemaVersion);
            ShellDefinitions = new StubShellDefinitions();
            Catalogs = new StubCatalogs();
            CapabilityDescriptors = new StubCapabilityDescriptorProvider();
            Capabilities = new StubCapabilityHost();
            Rules = new StubRuleHost();
            Scripts = new StubScriptHost();
        }

        public RulesetId Id { get; }

        public string DisplayName { get; }

        public IRulesetSerializer Serializer { get; }

        public IRulesetShellDefinitionProvider ShellDefinitions { get; }

        public IRulesetCatalogProvider Catalogs { get; }

        public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; }

        public IRulesetCapabilityHost Capabilities { get; }

        public IRulesetRuleHost Rules { get; }

        public IRulesetScriptHost Scripts { get; }
    }

    private sealed class StubSerializer : IRulesetSerializer
    {
        public StubSerializer(RulesetId rulesetId, int schemaVersion)
        {
            RulesetId = rulesetId;
            SchemaVersion = schemaVersion;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion { get; }

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload) => new(RulesetId.NormalizedValue, SchemaVersion, payloadKind, payload);
    }

    private sealed class StubShellDefinitions : IRulesetShellDefinitionProvider
    {
        public IReadOnlyList<AppCommandDefinition> GetCommands() => [];

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => [];
    }

    private sealed class StubCatalogs : IRulesetCatalogProvider
    {
        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => [];
    }

    private sealed class StubRuleHost : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetRuleEvaluationResult(true, new Dictionary<string, object?>(), []));
    }

    private sealed class StubCapabilityHost : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                true,
                new RulesetCapabilityValue(RulesetCapabilityValueKinds.Object, Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)),
                []));
    }

    private sealed class StubCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => [];
    }

    private sealed class StubScriptHost : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetScriptExecutionResult(true, null, new Dictionary<string, object?>()));
    }
}
