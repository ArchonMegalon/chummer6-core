using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPurchaseCompletionPlannerTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Purchase_plan_prices_ranked_additions_skips_unaffordable_and_unavailable_without_writes(string method)
    {
        using var context = ReadyContext.CreateUnprepared(method);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var (inputs, magic) = Load(context);
        var request = Request(inputs, magic);
        var baseline = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request).Value!;
        Assert.IsNotNull(baseline);
        decimal funding = baseline.Preview.Gear.Resources.Budget.TotalStartingNuyen;
        var cheap = inputs.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageCost > 0m && row.PackageCost < 100m && row.PackageQuantity == 1);
        var costly = inputs.Gear.State.Authority.Options.First(row => row.IsSelectable && row.PackageQuantity == 1
            && row.PackageCost > 0m && decimal.Floor(funding / row.PackageCost) + 1
                <= inputs.Gear.State.Authority.MaximumQuantityPerLine);
        var unavailable = inputs.Gear.State.Authority.Options.First(row => !row.IsSelectable
            && row.Blockers.Contains(CharacterCreationGearBlockers.UnsupportedSemantics));
        var home = Home(inputs.Authority);
        CharacterCreationPurchaseCompletionTarget[] targets = [
            new(new(costly.OptionId, (int)decimal.Floor(funding / costly.PackageCost) + 1)),
            new(new(unavailable.OptionId, 1)), new(new(cheap.OptionId, 1)), new(Lifestyle: home)];
        request = request with { Targets = targets, StartingLifestyleId = home.LifestyleId,
            MaximumAdditionalSpend = cheap.PackageCost + 2000m, NuyenToReserve = funding - cheap.PackageCost - 2000m };
        var files = Snapshot(context);
        var result = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.IsNotNull(plan);
        Assert.IsFalse(plan.IsPurchasePlanComplete);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unaffordable, plan.Targets[0].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unavailable, plan.Targets[1].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, plan.Targets[2].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, plan.Targets[3].Outcome);
        Assert.AreEqual(5, plan.Evaluations);
        Assert.AreEqual(cheap.OptionId, plan.Gear.Single().OptionId);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(home, plan.Lifestyles.Single()));
        Assert.AreEqual(home.LifestyleId, plan.StartingLifestyleId);
        Assert.AreEqual(cheap.PackageCost + 2000m, plan.AdditionalSpend);
        Assert.AreEqual(request.NuyenToReserve, plan.Preview.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(0, plan.Preview.Gear.Resources.SelectedOption.KarmaInvestment);
        Assert.IsNull(plan.GetType().GetProperty("CanConfirm"));
        Assert.AreEqual(plan.PlanDigest,
            CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request).Value!.PlanDigest);
        var accepted = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
            request with { Targets = targets.Skip(2).ToArray() });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, accepted.Outcome, string.Join(",", accepted.Blockers));
        Assert.IsTrue(accepted.Value!.IsPurchasePlanComplete);
        targets[2] = new(new("not-a-source-option", 999));
        Assert.AreEqual(cheap.OptionId, plan.Request.Targets[2].Gear!.OptionId);
        Assert.AreEqual(plan.PlanDigest, CharacterCreationPurchaseCompletionRules.ComputePlanDigest(plan));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Purchase_plan_respects_explicit_reserve_spend_and_work_caps_and_cancellation()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var (inputs, magic) = Load(context);
        var request = Request(inputs, magic);
        decimal funding = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request)
            .Value!.Preview.Gear.Resources.Budget.TotalStartingNuyen;
        var cheap = inputs.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageCost > 0m && row.PackageCost < 100m && row.PackageQuantity == 1);
        var larger = inputs.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageCost > cheap.PackageCost && row.PackageCost < funding && row.PackageQuantity == 1);
        request = request with { Targets = [new(new(larger.OptionId, 1)), new(new(cheap.OptionId, 1))],
            NuyenToReserve = funding - cheap.PackageCost, MaximumAdditionalSpend = cheap.PackageCost };
        var files = Snapshot(context);
        var reserve = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ReserveRequired, reserve.Targets[0].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, reserve.Targets[1].Outcome);
        Assert.AreEqual(cheap.PackageCost, reserve.AdditionalSpend);
        var spend = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request with { NuyenToReserve = 0 }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.SpendLimit, spend.Targets[0].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, spend.Targets[1].Outcome);
        var zero = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
            request with { NuyenToReserve = 0, MaximumAdditionalSpend = 0 }).Value!;
        Assert.AreEqual(0, zero.Gear.Count);
        Assert.AreEqual(0m, zero.AdditionalSpend);
        var capped = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
            request with { MaximumEvaluations = 1 }).Value!;
        Assert.AreEqual(1, capped.Evaluations);
        Assert.IsTrue(capped.Targets.All(row => row.Outcome == CharacterCreationPurchaseCompletionTargetOutcomes.WorkLimit));
        Assert.IsFalse(capped.HasChanges);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid,
            CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request with { MaximumEvaluations = 65 }).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid,
            CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
                request with { Targets = [new(new(cheap.OptionId, 1), Home(inputs.Authority))] }).Outcome);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict,
            CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
                request with { LifestylesSnapshotDigest = "stale" }).Outcome);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try { CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request, cancellation.Token); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Purchase_plan_preserves_exact_saved_gear_and_lifestyle_configuration_and_history()
    {
        using var context = ReadyContext.Create(true, buildMethod: CharacterCreationBuildMethods.Priority);
        var gearService = new CharacterCreationGearService(context.Store, context.Resolver);
        var gearState = gearService.Load(new(context.WorkspaceId)).Value!;
        var item = gearState.Authority.Options.First(row => row.IsSelectable && row.PackageCost > 0
            && row.PackageCost < 100 && row.PackageQuantity == 1);
        CharacterCreationGearSelection[] basket = [new(item.OptionId, 1)];
        var gearPreview = gearService.Preview(new(gearState.Binding, basket)).Value!;
        Assert.IsTrue(gearPreview.CanConfirm, string.Join(",", gearPreview.Blockers));
        Assert.IsTrue(gearService.Confirm(new(gearState.Binding, basket, gearPreview.PreviewDigest,
            "purchase-planner-original-gear", true)).Success);
        var lifestyleService = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = lifestyleService.Load(new(context.WorkspaceId)).Value!;
        var home = Home(state.Authority);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create, home.LifestyleId, home);
        var preview = lifestyleService.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsTrue(lifestyleService.Confirm(new(state.Binding, mutation, preview.PreviewDigest,
            "purchase-planner-original-home", true)).Success);
        using var cold = context.Restart();
        var (inputs, magic) = Load(cold);
        var storedHome = inputs.Lifestyles.Single();
        var low = inputs.Authority.LifestyleOptions.Single(row => row.Name == "Low" && row.IsSelectable);
        Assert.IsTrue(low.BuiltInQualities.Any(row => row.Extra == "Public Grid"));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            CharacterCreationLifestylesRules.AddBuiltInQualities(home, low), storedHome));
        Assert.IsTrue(storedHome.Qualities.Any(row => row.IsBuiltIn && row.IsFree
            && row.UseLifestylePoints && row.Extra == "Public Grid"));
        var request = Request(inputs, magic) with { StartingLifestyleId = storedHome.LifestyleId, MaximumAdditionalSpend = 10_000m,
            Targets = [new(new(item.OptionId, 1)), new(new(item.OptionId, 2)),
                new(Lifestyle: storedHome), new(Lifestyle: storedHome with { Increments = 2 })] };
        var files = Snapshot(cold);
        var result = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request);
        var plan = result.Value!;
        Assert.IsNotNull(plan, string.Join(",", result.Blockers));
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent, plan.Targets[0].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked, plan.Targets[1].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent, plan.Targets[2].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked, plan.Targets[3].Outcome);
        Assert.AreEqual(1, plan.Gear.Single().Quantity);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(storedHome, plan.Lifestyles.Single()));
        Assert.AreEqual(0m, plan.AdditionalSpend);
        Assert.IsFalse(plan.HasChanges);
        Assert.AreEqual(item.PackageCost + 2000m, plan.Preview.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(1, plan.Evaluations);
        AssertUnchanged(cold, files);
    }

    [TestMethod]
    public void Purchase_plan_never_refunds_retained_karma_or_relabels_unknown_price_as_affordable()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var service = new CharacterCreationResourcesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        string optionId = state.Options.Single(option => option.KarmaInvestment == 3 && option.IsEnabled).OptionId;
        var preview = service.Preview(new(state.Binding, optionId)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsTrue(service.Confirm(new(state.Binding, optionId, preview.PreviewDigest,
            "purchase-planner-original-funding", true)).Success);
        var (inputs, magic) = Load(context);
        var files = Snapshot(context);
        var request = Request(inputs, magic);
        Assert.AreEqual(3, request.TotalKarmaInvestment);
        var denied = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, request with { TotalKarmaInvestment = 2 });
        Assert.IsNull(denied.Value);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationPurchaseCompletionBlockers.InvestmentDecrease);
        var unknown = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic,
            request with { Targets = [new(new("not-an-owned-source-option", 1))], MaximumAdditionalSpend = 10_000m });
        Assert.IsNotNull(unknown.Value);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unavailable, unknown.Value!.Targets.Single().Outcome);
        Assert.IsFalse(unknown.Value.IsPurchasePlanComplete);
        Assert.AreEqual(3, unknown.Value.Preview.Gear.Resources.SelectedOption.KarmaInvestment);
        Assert.AreEqual(0m, unknown.Value.AdditionalSpend);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Purchase_plan_classifies_expensive_gear_with_pending_starting_home_as_unaffordable_then_adds_home()
    {
        using var context = ReadyContext.Create(true, buildMethod: CharacterCreationBuildMethods.Priority);
        var service = new CharacterCreationLifestylesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var savedHome = Home(state.Authority);
        var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
            savedHome.LifestyleId, savedHome);
        var preview = service.Preview(new(state.Binding, mutation)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsTrue(service.Confirm(new(state.Binding, mutation, preview.PreviewDigest,
            "purchase-classification-saved-low", true)).Success);
        var (inputs, magic) = Load(context);
        var newHome = savedHome with { LifestyleId = Guid.Parse("1f3db573-0dc0-491f-84dc-080b2bba478e"), Name = "Next home" };
        var baselineRequest = Request(inputs, magic) with { StartingLifestyleId = savedHome.LifestyleId };
        var baseline = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, baselineRequest).Value!;
        Assert.IsNotNull(baseline);
        decimal available = baseline.Preview.Gear.Resources.Budget.RemainingNuyen;
        Assert.IsTrue(available >= 2000m);
        var costly = inputs.Gear.State.Authority.Options.First(option => option.IsSelectable
            && option.PackageQuantity == 1 && option.PackageCost > 0m
            && decimal.Floor(available / option.PackageCost) + 1 <= inputs.Gear.State.Authority.MaximumQuantityPerLine);
        var files = Snapshot(context);
        var result = CharacterCreationPurchaseCompletionPlanner.Plan(inputs, magic, baselineRequest with
        {
            StartingLifestyleId = newHome.LifestyleId, MaximumAdditionalSpend = 2000m,
            Targets = [new(new(costly.OptionId, (int)decimal.Floor(available / costly.PackageCost) + 1)),
                new(Lifestyle: newHome)]
        });
        var plan = result.Value!;
        Assert.IsNotNull(plan, string.Join(",", result.Blockers));
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unaffordable, plan.Targets[0].Outcome);
        CollectionAssert.Contains(plan.Targets[0].Blockers.ToArray(),
            CharacterCreationLifestylesCompletionSchemas.StartingLifestyleRequired);
        Assert.IsTrue(plan.Targets[0].Blockers.Any(blocker => blocker is CharacterCreationGearBlockers.InsufficientFunds
            or CharacterCreationLifestylesBlockers.InsufficientFunds or CharacterCreationResourcesBlockers.InsufficientNuyen));
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, plan.Targets[1].Outcome);
        Assert.AreEqual(newHome.LifestyleId, plan.StartingLifestyleId);
        Assert.AreEqual(2, plan.Lifestyles.Count);
        Assert.AreEqual(0, plan.Gear.Count);
        Assert.AreEqual(2000m, plan.AdditionalSpend);
        Assert.IsFalse(plan.IsPurchasePlanComplete);
        AssertUnchanged(context, files);
    }

    private static (CharacterCreationLifestylesCompletionInputs Lifestyles, CharacterCreationMagicResonanceCompletionInputs Magic)
        Load(ReadyContext context)
    {
        var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver, context.Queries)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, lifestyles.Outcome, string.Join(",", lifestyles.Blockers));
        var magic = new CharacterCreationMagicResonanceService(context.Store, context.Resolver)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, magic.Outcome, string.Join(",", magic.Blockers));
        return (lifestyles.Value!, magic.Value!);
    }

    private static CharacterCreationPurchaseCompletionRequest Request(CharacterCreationLifestylesCompletionInputs inputs,
        CharacterCreationMagicResonanceCompletionInputs magic)
        => new(inputs.SnapshotDigest, magic.SnapshotDigest,
            inputs.Qualities.Attributes.PendingDraft?.Allocations.ToArray() ?? [new("MAG", 1, 0)],
            inputs.Qualities.Attributes.PendingDraft is not null ? magic.Selections
                : MagicResonanceCompletionTests.CompleteSelections(magic, magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1),
            inputs.Qualities.SelectedOptionIds, inputs.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0, []);

    private static CharacterCreationLifestyleConfiguration Home(CharacterCreationLifestylesAuthority authority)
    {
        var low = authority.LifestyleOptions.Single(row => row.Name == "Low" && row.IsSelectable);
        return new(Guid.Parse("1b2516ca-404b-429a-9f67-bd1ecf6fdcb5"), low.OptionId, "Planner apartment",
            CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
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
