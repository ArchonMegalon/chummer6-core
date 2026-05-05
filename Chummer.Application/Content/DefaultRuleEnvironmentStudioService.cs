using System.Linq;
using Chummer.Application.Explain;
using Chummer.Contracts.Content;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuleEnvironmentStudioService : IRuleEnvironmentStudioService
{
    private readonly IRuleProfileApplicationService _ruleProfileApplicationService;
    private readonly IRuntimeInspectorService _runtimeInspectorService;
    private readonly IRuntimeLockDiffService _runtimeLockDiffService;
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;

    public DefaultRuleEnvironmentStudioService(
        IRuleProfileApplicationService ruleProfileApplicationService,
        IRuntimeInspectorService runtimeInspectorService,
        IRuntimeLockDiffService runtimeLockDiffService,
        IRuntimeLockRegistryService runtimeLockRegistryService)
    {
        _ruleProfileApplicationService = ruleProfileApplicationService;
        _runtimeInspectorService = runtimeInspectorService;
        _runtimeLockDiffService = runtimeLockDiffService;
        _runtimeLockRegistryService = runtimeLockRegistryService;
    }

    public RuleEnvironmentStudioProjection? GetProfileProjection(
        OwnerScope owner,
        string profileId,
        RuleProfileApplyTarget target,
        string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(target);

        RuleProfilePreviewReceipt? preview = _ruleProfileApplicationService.Preview(owner, profileId, target, rulesetId);
        RuntimeInspectorProjection? inspector = _runtimeInspectorService.GetProfileProjection(owner, profileId, rulesetId);
        if (preview is null || inspector is null)
        {
            return null;
        }

        RuntimeLockRegistryEntry? currentRuntime = FindCurrentRuntime(owner, target, inspector.RuntimeLock.RulesetId);
        RuleEnvironmentStudioDiffProjection diff = BuildDiff(currentRuntime, inspector.RuntimeLock);
        RuleEnvironmentStudioLifecycleProjection lifecycle = BuildLifecycle(inspector.Promotion);
        RuleEnvironmentStudioExplainReceiptProjection explainReceipt = BuildExplainReceipt(inspector, preview, lifecycle, diff);

        return new RuleEnvironmentStudioProjection(
            ProfileId: preview.ProfileId,
            RulesetId: inspector.RuntimeLock.RulesetId,
            Target: preview.Target,
            RuntimeInspector: inspector,
            Preview: preview,
            Lifecycle: lifecycle,
            Diff: diff,
            ExplainReceipt: explainReceipt,
            CurrentRuntime: currentRuntime);
    }

    private RuntimeLockRegistryEntry? FindCurrentRuntime(OwnerScope owner, RuleProfileApplyTarget target, string rulesetId)
    {
        return _runtimeLockRegistryService.List(owner, rulesetId).Entries
            .Where(entry =>
                string.Equals(entry.Install.InstalledTargetKind, target.TargetKind, StringComparison.Ordinal)
                && string.Equals(entry.Install.InstalledTargetId, target.TargetId, StringComparison.Ordinal))
            .OrderByDescending(static entry => entry.Install.InstalledAtUtc ?? DateTimeOffset.MinValue)
            .ThenBy(static entry => entry.LockId, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private RuleEnvironmentStudioDiffProjection BuildDiff(RuntimeLockRegistryEntry? currentRuntime, ResolvedRuntimeLock desiredRuntime)
    {
        if (currentRuntime is null)
        {
            return new RuleEnvironmentStudioDiffProjection(
                Status: RuleEnvironmentStudioDiffStatuses.FirstPin,
                DesiredRuntimeFingerprint: desiredRuntime.RuntimeFingerprint,
                SummaryKey: "ruleenvironment.studio.diff.first-pin",
                SummaryParameters:
                [
                    Param("desiredRuntimeFingerprint", desiredRuntime.RuntimeFingerprint)
                ]);
        }

        RuntimeLockDiffProjection delta = _runtimeLockDiffService.Diff(currentRuntime.RuntimeLock, desiredRuntime);
        string status = delta.Changes.Count == 0
            ? RuleEnvironmentStudioDiffStatuses.Clear
            : RuleEnvironmentStudioDiffStatuses.RequiresReview;

        return new RuleEnvironmentStudioDiffProjection(
            Status: status,
            DesiredRuntimeFingerprint: desiredRuntime.RuntimeFingerprint,
            CurrentRuntimeFingerprint: currentRuntime.RuntimeLock.RuntimeFingerprint,
            Delta: delta,
            SummaryKey: status == RuleEnvironmentStudioDiffStatuses.Clear
                ? "ruleenvironment.studio.diff.clear"
                : "ruleenvironment.studio.diff.requires-review",
            SummaryParameters:
            [
                Param("currentRuntimeFingerprint", currentRuntime.RuntimeLock.RuntimeFingerprint),
                Param("desiredRuntimeFingerprint", desiredRuntime.RuntimeFingerprint),
                Param("changeCount", delta.Changes.Count)
            ]);
    }

    private static RuleEnvironmentStudioLifecycleProjection BuildLifecycle(RuntimeInspectorPromotionProjection? promotion)
    {
        if (promotion is null)
        {
            return new RuleEnvironmentStudioLifecycleProjection(
                CurrentStage: RuntimeInspectorPromotionStages.Sandbox,
                PromotionTargetStage: RuntimeInspectorPromotionStages.CampaignApproved,
                UpdateChannel: RuleProfileUpdateChannels.Preview,
                PublicationStatus: RuleProfilePublicationStatuses.Draft,
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PromotionSummary: "Rule environment is still in sandbox posture.",
                RollbackSummary: "No governed rollback path is available yet.",
                LineageSummary: "Lineage is not published yet.");
        }

        return new RuleEnvironmentStudioLifecycleProjection(
            CurrentStage: promotion.CurrentStage ?? RuntimeInspectorPromotionStages.Sandbox,
            PromotionTargetStage: promotion.PromotionTargetStage ?? RuntimeInspectorPromotionStages.CampaignApproved,
            UpdateChannel: promotion.UpdateChannel,
            PublicationStatus: promotion.PublicationStatus,
            Visibility: promotion.Visibility,
            PromotionSummary: promotion.PromotionSummary,
            RollbackSummary: promotion.RollbackSummary,
            LineageSummary: promotion.LineageSummary,
            PublishedAtUtc: promotion.PublishedAtUtc);
    }

    private static RuleEnvironmentStudioExplainReceiptProjection BuildExplainReceipt(
        RuntimeInspectorProjection inspector,
        RuleProfilePreviewReceipt preview,
        RuleEnvironmentStudioLifecycleProjection lifecycle,
        RuleEnvironmentStudioDiffProjection diff)
    {
        HashSet<string> requiredCoverageKinds =
        [
            ExplainValuePacketCoverageKinds.MechanicalResult,
            ExplainValuePacketCoverageKinds.SourceAnchor
        ];

        if (preview.Warnings.Count > 0 || inspector.Warnings.Count > 0)
        {
            requiredCoverageKinds.Add(ExplainValuePacketCoverageKinds.Warning);
        }

        bool deltaIncluded = diff.Delta is { Changes.Count: > 0 };
        if (deltaIncluded)
        {
            requiredCoverageKinds.Add(ExplainValuePacketCoverageKinds.BeforeAfterDelta);
        }

        return new RuleEnvironmentStudioExplainReceiptProjection(
            SourceKind: RuleEnvironmentStudioExplainSources.Engine,
            RuntimeFingerprint: inspector.RuntimeLock.RuntimeFingerprint,
            PrivacyMode: CalculationReportPrivacyModes.SupportCase,
            DiffStatus: diff.Status,
            CurrentStage: lifecycle.CurrentStage,
            PromotionTargetStage: lifecycle.PromotionTargetStage,
            SummaryKey: "ruleenvironment.studio.explain.engine-truth",
            SummaryParameters:
            [
                Param("profileId", preview.ProfileId),
                Param("runtimeFingerprint", inspector.RuntimeLock.RuntimeFingerprint),
                Param("targetKind", preview.Target.TargetKind),
                Param("targetId", preview.Target.TargetId),
                Param("diffStatus", diff.Status),
                Param("currentStage", lifecycle.CurrentStage),
                Param("promotionTargetStage", lifecycle.PromotionTargetStage),
                Param("deltaIncluded", deltaIncluded),
                Param("warningCount", preview.Warnings.Count + inspector.Warnings.Count)
            ],
            RequiredCoverageKinds: requiredCoverageKinds.OrderBy(static kind => kind, StringComparer.Ordinal).ToArray(),
            CounterfactualLimit: DefaultExplainValuePacketService.MaxCounterfactuals,
            DeltaIncluded: deltaIncluded);
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
