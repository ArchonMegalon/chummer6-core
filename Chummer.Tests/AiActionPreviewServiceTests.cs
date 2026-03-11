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
public sealed class AiActionPreviewServiceTests
{
    [TestMethod]
    public void Default_ai_action_preview_service_projects_karma_nuyen_and_apply_receipts()
    {
        DefaultAiActionPreviewService service = new(new StubAiDigestService());

        AiActionPreviewReceipt? karmaPreview = service.PreviewKarmaSpend(
            OwnerScope.LocalSingleUser,
            new AiSpendPlanPreviewRequest(
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                Steps:
                [
                    new AiSpendPlanStep("step-1", "Raise Sneaking", Amount: 10m),
                    new AiSpendPlanStep("step-2", "Raise Etiquette", Amount: 8m)
                ],
                Goal: "Advance stealth and social coverage",
                WorkspaceId: "ws-stealth"));
        AiActionPreviewReceipt? nuyenPreview = service.PreviewNuyenSpend(
            OwnerScope.LocalSingleUser,
            new AiSpendPlanPreviewRequest(
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                Steps:
                [
                    new AiSpendPlanStep("step-1", "Upgrade fake SIN", Amount: 12000m)
                ],
                Goal: "Cover heat",
                WorkspaceId: "ws-stealth"));
        AiActionPreviewReceipt? applyPreview = service.CreateApplyPreview(
            OwnerScope.LocalSingleUser,
            new AiApplyPreviewRequest(
                CharacterId: "char-7",
                RuntimeFingerprint: "sha256:coach",
                ActionDraft: new AiActionDraft(
                    ActionId: AiSuggestedActionIds.PreviewApplyPlan,
                    Title: "Apply stealth plan",
                    Description: "Preview the highest-ranked stealth coaching recommendation.",
                    WorkspaceId: "ws-stealth"),
                WorkspaceId: "ws-stealth"));

        Assert.IsNotNull(karmaPreview);
        Assert.AreEqual(AiActionPreviewApiOperations.PreviewKarmaSpend, karmaPreview.Operation);
        Assert.AreEqual(AiActionPreviewKinds.KarmaSpend, karmaPreview.PreviewKind);
        Assert.AreEqual(2, karmaPreview.StepCount);
        Assert.AreEqual(18m, karmaPreview.TotalRequested);
        Assert.AreEqual("karma", karmaPreview.Unit);
        Assert.AreEqual("Cipher (Ghostwire)", karmaPreview.CharacterDisplayName);
        Assert.AreEqual("Official SR5 Core", karmaPreview.ProfileTitle);
        Assert.AreEqual("ws-stealth", karmaPreview.WorkspaceId);
        Assert.IsTrue(karmaPreview.Evidence.Any(entry => entry.ReferenceId == "ws-stealth"));

        Assert.IsNotNull(nuyenPreview);
        Assert.AreEqual(AiActionPreviewApiOperations.PreviewNuyenSpend, nuyenPreview.Operation);
        Assert.AreEqual(1, nuyenPreview.StepCount);
        Assert.AreEqual(12000m, nuyenPreview.TotalRequested);
        Assert.AreEqual("nuyen", nuyenPreview.Unit);
        Assert.AreEqual("ws-stealth", nuyenPreview.WorkspaceId);

        Assert.IsNotNull(applyPreview);
        Assert.AreEqual(AiActionPreviewApiOperations.CreateApplyPreview, applyPreview.Operation);
        Assert.AreEqual(AiActionPreviewKinds.ApplyPreview, applyPreview.PreviewKind);
        Assert.AreEqual(1, applyPreview.StepCount);
        Assert.IsGreaterThanOrEqualTo(2, applyPreview.PreparedEffects.Count);
        Assert.IsGreaterThanOrEqualTo(1, applyPreview.Risks.Count);
        Assert.AreEqual("ws-stealth", applyPreview.WorkspaceId);
    }

    [TestMethod]
    public void Default_ai_action_preview_service_returns_null_for_missing_character_or_runtime()
    {
        DefaultAiActionPreviewService service = new(new StubAiDigestService(
            runtimeSummary: null,
            characterDigest: null,
            sessionDigest: null));

        Assert.IsNull(service.PreviewKarmaSpend(
            OwnerScope.LocalSingleUser,
            new AiSpendPlanPreviewRequest("missing", "sha256:missing", [])));
        Assert.IsNull(service.PreviewNuyenSpend(
            OwnerScope.LocalSingleUser,
            new AiSpendPlanPreviewRequest("missing", "sha256:missing", [])));
        Assert.IsNull(service.CreateApplyPreview(
            OwnerScope.LocalSingleUser,
            new AiApplyPreviewRequest(
                "missing",
                "sha256:missing",
                new AiActionDraft("draft-1", "Draft", "Preview"))));
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
                ProviderBindings: new Dictionary<string, string> { ["availability.item"] = "official.sr5.core:availability.item" });
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
                SelectionState: "selected",
                SessionReady: true,
                BundleFreshness: "current",
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
}
