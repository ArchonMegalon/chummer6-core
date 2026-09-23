using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Military_school_requires_explicit_dependent_quality_inputs_after_the_SIN_is_answered()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("military-quality-inputs");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            AppendSequence(service, id, [FormativeArcologyId, "15bd4283-f287-4be7-b174-9e5ab97bda1a", SkipEducationId]);
            var journey = JourneyState(service, id);
            var request = new CharacterCreationLifeModuleFinishRequest(journey.Binding, journey.DraftRevision, journey.DraftDigest);
            var finish = service.PreviewFinishSelection(request).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ConfirmFinishSelection(new(request, finish.PreviewDigest, true)).Outcome);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var first = SequencePreview(service, id);
            var sin = first.ModuleSequence!.QualityLevels.Single().InstancePrompt!;
            var preview = service.PreviewFinalization(QualityInstanceRequest(service, id,
                new Dictionary<string, string> { [sin.PromptId] = "Renraku" })).Value!;
            CollectionAssert.Contains(preview.FinalizationBlocked.ToArray(),
                CharacterCreationFoundationBlockers.FinalizationPromptRequired,
                "Rank and Code of Honor each need player text; answering the SIN must not leave only an opaque unsupported-effect blocker.");
            var instances = preview.ModuleSequence!.DependentQualityInstances!;
            Assert.HasCount(2, instances);
            CollectionAssert.AreEquivalent(new[] { "Rank (Neither Military nor Law Enforcement) I", "Code of Honor" },
                instances.Select(row => row.InstancePrompt.Label).ToArray());
            Assert.IsTrue(instances.All(row => row.InstanceValue is null && row.InstancePrompt.IsRequired
                && row.InstancePrompt.InputKind == "text" && row.InstancePrompt.SourceAnchorIds.Any(anchor => anchor.StartsWith("qualities.xml#quality:", StringComparison.Ordinal))));
            var values = new Dictionary<string, string> { [sin.PromptId] = "Renraku" };
            foreach (var row in instances) values[row.InstancePrompt.PromptId] = row.InstancePrompt.Label == "Code of Honor"
                ? "Protect noncombatants" : "Academy cadet";
            var answeredRequest = QualityInstanceRequest(service, id, values);
            var answered = service.PreviewFinalization(answeredRequest).Value!;
            Assert.IsNotNull(answered.EffectWriteSummary, string.Join(", ", answered.FinalizationBlocked));
            Assert.AreEqual(2, answered.EffectWriteSummary.DependentQualityCount);
            Assert.AreNotEqual(preview.PreviewDigest, answered.PreviewDigest);
            Assert.IsFalse(answered.FinalizationBlocked.Contains(CharacterCreationFoundationBlockers.FinalizationPromptRequired));
            Assert.IsFalse(answered.CanApply, "Resolving quality text must not supply the other unchosen Career inputs.");
            Assert.AreEqual(JsonSerializer.Serialize(answered), JsonSerializer.Serialize(
                CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(answeredRequest).Value));

            var plan = BuildDependentQualityPlan(store, id, answered.ModuleSequence!);
            Assert.IsNotNull(plan.Plan, string.Join(", ", plan.Blockers));
            var created = plan.Plan.QualityXml.Select(XElement.Parse).ToArray();
            Assert.AreEqual("Protect noncombatants", created.Single(row => row.Element("name")!.Value == "Code of Honor").Element("extra")!.Value);
            Assert.AreEqual("Academy cadet", created.Single(row => row.Element("name")!.Value == "Rank (Neither Military nor Law Enforcement) I").Element("extra")!.Value);
            var military = answered.ModuleSequence!.Occurrences.Single(row => row.Selection.ModuleId == "15bd4283-f287-4be7-b174-9e5ab97bda1a");
            Assert.HasCount(0, military.Compilation.SelectionBindings, "Player input must not be relabelled a source push.");
            Assert.HasCount(0, military.Compilation.SelectionPushes);

            foreach (string invalid in new[] { "", "[Code]", "trailing ", "line\nbreak", "\ufffe", new string('x', 1025) })
            {
                var invalidValues = new Dictionary<string, string>(values) { [instances[0].InstancePrompt.PromptId] = invalid };
                var rejected = service.PreviewFinalization(answeredRequest with { QualityInstanceValues = invalidValues });
                Assert.IsFalse(rejected.Value?.CanApply == true);
                Assert.IsNull(rejected.Value?.EffectWriteSummary, "Invalid dependent text produced a write plan.");
            }
            var unknownValues = new Dictionary<string, string>(values) { ["dependent-quality-instance:foreign"] = "Unbound text" };
            Assert.IsNull(service.PreviewFinalization(answeredRequest with { QualityInstanceValues = unknownValues }).Value?.EffectWriteSummary);

            var confirm = new CharacterCreationFoundationFinalizationConfirmRequest(answeredRequest.Binding,
                answeredRequest.DraftRevision, answeredRequest.DraftDigest, answered.PreviewDigest, true)
                { QualityInstanceValues = values };
            var changedValues = new Dictionary<string, string>(values) { [instances[0].InstancePrompt.PromptId] = "Changed after review" };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                service.ConfirmFinalization(confirm with { QualityInstanceValues = changedValues }).Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                service.ConfirmFinalization(confirm with { QualityInstanceValues = new Dictionary<string, string> { [sin.PromptId] = "Renraku" } }).Outcome);

            foreach (string tamper in new[] { "occurrence", "consumer", "prompt", "removed" })
            {
                var sequence = answered.ModuleSequence!;
                var altered = sequence.DependentQualityInstances!.ToArray();
                altered[0] = tamper switch
                {
                    "occurrence" => altered[0] with { OccurrenceId = sequence.Occurrences[0].OccurrenceId },
                    "consumer" => altered[0] with { ConsumerId = altered[1].ConsumerId },
                    "prompt" => altered[0] with { InstancePrompt = altered[0].InstancePrompt with { Label = "Forged" } },
                    _ => altered[0]
                };
                sequence = sequence with { DependentQualityInstances = tamper == "removed" ? altered.Skip(1).ToArray() : altered };
                sequence = sequence with { CompilationDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    sequence with { CompilationDigest = string.Empty }) };
                Assert.IsNull(BuildDependentQualityPlan(store, id, sequence).Plan, "Rehashed " + tamper + " selection was trusted.");
            }
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Sequence_without_dependent_inputs_preserves_the_historical_serialized_shape()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("historical-sequence-shape");
            var preview = SequencePreview(CreateService(SeedJourney(directory, id)), id);
            Assert.IsNull(preview.ModuleSequence!.DependentQualityInstances);
            string json = JsonSerializer.Serialize(preview);
            Assert.IsFalse(json.Contains("DependentQualityInstances", StringComparison.Ordinal),
                "A new empty field changes canonical digests of already-saved finalization previews.");
            var reopened = JsonSerializer.Deserialize<CharacterCreationFoundationFinalizationPreview>(json)!;
            Assert.AreEqual(json, JsonSerializer.Serialize(reopened));
            Assert.AreEqual(preview.PreviewDigest, reopened.PreviewDigest);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationFoundationSequenceWritePlanResult BuildDependentQualityPlan(
        FileWorkspaceStore store, CharacterWorkspaceId id, CharacterCreationLifeModuleSequenceCompilation sequence)
    {
        var workspace = store.Get(id).Value!;
        var catalog = CreateCatalog();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(workspace.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationFoundationEffectSources(out var sources));
        Assert.IsTrue(sources!.TryCreateAuthorities(out var skills, out var qualities, out var levels, out _));
        string catalogDigest = catalog.GetAuthority().RawXmlDigest;
        return CharacterCreationFoundationLifeModuleQualityWritePlanner.BuildSequence(RulesetDefaults.Sr5,
            workspace.Document.Content, workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!, sequence,
            catalog.GetOptionProjections().ToArray(), catalog.ReadSourceBytes(catalogDigest), catalogDigest, skills, qualities, levels);
    }
}
