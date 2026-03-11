using Chummer.Contracts.BuildLab;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.BuildLab;

public sealed class DefaultBuildLabService : IBuildLabService
{
    public IReadOnlyList<BuildVariantProjection> GenerateBuildVariants(string characterId, IReadOnlyList<string> roleTags)
    {
        string seed = Normalize(characterId);
        string[] tags = roleTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        if (tags.Length == 0)
        {
            tags = ["generalist"];
        }

        bool defaultedRoleTags = roleTags is null || roleTags.Count == 0 || roleTags.All(static tag => string.IsNullOrWhiteSpace(tag));

        return tags.Select((tag, index) => CreateVariantProjection(seed, tag, index + 1, defaultedRoleTags))
            .OrderByDescending(static variant => variant.Rank)
            .ThenBy(static variant => variant.VariantId, StringComparer.Ordinal)
            .ToArray();
    }

    public BuildVariantProjection? ScoreBuildVariant(string characterId, string variantId)
    {
        string normalizedVariantId = NormalizeVariantId(characterId, variantId);
        IReadOnlyList<BuildVariantProjection> candidates = GenerateBuildVariants(characterId, [ExtractTag(normalizedVariantId)]);

        return candidates.FirstOrDefault(candidate => string.Equals(candidate.VariantId, normalizedVariantId, StringComparison.Ordinal))
            ?? candidates.FirstOrDefault();
    }

    public KarmaSpendProjection ProjectKarmaSpend(string characterId, string variantId, IReadOnlyList<int> milestones)
    {
        string normalizedVariantId = NormalizeVariantId(characterId, variantId);
        string tag = ExtractTag(normalizedVariantId);
        int[] orderedMilestones = milestones
            .Where(static milestone => milestone > 0)
            .Distinct()
            .OrderBy(static milestone => milestone)
            .ToArray();
        bool defaultedMilestones = orderedMilestones.Length == 0;

        if (defaultedMilestones)
        {
            orderedMilestones = [25, 50, 100];
        }

        KarmaSpendStep[] steps = orderedMilestones
            .Select((milestone, index) => new KarmaSpendStep(
                StepId: $"{normalizedVariantId}:karma:{milestone}",
                KarmaTotal: milestone,
                Rank: orderedMilestones.Length - index,
                SummaryKey: "buildlab.progression.step.summary",
                SummaryParameters:
                [
                    Param("variantId", normalizedVariantId),
                    Param("karmaTotal", milestone),
                    Param("tag", tag)
                ],
                Scores:
                [
                    new BuildVariantScore("consistency", Math.Max(0m, 100m - (milestone / 3m)), Weight: 0.55m, ExplainEntryId: $"{normalizedVariantId}:progression:{milestone}:consistency"),
                    new BuildVariantScore("ceiling", Math.Min(100m, 40m + (milestone / 2m)), Weight: 0.45m, ExplainEntryId: $"{normalizedVariantId}:progression:{milestone}:ceiling")
                ],
                AppliedChoiceIds: [$"{milestone}:core", $"{milestone}:{tag}"],
                Diagnostics: [],
                ExplainEntryId: $"{normalizedVariantId}:progression:{milestone}"))
            .ToArray();

        return new KarmaSpendProjection(
            VariantId: normalizedVariantId,
            SummaryKey: "buildlab.progression.summary",
            SummaryParameters:
            [
                Param("variantId", normalizedVariantId),
                Param("milestoneCount", steps.Length),
                Param("tag", tag)
            ],
            Steps: steps,
            Diagnostics: defaultedMilestones ? [Diagnostic("buildlab.progression.milestone-defaulted", ("variantId", normalizedVariantId), ("tag", tag))] : [],
            ExplainEntryId: $"{normalizedVariantId}:progression");
    }

    public IReadOnlyList<BuildTrapChoice> DetectTrapChoices(string characterId, string variantId)
    {
        string normalizedVariantId = NormalizeVariantId(characterId, variantId);
        string tag = ExtractTag(normalizedVariantId);
        return
        [
            new BuildTrapChoice(
                ChoiceId: $"{normalizedVariantId}:trap:resource-overcommit",
                ReasonKey: "buildlab.trap.resource-overcommit",
                Parameters:
                [
                    Param("variantId", normalizedVariantId),
                    Param("tag", tag),
                    Param("primaryResource", "nuyen"),
                    Param("secondaryResource", "karma")
                ],
                ExplainEntryId: $"{normalizedVariantId}:trap:resource-overcommit")
        ];
    }

    public IReadOnlyList<BuildRoleOverlap> DetectRoleOverlap(string characterId, IReadOnlyList<string> variantIds)
    {
        string[] ordered = variantIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

        List<BuildRoleOverlap> overlaps = [];
        for (int i = 0; i < ordered.Length; i++)
        {
            for (int j = i + 1; j < ordered.Length; j++)
            {
                string leftTag = ExtractTag(ordered[i]);
                string rightTag = ExtractTag(ordered[j]);
                decimal overlapScore = string.Equals(leftTag, rightTag, StringComparison.Ordinal)
                    ? 1.0m
                    : string.Equals(leftTag, "generalist", StringComparison.Ordinal) || string.Equals(rightTag, "generalist", StringComparison.Ordinal)
                        ? 0.6m
                        : 0.35m;

                overlaps.Add(new BuildRoleOverlap(
                    LeftVariantId: ordered[i],
                    RightVariantId: ordered[j],
                    OverlapScore: overlapScore,
                    ReasonKey: "buildlab.role-overlap.summary",
                    ReasonParameters:
                    [
                        Param("leftVariantId", ordered[i]),
                        Param("rightVariantId", ordered[j]),
                        Param("leftTag", leftTag),
                        Param("rightTag", rightTag)
                    ],
                    ExplainEntryId: $"{ordered[i]}:{ordered[j]}:overlap"));
            }
        }

        return overlaps
            .OrderByDescending(static overlap => overlap.OverlapScore)
            .ThenBy(static overlap => overlap.LeftVariantId, StringComparer.Ordinal)
            .ThenBy(static overlap => overlap.RightVariantId, StringComparer.Ordinal)
            .ToArray();
    }

    public IReadOnlyList<BuildCorePackageSuggestion> SuggestCorePackages(string characterId, string variantId)
    {
        string normalizedVariantId = NormalizeVariantId(characterId, variantId);
        string tag = ExtractTag(normalizedVariantId);

        return new[]
        {
            CreatePackageSuggestion(normalizedVariantId, tag, "a", 0.91m),
            CreatePackageSuggestion(normalizedVariantId, tag, "b", 0.83m)
        }
            .OrderByDescending(static package => package.Rank)
            .ThenBy(static package => package.PackageId, StringComparer.Ordinal)
            .ToArray();
    }

    private static BuildVariantProjection CreateVariantProjection(string seed, string tag, int ordinal, bool defaultedRoleTags)
    {
        string variantId = $"{seed}-{tag}-{ordinal}";
        decimal synergy = Math.Max(0m, 100m - ((ordinal - 1) * 7m));
        decimal efficiency = Math.Max(0m, 80m - ((ordinal - 1) * 5m));
        decimal rank = Math.Round((synergy * 0.6m) + (efficiency * 0.4m), 2, MidpointRounding.AwayFromZero);

        return new BuildVariantProjection(
            VariantId: variantId,
            LabelKey: "buildlab.variant.label",
            LabelParameters:
            [
                Param("tag", tag),
                Param("ordinal", ordinal)
            ],
            RoleTags: [tag],
            Rank: rank,
            SummaryKey: "buildlab.variant.summary",
            SummaryParameters:
            [
                Param("variantId", variantId),
                Param("tag", tag),
                Param("rank", rank)
            ],
            Scores:
            [
                new BuildVariantScore("synergy", synergy, Weight: 0.6m, ExplainEntryId: $"{variantId}:score:synergy"),
                new BuildVariantScore("efficiency", efficiency, Weight: 0.4m, ExplainEntryId: $"{variantId}:score:efficiency")
            ],
            Constraints: ordinal > 1
                ? [new BuildVariantConstraint(
                    ConstraintId: $"{variantId}:constraint:secondary-role",
                    ConstraintKey: "buildlab.variant.constraint.secondary-role",
                    Parameters:
                    [
                        Param("variantId", variantId),
                        Param("tag", tag),
                        Param("ordinal", ordinal)
                    ])]
                : [],
            Diagnostics: defaultedRoleTags
                ? [Diagnostic("buildlab.variant.role-tag-defaulted", ("variantId", variantId), ("tag", tag))]
                : [],
            ExplainEntryId: $"{variantId}:summary");
    }

    private static BuildCorePackageSuggestion CreatePackageSuggestion(string variantId, string tag, string slot, decimal rank)
    {
        string packageId = $"{tag}.core.{slot}";
        return new BuildCorePackageSuggestion(
            PackageId: packageId,
            LabelKey: "buildlab.package.label",
            LabelParameters:
            [
                Param("packageId", packageId),
                Param("tag", tag),
                Param("slot", slot)
            ],
            Rank: rank,
            SummaryKey: "buildlab.package.summary",
            SummaryParameters:
            [
                Param("packageId", packageId),
                Param("variantId", variantId),
                Param("tag", tag),
                Param("rank", rank)
            ],
            Diagnostics: [],
            ExplainEntryId: $"{variantId}:package:{slot}");
    }

    private static RulesetCapabilityDiagnostic Diagnostic(
        string code,
        params (string Name, object? Value)[] parameters)
    {
        return new RulesetCapabilityDiagnostic(
            Code: code,
            Message: code,
            Severity: RulesetCapabilityDiagnosticSeverities.Info,
            MessageKey: code,
            MessageParameters: parameters.Select(static parameter => Param(parameter.Name, parameter.Value)).ToArray());
    }

    private static RulesetExplainParameter Param(string name, object? value)
        => new(name, RulesetCapabilityBridge.FromObject(value));

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "character";
        }

        return value.Trim().Replace(' ', '-').ToLowerInvariant();
    }

    private static string NormalizeVariantId(string characterId, string variantId)
        => string.IsNullOrWhiteSpace(variantId) ? $"{Normalize(characterId)}-generalist-1" : variantId.Trim();

    private static string ExtractTag(string variantId)
    {
        if (string.IsNullOrWhiteSpace(variantId))
        {
            return "generalist";
        }

        string[] parts = variantId.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : "generalist";
    }
}
