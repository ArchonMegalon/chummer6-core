using System.Globalization;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using O = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    private static readonly O[] EconomicOperations383 = [O.ResourcesAndAvailability, O.Gear];

    private static CharacterCreationCompletionReviewRequest EconomicRequest383(CharacterCreationCompletionReviewInputs inputs,
        int investment = 1, int quantity = 1)
    {
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        var retained = request.Selections.Gear.Select(row => row.OptionId).ToHashSet(StringComparer.Ordinal);
        var option = inputs.Lifestyles.Gear.State.Authority.Options.First(row => row.IsSelectable && row.PackageQuantity == 1
            && row.PackageCost > 0m && row.PackageCost < 100m && !retained.Contains(row.OptionId));
        return request with
        {
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            Selections = request.Selections with
            {
                TotalKarmaInvestment = request.Selections.TotalKarmaInvestment + investment,
                // Finish may fill gaps, but it must not rewrite a retained manual purchase.
                Gear = [.. request.Selections.Gear, new(option.OptionId, quantity)]
            }
        };
    }

    private static void AssertEconomics383(CharacterCreationCompleteAssessment proof, C coverage)
    {
        foreach (var operation in EconomicOperations383)
        {
            var row = Operation332(proof, operation);
            Assert.AreEqual(coverage, row.Coverage, operation.ToString());
            if (coverage == C.Evaluated) Assert.IsTrue(CharacterCreationFinalizationDigest.IsCanonical(row.EvaluationDigest));
            else Assert.IsNull(row.EvaluationDigest);
        }
        Assert.IsFalse(proof.SatisfiesComplete);
    }

    [TestMethod]
    [DataRow("2000.5", 1, 1, false)]
    [DataRow("2400.25", 2, 3, false)]
    [DataRow("2000.5", 1, 1, true)]
    public void Complete383_Funding_and_gear_execute_changed_rules_in_full_and_cold_replay(string rateText, int investment, int quantity, bool retained)
    {
        decimal rate = decimal.Parse(rateText, CultureInfo.InvariantCulture);
        using var context = ReadyContext.Create(retained, includeNonEmptyPurchases: retained, amendSettings: settings =>
        {
            settings.SetElementValue("chargenkarmatonuyenexpression", "{Karma} * " + rateText + " + {PriorityNuyen}");
            settings.SetElementValue("nuyenmaxbp", 10);
        });
        string before = JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!);
        var inputs = LoadConsumption(context);
        string inputBytes = JsonSerializer.Serialize(inputs);
        if (retained)
        {
            Assert.IsNotNull(inputs.Lifestyles.Gear.State.PendingDraft);
            Assert.IsTrue(inputs.Lifestyles.Gear.Receipts.Count > 0);
        }
        var request = EconomicRequest383(inputs, investment, quantity);
        decimal expectedGear = request.Selections.Gear.Sum(selection => selection.Quantity
            * inputs.Lifestyles.Gear.State.Authority.Options.Single(row => row.OptionId == selection.OptionId).PackageCost);
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        var review = full.Value!;
        Assert.AreEqual((decimal?)(inputs.Lifestyles.Gear.Resources.PriorityOption.BasePriorityNuyen
            + rate * request.Selections.TotalKarmaInvestment), review.Balances.CreationNuyen);
        Assert.AreEqual((decimal?)expectedGear, review.Balances.GearCost);
        AssertEconomics383(review.CompleteAssessment!, C.Evaluated);
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(review.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(JsonSerializer.Serialize(review.Lifestyles!.Gear.Lines), JsonSerializer.Serialize(cold.Gear));
        Assert.AreEqual(JsonSerializer.Serialize(review.Lifestyles.Gear.Resources.Budget), JsonSerializer.Serialize(cold.Resources.Budget));
        Assert.AreEqual(inputBytes, JsonSerializer.Serialize(inputs));
        Assert.AreEqual(before, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!));

        var required = CharacterCreationCompletionReviewService.Review(inputs,
            request with { PolicyRequirement = CharacterCreationPolicyRequirement.Complete });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, required.Outcome);
        Assert.AreEqual(true, required.Value!.CompleteAssessment!.MechanicalSucceeded);
        AssertEconomics383(required.Value.CompleteAssessment, C.Evaluated);
    }

    [TestMethod]
    public void Complete383_Empty_gear_needs_invocation_and_absent_scope_or_authority_cannot_supply_it()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with
            { CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4 };
        Assert.AreEqual(0, request.Selections.Gear.Count);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        AssertEconomics383(review.CompleteAssessment!, C.Evaluated);
        var copied = JsonSerializer.Deserialize<CharacterCreationCompletionReview>(JsonSerializer.Serialize(review))!;
        AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, copied.PolicyConsumption, true), C.Unresolved);
        Assert.IsNotNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, copied, authority));
        Assert.IsNotNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, copied, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, copied, null));
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, copied, null));
        var absent = request with { Selections = request.Selections with { Gear = null! } };
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, copied with { Request = absent }, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, copied with { Request = absent }, authority));
        var cold = Cold342(context, inputs, request, out var witness);
        Assert.AreEqual(JsonSerializer.Serialize(review.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, absent, cold, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, null));
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, cold, null));
        Assert.IsFalse(JsonSerializer.Serialize(witness).Contains("CompleteFundingExecution", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(witness).Contains("CompleteCostExecution", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Complete383_Private_executions_bind_original_inputs_authority_request_and_mechanical_results()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = EconomicRequest383(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        Assert.IsNotNull(review);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var funding = CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority);
        var gear = CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, review, authority);
        var consumers = CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, authority);
        Assert.IsNotNull(funding);
        Assert.IsNotNull(gear);
        Assert.IsNotNull(consumers);
        Assert.IsTrue(funding!.MatchesFull(inputs, review, authority));
        Assert.IsTrue(gear!.MatchesFull(inputs, review, authority));
        string drift = CharacterCreationFinalizationDigest.Compute("different economic authority");
        foreach (var borrowed in new[]
        {
            authority with { InputsSnapshotDigest = drift }, authority with { RawCharacterXmlDigest = drift },
            authority with { SelectedInputsDigest = drift }, authority with { PolicyDigest = drift },
            authority with { RequestDigest = drift }, authority with { SelectionsDigest = drift },
            authority with { SettingsObservationDigest = drift },
            authority with { Obligations = authority.Obligations.Select(row => row with { AuthorityDigest = drift }).ToArray() }
        })
        {
            Assert.IsFalse(funding.MatchesFull(inputs, review, borrowed));
            Assert.IsFalse(gear.MatchesFull(inputs, review, borrowed));
            Assert.AreEqual(0, consumers!.EvaluationsFor(borrowed).Count);
            Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(inputs, review, borrowed));
            Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, borrowed));
            Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, review, borrowed));
        }
        // Same prices, different request: a reserve claim cannot borrow a trace.
        var changedRequest = request with { Selections = request.Selections with { KarmaToReserve = request.Selections.KarmaToReserve + 1 } };
        Assert.IsFalse(funding.MatchesFull(inputs, review with { Request = changedRequest }, authority));
        Assert.IsFalse(gear.MatchesFull(inputs, review with { Request = changedRequest }, authority));
        var changedInputs = inputs with { SnapshotDigest = drift };
        Assert.IsFalse(funding.MatchesFull(changedInputs, review, authority));
        Assert.IsFalse(gear.MatchesFull(changedInputs, review, authority));
        var actual = review.Lifestyles!.Gear;
        var wrongFunding = review with { Lifestyles = review.Lifestyles with { Gear = actual with
        {
            Resources = actual.Resources with { Budget = actual.Resources.Budget with { RemainingNuyen = actual.Resources.Budget.RemainingNuyen + 1m } }
        } } };
        Assert.IsFalse(funding.MatchesFull(inputs, wrongFunding, authority));
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, wrongFunding, authority));
        var wrongGear = review with { Lifestyles = review.Lifestyles with { Gear = actual with
            { Budget = actual.Budget with { BasketCost = actual.Budget.BasketCost + 1m } } } };
        Assert.IsFalse(gear.MatchesFull(inputs, wrongGear, authority));
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, wrongGear, authority));
        foreach (var changed in new[] { wrongFunding, wrongGear })
            AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, true,
                consumers: CharacterCreationCompleteConsumerExecution.FromFull(inputs, changed, authority)), C.Unresolved);
        AssertEconomics383(CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, review.PolicyConsumption, false,
            consumers: consumers), C.Unresolved);

        var cold = Cold342(context, inputs, request, out var witness);
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var selectedAuthority = CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, inputs.Contacts.OriginalEffects);
        Assert.IsFalse(funding.MatchesSelected(witness, request, cold, selectedAuthority));
        Assert.IsFalse(gear.MatchesSelected(witness, request, cold, selectedAuthority));
        var coldFunding = CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, cold, selectedAuthority);
        var coldGear = CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, cold, selectedAuthority);
        Assert.IsNotNull(coldFunding);
        Assert.IsNotNull(coldGear);
        Assert.IsTrue(coldFunding!.MatchesSelected(witness, request, cold, selectedAuthority));
        Assert.IsTrue(coldGear!.MatchesSelected(witness, request, cold, selectedAuthority));
        Assert.AreEqual(funding.EvaluationDigest, coldFunding.EvaluationDigest);
        Assert.AreEqual(gear.EvaluationDigest, coldGear.EvaluationDigest);
        foreach (var borrowed in new[]
        {
            selectedAuthority with { PolicyDigest = drift }, selectedAuthority with { RawCharacterXmlDigest = drift },
            selectedAuthority with { RequestDigest = drift }
        })
        {
            Assert.IsFalse(coldFunding.MatchesSelected(witness, request, cold, borrowed));
            Assert.IsFalse(coldGear.MatchesSelected(witness, request, cold, borrowed));
            Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromSelected(witness, request, cold, original,
                inputs.Contacts.OriginalEffects, borrowed));
        }
        Assert.IsFalse(coldFunding.MatchesSelected(witness, changedRequest, cold, selectedAuthority));
        Assert.IsFalse(coldGear.MatchesSelected(witness, changedRequest, cold, selectedAuthority));
        Assert.IsFalse(coldFunding.MatchesSelected(witness with { ProvenanceInputsSnapshotDigest = drift }, request, cold, selectedAuthority));
        Assert.IsFalse(coldGear.MatchesSelected(witness with { ProvenanceInputsSnapshotDigest = drift }, request, cold, selectedAuthority));
        var wrongCold = cold with { GearBudget = cold.GearBudget with { BasketCost = cold.GearBudget.BasketCost + 1m } };
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, wrongCold, selectedAuthority));
        Assert.IsFalse(coldFunding.MatchesSelected(witness, request, wrongCold, selectedAuthority));
        var refused = CharacterCreationCompleteConsumerExecution.FromSelected(witness, request, wrongCold, original,
            inputs.Contacts.OriginalEffects, selectedAuthority);
        foreach (var operation in EconomicOperations383)
            Assert.IsFalse(refused!.EvaluationsFor(selectedAuthority).ContainsKey(Operation332(selectedAuthority, operation).Id));
    }

    [TestMethod]
    public void Complete383_Changed_funding_and_basket_get_fresh_execution_and_failures_remain_unresolved()
    {
        using var context = ReadyContext.Create(false, amendSettings: settings => settings.SetElementValue("nuyenmaxbp", 2));
        var inputs = LoadConsumption(context);
        var request = EconomicRequest383(inputs);
        var first = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var changedRequest = EconomicRequest383(inputs, investment: 2, quantity: 2);
        var changed = CharacterCreationCompletionReviewService.Review(inputs, changedRequest);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, changed.Outcome, string.Join(",", changed.Blockers));
        Assert.IsTrue(changed.Value!.Balances.CreationNuyen > first.Balances.CreationNuyen);
        Assert.IsTrue(changed.Value.Balances.GearCost > first.Balances.GearCost);
        AssertEconomics383(changed.Value.CompleteAssessment!, C.Evaluated);
        foreach (var operation in EconomicOperations383)
            Assert.AreNotEqual(Operation332(first.CompleteAssessment!, operation).EvaluationDigest,
                Operation332(changed.Value.CompleteAssessment!, operation).EvaluationDigest);
        Assert.AreEqual(JsonSerializer.Serialize(changed.Value.CompleteAssessment),
            JsonSerializer.Serialize(Cold342(context, inputs, changedRequest).CompleteAssessment));
        foreach (var invalid in new[]
        {
            request with { Selections = request.Selections with { TotalKarmaInvestment = 3 } },
            request with { Selections = request.Selections with { Gear = [new("absent-source-option-383", 1)] } },
            request with { Selections = request.Selections with { Gear = [new(request.Selections.Gear.Single().OptionId, 0)] } }
        })
        {
            var rejected = CharacterCreationCompletionReviewService.Review(inputs, invalid);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome);
            Assert.IsTrue(rejected.Blockers.Count > 0);
            if (rejected.Value is { CompleteAssessment: { } proof } review)
            {
                AssertEconomics383(proof, C.Unresolved);
                Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(inputs, review,
                    CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, invalid)));
            }
        }
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        Assert.Throws<OperationCanceledException>(() => CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, first, authority, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, first, authority, cancellation.Token));
    }

    [TestMethod]
    public void Complete383_Absent_or_resealed_economic_policy_cannot_borrow_complete_execution()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = EconomicRequest383(inputs);
        var review = CharacterCreationCompletionReviewService.Review(inputs, request).Value!;
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        var policy = inputs.EffectivePolicy!;
        foreach (var changed in new[]
        {
            WithPolicy(inputs, policy with { Consumers = null }),
            WithPolicy(inputs, policy with { Consumers = policy.Consumers! with { Domains = policy.Consumers!.Domains! with
                { Resources = policy.Consumers.Domains!.Resources with { KarmaToNuyenRate = policy.Consumers.Domains.Resources.KarmaToNuyenRate + 1m } } } }),
            WithPolicy(inputs, policy with { Consumers = policy.Consumers! with { Domains = policy.Consumers!.Domains! with
                { Gear = policy.Consumers.Domains!.Gear with { MaximumAvailability = policy.Consumers.Domains.Gear.MaximumAvailability + 1 } } } })
        })
        {
            Assert.IsNull(CharacterCreationCompleteConsumerExecution.FromFull(changed, review, authority));
            var denied = CharacterCreationCompletionReviewService.Review(changed, EconomicRequest383(changed));
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, denied.Outcome);
            Assert.IsTrue(denied.Blockers.Count > 0);
        }
    }

    [TestMethod]
    public void Complete383_Rehashed_public_evidence_and_changed_selected_source_are_refused_on_replay()
    {
        using var context = ReadyContext.Create(false);
        var inputs = LoadConsumption(context);
        var request = EconomicRequest383(inputs);
        _ = Cold342(context, inputs, request, out var witness);
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionBootstrapSnapshot.TryCapture(
            context.Resolver.TryCreateContext(original.Document.Content)!, original.Document.Content, out var bootstrap));
        string drift = CharacterCreationFinalizationDigest.Compute("rehashed economic claim");
        foreach (var forged in new[]
        {
            witness with { CompleteAssessment = witness.CompleteAssessment! with
                { Obligations = witness.CompleteAssessment!.Obligations.Select(row => EconomicOperations383.Contains(row.Operation.GetValueOrDefault())
                    ? row with { EvaluationDigest = drift } : row).ToArray() } },
            witness with { Gear = witness.Gear with { SourceDigest = drift } },
            witness with { Resources = witness.Resources with { Authority = witness.Resources.Authority with { SourceDigest = drift } } },
            witness with { EffectivePolicy = null }
        })
        {
            var resealed = forged with { WitnessDigest = CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(forged) };
            var rejected = CharacterCreationSelectedCompletionEvaluator.Replay(original, bootstrap, resealed, request);
            Assert.IsNull(rejected.Value);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome);
            Assert.IsTrue(rejected.Blockers.Count > 0);
        }
    }

    [TestMethod]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV1)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV2)]
    [DataRow(CharacterCreationCompleteAssessment.SchemaV3)]
    public void Complete383_Historical_versions_keep_unresolved_economics_and_repeat_exactly(string schema)
    {
        using var context = ReadyContext.Create(true, includeNonEmptyPurchases: true);
        var inputs = LoadConsumption(context);
        var request = Request(inputs, CharacterCreationPolicyRequirement.None) with { CompleteAssessmentSchema = schema };
        var full = CharacterCreationCompletionReviewService.Review(inputs, request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, full.Outcome, string.Join(",", full.Blockers));
        AssertEconomics383(full.Value!.CompleteAssessment!, C.Unresolved);
        string bytes = JsonSerializer.Serialize(full.Value);
        var authority = CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, request);
        Assert.IsNull(CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, full.Value, authority));
        Assert.IsNull(CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, full.Value, authority));
        var cold = Cold342(context, inputs, request);
        Assert.AreEqual(JsonSerializer.Serialize(full.Value.CompleteAssessment), JsonSerializer.Serialize(cold.CompleteAssessment));
        Assert.AreEqual(bytes, JsonSerializer.Serialize(CharacterCreationCompletionReviewService.Review(inputs, request).Value));
        var omitted = CharacterCreationCompletionReviewService.Review(inputs, request with { CompleteAssessmentSchema = null });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, omitted.Outcome);
        Assert.IsFalse(JsonSerializer.Serialize(omitted.Value).Contains("CompleteAssessment", StringComparison.Ordinal));
    }
}
