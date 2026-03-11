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
public class HubProjectCompatibilityServiceTests
{
    [TestMethod]
    public void Hub_project_compatibility_service_builds_rulepack_matrix()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
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
                        Capabilities:
                        [
                            new RulePackCapabilityDescriptor(
                                CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                                AssetKind: RulePackAssetKinds.Lua,
                                AssetMode: RulePackAssetModes.AddProvider,
                                Explainable: true,
                                SessionSafe: true)
                        ],
                        ExecutionPolicies:
                        [
                            new RulePackExecutionPolicyHint(
                                Environment: RulePackExecutionEnvironments.HostedServer,
                                PolicyMode: RulePackExecutionPolicyModes.ReviewRequired,
                                MinimumTrustTier: ArtifactTrustTiers.Curated,
                                AllowedAssetModes: [RulePackAssetModes.AddProvider])
                        ]),
                    new RulePackPublicationMetadata(
                        OwnerId: "local-single-user",
                        Visibility: ArtifactVisibilityModes.LocalOnly,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RulePack, "house-rules", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.RulePack, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row => row.Kind == HubProjectCompatibilityRowKinds.Capabilities && row.CurrentValue == "1"));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.LabelKey == "hub.project.compatibility.row.session-runtime.label"
            && row.CurrentValueKey == "hub.project.compatibility.row.session-runtime.value.session-safe"));
        Assert.IsTrue(matrix.Rows.Any(row => row.Kind == HubProjectCompatibilityRowKinds.HostedPublic && row.State == HubProjectCompatibilityStates.ReviewRequired));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.Capabilities
            && row.NotesKey == "hub.project.compatibility.notes.capabilities.summary"
            && row.NotesParameters is { Count: 2 }));
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsTrue(matrix.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.SessionQuickActions
            && capability.InvocationKind == RulesetCapabilityInvocationKinds.Script
            && capability.SessionSafe));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_marks_buildkits_as_workbench_only_for_session_runtime()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
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
            ]),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.BuildKit, "street-sam-starter", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.BuildKit, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.NotesKey == "hub.project.compatibility.notes.session-runtime.buildkit-blocked"));
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsEmpty(matrix.Capabilities);
    }

    [TestMethod]
    public void Hub_project_compatibility_service_includes_runtime_lock_install_state()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new RuntimeLockRegistryServiceStub(
                new RuntimeLockRegistryEntry(
                    LockId: "sha256:core",
                    Owner: new OwnerScope("alice"),
                    Title: "Alice Campaign Runtime",
                    Visibility: ArtifactVisibilityModes.Private,
                    CatalogKind: RuntimeLockCatalogKinds.Saved,
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
                        RuntimeFingerprint: "sha256:core"))));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(new OwnerScope("alice"), HubCatalogItemKinds.RuntimeLock, "sha256:core", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.RuntimeLock, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.InstallState
            && row.CurrentValue == ArtifactInstallStates.Pinned
            && row.Notes == "workspace-1"
            && row.CurrentValueKey == "hub.project.compatibility.row.install-state.value.pinned"));
        Assert.IsTrue(matrix.Rows.Any(row => row.Kind == HubProjectCompatibilityRowKinds.Capabilities && row.CurrentValue == "2"));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.NotesKey == "hub.project.compatibility.notes.session-runtime.resolved-rulepacks"
            && row.NotesParameters is { Count: 1 }));
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsTrue(matrix.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.DeriveStat
            && capability.InvocationKind == RulesetCapabilityInvocationKinds.Rule));
    }

    private static RulesetPluginRegistry CreatePluginRegistry() =>
        new(
        [
            new HubRulesetPluginStub(RulesetDefaults.Sr5),
            new HubRulesetPluginStub(RulesetDefaults.Sr6)
        ]);

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry => entry.Manifest.PackId == packId);
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly IReadOnlyList<RuleProfileRegistryEntry> _entries;

        public RuleProfileRegistryServiceStub(IReadOnlyList<RuleProfileRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry => entry.Manifest.ProfileId == profileId);
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
            _entries.FirstOrDefault(entry => entry.Manifest.BuildKitId == buildKitId);
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
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() =>
        [
            new RulesetCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                Title: "Derived Stat Evaluation",
                Explainable: true,
                SessionSafe: false,
                DefaultGasBudget: new RulesetGasBudget(2_000, 5_000, 4_194_304),
                MaximumGasBudget: new RulesetGasBudget(5_000, 10_000, 8_388_608)),
            new RulesetCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                InvocationKind: RulesetCapabilityInvocationKinds.Script,
                Title: "Session Quick Actions",
                Explainable: true,
                SessionSafe: true,
                DefaultGasBudget: new RulesetGasBudget(2_000, 5_000, 4_194_304),
                MaximumGasBudget: new RulesetGasBudget(5_000, 10_000, 8_388_608))
        ];
    }

    private sealed class ScriptHostStub : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetScriptExecutionResult(true, null, new Dictionary<string, object?>()));
    }
}
