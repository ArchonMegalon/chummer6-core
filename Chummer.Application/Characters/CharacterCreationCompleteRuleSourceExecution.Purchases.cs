using Chummer.Contracts.Characters;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private IReadOnlyDictionary<string, string> ExecutePurchases(
        CharacterCreationCompleteAssessment authority,
        CharacterCreationGearService.CompleteCostExecution? gear,
        CharacterCreationQualitiesCompletionEvaluation? actualQualities,
        XElement compiledGraph, IReadOnlyList<CharacterCreationFinalizationDelta> compiledDeltas)
    {
        var empty = new Dictionary<string, string>(StringComparer.Ordinal);
        // V1 retains exactly its deployed source coverage and replay identity.
        if (request.RuleSourceExecutionSchema is not (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            || Inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
            || gear is null || actualQualities is null || request.Selections.Gear is not { } basket)
            return empty;
        try
        {
            // Inputs were freshly reconstructed by this resolution from the private
            // archive and original workspace. Reuse the canonical consumers; never
            // infer arithmetic from option hashes or copy their pricing rules.
            if (!CharacterCreationGearRules.TryProjectBasket(basket, Inputs.Lifestyles.Gear.State.Authority,
                    gear.EvaluatedStartingNuyen, out var lines, out var budget, out var blockers)
                || blockers.Length != 0 || !gear.MatchesSourceResult(basket, lines, budget, authority))
                return empty;
            var quality = CharacterCreationQualitiesService.PreviewWithProposedAttributes(
                Inputs.Lifestyles.Qualities, request.Selections.AttributeAllocations,
                request.Selections.QualityOptionIds);
            if (quality.Outcome != CharacterCreationFoundationOutcomes.Success || quality.Blockers.Count != 0
                || quality.Value?.Qualities is not { Blockers.Count: 0 } evaluated
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    PurchaseQualityResult(evaluated), PurchaseQualityResult(actualQualities)))
                return empty;
            // Complete also requires the private selected-effects compiler result.
            // Both full and cold routes meet here with the actual numeric result,
            // while the resolver owns the canonical source/book/filter decisions.
            bool grantedGearExecuted = TryExecutedGrantedGear(lines, compiledGraph, compiledDeltas, out var grantedGear);
            var consumed = resolution.ConsumeGearAndQualitySources(Inputs.Lifestyles.Gear.State.Authority,
                Inputs.Lifestyles.Qualities.Authority, lines, budget, evaluated, grantedGear);
            // Purchased catalogue selection is not proof for additional active
            // origin/grant instances. Keep those source obligations unresolved
            // until their own origin-aware selector joins the same trace.
            var root = XDocument.Parse(Inputs.OriginalCharacter!.RawCharacterXml).Root!;
            bool gearOrigins = !grantedGearExecuted || root.Elements("gears").Any(node => node.HasAttributes || node.HasElements
                || !string.IsNullOrWhiteSpace(node.Value));
            bool qualityGrants = evaluated.GrantedQualities.Count != 0
                || Inputs.Magic.SelectedTalent.GrantedQualitySources is { Count: > 0 }
                || Inputs.Skills.PrerequisiteDraft.HeritageSelection?.RacialQualitySources is { Count: > 0 };
            var unsupported = Inputs.EffectivePolicy!.Sources.Where(source =>
                source.Kind == CharacterCreationPolicySourceKind.Rules
                && (source.SourceId == "gear" && gearOrigins || source.SourceId == "qualities" && qualityGrants))
                .Select(source => "source:" + source.Kind + ":" + source.Order).ToHashSet(StringComparer.Ordinal);
            return consumed.Where(row => !unsupported.Contains(row.Key))
                .ToDictionary(row => row.Key, row => row.Value, StringComparer.Ordinal);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return empty;
        }
    }

    private static object PurchaseQualityResult(CharacterCreationQualitiesCompletionEvaluation result) => new
    {
        // Full and selected authority digests deliberately differ. Every mechanical
        // field, including prerequisites' attribute spend and residual Karma, agrees.
        result.ProposedAttributeKarmaUsed, result.Selections, result.GrantedQualities,
        result.PositiveQualityBudget, result.NegativeQualityBudget,
        result.MetagenicPositiveKarma, result.MetagenicNegativeKarma,
        result.KarmaRemaining, result.SourceAnchorIds, result.Blockers
    };
}
