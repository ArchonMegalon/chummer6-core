using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("valid")]
    [DataRow("conflicting-fund")]
    [DataRow("superseded-national-sin")]
    public void Rich_kid_school_business_sequence_checks_Trust_Fund_against_the_complete_quality_graph(string scenario)
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("salish-rich-kid-school-business");
            var store = new FileWorkspaceStore(directory);
            string xml = CharacterXml("Human");
            if (scenario == "conflicting-fund")
                xml = xml.Replace("</character>", "<qualities><quality>"
                    + "<guid>00000000-0000-0000-0000-000000000001</guid>"
                    + "<name>Trust Fund III</name><qualitysource>Selected</qualitysource>"
                    + "</quality></qualities></character>", StringComparison.Ordinal);
            Assert.IsTrue(store.CreateWorkspaceDocument(id, new WorkspaceDocument(xml, RulesetDefaults.Sr5)).Success);
            var service = CreateService(store);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                Confirm(service, Preview(service, Load(service, id).Binding,
                    "67023613-54af-4950-9a6e-a33bee9ecf59", "042ede14-e0fb-4fb9-95d6-8ddad27d96a3")).Outcome);
            AppendSequence(service, id, ["4f078a7f-bfa5-4eba-97f9-a97f06eab6e8", "15bd4283-f287-4be7-b174-9e5ab97bda1a"]);
            var journey = JourneyState(service, id);
            var university = journey.Options.Single(row => row.ModuleId == "142d7a1b-c676-4bf6-a71f-bc2d29617a14");
            var business = university.Versions.Single(row => row.VersionId == "e5b8fad6-b50b-40a9-93ba-7e6377aae061");
            var values = university.FollowUps.Concat(business.FollowUps).ToDictionary(row => row.PromptId,
                row => row.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Economics");
            var moduleRequest = new CharacterCreationLifeModulePreviewRequest(journey.Binding, journey.DraftRevision,
                journey.DraftDigest, new(university.ModuleId, business.VersionId), values);
            var modulePreview = service.PreviewModule(moduleRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ConfirmModule(new(moduleRequest, modulePreview.PreviewDigest, true)).Outcome);
            journey = JourneyState(service, id);
            Assert.AreEqual(170m, journey.Budget.Used);
            if (scenario == "superseded-national-sin")
            {
                // Corporate Limited replaces National; the retired National
                // tier must not satisfy Trust Fund II's source requirement.
                var corporate = journey.Options.Single(row => row.ModuleId == "adeea2d5-ef7a-4852-81da-627197a31dd2");
                var employee = corporate.Versions.Single(row => row.VersionId == "2c0070c5-2284-4161-8a90-2c89e0482b85");
                var corporateValues = corporate.FollowUps.Concat(employee.FollowUps).ToDictionary(row => row.PromptId,
                    row => row.Options.FirstOrDefault(option => option.IsEnabled)?.SourceValue ?? "Renraku");
                var corporateRequest = new CharacterCreationLifeModulePreviewRequest(journey.Binding, journey.DraftRevision,
                    journey.DraftDigest, new(corporate.ModuleId, employee.VersionId), corporateValues);
                var corporatePreview = service.PreviewModule(corporateRequest).Value!;
                Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                    service.ConfirmModule(new(corporateRequest, corporatePreview.PreviewDigest, true)).Outcome);
                journey = JourneyState(service, id);
            }
            var finishRequest = new CharacterCreationLifeModuleFinishRequest(journey.Binding, journey.DraftRevision, journey.DraftDigest);
            var finish = service.PreviewFinishSelection(finishRequest).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ConfirmFinishSelection(new(finishRequest, finish.PreviewDigest, true)).Outcome);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));

            var baseline = SequencePreview(service, id);
            var answers = baseline.ModuleSequence!.DependentQualityInstances!.ToDictionary(row => row.InstancePrompt.PromptId,
                row => row.InstancePrompt.Label == "Code of Honor" ? "Protect noncombatants" : "Academy cadet");
            foreach (var level in baseline.ModuleSequence.QualityLevels.Where(row => row.InstancePrompt is not null))
                answers[level.InstancePrompt!.PromptId] = level.InstanceValue
                    ?? level.InstancePrompt.Options.FirstOrDefault(row => row.IsEnabled)?.SourceValue ?? "Renraku";
            var request = QualityInstanceRequest(service, id, answers);
            var preview = service.PreviewFinalization(request).Value!;
            var plan = BuildDependentQualityPlan(store, id, preview.ModuleSequence!);
            if (scenario != "valid")
            {
                Assert.IsNull(plan.Plan, "Trust Fund conditions must not be ignored: " + scenario);
                CollectionAssert.Contains(plan.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationRequirementUnsupported);
                Assert.IsNull(preview.EffectWriteSummary);
            }
            else
            {
                Assert.IsNotNull(plan.Plan, string.Join(", ", plan.Blockers));
                Assert.IsNotNull(preview.EffectWriteSummary, string.Join(", ", preview.FinalizationBlocked));
                var qualities = plan.Plan.QualityXml.Select(XElement.Parse).ToArray();
                Assert.AreEqual("Salish-Shidhe Council", qualities.Single(row => row.Element("name")!.Value == "SINner (National)").Element("extra")!.Value);
                Assert.AreEqual("Poor", qualities.Single(row => row.Element("name")!.Value == "Prejudiced (Common, Outspoken)").Element("extra")!.Value);
                var fund = qualities.Single(row => row.Element("name")!.Value == "Trust Fund II");
                var improvement = plan.Plan.ImprovementXml.Select(XElement.Parse)
                    .Single(row => row.Element("improvementttype")!.Value == "TrustFund");
                Assert.AreEqual("2", improvement.Element("val")!.Value);
                Assert.AreEqual(fund.Element("guid")!.Value, improvement.Element("sourcename")!.Value);
                Assert.IsFalse(preview.CanApply, "Quality admission must not choose the remaining Career inputs.");
                Assert.AreEqual(JsonSerializer.Serialize(preview), JsonSerializer.Serialize(
                    CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value));
            }
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)), "Preview must not write the runner.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
