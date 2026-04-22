using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed class DefaultSessionActionBudgetService : ISessionActionBudgetService
{
    public SessionActionBudgetResult Compute(SessionActionBudgetInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        string actorRef = RequireTrimmed(input.ActorRef, nameof(input.ActorRef));
        string roundRef = RequireTrimmed(input.RoundRef, nameof(input.RoundRef));
        string rulesetId = RequireTrimmed(input.RulesetId, nameof(input.RulesetId));
        int initiativeDice = Math.Max(0, input.InitiativeDice);
        int majorSpent = Math.Max(0, input.MajorSpent);
        int minorSpent = Math.Max(0, input.MinorSpent);
        int computedMinor = 1 + initiativeDice;
        int effectiveMinorCap = input.MinorTurnStartCap > 0 ? input.MinorTurnStartCap : computedMinor;
        int startingMinor = Math.Min(computedMinor, effectiveMinorCap);
        bool isOwnTurnActive = input.IsOwnTurnActive;

        List<RulesetCapabilityDiagnostic> diagnostics = [];
        int heldConvertedMajorCount = Math.Max(0, input.HeldConvertedMajorCount);
        if (!isOwnTurnActive && heldConvertedMajorCount > 0)
        {
            diagnostics.Add(new RulesetCapabilityDiagnostic(
                Code: "session.action-budget.converted-major.cleared-before-turn",
                Message: "Converted major actions cannot be held before the actor's turn.",
                Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                MessageKey: "session.action-budget.converted-major.cleared-before-turn",
                MessageParameters:
                [
                    Param("actorRef", actorRef),
                    Param("roundRef", roundRef),
                    Param("clearedHeldConvertedMajorCount", heldConvertedMajorCount)
                ]));
            heldConvertedMajorCount = 0;
        }

        int availableMinor = Math.Max(0, startingMinor - minorSpent);
        int availableMajor = Math.Max(0, 1 + heldConvertedMajorCount - majorSpent);
        int convertibleAnytimeMajorCount = input.CanSpendFourMinorForAnytimeMajor && isOwnTurnActive
            ? availableMinor / 4
            : 0;

        SessionActionBudgetBucket major = new(
            Base: 1,
            Available: availableMajor,
            Spent: majorSpent);
        SessionActionBudgetBucket minor = new(
            Base: startingMinor,
            Available: availableMinor,
            Spent: minorSpent,
            Computed: computedMinor,
            TurnStartCap: effectiveMinorCap);
        SessionActionBudgetConversionState conversions = new(
            CanSpendFourMinorForAnytimeMajor: input.CanSpendFourMinorForAnytimeMajor,
            CanHoldConvertedMajorBeforeTurn: input.CanHoldConvertedMajorBeforeTurn,
            ConvertibleAnytimeMajorCount: convertibleAnytimeMajorCount,
            HeldConvertedMajorCount: heldConvertedMajorCount);

        SessionActionAffordanceTemplate[] templates = NormalizeAffordances(input.Affordances, rulesetId);
        SessionActionAffordance[] affordances = templates
            .Select(template => EvaluateAffordance(template, isOwnTurnActive, major, minor))
            .ToArray();
        SessionActionBudgetReceipt[] receipts = NormalizeReceipts(input.Receipts, rulesetId);

        return new SessionActionBudgetResult(
            ActorRef: actorRef,
            RoundRef: roundRef,
            RulesetId: rulesetId,
            InitiativeDice: initiativeDice,
            Major: major,
            Minor: minor,
            Conversions: conversions,
            Affordances: affordances,
            Receipts: receipts,
            Diagnostics: diagnostics,
            ExplainEntryId: string.IsNullOrWhiteSpace(input.ExplainEntryId) ? $"{actorRef}:{roundRef}:action-budget" : input.ExplainEntryId.Trim());
    }

    private static SessionActionAffordance EvaluateAffordance(
        SessionActionAffordanceTemplate template,
        bool isOwnTurnActive,
        SessionActionBudgetBucket major,
        SessionActionBudgetBucket minor)
    {
        string timing = NormalizeTiming(template.Timing);
        SessionActionBudgetCost cost = NormalizeCost(template.Cost);
        bool timingAllowed = timing switch
        {
            SessionActionBudgetTimingModes.OnTurn => isOwnTurnActive,
            SessionActionBudgetTimingModes.BetweenTurns => !isOwnTurnActive,
            _ => true
        };

        bool budgetAllowed = major.Available >= cost.Major && minor.Available >= cost.Minor;
        string? unavailableReasonKey = null;
        IReadOnlyList<RulesetExplainParameter>? unavailableReasonParameters = null;

        if (!timingAllowed)
        {
            unavailableReasonKey = isOwnTurnActive
                ? "session.action-budget.reason.available-between-turns"
                : "session.action-budget.reason.available-on-turn";
            unavailableReasonParameters =
            [
                Param("actionKey", template.ActionKey),
                Param("timing", timing)
            ];
        }
        else if (!budgetAllowed)
        {
            unavailableReasonKey = "session.action-budget.reason.insufficient-budget";
            unavailableReasonParameters =
            [
                Param("actionKey", template.ActionKey),
                Param("requiredMajor", cost.Major),
                Param("requiredMinor", cost.Minor),
                Param("availableMajor", major.Available),
                Param("availableMinor", minor.Available)
            ];
        }

        return new SessionActionAffordance(
            ActionKey: template.ActionKey.Trim(),
            Timing: timing,
            Cost: cost,
            State: timingAllowed && budgetAllowed ? SessionActionAffordanceStates.Available : SessionActionAffordanceStates.Unavailable,
            SummaryKey: string.IsNullOrWhiteSpace(template.SummaryKey) ? null : template.SummaryKey.Trim(),
            SummaryParameters: template.SummaryParameters ?? [],
            UnavailableReasonKey: unavailableReasonKey,
            UnavailableReasonParameters: unavailableReasonParameters,
            ExplainEntryId: string.IsNullOrWhiteSpace(template.ExplainEntryId) ? null : template.ExplainEntryId.Trim());
    }

    private static SessionActionAffordanceTemplate[] NormalizeAffordances(
        IReadOnlyList<SessionActionAffordanceTemplate>? affordances,
        string rulesetId)
    {
        SessionActionAffordanceTemplate[] normalized = (affordances ?? GetDefaultAffordances(rulesetId))
            .Where(static template => !string.IsNullOrWhiteSpace(template.ActionKey))
            .Select(template => template with
            {
                ActionKey = template.ActionKey.Trim(),
                Timing = NormalizeTiming(template.Timing),
                Cost = NormalizeCost(template.Cost),
                SummaryKey = string.IsNullOrWhiteSpace(template.SummaryKey) ? null : template.SummaryKey.Trim(),
                SummaryParameters = template.SummaryParameters ?? [],
                ExplainEntryId = string.IsNullOrWhiteSpace(template.ExplainEntryId) ? null : template.ExplainEntryId.Trim()
            })
            .GroupBy(static template => $"{template.ActionKey}|{template.Timing}", StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static template => template.Timing, StringComparer.Ordinal)
            .ThenBy(static template => template.ActionKey, StringComparer.Ordinal)
            .ToArray();

        return normalized;
    }

    private static SessionActionBudgetReceipt[] NormalizeReceipts(
        IReadOnlyList<SessionActionBudgetReceipt>? receipts,
        string rulesetId)
    {
        SessionActionBudgetReceipt[] seed = receipts?.Count > 0
            ? receipts
                .Where(static receipt => !string.IsNullOrWhiteSpace(receipt.SourceAnchorRef))
                .Select(receipt => receipt with
                {
                    SourceAnchorRef = receipt.SourceAnchorRef.Trim(),
                    SummaryKey = string.IsNullOrWhiteSpace(receipt.SummaryKey) ? "session.action-budget.receipt" : receipt.SummaryKey.Trim(),
                    SummaryParameters = receipt.SummaryParameters ?? []
                })
                .ToArray()
            : GetDefaultReceipts(rulesetId);

        return seed
            .GroupBy(static receipt => receipt.SourceAnchorRef, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static receipt => receipt.SourceAnchorRef, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<SessionActionAffordanceTemplate> GetDefaultAffordances(string rulesetId)
    {
        if (!string.Equals(rulesetId, "sr6", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return
        [
            new(
                ActionKey: "take-major-action",
                Timing: SessionActionBudgetTimingModes.OnTurn,
                Cost: new SessionActionBudgetCost(Major: 1),
                SummaryKey: "session.action-budget.affordance.take-major-action"),
            new(
                ActionKey: "take-minor-action",
                Timing: SessionActionBudgetTimingModes.OnTurn,
                Cost: new SessionActionBudgetCost(Minor: 1),
                SummaryKey: "session.action-budget.affordance.take-minor-action"),
            new(
                ActionKey: "full-defense",
                Timing: SessionActionBudgetTimingModes.Anytime,
                Cost: new SessionActionBudgetCost(Minor: 4),
                SummaryKey: "session.action-budget.affordance.full-defense")
        ];
    }

    private static SessionActionBudgetReceipt[] GetDefaultReceipts(string rulesetId)
    {
        if (!string.Equals(rulesetId, "sr6", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return
        [
            new(
                SourceAnchorRef: "sr6_core_minor_actions",
                SummaryKey: "session.action-budget.receipt.sr6.minor-actions"),
            new(
                SourceAnchorRef: "sr6_core_anytime_major_conversion",
                SummaryKey: "session.action-budget.receipt.sr6.anytime-major-conversion")
        ];
    }

    private static SessionActionBudgetCost NormalizeCost(SessionActionBudgetCost cost)
        => new(
            Major: Math.Max(0, cost.Major),
            Minor: Math.Max(0, cost.Minor));

    private static string NormalizeTiming(string timing)
        => string.IsNullOrWhiteSpace(timing)
            ? SessionActionBudgetTimingModes.Anytime
            : timing.Trim().ToLowerInvariant();

    private static string RequireTrimmed(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value.Trim();
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));
}
