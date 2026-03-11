using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.BuildLab;
using Chummer.Contracts.Rulesets;

namespace Chummer.Backend.BuildLab
{
    public class BuildLabEngine : IBuildLabEngine
    {
        public IReadOnlyList<BuildVariantProjection> GenerateBuildVariants(string characterId, IReadOnlyList<string> roleTags)
        {
            string seed = Normalize(characterId);
            string[] tags = roleTags
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(tag => tag.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(tag => tag, StringComparer.Ordinal)
                .ToArray();

            bool defaultedRoleTags = tags.Length == 0;
            if (defaultedRoleTags)
            {
                tags = new[] { "generalist" };
            }

            return tags.Select((tag, index) => CreateVariantProjection(seed, tag, index + 1, defaultedRoleTags))
                .OrderByDescending(variant => variant.Rank)
                .ThenBy(variant => variant.VariantId, StringComparer.Ordinal)
                .ToArray();
        }

        public BuildVariantProjection? ScoreBuildVariant(string characterId, string variantId)
        {
            string normalizedVariantId = NormalizeVariantId(characterId, variantId);
            IReadOnlyList<BuildVariantProjection> candidates = GenerateBuildVariants(characterId, new[] { ExtractTag(normalizedVariantId) });

            return candidates.FirstOrDefault(candidate => string.Equals(candidate.VariantId, normalizedVariantId, StringComparison.Ordinal))
                   ?? candidates.First();
        }

        public KarmaSpendProjection ProjectKarmaSpend(string characterId, string variantId, IReadOnlyList<int> milestones)
        {
            string normalizedVariantId = NormalizeVariantId(characterId, variantId);
            string tag = ExtractTag(normalizedVariantId);
            int[] orderedMilestones = milestones
                .Where(milestone => milestone > 0)
                .Distinct()
                .OrderBy(milestone => milestone)
                .ToArray();
            bool defaultedMilestones = orderedMilestones.Length == 0;

            if (defaultedMilestones)
            {
                orderedMilestones = new[] { 25, 50, 100 };
            }

            KarmaSpendStep[] steps = orderedMilestones
                .Select((milestone, index) => new KarmaSpendStep(
                    StepId: normalizedVariantId + ":karma:" + milestone,
                    KarmaTotal: milestone,
                    Rank: orderedMilestones.Length - index,
                    SummaryKey: "buildlab.progression.step.summary",
                    SummaryParameters: new[]
                    {
                        Param("variantId", normalizedVariantId),
                        Param("karmaTotal", milestone),
                        Param("tag", tag)
                    },
                    Scores: new[]
                    {
                        new BuildVariantScore("consistency", Math.Max(0m, 100m - (milestone / 3m)), 0.55m, normalizedVariantId + ":progression:" + milestone + ":consistency"),
                        new BuildVariantScore("ceiling", Math.Min(100m, 40m + (milestone / 2m)), 0.45m, normalizedVariantId + ":progression:" + milestone + ":ceiling")
                    },
                    AppliedChoiceIds: new[] { milestone + ":core", milestone + ":" + tag },
                    Diagnostics: Array.Empty<RulesetCapabilityDiagnostic>(),
                    ExplainEntryId: normalizedVariantId + ":progression:" + milestone))
                .ToArray();

            return new KarmaSpendProjection(
                VariantId: normalizedVariantId,
                SummaryKey: "buildlab.progression.summary",
                SummaryParameters: new[]
                {
                    Param("variantId", normalizedVariantId),
                    Param("milestoneCount", steps.Length),
                    Param("tag", tag)
                },
                Steps: steps,
                Diagnostics: defaultedMilestones
                    ? new[] { Diagnostic("buildlab.progression.milestone-defaulted", Tuple.Create("variantId", (object)normalizedVariantId), Tuple.Create("tag", (object)tag)) }
                    : Array.Empty<RulesetCapabilityDiagnostic>(),
                ExplainEntryId: normalizedVariantId + ":progression");
        }

        public IReadOnlyList<BuildTrapChoice> DetectTrapChoices(string characterId, string variantId)
        {
            string normalizedVariantId = NormalizeVariantId(characterId, variantId);
            string tag = ExtractTag(normalizedVariantId);
            return new[]
            {
                new BuildTrapChoice(
                    ChoiceId: normalizedVariantId + ":trap:resource-overcommit",
                    ReasonKey: "buildlab.trap.resource-overcommit",
                    Parameters: new[]
                    {
                        Param("variantId", normalizedVariantId),
                        Param("tag", tag),
                        Param("primaryResource", "nuyen"),
                        Param("secondaryResource", "karma")
                    },
                    Severity: RulesetCapabilityDiagnosticSeverities.Warning,
                    ExplainEntryId: normalizedVariantId + ":trap:resource-overcommit")
            };
        }

        public IReadOnlyList<BuildRoleOverlap> DetectRoleOverlap(string characterId, IReadOnlyList<string> variantIds)
        {
            string[] ordered = variantIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            List<BuildRoleOverlap> overlaps = new List<BuildRoleOverlap>();
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

                    overlaps.Add(
                        new BuildRoleOverlap(
                            LeftVariantId: ordered[i],
                            RightVariantId: ordered[j],
                            OverlapScore: overlapScore,
                            ReasonKey: "buildlab.role-overlap.summary",
                            ReasonParameters: new[]
                            {
                                Param("leftVariantId", ordered[i]),
                                Param("rightVariantId", ordered[j]),
                                Param("leftTag", leftTag),
                                Param("rightTag", rightTag)
                            },
                            ExplainEntryId: ordered[i] + ":" + ordered[j] + ":overlap"));
                }
            }

            return overlaps
                .OrderByDescending(overlap => overlap.OverlapScore)
                .ThenBy(overlap => overlap.LeftVariantId, StringComparer.Ordinal)
                .ThenBy(overlap => overlap.RightVariantId, StringComparer.Ordinal)
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
                .OrderByDescending(package => package.Rank)
                .ThenBy(package => package.PackageId, StringComparer.Ordinal)
                .ToArray();
        }

        private static BuildVariantProjection CreateVariantProjection(string seed, string tag, int ordinal, bool defaultedRoleTags)
        {
            string variantId = seed + "-" + tag + "-" + ordinal;
            decimal synergy = Math.Max(0m, 100m - ((ordinal - 1) * 7m));
            decimal efficiency = Math.Max(0m, 80m - ((ordinal - 1) * 5m));
            decimal rank = Math.Round((synergy * 0.6m) + (efficiency * 0.4m), 2, MidpointRounding.AwayFromZero);

            return new BuildVariantProjection(
                VariantId: variantId,
                LabelKey: "buildlab.variant.label",
                LabelParameters: new[]
                {
                    Param("tag", tag),
                    Param("ordinal", ordinal)
                },
                RoleTags: new[] { tag },
                Rank: rank,
                SummaryKey: "buildlab.variant.summary",
                SummaryParameters: new[]
                {
                    Param("variantId", variantId),
                    Param("tag", tag),
                    Param("rank", rank)
                },
                Scores: new[]
                {
                    new BuildVariantScore("synergy", synergy, 0.6m, variantId + ":score:synergy"),
                    new BuildVariantScore("efficiency", efficiency, 0.4m, variantId + ":score:efficiency")
                },
                Constraints: ordinal > 1
                    ? new[]
                    {
                        new BuildVariantConstraint(
                            ConstraintId: variantId + ":constraint:secondary-role",
                            ConstraintKey: "buildlab.variant.constraint.secondary-role",
                            Parameters: new[]
                            {
                                Param("variantId", variantId),
                                Param("tag", tag),
                                Param("ordinal", ordinal)
                            },
                            Severity: RulesetCapabilityDiagnosticSeverities.Warning)
                    }
                    : Array.Empty<BuildVariantConstraint>(),
                Diagnostics: defaultedRoleTags
                    ? new[] { Diagnostic("buildlab.variant.role-tag-defaulted", Tuple.Create("variantId", (object)variantId), Tuple.Create("tag", (object)tag)) }
                    : Array.Empty<RulesetCapabilityDiagnostic>(),
                ExplainEntryId: variantId + ":summary");
        }

        private static BuildCorePackageSuggestion CreatePackageSuggestion(string variantId, string tag, string slot, decimal rank)
        {
            string packageId = tag + ".core." + slot;
            return new BuildCorePackageSuggestion(
                PackageId: packageId,
                LabelKey: "buildlab.package.label",
                LabelParameters: new[]
                {
                    Param("packageId", packageId),
                    Param("tag", tag),
                    Param("slot", slot)
                },
                Rank: rank,
                SummaryKey: "buildlab.package.summary",
                SummaryParameters: new[]
                {
                    Param("packageId", packageId),
                    Param("variantId", variantId),
                    Param("tag", tag),
                    Param("rank", rank)
                },
                Diagnostics: Array.Empty<RulesetCapabilityDiagnostic>(),
                ExplainEntryId: variantId + ":package:" + slot);
        }

        private static RulesetCapabilityDiagnostic Diagnostic(string code, params Tuple<string, object>[] parameters)
        {
            return new RulesetCapabilityDiagnostic(
                Code: code,
                Message: code,
                Severity: RulesetCapabilityDiagnosticSeverities.Info,
                MessageKey: code,
                MessageParameters: parameters.Select(parameter => Param(parameter.Item1, parameter.Item2)).ToArray());
        }

        private static RulesetExplainParameter Param(string name, object value)
        {
            return new RulesetExplainParameter(name, RulesetCapabilityBridge.FromObject(value));
        }

        private static string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "character";
            }

            return value.Trim().Replace(' ', '-').ToLowerInvariant();
        }

        private static string NormalizeVariantId(string characterId, string variantId)
        {
            return string.IsNullOrWhiteSpace(variantId) ? Normalize(characterId) + "-generalist-1" : variantId.Trim();
        }

        private static string ExtractTag(string variantId)
        {
            if (string.IsNullOrWhiteSpace(variantId))
            {
                return "generalist";
            }

            string[] parts = variantId.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 ? parts[parts.Length - 2] : "generalist";
        }
    }
}
