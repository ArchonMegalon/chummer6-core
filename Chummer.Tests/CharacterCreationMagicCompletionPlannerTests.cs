using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationMagicCompletionPlannerTests
{
    [TestMethod]
    [DataRow("Magician", "MAG")]
    [DataRow("Technomancer", "RES")]
    public void Magic_plan_fills_actual_ranked_tradition_or_stream_and_spell_or_form_slots_without_writes(
        string talentName, string attributeId)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, talentName, "B");
        var (magic, qualities) = Load(context);
        CharacterCreationAttributeAllocation[] attributes = [new(attributeId, 1, 0)];
        var complete = MagicResonanceCompletionTests.CompleteSelections(magic,
            magic.SelectedTalent.Magic + (attributeId == "MAG" ? 1 : 0));
        var request = Request(magic, qualities, complete);
        var files = Snapshot(context);
        var result = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.IsTrue(plan.IsMagicComplete);
        Assert.IsTrue(plan.HasChanges);
        Assert.IsTrue(plan.Evaluations <= request.EvaluationLimit);
        Assert.AreEqual(complete.Tradition, plan.Selections.Tradition);
        Assert.AreEqual(complete.Stream, plan.Selections.Stream);
        CollectionAssert.AreEquivalent(complete.Spells.ToArray(), plan.Selections.Spells.ToArray());
        CollectionAssert.AreEquivalent(complete.ComplexForms.ToArray(), plan.Selections.ComplexForms.ToArray());
        Assert.AreEqual(0m, plan.Preview.SpellBudget.Remaining);
        Assert.AreEqual(0m, plan.Preview.ComplexFormBudget.Remaining);
        Assert.AreEqual(0m, plan.Preview.TraditionBudget.Remaining);
        Assert.AreEqual(0m, plan.Preview.StreamBudget.Remaining);
        Assert.IsNull(plan.GetType().GetProperty("CanConfirm"));
        Assert.AreEqual(plan.PlanDigest, CharacterCreationMagicCompletionRules.ComputePlanDigest(plan));
        Assert.AreEqual(plan.PlanDigest,
            CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], request).Value!.PlanDigest);
        var direct = CharacterCreationMagicResonanceService.PreviewCompletion(magic, attributes, plan.Selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, direct.Outcome);
        Assert.AreEqual(direct.Value!.ProposalDigest, plan.Preview.ProposalDigest);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            direct.Value.Selections, plan.Selections));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Mystic_plan_buys_only_explicit_power_points_with_source_cost_cap_and_remaining_karma_reserve()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var (magic, qualities) = Load(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var desired = MagicResonanceCompletionTests.CompleteSelections(magic, magic.SelectedTalent.Magic + 1, 2);
        var source = CharacterCreationMagicResonanceService.PreviewCompletion(magic, attributes, desired).Value!;
        int actualCost = source.MysticAdeptPowerPoints!.KarmaCost;
        Assert.IsTrue(actualCost > 0, "This real Priority profile must charge the explicit test purchase.");
        var withoutPowerPoints = MagicResonanceCompletionTests.CompleteSelections(magic, magic.SelectedTalent.Magic + 1);
        var baseBudget = CharacterCreationCompletionBudgetService.Preview(magic, qualities, attributes,
            withoutPowerPoints, []).Value!;
        int allowance = baseBudget.AvailableKarmaBeforeResources!.Value;
        var request = Request(magic, qualities, desired);
        var files = Snapshot(context);

        var withoutExplicitPurchase = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], request);
        Assert.AreEqual(0, withoutExplicitPurchase.Value!.Selections.MysticAdeptPowerPoints);
        Assert.AreEqual(0, withoutExplicitPurchase.Value.Preview.MysticAdeptPowerPoints!.KarmaCost);
        Assert.IsFalse(withoutExplicitPurchase.Value.Selections.AdeptPowers.Any());

        var allowedRequest = request with { DesiredTotalMysticAdeptPowerPoints = 2,
            MaximumAdditionalPowerPointKarmaSpend = actualCost, KarmaToReserve = allowance - actualCost };
        var allowed = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], allowedRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, allowed.Outcome, string.Join(",", allowed.Blockers));
        Assert.AreEqual(2, allowed.Value!.Selections.MysticAdeptPowerPoints);
        Assert.AreEqual(actualCost, allowed.Value.Preview.MysticAdeptPowerPoints!.KarmaCost);
        Assert.AreEqual(0m, allowed.Value.Preview.AdeptPowerPointBudget.Remaining);
        Assert.AreEqual(allowance - actualCost, allowed.Value.Budget!.AvailableKarmaBeforeResources!.Value);
        var capped = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            allowedRequest with { MaximumAdditionalPowerPointKarmaSpend = actualCost - 1 });
        Assert.AreEqual(0, capped.Value!.Selections.MysticAdeptPowerPoints);
        CollectionAssert.Contains(capped.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.PowerPointSpendNotAllowed);
        var reserved = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            allowedRequest with { KarmaToReserve = allowance - actualCost + 1 });
        Assert.AreEqual(0, reserved.Value!.Selections.MysticAdeptPowerPoints);
        CollectionAssert.Contains(reserved.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.PowerPointSpendNotAllowed);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Magic_plan_retains_real_saved_choices_and_receipts_and_rejects_manual_removal()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var original = Load(context).Magic;
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var chosen = MagicResonanceCompletionTests.CompleteSelections(original, original.SelectedTalent.Magic + 1, 1);
        MagicResonanceCompletionTests.SaveAttributes(context, attributes);
        var service = new CharacterCreationMagicResonanceService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var preview = service.Preview(new(state.Binding, chosen));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
            service.Confirm(new(state.Binding, chosen, preview.Value!.PreviewDigest, "planner-original-magic", true)).Outcome);
        using var cold = context.Restart();
        var (magic, qualities) = Load(cold);
        Assert.IsNotNull(magic.SavedMagicState);
        var savedChoices = magic.Selections;
        Assert.AreEqual(chosen.Tradition, savedChoices.Tradition);
        Assert.AreEqual(chosen.Stream, savedChoices.Stream);
        Assert.AreEqual(chosen.MysticAdeptPowerPoints, savedChoices.MysticAdeptPowerPoints);
        CollectionAssert.AreEquivalent(chosen.AdeptPowers.ToArray(), savedChoices.AdeptPowers.ToArray());
        CollectionAssert.AreEquivalent(chosen.Spells.ToArray(), savedChoices.Spells.ToArray());
        CollectionAssert.AreEquivalent(chosen.ComplexForms.ToArray(), savedChoices.ComplexForms.ToArray());
        var files = Snapshot(cold);
        var request = Request(magic, qualities, savedChoices) with { ManualSelections = savedChoices };
        var unchanged = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, unchanged.Outcome, string.Join(",", unchanged.Blockers));
        Assert.IsFalse(unchanged.Value!.HasChanges);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(savedChoices, unchanged.Value.Selections));
        Assert.AreEqual(magic.SnapshotDigest, Load(cold).Magic.SnapshotDigest);
        var removal = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { ManualSelections = savedChoices with { AdeptPowers = [] } });
        Assert.IsNull(removal.Value);
        CollectionAssert.Contains(removal.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.ManualSelectionConflict);
        var noRefund = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { DesiredTotalMysticAdeptPowerPoints = 0 });
        Assert.AreEqual(1, noRefund.Value!.Selections.MysticAdeptPowerPoints);
        CollectionAssert.Contains(noRefund.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.ManualSelectionConflict);
        AssertUnchanged(cold, files);
    }

    [TestMethod]
    public void Magic_plan_reports_exhaustion_work_limit_stale_snapshot_and_cancellation_without_saving()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Magician", "B");
        var (magic, qualities) = Load(context);
        CharacterCreationAttributeAllocation[] attributes = [new("MAG", 1, 0)];
        var complete = MagicResonanceCompletionTests.CompleteSelections(magic, magic.SelectedTalent.Magic + 1);
        var request = Request(magic, qualities, complete);
        var files = Snapshot(context);
        var exhausted = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { RankedTraditions = [], RankedSpells = [] });
        Assert.IsNotNull(exhausted.Value);
        Assert.IsFalse(exhausted.Value!.IsMagicComplete);
        CollectionAssert.Contains(exhausted.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.CandidateOrderExhausted);
        var capped = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { EvaluationLimit = 3 });
        Assert.AreEqual(3, capped.Value!.Evaluations);
        Assert.IsFalse(capped.Value.HasChanges);
        CollectionAssert.Contains(capped.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.WorkLimitReached);
        var stale = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { MagicSnapshotDigest = "stale" });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        var unknown = CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [],
            request with { RankedSpells = [complete.Spells[0] with { SourceId = "not-an-admitted-source" }] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, unknown.Outcome);
        Assert.IsNull(unknown.Value);
        CollectionAssert.Contains(unknown.Blockers.ToArray(), CharacterCreationMagicCompletionBlockers.CandidateInvalid);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool caught = false;
        try { CharacterCreationMagicCompletionPlanner.Plan(magic, qualities, attributes, [], request, canceled.Token); }
        catch (OperationCanceledException) { caught = true; }
        Assert.IsTrue(caught);
        AssertUnchanged(context, files);
    }

    private static CharacterCreationMagicCompletionRequest Request(CharacterCreationMagicResonanceCompletionInputs magic,
        CharacterCreationQualitiesCompletionInputs qualities, CharacterCreationMagicResonanceSelections complete)
        => new(magic.SnapshotDigest, qualities.SnapshotDigest, new(null, null, [], [], []),
            complete.Tradition is null ? [] : [complete.Tradition],
            complete.Stream is null ? [] : [complete.Stream], complete.Spells.ToArray(), complete.ComplexForms.ToArray(),
            complete.AdeptPowers.Select(row => row.Identity).ToArray());

    private static (CharacterCreationMagicResonanceCompletionInputs Magic, CharacterCreationQualitiesCompletionInputs Qualities)
        Load(ReadyContext context)
    {
        var magic = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        var qualities = new CharacterCreationQualitiesService(context.Store, context.Resolver,
            new CharacterCreationPrerequisiteService(context.Store, context.Queries, context.Resolver),
            new CharacterCreationAttributesService(context.Store, context.Resolver)).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome, string.Join(",", qualities.Blockers));
        return (magic.Value!, qualities.Value!);
    }

    private static Dictionary<string, byte[]> Snapshot(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(ReadyContext context, Dictionary<string, byte[]> before)
    {
        var after = Snapshot(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }
}
