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

    public IReadOnlyList<KarmaSpendProjection> PlanProgressionPaths(
        string characterId,
        IReadOnlyList<string> roleTags,
        IReadOnlyList<int> milestones,
        IReadOnlyList<string> campaignConstraintTags)
    {
        string[] constraintTags = NormalizeTags(campaignConstraintTags, defaultWhenEmpty: false);

        return GenerateBuildVariants(characterId, roleTags)
            .Select(variant =>
            {
                KarmaSpendProjection projection = ProjectKarmaSpend(characterId, variant.VariantId, milestones);
                List<RulesetCapabilityDiagnostic> diagnostics = projection.Diagnostics?.ToList() ?? [];
                diagnostics.AddRange(BuildCampaignConstraintDiagnostics(variant, constraintTags));
                string[] matchedConstraintTags = constraintTags
                    .Intersect(variant.RoleTags, StringComparer.Ordinal)
                    .OrderBy(static tag => tag, StringComparer.Ordinal)
                    .ToArray();
                string[] missingConstraintTags = constraintTags
                    .Except(matchedConstraintTags, StringComparer.Ordinal)
                    .OrderBy(static tag => tag, StringComparer.Ordinal)
                    .ToArray();
                decimal constraintCoverageScore = constraintTags.Length == 0
                    ? 100m
                    : Math.Round((matchedConstraintTags.Length * 100m) / constraintTags.Length, 2, MidpointRounding.AwayFromZero);
                string tradeoffSummaryKey = constraintTags.Length == 0
                    ? "buildlab.progression.tradeoff.unconstrained"
                    : missingConstraintTags.Length == 0
                        ? "buildlab.progression.tradeoff.constraint-aligned"
                        : "buildlab.progression.tradeoff.constraint-gap";
                BuildVariantScore? earlyConsistency = projection.Steps
                    .FirstOrDefault()?
                    .Scores
                    .FirstOrDefault(static score => string.Equals(score.MetricId, "consistency", StringComparison.Ordinal));
                BuildVariantScore? lateCeiling = projection.Steps
                    .LastOrDefault()?
                    .Scores
                    .FirstOrDefault(static score => string.Equals(score.MetricId, "ceiling", StringComparison.Ordinal));
                RulesetExplainParameter[] tradeoffSummaryParameters =
                [
                    Param("variantId", projection.VariantId),
                    Param("tag", ExtractTag(projection.VariantId)),
                    Param("matchedConstraintCount", matchedConstraintTags.Length),
                    Param("missingConstraintCount", missingConstraintTags.Length),
                    Param("constraintCoverageScore", constraintCoverageScore),
                    Param("initialConsistency", earlyConsistency?.Value),
                    Param("finalCeiling", lateCeiling?.Value)
                ];

                return projection with
                {
                    SummaryParameters = projection.SummaryParameters
                        .Concat(
                        [
                            Param("constraintCount", constraintTags.Length),
                            Param("matchedConstraintCount", matchedConstraintTags.Length),
                            Param("missingConstraintCount", missingConstraintTags.Length),
                            Param("constraintCoverageScore", constraintCoverageScore)
                        ])
                        .ToArray(),
                    Diagnostics = diagnostics,
                    ExplainEntryId = $"{projection.ExplainEntryId}:planner",
                    TradeoffSummaryKey = tradeoffSummaryKey,
                    TradeoffSummaryParameters = tradeoffSummaryParameters,
                    MatchedConstraintTags = matchedConstraintTags,
                    MissingConstraintTags = missingConstraintTags,
                    ConstraintCoverageScore = constraintCoverageScore
                };
            })
            .OrderByDescending(static projection => projection.Steps.FirstOrDefault()?.Rank ?? 0m)
            .ThenBy(static projection => projection.VariantId, StringComparer.Ordinal)
            .ToArray();
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

    public BuildTeamCoverageProjection EvaluateTeamCoverage(string characterId, IReadOnlyList<string> variantIds, IReadOnlyList<string> requiredRoleTags)
    {
        string[] orderedVariantIds = variantIds
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Select(static id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<BuildRoleOverlap> overlaps = DetectRoleOverlap(characterId, orderedVariantIds);
        string[] presentTags = orderedVariantIds
            .Select(ExtractTag)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        string[] requiredTags = NormalizeTags(requiredRoleTags, defaultWhenEmpty: presentTags.Length == 0);

        if (requiredTags.Length == 0)
        {
            requiredTags = presentTags;
        }

        string[] missingRoleTags = requiredTags
            .Except(presentTags, StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        string[] coveredRoleTags = requiredTags
            .Intersect(presentTags, StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        string[] duplicateRoleTags = orderedVariantIds
            .Select(ExtractTag)
            .GroupBy(static tag => tag, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        decimal coverageScore = requiredTags.Length == 0
            ? 100m
            : Math.Round(((requiredTags.Length - missingRoleTags.Length) * 100m) / requiredTags.Length, 2, MidpointRounding.AwayFromZero);
        decimal overlapPressure = overlaps.Count == 0
            ? 0m
            : overlaps.Max(static overlap => overlap.OverlapScore) * 25m;
        decimal duplicationPressure = Math.Max(0, orderedVariantIds.Length - presentTags.Length) * 10m;
        decimal missingPressure = missingRoleTags.Length * 30m;
        decimal rolePressureScore = Math.Min(100m, Math.Round(missingPressure + overlapPressure + duplicationPressure, 2, MidpointRounding.AwayFromZero));

        List<RulesetCapabilityDiagnostic> diagnostics = [];
        if (missingRoleTags.Length > 0)
        {
            diagnostics.Add(Diagnostic(
                "buildlab.team.missing-role-tags",
                RulesetCapabilityDiagnosticSeverities.Warning,
                ("missingRoleCount", missingRoleTags.Length),
                ("missingRoles", string.Join(",", missingRoleTags))));
        }
        else
        {
            diagnostics.Add(Diagnostic(
                "buildlab.team.coverage-aligned",
                ("requiredRoleCount", requiredTags.Length),
                ("variantCount", orderedVariantIds.Length)));
        }

        if (duplicateRoleTags.Length > 0)
        {
            diagnostics.Add(Diagnostic(
                "buildlab.team.duplicate-role-tags",
                RulesetCapabilityDiagnosticSeverities.Warning,
                ("duplicateRoleCount", duplicateRoleTags.Length),
                ("duplicateRoles", string.Join(",", duplicateRoleTags))));
        }

        if (overlaps.Any(static overlap => overlap.OverlapScore >= 0.85m))
        {
            diagnostics.Add(Diagnostic(
                "buildlab.team.role-pressure-high",
                RulesetCapabilityDiagnosticSeverities.Warning,
                ("highestOverlap", overlaps.Max(static overlap => overlap.OverlapScore)),
                ("variantCount", orderedVariantIds.Length)));
        }

        return new BuildTeamCoverageProjection(
            SummaryKey: "buildlab.team.summary",
            SummaryParameters:
            [
                Param("variantCount", orderedVariantIds.Length),
                Param("requiredRoleCount", requiredTags.Length),
                Param("coveredRoleCount", coveredRoleTags.Length),
                Param("missingRoleCount", missingRoleTags.Length),
                Param("duplicateRoleCount", duplicateRoleTags.Length),
                Param("coverageScore", coverageScore),
                Param("rolePressureScore", rolePressureScore)
            ],
            CoverageScore: coverageScore,
            RolePressureScore: rolePressureScore,
            MissingRoleTags: missingRoleTags,
            RoleOverlaps: overlaps,
            Diagnostics: diagnostics,
            ExplainEntryId: $"{Normalize(characterId)}:team-coverage",
            CoveredRoleTags: coveredRoleTags,
            DuplicateRoleTags: duplicateRoleTags);
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

    private static IReadOnlyList<RulesetCapabilityDiagnostic> BuildCampaignConstraintDiagnostics(
        BuildVariantProjection variant,
        IReadOnlyList<string> constraintTags)
    {
        if (constraintTags.Count == 0)
        {
            return [];
        }

        string[] missingTags = constraintTags
            .Except(variant.RoleTags, StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
        if (missingTags.Length == 0)
        {
            return
            [
                Diagnostic(
                    "buildlab.progression.campaign-constraint-aligned",
                    ("variantId", variant.VariantId),
                    ("constraintCount", constraintTags.Count))
            ];
        }

        return
        [
            Diagnostic(
                "buildlab.progression.campaign-constraint-gap",
                RulesetCapabilityDiagnosticSeverities.Warning,
                ("variantId", variant.VariantId),
                ("missingConstraints", string.Join(",", missingTags)))
        ];
    }

    private static RulesetCapabilityDiagnostic Diagnostic(
        string code,
        params (string Name, object? Value)[] parameters)
        => Diagnostic(code, RulesetCapabilityDiagnosticSeverities.Info, parameters);

    private static RulesetCapabilityDiagnostic Diagnostic(
        string code,
        string severity,
        params (string Name, object? Value)[] parameters)
    {
        return new RulesetCapabilityDiagnostic(
            Code: code,
            Message: code,
            Severity: severity,
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

    private static string[] NormalizeTags(IReadOnlyList<string> tags, bool defaultWhenEmpty)
    {
        string[] normalized = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        if (normalized.Length == 0 && defaultWhenEmpty)
        {
            return ["generalist"];
        }

        return normalized;
    }

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
