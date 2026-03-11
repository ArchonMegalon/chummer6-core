using System.Linq;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuntimeInspectorService : IRuntimeInspectorService
{
    private readonly IRulesetPluginRegistry _rulesetPluginRegistry;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRulePackRegistryService _rulePackRegistryService;

    public DefaultRuntimeInspectorService(
        IRulesetPluginRegistry rulesetPluginRegistry,
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRulePackRegistryService rulePackRegistryService)
    {
        _rulesetPluginRegistry = rulesetPluginRegistry;
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _rulePackRegistryService = rulePackRegistryService;
    }

    public RuntimeInspectorProjection? GetProfileProjection(OwnerScope owner, string profileId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        RuleProfileRegistryEntry? profile = _ruleProfileRegistryService.Get(owner, profileId, rulesetId);
        if (profile is null)
        {
            return null;
        }

        IReadOnlyDictionary<string, RulePackRegistryEntry> registryEntries = _rulePackRegistryService.List(owner, profile.Manifest.RulesetId)
            .ToDictionary(entry => entry.Manifest.PackId, StringComparer.Ordinal);
        string[] knownPackIds = registryEntries.Keys
            .OrderByDescending(static packId => packId.Length)
            .ThenBy(static packId => packId, StringComparer.Ordinal)
            .ToArray();

        RuntimeInspectorRulePackEntry[] resolvedRulePacks = profile.Manifest.RulePacks
            .Select(selection => ToResolvedRulePackEntry(selection, registryEntries.GetValueOrDefault(selection.RulePack.Id)))
            .OrderBy(static entry => entry.RulePack.Id, StringComparer.Ordinal)
            .ThenBy(static entry => entry.RulePack.Version, StringComparer.Ordinal)
            .ToArray();
        RuntimeInspectorProviderBinding[] providerBindings = profile.Manifest.RuntimeLock.ProviderBindings
            .OrderBy(static binding => binding.Key, StringComparer.Ordinal)
            .ThenBy(static binding => binding.Value, StringComparer.Ordinal)
            .Select(binding => new RuntimeInspectorProviderBinding(
                CapabilityId: binding.Key,
                ProviderId: binding.Value,
                PackId: TryResolvePackId(binding.Value, knownPackIds),
                SourceAssetPath: null,
                SessionSafe: false))
            .ToArray();
        RuntimeInspectorCapabilityDescriptorProjection[] capabilityDescriptors = BuildCapabilityDescriptors(
            profile.Manifest.RulesetId,
            profile.Manifest.RuntimeLock.ProviderBindings,
            knownPackIds);
        RuntimeLockCompatibilityDiagnostic[] compatibilityDiagnostics = BuildCompatibilityDiagnostics(profile, registryEntries);
        RuntimeInspectorWarning[] warnings = BuildWarnings(profile, resolvedRulePacks, compatibilityDiagnostics);
        RuntimeMigrationPreviewItem[] migrationPreview = BuildMigrationPreview(profile, resolvedRulePacks);

        return new RuntimeInspectorProjection(
            TargetKind: RuntimeInspectorTargetKinds.RuntimeLock,
            TargetId: profile.Manifest.ProfileId,
            RuntimeLock: profile.Manifest.RuntimeLock,
            Install: NormalizeInstall(profile.Install, profile.Manifest.RuntimeLock.RuntimeFingerprint),
            ResolvedRulePacks: resolvedRulePacks,
            ProviderBindings: providerBindings,
            CompatibilityDiagnostics: compatibilityDiagnostics,
            Warnings: warnings,
            MigrationPreview: migrationPreview,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            ProfileSourceKind: profile.SourceKind,
            CapabilityDescriptors: capabilityDescriptors);
    }

    private RuntimeInspectorCapabilityDescriptorProjection[] BuildCapabilityDescriptors(
        string rulesetId,
        IReadOnlyDictionary<string, string> providerBindings,
        IEnumerable<string> packIds)
    {
        IRulesetPlugin? plugin = _rulesetPluginRegistry.Resolve(rulesetId);
        if (plugin is null)
        {
            return [];
        }

        return plugin.CapabilityDescriptors
            .GetCapabilityDescriptors()
            .OrderBy(descriptor => descriptor.CapabilityId, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                string? providerId = providerBindings.GetValueOrDefault(descriptor.CapabilityId);
                return new RuntimeInspectorCapabilityDescriptorProjection(
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

    private static RuntimeInspectorRulePackEntry ToResolvedRulePackEntry(
        RuleProfilePackSelection selection,
        RulePackRegistryEntry? registryEntry)
    {
        return new RuntimeInspectorRulePackEntry(
            RulePack: selection.RulePack,
            Title: registryEntry?.Manifest.Title ?? selection.RulePack.Id,
            Visibility: registryEntry?.Publication.Visibility ?? ArtifactVisibilityModes.LocalOnly,
            TrustTier: registryEntry?.Manifest.TrustTier ?? ArtifactTrustTiers.LocalOnly,
            CapabilityIds: registryEntry is null
                ? []
                : registryEntry.Manifest.Capabilities
                    .Select(static capability => capability.CapabilityId)
                    .OrderBy(static capabilityId => capabilityId, StringComparer.Ordinal)
                    .ToArray(),
            Enabled: selection.EnabledByDefault,
            SourceKind: registryEntry?.SourceKind ?? RegistryEntrySourceKinds.PersistedManifest);
    }

    private static RuntimeLockCompatibilityDiagnostic[] BuildCompatibilityDiagnostics(
        RuleProfileRegistryEntry profile,
        IReadOnlyDictionary<string, RulePackRegistryEntry> registryEntries)
    {
        List<RuntimeLockCompatibilityDiagnostic> diagnostics = [];

        foreach (RuleProfilePackSelection selection in profile.Manifest.RulePacks
                     .OrderBy(static selection => selection.RulePack.Id, StringComparer.Ordinal)
                     .ThenBy(static selection => selection.RulePack.Version, StringComparer.Ordinal))
        {
            if (!registryEntries.ContainsKey(selection.RulePack.Id))
            {
                diagnostics.Add(new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.MissingPack,
                    Message: "runtime.lock.compatibility.missing-pack",
                    RequiredRulesetId: profile.Manifest.RulesetId,
                    RequiredRuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                    MessageKey: "runtime.lock.compatibility.missing-pack",
                    MessageParameters:
                    [
                        Param("packId", selection.RulePack.Id),
                        Param("version", selection.RulePack.Version),
                        Param("rulesetId", profile.Manifest.RulesetId),
                        Param("runtimeFingerprint", profile.Manifest.RuntimeLock.RuntimeFingerprint)
                    ]));
            }
        }

        if (diagnostics.Count == 0)
        {
            diagnostics.Add(new RuntimeLockCompatibilityDiagnostic(
                State: RuntimeLockCompatibilityStates.Compatible,
                Message: "runtime.lock.compatibility.compatible",
                RequiredRulesetId: profile.Manifest.RulesetId,
                RequiredRuntimeFingerprint: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                MessageKey: "runtime.lock.compatibility.compatible",
                MessageParameters:
                [
                    Param("rulesetId", profile.Manifest.RulesetId),
                    Param("runtimeFingerprint", profile.Manifest.RuntimeLock.RuntimeFingerprint)
                ]));
        }

        return diagnostics.ToArray();
    }

    private static RuntimeInspectorWarning[] BuildWarnings(
        RuleProfileRegistryEntry profile,
        IReadOnlyList<RuntimeInspectorRulePackEntry> resolvedRulePacks,
        IReadOnlyList<RuntimeLockCompatibilityDiagnostic> compatibilityDiagnostics)
    {
        List<RuntimeInspectorWarning> warnings = [];

        if (string.Equals(profile.Publication.Visibility, ArtifactVisibilityModes.LocalOnly, StringComparison.Ordinal))
        {
            warnings.Add(new RuntimeInspectorWarning(
                Kind: RuntimeInspectorWarningKinds.Trust,
                Severity: RuntimeInspectorWarningSeverityLevels.Info,
                Message: "runtime.inspector.warning.trust.local-only",
                SubjectId: profile.Manifest.ProfileId,
                MessageKey: "runtime.inspector.warning.trust.local-only",
                MessageParameters: [Param("profileId", profile.Manifest.ProfileId)]));
        }

        if (compatibilityDiagnostics.Any(diagnostic => string.Equals(diagnostic.State, RuntimeLockCompatibilityStates.MissingPack, StringComparison.Ordinal)))
        {
            warnings.Add(new RuntimeInspectorWarning(
                Kind: RuntimeInspectorWarningKinds.Compatibility,
                Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                Message: "runtime.inspector.warning.compatibility.missing-pack",
                SubjectId: profile.Manifest.ProfileId,
                MessageKey: "runtime.inspector.warning.compatibility.missing-pack",
                MessageParameters: [Param("profileId", profile.Manifest.ProfileId)]));
        }

        if (resolvedRulePacks.Count == 0)
        {
            warnings.Add(new RuntimeInspectorWarning(
                Kind: RuntimeInspectorWarningKinds.ProviderBinding,
                Severity: RuntimeInspectorWarningSeverityLevels.Info,
                Message: "runtime.inspector.warning.provider-binding.none",
                SubjectId: profile.Manifest.ProfileId,
                MessageKey: "runtime.inspector.warning.provider-binding.none",
                MessageParameters: [Param("profileId", profile.Manifest.ProfileId)]));
        }

        return warnings.ToArray();
    }

    private static RuntimeMigrationPreviewItem[] BuildMigrationPreview(
        RuleProfileRegistryEntry profile,
        IReadOnlyList<RuntimeInspectorRulePackEntry> resolvedRulePacks)
    {
        List<RuntimeMigrationPreviewItem> preview = resolvedRulePacks
            .Select(rulePack => new RuntimeMigrationPreviewItem(
                Kind: RuntimeMigrationPreviewChangeKinds.RulePackAdded,
                Summary: "runtime.inspector.preview.rulepack-added",
                SubjectId: rulePack.RulePack.Id,
                AfterValue: rulePack.RulePack.Version,
                RequiresRebind: false,
                SummaryKey: "runtime.inspector.preview.rulepack-added",
                SummaryParameters:
                [
                    Param("packId", rulePack.RulePack.Id),
                    Param("version", rulePack.RulePack.Version)
                ]))
            .ToList();

        if (preview.Count == 0)
        {
            preview.Add(new RuntimeMigrationPreviewItem(
                Kind: RuntimeMigrationPreviewChangeKinds.ContentBundleUpdated,
                Summary: "runtime.inspector.preview.runtime-pinned",
                SubjectId: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                AfterValue: profile.Manifest.RuntimeLock.RuntimeFingerprint,
                RequiresRebind: false,
                SummaryKey: "runtime.inspector.preview.runtime-pinned",
                SummaryParameters:
                [
                    Param("profileId", profile.Manifest.ProfileId),
                    Param("runtimeFingerprint", profile.Manifest.RuntimeLock.RuntimeFingerprint)
                ]));
        }

        return preview.ToArray();
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

    private static ArtifactInstallState NormalizeInstall(ArtifactInstallState install, string runtimeFingerprint)
    {
        return string.IsNullOrWhiteSpace(install.RuntimeFingerprint)
            ? install with { RuntimeFingerprint = runtimeFingerprint }
            : install;
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
