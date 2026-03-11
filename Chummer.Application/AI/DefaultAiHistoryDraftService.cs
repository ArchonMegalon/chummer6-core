using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiHistoryDraftService : IAiHistoryDraftService
{
    private readonly IAiDigestService _aiDigestService;
    private readonly ITranscriptProvider _transcriptProvider;

    public DefaultAiHistoryDraftService(
        IAiDigestService aiDigestService,
        ITranscriptProvider transcriptProvider)
    {
        _aiDigestService = aiDigestService;
        _transcriptProvider = transcriptProvider;
    }

    public AiHistoryDraftProjection? CreateHistoryDraft(OwnerScope owner, AiHistoryDraftRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string? characterId = NormalizeOptional(request.CharacterId);
        if (characterId is null)
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
        string? transcriptId = NormalizeOptional(request.TranscriptId);
        string? sessionId = NormalizeOptional(request.SessionId);
        AiApiResult<AiTranscriptDocumentReceipt>? transcript = transcriptId is null
            ? null
            : _transcriptProvider.GetTranscript(owner, transcriptId);
        string sourceKind = ResolveSourceKind(transcriptId, sessionId);
        string sourceId = transcriptId ?? sessionId ?? characterDigest.CharacterId;
        int maxEntries = Math.Clamp(request.MaxEntries, 1, 4);
        string focus = NormalizeOptional(request.Focus) ?? "canon-safe session fallout and character momentum";
        string sessionDescriptor = sessionDigest is null
            ? "No active session digest is available yet."
            : $"Session state is {sessionDigest.SelectionState} with bundle freshness '{sessionDigest.BundleFreshness}'.";
        string transcriptDescriptor = transcriptId is null
            ? "No transcript source was requested."
            : transcript is { IsImplemented: true, Payload: not null }
                ? $"Transcript {transcript.Payload.TranscriptId} is currently '{transcript.Payload.State}'."
                : $"Transcript {transcriptId} is still scaffolded; the tool is using the identifier as a source reference only.";
        string summary = $"Prepared scaffolded history drafts for {characterDigest.DisplayName} using {sourceKind} source '{sourceId}' under runtime {runtimeSummary.RuntimeFingerprint}.";

        AiHistoryDraftEntry[] availableEntries =
        [
            new(
                AiHistoryDraftEntryKinds.SessionRecap,
                $"{characterDigest.DisplayName} recap draft",
                $"Draft a concise recap that stays grounded in runtime '{runtimeSummary.Title}', focuses on {focus}, and keeps the tone aligned with current campaign notes. {sessionDescriptor} {transcriptDescriptor}"),
            new(
                AiHistoryDraftEntryKinds.TimelineEvent,
                "Timeline event draft",
                $"Capture the approved run outcome, source '{sourceId}', and runtime fingerprint {runtimeSummary.RuntimeFingerprint} as a timeline-ready event card."),
            new(
                AiHistoryDraftEntryKinds.JournalEntry,
                "Journal entry draft",
                $"Prepare a player-facing journal entry for {characterDigest.DisplayName} with emphasis on {focus} and any unresolved table hooks."),
            new(
                AiHistoryDraftEntryKinds.CharacterHistory,
                "Character history update draft",
                $"Package the approved recap outcome as a canonical history update for {characterDigest.DisplayName} without mutating the character record yet.",
                ApprovalTargetKind: AiApprovalTargetKinds.CharacterActionDraft)
        ];
        AiHistoryDraftEntry[] entries = availableEntries.Take(maxEntries).ToArray();

        return new AiHistoryDraftProjection(
            DraftId: BuildDraftId(sourceKind, sourceId, characterDigest.CharacterId, runtimeSummary.RuntimeFingerprint),
            Operation: AiHistoryDraftApiOperations.CreateHistoryDraft,
            CharacterId: characterDigest.CharacterId,
            CharacterDisplayName: characterDigest.DisplayName,
            RulesetId: runtimeSummary.RulesetId,
            RuntimeFingerprint: runtimeSummary.RuntimeFingerprint,
            SourceKind: sourceKind,
            SourceId: sourceId,
            Summary: summary,
            Entries: entries,
            Evidence: BuildEvidence(runtimeSummary, characterDigest, sessionDigest, transcriptId),
            Risks: BuildRisks(runtimeSummary.RuntimeFingerprint, characterDigest.RuntimeFingerprint, sessionDigest, transcriptId, transcript),
            TranscriptState: transcript?.Payload?.State,
            ProfileId: sessionDigest?.ProfileId,
            ProfileTitle: sessionDigest?.ProfileTitle);
    }

    private static IReadOnlyList<AiEvidenceEntry> BuildEvidence(
        AiRuntimeSummaryProjection runtimeSummary,
        AiCharacterDigestProjection characterDigest,
        AiSessionDigestProjection? sessionDigest,
        string? transcriptId)
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

        if (transcriptId is not null)
        {
            evidence.Add(new AiEvidenceEntry(
                Title: "Transcript",
                Summary: $"Transcript source reference {transcriptId}",
                ReferenceId: transcriptId,
                Source: "transcript"));
        }

        return evidence;
    }

    private static IReadOnlyList<AiRiskEntry> BuildRisks(
        string runtimeFingerprint,
        string characterRuntimeFingerprint,
        AiSessionDigestProjection? sessionDigest,
        string? transcriptId,
        AiApiResult<AiTranscriptDocumentReceipt>? transcript)
    {
        List<AiRiskEntry> risks =
        [
            new(
                Severity: AiRiskSeverities.Warning,
                Title: "Scaffolded history draft",
                Summary: "This draft prepares recap, timeline, journal, and character-history candidates only. Approval and canonical writes still happen through separate flows.")
        ];

        if (!string.Equals(runtimeFingerprint, characterRuntimeFingerprint, StringComparison.Ordinal))
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "Runtime mismatch",
                Summary: $"Requested runtime {runtimeFingerprint} does not match character digest runtime {characterRuntimeFingerprint}."));
        }

        if (sessionDigest is null)
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Note,
                Title: "No session digest",
                Summary: "No live session digest was available, so the draft used character/runtime context only."));
        }

        if (transcriptId is not null && transcript is { IsImplemented: false, NotImplemented: not null })
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Note,
                Title: "Transcript provider deferred",
                Summary: transcript.NotImplemented.Message));
        }

        return risks;
    }

    private static string ResolveSourceKind(string? transcriptId, string? sessionId)
    {
        if (transcriptId is not null)
        {
            return AiHistoryDraftSourceKinds.Transcript;
        }

        if (sessionId is not null)
        {
            return AiHistoryDraftSourceKinds.Session;
        }

        return AiHistoryDraftSourceKinds.Character;
    }

    private static string BuildDraftId(string sourceKind, string sourceId, string characterId, string runtimeFingerprint)
        => $"history-draft:{sourceKind}:{sourceId}:{characterId}:{runtimeFingerprint}";

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
