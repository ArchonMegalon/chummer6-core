using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public sealed class DefaultActiveRuntimeStatusService : IActiveRuntimeStatusService
{
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRuntimeInspectorService _runtimeInspectorService;

    public DefaultActiveRuntimeStatusService(
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRuntimeInspectorService runtimeInspectorService)
    {
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _runtimeInspectorService = runtimeInspectorService;
    }

    public ActiveRuntimeStatusProjection? GetActiveProfileStatus(OwnerScope owner, string? rulesetId = null)
    {
        RuleProfileRegistryEntry[] entries = _ruleProfileRegistryService.List(owner, rulesetId).ToArray();
        if (entries.Length == 0)
        {
            return null;
        }

        string? normalizedRulesetId = string.IsNullOrWhiteSpace(rulesetId)
            ? null
            : rulesetId.Trim().ToLowerInvariant();
        RuleProfileRegistryEntry selected = entries
            .OrderBy(entry => GetPriority(entry, normalizedRulesetId))
            .ThenByDescending(entry => entry.Install.InstalledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(entry => entry.Manifest.ProfileId, StringComparer.Ordinal)
            .First();
        RuntimeInspectorProjection? runtimeProjection = _runtimeInspectorService.GetProfileProjection(
            owner,
            selected.Manifest.ProfileId,
            selected.Manifest.RulesetId);

        return new ActiveRuntimeStatusProjection(
            ProfileId: selected.Manifest.ProfileId,
            Title: selected.Manifest.Title,
            RulesetId: selected.Manifest.RulesetId,
            RuntimeFingerprint: selected.Manifest.RuntimeLock.RuntimeFingerprint,
            InstallState: selected.Install.State,
            InstalledTargetKind: selected.Install.InstalledTargetKind,
            InstalledTargetId: selected.Install.InstalledTargetId,
            RulePackCount: selected.Manifest.RuntimeLock.RulePacks.Count,
            ProviderBindingCount: selected.Manifest.RuntimeLock.ProviderBindings.Count,
            WarningCount: runtimeProjection?.Warnings.Count ?? 0);
    }

    private static int GetPriority(RuleProfileRegistryEntry entry, string? rulesetId)
    {
        if (string.Equals(entry.Install.State, ArtifactInstallStates.Pinned, StringComparison.Ordinal)
            && string.Equals(entry.Install.InstalledTargetKind, RuleProfileApplyTargetKinds.GlobalDefaults, StringComparison.Ordinal))
        {
            return 0;
        }

        if (string.Equals(entry.Install.State, ArtifactInstallStates.Installed, StringComparison.Ordinal)
            && string.Equals(entry.Install.InstalledTargetKind, RuleProfileApplyTargetKinds.GlobalDefaults, StringComparison.Ordinal))
        {
            return 1;
        }

        if (rulesetId is not null
            && string.Equals(entry.Manifest.ProfileId, $"official.{rulesetId}.core", StringComparison.Ordinal))
        {
            return 2;
        }

        if (!string.Equals(entry.Install.State, ArtifactInstallStates.Available, StringComparison.Ordinal))
        {
            return 3;
        }

        return 4;
    }
}
