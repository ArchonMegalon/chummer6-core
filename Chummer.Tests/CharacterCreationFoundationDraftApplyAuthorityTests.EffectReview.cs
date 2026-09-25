using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Effect_review_preserves_legacy_checkpoints_and_accepted_steps_but_requires_fresh_review_before_new_confirm()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("legacy-effect-review");
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            var authority = new CharacterCreationFoundationLifeModuleDecisionAuthority(store, CreateService(store),
                new XmlCharacterFileQueries(new CharacterFileService()));
            var legacy = new LifeModuleOriginDossierInteractionService(new(new LegacyEffectReviewAuthority(authority)));
            var modernDossier = new LifeModuleOriginDossierService(authority);
            var modern = new LifeModuleOriginDossierInteractionService(modernDossier);
            var initial = legacy.Start(id.Value).Value!;
            var choice = initial.Projection.CurrentTurn.LegalChoices.First(item => item.FollowUps is null);
            var oldPrepared = legacy.Prepare(initial, choice.ChoiceId).Value!;
            Assert.IsNull(oldPrepared.PendingPreview!.EffectReview);
            string oldCheckpoint = JsonSerializer.Serialize(oldPrepared);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            Assert.AreEqual(oldCheckpoint, JsonSerializer.Serialize(modern.Restore(oldPrepared).Value));
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Conflict,
                modern.Confirm(oldPrepared, oldPrepared.PendingPreview.PreviewDigest, "not-yet-reviewed", true).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));

            var prepared = modern.Prepare(oldPrepared, choice.ChoiceId).Value!;
            Assert.IsNotNull(prepared.PendingPreview!.EffectReview);
            Assert.AreEqual(JsonSerializer.Serialize(oldPrepared.Projection), JsonSerializer.Serialize(prepared.Projection));
            Assert.AreNotEqual(oldPrepared.PendingPreview.PreviewDigest, prepared.PendingPreview.PreviewDigest);
            Assert.AreEqual(prepared.CheckpointDigest, modern.Restore(prepared).Value!.CheckpointDigest);
            var review = prepared.PendingPreview.EffectReview;
            var forgedRows = review.Contributions.ToArray();
            int scalar = Array.FindIndex(forgedRows, item => item.Amount.HasValue);
            Assert.IsTrue(scalar >= 0);
            forgedRows[scalar] = forgedRows[scalar] with { Amount = 999 };
            var forged = LifeModuleEffectReviewIntegrity.Seal(review with { Contributions = forgedRows });
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Conflict,
                modernDossier.AcceptReviewed(prepared.Projection, choice.ChoiceId, "forged-review", true,
                    prepared.PendingPreview.InputResolution, forged).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));

            // Produce an actual pre-review-format acceptance and prove its exact
            // NextStep/ledger is still usable, not regenerated as new evidence.
            var oldAccepted = legacy.Confirm(oldPrepared, oldPrepared.PendingPreview.PreviewDigest, "old-acceptance", true).Value!;
            before = File.ReadAllBytes(WorkspacePath(directory, id));
            Assert.AreEqual(oldAccepted.Checkpoint.CheckpointDigest, modern.Restore(oldAccepted.Checkpoint).Value!.CheckpointDigest);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success,
                modern.Confirm(oldPrepared, oldPrepared.PendingPreview.PreviewDigest, "old-acceptance", true).Outcome);
            var nextChoice = oldAccepted.Checkpoint.Projection.CurrentTurn.LegalChoices.First(item => item.FollowUps is null);
            var next = modern.Prepare(oldAccepted.Checkpoint, nextChoice.ChoiceId);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, next.Outcome, string.Join(",", next.Blockers));
            Assert.IsNotNull(next.Value!.PendingPreview!.EffectReview);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            var confirmed = modern.Confirm(next.Value, next.Value.PendingPreview.PreviewDigest, "new-reviewed-module", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class LegacyEffectReviewAuthority(CharacterCreationFoundationLifeModuleDecisionAuthority inner)
        : ILifeModuleDecisionAuthority, ILifeModuleDecisionInputAuthority, ILifeModuleDecisionHistoryAuthority
    {
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string id) => inner.Load(id);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(string id, string key) => inner.FindAcceptance(id, key);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(LifeModuleDecisionAcceptanceCommand command) => inner.Accept(command);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionInputResolution> ResolveInputs(LifeModuleDecisionInputRequest request) => inner.ResolveInputs(request);
        public LifeModuleDecisionAuthorityResult<IReadOnlyList<LifeModuleDecisionAcceptance>> LoadHistory(string id) => inner.LoadHistory(id);
    }

    [TestMethod]
    public void Effect_review_resolves_Salish_defaults_pool_and_quality_without_rewriting_raw_draft()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("salish-effect-review");
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            var service = CreateService(store);
            var state = Load(service, id);
            var module = state.NationalityOptions.Single(item => item.Versions.Any(version => version.Label == "Salish-Shidhe Council"));
            var version = module.Versions.Single(item => item.Label == "Salish-Shidhe Council");
            var preview = Preview(service, state.Binding, module.ModuleId, version.VersionId);
            var raw = JsonSerializer.Serialize(preview);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var request = new CharacterCreationFoundationPreviewRequest(state.Binding, "Human", preview.Selection, preview.FollowUpValues);
            var result = service.ReviewFoundationEffects(request, preview.PreviewDigest);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.IsNotNull(result.Value);
            var rows = CharacterCreationFoundationEffectCompiler.ReviewContributions(result.Value);
            Assert.AreEqual(1m, rows.Single(item => item.Kind == "attributelevel" && item.TargetId == "LOG").Amount);
            Assert.AreEqual(1m, rows.Single(item => item.Kind == "skilllevel" && item.TargetName == "Survival").Amount);
            var pool = rows.Where(item => item.Kind == "knowledgeskilllevel").ToArray();
            Assert.HasCount(4, pool);
            Assert.IsTrue(pool.All(item => item.TargetName == "FreeKnowledgeSkills"));
            CollectionAssert.AreEqual(new decimal?[] { 0, 1, 1, 1 }, pool.Select(item => item.Amount).ToArray());
            Assert.IsTrue(pool.Any(item => item.DescriptiveMetadata.Values.Contains("History")));
            Assert.IsTrue(pool.Any(item => item.DescriptiveMetadata.Values.Contains("Salish")));
            var quality = rows.Single(item => item.Kind == "qualitylevel");
            Assert.AreEqual("SINner (National)", quality.TargetName);
            Assert.AreNotEqual("1", quality.TargetId);
            Assert.AreEqual(1m, quality.Amount);
            Assert.AreEqual("SINner", quality.DescriptiveMetadata["group"]);
            Assert.IsTrue(quality.SourceAnchorIds.Any(item => item.StartsWith("qualities.xml#quality:")));
            Assert.AreEqual("Salish-Shidhe Council", rows.Single(item => item.Kind == "pushtext").SelectionText);
            Assert.AreEqual(raw, JsonSerializer.Serialize(service.Preview(request).Value));
            Assert.IsTrue(version.Effects.Any(item => item.TargetId == "LOG" && item.AfterValue is null));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));

            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                service.ReviewFoundationEffects(request, "sha256:" + new string('0', 64)).Outcome);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.ReviewFoundationEffects(request with { FollowUpValues = null }, preview.PreviewDigest).Outcome);
            var reopened = CreateService(new FileWorkspaceStore(directory)).ReviewFoundationEffects(request, preview.PreviewDigest);
            Assert.AreEqual(JsonSerializer.Serialize(result), JsonSerializer.Serialize(reopened));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
