#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Content;
using Chummer.Application.Hub;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class HubInstallPreviewServiceTests
{
    [TestMethod]
    public void Hub_install_preview_service_maps_ruleprofile_preview_receipts()
    {
        DefaultHubInstallPreviewService service = new(
            CreatePluginRegistry(),
            new RulePackInstallServiceStub(null),
            new RuleProfileRegistryServiceStub(
                new RuleProfileRegistryEntry(
                    new RuleProfileManifest(
                        ProfileId: "official.sr5.core",
                        Title: "Official SR5 Core",
                        Description: "Curated runtime.",
                        RulesetId: RulesetDefaults.Sr5,
                        Audience: RuleProfileAudienceKinds.General,
                        CatalogKind: RuleProfileCatalogKinds.Official,
                        RulePacks: [],
                        DefaultToggles: [],
                        RuntimeLock: new ResolvedRuntimeLock(
                            RulesetId: RulesetDefaults.Sr5,
                            ContentBundles: [],
                            RulePacks: [],
                            ProviderBindings: new Dictionary<string, string>(),
                            EngineApiVersion: "rulepack-v1",
                            RuntimeFingerprint: "sha256:core"),
                        UpdateChannel: RuleProfileUpdateChannels.Stable),
                    new RuleProfilePublicationMetadata(
                        OwnerId: "system",
                        Visibility: ArtifactVisibilityModes.Public,
                        PublicationStatus: RuleProfilePublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Pinned, InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace, InstalledTargetId: "workspace-1"))),
            new RuleProfileApplicationServiceStub(
                new RuleProfilePreviewReceipt(
                    ProfileId: "official.sr5.core",
                    Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:core"),
                    Changes:
                    [
                        new RuleProfilePreviewItem(
                            Kind: RuleProfilePreviewChangeKinds.RuntimeLockPinned,
                            Summary: "Pin runtime lock.",
                            SubjectId: "sha256:core")
                    ],
                    Warnings:
                    [
                        new RuntimeInspectorWarning(
                            Kind: RuntimeInspectorWarningKinds.Trust,
                            Severity: RuntimeInspectorWarningSeverityLevels.Info,
                            Message: "Local-only profile.",
                            SubjectId: "official.sr5.core")
                    ])),
            new RuntimeLockInstallServiceStub(null),
            new RuntimeLockRegistryServiceStub(null),
            new RulePackRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]));

        HubProjectInstallPreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.RuleProfile,
            "official.sr5.core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual(HubProjectInstallPreviewStates.Ready, preview.State);
        Assert.AreEqual("sha256:core", preview.RuntimeFingerprint);
        Assert.AreEqual(HubProjectInstallPreviewChangeKinds.RuntimeLockPinned, preview.Changes[0].Kind);
        Assert.AreEqual(RuntimeInspectorWarningKinds.Trust, preview.Diagnostics[0].Kind);
        Assert.IsTrue(preview.Diagnostics.Any(diagnostic => diagnostic.Kind == HubProjectInstallPreviewDiagnosticKinds.InstallState));
        Assert.IsTrue(preview.RequiresConfirmation);
    }

    [TestMethod]
    public void Hub_install_preview_service_builds_runtime_lock_preview_receipts()
    {
        DefaultHubInstallPreviewService service = new(
            CreatePluginRegistry(),
            new RulePackInstallServiceStub(null),
            new RuleProfileRegistryServiceStub(null),
            new RuleProfileApplicationServiceStub(null),
            new RuntimeLockInstallServiceStub(
                new RuntimeLockInstallPreviewReceipt(
                    LockId: "sha256:core",
                    Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:core"),
                    Changes:
                    [
                        new RuntimeLockInstallPreviewItem(
                            Kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
                            Summary: "Pin runtime lock.",
                            SubjectId: "sha256:core")
                    ],
                    Warnings:
                    [
                        new RuntimeInspectorWarning(
                            Kind: RuntimeInspectorWarningKinds.ProviderBinding,
                            Severity: RuntimeInspectorWarningSeverityLevels.Info,
                            Message: "Built-in only runtime lock.",
                            SubjectId: "sha256:core")
                    ])),
            new RuntimeLockRegistryServiceStub(
                new RuntimeLockRegistryEntry(
                    LockId: "sha256:core",
                    Owner: new OwnerScope("system"),
                    Title: "Official SR5 Core Runtime Lock",
                    Visibility: ArtifactVisibilityModes.Public,
                    CatalogKind: RuntimeLockCatalogKinds.Published,
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:core"),
                    UpdatedAtUtc: System.DateTimeOffset.UtcNow,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Pinned,
                        InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"))),
            new RulePackRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]));

        HubProjectInstallPreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.RuntimeLock,
            "sha256:core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual(HubProjectInstallPreviewStates.Ready, preview.State);
        Assert.AreEqual("sha256:core", preview.RuntimeFingerprint);
        Assert.IsNotEmpty(preview.Changes);
        Assert.AreEqual(HubProjectInstallPreviewChangeKinds.RuntimeLockPinned, preview.Changes[0].Kind);
        Assert.IsTrue(preview.Diagnostics.Any(diagnostic => diagnostic.Kind == HubProjectInstallPreviewDiagnosticKinds.InstallState));
        Assert.IsTrue(preview.RequiresConfirmation);
    }

    [TestMethod]
    public void Hub_install_preview_service_returns_deferred_receipts_for_buildkits_until_apply_preview_exists()
    {
        DefaultHubInstallPreviewService service = new(
            CreatePluginRegistry(),
            new RulePackInstallServiceStub(null),
            new RuleProfileRegistryServiceStub(null),
            new RuleProfileApplicationServiceStub(null),
            new RuntimeLockInstallServiceStub(null),
            new RuntimeLockRegistryServiceStub(null),
            new RulePackRegistryServiceStub([]),
            new BuildKitRegistryServiceStub(
            [
                new BuildKitRegistryEntry(
                    new BuildKitManifest(
                        BuildKitId: "street-sam-starter",
                        Version: "1.0.0",
                        Title: "Street Sam Starter",
                        Description: "Starter template.",
                        Targets: [RulesetDefaults.Sr5],
                        RuntimeRequirements: [],
                        Prompts: [],
                        Actions: [],
                        Visibility: ArtifactVisibilityModes.Public,
                        TrustTier: ArtifactTrustTiers.Curated),
                    Owner: new OwnerScope("system"),
                    Visibility: ArtifactVisibilityModes.Public,
                    PublicationStatus: BuildKitPublicationStatuses.Published,
                    UpdatedAtUtc: System.DateTimeOffset.UtcNow)
            ]));

        HubProjectInstallPreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            HubCatalogItemKinds.BuildKit,
            "street-sam-starter",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual(HubProjectInstallPreviewStates.Deferred, preview.State);
        Assert.AreEqual("hub_buildkit_apply_preview_not_implemented", preview.DeferredReason);
        Assert.AreEqual(HubProjectInstallPreviewChangeKinds.InstallDeferred, preview.Changes[0].Kind);
    }

    [TestMethod]
    public void Hub_install_preview_service_maps_rulepack_preview_receipts_and_install_state()
    {
        DefaultHubInstallPreviewService service = new(
            CreatePluginRegistry(),
            new RulePackInstallServiceStub(
                new RulePackInstallPreviewReceipt(
                    PackId: "house-rules",
                    RulesetId: RulesetDefaults.Sr5,
                    Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
                    Changes:
                    [
                        new RulePackInstallPreviewItem(
                            Kind: RulePackInstallPreviewChangeKinds.InstallStateChanged,
                            Summary: "Install rulepack.",
                            SubjectId: "house-rules"),
                        new RulePackInstallPreviewItem(
                            Kind: RulePackInstallPreviewChangeKinds.RuntimeReviewRequired,
                            Summary: "Review runtime capability bindings.",
                            SubjectId: "house-rules",
                            RequiresConfirmation: true)
                    ],
                    Warnings:
                    [
                        new RuntimeInspectorWarning(
                            Kind: RuntimeInspectorWarningKinds.Trust,
                            Severity: RuntimeInspectorWarningSeverityLevels.Info,
                            Message: "Local-only pack.",
                            SubjectId: "house-rules")
                    ],
                    RequiresConfirmation: true)),
            new RuleProfileRegistryServiceStub(null),
            new RuleProfileApplicationServiceStub(null),
            new RuntimeLockInstallServiceStub(null),
            new RuntimeLockRegistryServiceStub(null),
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "house-rules",
                        Version: "overlay-v1",
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
                    new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1"))
            ]),
            new BuildKitRegistryServiceStub([]));

        HubProjectInstallPreviewReceipt? preview = service.Preview(
            new OwnerScope("alice"),
            HubCatalogItemKinds.RulePack,
            "house-rules",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual(HubProjectInstallPreviewStates.Ready, preview.State);
        Assert.AreEqual(HubProjectInstallPreviewChangeKinds.InstallStateChanged, preview.Changes[0].Kind);
        Assert.AreEqual(RuntimeInspectorWarningKinds.Trust, preview.Diagnostics[0].Kind);
        Assert.IsTrue(preview.Diagnostics.Any(diagnostic => diagnostic.Kind == HubProjectInstallPreviewDiagnosticKinds.InstallState));
        Assert.IsTrue(preview.RequiresConfirmation);
    }

    private static RulesetPluginRegistry CreatePluginRegistry() =>
        new(
        [
            new HubRulesetPluginStub(RulesetDefaults.Sr5),
            new HubRulesetPluginStub(RulesetDefaults.Sr6)
        ]);

    private sealed class RuleProfileApplicationServiceStub : IRuleProfileApplicationService
    {
        private readonly RuleProfilePreviewReceipt? _preview;

        public RuleProfileApplicationServiceStub(RuleProfilePreviewReceipt? preview)
        {
            _preview = preview;
        }

        public RuleProfilePreviewReceipt? Preview(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null) => _preview;

        public RuleProfileApplyReceipt? Apply(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null) => null;
    }

    private sealed class RulePackInstallServiceStub : IRulePackInstallService
    {
        private readonly RulePackInstallPreviewReceipt? _preview;

        public RulePackInstallServiceStub(RulePackInstallPreviewReceipt? preview)
        {
            _preview = preview;
        }

        public RulePackInstallPreviewReceipt? Preview(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null) => _preview;

        public RulePackInstallReceipt? Apply(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null) => null;
    }

    private sealed class RuntimeLockInstallServiceStub : IRuntimeLockInstallService
    {
        private readonly RuntimeLockInstallPreviewReceipt? _preview;

        public RuntimeLockInstallServiceStub(RuntimeLockInstallPreviewReceipt? preview)
        {
            _preview = preview;
        }

        public RuntimeLockInstallPreviewReceipt? Preview(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null) => _preview;

        public RuntimeLockInstallReceipt? Apply(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null) => null;
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly RuleProfileRegistryEntry? _entry;

        public RuleProfileRegistryServiceStub(RuleProfileRegistryEntry? entry)
        {
            _entry = entry;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entry is null ? [] : [_entry];

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null) => _entry;
    }

    private sealed class RuntimeLockRegistryServiceStub : IRuntimeLockRegistryService
    {
        private readonly RuntimeLockRegistryEntry? _entry;

        public RuntimeLockRegistryServiceStub(RuntimeLockRegistryEntry? entry)
        {
            _entry = entry;
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null) =>
            _entry is null ? new RuntimeLockRegistryPage([], 0) : new RuntimeLockRegistryPage([_entry], 1);

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null) => _entry;

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request) => throw new NotSupportedException();
    }

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null) =>
            _entries.Count == 0 ? null : _entries[0];
    }

    private sealed class BuildKitRegistryServiceStub : IBuildKitRegistryService
    {
        private readonly IReadOnlyList<BuildKitRegistryEntry> _entries;

        public BuildKitRegistryServiceStub(IReadOnlyList<BuildKitRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null) =>
            _entries.Count == 0 ? null : _entries[0];
    }

    private sealed class HubRulesetPluginStub : IRulesetPlugin
    {
        public HubRulesetPluginStub(string rulesetId)
        {
            Id = new RulesetId(rulesetId);
            DisplayName = rulesetId;
            Serializer = new RulesetSerializerStub(Id);
            ShellDefinitions = new ShellDefinitionProviderStub();
            Catalogs = new CatalogProviderStub();
            CapabilityDescriptors = new CapabilityDescriptorProviderStub();
            Capabilities = new CapabilityHostStub();
            Rules = new RuleHostStub();
            Scripts = new ScriptHostStub();
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

    private sealed class RulesetSerializerStub : IRulesetSerializer
    {
        public RulesetSerializerStub(RulesetId rulesetId)
        {
            RulesetId = rulesetId;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload) => new(RulesetId.NormalizedValue, SchemaVersion, payloadKind, payload);
    }

    private sealed class ShellDefinitionProviderStub : IRulesetShellDefinitionProvider
    {
        public IReadOnlyList<AppCommandDefinition> GetCommands() => [];

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => [];
    }

    private sealed class CatalogProviderStub : IRulesetCatalogProvider
    {
        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => [];
    }

    private sealed class RuleHostStub : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetRuleEvaluationResult(true, new Dictionary<string, object?>(), []));
    }

    private sealed class CapabilityHostStub : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                true,
                new RulesetCapabilityValue(RulesetCapabilityValueKinds.Object, Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)),
                []));
    }

    private sealed class CapabilityDescriptorProviderStub : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => [];
    }

    private sealed class ScriptHostStub : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetScriptExecutionResult(true, null, new Dictionary<string, object?>()));
    }
}
