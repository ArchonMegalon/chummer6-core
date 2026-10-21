using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationLifestylesService
{
    // Private execution state, never a DTO/receipt or an input to archive replay.
    // Price invokes the existing consumer once. Full review and selected replay
    // each retain their own invocation; no second upstream preparation is needed.
    internal sealed class CompleteGraphExecution
    {
        private readonly SelectedCompletionLifestylesPricingInput input;
        private readonly SelectedCompletionLifestylesPrice price;
        private readonly string authorityDigest;

        private CompleteGraphExecution(SelectedCompletionLifestylesPricingInput input,
            SelectedCompletionLifestylesPrice price, CharacterCreationCompleteAssessment authority)
        {
            this.input = DetachCompletion(input);
            this.price = DetachCompletion(price);
            authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
            EvaluationDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Input = SemanticInput(this.input), Result = this.price
            });
        }

        internal string EvaluationDigest { get; }
        internal CharacterCreationLifestylesAuthority CopySourceAuthority718() => DetachCompletion(price.Authority);
        internal IReadOnlyList<CharacterCreationLifestyleProjection> CopySourceProjections718() => DetachCompletion(price.Lifestyles);
        internal Guid? SourceStartingLifestyleId718 => price.StartingLifestyleId;

        internal static CharacterCreationLifestyleResult<SelectedCompletionLifestylesPrice> Price(
            SelectedCompletionLifestylesPricingInput input, CancellationToken cancellationToken,
            Action<CompleteGraphExecution>? observe = null, CharacterCreationCompleteAssessment? authority = null)
        {
            if (observe is null) return PriceSelectedCompletion(input, cancellationToken);
            input = DetachCompletion(input);
            authority = authority is null ? null : DetachCompletion(authority);
            var result = PriceSelectedCompletion(input, cancellationToken);
            // This success specifically includes AreCompletionGrantEffectsSupported,
            // TryCompletionAuthority and TryProject for every frozen configuration.
            // In particular, an empty basket cannot bypass the source compilers.
            if (result.Outcome == CharacterCreationLifestyleOutcomes.Available
                && result.Blockers.Count == 0 && result.Value is { Blockers.Count: 0 } value
                && authority is { Schema: CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4,
                    Phase: CharacterCreationCompletePhase.Authority })
                observe(new CompleteGraphExecution(input, value, authority));
            return result;
        }

        internal bool MatchesFull(CharacterCreationCompletionReviewInputs inputs,
            CharacterCreationCompletionReview review, CharacterCreationCompleteAssessment authority)
        {
            if (authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
                || review.Blockers.Count != 0 || review.Lifestyles is not { } actual
                || review.UpstreamBudget is not { } upstream) return false;
            var choices = review.Request.Selections;
            var gear = actual.Gear;
            decimal loadoutCost = review.Loadout?.PurchaseCost
                ?? inputs.Lifestyles.Gear.Resources.LoadoutDraft?.PurchaseCost ?? 0m;
            IReadOnlyList<string> anchors = review.Loadout is null ? gear.SourceAnchorIds : gear.SourceAnchorIds
                .Concat(review.Loadout.Lines.SelectMany(row => row.SourceAnchorIds))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return Matches(new(inputs.Lifestyles.Authority, inputs.Lifestyles.Qualities.PrerequisiteDraft,
                    inputs.Magic.SelectedTalent.GrantedQualitySources ?? [], upstream.Qualities.Qualities.Selections,
                    upstream.Qualities.ProposalDigest, choices.Lifestyles, choices.StartingLifestyleId,
                    gear.Budget.RemainingNuyen - loadoutCost, anchors, []),
                new(actual.Authority, actual.Lifestyles, actual.StartingLifestyleId, actual.Budget.Used,
                    actual.Budget, actual.SourceAnchorIds, actual.Blockers));
        }

        internal bool MatchesSelected(CharacterCreationSelectedCompletionWitness witness,
            CharacterCreationCompletionReviewRequest request, CharacterCreationSelectedCompletionEvaluation result,
            CharacterCreationCompleteAssessment authority)
        {
            if (authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)) return false;
            var choices = request.Selections;
            IReadOnlyList<string> anchors = result.Loadout is null ? result.GearSourceAnchorIds : result.GearSourceAnchorIds
                .Concat(result.Loadout.Lines.SelectMany(row => row.SourceAnchorIds))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            return Matches(new(witness.Lifestyles, witness.Skills.PrerequisiteDraft,
                witness.Magic.Talent.GrantedQualitySources ?? [], result.Qualities.Selections,
                witness.ProposedQualityProjectionProvenanceSeed, choices.Lifestyles, choices.StartingLifestyleId,
                result.GearBudget.RemainingNuyen - (result.Loadout?.PurchaseCost ?? 0m), anchors, []), result.Lifestyles);
        }

        private bool Matches(SelectedCompletionLifestylesPricingInput expected, SelectedCompletionLifestylesPrice actual)
            => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(SemanticInput(input), SemanticInput(expected))
                && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(price, actual);

        // Gear failure messages are carried only on failure of lifestyle projection;
        // successful pricing replaces those findings after funding is recomputed.
        // Compare every pricing authority/selection/budget input; global failure is
        // still independently gated before coverage can be attached.
        private static SelectedCompletionLifestylesPricingInput SemanticInput(SelectedCompletionLifestylesPricingInput value)
            => value with { GearBlockersBeforeReplacement = [] };
    }
}
