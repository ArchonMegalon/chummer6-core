using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static CharacterCreationCompletionReviewInputs LoadEconomic387(ReadyContext context)
    {
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var reader = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithPolicyConsumption(owners.Capture(), new(context.WorkspaceId), includeLoadout: true);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        Assert.IsNotNull(loaded.Value!.Loadout);
        return loaded.Value;
    }

    private static CharacterCreationCompletionReviewRequest EconomicRequest387(CharacterCreationCompletionReviewInputs inputs)
    {
        var request = Request342(inputs, EconomicRequest383(inputs)) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var retained = request.Selections.Loadout!.Weapons.Select(row => row.OptionId).ToHashSet(StringComparer.Ordinal);
        // Finish preserves a manually chosen weapon family, including its count.
        // Exercise that retained cost instead of adding a second weapon to it.
        if (retained.Count > 0) return request;
        var option = inputs.Loadout!.Authority.Options.Where(row => row.IsSelectable
                && row.Kind == CharacterCreationLoadoutKinds.Weapon && row.UnitCost > 0m && !retained.Contains(row.OptionId))
            .OrderBy(row => row.UnitCost).ThenBy(row => row.OptionId, StringComparer.Ordinal).First();
        return request with { Selections = request.Selections with
        {
            Loadout = request.Selections.Loadout with
                { Weapons = [.. request.Selections.Loadout.Weapons, new(option.OptionId, 1)] }
        } };
    }

    private static void RetainLoadout387(ReadyContext context)
    {
        var service = new CharacterCreationLoadoutService(context.Store, context.Resolver);
        var prepared = service.Prepare(context.WorkspaceId);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prepared.Outcome, string.Join(",", prepared.Blockers));
        var inputs = prepared.Value!.Inputs;
        var option = inputs.Authority.Options.Where(row => row.IsSelectable
                && row.Kind == CharacterCreationLoadoutKinds.Weapon && row.UnitCost > 0m)
            .OrderBy(row => row.UnitCost).ThenBy(row => row.OptionId, StringComparer.Ordinal).First();
        var request = new CharacterCreationLoadoutPreviewRequest(inputs.Binding, inputs.SnapshotDigest,
            new([new(option.OptionId, 1)], []));
        var preview = service.Preview(request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var saved = service.Confirm(new(request, preview.Value!.PreviewDigest, "economic-upstream-387-retained", true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
    }

    [TestMethod]
    [DataRow("missing-loadout")]
    [DataRow("altered-loadout")]
    [DataRow("altered-lifestyle")]
    public void Complete387_Coherent_cost_forgery_refuses_private_funding_and_economic_coverage(string change)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        var inputs = LoadEconomic387(context);
        var request = EconomicRequest387(inputs);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var review = full.Value!;
        Assert.IsTrue(review.Loadout!.PurchaseCost > 0m);
        Assert.IsTrue(review.Lifestyles!.Budget.Used > 0m);
        AssertEconomics383(review.CompleteAssessment!, C.Evaluated);
        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, inputs.Contacts.OriginalEffects);
        var fullExecution = CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority);
        var coldExecution = CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, selectedAuthority);
        Assert.IsNotNull(fullExecution);
        Assert.IsNotNull(coldExecution);

        // Keep the admitted request/witness. Produce coherent alternative costs
        // through the real consumers, then reprice the entire dependent wallet.
        var forged = RepriceEconomic387(inputs, witness, request, cold, selectedAuthority, change, out var graph);
        var forgedReview = WithEconomicResult387(inputs, review, forged);
        Assert.AreNotEqual(cold.Resources.Budget.KnownPurchaseCost, forged.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(forged.Resources.Budget.KnownPurchaseCost, forgedReview.Lifestyles!.Gear.Resources.Budget.KnownPurchaseCost);
        Assert.AreEqual(0, forged.Resources.Blockers.Count);
        // Gear really is still correctly priced. This negative must reach the
        // missing/mismatched upstream-cost admission rather than a broken basket.
        Assert.IsNotNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, forgedReview, authority));
        Assert.IsNotNull(CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, forged, selectedAuthority));
        if (change == "missing-loadout")
        {
            Assert.IsNotNull(graph);
            Assert.IsTrue(graph!.MatchesSelected(witness, request, forged, selectedAuthority));
            Assert.IsTrue(graph.MatchesFull(inputs, forgedReview, authority));
        }
        Assert.IsFalse(fullExecution!.MatchesFull(inputs, forgedReview, authority));
        Assert.IsFalse(coldExecution!.MatchesSelected(witness, request, forged, selectedAuthority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, forgedReview, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, forged, selectedAuthority));
        foreach (bool supplyGraph in new[] { false, true })
        {
            var fullConsumers = CharacterCreationCompleteConsumerExecution.FromFull(inputs, forgedReview, authority,
                lifestyles: supplyGraph ? graph : null);
            var coldConsumers = CharacterCreationCompleteConsumerExecution.FromSelected(witness, request, forged, original,
                inputs.Contacts.OriginalEffects, selectedAuthority, lifestyles: supplyGraph ? graph : null);
            Assert.IsNotNull(fullConsumers);
            Assert.IsNotNull(coldConsumers);
            AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true,
                consumers: fullConsumers), C.Unresolved);
            AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(selectedAuthority,
                witness.EffectivePolicy!.SelectedConsumption, true, consumers: coldConsumers), C.Unresolved);
        }
    }

    private static CharacterCreationSelectedCompletionEvaluation RepriceEconomic387(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
        CharacterCreationCompleteAssessment authority, string change,
        out CharacterCreationLifestylesService.CompleteGraphExecution? graph)
    {
        var loadout = result.Loadout;
        if (change == "missing-loadout") loadout = null;
        else if (change == "altered-loadout")
        {
            var alternate = request.Selections.Loadout! with
            {
                Weapons = request.Selections.Loadout!.Weapons.Select(row => row with { Quantity = row.Quantity + 1 }).ToArray()
            };
            var quote = CharacterCreationLoadoutCompletionService.Preview(inputs.Loadout!, alternate);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, quote.Outcome, string.Join(",", quote.Blockers));
            loadout = quote.Value!;
        }
        var homes = change == "altered-lifestyle"
            ? request.Selections.Lifestyles.Select(row => row with { Increments = row.Increments + 1 }).ToArray()
            : request.Selections.Lifestyles;
        string[] anchors = loadout is null ? result.GearSourceAnchorIds.ToArray() : result.GearSourceAnchorIds
            .Concat(loadout.Lines.SelectMany(row => row.SourceAnchorIds)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        CharacterCreationLifestylesService.CompleteGraphExecution? executed = null;
        var lifestyle = CharacterCreationLifestylesService.CompleteGraphExecution.Price(new(witness.Lifestyles,
            witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], result.Qualities.Selections,
            witness.ProposedQualityProjectionProvenanceSeed, homes, request.Selections.StartingLifestyleId,
            result.GearBudget.RemainingNuyen - (loadout?.PurchaseCost ?? 0m), anchors, []), default,
            value => executed = value, authority);
        Assert.AreEqual(CharacterCreationLifestyleOutcomes.Available, lifestyle.Outcome, string.Join(",", lifestyle.Blockers));
        Assert.IsTrue(CharacterCreationPriorityKarmaBudget.TryEvaluate(result.Qualities.KarmaRemaining, 0,
            result.Magic.Proposed.MysticAdeptPowerPoints, out int available));
        var resources = CharacterCreationResourcesService.PriceSelectedCompletion(new(witness.Resources.Authority,
            witness.Resources.PriorityOption, available, request.Selections.TotalKarmaInvestment, witness.Resources.PurchaseAuthorityExact,
            result.GearBudget.BasketCost + lifestyle.Value!.FullLifestyleCost + (loadout?.PurchaseCost ?? 0m), []));
        Assert.AreEqual(CharacterCreationResourcesOutcomes.Available, resources.Outcome, string.Join(",", resources.Blockers));
        graph = executed;
        var forged = result with
        {
            Loadout = loadout, Lifestyles = lifestyle.Value, Resources = resources.Value!,
            Balances = result.Balances with
            {
                LifestyleCost = lifestyle.Value.FullLifestyleCost, LoadoutCost = loadout?.PurchaseCost,
                RemainingNuyen = resources.Value!.Budget.RemainingNuyen, NuyenOverspend = resources.Value.Budget.Overspend
            }
        };
        return forged with { ReplayDigest = CharacterCreationSelectedCompletionEvaluator.ComputeReplayDigest(forged) };
    }

    private static CharacterCreationCompletionReview WithEconomicResult387(CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionReview review, CharacterCreationSelectedCompletionEvaluation result)
    {
        var actual = review.Lifestyles!;
        var resources = CharacterCreationResourcesService.RepriceCompletionPurchases(inputs.Lifestyles.Gear.Resources,
            actual.Gear.Resources, result.GearBudget.BasketCost, result.Lifestyles.FullLifestyleCost, result.Loadout?.PurchaseCost ?? 0m);
        var gear = actual.Gear with { Resources = resources };
        gear = gear with { ProposalDigest = CharacterCreationGearCompletionRules.ComputeProjectionDigest(gear) };
        var lifestyles = actual with
        {
            Gear = gear, Authority = result.Lifestyles.Authority, Lifestyles = result.Lifestyles.Lifestyles,
            StartingLifestyleId = result.Lifestyles.StartingLifestyleId, Budget = result.Lifestyles.Budget,
            SourceAnchorIds = result.Lifestyles.SourceAnchorIds, Blockers = result.Lifestyles.Blockers
        };
        lifestyles = lifestyles with { ProposalDigest = CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(lifestyles) };
        var forged = review with { Lifestyles = lifestyles, Loadout = result.Loadout, Balances = result.Balances };
        return forged with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(forged) };
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Complete387_Executed_empty_and_nonempty_scopes_preserve_full_cold_equality(bool purchases, bool retained)
    {
        using var context = ReadyContext.Create(retained, includeNonEmptyPurchases: retained);
        if (retained) RetainLoadout387(context);
        var inputs = LoadEconomic387(context);
        string original = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        string inputBytes = JsonSerializer.Serialize(inputs);
        var request = purchases ? EconomicRequest387(inputs) : Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var review = full.Value!;
        Assert.IsNotNull(review.Loadout);
        Assert.AreEqual(purchases, review.Loadout.PurchaseCost > 0m);
        Assert.AreEqual(purchases, review.Lifestyles!.Budget.Used > 0m);
        AssertEconomics383(review.CompleteAssessment!, C.Evaluated);
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.AreEqual(JsonSerializer.Serialize(review.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(JsonSerializer.Serialize(review.Loadout), JsonSerializer.Serialize(cold.Loadout));
        Assert.AreEqual(JsonSerializer.Serialize(review.Lifestyles.Gear.Resources.Budget), JsonSerializer.Serialize(cold.Resources.Budget));
        Assert.AreEqual(review.Lifestyles.Gear.Budget.BasketCost + review.Lifestyles.Budget.Used + review.Loadout.PurchaseCost,
            cold.Resources.Budget.KnownPurchaseCost);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var fullExecution = CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority);
        var coldExecution = CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, authority);
        Assert.IsNotNull(fullExecution);
        Assert.IsNotNull(coldExecution);
        Assert.AreEqual(fullExecution!.EvaluationDigest, coldExecution!.EvaluationDigest);
        Assert.IsFalse(fullExecution.MatchesSelected(witness, request, cold, authority));
        if (retained)
        {
            Assert.IsTrue(inputs.Loadout!.PendingDraft!.Instances.Count > 0);
            Assert.AreEqual(JsonSerializer.Serialize(inputs.Loadout.PendingDraft.Instances), JsonSerializer.Serialize(review.Loadout.RetainedInstances));
            Assert.IsTrue(CharacterCreationLoadoutReceiptLedgerIntegrity.Preserves(inputs.Loadout.OriginalSelections, request.Selections.Loadout!));
            foreach (var row in inputs.Lifestyles.Gear.State.PendingDraft!.Lines)
                Assert.AreEqual(row.Quantity, request.Selections.Gear.Single(item => item.OptionId == row.OptionId).Quantity);
        }
        Assert.AreEqual(inputBytes, JsonSerializer.Serialize(inputs));
        Assert.AreEqual(original, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));
        var required = CharacterCreationCompletionReviewService.Review(inputs, request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, required.Outcome);
        AssertEconomics383(required.Value!.CompleteAssessment!, C.Evaluated);
    }

    [TestMethod]
    public void Complete387_Omitted_legacy_loadout_scope_requires_bound_absence()
    {
        using var context = ReadyContext.Create(false);
        // The pre-loadout contract has no proposed loadout at all. Preserve its
        // valid economic route without treating a missing required component as zero.
        var inputs = LoadConsumption(context);
        Assert.IsNull(inputs.Loadout);
        var request = EconomicRequest383(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        AssertEconomics383(review.Value!.CompleteAssessment!, C.Evaluated);
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(review.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));

        var scoped = LoadEconomic387(context);
        var scopedRequest = EconomicRequest387(scoped);
        var scopedReview = CharacterCreationCompletionReviewService.Review(scoped, scopedRequest).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(scoped, scopedRequest);
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(scoped with { Loadout = null }, scopedReview, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(scoped, scopedReview with { Loadout = null }, authority));
    }

    [TestMethod]
    public void Complete387_Missing_cost_scopes_and_borrowed_execution_are_not_empty_scopes()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadEconomic387(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var cold = Cold342(context, inputs, request, out var witness);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var execution = CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority);
        var selected = CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, authority);
        Assert.IsNotNull(execution);
        Assert.IsNotNull(selected);
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review with { Loadout = null }, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold with { Loadout = null }, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness with { Loadout = null }, request, cold, authority));
        foreach (var absent in new[]
        {
            request with { Selections = request.Selections with { Loadout = null } },
            request with { Selections = request.Selections with { Lifestyles = null! } }
        })
        {
            Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review with { Request = absent }, authority));
            Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, absent, cold, authority));
        }
        string drift = CharacterCreationFinalizationDigest.Compute("economic-upstream-387-drift");
        Assert.IsFalse(execution!.MatchesFull(inputs with { SnapshotDigest = drift }, review, authority));
        Assert.IsFalse(selected!.MatchesSelected(witness, request, cold, authority with { PolicyDigest = drift }));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, authority with { PolicyDigest = drift }));
        var copied = JsonSerializer.Deserialize<CharacterCreationCompletionReview>(JsonSerializer.Serialize(review))!;
        AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, copied.PolicyConsumption, true), C.Unresolved);
        Assert.IsNotNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, copied, authority));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, authority, cancellation.Token));
    }
}
