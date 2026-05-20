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
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
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
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.BuildKit, "street-sam-starter", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.BuildKit, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.CurrentValue == "workbench-first"
            && row.Notes is not null
            && row.Notes.Contains("emitted build receipt", StringComparison.Ordinal)
            && row.Notes.Contains("grounded campaign/profile runtime", StringComparison.Ordinal)
            && row.Notes.Contains("Next safe action:", StringComparison.Ordinal)
            && row.Notes.Contains("hand it into the selected workspace or campaign lane", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.RuntimeRequirements
            && row.Notes is not null
            && row.Notes.Contains("No extra runtime fingerprint or rule pack is pinned yet", StringComparison.Ordinal)
            && row.Notes.Contains("rule environment", StringComparison.Ordinal)
            && row.Notes.Contains("migration oracle", StringComparison.Ordinal)
            && row.Notes.Contains("No extra prompt resolution or grounded action staging is required", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes is not null
            && row.Notes.Contains("selected workspace or campaign lane", StringComparison.Ordinal)
            && row.Notes.Contains("grounded campaign/profile runtime", StringComparison.Ordinal)
            && row.Notes.Contains("rule environment", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes is not null
            && row.Notes.Contains("Support closure can cite", StringComparison.Ordinal)
            && row.Notes.Contains("rule environment", StringComparison.Ordinal)));
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsEmpty(matrix.Capabilities);
    }

    [TestMethod]
    public void Hub_project_compatibility_service_summarizes_buildkit_runtime_requirements_for_campaign_handoff()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub(
            [
                new BuildKitRegistryEntry(
                    new BuildKitManifest(
                        BuildKitId: "matrix-operator",
                        Version: "1.1.0",
                        Title: "Matrix Operator",
                        Description: "Decker planning lane.",
                        Targets: [RulesetDefaults.Sr5],
                        RuntimeRequirements:
                        [
                            new BuildKitRuntimeRequirement(
                                RulesetId: RulesetDefaults.Sr5,
                                RequiredRuntimeFingerprints: ["sha256:campaign-a"],
                                RequiredRulePacks: [new ArtifactVersionReference("official-errata", "1.2.0")])
                        ],
                        Prompts:
                        [
                            new BuildKitPromptDescriptor(
                                PromptId: "matrix-lane",
                                Kind: BuildKitPromptKinds.Choice,
                                Label: "Matrix lane",
                                Options: [new BuildKitPromptOption("stealth", "Stealth")],
                                Required: true)
                        ],
                        Actions:
                        [
                            new BuildKitActionDescriptor(
                                ActionId: "queue-specialty",
                                Kind: BuildKitActionKinds.QueueCareerUpdate,
                                TargetId: "career.matrix-operator")
                        ],
                        Visibility: ArtifactVisibilityModes.Public,
                        TrustTier: ArtifactTrustTiers.Curated),
                    Owner: new OwnerScope("system"),
                    Visibility: ArtifactVisibilityModes.Public,
                    PublicationStatus: BuildKitPublicationStatuses.Published,
                    UpdatedAtUtc: System.DateTimeOffset.UtcNow)
            ]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.BuildKit, "matrix-operator", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.RuntimeRequirements
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes is not null
            && row.Notes.Contains("Requires a compatible campaign/profile runtime and rule environment before handoff", StringComparison.Ordinal)
            && row.Notes.Contains("sha256:campaign-a", StringComparison.Ordinal)
            && row.Notes.Contains("official-errata@1.2.0", StringComparison.Ordinal)
            && row.Notes.Contains("migration oracle", StringComparison.Ordinal)
            && row.Notes.Contains("1 prompt(s) must be resolved and 1 grounded action(s) will be staged", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.CurrentValue == "workbench-first"
            && row.Notes is not null
            && row.Notes.Contains("compatible runtime and rule environment that match", StringComparison.Ordinal)
            && row.Notes.Contains("sha256:campaign-a", StringComparison.Ordinal)
            && row.Notes.Contains("emitted build receipt", StringComparison.Ordinal)
            && row.Notes.Contains("Next safe action:", StringComparison.Ordinal)
            && row.Notes.Contains("Resolve the build path in the workbench", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes is not null
            && row.Notes.Contains("selected workspace or campaign lane", StringComparison.Ordinal)
            && row.Notes.Contains("sha256:campaign-a", StringComparison.Ordinal)
            && row.Notes.Contains("migration oracle", StringComparison.Ordinal)));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes is not null
            && row.Notes.Contains("Support closure can reuse", StringComparison.Ordinal)
            && row.Notes.Contains("migration-oracle contract", StringComparison.Ordinal)
            && row.Notes.Contains("official-errata@1.2.0", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_includes_runtime_lock_install_state()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
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
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("workspace-1", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("runtime fingerprint sha256:core", StringComparison.Ordinal) == true));
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsTrue(matrix.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.DeriveStat
            && capability.InvocationKind == RulesetCapabilityInvocationKinds.Rule));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_marks_rule_profiles_for_review_when_runtime_rebind_is_required()
    {
        RuleProfileRegistryEntry profile = CreateRuleProfile("campaign-profile", ArtifactInstallStates.Installed, "sha256:required");
        RuntimeInspectorProjection projection = CreateRuntimeInspectorProjection(
            profile,
            diagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "runtime.lock.compatibility.install-runtime-drift")
            ],
            warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Migration,
                    Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                    Message: "runtime.inspector.warning.migration.rebind-required")
            ]);

        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([profile]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(projection),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RuleProfile, "campaign-profile", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes?.Contains("must be rebound", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes?.Contains("runtime review", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && !string.IsNullOrWhiteSpace(row.Notes)));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_blocks_rule_profiles_when_required_pack_is_missing()
    {
        RuleProfileRegistryEntry profile = CreateRuleProfile("campaign-profile", ArtifactInstallStates.Installed, "sha256:required");
        RuntimeInspectorProjection projection = CreateRuntimeInspectorProjection(
            profile,
            diagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.MissingPack,
                    Message: "runtime.lock.compatibility.missing-pack")
            ],
            warnings: []);

        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([profile]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(projection),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RuleProfile, "campaign-profile", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.Notes == "1 required rule pack(s) are missing from the grounded runtime."));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.Notes?.Contains("missing rule packs land on the grounded runtime again", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.Blocked
            && row.Notes?.Contains("0 selected rule pack(s)", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_projects_partial_npc_entry_prep_without_runtime_row()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(
                entries:
                [
                    new NpcEntryRegistryEntry(
                        new NpcEntryManifest(
                            EntryId: "entry-1",
                            Version: "1.0.0",
                            Title: "Dock Ambusher",
                            Description: "Prepared ambush lane.",
                            RulesetId: RulesetDefaults.Sr5,
                            ThreatTier: "high",
                            Faction: "black-lodge",
                            RuntimeFingerprint: null,
                            SessionReady: true,
                            GmBoardReady: false),
                        OwnerScope.LocalSingleUser,
                        NpcPublicationStatuses.Published,
                        DateTimeOffset.UtcNow)
                ]),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.NpcEntry, "entry-1", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.NpcEntry, matrix.Kind);
        Assert.IsFalse(matrix.Rows.Any(row => row.Kind == HubProjectCompatibilityRowKinds.RuntimeFingerprint));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.CurrentValue == "prep-review-required"
            && row.Notes?.Contains("without an explicit runtime fingerprint yet", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes?.Contains("both session-ready and GM-board-ready", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.ReviewRequired
            && row.Notes?.Contains("Dock Ambusher", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_projects_compatible_encounter_pack_role_summary()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(
                encounterPacks:
                [
                    new EncounterPackRegistryEntry(
                        new EncounterPackManifest(
                            EncounterPackId: "enc-1",
                            Version: "1.0.0",
                            Title: "Harbor Sweep",
                            Description: "Harbor sweep encounter.",
                            RulesetId: RulesetDefaults.Sr5,
                            Participants:
                            [
                                new EncounterPackParticipantReference("guard-a", 2, "overwatch"),
                                new EncounterPackParticipantReference("guard-b", 1, "striker")
                            ],
                            Visibility: ArtifactVisibilityModes.Public,
                            TrustTier: ArtifactTrustTiers.Curated,
                            SessionReady: true,
                            GmBoardReady: true),
                        OwnerScope.LocalSingleUser,
                        NpcPublicationStatuses.Published,
                        DateTimeOffset.UtcNow)
                ]),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.EncounterPack, "enc-1", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.EncounterPack, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.CurrentValue == "campaign-bindable"
            && row.Notes?.Contains("3 opposition seat(s)", StringComparison.Ordinal) == true
            && row.Notes?.Contains("2 explicit role lane(s)", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("governed role and quantity truth", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("2 explicit role lane(s)", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_handles_runtime_lock_without_plugin_or_install_target_id()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(
                new RuntimeLockRegistryEntry(
                    LockId: "sha256:orphan",
                    Owner: new OwnerScope("alice"),
                    Title: "Orphan Runtime",
                    Visibility: ArtifactVisibilityModes.Public,
                    CatalogKind: RuntimeLockCatalogKinds.Saved,
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: "shadowrun-x",
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [RulePackCapabilityIds.DeriveStat] = "missing-pack/derive-stat"
                        },
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:orphan"),
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Pinned,
                        InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                        InstalledTargetId: null,
                        RuntimeFingerprint: "sha256:orphan"))));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(new OwnerScope("alice"), HubCatalogItemKinds.RuntimeLock, "sha256:orphan", "shadowrun-x");

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.RuntimeLock, matrix.Kind);
        Assert.IsNotNull(matrix.Capabilities);
        Assert.IsEmpty(matrix.Capabilities);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.Trust
            && row.CurrentValue == ArtifactTrustTiers.Curated));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.InstallState
            && row.Notes is null));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.Notes?.Contains("the selected workspace", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.Capabilities
            && row.CurrentValue == "0"
            && row.NotesKey == "hub.project.compatibility.notes.capabilities.none"));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_rejects_unsupported_kind()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            service.GetMatrix(OwnerScope.LocalSingleUser, "unsupported-kind", "item-1", RulesetDefaults.Sr5));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_can_resolve_rulepack_without_explicit_ruleset_query()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub(
            [
                new RulePackRegistryEntry(
                    new RulePackManifest(
                        PackId: "sr6-pack",
                        Version: "1.0.0",
                        Title: "SR6 Capability Pack",
                        Author: "GM",
                        Description: "Session lane.",
                        Targets: [RulesetDefaults.Sr6],
                        EngineApiVersion: "rulepack-v1",
                        DependsOn: [],
                        ConflictsWith: [],
                        Visibility: ArtifactVisibilityModes.Public,
                        TrustTier: ArtifactTrustTiers.Curated,
                        Assets: [],
                        Capabilities:
                        [
                            new RulePackCapabilityDescriptor(
                                CapabilityId: RulePackCapabilityIds.DeriveStat,
                                AssetKind: RulePackAssetKinds.Lua,
                                AssetMode: RulePackAssetModes.AddProvider,
                                Explainable: false,
                                SessionSafe: false)
                        ],
                        ExecutionPolicies:
                        [
                            new RulePackExecutionPolicyHint(
                                Environment: RulePackExecutionEnvironments.SessionRuntimeBundle,
                                PolicyMode: RulePackExecutionPolicyModes.Allow,
                                MinimumTrustTier: ArtifactTrustTiers.LocalOnly,
                                AllowedAssetModes: []),
                            new RulePackExecutionPolicyHint(
                                Environment: RulePackExecutionEnvironments.HostedServer,
                                PolicyMode: RulePackExecutionPolicyModes.Allow,
                                MinimumTrustTier: ArtifactTrustTiers.Curated,
                                AllowedAssetModes: [])
                        ]),
                    new RulePackPublicationMetadata(
                        OwnerId: "local-single-user",
                        Visibility: ArtifactVisibilityModes.Public,
                        PublicationStatus: RulePackPublicationStatuses.Published,
                        Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                        Shares: []),
                    new ArtifactInstallState(ArtifactInstallStates.Installed))
            ]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RulePack, "sr6-pack");

        Assert.IsNotNull(matrix);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.Ruleset
            && row.CurrentValue == RulesetDefaults.Sr6));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.CurrentValueKey == "hub.project.compatibility.row.session-runtime.value.not-session-safe"));
    }

    [TestMethod]
    public void Hub_project_compatibility_service_projects_compatible_npc_pack_summary()
    {
        DefaultHubProjectCompatibilityService service = new(
            CreatePluginRegistry(),
            new RulePackRegistryServiceStub([]),
            new RuleProfileRegistryServiceStub([]),
            new BuildKitRegistryServiceStub([]),
            new NpcVaultRegistryServiceStub(
                packs:
                [
                    new NpcPackRegistryEntry(
                        new NpcPackManifest(
                            PackId: "dock-pack",
                            Version: "1.0.0",
                            Title: "Dock Sweep",
                            Description: "Prepared dock sweep.",
                            RulesetId: RulesetDefaults.Sr5,
                            Entries:
                            [
                                new NpcPackMemberReference("guard-a", 2),
                                new NpcPackMemberReference("guard-b", 1)
                            ],
                            Visibility: ArtifactVisibilityModes.Public,
                            TrustTier: ArtifactTrustTiers.Curated,
                            SessionReady: true,
                            GmBoardReady: true),
                        OwnerScope.LocalSingleUser,
                        NpcPublicationStatuses.Published,
                        DateTimeOffset.UtcNow)
                ]),
            new RuntimeInspectorServiceStub(null),
            new RuntimeLockRegistryServiceStub(null));

        HubProjectCompatibilityMatrix? matrix = service.GetMatrix(OwnerScope.LocalSingleUser, HubCatalogItemKinds.NpcPack, "dock-pack", RulesetDefaults.Sr5);

        Assert.IsNotNull(matrix);
        Assert.AreEqual(HubCatalogItemKinds.NpcPack, matrix.Kind);
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SessionRuntime
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.CurrentValue == "campaign-bindable"
            && row.Notes?.Contains("3 opposition seat(s)", StringComparison.Ordinal) == true
            && row.Notes?.Contains("2 entry type(s)", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.CampaignReturn
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("governed opposition roster", StringComparison.Ordinal) == true));
        Assert.IsTrue(matrix.Rows.Any(row =>
            row.Kind == HubProjectCompatibilityRowKinds.SupportClosure
            && row.State == HubProjectCompatibilityStates.Compatible
            && row.Notes?.Contains("3 prepared opposition seat(s)", StringComparison.Ordinal) == true));
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
            _entries.FirstOrDefault(entry =>
                entry.Manifest.PackId == packId
                && (rulesetId is null || entry.Manifest.Targets.Contains(rulesetId, StringComparer.Ordinal)));
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

    private sealed class NpcVaultRegistryServiceStub : INpcVaultRegistryService
    {
        private readonly IReadOnlyList<NpcEntryRegistryEntry> _entries;
        private readonly IReadOnlyList<NpcPackRegistryEntry> _packs;
        private readonly IReadOnlyList<EncounterPackRegistryEntry> _encounterPacks;

        public NpcVaultRegistryServiceStub(
            IReadOnlyList<NpcEntryRegistryEntry>? entries = null,
            IReadOnlyList<NpcPackRegistryEntry>? packs = null,
            IReadOnlyList<EncounterPackRegistryEntry>? encounterPacks = null)
        {
            _entries = entries ?? [];
            _packs = packs ?? [];
            _encounterPacks = encounterPacks ?? [];
        }

        public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null) => _entries;

        public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry => string.Equals(entry.Manifest.EntryId, entryId, StringComparison.Ordinal));

        public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null) => _packs;

        public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null) =>
            _packs.FirstOrDefault(pack => string.Equals(pack.Manifest.PackId, packId, StringComparison.Ordinal));

        public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null) => _encounterPacks;

        public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null) =>
            _encounterPacks.FirstOrDefault(pack => string.Equals(pack.Manifest.EncounterPackId, encounterPackId, StringComparison.Ordinal));
    }

    private sealed class RuntimeInspectorServiceStub : IRuntimeInspectorService
    {
        private readonly RuntimeInspectorProjection? _projection;

        public RuntimeInspectorServiceStub(RuntimeInspectorProjection? projection)
        {
            _projection = projection;
        }

        public RuntimeInspectorProjection? GetProfileProjection(OwnerScope owner, string profileId, string? rulesetId = null) => _projection;
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

    private static RuleProfileRegistryEntry CreateRuleProfile(string profileId, string installState, string runtimeFingerprint)
    {
        return new RuleProfileRegistryEntry(
            Manifest: new RuleProfileManifest(
                ProfileId: profileId,
                Title: "Campaign Runtime",
                Description: "Campaign profile",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.Campaign,
                CatalogKind: RuleProfileCatalogKinds.Personal,
                RulePacks: [],
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
                    RulesetId: RulesetDefaults.Sr5,
                    ContentBundles: [],
                    RulePacks: [],
                    ProviderBindings: new Dictionary<string, string>(),
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: runtimeFingerprint),
                UpdateChannel: RuleProfileUpdateChannels.CampaignPinned),
            Publication: new RuleProfilePublicationMetadata(
                OwnerId: "local-single-user",
                Visibility: ArtifactVisibilityModes.Private,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            Install: new ArtifactInstallState(
                State: installState,
                InstalledTargetKind: RuleProfileApplyTargetKinds.GlobalDefaults,
                InstalledTargetId: "desktop",
                RuntimeFingerprint: "sha256:installed"),
            SourceKind: RegistryEntrySourceKinds.PersistedManifest);
    }

    private static RuntimeInspectorProjection CreateRuntimeInspectorProjection(
        RuleProfileRegistryEntry profile,
        IReadOnlyList<RuntimeLockCompatibilityDiagnostic> diagnostics,
        IReadOnlyList<RuntimeInspectorWarning> warnings)
    {
        return new RuntimeInspectorProjection(
            TargetKind: RuntimeInspectorTargetKinds.RuntimeLock,
            TargetId: profile.Manifest.ProfileId,
            RuntimeLock: profile.Manifest.RuntimeLock,
            Install: profile.Install,
            ResolvedRulePacks: [],
            ProviderBindings: [],
            CompatibilityDiagnostics: diagnostics,
            Warnings: warnings,
            MigrationPreview: [],
            GeneratedAtUtc: DateTimeOffset.UtcNow);
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
