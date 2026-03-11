using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiActionPreviewService : IAiActionPreviewService
{
    private readonly IAiDigestService _aiDigestService;

    public DefaultAiActionPreviewService(IAiDigestService aiDigestService)
    {
        _aiDigestService = aiDigestService;
    }

    public AiActionPreviewReceipt? PreviewKarmaSpend(OwnerScope owner, AiSpendPlanPreviewRequest? request)
        => CreateSpendPreview(owner, request, AiActionPreviewApiOperations.PreviewKarmaSpend, AiActionPreviewKinds.KarmaSpend, "karma");

    public AiActionPreviewReceipt? PreviewNuyenSpend(OwnerScope owner, AiSpendPlanPreviewRequest? request)
        => CreateSpendPreview(owner, request, AiActionPreviewApiOperations.PreviewNuyenSpend, AiActionPreviewKinds.NuyenSpend, "nuyen");

    public AiActionPreviewReceipt? CreateApplyPreview(OwnerScope owner, AiApplyPreviewRequest? request)
    {
        if (request is null || request.ActionDraft is null || string.IsNullOrWhiteSpace(request.CharacterId) || string.IsNullOrWhiteSpace(request.RuntimeFingerprint))
        {
            return null;
        }

        AiCharacterDigestProjection? characterDigest = _aiDigestService.GetCharacterDigest(owner, request.CharacterId);
        AiRuntimeSummaryProjection? runtimeSummary = _aiDigestService.GetRuntimeSummary(owner, request.RuntimeFingerprint, characterDigest?.RulesetId);
        if (characterDigest is null || runtimeSummary is null)
        {
            return null;
        }

        AiSessionDigestProjection? sessionDigest = _aiDigestService.GetSessionDigest(owner, request.CharacterId);
        string? workspaceId = request.WorkspaceId ?? request.ActionDraft.WorkspaceId;
        List<string> effects =
        [
            $"Prepared a non-mutating apply preview for '{request.ActionDraft.Title}'.",
            "No workspace, character, or runtime mutation was executed."
        ];
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            effects.Add($"Workbench origin preserved from workspace {workspaceId}.");
        }

        return new AiActionPreviewReceipt(
            PreviewId: BuildPreviewId(AiActionPreviewKinds.ApplyPreview, characterDigest.CharacterId, runtimeSummary.RuntimeFingerprint, workspaceId),
            Operation: AiActionPreviewApiOperations.CreateApplyPreview,
            PreviewKind: AiActionPreviewKinds.ApplyPreview,
            CharacterId: characterDigest.CharacterId,
            CharacterDisplayName: characterDigest.DisplayName,
            RulesetId: characterDigest.RulesetId,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            State: AiActionPreviewStates.Scaffolded,
            Summary: $"Scaffolded apply preview for {characterDigest.DisplayName} against runtime {runtimeSummary.RuntimeFingerprint}.",
            StepCount: 1,
            TotalRequested: null,
            Unit: null,
            PreparedEffects: effects,
            Evidence: BuildEvidence(runtimeSummary, characterDigest, sessionDigest, workspaceId),
            Risks: BuildRisks(
                runtimeSummary.RuntimeFingerprint,
                characterDigest.RuntimeFingerprint,
                "This preview does not execute a real mutation path yet; it only packages grounded follow-up context."),
            RequiresConfirmation: request.ActionDraft.RequiresConfirmation,
            ProfileId: sessionDigest?.ProfileId,
            ProfileTitle: sessionDigest?.ProfileTitle,
            WorkspaceId: workspaceId);
    }

    private AiActionPreviewReceipt? CreateSpendPreview(
        OwnerScope owner,
        AiSpendPlanPreviewRequest? request,
        string operation,
        string previewKind,
        string unit)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.CharacterId) || string.IsNullOrWhiteSpace(request.RuntimeFingerprint))
        {
            return null;
        }

        AiCharacterDigestProjection? characterDigest = _aiDigestService.GetCharacterDigest(owner, request.CharacterId);
        AiRuntimeSummaryProjection? runtimeSummary = _aiDigestService.GetRuntimeSummary(owner, request.RuntimeFingerprint, characterDigest?.RulesetId);
        if (characterDigest is null || runtimeSummary is null)
        {
            return null;
        }

        AiSessionDigestProjection? sessionDigest = _aiDigestService.GetSessionDigest(owner, request.CharacterId);
        string? workspaceId = request.WorkspaceId;
        IReadOnlyList<AiSpendPlanStep> steps = request.Steps ?? [];
        decimal? totalRequested = steps.Count == 0
            ? null
            : steps.Where(static step => step.Amount.HasValue).Sum(static step => step.Amount ?? 0m);
        List<string> effects =
        [
            $"Prepared a non-mutating {unit}-spend preview for {steps.Count} step(s).",
            totalRequested.HasValue
                ? $"If later promoted to a real simulator, the current draft would request {totalRequested.Value.ToString(CultureInfo.InvariantCulture)} {unit}."
                : $"No explicit {unit} total was supplied in the current draft."
        ];
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            effects.Add($"Workbench origin preserved from workspace {workspaceId}.");
        }

        return new AiActionPreviewReceipt(
            PreviewId: BuildPreviewId(previewKind, characterDigest.CharacterId, runtimeSummary.RuntimeFingerprint, workspaceId),
            Operation: operation,
            PreviewKind: previewKind,
            CharacterId: characterDigest.CharacterId,
            CharacterDisplayName: characterDigest.DisplayName,
            RulesetId: characterDigest.RulesetId,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            State: AiActionPreviewStates.Scaffolded,
            Summary: $"Scaffolded {unit}-spend preview for {characterDigest.DisplayName} against runtime {runtimeSummary.RuntimeFingerprint}.",
            StepCount: steps.Count,
            TotalRequested: totalRequested,
            Unit: unit,
            PreparedEffects: effects,
            Evidence: BuildEvidence(runtimeSummary, characterDigest, sessionDigest, workspaceId),
            Risks: BuildRisks(
                runtimeSummary.RuntimeFingerprint,
                characterDigest.RuntimeFingerprint,
                $"This preview does not execute a real {unit}-spend simulator yet; downstream derived values are still explanatory scaffolds."),
            RequiresConfirmation: true,
            ProfileId: sessionDigest?.ProfileId,
            ProfileTitle: sessionDigest?.ProfileTitle,
            WorkspaceId: workspaceId);
    }

    private static IReadOnlyList<AiEvidenceEntry> BuildEvidence(
        AiRuntimeSummaryProjection runtimeSummary,
        AiCharacterDigestProjection characterDigest,
        AiSessionDigestProjection? sessionDigest,
        string? workspaceId)
    {
        List<AiEvidenceEntry> evidence =
        [
            new(
                Title: "Runtime",
                Summary: $"{runtimeSummary.Title} ({runtimeSummary.RulesetId})",
                ReferenceId: runtimeSummary.RuntimeFingerprint,
                Source: AiRetrievalCorpusIds.Runtime),
            new(
                Title: "Character",
                Summary: characterDigest.DisplayName,
                ReferenceId: characterDigest.CharacterId,
                Source: "character-digest")
        ];

        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            evidence.Add(new AiEvidenceEntry(
                Title: "Workspace",
                Summary: $"Workbench origin preserved from {workspaceId}.",
                ReferenceId: workspaceId,
                Source: "workspace"));
        }

        if (sessionDigest is not null)
        {
            evidence.Add(new AiEvidenceEntry(
                Title: "Session",
                Summary: $"{sessionDigest.SelectionState}; bundle {sessionDigest.BundleFreshness}",
                ReferenceId: sessionDigest.CharacterId,
                Source: "session-digest"));
        }

        return evidence;
    }

    private static IReadOnlyList<AiRiskEntry> BuildRisks(
        string runtimeFingerprint,
        string characterRuntimeFingerprint,
        string scaffoldSummary)
    {
        List<AiRiskEntry> risks =
        [
            new(
                Severity: AiRiskSeverities.Warning,
                Title: "Scaffolded preview",
                Summary: scaffoldSummary)
        ];

        if (!string.Equals(runtimeFingerprint, characterRuntimeFingerprint, StringComparison.Ordinal))
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "Runtime mismatch",
                Summary: $"Requested runtime {runtimeFingerprint} does not match character digest runtime {characterRuntimeFingerprint}."));
        }

        return risks;
    }

    private static string BuildPreviewId(string previewKind, string characterId, string runtimeFingerprint, string? workspaceId)
        => string.IsNullOrWhiteSpace(workspaceId)
            ? $"{previewKind}:{characterId}:{runtimeFingerprint}"
            : $"{previewKind}:{characterId}:{runtimeFingerprint}:{workspaceId}";
}
