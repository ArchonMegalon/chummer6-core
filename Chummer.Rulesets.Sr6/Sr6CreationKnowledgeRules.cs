using Chummer.Application.Characters;
using Chummer.Contracts.Characters;

namespace Chummer.Rulesets.Sr6;

/// <summary>Free creation knowledge/languages before Karma, bilingual qualities or augmentation.</summary>
public static class Sr6CreationKnowledgeRules
{
    public const string SourceAnchor = "sr6_core_de_2024:p70,100";

    public static CharacterCreationFoundationResult<Sr6CreationKnowledgePreview> Evaluate(
        Sr6CreationFoundationPreview foundation, Sr6CreationKnowledgeSelection selection)
    {
        if (!Sr6CreationFoundationIntegrity.TryFreezeKnowledge(selection, out var frozen))
            return Fail(Sr6CreationKnowledgeBlockers.InvalidSelection);
        if (foundation.Attributes is not { } attributes)
            return Fail(Sr6CreationKnowledgeBlockers.AttributesRequired);
        int logic = Sr6CreationKarmaRules.AttributeRating(foundation, "Logic");
        var languages = frozen!.Languages.Select(row =>
        {
            int cost = row.Level switch
            {
                Sr6CreationLanguageLevels.Basic => 1,
                Sr6CreationLanguageLevels.Specialist => 2,
                Sr6CreationLanguageLevels.Expert => 3,
                _ => throw new InvalidOperationException("Language level was not frozen.")
            };
            return new Sr6CreationLanguageValue(row.Id, row.Name, row.Level, cost, cost == 1 ? 0 : cost);
        }).ToArray();
        int spent = frozen.KnowledgeSkills.Count + languages.Sum(row => row.PointCost);
        if (spent > logic) return Fail(Sr6CreationKnowledgeBlockers.BudgetExceeded);
        string authority = Sr6CreationFoundationIntegrity.Digest(new
        {
            Schema = "chummer.sr6.creation-knowledge.v1", SourceAnchor, Sr6CreationFoundationRules.CoreSourceSha256,
            foundation.Binding.AuthorityDigest, AttributeAuthorityDigest = attributes.AuthorityDigest,
            Logic = logic, FreeNativeLanguages = 1, KnowledgeCost = 1, LanguageLevelCosts = new[] { 1, 2, 3 },
            ComprehensionBonuses = new[] { 0, 2, 3 }, AdditionalNativeLanguageFromPoints = false
        });
        return new(CharacterCreationFoundationOutcomes.Success,
            new(frozen.NativeLanguage, frozen.KnowledgeSkills, languages, logic, spent, logic - spent,
                spent == logic, frozen.KnowledgeSkills.Count > 0, attributes.AuthorityDigest, authority, [SourceAnchor]), []);
    }

    private static CharacterCreationFoundationResult<Sr6CreationKnowledgePreview> Fail(string reason)
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [reason]);
}
