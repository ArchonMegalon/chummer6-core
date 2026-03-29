namespace Chummer.Contracts.BuildLab;

public interface IBuildLabEngine
{
    IReadOnlyList<BuildVariantProjection> GenerateBuildVariants(string characterId, IReadOnlyList<string> roleTags);
    BuildVariantProjection? ScoreBuildVariant(string characterId, string variantId);
    KarmaSpendProjection ProjectKarmaSpend(string characterId, string variantId, IReadOnlyList<int> milestones);
    IReadOnlyList<KarmaSpendProjection> PlanProgressionPaths(string characterId, IReadOnlyList<string> roleTags, IReadOnlyList<int> milestones, IReadOnlyList<string> campaignConstraintTags);
    IReadOnlyList<BuildTrapChoice> DetectTrapChoices(string characterId, string variantId);
    IReadOnlyList<BuildRoleOverlap> DetectRoleOverlap(string characterId, IReadOnlyList<string> variantIds);
    BuildTeamCoverageProjection EvaluateTeamCoverage(string characterId, IReadOnlyList<string> variantIds, IReadOnlyList<string> requiredRoleTags);
    IReadOnlyList<BuildCorePackageSuggestion> SuggestCorePackages(string characterId, string variantId);
}
