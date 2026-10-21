using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationCompletionPlanner
{
    /// <summary>
    /// Admit one detached historical input graph for a bounded proposal search.
    /// Reusing it avoids repeating original-state admission, not candidate rules
    /// or final review. It grants no current owner, workspace or write authority.
    /// Discard the session when the observed inputs change.
    /// </summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSession(
        CharacterCreationCompletionReviewInputs inputs, CancellationToken cancellationToken = default)
        => CreateSession(inputs, CharacterCreationPolicyRequirement.None, null, cancellationToken);

    /// <summary>Admit the caller's exact policy binding and coverage requirement.
    /// Every candidate must retain both; V3 requires an explicit digest even
    /// when the requirement is None. Complete coverage remains unsupported.</summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSession(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement policyRequirement,
        string? effectivePolicyDigest, CancellationToken cancellationToken = default)
        => CreateSessionCore(inputs, policyRequirement, effectivePolicyDigest, null, null, cancellationToken);

    /// <summary>Admit an explicitly versioned consumption observation through the
    /// same full original review. The version must match the authoritative inputs;
    /// it is retained unchanged by every candidate. Complete remains unsupported.</summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSessionWithPolicyConsumption(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement policyRequirement,
        string? effectivePolicyDigest, string policyConsumptionSchema, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (policyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationEffectivePolicyContext.Invalid]);
        return CreateSessionCore(inputs, policyRequirement, effectivePolicyDigest, policyConsumptionSchema, null, cancellationToken);
    }

    /// <summary>Admit both explicit version pins without relaxing the requested
    /// requirement. Complete also requires a fresh fixed-authority assessment;
    /// a blocked local diagnostic cannot admit a candidate-search session.</summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSessionWithCompleteAssessment(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement policyRequirement,
        string? effectivePolicyDigest, string policyConsumptionSchema, string completeAssessmentSchema,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (policyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1
            || completeAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV1)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompleteAssessment.Invalid]);
        return CreateSessionCore(inputs, policyRequirement, effectivePolicyDigest,
            policyConsumptionSchema, completeAssessmentSchema, cancellationToken);
    }

    /// <summary>Explicit V4 execution entry point. Takes only the supplied
    /// request's policy/execution pins; original choices are reconstructed from
    /// the admitted inputs. It does not upgrade a historical session. Complete
    /// still requires every fixed obligation before candidate search.</summary>
    public static CharacterCreationFoundationResult<PlanningSession> CreateSessionWithExecution(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionPlanRequest authority,
        CharacterCreationRuleSourceExecutionContext? ruleSources,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(authority);
        cancellationToken.ThrowIfCancellationRequested();
        if (authority.RuleSourceExecutionSchema is not null
            && authority.RuleSourceExecutionSchema is not
                (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            || authority.PolicyConsumptionSchema != CharacterCreationPolicyConsumption.SchemaV1
            || authority.CompleteAssessmentSchema != CharacterCreationCompleteAssessment.SchemaV4)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompleteAssessment.Invalid]);
        if (!Digest(inputs.SnapshotDigest, authority.InputsSnapshotDigest))
            return new(CharacterCreationFoundationOutcomes.Conflict, null, [CharacterCreationCompletionReviewSchemas.SnapshotConflict]);
        return CreateSessionCore(inputs, authority.PolicyRequirement, authority.EffectivePolicyDigest,
            authority.PolicyConsumptionSchema, authority.CompleteAssessmentSchema, cancellationToken, authority, ruleSources, campaign);
    }

    private static CharacterCreationFoundationResult<PlanningSession> CreateSessionCore(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationPolicyRequirement policyRequirement,
        string? effectivePolicyDigest, string? policyConsumptionSchema, string? completeAssessmentSchema,
        CancellationToken cancellationToken, CharacterCreationCompletionPlanRequest? execution = null,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null, CharacterCreationCampaignExecutionContext? campaign = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        cancellationToken.ThrowIfCancellationRequested();
        if (!CharacterCreationSkillCompletionPermissionRules.TrySnapshotAuthorityCatalogs(inputs.Skills?.Authority,
                out var authority, cancellationToken))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
        inputs = inputs with { Skills = inputs.Skills! with { Authority = authority } };
        inputs = Detach(inputs);
        if (!TryOriginalSelections(inputs, out var original))
            return new(CharacterCreationFoundationOutcomes.Blocked, null,
                [CharacterCreationCompletionReviewSchemas.AuthorityUnavailable]);
        var originalRequest = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, original)
            {
                CampaignAuthority = execution?.CampaignAuthority,
                PolicyRequirement = policyRequirement,
                EffectivePolicyDigest = effectivePolicyDigest,
                PolicyConsumptionSchema = policyConsumptionSchema,
                CompleteAssessmentSchema = completeAssessmentSchema,
                MaximumAttributesDefaultExecutionSchema = execution?.MaximumAttributesDefaultExecutionSchema,
                AdditionalConsumerExecutionSchema = execution?.AdditionalConsumerExecutionSchema,
                ProfileSettingsExecutionSchema = execution?.ProfileSettingsExecutionSchema,
                OriginalCharacterExecutionSchema = execution?.OriginalCharacterExecutionSchema,
                RuleSourceExecutionSchema = execution?.RuleSourceExecutionSchema,
                RuleSourceCaptureDigest = execution?.RuleSourceCaptureDigest,
                SourcebookSelection = execution?.SourcebookSelection
            };
        var fixedBlockers = CompleteCandidateBlockers(inputs, originalRequest, cancellationToken, campaign, ruleSources);
        if (fixedBlockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, fixedBlockers);
        var admission = CharacterCreationCompletionReviewService.Review(inputs, originalRequest, cancellationToken, ruleSources, campaign);
        if (admission.Value is null) return new(admission.Outcome, null, admission.Blockers);
        cancellationToken.ThrowIfCancellationRequested();
        // Original readiness blockers are still evaluated by every candidate.
        // Success here means only that the historical inputs were admitted.
        return new(CharacterCreationFoundationOutcomes.Success,
            new PlanningSession(inputs, original, policyRequirement, effectivePolicyDigest,
                policyConsumptionSchema, completeAssessmentSchema, originalRequest, ruleSources, campaign), []);
    }

    public sealed class PlanningSession
    {
        private readonly CharacterCreationCompletionReviewInputs _inputs;
        private readonly CharacterCreationCompletionSelections _original;
        private readonly CharacterCreationPolicyRequirement _policyRequirement;
        private readonly string? _effectivePolicyDigest;
        private readonly string? _policyConsumptionSchema;
        private readonly string? _completeAssessmentSchema;
        private readonly string? _maximumAttributesDefaultExecutionSchema;
        private readonly string? _additionalConsumerExecutionSchema;
        private readonly string? _profileSettingsExecutionSchema;
        private readonly string? _originalCharacterExecutionSchema;
        private readonly string? _ruleSourceExecutionSchema;
        private readonly string? _ruleSourceCaptureDigest;
        private readonly CharacterCreationSourcebookSelectionAuthority? _sourcebookSelection;
        private readonly CharacterCreationRuleSourceExecutionContext? _ruleSources;
        private readonly CharacterCreationCampaignExecutionContext? _campaign;
        private readonly CharacterCreationCampaignBinding? _campaignBinding;

        internal PlanningSession(CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections original,
            CharacterCreationPolicyRequirement policyRequirement, string? effectivePolicyDigest,
            string? policyConsumptionSchema, string? completeAssessmentSchema,
            CharacterCreationCompletionReviewRequest execution, CharacterCreationRuleSourceExecutionContext? ruleSources,
            CharacterCreationCampaignExecutionContext? campaign)
        {
            _inputs = inputs;
            _original = original;
            _policyRequirement = policyRequirement;
            _effectivePolicyDigest = effectivePolicyDigest;
            _policyConsumptionSchema = policyConsumptionSchema;
            _completeAssessmentSchema = completeAssessmentSchema;
            _maximumAttributesDefaultExecutionSchema = execution.MaximumAttributesDefaultExecutionSchema;
            _additionalConsumerExecutionSchema = execution.AdditionalConsumerExecutionSchema;
            _profileSettingsExecutionSchema = execution.ProfileSettingsExecutionSchema;
            _originalCharacterExecutionSchema = execution.OriginalCharacterExecutionSchema;
            _ruleSourceExecutionSchema = execution.RuleSourceExecutionSchema;
            _ruleSourceCaptureDigest = execution.RuleSourceCaptureDigest;
            _sourcebookSelection = execution.SourcebookSelection;
            _ruleSources = ruleSources;
            _campaign = campaign;
            _campaignBinding = execution.CampaignAuthority;
        }

        public CharacterCreationFoundationResult<CharacterCreationCompletionPlan> Plan(
            CharacterCreationCompletionPlanRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ValidRequest(request)) return Reject(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationCompletionPlanBlockers.RequestInvalid);
            if (request.CampaignAuthority != _campaignBinding || request.PolicyRequirement != _policyRequirement || request.EffectivePolicyDigest != _effectivePolicyDigest
                || request.PolicyConsumptionSchema != _policyConsumptionSchema
                || request.CompleteAssessmentSchema != _completeAssessmentSchema
                || request.MaximumAttributesDefaultExecutionSchema != _maximumAttributesDefaultExecutionSchema
                || request.AdditionalConsumerExecutionSchema != _additionalConsumerExecutionSchema
                || request.ProfileSettingsExecutionSchema != _profileSettingsExecutionSchema
                || request.OriginalCharacterExecutionSchema != _originalCharacterExecutionSchema
                || request.RuleSourceExecutionSchema != _ruleSourceExecutionSchema
                || request.RuleSourceCaptureDigest != _ruleSourceCaptureDigest
                || request.SourcebookSelection != _sourcebookSelection)
                return Reject(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationCompletionPlanBlockers.PolicyConflict);
            if (!CharacterCreationSkillCompletionPlanner.TrySnapshotRequest(_inputs.Skills.Authority,
                    request.Skills, out var skillsRequest, cancellationToken))
                return Reject(CharacterCreationFoundationOutcomes.Invalid, CharacterCreationSkillCompletionBlockers.RequestInvalid);
            request = request with { Skills = skillsRequest };
            request = Detach(request);
            if (!Digest(_inputs.SnapshotDigest, request.InputsSnapshotDigest))
                return Reject(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationCompletionReviewSchemas.SnapshotConflict);
            return PlanAdmitted(_inputs, request, _original, cancellationToken, _ruleSources, _campaign);
        }
    }
}
