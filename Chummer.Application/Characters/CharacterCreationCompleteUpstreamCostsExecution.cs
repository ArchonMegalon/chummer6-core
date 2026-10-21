using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Invocation-local cost evidence. Neither a projected total nor a serialized
// receipt can construct it. Both routes execute each admitted loadout scope
// and require private Gear and Lifestyle executions for the exact proposal.
internal sealed class CharacterCreationCompleteUpstreamCostsExecution
{
    private readonly string originalDigest;
    private readonly string authorityDigest;
    private readonly string requestDigest;
    private readonly CharacterCreationLoadoutCompletionProjection? loadout;
    private readonly CharacterCreationGearService.CompleteCostExecution gear;
    private readonly CharacterCreationLifestylesService.CompleteGraphExecution lifestyles;

    private CharacterCreationCompleteUpstreamCostsExecution(object original,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompleteAssessment authority,
        CharacterCreationLoadoutCompletionProjection? loadout,
        CharacterCreationGearService.CompleteCostExecution gear,
        CharacterCreationLifestylesService.CompleteGraphExecution lifestyles,
        decimal gearCost, decimal lifestyleCost)
    {
        originalDigest = CharacterCreationFinalizationDigest.Compute(original);
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        requestDigest = CharacterCreationFinalizationDigest.Compute(request);
        this.loadout = JsonSerializer.Deserialize<CharacterCreationLoadoutCompletionProjection>(JsonSerializer.Serialize(loadout));
        this.gear = gear;
        this.lifestyles = lifestyles;
        FullPurchaseCost = checked(gearCost + lifestyleCost);
        if (this.loadout is not null) FullPurchaseCost = checked(FullPurchaseCost + this.loadout.PurchaseCost);
        // Route-independent executed semantics; original input identities remain
        // bound privately and cannot transfer an execution between full and cold.
        EvaluationDigest = CharacterCreationFinalizationDigest.Compute(new
            { Gear = gear.EvaluationDigest, Lifestyles = lifestyles.EvaluationDigest, Loadout = this.loadout });
    }

    internal decimal FullPurchaseCost { get; }
    internal string EvaluationDigest { get; }

    // An omitted legacy loadout is not an executed empty loadout. Only the
    // actual full/selected basket projector can populate this private scope.
    internal string? LoadoutEvaluationDigest => loadout is null ? null : EvaluationDigest;

    internal static CharacterCreationCompleteUpstreamCostsExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var choices = review.Request.Selections;
        if (!CanExecute(authority) || choices.Lifestyles is null || review.Lifestyles is not { } actual
            || review.UpstreamBudget is not { } upstream) return null;
        var resources = inputs.Lifestyles.Gear.Resources;
        if (!Same(actual.Qualities, upstream.Qualities)) return null;
        CharacterCreationLoadoutCompletionProjection? loadout = null;
        if (inputs.Loadout is { } original)
        {
            if (choices.Loadout is null || review.Loadout is null
                || original.Binding.WorkspaceId != resources.Binding.WorkspaceId
                || original.Binding.ContentRevision != resources.Binding.ContentRevision
                || original.Binding.SavedRevision != resources.Binding.SavedRevision
                || original.Binding.RawCharacterXmlDigest != resources.Binding.RawCharacterXmlDigest
                || original.Binding.AuxiliaryStateDigest != resources.Binding.AuxiliaryStateDigest
                || !Same(original.PendingDraft, resources.LoadoutDraft)
                || !Same(original.Receipts, resources.LoadoutReceipts)) return null;
            var projected = CharacterCreationLoadoutCompletionService.Preview(original, choices.Loadout);
            if (projected.Outcome != CharacterCreationFoundationOutcomes.Success || projected.Blockers.Count != 0
                || projected.Value is null || !Same(projected.Value, review.Loadout)) return null;
            loadout = projected.Value;
        }
        else if (!IsOmittedLoadoutScope(resources, choices.Loadout, review.Loadout)) return null;
        // Reconstruct gear authority with the same private source execution as the full review.
        var gear = CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, review, authority, cancellationToken, ruleSourceExecution);
        if (gear is null) return null;
        if (lifestyles is null)
        {
            CharacterCreationLifestylesService.CompleteGraphExecution.Price(new(inputs.Lifestyles.Authority,
                inputs.Lifestyles.Qualities.PrerequisiteDraft, inputs.Magic.SelectedTalent.GrantedQualitySources ?? [],
                upstream.Qualities.Qualities.Selections, upstream.Qualities.ProposalDigest, choices.Lifestyles,
                choices.StartingLifestyleId, AfterLoadout(actual.Gear.Budget.RemainingNuyen, loadout),
                PurchaseAnchors(actual.Gear.SourceAnchorIds, loadout), []), cancellationToken,
                execution => lifestyles = execution, authority);
        }
        if (lifestyles?.MatchesFull(inputs, review, authority) != true) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(inputs, review.Request, authority, loadout, gear, lifestyles,
            actual.Gear.Budget.BasketCost, actual.Budget.Used);
    }

    internal static CharacterCreationCompleteUpstreamCostsExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken, CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var choices = request.Selections;
        if (!CanExecute(authority) || choices.Lifestyles is null) return null;
        CharacterCreationLoadoutCompletionProjection? loadout = null;
        if (witness.Loadout is { } source)
        {
            // A present scope requires all of its inputs, even when explicitly empty.
            if (choices.Loadout is null || result.Loadout is null
                || !Same(source.OriginalSelections,
                    witness.Resources.LoadoutDraft?.Selections ?? CharacterCreationLoadoutSelections.Empty)
                || source.SettingsProfileId != witness.Resources.Authority.SettingsProfileId
                || source.ProfileDigest != witness.Resources.Authority.ProfileDigest
                || !CharacterCreationLoadoutReceiptLedgerIntegrity.Preserves(
                    witness.Resources.LoadoutDraft?.Selections ?? CharacterCreationLoadoutSelections.Empty, choices.Loadout)
                || !CharacterCreationLoadoutRules.TryProjectSelectedBasket(choices.Loadout, source,
                    out var lines, out decimal cost, out var blockers) || blockers.Length != 0) return null;
            loadout = CharacterCreationLoadoutCompletionService.BuildProjection(choices.Loadout, lines, cost,
                witness.Resources.LoadoutDraft?.Instances);
            if (!Same(loadout, result.Loadout)) return null;
        }
        else if (witness.SelectedPolicyInputs is not { Policy.Loadout: null }
            || !IsOmittedLoadoutScope(witness.Resources, choices.Loadout, result.Loadout)) return null;
        var gear = CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, result, authority, cancellationToken);
        if (gear is null) return null;
        if (lifestyles is null)
        {
            CharacterCreationLifestylesService.CompleteGraphExecution.Price(new(witness.Lifestyles,
                witness.Skills.PrerequisiteDraft, witness.Magic.Talent.GrantedQualitySources ?? [], result.Qualities.Selections,
                witness.ProposedQualityProjectionProvenanceSeed, choices.Lifestyles, choices.StartingLifestyleId,
                AfterLoadout(result.GearBudget.RemainingNuyen, loadout),
                PurchaseAnchors(result.GearSourceAnchorIds, loadout), []), cancellationToken,
                execution => lifestyles = execution, authority);
        }
        if (lifestyles?.MatchesSelected(witness, request, result, authority) != true) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(witness, request, authority, loadout, gear, lifestyles,
            result.GearBudget.BasketCost, result.Lifestyles.FullLifestyleCost);
    }

    internal bool MatchesFull(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority)
        => Matches(inputs, review.Request, authority) && Same(loadout, review.Loadout)
            && gear.MatchesFull(inputs, review, authority) && lifestyles.MatchesFull(inputs, review, authority);

    internal bool MatchesSelected(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
        CharacterCreationCompleteAssessment authority)
        => Matches(witness, request, authority) && Same(loadout, result.Loadout)
            && gear.MatchesSelected(witness, request, result, authority)
            && lifestyles.MatchesSelected(witness, request, result, authority);

    private bool Matches(object original, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompleteAssessment authority)
        => originalDigest == CharacterCreationFinalizationDigest.Compute(original)
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
            && requestDigest == CharacterCreationFinalizationDigest.Compute(request);

    // The older no-loadout observation is a distinct contract, not an empty
    // loadout execution. Its admitted original ledger must exclude retained costs
    // and all three scope components must be absent. It earns no loadout coverage.
    private static bool IsOmittedLoadoutScope(CharacterCreationResourcesCompletionInputs resources,
        CharacterCreationLoadoutSelections? choices, CharacterCreationLoadoutCompletionProjection? result)
        => choices is null && result is null && resources.LoadoutDraft is null
            && resources.LoadoutReceipts is null
            && CharacterCreationResourcesService.TryGetRetainedCompletionLoadoutCost(resources, out decimal cost) && cost == 0m;

    private static decimal AfterLoadout(decimal remaining, CharacterCreationLoadoutCompletionProjection? loadout)
        => loadout is null ? remaining : checked(remaining - loadout.PurchaseCost);

    private static string[] PurchaseAnchors(IReadOnlyList<string> gear, CharacterCreationLoadoutCompletionProjection? loadout)
        => loadout is null ? gear.ToArray() : gear.Concat(loadout.Lines.SelectMany(row => row.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private static bool CanExecute(CharacterCreationCompleteAssessment authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority };

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
