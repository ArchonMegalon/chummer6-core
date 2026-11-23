using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    public const int MaximumBaselineCandidateEvaluations = 16;

    /// <summary>
    /// Compose a deterministic feasible baseline under explicit permissions, then
    /// independently run the canonical complete review and finalization projector.
    /// No partial plan escapes this entry point. This does not write, spend provider
    /// credits, establish live ownership, or automatically enter Career.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationCompletionBaseline> PlanCompleteBaseline(
        CharacterCreationCompletionReviewInputs inputs,
        CharacterCreationCompletionBaselineRequest request,
        WorkspaceStoredDocument original,
        CharacterCreationCompletionBootstrapSnapshot bootstrap,
        CharacterCreationStartingNuyenSource startingCashSource,
        CharacterCreationStartingCashChoice? cashChoice,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null,
        CancellationToken cancellationToken = default,
        CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentNullException.ThrowIfNull(startingCashSource);
        var result = PlanBaselineCore(inputs, request, original, bootstrap, startingCashSource,
            cashChoice, ruleSources, cancellationToken, campaign, null);
        return new(result.Outcome, result.Value?.Complete, result.Blockers);
    }

    /// <summary>Runs the same bounded candidate search up to the player cash decision.
    /// No finalization projection, baseline Value or Apply authority is returned.</summary>
    public static CharacterCreationFoundationResult<CharacterCreationCompletionCashRequirement> PrepareCompleteStartingCash(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionBaselineRequest request,
        WorkspaceStoredDocument original,
        Func<CharacterCreationCompletionPlan, CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource>> resolveCash,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(resolveCash);
        var result = PlanBaselineCore(inputs, request, original, null, null, null,
            ruleSources, cancellationToken, campaign, resolveCash);
        return new(result.Outcome, result.Value?.RequiredCash, result.Blockers);
    }

    private sealed record BaselineEvaluation(CharacterCreationCompletionBaseline? Complete,
        CharacterCreationCompletionCashRequirement? RequiredCash);

    private static CharacterCreationFoundationResult<BaselineEvaluation> PlanBaselineCore(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionBaselineRequest request,
        WorkspaceStoredDocument original, CharacterCreationCompletionBootstrapSnapshot? bootstrap,
        CharacterCreationStartingNuyenSource? startingCashSource, CharacterCreationStartingCashChoice? cashChoice,
        CharacterCreationRuleSourceExecutionContext? ruleSources, CancellationToken cancellationToken,
        CharacterCreationCampaignExecutionContext? campaign,
        Func<CharacterCreationCompletionPlan, CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource>>? resolveCash)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(original);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Plan is null || !ValidRequest(request.Plan)
            || !CharacterCreationMagicCompletionPlanner.ValidShape(request.Plan.Magic)
            || request.MaximumCandidateEvaluations is < 1 or > MaximumBaselineCandidateEvaluations)
            return BaselineRejected(CharacterCreationFoundationOutcomes.Invalid,
                [CharacterCreationCompletionBaselineBlockers.RequestInvalid]);

        inputs = Detach(inputs);
        request = Detach(request);
        var seed = request.Plan;
        if (seed.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
            || seed.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4
            || seed.RuleSourceExecutionSchema != CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            || seed.OriginalCharacterExecutionSchema != CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1)
            return BaselineRejected(CharacterCreationFoundationOutcomes.Blocked,
                [CharacterCreationCompletionBaselineBlockers.CompleteReviewRequired]);
        var admitted = CreateSessionWithExecution(inputs, seed, ruleSources, cancellationToken, campaign);
        if (admitted.Value is not { } session)
            return BaselineRejected(admitted.Outcome, admitted.Blockers);

        // Catalog fallback appends to the caller's order. It never removes an
        // invalid ranked identity; canonical admission still rejects that choice.
        var seenRequests = new HashSet<string>(StringComparer.Ordinal);
        var projectedSelections = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        IReadOnlyList<string> findings = [];
        bool catalogLimited = false;
        bool searchLimited = false;
        int evaluations = 0;
        foreach (var candidateRequest in Candidates())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenRequests.Add(CharacterCreationFinalizationDigest.Compute(candidateRequest))) continue;
            if (evaluations == request.MaximumCandidateEvaluations)
            {
                searchLimited = true;
                break;
            }
            evaluations++;
            var planned = session.PlanBaseline(candidateRequest, cancellationToken);
            if (Terminal(planned.Outcome)) return BaselineRejected(planned.Outcome, planned.Blockers);
            findings = planned.Blockers;
            if (planned.Value is null || planned.Value.Attributes is null || planned.Value.Magic is null)
                return BaselineRejected(planned.Outcome, findings);
            if (planned.Outcome != CharacterCreationFoundationOutcomes.Success || planned.Value is not { } plan)
            {
                // Keep the failed candidate's exact coverage gate visible when the
                // baseline discards its review. These enum-only findings confer no
                // authority and contain no source IDs, hashes, choices or content.
                if (planned.Blockers.Contains(CharacterCreationEffectivePolicyContext.CoverageUnavailable))
                {
                    var failedReview = planned.Value.Review;
                    var assessment = failedReview.CompleteAssessment;
                    string reason = assessment is null ? "assessment-unavailable"
                        : assessment.Schema != failedReview.Request.CompleteAssessmentSchema ? "schema-mismatch"
                        : assessment.Phase != CharacterCreationCompletePhase.Coverage ? "phase-mismatch"
                        : assessment.RequestDigest != CharacterCreationFinalizationDigest.Compute(failedReview.Request) ? "request-binding-mismatch"
                        : assessment.InputsSnapshotDigest != failedReview.Request.InputsSnapshotDigest ? "inputs-binding-mismatch"
                        : assessment.PolicyDigest != inputs.EffectivePolicy?.PolicyDigest ? "policy-binding-mismatch"
                        : assessment.MechanicalSucceeded != true ? "mechanical-evaluation-unavailable"
                        : assessment.ConsumptionDigest is null ? "consumption-unavailable"
                        : assessment.Obligations.Count is < 1 or > CharacterCreationCompleteAssessment.MaximumObligations ? "obligation-shape-invalid"
                        : !assessment.MayRequestCompleteCandidate ? "fixed-authority-unavailable"
                        : "evaluated-coverage-insufficient";
                    findings = [.. findings, "creation-completion-baseline-coverage-" + reason];
                    if (assessment is not null)
                        findings = [.. findings, .. assessment.Obligations
                            .Where(row => row.Evaluability is not (CharacterCreationCompleteEvaluability.Supported
                                    or CharacterCreationCompleteEvaluability.CandidateDependent)
                                || row.Coverage is not (CharacterCreationCompleteCoverage.Evaluated
                                    or CharacterCreationCompleteCoverage.ProvenInactive))
                            .Select(row => $"creation-completion-baseline-coverage-gap:{row.Kind}:{row.Domain}:{row.Operation}:{row.SourceKind}:{row.Setting?.Key}:{row.Consumer}:{row.Evaluability}:{row.Coverage}:{row.Reason}:{row.ConsumptionReason}")
                            .Distinct(StringComparer.Ordinal)];
                }
                continue;
            }
            if (plan.Blockers.Count != 0 || plan.Review.Blockers.Count != 0
                || plan.Review.CompleteAssessment?.SatisfiesComplete != true)
            {
                findings = [.. plan.Blockers, .. plan.Review.Blockers,
                    CharacterCreationCompletionBaselineBlockers.CompleteReviewRequired];
                continue;
            }
            // Different preference orders can yield the same complete selections.
            // Do not repeat the more expensive independent finalization gate.
            string selectionsDigest = CharacterCreationFinalizationDigest.Compute(plan.Selections);
            if (projectedSelections.TryGetValue(selectionsDigest, out var previousFindings))
            {
                findings = previousFindings;
                continue;
            }
            if (resolveCash is not null)
            {
                var terms = resolveCash(plan);
                cancellationToken.ThrowIfCancellationRequested();
                if (terms.Outcome != CharacterCreationFoundationOutcomes.Success || terms.Value is null || terms.Blockers.Count != 0)
                    return BaselineRejected(terms.Outcome, terms.Blockers);
                return new(CharacterCreationFoundationOutcomes.Success, new(null,
                    new(inputs.SnapshotDigest, CharacterCreationFinalizationDigest.Compute(request), plan.PlanDigest,
                        CharacterCreationFinalizationDigest.Compute(original), request.Plan.StartingLifestyleId, Detach(terms.Value))), []);
            }
            var projected = CharacterCreationCompletionFinalizationProjector.Project(original, bootstrap!,
                inputs, plan.Review, startingCashSource!, cashChoice, cancellationToken, ruleSources, campaign);
            if (Terminal(projected.Outcome)) return BaselineRejected(projected.Outcome, projected.Blockers);
            findings = projected.Blockers;
            projectedSelections.Add(selectionsDigest, findings);
            if (projected.Outcome == CharacterCreationFoundationOutcomes.Success
                && projected.Value is { } finalization && projected.Blockers.Count == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return new(CharacterCreationFoundationOutcomes.Success,
                    new(new(plan, finalization, evaluations), null), []);
            }
            // A missing explicit cash choice cannot be repaired by rearranging
            // spells or powers. Preserve that concrete player decision immediately.
            if (projected.Blockers.Contains(CharacterCreationFinalizationBlockers.StartingCashChoiceRequired)
                || projected.Blockers.Contains(CharacterCreationFinalizationBlockers.StartingCashChoiceInvalid))
                return BaselineRejected(projected.Outcome, projected.Blockers);
        }

        var blockers = findings.Append(CharacterCreationCompletionBaselineBlockers.NoCompleteCandidate).ToList();
        if (searchLimited) blockers.Add(CharacterCreationCompletionBaselineBlockers.SearchLimitReached);
        if (catalogLimited) blockers.Add(CharacterCreationCompletionBaselineBlockers.CatalogLimitReached);
        return BaselineRejected(CharacterCreationFoundationOutcomes.Blocked, blockers);

        IEnumerable<CharacterCreationCompletionPlanRequest> Candidates()
        {
            var magic = seed.Magic;
            if (request.PermitCatalogMagicCompletion)
            {
                var talent = inputs.Magic.SelectedTalent;
                var authority = inputs.Magic.Authority;
                magic = magic with
                {
                    RankedTraditions = talent.RequiresTradition && magic.ManualSelections.Tradition is null
                        ? AddCatalog(magic.RankedTraditions, authority.Traditions) : magic.RankedTraditions,
                    RankedStreams = talent.RequiresStream && magic.ManualSelections.Stream is null
                        ? AddCatalog(magic.RankedStreams, authority.Streams) : magic.RankedStreams,
                    RankedSpells = talent.AllowsSpells ? AddCatalog(magic.RankedSpells, authority.Spells) : magic.RankedSpells,
                    RankedComplexForms = talent.AllowsComplexForms
                        ? AddCatalog(magic.RankedComplexForms, authority.ComplexForms) : magic.RankedComplexForms,
                    RankedAdeptPowers = talent.AllowsAdeptPowers
                        ? AddCatalog(magic.RankedAdeptPowers, authority.AdeptPowers) : magic.RankedAdeptPowers
                };
            }
            yield return seed with { Magic = magic };

            // Repair only planner-generated choices. Each attempt starts from the
            // same admitted original, not the previous branch's generated choices.
            // Interleave dimensions so a long tradition catalog cannot consume the
            // whole search before trying a different power packing. No randomness,
            // provider calls, unbounded recursion or relaxed Core predicates.
            int orderLimit = Math.Min(CharacterCreationMagicCompletionPlanner.MaximumCandidatesPerKind,
                new[] { magic.RankedAdeptPowers.Count, magic.RankedTraditions.Count, magic.RankedStreams.Count,
                    magic.RankedSpells.Count, magic.RankedComplexForms.Count }.Max());
            for (int offset = 1; offset < orderLimit; offset++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (offset < magic.RankedAdeptPowers.Count)
                    yield return seed with { Magic = magic with { RankedAdeptPowers = Rotate(magic.RankedAdeptPowers, offset) } };
                if (offset < magic.RankedTraditions.Count && magic.ManualSelections.Tradition is null)
                    yield return seed with { Magic = magic with { RankedTraditions = Rotate(magic.RankedTraditions, offset) } };
                if (offset < magic.RankedStreams.Count && magic.ManualSelections.Stream is null)
                    yield return seed with { Magic = magic with { RankedStreams = Rotate(magic.RankedStreams, offset) } };
                if (offset < magic.RankedSpells.Count)
                    yield return seed with { Magic = magic with { RankedSpells = Rotate(magic.RankedSpells, offset) } };
                if (offset < magic.RankedComplexForms.Count)
                    yield return seed with { Magic = magic with { RankedComplexForms = Rotate(magic.RankedComplexForms, offset) } };
            }
        }

        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> AddCatalog(
            IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> ranked,
            IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog)
        {
            var additions = catalog.GroupBy(row => row.Identity)
                .Where(group => group.Count() == 1)
                .Select(group => group.Single())
                .Where(row => row.IsEnabled && row.Blockers.Count == 0 && !ranked.Contains(row.Identity))
                .OrderBy(row => row.Identity.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal)
                .Take(CharacterCreationMagicCompletionPlanner.MaximumCandidatesPerKind + 1).ToArray();
            int room = CharacterCreationMagicCompletionPlanner.MaximumCandidatesPerKind - ranked.Count;
            if (additions.Length > room) catalogLimited = true;
            return [.. ranked, .. additions.Take(Math.Max(0, room)).Select(row => row.Identity)];
        }
    }

    private static IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> Rotate(
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> order, int offset)
        => [.. order.Skip(offset), .. order.Take(offset)];

    private static CharacterCreationFoundationResult<BaselineEvaluation> BaselineRejected(
        string outcome, IEnumerable<string> blockers)
    {
        string[] findings = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new(outcome == CharacterCreationFoundationOutcomes.Success ? CharacterCreationFoundationOutcomes.Blocked : outcome,
            null, findings.Length == 0 ? [CharacterCreationCompletionBaselineBlockers.NoCompleteCandidate] : findings);
    }
}
