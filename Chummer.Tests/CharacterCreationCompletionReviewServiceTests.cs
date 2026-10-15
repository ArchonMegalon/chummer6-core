using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionReviewServiceTests
{
    [TestMethod]
    public void Whole_review_before_saved_stages_recomputes_one_shared_karma_budget_without_writing()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputs = LoadInputs(context);
        Assert.IsNull(inputs.Skills.Attributes.PendingDraft);
        Assert.IsNull(inputs.Lifestyles.Gear.Resources.PendingDraft);
        Assert.IsNull(inputs.Lifestyles.Gear.State.PendingDraft);
        Assert.IsNull(inputs.Contacts.PendingDraft);
        var selections = InitialSelections(inputs);
        var result = Review(inputs, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var review = result.Value!;
        Assert.AreEqual(inputs.SnapshotDigest, review.InputsSnapshotDigest);
        Assert.AreEqual(25, review.Balances.CreationKarmaTotal);
        Assert.AreEqual((int?)15, review.Balances.KarmaAfterAttributesAndQualities);
        Assert.AreEqual((int?)0, review.Balances.SkillsKarmaCost);
        Assert.AreEqual((int?)5, review.Balances.MysticAdeptPowerPointKarmaCost);
        Assert.AreEqual((int?)3, review.Balances.ResourcesKarmaInvestment);
        Assert.AreEqual((int?)0, review.Balances.ContactsKarmaCost);
        Assert.AreEqual((int?)7, review.Balances.RemainingCreationKarma);
        Assert.AreEqual((int?)0, review.Balances.KarmaOverspend);
        Assert.AreEqual((decimal?)56000m, review.Balances.CreationNuyen);
        Assert.AreEqual((decimal?)100m, review.Balances.GearCost);
        Assert.AreEqual((decimal?)2000m, review.Balances.LifestyleCost);
        Assert.AreEqual((decimal?)53900m, review.Balances.RemainingNuyen);
        Assert.AreEqual(review.ReviewDigest, CharacterCreationCompletionReviewRules.ComputeReviewDigest(review));
        Assert.AreEqual(review.ReviewDigest, Review(inputs, selections).Value!.ReviewDigest);
        Assert.IsNull(typeof(CharacterCreationCompletionReview).GetProperty("CanConfirm"));
        Assert.IsNull(typeof(CharacterCreationCompletionReview).GetProperty("WritePlan"));

        // The same real Attribute purchase consumes ten Karma; PP and Guts
        // must not each see the original twenty-five-point allowance again.
        var allKarma = selections with
        {
            AttributeAllocations = [new("MAG", 1, 0), new("BOD", 0, 1)],
            TotalKarmaInvestment = 0
        };
        var exact = Review(inputs, allKarma);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, exact.Outcome, string.Join(",", exact.Blockers));
        Assert.AreEqual((int?)5, exact.Value!.Balances.KarmaAfterAttributesAndQualities);
        Assert.AreEqual((int?)0, exact.Value.Balances.RemainingCreationKarma);
        var overspent = Review(inputs, allKarma with { TotalKarmaInvestment = 3 });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, overspent.Outcome);
        Assert.AreEqual((int?)-3, overspent.Value!.Balances.RemainingCreationKarma);
        Assert.AreEqual((int?)3, overspent.Value.Balances.KarmaOverspend);
        CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationResourcesBlockers.InsufficientCreationKarma);

        var unpriced = Review(inputs, selections with
        {
            MagicSelections = selections.MagicSelections with { MysticAdeptPowerPoints = int.MaxValue }
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, unpriced.Outcome);
        Assert.IsNotNull(unpriced.Value);
        Assert.IsNull(unpriced.Value!.Balances.RemainingCreationKarma,
            "An unpriced Power Point choice must not become a zero-cost allowance.");
        CollectionAssert.Contains(unpriced.Blockers.ToArray(), CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(30)]
    public void Whole_review_matches_independent_budget_with_a_lifestyle_projection(int increments)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var selections = InitialSelections(inputs);
        selections = selections with { Lifestyles = [selections.Lifestyles.Single() with { Increments = increments }] };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        // Lifestyle pricing retains one fractional digit in this source fixture.
        // Review digests bind that representation, not just decimal equality.
        decimal remaining = 56000m - 100m - 2000.0m * increments;
        var expectedBalances = new CharacterCreationCompletionPoolBalances(25, 15, 0, 5, 3, 0, 7, 0,
            56000m, 100m, 2000.0m * increments, Math.Max(0m, remaining), Math.Max(0m, -remaining));

        var result = AssertMatchesIndependentReview(inputs, selections, expectedBalances);
        Assert.AreEqual(increments == 1 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        var review = result.Value!;
        Assert.IsNotNull(review.Lifestyles);
        Assert.IsNotNull(review.UpstreamBudget);
        Assert.AreEqual(0, review.UpstreamBudget!.Blockers.Count,
            "Purchase failures must not become upstream budget findings.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            review.UpstreamBudget, review.Lifestyles!.Gear.Resources.UpstreamBudget));
        Assert.AreNotSame(review.UpstreamBudget, review.Lifestyles.Gear.Resources.UpstreamBudget);
        if (increments > 1)
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        Assert.AreEqual(review.ReviewDigest, Review(inputs, selections).Value!.ReviewDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Whole_review_without_lifestyle_projection_keeps_independent_upstream_findings(bool unpricedPowerPoints)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var original = InitialSelections(inputs);
        var selections = original with
        {
            TotalKarmaInvestment = int.MaxValue,
            MagicSelections = unpricedPowerPoints
                ? original.MagicSelections with { MysticAdeptPowerPoints = int.MaxValue } : original.MagicSelections
        };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var expectedBalances = new CharacterCreationCompletionPoolBalances(25, 15, 0,
            unpricedPowerPoints ? null : 5, null, 0, null, null, null, null, null, null, null);

        var result = AssertMatchesIndependentReview(inputs, selections, expectedBalances,
            CharacterCreationCompletionReviewSchemas.KarmaBudgetUnavailable);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value!.Lifestyles);
        Assert.IsNotNull(result.Value.UpstreamBudget);
        if (unpricedPowerPoints)
        {
            Assert.IsNull(result.Value.UpstreamBudget!.AvailableKarmaBeforeResources);
            CollectionAssert.Contains(result.Value.UpstreamBudget.Blockers.ToArray(),
                CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);
        }
        else
        {
            Assert.AreEqual((int?)10, result.Value.UpstreamBudget!.AvailableKarmaBeforeResources);
            Assert.AreEqual(0, result.Value.UpstreamBudget.Blockers.Count);
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationResourcesBlockers.InvalidOption);
        }
        bool canceled = false;
        try { Review(inputs, selections, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        // The failed proposal cannot supply the next invocation's budget.
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, Review(inputs, original).Outcome);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Whole_review_revalidates_resealed_lifestyle_authority_before_budget_reuse()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var selections = InitialSelections(inputs);
        var original = Review(inputs, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, original.Outcome);
        var authority = inputs.Lifestyles.Authority with
        {
            SourceDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("foreign-review-lifestyle-source")
        };
        authority = authority with { AuthorityDigest = CharacterCreationLifestylesRules.ComputeAuthorityDigest(authority) };
        var lifestyles = inputs.Lifestyles with { Authority = authority };
        lifestyles = lifestyles with { SnapshotDigest = CharacterCreationLifestylesCompletionRules.ComputeInputsDigest(lifestyles) };
        var changed = inputs with { Lifestyles = lifestyles };
        changed = changed with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(changed) };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var expectedBalances = new CharacterCreationCompletionPoolBalances(25, 15, 0, 5, null, 0,
            null, null, null, null, null, null, null);

        var rejected = AssertMatchesIndependentReview(changed, selections, expectedBalances,
            CharacterCreationCompletionReviewSchemas.KarmaBudgetUnavailable);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        Assert.IsNull(rejected.Value!.Lifestyles);
        Assert.IsNotNull(rejected.Value.UpstreamBudget);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        Assert.AreEqual(original.Value!.ReviewDigest, Review(inputs, selections).Value!.ReviewDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Whole_review_combines_individually_affordable_gear_and_lifestyle_purchases_once()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var baseline = InitialSelections(inputs) with { TotalKarmaInvestment = 0 };
        var supplies = inputs.Lifestyles.Gear.State.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && item.IsSelectable);
        Assert.AreEqual(100m, supplies.PackageCost);
        Assert.AreEqual(1, supplies.PackageQuantity);
        Assert.IsTrue(inputs.Lifestyles.Gear.State.Authority.MaximumQuantityPerLine >= 300);
        var largeBasket = baseline with { Gear = [new(supplies.OptionId, 300)] };
        var longLease = baseline with { Lifestyles = [baseline.Lifestyles.Single() with { Increments = 15 }] };
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var basketOnly = Review(inputs, largeBasket);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, basketOnly.Outcome, string.Join(",", basketOnly.Blockers));
        Assert.AreEqual((decimal?)30000m, basketOnly.Value!.Balances.GearCost);
        var leaseOnly = Review(inputs, longLease);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, leaseOnly.Outcome, string.Join(",", leaseOnly.Blockers));
        Assert.AreEqual((decimal?)30000m, leaseOnly.Value!.Balances.LifestyleCost);

        var combined = Review(inputs, largeBasket with { Lifestyles = longLease.Lifestyles });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, combined.Outcome);
        var review = combined.Value!;
        Assert.AreEqual((decimal?)50000m, review.Balances.CreationNuyen);
        Assert.AreEqual((decimal?)30000m, review.Balances.GearCost);
        Assert.AreEqual((decimal?)30000m, review.Balances.LifestyleCost);
        Assert.AreEqual((decimal?)0m, review.Balances.RemainingNuyen);
        Assert.AreEqual((decimal?)10000m, review.Balances.NuyenOverspend);
        Assert.AreEqual(60000m, review.Lifestyles!.Gear.Resources.Budget.KnownPurchaseCost);
        CollectionAssert.Contains(combined.Blockers.ToArray(), CharacterCreationLifestylesBlockers.InsufficientFunds);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Whole_review_preserves_real_cold_saved_choices_receipts_and_never_confirms_a_projection()
    {
        using var context = CreateSavedContext();
        using var cold = context.Restart();
        var inputs = LoadInputs(cold);
        var selections = BaselineSelections(inputs);
        Assert.IsTrue(selections.SkillAllocations.Any(item => item.IsNativeLanguage));
        Assert.IsTrue(selections.SkillAllocations.Any(item => item.Kind == CharacterCreationSkillKinds.Active));
        Assert.IsTrue(selections.GroupAllocations.Count > 0);
        Assert.IsTrue(selections.MagicSelections.AdeptPowers.Count > 0);
        Assert.IsTrue(selections.MagicSelections.Spells.Count > 0);
        Assert.AreEqual(1, selections.MagicSelections.MysticAdeptPowerPoints);
        Assert.AreEqual(1, selections.QualityOptionIds.Count);
        Assert.IsTrue(selections.Gear.Count > 0);
        Assert.AreEqual(1, selections.Contacts.Count);
        Assert.AreEqual(1, selections.Lifestyles.Count);
        Assert.IsTrue(inputs.Lifestyles.Gear.Receipts.Count > 0);
        Assert.IsTrue(inputs.Contacts.Receipts.Count > 0);
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var result = Review(inputs, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(selections, result.Value!.Request.Selections));
        Assert.AreEqual((int?)7, result.Value.Balances.RemainingCreationKarma);
        Assert.AreEqual(inputs.Lifestyles.Gear.State.PendingDraft!.Budget.BasketCost, result.Value.Balances.GearCost);
        Assert.AreEqual((decimal?)2000m, result.Value.Balances.LifestyleCost);

        var dropped = Review(inputs, selections with { Gear = [] });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, dropped.Outcome);
        CollectionAssert.Contains(dropped.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SelectionPreservationConflict);

        // A complete review remains read-only, not an ordinary mutation receipt.
        var contacts = new CharacterCreationContactsService(cold.Store, cold.Resolver, cold.Queries);
        var current = contacts.Load(new(cold.WorkspaceId)).Value!;
        var retained = selections.Contacts.Single();
        var edit = new CharacterCreationContactEdit(retained.ContactId,
            retained.Identity with { Name = "Not applied by review" });
        var ordinary = contacts.Preview(new(current.Binding, edit));
        Assert.IsTrue(ordinary.Value!.CanConfirm, string.Join(",", ordinary.Blockers));
        var rejected = contacts.Confirm(new(current.Binding, edit, result.Value.ReviewDigest,
            "whole-review-is-not-confirm-authority", true));
        Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationContactsBlockers.PreviewDigestMismatch);
        AssertWorkspaceUnchanged(cold, before, files);
        Assert.AreEqual(inputs.SnapshotDigest, LoadInputs(cold).SnapshotDigest);
    }

    [TestMethod]
    public void Whole_review_is_detached_cancellable_and_rejects_mixed_actual_observations()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = LoadInputs(context);
        var selections = InitialSelections(inputs);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        bool canceled = false;
        try { Review(inputs, selections, new CancellationToken(true)); }
        catch (OperationCanceledException) { canceled = true; }
        Assert.IsTrue(canceled);
        var corrupt = Review(inputs with { SnapshotDigest = new string('0', 64) }, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, corrupt.Outcome);
        Assert.IsNull(corrupt.Value);
        CollectionAssert.Contains(corrupt.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.AuthorityUnavailable);
        var staleRequest = CharacterCreationCompletionReviewService.Review(inputs,
            new(new string('0', 64), selections));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, staleRequest.Outcome);
        Assert.IsNull(staleRequest.Value);
        CollectionAssert.Contains(staleRequest.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);

        var attributes = selections.AttributeAllocations.ToArray();
        var skills = selections.SkillAllocations.ToArray();
        var powers = selections.MagicSelections.AdeptPowers.ToList();
        var spells = selections.MagicSelections.Spells.ToList();
        var gear = selections.Gear.ToArray();
        var contacts = selections.Contacts.ToArray();
        var homes = selections.Lifestyles.ToArray();
        var mutable = selections with
        {
            AttributeAllocations = attributes, SkillAllocations = skills,
            MagicSelections = selections.MagicSelections with { AdeptPowers = powers, Spells = spells },
            Gear = gear, Contacts = contacts, Lifestyles = homes
        };
        var result = Review(inputs, mutable);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        var heldDigest = CharacterCreationFinalizationDigest.Compute(result.Value);
        attributes[0] = new("MAG", 100, 0);
        skills[0] = skills[0] with { IsNativeLanguage = false, Rating = 999 };
        powers.Clear();
        spells.Clear();
        gear[0] = gear[0] with { Quantity = 999999 };
        contacts[0] = contacts[0] with { Connection = 999 };
        homes[0] = homes[0] with { Increments = 999 };
        Assert.AreEqual(heldDigest, CharacterCreationFinalizationDigest.Compute(result.Value));
        Assert.AreEqual(result.Value!.ReviewDigest, CharacterCreationCompletionReviewRules.ComputeReviewDigest(result.Value));
        AssertWorkspaceUnchanged(context, before, files);

        // Both components came from honest source reads, separated by a real
        // save. Resealing their outer envelope cannot make them one observation.
        MagicResonanceCompletionTests.SaveAttributes(context, [new("MAG", 1, 0), new("CHA", 1, 0)]);
        var current = LoadInputs(context);
        Assert.AreNotEqual(inputs.Skills.Attributes.SnapshotDigest, current.Skills.Attributes.SnapshotDigest);
        var mixed = inputs with { Contacts = current.Contacts };
        mixed = mixed with { SnapshotDigest = CharacterCreationCompletionReviewRules.ComputeInputsDigest(mixed) };
        before = context.Store.Get(context.WorkspaceId).Value!;
        files = CaptureFiles(context);
        var denied = Review(mixed, selections);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, denied.Outcome);
        Assert.IsNull(denied.Value);
        CollectionAssert.Contains(denied.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        AssertWorkspaceUnchanged(context, before, files);
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReview> AssertMatchesIndependentReview(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections selections,
        CharacterCreationCompletionPoolBalances expectedBalances, params string[] additionalBlockers)
    {
        // Independently evaluate each component as the original full review did.
        // Balances are fixture expectations, not copied from the review under test.
        Assert.IsNull(inputs.Loadout);
        var skills = CharacterCreationSkillsService.PreviewWithProposedAttributes(inputs.Skills,
            selections.AttributeAllocations, selections.SkillAllocations, selections.GroupAllocations);
        var upstream = CharacterCreationCompletionBudgetService.Preview(inputs.Magic, inputs.Lifestyles.Qualities,
            selections.AttributeAllocations, selections.MagicSelections, selections.QualityOptionIds);
        var lifestyles = CharacterCreationLifestylesService.PreviewCompletion(inputs.Lifestyles, inputs.Magic,
            selections.AttributeAllocations, selections.MagicSelections, selections.QualityOptionIds,
            selections.TotalKarmaInvestment, selections.Gear, selections.Lifestyles, selections.StartingLifestyleId);
        var contacts = CharacterCreationContactsService.PreviewCompletion(inputs.Contacts,
            selections.AttributeAllocations, selections.QualityOptionIds, selections.Contacts);
        string[] blockers = skills.Blockers.Concat(upstream.Blockers).Concat(lifestyles.Blockers)
            .Concat(contacts.Blockers).Concat(CharacterCreationCompletionPreservationRules.Check(inputs, selections))
            .Concat(additionalBlockers)
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var expected = new CharacterCreationCompletionReview(CharacterCreationCompletionReviewSchemas.ReviewV1,
            inputs.SnapshotDigest, new(inputs.SnapshotDigest, selections), skills.Value, upstream.Value,
            lifestyles.Value, contacts.Value, expectedBalances, blockers, string.Empty);
        expected = expected with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(expected) };

        var actual = Review(inputs, selections);
        Assert.AreEqual(blockers.Length == 0 ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, actual.Outcome, string.Join(",", actual.Blockers));
        CollectionAssert.AreEqual(blockers, actual.Blockers.ToArray());
        Assert.IsNotNull(actual.Value);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(upstream.Value,
            actual.Value!.UpstreamBudget), "The reused upstream projection must match an independent budget calculation.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills.Value,
            actual.Value.Skills), "The skill projection must remain unchanged.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(lifestyles.Value,
            actual.Value.Lifestyles), "The lifestyle projection must remain unchanged.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(contacts.Value,
            actual.Value.Contacts), "The contacts projection must remain unchanged.");
        Assert.AreEqual(expectedBalances, actual.Value.Balances,
            "Whole-review balances must match the independently priced fixture.");
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(expectedBalances),
            System.Text.Json.JsonSerializer.Serialize(actual.Value.Balances),
            "Expected currency values must retain their source-pricing decimal representation.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected.Request,
            actual.Value.Request), "The admitted request must remain unchanged.");
        Assert.AreEqual(expected.Schema, actual.Value.Schema);
        Assert.AreEqual(expected.InputsSnapshotDigest, actual.Value.InputsSnapshotDigest);
        Assert.AreEqual(expected.ReviewDigest, actual.Value.ReviewDigest,
            "The whole-review digest must retain the independent representation.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, actual.Value));
        return actual;
    }

    private static CharacterCreationFoundationResult<CharacterCreationCompletionReview> Review(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections selections,
        CancellationToken cancellationToken = default)
        => CharacterCreationCompletionReviewService.Review(inputs, new(inputs.SnapshotDigest, selections), cancellationToken);

    private static CharacterCreationCompletionSelections InitialSelections(CharacterCreationCompletionReviewInputs inputs)
    {
        var native = inputs.Skills.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(item => item.Name == "Pistols");
        var athletics = inputs.Skills.Authority.SkillGroups.Single(item => item.Name == "Athletics");
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && item.Rating == 1);
        Assert.IsTrue(guts.IsSelectable, guts.DisableReasonKey);
        Assert.AreEqual(10, guts.KarmaCost);
        var supplies = inputs.Lifestyles.Gear.State.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && item.IsSelectable);
        Assert.AreEqual(100m, supplies.PackageCost);
        var home = LowLifestyle(inputs.Lifestyles.Authority);
        // The chosen Priority Talent already grants real skill rows before the
        // first Skills save. Preserve those choices rather than replacing them
        // with only the newly proposed point-funded selections.
        Assert.IsTrue(inputs.Skills.Allocations.Count > 0);
        CharacterCreationSkillAllocation[] skills =
        [
            .. inputs.Skills.Allocations,
            new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
            new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, 2, null, false)
        ];
        CharacterCreationSkillGroupAllocation[] groups = [.. inputs.Skills.GroupAllocations, new(athletics.GroupId, 1)];
        Assert.IsTrue(inputs.Skills.Allocations.All(original => skills.Contains(original)));
        Assert.IsTrue(inputs.Skills.GroupAllocations.All(original => groups.Contains(original)));
        return new([new("MAG", 1, 0)],
            skills, groups,
            MagicResonanceCompletionTests.CompleteSelections(inputs.Magic, inputs.Magic.SelectedTalent.Magic + 1, mysticPowerPoints: 1),
            [guts.OptionId], 3, [new(supplies.OptionId, 1)],
            [new(Guid.Parse("4ede9a39-0bf5-40aa-9da9-ec8a91c2daf3"),
                new CharacterCreationContactIdentity("Street Medic", "Professional", "Seattle", "", "",
                    "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), 1, 1)],
            [home], home.LifestyleId)
        {
            ChangeableAttributeIds = ["MAG", "BOD"],
            ChangeableSkillTargets =
            [
                new(CharacterCreationSkillCompletionKinds.Group, athletics.GroupId),
                .. athletics.MemberSkillSourceIds.Select(id => new CharacterCreationSkillCompletionTarget(CharacterCreationSkillKinds.Active, id))
            ]
        };
    }

    internal static CharacterCreationCompletionReviewInputs LoadInputs(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var result = reader.LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(result.Value!.SnapshotDigest, CharacterCreationCompletionReviewRules.ComputeInputsDigest(result.Value));
        return result.Value;
    }

    internal static CharacterCreationCompletionSelections BaselineSelections(CharacterCreationCompletionReviewInputs inputs)
        => new(inputs.Skills.Attributes.PendingDraft?.Allocations.ToArray() ?? [],
            inputs.Skills.Allocations.ToArray(), inputs.Skills.GroupAllocations.ToArray(), inputs.Magic.Selections,
            inputs.Lifestyles.Qualities.SelectedOptionIds.ToArray(),
            inputs.Lifestyles.Gear.Resources.PendingDraft?.KarmaInvestment ?? 0,
            inputs.Lifestyles.Gear.State.PendingDraft?.Lines
                .Select(line => new CharacterCreationGearSelection(line.OptionId, line.Quantity)).ToArray() ?? [],
            inputs.Contacts.Contacts.ToArray(), inputs.Lifestyles.Lifestyles.ToArray(),
            inputs.Lifestyles.Lifestyles.FirstOrDefault()?.LifestyleId);

    internal static ReadyContext CreateSavedContext(IReadOnlyList<string>? playerChosenAttributeIds = null)
    {
        var context = ReadyContext.Create(includeGearReview: false, includeNonEmptyPurchases: true,
            talentValue: "Mystic Adept", mysticPowerPoints: 1, qualityName: "Guts", includeSkillPurchase: true,
            playerChosenAttributeIds: playerChosenAttributeIds);
        try
        {
            // A real three-Karma investment gives the preservation tests a
            // nonzero saved choice to protect against silent refunds.
            var resources = new CharacterCreationResourcesService(context.Store, context.Resolver);
            var resourceState = resources.Load(new(context.WorkspaceId)).Value!;
            var resourceOption = resourceState.Options.Single(item => item.KarmaInvestment == 3 && item.IsEnabled);
            var resourcePreview = resources.Preview(new(resourceState.Binding, resourceOption.OptionId)).Value!;
            Assert.IsTrue(resourcePreview.CanConfirm, string.Join(",", resourcePreview.Blockers));
            var resourceReceipt = resources.Confirm(new(resourceState.Binding, resourceOption.OptionId,
                resourcePreview.PreviewDigest, "whole-completion-chosen-resources", true));
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resourceReceipt.Outcome,
                string.Join(",", resourceReceipt.Blockers));
            var gear = new CharacterCreationGearService(context.Store, context.Resolver);
            var gearState = gear.Load(new(context.WorkspaceId)).Value!;
            var supplies = gearState.Authority.Options.Single(item =>
                item.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && item.IsSelectable);
            CharacterCreationGearSelection[] basket = [new(supplies.OptionId, 1)];
            var gearPreview = gear.Preview(new(gearState.Binding, basket)).Value!;
            Assert.IsTrue(gearPreview.CanConfirm, string.Join(",", gearPreview.Blockers));
            var gearReceipt = gear.Confirm(new(gearState.Binding, basket, gearPreview.PreviewDigest,
                "whole-completion-chosen-gear", true));
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gearReceipt.Outcome, string.Join(",", gearReceipt.Blockers));

            var contacts = new CharacterCreationContactsService(context.Store, context.Resolver, context.Queries);
            var contactState = contacts.Load(new(context.WorkspaceId)).Value!;
            Assert.IsTrue(contactState.CanEdit, string.Join(",", contactState.Blockers));
            var addContact = new CharacterCreationContactEdit(Guid.NewGuid(),
                contactState.NewContactTemplate!.Identity with { Name = "Chosen Street Medic" }, Connection: 1, Loyalty: 1)
            { ChangeKind = CharacterCreationContactChangeKind.Add };
            var contactPreview = contacts.Preview(new(contactState.Binding, addContact)).Value!;
            Assert.IsTrue(contactPreview.CanConfirm, string.Join(",", contactPreview.Blockers));
            var contactReceipt = contacts.Confirm(new(contactState.Binding, addContact, contactPreview.PreviewDigest,
                "whole-completion-chosen-contact", true));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, contactReceipt.Outcome,
                string.Join(",", contactReceipt.Blockers));

            var lifestyles = new CharacterCreationLifestylesService(context.Store, context.Resolver, context.Queries);
            var lifestyleState = lifestyles.Load(new(context.WorkspaceId)).Value!;
            Assert.IsTrue(lifestyleState.CanEdit, string.Join(",", lifestyleState.Blockers));
            var home = LowLifestyle(lifestyleState.Authority);
            var mutation = new CharacterCreationLifestyleMutation(CharacterCreationLifestyleMutationKinds.Create,
                home.LifestyleId, home);
            var lifestylePreview = lifestyles.Preview(new(lifestyleState.Binding, mutation)).Value!;
            Assert.IsTrue(lifestylePreview.CanConfirm, string.Join(",", lifestylePreview.Blockers));
            var lifestyleReceipt = lifestyles.Confirm(new(lifestyleState.Binding, mutation, lifestylePreview.PreviewDigest,
                "whole-completion-chosen-home", true));
            Assert.AreEqual(CharacterCreationLifestyleOutcomes.Applied, lifestyleReceipt.Outcome,
                string.Join(",", lifestyleReceipt.Blockers));
            return context;
        }
        catch
        {
            context.Dispose();
            throw;
        }
    }

    internal static CharacterCreationLifestyleConfiguration LowLifestyle(CharacterCreationLifestylesAuthority authority)
    {
        var low = authority.LifestyleOptions.Single(item => item.Name == "Low" && item.IsSelectable);
        return new(Guid.Parse("8e148991-7741-4fb4-843f-daa206aa60bc"), low.OptionId, "Chosen Apartment",
            CharacterCreationLifestyleStyleIds.Standard, low.DefaultIncrementId, 1, 100m, 0,
            false, false, 0, 0, 0, 0, "Seattle", "", "", []);
    }

    private static IReadOnlyDictionary<string, byte[]> CaptureFiles(ReadyContext context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkspaceUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        var current = CaptureFiles(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), current.Keys.ToArray());
        foreach (var file in files)
            CollectionAssert.AreEqual(file.Value, current[file.Key], file.Key);
    }
}
