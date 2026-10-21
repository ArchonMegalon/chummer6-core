using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class Mechanics695Execution
{
    // Called only after the canonical full skills replay has completed. It
    // evaluates the actual selected inventory, including native-language state;
    // missing prices on rated knowledge skills are never evidence of inactivity.
    internal sealed record KnowledgeTarget711(string SourceId, string Category, bool Native,
        bool MatchesCategory, int PointCost, int KarmaCost, CharacterCreationKnowledgeCategoryPrice? Price);
    internal sealed record KnowledgeInactivity711(string AuthorityDigest,
        IReadOnlyList<CharacterCreationLegacySourceProjector.SourceQualityEffect> Effects,
        IReadOnlyList<KnowledgeTarget711> Targets, int RatedMatchingTargets, int MatchingPointCost, int MatchingKarmaCost);

    internal static bool TryKnowledgeInactivity711(CharacterCreationAttributeSkillsPreview actual,
        CharacterCreationSkillsAuthority authority,
        IReadOnlyList<CharacterCreationLegacySourceProjector.SourceQualityEffect> effects, out object? result)
    {
        result = null;
        if (!authority.IsAuthoritative || authority.Blockers.Count != 0 || authority.KnowledgeSkills.Count == 0
            || actual.Skills.Count == 0 || effects.Count == 0
            || effects.Any(effect => !CharacterCreationKnowledgeCategoryRules.IsSupportedType(effect.ImprovementType)
                || !CharacterCreationKnowledgeCategoryRules.IsSupportedCondition(effect.Condition)
                || !authority.KnowledgeSkills.Any(row => row.Category == effect.ImprovedName))) return false;
        var targets = new List<KnowledgeTarget711>();
        foreach (var skill in actual.Skills.Where(row => row.Kind == CharacterCreationSkillKinds.Knowledge))
        {
            var source = authority.KnowledgeSkills.SingleOrDefault(row => row.SourceSkillId == skill.SourceSkillId);
            if (source is null || source.Category != skill.Category || !skill.IsEnabled || skill.Blockers.Count != 0) return false;
            bool matches = effects.Any(effect => effect.ImprovedName == source.Category);
            if (skill.IsNativeLanguage)
            {
                // The canonical allocation consumer exempts a non-rated native
                // language before pricing. Validate its concrete resulting state.
                if (skill.Rating is not null || skill.EffectiveRating is not null || skill.PointCost != 0
                    || skill.KarmaCost != 0 || skill.KarmaLevels != 0 || skill.SpecializationOptionId is not null)
                    return false;
            }
            else
            {
                // An actual target needs its actual price; never turn an absent
                // or malformed price into a zero-target proof.
                if (matches || skill.KnowledgeCategoryPrice is not { } price
                    || price.Category != source.Category || price.PointCost != skill.PointCost || price.KarmaCost != skill.KarmaCost
                    || !effects.All(effect => price.Effects.Any(applied =>
                        applied.SourceId == effect.SourceId && applied.SourceNodeDigest == effect.SourceNodeDigest
                        && applied.OptionId == effect.OptionId && applied.Level == effect.Level
                        && applied.Container == effect.Container && applied.Position == effect.Position
                        && applied.ImprovementType == effect.ImprovementType && applied.Category == effect.ImprovedName
                        && applied.Value == effect.Value && applied.Minimum == effect.Minimum
                        && applied.Maximum == effect.Maximum && applied.Condition == effect.Condition))) return false;
            }
            targets.Add(new(skill.SourceSkillId, source.Category, skill.IsNativeLanguage, matches,
                skill.PointCost, skill.KarmaCost, skill.KnowledgeCategoryPrice));
        }
        // Require concrete canonical knowledge output. No vacuous All(empty),
        // synthetic zero-rating quote, or catalog-only execution receipt.
        if (targets.Count == 0) return false;
        var matching = targets.Where(row => row.MatchesCategory).ToArray();
        int rated = matching.Count(row => !row.Native);
        int points = matching.Sum(row => row.PointCost), karma = matching.Sum(row => row.KarmaCost);
        if (rated != 0 || points != 0 || karma != 0) return false;
        result = new KnowledgeInactivity711(authority.AuthorityDigest, effects.ToArray(), targets, rated, points, karma);
        return true;
    }

    internal static bool TryLifestylePrice711(CharacterCreationLifestylesCompletionProjection? projection, out object? result)
    {
        result = null;
        if (projection is null || projection.Blockers.Count != 0
            || projection.ProposalDigest != CharacterCreationLifestylesCompletionRules.ComputeProjectionDigest(projection)
            || !CharacterCreationLifestylesRules.IsValidAuthority(projection.Authority)
            || !CharacterCreationLifestyleImprovementRules.IsValidPurchasedQualityCosts(projection.Authority.PurchasedQualityCosts))
            return false;
        if (projection.Lifestyles.Count == 0 && (projection.StartingLifestyleId is not null || projection.Budget.Used != 0))
            return false;
        // Projection is the re-executed full replacement basket. PriceSelectedCompletion
        // has compiled all quality/grant costs even for an explicitly empty basket.
        result = new { projection.Authority, projection.Lifestyles, projection.Budget, projection.StartingLifestyleId,
            projection.ProposalDigest, Inactive = projection.Lifestyles.Count == 0 };
        return true;
    }
}
