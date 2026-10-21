using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
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

    [TestMethod]
    public void Prepared_loadout_catalog_preserves_fresh_basket_results_and_negative_admission()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var files = Snapshot(context);
        var session = CharacterCreationLoadoutRules.CreateProjectionSession(fixture.Loadout.Authority);
        Assert.IsNotNull(session);
        var unsupported = fixture.Loadout.Authority.Options.First(row => !row.IsSelectable
            && row.Kind == CharacterCreationLoadoutKinds.Weapon);
        CharacterCreationLoadoutSelections?[] baskets =
        [
            null, CharacterCreationLoadoutSelections.Empty,
            new([new(fixture.Weapon.OptionId)], [new(fixture.Armor.OptionId)]),
            new([new(fixture.Armor.OptionId)], []),
            new([new(unsupported.OptionId)], []),
            new([new("weapon:missing")], []),
            new([new(fixture.Weapon.OptionId, 0)], []),
            new([new(fixture.Weapon.OptionId, fixture.Loadout.Authority.MaximumQuantityPerLine + 1)], []),
            new([new(fixture.Weapon.OptionId), new(fixture.Weapon.OptionId)], []),
            new([], [new(fixture.Armor.OptionId, 2)])
        ];
        foreach (var basket in baskets)
        {
            bool expected = CharacterCreationLoadoutRules.TryProjectBasket(basket, fixture.Loadout.Authority,
                out var expectedLines, out var expectedCost, out var expectedBlockers);
            bool actual = session.TryProjectBasket(basket, out var lines, out var cost, out var blockers);
            Assert.AreEqual(expected, actual);
            Assert.AreEqual(expectedCost, cost);
            CollectionAssert.AreEqual(expectedBlockers, blockers);
            CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(expectedLines),
                JsonSerializer.SerializeToUtf8Bytes(lines));
        }
        Assert.IsNull(CharacterCreationLoadoutRules.CreateProjectionSession(null));
        Assert.IsNull(CharacterCreationLoadoutRules.CreateProjectionSession(
            fixture.Loadout.Authority with { AuthorityDigest = "stale" }));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Prepared_loadout_catalog_detaches_caller_inputs_and_returned_source_references()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var files = Snapshot(context);
        var options = fixture.Loadout.Authority.Options.ToArray();
        var authority = fixture.Loadout.Authority with { Options = options };
        var session = CharacterCreationLoadoutRules.CreateProjectionSession(authority);
        Assert.IsNotNull(session);
        var basket = new CharacterCreationLoadoutSelections([new(fixture.Weapon.OptionId)], []);
        Assert.IsTrue(session.TryProjectBasket(basket, out var first, out decimal price, out _));
        byte[] expected = JsonSerializer.SerializeToUtf8Bytes(first);
        int index = Array.FindIndex(options, row => row.OptionId == fixture.Weapon.OptionId);
        options[index] = options[index] with { UnitCost = options[index].UnitCost + 1m };
        Assert.IsNull(CharacterCreationLoadoutRules.CreateProjectionSession(authority),
            "A later operation must re-admit its own source bytes, never reuse this session by identity.");
        ((IList<string>)first[0].SourceAnchorIds)[0] = "caller-mutated-reference";
        Assert.IsTrue(session.TryProjectBasket(basket, out var second, out decimal secondPrice, out _));
        Assert.AreEqual(price, secondPrice);
        CollectionAssert.AreEqual(expected, JsonSerializer.SerializeToUtf8Bytes(second));
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Ammunition_purchases_price_owned_cartridges_once_and_preserve_existing_gear()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var baseline = Plan(fixture, fixture.Request).Value!;
        var ammo = RegularCartridges(fixture.Lifestyles.Gear.State.Authority);
        Assert.AreEqual(20m, ammo.PackageCost);
        Assert.AreEqual(10, ammo.PackageQuantity);
        Assert.AreEqual(2, ammo.Availability);
        Assert.AreEqual(CharacterCreationGearLegality.Restricted, ammo.Legality);
        var target = new CharacterCreationAmmunitionPurchase(ammo.OptionId, 37, ammo.SourceNodeDigest);
        var request = fixture.Request with
        {
            Targets = [new() { Ammunition = target }], MaximumAdditionalSpend = 74m,
            NuyenToReserve = baseline.Preview.Gear.Resources.Budget.RemainingNuyen - 74m
        };
        var files = Snapshot(context);
        var result = Plan(fixture, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var plan = result.Value!;
        Assert.AreEqual(2, plan.Evaluations);
        Assert.AreEqual(74m, plan.AdditionalSpend);
        Assert.AreEqual(request.NuyenToReserve, plan.Preview.Gear.Resources.Budget.RemainingNuyen);
        Assert.AreEqual(baseline.Preview.Gear.Resources.Budget.KnownPurchaseCost + 74m,
            plan.Preview.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.IsTrue(baseline.Gear.All(plan.Gear.Contains));
        Assert.AreEqual(37, plan.Gear.Single(row => row.OptionId == ammo.OptionId).Quantity);
        var evidence = plan.Targets.Single().Ammunition!;
        Assert.IsNotNull(evidence);
        Assert.AreEqual(ammo.SourceId, evidence.SourceId);
        Assert.AreEqual(ammo.SourceNodeDigest, evidence.SourceNodeDigest);
        Assert.AreEqual(37, evidence.OwnedCartridges);
        Assert.AreEqual(74m, evidence.TotalCost);
        CollectionAssert.AreEquivalent(new[] { CharacterCreationAmmunitionCompletionGaps.WeaponCompatibility,
            CharacterCreationAmmunitionCompletionGaps.LoadedState, CharacterCreationAmmunitionCompletionGaps.CombatEffects },
            evidence.RemainingGaps.ToArray());
        Assert.AreEqual(plan.PlanDigest, CharacterCreationPurchaseCompletionRules.ComputePlanDigest(plan));

        var reserve = Plan(fixture, request with { NuyenToReserve = request.NuyenToReserve + 1m }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ReserveRequired, reserve.Targets.Single().Outcome);
        Assert.IsNull(reserve.Targets.Single().Ammunition);
        var cap = Plan(fixture, request with { MaximumAdditionalSpend = 73m }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.SpendLimit, cap.Targets.Single().Outcome);
        Assert.AreEqual(0m, cap.AdditionalSpend);
        var work = Plan(fixture, request with { MaximumEvaluations = 1 }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.WorkLimit, work.Targets.Single().Outcome);
        var funds = Plan(fixture, request with { Targets = [new() { Ammunition = target with { Cartridges = 1_000_000 } }],
            MaximumAdditionalSpend = decimal.MaxValue, NuyenToReserve = 0m }).Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.Unaffordable, funds.Targets.Single().Outcome);
        Assert.AreEqual(0m, funds.AdditionalSpend);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Ammunition_targets_reject_stale_mismatched_and_unsupported_sources_without_claiming_evaluation()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var fixture = Load(context);
        var ammo = RegularCartridges(fixture.Lifestyles.Gear.State.Authority);
        var saved = fixture.Lifestyles.Gear.State.PendingDraft!.Lines.Single();
        var apds = fixture.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("ef9c8aae-26df-4fe6-88b3-79fbb5eb77c5"));
        var cannon = fixture.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("0bf4113c-dedb-411d-8981-7b6af169f056"));
        var files = Snapshot(context);
        var result = Plan(fixture, fixture.Request with
        {
            MaximumAdditionalSpend = 10_000m,
            Targets =
            [
                new() { Ammunition = new(ammo.OptionId, 37, "sha256:" + new string('0', 64)) },
                new() { Ammunition = new(saved.OptionId, saved.Quantity, saved.SourceNodeDigest) },
                new() { Ammunition = new("gear:missing", 37, ammo.SourceNodeDigest) },
                new() { Ammunition = new(apds.OptionId, 10, apds.SourceNodeDigest) },
                new() { Ammunition = new(cannon.OptionId, 10, cannon.SourceNodeDigest) }
            ]
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var plan = result.Value!;
        Assert.IsTrue(plan.Targets.All(row => row.Outcome == CharacterCreationPurchaseCompletionTargetOutcomes.Unavailable));
        Assert.IsTrue(plan.Targets.All(row => row.Ammunition is null));
        CollectionAssert.Contains(plan.Targets[0].Blockers.ToArray(), CharacterCreationAmmunitionCompletionBlockers.SourceChanged);
        CollectionAssert.Contains(plan.Targets[1].Blockers.ToArray(), CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics);
        CollectionAssert.Contains(plan.Targets[2].Blockers.ToArray(), CharacterCreationGearBlockers.InvalidOption);
        CollectionAssert.Contains(plan.Targets[3].Blockers.ToArray(), CharacterCreationGearBlockers.UnsupportedSemantics);
        CollectionAssert.Contains(plan.Targets[4].Blockers.ToArray(), CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics);
        Assert.AreEqual(0m, plan.AdditionalSpend);
        Assert.AreEqual(saved.Quantity, plan.Gear.Single().Quantity);

        var mixed = Plan(fixture, fixture.Request with { Targets = [new(new(ammo.OptionId, 37))
            { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) }] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, mixed.Outcome);
        foreach (int quantity in new[] { 0, -1, 1_000_001 })
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid, Plan(fixture, fixture.Request with
                { Targets = [new() { Ammunition = new(ammo.OptionId, quantity, ammo.SourceNodeDigest) }] }).Outcome);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Ammunition_source_metadata_and_actual_profile_restrictions_are_not_bypassed()
    {
        // The ordinary fixture establishes all its prerequisites first; this
        // profile only restricts sources/availability, never removes a requirement.
        using var context = ReadyContext.Create(true, amendSettings: profile =>
        {
            profile.Element("availability")!.Value = "1";
            foreach (var book in profile.Element("books")!.Elements("book").Where(row => row.Value == "HT").ToArray()) book.Remove();
        });
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationGearAuthority(out var authority));
        var ammo = authority.Options.Single(row => row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae"));
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TrySelect(authority,
            new(ammo.OptionId, 37, ammo.SourceNodeDigest), out _, out var illegal));
        CollectionAssert.Contains(illegal, CharacterCreationGearBlockers.AvailabilityExceeded);
        var disabled = authority.Options.Single(row => row.SourceId == Guid.Parse("b286a62d-c7cb-472a-bea8-0f41a300bb9c"));
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TrySelect(authority,
            new(disabled.OptionId, 10, disabled.SourceNodeDigest), out _, out var sourceDisabled));
        CollectionAssert.Contains(sourceDisabled, CharacterCreationGearBlockers.SourceDisabled);

        using var ordinary = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var full = Load(ordinary).Lifestyles.Gear.State.Authority;
        var original = RegularCartridges(full);
        var forged = original with { PackageCost = original.PackageCost + 1m };
        forged = forged with { OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(forged) };
        var resealed = full with { Options = full.Options.Select(row => row.OptionId == forged.OptionId ? forged : row).ToArray() };
        resealed = resealed with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(resealed) };
        Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TrySelect(resealed,
            new(forged.OptionId, 37, forged.SourceNodeDigest), out _, out var mismatch));
        CollectionAssert.Contains(mismatch, CharacterCreationAmmunitionCompletionBlockers.SourceMismatch);

        foreach (string xml in new[]
        {
            original.SourceNodeXml.Replace(">gun<", ">unknown-family<", StringComparison.Ordinal),
            original.SourceNodeXml.Replace("</gear>", "<ammoforweapontype>gun</ammoforweapontype></gear>", StringComparison.Ordinal),
            "<!DOCTYPE gear [<!ENTITY family 'gun'>]>" + original.SourceNodeXml.Replace(">gun<", ">&family;<", StringComparison.Ordinal)
        })
        {
            var unsupported = original with { SourceNodeXml = xml, SourceNodeDigest = CharacterCreationGearRules.ComputeSourceNodeDigest(xml) };
            unsupported = unsupported with { OptionDigest = CharacterCreationGearRules.ComputeOptionDigest(unsupported) };
            var changed = full with { Options = full.Options.Select(row => row.OptionId == unsupported.OptionId ? unsupported : row).ToArray() };
            changed = changed with { AuthorityDigest = CharacterCreationGearRules.ComputeAuthorityDigest(changed) };
            Assert.IsFalse(CharacterCreationAmmunitionCompletionConsumer.TrySelect(changed,
                new(unsupported.OptionId, 37, unsupported.SourceNodeDigest), out _, out var unknown));
            CollectionAssert.Contains(unknown, CharacterCreationAmmunitionCompletionBlockers.UnsupportedSemantics);
        }
    }

    [TestMethod]
    public void Ammunition_targets_cannot_increase_or_rebuy_preselected_cartridges()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var original = context.Store.Get(context.WorkspaceId).Value!;
        var originalInputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var originalChoices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(originalInputs);
        var service = new CharacterCreationGearService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        var ammo = RegularCartridges(state.Authority);
        CharacterCreationGearSelection[] basket = [.. state.PendingDraft!.Lines.Select(row =>
            new CharacterCreationGearSelection(row.OptionId, row.Quantity)), new(ammo.OptionId, 37)];
        var preview = service.Preview(new(state.Binding, basket)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsTrue(service.Confirm(new(state.Binding, basket, preview.PreviewDigest, "preselected-cartridges", true)).Success);
        using var cold = context.Restart();
        var files = Snapshot(cold);
        var saved = cold.Store.Get(cold.WorkspaceId).Value!;
        var auxiliary = saved.Document.AuxiliaryState;
        Assert.AreEqual(original.Document.Content, saved.Document.Content);
        Assert.AreEqual(original.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            original.Document.AuxiliaryState, auxiliary with
            {
                CharacterCreationGearDraft = original.Document.AuxiliaryState.CharacterCreationGearDraft,
                CharacterCreationGearReceipts = original.Document.AuxiliaryState.CharacterCreationGearReceipts
            }), "Gear confirmation must preserve every other saved choice and receipt.");
        var retained = auxiliary.CharacterCreationLifestylesDraft!;
        Assert.AreNotEqual(retained.GearDraftDigest, auxiliary.CharacterCreationGearDraft!.DraftDigest);
        var lifestyleState = new CharacterCreationLifestylesService(cold.Store, cold.Resolver, cold.Queries)
            .Load(new(cold.WorkspaceId)).Value!;
        Assert.IsTrue(lifestyleState.CanEdit, string.Join(",", lifestyleState.Blockers));
        Assert.IsTrue(CharacterCreationLifestylesDraftRules.TryLoad(saved, cold.Resolver, out var refreshed, out _, out _));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(retained.Lifestyles, refreshed!.Lifestyles));
        Assert.AreEqual(auxiliary.CharacterCreationGearDraft.DraftDigest, refreshed.GearDraftDigest);
        Assert.IsFalse(CharacterCreationLifestylesDraftRules.InputsMatch(retained, refreshed),
            "Repricing must not relabel the old receipt as a current saved binding.");
        Assert.IsTrue(CharacterCreationLifestylesDraftRules.HasConfirmedGearSuccessor(saved, retained));
        Assert.IsTrue(CharacterCreationLifestylesDraftRules.TryProject(saved, retained, out _, out _, out var lifestyleBudget));
        Assert.AreEqual(preview.BudgetAfter.RemainingNuyen - 2000m, lifestyleBudget!.Remaining);
        Assert.AreEqual(0m, lifestyleBudget.Overspend);
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(cold);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            originalChoices with { Gear = choices.Gear }, choices));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            retained, inputs.Lifestyles.Gear.Resources.LifestylesDraft));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            auxiliary.CharacterCreationLifestyleReceipts, inputs.Lifestyles.Gear.Resources.LifestyleReceipts));
        var review = CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(choices, review.Value!.Request.Selections));
        Assert.AreEqual((decimal?)preview.BudgetAfter.BasketCost, review.Value.Balances.GearCost);
        Assert.AreEqual((decimal?)2000m, review.Value.Balances.LifestyleCost);
        Assert.AreEqual(preview.BudgetAfter.BasketCost + 2000m, inputs.Lifestyles.Gear.Resources.KnownPurchaseCost);

        // Exercise the same live source admission and selected replay used by
        // Complete; the old Lifestyle receipt remains historical, never renewed.
        var source = cold.Resolver.TryCreateContext(saved.Document.Content)!;
        var home = choices.Lifestyles.Single(row => row.LifestyleId == choices.StartingLifestyleId);
        var homeOption = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
        Assert.IsTrue(source.TryResolveCreationPriorityStartingNuyen(homeOption.SourceId, out var cash));
        Assert.IsNotNull(cash);
        Assert.IsTrue(CharacterCreationCompletionTransaction.TryAdmit(cold.Store, OwnerScope.LocalSingleUser,
            saved, cold.Resolver, cold.Queries, review.Value.Request, new(cash!.AuthorityDigest, cash.Dice * 3), default,
            out var authority, out var projection, out var blockers), string.Join(",", blockers));
        Assert.IsNotNull(authority!.SelectedReplay);
        Assert.AreEqual(review.Value.Balances.RemainingNuyen, (decimal?)projection!.NuyenBeforeCarryover);
        var character = System.Xml.Linq.XElement.Parse(projection.CharacterXml);
        Assert.AreEqual("37", character.Element("gears")!.Elements("gear")
            .Single(row => row.Element("sourceid")?.Value == ammo.SourceId.ToString("D")).Element("qty")!.Value);
        Assert.AreEqual(choices.Contacts.Count, character.Element("contacts")!.Elements("contact").Count());
        Assert.AreEqual(choices.Lifestyles.Count, character.Element("lifestyles")!.Elements("lifestyle").Count());
        var fixture = Load(cold);
        var result = Plan(fixture, fixture.Request with
        {
            MaximumAdditionalSpend = 10_000m,
            Targets = [new() { Ammunition = new(ammo.OptionId, 37, ammo.SourceNodeDigest) },
                new() { Ammunition = new(ammo.OptionId, 38, ammo.SourceNodeDigest) }]
        });
        var plan = result.Value!;
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.AlreadyPresent, plan.Targets[0].Outcome);
        Assert.AreEqual(37, plan.Targets[0].Ammunition!.OwnedCartridges);
        Assert.AreEqual(CharacterCreationPurchaseCompletionTargetOutcomes.ExistingSelectionLocked, plan.Targets[1].Outcome);
        Assert.IsNull(plan.Targets[1].Ammunition);
        Assert.AreEqual(1, plan.Evaluations);
        Assert.AreEqual(0m, plan.AdditionalSpend);
        CollectionAssert.AreEquivalent(basket, plan.Gear.ToArray());

        // A stale dependent budget cannot authorize spending the saved home twice.
        // This basket fits Gear alone but not Gear plus the retained Lifestyle.
        var currentService = new CharacterCreationGearService(cold.Store, cold.Resolver);
        var currentState = currentService.Load(new(cold.WorkspaceId)).Value!;
        var supplies = currentState.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && row.IsSelectable);
        Assert.AreEqual(1, supplies.PackageQuantity);
        decimal ammoCost = currentState.PendingDraft!.Lines.Single(row => row.OptionId == ammo.OptionId).TotalCost;
        int quantity = checked((int)decimal.Floor((currentState.Budget.TotalStartingNuyen - ammoCost) / supplies.PackageCost));
        Assert.IsTrue(quantity <= currentState.Authority.MaximumQuantityPerLine);
        Assert.IsTrue(ammoCost + quantity * supplies.PackageCost <= currentState.Budget.TotalStartingNuyen);
        Assert.IsTrue(ammoCost + quantity * supplies.PackageCost + 2000m > currentState.Budget.TotalStartingNuyen);
        var excessive = basket.Select(row => row.OptionId == supplies.OptionId ? row with { Quantity = quantity } : row).ToArray();
        var blocked = currentService.Preview(new(currentState.Binding, excessive));
        Assert.IsFalse(blocked.Value!.CanConfirm);
        CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationGearBlockers.InsufficientFunds);
        Assert.IsFalse(currentService.Confirm(new(currentState.Binding, excessive, blocked.Value.PreviewDigest,
            "preselected-cartridges-over-budget", true)).Success);

        // Even structurally valid receipt chains cannot refresh a Lifestyle whose
        // historical Gear anchor is missing or has a different source identity.
        foreach (string change in new[] { "missing-anchor", "different-source" })
        {
            var receipts = new List<CharacterCreationGearReceiptLedgerEntry>();
            string previous = CharacterCreationGearRules.ReceiptLedgerRootDigest;
            foreach (var entry in auxiliary.CharacterCreationGearReceipts!)
            {
                var receipt = entry.Receipt with { PreviousReceiptDigest = previous };
                if (receipt.DraftDigest == retained.GearDraftDigest)
                    receipt = change == "missing-anchor"
                        ? receipt with { DraftDigest = CharacterCreationFinalizationDigest.Compute(change) }
                        : receipt with { SourceDigest = CharacterCreationFinalizationDigest.Compute(change) };
                receipt = receipt with { ReceiptDigest = CharacterCreationGearRules.ComputeReceiptDigest(receipt) };
                receipts.Add(entry with { Receipt = receipt });
                previous = receipt.ReceiptDigest;
            }
            var altered = auxiliary with { CharacterCreationGearReceipts = receipts.ToArray() };
            Assert.IsTrue(WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, saved.ContentRevision, altered), change);
            var observation = saved with { Document = saved.Document with
            { State = saved.Document.State with { AuxiliaryState = altered } } };
            Assert.IsFalse(CharacterCreationLifestylesDraftRules.HasConfirmedGearSuccessor(observation, retained), change);
            var readView = new WorkspaceContinuationReadView(observation);
            var denied = new CharacterCreationResourcesService(readView, cold.Resolver).LoadCompletionInputs(new(saved.Id));
            Assert.IsNull(denied.Value, change);
            CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        }

        // A resealed latest receipt cannot claim the Lifestyle commit while its
        // Gear draft still names the later base observed by the real Confirm.
        var chronologyGear = auxiliary.CharacterCreationGearDraft!;
        var chronologyReceipts = auxiliary.CharacterCreationGearReceipts!.ToArray();
        var latestReceipt = chronologyReceipts[^1].Receipt;
        Assert.AreEqual(chronologyGear.BaseContentRevision, latestReceipt.PreviousWorkspaceRevision);
        Assert.IsTrue(CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(saved.Id, saved.ContentRevision,
            chronologyGear, chronologyReceipts));
        var misdatedReceipt = latestReceipt with
        {
            PreviousWorkspaceRevision = retained.BaseContentRevision,
            WorkspaceRevision = retained.BaseContentRevision + 1,
            PreviousSavedRevision = retained.BaseContentRevision,
            SavedRevision = retained.BaseContentRevision + 1
        };
        misdatedReceipt = misdatedReceipt with
        {
            ReceiptDigest = CharacterCreationGearRules.ComputeReceiptDigest(misdatedReceipt)
        };
        Assert.AreNotEqual(chronologyGear.BaseContentRevision, misdatedReceipt.PreviousWorkspaceRevision);
        Assert.IsTrue(chronologyReceipts[^2].Receipt.WorkspaceRevision <= misdatedReceipt.PreviousWorkspaceRevision,
            "The detached chronology must still satisfy the existing monotonic receipt-chain check.");
        chronologyReceipts[^1] = chronologyReceipts[^1] with { Receipt = misdatedReceipt };
        var misdatedAuxiliary = auxiliary with { CharacterCreationGearReceipts = chronologyReceipts };
        Assert.IsFalse(CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(saved.Id, saved.ContentRevision,
            chronologyGear, chronologyReceipts));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, saved.ContentRevision, misdatedAuxiliary));
        var misdatedObservation = saved with { Document = saved.Document with
        { State = saved.Document.State with { AuxiliaryState = misdatedAuxiliary } } };
        Assert.IsFalse(CharacterCreationLifestylesDraftRules.HasConfirmedGearSuccessor(misdatedObservation, retained));
        var misdatedReadView = new WorkspaceContinuationReadView(misdatedObservation);
        var chronologyDenied = new CharacterCreationResourcesService(misdatedReadView, cold.Resolver)
            .LoadCompletionInputs(new(saved.Id));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Blocked, chronologyDenied.Outcome);
        Assert.IsNull(chronologyDenied.Value);
        CollectionAssert.Contains(chronologyDenied.Blockers.ToArray(),
            CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        AssertUnchanged(cold, files);
    }

    private static CharacterCreationGearCatalogOption RegularCartridges(CharacterCreationGearAuthority authority)
        => authority.Options.Single(row => row.SourceId == Guid.Parse("b2a0b340-c793-4322-8422-8b03d18a6fae") && row.IsSelectable);

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
        => CharacterCreationPurchaseCompletionPlannerTests.AssertMatchesFresh(fixture.Lifestyles, fixture.Magic,
            request, fixture.Loadout);

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
