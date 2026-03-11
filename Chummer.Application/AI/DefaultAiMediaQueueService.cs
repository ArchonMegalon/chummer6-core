using System;
using System.Collections.Generic;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiMediaQueueService : IAiMediaQueueService
{
    private readonly IAiDigestService _aiDigestService;
    private readonly IAiPortraitPromptService _portraitPromptService;
    private readonly IAiMediaJobService _mediaJobService;

    public DefaultAiMediaQueueService(
        IAiDigestService aiDigestService,
        IAiPortraitPromptService portraitPromptService,
        IAiMediaJobService mediaJobService)
    {
        _aiDigestService = aiDigestService;
        _portraitPromptService = portraitPromptService;
        _mediaJobService = mediaJobService;
    }

    public AiMediaQueueReceipt? QueueMediaJob(OwnerScope owner, AiMediaQueueRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? characterId = NormalizeOptional(request.CharacterId);
        string? normalizedJobType = NormalizeJobType(request.JobType);
        if (characterId is null || normalizedJobType is null)
        {
            return null;
        }

        AiCharacterDigestProjection? characterDigest = _aiDigestService.GetCharacterDigest(owner, characterId);
        if (characterDigest is null)
        {
            return null;
        }

        string runtimeFingerprint = NormalizeOptional(request.RuntimeFingerprint) ?? characterDigest.RuntimeFingerprint;
        AiRuntimeSummaryProjection? runtimeSummary = _aiDigestService.GetRuntimeSummary(owner, runtimeFingerprint, characterDigest.RulesetId);
        if (runtimeSummary is null)
        {
            return null;
        }

        AiSessionDigestProjection? sessionDigest = _aiDigestService.GetSessionDigest(owner, characterId);
        string prompt = BuildPrompt(owner, normalizedJobType, request, characterDigest, runtimeSummary);
        Dictionary<string, string> options = BuildOptions(request, normalizedJobType);
        AiMediaJobRequest mediaJobRequest = new(
            Prompt: prompt,
            CharacterId: characterDigest.CharacterId,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            StylePackId: NormalizeOptional(request.StylePackId),
            Options: options);
        AiApiResult<AiMediaJobReceipt> downstream = QueueUnderlying(owner, normalizedJobType, mediaJobRequest);
        string queueId = downstream.Payload?.JobId ?? BuildQueueId(normalizedJobType, characterDigest.CharacterId, runtimeSummary.RuntimeFingerprint);
        string state = downstream.IsImplemented
            ? downstream.Payload?.State ?? AiMediaQueueStates.Queued
            : AiMediaQueueStates.Scaffolded;
        string summary = downstream.IsImplemented
            ? downstream.Payload?.Message ?? $"Queued {normalizedJobType} media job for {characterDigest.DisplayName}."
            : $"Prepared a grounded {normalizedJobType} media queue draft for {characterDigest.DisplayName}; downstream media execution is still deferred.";

        return new AiMediaQueueReceipt(
            QueueId: queueId,
            Operation: AiMediaQueueApiOperations.QueueMediaJob,
            JobType: normalizedJobType,
            State: state,
            CharacterId: characterDigest.CharacterId,
            CharacterDisplayName: characterDigest.DisplayName,
            RulesetId: runtimeSummary.RulesetId,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            Prompt: prompt,
            Options: options,
            Evidence: BuildEvidence(runtimeSummary, characterDigest, sessionDigest),
            Risks: BuildRisks(runtimeSummary.RuntimeFingerprint, characterDigest.RuntimeFingerprint, downstream),
            ApprovalRequired: downstream.Payload?.ApprovalRequired ?? true,
            UnderlyingOperation: ResolveUnderlyingOperation(normalizedJobType),
            UnderlyingState: downstream.Payload?.State);
    }

    private string BuildPrompt(
        OwnerScope owner,
        string jobType,
        AiMediaQueueRequest request,
        AiCharacterDigestProjection characterDigest,
        AiRuntimeSummaryProjection runtimeSummary)
    {
        string? prompt = NormalizeOptional(request.Prompt);
        if (prompt is not null)
        {
            return prompt;
        }

        if (string.Equals(jobType, AiMediaJobTypes.Portrait, StringComparison.Ordinal))
        {
            AiPortraitPromptProjection? portraitPrompt = _portraitPromptService.CreatePortraitPrompt(
                owner,
                new AiPortraitPromptRequest(
                    CharacterId: characterDigest.CharacterId,
                    RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
                    StylePackId: request.StylePackId));
            if (portraitPrompt is not null)
            {
                return portraitPrompt.Prompt;
            }
        }

        return jobType switch
        {
            AiMediaJobTypes.Dossier => $"Mission dossier package for {characterDigest.DisplayName} under runtime '{runtimeSummary.Title}'. Include profile-ready typography, faction-safe labels, and approval-friendly summaries.",
            AiMediaJobTypes.RouteVideo => $"Route video brief for {characterDigest.DisplayName} under runtime '{runtimeSummary.Title}'. Focus on travel beats, scene transitions, and session-safe storytelling.",
            _ => $"Portrait prompt for {characterDigest.DisplayName} grounded by runtime '{runtimeSummary.Title}' and a clean decker-contact dossier presentation."
        };
    }

    private static Dictionary<string, string> BuildOptions(AiMediaQueueRequest request, string jobType)
    {
        Dictionary<string, string> options = request.Options is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Options, StringComparer.Ordinal);

        options["jobType"] = jobType;
        if (!string.IsNullOrWhiteSpace(request.StylePackId))
        {
            options["stylePackId"] = request.StylePackId.Trim();
        }

        return options;
    }

    private AiApiResult<AiMediaJobReceipt> QueueUnderlying(OwnerScope owner, string jobType, AiMediaJobRequest request)
    {
        return jobType switch
        {
            AiMediaJobTypes.Portrait => _mediaJobService.QueuePortraitJob(owner, request),
            AiMediaJobTypes.Dossier => _mediaJobService.QueueDossierJob(owner, request),
            _ => _mediaJobService.QueueRouteVideoJob(owner, request)
        };
    }

    private static IReadOnlyList<AiEvidenceEntry> BuildEvidence(
        AiRuntimeSummaryProjection runtimeSummary,
        AiCharacterDigestProjection characterDigest,
        AiSessionDigestProjection? sessionDigest)
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
        AiApiResult<AiMediaJobReceipt> downstream)
    {
        List<AiRiskEntry> risks =
        [
            new(
                Severity: AiRiskSeverities.Warning,
                Title: "Approval required",
                Summary: "Any generated portrait, dossier, or route-video output remains a derived draft until explicit approval is recorded.")
        ];

        if (!string.Equals(runtimeFingerprint, characterRuntimeFingerprint, StringComparison.Ordinal))
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "Runtime mismatch",
                Summary: $"Requested runtime {runtimeFingerprint} does not match character digest runtime {characterRuntimeFingerprint}."));
        }

        if (!downstream.IsImplemented && downstream.NotImplemented is not null)
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Note,
                Title: "Media pipeline deferred",
                Summary: downstream.NotImplemented.Message));
        }

        return risks;
    }

    private static string ResolveUnderlyingOperation(string jobType)
    {
        return jobType switch
        {
            AiMediaJobTypes.Portrait => AiMediaApiOperations.QueuePortraitJob,
            AiMediaJobTypes.Dossier => AiMediaApiOperations.QueueDossierJob,
            _ => AiMediaApiOperations.QueueRouteVideoJob
        };
    }

    private static string BuildQueueId(string jobType, string characterId, string runtimeFingerprint)
        => $"ai-media-queue:{jobType}:{characterId}:{runtimeFingerprint}";

    private static string? NormalizeJobType(string? jobType)
    {
        string? normalized = NormalizeOptional(jobType);
        if (normalized is null)
        {
            return null;
        }

        if (string.Equals(normalized, AiMediaJobTypes.Portrait, StringComparison.OrdinalIgnoreCase))
        {
            return AiMediaJobTypes.Portrait;
        }

        if (string.Equals(normalized, AiMediaJobTypes.Dossier, StringComparison.OrdinalIgnoreCase))
        {
            return AiMediaJobTypes.Dossier;
        }

        if (string.Equals(normalized, AiMediaJobTypes.RouteVideo, StringComparison.OrdinalIgnoreCase))
        {
            return AiMediaJobTypes.RouteVideo;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
