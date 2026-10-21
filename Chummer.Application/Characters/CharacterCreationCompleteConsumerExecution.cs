using Q947 = Chummer.Application.Characters.CharacterCreationCompleteProfileSettingsExecution.QualityKarmaLimitExecution947;
using Chummer.Contracts.Characters;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using O = Chummer.Contracts.Characters.CharacterCreationConsumerPolicyOperation;

namespace Chummer.Application.Characters;

/// <summary>
/// Bounded in-process executions, never receipt input. Each route re-executes the
/// canonical consumers against its own admitted inputs. Equality only checks the
/// result of that execution; it cannot create one. No rules math or inactivity
/// inference belongs here. V1 and unassessed requests never enter these consumers.
/// </summary>
internal sealed class CharacterCreationCompleteConsumerExecution
{
    private readonly string authorityDigest;
    private readonly IReadOnlyDictionary<string, string> limits;
    private readonly string? skillPoints;
    private readonly string? attributeSpending;
    private readonly string? skillPurchases;
    private readonly string? resourcesFunding;
    private readonly string? gearCosts;
    private readonly string? magicAndResonance;
    private readonly string? contacts;
    private readonly CharacterCreationCompleteProfileExecution? profile;
    private readonly CharacterCreationCompleteSettingsExecution? settings;
    private readonly CharacterCreationCompleteProfileSettingsExecution? profileSettings;
    private readonly string? qualityCosts;
    private readonly string? skillKarmaModifiers;
    private readonly string? lifestyleGrantEffects;
    private readonly string? lifestyleCostEffects;
    private readonly string? selectedEffects;
    // Assigned once by Capture, after the other canonical consumers exist.
    private IReadOnlyDictionary<string, string>? ruleSources;
    private readonly CharacterCreationPolicyConsumption? canonicalConsumption955;
    private readonly IReadOnlyDictionary<O, string> additionalConsumers;

    private CharacterCreationCompleteConsumerExecution(CharacterCreationCompleteAssessment authority,
        IReadOnlyDictionary<string, string> limits, string? skillPoints, string? attributeSpending,
        string? skillPurchases, string? resourcesFunding, string? gearCosts, string? qualityCosts,
        string? skillKarmaModifiers, string? lifestyleGrantEffects, string? lifestyleCostEffects, string? selectedEffects,
        string? magicAndResonance, string? contacts, CharacterCreationCompleteProfileExecution? profile,
        CharacterCreationCompleteSettingsExecution? settings, IReadOnlyDictionary<string, string>? ruleSources,
        IReadOnlyDictionary<O, string> additionalConsumers, CharacterCreationCompleteProfileSettingsExecution? profileSettings,
        CharacterCreationPolicyConsumption? canonicalConsumption955)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        this.limits = limits;
        this.skillPoints = skillPoints;
        this.attributeSpending = attributeSpending;
        this.skillPurchases = skillPurchases;
        this.resourcesFunding = resourcesFunding;
        this.gearCosts = gearCosts;
        this.magicAndResonance = magicAndResonance;
        this.contacts = contacts;
        this.profile = profile;
        this.settings = settings;
        this.profileSettings = profileSettings;
        this.qualityCosts = qualityCosts;
        this.skillKarmaModifiers = skillKarmaModifiers;
        this.lifestyleGrantEffects = lifestyleGrantEffects;
        this.lifestyleCostEffects = lifestyleCostEffects;
        this.selectedEffects = selectedEffects;
        this.ruleSources = ruleSources;
        this.canonicalConsumption955 = canonicalConsumption955;
        this.additionalConsumers = additionalConsumers;
    }

    internal static CharacterCreationCompleteConsumerExecution? FromFull(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReview review,
        CharacterCreationCompleteAssessment authority, CancellationToken cancellationToken = default,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        if (authority.Schema is not (CharacterCreationCompleteAssessment.SchemaV2 or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4) || review.Blockers.Count != 0
            || review.Skills is null || review.UpstreamBudget is null
            || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
            || inputs.SnapshotDigest != review.Request.InputsSnapshotDigest
            || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy!, inputs)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromFullAuthority(inputs, review.Request, ruleSourceExecution)))
            return null;
        var selected = CharacterCreationSelectedPolicyProjection.FromFull(inputs,
            CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs));
        var skills = ExecuteSkills(inputs.Skills, review.Request.Selections, inputs.EffectivePolicy!,
            review.Skills, review.UpstreamBudget.Qualities.Qualities,
            (inputs.Magic.SelectedTalent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml),
            cancellationToken, authority.Schema == CharacterCreationCompleteAssessment.SchemaV4);
        var qualityLimit947 = Q947.ExecuteFull(inputs, review, authority, out var qualities, cancellationToken);
        var funding = CharacterCreationResourcesService.CompleteFundingExecution.FromFull(inputs, review, authority, cancellationToken, lifestyles, ruleSourceExecution);
        var gear = CharacterCreationGearService.CompleteCostExecution.FromFull(inputs, review, authority, cancellationToken, ruleSourceExecution);
        string? creationLifecycle = null;
        if (review.Request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
            && review.Request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
            && authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && funding?.LoadoutEvaluationDigest is not null
            && CharacterCreationOriginalCharacterSource.TryFromFull(inputs, out var originalCharacter, cancellationToken))
            creationLifecycle = originalCharacter!.CreationLifecycleDigestFor(authority, cancellationToken);
        return Capture(authority, selected, skills, review.Skills,
            qualities.Outcome == CharacterCreationFoundationOutcomes.Success && qualities.Blockers.Count == 0
                ? qualities.Value?.Qualities : null, review.UpstreamBudget.Qualities.Qualities,
            inputs.EffectivePolicy!.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5,
            lifestyles?.MatchesFull(inputs, review, authority) == true ? lifestyles : null,
            qualities.Outcome == CharacterCreationFoundationOutcomes.Success && qualities.Blockers.Count == 0
                && qualities.Value is not null && Same(qualities.Value, review.UpstreamBudget.Qualities),
            skills is null ? null : CharacterCreationSelectedEffectsConsumer.FromFull(inputs, review, authority, cancellationToken),
            funding, gear,
            CharacterCreationCompleteMagicExecution.FromFull(inputs, review, authority, cancellationToken, ruleSourceExecution),
            CharacterCreationCompleteContactsExecution.FromFull(inputs, review, authority, cancellationToken, ruleSourceExecution),
            CharacterCreationCompleteProfileExecution.FromFull(inputs, review.Request, authority, ruleSourceExecution),
            CharacterCreationCompleteSettingsExecution.FromFull(inputs, review, authority, cancellationToken, ruleSourceExecution), ruleSourceExecution,
            review.Request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1,
            creationLifecycle, CharacterCreationCompleteProfileSettingsExecution.FromCanonical(
                inputs.EffectivePolicy!, inputs.Magic.Authority.MysticAdeptPowerPointPolicy, selected.Policy,
                inputs.Skills.Attributes, review.Request, authority, skills, review.Skills), qualityLimit947,
            CharacterCreationCompleteRuleSourceExecution.CanAttemptOtherSettings955(review.Request)
                ? CharacterCreationPolicyConsumptionProjection.FromFull(inputs, review) : null);
    }

    internal static CharacterCreationCompleteConsumerExecution? FromSelected(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationSelectedCompletionEvaluation result, CharacterCreationCompletionOriginalSelections original,
        IReadOnlyList<CharacterContactRuleEffect> effects, CharacterCreationCompleteAssessment authority,
        CancellationToken cancellationToken = default,
        CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles = null,
        IReadOnlyList<CharacterCreationLoadoutInstance>? originalLoadoutInstances = null,
        CharacterCreationOriginalCharacterSource? originalCharacter = null,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        if (authority.Schema is not (CharacterCreationCompleteAssessment.SchemaV2 or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4)
            || witness.ProvenanceInputsSnapshotDigest != request.InputsSnapshotDigest
            || !CharacterCreationEffectivePolicyValidation.IsValidSelected(witness, original, effects)
            || !Same(authority, CharacterCreationCompleteAssessmentService.FromSelectedAuthority(witness, request, original, effects, ruleSourceExecution)))
            return null;
        var selected = CharacterCreationSelectedPolicyProjection.FromSelected(witness, original, effects);
        var skills = ExecuteSkills(witness.Skills, request.Selections, witness.EffectivePolicy!, result.Skills,
            result.Qualities, (witness.Magic.Talent.GrantedQualitySources ?? []).Select(row => row.CanonicalSourceXml),
            cancellationToken, authority.Schema == CharacterCreationCompleteAssessment.SchemaV4);
        var qualityLimit947 = Q947.ExecuteSelected(witness, request, result, authority, out var qualities, cancellationToken, ruleSourceExecution);
        var funding = CharacterCreationResourcesService.CompleteFundingExecution.FromSelected(witness, request, result, authority, cancellationToken, lifestyles);
        var gear = CharacterCreationGearService.CompleteCostExecution.FromSelected(witness, request, result, authority, cancellationToken);
        // Replay reconstructs this private source from its own original history.
        // A copied witness or public status cannot supply the execution.
        string? creationLifecycle =
            request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
            && request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
            && funding?.LoadoutEvaluationDigest is not null
                ? originalCharacter?.CreationLifecycleDigestFor(authority, cancellationToken) : null;
        return Capture(authority, selected, skills, result.Skills, qualities, result.Qualities,
            witness.EffectivePolicy!.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5,
            lifestyles?.MatchesSelected(witness, request, result, authority) == true ? lifestyles : null,
            qualities is { Blockers.Count: 0 } && Same(qualities, result.Qualities),
            skills is null ? null : CharacterCreationSelectedEffectsConsumer.FromSelected(witness, request, result,
                authority, originalLoadoutInstances, cancellationToken, originalCharacter), funding, gear,
            CharacterCreationCompleteMagicExecution.FromSelected(witness, request, result, original, effects, authority, cancellationToken, ruleSourceExecution),
            CharacterCreationCompleteContactsExecution.FromSelected(witness, request, result, original, effects, authority, cancellationToken, ruleSourceExecution),
            CharacterCreationCompleteProfileExecution.FromSelected(witness, request, original, effects, authority, ruleSourceExecution),
            CharacterCreationCompleteSettingsExecution.FromSelected(witness, request, result, original, effects, authority, cancellationToken, ruleSourceExecution), ruleSourceExecution,
            request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1,
            creationLifecycle, CharacterCreationCompleteProfileSettingsExecution.FromCanonical(
                // Replay validates this input policy and the witness's consumption separately.
                ruleSourceExecution?.Inputs.EffectivePolicy ?? witness.EffectivePolicy!,
                witness.Magic.MysticAdeptPowerPointPolicy, selected.Policy,
                witness.Skills.Attributes, request, authority, skills, result.Skills), qualityLimit947,
            CharacterCreationCompleteRuleSourceExecution.CanAttemptOtherSettings955(request)
                ? CharacterCreationPolicyConsumptionProjection.FromSelected(witness, request, result, original, effects) : null);
    }

    internal static CharacterCreationSkillsService.CompleteSkillTrace? ExecuteSkills(
        CharacterCreationSkillsCompletionInputs inputs, CharacterCreationCompletionSelections choices,
        CharacterCreationEffectivePolicyContext policy, CharacterCreationAttributeSkillsPreview actual,
        CharacterCreationQualitiesCompletionEvaluation qualities, IEnumerable<string> grantedSources,
        CancellationToken cancellationToken, bool allowDiceOnlyGrants)
    {
        var trace = new CharacterCreationSkillsService.CompleteSkillTrace();
        bool retainIntervals = policy.SettingObservations?.Schema == CharacterCreationSettingsObservation.SchemaV2;
        CharacterCreationFoundationResult<CharacterCreationAttributeSkillsPreview> result;
        if (policy.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5)
            result = CharacterCreationSkillsService.PreviewCompletionKnowledgePurchases(inputs, choices,
                qualities.Selections, grantedSources, cancellationToken, retainIntervals, trace, allowDiceOnlyGrants);
        else
        {
            if (choices.SkillKarmaPurchases is not null
                && CharacterCreationSkillsService.HasUnsupportedCompletionSkillModifiers(
                    qualities.Selections.Select(row => row.SourceNodeXml).Concat(grantedSources))) return null;
            result = CharacterCreationSkillsService.PreviewCompletionPurchases(inputs, choices.AttributeAllocations,
                choices.SkillAllocations, choices.GroupAllocations, choices.SkillKarmaPurchases,
                cancellationToken, retainIntervals, trace);
        }
        return result.Outcome == CharacterCreationFoundationOutcomes.Success && result.Blockers.Count == 0
            && trace.Completed && result.Value is not null && Same(result.Value, actual) ? trace : null;
    }

    private static CharacterCreationCompleteConsumerExecution Capture(
        CharacterCreationCompleteAssessment authority, CharacterCreationSelectedPolicyInputs selected,
        CharacterCreationSkillsService.CompleteSkillTrace? trace, CharacterCreationAttributeSkillsPreview skills,
        CharacterCreationQualitiesCompletionEvaluation? qualities, CharacterCreationQualitiesCompletionEvaluation actualQualities,
        bool usesSkillModifierConsumer, CharacterCreationLifestylesService.CompleteGraphExecution? lifestyles,
        bool graphQualitiesMatch, CharacterCreationSelectedEffectsConsumer? selectedEffects,
        CharacterCreationResourcesService.CompleteFundingExecution? funding,
        CharacterCreationGearService.CompleteCostExecution? gear,
        CharacterCreationCompleteMagicExecution? magic,
        CharacterCreationCompleteContactsExecution? contacts,
        CharacterCreationCompleteProfileExecution? profile,
        CharacterCreationCompleteSettingsExecution? settings, CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution,
        bool includeAdditionalConsumers, string? creationLifecycle, CharacterCreationCompleteProfileSettingsExecution? profileSettings,
        Q947? qualityLimit947, CharacterCreationPolicyConsumption? canonicalConsumption955)
    {
        string Digest(object result) => CharacterCreationFinalizationDigest.Compute(new
        {
            authority.Schema, authority.InputsSnapshotDigest, authority.RawCharacterXmlDigest,
            authority.SelectedInputsDigest, authority.PolicyDigest, authority.RequestDigest, Result = result
        });
        var additional = new Dictionary<O, string>();
        if (includeAdditionalConsumers && authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
            && trace is not null && graphQualitiesMatch)
        {
            // All values come from completed canonical invocations above. A
            // request pin, projected scalar or caller-supplied receipt earns no
            // coverage. Keep historical unpinned assessments byte-identical.
            void Add(O operation, string? execution)
            {
                if (execution is not null)
                    additional.Add(operation, Digest(new { Operation = operation, Execution = execution }));
            }
            Add(O.CharacterMechanics, ruleSourceExecution?.ExecuteMechanics695(authority, selectedEffects,
                skills, actualQualities, trace, lifestyles, funding, gear, magic, contacts)?.DigestFor(authority));
            Add(O.MysticAdeptExchange, magic?.DigestFor(authority));
            Add(O.MysticAdeptSeparateMagic, magic?.DigestFor(authority));
            Add(O.LifestyleGrantEffects, lifestyles?.EvaluationDigest);
            Add(O.LifestyleCostEffects, lifestyles?.EvaluationDigest);
            Add(O.LifestylesAndFreeGrid, lifestyles?.EvaluationDigest);
            Add(O.LifestyleMetatypeCost, lifestyles?.EvaluationDigest);
            Add(O.Loadout, selected.Policy.Loadout is null ? null : funding?.LoadoutEvaluationDigest);
            // Both private original-character execution and canonical pricing of
            // this exact basket must finish. Frozen Lifecycle=null stays null;
            // it is never interpreted as evidence of either lifecycle.
            if (creationLifecycle is not null && funding?.LoadoutEvaluationDigest is { } loadoutExecution)
            {
                var inactive = CharacterCreationFinalizationDigest.Compute(new { creationLifecycle, loadoutExecution });
                Add(O.LoadoutLegalityExclusions, inactive);
                Add(O.LoadoutLegalityMultiplierPricing, inactive);
            }
            Add(O.SkillGroupKarmaCompensation, trace.PurchasesDigest);
            // Category-specific knowledge pricing is executed only by the V5
            // completion purchase path, not by point allocation or V4 pricing.
            if (usesSkillModifierConsumer)
            {
                Add(O.KnowledgeCategoryPricing, trace.PurchasesDigest);
                Add(O.SkillKarmaModifiers, trace.PurchasesDigest);
            }
        }
        var limits = new Dictionary<string, string>(StringComparer.Ordinal);
        if (trace is not null && authority.Schema == CharacterCreationCompleteAssessment.SchemaV4)
            foreach (var scope in trace.Scopes)
            {
                var row = authority.Obligations.Single(item => item.Kind == K.SkillLimit && item.Id == "limit:" + scope.SettingId);
                if (row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
                    && row.Reason == CharacterCreationCompleteReason.CandidateEvaluationRequired)
                    limits.Add(scope.SettingId, Digest(new { row.AuthorityDigest, Scope = scope }));
            }
        else if (trace is not null)
            foreach (var group in trace.Checks.GroupBy(check => check.SettingId))
            {
                // Authority must reconstruct source semantics (V3) or retain the
                // historical explicit-source rule (V2). A successful comparison
                // alone cannot promote an unproven default or malformed source.
                var row = authority.Obligations.Single(item => item.Kind == K.SkillLimit && item.Id == "limit:" + group.Key);
                if (row.Evaluability == CharacterCreationCompleteEvaluability.CandidateDependent
                    && row.Reason == CharacterCreationCompleteReason.CandidateEvaluationRequired)
                    limits.Add(group.Key, Digest(new { row.AuthorityDigest, Checks = group.ToArray() }));
            }
        var execution = new CharacterCreationCompleteConsumerExecution(authority, limits,
            trace is null ? null : Digest(new { Operation = O.SkillPointRules, Policy = selected.Policy.Skills, Result = skills }),
            // Successful ExecuteSkills has actually prepared and evaluated the
            // proposed attributes, including configured order/metatype rules.
            // A missing typed spending policy still cannot earn coverage.
            trace is not null && authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
                && selected.Policy.Attributes.Spending is not null
                ? Digest(new { Operation = O.AttributeSpending, Policy = selected.Policy.Attributes, Result = skills.Attributes }) : null,
            // Point allocation success does not prove that the optional purchase
            // consumer ran. Its private end-of-pricing trace also covers the
            // explicit empty purchase scope, never an absent request/policy.
            authority.Schema == CharacterCreationCompleteAssessment.SchemaV4 && trace?.PurchasesDigest is { } purchases
                ? Digest(new { Operation = O.SkillPurchases, Policy = selected.Policy.Skills, Execution = purchases }) : null,
            // Funding requires private execution of every contributing purchase
            // cost, including loadout and lifestyles. Both operations remain
            // bound to the admitted upstream and exact mechanical results.
            trace is not null && graphQualitiesMatch && funding is not null && gear is not null
                ? Digest(new { Operation = O.ResourcesAndAvailability, Policy = selected.Policy.Resources, funding.EvaluationDigest }) : null,
            trace is not null && graphQualitiesMatch && funding is not null && gear is not null
                ? Digest(new { Operation = O.Gear, Policy = selected.Policy.Gear, gear.EvaluationDigest }) : null,
            qualities is { Blockers.Count: 0 } && Same(QualityResult(qualities), QualityResult(actualQualities))
                ? Digest(new { Operation = O.QualityCostsAndLimits, Policy = selected.Policy.Qualities, Result = QualityResult(qualities) })
                : null,
            // ExecuteSkills has run the V5 source compilers and canonical pricing,
            // including grant rejection, and matched the actual admitted skill result.
            trace is not null && usesSkillModifierConsumer && graphQualitiesMatch
                ? Digest(new { Operation = O.SkillKarmaModifiers, Policy = selected.Policy.Skills, Result = skills }) : null,
            lifestyles is null || !graphQualitiesMatch ? null : Digest(new { Operation = O.LifestyleGrantEffects, lifestyles.EvaluationDigest }),
            lifestyles is null || !graphQualitiesMatch ? null : Digest(new { Operation = O.LifestyleCostEffects, lifestyles.EvaluationDigest }),
            trace is null || !graphQualitiesMatch ? null : selectedEffects?.DigestFor(authority),
            trace is null || !graphQualitiesMatch ? null : magic?.DigestFor(authority),
            trace is null || !graphQualitiesMatch ? null : contacts?.DigestFor(authority),
            profile, settings, null, additional, graphQualitiesMatch ? profileSettings : null, canonicalConsumption955);
        if (graphQualitiesMatch)
            execution.ruleSources = ruleSourceExecution?.Complete(authority, trace, selectedEffects, gear,
                actualQualities, skills, magic, funding, lifestyles, profileSettings, qualityLimit947, execution, contacts);
        return execution;
    }

    internal IReadOnlyDictionary<string, string> EvaluationsFor(CharacterCreationCompleteAssessment authority)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (authority.Schema is not (CharacterCreationCompleteAssessment.SchemaV2 or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4)
            || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)) return result;
        if (ruleSources is not null)
            foreach (var evaluation in ruleSources) result.Add(evaluation.Key, evaluation.Value);
        if (profile is not null)
            foreach (var evaluation in profile.EvaluationsFor(authority))
                result.Add(evaluation.Key, evaluation.Value);
        if (profileSettings is not null)
            foreach (var evaluation in profileSettings.EvaluationsFor(authority))
                result.Add(evaluation.Key, evaluation.Value);
        if (settings is not null)
            foreach (var evaluation in settings.EvaluationsFor(authority))
                result.Add(evaluation.Key, evaluation.Value);
        foreach (var row in authority.Obligations)
        {
            if (row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent) continue;
            string? digest = null;
            if (row.Kind == K.SkillLimit && row.Id.StartsWith("limit:", StringComparison.Ordinal))
                limits.TryGetValue(row.Id["limit:".Length..], out digest);
            else if (row.Kind == K.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.SkillLimits
                && new[] { CharacterCreationEffectivePolicyContext.ActiveLimit,
                    CharacterCreationEffectivePolicyContext.KnowledgeLimit, CharacterCreationEffectivePolicyContext.GroupLimit }
                    .All(limits.ContainsKey))
                digest = CharacterCreationFinalizationDigest.Compute(limits.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray());
            else if (row.Kind == K.PolicyDomain && row.Domain == CharacterCreationPolicyDomain.CharacterMechanics)
                additionalConsumers.TryGetValue(O.CharacterMechanics, out digest);
            else if (row.Kind == K.ConsumerOperation)
                digest = row.Operation switch
                {
                    O.SkillPointRules => skillPoints,
                    O.AttributeSpending or O.AttributeHouseRules => attributeSpending,
                    O.SkillPurchases => skillPurchases,
                    O.ResourcesAndAvailability => resourcesFunding,
                    O.Gear => gearCosts,
                O.MagicAndResonance => magicAndResonance,
                O.ContactPointAllowance or O.ContactGroupKarma => contacts,
                    O.QualityCostsAndLimits => qualityCosts,
                    O.SelectedEffects => selectedEffects,
                    _ => additionalConsumers.TryGetValue(row.Operation!.Value, out var execution) ? execution : null
                };
            else if (row.Kind == K.ActiveGraph)
                digest = row.Operation switch
                {
                    O.SkillKarmaModifiers => skillKarmaModifiers,
                    O.LifestyleGrantEffects => lifestyleGrantEffects,
                    O.LifestyleCostEffects => lifestyleCostEffects,
                    O.SelectedEffects => selectedEffects,
                    O.CharacterMechanics => additionalConsumers.TryGetValue(O.CharacterMechanics, out var mechanics695) ? mechanics695 : null,
                    _ => null
                };
            if (digest is not null) result.Add(row.Id, digest);
        }
        return result;
    }

    // Rebuild the setting verdicts from this invocation's own projection and
    // completed consumers. Caller-provided consumption/assessment DTOs never enter.
    internal CharacterCreationCompleteAssessment? AssessProfileSettings955(CharacterCreationCompleteAssessment authority)
        => canonicalConsumption955 is null || authorityDigest != CharacterCreationFinalizationDigest.Compute(authority)
            ? null : CharacterCreationCompleteAssessmentService.AfterEvaluation(authority, canonicalConsumption955,
                mechanicalSucceeded: true, consumers: this);

    internal bool IsProfileSettingInactive(CharacterCreationCompleteAssessment authority, string id)
        => profileSettings?.IsInactiveFor(authority, id) == true;

    private static object QualityResult(CharacterCreationQualitiesCompletionEvaluation value) => new
    {
        value.Selections, value.GrantedQualities, value.PositiveQualityBudget,
        value.NegativeQualityBudget, value.SourceAnchorIds
    };

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}

public sealed partial class CharacterCreationSkillsService
{
    // Private to one canonical invocation. DTO serialization never carries this
    // trace. A later final allocation replaces preliminary checks, including the
    // distinction between rated knowledge skills and native languages.
    internal sealed partial class CompleteSkillTrace
    {
        internal sealed record LimitCheck(string SettingId, string TargetId, int Rating, int Limit);
        internal sealed record LimitScope(string SettingId, int Limit, IReadOnlyList<LimitCheck> Checks);
        internal sealed record GroupPolicyCheck(string TargetId, int GroupRating, bool PointSpecialization);
        internal sealed record GroupPolicyScope(bool UsePointsOnBrokenGroups, bool StrictGroups,
            bool SpecializationsBreakGroups, IReadOnlyList<GroupPolicyCheck> Checks);
        private readonly List<GroupPolicyCheck> groupPolicyChecks = [];
        internal GroupPolicyScope? GroupPolicy { get; private set; }
        internal void CheckedGroupPolicy(string target, int groupRating, bool pointSpecialization)
            => groupPolicyChecks.Add(new(target, groupRating, pointSpecialization));
        private readonly List<LimitCheck> checks = [];
        private readonly List<LimitScope> scopes = [];
        internal IReadOnlyList<LimitCheck> Checks => checks;
        internal IReadOnlyList<LimitScope> Scopes => scopes;
        internal bool Completed { get; private set; }
        internal string? PurchasesDigest { get; private set; }
        internal void Begin()
        {
            checks.Clear(); scopes.Clear(); groupPolicyChecks.Clear(); GroupPolicy = null;
            Completed = false; PurchasesDigest = null; SkillCosts972 = null; CompensationPolicy1015 = null;
        }
        internal void FinishPurchases(CharacterCreationKarmaSkillsPolicy policy,
            IReadOnlyList<CharacterCreationSkillKarmaPurchase> purchases, CharacterCreationAttributeSkillsPreview result)
        {
            // Called by the canonical purchase consumer only after source,
            // interval, specialization, modifier and compensation validation.
            // Neither a point-only trace nor serialized result can set it.
            PurchasesDigest = Completed ? CharacterCreationFinalizationDigest.Compute(new
            {
                Policy = policy, Purchases = purchases, Result = result
            }) : null;
        }
        internal void Checked(string setting, string target, int rating, int limit)
            => checks.Add(new(setting, target, rating, limit));
        internal void Finish(CharacterCreationSkillsAuthority authority,
            IReadOnlyList<CharacterCreationSkillProjection> skills,
            IReadOnlyList<CharacterCreationSkillGroupProjection> groups)
        {
            // Called only after the canonical enumeration, automatic grants and
            // all allocation/budget checks succeed. Native languages have no
            // numeric rating; they never become synthetic zero-rated checks.
            CompleteScope(CharacterCreationEffectivePolicyContext.ActiveLimit,
                authority.MaxActiveSkillRatingCreate,
                skills.Where(row => row.Kind == CharacterCreationSkillKinds.Active)
                    .Select(row => (row.SourceSkillId, row.Rating)));
            CompleteScope(CharacterCreationEffectivePolicyContext.KnowledgeLimit,
                authority.MaxKnowledgeSkillRatingCreate,
                skills.Where(row => row.Kind == CharacterCreationSkillKinds.Knowledge && !row.IsNativeLanguage)
                    .Select(row => (row.SourceSkillId, row.Rating)));
            CompleteScope(CharacterCreationEffectivePolicyContext.GroupLimit,
                authority.MaxSkillGroupRatingCreate,
                groups.Select(row => (row.GroupId, (int?)row.Rating)));
            GroupPolicy = new(authority.UsePointsOnBrokenGroups, authority.StrictSkillGroupsInCreateMode,
                authority.SpecializationsBreakSkillGroups, Array.AsReadOnly(groupPolicyChecks
                    .OrderBy(row => row.TargetId, StringComparer.Ordinal).ThenBy(row => row.GroupRating)
                    .ThenBy(row => row.PointSpecialization).ToArray()));
            Completed = true;
        }

        private void CompleteScope(string setting, int limit, IEnumerable<(string Id, int? Rating)> inventory)
        {
            var targets = inventory.OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
            if (limit < 1 || targets.Any(row => string.IsNullOrWhiteSpace(row.Id)
                    || row.Rating is not int rating || rating < 1 || rating > limit)
                || targets.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != targets.Length)
                return;
            var executed = checks.Where(row => row.SettingId == setting)
                .OrderBy(row => row.TargetId, StringComparer.Ordinal).ToArray();
            if (!executed.SequenceEqual(targets.Select(row => new LimitCheck(setting, row.Id, row.Rating!.Value, limit))))
                return;
            // Empty coverage comes from completing this actual validation scope,
            // not from inspecting empty request arrays or an inactivity claim.
            scopes.Add(new(setting, limit, Array.AsReadOnly(executed)));
        }
    }
}
