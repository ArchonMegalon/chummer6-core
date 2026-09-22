using System.Text.Json;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void Origin_finish_is_explicit_optional_real_life_zero_cost_and_recoverable(int realLifeModules)
    {
        string directory = CreateTempDirectory();
        try
        {
            const string finishId = "finish-life-module-selection";
            var id = new CharacterWorkspaceId("origin-finish-selection");
            var store = new FileWorkspaceStore(directory);
            string xml = CharacterXml("Human");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            LifeModuleOriginDossierInteractionService Interaction() => new(new LifeModuleOriginDossierService(
                new CharacterCreationFoundationLifeModuleDecisionAuthority(store, CreateService(store),
                    new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE")));
            var interaction = Interaction();
            var checkpoint = interaction.Start(id.Value).Value!;
            for (int number = 0; number < 4 + realLifeModules; number++)
            {
                var choices = checkpoint.Projection.CurrentTurn.LegalChoices;
                if (number < 4)
                    Assert.IsFalse(choices.Any(choice => choice.ChoiceId == finishId), "Finish appeared before required stages.");
                var chosen = number == 3 ? choices.Single(choice => choice.SourceAnchorIds.Any(anchor => anchor.Contains(SkipEducationId)))
                    : number >= 4 ? choices.Single(choice => choice.SourceAnchorIds.Any(anchor => anchor.Contains(BountyHunterId)))
                    : choices.First(choice => choice.FollowUps is null);
                var pending = interaction.Prepare(checkpoint, chosen.ChoiceId).Value!;
                var advanced = interaction.Confirm(pending, pending.PendingPreview!.PreviewDigest, "module-" + number, true);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, advanced.Outcome, string.Join(", ", advanced.Blockers));
                checkpoint = advanced.Value!.Checkpoint;
                if (number < 3)
                {
                    var earlyState = JourneyState(CreateService(store), id);
                    Assert.IsFalse(CreateService(store).PreviewFinishSelection(new(earlyState.Binding,
                        earlyState.DraftRevision, earlyState.DraftDigest)).Value!.CanConfirm);
                }
            }
            var beforeState = JourneyState(CreateService(store), id);
            Assert.IsTrue(beforeState.CanFinishSelection);
            var finish = checkpoint.Projection.CurrentTurn.LegalChoices.Single(choice => choice.ChoiceId == finishId);
            Assert.AreEqual("Modulauswahl beenden", finish.Label);
            Assert.AreEqual(0m, finish.MechanicsPreview.KarmaCost);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var prepared = interaction.Prepare(checkpoint, finishId).Value!;
            Assert.AreNotEqual(LifeModuleOriginDossierOutcomes.Success,
                interaction.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "finish", false).Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            var result = interaction.Confirm(prepared, prepared.PendingPreview!.PreviewDigest, "finish", true);
            Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, result.Outcome, string.Join(", ", result.Blockers));
            var finished = result.Value!.Checkpoint;
            Assert.IsTrue(finished.Projection.CurrentTurn.IsTerminal);
            Assert.AreEqual("life-module-selection-finished", finished.Projection.CurrentTurn.StageId);
            Assert.HasCount(0, finished.Projection.CurrentTurn.LegalChoices);
            Assert.HasCount(5 + realLifeModules, finished.Projection.VisibleChapters);
            var workspace = store.Get(id).Value!;
            Assert.AreEqual(xml, workspace.Document.Content, "Ending selection must not partially apply effects or enter Career.");
            Assert.IsTrue(workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!.ModuleSelectionFinished);
            var afterState = JourneyState(CreateService(store), id);
            Assert.IsTrue(afterState.SelectionFinished);
            Assert.IsFalse(afterState.CanFinishSelection);
            Assert.AreEqual(beforeState.Budget.Used, afterState.Budget.Used);
            Assert.HasCount(beforeState.AdditionalModules.Count, afterState.AdditionalModules);
            byte[] saved = File.ReadAllBytes(WorkspacePath(directory, id));
            var compiled = SequencePreview(CreateService(store), id);
            Assert.HasCount(4 + realLifeModules, compiled.ModuleSequence!.Occurrences);
            Assert.IsTrue(compiled.ModuleSequence.SelectionFinished);
            Assert.IsFalse(compiled.CanApply, "A complete sequence preview is not yet the character transaction.");
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(WorkspacePath(directory, id)),
                "Compiling the finished module sequence must preserve all accepted chapters and receipts.");
            store = new FileWorkspaceStore(directory);
            interaction = Interaction();
            Assert.AreEqual(JsonSerializer.Serialize(finished), JsonSerializer.Serialize(interaction.Start(id.Value).Value));
            Assert.AreEqual(finished.CheckpointDigest, interaction.Restore(finished).Value!.CheckpointDigest);
            Assert.AreEqual(finished.CheckpointDigest,
                interaction.Confirm(prepared, prepared.PendingPreview.PreviewDigest, "finish", true).Value!.Checkpoint.CheckpointDigest);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                CreateService(store).PreviewModule(new(afterState.Binding, afterState.DraftRevision, afterState.DraftDigest,
                    new(BountyHunterId, null))).Outcome);
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
