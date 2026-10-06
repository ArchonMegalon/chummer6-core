using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Corporate_childhood_military_science_and_face_sequence_opens_allocations_after_quality_answers()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("corporate-military-science-face");
            var store = new FileWorkspaceStore(directory);
            Assert.IsTrue(store.CreateWorkspaceDocument(id,
                new WorkspaceDocument(CharacterXml("Human").Replace(CanonicalLifeModuleSettingsId,
                    "a75e2db7-54b3-4631-9d3a-e6c697a9018a", StringComparison.Ordinal), RulesetDefaults.Sr5)).Success);
            var service = CreateService(store);
            var nationality = CreateCatalog().GetOptionProjections("Nationality")
                .Single(row => row.ModuleId == "36b14645-f416-4598-ae10-f60f80d93155");
            var country = nationality.Versions.Single(row => row.VersionId == "5129c5a5-066f-47b6-a37e-fae8d3e87471");
            var openingAnswers = nationality.FollowUps.Concat(country.FollowUps).ToDictionary(row => row.PromptId,
                row => row.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Reviewed birthplace");
            var opening = service.Preview(new(Load(service, id).Binding, "Human",
                new(nationality.ModuleId, country.VersionId), openingAnswers));
            Assert.IsNotNull(opening.Value, string.Join(", ", opening.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, Confirm(service, opening.Value).Outcome);

            foreach (var selection in new CharacterCreationFoundationSelection[]
            {
                new("8b05f9be-4713-4d96-9c8b-25952b538b3a", null),
                new("15bd4283-f287-4be7-b174-9e5ab97bda1a", null),
                new("87ad3d80-8d96-4c16-aa62-d63eb2575ff3", "17b012c8-8f06-4ffc-97da-4d374a2415ad"),
                new("406ee650-fa40-4bf0-9e5f-77b7f9b37b23", "096705a0-e145-4849-84bf-9e4421122d5e")
            })
            {
                var state = JourneyState(service, id);
                var module = state.Options.Single(row => row.ModuleId == selection.ModuleId);
                var version = module.Versions.SingleOrDefault(row => row.VersionId == selection.VersionId);
                var answers = module.FollowUps.Concat(version?.FollowUps ?? []).ToDictionary(row => row.PromptId,
                    row => row.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Reviewed choice");
                var request = new CharacterCreationLifeModulePreviewRequest(state.Binding,
                    state.DraftRevision, state.DraftDigest, selection, answers);
                var preview = service.PreviewModule(request);
                Assert.IsNotNull(preview.Value, string.Join(", ", preview.Blockers));
                var confirmed = service.ConfirmModule(new(request, preview.Value.PreviewDigest, true));
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome,
                    string.Join(", ", confirmed.Blockers));
            }

            var journey = JourneyState(service, id);
            var finishRequest = new CharacterCreationLifeModuleFinishRequest(
                journey.Binding, journey.DraftRevision, journey.DraftDigest);
            var finish = service.PreviewFinishSelection(finishRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ConfirmFinishSelection(new(finishRequest, finish.PreviewDigest, true)).Outcome);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var initial = SequencePreview(service, id);
            var values = (initial.ModuleSequence!.DependentQualityInstances ?? [])
                .ToDictionary(row => row.InstancePrompt.PromptId,
                    row => row.InstancePrompt.Label == "Code of Honor" ? "Protect noncombatants" : "Academy cadet");
            foreach (var level in initial.ModuleSequence.QualityLevels.Where(row => row.InstancePrompt is not null))
                values[level.InstancePrompt!.PromptId] = level.InstanceValue
                    ?? level.InstancePrompt.Options.FirstOrDefault(row => row.IsEnabled)?.SourceValue ?? "UCAS";
            var finalRequest = QualityInstanceRequest(service, id, values);
            var final = service.PreviewFinalization(finalRequest).Value!;
            var plan = BuildDependentQualityPlan(store, id, final.ModuleSequence!);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
            Assert.AreEqual(JsonSerializer.Serialize(final), JsonSerializer.Serialize(
                CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(finalRequest).Value));
            Assert.IsNotNull(final.EffectWriteSummary, string.Join(", ", final.FinalizationBlocked)
                + "; unsupported: " + string.Join("; ", final.ModuleSequence!.Occurrences.SelectMany(row =>
                    row.Compilation.Effects.Where(effect => effect.CompilationStatus != CharacterCreationFoundationEffectCompilationStatuses.Supported)
                        .Select(effect => row.Selection.ModuleId + "/" + effect.EffectKind + "/" + effect.TargetId)))
                + "; dependents: " + string.Join("; ", final.ModuleSequence.Occurrences.SelectMany(row => row.Compilation.DependentQualities)
                    .Select(row => row.TargetBinding.CanonicalName + "/" + row.CompilationStatus + "/" + row.Blocker))
                + "; plan: " + string.Join(",", plan.Blockers));
            Assert.IsNotNull(final.TalentCatalog, "The actual completed Origin sequence must expose the next allocation step.");
            Assert.IsFalse(final.CanApply, "Quality answers do not supply unchosen final allocations.");
            Assert.IsNotNull(plan.Plan, string.Join(", ", plan.Blockers));
            var qualities = plan.Plan.QualityXml.Select(XElement.Parse).ToArray();
            var improvements = plan.Plan.ImprovementXml.Select(XElement.Parse).ToArray();
            var bornRich = qualities.Single(row => row.Element("name")!.Value == "Born Rich");
            var firstImpression = qualities.Single(row => row.Element("name")!.Value == "First Impression");
            var resources = improvements.Single(row => row.Element("improvementttype")!.Value == "NuyenMaxBP");
            Assert.AreEqual("30", resources.Element("val")!.Value);
            Assert.AreEqual(bornRich.Element("guid")!.Value, resources.Element("sourcename")!.Value);
            var conditional = improvements.Single(row => row.Element("improvementttype")!.Value == "SkillCategory");
            Assert.AreEqual("Social Active", conditional.Element("improvedname")!.Value);
            Assert.AreEqual("2", conditional.Element("val")!.Value);
            Assert.AreEqual("Meeting people the first time", conditional.Element("condition")!.Value);
            Assert.AreEqual("0", conditional.Element("addtorating")!.Value);
            Assert.AreEqual(firstImpression.Element("guid")!.Value, conditional.Element("sourcename")!.Value);
            var corporate = final.ModuleSequence!.Occurrences.Single(row => row.Selection.ModuleId == "8b05f9be-4713-4d96-9c8b-25952b538b3a");
            Assert.AreEqual("<qualitylevel group=\"SINner\">3</qualitylevel>", corporate.Compilation.Effects
                .Single(row => row.EffectKind == "addqualities").IgnoredSourceMetadata["legacy-ignored-nested-qualitylevel"]);
            Assert.IsFalse(corporate.Compilation.Effects.Any(row => row.EffectKind == "qualitylevel"),
                "Legacy addqualities does not execute the nested level. Do not invent a grant.");
            Assert.AreEqual("SINner (National)", final.ModuleSequence.QualityLevels.Single().Target.CanonicalName);

            var mundane = service.PreviewFinalization(finalRequest with
            {
                TalentSelection = new(CharacterCreationKarmaTalentCatalog.MundaneOptionId),
                AttributePurchases = [], SkillSelection = new([], [])
            }).Value!;
            Assert.IsNotNull(mundane.SkillsCatalog, string.Join(", ", mundane.FinalizationBlocked));
            var language = mundane.SkillsCatalog.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
            var completeRequest = finalRequest with
            {
                TalentSelection = new(CharacterCreationKarmaTalentCatalog.MundaneOptionId), AttributePurchases = [],
                SkillSelection = new([new(language.SourceSkillId, language.Kind, 0, IsNativeLanguage: true)], []),
                KarmaResourceInvestment = 0m, GearSelection = [], LifestyleSelection = [], ContactSelection = [],
                MagicSelection = new(null, null, [], [], []), StartingNuyenDiceTotal = 4
            };
            var complete = service.PreviewFinalization(completeRequest).Value!;
            Assert.IsTrue(complete.CanConfirm, string.Join(", ", complete.FinalizationBlocked));
            Assert.AreEqual(complete.ResourcesQuote!.Policy.MaximumKarmaInvestment + 30m,
                complete.ResourcesQuote.MaximumKarmaInvestment, "Born Rich changes the spend cap, not a cash award.");
            var atLimit = service.PreviewFinalization(completeRequest with
                { KarmaResourceInvestment = complete.ResourcesQuote.MaximumKarmaInvestment }).Value!;
            Assert.IsNotNull(atLimit.ResourcesQuote, string.Join(", ", atLimit.FinalizationBlocked));
            Assert.IsFalse(atLimit.ResourcesQuote.Blockers.Contains(CharacterCreationKarmaResourcesRules.InvestmentLimitExceeded));
            var overLimit = service.PreviewFinalization(completeRequest with
                { KarmaResourceInvestment = complete.ResourcesQuote.MaximumKarmaInvestment + 1m }).Value!;
            CollectionAssert.Contains(overLimit.ResourcesQuote!.Blockers.ToArray(), CharacterCreationKarmaResourcesRules.InvestmentLimitExceeded);
            Assert.AreEqual(JsonSerializer.Serialize(complete.ResourcesQuote), JsonSerializer.Serialize(
                CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(completeRequest).Value!.ResourcesQuote),
                "Reopen must not add the cap bonus twice.");
            var command = new CharacterCreationFoundationFinalizationConfirmRequest(completeRequest.Binding,
                completeRequest.DraftRevision, completeRequest.DraftDigest, complete.PreviewDigest, true)
            {
                QualityInstanceValues = completeRequest.QualityInstanceValues,
                TalentSelection = completeRequest.TalentSelection, AttributePurchases = completeRequest.AttributePurchases,
                SkillSelection = completeRequest.SkillSelection, KarmaResourceInvestment = completeRequest.KarmaResourceInvestment,
                GearSelection = completeRequest.GearSelection, LifestyleSelection = completeRequest.LifestyleSelection,
                ContactSelection = completeRequest.ContactSelection, MagicSelection = completeRequest.MagicSelection,
                StartingNuyenDiceTotal = completeRequest.StartingNuyenDiceTotal
            };
            var committed = service.ConfirmFinalization(command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, committed.Outcome, string.Join(", ", committed.Blockers));
            Assert.IsTrue(committed.Value!.CharacterCreated);
            byte[] saved = File.ReadAllBytes(WorkspacePath(directory, id));
            var reopenedStore = new FileWorkspaceStore(directory);
            var reopened = reopenedStore.Get(id).Value!;
            Assert.AreEqual("True", XElement.Parse(reopened.Document.Content).Element("created")!.Value);
            Assert.AreEqual(reopened.ContentRevision, reopened.SavedRevision);
            Assert.AreEqual(committed.Value, CreateService(reopenedStore).ConfirmFinalization(command).Value);
            CollectionAssert.AreEqual(saved, File.ReadAllBytes(WorkspacePath(directory, id)), "No duplicate finalization on reopen.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
