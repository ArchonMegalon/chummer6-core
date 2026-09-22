using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static class CharacterCreationLifeModuleResourcesRules
{
    internal static CharacterCreationLifeModuleResourcesQuoteResult Evaluate(string characterXml,
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, decimal totalKarma, decimal? investment,
        ICharacterSourceDataContext context)
    {
        try
        {
            if (!context.TryResolveCreationLifeModuleResourcesPolicy(out var policy) || policy is null)
                return Failed(CharacterCreationResourcesBlockers.AuthorityUnavailable);
            var currentSkills = CharacterCreationLifeModuleSkillsRules.Evaluate(characterXml, effects, racial, talent,
                attributes, skills.Selection, context);
            if (currentSkills.Quote is null
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(skills, currentSkills.Quote))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            var result = Quote(effects, racial, talent, attributes, skills, policy, totalKarma, investment);
            if (!context.TryResolveCreationLifeModuleResourcesPolicy(out var finalPolicy)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(policy, finalPolicy))
                return Failed(CharacterCreationFoundationBlockers.SourceDigestConflict);
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { return Failed(CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired); }
    }

    // Called with source-revalidated component quotes above. Hash checks here
    // protect composition, not a substitute for admitting current source data.
    internal static CharacterCreationLifeModuleResourcesQuoteResult Quote(
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial,
        CharacterCreationLifeModuleTalentWritePlan talent, CharacterCreationLifeModuleAttributeQuote attributes,
        CharacterCreationLifeModuleSkillsQuote skills, CharacterCreationKarmaResourcesPolicy policy,
        decimal totalKarma, decimal? investment)
    {
        try
        {
            if (!CharacterCreationKarmaResourcesRules.IsValidSpendingPolicy(policy, CharacterCreationKarmaResourcesPolicy.LifeModulesSchemaV1)
                || totalKarma is < 0 or > int.MaxValue || decimal.Truncate(totalKarma) != totalKarma
                || effects.ModuleKarmaCost < 0 || racial.Metatype.KarmaCost < 0 || talent.Talent.KarmaCost < 0
                || attributes.KarmaUsed < 0 || skills.KarmaUsed < 0
                || effects.PlanDigest != Hash(effects with { PlanDigest = string.Empty })
                || racial.PlanDigest != Hash(racial with { PlanDigest = string.Empty })
                || talent.PlanDigest != Hash(talent with { PlanDigest = string.Empty })
                || attributes.QuoteDigest != Hash(attributes with { QuoteDigest = string.Empty })
                || skills.QuoteDigest != Hash(skills with { QuoteDigest = string.Empty })
                || skills.EffectPlanDigest != effects.PlanDigest || skills.MetatypePlanDigest != racial.PlanDigest
                || skills.TalentPlanDigest != talent.PlanDigest || skills.AttributeQuoteDigest != attributes.QuoteDigest
                || attributes.EffectPlanDigest != effects.PlanDigest || attributes.MetatypePlanDigest != racial.PlanDigest
                || attributes.TalentPlanDigest != talent.PlanDigest
                || racial.EffectPlanDigest != effects.PlanDigest || talent.EffectPlanDigest != effects.PlanDigest
                || talent.MetatypePlanDigest != racial.PlanDigest
                || skills.Policy.Schema != CharacterCreationKarmaSkillsPolicy.LifeModulesSchemaV1
                || skills.Policy.SettingsProfileId != policy.SettingsProfileId || skills.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest
                || attributes.Policy.SettingsProfileId != policy.SettingsProfileId || attributes.Policy.RawProfileInputsDigest != policy.RawProfileInputsDigest)
                return Failed(CharacterCreationResourcesBlockers.AuthorityUnavailable);
            if (!investment.HasValue)
                return new(policy, null, [CharacterCreationLifeModuleResourcesQuote.SelectionRequired]);
            if (!CharacterCreationKarmaResourcesRules.TryFundingAmount(policy.FundingExpression, investment.Value,
                attributes.Attributes.ToDictionary(row => row.AttributeId, row => row.Current), out decimal nuyen))
                return new(policy, null, [CharacterCreationLifeModuleResourcesQuote.InvestmentInvalid]);
            decimal available = checked(totalKarma - effects.ModuleKarmaCost - racial.Metatype.KarmaCost
                - talent.Talent.KarmaCost - attributes.KarmaUsed - skills.KarmaUsed);
            var blockers = new HashSet<string>(attributes.Blockers.Concat(skills.Blockers), StringComparer.Ordinal);
            if (investment.Value > policy.MaximumKarmaInvestment)
                blockers.Add(CharacterCreationKarmaResourcesRules.InvestmentLimitExceeded);
            if (investment.Value > available) blockers.Add(CharacterCreationAttributesBlockers.GlobalKarmaExceeded);
            var result = new CharacterCreationLifeModuleResourcesQuote(policy, effects.PlanDigest, racial.PlanDigest,
                talent.PlanDigest, attributes.QuoteDigest, skills.QuoteDigest, totalKarma, available, investment.Value,
                nuyen, checked(available - investment.Value), blockers.Order(StringComparer.Ordinal).ToArray(), string.Empty);
            return new(policy, result with { QuoteDigest = Hash(result) }, result.Blockers);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
        { return Failed(CharacterCreationLifeModuleResourcesQuote.InvestmentInvalid); }
    }

    private static string Hash<T>(T value) => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
    private static CharacterCreationLifeModuleResourcesQuoteResult Failed(string blocker) => new(null, null, [blocker]);
}

internal sealed record CharacterCreationLifeModuleResourcesQuoteResult(CharacterCreationKarmaResourcesPolicy? Policy,
    CharacterCreationLifeModuleResourcesQuote? Quote, IReadOnlyList<string> Blockers);
