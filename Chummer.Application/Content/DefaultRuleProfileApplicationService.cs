using System.Linq;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuleProfileApplicationService : IRuleProfileApplicationService
{
    private readonly IRuleProfileInstallHistoryStore _installHistoryStore;
    private readonly IRuleProfileInstallStateStore _installStateStore;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRuntimeLockInstallService _runtimeLockInstallService;

    public DefaultRuleProfileApplicationService(
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRuntimeLockInstallService runtimeLockInstallService,
        IRuleProfileInstallStateStore installStateStore,
        IRuleProfileInstallHistoryStore installHistoryStore)
    {
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _runtimeLockInstallService = runtimeLockInstallService;
        _installStateStore = installStateStore;
        _installHistoryStore = installHistoryStore;
    }

    public RuleProfilePreviewReceipt? Preview(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetId);

        RuleProfileRegistryEntry? entry = _ruleProfileRegistryService.Get(owner, profileId, rulesetId);
        return entry is null ? null : CreatePreview(entry, target);
    }

    public RuleProfileApplyReceipt? Apply(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetId);

        RuleProfileRegistryEntry? entry = _ruleProfileRegistryService.Get(owner, profileId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        RuleProfilePreviewReceipt preview = CreatePreview(entry, target);
        string resolvedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? entry.Manifest.RulesetId;
        RuntimeLockInstallReceipt? installReceipt = _runtimeLockInstallService.Apply(
            owner,
            entry.Manifest.RuntimeLock.RuntimeFingerprint,
            target,
            resolvedRulesetId);
        if (installReceipt is null)
        {
            return new RuleProfileApplyReceipt(
                ProfileId: preview.ProfileId,
                Target: preview.Target,
                Outcome: RuleProfileApplyOutcomes.Blocked,
                Preview: preview);
        }

        if (string.Equals(installReceipt.Outcome, RuntimeLockInstallOutcomes.Blocked, StringComparison.Ordinal))
        {
            return new RuleProfileApplyReceipt(
                ProfileId: preview.ProfileId,
                Target: preview.Target,
                Outcome: RuleProfileApplyOutcomes.Blocked,
                Preview: preview,
                InstallReceipt: installReceipt);
        }

        PersistProfileInstall(owner, entry, target, resolvedRulesetId, installReceipt);

        return new RuleProfileApplyReceipt(
            ProfileId: preview.ProfileId,
            Target: preview.Target,
            Outcome: RuleProfileApplyOutcomes.Applied,
            Preview: preview,
            InstallReceipt: installReceipt);
    }

    private RuleProfilePreviewReceipt CreatePreview(RuleProfileRegistryEntry entry, RuleProfileApplyTarget target)
    {
        RuleProfilePreviewItem[] changes = BuildPreviewChanges(entry, target);
        RuntimeInspectorWarning[] warnings = BuildWarnings(entry);

        return new RuleProfilePreviewReceipt(
            ProfileId: entry.Manifest.ProfileId,
            Target: target,
            RuntimeLock: entry.Manifest.RuntimeLock,
            Changes: changes,
            Warnings: warnings,
            RequiresConfirmation: changes.Any(change => change.RequiresConfirmation));
    }

    private void PersistProfileInstall(
        OwnerScope owner,
        RuleProfileRegistryEntry entry,
        RuleProfileApplyTarget target,
        string resolvedRulesetId,
        RuntimeLockInstallReceipt installReceipt)
    {
        ArtifactInstallState current = entry.Install;
        string runtimeFingerprint = installReceipt.RuntimeLock.RuntimeFingerprint;

        if (string.Equals(current.State, ArtifactInstallStates.Pinned, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetKind, target.TargetKind, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetId, target.TargetId, StringComparison.Ordinal)
            && string.Equals(current.RuntimeFingerprint, runtimeFingerprint, StringComparison.Ordinal)
            && current.InstalledAtUtc is not null)
        {
            return;
        }

        DateTimeOffset appliedAtUtc = installReceipt.InstalledAtUtc;
        ArtifactInstallState install = new(
            State: ArtifactInstallStates.Pinned,
            InstalledAtUtc: current.InstalledAtUtc ?? appliedAtUtc,
            InstalledTargetKind: target.TargetKind,
            InstalledTargetId: target.TargetId,
            RuntimeFingerprint: runtimeFingerprint);
        ArtifactInstallState persistedInstall = _installStateStore.Upsert(
            owner,
            new RuleProfileInstallRecord(
                entry.Manifest.ProfileId,
                resolvedRulesetId,
                install)).Install;
        _installHistoryStore.Append(
            owner,
            new RuleProfileInstallHistoryRecord(
                entry.Manifest.ProfileId,
                resolvedRulesetId,
                new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: persistedInstall,
                    AppliedAtUtc: appliedAtUtc)));
    }

    private static RuleProfilePreviewItem[] BuildPreviewChanges(RuleProfileRegistryEntry entry, RuleProfileApplyTarget target)
    {
        List<RuleProfilePreviewItem> changes =
        [
            PreviewItem(
                kind: RuleProfilePreviewChangeKinds.RuntimeLockPinned,
                summaryKey: "ruleprofile.preview.runtime-lock-pinned",
                subjectId: entry.Manifest.RuntimeLock.RuntimeFingerprint,
                requiresConfirmation: false,
                ("profileId", entry.Manifest.ProfileId),
                ("runtimeFingerprint", entry.Manifest.RuntimeLock.RuntimeFingerprint),
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId))
        ];

        if (entry.Manifest.RulePacks.Count > 0)
        {
            changes.Add(PreviewItem(
                kind: RuleProfilePreviewChangeKinds.RulePackSelectionChanged,
                summaryKey: "ruleprofile.preview.rulepack-selection-changed",
                subjectId: entry.Manifest.ProfileId,
                requiresConfirmation: true,
                ("profileId", entry.Manifest.ProfileId),
                ("rulePackCount", entry.Manifest.RulePacks.Count)));
        }

        if (string.Equals(target.TargetKind, RuleProfileApplyTargetKinds.SessionLedger, StringComparison.Ordinal))
        {
            changes.Add(PreviewItem(
                kind: RuleProfilePreviewChangeKinds.SessionReplayRequired,
                summaryKey: "ruleprofile.preview.session-replay-required",
                subjectId: target.TargetId,
                requiresConfirmation: true,
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId)));
        }

        return changes.ToArray();
    }

    private static RuntimeInspectorWarning[] BuildWarnings(RuleProfileRegistryEntry entry)
    {
        List<RuntimeInspectorWarning> warnings = [];

        if (string.Equals(entry.Publication.Visibility, ArtifactVisibilityModes.LocalOnly, StringComparison.Ordinal))
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.Trust,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "ruleprofile.preview.warning.local-only",
                subjectId: entry.Manifest.ProfileId,
                ("profileId", entry.Manifest.ProfileId),
                ("visibility", entry.Publication.Visibility)));
        }

        if (entry.Manifest.RulePacks.Count == 0)
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.ProviderBinding,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "ruleprofile.preview.warning.builtin-only",
                subjectId: entry.Manifest.ProfileId,
                ("profileId", entry.Manifest.ProfileId),
                ("rulesetId", entry.Manifest.RulesetId)));
        }

        return warnings.ToArray();
    }

    private static RuleProfilePreviewItem PreviewItem(
        string kind,
        string summaryKey,
        string subjectId,
        bool requiresConfirmation = false,
        params (string Name, object? Value)[] parameters)
    {
        return new RuleProfilePreviewItem(
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
