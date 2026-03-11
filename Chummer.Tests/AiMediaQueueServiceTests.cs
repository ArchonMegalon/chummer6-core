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
public sealed class AiMediaQueueServiceTests
{
    [TestMethod]
    public void Default_ai_media_queue_service_projects_grounded_queue_receipt()
    {
        DefaultAiMediaQueueService service = new(
            new StubAiDigestService(),
            new StubPortraitPromptService(),
            new NotImplementedAiMediaJobService());

        AiMediaQueueReceipt? receipt = service.QueueMediaJob(
            OwnerScope.LocalSingleUser,
            new AiMediaQueueRequest(
                JobType: AiMediaJobTypes.Portrait,
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                StylePackId: "neo-noir"));

        Assert.IsNotNull(receipt);
        Assert.AreEqual(AiMediaQueueApiOperations.QueueMediaJob, receipt.Operation);
        Assert.AreEqual(AiMediaJobTypes.Portrait, receipt.JobType);
        Assert.AreEqual(AiMediaQueueStates.Scaffolded, receipt.State);
        Assert.AreEqual("char-7", receipt.CharacterId);
        Assert.AreEqual("Cipher (Ghostwire)", receipt.CharacterDisplayName);
        Assert.AreEqual("sr5", receipt.RulesetId);
        Assert.AreEqual("sha256:coach", receipt.RuntimeFingerprint);
        Assert.AreEqual(AiMediaApiOperations.QueuePortraitJob, receipt.UnderlyingOperation);
        StringAssert.Contains(receipt.Prompt, "Cipher");
        Assert.AreEqual("neo-noir", receipt.Options["stylePackId"]);
        Assert.IsGreaterThanOrEqualTo(2, receipt.Evidence.Count);
        Assert.IsGreaterThanOrEqualTo(2, receipt.Risks.Count);
    }

    [TestMethod]
    public void Default_ai_media_queue_service_returns_null_for_missing_character_or_runtime()
    {
        DefaultAiMediaQueueService service = new(
            new StubAiDigestService(runtimeSummary: null, characterDigest: null),
            new StubPortraitPromptService(),
            new NotImplementedAiMediaJobService());

        Assert.IsNull(service.QueueMediaJob(
            OwnerScope.LocalSingleUser,
            new AiMediaQueueRequest(
                JobType: AiMediaJobTypes.Dossier,
                CharacterId: "missing",
                RuntimeFingerprint: "sha256:missing")));
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
                BundleFreshness: "fresh");
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

    private sealed class StubPortraitPromptService : IAiPortraitPromptService
    {
        public AiPortraitPromptProjection? CreatePortraitPrompt(OwnerScope owner, AiPortraitPromptRequest request)
            => new(
                CharacterId: request.CharacterId,
                DisplayName: "Cipher (Ghostwire)",
                RuntimeFingerprint: request.RuntimeFingerprint ?? "sha256:coach",
                RulesetId: "sr5",
                Prompt: "Half-length portrait of Cipher in neon rain.",
                Tags: ["sr5", "neo-noir"],
                Notes: ["Grounded portrait prompt."],
                Variants:
                [
                    new(AiPortraitPromptVariantKinds.Primary, "Primary", "Half-length portrait of Cipher in neon rain.")
                ],
                StylePackId: request.StylePackId);
    }
}
