using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRulePackInstallService : IRulePackInstallService
{
    private readonly IRulePackInstallHistoryStore _installHistoryStore;
    private readonly IRulePackInstallStateStore _installStateStore;
    private readonly IRulePackRegistryService _rulePackRegistryService;

    public DefaultRulePackInstallService(
        IRulePackRegistryService rulePackRegistryService,
        IRulePackInstallStateStore installStateStore,
        IRulePackInstallHistoryStore installHistoryStore)
    {
        _rulePackRegistryService = rulePackRegistryService;
        _installStateStore = installStateStore;
        _installHistoryStore = installHistoryStore;
    }

    public RulePackInstallPreviewReceipt? Preview(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetId);

        RulePackRegistryEntry? entry = _rulePackRegistryService.Get(owner, packId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        string resolvedRulesetId = ResolveRulesetId(entry, rulesetId);
        List<RulePackInstallPreviewItem> changes =
        [
            PreviewItem(
                kind: RulePackInstallPreviewChangeKinds.InstallStateChanged,
                summaryKey: "rulepack.install.preview.install-state-changed",
                subjectId: entry.Manifest.PackId,
                requiresConfirmation: false,
                ("packId", entry.Manifest.PackId),
                ("version", entry.Manifest.Version),
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId))
        ];
        List<RuntimeInspectorWarning> warnings = BuildWarnings(entry);

        if (entry.Manifest.Capabilities.Count > 0)
        {
            changes.Add(PreviewItem(
                kind: RulePackInstallPreviewChangeKinds.RuntimeReviewRequired,
                summaryKey: "rulepack.install.preview.runtime-review-required",
                subjectId: entry.Manifest.PackId,
                requiresConfirmation: true,
                ("packId", entry.Manifest.PackId),
                ("capabilityCount", entry.Manifest.Capabilities.Count)));
        }

        if (string.Equals(target.TargetKind, RuleProfileApplyTargetKinds.SessionLedger, StringComparison.Ordinal))
        {
            changes.Add(PreviewItem(
                kind: RulePackInstallPreviewChangeKinds.SessionReplayRequired,
                summaryKey: "rulepack.install.preview.session-replay-required",
                subjectId: target.TargetId,
                requiresConfirmation: true,
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId)));
        }

        bool requiresConfirmation = changes.Any(change => change.RequiresConfirmation);
        return new RulePackInstallPreviewReceipt(
            PackId: entry.Manifest.PackId,
            RulesetId: resolvedRulesetId,
            Target: target,
            Changes: changes,
            Warnings: warnings,
            RequiresConfirmation: requiresConfirmation);
    }

    public RulePackInstallReceipt? Apply(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        RulePackInstallPreviewReceipt? preview = Preview(owner, packId, target, rulesetId);
        if (preview is null)
        {
            return null;
        }

        RulePackRegistryEntry? entry = _rulePackRegistryService.Get(owner, packId, preview.RulesetId);
        if (entry is null)
        {
            return null;
        }

        ArtifactInstallState current = entry.Install;
        string desiredState = ResolveInstallState(target);
        if (string.Equals(current.State, desiredState, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetKind, target.TargetKind, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetId, target.TargetId, StringComparison.Ordinal))
        {
            return new RulePackInstallReceipt(
                PackId: preview.PackId,
                RulesetId: preview.RulesetId,
                Target: target,
                Outcome: RulePackInstallOutcomes.AlreadyInstalled,
                Install: current,
                Preview: preview);
        }

        DateTimeOffset appliedAtUtc = DateTimeOffset.UtcNow;
        ArtifactInstallState install = new(
            State: desiredState,
            InstalledAtUtc: appliedAtUtc,
            InstalledTargetKind: target.TargetKind,
            InstalledTargetId: target.TargetId,
            RuntimeFingerprint: current.RuntimeFingerprint);
        ArtifactInstallState persistedInstall = _installStateStore.Upsert(
            owner,
            new RulePackInstallRecord(entry.Manifest.PackId, entry.Manifest.Version, preview.RulesetId, install)).Install;
        _installHistoryStore.Append(
            owner,
            new RulePackInstallHistoryRecord(
                entry.Manifest.PackId,
                entry.Manifest.Version,
                preview.RulesetId,
                new ArtifactInstallHistoryEntry(
                    Operation: string.Equals(desiredState, ArtifactInstallStates.Pinned, StringComparison.Ordinal)
                        ? ArtifactInstallHistoryOperations.Pin
                        : ArtifactInstallHistoryOperations.Install,
                    Install: persistedInstall,
                    AppliedAtUtc: appliedAtUtc)));

        return new RulePackInstallReceipt(
            PackId: preview.PackId,
            RulesetId: preview.RulesetId,
            Target: target,
            Outcome: RulePackInstallOutcomes.Applied,
            Install: persistedInstall,
            Preview: preview);
    }

    private static string ResolveInstallState(RuleProfileApplyTarget target)
    {
        return string.Equals(target.TargetKind, RuleProfileApplyTargetKinds.GlobalDefaults, StringComparison.Ordinal)
            ? ArtifactInstallStates.Pinned
            : ArtifactInstallStates.Installed;
    }

    private static string ResolveRulesetId(RulePackRegistryEntry entry, string? rulesetId)
    {
        return RulesetDefaults.NormalizeOptional(rulesetId)
            ?? entry.Manifest.Targets.FirstOrDefault()
            ?? throw new InvalidOperationException($"RulePack '{entry.Manifest.PackId}' did not declare any target rulesets.");
    }

    private static List<RuntimeInspectorWarning> BuildWarnings(RulePackRegistryEntry entry)
    {
        List<RuntimeInspectorWarning> warnings = [];
        if (string.Equals(entry.Publication.Visibility, ArtifactVisibilityModes.LocalOnly, StringComparison.Ordinal))
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.Trust,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "rulepack.install.warning.local-only",
                subjectId: entry.Manifest.PackId,
                ("packId", entry.Manifest.PackId),
                ("visibility", entry.Publication.Visibility)));
        }

        if (entry.Manifest.Capabilities.Count == 0)
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.ProviderBinding,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "rulepack.install.warning.content-only",
                subjectId: entry.Manifest.PackId,
                ("packId", entry.Manifest.PackId),
                ("version", entry.Manifest.Version)));
        }

        return warnings;
    }

    private static RulePackInstallPreviewItem PreviewItem(
        string kind,
        string summaryKey,
        string subjectId,
        bool requiresConfirmation = false,
        params (string Name, object? Value)[] parameters)
    {
        return new RulePackInstallPreviewItem(
            Kind: kind,
            Summary: summaryKey,
            SubjectId: subjectId,
            RequiresConfirmation: requiresConfirmation,
            SummaryKey: summaryKey,
            SummaryParameters: parameters.Select(static parameter => Param(parameter.Name, parameter.Value)).ToArray());
    }

    private static RuntimeInspectorWarning Warning(
        string kind,
        string severity,
        string messageKey,
        string subjectId,
        params (string Name, object? Value)[] parameters)
    {
        return new RuntimeInspectorWarning(
            Kind: kind,
            Severity: severity,
            Message: messageKey,
            SubjectId: subjectId,
            MessageKey: messageKey,
            MessageParameters: parameters.Select(static parameter => Param(parameter.Name, parameter.Value)).ToArray());
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
