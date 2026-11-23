using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterCreationCompleteRuleSourceExecution
{
    private sealed record FocusBudget1220(string AuthorityDigest, string EffectsDigest, string Lifecycle,
        CharacterCreationFocusKarma1220.Calculation Calculation, CharacterCreationCompletionPoolBalances Before,
        CharacterCreationCompletionPoolBalances After, string ExecutionDigest);
    private FocusBudget1220? focusBudget1220;

    // V4 alone predates these source semantics. Keep the historical authority
    // and wallet when the retained capture lacks the native Focus recipe.
    internal bool HasNativeFocusSources1339 => resolution.Resolver.UsesNativeFocusCapture1339;
    internal bool CanExecuteSharedCharges1339 => HasNativeFocusSources1339 && CanAttemptFocus1220(request);

    internal static bool CanAttemptFocus1220(CharacterCreationCompletionReviewRequest request)
        => CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
            && request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
            && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
            && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            && request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1;

    private bool MatchesFocusAuthority1220(CharacterCreationCompleteAssessment authority)
        => authority is { Schema: CharacterCreationCompleteAssessment.SchemaV4, Phase: CharacterCreationCompletePhase.Authority }
            && original.MatchesInputs(Inputs.SnapshotDigest)
            && CharacterCreationProfileInputRead8576.Matches(Inputs)
            && request.InputsSnapshotDigest == Inputs.SnapshotDigest && authority.InputsSnapshotDigest == Inputs.SnapshotDigest
            && requestDigest == CharacterCreationFinalizationDigest.Compute(request) && authority.RequestDigest == requestDigest
            && authority.SelectionsDigest == CharacterCreationFinalizationDigest.Compute(request.Selections)
            && authority.RawCharacterXmlDigest == Inputs.Skills.Binding.RawCharacterXmlDigest
            && authority.PolicyDigest == Inputs.EffectivePolicy?.PolicyDigest
            && request.RuleSourceCaptureDigest == Inputs.RuleSourceCaptureDigest
            && Inputs.RuleSourceExecutionSchema == request.RuleSourceExecutionSchema
            && CharacterCreationFinalizationDigest.IsCanonical(Inputs.RuleSourceCaptureDigest);
}
