using System.Text.Json;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>Projects already admitted consumer inputs; never resolves rules or grants authority.
/// Pure review still invokes each domain evaluator. Live authority remains the owner-bound read.</summary>
internal static class CharacterCreationCompletionConsumerPolicyProjection
{
    internal static CharacterCreationEffectivePolicyContext Attach(
        CharacterCreationEffectivePolicyContext inventory, CharacterCreationCompletionReviewInputs inputs, bool knowledgeModifiers = false)
    {
        var policy = inventory with
        {
            Schema = CharacterCreationEffectivePolicyContext.SchemaV2,
            // Only compensation observations need the new coverage bytes. Other inputs
            // retain V3; validation always reconstructs the version actually recorded.
            Consumers = Project(inputs, compensation: inputs.Skills.KarmaPurchasePolicy?.CompensateSkillGroupKarmaDifference == true,
                knowledgeModifiers: knowledgeModifiers),
            PolicyDigest = string.Empty
        };
        policy = policy with { PolicyDigest = CharacterCreationEffectivePolicyContext.ComputeDigest(policy) };
        return JsonSerializer.Deserialize<CharacterCreationEffectivePolicyContext>(
            JsonSerializer.SerializeToUtf8Bytes(policy))!;
    }

    internal static bool IsValid(CharacterCreationEffectivePolicyContext policy,
        CharacterCreationCompletionReviewInputs inputs)
    {
        if (policy.Schema != CharacterCreationEffectivePolicyContext.SchemaV2
            || policy.Consumers is not { } consumers
            || consumers.Schema is not (CharacterCreationCompletionConsumerPolicy.SchemaV1 or CharacterCreationCompletionConsumerPolicy.SchemaV2
                or CharacterCreationCompletionConsumerPolicy.SchemaV3 or CharacterCreationCompletionConsumerPolicy.SchemaV4
                or CharacterCreationCompletionConsumerPolicy.SchemaV5)
            || consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5 && inputs.Skills.KarmaPurchasePolicy is null
            || consumers.Qualities is null || consumers.Contacts is null || consumers.Coverage is null
            || inputs.Lifestyles?.Qualities?.Authority is not { } qualities
            || inputs.Contacts?.Policy is not { } contacts
            || !qualities.IsAuthoritative || qualities.Blockers is not { Count: 0 }
            || qualities.SettingsProfileId != policy.SettingsProfileId
            || qualities.RulesetId != policy.RulesetId
            || inputs.Lifestyles.Qualities.Binding.AuthorityDigest != qualities.AuthorityDigest
            || contacts.SettingsProfileId != policy.SettingsProfileId
            || contacts.RawProfileInputsDigest != policy.ProfileInputsDigest
            || !CharacterCreationKarmaContactsRules.IsValidPolicy(contacts)
            || consumers.Coverage.Any(row => row is null)) return false;

        bool expanded = consumers.Schema != CharacterCreationCompletionConsumerPolicy.SchemaV1;
        if (expanded ? consumers.Domains is null
                || !CharacterCreationCompletionDomainPolicyProjection.HasAdmittedInputs(policy, inputs)
            : consumers.Domains is not null || inputs.AttributePolicy is not null || inputs.LoadoutLegalityPolicy is not null) return false;
        var expected = Project(inputs, expanded,
            consumers.Schema is CharacterCreationCompletionConsumerPolicy.SchemaV3 or CharacterCreationCompletionConsumerPolicy.SchemaV4 or CharacterCreationCompletionConsumerPolicy.SchemaV5,
            consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV4
                || consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5 && inputs.Skills.KarmaPurchasePolicy?.CompensateSkillGroupKarmaDifference == true,
            consumers.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5);
        // Compare actual typed values, not just caller-recomputed authority hashes.
        // The input digest also binds source/effects, original selections, revisions,
        // all other domain inputs and the presence of loadout/skill-purchase policies.
        // A whole typed comparison rejects omitted, extra, reordered or forged domain claims
        // even when every public digest on the proposal has been recomputed by its caller.
        return CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(consumers, expected);
    }

    private static CharacterCreationCompletionConsumerPolicy Project(CharacterCreationCompletionReviewInputs inputs, bool expanded = true, bool groupKarma = true, bool compensation = false, bool knowledgeModifiers = false)
    {
        var qualities = inputs.Lifestyles.Qualities.Authority;
        var original = inputs with
        {
            Schema = inputs.Loadout is null ? CharacterCreationCompletionReviewSchemas.InputsV1
                : CharacterCreationCompletionReviewSchemas.InputsV2,
            EffectivePolicy = null,
            SnapshotDigest = string.Empty
        };
        return new(knowledgeModifiers ? CharacterCreationCompletionConsumerPolicy.SchemaV5
                : compensation ? CharacterCreationCompletionConsumerPolicy.SchemaV4
                : groupKarma ? CharacterCreationCompletionConsumerPolicy.SchemaV3
                : expanded ? CharacterCreationCompletionConsumerPolicy.SchemaV2 : CharacterCreationCompletionConsumerPolicy.SchemaV1,
            CharacterCreationCompletionReviewRules.ComputeInputsDigest(original),
            new(qualities.AuthorityDigest, qualities.SourceDigest, qualities.ProfileDigest, qualities.GmPolicyDigest, qualities.RuntimeDigest,
                qualities.QualityKarmaLimit, qualities.MayExceedPositiveQualityLimit,
                qualities.MayExceedNegativeQualityLimit, qualities.MetagenicLimit,
                qualities.CostPolicy ?? CharacterCreationQualityCostPolicy.Default),
            inputs.Contacts.Policy, expanded
                ? CharacterCreationCompletionDomainPolicyProjection.DescribeCoverage(inputs, DescribeCoverage(groupKarma), compensation, knowledgeModifiers) : DescribeCoverage(groupKarma))
        {
            Domains = expanded ? CharacterCreationCompletionDomainPolicyProjection.Project(inputs) : null
        };
    }

    // V3 observes the implemented group's source inputs before there is a proposal.
    // Selected legality still requires contact/effect evaluation, cap checks and
    // the shared wallet. False preserves historical V1/V2 coverage byte identity.
    internal static CharacterCreationConsumerPolicyCoverage[] DescribeCoverage(bool groupKarma = false)
        =>
        [
            Missing(CharacterCreationConsumerPolicyOperation.AttributeSpending),
            Missing(CharacterCreationConsumerPolicyOperation.SkillPurchases),
            new(CharacterCreationConsumerPolicyOperation.QualityCostsAndLimits,
                CharacterCreationConsumerPolicyDisposition.Admitted, CharacterCreationConsumerPolicyReason.MatchedConsumerInputs),
            new(CharacterCreationConsumerPolicyOperation.ContactPointAllowance,
                CharacterCreationConsumerPolicyDisposition.Admitted, CharacterCreationConsumerPolicyReason.MatchedConsumerInputs),
            new(CharacterCreationConsumerPolicyOperation.ContactGroupKarma,
                groupKarma ? CharacterCreationConsumerPolicyDisposition.Admitted : CharacterCreationConsumerPolicyDisposition.Unsupported,
                groupKarma ? CharacterCreationConsumerPolicyReason.MatchedConsumerInputs : CharacterCreationConsumerPolicyReason.GroupContactKarmaUnavailable),
            Missing(CharacterCreationConsumerPolicyOperation.MysticAdeptExchange),
            Missing(CharacterCreationConsumerPolicyOperation.ResourcesAndAvailability),
            Missing(CharacterCreationConsumerPolicyOperation.Gear),
            Missing(CharacterCreationConsumerPolicyOperation.LifestylesAndFreeGrid),
            Missing(CharacterCreationConsumerPolicyOperation.Loadout),
            new(CharacterCreationConsumerPolicyOperation.SelectedEffects,
                CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.SelectedEffectsRequireEvaluation),
            new(CharacterCreationConsumerPolicyOperation.Amendments,
                CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.AmendValidationUnavailable),
            new(CharacterCreationConsumerPolicyOperation.Errata,
                CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.ErrataAuthorityUnavailable),
            new(CharacterCreationConsumerPolicyOperation.CharacterMechanics,
                CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.MechanicsCoverageUnavailable),
            new(CharacterCreationConsumerPolicyOperation.Campaign,
                CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.CampaignApplicabilityUnavailable),
            new(CharacterCreationConsumerPolicyOperation.UnclassifiedSettings,
                CharacterCreationConsumerPolicyDisposition.Unsupported, CharacterCreationConsumerPolicyReason.UnclassifiedLegacySetting)
        ];

    private static CharacterCreationConsumerPolicyCoverage Missing(CharacterCreationConsumerPolicyOperation operation)
        => new(operation, CharacterCreationConsumerPolicyDisposition.Missing,
            CharacterCreationConsumerPolicyReason.PolicyProjectionUnavailable);
}
