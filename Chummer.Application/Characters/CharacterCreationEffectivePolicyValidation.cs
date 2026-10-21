using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Characters;

/// <summary>Validates coverage claims against existing consumers. This is not a rules evaluator
/// or a signature: live authority still comes from the owner-bound source observation.</summary>
internal static class CharacterCreationEffectivePolicyValidation
{
    internal static bool IsValid(CharacterCreationEffectivePolicyContext policy, CharacterCreationCompletionReviewInputs inputs)
    {
        if (inputs.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
            ? !CharacterCreationPhysicalInitiative886.Matches(inputs.PhysicalInitiative886, inputs.Skills.PrerequisiteDraft)
            : inputs.PhysicalInitiative886 is not null) return false;
        if (policy.SelectedConsumption is not null || !CharacterCreationPolicyConsumptionProjection.HasValidObservation(policy)
            || policy.SettingObservations is not null && (policy.Consumers?.Domains is null
                || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, inputs.Magic?.Authority?.MysticAdeptPowerPointPolicy))) return false;
        if (policy.Schema == CharacterCreationEffectivePolicyContext.SchemaV1)
            return inputs.AttributePolicy is null && inputs.LoadoutLegalityPolicy is null && IsValid(policy, inputs.Skills);
        if (!CharacterCreationFinalizationDigest.IsCanonical(policy.PolicyDigest)
            || policy.PolicyDigest != CharacterCreationEffectivePolicyContext.ComputeDigest(policy)
            || !CharacterCreationCompletionConsumerPolicyProjection.IsValid(policy, inputs)) return false;

        // Validate the unchanged source inventory separately from its consumer projection.
        var inventory = policy with { Schema = CharacterCreationEffectivePolicyContext.SchemaV1, Consumers = null };
        inventory = inventory with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(inventory) };
        return IsValid(inventory, inputs.Skills);
    }

    internal static bool IsValidSelected(CharacterCreationSelectedCompletionWitness witness,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects)
        => CharacterCreationSelectedPolicyProjection.IsValid(witness, original, effects);

    // The historical Skills-only validator remains V1-only. V4 uses its separately
    // named bounded projection and never claims to reconstruct the full observation.
    internal static bool IsValid(CharacterCreationEffectivePolicyContext policy, CharacterCreationSkillsCompletionInputs skills)
    {
        if (policy.Schema != CharacterCreationEffectivePolicyContext.SchemaV1 || policy.Consumers is not null
            || policy.SelectedConsumption is not null || !CharacterCreationPolicyConsumptionProjection.HasValidObservation(policy)
            || policy.RulesetId != RulesetDefaults.Sr5
            || policy.BuildMethod is not (CharacterCreationBuildMethods.Priority or CharacterCreationBuildMethods.SumToTen)
            || policy.BuildMethod != skills.PrerequisiteDraft.BuildMethod
            || policy.SettingsProfileId != skills.Authority.SettingsProfileId
            || policy.ProfileInputsDigest != skills.Authority.RawProfileInputsDigest
            || !Guid.TryParseExact(policy.SettingsProfileId, "D", out _)
            || !CharacterCreationFinalizationDigest.IsCanonical(policy.ProfileInputsDigest)
            || policy.EnabledSourcebooks is null || policy.EnabledSourcebooks.Count > 256
            || policy.EnabledSourcebooks.Any(book => !IsBookCode(book))
            || policy.BooksInputsDigest is not null && !CharacterCreationFinalizationDigest.IsCanonical(policy.BooksInputsDigest)
            || !policy.EnabledSourcebooks.SequenceEqual(policy.EnabledSourcebooks.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
            || policy.Settings is null || policy.Settings.Count is < 3 or > CharacterCreationEffectivePolicyContext.MaximumSettings
            || policy.Settings.Any(row => row is null) || policy.Domains is null || policy.Domains.Count != 7 || policy.Domains.Any(row => row is null)
            || policy.Sources is null || policy.Sources.Count > CharacterCreationEffectivePolicyContext.MaximumSources
            || policy.Sources.Any(row => row is null)
            || !CharacterCreationFinalizationDigest.IsCanonical(policy.PolicyDigest)
            || policy.PolicyDigest != CharacterCreationEffectivePolicyContext.ComputeDigest(policy)) return false;

        var scopes = CharacterCreationEffectivePolicyContext.RulesScopes;
        if (policy.Sources.Count < scopes.Count) return false;
        for (int i = 0; i < policy.Sources.Count; i++)
        {
            var source = policy.Sources[i];
            if (!CharacterCreationFinalizationDigest.IsCanonical(source.IdentityDigest)
                || source.ContentDigest is not null && !CharacterCreationFinalizationDigest.IsCanonical(source.ContentDigest)) return false;
            if (i < scopes.Count)
            {
                if (source.Kind != CharacterCreationPolicySourceKind.Rules || source.Order != i || source.SourceId != scopes[i]
                    || source.IdentityDigest != CharacterCreationFinalizationDigest.Compute(new { Scope = source.SourceId })) return false;
            }
            else if (source.Kind is not (CharacterCreationPolicySourceKind.GlobalAmend or CharacterCreationPolicySourceKind.SelectedAmend)
                || source.SourceId != "amend" || source.ContentDigest is null) return false;
        }
        if (policy.Sources[0].ContentDigest != policy.ProfileInputsDigest
            || policy.Sources[3].ContentDigest != skills.Authority.EffectiveSkillsInputsDigest) return false;
        foreach (var kind in new[] { CharacterCreationPolicySourceKind.GlobalAmend, CharacterCreationPolicySourceKind.SelectedAmend })
            if (!policy.Sources.Where(row => row.Kind == kind).Select(row => row.Order)
                .SequenceEqual(Enumerable.Range(0, policy.Sources.Count(row => row.Kind == kind)))) return false;
        if (!policy.Sources.Select(row => (int)row.Kind).SequenceEqual(policy.Sources.Select(row => (int)row.Kind).Order())) return false;

        string[] ids = [CharacterCreationEffectivePolicyContext.ActiveLimit, CharacterCreationEffectivePolicyContext.KnowledgeLimit,
            CharacterCreationEffectivePolicyContext.GroupLimit];
        int[] values = [skills.Authority.MaxActiveSkillRatingCreate, skills.Authority.MaxKnowledgeSkillRatingCreate,
            skills.Authority.MaxSkillGroupRatingCreate];
        for (int i = 0; i < ids.Length; i++)
        {
            var row = policy.Settings[i];
            if (row.SettingId != ids[i] || row.Domain != CharacterCreationPolicyDomain.SkillLimits) return false;
            if (row.Disposition == CharacterCreationPolicyDisposition.Effective)
            {
                if (!skills.Authority.IsAuthoritative || row.IntegerValue != values[i]
                    || !CharacterCreationStandardPrioritySkillsRules.IsSupportedCreationRatingLimit(values[i])
                    || row.Consumer != CharacterCreationPolicyConsumer.StandardPrioritySkills
                    || row.Reason is not (CharacterCreationPolicyReason.ResolvedAuthority or CharacterCreationPolicyReason.ExistingConsumerDefault)) return false;
            }
            else if (row.Consumer != CharacterCreationPolicyConsumer.None
                || row.Disposition == CharacterCreationPolicyDisposition.Unsupported
                    && !(row.Reason == CharacterCreationPolicyReason.ConfiguredLimitUnsupported && row.IntegerValue is >= 0
                            && row.IntegerValue != CharacterCreationStandardPrioritySkillsRules.MaximumRatingAtCreation
                        || row.Reason == CharacterCreationPolicyReason.InvalidSetting && row.IntegerValue is null)
                || row.Disposition == CharacterCreationPolicyDisposition.Missing
                    && (row.Reason != CharacterCreationPolicyReason.ConsumerUnavailable || row.IntegerValue is not null)
                || !Enum.IsDefined(row.Disposition)) return false;
        }
        if (policy.Settings.Skip(3).Any(row => row.Domain != CharacterCreationPolicyDomain.OtherSettings
                || row.Disposition != CharacterCreationPolicyDisposition.Unsupported || row.IntegerValue is not null
                || row.Consumer != CharacterCreationPolicyConsumer.None || row.Reason != CharacterCreationPolicyReason.UnclassifiedLegacySetting
                || row.SettingId is null || !row.SettingId.StartsWith("legacy:", StringComparison.Ordinal)
                || !CharacterCreationFinalizationDigest.IsCanonical(row.SettingId[7..]))
            || policy.Settings.Select(row => row.SettingId).Distinct(StringComparer.Ordinal).Count() != policy.Settings.Count) return false;
        return policy.Domains.SequenceEqual(CharacterCreationEffectivePolicyContext.DescribeDomains(policy.Settings, policy.Sources, policy.BooksInputsDigest));
    }

    internal static bool Satisfies(CharacterCreationEffectivePolicyContext? policy, CharacterCreationPolicyRequirement requirement)
        => requirement switch
        {
            CharacterCreationPolicyRequirement.None => true,
            CharacterCreationPolicyRequirement.SkillLimits => policy?.Domains.Any(row =>
                row.Domain == CharacterCreationPolicyDomain.SkillLimits && row.Disposition == CharacterCreationPolicyDisposition.Effective) == true,
            // Neither version establishes complete amendments, errata, mechanics or campaign applicability.
            CharacterCreationPolicyRequirement.Complete => false,
            _ => false
        };

    // Legacy requests retain the old fail-closed Complete check and bytes.
    // Opt-in permits canonical diagnostics, not Complete admission or provider work.
    internal static bool MayEvaluate(CharacterCreationEffectivePolicyContext? policy,
        CharacterCreationCompletionReviewRequest request)
    {
        if (!CharacterCreationCompletionReviewRules.HasValidProfileSettingsExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidAdditionalConsumerExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidMaximumAttributesDefaultExecution(request)) return false;
        if (request.CompleteAssessmentSchema is null) return Satisfies(policy, request.PolicyRequirement);
        if (request.CompleteAssessmentSchema is not (CharacterCreationCompleteAssessment.SchemaV1 or CharacterCreationCompleteAssessment.SchemaV2
                    or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4)
            || policy is not { Schema: CharacterCreationEffectivePolicyContext.SchemaV2,
                SettingObservations: not null, Consumers.Domains: not null }
            || request.EffectivePolicyDigest != policy.PolicyDigest
            || request.PolicyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1
            || !Enum.IsDefined(request.PolicyRequirement)) return false;
        return request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
            || Satisfies(policy, request.PolicyRequirement);
    }

    internal static bool SatisfiesAfterEvaluation(CharacterCreationEffectivePolicyContext? policy,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompleteAssessment? assessment)
        => request.CompleteAssessmentSchema is null ? Satisfies(policy, request.PolicyRequirement)
            : request.CompleteAssessmentSchema is CharacterCreationCompleteAssessment.SchemaV1 or CharacterCreationCompleteAssessment.SchemaV2
                    or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4
                && assessment?.Schema == request.CompleteAssessmentSchema
                && assessment.Phase == CharacterCreationCompletePhase.Coverage
                && assessment.RequestDigest == CharacterCreationFinalizationDigest.Compute(request)
                && assessment.InputsSnapshotDigest == request.InputsSnapshotDigest
                && assessment.PolicyDigest == policy?.PolicyDigest
                && (request.PolicyRequirement == CharacterCreationPolicyRequirement.Complete
                    ? assessment.SatisfiesComplete : Satisfies(policy, request.PolicyRequirement));

    private static bool IsBookCode(string? book) => book is { Length: > 0 and <= 32 }
        && book.All(value => char.IsAsciiLetterOrDigit(value) || value == '-');
}
