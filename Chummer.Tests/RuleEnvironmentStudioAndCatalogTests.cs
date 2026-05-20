#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class RuleEnvironmentStudioAndCatalogTests
{
    [TestMethod]
    public void Default_rule_environment_studio_service_returns_null_when_preview_or_inspector_is_missing()
    {
        DefaultRuleEnvironmentStudioService service = new(
            new StubRuleProfileApplicationService(preview: null),
            new StubRuntimeInspectorService(projection: null),
            new DefaultRuntimeLockDiffService(),
            new StubRuntimeLockRegistryService([]));

        RuleEnvironmentStudioProjection? projection = service.GetProfileProjection(
            OwnerScope.LocalSingleUser,
            "profile-1",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Character, "char-1"),
            RulesetDefaults.Sr5);

        Assert.IsNull(projection);
    }

    [TestMethod]
    public void Default_rule_environment_studio_service_projects_first_pin_when_no_current_runtime_exists()
    {
        RuleProfilePreviewReceipt preview = CreatePreviewReceipt([]);
        RuntimeInspectorProjection inspector = CreateInspectorProjection(
            runtimeFingerprint: "sha256:desired",
            warnings: [],
            promotion: null);
        DefaultRuleEnvironmentStudioService service = new(
            new StubRuleProfileApplicationService(preview),
            new StubRuntimeInspectorService(inspector),
            new DefaultRuntimeLockDiffService(),
            new StubRuntimeLockRegistryService([]));

        RuleEnvironmentStudioProjection? projection = service.GetProfileProjection(
            OwnerScope.LocalSingleUser,
            "profile-1",
            preview.Target,
            RulesetDefaults.Sr5);

        Assert.IsNotNull(projection);
        Assert.AreEqual(RuleEnvironmentStudioDiffStatuses.FirstPin, projection.Diff.Status);
        Assert.AreEqual("ruleenvironment.studio.diff.first-pin", projection.Diff.SummaryKey);
        Assert.IsNull(projection.CurrentRuntime);
        Assert.AreEqual(RuntimeInspectorPromotionStages.Sandbox, projection.Lifecycle.CurrentStage);
        Assert.AreEqual(RuleProfileUpdateChannels.Preview, projection.Lifecycle.UpdateChannel);
        Assert.IsFalse(projection.ExplainReceipt.DeltaIncluded);
        CollectionAssert.AreEqual(
            new[]
            {
                ExplainValuePacketCoverageKinds.MechanicalResult,
                ExplainValuePacketCoverageKinds.SourceAnchor
            },
            projection.ExplainReceipt.RequiredCoverageKinds.ToArray());
    }

    [TestMethod]
    public void Default_rule_environment_studio_service_projects_clear_and_requires_review_diff_states()
    {
        RuleProfilePreviewReceipt preview = CreatePreviewReceipt(
            warnings:
            [
                new RuntimeInspectorWarning(RuntimeInspectorWarningKinds.Trust, RuntimeInspectorWarningSeverityLevels.Warning, "warn")
            ]);
        RuntimeInspectorProjection desiredInspector = CreateInspectorProjection(
            runtimeFingerprint: "sha256:desired",
            warnings:
            [
                new RuntimeInspectorWarning(RuntimeInspectorWarningKinds.ProviderBinding, RuntimeInspectorWarningSeverityLevels.Warning, "warn")
            ],
            promotion: new RuntimeInspectorPromotionProjection(
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Visibility: ArtifactVisibilityModes.Public,
                UpdateChannel: RuleProfileUpdateChannels.Stable,
                PromotionSummary: "Promoted",
                RollbackSummary: "Rollback",
                LineageSummary: "Lineage",
                PublishedAtUtc: new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero),
                CurrentStage: RuntimeInspectorPromotionStages.Published,
                PromotionTargetStage: RuntimeInspectorPromotionStages.Published));

        RuntimeLockRegistryEntry currentSame = CreateRuntimeEntry("lock-same", CreateRuntimeLock("sha256:desired", RulesetDefaults.Sr5));
        DefaultRuleEnvironmentStudioService clearService = new(
            new StubRuleProfileApplicationService(preview),
            new StubRuntimeInspectorService(desiredInspector),
            new DefaultRuntimeLockDiffService(),
            new StubRuntimeLockRegistryService([currentSame]));

        RuleEnvironmentStudioProjection? clearProjection = clearService.GetProfileProjection(
            OwnerScope.LocalSingleUser,
            "profile-1",
            preview.Target,
            RulesetDefaults.Sr5);

        Assert.IsNotNull(clearProjection);
        Assert.AreEqual(RuleEnvironmentStudioDiffStatuses.Clear, clearProjection.Diff.Status);
        Assert.AreEqual("ruleenvironment.studio.diff.clear", clearProjection.Diff.SummaryKey);
        Assert.IsNotNull(clearProjection.Diff.Delta);
        Assert.AreEqual(0, clearProjection.Diff.Delta.Changes.Count);
        Assert.AreEqual(RuleProfileUpdateChannels.Stable, clearProjection.Lifecycle.UpdateChannel);
        Assert.AreEqual(RuntimeInspectorPromotionStages.Published, clearProjection.Lifecycle.CurrentStage);
        Assert.IsFalse(clearProjection.ExplainReceipt.DeltaIncluded);
        Assert.IsTrue(clearProjection.ExplainReceipt.RequiredCoverageKinds.Contains(ExplainValuePacketCoverageKinds.Warning));

        RuntimeLockRegistryEntry currentDifferent = CreateRuntimeEntry("lock-old", CreateRuntimeLock("sha256:current", RulesetDefaults.Sr4));
        DefaultRuleEnvironmentStudioService reviewService = new(
            new StubRuleProfileApplicationService(preview),
            new StubRuntimeInspectorService(desiredInspector),
            new DefaultRuntimeLockDiffService(),
            new StubRuntimeLockRegistryService([currentDifferent]));

        RuleEnvironmentStudioProjection? reviewProjection = reviewService.GetProfileProjection(
            OwnerScope.LocalSingleUser,
            "profile-1",
            preview.Target,
            RulesetDefaults.Sr5);

        Assert.IsNotNull(reviewProjection);
        Assert.AreEqual(RuleEnvironmentStudioDiffStatuses.RequiresReview, reviewProjection.Diff.Status);
        Assert.AreEqual("ruleenvironment.studio.diff.requires-review", reviewProjection.Diff.SummaryKey);
        Assert.IsTrue((reviewProjection.Diff.Delta?.Changes.Count ?? 0) > 0);
        Assert.IsTrue(reviewProjection.ExplainReceipt.DeltaIncluded);
        Assert.IsTrue(reviewProjection.ExplainReceipt.RequiredCoverageKinds.Contains(ExplainValuePacketCoverageKinds.BeforeAfterDelta));
    }

    [TestMethod]
    public void Workspace_surface_action_catalog_filters_by_ruleset_and_falls_back_to_tab_info()
    {
        IReadOnlyList<WorkspaceSurfaceActionDefinition> sr5Actions = WorkspaceSurfaceActionCatalog.ForRuleset(null);
        IReadOnlyList<WorkspaceSurfaceActionDefinition> gearActions = WorkspaceSurfaceActionCatalog.ForTab("tab-gear", RulesetDefaults.Sr5);
        IReadOnlyList<WorkspaceSurfaceActionDefinition> fallbackActions = WorkspaceSurfaceActionCatalog.ForTab("missing-tab", RulesetDefaults.Sr5);

        Assert.IsTrue(sr5Actions.Count > 0);
        Assert.IsTrue(sr5Actions.All(action => action.RulesetId == RulesetDefaults.Sr5));
        Assert.IsTrue(gearActions.Count > 0);
        Assert.IsTrue(gearActions.All(action => action.TabId == "tab-gear"));
        Assert.IsTrue(fallbackActions.Count > 0);
        Assert.IsTrue(fallbackActions.All(action => action.TabId == "tab-info"));
        Assert.HasCount(0, WorkspaceSurfaceActionCatalog.ForRuleset("sr6"));
    }

    [TestMethod]
    public void App_command_catalog_filters_by_ruleset_with_sr5_compatibility_default()
    {
        IReadOnlyList<AppCommandDefinition> defaultCommands = AppCommandCatalog.ForRuleset(null);
        IReadOnlyList<AppCommandDefinition> sr5Commands = AppCommandCatalog.ForRuleset(RulesetDefaults.Sr5);

        Assert.IsTrue(defaultCommands.Count > 0);
        CollectionAssert.AreEqual(
            defaultCommands.Select(command => command.Id).ToArray(),
            sr5Commands.Select(command => command.Id).ToArray());
        Assert.IsTrue(defaultCommands.Any(command => command.Id == "new_character"));
        Assert.IsTrue(defaultCommands.Any(command => command.Id == "print_character"));
        Assert.HasCount(0, AppCommandCatalog.ForRuleset("sr6"));
    }

    private static RuleProfilePreviewReceipt CreatePreviewReceipt(IReadOnlyList<RuntimeInspectorWarning> warnings)
        => new(
            ProfileId: "profile-1",
            Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Character, "char-1"),
            RuntimeLock: CreateRuntimeLock("sha256:desired", RulesetDefaults.Sr5),
            Changes: [],
            Warnings: warnings,
            RequiresConfirmation: warnings.Count > 0);

    private static RuntimeInspectorProjection CreateInspectorProjection(
        string runtimeFingerprint,
        IReadOnlyList<RuntimeInspectorWarning> warnings,
        RuntimeInspectorPromotionProjection? promotion)
        => new(
            TargetKind: RuleProfileApplyTargetKinds.Character,
            TargetId: "char-1",
            RuntimeLock: CreateRuntimeLock(runtimeFingerprint, RulesetDefaults.Sr5),
            Install: new ArtifactInstallState(
                ArtifactInstallStates.Installed,
                InstalledAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
                InstalledTargetKind: RuleProfileApplyTargetKinds.Character,
                InstalledTargetId: "char-1",
                RuntimeFingerprint: runtimeFingerprint),
            ResolvedRulePacks: [],
            ProviderBindings: [],
            CompatibilityDiagnostics: [],
            Warnings: warnings,
            MigrationPreview: [],
            GeneratedAtUtc: new DateTimeOffset(2026, 5, 20, 11, 0, 0, TimeSpan.Zero),
            Promotion: promotion);

    private static RuntimeLockRegistryEntry CreateRuntimeEntry(string lockId, ResolvedRuntimeLock runtimeLock)
        => new(
            LockId: lockId,
            Owner: OwnerScope.LocalSingleUser,
            Title: lockId,
            Visibility: ArtifactVisibilityModes.LocalOnly,
            CatalogKind: RuntimeLockCatalogKinds.Saved,
            RuntimeLock: runtimeLock,
            UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 9, 0, 0, TimeSpan.Zero),
            Install: new ArtifactInstallState(
                ArtifactInstallStates.Installed,
                InstalledAtUtc: new DateTimeOffset(2026, 5, 20, 9, 30, 0, TimeSpan.Zero),
                InstalledTargetKind: RuleProfileApplyTargetKinds.Character,
                InstalledTargetId: "char-1",
                RuntimeFingerprint: runtimeLock.RuntimeFingerprint));

    private static ResolvedRuntimeLock CreateRuntimeLock(string runtimeFingerprint, string rulesetId)
        => new(
            RulesetId: rulesetId,
            ContentBundles:
            [
                new ContentBundleDescriptor("bundle-core", rulesetId, "1.0.0", "Core", "Core bundle", ["core.xml"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference("pack-core", "1.0.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RulePackCapabilityIds.DeriveStat] = $"provider:{rulesetId}"
            },
            EngineApiVersion: "1.0.0",
            RuntimeFingerprint: runtimeFingerprint);

    private sealed class StubRuleProfileApplicationService : IRuleProfileApplicationService
    {
        private readonly RuleProfilePreviewReceipt? _preview;

        public StubRuleProfileApplicationService(RuleProfilePreviewReceipt? preview)
        {
            _preview = preview;
        }

        public RuleProfilePreviewReceipt? Preview(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null)
            => _preview;

        public RuleProfileApplyReceipt? Apply(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null)
            => null;
    }

    private sealed class StubRuntimeInspectorService : IRuntimeInspectorService
    {
        private readonly RuntimeInspectorProjection? _projection;

        public StubRuntimeInspectorService(RuntimeInspectorProjection? projection)
        {
            _projection = projection;
        }

        public RuntimeInspectorProjection? GetProfileProjection(OwnerScope owner, string profileId, string? rulesetId = null)
            => _projection;
    }

    private sealed class StubRuntimeLockRegistryService : IRuntimeLockRegistryService
    {
        private readonly IReadOnlyList<RuntimeLockRegistryEntry> _entries;

        public StubRuntimeLockRegistryService(IReadOnlyList<RuntimeLockRegistryEntry> entries)
        {
            _entries = entries;
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null)
            => new(_entries, _entries.Count);

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
            => _entries.FirstOrDefault(entry => entry.LockId == lockId);

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)
            => throw new NotSupportedException();
    }
}
