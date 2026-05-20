#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiExplainServiceTests
{
    [TestMethod]
    public void Default_ai_explain_service_projects_capability_backed_explain_details()
    {
        DefaultAiExplainService service = new(
            new StubAiDigestService(),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                RuntimeFingerprint: "sha256:coach",
                CharacterId: "char-7",
                CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                RulesetId: "sr5"));

        Assert.IsNotNull(projection);
        Assert.AreEqual(RulePackCapabilityIds.SessionQuickActions, projection.CapabilityId);
        Assert.AreEqual("sha256:coach", projection.RuntimeFingerprint);
        Assert.AreEqual("sr5", projection.RulesetId);
        Assert.AreEqual(AiExplainEntryKinds.QuickActionAvailability, projection.Kind);
        Assert.AreEqual($"ruleset.capability.{RulePackCapabilityIds.SessionQuickActions}.title", projection.TitleKey);
        Assert.AreEqual(RulesetCapabilityInvocationKinds.Script, projection.InvocationKind);
        Assert.IsTrue(projection.Explainable);
        Assert.IsTrue(projection.SessionSafe);
        Assert.IsGreaterThanOrEqualTo(4, projection.Fragments?.Count ?? 0);
        Assert.IsGreaterThanOrEqualTo(1, projection.Diagnostics?.Count ?? 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(projection.SummaryKey));
    }

    [TestMethod]
    public void Default_ai_explain_service_returns_null_for_missing_runtime_or_capability()
    {
        DefaultAiExplainService service = new(
            new StubAiDigestService(runtimeSummary: null, characterDigest: null),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        Assert.IsNull(service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:missing", CapabilityId: RulePackCapabilityIds.SessionQuickActions)));

        DefaultAiExplainService missingCapabilityService = new(
            new StubAiDigestService(),
            new StubRulesetPluginRegistry(new Sr5RulesetPlugin()));

        Assert.IsNull(missingCapabilityService.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:coach", CapabilityId: "missing.capability")));

        DefaultAiExplainService missingPluginService = new(
            new StubAiDigestService(),
            new StubRulesetPluginRegistry());

        Assert.IsNull(missingPluginService.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:coach", CapabilityId: RulePackCapabilityIds.DeriveStat)));

        Assert.IsNull(missingCapabilityService.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(RuntimeFingerprint: "sha256:coach")));
    }

    [TestMethod]
    public void Default_ai_explain_service_builds_fallback_trace_when_provider_invocation_has_no_explain_trace()
    {
        StubRulesetCapabilityHost capabilityHost = new(
            new RulesetCapabilityInvocationResult(
                Success: true,
                Output: RulesetCapabilityBridge.FromObject(7m),
                Diagnostics:
                [
                    new RulesetCapabilityDiagnostic(
                        Code: "warn.unbounded",
                        Message: "Unbounded",
                        Severity: RulesetCapabilityDiagnosticSeverities.Warning)
                ]));
        StubRulesetPlugin plugin = new(
            "srx",
            [
                new RulesetCapabilityDescriptor(
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                    Title: "Derived Stat",
                    Explainable: true,
                    SessionSafe: false,
                    DefaultGasBudget: new RulesetGasBudget(32, 16, 4096))
            ],
            capabilityHost);
        StubAiDigestService digestService = new(
            runtimeSummary: new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:coach",
                RulesetId: "srx",
                Title: "Custom Runtime",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "1.2.3",
                ContentBundles: ["core.bundle@1.0.0"],
                RulePacks: ["pack.alpha@1.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RulePackCapabilityIds.DeriveStat] = "pack.alpha/provider.derive-stat"
                }),
            sessionDigest: new AiSessionDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher",
                RulesetId: "srx",
                RuntimeFingerprint: "sha256:coach",
                SelectionState: "selected",
                SessionReady: true,
                BundleFreshness: "fresh",
                ProfileId: "profile-7",
                ProfileTitle: "Street Ops"));
        DefaultAiExplainService service = new(
            digestService,
            new StubRulesetPluginRegistry(plugin));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                CharacterId: "char-7",
                ExplainEntryId: "armor.score",
                CapabilityId: RulePackCapabilityIds.DeriveStat));

        Assert.IsNotNull(projection);
        Assert.AreEqual("armor.score", projection.ExplainEntryId);
        Assert.AreEqual(AiExplainEntryKinds.DerivedValue, projection.Kind);
        Assert.AreEqual("pack.alpha/provider.derive-stat", projection.ProviderId);
        Assert.AreEqual("pack.alpha", projection.PackId);
        Assert.AreEqual("ruleset.explain.summary.diagnostic", projection.SummaryKey);
        Assert.IsTrue(projection.SummaryParameters.Any(parameter => parameter.Name == "code"));
        Assert.IsTrue(projection.Fragments!.Any(fragment => fragment.Kind == AiExplainFragmentKinds.Output));
        Assert.IsTrue(projection.Fragments.Any(fragment => fragment.Key == "ruleset.explain.fragment.trace.missing"));
        Assert.IsTrue(projection.Trace!.Any(step => step.StepId == "binding:0"));
        Assert.IsTrue(projection.Trace.Any(step => step.StepId == "trace:missing"));
        Assert.IsTrue(projection.Trace.Any(step => step.StepId == "diagnostic:0"));
        Assert.IsTrue(projection.Evidence!.Any(pointer => pointer.Kind == RulesetEvidencePointerKinds.RuleProfile && pointer.Pointer == "profile-7"));
        Assert.IsNotNull(projection.Provenance);
        Assert.AreEqual("profile-7", projection.Provenance!.ProfileId);
        Assert.IsNotNull(capabilityHost.LastRequest);
        Assert.AreEqual(AiExplainApiOperations.ExplainValue, capabilityHost.LastRequest!.Source);
        Assert.IsTrue(capabilityHost.LastRequest.Options?.Explain == true);
        Assert.AreEqual("pack.alpha/provider.derive-stat", capabilityHost.LastRequest.ProviderId);
        Assert.IsTrue(capabilityHost.LastRequest.Arguments.Any(argument => argument.Name == "characterId"));
        Assert.IsTrue(capabilityHost.LastRequest.Arguments.Any(argument => argument.Name == "characterName"));
        Assert.IsTrue(capabilityHost.LastRequest.Arguments.Any(argument => argument.Name == "karma"));
        Assert.IsTrue(capabilityHost.LastRequest.Arguments.Any(argument => argument.Name == "explainEntryId"));
    }

    [TestMethod]
    public void Default_ai_explain_service_uses_explain_trace_provider_and_pack_when_binding_is_missing()
    {
        RulesetEvidencePointer duplicateRuleEvidence = new(
            Kind: RulesetEvidencePointerKinds.RuleReference,
            Pointer: "rule:quick-action",
            LabelKey: "ruleset.explain.evidence.rule",
            LabelParameters: [new RulesetExplainParameter("ruleId", RulesetCapabilityBridge.FromObject("rule:quick-action"))]);
        StubRulesetCapabilityHost capabilityHost = new(
            new RulesetCapabilityInvocationResult(
                Success: true,
                Output: RulesetCapabilityBridge.FromObject(new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["available"] = true
                }),
                Diagnostics: [],
                Explain: new RulesetExplainTrace(
                    TargetKey: "entry.target",
                    FinalValue: RulesetCapabilityBridge.FromObject(true),
                    SummaryKey: "ruleset.explain.summary.ready",
                    SummaryParameters: [new RulesetExplainParameter("lane", RulesetCapabilityBridge.FromObject("quick-action"))],
                    Providers:
                    [
                        new RulesetProviderTrace(
                            ProviderId: "pack.beta/provider.quick",
                            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                            PackId: "pack.beta",
                            Success: true,
                            Steps:
                            [
                                new RulesetTraceStep(
                                    ProviderId: "pack.beta/provider.quick",
                                    CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                                    PackId: "pack.beta",
                                    ExplanationKey: "ruleset.explain.trace.quick-action",
                                    ExplanationParameters: [],
                                    Category: "provider",
                                    Evidence: [duplicateRuleEvidence])
                            ],
                            GasUsage: new RulesetGasUsage(4, 7, 1024),
                            Evidence: [duplicateRuleEvidence])
                    ],
                    AggregateGasUsage: new RulesetGasUsage(4, 7, 1024),
                    RuntimeFingerprint: "sha256:coach",
                    ProfileId: "profile-from-explain",
                    Evidence: [duplicateRuleEvidence])));
        StubRulesetPlugin plugin = new(
            "srx",
            [
                new RulesetCapabilityDescriptor(
                    CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                    InvocationKind: RulesetCapabilityInvocationKinds.Script,
                    Title: "Quick Actions",
                    Explainable: false,
                    SessionSafe: true,
                    DefaultGasBudget: new RulesetGasBudget(12, 8, 2048))
            ],
            capabilityHost);
        StubAiDigestService digestService = new(
            runtimeSummary: new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:coach",
                RulesetId: "srx",
                Title: "Custom Runtime",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "2.0.0",
                ContentBundles: ["core.bundle@1.0.0"],
                RulePacks: ["pack.beta@2.0.0", "pack.gamma@1.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)),
            sessionDigest: new AiSessionDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher",
                RulesetId: "other-ruleset",
                RuntimeFingerprint: "sha256:other",
                SelectionState: "selected",
                SessionReady: true,
                BundleFreshness: "fresh",
                ProfileId: "wrong-profile",
                ProfileTitle: "Wrong Profile"));
        DefaultAiExplainService service = new(
            digestService,
            new StubRulesetPluginRegistry(plugin));

        AiExplainValueProjection? projection = service.GetExplainValue(
            OwnerScope.LocalSingleUser,
            new AiExplainValueQuery(
                CharacterId: "char-7",
                CapabilityId: RulePackCapabilityIds.SessionQuickActions));

        Assert.IsNotNull(projection);
        Assert.AreEqual("entry.target", projection.ExplainEntryId);
        Assert.AreEqual(AiExplainEntryKinds.QuickActionAvailability, projection.Kind);
        Assert.AreEqual("pack.beta/provider.quick", projection.ProviderId);
        Assert.AreEqual("pack.beta", projection.PackId);
        Assert.AreEqual("ruleset.explain.summary.ready", projection.SummaryKey);
        Assert.IsFalse(projection.Trace!.Any(step => step.StepId == "trace:missing"));
        Assert.IsTrue(projection.Trace.Any(step => step.StepId == "pack.beta/provider.quick:0:0"));
        Assert.IsTrue(projection.Evidence!.Any(pointer => pointer.Kind == RulesetEvidencePointerKinds.ProviderBinding && pointer.Pointer == "pack.beta/provider.quick"));
        Assert.IsTrue(projection.Evidence.Any(pointer => pointer.Kind == RulesetEvidencePointerKinds.RulePack && pointer.Pointer == "pack.beta"));
        Assert.AreEqual(1, projection.Evidence.Count(pointer => pointer.Kind == RulesetEvidencePointerKinds.RuleReference && pointer.Pointer == "rule:quick-action"));
        Assert.AreEqual("profile-from-explain", projection.Provenance!.ProfileId);
        Assert.IsNull(projection.Evidence.FirstOrDefault(pointer => pointer.Kind == RulesetEvidencePointerKinds.RuleProfile));
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        private readonly AiRuntimeSummaryProjection? _runtimeSummary;
        private readonly AiCharacterDigestProjection? _characterDigest;
        private readonly AiSessionDigestProjection? _sessionDigest;

        public StubAiDigestService(
            AiRuntimeSummaryProjection? runtimeSummary = null,
            AiCharacterDigestProjection? characterDigest = null,
            AiSessionDigestProjection? sessionDigest = null)
        {
            _runtimeSummary = runtimeSummary ?? new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:coach",
                RulesetId: "sr5",
                Title: "Street-Level Runtime Lock",
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                EngineApiVersion: "1.0.0",
                ContentBundles: ["official.sr5.core@1.0.0"],
                RulePacks: ["campaign.street-level@2.0.0"],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [RulePackCapabilityIds.DeriveStat] = "campaign.street-level:derive.stat"
                },
                Visibility: ArtifactVisibilityModes.LocalOnly,
                Description: "Street-level runtime lock");
            _characterDigest = characterDigest ?? new AiCharacterDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:coach",
                Summary: new CharacterFileSummary(
                    Name: "Cipher",
                    Alias: "Ghostwire",
                    Metatype: "Human",
                    BuildMethod: "Priority",
                    CreatedVersion: "5",
                    AppVersion: "10",
                    Karma: 18m,
                    Nuyen: 1500m,
                    Created: true),
                LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
                HasSavedWorkspace: true);
            _sessionDigest = sessionDigest;
        }

        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => _runtimeSummary is not null && string.Equals(_runtimeSummary.RuntimeFingerprint, runtimeFingerprint, StringComparison.Ordinal)
                ? _runtimeSummary
                : null;

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => _characterDigest is not null && string.Equals(_characterDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _characterDigest
                : null;

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
            => _sessionDigest is not null && string.Equals(_sessionDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _sessionDigest
                : null;
    }

    private sealed class StubRulesetPluginRegistry : IRulesetPluginRegistry
    {
        private readonly IRulesetPlugin[] _plugins;

        public StubRulesetPluginRegistry(params IRulesetPlugin[] plugins)
        {
            _plugins = plugins;
        }

        public IReadOnlyList<IRulesetPlugin> All => _plugins;

        public IRulesetPlugin? Resolve(string? rulesetId)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            foreach (IRulesetPlugin plugin in _plugins)
            {
                if (string.Equals(plugin.Id.NormalizedValue, normalizedRulesetId, StringComparison.Ordinal))
                {
                    return plugin;
                }
            }

            return null;
        }
    }

    private sealed class StubRulesetPlugin : IRulesetPlugin
    {
        public StubRulesetPlugin(
            string rulesetId,
            IReadOnlyList<RulesetCapabilityDescriptor> descriptors,
            StubRulesetCapabilityHost capabilityHost)
        {
            Id = new RulesetId(rulesetId);
            DisplayName = $"Stub {rulesetId}";
            Serializer = new StubRulesetSerializer(Id);
            ShellDefinitions = new StubRulesetShellDefinitions();
            Catalogs = new StubRulesetCatalogProvider();
            CapabilityDescriptors = new StubRulesetCapabilityDescriptorProvider(descriptors);
            Capabilities = capabilityHost;
            Rules = new StubRulesetRuleHost();
            Scripts = new StubRulesetScriptHost();
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

    private sealed class StubRulesetSerializer : IRulesetSerializer
    {
        public StubRulesetSerializer(RulesetId rulesetId)
        {
            RulesetId = rulesetId;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload) =>
            new(RulesetId.ToString(), SchemaVersion, payloadKind, payload);
    }

    private sealed class StubRulesetShellDefinitions : IRulesetShellDefinitionProvider
    {
        public IReadOnlyList<AppCommandDefinition> GetCommands() => [];

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => [];
    }

    private sealed class StubRulesetCatalogProvider : IRulesetCatalogProvider
    {
        public IReadOnlyList<WorkflowDefinition> GetWorkflowDefinitions() => [];

        public IReadOnlyList<WorkflowSurfaceDefinition> GetWorkflowSurfaces() => [];

        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => [];
    }

    private sealed class StubRulesetCapabilityDescriptorProvider : IRulesetCapabilityDescriptorProvider
    {
        private readonly IReadOnlyList<RulesetCapabilityDescriptor> _descriptors;

        public StubRulesetCapabilityDescriptorProvider(IReadOnlyList<RulesetCapabilityDescriptor> descriptors)
        {
            _descriptors = descriptors;
        }

        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() => _descriptors;
    }

    private sealed class StubRulesetCapabilityHost : IRulesetCapabilityHost
    {
        private readonly RulesetCapabilityInvocationResult _result;

        public StubRulesetCapabilityHost(RulesetCapabilityInvocationResult result)
        {
            _result = result;
        }

        public RulesetCapabilityInvocationRequest? LastRequest { get; private set; }

        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            LastRequest = request;
            return ValueTask.FromResult(_result);
        }
    }

    private sealed class StubRulesetRuleHost : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetRuleEvaluationResult(true, new Dictionary<string, object?>(), Array.Empty<string>()));
        }
    }

    private sealed class StubRulesetScriptHost : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new RulesetScriptExecutionResult(true, null, new Dictionary<string, object?>()));
        }
    }
}
