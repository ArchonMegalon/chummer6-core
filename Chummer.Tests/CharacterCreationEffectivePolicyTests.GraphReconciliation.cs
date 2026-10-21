using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using O = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static void AssertGraph352(CharacterCreationCompleteAssessment assessment)
    {
        Assert.AreEqual(CharacterCreationCompleteAssessment.SchemaV3, assessment.Schema);
        Assert.AreEqual(8, assessment.Obligations.Count(row => row.Kind == K.ActiveGraph));
        foreach (var operation in new[] { O.LifestyleGrantEffects, O.LifestyleCostEffects, O.SkillKarmaModifiers })
        {
            var row = Graph342(assessment, operation);
            Assert.AreEqual(C.Evaluated, row.Coverage, operation.ToString());
            Assert.AreEqual(CharacterCreationCompleteReason.CanonicalEvaluation, row.Reason);
            Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
        }
        foreach (var operation in new[] { O.SelectedEffects, O.Amendments, O.Errata, O.CharacterMechanics, O.Campaign })
        {
            var row = Graph342(assessment, operation);
            Assert.AreEqual(C.Unresolved, row.Coverage, operation.ToString());
            Assert.IsNull(row.EvaluationDigest);
        }
        Assert.IsTrue(assessment.Obligations.Where(row => row.Kind is K.Source or K.Sourcebook or K.SelectedGrants)
            .All(row => row.Coverage == C.Unresolved));
        Assert.IsFalse(assessment.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow(13, 14)]
    [DataRow(100, 100)]
    public void Complete352_Absent_career_caps_above_twelve_and_at_100_use_legal_small_allocations(int activeCap, int knowledgeCap)
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, activeCap, knowledgeCap, -1, -1));
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        Assert.AreEqual(activeCap, inputs.Skills.Authority.MaxActiveSkillRatingCreate);
        Assert.AreEqual(activeCap, inputs.Skills.Authority.MaxSkillGroupRatingCreate);
        Assert.AreEqual(knowledgeCap, inputs.Skills.Authority.MaxKnowledgeSkillRatingCreate);
        var source = XElement.Parse(inputs.Magic.Authority.MysticAdeptPowerPointPolicy!.CanonicalSourceXml);
        Assert.IsNull(source.Element("maxskillrating"));
        Assert.IsNull(source.Element("maxknowledgeskillrating"));
        // Source authority at 100 does not require buying 100 levels with a finite point wallet.
        var request = Request340(inputs, 2, 1);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(2, full.Value!.Skills!.SkillGroups.Single().Rating);
        Assert.AreEqual((int?)2, full.Value.Skills.Skills.Single(row => row.Name == "Pistols").Rating);
        AssertLimits340(full.Value.CompleteAssessment!, C.Evaluated);
        AssertGraph352(full.Value.CompleteAssessment!);
        AssertCold340(context, inputs, request, full.Value);
        var complete = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, complete.Outcome);
        Assert.AreEqual(CharacterCreationPolicyRequirement.Complete, complete.Value!.Request.PolicyRequirement);
        AssertLimits340(complete.Value.CompleteAssessment!, C.Evaluated);
        AssertGraph352(complete.Value.CompleteAssessment!);
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete352_Source_101_is_unsupported_even_with_small_legal_allocations_and_matching_typed_caps()
    {
        using var context = ReadyContext.Create(false, rankAssignments: ConfiguredSkillRanks(),
            amendSettings: settings => SetLimits340(settings, 100, 100, -1, -1));
        var inputs = LoadConsumption(context);
        var request = Request340(inputs, 2, 1);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var previous = inputs.Magic.Authority.MysticAdeptPowerPointPolicy!;
        var xml = XElement.Parse(previous.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        SetLimits340(xml, 101, 101, -1, -1);
        Assert.IsTrue(CharacterCreationMysticAdeptPowerPointRules.TryCreatePolicy(previous.SettingsProfileId,
            previous.SettingsInputsDigest, xml.ToString(SaveOptions.DisableFormatting), out var unsupported));
        // Isolate the V3 bound: even a matching caller-typed number is not supported source authority.
        var forged = WithSettingsSource340(inputs, unsupported) with
        {
            EffectivePolicy = inputs.EffectivePolicy! with
            {
                Settings = inputs.EffectivePolicy.Settings.Select(row => LimitIds340.Contains(row.SettingId)
                    ? row with { IntegerValue = 101 } : row).ToArray()
            }
        };
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(forged, request);
        foreach (string id in LimitIds340)
        {
            Assert.AreEqual(CharacterCreationCompleteEvaluability.Unsupported, Limit332(authority, id).Evaluability);
            Assert.AreEqual(CharacterCreationCompleteReason.SourceSemanticsUnproven, Limit332(authority, id).Reason);
        }
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(forged, request).Value);
        string path = ConfiguredSkillSettingsPath(context, CharacterCreationBuildMethods.Priority);
        var document = XDocument.Load(path);
        SetLimits340(ConfiguredSkillProfile(document, CharacterCreationBuildMethods.Priority), 101, 101, -1, -1);
        document.Save(path);
        var current = context.Resolver.TryCreateContext(original.Document.Content)!;
        Assert.IsTrue(current.TryResolveCreationSkillsAuthority(out var skills));
        Assert.IsFalse(skills.IsAuthoritative);
        CollectionAssert.Contains(skills.Blockers.ToArray(), CharacterCreationEffectivePolicyContext.SkillLimitsUnsupported);
        Assert.AreEqual(JsonSerializer.Serialize(original), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }

    [TestMethod]
    public void Complete352_Real_adept_talent_grant_executes_bounded_graph_without_closing_selected_grants()
    {
        using var context = ReadyContext.Create(false, talentValue: "Adept", includeSkillPurchase: true,
            amendSettings: settings => SetLimits340(settings, 6, 6, 12, 12));
        var inputs = LoadConsumption(context);
        var grants = inputs.Magic.SelectedTalent.GrantedQualitySources!;
        Assert.IsTrue(grants.Count > 0);
        Assert.IsTrue(grants.All(CharacterCreationTalentQualitySourceRules.IsValidSource));
        Assert.IsTrue(grants.Any(row => XElement.Parse(row.CanonicalSourceXml).Element("bonus")?.HasElements == true));
        Assert.IsTrue(CharacterCreationLegacySourceProjector.AreCompletionGrantEffectsIndependent(
            inputs.Skills.PrerequisiteDraft, grants));
        // This case exercises the actual talent grant, not a purchased Ambidextrous quality.
        var baseline = Request(inputs, CharacterCreationPolicyRequirement.None) with
        {
            Selections = CharacterCreationCompletionPlannerTests.KnowledgeChoices(inputs)
        };
        var request = Request342(inputs, baseline);
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        Assert.AreEqual(2000m, full.Value!.Lifestyles!.Budget.Used);
        AssertLimits340(full.Value.CompleteAssessment!, C.Evaluated);
        AssertGraph352(full.Value.CompleteAssessment!);
        var replay = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(replay.CompleteAssessment));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
    }
}
