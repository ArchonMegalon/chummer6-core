using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Hub;

public sealed class DefaultHubProjectCompatibilityService : IHubProjectCompatibilityService
{
    private readonly IRulesetPluginRegistry _rulesetPluginRegistry;
    private readonly IRulePackRegistryService _rulePackRegistryService;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IBuildKitRegistryService _buildKitRegistryService;
    private readonly IRuntimeInspectorService _runtimeInspectorService;
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;

    public DefaultHubProjectCompatibilityService(
        IRulesetPluginRegistry rulesetPluginRegistry,
        IRulePackRegistryService rulePackRegistryService,
        IRuleProfileRegistryService ruleProfileRegistryService,
        IBuildKitRegistryService buildKitRegistryService,
        IRuntimeInspectorService runtimeInspectorService,
        IRuntimeLockRegistryService runtimeLockRegistryService)
    {
        _rulesetPluginRegistry = rulesetPluginRegistry;
        _rulePackRegistryService = rulePackRegistryService;
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _buildKitRegistryService = buildKitRegistryService;
        _runtimeInspectorService = runtimeInspectorService;
        _runtimeLockRegistryService = runtimeLockRegistryService;
    }

    public HubProjectCompatibilityMatrix? GetMatrix(OwnerScope owner, string kind, string itemId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        string normalizedKind = HubCatalogItemKinds.NormalizeRequired(kind);

        return normalizedKind switch
        {
            HubCatalogItemKinds.RulePack => GetRulePackMatrix(owner, itemId, rulesetId),
            HubCatalogItemKinds.RuleProfile => GetRuleProfileMatrix(owner, itemId, rulesetId),
            HubCatalogItemKinds.BuildKit => GetBuildKitMatrix(owner, itemId, rulesetId),
            HubCatalogItemKinds.RuntimeLock => GetRuntimeLockMatrix(owner, itemId, rulesetId),
            _ => null
        };
    }

    private HubProjectCompatibilityMatrix? GetRulePackMatrix(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            RulePackRegistryEntry? entry = _rulePackRegistryService.Get(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            RulePackExecutionPolicyHint? sessionPolicy = entry.Manifest.ExecutionPolicies
                .FirstOrDefault(policy => string.Equals(policy.Environment, RulePackExecutionEnvironments.SessionRuntimeBundle, StringComparison.Ordinal));
            RulePackExecutionPolicyHint? hostedPolicy = entry.Manifest.ExecutionPolicies
                .FirstOrDefault(policy => string.Equals(policy.Environment, RulePackExecutionEnvironments.HostedServer, StringComparison.Ordinal));
            bool hasSessionSafeCapability = entry.Manifest.Capabilities.Any(capability => capability.SessionSafe);
            HubProjectCapabilityDescriptorProjection[] capabilities = BuildRulePackCapabilities(candidateRulesetId, entry);

            return new HubProjectCompatibilityMatrix(
                Kind: HubCatalogItemKinds.RulePack,
                ItemId: itemId,
                Rows:
                [
                    CreateRulesetRow(candidateRulesetId),
                    CreateInformationalRow(HubProjectCompatibilityRowKinds.EngineApi, entry.Manifest.EngineApiVersion),
                    CreateInformationalRow(HubProjectCompatibilityRowKinds.Visibility, entry.Publication.Visibility),
                    CreateInformationalRow(HubProjectCompatibilityRowKinds.Trust, entry.Manifest.TrustTier),
                    CreateCapabilitiesRow(capabilities),
                    CreateExecutionRow(
                        HubProjectCompatibilityRowKinds.SessionRuntime,
                        ResolveExecutionState(sessionPolicy?.PolicyMode, hasSessionSafeCapability),
                        hasSessionSafeCapability ? "session-safe" : "not-session-safe",
                        RulePackExecutionEnvironments.SessionRuntimeBundle,
                        sessionPolicy?.PolicyMode),
                    CreateExecutionRow(
                        HubProjectCompatibilityRowKinds.HostedPublic,
                        ResolveExecutionState(hostedPolicy?.PolicyMode, false),
                        hostedPolicy?.PolicyMode ?? "not-declared",
                        RulePackExecutionEnvironments.HostedServer,
                        hostedPolicy?.MinimumTrustTier)
                ],
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: capabilities);
        }

        return null;
    }

    private HubProjectCompatibilityMatrix? GetRuleProfileMatrix(OwnerScope owner, string itemId, string? rulesetId)
    {
        RuleProfileRegistryEntry? entry = _ruleProfileRegistryService.Get(owner, itemId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        RulePackRegistryEntry[] resolvedRulePacks = entry.Manifest.RulePacks
            .Select(selection => _rulePackRegistryService.Get(owner, selection.RulePack.Id, entry.Manifest.RulesetId))
            .OfType<RulePackRegistryEntry>()
            .ToArray();
        bool sessionReady = resolvedRulePacks.Length == 0 || resolvedRulePacks.All(IsSessionReadyRulePack);
        RuntimeInspectorProjection? runtimeProjection = _runtimeInspectorService.GetProfileProjection(owner, itemId, entry.Manifest.RulesetId);
        HubProjectCapabilityDescriptorProjection[] capabilities = BuildRuntimeCapabilities(
            entry.Manifest.RulesetId,
            entry.Manifest.RuntimeLock.ProviderBindings,
            entry.Manifest.RuntimeLock.RulePacks.Select(reference => reference.Id));
        string sessionRuntimeState = ResolveRuleProfileSessionRuntimeState(sessionReady, runtimeProjection);
        string sessionRuntimeValue = string.Equals(sessionRuntimeState, HubProjectCompatibilityStates.Compatible, StringComparison.Ordinal)
            ? "session-ready"
            : "session-review-required";
        string? sessionRuntimeNotes = ResolveRuleProfileSessionRuntimeNotes(runtimeProjection, entry.Manifest.RulePacks.Count);

        return new HubProjectCompatibilityMatrix(
            Kind: HubCatalogItemKinds.RuleProfile,
            ItemId: itemId,
            Rows:
            [
                CreateRulesetRow(entry.Manifest.RulesetId),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.EngineApi, entry.Manifest.RuntimeLock.EngineApiVersion),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.Visibility, entry.Publication.Visibility),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.Trust, ResolveTrustTier(entry.Publication.Visibility)),
                CreateCapabilitiesRow(capabilities),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.RuntimeFingerprint, entry.Manifest.RuntimeLock.RuntimeFingerprint),
                CreateSessionRuntimeSummaryRow(
                    sessionRuntimeState,
                    sessionRuntimeValue,
                    string.IsNullOrWhiteSpace(sessionRuntimeNotes) ? "hub.project.compatibility.notes.session-runtime.selected-rulepacks" : null,
                    string.IsNullOrWhiteSpace(sessionRuntimeNotes) ? [Param("rulePackCount", entry.Manifest.RulePacks.Count)] : [],
                    string.IsNullOrWhiteSpace(sessionRuntimeNotes) ? entry.Manifest.RulePacks.Count.ToString() : sessionRuntimeNotes),
                CreateNarrativeRow(
                    HubProjectCompatibilityRowKinds.CampaignReturn,
                    sessionRuntimeState,
                    DescribeRuleProfileCampaignReturn(entry.Manifest.RuntimeLock.RuntimeFingerprint, sessionRuntimeState, runtimeProjection)),
                CreateNarrativeRow(
                    HubProjectCompatibilityRowKinds.SupportClosure,
                    sessionRuntimeState,
                    DescribeRuleProfileSupportClosure(entry.Manifest.RuntimeLock.RuntimeFingerprint, runtimeProjection, entry.Manifest.RulePacks.Count))
            ],
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: capabilities);
    }

    private HubProjectCompatibilityMatrix? GetBuildKitMatrix(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            BuildKitRegistryEntry? entry = _buildKitRegistryService.Get(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            return new HubProjectCompatibilityMatrix(
                Kind: HubCatalogItemKinds.BuildKit,
                ItemId: itemId,
                Rows:
                [
                    CreateRulesetRow(candidateRulesetId),
                    CreateInformationalRow(HubProjectCompatibilityRowKinds.Visibility, entry.Visibility),
                    CreateInformationalRow(HubProjectCompatibilityRowKinds.Trust, entry.Manifest.TrustTier),
                    new HubProjectCompatibilityRow(
                        Kind: HubProjectCompatibilityRowKinds.RuntimeRequirements,
                        Label: GetDefaultLabel(HubProjectCompatibilityRowKinds.RuntimeRequirements),
                        State: entry.Manifest.RuntimeRequirements.Count == 0 ? HubProjectCompatibilityStates.Compatible : HubProjectCompatibilityStates.ReviewRequired,
                        CurrentValue: entry.Manifest.RuntimeRequirements.Count.ToString(),
                        Notes: BuildKitHandoffNarrator.DescribeRuntimeRequirements(entry.Manifest),
                        LabelKey: GetDefaultLabelKey(HubProjectCompatibilityRowKinds.RuntimeRequirements),
                        NotesParameters: []),
                    CreateSessionRuntimeSummaryRow(
                        HubProjectCompatibilityStates.Blocked,
                        "workbench-only",
                        null,
                        [],
                        BuildKitHandoffNarrator.DescribeSessionRuntimeHandoff(entry.Manifest)),
                    CreateNarrativeRow(
                        HubProjectCompatibilityRowKinds.CampaignReturn,
                        entry.Manifest.RuntimeRequirements.Count == 0 ? HubProjectCompatibilityStates.Compatible : HubProjectCompatibilityStates.ReviewRequired,
                        BuildKitHandoffNarrator.DescribeCampaignReturnContract(entry.Manifest)),
                    CreateNarrativeRow(
                        HubProjectCompatibilityRowKinds.SupportClosure,
                        entry.Manifest.RuntimeRequirements.Count == 0 ? HubProjectCompatibilityStates.Compatible : HubProjectCompatibilityStates.ReviewRequired,
                        BuildKitHandoffNarrator.DescribeSupportClosure(entry.Manifest))
                ],
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: []);
        }

        return null;
    }

    private HubProjectCompatibilityMatrix? GetRuntimeLockMatrix(OwnerScope owner, string itemId, string? rulesetId)
    {
        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, itemId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        HubProjectCapabilityDescriptorProjection[] capabilities = BuildRuntimeCapabilities(
            entry.RuntimeLock.RulesetId,
            entry.RuntimeLock.ProviderBindings,
            entry.RuntimeLock.RulePacks.Select(reference => reference.Id));

        return new HubProjectCompatibilityMatrix(
            Kind: HubCatalogItemKinds.RuntimeLock,
            ItemId: itemId,
            Rows:
            [
                CreateRulesetRow(entry.RuntimeLock.RulesetId),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.EngineApi, entry.RuntimeLock.EngineApiVersion),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.Visibility, entry.Visibility),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.Trust, ResolveTrustTier(entry.Visibility)),
                new HubProjectCompatibilityRow(
                    Kind: HubProjectCompatibilityRowKinds.InstallState,
                    Label: GetDefaultLabel(HubProjectCompatibilityRowKinds.InstallState),
                    State: HubProjectCompatibilityStates.Informational,
                    CurrentValue: entry.Install.State,
                    Notes: entry.Install.InstalledTargetId,
                    LabelKey: GetDefaultLabelKey(HubProjectCompatibilityRowKinds.InstallState),
                    CurrentValueKey: GetValueKey(HubProjectCompatibilityRowKinds.InstallState, entry.Install.State)),
                CreateCapabilitiesRow(capabilities),
                CreateInformationalRow(HubProjectCompatibilityRowKinds.RuntimeFingerprint, entry.RuntimeLock.RuntimeFingerprint),
                CreateSessionRuntimeSummaryRow(
                    HubProjectCompatibilityStates.Compatible,
                    "bundle-ready",
                    "hub.project.compatibility.notes.session-runtime.resolved-rulepacks",
                    [Param("rulePackCount", entry.RuntimeLock.RulePacks.Count)],
                    entry.RuntimeLock.RulePacks.Count.ToString()),
                CreateNarrativeRow(
                    HubProjectCompatibilityRowKinds.CampaignReturn,
                    HubProjectCompatibilityStates.Compatible,
                    DescribeRuntimeLockCampaignReturn(entry)),
                CreateNarrativeRow(
                    HubProjectCompatibilityRowKinds.SupportClosure,
                    HubProjectCompatibilityStates.Compatible,
                    DescribeRuntimeLockSupportClosure(entry))
            ],
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: capabilities);
    }

    private IEnumerable<string> EnumerateRulesetIds(string? rulesetId)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        if (normalizedRulesetId is not null)
        {
            yield return normalizedRulesetId;
            yield break;
        }

        foreach (IRulesetPlugin plugin in _rulesetPluginRegistry.All)
        {
            yield return plugin.Id.NormalizedValue;
        }
    }

    private static HubProjectCompatibilityRow CreateRulesetRow(string rulesetId) =>
        new(
            Kind: HubProjectCompatibilityRowKinds.Ruleset,
            Label: GetDefaultLabel(HubProjectCompatibilityRowKinds.Ruleset),
            State: HubProjectCompatibilityStates.Compatible,
            CurrentValue: rulesetId,
            LabelKey: GetDefaultLabelKey(HubProjectCompatibilityRowKinds.Ruleset));

    private static HubProjectCompatibilityRow CreateCapabilitiesRow(IReadOnlyList<HubProjectCapabilityDescriptorProjection> capabilities) =>
        new(
            Kind: HubProjectCompatibilityRowKinds.Capabilities,
            Label: GetDefaultLabel(HubProjectCompatibilityRowKinds.Capabilities),
            State: HubProjectCompatibilityStates.Informational,
            CurrentValue: capabilities.Count.ToString(),
            Notes: capabilities.Count == 0
                ? "No typed capability descriptors are published for this runtime."
                : $"{capabilities.Count(capability => capability.SessionSafe)} session-safe; {capabilities.Count(capability => capability.Explainable)} explainable",
            LabelKey: GetDefaultLabelKey(HubProjectCompatibilityRowKinds.Capabilities),
            NotesKey: capabilities.Count == 0
                ? "hub.project.compatibility.notes.capabilities.none"
                : "hub.project.compatibility.notes.capabilities.summary",
            NotesParameters: capabilities.Count == 0
                ? []
                : [
                    Param("sessionSafeCount", capabilities.Count(capability => capability.SessionSafe)),
                    Param("explainableCount", capabilities.Count(capability => capability.Explainable))
                ]);

    private static HubProjectCompatibilityRow CreateInformationalRow(string kind, string currentValue) =>
        new(
            Kind: kind,
            Label: GetDefaultLabel(kind),
            State: HubProjectCompatibilityStates.Informational,
            CurrentValue: currentValue,
            LabelKey: GetDefaultLabelKey(kind));

    private static HubProjectCompatibilityRow CreateNarrativeRow(
        string kind,
        string state,
        string notes)
        => new(
            Kind: kind,
            Label: GetDefaultLabel(kind),
            State: state,
            CurrentValue: state,
            Notes: notes,
            LabelKey: GetDefaultLabelKey(kind),
            CurrentValueKey: GetValueKey(kind, state));

    private static HubProjectCompatibilityRow CreateExecutionRow(
        string kind,
        string state,
        string currentValue,
        string requiredValue,
        string? notes)
        => new(
            Kind: kind,
            Label: GetDefaultLabel(kind),
            State: state,
            CurrentValue: currentValue,
            RequiredValue: requiredValue,
            Notes: notes,
            LabelKey: GetDefaultLabelKey(kind),
            CurrentValueKey: GetValueKey(kind, currentValue),
            RequiredValueKey: GetValueKey(kind, requiredValue),
            NotesKey: notes is null ? null : GetValueKey(kind, notes));

    private static HubProjectCompatibilityRow CreateSessionRuntimeSummaryRow(
        string state,
        string currentValue,
        string? notesKey,
        IReadOnlyList<RulesetExplainParameter> notesParameters,
        string? notes)
        => new(
            Kind: HubProjectCompatibilityRowKinds.SessionRuntime,
            Label: GetDefaultLabel(HubProjectCompatibilityRowKinds.SessionRuntime),
            State: state,
            CurrentValue: currentValue,
            RequiredValue: RulePackExecutionEnvironments.SessionRuntimeBundle,
            Notes: notes,
            LabelKey: GetDefaultLabelKey(HubProjectCompatibilityRowKinds.SessionRuntime),
            CurrentValueKey: GetValueKey(HubProjectCompatibilityRowKinds.SessionRuntime, currentValue),
            RequiredValueKey: GetValueKey(HubProjectCompatibilityRowKinds.SessionRuntime, RulePackExecutionEnvironments.SessionRuntimeBundle),
            NotesKey: notesKey,
            NotesParameters: notesParameters);

    private static string ResolveRuleProfileSessionRuntimeState(
        bool sessionReady,
        RuntimeInspectorProjection? runtimeProjection)
    {
        if (runtimeProjection?.CompatibilityDiagnostics.Any(static diagnostic =>
                string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.MissingPack, StringComparison.Ordinal)) == true)
        {
            return HubProjectCompatibilityStates.Blocked;
        }

        if (!sessionReady)
        {
            return HubProjectCompatibilityStates.ReviewRequired;
        }

        if (runtimeProjection?.CompatibilityDiagnostics.Any(static diagnostic =>
                string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.RebindRequired, StringComparison.Ordinal)) == true)
        {
            return HubProjectCompatibilityStates.ReviewRequired;
        }

        if ((runtimeProjection?.Warnings.Count ?? 0) > 0)
        {
            return HubProjectCompatibilityStates.ReviewRequired;
        }

        return HubProjectCompatibilityStates.Compatible;
    }

    private static string? ResolveRuleProfileSessionRuntimeNotes(
        RuntimeInspectorProjection? runtimeProjection,
        int selectedRulePackCount)
    {
        if (runtimeProjection is null)
        {
            return null;
        }

        int missingPackCount = runtimeProjection.CompatibilityDiagnostics.Count(static diagnostic =>
            string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.MissingPack, StringComparison.Ordinal));
        if (missingPackCount > 0)
        {
            return $"{missingPackCount} required rule pack(s) are missing from the grounded runtime.";
        }

        if (runtimeProjection.CompatibilityDiagnostics.Any(static diagnostic =>
                string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.RebindRequired, StringComparison.Ordinal)))
        {
            return "Installed runtime fingerprint differs from the selected runtime profile and must be rebound before the next campaign-safe handoff.";
        }

        if (runtimeProjection.Warnings.Count > 0)
        {
            return $"{runtimeProjection.Warnings.Count} runtime inspector warning(s) still need review across {selectedRulePackCount} selected rule pack(s).";
        }

        return null;
    }

    private static string DescribeRuleProfileCampaignReturn(
        string runtimeFingerprint,
        string sessionRuntimeState,
        RuntimeInspectorProjection? runtimeProjection)
    {
        if (runtimeProjection?.CompatibilityDiagnostics.Any(static diagnostic =>
                string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.MissingPack, StringComparison.Ordinal)) == true)
        {
            return "Campaign return stays blocked until the missing rule packs land on the grounded runtime again.";
        }

        if (string.Equals(sessionRuntimeState, HubProjectCompatibilityStates.ReviewRequired, StringComparison.Ordinal))
        {
            return $"Campaign return still needs runtime review because fingerprint {runtimeFingerprint} has drift or warning posture that must be cleared before the next safe handoff.";
        }

        return $"Campaign return can reuse grounded runtime fingerprint {runtimeFingerprint} without reopening migration or runtime drift review.";
    }

    private static string DescribeRuleProfileSupportClosure(
        string runtimeFingerprint,
        RuntimeInspectorProjection? runtimeProjection,
        int selectedRulePackCount)
    {
        int warningCount = runtimeProjection?.Warnings.Count ?? 0;
        if (warningCount > 0)
        {
            return $"Support closure should stay review-aware because {warningCount} runtime inspector warning(s) are still open across {selectedRulePackCount} selected rule pack(s).";
        }

        if (runtimeProjection?.CompatibilityDiagnostics.Any(static diagnostic =>
                string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.RebindRequired, StringComparison.Ordinal)) == true)
        {
            return $"Support closure can cite runtime fingerprint {runtimeFingerprint}, but the install must rebind before the next closure claim is safe.";
        }

        return $"Support closure can cite grounded runtime fingerprint {runtimeFingerprint} and {selectedRulePackCount} selected rule pack(s) without adding a new compatibility gap.";
    }

    private static string DescribeRuntimeLockCampaignReturn(RuntimeLockRegistryEntry entry)
    {
        string installedTarget = string.IsNullOrWhiteSpace(entry.Install.InstalledTargetId)
            ? "the selected workspace"
            : entry.Install.InstalledTargetId!;
        return $"Campaign return can reuse runtime fingerprint {entry.RuntimeLock.RuntimeFingerprint} through {installedTarget} without reopening the runtime contract.";
    }

    private static string DescribeRuntimeLockSupportClosure(RuntimeLockRegistryEntry entry)
    {
        int resolvedRulePackCount = entry.RuntimeLock.RulePacks.Count;
        return $"Support closure can cite runtime fingerprint {entry.RuntimeLock.RuntimeFingerprint} and {resolvedRulePackCount} resolved rule pack(s) as the install-local compatibility oracle.";
    }

    private static string GetDefaultLabel(string kind) => kind switch
    {
        HubProjectCompatibilityRowKinds.Ruleset => "Ruleset",
        HubProjectCompatibilityRowKinds.EngineApi => "Engine API",
        HubProjectCompatibilityRowKinds.Visibility => "Visibility",
        HubProjectCompatibilityRowKinds.Trust => "Trust Tier",
        HubProjectCompatibilityRowKinds.InstallState => "Install State",
        HubProjectCompatibilityRowKinds.Capabilities => "Capabilities",
        HubProjectCompatibilityRowKinds.SessionRuntime => "Session Runtime Bundle",
        HubProjectCompatibilityRowKinds.HostedPublic => "Hosted/Public Runtime",
        HubProjectCompatibilityRowKinds.RuntimeFingerprint => "Runtime Fingerprint",
        HubProjectCompatibilityRowKinds.RuntimeRequirements => "Runtime Requirements",
        HubProjectCompatibilityRowKinds.CampaignReturn => "Campaign Return",
        HubProjectCompatibilityRowKinds.SupportClosure => "Support Closure",
        _ => kind
    };

    private static string GetDefaultLabelKey(string kind) =>
        $"hub.project.compatibility.row.{kind}.label";

    private static string GetValueKey(string kind, string value) =>
        $"hub.project.compatibility.row.{kind}.value.{value}";

    private static RulesetExplainParameter Param(string name, object? value) =>
        new(name, RulesetCapabilityBridge.FromObject(value));

    private static string ResolveExecutionState(string? policyMode, bool sessionSafe)
    {
        if (sessionSafe)
        {
            return HubProjectCompatibilityStates.Compatible;
        }

        return policyMode switch
        {
            RulePackExecutionPolicyModes.Allow => HubProjectCompatibilityStates.Compatible,
            RulePackExecutionPolicyModes.ReviewRequired => HubProjectCompatibilityStates.ReviewRequired,
            _ => HubProjectCompatibilityStates.Blocked
        };
    }

    private static bool IsSessionReadyRulePack(RulePackRegistryEntry entry)
    {
        return entry.Manifest.Capabilities.Any(capability => capability.SessionSafe)
            || entry.Manifest.ExecutionPolicies.Any(policy =>
                string.Equals(policy.Environment, RulePackExecutionEnvironments.SessionRuntimeBundle, StringComparison.Ordinal)
                && !string.Equals(policy.PolicyMode, RulePackExecutionPolicyModes.Deny, StringComparison.Ordinal));
    }

    private HubProjectCapabilityDescriptorProjection[] BuildRulePackCapabilities(string rulesetId, RulePackRegistryEntry entry)
    {
        IReadOnlyDictionary<string, RulesetCapabilityDescriptor> rulesetDescriptors = GetRulesetCapabilityDescriptors(rulesetId);

        return entry.Manifest.Capabilities
            .OrderBy(capability => capability.CapabilityId, StringComparer.Ordinal)
            .Select(capability =>
            {
                rulesetDescriptors.TryGetValue(capability.CapabilityId, out RulesetCapabilityDescriptor? descriptor);
                return new HubProjectCapabilityDescriptorProjection(
                    CapabilityId: capability.CapabilityId,
                    InvocationKind: descriptor?.InvocationKind,
                    Title: descriptor?.Title,
                    Explainable: capability.Explainable || descriptor?.Explainable == true,
                    SessionSafe: capability.SessionSafe || descriptor?.SessionSafe == true,
                    DefaultGasBudget: descriptor?.DefaultGasBudget,
                    MaximumGasBudget: descriptor?.MaximumGasBudget,
                    PackId: entry.Manifest.PackId,
                    AssetKind: capability.AssetKind,
                    AssetMode: capability.AssetMode,
                    TitleKey: descriptor is null ? null : RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor),
                    TitleParameters: descriptor is null ? null : RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor));
            })
            .ToArray();
    }

    private HubProjectCapabilityDescriptorProjection[] BuildRuntimeCapabilities(
        string rulesetId,
        IReadOnlyDictionary<string, string> providerBindings,
        IEnumerable<string> packIds)
    {
        return GetRulesetCapabilityDescriptors(rulesetId)
            .Values
            .OrderBy(descriptor => descriptor.CapabilityId, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                string? providerId = providerBindings.GetValueOrDefault(descriptor.CapabilityId);
                return new HubProjectCapabilityDescriptorProjection(
                    CapabilityId: descriptor.CapabilityId,
                    InvocationKind: descriptor.InvocationKind,
                    Title: descriptor.Title,
                    Explainable: descriptor.Explainable,
                    SessionSafe: descriptor.SessionSafe,
                    DefaultGasBudget: descriptor.DefaultGasBudget,
                    MaximumGasBudget: descriptor.MaximumGasBudget,
                    ProviderId: providerId,
                    PackId: providerId is null ? null : TryResolvePackId(providerId, packIds),
                    TitleKey: RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor),
                    TitleParameters: RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor));
            })
            .ToArray();
    }

    private IReadOnlyDictionary<string, RulesetCapabilityDescriptor> GetRulesetCapabilityDescriptors(string rulesetId)
    {
        IRulesetPlugin? plugin = _rulesetPluginRegistry.Resolve(rulesetId);
        if (plugin is null)
        {
            return new Dictionary<string, RulesetCapabilityDescriptor>(StringComparer.Ordinal);
        }

        return plugin.CapabilityDescriptors
            .GetCapabilityDescriptors()
            .ToDictionary(descriptor => descriptor.CapabilityId, descriptor => descriptor, StringComparer.Ordinal);
    }

    private static string? TryResolvePackId(string providerId, IEnumerable<string> packIds)
    {
        foreach (string packId in packIds)
        {
            if (providerId.StartsWith($"{packId}/", StringComparison.Ordinal))
            {
                return packId;
            }
        }

        return null;
    }

    private static string ResolveTrustTier(string visibility) =>
        string.Equals(visibility, ArtifactVisibilityModes.Public, StringComparison.Ordinal)
            ? ArtifactTrustTiers.Curated
            : ArtifactTrustTiers.LocalOnly;
}
