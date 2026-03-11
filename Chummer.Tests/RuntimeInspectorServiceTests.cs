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
            string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal)
            && string.Equals(descriptor.TitleKey, "ruleset.capability.session.quick-actions.title", StringComparison.Ordinal)
            && descriptor.SessionSafe
            && string.IsNullOrWhiteSpace(descriptor.ProviderId)));
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

    private static RulesetPluginRegistry CreatePluginRegistry() =>
        new(
        [
            new Sr5RulesetPlugin(),
            new Sr6RulesetPlugin()
        ]);

    private static RuleProfileRegistryEntry CreateProfile()
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
            new ArtifactInstallState(ArtifactInstallStates.Available),
            RegistryEntrySourceKinds.BuiltInCoreProfile);
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
