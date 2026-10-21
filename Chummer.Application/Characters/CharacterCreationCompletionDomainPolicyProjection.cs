using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>One projection over actual original inputs. No source lookup, default policy,
/// repricing, selected-effect evaluation or owner authority is supplied by this helper.</summary>
internal static class CharacterCreationCompletionDomainPolicyProjection
{
    internal static CharacterCreationCompletionDomainPolicies Project(CharacterCreationCompletionReviewInputs inputs)
    {
        var attributes = inputs.Skills.Attributes;
        var skills = inputs.Skills.Authority;
        var magic = inputs.Magic.Authority;
        var talent = inputs.Magic.SelectedTalent;
        var resources = inputs.Lifestyles.Gear.Resources;
        var cash = resources.Authority;
        var gear = inputs.Lifestyles.Gear.State.Authority;
        var lifestyles = inputs.Lifestyles.Authority;
        var loadout = inputs.Loadout?.Authority;
        return new(
            new(inputs.AttributePolicy, attributes.KarmaAttribute, attributes.MaxNumberMaxAttributesCreate,
                attributes.Binding.PrerequisiteAuthorityDigest, attributes.Attributes),
            new(skills.AuthorityDigest, skills.RawProfileInputsDigest, skills.EffectiveSkillsInputsDigest, skills.RuntimeDigest,
                skills.KnowledgePointsExpression, skills.MaxActiveSkillRatingCreate, skills.MaxKnowledgeSkillRatingCreate,
                skills.MaxSkillGroupRatingCreate, skills.BaseNativeLanguageLimit, skills.UsePointsOnBrokenGroups,
                skills.StrictSkillGroupsInCreateMode, skills.SpecializationsBreakSkillGroups,
                inputs.Skills.KarmaPurchasePolicy, skills.TalentAccess, inputs.Skills.KnowledgePointContributions,
                inputs.Skills.MovementCapability, inputs.Skills.SelectedActiveSkillPoints, inputs.Skills.SelectedSkillGroupPoints),
            new(magic.AuthorityDigest, magic.PrerequisiteAuthorityDigest, magic.SourceInputsDigest,
                magic.CustomDataInputsDigest, magic.GmPolicyDigest, magic.RuntimeDigest,
                talent.Identity, CharacterCreationFinalizationDigest.Compute(talent),
                talent.Magic, talent.Resonance, talent.Depth, talent.SpellBudget, talent.ComplexFormBudget,
                talent.AdeptPowerPointBudget, talent.RequiresTradition, talent.RequiresStream,
                talent.AllowsAdeptPowers, talent.AllowsSpells, talent.AllowsComplexForms, magic.MysticAdeptPowerPointPolicy),
            new(cash.AuthorityDigest, cash.SourceDigest, cash.ProfileDigest, cash.RulesDigest, cash.RuntimeDigest,
                cash.KarmaToNuyenRate, cash.MaximumKarmaInvestment, cash.NuyenCarryover, cash.MaximumAvailability,
                cash.UnrestrictedNuyen, resources.PriorityOption, resources.PurchaseAuthorityExact, resources.KnownPurchaseCost),
            new(gear.AuthorityDigest, gear.SourceDigest, gear.ProfileDigest, gear.RulesDigest, gear.RuntimeDigest,
                gear.MaximumAvailability, gear.MaximumBasketLines, gear.MaximumQuantityPerLine,
                CharacterCreationFinalizationDigest.Compute(gear.Options)),
            new(lifestyles.AuthorityDigest, lifestyles.SourceDigest, lifestyles.ProfileDigest, lifestyles.GmPolicyDigest,
                lifestyles.RuntimeDigest, lifestyles.TrustFundLevel, lifestyles.FreeGridsEnabled, lifestyles.MetatypeCostPercent,
                CharacterCreationFinalizationDigest.Compute(lifestyles.LifestyleOptions),
                CharacterCreationFinalizationDigest.Compute(lifestyles.QualityOptions)),
            loadout is null ? null : new(loadout.AuthorityDigest, loadout.SourceDigest, loadout.ProfileDigest,
                loadout.RulesDigest, loadout.RuntimeDigest, loadout.MaximumAvailability, loadout.MaximumBasketLines,
                loadout.MaximumQuantityPerLine, CharacterCreationFinalizationDigest.Compute(loadout.Options))
                { LoadoutLegality = inputs.LoadoutLegalityPolicy });
    }

    internal static bool HasAdmittedInputs(CharacterCreationEffectivePolicyContext inventory,
        CharacterCreationCompletionReviewInputs inputs)
    {
        if (inputs.Skills?.Attributes?.Attributes is null || inputs.Skills.Attributes.Binding is null
            || inputs.Skills.Authority is not { IsAuthoritative: true, Blockers.Count: 0 } skills
            || inputs.Skills.KnowledgePointContributions is null
            || inputs.Magic?.SelectedTalent is null
            || inputs.Magic.Authority is not { IsAuthoritative: true, Blockers.Count: 0 } magic
            || inputs.Lifestyles?.Authority is not { IsAuthoritative: true, Blockers.Count: 0 } lifestyles
            || inputs.Lifestyles.Gear?.State?.Authority is not { IsAuthoritative: true, Blockers.Count: 0 } gear
            || inputs.Lifestyles.Gear.Resources?.Authority is not { IsAuthoritative: true, Blockers.Count: 0 } resources
            || inputs.Lifestyles.Gear.Resources.PriorityOption is null
            || magic.SettingsProfileId != inventory.SettingsProfileId
            || lifestyles.SettingsProfileId != inventory.SettingsProfileId || lifestyles.ProfileDigest != inventory.ProfileInputsDigest
            || gear.SettingsProfileId != inventory.SettingsProfileId || gear.ProfileDigest != inventory.ProfileInputsDigest
            || resources.SettingsProfileId != inventory.SettingsProfileId || resources.ProfileDigest != inventory.ProfileInputsDigest
            || resources.BuildMethod != inventory.BuildMethod
            || magic.PrerequisiteAuthorityDigest != inputs.Skills.PrerequisiteDraft.AuthorityDigest
            || inputs.Skills.KarmaPurchasePolicy is { } purchase
                && !CharacterCreationSkillsService.IsCompletionKarmaPolicyValid(purchase, skills)) return false;

        if (!CharacterCreationAttributePolicyAuthority.MatchesHouseRules(inputs.AttributePolicy, inputs.Skills.Attributes))
            return false;
        if (inputs.AttributePolicy is { } attribute
            && (attribute.Schema != CharacterCreationAttributePolicy.SchemaV1
                || attribute.SettingsProfileId != inventory.SettingsProfileId
                || attribute.BuildMethod != inventory.BuildMethod
                || attribute.RawProfileInputsDigest != inventory.ProfileInputsDigest
                || attribute.KarmaAttribute != inputs.Skills.Attributes.KarmaAttribute
                || attribute.MaxNumberMaxAttributesCreate != inputs.Skills.Attributes.MaxNumberMaxAttributesCreate
                || attribute.SourceAnchorIds is not { Count: > 0 }
                || attribute.AuthorityDigest != CharacterCreationAttributePolicyAuthority.ComputeDigest(attribute))) return false;

        if (magic.MysticAdeptPowerPointPolicy is { } mystic
            && (mystic.SettingsProfileId != inventory.SettingsProfileId
                || !CharacterCreationMysticAdeptPowerPointRules.IsValidPolicy(mystic))) return false;

        if (inputs.LoadoutLegalityPolicy is { } legality
            && (inputs.Loadout is null || legality.SettingsProfileId != inventory.SettingsProfileId
                || legality.RawProfileInputsDigest != inventory.ProfileInputsDigest
                || legality.PolicyDigest != CharacterCreationLoadoutLegalityPolicy.ComputeDigest(legality)
            || legality.Lifecycle is not null && !legality.HasCreationLifecycle(inputs.Skills.Binding.RawCharacterXmlDigest)
                || inputs.Loadout.Authority is null
                || inputs.Loadout.Authority.RulesDigest != legality.ComputeRulesDigest(inputs.Loadout.Authority.MaximumAvailability))) return false;

        return inputs.Loadout is null || inputs.Loadout.Authority is { IsAuthoritative: true, Blockers.Count: 0 } loadout
            && loadout.SettingsProfileId == inventory.SettingsProfileId && loadout.ProfileDigest == inventory.ProfileInputsDigest;
    }

    internal static IReadOnlyList<CharacterCreationConsumerPolicyCoverage> DescribeCoverage(
        CharacterCreationCompletionReviewInputs inputs, IReadOnlyList<CharacterCreationConsumerPolicyCoverage> legacy,
        bool compensation = false, bool knowledgeModifiers = false)
        => DescribeCoverage(inputs.AttributePolicy is not null, inputs.Skills.KarmaPurchasePolicy is not null,
            inputs.Magic.Authority.MysticAdeptPowerPointPolicy, inputs.Loadout is not null,
            inputs.LoadoutLegalityPolicy is not null, legacy,
            inputs.Skills.Attributes.HouseRules is not null
                && CharacterCreationAttributePolicyAuthority.MatchesHouseRules(inputs.AttributePolicy, inputs.Skills.Attributes),
            inputs.LoadoutLegalityPolicy?.HasCreationLifecycle(inputs.Skills.Binding.RawCharacterXmlDigest) == true,
            compensation ? inputs.Skills : null, knowledgeModifiers);

    internal static IReadOnlyList<CharacterCreationConsumerPolicyCoverage> DescribeCoverage(
        bool attributes, bool skills, CharacterCreationMysticAdeptPowerPointPolicy? mystic,
        bool loadout, bool legality, IReadOnlyList<CharacterCreationConsumerPolicyCoverage> legacy,
        bool evaluatedAttributeHouseRules = false, bool creationLegalityLifecycle = false,
        CharacterCreationSkillsCompletionInputs? compensationInputs = null, bool knowledgeModifiers = false)
    {
        // Policy presence never converts selected-effect, campaign or amendment gaps to success.
        var coverage = legacy.Select(row => row.Operation switch
        {
            CharacterCreationConsumerPolicyOperation.AttributeSpending => Observed(row.Operation, attributes),
            CharacterCreationConsumerPolicyOperation.SkillPurchases => Observed(row.Operation, skills),
            CharacterCreationConsumerPolicyOperation.MysticAdeptExchange =>
                mystic is { UsesSeparateMagicAttribute: true }
                    ? Unsupported(row.Operation) : Observed(row.Operation, mystic is not null),
            CharacterCreationConsumerPolicyOperation.ResourcesAndAvailability or CharacterCreationConsumerPolicyOperation.Gear
                or CharacterCreationConsumerPolicyOperation.LifestylesAndFreeGrid => Observed(row.Operation, true),
            CharacterCreationConsumerPolicyOperation.Loadout => Observed(row.Operation, loadout),
            _ => row
        }).ToList();
        coverage.Add(Observed(CharacterCreationConsumerPolicyOperation.MagicAndResonance, true));
        coverage.Add(Observed(CharacterCreationConsumerPolicyOperation.SkillPointRules, true));
        // Preserve historical false/false coverage bytes. A versioned configured
        // input earns coverage only after the shared allocation evaluator matches.
        coverage.Add(evaluatedAttributeHouseRules
            ? Observed(CharacterCreationConsumerPolicyOperation.AttributeHouseRules, true)
            : Unsupported(CharacterCreationConsumerPolicyOperation.AttributeHouseRules));
        coverage.Add(Unsupported(CharacterCreationConsumerPolicyOperation.SkillKarmaModifiers));
        coverage.Add(compensationInputs is null
            ? Unsupported(CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation)
            : DescribeCompensationCoverage(compensationInputs));
        coverage.Add(creationLegalityLifecycle
            ? CareerOnly(CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions)
            : Observed(CharacterCreationConsumerPolicyOperation.LoadoutLegalityExclusions, legality));
        coverage.Add(creationLegalityLifecycle
            ? CareerOnly(CharacterCreationConsumerPolicyOperation.LoadoutLegalityMultiplierPricing)
            : Unsupported(CharacterCreationConsumerPolicyOperation.LoadoutLegalityMultiplierPricing));
        coverage.Add(Unsupported(CharacterCreationConsumerPolicyOperation.MysticAdeptSeparateMagic));
        coverage.Add(Unsupported(CharacterCreationConsumerPolicyOperation.LifestyleCostEffects));
        coverage.Add(new(CharacterCreationConsumerPolicyOperation.LifestyleGrantEffects,
            CharacterCreationConsumerPolicyDisposition.Missing, CharacterCreationConsumerPolicyReason.SelectedEffectsRequireEvaluation));
        // The existing lifestyle evaluator consumes the admitted racial percentage.
        // General quality cost effects still require unsupported modifier evaluation.
        coverage.Add(Observed(CharacterCreationConsumerPolicyOperation.LifestyleMetatypeCost, true));
        if (knowledgeModifiers)
            coverage.Add(Observed(CharacterCreationConsumerPolicyOperation.KnowledgeCategoryPricing, skills));
        return coverage.ToArray();
    }

    private static CharacterCreationConsumerPolicyCoverage DescribeCompensationCoverage(CharacterCreationSkillsCompletionInputs inputs)
    {
        const CharacterCreationConsumerPolicyOperation operation = CharacterCreationConsumerPolicyOperation.SkillGroupKarmaCompensation;
        if (inputs.KarmaPurchasePolicy is null || inputs.KarmaPurchasePolicy.AllowSkillRegrouping is null
            || inputs.KarmaCompensationSourceOrder is null)
            return Observed(operation, false);
        return CharacterCreationSkillsService.HasCompletionCompensationInputs(inputs)
            ? Observed(operation, true) : Unsupported(operation);
    }

    private static CharacterCreationConsumerPolicyCoverage CareerOnly(CharacterCreationConsumerPolicyOperation operation)
        => new(operation, CharacterCreationConsumerPolicyDisposition.NotApplicable,
            CharacterCreationConsumerPolicyReason.CareerPurchaseOnly);

    private static CharacterCreationConsumerPolicyCoverage Observed(CharacterCreationConsumerPolicyOperation operation, bool present)
        => new(operation, present ? CharacterCreationConsumerPolicyDisposition.Admitted : CharacterCreationConsumerPolicyDisposition.Missing,
            present ? CharacterCreationConsumerPolicyReason.MatchedConsumerInputs : CharacterCreationConsumerPolicyReason.OriginalInputUnavailable);

    private static CharacterCreationConsumerPolicyCoverage Unsupported(CharacterCreationConsumerPolicyOperation operation)
        => new(operation, CharacterCreationConsumerPolicyDisposition.Unsupported, CharacterCreationConsumerPolicyReason.ConsumerOperationUnavailable);
}
