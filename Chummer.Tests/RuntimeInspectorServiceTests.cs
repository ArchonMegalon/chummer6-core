#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RuntimeInspectorServiceTests
{
    [TestMethod]
    public void Runtime_inspector_service_projects_profile_runtime_lock_rulepacks_and_warnings()
    {
        DefaultRuntimeInspectorService service = new(
            CreatePluginRegistry(),
            new RuleProfileRegistryServiceStub(CreateProfile()),
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
                        OwnerId: "local-single-user",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "official.sr5.core", RulesetDefaults.Sr5);

        Assert.IsNotNull(projection);
        Assert.AreEqual(RuntimeInspectorTargetKinds.RuntimeLock, projection.TargetKind);
        Assert.AreEqual("official.sr5.core", projection.TargetId);
        Assert.AreEqual(ArtifactInstallStates.Available, projection.Install.State);
        Assert.AreEqual("runtime-lock-sha256", projection.Install.RuntimeFingerprint);
        Assert.AreEqual(RegistryEntrySourceKinds.BuiltInCoreProfile, projection.ProfileSourceKind);
        Assert.HasCount(1, projection.ResolvedRulePacks);
        Assert.AreEqual("house-rules", projection.ResolvedRulePacks[0].RulePack.Id);
        Assert.AreEqual(RegistryEntrySourceKinds.PersistedManifest, projection.ResolvedRulePacks[0].SourceKind);
        Assert.IsNotNull(projection.CapabilityDescriptors);
        Assert.IsTrue(projection.CapabilityDescriptors.Any(descriptor =>
            string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal)
            && string.Equals(descriptor.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
            && string.Equals(descriptor.TitleKey, "ruleset.capability.derive.stat.title", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(descriptor.ProviderId)));
        Assert.IsTrue(projection.CapabilityDescriptors.Any(descriptor =>
            string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveInitiative, StringComparison.Ordinal)
            && string.Equals(descriptor.InvocationKind, RulesetCapabilityInvocationKinds.Rule, StringComparison.Ordinal)
            && string.Equals(descriptor.TitleKey, "ruleset.capability.derive.initiative.title", StringComparison.Ordinal)
            && !descriptor.SessionSafe
            && string.IsNullOrWhiteSpace(descriptor.ProviderId)));
        Assert.IsTrue(projection.CapabilityDescriptors.Any(descriptor =>
            string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal)
            && string.Equals(descriptor.TitleKey, "ruleset.capability.session.quick-actions.title", StringComparison.Ordinal)
            && descriptor.SessionSafe
            && string.IsNullOrWhiteSpace(descriptor.ProviderId)));
        Assert.IsNotNull(projection.Promotion);
        Assert.AreEqual(RuleProfilePublicationStatuses.Published, projection.Promotion!.PublicationStatus);
        StringAssert.Contains(projection.Promotion.PromotionSummary, "Stable rule environment");
        StringAssert.Contains(projection.Promotion.RollbackSummary, "No install target is pinned yet");
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, projection.Promotion.CurrentStage);
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, projection.Promotion.PromotionTargetStage);
        Assert.IsTrue(projection.Warnings.Any(warning => string.Equals(warning.Kind, RuntimeInspectorWarningKinds.Trust, StringComparison.Ordinal)));
        Assert.IsTrue(projection.CompatibilityDiagnostics.Any(diagnostic =>
            string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.Compatible, StringComparison.Ordinal)
            && string.Equals(diagnostic.MessageKey, "runtime.lock.compatibility.compatible", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Runtime_inspector_service_returns_null_for_unknown_profile()
    {
        DefaultRuntimeInspectorService service = new(
            CreatePluginRegistry(),
            new RuleProfileRegistryServiceStub(null),
            new RulePackRegistryServiceStub([]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "missing-profile", RulesetDefaults.Sr5);

        Assert.IsNull(projection);
    }

    [TestMethod]
    public void Runtime_inspector_service_surfaces_installed_runtime_drift_as_rebind_receipt()
    {
        DefaultRuntimeInspectorService service = new(
            CreatePluginRegistry(),
            new RuleProfileRegistryServiceStub(CreateProfile(installedRuntimeFingerprint: "runtime-lock-sha256-old")),
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
                        OwnerId: "local-single-user",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "official.sr5.core", RulesetDefaults.Sr5);

        Assert.IsNotNull(projection);
        Assert.IsTrue(projection.CompatibilityDiagnostics.Any(diagnostic =>
            string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.RebindRequired, StringComparison.Ordinal)
            && string.Equals(diagnostic.MessageKey, "runtime.lock.compatibility.install-runtime-drift", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Warnings.Any(warning =>
            string.Equals(warning.Kind, RuntimeInspectorWarningKinds.Migration, StringComparison.Ordinal)
            && string.Equals(warning.MessageKey, "runtime.inspector.warning.migration.rebind-required", StringComparison.Ordinal)));
        Assert.IsTrue(projection.MigrationPreview.Any(item =>
            string.Equals(item.Kind, RuntimeMigrationPreviewChangeKinds.ContentBundleUpdated, StringComparison.Ordinal)
            && string.Equals(item.BeforeValue, "runtime-lock-sha256-old", StringComparison.Ordinal)
            && string.Equals(item.AfterValue, "runtime-lock-sha256", StringComparison.Ordinal)
            && item.RequiresRebind));
        Assert.IsNotNull(projection.Promotion);
        StringAssert.Contains(projection.Promotion!.RollbackSummary, "No install target is pinned yet");
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, projection.Promotion.CurrentStage);
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, projection.Promotion.PromotionTargetStage);
    }

    [TestMethod]
    public void Runtime_inspector_service_handles_missing_plugin_pack_and_provider_binding_edge_paths()
    {
        RuleProfileRegistryEntry profile = CreateCustomProfile(
            rulesetId: "shadowrun-x",
            updateChannel: RuleProfileUpdateChannels.Preview,
            sourceKind: "custom-profile",
            publicationVisibility: ArtifactVisibilityModes.Public,
            publicationStatus: RuleProfilePublicationStatuses.Draft,
            publishedAtUtc: null,
            installedRuntimeFingerprint: "",
            installedTargetKind: RuleProfileApplyTargetKinds.Character,
            installedTargetId: "char-7",
            rulePacks:
            [
                new RuleProfilePackSelection(new ArtifactVersionReference("pack", "1.0.0"), Required: true, EnabledByDefault: true),
                new RuleProfilePackSelection(new ArtifactVersionReference("pack.ext", "2.0.0"), Required: true, EnabledByDefault: false),
                new RuleProfilePackSelection(new ArtifactVersionReference("missing-pack", "9.9.9"), Required: true, EnabledByDefault: true)
            ],
            providerBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = "pack.ext/derive-stat",
                [RulePackCapabilityIds.DeriveInitiative] = "provider-without-pack"
            });

        DefaultRuntimeInspectorService service = new(
            new RulesetPluginRegistry([]),
            new RuleProfileRegistryServiceStub(profile),
            new RulePackRegistryServiceStub(
            [
                CreateRulePackEntry("pack", "1.0.0", "Pack Base", ArtifactVisibilityModes.LocalOnly),
                CreateRulePackEntry("pack.ext", "2.0.0", "Pack Extended", ArtifactVisibilityModes.Public)
            ]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "official.sr5.core", "shadowrun-x");

        Assert.IsNotNull(projection);
        Assert.HasCount(0, projection.CapabilityDescriptors ?? []);
        Assert.AreEqual("runtime-lock-sha256", projection.Install.RuntimeFingerprint);
        Assert.AreEqual("pack.ext", projection.ProviderBindings.Single(binding => binding.CapabilityId == RulePackCapabilityIds.DeriveStat).PackId);
        Assert.IsNull(projection.ProviderBindings.Single(binding => binding.CapabilityId == RulePackCapabilityIds.DeriveInitiative).PackId);
        Assert.AreEqual("missing-pack", projection.ResolvedRulePacks.Single(entry => entry.RulePack.Id == "missing-pack").Title);
        Assert.AreEqual(ArtifactVisibilityModes.LocalOnly, projection.ResolvedRulePacks.Single(entry => entry.RulePack.Id == "missing-pack").Visibility);
        Assert.IsTrue(projection.CompatibilityDiagnostics.Any(diagnostic =>
            string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.MissingPack, StringComparison.Ordinal)
            && string.Equals(diagnostic.MessageKey, "runtime.lock.compatibility.missing-pack", StringComparison.Ordinal)));
        Assert.IsTrue(projection.Warnings.Any(warning =>
            string.Equals(warning.Kind, RuntimeInspectorWarningKinds.Compatibility, StringComparison.Ordinal)
            && string.Equals(warning.MessageKey, "runtime.inspector.warning.compatibility.missing-pack", StringComparison.Ordinal)));
        Assert.IsNotNull(projection.Promotion);
        StringAssert.Contains(projection.Promotion!.PromotionSummary, "Preview rule environment");
        StringAssert.Contains(projection.Promotion.RollbackSummary, "character:char-7");
        StringAssert.Contains(projection.Promotion.LineageSummary, "custom-profile profile compiles");
        Assert.AreEqual(RuntimeInspectorPromotionStages.Sandbox, projection.Promotion.CurrentStage);
        Assert.AreEqual(RuntimeInspectorPromotionStages.CampaignApproved, projection.Promotion.PromotionTargetStage);
    }

    [TestMethod]
    public void Runtime_inspector_service_surfaces_provider_binding_none_and_runtime_pinned_when_profile_has_no_rulepacks()
    {
        RuleProfileRegistryEntry profile = CreateCustomProfile(
            rulePacks: [],
            providerBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            updateChannel: RuleProfileUpdateChannels.CampaignPinned,
            installedRuntimeFingerprint: "");

        DefaultRuntimeInspectorService service = new(
            CreatePluginRegistry(),
            new RuleProfileRegistryServiceStub(profile),
            new RulePackRegistryServiceStub([]));

        RuntimeInspectorProjection? projection = service.GetProfileProjection(OwnerScope.LocalSingleUser, "official.sr5.core", RulesetDefaults.Sr5);

        Assert.IsNotNull(projection);
        Assert.HasCount(0, projection.ResolvedRulePacks);
        Assert.IsTrue(projection.Warnings.Any(warning =>
            string.Equals(warning.Kind, RuntimeInspectorWarningKinds.ProviderBinding, StringComparison.Ordinal)
            && string.Equals(warning.MessageKey, "runtime.inspector.warning.provider-binding.none", StringComparison.Ordinal)));
        Assert.HasCount(1, projection.MigrationPreview);
        Assert.AreEqual("runtime.inspector.preview.runtime-pinned", projection.MigrationPreview[0].SummaryKey);
        Assert.IsFalse(projection.MigrationPreview[0].RequiresRebind);
        Assert.IsNotNull(projection.Promotion);
        Assert.AreEqual(RuntimeInspectorPromotionStages.CampaignApproved, projection.Promotion!.CurrentStage);
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, projection.Promotion.PromotionTargetStage);
    }

    private static RulesetPluginRegistry CreatePluginRegistry() =>
        new(
        [
            new Sr5RulesetPlugin(),
            new Sr6RulesetPlugin()
        ]);

    private static RuleProfileRegistryEntry CreateProfile(string? installedRuntimeFingerprint = null)
    {
        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: "official.sr5.core",
                Title: "Official SR5 Core",
                Description: "Curated runtime.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks:
                [
                    new RuleProfilePackSelection(
                        new ArtifactVersionReference("house-rules", "1.0.0"),
                        Required: true,
                        EnabledByDefault: true)
                ],
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: RulesetDefaults.Sr5,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: "official.sr5.base",
                            RulesetId: RulesetDefaults.Sr5,
                            Version: "schema-1",
                            Title: "SR5 Base",
                            Description: "Built-in base content.",
                            AssetPaths: ["data/", "lang/"])
                    ],
                    RulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")],
                    ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["content.catalog"] = "house-rules/content.catalog"
                    },
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: "runtime-lock-sha256"),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available, RuntimeFingerprint: installedRuntimeFingerprint),
            RegistryEntrySourceKinds.BuiltInCoreProfile);
    }

    private static RuleProfileRegistryEntry CreateCustomProfile(
        string rulesetId = RulesetDefaults.Sr5,
        string updateChannel = RuleProfileUpdateChannels.Stable,
        string sourceKind = RegistryEntrySourceKinds.BuiltInCoreProfile,
        string publicationVisibility = ArtifactVisibilityModes.LocalOnly,
        string publicationStatus = RuleProfilePublicationStatuses.Published,
        DateTimeOffset? publishedAtUtc = null,
        string? installedRuntimeFingerprint = null,
        string? installedTargetKind = null,
        string? installedTargetId = null,
        IReadOnlyList<RuleProfilePackSelection>? rulePacks = null,
        IReadOnlyDictionary<string, string>? providerBindings = null)
    {
        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: "official.sr5.core",
                Title: "Official SR5 Core",
                Description: "Curated runtime.",
                RulesetId: rulesetId,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks: rulePacks ?? [],
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: rulesetId,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: $"official.{rulesetId}.base",
                            RulesetId: rulesetId,
                            Version: "schema-1",
                            Title: $"{rulesetId} Base",
                            Description: "Built-in base content.",
                            AssetPaths: ["data/", "lang/"])
                    ],
                    RulePacks: (rulePacks ?? []).Select(selection => selection.RulePack).ToArray(),
                    ProviderBindings: providerBindings ?? new Dictionary<string, string>(StringComparer.Ordinal),
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: "runtime-lock-sha256"),
                UpdateChannel: updateChannel),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: publicationVisibility,
                PublicationStatus: publicationStatus,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: [],
                PublishedAtUtc: publishedAtUtc),
            new ArtifactInstallState(
                ArtifactInstallStates.Available,
                InstalledTargetKind: installedTargetKind,
                InstalledTargetId: installedTargetId,
                RuntimeFingerprint: installedRuntimeFingerprint),
            sourceKind);
    }

    private static RulePackRegistryEntry CreateRulePackEntry(
        string packId,
        string version,
        string title,
        string visibility)
    {
        return new RulePackRegistryEntry(
            new RulePackManifest(
                PackId: packId,
                Version: version,
                Title: title,
                Author: "GM",
                Description: "Campaign overlay.",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "rulepack-v1",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: visibility,
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
                OwnerId: "local-single-user",
                Visibility: visibility,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Installed));
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly RuleProfileRegistryEntry? _entry;

        public RuleProfileRegistryServiceStub(RuleProfileRegistryEntry? entry)
        {
            _entry = entry;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entry is null ? [] : [_entry];

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null)
        {
            return _entry is not null && string.Equals(profileId, _entry.Manifest.ProfileId, StringComparison.Ordinal)
                ? _entry
                : null;
        }
    }

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null)
        {
            return _entries.FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));
        }
    }
}
