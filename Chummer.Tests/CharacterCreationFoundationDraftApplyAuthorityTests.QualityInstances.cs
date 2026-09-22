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
    public void Quality_instance_corporate_winner_does_not_inherit_nationality_and_review_is_bound_without_writes()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("quality-instance-review");
            var store = SeedJourney(directory, id);
            var service = CreateService(store);
            AppendSequence(service, id, [FormativeArcologyId, TeenCorporateId, SkipEducationId]);
            byte[] before = File.ReadAllBytes(WorkspacePath(directory, id));
            var first = SequencePreview(service, id);
            var quality = first.ModuleSequence!.QualityLevels.Single();
            Assert.AreEqual("SINner (Corporate Limited)", quality.Target.CanonicalName);
            Assert.AreEqual(3, quality.Level);
            Assert.IsNull(quality.InstanceValue, "A national Tír SIN is not a corporate affiliation.");
            Assert.IsNull(quality.InstancePush);
            Assert.AreEqual("text", quality.InstancePrompt!.InputKind);
            Assert.AreEqual("quality/bonus/selecttext", quality.InstancePrompt.ValuePath);
            CollectionAssert.Contains(first.FinalizationBlocked.ToArray(), CharacterCreationFoundationBlockers.FinalizationPromptRequired);

            var request = QualityInstanceRequest(service, id, new Dictionary<string, string>
                { [quality.InstancePrompt.PromptId] = "Renraku" });
            var reviewed = service.PreviewFinalization(request).Value!;
            var selected = reviewed.ModuleSequence!.QualityLevels.Single();
            Assert.AreEqual("Renraku", selected.InstanceValue);
            Assert.IsNull(selected.InstancePush, "Player input must not be relabelled a source push.");
            Assert.IsFalse(reviewed.FinalizationBlocked.Contains(CharacterCreationFoundationBlockers.FinalizationPromptRequired));
            Assert.AreNotEqual(first.PreviewDigest, reviewed.PreviewDigest);
            Assert.AreNotEqual(first.ModuleSequence.CompilationDigest, reviewed.ModuleSequence.CompilationDigest);
            Assert.AreEqual(quality.InstancePrompt.PromptId, selected.InstancePrompt!.PromptId);
            Assert.IsFalse(reviewed.CanApply, "A resolved instance is not yet the complete atomic writer.");
            var confirm = new CharacterCreationFoundationFinalizationConfirmRequest(request.Binding,
                request.DraftRevision, request.DraftDigest, reviewed.PreviewDigest, true)
                { QualityInstanceValues = request.QualityInstanceValues };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, service.ConfirmFinalization(confirm).Outcome);
            var changed = confirm with { QualityInstanceValues = new Dictionary<string, string>
                { [quality.InstancePrompt.PromptId] = "Shiawase" } };
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, service.ConfirmFinalization(changed).Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
                service.ConfirmFinalization(confirm with { QualityInstanceValues = null }).Outcome);

            var reopened = CreateService(new FileWorkspaceStore(directory));
            Assert.AreEqual(JsonSerializer.Serialize(reviewed), JsonSerializer.Serialize(reopened.PreviewFinalization(request).Value));
            CollectionAssert.AreEqual(before, File.ReadAllBytes(WorkspacePath(directory, id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Quality_instance_unique_same_tier_source_push_is_reused_not_reasked_or_overridden()
    {
        string directory = CreateTempDirectory();
        try
        {
            var id = new CharacterWorkspaceId("quality-instance-source");
            var service = CreateService(SeedJourney(directory, id));
            var preview = SequencePreview(service, id);
            var quality = preview.ModuleSequence!.QualityLevels.Single();
            Assert.AreEqual(1, quality.Level);
            Assert.AreEqual("Tír Tairngire", quality.InstanceValue);
            Assert.AreEqual("single-select", quality.InstancePrompt!.InputKind);
            Assert.HasCount(1, quality.InstancePrompt.Options);
            Assert.AreEqual(quality.InstanceValue, quality.InstancePush!.Literal);
            Assert.AreEqual(preview.ModuleSequence.Occurrences[0].OccurrenceId, quality.InstancePushOccurrenceId);
            var invalid = service.PreviewFinalization(QualityInstanceRequest(service, id,
                new Dictionary<string, string> { [quality.InstancePrompt.PromptId] = "Different nation" })).Value!;
            Assert.IsNull(invalid.ModuleSequence!.QualityLevels.Single().InstanceValue);
            CollectionAssert.Contains(invalid.FinalizationBlocked.ToArray(), CharacterCreationFoundationBlockers.FinalizationPromptRequired);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown")]
    [DataRow("long")]
    [DataRow("control")]
    [DataRow("placeholder")]
    [DataRow("invalid-xml")]
    public void Quality_instance_invalid_input_stays_blocked(string kind)
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, qualities, _) = LoadQualityLevelSources();
            var occurrence = QualityInstanceOccurrence(directory, "", 3, levels, qualities, 1);
            var resolution = QualityInstanceLevel([occurrence], 3, levels, qualities);
            var blockers = new List<string>();
            var first = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [occurrence], qualities, null, blockers).Single();
            string value = kind switch { "long" => new string('x', 1025), "control" => "Renraku\n",
                "placeholder" => "[Corporation]", "invalid-xml" => "\ufffe", _ => "Renraku" };
            var values = new Dictionary<string, string>();
            if (kind != "missing") values[kind == "unknown" ? "wrong-prompt" : first.InstancePrompt!.PromptId] = value;
            blockers.Clear();
            var result = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [occurrence], qualities, values, blockers).Single();
            Assert.IsNull(result.InstanceValue);
            Assert.IsTrue(blockers.Count > 0);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Quality_instance_competing_source_pushes_require_an_exact_player_choice()
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, qualities, _) = LoadQualityLevelSources();
            var first = QualityInstanceOccurrence(Path.Combine(directory, "first"), "<pushtext>UCAS</pushtext>", 1, levels, qualities, 1);
            var second = QualityInstanceOccurrence(Path.Combine(directory, "second"), "<pushtext>CAS</pushtext>", 1, levels, qualities, 2);
            var resolution = QualityInstanceLevel([first, second], 1, levels, qualities);
            var blockers = new List<string>();
            var result = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [first, second], qualities, null, blockers).Single();
            Assert.IsNull(result.InstanceValue);
            CollectionAssert.AreEqual(new[] { "CAS", "UCAS" }, result.InstancePrompt!.Options.Select(option => option.SourceValue).ToArray());
            blockers.Clear();
            var chosen = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [first, second], qualities,
                new Dictionary<string, string> { [result.InstancePrompt.PromptId] = "CAS" }, blockers).Single();
            Assert.AreEqual("CAS", chosen.InstanceValue);
            Assert.AreEqual(second.OccurrenceId, chosen.InstancePushOccurrenceId);
            Assert.HasCount(0, blockers);
            blockers.Clear();
            var invalid = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [first, second], qualities,
                new Dictionary<string, string> { [result.InstancePrompt.PromptId] = "Other nation" }, blockers).Single();
            Assert.IsNull(invalid.InstanceValue);
            Assert.HasCount(1, blockers);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Quality_instance_does_not_reuse_push_already_consumed_by_a_dependent_quality()
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, qualities, _) = LoadQualityLevelSources();
            var occurrence = QualityInstanceOccurrence(directory,
                "<pushtext>UCAS</pushtext><addqualities><addquality>SINner (National)</addquality></addqualities>",
                1, levels, qualities, 1);
            Assert.HasCount(1, occurrence.Compilation.SelectionBindings);
            var resolution = QualityInstanceLevel([occurrence], 1, levels, qualities);
            var blockers = new List<string>();
            var result = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [occurrence], qualities, null, blockers).Single();
            Assert.IsNull(result.InstancePush);
            Assert.IsNull(result.InstanceValue);
            CollectionAssert.Contains(blockers, CharacterCreationFoundationBlockers.FinalizationPromptRequired);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void Quality_instance_rejects_noneditable_catalog_without_typed_option_authority()
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, _, _) = LoadQualityLevelSources();
            var source = XElement.Load(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
            source.Element("qualities")!.Elements("quality").Single(quality => quality.Element("name")!.Value == "SINner (Corporate Limited)")
                .Element("bonus")!.Element("selecttext")!.SetAttributeValue("allowedit", "False");
            string xml = source.ToString(SaveOptions.DisableFormatting);
            Assert.IsTrue(CharacterCreationFoundationQualitySourceAuthority.TryCreate(xml,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml), out var qualities));
            var occurrence = QualityInstanceOccurrence(directory, "", 3, levels, qualities!, 1);
            var resolution = QualityInstanceLevel([occurrence], 3, levels, qualities!);
            var blockers = new List<string>();
            var result = CharacterCreationFoundationQualityInstanceResolver.Resolve([resolution], [occurrence], qualities, null, blockers).Single();
            Assert.IsNull(result.InstancePrompt);
            Assert.IsNull(result.InstanceValue);
            CollectionAssert.Contains(blockers, CharacterCreationFoundationBlockers.FinalizationEffectUnsupported);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static CharacterCreationFoundationFinalizationPreviewRequest QualityInstanceRequest(
        CharacterCreationFoundationService service, CharacterWorkspaceId id, IReadOnlyDictionary<string, string> values)
    {
        var state = Load(service, id);
        return new(state.Binding, state.PendingDraft!.DraftRevision, state.PendingDraft.DraftDigest) { QualityInstanceValues = values };
    }

    private static CharacterCreationLifeModuleOccurrenceCompilation QualityInstanceOccurrence(string directory,
        string pushes, int tier, CharacterCreationFoundationQualityLevelSourceAuthority levels,
        CharacterCreationFoundationQualitySourceAuthority qualities, int order)
    {
        var fixture = InputFixture(directory, pushes + "<qualitylevel group=\"SINner\">"
            + tier.ToString(System.Globalization.CultureInfo.InvariantCulture) + "</qualitylevel>");
        var compiled = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5,
            fixture.Ledger, fixture.Module, fixture.Version, qualitySourceAuthority: qualities, qualityLevelSourceAuthority: levels);
        return new(order, "occurrence-" + order.ToString(System.Globalization.CultureInfo.InvariantCulture), 1,
            fixture.Ledger.Selection, fixture.Ledger.FollowUpValues, fixture.Ledger.SourceAnchorIds, compiled);
    }

    private static CharacterCreationLifeModuleQualityLevelResolution QualityInstanceLevel(
        IReadOnlyList<CharacterCreationLifeModuleOccurrenceCompilation> occurrences, int tier,
        CharacterCreationFoundationQualityLevelSourceAuthority levels, CharacterCreationFoundationQualitySourceAuthority qualities)
    {
        Assert.IsTrue(levels.TryResolveExact("SINner", tier, qualities, out var target));
        return new("SINner", tier, target!, levels.SourceDigest, occurrences.Select(occurrence =>
        {
            var effect = occurrence.Compilation.Effects.Single(item => item.EffectKind == "qualitylevel");
            return new CharacterCreationLifeModuleQualityLevelContribution(occurrence.OccurrenceId, effect.EffectId,
                effect.InstructionDigest, tier);
        }).ToArray(), ["qualities.xml#quality:" + target!.SourceId]);
    }
}
