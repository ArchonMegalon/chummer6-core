using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;

namespace Chummer.Application.Session;

public sealed class DefaultSessionActionBudgetService : ISessionActionBudgetService
{
    private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";
    private static readonly string[] CanonicalWorkflowRouteIds =
    [
        "workflow:initiative",
        "workflow:actions",
        "workflow:turn-ledger",
        "workflow:rules-reference"
    ];

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
        SessionTurnLedgerDelta[] turnLedger = BuildTurnLedger(
            rulesetId,
            isOwnTurnActive,
            input.CanSpendFourMinorForAnytimeMajor,
            major,
            minor,
            conversions,
            affordances,
            receipts);
        string explainEntryId = string.IsNullOrWhiteSpace(input.ExplainEntryId)
            ? $"{actorRef}:{roundRef}:action-budget"
            : input.ExplainEntryId.Trim();

        return new SessionActionBudgetResult(
            ActorRef: actorRef,
            RoundRef: roundRef,
            RulesetId: rulesetId,
            InitiativeDice: initiativeDice,
            Major: major,
            Minor: minor,
            Conversions: conversions,
            Affordances: affordances,
            TurnLedger: turnLedger,
            Receipts: receipts,
            Diagnostics: diagnostics,
            ExplainEntryId: explainEntryId,
            DeterministicReceipt: BuildDeterministicReceipt(
                actorRef,
                roundRef,
                rulesetId,
                initiativeDice,
                major,
                minor,
                conversions,
                affordances,
                turnLedger,
                receipts,
                explainEntryId));
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
                    SummaryParameters = receipt.SummaryParameters ?? [],
                    SourceAnchor = NormalizeSourceAnchor(receipt.SourceAnchor)
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
                SourceAnchorRef: "sr6_core_major_actions",
                SummaryKey: "session.action-budget.receipt.sr6.major-actions",
                SourceAnchor: BuildDefaultSourceAnchor(
                    rulesetId,
                    sourceAnchorRef: "sr6_core_major_actions",
                    page: 41,
                    sectionHint: "Major Actions")),
            new(
                SourceAnchorRef: "sr6_core_minor_actions",
                SummaryKey: "session.action-budget.receipt.sr6.minor-actions",
                SourceAnchor: BuildDefaultSourceAnchor(
                    rulesetId,
                    sourceAnchorRef: "sr6_core_minor_actions",
                    page: 42,
                    sectionHint: "Minor Actions")),
            new(
                SourceAnchorRef: "sr6_core_full_defense",
                SummaryKey: "session.action-budget.receipt.sr6.full-defense",
                SourceAnchor: BuildDefaultSourceAnchor(
                    rulesetId,
                    sourceAnchorRef: "sr6_core_full_defense",
                    page: 44,
                    sectionHint: "Full Defense")),
            new(
                SourceAnchorRef: "sr6_core_anytime_major_conversion",
                SummaryKey: "session.action-budget.receipt.sr6.anytime-major-conversion",
                SourceAnchor: BuildDefaultSourceAnchor(
                    rulesetId,
                    sourceAnchorRef: "sr6_core_anytime_major_conversion",
                    page: 45,
                    sectionHint: "Anytime Major Conversion"))
        ];
    }

    private static SessionTurnLedgerDelta[] BuildTurnLedger(
        string rulesetId,
        bool isOwnTurnActive,
        bool canSpendFourMinorForAnytimeMajor,
        SessionActionBudgetBucket major,
        SessionActionBudgetBucket minor,
        SessionActionBudgetConversionState conversions,
        IReadOnlyList<SessionActionAffordance> affordances,
        IReadOnlyList<SessionActionBudgetReceipt> receipts)
    {
        List<SessionTurnLedgerDelta> deltas = affordances
            .Select(affordance => BuildAffordanceDelta(affordance, isOwnTurnActive, canSpendFourMinorForAnytimeMajor, major, minor, receipts))
            .ToList();

        SessionTurnLedgerDelta? conversionDelta = BuildConversionDelta(
            rulesetId,
            isOwnTurnActive,
            canSpendFourMinorForAnytimeMajor,
            major,
            minor,
            conversions,
            receipts);
        if (conversionDelta is not null)
        {
            deltas.Add(conversionDelta);
        }

        return deltas
            .OrderBy(static delta => delta.Timing, StringComparer.Ordinal)
            .ThenBy(static delta => delta.ActionKey, StringComparer.Ordinal)
            .ToArray();
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

    private static SessionActionBudgetDeterministicReceipt BuildDeterministicReceipt(
        string actorRef,
        string roundRef,
        string rulesetId,
        int initiativeDice,
        SessionActionBudgetBucket major,
        SessionActionBudgetBucket minor,
        SessionActionBudgetConversionState conversions,
        IReadOnlyList<SessionActionAffordance> affordances,
        IReadOnlyList<SessionTurnLedgerDelta> turnLedger,
        IReadOnlyList<SessionActionBudgetReceipt> receipts,
        string explainEntryId)
    {
        string[] affordanceKeys = affordances
            .Select(static affordance => $"{affordance.Timing}:{affordance.ActionKey}")
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
        string[] turnLedgerDeltaIds = turnLedger
            .Select(static delta => delta.DeltaId)
            .OrderBy(static deltaId => deltaId, StringComparer.Ordinal)
            .ToArray();
        string[] receiptSourceAnchors = receipts
            .Select(static receipt => receipt.SourceAnchorRef)
            .OrderBy(static anchor => anchor, StringComparer.Ordinal)
            .ToArray();
        int sourceAnchorReceiptCount = receipts.Count(static receipt => receipt.SourceAnchor is not null);
        int missingSourceAnchorReceiptCount = receipts.Count - sourceAnchorReceiptCount;
        int missingTurnLedgerReceiptSourceAnchorCount = turnLedger.Count(static delta => HasMissingRequiredReceiptSourceAnchors(delta));
        bool hasActionSurface = affordanceKeys.Length > 0;
        bool hasTurnLedgerSurface = turnLedgerDeltaIds.Length > 0;
        bool hasRulesReferenceSurface = receiptSourceAnchors.Length > 0;
        string[] coveredWorkflowRouteIds = CanonicalWorkflowRouteIds
            .Where(routeId => routeId switch
            {
                "workflow:initiative" => true,
                "workflow:actions" => hasActionSurface,
                "workflow:turn-ledger" => hasTurnLedgerSurface,
                "workflow:rules-reference" => hasRulesReferenceSurface,
                _ => false
            })
            .ToArray();
        string[] missingWorkflowRouteIds = CanonicalWorkflowRouteIds
            .Except(coveredWorkflowRouteIds, StringComparer.Ordinal)
            .ToArray();
        int coveragePercent = (int)Math.Round((double)(coveredWorkflowRouteIds.Length * 100) / CanonicalWorkflowRouteIds.Length, MidpointRounding.AwayFromZero);

        return new SessionActionBudgetDeterministicReceipt(
            ParityFamilyId: DenseWorkbenchParityFamilyId,
            ActionBudgetPosture: ResolveActionBudgetPosture(
                affordanceKeys.Length,
                turnLedgerDeltaIds.Length,
                receiptSourceAnchors.Length,
                missingSourceAnchorReceiptCount,
                missingTurnLedgerReceiptSourceAnchorCount),
            ReceiptId: BuildDeterministicReceiptId(
                rulesetId,
                actorRef,
                roundRef,
                initiativeDice,
                major.Available,
                minor.Available,
                conversions.ConvertibleAnytimeMajorCount,
                conversions.HeldConvertedMajorCount),
            RulesetId: rulesetId,
            ActorRef: actorRef,
            RoundRef: roundRef,
            InitiativeDice: initiativeDice,
            CoveragePercent: coveragePercent,
            MajorAvailable: major.Available,
            MinorAvailable: minor.Available,
            ConvertibleAnytimeMajorCount: conversions.ConvertibleAnytimeMajorCount,
            HeldConvertedMajorCount: conversions.HeldConvertedMajorCount,
            CoveredWorkflowRouteIds: coveredWorkflowRouteIds,
            MissingWorkflowRouteIds: missingWorkflowRouteIds,
            AffordanceKeys: affordanceKeys,
            TurnLedgerDeltaIds: turnLedgerDeltaIds,
            ReceiptSourceAnchors: receiptSourceAnchors,
            SourceAnchorReceiptCount: sourceAnchorReceiptCount,
            MissingSourceAnchorReceiptCount: missingSourceAnchorReceiptCount,
            ExplainEntryId: explainEntryId);
    }

    private static string ResolveActionBudgetPosture(
        int affordanceCount,
        int turnLedgerCount,
        int receiptCount,
        int missingSourceAnchorReceiptCount,
        int missingTurnLedgerReceiptSourceAnchorCount)
    {
        if (affordanceCount <= 0 && turnLedgerCount <= 0 && receiptCount <= 0)
        {
            return "missing";
        }

        return affordanceCount <= 0
            || turnLedgerCount <= 0
            || receiptCount <= 0
            || missingSourceAnchorReceiptCount > 0
            || missingTurnLedgerReceiptSourceAnchorCount > 0
            ? "stale"
            : "governed";
    }

    private static string BuildDeterministicReceiptId(
        string rulesetId,
        string actorRef,
        string roundRef,
        int initiativeDice,
        int majorAvailable,
        int minorAvailable,
        int convertibleAnytimeMajorCount,
        int heldConvertedMajorCount)
    {
        string rulesetToken = string.IsNullOrWhiteSpace(rulesetId) ? "ruleset" : rulesetId.Trim().ToLowerInvariant();
        string actorToken = string.IsNullOrWhiteSpace(actorRef) ? "actor" : actorRef.Trim().ToLowerInvariant();
        string roundToken = string.IsNullOrWhiteSpace(roundRef) ? "round" : roundRef.Trim().ToLowerInvariant();
        return $"action-budget-{rulesetToken}-{actorToken}-{roundToken}-{initiativeDice}-{majorAvailable}-{minorAvailable}-{convertibleAnytimeMajorCount}-{heldConvertedMajorCount}";
    }

    private static SessionTurnLedgerDelta BuildAffordanceDelta(
        SessionActionAffordance affordance,
        bool isOwnTurnActive,
        bool canSpendFourMinorForAnytimeMajor,
        SessionActionBudgetBucket major,
        SessionActionBudgetBucket minor,
        IReadOnlyList<SessionActionBudgetReceipt> receipts)
    {
        bool isPreviewable = string.Equals(affordance.State, SessionActionAffordanceStates.Available, StringComparison.Ordinal);
        int majorDelta = isPreviewable ? -affordance.Cost.Major : 0;
        int minorDelta = isPreviewable ? -affordance.Cost.Minor : 0;
        int majorAvailableAfter = Math.Max(0, major.Available + majorDelta);
        int minorAvailableAfter = Math.Max(0, minor.Available + minorDelta);
        int convertibleAnytimeMajorCountAfter = canSpendFourMinorForAnytimeMajor && isOwnTurnActive
            ? minorAvailableAfter / 4
            : 0;
        string[] receiptSourceAnchorRefs = ResolveReceiptSourceAnchors(affordance.ActionKey, receipts);

        return new SessionTurnLedgerDelta(
            DeltaId: BuildTurnLedgerDeltaId(affordance.ActionKey, affordance.Timing),
            ActionKey: affordance.ActionKey,
            Timing: affordance.Timing,
            Cost: affordance.Cost,
            State: isPreviewable ? SessionTurnLedgerDeltaStates.Previewable : SessionTurnLedgerDeltaStates.Blocked,
            MajorDelta: majorDelta,
            MinorDelta: minorDelta,
            HeldConvertedMajorDelta: 0,
            MajorAvailableAfter: majorAvailableAfter,
            MinorAvailableAfter: minorAvailableAfter,
            ConvertibleAnytimeMajorCountAfter: convertibleAnytimeMajorCountAfter,
            ReceiptSourceAnchorRefs: receiptSourceAnchorRefs,
            SummaryKey: affordance.SummaryKey,
            SummaryParameters: affordance.SummaryParameters ?? [],
            ExplainEntryId: affordance.ExplainEntryId,
            UnavailableReasonKey: affordance.UnavailableReasonKey,
            UnavailableReasonParameters: affordance.UnavailableReasonParameters ?? []);
    }

    private static SessionTurnLedgerDelta? BuildConversionDelta(
        string rulesetId,
        bool isOwnTurnActive,
        bool canSpendFourMinorForAnytimeMajor,
        SessionActionBudgetBucket major,
        SessionActionBudgetBucket minor,
        SessionActionBudgetConversionState conversions,
        IReadOnlyList<SessionActionBudgetReceipt> receipts)
    {
        if (!string.Equals(rulesetId, "sr6", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        bool isPreviewable = isOwnTurnActive && canSpendFourMinorForAnytimeMajor && conversions.ConvertibleAnytimeMajorCount > 0;
        int majorDelta = isPreviewable ? 1 : 0;
        int minorDelta = isPreviewable ? -4 : 0;
        int majorAvailableAfter = Math.Max(0, major.Available + majorDelta);
        int minorAvailableAfter = Math.Max(0, minor.Available + minorDelta);
        int convertibleAnytimeMajorCountAfter = isPreviewable ? Math.Max(0, minorAvailableAfter / 4) : 0;

        return new SessionTurnLedgerDelta(
            DeltaId: BuildTurnLedgerDeltaId("convert-four-minor-to-anytime-major", SessionActionBudgetTimingModes.OnTurn),
            ActionKey: "convert-four-minor-to-anytime-major",
            Timing: SessionActionBudgetTimingModes.OnTurn,
            Cost: new SessionActionBudgetCost(Minor: 4),
            State: isPreviewable ? SessionTurnLedgerDeltaStates.Previewable : SessionTurnLedgerDeltaStates.Blocked,
            MajorDelta: majorDelta,
            MinorDelta: minorDelta,
            HeldConvertedMajorDelta: 0,
            MajorAvailableAfter: majorAvailableAfter,
            MinorAvailableAfter: minorAvailableAfter,
            ConvertibleAnytimeMajorCountAfter: convertibleAnytimeMajorCountAfter,
            ReceiptSourceAnchorRefs: ResolveReceiptSourceAnchors("convert-four-minor-to-anytime-major", receipts),
            SummaryKey: "session.action-budget.turn-ledger.convert-four-minor-to-anytime-major",
            SummaryParameters:
            [
                Param("majorAvailableAfter", majorAvailableAfter),
                Param("minorAvailableAfter", minorAvailableAfter)
            ],
            ExplainEntryId: "session.action-budget.turn-ledger.convert-four-minor-to-anytime-major",
            UnavailableReasonKey: isPreviewable ? null : "session.action-budget.reason.insufficient-budget",
            UnavailableReasonParameters: isPreviewable
                ? []
                : [
                    Param("requiredMinor", 4),
                    Param("availableMinor", minor.Available),
                    Param("isOwnTurnActive", isOwnTurnActive)
                ]);
    }

    private static string[] ResolveReceiptSourceAnchors(string actionKey, IReadOnlyList<SessionActionBudgetReceipt> receipts)
    {
        string[] preferred = GetPreferredReceiptSourceAnchors(actionKey);

        HashSet<string> available = receipts
            .Where(static receipt => receipt.SourceAnchor is not null)
            .Select(static receipt => receipt.SourceAnchorRef)
            .ToHashSet(StringComparer.Ordinal);

        return preferred
            .Where(available.Contains)
            .OrderBy(static anchor => anchor, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool HasMissingRequiredReceiptSourceAnchors(SessionTurnLedgerDelta delta)
    {
        string[] preferred = GetPreferredReceiptSourceAnchors(delta.ActionKey);
        if (preferred.Length == 0)
        {
            return false;
        }

        HashSet<string> actual = delta.ReceiptSourceAnchorRefs.ToHashSet(StringComparer.Ordinal);
        return preferred.Any(anchor => !actual.Contains(anchor));
    }

    private static string[] GetPreferredReceiptSourceAnchors(string actionKey)
        => actionKey switch
        {
            "take-major-action" => ["sr6_core_major_actions"],
            "take-minor-action" => ["sr6_core_minor_actions"],
            "full-defense" => ["sr6_core_full_defense", "sr6_core_minor_actions"],
            "convert-four-minor-to-anytime-major" => ["sr6_core_anytime_major_conversion", "sr6_core_minor_actions"],
            _ => []
        };

    private static string BuildTurnLedgerDeltaId(string actionKey, string timing)
    {
        string normalizedActionKey = string.IsNullOrWhiteSpace(actionKey) ? "action" : actionKey.Trim().ToLowerInvariant();
        string normalizedTiming = string.IsNullOrWhiteSpace(timing) ? SessionActionBudgetTimingModes.Anytime : timing.Trim().ToLowerInvariant();
        return $"turn-ledger-{normalizedTiming}-{normalizedActionKey}";
    }

    private static SourceAnchor? NormalizeSourceAnchor(SourceAnchor? sourceAnchor)
    {
        if (sourceAnchor is null)
        {
            return null;
        }

        return sourceAnchor with
        {
            Id = RequireTrimmed(sourceAnchor.Id, nameof(sourceAnchor.Id)),
            RulesetId = RequireTrimmed(sourceAnchor.RulesetId, nameof(sourceAnchor.RulesetId)),
            SourcePackRef = RequireTrimmed(sourceAnchor.SourcePackRef, nameof(sourceAnchor.SourcePackRef)),
            Locale = RequireTrimmed(sourceAnchor.Locale, nameof(sourceAnchor.Locale)),
            Page = Math.Max(1, sourceAnchor.Page),
            SectionHint = RequireTrimmed(sourceAnchor.SectionHint, nameof(sourceAnchor.SectionHint)),
            AnchorKey = RequireTrimmed(sourceAnchor.AnchorKey, nameof(sourceAnchor.AnchorKey)),
            BindingPolicy = string.IsNullOrWhiteSpace(sourceAnchor.BindingPolicy)
                ? SourceAnchorBindingPolicies.UserLocalFileOnly
                : sourceAnchor.BindingPolicy.Trim()
        };
    }

    private static SourceAnchor BuildDefaultSourceAnchor(
        string rulesetId,
        string sourceAnchorRef,
        int page,
        string sectionHint)
        => new(
            Id: sourceAnchorRef,
            RulesetId: rulesetId,
            SourcePackRef: $"{rulesetId}-core",
            Locale: "en-US",
            Page: page,
            SectionHint: sectionHint,
            AnchorKey: sourceAnchorRef);
}
