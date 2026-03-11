#nullable enable annotations

using System;
using System.Collections.Generic;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiHistoryDraftServiceTests
{
    [TestMethod]
    public void Default_ai_history_draft_service_projects_grounded_entries()
    {
        DefaultAiHistoryDraftService service = new(new StubAiDigestService(), new StubTranscriptProvider());

        AiHistoryDraftProjection? projection = service.CreateHistoryDraft(
            OwnerScope.LocalSingleUser,
            new AiHistoryDraftRequest(
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                SessionId: "session-7",
                TranscriptId: "transcript-7",
                Focus: "downtime fallout",
                MaxEntries: 3));

        Assert.IsNotNull(projection);
        Assert.AreEqual(AiHistoryDraftApiOperations.CreateHistoryDraft, projection.Operation);
        Assert.AreEqual("char-7", projection.CharacterId);
        Assert.AreEqual("Cipher (Ghostwire)", projection.CharacterDisplayName);
        Assert.AreEqual("sha256:coach", projection.RuntimeFingerprint);
        Assert.AreEqual("sr5", projection.RulesetId);
        Assert.AreEqual(AiHistoryDraftSourceKinds.Transcript, projection.SourceKind);
        Assert.AreEqual("transcript-7", projection.SourceId);
        Assert.AreEqual(AiTranscriptStates.Transcribed, projection.TranscriptState);
        Assert.HasCount(3, projection.Entries);
        Assert.AreEqual(AiHistoryDraftEntryKinds.SessionRecap, projection.Entries[0].EntryKind);
        StringAssert.Contains(projection.Entries[0].Summary, "downtime fallout");
        Assert.IsGreaterThanOrEqualTo(3, projection.Evidence.Count);
        Assert.IsGreaterThanOrEqualTo(1, projection.Risks.Count);
    }

    [TestMethod]
    public void Default_ai_history_draft_service_returns_null_for_missing_character_or_runtime()
    {
        DefaultAiHistoryDraftService service = new(new StubAiDigestService(runtimeSummary: null, characterDigest: null), new StubTranscriptProvider());

        Assert.IsNull(service.CreateHistoryDraft(
            OwnerScope.LocalSingleUser,
            new AiHistoryDraftRequest("missing", RuntimeFingerprint: "sha256:missing")));
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        private readonly AiRuntimeSummaryProjection? _runtimeSummary;
        private readonly AiCharacterDigestProjection? _characterDigest;
        private readonly AiSessionDigestProjection? _sessionDigest;

        public StubAiDigestService(
            AiRuntimeSummaryProjection? runtimeSummary = null,
            AiCharacterDigestProjection? characterDigest = null,
            AiSessionDigestProjection? sessionDigest = null)
        {
            _runtimeSummary = runtimeSummary ?? new AiRuntimeSummaryProjection(
                RuntimeFingerprint: "sha256:coach",
                RulesetId: "sr5",
                Title: "Street-Level Runtime Lock",
                CatalogKind: "saved",
                EngineApiVersion: "1.0.0",
                ContentBundles: ["official.sr5.core@1.0.0"],
                RulePacks: ["campaign.street-level@2.0.0"],
                ProviderBindings: new Dictionary<string, string>());
            _characterDigest = characterDigest ?? new AiCharacterDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:coach",
                Summary: new CharacterFileSummary(
                    Name: "Cipher",
                    Alias: "Ghostwire",
                    Metatype: "Human",
                    BuildMethod: "Priority",
                    CreatedVersion: "5",
                    AppVersion: "10",
                    Karma: 18m,
                    Nuyen: 1500m,
                    Created: true),
                LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
                HasSavedWorkspace: true);
            _sessionDigest = sessionDigest ?? new AiSessionDigestProjection(
                CharacterId: "char-7",
                DisplayName: "Cipher (Ghostwire)",
                RulesetId: "sr5",
                RuntimeFingerprint: "sha256:coach",
                SelectionState: SessionRuntimeSelectionStates.Selected,
                SessionReady: true,
                BundleFreshness: "fresh",
                RequiresBundleRefresh: false,
                ProfileId: "official.sr5.core",
                ProfileTitle: "Official SR5 Core");
        }

        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => _runtimeSummary is not null && string.Equals(_runtimeSummary.RuntimeFingerprint, runtimeFingerprint, StringComparison.Ordinal)
                ? _runtimeSummary
                : null;

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => _characterDigest is not null && string.Equals(_characterDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _characterDigest
                : null;

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
            => _sessionDigest is not null && string.Equals(_sessionDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _sessionDigest
                : null;
    }

    private sealed class StubTranscriptProvider : ITranscriptProvider
    {
        public AiApiResult<AiTranscriptDocumentReceipt> SubmitTranscript(OwnerScope owner, AiTranscriptSubmissionRequest? request)
            => AiApiResult<AiTranscriptDocumentReceipt>.Implemented(
                new AiTranscriptDocumentReceipt(
                    TranscriptId: "transcript-7",
                    State: AiTranscriptStates.Pending,
                    Message: "Queued.",
                    ExternalProviderConfigured: true,
                    SessionId: request?.SessionId,
                    CharacterId: request?.CharacterId,
                    OwnerId: owner.NormalizedValue));

        public AiApiResult<AiTranscriptDocumentReceipt> GetTranscript(OwnerScope owner, string transcriptId)
            => AiApiResult<AiTranscriptDocumentReceipt>.Implemented(
                new AiTranscriptDocumentReceipt(
                    TranscriptId: transcriptId,
                    State: AiTranscriptStates.Transcribed,
                    Message: "Transcript ready.",
                    ExternalProviderConfigured: true,
                    SessionId: "session-7",
                    CharacterId: "char-7",
                    OwnerId: owner.NormalizedValue));
    }
}
