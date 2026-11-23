using Chummer.Contracts.Characters;
using System.Xml.Linq;
using E = Chummer.Contracts.Characters.CharacterCreationCompleteEvaluability;
using C = Chummer.Contracts.Characters.CharacterCreationCompleteCoverage;
using K = Chummer.Contracts.Characters.CharacterCreationCompleteObligationKind;
using R = Chummer.Contracts.Characters.CharacterCreationCompleteReason;

namespace Chummer.Application.Characters;

/// <summary>
/// Pure policy diagnostics, not a source reader, rules engine or mutation authority.
/// Callers must obtain a current owner-bound observation before hosted admission.
/// V2/V3 retain historical consumers; V4 adds selected-grant and profile/book selection execution.
/// Individual settings and source semantics remain independent obligations.
/// </summary>
public static class CharacterCreationCompleteAssessmentService
{
    /// <summary>
    /// Provider-neutral Prepare seam. Performs no candidate mechanics or provider I/O.
    /// Blocked may carry an authority assessment suitable for explaining fixed gaps.
    /// A successful result would permit candidate search only, never review or Apply.
    /// </summary>
    public static CharacterCreationFoundationResult<CharacterCreationCompleteAssessment> AssessAuthority(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CancellationToken cancellationToken = default, CharacterCreationCampaignExecutionContext? campaign = null,
        CharacterCreationRuleSourceExecutionContext? ruleSources = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (request.CompleteAssessmentSchema is not (CharacterCreationCompleteAssessment.SchemaV1 or CharacterCreationCompleteAssessment.SchemaV2
                    or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4)
                || !CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request)
                || inputs.RuleSourceExecutionSchema != request.RuleSourceExecutionSchema
                || inputs.RuleSourceCaptureDigest != request.RuleSourceCaptureDigest
            || inputs.SourcebookSelection != request.SourcebookSelection
                || request.PolicyRequirement != CharacterCreationPolicyRequirement.Complete
                || !CharacterCreationEffectivePolicyValidation.MayEvaluate(inputs.EffectivePolicy, request)
                || inputs.SnapshotDigest != request.InputsSnapshotDigest
                || inputs.SnapshotDigest != CharacterCreationCompletionReviewRules.ComputeInputsDigest(inputs)
                || !CharacterCreationEffectivePolicyValidation.IsValid(inputs.EffectivePolicy!, inputs))
                return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompleteAssessment.Invalid]);
            if ((request.CampaignAuthority is null) != (campaign is null)
                || campaign is not null && campaign.Phase != CharacterCreationCampaignPhase.Preparation)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCampaignExecutionContext.Blocker]);
            var campaignExecution = campaign?.Execute(request, inputs.SnapshotDigest,
                inputs.Skills.Binding.RawCharacterXmlDigest, inputs.EffectivePolicy!.PolicyDigest, cancellationToken);
            if (request.CampaignAuthority is not null && campaignExecution is null)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCampaignExecutionContext.Blocker]);
            var sourceExecution = ruleSources?.Resolve(inputs, request, cancellationToken);
            if (request.SourcebookSelection is not null
                && sourceExecution?.CanExecuteSourcebooks800() != true)
                return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationSourcebookSelectionAuthority.Unavailable]);
            var assessment = FromFullAuthority(inputs, request, sourceExecution);
            if (campaignExecution is not null) assessment = campaignExecution.Project(assessment, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return new(assessment.MayRequestCompleteCandidate
                    ? CharacterCreationFoundationOutcomes.Success : CharacterCreationFoundationOutcomes.Blocked,
                assessment, assessment.MayRequestCompleteCandidate ? [] : [CharacterCreationCompleteAssessment.FixedAuthorityUnavailable]);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NullReferenceException)
        { return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationCompleteAssessment.Invalid]); }
    }

    // The full review invokes this only after its existing shape/source/snapshot checks.
    internal static CharacterCreationCompleteAssessment FromFullAuthority(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
    {
        return ruleSourceExecution is null ? Evaluate() : ruleSourceExecution.FullAuthorityDiagnostic(inputs, request, Evaluate);

        CharacterCreationCompleteAssessment Evaluate() => Authority(inputs.EffectivePolicy!, inputs.SnapshotDigest, inputs.Skills.Binding.RawCharacterXmlDigest,
            CharacterCreationSelectedPolicyProjection.FromFull(inputs,
                CharacterCreationCompletionPreservationRules.SnapshotOriginal(inputs)), request,
            inputs.Magic.Authority.MysticAdeptPowerPointPolicy, ruleSourceExecution);
    }

    // Replay invokes this only after reading the original archive and validating
    // selected policy inputs; copied full-policy hashes are not reconstructed input.
    internal static CharacterCreationCompleteAssessment FromSelectedAuthority(
        CharacterCreationSelectedCompletionWitness witness, CharacterCreationCompletionReviewRequest request,
        CharacterCreationCompletionOriginalSelections original, IReadOnlyList<CharacterContactRuleEffect> effects,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution = null)
        => Authority(witness.EffectivePolicy!, witness.ProvenanceInputsSnapshotDigest,
            witness.Skills.Binding.RawCharacterXmlDigest,
            CharacterCreationSelectedPolicyProjection.FromSelected(witness, original, effects), request,
            witness.Magic.MysticAdeptPowerPointPolicy, ruleSourceExecution);

    private static CharacterCreationCompleteAssessment Authority(
        CharacterCreationEffectivePolicyContext policy, string snapshot, string rawXml,
        CharacterCreationSelectedPolicyInputs selected, CharacterCreationCompletionReviewRequest request,
        CharacterCreationMysticAdeptPowerPointPolicy? settingsSource,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution)
        => ruleSourceExecution is null
            ? EvaluateAuthority(policy, snapshot, rawXml, selected, request, settingsSource, null)
            : ruleSourceExecution.AuthorityDiagnostic(new { policy, snapshot, rawXml, selected, request, settingsSource },
                () => EvaluateAuthority(policy, snapshot, rawXml, selected, request, settingsSource, ruleSourceExecution));

    private static CharacterCreationCompleteAssessment EvaluateAuthority(
        CharacterCreationEffectivePolicyContext policy, string snapshot, string rawXml,
        CharacterCreationSelectedPolicyInputs selected, CharacterCreationCompletionReviewRequest request,
        CharacterCreationMysticAdeptPowerPointPolicy? settingsSource,
        CharacterCreationCompleteRuleSourceExecution? ruleSourceExecution)
    {
        CharacterCreationCompletionCost3340.Log.Checkpoint(30); // cost3340 authority.begin
        if (!CharacterCreationCompletionReviewRules.HasValidProfileSettingsExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidAdditionalConsumerExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidRuleSourceExecution(request)
            || !CharacterCreationCompletionReviewRules.HasValidMaximumAttributesDefaultExecution(request))
            throw new ArgumentException("Unsupported MaximumAttributes default execution variant.", nameof(request));
        bool mechanics695 = Mechanics695Execution.CanAttempt(request);
        bool sourceScopes696 = CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request);
        // Admission requires this exact privately reconstructed source execution.
        bool amendmentAbsence954 = ruleSourceExecution?.AmendmentAbsence954For(snapshot, rawXml,
            policy.PolicyDigest, CharacterCreationFinalizationDigest.Compute(request)) is not null;
        var rows = new List<CharacterCreationCompleteObligation>();
        CharacterCreationCompleteObligation Row(K kind, string id, E evaluability, R reason, object authority)
            => new(kind, id, evaluability, C.Pending, reason, CharacterCreationFinalizationDigest.Compute(authority));

        // Preserve every existing domain, including missing and opaque/default states.
        // Effective source inventory is not exhaustive semantic execution.
        foreach (var domain in policy.Domains)
        {
            // Admission of the exact resolved profile/book selection does not
            // evaluate its individual settings, amendments or source rules.
            bool selectionAdmission = request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && CharacterCreationCompleteProfileExecution.SupportsDomain(policy, settingsSource, domain.Domain);
            bool mechanicsDomain695 = mechanics695 && domain.Domain == CharacterCreationPolicyDomain.CharacterMechanics;
            // The retained-source executor already interprets corrected effective
            // sources for the Errata graph. Admit the matching domain to that same
            // execution, never to inventory-only coverage. CompleteErrata still
            // requires every frozen source and every corrected compiler replay.
            bool errataDomain788 = sourceScopes696 && domain.Domain == CharacterCreationPolicyDomain.Errata
                && domain.Disposition == CharacterCreationPolicyDisposition.Missing
                && domain.Reason == CharacterCreationPolicyReason.ErrataAuthorityUnavailable;
            bool amendmentsDomain954 = amendmentAbsence954 && domain.Domain == CharacterCreationPolicyDomain.Amendments
                && domain.Disposition == CharacterCreationPolicyDisposition.Effective
                && domain.Reason == CharacterCreationPolicyReason.ResolvedAuthority;
            // Admission is only permission to attempt the exhaustive private
            // profile/source/setting execution. Inventory alone remains unproven.
            bool otherSettings955 = CharacterCreationCompleteRuleSourceExecution.CanAttemptOtherSettings955(request)
                && domain.Domain == CharacterCreationPolicyDomain.OtherSettings
                && domain.Disposition == CharacterCreationPolicyDisposition.Unsupported
                && domain.Reason == CharacterCreationPolicyReason.UnclassifiedLegacySetting
                && CharacterCreationCompleteProfileExecution.SupportsDomain(policy, settingsSource, CharacterCreationPolicyDomain.Profile);
            rows.Add(Row(K.PolicyDomain, "domain:" + domain.Domain,
                mechanicsDomain695 || errataDomain788 || amendmentsDomain954 || otherSettings955 ? E.CandidateDependent
                    : domain.Disposition == CharacterCreationPolicyDisposition.Missing ? E.AuthorityUnavailable
                    : selectionAdmission || domain.Domain == CharacterCreationPolicyDomain.SkillLimits
                        && domain.Disposition == CharacterCreationPolicyDisposition.Effective ? E.CandidateDependent : E.Unsupported,
                mechanicsDomain695 || errataDomain788 || amendmentsDomain954 || otherSettings955 ? R.CandidateEvaluationRequired
                    : domain.Disposition == CharacterCreationPolicyDisposition.Missing ? R.AuthorityUnavailable
                    : selectionAdmission ? R.CandidateEvaluationRequired : R.SourceSemanticsUnproven,
                selectionAdmission ? new { Domain = domain, ProfileSelection = settingsSource } : (object)domain)
                with { Domain = domain.Domain });
        }
        foreach (var source in policy.Sources)
            rows.Add(Row(K.Source, "source:" + source.Kind + ":" + source.Order,
                source.ContentDigest is null ? E.AuthorityUnavailable
                    : (request.RuleSourceExecutionSchema is not null && source.Kind == CharacterCreationPolicySourceKind.SelectedAmend
                    || request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                    && source.Kind == CharacterCreationPolicySourceKind.Rules
                        && (source.SourceId is ("gear" or "qualities" or "skills")
                            || sourceScopes696 && source.SourceId is ("traditions" or "streams" or "powers"
                                or "spells" or "complexforms" or "weapons" or "armor"
                                or "profile" or "metatypes" or "priorities" or "lifestyles")))
                        ? E.CandidateDependent : E.Unsupported,
                source.ContentDigest is null ? R.AuthorityUnavailable
                    : (request.RuleSourceExecutionSchema is not null && source.Kind == CharacterCreationPolicySourceKind.SelectedAmend
                    || request.RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                    && source.Kind == CharacterCreationPolicySourceKind.Rules
                        && (source.SourceId is ("gear" or "qualities" or "skills")
                            || sourceScopes696 && source.SourceId is ("traditions" or "streams" or "powers"
                                or "spells" or "complexforms" or "weapons" or "armor"
                                or "profile" or "metatypes" or "priorities" or "lifestyles")))
                        ? R.CandidateEvaluationRequired : R.SourceSemanticsUnproven, source)
                with { SourceKind = source.Kind, SourceOrder = source.Order });
        foreach (string book in policy.EnabledSourcebooks)
            rows.Add(Row(K.Sourcebook, "book:" + book,
                policy.BooksInputsDigest is null ? E.AuthorityUnavailable
                    : request.SourcebookSelection is { } sourcebooks && sourcebooks.IsValid()
                        ? E.CandidateDependent : E.Unsupported,
                request.SourcebookSelection is not null && policy.BooksInputsDigest is not null
                    ? R.CandidateEvaluationRequired : R.SourceSemanticsUnproven,
                request.SourcebookSelection is null ? new { book, policy.BooksInputsDigest }
                    : (object)new { book, policy.BooksInputsDigest, request.SourcebookSelection }));
        // The retained canonical profile is already admitted by the full/selected
        // route. V3 additionally binds its raw-input identity to the selected
        // profile and interprets the exact source with the LIVE resolver's parser.
        // Neither the old numeric-default reason nor a matching cap is authority.
        XElement? settings = request.CompleteAssessmentSchema is CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4
            && settingsSource is not null && settingsSource.SettingsProfileId == policy.SettingsProfileId
            && CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(settingsSource)
            && CharacterCreationBootstrapProfiles.BindSettingsInputsDigest(
                settingsSource.SettingsInputsDigest, settingsSource.SettingsProfileId) == policy.ProfileInputsDigest
                ? XElement.Parse(settingsSource.CanonicalSourceXml, LoadOptions.PreserveWhitespace) : null;
        foreach (var limit in policy.Settings.Take(3))
        {
            if (request.CompleteAssessmentSchema is not (CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4))
            {
                rows.Add(Row(K.SkillLimit, "limit:" + limit.SettingId,
                    limit.Disposition == CharacterCreationPolicyDisposition.Effective ? E.CandidateDependent
                        : limit.Disposition == CharacterCreationPolicyDisposition.Missing ? E.AuthorityUnavailable : E.Unsupported,
                    limit.Reason == CharacterCreationPolicyReason.ExistingConsumerDefault
                        ? R.AbsentSourceDefaultUnproven : R.CandidateEvaluationRequired, limit));
                continue;
            }
            bool knowledge = limit.SettingId == CharacterCreationEffectivePolicyContext.KnowledgeLimit;
            string creationName = knowledge ? "maxknowledgeskillratingcreate" : "maxskillratingcreate";
            string careerName = knowledge ? "maxknowledgeskillrating" : "maxskillrating";
            bool supported = settings is not null
                && CharacterCreationStandardPrioritySkillsRules.TryReadCreationRatingLimit(settings,
                    creationName, careerName, out int cap)
                && CharacterCreationStandardPrioritySkillsRules.IsSupportedCreationRatingLimit(cap)
                && limit.IntegerValue == cap && limit.Disposition == CharacterCreationPolicyDisposition.Effective
                && limit.Consumer == CharacterCreationPolicyConsumer.StandardPrioritySkills;
            // Groups deliberately share the ACTIVE setting pair; there is no
            // independent group-cap setting. Absence is supported only within this
            // exact, validated profile under the explicit creation default rule.
            rows.Add(Row(K.SkillLimit, "limit:" + limit.SettingId,
                supported ? E.CandidateDependent : E.Unsupported,
                supported ? R.CandidateEvaluationRequired : R.SourceSemanticsUnproven,
                supported ? new
                {
                    Limit = limit, CreationSetting = creationName, CareerSetting = careerName,
                    SettingsProfileId = settingsSource!.SettingsProfileId,
                    SettingsInputsDigest = settingsSource!.SettingsInputsDigest,
                    CanonicalSourceXml = settingsSource!.CanonicalSourceXml
                } : (object)limit));
        }

        // Share only canonical input hashing within this synchronous call.
        // Every retained-profile query still captures all current input bytes
        // and runs its request, policy, source and private capability checks.
        using var profileSettingInputs1203 = ruleSourceExecution is null ? null
            : new CharacterCreationCompleteRuleSourceExecution.ProfileSettingInputValidation1203();

        // Consumers are obtained from the SAME typed binding table as consumption;
        // neither a second setting registry nor receipt-driven obligation discovery.
        CharacterCreationCompletionCost3340.Log.Checkpoint(31); // cost3340 authority.settings.begin
        // Keep the original loop on the unsubscribed path: one opt-in check,
        // no per-setting timing calls or timing branches. The measured copy keeps
        // the same enumeration and predicates; only three numeric totals escape.
        if (CharacterCreationCompletionCost3340.Log.TryBeginSettings3365(out int invocation3365))
        {
            profileSettingInputs1203?.EnableCosts3431();
            int settingCount3365 = 0, fallbackCount3365 = 0;
            long directTicks3365 = 0, fallbackTicks3365 = 0, rowTicks3365 = 0;
            foreach (var (setting, consumer, hasInput) in
                CharacterCreationPolicyConsumptionProjection.DescribeSettingObligations(policy, selected.Policy))
            {
                // V4 alone retains its historical unresolved/default-unproven row,
                // including the original authority digest used by archived replay.
                long started3365 = System.Diagnostics.Stopwatch.GetTimestamp();
                settingCount3365++;
                bool maximumDefault = request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && request.MaximumAttributesDefaultExecutionSchema == CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
                    && hasInput && CharacterCreationCompleteSettingsExecution.SupportsAbsentMaximum(
                        policy, settingsSource, setting, consumer, selected.Policy);
                bool profileSetting = CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && hasInput && CharacterCreationCompleteProfileSettingsExecution.SupportsSetting(
                        policy, settingsSource, setting, consumer, selected.Policy);
                profileSetting |= ruleSourceExecution is { HasNativeFocusSources1339: true }
                    && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsRetainedMagicSetting1221(policy, settingsSource, setting, consumer);
                profileSetting |= ruleSourceExecution is { HasNativeFocusSources1339: true }
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptFocus1220(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsFocusSetting1220(policy, settingsSource, setting, consumer);
                bool karmaSetting1208 = CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsKarmaSetting1208(policy, settingsSource, setting, consumer);
                bool metadataSetting1209 = ruleSourceExecution?.CanExecuteMetadata1209(
                    policy, settingsSource, setting, consumer, request) == true;
                bool essenceSetting1222 = ruleSourceExecution?.CanExecuteEssence1222(policy, settingsSource, setting, consumer, request) == true;
                long boundary3365 = System.Diagnostics.Stopwatch.GetTimestamp();
                directTicks3365 += boundary3365 - started3365;
                started3365 = boundary3365;
                // Broad retained admission is needed only when no fully guarded alternative admits this row.
                // Private consumption independently validates current inputs and builds capabilities on demand.
                if (!maximumDefault && !profileSetting && !karmaSetting1208 && !metadataSetting1209
                    && !essenceSetting1222 && ruleSourceExecution is not null)
                {
                    fallbackCount3365++;
                    profileSetting = ruleSourceExecution.SupportsProfileSetting1203(policy, settingsSource, setting,
                        consumer, snapshot, rawXml, request, profileSettingInputs1203);
                }
                boundary3365 = System.Diagnostics.Stopwatch.GetTimestamp();
                fallbackTicks3365 += boundary3365 - started3365;
                started3365 = boundary3365;
                var reason = maximumDefault || profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? R.CandidateEvaluationRequired : setting.State switch
                {
                    CharacterCreationSettingObservationState.Absent => R.AbsentSourceDefaultUnproven,
                    CharacterCreationSettingObservationState.Opaque => R.OpaqueSource,
                    CharacterCreationSettingObservationState.Invalid => R.InvalidSource,
                    CharacterCreationSettingObservationState.Duplicate => R.DuplicateSource,
                    _ => hasInput ? R.CandidateEvaluationRequired : R.ConsumerUnavailable
                };
                rows.Add(Row(K.Setting, "setting:" + setting.SettingId + ":" + consumer,
                    maximumDefault || profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? E.CandidateDependent
                    : setting.State == CharacterCreationSettingObservationState.Absent ? E.AuthorityUnavailable
                    : setting.State != CharacterCreationSettingObservationState.Present || !hasInput ? E.Unsupported
                    : E.CandidateDependent, reason, maximumDefault
                        ? new { setting, consumer, SettingsSource = settingsSource, Attributes = selected.Policy.Attributes }
                        : profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? new { setting, consumer, SettingsSource = settingsSource, Selected = selected.Policy }
                        : (object)new { setting, consumer })
                    with { Setting = setting, Consumer = consumer });
                rowTicks3365 += System.Diagnostics.Stopwatch.GetTimestamp() - started3365;
            }
            CharacterCreationCompletionCost3340.Log.Checkpoint(32); // cost3340 authority.settings.returned
            CharacterCreationCompletionCost3340.Log.Settings3365(invocation3365, settingCount3365,
                fallbackCount3365, directTicks3365, fallbackTicks3365, rowTicks3365);
            if (profileSettingInputs1203?.Costs3431 is { } costs3431)
                CharacterCreationCompletionCost3340.Log.ProfileCosts3431(invocation3365,
                    costs3431.InputCalls, costs3431.InputTicks,
                    costs3431.ComparisonCalls, costs3431.ComparisonTicks,
                    costs3431.SerializationCalls, costs3431.SerializationTicks, costs3431.ValidatedHashTicks);
        }
        else
        {
            foreach (var (setting, consumer, hasInput) in
                CharacterCreationPolicyConsumptionProjection.DescribeSettingObligations(policy, selected.Policy))
            {
                // V4 alone retains its historical unresolved/default-unproven row,
                // including the original authority digest used by archived replay.
                bool maximumDefault = request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && request.MaximumAttributesDefaultExecutionSchema == CharacterCreationCompletionReviewSchemas.MaximumAttributesDefaultExecutionV1
                    && hasInput && CharacterCreationCompleteSettingsExecution.SupportsAbsentMaximum(
                        policy, settingsSource, setting, consumer, selected.Policy);
                bool profileSetting = CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && hasInput && CharacterCreationCompleteProfileSettingsExecution.SupportsSetting(
                        policy, settingsSource, setting, consumer, selected.Policy);
                profileSetting |= ruleSourceExecution is { HasNativeFocusSources1339: true }
                    && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsRetainedMagicSetting1221(policy, settingsSource, setting, consumer);
                profileSetting |= ruleSourceExecution is { HasNativeFocusSources1339: true }
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptFocus1220(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsFocusSetting1220(policy, settingsSource, setting, consumer);
                bool karmaSetting1208 = CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                    && request.RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
                    && request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && CharacterCreationCompleteRuleSourceExecution.CanAttemptSourceScopes696(request)
                    && CharacterCreationCompleteRuleSourceExecution.SupportsKarmaSetting1208(policy, settingsSource, setting, consumer);
                bool metadataSetting1209 = ruleSourceExecution?.CanExecuteMetadata1209(
                    policy, settingsSource, setting, consumer, request) == true;
                bool essenceSetting1222 = ruleSourceExecution?.CanExecuteEssence1222(policy, settingsSource, setting, consumer, request) == true;
                // Broad retained admission is needed only when no fully guarded alternative admits this row.
                // Private consumption independently validates current inputs and builds capabilities on demand.
                if (!maximumDefault && !profileSetting && !karmaSetting1208 && !metadataSetting1209
                    && !essenceSetting1222 && ruleSourceExecution is not null)
                {
                    profileSetting = ruleSourceExecution.SupportsProfileSetting1203(policy, settingsSource, setting,
                        consumer, snapshot, rawXml, request, profileSettingInputs1203);
                }
                var reason = maximumDefault || profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? R.CandidateEvaluationRequired : setting.State switch
                {
                    CharacterCreationSettingObservationState.Absent => R.AbsentSourceDefaultUnproven,
                    CharacterCreationSettingObservationState.Opaque => R.OpaqueSource,
                    CharacterCreationSettingObservationState.Invalid => R.InvalidSource,
                    CharacterCreationSettingObservationState.Duplicate => R.DuplicateSource,
                    _ => hasInput ? R.CandidateEvaluationRequired : R.ConsumerUnavailable
                };
                rows.Add(Row(K.Setting, "setting:" + setting.SettingId + ":" + consumer,
                    maximumDefault || profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? E.CandidateDependent
                    : setting.State == CharacterCreationSettingObservationState.Absent ? E.AuthorityUnavailable
                    : setting.State != CharacterCreationSettingObservationState.Present || !hasInput ? E.Unsupported
                    : E.CandidateDependent, reason, maximumDefault
                        ? new { setting, consumer, SettingsSource = settingsSource, Attributes = selected.Policy.Attributes }
                        : profileSetting || karmaSetting1208 || metadataSetting1209 || essenceSetting1222 ? new { setting, consumer, SettingsSource = settingsSource, Selected = selected.Policy }
                        : (object)new { setting, consumer })
                    with { Setting = setting, Consumer = consumer });
            }
            CharacterCreationCompletionCost3340.Log.Checkpoint(32); // cost3340 authority.settings.returned
        }
        foreach (var operation in selected.Policy.Coverage)
        {
            bool profileHouseRules = operation.Operation == CharacterCreationConsumerPolicyOperation.AttributeHouseRules
                && CharacterCreationCompleteProfileSettingsExecution.CanAttempt(request)
                && CharacterCreationCompleteProfileSettingsExecution.SupportsHouseRules(policy, settingsSource, selected.Policy);
            // Preserve the frozen policy inventory. Selected effects and opted-in
            // amendments have private execution on both routes; only that bound
            // execution can discharge their candidate-dependent obligations.
            bool selectedEffectsExecution = request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                && operation.Operation == CharacterCreationConsumerPolicyOperation.SelectedEffects
                && operation.Disposition == CharacterCreationConsumerPolicyDisposition.Missing
                && operation.Reason == CharacterCreationConsumerPolicyReason.SelectedEffectsRequireEvaluation;
            bool amendmentExecution = request.RuleSourceExecutionSchema is not null
                && operation.Operation == CharacterCreationConsumerPolicyOperation.Amendments
                && operation.Disposition == CharacterCreationConsumerPolicyDisposition.Missing
                && operation.Reason == CharacterCreationConsumerPolicyReason.AmendValidationUnavailable;
            // The same exhaustive corrected-source execution also owns this
            // consumer row. Admitting an attempt is not coverage: CompleteErrata
            // returns nothing until every frozen source and compiler replay passes.
            bool errataExecution = sourceScopes696
                && operation.Operation == CharacterCreationConsumerPolicyOperation.Errata
                && operation.Disposition == CharacterCreationConsumerPolicyDisposition.Missing
                && operation.Reason == CharacterCreationConsumerPolicyReason.ErrataAuthorityUnavailable;
            // Historical coverage deliberately kept the disabled compensation
            // branch Unsupported. The new opt-in can run that branch of the
            // actual purchase evaluator without rewriting the frozen policy.
            // Typed inputs make it evaluable, not evaluated: only the private
            // completed purchase trace below can discharge this obligation.
            bool disabledCompensationExecution =
                request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
                && operation.Operation == CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation
                && operation.Disposition == CharacterCreationConsumerPolicyDisposition.Unsupported
                && operation.Reason == CharacterCreationConsumerPolicyReason.ConsumerOperationUnavailable
                && selected.Policy.Skills.Purchases is
                    { CompensateSkillGroupKarmaDifference: false, AllowSkillRegrouping: not null };
            // OtherSettings and UnclassifiedSettings summarize the same exhaustive
            // profile execution. This admits only the aggregate attempt; every
            // individual setting retains its independent evaluability and coverage.
            bool unclassifiedSettings1198 =
                CharacterCreationCompleteRuleSourceExecution.CanAttemptOtherSettings955(request)
                && operation.Operation == CharacterCreationConsumerPolicyOperation.UnclassifiedSettings
                && operation.Disposition == CharacterCreationConsumerPolicyDisposition.Unsupported
                && operation.Reason == CharacterCreationConsumerPolicyReason.UnclassifiedLegacySetting
                && CharacterCreationCompleteProfileExecution.SupportsDomain(
                    policy, settingsSource, CharacterCreationPolicyDomain.Profile);
            // This unreleased opt-in broadens evaluability only. The frozen
            // coverage inventory remains unchanged; private canonical execution
            // must still match the complete authority and numerical result.
            bool additionalExecution =
                request.AdditionalConsumerExecutionSchema == CharacterCreationCompletionReviewSchemas.AdditionalConsumerExecutionV1
                && SupportsAdditionalConsumer(operation, selected.Policy, policy.Consumers?.Schema,
                    request.OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1)
                || mechanics695 && operation.Operation == CharacterCreationConsumerPolicyOperation.CharacterMechanics;
            rows.Add(Row(K.ConsumerOperation, "consumer:" + operation.Operation,
                profileHouseRules || selectedEffectsExecution || amendmentExecution || errataExecution || disabledCompensationExecution || additionalExecution || unclassifiedSettings1198
                    || operation.Disposition == CharacterCreationConsumerPolicyDisposition.Admitted ? E.CandidateDependent
                    : operation.Disposition == CharacterCreationConsumerPolicyDisposition.Missing ? E.AuthorityUnavailable : E.Unsupported,
                profileHouseRules || selectedEffectsExecution || amendmentExecution || errataExecution || disabledCompensationExecution || additionalExecution || unclassifiedSettings1198 ? R.CandidateEvaluationRequired
                    : operation.Disposition == CharacterCreationConsumerPolicyDisposition.NotApplicable
                    ? R.UntypedInactivity : R.ConsumerCoverageUnproven,
                profileHouseRules ? new { Observation = operation, SettingsSource = settingsSource, selected.Policy.Attributes }
                    : unclassifiedSettings1198 ? new { Observation = operation, SettingsSource = settingsSource }
                : additionalExecution ? new { Observation = operation, selected.InputsDigest }
                    : disabledCompensationExecution
                    ? new { Observation = operation, selected.Policy.Skills.Purchases } : (object)operation)
                with { Operation = operation.Operation });
        }

        // Keep the full graph inventory. Only V3 and newer admit the bounded graph consumers.
        // Empty amendments, null policies and absent campaign data remove nothing.
        foreach (var operation in CharacterCreationPolicyConsumptionProjection.ActiveGraphObligations)
        {
            bool supported = request.CompleteAssessmentSchema is CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4
                && (operation is CharacterCreationConsumerPolicyOperation.LifestyleGrantEffects
                    or CharacterCreationConsumerPolicyOperation.LifestyleCostEffects
                    || operation == CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers
                        && policy.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5)
                || request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4
                    && operation == CharacterCreationConsumerPolicyOperation.SelectedEffects
                || request.RuleSourceExecutionSchema is not null && operation == CharacterCreationConsumerPolicyOperation.Amendments
                || mechanics695 && operation == CharacterCreationConsumerPolicyOperation.CharacterMechanics
                || sourceScopes696 && operation == CharacterCreationConsumerPolicyOperation.Errata;
            rows.Add(Row(K.ActiveGraph, "graph:" + operation,
                supported ? E.CandidateDependent
                    : operation == CharacterCreationConsumerPolicyOperation.Campaign ? E.AuthorityUnavailable : E.Unsupported,
                supported ? R.CandidateEvaluationRequired
                    : operation == CharacterCreationConsumerPolicyOperation.Campaign ? R.CampaignAuthorityUnavailable
                    : R.ConsumerCoverageUnproven, new { operation, selected.InputsDigest })
                with { Operation = operation });
        }
        // V4 can discharge selected grants only after the actual racial/talent
        // compilers run. Empty lists and equal input hashes establish no coverage.
        bool selectedGrantExecution = request.CompleteAssessmentSchema == CharacterCreationCompleteAssessment.SchemaV4;
        rows.Add(Row(K.SelectedGrants, "selected-grants",
            selectedGrantExecution ? E.CandidateDependent : E.Unsupported,
            selectedGrantExecution ? R.CandidateEvaluationRequired : R.ConsumerCoverageUnproven,
            new { selected.InputsDigest, request.Selections }));
        rows.Add(Row(K.SelectedPrerequisites, "selected-prerequisites", E.CandidateDependent, R.CandidateEvaluationRequired,
            new { selected.InputsDigest, request.Selections }));
        rows.Add(Row(K.SharedBudgets, "shared-budgets", E.CandidateDependent, R.CandidateEvaluationRequired,
            new { snapshot, selected.InputsDigest }));
        rows.Add(Row(K.OriginalSelections, "original-selections", E.CandidateDependent, R.CandidateEvaluationRequired,
            new { selected.InputsDigest, request.Selections }));
        if (rows.Count > CharacterCreationCompleteAssessment.MaximumObligations
            || rows.Select(row => (row.Kind, row.Id)).Distinct().Count() != rows.Count)
            throw new InvalidOperationException("Invalid complete obligation inventory.");
        CharacterCreationCompletionCost3340.Log.Checkpoint(33); // cost3340 authority.inventory.returned
        return new(request.CompleteAssessmentSchema!, CharacterCreationCompletePhase.Authority,
            snapshot, rawXml, selected.InputsDigest, policy.PolicyDigest,
            CharacterCreationFinalizationDigest.Compute(request),
            CharacterCreationFinalizationDigest.Compute(request.Selections),
            CharacterCreationFinalizationDigest.Compute(policy.SettingObservations), null, null, rows.ToArray());
    }

    private static bool SupportsAdditionalConsumer(
        CharacterCreationConsumerPolicyCoverage operation, CharacterCreationSelectedConsumerPolicy selected,
        string? consumerSchema, bool originalCharacterExecution)
    {
        bool unavailable = operation.Disposition == CharacterCreationConsumerPolicyDisposition.Unsupported
            && operation.Reason == CharacterCreationConsumerPolicyReason.ConsumerOperationUnavailable;
        bool originalLoadout = originalCharacterExecution && selected.Loadout?.LoadoutLegality is not null;
        bool careerOnly = operation.Disposition == CharacterCreationConsumerPolicyDisposition.NotApplicable
            && operation.Reason == CharacterCreationConsumerPolicyReason.CareerPurchaseOnly;
        return operation.Operation switch
        {
            CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers => unavailable
                && consumerSchema == CharacterCreationCompletionConsumerPolicy.SchemaV5
                && selected.Skills.Purchases is not null,
            CharacterCreationConsumerPolicyOperation.LifestyleCostEffects => unavailable,
            CharacterCreationConsumerPolicyOperation.LifestyleGrantEffects =>
                operation.Disposition == CharacterCreationConsumerPolicyDisposition.Missing
                && operation.Reason == CharacterCreationConsumerPolicyReason.SelectedEffectsRequireEvaluation,
            CharacterCreationConsumerPolicyOperation.MysticAdeptSeparateMagic => unavailable
                && selected.Magic.MysticAdeptExchange is not null,
            CharacterCreationConsumerPolicyOperation.MysticAdeptExchange => unavailable
                && selected.Magic.MysticAdeptExchange is { UsesSeparateMagicAttribute: true },
            // Keep the frozen null-lifecycle shapes (Admitted exclusions and
            // Unsupported multiplier pricing) intact. The pin permits attempting
            // a private original observation; neither shape proves inactivity.
            CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions => originalLoadout
                && (careerOnly || operation.Disposition == CharacterCreationConsumerPolicyDisposition.Admitted
                    && operation.Reason == CharacterCreationConsumerPolicyReason.MatchedConsumerInputs),
            CharacterCreationConsumerPolicyOperation.LoadoutLegalityMultiplierPricing => originalLoadout
                && (careerOnly || unavailable),
            _ => false
        };
    }

    // mechanicalSucceeded is captured BEFORE a coverage blocker is appended.
    // This consumes canonical projection results; it never prices or infers a rule.
    internal static CharacterCreationCompleteAssessment AfterEvaluation(
        CharacterCreationCompleteAssessment authority, CharacterCreationPolicyConsumption? consumption,
        bool mechanicalSucceeded, CharacterCreationSelectedPrerequisiteConsumer.Execution? selectedPrerequisites = null,
        CharacterCreationCompleteConsumerExecution? consumers = null)
    {
        bool bound = consumption is { Schema: CharacterCreationPolicyConsumption.SchemaV1 }
            && consumption.InputsSnapshotDigest == authority.InputsSnapshotDigest
            && consumption.SelectionsDigest == authority.SelectionsDigest
            && consumption.ObservationsDigest == authority.SettingsObservationDigest
            && consumption.RemainingOperations.SequenceEqual(CharacterCreationPolicyConsumptionProjection.ActiveGraphObligations);
        var evaluatedConsumers = mechanicalSucceeded && bound ? consumers?.EvaluationsFor(authority) : null;
        var rows = authority.Obligations.Select(row =>
        {
            if (authority.Schema is CharacterCreationCompleteAssessment.SchemaV2 or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4
                && evaluatedConsumers is not null && evaluatedConsumers.TryGetValue(row.Id, out var digest))
            {
                // These two entries are issued only after private loadout pricing
                // and a source-bound Creation lifecycle. They prove the Career
                // branch inactive, never claim to execute Career multipliers.
                bool creationOnly = row.Kind == K.ConsumerOperation
                    && row.Operation is CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions
                        or CharacterCreationConsumerPolicyOperation.LoadoutLegalityMultiplierPricing;
                bool profileInactive = consumers!.IsProfileSettingInactive(authority, row.Id);
                bool amendmentInactive = consumers!.IsAmendmentInactive(authority, row.Id);
                return row with { Coverage = creationOnly || profileInactive || amendmentInactive ? C.ProvenInactive : C.Evaluated,
                    Reason = R.CanonicalEvaluation, EvaluationDigest = digest };
            }
            if (authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
                && row.Kind is K.SelectedGrants or K.SelectedPrerequisites)
            {
                string? compiled = selectedPrerequisites?.CompiledGrantDigestFor(authority);
                bool evaluated = mechanicalSucceeded && bound && compiled is not null
                    && row.Evaluability == E.CandidateDependent;
                return row with
                {
                    Coverage = evaluated ? C.Evaluated : C.Unresolved,
                    Reason = evaluated ? R.CanonicalEvaluation
                        : mechanicalSucceeded ? R.EvaluatedUseUnavailable : R.MechanicalEvaluationFailed,
                    EvaluationDigest = evaluated ? compiled : null
                };
            }
            if (row.Kind == K.SelectedPrerequisites)
            {
                var execution = selectedPrerequisites?.Evaluation;
                bool evaluated = mechanicalSucceeded && bound
                    && execution is { Schema: CharacterCreationSelectedPrerequisiteEvaluation.SchemaV1, Checks.Count: > 0 }
                    && execution.Checks.Count <= CharacterCreationSelectedPrerequisiteEvaluation.MaximumChecks
                    && execution.InputsSnapshotDigest == authority.InputsSnapshotDigest
                    && execution.SelectionsDigest == authority.SelectionsDigest;
                return row with
                {
                    Coverage = evaluated ? C.Evaluated : C.Unresolved,
                    Reason = evaluated ? R.CanonicalEvaluation : mechanicalSucceeded
                        ? R.EvaluatedUseUnavailable : R.MechanicalEvaluationFailed,
                    EvaluationDigest = evaluated ? CharacterCreationFinalizationDigest.Compute(execution) : null
                };
            }
            if (row.Kind == K.Setting && bound)
            {
                var matches = consumption!.Settings.Where(item =>
                    item.SettingId == row.Setting!.SettingId && item.Key == row.Setting.Key
                    && item.SourceValueDigest == row.Setting.SourceValueDigest && item.Consumer == row.Consumer).ToArray();
                var actual = matches.Length == 1 ? matches[0] : null;
                bool evaluated = mechanicalSucceeded && row.Evaluability == E.CandidateDependent
                    // V4 default and attribute price require the private source
                    // and execution proofs consumed above. Paid allocation accounting
                    // remains diagnostic; it cannot replace the typed profile producer.
                    && !(authority.Schema == CharacterCreationCompleteAssessment.SchemaV4
                        && (row.Id == CharacterCreationCompleteSettingsExecution.MaximumAttributesObligationId
                            || row.Setting!.Key == CharacterCreationObservedSetting.AttributeKarma))
                    // Literal absence has no input-value digest. It cannot be
                    // discharged by a forged public null == null consumption row.
                    && !(row.Setting!.State == CharacterCreationSettingObservationState.Absent
                        && row.Setting.Key is CharacterCreationObservedSetting.BrokenGroups
                            or CharacterCreationObservedSetting.StrictGroups or CharacterCreationObservedSetting.SpecializationsBreakGroups)
                    && actual?.Reason == CharacterCreationSettingConsumptionReason.Evaluated
                    && actual.InputValueDigest == row.Setting!.ValueDigest
                    && CharacterCreationFinalizationDigest.IsCanonical(actual.ConsumerInputsDigest)
                    && CharacterCreationFinalizationDigest.IsCanonical(actual.EvaluationDigest);
                return row with
                {
                    Coverage = evaluated ? C.Evaluated : C.Unresolved,
                    Reason = evaluated ? R.CanonicalEvaluation
                        : row.Reason == R.CandidateEvaluationRequired ? mechanicalSucceeded
                            ? R.EvaluatedUseUnavailable : R.MechanicalEvaluationFailed : row.Reason,
                    ConsumptionReason = actual?.Reason,
                    EvaluationDigest = evaluated ? actual!.EvaluationDigest : null
                };
            }
            // These are the exact existing shared-wallet and preservation checks,
            // not a claim that they implement the remaining graph operations.
            if (row.Kind is K.SharedBudgets or K.OriginalSelections
                && mechanicalSucceeded && bound)
                return row with { Coverage = C.Evaluated, Reason = R.CanonicalEvaluation, EvaluationDigest = null };
            // No default semantics or replayable inactivity predicate is available
            // for remaining rows. Keep the obligation even if accounting is empty.
            return row with
            {
                Coverage = C.Unresolved,
                EvaluationDigest = null,
                Reason = row.Reason == R.CandidateEvaluationRequired
                    ? mechanicalSucceeded ? R.ConsumerCoverageUnproven : R.MechanicalEvaluationFailed : row.Reason
            };
        }).ToArray();
        return authority with
        {
            Phase = CharacterCreationCompletePhase.Coverage,
            MechanicalSucceeded = mechanicalSucceeded,
            ConsumptionDigest = consumption is null ? null : CharacterCreationFinalizationDigest.Compute(consumption),
            Obligations = rows
        };
    }
}
