using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Hub;

public sealed class DefaultHubInstallPreviewService : IHubInstallPreviewService
{
    private readonly IRulesetPluginRegistry _rulesetPluginRegistry;
    private readonly IRulePackInstallService _rulePackInstallService;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRuleProfileApplicationService _ruleProfileApplicationService;
    private readonly IRuntimeLockInstallService _runtimeLockInstallService;
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;
    private readonly IRulePackRegistryService _rulePackRegistryService;
    private readonly IBuildKitRegistryService _buildKitRegistryService;

    public DefaultHubInstallPreviewService(
        IRulesetPluginRegistry rulesetPluginRegistry,
        IRulePackInstallService rulePackInstallService,
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRuleProfileApplicationService ruleProfileApplicationService,
        IRuntimeLockInstallService runtimeLockInstallService,
        IRuntimeLockRegistryService runtimeLockRegistryService,
        IRulePackRegistryService rulePackRegistryService,
        IBuildKitRegistryService buildKitRegistryService)
    {
        _rulesetPluginRegistry = rulesetPluginRegistry;
        _rulePackInstallService = rulePackInstallService;
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _ruleProfileApplicationService = ruleProfileApplicationService;
        _runtimeLockInstallService = runtimeLockInstallService;
        _runtimeLockRegistryService = runtimeLockRegistryService;
        _rulePackRegistryService = rulePackRegistryService;
        _buildKitRegistryService = buildKitRegistryService;
    }

    public HubProjectInstallPreviewReceipt? Preview(OwnerScope owner, string kind, string itemId, RuleProfileApplyTarget target, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.TargetId);

        string normalizedKind = HubCatalogItemKinds.NormalizeRequired(kind);

        return normalizedKind switch
        {
            HubCatalogItemKinds.RuleProfile => PreviewRuleProfile(owner, itemId, target, rulesetId),
            HubCatalogItemKinds.RuntimeLock => PreviewRuntimeLock(owner, itemId, target, rulesetId),
            HubCatalogItemKinds.RulePack => PreviewRulePack(owner, itemId, target, rulesetId),
            HubCatalogItemKinds.BuildKit => PreviewBuildKit(owner, itemId, target, rulesetId),
            _ => null
        };
    }

    private HubProjectInstallPreviewReceipt? PreviewRuleProfile(OwnerScope owner, string itemId, RuleProfileApplyTarget target, string? rulesetId)
    {
        RuleProfilePreviewReceipt? preview = _ruleProfileApplicationService.Preview(owner, itemId, target, rulesetId);
        if (preview is null)
        {
            return null;
        }

        List<HubProjectInstallPreviewDiagnostic> diagnostics = preview.Warnings
            .Select(warning => new HubProjectInstallPreviewDiagnostic(warning.Kind, warning.Severity, warning.Message, warning.SubjectId))
            .ToList();
        bool requiresConfirmation = preview.RequiresConfirmation;
        RuleProfileRegistryEntry? entry = _ruleProfileRegistryService.Get(owner, itemId, rulesetId);
        if (entry is not null && !string.Equals(entry.Install.State, ArtifactInstallStates.Available, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateInstallStateDiagnostic(entry.Install, itemId));
            requiresConfirmation = true;
        }

        return new HubProjectInstallPreviewReceipt(
            Kind: HubCatalogItemKinds.RuleProfile,
            ItemId: itemId,
            Target: target,
            State: HubProjectInstallPreviewStates.Ready,
            Changes: preview.Changes.Select(change => new HubProjectInstallPreviewChange(change.Kind, change.Summary, change.SubjectId ?? itemId, change.RequiresConfirmation)).ToArray(),
            Diagnostics: diagnostics,
            RuntimeFingerprint: preview.RuntimeLock.RuntimeFingerprint,
            RequiresConfirmation: requiresConfirmation);
    }

    private HubProjectInstallPreviewReceipt? PreviewRuntimeLock(OwnerScope owner, string itemId, RuleProfileApplyTarget target, string? rulesetId)
    {
        RuntimeLockInstallPreviewReceipt? preview = _runtimeLockInstallService.Preview(owner, itemId, target, rulesetId);
        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, itemId, rulesetId);
        if (preview is null || entry is null)
        {
            return null;
        }

        List<HubProjectInstallPreviewChange> changes = preview.Changes
            .Select(change => new HubProjectInstallPreviewChange(
                Kind: change.Kind,
                Summary: change.Summary,
                SubjectId: change.SubjectId,
                RequiresConfirmation: change.RequiresConfirmation))
            .ToList();
        List<HubProjectInstallPreviewDiagnostic> diagnostics = preview.Warnings
            .Select(warning => new HubProjectInstallPreviewDiagnostic(warning.Kind, warning.Severity, warning.Message, warning.SubjectId))
            .ToList();
        bool requiresConfirmation = preview.RequiresConfirmation;
        if (!string.Equals(entry.Install.State, ArtifactInstallStates.Available, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateInstallStateDiagnostic(entry.Install, itemId));
            requiresConfirmation = true;
        }

        return new HubProjectInstallPreviewReceipt(
            Kind: HubCatalogItemKinds.RuntimeLock,
            ItemId: itemId,
            Target: target,
            State: HubProjectInstallPreviewStates.Ready,
            Changes: changes,
            Diagnostics: diagnostics,
            RuntimeFingerprint: preview.RuntimeLock.RuntimeFingerprint,
            RequiresConfirmation: requiresConfirmation);
    }

    private HubProjectInstallPreviewReceipt? PreviewRulePack(OwnerScope owner, string itemId, RuleProfileApplyTarget target, string? rulesetId)
    {
        RulePackInstallPreviewReceipt? preview = _rulePackInstallService.Preview(owner, itemId, target, rulesetId);
        RulePackRegistryEntry? entry = preview is null
            ? null
            : _rulePackRegistryService.Get(owner, itemId, preview.RulesetId);
        if (preview is null || entry is null)
        {
            return null;
        }

        List<HubProjectInstallPreviewDiagnostic> diagnostics = preview.Warnings
            .Select(warning => new HubProjectInstallPreviewDiagnostic(warning.Kind, warning.Severity, warning.Message, warning.SubjectId))
            .ToList();
        bool requiresConfirmation = preview.RequiresConfirmation;
        if (!string.Equals(entry.Install.State, ArtifactInstallStates.Available, StringComparison.Ordinal))
        {
            diagnostics.Add(CreateInstallStateDiagnostic(entry.Install, itemId));
            requiresConfirmation = true;
        }

        return new HubProjectInstallPreviewReceipt(
            Kind: HubCatalogItemKinds.RulePack,
            ItemId: itemId,
            Target: target,
            State: HubProjectInstallPreviewStates.Ready,
            Changes: preview.Changes
                .Select(change => new HubProjectInstallPreviewChange(
                    Kind: change.Kind,
                    Summary: change.Summary,
                    SubjectId: change.SubjectId,
                    RequiresConfirmation: change.RequiresConfirmation))
                .ToArray(),
            Diagnostics: diagnostics,
            RequiresConfirmation: requiresConfirmation);
    }

    private HubProjectInstallPreviewReceipt? PreviewBuildKit(OwnerScope owner, string itemId, RuleProfileApplyTarget target, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            BuildKitRegistryEntry? entry = _buildKitRegistryService.Get(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            return CreateDeferredReceipt(
                kind: HubCatalogItemKinds.BuildKit,
                itemId: itemId,
                target: target,
                deferredReason: "hub_buildkit_apply_preview_not_implemented",
                message: $"BuildKit apply preview is not implemented yet for '{entry.Manifest.BuildKitId}'.");
        }

        return null;
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

    private static HubProjectInstallPreviewReceipt CreateDeferredReceipt(
        string kind,
        string itemId,
        RuleProfileApplyTarget target,
        string deferredReason,
        string message)
    {
        return new HubProjectInstallPreviewReceipt(
            Kind: kind,
            ItemId: itemId,
            Target: target,
            State: HubProjectInstallPreviewStates.Deferred,
            Changes:
            [
                new HubProjectInstallPreviewChange(
                    Kind: HubProjectInstallPreviewChangeKinds.InstallDeferred,
                    Summary: message,
                    SubjectId: itemId)
            ],
            Diagnostics:
            [
                new HubProjectInstallPreviewDiagnostic(
                    Kind: HubProjectInstallPreviewDiagnosticKinds.Installability,
                    Severity: HubProjectInstallPreviewDiagnosticSeverityLevels.Info,
                    Message: message,
                    SubjectId: itemId)
            ],
            DeferredReason: deferredReason);
    }

    private static HubProjectInstallPreviewDiagnostic CreateInstallStateDiagnostic(ArtifactInstallState install, string itemId)
    {
        string message = install.State switch
        {
            ArtifactInstallStates.Pinned => $"Artifact is already pinned for target '{install.InstalledTargetId ?? itemId}'.",
            ArtifactInstallStates.Installed => $"Artifact is already installed for target '{install.InstalledTargetId ?? itemId}'.",
            _ => $"Artifact install state is '{install.State}'."
        };

        return new HubProjectInstallPreviewDiagnostic(
            Kind: HubProjectInstallPreviewDiagnosticKinds.InstallState,
            Severity: HubProjectInstallPreviewDiagnosticSeverityLevels.Info,
            Message: message,
            SubjectId: itemId);
    }
}
