using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    private const string AnsweredKnowledgeXml =
        "<knowledgeskilllevel><name>[Corporation]</name><group>Professional</group><val>2.50</val></knowledgeskilllevel>";

    [TestMethod]
    public void Confirmed_input_text_is_bound_escaped_and_not_a_new_rated_skill_or_source_rewrite()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = InputFixture(directory, AnsweredKnowledgeXml);
            var prompt = fixture.Version.FollowUps.Single();
            const string answer = "Renraku & Söhne <Ops>";
            var ledger = AnswerInput(fixture.Ledger, prompt, answer);
            string before = JsonSerializer.Serialize(ledger);
            var compilation = CharacterCreationFoundationEffectCompiler.Compile(
                RulesetDefaults.Sr5, ledger, fixture.Module, fixture.Version);
            var instruction = compilation.Effects.Single();
            Assert.AreEqual(CharacterCreationFoundationEffectCompilationStatuses.Supported, instruction.CompilationStatus);
            Assert.HasCount(0, instruction.PromptIds);
            Assert.AreEqual(answer, instruction.Parameters["name"]);
            Assert.AreEqual("2.50", instruction.Parameters["val"]);
            Assert.AreEqual("FreeKnowledgeSkills", instruction.TargetBinding!.SourceId);
            Assert.AreEqual(answer, instruction.IgnoredSourceMetadata["legacy-ignored-literal-name"]);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(ledger.ProjectedEffects[0]),
                instruction.InputResolution!.SourceEffectDigest);
            var input = instruction.InputResolution.Inputs.Single();
            Assert.AreEqual(prompt.PromptId, input.PromptId);
            Assert.AreEqual(prompt.ValuePath, input.ValuePath);
            Assert.AreEqual(answer, input.Value);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(prompt), input.PromptDigest);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                instruction with { InstructionDigest = string.Empty }), instruction.InstructionDigest);

            var result = InputPlan(fixture, ledger);
            Assert.IsTrue(result.IsReady, string.Join(", ", result.Blockers));
            var plan = result.Plan!;
            var improvement = XElement.Parse(plan.ImprovementXml.Single());
            Assert.AreEqual("FreeKnowledgeSkills", improvement.Element("improvementttype")!.Value);
            Assert.AreEqual(string.Empty, improvement.Element("improvedname")!.Value);
            Assert.AreEqual("2.50", improvement.Element("val")!.Value);
            Assert.AreEqual("[Corporation]", XElement.Parse(plan.QualityXml)
                .Element("bonus")!.Element("knowledgeskilllevel")!.Element("name")!.Value);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                instruction.InputResolution, plan.EffectProvenance.Single().InputResolution));
            Assert.AreEqual(before, JsonSerializer.Serialize(ledger));
            Assert.AreEqual(plan.PlanDigest, InputPlan(fixture, ledger).Plan!.PlanDigest);

            var other = AnswerInput(ledger, prompt, "Shiawase");
            var changed = InputPlan(fixture, other).Plan!;
            Assert.AreNotEqual(plan.PlanDigest, changed.PlanDigest);
            Assert.AreNotEqual(plan.InstructionDigests[0], changed.InstructionDigests[0]);
            Assert.AreNotEqual(plan.EffectProvenance[0].InputResolution!.ResolvedEffectDigest,
                changed.EffectProvenance[0].InputResolution!.ResolvedEffectDigest);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("German", true)]
    [DataRow("german", false)]
    [DataRow("Japanese", false)]
    public void Confirmed_input_options_require_exact_source_choice_and_preserve_ignored_id(string answer, bool ready)
    {
        string directory = CreateTempDirectory();
        try
        {
            const string sourceId = "a9df9852-6f4d-423d-8251-c92a709c1476";
            var fixture = InputFixture(directory, "<knowledgeskilllevel><options><de>German</de><es>Spanish</es>"
                + "</options><id>" + sourceId + "</id><group>Language</group><val>2</val></knowledgeskilllevel>");
            var ledger = AnswerInput(fixture.Ledger, fixture.Version.FollowUps.Single(), answer);
            var result = InputPlan(fixture, ledger);
            Assert.AreEqual(ready, result.IsReady);
            if (!ready) { Assert.IsNull(result.Plan); return; }
            var provenance = result.Plan!.EffectProvenance.Single();
            Assert.AreEqual(answer, provenance.InputResolution!.Inputs.Single().Value);
            Assert.AreEqual(sourceId, provenance.IgnoredSourceMetadata["legacy-ignored-source-id"]);
            Assert.AreEqual("FreeKnowledgeSkills", provenance.TargetBinding!.SourceId);
            Assert.HasCount(1, result.Plan.ImprovementXml);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("unknown-key")]
    [DataRow("long")]
    [DataRow("blank")]
    [DataRow("whitespace")]
    [DataRow("control")]
    [DataRow("invalid-xml")]
    [DataRow("placeholder")]
    [DataRow("expression")]
    public void Confirmed_input_invalid_answers_never_authorize_a_write_plan(string kind)
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = InputFixture(directory, AnsweredKnowledgeXml);
            var prompt = fixture.Version.FollowUps.Single();
            string value = kind switch
            {
                "long" => new string('x', 1025), "blank" => "", "whitespace" => " Renraku ",
                "control" => "Renraku\nCorporation", "invalid-xml" => "Renraku\ufffe",
                "placeholder" => "[Any]", "expression" => "$Rating", _ => "Renraku"
            };
            var answers = new Dictionary<string, string>(StringComparer.Ordinal);
            if (kind != "missing") answers[prompt.PromptId] = value;
            if (kind == "unknown-key") answers["other-effect:follow-up:1"] = "99";
            var ledger = fixture.Ledger with { FollowUpValues = answers };
            ledger = ledger with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(ledger) };
            var result = InputPlan(fixture, ledger);
            Assert.IsFalse(result.IsReady);
            Assert.IsNull(result.Plan);
            var compiled = CharacterCreationFoundationEffectCompiler.Compile(
                RulesetDefaults.Sr5, ledger, fixture.Module, fixture.Version);
            Assert.IsNull(compiled.Effects.Single().InputResolution);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("path")]
    [DataRow("anchor")]
    [DataRow("duplicate-path")]
    [DataRow("numeric")]
    [DataRow("ambiguous-name")]
    [DataRow("nested")]
    [DataRow("selectskill")]
    [DataRow("projection")]
    public void Confirmed_input_resolution_is_not_a_generic_effect_or_xpath_editor(string kind)
    {
        string directory = CreateTempDirectory();
        try
        {
            string xml = kind switch
            {
                "numeric" => "<knowledgeskilllevel><name>Renraku</name><val>[Rating]</val></knowledgeskilllevel>",
                "ambiguous-name" => "<knowledgeskilllevel><name>[Any]</name><name>Renraku</name></knowledgeskilllevel>",
                "nested" => "<knowledgeskilllevel><nested><name>[Any]</name></nested><val>1</val></knowledgeskilllevel>",
                "selectskill" => "<selectskill limittoskill=\"Pistols,Rifles\" />",
                _ => AnsweredKnowledgeXml
            };
            var fixture = InputFixture(directory, xml);
            var version = fixture.Version;
            var prompt = version.FollowUps.Single();
            if (kind == "path") prompt = prompt with { ValuePath = "knowledgeskilllevel/../val" };
            if (kind == "anchor") prompt = prompt with { SourceAnchorIds = ["other#source"] };
            version = version with { FollowUps = kind == "duplicate-path"
                ? [prompt, prompt with { PromptId = prompt.PromptId + "-duplicate" }] : [prompt] };
            var answers = version.FollowUps.ToDictionary(input => input.PromptId,
                input => input.Options.FirstOrDefault()?.SourceValue ?? "99", StringComparer.Ordinal);
            var ledger = fixture.Ledger with { FollowUpValues = answers };
            if (kind == "projection") ledger = ledger with { ProjectedEffects = ledger.ProjectedEffects
                .Select(effect => effect with { AfterValue = "99" }).ToArray() };
            ledger = ledger with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(ledger) };
            Assert.IsFalse(InputPlan(fixture with { Version = version }, ledger).IsReady);
            var compiled = CharacterCreationFoundationEffectCompiler.Compile(
                RulesetDefaults.Sr5, ledger, fixture.Module, version);
            Assert.IsNull(compiled.Effects.Single().InputResolution);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static LevelWritePlanFixture InputFixture(string directory, string bonus)
        => CreateLevelWritePlanFixture(directory, "confirmed-input", TirModuleId, "Input module", "Nationality",
            15, "67", TirHumanElfVersionId, "Input version", bonus, "");

    private static CharacterCreationFoundationDraftLedger AnswerInput(CharacterCreationFoundationDraftLedger ledger,
        LifeModuleFollowUpPromptDto prompt, string value)
    {
        var result = ledger with { FollowUpValues = new Dictionary<string, string> { [prompt.PromptId] = value } };
        return result with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(result) };
    }

    private static CharacterCreationFoundationEffectWritePlanResult InputPlan(LevelWritePlanFixture fixture,
        CharacterCreationFoundationDraftLedger ledger)
        => CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(fixture.WorkspaceId, RulesetDefaults.Sr5,
            ledger, fixture.Module, fixture.Version, fixture.EffectiveSourceXml, fixture.SourceDigest, "Chocolate");
}
