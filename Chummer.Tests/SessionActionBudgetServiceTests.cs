#nullable enable annotations

using System;
using System.Linq;
using Chummer.Application.Session;
using Chummer.Contracts.Rulesets;
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
        Assert.IsNotNull(result.DeterministicReceipt);
        Assert.AreEqual("governed", result.DeterministicReceipt!.ActionBudgetPosture);
        Assert.AreEqual(100, result.DeterministicReceipt.CoveragePercent);
        CollectionAssert.AreEqual(
            new[]
            {
                "workflow:initiative",
                "workflow:actions",
                "workflow:turn-ledger",
                "workflow:rules-reference"
            },
            result.DeterministicReceipt.CoveredWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), result.DeterministicReceipt.MissingWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "anytime:full-defense", "on-turn:take-major-action", "on-turn:take-minor-action" },
            result.DeterministicReceipt.AffordanceKeys.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "sr6_core_anytime_major_conversion",
                "sr6_core_full_defense",
                "sr6_core_major_actions",
                "sr6_core_minor_actions"
            },
            result.DeterministicReceipt.ReceiptSourceAnchors.ToArray());
        CollectionAssert.AreEqual(
            new[]
            {
                "turn-ledger-anytime-full-defense",
                "turn-ledger-on-turn-convert-four-minor-to-anytime-major",
                "turn-ledger-on-turn-take-major-action",
                "turn-ledger-on-turn-take-minor-action"
            },
            result.DeterministicReceipt.TurnLedgerDeltaIds.ToArray());
        Assert.AreEqual(4, result.DeterministicReceipt.SourceAnchorReceiptCount);
        Assert.AreEqual(0, result.DeterministicReceipt.MissingSourceAnchorReceiptCount);
        Assert.AreEqual(4, result.TurnLedger.Count);
        Assert.IsTrue(result.Receipts.All(receipt => receipt.SourceAnchor is not null));
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
        SessionTurnLedgerDelta fullDefenseDelta = result.TurnLedger.Single(delta => delta.ActionKey == "full-defense");
        SessionTurnLedgerDelta conversionDelta = result.TurnLedger.Single(delta => delta.ActionKey == "convert-four-minor-to-anytime-major");

        Assert.AreEqual(SessionActionAffordanceStates.Available, fullDefense.State);
        Assert.AreEqual(SessionActionBudgetTimingModes.Anytime, fullDefense.Timing);
        Assert.AreEqual(4, fullDefense.Cost.Minor);
        Assert.AreEqual(1, result.Conversions.ConvertibleAnytimeMajorCount);
        Assert.AreEqual(SessionTurnLedgerDeltaStates.Previewable, fullDefenseDelta.State);
        Assert.AreEqual(1, fullDefenseDelta.MajorAvailableAfter);
        Assert.AreEqual(0, fullDefenseDelta.MinorAvailableAfter);
        CollectionAssert.AreEqual(
            new[] { "sr6_core_full_defense", "sr6_core_minor_actions" },
            fullDefenseDelta.ReceiptSourceAnchorRefs.ToArray());
        Assert.AreEqual(SessionTurnLedgerDeltaStates.Previewable, conversionDelta.State);
        Assert.AreEqual(2, conversionDelta.MajorAvailableAfter);
        Assert.AreEqual(0, conversionDelta.MinorAvailableAfter);
        CollectionAssert.AreEqual(
            new[] { "sr6_core_anytime_major_conversion", "sr6_core_minor_actions" },
            conversionDelta.ReceiptSourceAnchorRefs.ToArray());
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
        SessionTurnLedgerDelta conversionDelta = result.TurnLedger.Single(delta => delta.ActionKey == "convert-four-minor-to-anytime-major");
        Assert.AreEqual(SessionActionAffordanceStates.Unavailable, takeMajor.State);
        Assert.AreEqual("session.action-budget.reason.available-on-turn", takeMajor.UnavailableReasonKey);
        Assert.AreEqual(SessionTurnLedgerDeltaStates.Blocked, conversionDelta.State);
        Assert.AreEqual("session.action-budget.reason.insufficient-budget", conversionDelta.UnavailableReasonKey);
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

    [TestMethod]
    public void Compute_downgrades_deterministic_receipt_when_custom_receipts_omit_source_anchor_objects()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-5",
            RoundRef: "round-2",
            RulesetId: "sr6",
            InitiativeDice: 3,
            Receipts:
            [
                new SessionActionBudgetReceipt(
                    SourceAnchorRef: "sr6_core_major_actions",
                    SummaryKey: "session.action-budget.receipt.sr6.major-actions"),
                new SessionActionBudgetReceipt(
                    SourceAnchorRef: "sr6_core_minor_actions",
                    SummaryKey: "session.action-budget.receipt.sr6.minor-actions")
            ]));

        SessionTurnLedgerDelta takeMajorDelta = result.TurnLedger.Single(delta => delta.ActionKey == "take-major-action");
        SessionTurnLedgerDelta takeMinorDelta = result.TurnLedger.Single(delta => delta.ActionKey == "take-minor-action");
        SessionTurnLedgerDelta fullDefenseDelta = result.TurnLedger.Single(delta => delta.ActionKey == "full-defense");
        SessionTurnLedgerDelta conversionDelta = result.TurnLedger.Single(delta => delta.ActionKey == "convert-four-minor-to-anytime-major");

        Assert.IsNotNull(result.DeterministicReceipt);
        Assert.AreEqual("stale", result.DeterministicReceipt!.ActionBudgetPosture);
        Assert.AreEqual(100, result.DeterministicReceipt.CoveragePercent);
        Assert.AreEqual(0, result.DeterministicReceipt.SourceAnchorReceiptCount);
        Assert.AreEqual(2, result.DeterministicReceipt.MissingSourceAnchorReceiptCount);
        CollectionAssert.AreEqual(
            new[]
            {
                "workflow:initiative",
                "workflow:actions",
                "workflow:turn-ledger",
                "workflow:rules-reference"
            },
            result.DeterministicReceipt.CoveredWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), result.DeterministicReceipt.MissingWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), takeMajorDelta.ReceiptSourceAnchorRefs.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), takeMinorDelta.ReceiptSourceAnchorRefs.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), fullDefenseDelta.ReceiptSourceAnchorRefs.ToArray());
        CollectionAssert.AreEqual(Array.Empty<string>(), conversionDelta.ReceiptSourceAnchorRefs.ToArray());
    }

    [TestMethod]
    public void Compute_downgrades_deterministic_receipt_when_required_turn_ledger_source_anchor_refs_are_missing()
    {
        DefaultSessionActionBudgetService service = new();

        SessionActionBudgetResult result = service.Compute(new SessionActionBudgetInput(
            ActorRef: "runner-6",
            RoundRef: "round-3",
            RulesetId: "sr6",
            InitiativeDice: 3,
            Receipts:
            [
                new SessionActionBudgetReceipt(
                    SourceAnchorRef: "sr6_core_major_actions",
                    SummaryKey: "session.action-budget.receipt.sr6.major-actions",
                    SourceAnchor: new SourceAnchor(
                        Id: "sr6_core_major_actions",
                        RulesetId: "sr6",
                        SourcePackRef: "sr6-core",
                        Locale: "en-US",
                        Page: 41,
                        SectionHint: "Major Actions",
                        AnchorKey: "sr6_core_major_actions")),
                new SessionActionBudgetReceipt(
                    SourceAnchorRef: "sr6_core_minor_actions",
                    SummaryKey: "session.action-budget.receipt.sr6.minor-actions",
                    SourceAnchor: new SourceAnchor(
                        Id: "sr6_core_minor_actions",
                        RulesetId: "sr6",
                        SourcePackRef: "sr6-core",
                        Locale: "en-US",
                        Page: 42,
                        SectionHint: "Minor Actions",
                        AnchorKey: "sr6_core_minor_actions"))
            ]));

        SessionTurnLedgerDelta fullDefenseDelta = result.TurnLedger.Single(delta => delta.ActionKey == "full-defense");
        SessionTurnLedgerDelta conversionDelta = result.TurnLedger.Single(delta => delta.ActionKey == "convert-four-minor-to-anytime-major");

        Assert.IsNotNull(result.DeterministicReceipt);
        Assert.AreEqual("stale", result.DeterministicReceipt!.ActionBudgetPosture);
        Assert.AreEqual(100, result.DeterministicReceipt.CoveragePercent);
        Assert.AreEqual(2, result.DeterministicReceipt.SourceAnchorReceiptCount);
        Assert.AreEqual(0, result.DeterministicReceipt.MissingSourceAnchorReceiptCount);
        CollectionAssert.AreEqual(Array.Empty<string>(), result.DeterministicReceipt.MissingWorkflowRouteIds.ToArray());
        CollectionAssert.AreEqual(
            new[] { "sr6_core_minor_actions" },
            fullDefenseDelta.ReceiptSourceAnchorRefs.ToArray());
        CollectionAssert.AreEqual(
            new[] { "sr6_core_minor_actions" },
            conversionDelta.ReceiptSourceAnchorRefs.ToArray());
    }
}
