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
public sealed class CharacterCreationSelectedAwakenedTests
{
    [TestMethod]
    [DataRow(null, "B", 0)]
    [DataRow("Magician", "B", 0)]
    [DataRow("Mystic Adept", "B", 1)]
    [DataRow("Adept", "D", 0)]
    [DataRow("Technomancer", "B", 0)]
    [DataRow("Aspected Magician", "B", 0)]
    public void Selected_graph_matches_full_source_graph_XML_deltas_and_instance_ids(
        string? talent, string rank, int powerPoints)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true,
            talentValue: talent, talentRank: rank, mysticPowerPoints: powerPoints,
            qualityName: "Guts", includeSkillPurchase: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var inputs = LoadInputs(context);
        var full = CharacterCreationMagicResonanceService.PreviewCompletion(inputs,
            auxiliary.CharacterCreationAttributesDraft!.Allocations, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var evaluated = full.Value!;
        var capture = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs, evaluated.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, capture.Outcome, string.Join(",", capture.Blockers));
        var witness = capture.Value!;
        string witnessBefore = CharacterCreationSelectedMagicRules.ComputePayloadDigest(witness);
        string seed = auxiliary.CharacterCreationMagicResonanceDraft?.DraftDigest ?? prerequisite.DraftDigest;
        var fullRoot = BaseGraph(before, evaluated.Attributes.Attributes, out var fullDeltas, out int fullOrder);
        var selectedRoot = BaseGraph(before, evaluated.Attributes.Attributes, out var selectedDeltas, out int selectedOrder);
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(fullRoot, prerequisite,
            evaluated.Attributes.Attributes, skills.Skills, skills.SkillGroups,
            inputs, evaluated, seed, fullDeltas, ref fullOrder));
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApplySelected(selectedRoot, prerequisite,
            inputs.Attributes.Attributes, evaluated.Attributes.Attributes, skills.Skills, skills.SkillGroups,
            witness, seed, selectedDeltas, ref selectedOrder));

        AssertSameGraph(fullRoot, fullDeltas, fullOrder, selectedRoot, selectedDeltas, selectedOrder);
        Assert.IsTrue(selectedRoot.Element("qualities")!.HasElements);
        Assert.IsTrue(selectedRoot.Element("gears")!.HasElements);
        var improvements = selectedRoot.Element("improvements")!.Elements("improvement").ToArray();
        Assert.AreEqual(skills.Skills.Count(item => item.GrantedRating > 0),
            improvements.Count(item => (string?)item.Element("improvementttype") == "SkillBase"));
        Assert.AreEqual(skills.SkillGroups.Count(item => item.GrantedRating > 0),
            improvements.Count(item => (string?)item.Element("improvementttype") == "SkillGroupBase"));
        if (powerPoints > 0)
        {
            Assert.AreEqual(powerPoints.ToString(CultureInfo.InvariantCulture), (string?)selectedRoot.Element("magsplitadept"));
            Assert.AreEqual((decimal)evaluated.MysticAdeptPowerPoints!.KarmaCost,
                selectedDeltas.Single(item => item.DeltaId == "mystic-adept:power-points").KarmaCost);
        }
        Assert.AreEqual(witnessBefore, CharacterCreationSelectedMagicRules.ComputePayloadDigest(witness));
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Selected_graph_rejects_missing_or_mutated_source_rows_invalid_seed_and_missing_actual_grants()
    {
        using var context = ReadyContext.Create(true, talentValue: "Magician", includeSkillPurchase: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var inputs = LoadInputs(context);
        var full = CharacterCreationMagicResonanceService.PreviewCompletion(inputs,
            auxiliary.CharacterCreationAttributesDraft!.Allocations, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var evaluated = full.Value!;
        var captured = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs, evaluated.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        string seed = auxiliary.CharacterCreationMagicResonanceDraft!.DraftDigest;
        Assert.IsTrue(witness.Spells.Count > 0);
        Assert.IsTrue(skills.Skills.Any(item => item.GrantedRating > 0), "Use actual saved talent grants, not invented rows.");

        bool Apply(CharacterCreationSelectedMagicAuthority source, string instanceSeed,
            IReadOnlyList<CharacterCreationSkillProjection>? skillRows = null)
        {
            var root = BaseGraph(before, evaluated.Attributes.Attributes, out var deltas, out int order);
            return CharacterCreationAwakenedLegacyProjector.TryApplySelected(root, prerequisite,
                inputs.Attributes.Attributes, evaluated.Attributes.Attributes, skillRows ?? skills.Skills, skills.SkillGroups,
                source, instanceSeed, deltas, ref order);
        }

        Assert.IsTrue(Apply(witness, seed));
        Assert.IsFalse(Apply(witness, string.Empty));
        var missing = witness with { Spells = witness.Spells.Skip(1).ToArray() };
        missing = missing with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(missing) };
        Assert.IsFalse(Apply(missing, seed));
        var alteredSpell = witness.Spells[0] with { CanonicalSourceXml = "<spell />" };
        var changed = witness with { Spells = new[] { alteredSpell }.Concat(witness.Spells.Skip(1)).ToArray() };
        changed = changed with { PayloadDigest = CharacterCreationSelectedMagicRules.ComputePayloadDigest(changed) };
        Assert.IsFalse(Apply(changed, seed), "Rehashing the witness does not repair altered source XML or its original row digest.");
        Assert.IsFalse(Apply(witness, seed, skills.Skills.Where(item => item.GrantedRating == 0).ToArray()));
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    [TestMethod]
    public void Selected_graph_uses_proposed_attributes_without_reinterpreting_saved_history_as_future_attributes()
    {
        using var context = ReadyContext.Create(true, talentValue: "Magician", includeSkillPurchase: true);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var auxiliary = before.Document.AuxiliaryState;
        var prerequisite = auxiliary.CharacterCreationPrerequisiteDraft!;
        var skills = auxiliary.CharacterCreationSkillsDraft!;
        var inputs = LoadInputs(context);
        CharacterCreationAttributeAllocation[] proposedAllocations = [new("MAG", 1, 0)];
        var full = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, proposedAllocations, inputs.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var evaluated = full.Value!;
        Assert.AreEqual(inputs.Attributes.Attributes.Single(row => row.AttributeId == "MAG").Current + 1,
            evaluated.Attributes.Attributes.Single(row => row.AttributeId == "MAG").Current);
        var captured = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(inputs, evaluated.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        Assert.IsTrue(witness.OriginalHadSavedMagic);
        string seed = auxiliary.CharacterCreationMagicResonanceDraft!.DraftDigest;
        var fullRoot = BaseGraph(before, evaluated.Attributes.Attributes, out var fullDeltas, out int fullOrder);
        var selectedRoot = BaseGraph(before, evaluated.Attributes.Attributes, out var selectedDeltas, out int selectedOrder);
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApply(fullRoot, prerequisite,
            evaluated.Attributes.Attributes, skills.Skills, skills.SkillGroups,
            inputs, evaluated, seed, fullDeltas, ref fullOrder));
        Assert.IsTrue(CharacterCreationAwakenedLegacyProjector.TryApplySelected(selectedRoot, prerequisite,
            inputs.Attributes.Attributes, evaluated.Attributes.Attributes, skills.Skills, skills.SkillGroups,
            witness, seed, selectedDeltas, ref selectedOrder));
        AssertSameGraph(fullRoot, fullDeltas, fullOrder, selectedRoot, selectedDeltas, selectedOrder);

        var wrongRoot = BaseGraph(before, evaluated.Attributes.Attributes, out var wrongDeltas, out int wrongOrder);
        Assert.IsFalse(CharacterCreationAwakenedLegacyProjector.TryApplySelected(wrongRoot, prerequisite,
            evaluated.Attributes.Attributes, evaluated.Attributes.Attributes, skills.Skills, skills.SkillGroups,
            witness, seed, wrongDeltas, ref wrongOrder), "Proposed MAG must not replace the original saved history's attribute authority.");
        Assert.AreEqual(JsonSerializer.Serialize(before), JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value));
    }

    private static CharacterCreationMagicResonanceCompletionInputs LoadInputs(ReadyContext context)
    {
        var loaded = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value);
        return loaded.Value!;
    }

    private static void AssertSameGraph(XElement fullRoot, IReadOnlyList<CharacterCreationFinalizationDelta> fullDeltas,
        int fullOrder, XElement selectedRoot, IReadOnlyList<CharacterCreationFinalizationDelta> selectedDeltas, int selectedOrder)
    {
        Assert.AreEqual(fullRoot.ToString(SaveOptions.DisableFormatting), selectedRoot.ToString(SaveOptions.DisableFormatting));
        Assert.AreEqual(JsonSerializer.Serialize(fullDeltas), JsonSerializer.Serialize(selectedDeltas));
        Assert.AreEqual(fullOrder, selectedOrder);
        CollectionAssert.AreEqual(fullRoot.Descendants("guid").Select(item => item.Value).ToArray(),
            selectedRoot.Descendants("guid").Select(item => item.Value).ToArray());
    }

    private static XElement BaseGraph(WorkspaceStoredDocument workspace,
        IReadOnlyList<CharacterCreationAttributeProjection> proposedAttributes,
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
        root.Add(CharacterCreationFinalizationProjector.BuildAttributes(proposedAttributes, deltas, ref order));
        root.Add(CharacterCreationFinalizationProjector.BuildSkills(skills.Skills, skills.SkillGroups,
            skills.ActivePointTotal, skills.SkillGroupPointTotal, skills.DraftDigest, deltas, ref order));
        CharacterCreationFinalizationProjector.BuildQualityAndEffectGraph(root, qualities.Selections,
            attributes.CreationKarmaTotal, attributes.CreationKarmaUsed, qualities.KarmaRemaining,
            qualities.SourceAnchorIds, qualities.DraftDigest, deltas, ref order);
        root.Add(CharacterCreationFinalizationProjector.BuildGearGraph(gear.Lines, gear.DraftDigest, deltas, ref order));
        return root;
    }
}
