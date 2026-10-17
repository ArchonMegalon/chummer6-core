using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionPlannerTests
{
    [TestMethod]
    public void Planning_session_admission_does_not_promote_an_unfinished_character_to_ready()
    {
        using var context = CharacterCreationFinalizationServiceTests.ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var prepared = CharacterCreationCompletionPlanner.CreateSession(inputs);
        Assert.IsNotNull(prepared.Value);
        var actual = prepared.Value!.Plan(request);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, actual.Outcome);
        Assert.IsTrue(actual.Blockers.Count > 0);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            CharacterCreationCompletionPlanner.Plan(inputs, request), actual));
    }

    [TestMethod]
    public void Planning_session_matches_independent_plans_for_each_changed_candidate_without_writing()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var files = Snapshot(context);
        var prepared = CharacterCreationCompletionPlanner.CreateSession(inputs);
        Assert.IsNotNull(prepared.Value, string.Join(",", prepared.Blockers));
        foreach (var candidate in new[] { request, request with { KarmaToReserve = 3 },
                     request with { KarmaToReserve = 2, Magic = request.Magic with
                     { DesiredTotalMysticAdeptPowerPoints = 2, MaximumAdditionalPowerPointKarmaSpend = 5 } } })
        {
            var expected = CharacterCreationCompletionPlanner.Plan(inputs, candidate);
            var actual = prepared.Value!.Plan(candidate);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, actual));
            Assert.IsNotNull(actual.Value);
            var independent = CharacterCreationCompletionReviewService.Review(inputs,
                new(inputs.SnapshotDigest, actual.Value!.Selections));
            Assert.AreEqual(independent.Value!.ReviewDigest, actual.Value.Review.ReviewDigest);
        }
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Planning_session_detaches_original_inputs_candidate_requests_and_each_result()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var originalAttributes = inputs.Skills.Attributes.Attributes.ToArray();
        inputs = inputs with { Skills = inputs.Skills with
        { Attributes = inputs.Skills.Attributes with { Attributes = originalAttributes } } };
        var request = RetainingRequest(inputs);
        var expected = CharacterCreationCompletionPlanner.Plan(inputs, request);
        var session = CharacterCreationCompletionPlanner.CreateSession(inputs).Value!;
        originalAttributes[0] = originalAttributes[0] with { KarmaLevels = 99 };
        var ids = request.QualityOptionIds.ToArray();
        var result = session.Plan(request with { QualityOptionIds = ids });
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, result));
        Assert.IsTrue(ids.Length > 0);
        ids[0] = "mutated-caller-choice";
        Assert.AreEqual(expected.Value!.PlanDigest, CharacterCreationCompletionPlanRules.ComputePlanDigest(result.Value!));
        var returned = (IList<CharacterCreationAttributeAllocation>)result.Value!.Selections.AttributeAllocations;
        returned[0] = returned[0] with { KarmaLevels = 98 };
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, session.Plan(request)),
            "A result must not lend out session-owned collections.");
        Assert.IsNull(CharacterCreationCompletionPlanner.CreateSession(inputs).Value,
            "A newly supplied tampered observation must still fail fresh admission.");
    }

    [TestMethod]
    public void Planning_session_keeps_conflict_preservation_negative_quality_and_reserve_guards()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var session = CharacterCreationCompletionPlanner.CreateSession(inputs).Value!;
        var negative = inputs.Lifestyles.Qualities.Authority.Options.First(row =>
            row.Type == CharacterCreationQualityType.Negative && row.IsSelectable
            && !inputs.Lifestyles.Qualities.SelectedOptionIds.Contains(row.OptionId));
        foreach (var candidate in new[]
        {
            request with { InputsSnapshotDigest = new string('0', 64) },
            request with { Magic = request.Magic with { MagicSnapshotDigest = new string('0', 64) } },
            request with { QualityOptionIds = [] }, request with { Contacts = [] },
            request with { TotalKarmaInvestment = 0 }, request with { KarmaToReserve = 999 },
            request with { QualityOptionIds = [.. request.QualityOptionIds, negative.OptionId] },
            request with { Magic = request.Magic with
            { ManualSelections = request.Magic.ManualSelections with { MysticAdeptPowerPoints = 2 } } },
            request with { MaximumPurchaseEvaluations = 0 }
        })
        {
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                CharacterCreationCompletionPlanner.Plan(inputs, candidate), session.Plan(candidate)));
        }
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, session.Plan(request).Outcome,
            "A rejected candidate must not poison a later valid candidate.");
    }

    [TestMethod]
    public void Planning_session_cancellation_does_not_poison_the_snapshot()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext();
        var inputs = CharacterCreationCompletionReviewServiceTests.LoadInputs(context);
        var request = RetainingRequest(inputs);
        var session = CharacterCreationCompletionPlanner.CreateSession(inputs).Value!;
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            CharacterCreationCompletionPlanner.CreateSession(inputs, new CancellationToken(true)));
        Assert.ThrowsExactly<OperationCanceledException>(() => session.Plan(request, new CancellationToken(true)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, session.Plan(request).Outcome);
    }
}
