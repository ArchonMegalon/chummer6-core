using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationPurchaseLoadoutCompletionPlannerTests
{
    [TestMethod]
    public void Typed_purchases_share_one_nuyen_pool_with_saved_gear_lifestyles_reserve_and_spend_cap()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var baseline = Plan(fixture, fixture.Request).Value!;
        Assert.IsNotNull(baseline);
        var extraGear = fixture.Lifestyles.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageQuantity == 1 && row.PackageCost is > 0m and < 100m
            && baseline.Gear.All(saved => saved.OptionId != row.OptionId));
        // A new home gets a fresh configuration, not a clone of a saved home's
        // already materialized quality-instance identities.
        var extraHome = CharacterCreationCompletionReviewServiceTests.LowLifestyle(fixture.Lifestyles.Authority) with
        {
            LifestyleId = Guid.Parse("22140254-9878-4900-9a5d-0232ca4f8b19"), Name = "Additional planner home"
        };
        Assert.IsTrue(CharacterCreationLifestylesRules.TryProject(extraHome, fixture.Lifestyles.Authority,
            out var extraHomeProjection, out var extraHomeBlockers), string.Join(",", extraHomeBlockers));
        Assert.AreEqual(2000m, extraHomeProjection.Economics.TotalCost);
        decimal addedCost = extraGear.PackageCost + fixture.Weapon.UnitCost + fixture.Armor.UnitCost + 2000m;
        decimal originalRemaining = baseline.Preview.Gear.Resources.Budget.RemainingNuyen;
        Assert.IsTrue(originalRemaining > addedCost);
        CharacterCreationPurchaseCompletionTarget[] targets =
        [
            new(new(extraGear.OptionId, 1)), new() { Weapon = new(fixture.Weapon.OptionId) },
            new() { Armor = new(fixture.Armor.OptionId) }, new(Lifestyle: extraHome)
        ];
        var request = fixture.Request with
        {
            Targets = targets, MaximumAdditionalSpend = addedCost, NuyenToReserve = originalRemaining - addedCost
        };
        var files = Snapshot(context);
        string originalInputs = JsonSerializer.Serialize(fixture);
        var result = Plan(fixture, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome,
            JsonSerializer.Serialize(new { result.Blockers, result.Value?.Targets,
                result.Value?.AdditionalSpend, ExpectedAdditionalSpend = addedCost }));
        var plan = result.Value!;
        Assert.IsTrue(plan.IsPurchasePlanComplete);
        Assert.IsTrue(plan.HasChanges);
        Assert.IsTrue(plan.Targets.All(row => row.Outcome == CharacterCreationPurchaseCompletionTargetOutcomes.Added));
        Assert.AreEqual(5, plan.Evaluations);
        Assert.AreEqual(addedCost, plan.AdditionalSpend);
        Assert.AreEqual(baseline.Preview.Gear.Resources.Budget.KnownPurchaseCost + addedCost,
            plan.Preview.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(request.NuyenToReserve, plan.Preview.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(baseline.Preview.Gear.Resources.SelectedOption.KarmaInvestment,
            plan.Preview.Gear.Resources.SelectedOption.KarmaInvestment, "Purchases must not invent more funding.");
        Assert.AreEqual(fixture.Weapon.OptionId, plan.Loadout!.Weapons.Single().OptionId);
        Assert.AreEqual(fixture.Armor.OptionId, plan.Loadout.Armor.Single().OptionId);
        Assert.AreEqual(baseline.Gear.Count + 1, plan.Gear.Count);
        Assert.IsTrue(baseline.Gear.All(saved => plan.Gear.Contains(saved)));
        Assert.IsFalse(plan.Gear.Any(row => row.OptionId == fixture.Weapon.OptionId || row.OptionId == fixture.Armor.OptionId),
            "Weapons and armor must not be flattened into generic Gear.");
        Assert.AreEqual(2, plan.Lifestyles.Count);

        var reserve = Plan(fixture, request with { NuyenToReserve = request.NuyenToReserve + 1m }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ReserveRequired, reserve.Targets[^1].Outcome);
        Assert.AreEqual(addedCost - 2000m, reserve.AdditionalSpend);
        Assert.AreEqual(1, reserve.Lifestyles.Count);
        var capped = Plan(fixture, request with { NuyenToReserve = 0m, MaximumAdditionalSpend = addedCost - 1m }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.SpendLimit, capped.Targets[^1].Outcome);
        Assert.AreEqual(addedCost - 2000m, capped.AdditionalSpend);
        Assert.AreEqual(1, capped.Lifestyles.Count);

        Assert.AreEqual(originalInputs, JsonSerializer.Serialize(fixture));
        Assert.AreEqual(plan.PlanDigest, Plan(fixture, request).Value!.PlanDigest);
        targets[1] = new() { Weapon = new("not-a-source-option") };
        Assert.AreEqual(fixture.Weapon.OptionId, plan.Request.Targets[1].Weapon!.OptionId,
            "The returned plan must not retain the caller's mutable target array.");
        Assert.AreEqual(plan.PlanDigest, CharacterCreationPurchaseCompletionRules.ComputePlanDigest(plan));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Typed_targets_require_the_correct_source_kind_and_supported_semantics_and_classify_shared_overspend()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var baseline = Plan(fixture, fixture.Request).Value!;
        Assert.IsNotNull(baseline);
        decimal remaining = baseline.Preview.Gear.Resources.Budget.RemainingNuyen;
        var expensive = fixture.Loadout.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.UnitCost > 0m).OrderByDescending(row => row.UnitCost).First();
        int weaponQuantity = fixture.Loadout.Authority.MaximumQuantityPerLine;
        decimal weaponCost = expensive.UnitCost * weaponQuantity;
        var filler = fixture.Lifestyles.Gear.State.Authority.Options.First(row => row.IsSelectable
            && row.PackageQuantity == 1 && row.PackageCost > 0m && row.PackageCost <= remaining
            && baseline.Gear.All(saved => saved.OptionId != row.OptionId)
            && decimal.Floor(remaining / row.PackageCost) <= fixture.Lifestyles.Gear.State.Authority.MaximumQuantityPerLine
            && remaining % row.PackageCost < weaponCost);
        int fillerQuantity = (int)decimal.Floor(remaining / filler.PackageCost);
        var unsupported = fixture.Loadout.Authority.Options.First(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && !row.IsSelectable && row.Blockers.Contains(CharacterCreationLoadoutBlockers.UnsupportedSemantics));
        var files = Snapshot(context);
        var result = Plan(fixture, fixture.Request with
        {
            MaximumAdditionalSpend = remaining,
            Targets =
            [
                new() { Weapon = new(fixture.Armor.OptionId) },
                new() { Weapon = new(unsupported.OptionId) },
                new() { Armor = new("armor:not-an-owned-source-option") },
                new() { Armor = new(fixture.Armor.OptionId, 2) },
                new(new(filler.OptionId, fillerQuantity)),
                new() { Weapon = new(expensive.OptionId, weaponQuantity) }
            ]
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.IsNotNull(plan, string.Join(",", result.Blockers));
        Assert.IsTrue(plan.Targets.Take(4).All(row => row.Outcome == CharacterCreationPurchaseCompletionTargetOutcomes.Unavailable));
        CollectionAssert.Contains(plan.Targets[1].Blockers.ToArray(), CharacterCreationLoadoutBlockers.UnsupportedSemantics);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Added, plan.Targets[4].Outcome);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unaffordable, plan.Targets[5].Outcome);
        Assert.IsTrue(plan.Targets[5].Blockers.Any(row => row is CharacterCreationGearBlockers.InsufficientFunds
            or CharacterCreationLifestylesBlockers.InsufficientFunds or CharacterCreationResourcesBlockers.InsufficientNuyen));
        Assert.AreEqual(filler.PackageCost * fillerQuantity, plan.AdditionalSpend);
        Assert.AreEqual(remaining - plan.AdditionalSpend, plan.Preview.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(0, plan.Loadout!.Weapons.Count);
        Assert.AreEqual(0, plan.Loadout.Armor.Count);
        Assert.IsTrue(baseline.Gear.All(saved => plan.Gear.Contains(saved)));
        Assert.IsFalse(plan.IsPurchasePlanComplete);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Duplicate_typed_targets_are_idempotent_and_saved_manual_purchases_are_never_replaced()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var baseline = Plan(fixture, fixture.Request).Value!;
        Assert.IsNotNull(baseline);
        var savedGear = baseline.Gear.Single();
        var files = Snapshot(context);
        var request = fixture.Request with
        {
            MaximumAdditionalSpend = fixture.Weapon.UnitCost + fixture.Armor.UnitCost,
            Targets =
            [
                new(savedGear), new(savedGear with { Quantity = savedGear.Quantity + 1 }),
                new() { Weapon = new(fixture.Weapon.OptionId) },
                new() { Weapon = new(fixture.Weapon.OptionId) },
                new() { Weapon = new(fixture.Weapon.OptionId, 2) },
                new() { Armor = new(fixture.Armor.OptionId) },
                new() { Armor = new(fixture.Armor.OptionId) }
            ]
        };
        var result = Plan(fixture, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.IsNotNull(plan, string.Join(",", result.Blockers));
        CollectionAssert.AreEqual(new[]
        {
            CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent,
            CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked,
            CharacterCreationPurchaseCompletionTargetOutcomes.Added,
            CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent,
            CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked,
            CharacterCreationPurchaseCompletionTargetOutcomes.Added,
            CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent
        }, plan.Targets.Select(row => row.Outcome).ToArray());
        Assert.AreEqual(3, plan.Evaluations, "Duplicates and locked selections must not be repriced or repurchased.");
        Assert.AreEqual(savedGear, plan.Gear.Single());
        Assert.AreEqual(1, plan.Loadout!.Weapons.Single().Quantity);
        Assert.AreEqual(1, plan.Loadout.Armor.Single().Quantity);
        Assert.AreEqual(fixture.Weapon.UnitCost + fixture.Armor.UnitCost, plan.AdditionalSpend);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(baseline.Lifestyles, plan.Lifestyles));

        foreach (var mixed in new CharacterCreationPurchaseCompletionTarget[]
        {
            new() { Weapon = new(fixture.Weapon.OptionId), Armor = new(fixture.Armor.OptionId) },
            new(savedGear) { Weapon = new(fixture.Weapon.OptionId) },
            new(Lifestyle: baseline.Lifestyles.Single()) { Armor = new(fixture.Armor.OptionId) }
        })
        {
            var rejected = Plan(fixture, request with { Targets = [mixed] });
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, rejected.Outcome);
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationPurchaseCompletionBlockers.RequestInvalid);
        }
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Typed_planning_requires_exact_loadout_authority_and_respects_work_and_cancellation_boundaries()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var files = Snapshot(context);
        var request = fixture.Request with
        {
            MaximumAdditionalSpend = fixture.Weapon.UnitCost,
            Targets = [new() { Weapon = new(fixture.Weapon.OptionId) }]
        };
        var stale = Plan(fixture, request with { LoadoutSnapshotDigest = "stale" });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationPurchaseCompletionBlockers.SnapshotConflict);
        var missing = Plan(fixture, request with { LoadoutSnapshotDigest = null });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, missing.Outcome);
        Assert.IsNull(missing.Value);
        var legacy = CharacterCreationPurchaseCompletionPlanner.Plan(fixture.Lifestyles, fixture.Magic,
            request with { LoadoutSnapshotDigest = null }, CancellationToken.None);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, legacy.Outcome);
        Assert.IsNull(legacy.Value);
        CollectionAssert.Contains(legacy.Blockers.ToArray(), CharacterCreationLoadoutCompletionSchemas.AuthorityUnavailable);

        var capped = Plan(fixture, request with { MaximumEvaluations = 1 }).Value!;
        Assert.IsNotNull(capped);
        Assert.AreEqual(1, capped.Evaluations);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.WorkLimit, capped.Targets.Single().Outcome);
        Assert.AreEqual(0, capped.Loadout!.Weapons.Count);
        Assert.AreEqual(0m, capped.AdditionalSpend);
        Assert.IsFalse(capped.HasChanges);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        bool canceled = false;
        try
        {
            CharacterCreationPurchaseCompletionPlanner.Plan(fixture.Lifestyles, fixture.Magic, fixture.Loadout,
                request, cancellation.Token);
        }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        AssertUnchanged(context, files);
    }

    private sealed record Fixture(CharacterCreationLifestylesCompletionInputs Lifestyles,
        CharacterCreationMagicResonanceCompletionInputs Magic, CharacterCreationLoadoutCompletionInputs Loadout,
        CharacterCreationPurchaseCompletionRequest Request, CharacterCreationLoadoutCatalogOption Weapon,
        CharacterCreationLoadoutCatalogOption Armor);

    private static Fixture Load(ReadyContext context)
    {
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var observed = CharacterCreationLoadoutCompletionService.LoadFromObservation(
            context.Store.Get(context.WorkspaceId).Value!, context.Resolver, true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, observed.Outcome, string.Join(",", observed.Blockers));
        Assert.IsNotNull(observed.Value);
        var loadout = observed.Value;
        var weapon = loadout.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Weapon
            && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var armor = loadout.Authority.Options.Where(row => row.Kind == CharacterCreationLoadoutKinds.Armor
            && row.IsSelectable && row.UnitCost > 0m).OrderBy(row => row.UnitCost).First();
        var request = new CharacterCreationPurchaseCompletionRequest(inputs.Lifestyles.SnapshotDigest,
            inputs.Magic.SnapshotDigest, choices.AttributeAllocations, choices.MagicSelections, choices.QualityOptionIds,
            choices.TotalKarmaInvestment, [], choices.StartingLifestyleId) { LoadoutSnapshotDigest = loadout.SnapshotDigest };
        return new(inputs.Lifestyles, inputs.Magic, loadout, request, weapon, armor);
    }

    private static CharacterCreationFoundationResult<CharacterCreationPurchaseCompletionPlan> Plan(
        Fixture fixture, CharacterCreationPurchaseCompletionRequest request)
        => CharacterCreationPurchaseCompletionPlanner.Plan(fixture.Lifestyles, fixture.Magic, fixture.Loadout,
            request, CancellationToken.None);

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
