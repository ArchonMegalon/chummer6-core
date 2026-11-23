using Chummer.Contracts.Characters;
using V = Chummer.Application.Characters.CharacterRuleEffectValues2912;
using M = Chummer.Contracts.Characters.CharacterRuleEffectMetric;

namespace Chummer.Application.Characters;

public static partial class CharacterCurrentRuleEnvironmentConsumers
{
    private static IEnumerable<CharacterRuleEffectValue> OriginEffects2912(int? normal, int? special,
        CharacterCreationMetatypeOptionProjection? metatype)
    {
        yield return V.Number("priority-attributes", "Normal attribute points", M.BudgetTotal, normal,
            reason: "The current priority selection does not yet determine normal attribute points.");
        yield return V.Number("priority-special", "Special attribute points", M.BudgetTotal, special,
            reason: "The current heritage/talent selection does not yet determine special attribute points.");
        if (metatype is not null)
            yield return V.Number(metatype.OptionId, metatype.Label, M.RuleKarmaCost, metatype.KarmaCost, "metatype");
    }

    private static IEnumerable<CharacterRuleEffectValue> KarmaQualityEffects2912(CharacterCreationKarmaQualitiesQuote? quote)
    {
        if (quote is null) yield break;
        yield return V.Number("qualities", "Positive qualities", M.PositiveKarma, quote.Costs.PositiveKarmaSpent);
        yield return V.Number("qualities", "Negative qualities", M.NegativeKarma, quote.Costs.NegativeKarmaGranted);
        yield return V.Number("qualities", "Net quality cost", M.KarmaCost, quote.Costs.NetKarmaSpent);
        yield return V.Number("qualities", "Positive quality limit", M.BudgetUsed, quote.Costs.PositiveLimitKarma, "positive");
        yield return V.Number("qualities", "Negative quality limit", M.BudgetUsed, quote.Costs.NegativeLimitKarma, "negative");
    }

    private static IEnumerable<CharacterRuleEffectValue> KarmaMagicEffects2912(
        IEnumerable<CharacterCreationMagicResonanceOptionFinalizationSource> options, CharacterCreationKarmaMagicQuote? quote)
    {
        foreach (var row in V.Magic(options)) yield return row;
        if (quote is null) yield break;
        yield return V.Number("magic-budget", "Power points", M.BudgetTotal, quote.PowerPointsTotal);
        yield return V.Number("magic-budget", "Power points", M.BudgetUsed, quote.PowerPointsUsed);
        yield return V.Number("magic-budget", "Spell limit", M.Maximum, quote.SpellLimit, "spells");
        yield return V.Number("magic-budget", "Complex form limit", M.Maximum, quote.ComplexFormLimit, "complex-forms");
    }

    private static IEnumerable<CharacterRuleEffectValue> PowerEffects2912(CharacterCareerPowerGraph.ReadGraph graph)
    {
        yield return V.Number("power-points", "Remaining power points", M.BudgetRemaining, graph.RemainingPoints);
        foreach (var power in graph.Owned)
        {
            string id = $"power/{power.SourceId:D}/{power.ChoiceLabel}";
            yield return V.Number(id, power.Name, M.Rating, power.TotalRating,
                reason: power.Blockers.Count == 0 ? null : string.Join("; ", power.Blockers));
            int index = 0;
            foreach (var effect in power.Effects.OrderBy(e => e.Kind, StringComparer.Ordinal)
                .ThenBy(e => e.Target, StringComparer.Ordinal).ThenBy(e => e.Value))
                yield return V.Number(id, power.Name, M.Modifier, effect.Value, $"{effect.Kind}/{effect.Target}/{index++}");
        }
        foreach (var option in graph.Options.Where(o => o.Existing))
        {
            string id = "paid-power/" + option.Identity;
            yield return V.Number(id, option.Source.Name, M.Rating, option.CurrentPaidRating);
            yield return V.Number(id, option.Source.Name, M.Maximum, option.MaximumRating);
            yield return V.Flag(id, option.Source.Name, M.Condition, option.Discounted, "way-discount");
        }
    }

    private static IEnumerable<CharacterRuleEffectValue> ProfileEffects2912(
        CharacterCreationAttributesState? attributes, CharacterCreationResourcesState? funding)
        => V.Attributes(attributes?.Attributes ?? []).Concat(V.Budget(attributes?.NormalPointBudget))
            .Concat(V.Budget(attributes?.SpecialPointBudget)).Concat(V.Budget(attributes?.CreationKarmaBudget))
            .Concat(V.Resources(funding?.Budget));

    private static IEnumerable<CharacterRuleEffectValue> NativeProfileEffects2912(CurrentNativeMethod1958 method,
        IReadOnlyList<CharacterCareerAttributeAdvanceQuote> career, CharacterCurrentRuleStage stage)
    {
        foreach (var row in V.CareerAttributes(career)) yield return row;
        if (stage == CharacterCurrentRuleStage.Career) yield break;
        var quote = method.Karma?.Quote;
        foreach (var row in V.Budget(quote?.KarmaBudget ?? method.Karma?.State.KarmaBudget ?? method.LifeModules?.State.LifeModuleBudget))
            yield return row;
        foreach (var row in V.Attributes(quote?.Attributes?.Attributes ?? [])) yield return row;
        if (quote?.Resources is { } resources)
        {
            yield return V.Number("resources", "Karma funding", M.KarmaInvestment, resources.KarmaInvestment);
            yield return V.Number("resources", "Karma funding", M.NuyenFromKarma, resources.NuyenFromKarma);
            yield return V.Number("resources", "Karma funding", M.KarmaRemaining, resources.KarmaAvailable);
        }
        if (method.LifeModules?.Attributes is { } attributes)
        {
            foreach (var a in attributes.Attributes)
            {
                yield return V.Number(a.AttributeId, a.AttributeId, M.Unaugmented, a.Current);
                yield return V.Number(a.AttributeId, a.AttributeId, M.Minimum, a.Minimum);
                yield return V.Number(a.AttributeId, a.AttributeId, M.Maximum, a.Maximum);
                yield return V.Number(a.AttributeId, a.AttributeId, M.KarmaCost, a.KarmaCost);
                yield return V.Number(a.AttributeId, a.AttributeId, M.AllocatedBase, a.AppliedModuleLevels, "module-levels");
            }
        }
    }

    private static IEnumerable<CharacterRuleEffectValue> SkillsEffects2912(SkillsOutcome1949 output)
    {
        if (output.Creation is { } creation)
        {
            foreach (var skill in creation.Skills)
            {
                yield return V.Number(skill.SourceSkillId, skill.Name, M.Rating, skill.EffectiveRating);
                yield return V.Number(skill.SourceSkillId, skill.Name, M.LearnedRating, skill.Rating);
                yield return V.Number(skill.SourceSkillId, skill.Name, M.PriorityCost, skill.PointCost);
                yield return V.Flag(skill.SourceSkillId, skill.Name, M.Enabled, skill.IsEnabled);
                yield return V.Flag(skill.SourceSkillId, skill.Name, M.NativeLanguage, skill.IsNativeLanguage);
                foreach (string blocker in skill.Blockers) yield return V.Text(skill.SourceSkillId, skill.Name, M.Condition, blocker, blocker);
            }
            foreach (var group in creation.Groups)
            {
                string id = "group/" + group.GroupId;
                yield return V.Number(id, group.Name, M.Rating, group.Rating);
                yield return V.Number(id, group.Name, M.PriorityCost, group.PointCost);
                yield return V.Flag(id, group.Name, M.Enabled, group.IsEnabled);
            }
            foreach (var row in V.Budget(creation.ActiveBudget).Concat(V.Budget(creation.GroupBudget)).Concat(V.Budget(creation.KnowledgeBudget))) yield return row;
            foreach (string blocker in creation.CanonicalBlockers) yield return V.Text("skills", "Skill selection", M.Condition, blocker, blocker);
        }
        if (output.Career is not { } career) yield break;
        foreach (var skill in career.Active)
        {
            string id = "active/" + skill.Identity;
            foreach (var row in AdvanceSkillEffects2912(id, skill.Name, skill.TotalBaseRating, skill.RatingMaximum,
                skill.AvailableKarma, skill.KarmaCost, skill.CanAdvance, skill.Blocker.ToString())) yield return row;
        }
        foreach (var skill in career.Knowledge)
        {
            string id = "knowledge/" + skill.Identity;
            foreach (var row in AdvanceSkillEffects2912(id, skill.Name, skill.TotalBaseRating, skill.RatingMaximum,
                skill.AvailableKarma, skill.KarmaCost, skill.CanAdvance, skill.Blocker.ToString())) yield return row;
            yield return V.Flag(id, skill.Name, M.NativeLanguage, skill.IsNativeLanguage);
        }
        foreach (var group in career.Groups)
        {
            string id = "group/" + group.Identity;
            foreach (var row in AdvanceSkillEffects2912(id, group.Name, group.GroupRating, group.RatingMaximum,
                group.AvailableKarma, group.KarmaCost, group.CanAdvance, group.Blocker.ToString())) yield return row;
            yield return V.Flag(id, group.Name, M.Enabled, !group.Disabled);
            yield return V.Flag(id, group.Name, M.Condition, group.Broken, "broken-group");
        }
        foreach (var option in career.Specializations)
        {
            string id = $"specialization/{option.Identity}/{option.Selection}";
            yield return V.Number(id, option.SkillName, M.KarmaCost, option.KarmaCost, "add-specialization");
            yield return V.Number(id, option.SkillName, M.KarmaRemaining, option.AvailableKarma);
            yield return V.Number(id, option.SkillName, M.Rating, option.TotalBaseRating);
            yield return V.Flag(id, option.SkillName, M.Available, option.CanAdd, "add-specialization");
            yield return V.Flag(id, option.SkillName, M.Enabled, option.Enabled);
            yield return V.Flag(id, option.SkillName, M.Condition, option.SkillSpecializationsBlocked, "skill-blocked");
            yield return V.Flag(id, option.SkillName, M.Condition, option.SkillCategorySpecializationsBlocked, "category-blocked");
            yield return V.Flag(id, option.SkillName, M.Condition, option.WillBreakSkillGroup, "will-break-group");
            yield return V.Text(id, option.SkillName, M.Condition, option.Blocker.ToString(), "add-specialization");
        }
    }

    private static IEnumerable<CharacterRuleEffectValue> AdvanceSkillEffects2912(string id, string name,
        int rating, int maximum, int availableKarma, int cost, bool available, string blocker)
    {
        yield return V.Number(id, name, M.Rating, rating);
        yield return V.Number(id, name, M.Maximum, maximum);
        yield return V.Number(id, name, M.KarmaRemaining, availableKarma);
        yield return V.AdvanceCost(id, name, cost, blocker);
        yield return V.Flag(id, name, M.Available, available, "next-advance");
        yield return V.Text(id, name, M.Condition, blocker, "next-advance");
    }

    private static IEnumerable<CharacterRuleEffectValue> KarmaSkillsEffects2912(CharacterCreationKarmaSkillsQuote? quote)
    {
        if (quote is null) yield break;
        foreach (var skill in quote.Skills)
        {
            string id = skill.Allocation.SourceSkillId;
            yield return V.Number(id, skill.Name, M.Rating, skill.Rating);
            yield return V.Number(id, skill.Name, M.KarmaCost, skill.KarmaCost);
            yield return V.Number(id, skill.Name, M.KarmaCost, skill.SpecializationKarmaCost, "specialization");
            yield return V.Number(id, skill.Name, M.PriorityCost, skill.KnowledgePointCost, "knowledge");
            yield return V.Flag(id, skill.Name, M.Enabled, skill.IsEnabled);
        }
        foreach (var group in quote.Groups)
        {
            string id = "group/" + group.Allocation.GroupId;
            yield return V.Number(id, group.Name, M.KarmaCost, group.KarmaCost);
            yield return V.Flag(id, group.Name, M.Condition, group.IsBroken, "broken-group");
        }
        yield return V.Number("skills", "Karma skill purchases", M.BudgetUsed, quote.KarmaUsed);
        yield return V.Number("skills", "Knowledge points", M.KnowledgePoints, quote.KnowledgePointsTotal);
        yield return V.Number("skills", "Knowledge points", M.KnowledgePointsUsed, quote.KnowledgePointsUsed);
        yield return V.Number("skills", "Native languages", M.NativeLanguages, quote.NativeLanguagesUsed);
    }

    private static IEnumerable<CharacterRuleEffectValue> ModuleSkillsEffects2912(CharacterCreationLifeModuleSkillsQuote? quote)
    {
        if (quote is null) yield break;
        foreach (var skill in quote.Skills)
        {
            string id = skill.Allocation.SourceSkillId;
            yield return V.Number(id, skill.Name, M.Rating, skill.Rating);
            yield return V.Number(id, skill.Name, M.KarmaCost, skill.KarmaCost);
            yield return V.Number(id, skill.Name, M.KarmaCost, skill.SpecializationKarmaCost, "specialization");
            yield return V.Number(id, skill.Name, M.PriorityCost, skill.KnowledgePointCost, "knowledge");
            yield return V.Flag(id, skill.Name, M.Enabled, skill.IsEnabled);
        }
        foreach (var group in quote.Groups)
        {
            string id = "group/" + group.Allocation.GroupId;
            yield return V.Number(id, group.Name, M.Rating, group.EffectiveLevels);
            yield return V.Number(id, group.Name, M.KarmaCost, group.KarmaCost);
            yield return V.Flag(id, group.Name, M.Enabled, group.IsEnabled);
            yield return V.Flag(id, group.Name, M.Condition, group.IsBroken, "broken-group");
        }
        yield return V.Number("skills", "Karma skill purchases", M.BudgetUsed, quote.KarmaUsed);
        yield return V.Number("skills", "Knowledge points", M.KnowledgePoints, quote.KnowledgePointsTotal);
        yield return V.Number("skills", "Knowledge points", M.KnowledgePointsUsed, quote.KnowledgePointsUsed);
        yield return V.Number("skills", "Native languages", M.NativeLanguages, quote.NativeLanguagesUsed);
    }
}
