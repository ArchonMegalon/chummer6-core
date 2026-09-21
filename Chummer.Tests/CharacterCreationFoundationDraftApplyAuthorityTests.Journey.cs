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
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    private const string FormativeArcologyId = "924ccfd0-136c-4385-94fe-a8d7be2eb7ed";
    private const string TeenCorporateId = "f0393b9e-2698-4955-bd31-112b619ac7b8";
    private const string SkipEducationId = "5a2eee69-cedb-403e-9649-fdc9a1377374";
    private const string BountyHunterId = "47bf63cf-9a2a-4008-b455-c8ab68add581";

    [TestMethod]
    public void Origin_book_continuation_commits_each_module_and_chapter_receipt_together_and_reopens()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("origin-multiple-chapters");
            var store = new FileWorkspaceStore(directory);
            string xml = CharacterXml("Human");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            LifeModuleOriginDossierInteractionService Interaction(FileWorkspaceStore current) => new(
                new LifeModuleOriginDossierService(new CharacterCreationFoundationLifeModuleDecisionAuthority(
                    current, CreateService(current), new XmlCharacterFileQueries(new CharacterFileService()), () => "de-DE")));
            var interaction = Interaction(store);
            var checkpoint = interaction.Start(id.Value).Value!;
            for (int decision = 0; decision < 4; decision++)
            {
                var turn = checkpoint.Projection.CurrentTurn;
                Assert.IsFalse(turn.IsTerminal, $"No next choice at stage {turn.StageOrder}: {turn.DecisionPrompt}");
                Assert.AreEqual(decision + 1, turn.StageOrder);
                var choice = turn.LegalChoices.First();
                var prepared = interaction.Prepare(checkpoint, choice.ChoiceId);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, prepared.Outcome);
                string previewDigest = prepared.Value!.PendingPreview!.PreviewDigest;
                string key = $"origin-stage-{decision}";
                var accepted = interaction.Confirm(prepared.Value, previewDigest, key, true);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, accepted.Outcome, string.Join(", ", accepted.Blockers));
                checkpoint = accepted.Value!.Checkpoint;
                Assert.AreEqual(decision + 1, checkpoint.Projection.VisibleChapters.Count);
                Assert.AreEqual(decision + 2L, checkpoint.WorkspaceRevision);
                store = new FileWorkspaceStore(directory);
                var persisted = store.Get(id).Value!;
                Assert.AreEqual(xml, persisted.Document.Content);
                Assert.AreEqual(decision, persisted.Document.AuxiliaryState.CharacterCreationFoundationDraft!.AdditionalModules?.Count ?? 0);
                var ledger = persisted.Document.AuxiliaryState.LifeModuleDecisionAcceptances!;
                Assert.AreEqual(decision + 1, ledger.Count);
                Assert.IsTrue(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, ledger));
                Assert.AreEqual(checkpoint.Projection.CurrentTurn.DecisionDigest, ledger[^1].NextStep.DecisionDigest);
                Assert.AreEqual(persisted.ContentRevision, persisted.SavedRevision);
                byte[] bytes = File.ReadAllBytes(WorkspacePath(directory, id));
                interaction = Interaction(store);
                var resumed = interaction.Restore(checkpoint);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, resumed.Outcome, string.Join(", ", resumed.Blockers));
                Assert.AreEqual(checkpoint.CheckpointDigest, resumed.Value!.CheckpointDigest);
                var replay = interaction.Confirm(prepared.Value, previewDigest, key, true);
                Assert.AreEqual(LifeModuleOriginDossierOutcomes.Success, replay.Outcome);
                Assert.AreEqual(checkpoint.CheckpointDigest, replay.Value!.Checkpoint.CheckpointDigest);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(WorkspacePath(directory, id)));
                if (ledger.Count > 1)
                {
                    var changed = ledger.ToArray();
                    changed[^1] = changed[^1] with
                    {
                        NextStep = changed[^1].NextStep with { OwnerId = "different-owner" }
                    };
                    Assert.IsFalse(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, changed));
                    changed = ledger.ToArray();
                    changed[^1] = changed[^1] with
                    {
                        NextStep = changed[^1].NextStep with { PreviousTurnDigest = new string('0', 64) }
                    };
                    Assert.IsFalse(LifeModuleDecisionAcceptanceIntegrity.TryValidateLedger(id, persisted.ContentRevision, changed));
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Life_module_journey_appends_all_stages_and_reopens_exact_costs_without_character_writes(bool newRunner)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("full-module-journey");
            var store = SeedJourney(directory, id, newRunner);
            if (newRunner)
                id = store.List().Single().Id;
            var service = CreateService(store);
            var initial = Load(service, id).PendingDraft!;
            Assert.IsFalse(JsonSerializer.Serialize(initial).Contains("AdditionalModules", StringComparison.Ordinal),
                "Existing nationality-only JSON must retain its original digest representation.");
            string xml = store.Get(id).Value!.Document.Content;
            decimal used = 55; // Elf 40 + nationality 15, counted once for the entire journey.
            long revision = 2;
            string[] modules = [FormativeArcologyId, TeenCorporateId, SkipEducationId, BountyHunterId, BountyHunterId];
            decimal[] costs = [40, 50, 0, 100, 100];
            for (int i = 0; i < modules.Length; i++)
            {
                var state = JourneyState(service, id);
                Assert.AreEqual(Math.Min(2 + i, 5), state.CurrentStageOrder);
                Assert.AreEqual(used, state.Budget.Used);
                var request = JourneyRequest(state, modules[i]);
                var projected = service.PreviewModule(request);
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, projected.Outcome,
                    string.Join(", ", projected.Blockers));
                var preview = projected.Value!;
                Assert.AreEqual(costs[i], preview.Entry.KarmaCost);
                Assert.AreEqual(used + costs[i], preview.BudgetAfter.Used);
                byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                    service.ConfirmModule(new(request, preview.PreviewDigest, false)).Outcome);
                CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));

                var confirmed = service.ConfirmModule(new(request, preview.PreviewDigest, true));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome,
                    string.Join(", ", confirmed.Blockers));
                used += costs[i];
                revision++;
                Assert.AreEqual(revision, confirmed.Value!.ContentRevision);
                Assert.AreEqual(revision, confirmed.Value.SavedRevision);
                Assert.IsFalse(confirmed.Value.CharacterEffectsApplied);

                store = new FileWorkspaceStore(directory);
                service = CreateService(store);
                var reopened = JourneyState(service, id);
                Assert.AreEqual(i + 1, reopened.AdditionalModules.Count);
                Assert.AreEqual(used, reopened.Budget.Used);
                Assert.AreEqual(750 - used, reopened.Budget.Remaining);
                Assert.AreEqual(confirmed.Value.DraftDigest, reopened.DraftDigest);
                Assert.AreEqual(xml, store.Get(id).Value!.Document.Content);
                var savedDraft = Load(service, id).PendingDraft!;
                Assert.AreEqual(initial.Selection, savedDraft.Selection);
                Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    initial.ProjectedEffects, savedDraft.ProjectedEffects));
                Assert.AreEqual(0, service.ValidateContinuationDraft(store.Get(id).Value!).Count);

                byte[] committed = File.ReadAllBytes(WorkspacePath(directory, id));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                    service.ConfirmModule(new(request, preview.PreviewDigest, true)).Outcome,
                    "A stale confirm must never append a second module.");
                CollectionAssert.AreEqual(committed, File.ReadAllBytes(WorkspacePath(directory, id)));
            }

            var foundation = Load(service, id);
            var edit = service.Preview(new(foundation.Binding, "Human", initial.Selection, initial.FollowUpValues));
            CollectionAssert.Contains(edit.Blockers.ToList(), CharacterCreationFoundationBlockers.FoundationLockedByJourney);
            var finalization = service.PreviewFinalization(new(foundation.Binding,
                foundation.PendingDraft!.DraftRevision, foundation.PendingDraft.DraftDigest));
            Assert.IsFalse(finalization.Value?.CanApply == true,
                "A complete draft sequence is not permission to apply a partial effect compiler.");
            CollectionAssert.Contains(finalization.Blockers.ToList(),
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);

            // The repeatable stage must charge each occurrence, then stop exactly
            // at the source-owned budget rather than treating repeats as free.
            for (int repetition = 0; repetition < 5; repetition++)
            {
                var state = JourneyState(service, id);
                var request = JourneyRequest(state, BountyHunterId);
                var preview = service.PreviewModule(request);
                if (repetition < 4)
                    Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                        service.ConfirmModule(new(request, preview.Value!.PreviewDigest, true)).Outcome);
                else
                {
                    CollectionAssert.Contains(preview.Blockers.ToList(), CharacterCreationFoundationBlockers.LifeModuleBudgetExceeded);
                    byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
                    Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                        service.ConfirmModule(new(request, preview.Value!.PreviewDigest, true)).Outcome);
                    CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
                    Assert.AreEqual(745m, state.Budget.Used);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Life_module_journey_rejects_skips_missing_followups_and_forged_bindings_without_writes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("module-journey-invalid");
            var service = CreateService(SeedJourney(directory, id));
            var state = JourneyState(service, id);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var request = JourneyRequest(state, FormativeArcologyId);
            var skipped = service.PreviewModule(request with { Selection = new(TeenCorporateId, null) });
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, skipped.Outcome);
            var noPrompts = service.PreviewModule(request with { FollowUpValues = null });
            CollectionAssert.Contains(noPrompts.Blockers.ToList(), CharacterCreationFoundationBlockers.LifeModuleFollowUpRequired);
            var extraPrompt = service.PreviewModule(request with
            {
                FollowUpValues = new Dictionary<string, string> { ["unknown-prompt"] = "ignored?" }
            });
            CollectionAssert.Contains(extraPrompt.Blockers.ToList(), CharacterCreationFoundationBlockers.LifeModuleFollowUpUnknown);
            var stale = service.PreviewModule(request with { DraftDigest = "sha256:" + new string('0', 64) });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
            var source = service.PreviewModule(request with
            {
                Binding = request.Binding with { SourceDigest = "sha256:" + new string('0', 64) }
            });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, source.Outcome);
            var forged = service.ConfirmModule(new(request, "sha256:" + new string('0', 64), true));
            CollectionAssert.Contains(forged.Blockers.ToList(), CharacterCreationFoundationBlockers.PreviewDigestMismatch);
            var unavailable = CreateService(new FileWorkspaceStore(directory),
                new UnavailableCharacterCreationFoundationApplyAuthority());
            var validPreview = service.PreviewModule(request).Value!;
            var denied = unavailable.ConfirmModule(new(request, validPreview.PreviewDigest, true));
            CollectionAssert.Contains(denied.Blockers.ToList(),
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Life_module_journey_rejects_rehashed_cost_tampering_on_disk_reopen()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("module-journey-tampered");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            var request = JourneyRequest(JourneyState(service, id), FormativeArcologyId);
            var preview = service.PreviewModule(request).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ConfirmModule(new(request, preview.PreviewDigest, true)).Outcome);
            var workspace = store.Get(id).Value!;
            var draft = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
            var altered = draft with
            {
                DraftRevision = draft.DraftRevision + 1,
                BaseContentRevision = workspace.ContentRevision,
                AdditionalModules = [draft.AdditionalModules![0] with { KarmaCost = 0 }]
            };
            altered = altered with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(altered) };
            var document = workspace.Document with
            {
                State = workspace.Document.State with
                {
                    AuxiliaryState = workspace.Document.AuxiliaryState with { CharacterCreationFoundationDraft = altered }
                }
            };
            Assert.IsTrue(store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id,
                workspace.ContentRevision, workspace.Document.AuxiliaryStateDigest, document).Success);
            var reopened = CreateService(new FileWorkspaceStore(directory)).LoadJourney(new(id));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, reopened.Outcome);
            CollectionAssert.Contains(reopened.Blockers.ToList(),
                CharacterCreationFoundationBlockers.LifeModuleBudgetPendingDraftAuthorityRequired);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static FileWorkspaceStore SeedJourney(string directory, CharacterWorkspaceId id, bool newRunner = false)
    {
        var store = new FileWorkspaceStore(directory);
        if (newRunner)
        {
            var resolver = new FileSystemCharacterSourceDataResolver(CreateOverlays());
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var codec = new Sr5WorkspaceCodec(queries,
                new XmlCharacterSectionQueries(new CharacterSectionService(resolver)),
                new XmlCharacterMetadataCommands(new CharacterFileService()));
            var created = new CharacterCreationBootstrapService(store,
                new RulesetWorkspaceCodecResolver([codec]), queries, resolver).Create(new(
                CharacterCreationBootstrapSchemas.RequestV1,
                CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
                "Journey Runner", "No default metatype", CharacterCreationBuildMethods.LifeModules,
                CanonicalLifeModuleSettingsId));
            Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome,
                string.Join(", ", created.Blockers));
            id = created.Value!.WorkspaceId;
        }
        else
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(CharacterXml("Human"), RulesetDefaults.Sr5)).Success);
        var service = CreateService(store);
        var preview = Preview(service, Load(service, id).Binding, TirModuleId, TirHumanElfVersionId, "Elf");
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, Confirm(service, preview).Outcome);
        return store;
    }

    private static CharacterCreationLifeModuleJourneyState JourneyState(CharacterCreationFoundationService service,
        CharacterWorkspaceId id)
    {
        var result = service.LoadJourney(new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(", ", result.Blockers));
        return result.Value!;
    }

    private static CharacterCreationLifeModulePreviewRequest JourneyRequest(CharacterCreationLifeModuleJourneyState state,
        string moduleId)
    {
        var module = state.Options.Single(option => option.ModuleId == moduleId);
        Assert.HasCount(0, module.Versions);
        var values = module.FollowUps.ToDictionary(prompt => prompt.PromptId,
            prompt => prompt.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Reviewed choice",
            StringComparer.Ordinal);
        return new(state.Binding, state.DraftRevision, state.DraftDigest, new(moduleId, null), values);
    }
}
