#nullable enable annotations

using Chummer.Application.Session;
using Chummer.Contracts.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class SessionActionBudgetServiceTests
{
    [TestMethod]
    public void Compute_uses_sr6_minor_formula_and_turn_start_cap()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-1",
            RoundRef: "round-1",
            RulesetId: "sr6",
            InitiativeDice: 6,
            MinorTurnStartCap: 5));

        Assert.AreEqual(1, result.Major.Base);
        Assert.AreEqual(1, result.Major.Available);
        Assert.AreEqual(7, result.Minor.Computed);
        Assert.AreEqual(5, result.Minor.Base);
        Assert.AreEqual(5, result.Minor.Available);
        Assert.AreEqual(5, result.Minor.TurnStartCap);
    }

    [TestMethod]
    public void Compute_marks_full_defense_available_when_four_minor_actions_remain()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-2",
            RoundRef: "round-1",
            RulesetId: "sr6",
            InitiativeDice: 3,
            MinorSpent: 0,
            MajorSpent: 0));

        SessionActionAffordance fullDefense = result.Affordances.Single(affordance => affordance.ActionKey == "full-defense");

        Assert.AreEqual(SessionActionAffordanceStates.Available, fullDefense.State);
        Assert.AreEqual(SessionActionBudgetTimingModes.Anytime, fullDefense.Timing);
        Assert.AreEqual(4, fullDefense.Cost.Minor);
        Assert.AreEqual(1, result.Conversions.ConvertibleAnytimeMajorCount);
    }

    [TestMethod]
    public void Compute_does_not_offer_four_minor_conversion_before_actor_turn()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-3",
            RoundRef: "round-1",
            RulesetId: "sr6",
            InitiativeDice: 4,
            IsOwnTurnActive: false,
            MinorSpent: 0));

        Assert.AreEqual(0, result.Conversions.ConvertibleAnytimeMajorCount);

        SessionActionAffordance takeMajor = result.Affordances.Single(affordance => affordance.ActionKey == "take-major-action");
        Assert.AreEqual(SessionActionAffordanceStates.Unavailable, takeMajor.State);
        Assert.AreEqual("session.action-budget.reason.available-on-turn", takeMajor.UnavailableReasonKey);
    }

    [TestMethod]
    public void Compute_clears_held_converted_major_before_turn_and_emits_diagnostic()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-4",
            RoundRef: "round-1",
            RulesetId: "sr6",
            InitiativeDice: 3,
            IsOwnTurnActive: false,
            HeldConvertedMajorCount: 1));

        Assert.AreEqual(0, result.Conversions.HeldConvertedMajorCount);
        Assert.AreEqual(1, result.Major.Available);
        Assert.IsNotNull(result.Diagnostics);
        Assert.AreEqual("session.action-budget.converted-major.cleared-before-turn", result.Diagnostics![0].Code);
    }
}
