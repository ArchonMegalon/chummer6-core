using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Pure bounded proposals: no workspace, provider, owner lease or save capability.</summary>
public static class CharacterCreationMagicCompletionPlanner
{
    public const int MaximumEvaluations = 256;
    public const int MaximumCandidatesPerKind = 128;
    private const int MaximumManualSelections = 256;

    public static CharacterCreationFoundationResult<CharacterCreationMagicCompletionPlan> Plan(
        CharacterCreationMagicResonanceCompletionInputs inputs,
        CharacterCreationQualitiesCompletionInputs qualitiesInputs,
        IReadOnlyList<CharacterCreationAttributeAllocation> attributeAllocations,
        IReadOnlyList<string> qualityOptionIds,
        CharacterCreationMagicCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(qualitiesInputs);
        ArgumentNullException.ThrowIfNull(attributeAllocations);
        ArgumentNullException.ThrowIfNull(qualityOptionIds);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ValidShape(request) || attributeAllocations.Count > 32 || attributeAllocations.Any(item => item is null)
            || qualityOptionIds.Count > 65_536 || qualityOptionIds.Any(item => item is null))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationMagicCompletionBlockers.RequestInvalid);
        inputs = Detach(inputs);
        qualitiesInputs = Detach(qualitiesInputs);
        request = Detach(request);
        attributeAllocations = attributeAllocations.ToArray();
        qualityOptionIds = qualityOptionIds.ToArray();
        if (!SameDigest(inputs.SnapshotDigest, request.MagicSnapshotDigest)
            || !SameDigest(qualitiesInputs.SnapshotDigest, request.QualitiesSnapshotDigest))
            return Reject(CharacterCreationFoundationOutcomes.Conflict, CharacterCreationMagicCompletionBlockers.SnapshotConflict);

        int evaluations = 1;
        var first = CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributeAllocations,
            request.ManualSelections, cancellationToken);
        if (first.Value is not { } current)
            return new(first.Outcome, null, first.Blockers);
        if (!Retains(inputs.Selections, request.ManualSelections))
            return Reject(CharacterCreationFoundationOutcomes.Blocked, CharacterCreationMagicCompletionBlockers.ManualSelectionConflict);
        if (!Admitted(request.RankedTraditions, inputs.Authority.Traditions)
            || !Admitted(request.RankedStreams, inputs.Authority.Streams)
            || !Admitted(request.RankedSpells, inputs.Authority.Spells)
            || !Admitted(request.RankedComplexForms, inputs.Authority.ComplexForms)
            || !Admitted(request.RankedAdeptPowers, inputs.Authority.AdeptPowers))
            return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationMagicCompletionBlockers.CandidateInvalid);

        var original = current.Selections;
        evaluations++;
        var budgetResult = CharacterCreationCompletionBudgetService.Preview(inputs, qualitiesInputs,
            attributeAllocations, current.Selections, qualityOptionIds, cancellationToken);
        if (budgetResult.Outcome == CharacterCreationFoundationOutcomes.Conflict)
            return new(budgetResult.Outcome, null, budgetResult.Blockers);
        var findings = new List<string>();
        bool limitReached = false;
        bool fillable = current.Blockers.All(Incomplete);

        // Explicit desired PP is evaluated before spells: the source policy may
        // exchange spell slots, so never fill against the raw talent's budget.
        if (fillable && request.DesiredTotalMysticAdeptPowerPoints is int total
            && total != current.Selections.MysticAdeptPowerPoints)
        {
            if (total < current.Selections.MysticAdeptPowerPoints)
                findings.Add(CharacterCreationMagicCompletionBlockers.ManualSelectionConflict);
            else if (budgetResult.Value?.AvailableKarmaBeforeResources is null)
                findings.Add(CharacterCreationMagicCompletionBlockers.KarmaReserveUnavailable);
            else if (evaluations + 2 >= request.EvaluationLimit)
                limitReached = true;
            else
            {
                var candidate = Evaluate(current.Selections with { MysticAdeptPowerPoints = total });
                evaluations++;
                var paid = CharacterCreationCompletionBudgetService.Preview(inputs, qualitiesInputs,
                    attributeAllocations, candidate?.Selections ?? current.Selections with { MysticAdeptPowerPoints = total },
                    qualityOptionIds, cancellationToken);
                int? oldCost = current.MysticAdeptPowerPoints?.KarmaCost ?? 0;
                int? newCost = candidate?.MysticAdeptPowerPoints?.KarmaCost;
                if (candidate is not null && candidate.Blockers.All(Incomplete)
                    && oldCost is int before && newCost is int after && after >= before
                    && after - before <= request.MaximumAdditionalPowerPointKarmaSpend
                    && paid.Value?.AvailableKarmaBeforeResources is int remaining && remaining >= request.KarmaToReserve)
                {
                    current = candidate;
                    budgetResult = paid;
                }
                else
                {
                    findings.Add(CharacterCreationMagicCompletionBlockers.PowerPointSpendNotAllowed);
                    if (candidate is not null) findings.AddRange(candidate.Blockers.Where(item => !Incomplete(item)));
                    findings.AddRange(paid.Blockers.Where(item => !Incomplete(item)));
                }
            }
        }

        if (fillable)
        {
            FillSingle(request.RankedTraditions, true);
            FillSingle(request.RankedStreams, false);
            FillFlat(request.RankedSpells, true);
            FillFlat(request.RankedComplexForms, false);
            var manualPowers = request.ManualSelections.AdeptPowers.Select(item => item.Identity).ToHashSet();
            foreach (var identity in request.RankedAdeptPowers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (current.AdeptPowerPointBudget.Remaining == 0m) break;
                if (manualPowers.Contains(identity)) continue; // Never raise an existing manual power.
                var source = inputs.Authority.AdeptPowers.Single(item => item.Identity == identity);
                if (source.PointCost <= 0m) continue;
                decimal maxAffordable = decimal.Floor(current.AdeptPowerPointBudget.Remaining / source.PointCost);
                int levels = (int)Math.Min(maxAffordable,
                    CharacterCreationAdeptPowerSourceRules.EffectiveMaximumLevels(source, current.EffectiveAttributes.Magic));
                if (levels <= 0) continue;
                if (!Room()) break;
                var candidate = Evaluate(current.Selections with
                {
                    AdeptPowers = [.. current.Selections.AdeptPowers, new(identity, levels)]
                });
                if (candidate is not null && candidate.Blockers.All(Incomplete)
                    && candidate.AdeptPowerPointBudget.Used > current.AdeptPowerPointBudget.Used)
                    current = candidate;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        evaluations++;
        budgetResult = CharacterCreationCompletionBudgetService.Preview(inputs, qualitiesInputs,
            attributeAllocations, current.Selections, qualityOptionIds, cancellationToken);
        if (budgetResult.Outcome == CharacterCreationFoundationOutcomes.Conflict)
            return new(budgetResult.Outcome, null, budgetResult.Blockers);
        findings.AddRange(current.Blockers);
        findings.AddRange(budgetResult.Blockers);
        if (request.KarmaToReserve > 0 &&
            (budgetResult.Value?.AvailableKarmaBeforeResources is not int available || available < request.KarmaToReserve))
            findings.Add(CharacterCreationMagicCompletionBlockers.KarmaReserveUnavailable);
        if (limitReached) findings.Add(CharacterCreationMagicCompletionBlockers.WorkLimitReached);
        else if (fillable && current.Blockers.Any(Incomplete))
            findings.Add(CharacterCreationMagicCompletionBlockers.CandidateOrderExhausted);
        string[] blockers = Normalize(findings);
        var plan = new CharacterCreationMagicCompletionPlan(request, attributeAllocations, qualityOptionIds,
            current.Selections, current, budgetResult.Value,
            !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(original, current.Selections),
            current.Blockers.Count == 0, evaluations, blockers, string.Empty);
        plan = plan with { PlanDigest = CharacterCreationMagicCompletionRules.ComputePlanDigest(plan) };
        cancellationToken.ThrowIfCancellationRequested();
        return new(blockers.Length == 0 ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
            Detach(plan), blockers);

        bool Room()
        {
            if (evaluations < request.EvaluationLimit - 1) return true; // One final budget check retained.
            limitReached = true;
            return false;
        }

        CharacterCreationMagicResonanceCompletionProjection? Evaluate(CharacterCreationMagicResonanceSelections choices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations++;
            return CharacterCreationMagicResonanceService.PreviewCompletion(inputs, attributeAllocations,
                choices, cancellationToken).Value;
        }

        void FillSingle(IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> identities, bool tradition)
        {
            if (tradition ? current.TraditionBudget.Remaining == 0m : current.StreamBudget.Remaining == 0m) return;
            foreach (var identity in identities)
            {
                if (!Room()) return;
                var candidate = Evaluate(tradition ? current.Selections with { Tradition = identity }
                    : current.Selections with { Stream = identity });
                if (candidate is null || !candidate.Blockers.All(Incomplete)) continue;
                current = candidate;
                return;
            }
        }

        void FillFlat(IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> identities, bool spells)
        {
            foreach (var identity in identities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (spells ? current.SpellBudget.Remaining == 0m : current.ComplexFormBudget.Remaining == 0m) return;
                if ((spells ? current.Selections.Spells : current.Selections.ComplexForms).Contains(identity)) continue;
                if (!Room()) return;
                var candidate = Evaluate(spells
                    ? current.Selections with { Spells = [.. current.Selections.Spells, identity] }
                    : current.Selections with { ComplexForms = [.. current.Selections.ComplexForms, identity] });
                if (candidate is not null && candidate.Blockers.All(Incomplete)) current = candidate;
            }
        }
    }

    private static bool Incomplete(string blocker) => blocker is
        CharacterCreationMagicResonanceBlockers.TraditionRequired or CharacterCreationMagicResonanceBlockers.StreamRequired
        or CharacterCreationMagicResonanceBlockers.PowerBudgetIncomplete or CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete
        or CharacterCreationMagicResonanceBlockers.ComplexFormBudgetIncomplete;

    private static bool Admitted(IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> identities,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> options)
        => identities.All(identity => options.Count(option => option.Identity == identity && option.IsEnabled
            && option.Blockers.Count == 0) == 1);

    private static bool Retains(CharacterCreationMagicResonanceSelections original, CharacterCreationMagicResonanceSelections proposed)
        => (original.Tradition is null || original.Tradition == proposed.Tradition)
            && (original.Stream is null || original.Stream == proposed.Stream)
            && original.MysticAdeptPowerPoints <= proposed.MysticAdeptPowerPoints
            && original.AdeptPowers.All(item => proposed.AdeptPowers.Count(other => item == other) == 1)
            && original.Spells.All(item => proposed.Spells.Count(other => item == other) == 1)
            && original.ComplexForms.All(item => proposed.ComplexForms.Count(other => item == other) == 1);

    private static bool ValidShape(CharacterCreationMagicCompletionRequest request)
        => request.MaximumAdditionalPowerPointKarmaSpend >= 0 && request.KarmaToReserve >= 0
            && request.DesiredTotalMysticAdeptPowerPoints is null or >= 0
            && request.EvaluationLimit is >= 3 and <= MaximumEvaluations
            && request.ManualSelections is { AdeptPowers: not null, Spells: not null, ComplexForms: not null } manual
            && manual.AdeptPowers.Count <= MaximumManualSelections && manual.Spells.Count <= MaximumManualSelections
            && manual.ComplexForms.Count <= MaximumManualSelections
            && manual.AdeptPowers.All(item => item is not null && item.Identity is not null)
            && manual.Spells.All(item => item is not null) && manual.ComplexForms.All(item => item is not null)
            && ValidOrder(request.RankedTraditions) && ValidOrder(request.RankedStreams)
            && ValidOrder(request.RankedSpells) && ValidOrder(request.RankedComplexForms) && ValidOrder(request.RankedAdeptPowers);

    private static bool ValidOrder(IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity>? identities)
        => identities is not null && identities.Count <= MaximumCandidatesPerKind
            && identities.All(item => item is not null && !string.IsNullOrWhiteSpace(item.Kind)
                && !string.IsNullOrWhiteSpace(item.SourceId)) && identities.Distinct().Count() == identities.Count;
    private static bool SameDigest(string left, string right) => CharacterCreationMagicResonanceDigest.EqualsFixedTime(left, right);
    private static T Detach<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    private static string[] Normalize(IEnumerable<string> blockers) => blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    private static CharacterCreationFoundationResult<CharacterCreationMagicCompletionPlan> Reject(string outcome, string blocker)
        => new(outcome, null, [blocker]);
}
