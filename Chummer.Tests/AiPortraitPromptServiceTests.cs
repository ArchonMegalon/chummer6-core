#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiPortraitPromptServiceTests
{
    [TestMethod]
    public void Default_ai_portrait_prompt_service_projects_grounded_prompt_variants()
    {
        DefaultAiPortraitPromptService service = new(new StubAiDigestService());

        AiPortraitPromptProjection? projection = service.CreatePortraitPrompt(
            OwnerScope.LocalSingleUser,
            new AiPortraitPromptRequest(
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                StylePackId: "neo-noir",
                PromptFlavor: "runner dossier portrait"));

        Assert.IsNotNull(projection);
        Assert.AreEqual("char-7", projection.CharacterId);
        Assert.AreEqual("Cipher (Ghostwire)", projection.DisplayName);
        Assert.AreEqual("sha256:coach", projection.RuntimeFingerprint);
        Assert.AreEqual("sr5", projection.RulesetId);
        StringAssert.Contains(projection.Prompt, "Cipher (Ghostwire)");
        StringAssert.Contains(projection.Prompt, "runner dossier portrait");
        StringAssert.Contains(projection.Prompt, "neo-noir");
        Assert.HasCount(4, projection.Variants);
        CollectionAssert.Contains(projection.Tags.ToArray(), "neo-noir");
    }

    [TestMethod]
    public void Default_ai_portrait_prompt_service_returns_null_for_missing_character_or_runtime()
    {
        DefaultAiPortraitPromptService service = new(new StubAiDigestService(runtimeSummary: null, characterDigest: null));

        Assert.IsNull(service.CreatePortraitPrompt(
            OwnerScope.LocalSingleUser,
            new AiPortraitPromptRequest("missing", RuntimeFingerprint: "sha256:missing")));
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        private readonly AiRuntimeSummaryProjection? _runtimeSummary;
        private readonly AiCharacterDigestProjection? _characterDigest;

        public StubAiDigestService(
            AiRuntimeSummaryProjection? runtimeSummary = null,
            AiCharacterDigestProjection? characterDigest = null)
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
        }

        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => _runtimeSummary is not null && string.Equals(_runtimeSummary.RuntimeFingerprint, runtimeFingerprint, StringComparison.Ordinal)
                ? _runtimeSummary
                : null;

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => _characterDigest is not null && string.Equals(_characterDigest.CharacterId, characterId, StringComparison.Ordinal)
                ? _characterDigest
                : null;

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId) => null;
    }
}
