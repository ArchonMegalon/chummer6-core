using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationFinalizationEvaluatedGraphTests
{
    [TestMethod]
    [DataRow(null, "B", 0)]
    [DataRow("Magician", "B", 0)]
    [DataRow("Mystic Adept", "B", 1)]
    [DataRow("Adept", "D", 0)]
    [DataRow("Technomancer", "B", 0)]
    [DataRow("Aspected Magician", "B", 0)]
    public void Evaluated_graph_matches_confirmed_draft_XML_deltas_and_granted_skill_bases(
        string? talent, string rank, int powerPoints)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: talent, talentRank: rank, mysticPowerPoints: powerPoints,
            qualityName: "Guts", includeSkillPurchase: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        var attributes = auxiliary.CharacterCreationAttributesDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var magic = auxiliary.CharacterCreationMagicResonanceDraft;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft!;
        var loaded = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var result = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributes.Allocations, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        var oldRoot = BaseGraph(before, false, out var oldDeltas, out int oldOrder);
        var evaluatedRoot = BaseGraph(before, true, out var evaluatedDeltas, out int evaluatedOrder);
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(oldRoot, prerequisite, attributes,
            skills, magic, oldDeltas, ref oldOrder));
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(evaluatedRoot, prerequisite,
            projection.Attributes.Attributes, skills.Skills, skills.SkillGroups, inputs, projection,
            magic?.DraftDigest ?? prerequisite.DraftDigest, evaluatedDeltas, ref evaluatedOrder));

        Assert.AreEqual(oldRoot.ToString(SaveOptions.DisableFormatting), evaluatedRoot.ToString(SaveOptions.DisableFormatting));
        Assert.AreEqual(JsonSerializer.Serialize(oldDeltas), JsonSerializer.Serialize(evaluatedDeltas));
        Assert.AreEqual(oldOrder, evaluatedOrder);
        Assert.IsTrue(evaluatedRoot.Element("qualities")!.HasElements);
        Assert.IsTrue(evaluatedRoot.Element("gears")!.HasElements);
        foreach (var skill in skills.Skills)
        {
            string container = skill.Kind == CharacterCreationSkillKinds.Knowledge ? "knoskills" : "skills";
            var saved = evaluatedRoot.Element("newskills")!.Element(container)!.Elements("skill")
                .Single(item => (string?)item.Element("guid") == CharacterCreationFinalizationProjector.StableGuid(
                    $"skill:{skill.Kind}:{skill.SourceSkillId}:{skills.DraftDigest}").ToString("D"));
            Assert.AreEqual(skill.IsNativeLanguage ? 0 : skill.EffectiveRating.GetValueOrDefault() - skill.GrantedRating,
                int.Parse(saved.Element("base")!.Value, CultureInfo.InvariantCulture));
        }
        foreach (var group in skills.SkillGroups)
            Assert.AreEqual(group.Rating - group.GrantedRating,
                int.Parse(evaluatedRoot.Element("newskills")!.Element("groups")!.Elements("group")
                    .Single(item => (string?)item.Element("name") == group.Name).Element("base")!.Value,
                    CultureInfo.InvariantCulture));
        var improvements = evaluatedRoot.Element("improvements")!.Elements("improvement").ToArray();
        Assert.AreEqual(skills.Skills.Count(item => item.GrantedRating > 0),
            improvements.Count(item => (string?)item.Element("improvementttype") == "SkillBase"));
        Assert.AreEqual(skills.SkillGroups.Count(item => item.GrantedRating > 0),
            improvements.Count(item => (string?)item.Element("improvementttype") == "SkillGroupBase"));
        if (powerPoints > 0)
        {
            Assert.AreEqual(powerPoints.ToString(CultureInfo.InvariantCulture), (string?)evaluatedRoot.Element("magsplitadept"));
            Assert.AreEqual((decimal)projection.MysticAdeptPowerPoints!.KarmaCost,
                evaluatedDeltas.Single(item => item.DeltaId == "mystic-adept:power-points").KarmaCost);
        }
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Evaluated_awakened_graph_rejects_changed_sources_evaluation_seed_or_missing_grants()
    {
        using var context = ReadyContext.Create(true, talentValue: "Magician", includeSkillPurchase: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft!;
        var attributes = auxiliary.CharacterCreationAttributesDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var magic = auxiliary.CharacterCreationMagicResonanceDraft!;
        var inputs = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId)).Value!;
        var evaluated = CharacterCreationMagicResonanceService.PreviewCompletion(inputs,
            attributes.Allocations, inputs.Selections).Value!;
        Assert.IsTrue(skills.Skills.Any(item => item.GrantedRating > 0), "The fixture must exercise real talent grants.");
        bool Apply(CharacterCreationMagicResonanceCompletionInputs source,
            CharacterCreationMagicResonanceCompletionProjection proposal, string seed,
            IReadOnlyList<CharacterCreationSkillProjection>? rows = null)
        {
            var root = BaseGraph(before, true, out var deltas, out int order);
            return CharacterCreationAwakenedLegacyProjector.TryApply(root, prerequisite,
                proposal.Attributes.Attributes, rows ?? skills.Skills, skills.SkillGroups,
                source, proposal, seed, deltas, ref order);
        }
        Assert.IsFalse(Apply(inputs, evaluated, string.Empty));
        var wrongBudget = evaluated with { SpellBudget = evaluated.SpellBudget with { Total = evaluated.SpellBudget.Total + 1 } };
        wrongBudget = wrongBudget with { ProposalDigest = CharacterCreationMagicResonanceDigest.Compute(wrongBudget with { ProposalDigest = string.Empty }) };
        Assert.IsFalse(Apply(inputs, wrongBudget, magic.DraftDigest), "A self-hashed false evaluation is not source admission.");
        Assert.IsFalse(Apply(inputs with { Authority = inputs.Authority with
        {
            SourceInputsDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("changed source")
        } }, evaluated, magic.DraftDigest));
        Assert.IsFalse(Apply(inputs, evaluated, magic.DraftDigest,
            skills.Skills.Where(item => item.GrantedRating == 0).ToArray()));
        var legacyRoot = BaseGraph(before, false, out var legacyDeltas, out int legacyOrder);
        Assert.IsFalse(CharacterCreationAwakenedLegacyProjector.TryApply(legacyRoot, prerequisite,
            attributes, skills, magic with { DraftDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8("wrong draft") },
            legacyDeltas, ref legacyOrder), "The saved-draft adapter must retain its original integrity checks.");
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static XElement BaseGraph(WorkspaceStoredDocument workspace, bool evaluated,
        out List<CharacterCreationFinalizationDelta> deltas, out int order)
    {
        deltas = [];
        order = 0;
        var auxiliary = workspace.Document.AuxiliaryState;
        var attributes = auxiliary.CharacterCreationAttributesDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var qualities = auxiliary.CharacterCreationQualitiesDraft!;
        var gear = auxiliary.CharacterCreationGearDraft!;
        var root = XElement.Parse(workspace.Document.Content);
        root.Elements("attributes").Remove();
        root.Elements("newskills").Remove();
        root.Elements("gears").Remove();
        root.SetElementValue("metatype", auxiliary.CharacterCreationPrerequisiteDraft!.HeritageSelection!.MetatypeName);
        foreach (string flag in new[] { "magenabled", "resenabled", "depenabled", "adept", "magician", "technomancer", "ai" })
            root.SetElementValue(flag, "False");
        if (evaluated)
        {
            root.Add(CharacterCreationFinalizationProjector.BuildAttributes(attributes.Attributes, deltas, ref order));
            root.Add(CharacterCreationFinalizationProjector.BuildSkills(skills.Skills, skills.SkillGroups,
                skills.ActivePointTotal, skills.SkillGroupPointTotal, skills.DraftDigest, deltas, ref order));
            CharacterCreationFinalizationProjector.BuildQualityAndEffectGraph(root, qualities.Selections,
                attributes.CreationKarmaTotal, attributes.CreationKarmaUsed, qualities.KarmaRemaining,
                qualities.SourceAnchorIds, qualities.DraftDigest, deltas, ref order);
            root.Add(CharacterCreationFinalizationProjector.BuildGearGraph(gear.Lines, gear.DraftDigest, deltas, ref order));
        }
        else
        {
            root.Add(CharacterCreationFinalizationProjector.BuildAttributes(attributes, deltas, ref order));
            root.Add(CharacterCreationFinalizationProjector.BuildSkills(skills, deltas, ref order));
            CharacterCreationFinalizationProjector.BuildQualityAndEffectGraph(root, qualities, attributes, deltas, ref order);
            root.Add(CharacterCreationFinalizationProjector.BuildGearGraph(gear, deltas, ref order));
        }
        return root;
    }
}
