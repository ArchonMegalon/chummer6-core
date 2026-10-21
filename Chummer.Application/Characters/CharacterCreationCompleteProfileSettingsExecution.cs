using System.Xml.Linq;
using Chummer.Contracts.Characters;
using S = Chummer.Contracts.Characters.CharacterCreationObservedSetting;

namespace Chummer.Application.Characters;

// An exact source resolution plus a completed canonical candidate invocation.
// Does not rewrite observation/consumption snapshots or support OtherSettings.
internal sealed partial class CharacterCreationCompleteProfileSettingsExecution
{
    private readonly string authorityDigest;
    private readonly IReadOnlyDictionary<string, string> evaluations;
    private readonly IReadOnlySet<string> inactive;

    private CharacterCreationCompleteProfileSettingsExecution(CharacterCreationCompleteAssessment authority,
        IReadOnlyDictionary<string, string> evaluations, IReadOnlySet<string> inactive, CompletedBrokenGroup940? brokenGroup,
        CompletedKnowledge971? knowledge, CompletedSkillCosts972? skillCosts,
        CompletedAttributeHouseRules993? attributeHouseRules, CompletedCompensation1015? compensation)
    {
        authorityDigest = CharacterCreationFinalizationDigest.Compute(authority);
        this.evaluations = evaluations;
        this.inactive = inactive;
        this.brokenGroup940 = brokenGroup;
        this.knowledge971 = knowledge;
        this.skillCosts972 = skillCosts;
        this.attributeHouseRules993 = attributeHouseRules;
        this.compensation1015 = compensation;
    }

    internal static bool CanAttempt(CharacterCreationCompletionReviewRequest request)
        => request.ProfileSettingsExecutionSchema == CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1
            && CharacterCreationCompletionReviewRules.HasValidProfileSettingsExecution(request);

    internal static bool SupportsSetting(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSettingObservation observation,
        CharacterCreationSettingConsumer? consumer, CharacterCreationSelectedConsumerPolicy selected)
    {
        if (observation.Key is not (S.BrokenGroups or S.StrictGroups or S.SpecializationsBreakGroups
                or S.AlternateAttributeKarma or S.ReverseAttributeOrder)
            || !TrySource(policy, source, out var settings)
            || policy.SettingObservations!.Settings.Count(row => row == observation) != 1)
            return false;
        if (observation.Key is S.BrokenGroups or S.StrictGroups or S.SpecializationsBreakGroups)
            return consumer == CharacterCreationSettingConsumer.Skills
                && observation.State is CharacterCreationSettingObservationState.Present or CharacterCreationSettingObservationState.Absent
                && CharacterCreationSkillGroupPolicyAuthority.TryResolve(settings!, out bool broken, out bool strict, out bool specialization)
                && selected.Skills.UsePointsOnBrokenGroups == broken
                && selected.Skills.StrictSkillGroupsInCreateMode == strict
                && selected.Skills.SpecializationsBreakSkillGroups == specialization;
        return observation.Key is S.AlternateAttributeKarma or S.ReverseAttributeOrder
            && observation.State == CharacterCreationSettingObservationState.Present
            && consumer == CharacterCreationSettingConsumer.Attributes && HouseRulesMatch(settings!, selected);
    }

    internal static bool SupportsHouseRules(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, CharacterCreationSelectedConsumerPolicy selected)
        => TrySource(policy, source, out var settings) && HouseRulesMatch(settings!, selected);

    private static bool HouseRulesMatch(XElement settings, CharacterCreationSelectedConsumerPolicy selected)
        // These two fields must be explicit. No new absent attribute default is inferred.
        => settings.Elements("alternatemetatypeattributekarma").Count() == 1
            && settings.Elements("reverseattributepriorityorder").Count() == 1
            && CharacterCreationSkillGroupPolicyAuthority.TryReadBoolean(settings, "alternatemetatypeattributekarma", false, out bool alternate)
            && CharacterCreationSkillGroupPolicyAuthority.TryReadBoolean(settings, "reverseattributepriorityorder", false, out bool reverse)
            && selected.Attributes.Spending is { } spending
            && spending.AlternateMetatypeAttributeKarma == alternate
            && spending.ReverseAttributePriorityOrder == reverse;

    private static bool TrySource(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationMysticAdeptPowerPointPolicy? source, out XElement? settings)
    {
        settings = null;
        if (policy.SettingObservations is null
            || !CharacterCreationCompleteProfileExecution.SupportsDomain(policy, source, CharacterCreationPolicyDomain.Profile)
            || !CharacterCreationPolicyConsumptionProjection.MatchesSource(policy, source)) return false;
        settings = XElement.Parse(source!.CanonicalSourceXml, LoadOptions.PreserveWhitespace);
        return !settings.HasAttributes;
    }

    internal static CharacterCreationCompleteProfileSettingsExecution? FromCanonical(
        CharacterCreationEffectivePolicyContext policy, CharacterCreationMysticAdeptPowerPointPolicy? source,
        CharacterCreationSelectedConsumerPolicy selected, CharacterCreationAttributesState attributes,
        CharacterCreationCompletionReviewRequest request, CharacterCreationCompleteAssessment authority,
        CharacterCreationSkillsService.CompleteSkillTrace? trace, CharacterCreationAttributeSkillsPreview result)
    {
        // Both callers have independently executed ExecuteSkills and compared its
        // entire result, including attributes, prices, group members and budgets.
        if (!CanAttempt(request) || trace is not { Completed: true, GroupPolicy: not null }
            || authority.Phase != CharacterCreationCompletePhase.Authority
            || authority.RequestDigest != CharacterCreationFinalizationDigest.Compute(request)
            || authority.PolicyDigest != policy.PolicyDigest
            || authority.InputsSnapshotDigest != request.InputsSnapshotDigest
            || !TrySource(policy, source, out _)
            || !CharacterCreationAttributePolicyAuthority.MatchesHouseRules(selected.Attributes.Spending, attributes))
            return null;
        var group = trace.GroupPolicy;
        if (group.UsePointsOnBrokenGroups != selected.Skills.UsePointsOnBrokenGroups
            || group.StrictGroups != selected.Skills.StrictSkillGroupsInCreateMode
            || group.SpecializationsBreakGroups != selected.Skills.SpecializationsBreakSkillGroups)
            return null;
        var evaluations = new Dictionary<string, string>(StringComparer.Ordinal);
        var inactive = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in authority.Obligations)
        {
            if (row.Kind != CharacterCreationCompleteObligationKind.Setting || row.Setting is null
                || row.Evaluability != CharacterCreationCompleteEvaluability.CandidateDependent
                || !SupportsSetting(policy, source, row.Setting, row.Consumer, selected)) continue;
            // The scope comes from the final successful canonical enumeration,
            // including auto-projected group members and grants, not request emptiness.
            bool noTarget = row.Setting.Key switch
            {
                S.BrokenGroups or S.StrictGroups => !group.Checks.Any(check => check.GroupRating > 0),
                S.SpecializationsBreakGroups => !group.Checks.Any(check => check.GroupRating > 0 && check.PointSpecialization),
                S.AlternateAttributeKarma or S.ReverseAttributeOrder => !result.Attributes.Attributes.Any(attribute => attribute.KarmaLevels > 0),
                _ => false
            };
            if (noTarget) inactive.Add(row.Id);
            evaluations.Add(row.Id, CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCompletionReviewSchemas.ProfileSettingsExecutionV1,
                Authority = CharacterCreationFinalizationDigest.Compute(authority), row.Id, row.AuthorityDigest,
                Source = source, Policy = selected, GroupScope = group, Request = request,
                Result = result, DomainEmpty = noTarget
            }));
        }
        return new(authority, evaluations, inactive,
            CompletedBrokenGroup940.FromCanonical(policy, source!, selected, trace, result),
            CompletedKnowledge971.FromCanonical(policy, source!, selected, trace, result),
            CompletedSkillCosts972.FromCanonical(policy, source!, trace, result),
            CompletedAttributeHouseRules993.FromCanonical(policy, source!, selected, attributes, result),
            CompletedCompensation1015.FromCanonical(policy, source!, trace, result));
    }

    internal IReadOnlyDictionary<string, string> EvaluationsFor(CharacterCreationCompleteAssessment authority)
        => authority.Phase == CharacterCreationCompletePhase.Authority
            && authorityDigest == CharacterCreationFinalizationDigest.Compute(authority)
                ? evaluations : new Dictionary<string, string>();

    internal bool IsInactiveFor(CharacterCreationCompleteAssessment authority, string id)
        => authorityDigest == CharacterCreationFinalizationDigest.Compute(authority) && inactive.Contains(id);
}
