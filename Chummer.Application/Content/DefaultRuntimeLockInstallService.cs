using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuntimeLockInstallService : IRuntimeLockInstallService
{
    private readonly IRuntimeLockInstallHistoryStore _installHistoryStore;
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;

    public DefaultRuntimeLockInstallService(
        IRuntimeLockRegistryService runtimeLockRegistryService,
        IRuntimeLockInstallHistoryStore installHistoryStore)
    {
        _runtimeLockRegistryService = runtimeLockRegistryService;
        _installHistoryStore = installHistoryStore;
    }

    public RuntimeLockInstallPreviewReceipt? Preview(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetId);

        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, lockId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        List<RuntimeLockInstallPreviewItem> changes =
        [
            PreviewItem(
                kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
                summaryKey: "runtime.lock.install.preview.runtime-lock-pinned",
                subjectId: entry.LockId,
                requiresConfirmation: false,
                ("lockId", entry.LockId),
                ("runtimeFingerprint", entry.RuntimeLock.RuntimeFingerprint),
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId))
        ];
        List<RuntimeInspectorWarning> warnings = BuildWarnings(entry);
        if (string.Equals(target.TargetKind, RuleProfileApplyTargetKinds.SessionLedger, StringComparison.Ordinal))
        {
            changes.Add(PreviewItem(
                kind: RuntimeLockInstallPreviewChangeKinds.SessionReplayRequired,
                summaryKey: "runtime.lock.install.preview.session-replay-required",
                subjectId: target.TargetId,
                requiresConfirmation: true,
                ("targetKind", target.TargetKind),
                ("targetId", target.TargetId)));
        }

        bool requiresConfirmation = changes.Any(change => change.RequiresConfirmation);
        return new RuntimeLockInstallPreviewReceipt(
            LockId: entry.LockId,
            Target: target,
            RuntimeLock: entry.RuntimeLock,
            Changes: changes,
            Warnings: warnings,
            RequiresConfirmation: requiresConfirmation);
    }

    public RuntimeLockInstallReceipt? Apply(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        RuntimeLockInstallPreviewReceipt? preview = Preview(owner, lockId, target, rulesetId);
        if (preview is null)
        {
            return null;
        }

        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, lockId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        ArtifactInstallState current = entry.Install;
        if (string.Equals(current.State, ArtifactInstallStates.Pinned, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetKind, target.TargetKind, StringComparison.Ordinal)
            && string.Equals(current.InstalledTargetId, target.TargetId, StringComparison.Ordinal))
        {
            return new RuntimeLockInstallReceipt(
                TargetKind: target.TargetKind,
                TargetId: target.TargetId,
                Outcome: RuntimeLockInstallOutcomes.Unchanged,
                RuntimeLock: preview.RuntimeLock,
                InstalledAtUtc: current.InstalledAtUtc ?? DateTimeOffset.UtcNow,
                RebindNotices: [],
                RequiresSessionReplay: false);
        }

        DateTimeOffset appliedAtUtc = DateTimeOffset.UtcNow;
        ArtifactInstallState install = new(
            State: ArtifactInstallStates.Pinned,
            InstalledAtUtc: appliedAtUtc,
            InstalledTargetKind: target.TargetKind,
            InstalledTargetId: target.TargetId,
            RuntimeFingerprint: preview.RuntimeLock.RuntimeFingerprint);
        RuntimeLockRegistryEntry persistedEntry = _runtimeLockRegistryService.Upsert(
            owner,
            entry.LockId,
            new RuntimeLockSaveRequest(
                Title: entry.Title,
                RuntimeLock: preview.RuntimeLock,
                Visibility: RuntimeLockCatalogKinds.Saved == entry.CatalogKind
                    ? entry.Visibility
                    : ArtifactVisibilityModes.LocalOnly,
                Description: entry.Description,
                Install: install));
        _installHistoryStore.Append(
            owner,
            new RuntimeLockInstallHistoryRecord(
                LockId: persistedEntry.LockId,
                RulesetId: persistedEntry.RuntimeLock.RulesetId,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: persistedEntry.Install,
                    AppliedAtUtc: appliedAtUtc)));

        return new RuntimeLockInstallReceipt(
            TargetKind: target.TargetKind,
            TargetId: target.TargetId,
            Outcome: string.Equals(current.State, ArtifactInstallStates.Available, StringComparison.Ordinal)
                ? RuntimeLockInstallOutcomes.Installed
                : RuntimeLockInstallOutcomes.Updated,
            RuntimeLock: preview.RuntimeLock,
            InstalledAtUtc: persistedEntry.Install.InstalledAtUtc ?? appliedAtUtc,
            RebindNotices: [],
            RequiresSessionReplay: string.Equals(target.TargetKind, RuleProfileApplyTargetKinds.SessionLedger, StringComparison.Ordinal));
    }

    private static List<RuntimeInspectorWarning> BuildWarnings(RuntimeLockRegistryEntry entry)
    {
        List<RuntimeInspectorWarning> warnings = [];
        if (entry.RuntimeLock.RulePacks.Count == 0)
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.ProviderBinding,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "runtime.lock.install.warning.builtin-only",
                subjectId: entry.LockId,
                ("lockId", entry.LockId),
                ("runtimeFingerprint", entry.RuntimeLock.RuntimeFingerprint)));
        }

        if (string.Equals(entry.Visibility, ArtifactVisibilityModes.LocalOnly, StringComparison.Ordinal))
        {
            warnings.Add(Warning(
                kind: RuntimeInspectorWarningKinds.Trust,
                severity: RuntimeInspectorWarningSeverityLevels.Info,
                messageKey: "runtime.lock.install.warning.local-only",
                subjectId: entry.LockId,
                ("lockId", entry.LockId),
                ("visibility", entry.Visibility)));
        }

        return warnings;
    }

    private static RuntimeLockInstallPreviewItem PreviewItem(
        string kind,
        string summaryKey,
        string subjectId,
        bool requiresConfirmation = false,
        params (string Name, object? Value)[] parameters)
    {
        return new RuntimeLockInstallPreviewItem(
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
