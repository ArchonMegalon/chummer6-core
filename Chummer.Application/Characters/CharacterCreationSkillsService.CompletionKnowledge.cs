using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService
{
    internal static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompletionKnowledgePurchases(
        CharacterCreationSkillsCompletionInputs inputs, CharacterCreationCompletionSelections choices,
        IReadOnlyList<CharacterCreationQualitySelection> selections,
        IEnumerable<string> grantedQualityXml, CancellationToken cancellationToken, bool captureExecution = false, CompleteSkillTrace? completeTrace = null,
        bool allowDiceOnlyGrants = false)
    {
        // Grants keep their independent bounded compiler. Do not treat an inactive
        // or unknown companion as a supported source merely because creation costs zero.
        if (inputs.KarmaPurchasePolicy is null || choices.SkillKarmaPurchases is null
            || HasUnsupportedCompletionSkillModifiers(grantedQualityXml, allowDiceOnlyGrants)
            || !CharacterCreationLegacySourceProjector.TryKnowledgeModifiers(selections, inputs.Authority, out var effects)
            || !CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(selections, inputs.Authority, out var karmaEffects))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
        return PreviewCompletionSkillModifiersCore(inputs, choices.AttributeAllocations, choices.SkillAllocations,
            choices.GroupAllocations, choices.SkillKarmaPurchases, effects, karmaEffects, cancellationToken, captureExecution, completeTrace);
    }

    internal static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompletionSkillModifiersCore(
        CharacterCreationSkillsCompletionInputs inputs, IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases,
        IReadOnlyList<CharacterCreationKnowledgeCategoryEffect> knowledgeEffects,
        IReadOnlyList<CharacterCreationSkillKarmaEffect> karmaEffects, CancellationToken cancellationToken, bool captureExecution = false, CompleteSkillTrace? completeTrace = null)
    {
        var prepared = PrepareCompletionPreview(inputs, attributes, cancellationToken);
        if (prepared.Value is null) return new(prepared.Outcome, null, prepared.Blockers);
        if (purchases is null || !IsCompletionPurchaseShapeValid(purchases)
            || !CharacterCreationSkillKarmaModifierRules.IsValid(karmaEffects))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
        if (!IsCompletionKarmaPolicyValid(inputs.KarmaPurchasePolicy, inputs.Authority)
            || !IsCompletionCompensationOrderValid(inputs.KarmaCompensationSourceOrder, inputs.Authority))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.PolicyUnavailable);
        var baseline = prepared.Value.Preview(allocations, groups, cancellationToken, knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
        if (baseline.Value is { Blockers.Count: 0 } original && inputs.KarmaPurchasePolicy!.AllowSkillRegrouping.HasValue
            && (inputs.KarmaPurchasePolicy.CompensateSkillGroupKarmaDifference
                || original.Skills.Any(row => row.GroupRating > 0)
                || purchases.Any(p => p.Kind == CharacterCreationSkillKinds.Active
                    && original.SkillGroups.Any(g => g.MemberSkillSourceIds.Contains(p.SourceSkillId, StringComparer.Ordinal)))))
            baseline = prepared.Value.Preview(allocations, groups, cancellationToken, projectGroupMembers: true,
                knowledgeEffects: knowledgeEffects, completeTrace: completeTrace);
        if (baseline.Value is not { } point || baseline.Blockers.Count != 0)
            return purchases.Count == 0 ? baseline : new(baseline.Outcome, null, baseline.Blockers);
        return prepared.Value.PreviewPurchases(point, purchases.ToArray(), inputs.KarmaPurchasePolicy!,
            inputs.KarmaCompensationSourceOrder, cancellationToken, knowledgeEffects, karmaEffects, captureExecution, completeTrace);
    }

    internal static CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> PreviewCompositeSkillPurchases(
        CharacterCreationSkillsCompletionInputs inputs, IReadOnlyList<CharacterCreationAttributeAllocation> attributes,
        IReadOnlyList<CharacterCreationSkillAllocation> allocations, IReadOnlyList<CharacterCreationSkillGroupAllocation> groups,
        IReadOnlyList<CharacterCreationSkillKarmaPurchase>? purchases, CancellationToken cancellationToken = default, bool captureExecution = false, CompleteSkillTrace? completeTrace = null)
    {
        if (inputs.SkillKarmaQualitySelections is not { } selections)
        {
            if (purchases is not null && inputs.CompositeBaseline?.Selections.QualityOptionIds.Count > 0)
                return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                    CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
            return PreviewCompletionPurchasesCore(inputs, attributes, allocations, groups, purchases, null, cancellationToken, captureExecution, completeTrace);
        }
        if (inputs.CompositeBaseline is not { } composite
            || !selections.Select(s => s.OptionId).Order(StringComparer.Ordinal)
                .SequenceEqual(composite.Selections.QualityOptionIds.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || !CharacterCreationLegacySourceProjector.TryKnowledgeModifiers(selections, inputs.Authority, out var knowledge)
            || !CharacterCreationLegacySourceProjector.TrySkillKarmaModifiers(selections, inputs.Authority, out var effects))
            return Blocked<CharacterCreationAttributeSkillsPreview>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillKarmaPurchaseBlockers.ModifiersUnsupported);
        return PreviewCompletionSkillModifiersCore(inputs, attributes, allocations, groups, purchases, knowledge, effects, cancellationToken, captureExecution, completeTrace);
    }
}
