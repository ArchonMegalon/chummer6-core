#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class ActiveRuntimeStatusServiceTests
{
    [TestMethod]
    public void Active_runtime_status_service_prefers_pinned_global_default_profile_and_projects_runtime_summary()
    {
        ActiveRuntimeStatusProjection expected = new(
            ProfileId: "campaign.sr5.shadowops",
            Title: "Seattle ShadowOps",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "sha256:shadowops",
            InstallState: ArtifactInstallStates.Pinned,
            InstalledTargetKind: RuleProfileApplyTargetKinds.GlobalDefaults,
            InstalledTargetId: "global",
            RulePackCount: 1,
            ProviderBindingCount: 1,
            WarningCount: 2);
        DefaultActiveRuntimeStatusService service = new(
            new RuleProfileRegistryServiceStub(
            [
                CreateEntry(
                    profileId: "official.sr5.core",
                    title: "Official SR5 Core",
                    rulesetId: RulesetDefaults.Sr5,
                    install: new ArtifactInstallState(ArtifactInstallStates.Available),
                    runtimeFingerprint: "sha256:official"),
                CreateEntry(
                    profileId: expected.ProfileId,
                    title: expected.Title,
                    rulesetId: expected.RulesetId,
                    install: new ArtifactInstallState(
                        State: expected.InstallState,
                        InstalledAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                        InstalledTargetKind: expected.InstalledTargetKind,
                        InstalledTargetId: expected.InstalledTargetId,
                        RuntimeFingerprint: expected.RuntimeFingerprint),
                    runtimeFingerprint: expected.RuntimeFingerprint,
                    rulePackCount: expected.RulePackCount,
                    providerBindingCount: expected.ProviderBindingCount)
            ]),
            new RuntimeInspectorServiceStub(
                ("campaign.sr5.shadowops", RulesetDefaults.Sr5),
                CreateRuntimeProjection("campaign.sr5.shadowops", RulesetDefaults.Sr5, warningCount: 2)));

        ActiveRuntimeStatusProjection? projection = service.GetActiveProfileStatus(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.IsNotNull(projection);
        Assert.AreEqual(expected.ProfileId, projection.ProfileId);
        Assert.AreEqual(expected.Title, projection.Title);
        Assert.AreEqual(expected.RulesetId, projection.RulesetId);
        Assert.AreEqual(expected.RuntimeFingerprint, projection.RuntimeFingerprint);
        Assert.AreEqual(expected.InstallState, projection.InstallState);
        Assert.AreEqual(expected.InstalledTargetKind, projection.InstalledTargetKind);
        Assert.AreEqual(expected.InstalledTargetId, projection.InstalledTargetId);
        Assert.AreEqual(expected.RulePackCount, projection.RulePackCount);
        Assert.AreEqual(expected.ProviderBindingCount, projection.ProviderBindingCount);
        Assert.AreEqual(expected.WarningCount, projection.WarningCount);
    }

    [TestMethod]
    public void Active_runtime_status_service_falls_back_to_requested_official_core_when_no_profile_is_installed()
    {
        DefaultActiveRuntimeStatusService service = new(
            new RuleProfileRegistryServiceStub(
            [
                CreateEntry(
                    profileId: "official.sr5.core",
                    title: "Official SR5 Core",
                    rulesetId: RulesetDefaults.Sr5,
                    install: new ArtifactInstallState(ArtifactInstallStates.Available),
                    runtimeFingerprint: "sha256:official-sr5"),
                CreateEntry(
                    profileId: "official.sr6.core",
                    title: "Official SR6 Core",
                    rulesetId: RulesetDefaults.Sr6,
                    install: new ArtifactInstallState(ArtifactInstallStates.Available),
                    runtimeFingerprint: "sha256:official-sr6")
            ]),
            new RuntimeInspectorServiceStub(
                ("official.sr6.core", RulesetDefaults.Sr6),
                CreateRuntimeProjection("official.sr6.core", RulesetDefaults.Sr6, warningCount: 1)));

        ActiveRuntimeStatusProjection? projection = service.GetActiveProfileStatus(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);

        Assert.IsNotNull(projection);
        Assert.AreEqual("official.sr6.core", projection.ProfileId);
        Assert.AreEqual("Official SR6 Core", projection.Title);
        Assert.AreEqual(RulesetDefaults.Sr6, projection.RulesetId);
        Assert.AreEqual("sha256:official-sr6", projection.RuntimeFingerprint);
        Assert.AreEqual(1, projection.WarningCount);
    }

    private static RuleProfileRegistryEntry CreateEntry(
        string profileId,
        string title,
        string rulesetId,
        ArtifactInstallState install,
        string runtimeFingerprint,
        int rulePackCount = 0,
        int providerBindingCount = 0)
    {
        ArtifactVersionReference[] rulePacks = Enumerable.Range(1, rulePackCount)
            .Select(index => new ArtifactVersionReference($"pack-{index}", $"1.0.{index}"))
            .ToArray();
        Dictionary<string, string> providerBindings = Enumerable.Range(1, providerBindingCount)
            .ToDictionary(
                index => $"capability-{index}",
                index => $"provider-{index}",
                StringComparer.Ordinal);

        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: profileId,
                Title: title,
                Description: $"{title} runtime.",
                RulesetId: rulesetId,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks: rulePacks.Select(pack => new RuleProfilePackSelection(pack, Required: true, EnabledByDefault: true)).ToArray(),
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: rulesetId,
                    ContentBundles:
                    [
                        new ContentBundleDescriptor(
                            BundleId: $"official.{rulesetId}.base",
                            RulesetId: rulesetId,
                            Version: "schema-1",
                            Title: $"{title} Base",
                            Description: "Built-in bundle.",
                            AssetPaths: ["data/", "lang/"])
                    ],
                    RulePacks: rulePacks,
                    ProviderBindings: providerBindings,
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: runtimeFingerprint),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            install);
    }

    private static RuntimeInspectorProjection CreateRuntimeProjection(string profileId, string rulesetId, int warningCount)
    {
        return new RuntimeInspectorProjection(
            TargetKind: RuntimeInspectorTargetKinds.RuntimeLock,
            TargetId: profileId,
            RuntimeLock: new ResolvedRuntimeLock(
                RulesetId: rulesetId,
                ContentBundles:
                [
                    new ContentBundleDescriptor(
                        BundleId: $"official.{rulesetId}.base",
                        RulesetId: rulesetId,
                        Version: "schema-1",
                        Title: "Base",
                        Description: "Built-in bundle.",
                        AssetPaths: ["data/", "lang/"])
                ],
                RulePacks: [],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                EngineApiVersion: "rulepack-v1",
                RuntimeFingerprint: $"sha256:{profileId}"),
            Install: new ArtifactInstallState(ArtifactInstallStates.Available),
            ResolvedRulePacks: [],
            ProviderBindings: [],
            CompatibilityDiagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.Compatible,
                    Message: "Compatible.",
                    RequiredRulesetId: rulesetId,
                    RequiredRuntimeFingerprint: $"sha256:{profileId}")
            ],
            Warnings: Enumerable.Range(1, warningCount)
                .Select(index => new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Compatibility,
                    Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                    Message: $"Warning {index}",
                    SubjectId: profileId))
                .ToArray(),
            MigrationPreview: [],
            GeneratedAtUtc: DateTimeOffset.UtcNow);
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly IReadOnlyList<RuleProfileRegistryEntry> _entries;

        public RuleProfileRegistryServiceStub(IReadOnlyList<RuleProfileRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _entries
                : _entries.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null)
        {
            return List(owner, rulesetId)
                .FirstOrDefault(entry => string.Equals(entry.Manifest.ProfileId, profileId, StringComparison.Ordinal));
        }
    }

    private sealed class RuntimeInspectorServiceStub : IRuntimeInspectorService
    {
        private readonly (string ProfileId, string RulesetId) _expectedKey;
        private readonly RuntimeInspectorProjection _projection;

        public RuntimeInspectorServiceStub((string ProfileId, string RulesetId) expectedKey, RuntimeInspectorProjection projection)
        {
            _expectedKey = expectedKey;
            _projection = projection;
        }

        public RuntimeInspectorProjection? GetProfileProjection(OwnerScope owner, string profileId, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return string.Equals(profileId, _expectedKey.ProfileId, StringComparison.Ordinal)
                && string.Equals(normalizedRulesetId, _expectedKey.RulesetId, StringComparison.Ordinal)
                ? _projection
                : null;
        }
    }
}
