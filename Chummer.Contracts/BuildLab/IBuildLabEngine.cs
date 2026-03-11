namespace Chummer.Contracts.BuildLab;

public interface IBuildLabEngine
{
    IReadOnlyList<BuildVariantProjection> GenerateBuildVariants(string characterId, IReadOnlyList<string> roleTags);
    BuildVariantProjection? ScoreBuildVariant(string characterId, string variantId);
    KarmaSpendProjection ProjectKarmaSpend(string characterId, string variantId, IReadOnlyList<int> milestones);
    IReadOnlyList<BuildTrapChoice> DetectTrapChoices(string characterId, string variantId);
    IReadOnlyList<BuildRoleOverlap> DetectRoleOverlap(string characterId, IReadOnlyList<string> variantIds);
    IReadOnlyList<BuildCorePackageSuggestion> SuggestCorePackages(string characterId, string variantId);
}
