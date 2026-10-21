using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// In-process evidence of canonical Magic execution, never serialized receipt
/// input or save authority. Replay requires its independently admitted original
/// selections and source effects; this helper cannot reconstruct an archive.
/// </summary>
internal sealed class CharacterCreationCompleteMagicExecution
{
    private readonly string authorityDigest;
    private readonly string evaluationDigest;

    private CharacterCreationCompleteMagicExecution(CharacterCreationCompleteAssessment authority,
        CharacterCreationSelectedMagicAuthority source,
        IReadOnlyList<CharacterCreationAttributeProjection> attributes,
        CharacterCreationSelectedMagicEvaluation result)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest,
            Operation = CharacterCreationConsumerPolicyOperation.MagicAndResonance,
            Source = source.PayloadDigest,
            source.OriginalInputsSnapshotDigest,
            source.OriginalAttributesDigest,
            Attributes = attributes,
            Result = result
        });
    }

    internal string? DigestFor(CharacterCreationCompleteAssessment? authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                ? evaluationDigest : null;

    internal static CharacterCreationCompleteMagicExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment? authority, CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is null || inputs is null || review is null) return null;
        try
        {
            inputs = Detach(inputs);
            review = Detach(review);
            authority = Detach(authority);
            if (!CanEvaluate(authority, inputs.SnapshotDigest, review.Request)
                || review.Blockers.Count != 0 || review.Skills is null || review.UpstreamBudget is null
                || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
                || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy!, inputs)
                || !Same(authority, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, review.Request, ruleSourceExecution)))
                return null;

            var choices = review.Request.Selections;
            // This call validates the original source and saved history, runs the
            // attribute consumer, and evaluates tradition/stream/power/spell/form
            // budgets, including separate MAG and spell exchange. Empty choices
            // receive evidence only when that same execution actually succeeds.
            var preview = CharacterCreationMagicResonanceService.PreviewCompletion(inputs.Magic,
                choices.AttributeAllocations, choices.MagicSelections, cancellationToken);
            if (preview.Outcome != CharacterCreationFoundationOutcomes.Success || preview.Blockers.Count != 0
                || preview.Value is not { Blockers.Count: 0 } magic
                || !Same(magic, review.UpstreamBudget.Magic)
                || !Same(magic.Attributes.Attributes, review.Skills.Attributes.Attributes)
                || !Same(magic.Attributes.CreationKarmaBudget, review.Skills.Attributes.CreationKarmaBudget))
                return null;

            // A common selected source identity makes full and cold execution
            // comparable without treating copied full-policy hashes as execution.
            var captured = CharacterCreationMagicResonanceService.CaptureSelectedMagicAuthority(
                inputs.Magic, choices.MagicSelections, cancellationToken);
            if (captured.Outcome != CharacterCreationFoundationOutcomes.Success || captured.Blockers.Count != 0
                || captured.Value is not { } source) return null;
            cancellationToken.ThrowIfCancellationRequested();
            return new(authority, source, magic.Attributes.Attributes,
                new(magic.Selections, magic.EffectiveAttributes, magic.TraditionBudget, magic.StreamBudget,
                    magic.AdeptPowerPointBudget, magic.SpellBudget, magic.ComplexFormBudget,
                    magic.MysticAdeptPowerPoints, magic.SourceAnchorIds, magic.Blockers));
        }
        catch (Exception error) when (IsMalformed(error)) { return null; }
    }

    internal static CharacterCreationCompleteMagicExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompletionOriginalSelections original,
        IReadOnlyList<CharacterContactRuleEffect> effects, CharacterCreationCompleteAssessment? authority,
        CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is null || witness is null || request is null || result is null
            || original is null || effects is null) return null;
        try
        {
            witness = Detach(witness);
            request = Detach(request);
            result = Detach(result);
            original = Detach(original);
            effects = Detach(effects);
            authority = Detach(authority);
            if (!CanEvaluate(authority, witness.ProvenanceInputsSnapshotDigest, request)
                || !witness.HasVersionedShape()
                || witness.WitnessDigest != CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness)
                || result.WitnessDigest != witness.WitnessDigest
                || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, original, effects)
                || !Same(authority, CharacterCreationCompleteAssessmentService.FromSelectedAuthority(
                    witness, request, original, effects, ruleSourceExecution))
                || !Same(NormalizeMagic(original.Magic), witness.Magic.OriginalSelections)
                || !Same(NormalizeMagic(request.Selections.MagicSelections), witness.Magic.ProposedSelections)
                || !Same(witness.Skills.PrerequisiteDraft, witness.Skills.Attributes.PrerequisiteDraft))
                return null;

            // Neither caller-supplied projections nor the selected talent prove
            // attribute evaluation. Replay both original and proposed attributes
            // from the actual retained original state through the canonical session.
            var session = CharacterCreationAttributesService.CreateDraftSession(witness.Skills.Attributes);
            if (session is null) return null;
            var originalAllocations = witness.Skills.Attributes.Attributes.Select(row =>
                new CharacterCreationAttributeAllocation(row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var originalPreview = session.Preview(originalAllocations);
            var proposedPreview = session.Preview(request.Selections.AttributeAllocations);
            if (originalPreview.Value is not { } originalAttributes || proposedPreview.Value is not { } attributes
                // DraftDuplicate is the canonical read-only preview of unchanged
                // allocations, not a mechanical failure or permission to save.
                || originalPreview.Blockers.Any(value => value != CharacterCreationAttributesBlockers.DraftDuplicate)
                || proposedPreview.Blockers.Any(value => value != CharacterCreationAttributesBlockers.DraftDuplicate)
                || originalAttributes.Blockers.Any(value => value != CharacterCreationAttributesBlockers.DraftDuplicate)
                || attributes.Blockers.Any(value => value != CharacterCreationAttributesBlockers.DraftDuplicate)
                || !Same(originalAttributes.Attributes, witness.Skills.Attributes.Attributes)
                || !Same(attributes.Attributes, result.Skills.Attributes.Attributes)
                || !Same(attributes.CreationKarmaBudget, result.Skills.Attributes.CreationKarmaBudget))
                return null;

            var replay = CharacterCreationMagicResonanceService.EvaluateSelectedMagic(witness.Magic,
                witness.Skills.PrerequisiteDraft, originalAttributes.Attributes, attributes.Attributes, cancellationToken);
            if (replay.Outcome != CharacterCreationFoundationOutcomes.Success || replay.Blockers.Count != 0
                || replay.Value is not { Proposed.Blockers.Count: 0 } magic || !Same(magic, result.Magic))
                return null;
            cancellationToken.ThrowIfCancellationRequested();
            return new(authority, witness.Magic, attributes.Attributes, magic.Proposed);
        }
        catch (Exception error) when (IsMalformed(error)) { return null; }
    }

    private static bool CanEvaluate(CharacterCreationCompleteAssessment authority, string snapshot,
        CharacterCreationCompletionReviewRequest request)
        => authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.Phase == CharacterCreationCompletePhase.Authority
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && authority.InputsSnapshotDigest == snapshot && snapshot == request.InputsSnapshotDigest
            && CharacterCreationFinalizationDigest.IsCanonical(snapshot)
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections);

    private static CharacterCreationMagicResonanceSelections NormalizeMagic(CharacterCreationMagicResonanceSelections value)
        => value with
        {
            AdeptPowers = value.AdeptPowers.OrderBy(row => row.Identity.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.Identity.SourceId, StringComparer.Ordinal).ThenBy(row => row.Levels).ToArray(),
            Spells = value.Spells.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray(),
            ComplexForms = value.ComplexForms.OrderBy(row => row.Kind, StringComparer.Ordinal)
                .ThenBy(row => row.SourceId, StringComparer.Ordinal).ToArray()
        };

    private static bool Same<T>(T left, T right)
        => CharacterCreationFinalizationDigest.Compute(left) == CharacterCreationFinalizationDigest.Compute(right);

    private static T Detach<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;

    private static bool IsMalformed(Exception error)
        => error is ArgumentException or InvalidOperationException or NullReferenceException
            or JsonException or InvalidDataException or OverflowException;
}
