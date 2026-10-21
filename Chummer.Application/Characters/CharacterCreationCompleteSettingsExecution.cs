using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// One existing source rule, executed in process. No DTO, outcome flag or receipt
// can construct this evidence. Literal source absence remains in consumption V1.
internal sealed class CharacterCreationCompleteSettingsExecution
{
    internal const string MaximumAttributesObligationId = "setting:absent:MaximumAttributes:Attributes";
    private readonly string authorityDigest;
    private readonly string evaluationDigest;

    private CharacterCreationCompleteSettingsExecution(CharacterCreationCompleteAssessment authority,
        CharacterCreationMysticAdeptPowerPointPolicy source, int maximum,
        CharacterCreationAttributePolicy policy, CharacterCreationAttributesState state,
        CharacterCreationCompletionOriginalSelections original, CharacterCreationCompletionReviewRequest request,
        CharacterCreationAttributesPreview evaluated)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        evaluationDigest = CharacterCreationFinalizationDigest.Compute(new
        {
            Authority = authorityDigest, Obligation = MaximumAttributesObligationId,
            source.SettingsProfileId, source.SettingsInputsDigest, source.CanonicalSourceXml,
            policy.RawProfileInputsDigest, Maximum = maximum, Policy = policy, State = state,
            Original = original, Request = request, Result = evaluated
        });
    }

    internal IReadOnlyDictionary<string, string> EvaluationsFor(CharacterCreationCompleteAssessment authority)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
            && HasCandidateObligation(authority)) result.Add(MaximumAttributesObligationId, evaluationDigest);
        return result;
    }

    // Availability is deliberately bounded to the absent primary setting. This
    // does not promote the legacy observation, arbitrary defaults or source rules.
    internal static bool SupportsAbsentMaximum(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation setting,
        CharacterCreationSettingConsumer consumer, CharacterCreationSelectedConsumerPolicy selected)
        => setting is { Key: CharacterCreationObservedSetting.MaximumAttributes,
                State: CharacterCreationSettingObservationState.Absent, SettingId: "absent:MaximumAttributes",
                SourceValueDigest: null, ValueDigest: null }
            && consumer == CharacterCreationSettingConsumer.Attributes
            && TryResolveAbsentMaximum(policy, source, out int maximum)
            && selected.Attributes.Spending is { } spending
            && spending.MaxNumberMaxAttributesCreate == maximum
            && selected.Attributes.EffectiveMaximumAttributesAtCap == maximum;

    internal static CharacterCreationCompleteSettingsExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }) return null;
        inputs = Detach(inputs);
        review = Detach(review);
        authority = Detach(authority);
        if (inputs.EffectivePolicy is not { } policy || inputs.AttributePolicy is null
            || review.Skills is null || review.Blockers.Count != 0
            || !CanEvaluate(authority, inputs.SnapshotDigest, review.Request)
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || !CharacterCreationEffectivePolicyValidation.MayEvaluate(policy, review.Request)
            || !CharacterCreationEffectivePolicyValidation.IsValid(policy, inputs)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, review.Request, ruleSourceExecution)))
            return null;
        var original = CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs);
        var selected = CharacterCreationSelectedPolicyProjection.FromFull(inputs, original);
        return Execute(policy, inputs.Magic.Authority.MysticAdeptPowerPointPolicy, selected.Policy,
            inputs.AttributePolicy, inputs.Skills.Attributes, original, review.Request,
            review.Skills.Attributes, authority, cancellationToken);
    }

    internal static CharacterCreationCompleteSettingsExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompletionOriginalSelections original,
        IReadOnlyList<CharacterContactRuleEffect> effects, CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken = default,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (authority is not { Schema: CharacterCreationCompleteAssessment.SchemaV4,
                Phase: CharacterCreationCompletePhase.Authority }) return null;
        witness = Detach(witness);
        request = Detach(request);
        result = Detach(result);
        original = Detach(original);
        effects = Detach(effects);
        authority = Detach(authority);
        if (witness.EffectivePolicy is not { } policy || witness.AttributePolicy is null || result.Skills is null
            || !CanEvaluate(authority, witness.ProvenanceInputsSnapshotDigest, request)
            || witness.ProposalDigest != CharacterCreationFinalizationDigest.Compute(request)
            || witness.WitnessDigest != CharacterCreationSelectedCompletionEvaluator.ComputeWitnessDigest(witness)
            || result.WitnessDigest != witness.WitnessDigest
            || !CharacterCreationEffectivePolicyValidation.MayEvaluate(policy, request)
            || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, original, effects)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, effects, ruleSourceExecution)))
            return null;
        var selected = CharacterCreationSelectedPolicyProjection.FromSelected(witness, original, effects);
        return Execute(policy, witness.Magic.MysticAdeptPowerPointPolicy, selected.Policy,
            witness.AttributePolicy, witness.Skills.Attributes, original, request,
            result.Skills.Attributes, authority, cancellationToken);
    }

    private static CharacterCreationCompleteSettingsExecution? Execute(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSelectedConsumerPolicy selected,
        CharacterCreationAttributePolicy spending, CharacterCreationAttributesState state,
        CharacterCreationCompletionOriginalSelections original, CharacterCreationCompletionReviewRequest request,
        CharacterCreationAttributesPreview actual, CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Independently resolve canonical XML even if every mutable cap and digest
        // agrees. A coherent forgery of typed state is not source authority.
        if (!TryResolveAbsentMaximum(policy, source, out int maximum)
            || state.MaxNumberMaxAttributesCreate != maximum
            || spending.MaxNumberMaxAttributesCreate != maximum
            || selected.Attributes.EffectiveMaximumAttributesAtCap != maximum
            || !Same(spending, selected.Attributes.Spending)
            || spending.SettingsProfileId != source!.SettingsProfileId
            || spending.RawProfileInputsDigest != policy.ProfileInputsDigest
            || state.Binding.RawCharacterXmlDigest != authority.RawCharacterXmlDigest
            || !HasCandidateObligation(authority)) return null;

        // This is the actual allocation evaluator, including its maximum-count
        // enforcement. Do not count capped rows or price allocations here.
        var session = CharacterCreationAttributesService.CreateDraftSession(state);
        var executed = session?.Preview(request.Selections.AttributeAllocations);
        if (executed?.Value is not { } evaluated
            || executed.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate)
            || evaluated.Blockers.Any(blocker => blocker != CharacterCreationAttributesBlockers.DraftDuplicate)
            || !Same(evaluated, actual)) return null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(authority, source!, maximum, spending, state, original, request, evaluated);
    }

    private static bool TryResolveAbsentMaximum(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out int maximum)
    {
        maximum = 0;
        // Reuse bounded canonical XML, exact profile id and raw/bound digest
        // admission. It proves source identity, not execution of this setting.
        if (policy.SettingObservations is null
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return false;
        var settings = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        if (settings.Elements("maxnumbermaxattributescreate").Any()
            || CharacterCreationAttributePolicyAuthority.ResolveMaximumAttributesAtCap(settings) is not int resolved)
            return false;
        maximum = resolved;
        return true;
    }

    private static bool CanEvaluate(CharacterCreationCompleteAssessment authority, string snapshot,
        CharacterCreationCompletionReviewRequest request)
        => request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.MaximumAttributesDefaultExecutionSchema == CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
            && authority.InputsSnapshotDigest == snapshot && snapshot == request.InputsSnapshotDigest
            && authority.RequestDigest == CharacterCreationFinalizationDigest.Compute(request)
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections);

    private static bool HasCandidateObligation(CharacterCreationCompleteAssessment authority)
        => authority.Obligations.Count(row => row.Id == MaximumAttributesObligationId
            && row.Kind == CharacterCreationCompleteObligationKind.Setting
            && row.Consumer == CharacterCreationSettingConsumer.Attributes
            && row.Setting is { State: CharacterCreationSettingObservationState.Absent,
                Key: CharacterCreationObservedSetting.MaximumAttributes }
            && row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
            && row.Reason == CharacterCreationCompleteReason.CandidateEvaluationRequired) == 1;

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);

    private static T Detach<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
}
