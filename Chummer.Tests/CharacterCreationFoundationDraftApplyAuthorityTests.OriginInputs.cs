using System.Text.Json;
using Chummer.Application.LifeModules;
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
    public void Origin_candidate_cache_does_not_share_mutable_choices_with_callers()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("origin-cache-custody");
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(id,
                new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            var authority = new CharacterCreationFoundationLifeModuleDecisionAuthority(store, CreateService(store),
                new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE");
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var first = authority.Load(id.Value).Value!;
            string original = first.LegalChoices[0].MechanicsPreview.Items[0].AfterValue;
            ((LifeModuleMechanicsPreviewItem[])first.LegalChoices[0].MechanicsPreview.Items)[0] =
                first.LegalChoices[0].MechanicsPreview.Items[0] with { AfterValue = "not-authoritative" };
            var second = authority.Load(id.Value).Value!;
            Assert.AreEqual(original, second.LegalChoices[0].MechanicsPreview.Items[0].AfterValue);
            Assert.AreEqual(first.DecisionDigest, second.DecisionDigest);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Origin_followup_answers_are_previewed_persisted_and_replayed_without_another_module(bool nationality)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("origin-followups");
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(id,
                new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
            LifeModuleOriginDossierInteractionService Interaction() => new(new LifeModuleOriginDossierService(
                new CharacterCreationFoundationLifeModuleDecisionAuthority(store, CreateService(store),
                    new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE")));
            var interaction = Interaction();
            var checkpoint = interaction.Start(id.Value).Value!;
            if (!nationality)
            {
                var nation = checkpoint.Projection.CurrentTurn.LegalChoices.First(choice => choice.FollowUps is null);
                var nationalityPreview = interaction.Prepare(checkpoint, nation.ChoiceId).Value!;
                checkpoint = interaction.Confirm(nationalityPreview, nationalityPreview.PendingPreview!.PreviewDigest, "nation", true).Value!.Checkpoint;
            }
            var choice = nationality ? checkpoint.Projection.CurrentTurn.LegalChoices.First(choice => choice.FollowUps is { Count: > 0 })
                : checkpoint.Projection.CurrentTurn.LegalChoices.Single(choice => choice.Label == "Arcology Living");
            Assert.IsNotNull(choice.FollowUps);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success, interaction.Prepare(checkpoint, choice.ChoiceId).Outcome);
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success, interaction.Prepare(checkpoint, choice.ChoiceId,
                new Dictionary<string, string> { ["unknown"] = "answer" }).Outcome);
            var answers = choice.FollowUps.ToDictionary(prompt => prompt.PromptId,
                prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku Arcology");
            var oversized = new Dictionary<string, string>(answers) { [answers.Keys.First()] = new string('x', 1025) };
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success, interaction.Prepare(checkpoint, choice.ChoiceId, oversized).Outcome);
            var prepared = interaction.Prepare(checkpoint, choice.ChoiceId, answers);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, prepared.Outcome, string.Join(", ", prepared.Blockers));
            var pending = JsonSerializer.Deserialize<LifeModuleOriginDossierDraftCheckpoint>(JsonSerializer.Serialize(prepared.Value))!;
            Assert.IsNotNull(pending.PendingPreview!.InputResolution);
            Assert.HasCount(0, pending.PendingPreview.SelectedChoice.MechanicsPreview.PendingFollowUpIds);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            store = new FileWorkspaceStore(directory);
            interaction = Interaction();
            var restored = interaction.Restore(pending);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, restored.Outcome);
            Assert.AreEqual(JsonSerializer.Serialize(pending), JsonSerializer.Serialize(restored.Value),
                "Fresh display hints must not rewrite the legacy checkpoint or its decision digests.");
            var restoredPrompts = restored.Value!.Projection.CurrentTurn.LegalChoices
                .Single(item => item.ChoiceId == choice.ChoiceId).FollowUps!;
            CollectionAssert.AreEqual(choice.FollowUps.Select(prompt => prompt.DisplayLabel).ToArray(),
                restoredPrompts.Select(prompt => prompt.DisplayLabel).ToArray());
            if (!nationality)
                Assert.IsTrue(restoredPrompts.Any(prompt => prompt.DisplayLabel == "Academic · Corporation"));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            var tampered = pending with { PendingPreview = pending.PendingPreview with
            {
                InputResolution = pending.PendingPreview.InputResolution with
                { Values = new Dictionary<string, string> { [answers.Keys.First()] = "Changed answer" } }
            }};
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success, interaction.Restore(tampered).Outcome);
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success,
                interaction.Confirm(tampered, pending.PendingPreview.PreviewDigest, "arcology", true).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            var accepted = interaction.Confirm(pending, pending.PendingPreview.PreviewDigest, "arcology", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, accepted.Outcome, string.Join(", ", accepted.Blockers));
            Assert.HasCount(nationality ? 1 : 2, accepted.Value!.Checkpoint.Projection.VisibleChapters);
            var persisted = store.Get(id).Value!;
            var draft = persisted.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
            var savedAnswers = nationality ? draft.FollowUpValues : draft.AdditionalModules!.Single().FollowUpValues;
            Assert.IsTrue(answers.All(answer => savedAnswers[answer.Key] == answer.Value));
            var receipt = persisted.Document.AuxiliaryState.LifeModuleDecisionAcceptances![^1].Receipt;
            Assert.AreEqual(pending.PendingPreview.InputResolution.ResolutionDigest, receipt.InputResolutionDigest);
            Assert.IsTrue(receipt.CanonicalFacts.Any(fact => fact.FactKind == "accepted-life-module-answer"));
            byte[] after = File.ReadAllBytes(WorkspacePath(directory, id));
            // No phone timeline/checkpoint is supplied: a fresh process recovers
            // the exact book from the atomically committed workspace history.
            store = new FileWorkspaceStore(directory);
            interaction = Interaction();
            var recovered = interaction.Start(id.Value);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, recovered.Outcome,
                string.Join(", ", recovered.Blockers));
            Assert.AreEqual(JsonSerializer.Serialize(accepted.Value.Checkpoint), JsonSerializer.Serialize(recovered.Value));
            Assert.AreEqual(receipt.ChapterDigest, recovered.Value!.TimelineChapterDigests[^1]);
            CollectionAssert.AreEqual(after, File.ReadAllBytes(WorkspacePath(directory, id)));
            var history = persisted.Document.AuxiliaryState.LifeModuleDecisionAcceptances!.ToArray();
            var last = history[^1];
            Assert.IsNotNull(last.Chapter);
            history[^1] = last with { Chapter = last.Chapter with { VisibleMarkdown = "Different text" } };
            Assert.IsFalse(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, history));
            history[^1] = last with { Chapter = null };
            Assert.IsFalse(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, history));
            history[^1] = last with { Chapter = last.Chapter with { ThroughAcceptedDecisionId = "another-decision" } };
            Assert.IsFalse(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, history));
            // A legacy receipt remains valid, but cannot fabricate the missing
            // historical scene from today's narrative or source templates.
            var legacy = persisted.Document.AuxiliaryState.LifeModuleDecisionAcceptances!.Select(entry =>
            {
                var oldReceipt = entry.Receipt with { ChapterDigest = null };
                oldReceipt = oldReceipt with { ReceiptDigest = LifeModuleDecisionAcceptanceIntegrity.ComputeReceiptDigest(oldReceipt) };
                return entry with { Receipt = oldReceipt, Chapter = null };
            }).ToArray();
            Assert.IsTrue(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, legacy));
            var oldHistory = new LifeModuleOriginDossierService(new ReadOnlyOriginHistory(last.NextStep, legacy));
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Missing, oldHistory.Project(id.Value).Outcome);
            var corruptHistory = new LifeModuleOriginDossierService(new ReadOnlyOriginHistory(last.NextStep, history));
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success, corruptHistory.Project(id.Value).Outcome);
            var emptyHistory = new LifeModuleOriginDossierService(new ReadOnlyOriginHistory(last.NextStep, []));
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Invalid, emptyHistory.Project(id.Value).Outcome);
            var replay = interaction.Confirm(pending, pending.PendingPreview.PreviewDigest, "arcology", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, replay.Outcome);
            Assert.AreEqual(accepted.Value.Checkpoint.CheckpointDigest, replay.Value!.Checkpoint.CheckpointDigest);
            CollectionAssert.AreEqual(after, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class ReadOnlyOriginHistory(LifeModuleDecisionAuthorityStep step,
        IReadOnlyList<LifeModuleDecisionAcceptance> history) : ILifeModuleDecisionAuthority, ILifeModuleDecisionHistoryAuthority
    {
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAuthorityStep> Load(string workspaceId)
            => new(LifeModuleOriginDossierOutcomes.Success, step, []);
        public LifeModuleDecisionAuthorityResult<IReadOnlyList<LifeModuleDecisionAcceptance>> LoadHistory(string workspaceId)
            => new(LifeModuleOriginDossierOutcomes.Success, history, []);
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> FindAcceptance(string workspaceId, string key)
            => throw new InvalidOperationException("Read-only recovery must not retry a command.");
        public LifeModuleDecisionAuthorityResult<LifeModuleDecisionAcceptance> Accept(LifeModuleDecisionAcceptanceCommand command)
            => throw new InvalidOperationException("Read-only recovery must not mutate mechanics.");
    }
}
