using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Only called after the saved ledger has been re-evaluated by Load.
/// Does not mutate the ledger or reinterpret pool ratings as Karma increments.</summary>
internal static class Sr6CreationNaturalValuesRules
{
    internal static Sr6CreationNaturalValues? Project(Sr6CreationFoundationState state)
    {
        if (state.Selection is not { } saved) return null;
        var attributes = saved.Attributes?.Values.Select(row =>
        {
            int rating = Sr6CreationKarmaRules.AttributeRating(saved, row.AttributeId);
            return new Sr6CreationNaturalAttributeValue(row.AttributeId, row.BaseValue,
                row.AttributePoints, row.AdjustmentPoints, rating - row.Value, rating, row.Maximum);
        }).ToArray();
        var skills = saved.Skills is null ? null : state.SkillOptions!.Select(option =>
        {
            var pool = saved.Skills.Values.SingleOrDefault(row => row.SkillId == option.SkillId);
            var purchase = saved.Karma?.Skills.SingleOrDefault(row => row.Id == option.SkillId);
            int rating = Sr6CreationKarmaRules.SkillRating(saved, option.SkillId);
            var specialties = new List<Sr6CreationNaturalSpecialization>();
            foreach (string subject in pool?.Specializations ?? [])
                specialties.Add(new(subject, option.SkillId == "ExoticWeapons" ? 0 : Sr6CreationSkillRules.SpecializationDicePoolBonus));
            if (purchase?.FirstExoticSpecialization is { } firstWeapon)
                specialties.Add(new(firstWeapon, 0));
            foreach (var specialty in saved.Karma?.Specializations?.Where(row => row.SkillId == option.SkillId) ?? [])
                specialties.Add(new(specialty.Subject, specialty.DicePoolBonus));
            return new Sr6CreationNaturalSkill(option.SkillId, pool?.Rating ?? 0,
                rating - (pool?.Rating ?? 0), rating, option.Available, specialties.AsReadOnly());
        }).ToArray();

        Sr6CreationNaturalKnowledge? knowledge = null;
        if (saved.Knowledge is { } poolKnowledge)
        {
            var topics = poolKnowledge.KnowledgeSkills.Select(row => row with { }).ToList();
            topics.AddRange((saved.Karma?.Knowledge?.KnowledgeSkills ?? []).Select(row => new Sr6CreationKnowledgeEntry(row.Id, row.Name)));
            var languages = poolKnowledge.Languages.Select(row =>
                new Sr6CreationNaturalLanguage(row.Id, row.Name, row.Level, row.ComprehensionBonus)).ToList();
            foreach (var language in saved.Karma?.Knowledge?.Languages ?? [])
            {
                var combined = new Sr6CreationNaturalLanguage(language.Id, language.Name, language.Level, language.ComprehensionBonus);
                int index = languages.FindIndex(row => row.Id == language.Id);
                if (index >= 0) languages[index] = combined;
                else languages.Add(combined);
            }
            knowledge = new(poolKnowledge.NativeLanguage, topics.AsReadOnly(), languages.AsReadOnly());
        }
        return new(attributes is null ? null : Array.AsReadOnly(attributes),
            skills is null ? null : Array.AsReadOnly(skills), knowledge);
    }
}
