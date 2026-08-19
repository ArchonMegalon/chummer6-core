using System.Globalization;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;

namespace Chummer.Application.BuildGhost;

public sealed record BuildGhostWorkspaceAnalysisContext(
    string OwnerId,
    string? CampaignId,
    string RulesetId,
    string RuntimeFingerprint,
    string WorkspaceId,
    long WorkspaceRevision,
    string SourceDigest,
    string Locale,
    IReadOnlyList<string> LocaleFallbackChain,
    IReadOnlyList<string> SupportedLocales,
    BuildGhostRuleEnvironment RuleEnvironment,
    string RequestedGoal,
    BuildGhostGroupInput? Group,
    string DeterministicFallbackText);

/// <summary>
/// Materializes the provider-safe Build Ghost input from Chummer-owned section
/// projections. It deliberately uses only player-visible section data and emits
/// a variant only when the section already carries an exact, affordable career
/// upgrade cost.
/// </summary>
public static class BuildGhostWorkspaceProjectionFactory
{
    public static BuildGhostAnalysisRequest CreateRequest(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterProfileSection profile,
        CharacterProgressSection progress,
        CharacterRulesSection rules,
        CharacterBuildSection build,
        CharacterSkillsSection skills,
        CharacterAttributeDetailsSection attributes,
        CharacterAwakeningSection awakening,
        IReadOnlyList<BuildGhostSourceAnchor>? additionalSourceAnchors = null,
        IReadOnlyList<OptimizationStrategyProjection>? additionalStrategies = null,
        IReadOnlyList<BuildGhostRuleExplanationInput>? additionalRuleExplanations = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(awakening);

        List<BuildGhostSourceAnchor> anchors = [];
        List<BuildGhostFact> facts = [];
        AddProfileFacts(context, profile, build, anchors, facts);
        AddResourceFacts(context, progress, anchors, facts);
        AddSkillFacts(context, skills, anchors, facts);
        AddAttributeFacts(context, attributes, anchors, facts);
        AddRuleEnvironmentFacts(context, rules, anchors, facts);

        IReadOnlyList<string> expertiseTags = InferExpertiseTags(skills, awakening);
        IReadOnlyList<OptimizationStrategyProjection> generatedStrategies = CreateExactAttributeStrategies(
            progress,
            attributes,
            expertiseTags);
        IReadOnlyList<BuildGhostRuleExplanationInput> generatedExplanations = CreateAttributeRuleExplanations(
            attributes);

        return new BuildGhostAnalysisRequest(
            OwnerId: Require(context.OwnerId, nameof(context.OwnerId)),
            CampaignId: NormalizeOptional(context.CampaignId),
            RulesetId: Require(context.RulesetId, nameof(context.RulesetId)),
            RuntimeFingerprint: Require(context.RuntimeFingerprint, nameof(context.RuntimeFingerprint)),
            WorkspaceId: Require(context.WorkspaceId, nameof(context.WorkspaceId)),
            WorkspaceRevision: context.WorkspaceRevision,
            SourceDigest: Require(context.SourceDigest, nameof(context.SourceDigest)),
            Locale: Require(context.Locale, nameof(context.Locale)),
            LocaleFallbackChain: context.LocaleFallbackChain,
            SupportedLocales: context.SupportedLocales,
            RuleEnvironment: context.RuleEnvironment,
            Runner: new BuildGhostRunnerProjection(
                CharacterId: CreateCharacterId(context, profile),
                DisplayName: FirstNonEmpty(profile.Alias, profile.Name, "Runner"),
                CreationState: profile.Created ? "career" : "creation",
                ExpertiseTags: expertiseTags,
                Facts: facts,
                ResourceValues: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["karma"] = progress.Karma,
                    ["nuyen"] = progress.Nuyen,
                    ["essence"] = progress.TotalEssence
                }),
            RequestedGoal: FirstNonEmpty(context.RequestedGoal, "Review the current runner and compare safe improvements."),
            SourceAnchors: anchors
                .Concat(additionalSourceAnchors ?? [])
                .GroupBy(static anchor => anchor.AnchorId, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray(),
            Strategies: generatedStrategies
                .Concat(additionalStrategies ?? [])
                .GroupBy(static strategy => strategy.StrategyId, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray(),
            RuleExplanations: generatedExplanations
                .Concat(additionalRuleExplanations ?? [])
                .GroupBy(static explanation => explanation.ExplanationId, StringComparer.Ordinal)
                .Select(static group => group.First())
                .ToArray(),
            Group: context.Group,
            DeterministicFallbackText: Require(context.DeterministicFallbackText, nameof(context.DeterministicFallbackText)));
    }

    public static BuildGhostAnalysisPacket Analyze(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterProfileSection profile,
        CharacterProgressSection progress,
        CharacterRulesSection rules,
        CharacterBuildSection build,
        CharacterSkillsSection skills,
        CharacterAttributeDetailsSection attributes,
        CharacterAwakeningSection awakening,
        IBuildGhostAnalysisService? analysisService = null,
        IReadOnlyList<BuildGhostSourceAnchor>? additionalSourceAnchors = null,
        IReadOnlyList<OptimizationStrategyProjection>? additionalStrategies = null,
        IReadOnlyList<BuildGhostRuleExplanationInput>? additionalRuleExplanations = null)
        => (analysisService ?? new DefaultBuildGhostAnalysisService()).Analyze(CreateRequest(
            context,
            profile,
            progress,
            rules,
            build,
            skills,
            attributes,
            awakening,
            additionalSourceAnchors,
            additionalStrategies,
            additionalRuleExplanations));

    private static void AddProfileFacts(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterProfileSection profile,
        CharacterBuildSection build,
        ICollection<BuildGhostSourceAnchor> anchors,
        ICollection<BuildGhostFact> facts)
    {
        const string anchorId = "anchor:workspace:profile";
        anchors.Add(new BuildGhostSourceAnchor(
            AnchorId: anchorId,
            RulesetId: context.RulesetId,
            SourceId: "chummer-workspace",
            Page: null,
            ActiveCharacterSettings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["buildMethod"] = build.BuildMethod,
                ["gameplayOption"] = profile.GameplayOption
            },
            SavedValues: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["created"] = profile.Created.ToString(CultureInfo.InvariantCulture),
                ["metatype"] = profile.Metatype,
                ["concept"] = profile.Concept
            },
            CalculationTrace: ["Saved profile and build sections; no provider inference."],
            RuleId: "workspace-profile"));
        facts.Add(new BuildGhostFact(
            FactId: "fact:runner:creation-state",
            Category: "state",
            Label: "Creation state",
            Value: profile.Created ? "career" : "creation",
            Confidence: 1m,
            SourceAnchorIds: [anchorId]));
        facts.Add(new BuildGhostFact(
            FactId: "fact:runner:metatype",
            Category: "state",
            Label: "Metatype",
            Value: FirstNonEmpty(profile.Metatype, "Unresolved"),
            Confidence: 1m,
            SourceAnchorIds: [anchorId]));
    }

    private static void AddResourceFacts(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterProgressSection progress,
        ICollection<BuildGhostSourceAnchor> anchors,
        ICollection<BuildGhostFact> facts)
    {
        const string anchorId = "anchor:workspace:resources";
        anchors.Add(new BuildGhostSourceAnchor(
            AnchorId: anchorId,
            RulesetId: context.RulesetId,
            SourceId: "chummer-workspace",
            Page: null,
            ActiveCharacterSettings: new Dictionary<string, string>(),
            SavedValues: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["karma"] = Format(progress.Karma),
                ["nuyen"] = Format(progress.Nuyen),
                ["essence"] = Format(progress.TotalEssence)
            },
            CalculationTrace: ["Values copied from the current Chummer progress section."],
            RuleId: "workspace-resources"));
        facts.Add(new BuildGhostFact("fact:resource:karma", "resource", "Karma", Format(progress.Karma), 1m, [anchorId]));
        facts.Add(new BuildGhostFact("fact:resource:nuyen", "resource", "Nuyen", Format(progress.Nuyen), 1m, [anchorId]));
        facts.Add(new BuildGhostFact("fact:resource:essence", "resource", "Essence", Format(progress.TotalEssence), 1m, [anchorId]));
    }

    private static void AddSkillFacts(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterSkillsSection skills,
        ICollection<BuildGhostSourceAnchor> anchors,
        ICollection<BuildGhostFact> facts)
    {
        foreach (CharacterSkillSummary skill in skills.Skills
            .OrderBy(static skill => StableSkillId(skill), StringComparer.Ordinal))
        {
            string id = StableSkillId(skill);
            string anchorId = $"anchor:workspace:skill:{id}";
            int rating = skill.BaseValue + skill.KarmaValue;
            anchors.Add(new BuildGhostSourceAnchor(
                AnchorId: anchorId,
                RulesetId: context.RulesetId,
                SourceId: "chummer-workspace",
                Page: null,
                ActiveCharacterSettings: new Dictionary<string, string>(),
                SavedValues: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["base"] = skill.BaseValue.ToString(CultureInfo.InvariantCulture),
                    ["karma"] = skill.KarmaValue.ToString(CultureInfo.InvariantCulture),
                    ["rating"] = rating.ToString(CultureInfo.InvariantCulture)
                },
                CalculationTrace: [$"{skill.BaseValue} base + {skill.KarmaValue} Karma rating = {rating}"],
                RuleId: "workspace-skill-rating"));
            facts.Add(new BuildGhostFact(
                FactId: $"fact:skill:{id}",
                Category: rating >= 4 ? "strength" : "skill",
                Label: FirstNonEmpty(skill.Name, skill.CustomName, skill.Suid, skill.Guid, "Skill"),
                Value: rating.ToString(CultureInfo.InvariantCulture),
                Confidence: 1m,
                SourceAnchorIds: [anchorId]));
        }
    }

    private static void AddAttributeFacts(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterAttributeDetailsSection attributes,
        ICollection<BuildGhostSourceAnchor> anchors,
        ICollection<BuildGhostFact> facts)
    {
        foreach (CharacterAttributeDetailSummary attribute in attributes.Attributes
            .OrderBy(static attribute => StableId(attribute.Name), StringComparer.Ordinal))
        {
            string id = StableId(attribute.Name);
            string anchorId = $"anchor:workspace:attribute:{id}";
            anchors.Add(CreateAttributeAnchor(context.RulesetId, attribute, anchorId));
            facts.Add(new BuildGhostFact(
                FactId: $"fact:attribute:{id}",
                Category: attribute.TotalValue >= 5 ? "strength" : "attribute",
                Label: attribute.Name,
                Value: attribute.TotalValue.ToString(CultureInfo.InvariantCulture),
                Confidence: 1m,
                SourceAnchorIds: [anchorId]));
        }
    }

    private static void AddRuleEnvironmentFacts(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterRulesSection rules,
        ICollection<BuildGhostSourceAnchor> anchors,
        ICollection<BuildGhostFact> facts)
    {
        const string anchorId = "anchor:workspace:rule-environment";
        anchors.Add(new BuildGhostSourceAnchor(
            AnchorId: anchorId,
            RulesetId: context.RulesetId,
            SourceId: "chummer-settings",
            Page: null,
            ActiveCharacterSettings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["settings"] = rules.Settings,
                ["gameplayOption"] = rules.GameplayOption,
                ["qualityLimit"] = rules.GameplayOptionQualityLimit.ToString(CultureInfo.InvariantCulture),
                ["maxNuyen"] = rules.MaxNuyen.ToString(CultureInfo.InvariantCulture),
                ["maxKarma"] = rules.MaxKarma.ToString(CultureInfo.InvariantCulture)
            },
            SavedValues: new Dictionary<string, string>(),
            CalculationTrace: ["Active values copied from the current Chummer rules section."],
            RuleId: "character-settings"));
        facts.Add(new BuildGhostFact(
            FactId: "fact:rules:settings-profile",
            Category: "rules",
            Label: "Character Settings",
            Value: FirstNonEmpty(rules.Settings, "Unresolved"),
            Confidence: 1m,
            SourceAnchorIds: [anchorId]));
        if (context.RuleEnvironment.ActiveSourcebookIds.Count == 0)
        {
            facts.Add(new BuildGhostFact(
                FactId: "fact:rules:sourcebooks-unresolved",
                Category: "warning",
                Label: "Active sourcebooks",
                Value: "The section projection did not expose the active sourcebook ids.",
                Confidence: 1m,
                SourceAnchorIds: [anchorId]));
        }
    }

    private static IReadOnlyList<OptimizationStrategyProjection> CreateExactAttributeStrategies(
        CharacterProgressSection progress,
        CharacterAttributeDetailsSection attributes,
        IReadOnlyList<string> expertiseTags)
    {
        CharacterAttributeDetailSummary[] candidates = attributes.Attributes
            .Where(static attribute => attribute.Created
                && attribute.CanCareerUpgrade
                && attribute.UpgradeKarmaCost >= 0
                && attribute.AvailableKarma >= attribute.UpgradeKarmaCost
                && attribute.BaseValue < attribute.MetatypeMax)
            .OrderBy(static attribute => attribute.UpgradeKarmaCost)
            .ThenBy(static attribute => attribute.TotalValue)
            .ThenBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .ToArray();
        if (candidates.Length == 0)
        {
            return [];
        }

        List<(CharacterAttributeDetailSummary Attribute, string Type, int Priority, string Posture)> selected = [];
        AddDistinct(selected, candidates[0], "attribute-efficiency", 10, "repair the lowest-cost current attribute edge");
        AddDistinct(selected, candidates
            .OrderByDescending(static attribute => attribute.TotalValue)
            .ThenBy(static attribute => attribute.UpgradeKarmaCost)
            .ThenBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .First(), "dice-pool-breakpoint", 30, "strengthen the runner's current high attribute");
        AddDistinct(selected, candidates
            .OrderBy(static attribute => attribute.TotalValue)
            .ThenBy(static attribute => attribute.UpgradeKarmaCost)
            .ThenBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .First(), "balanced-advancement", 20, "raise a lower attribute for broader reliability");
        foreach (CharacterAttributeDetailSummary candidate in candidates)
        {
            if (selected.Count == 3)
            {
                break;
            }

            AddDistinct(selected, candidate, "balanced-advancement", 20 - selected.Count, "add a distinct affordable attribute alternative");
        }

        return selected.Select(item => CreateAttributeStrategy(progress, expertiseTags, item.Attribute, item.Type, item.Priority, item.Posture)).ToArray();
    }

    private static OptimizationStrategyProjection CreateAttributeStrategy(
        CharacterProgressSection progress,
        IReadOnlyList<string> expertiseTags,
        CharacterAttributeDetailSummary attribute,
        string strategyType,
        int priority,
        string posture)
    {
        string id = StableId(attribute.Name);
        string anchorId = $"anchor:workspace:attribute:{id}";
        string strategyId = $"strategy:workspace:attribute:{id}";
        decimal remainingKarma = progress.Karma - attribute.UpgradeKarmaCost;
        return new OptimizationStrategyProjection(
            StrategyId: strategyId,
            StrategyType: strategyType,
            ExpertiseTags: expertiseTags,
            Applicability: BuildGhostApplicabilityStatuses.ApplicableNow,
            TriggerFactIds: [$"fact:attribute:{id}", "fact:resource:karma"],
            ExpectedBenefit: $"Raise {attribute.Name} from {attribute.TotalValue} to {attribute.TotalValue + 1} before situational modifiers.",
            OpportunityCost: $"Spend exactly {attribute.UpgradeKarmaCost} Karma, leaving {Format(remainingKarma)} from the current projected balance.",
            Risk: "The same Karma is no longer available for another attribute, skill, quality, or contact.",
            Assumptions:
            [
                $"CanCareerUpgrade={attribute.CanCareerUpgrade}",
                $"AvailableKarma={attribute.AvailableKarma}",
                $"MetatypeMaximum={attribute.MetatypeMax}"
            ],
            Counterfactual: $"Keep {attribute.Name} at {attribute.TotalValue} and retain {attribute.UpgradeKarmaCost} Karma for another path.",
            ShortTermBenefit: $"{posture}: {attribute.Name} +1.",
            LongTermCeiling: $"{attribute.Name} remains bounded by the current metatype maximum {attribute.MetatypeMax}.",
            Dependencies: ["The preview must revalidate the same workspace revision and source digest."],
            GmPolicyConflicts: [],
            SourceAnchorIds: [anchorId, "anchor:workspace:resources"],
            Deltas:
            [
                new BuildGhostVariantDelta(
                    DeltaId: $"delta:attribute:{id}",
                    Domain: "attribute",
                    TargetId: $"attribute:{id}",
                    BeforeValue: attribute.TotalValue.ToString(CultureInfo.InvariantCulture),
                    AfterValue: (attribute.TotalValue + 1).ToString(CultureInfo.InvariantCulture),
                    NumericDelta: 1m,
                    Unit: "rating",
                    SourceAnchorIds: [anchorId]),
                new BuildGhostVariantDelta(
                    DeltaId: $"delta:karma:attribute:{id}",
                    Domain: "karma",
                    TargetId: "resource:karma",
                    BeforeValue: Format(progress.Karma),
                    AfterValue: Format(remainingKarma),
                    NumericDelta: -attribute.UpgradeKarmaCost,
                    Unit: "Karma",
                    SourceAnchorIds: [anchorId, "anchor:workspace:resources"])
            ],
            Priority: priority);
    }

    private static IReadOnlyList<BuildGhostRuleExplanationInput> CreateAttributeRuleExplanations(
        CharacterAttributeDetailsSection attributes)
        => attributes.Attributes
            .Where(static attribute => attribute.Created && attribute.CanCareerUpgrade && attribute.UpgradeKarmaCost >= 0)
            .OrderBy(static attribute => attribute.Name, StringComparer.Ordinal)
            .Take(3)
            .Select(attribute =>
            {
                string id = StableId(attribute.Name);
                return new BuildGhostRuleExplanationInput(
                    ExplanationId: $"explain:workspace:attribute-upgrade:{id}",
                    RuleId: "career-attribute-upgrade",
                    Question: $"What does it cost to raise {attribute.Name}?",
                    DeterministicExplanation: $"The active Chummer projection reports an exact career upgrade cost of {attribute.UpgradeKarmaCost} Karma to raise {attribute.Name} from {attribute.TotalValue} to {attribute.TotalValue + 1}.",
                    SourceAnchorIds: [$"anchor:workspace:attribute:{id}"],
                    Resolved: true,
                    SourceLookupRoute: "/build/attributes");
            })
            .ToArray();

    private static BuildGhostSourceAnchor CreateAttributeAnchor(
        string rulesetId,
        CharacterAttributeDetailSummary attribute,
        string anchorId)
        => new(
            AnchorId: anchorId,
            RulesetId: rulesetId,
            SourceId: "chummer-workspace",
            Page: null,
            ActiveCharacterSettings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["metatypeMinimum"] = attribute.MetatypeMin.ToString(CultureInfo.InvariantCulture),
                ["metatypeMaximum"] = attribute.MetatypeMax.ToString(CultureInfo.InvariantCulture),
                ["metatypeAugmentedMaximum"] = attribute.MetatypeAugMax.ToString(CultureInfo.InvariantCulture)
            },
            SavedValues: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["base"] = attribute.BaseValue.ToString(CultureInfo.InvariantCulture),
                ["karma"] = attribute.KarmaValue.ToString(CultureInfo.InvariantCulture),
                ["total"] = attribute.TotalValue.ToString(CultureInfo.InvariantCulture),
                ["availableKarma"] = attribute.AvailableKarma.ToString(CultureInfo.InvariantCulture),
                ["upgradeKarmaCost"] = attribute.UpgradeKarmaCost.ToString(CultureInfo.InvariantCulture),
                ["canCareerUpgrade"] = attribute.CanCareerUpgrade.ToString(CultureInfo.InvariantCulture)
            },
            CalculationTrace:
            [
                $"Base {attribute.BaseValue} + Karma {attribute.KarmaValue} projects total {attribute.TotalValue}.",
                $"Current engine projection reports career upgrade cost {attribute.UpgradeKarmaCost} with {attribute.AvailableKarma} Karma available."
            ],
            RuleId: "career-attribute-upgrade");

    private static IReadOnlyList<string> InferExpertiseTags(
        CharacterSkillsSection skills,
        CharacterAwakeningSection awakening)
    {
        string searchable = string.Join('|', skills.Skills.Select(static skill =>
            $"{skill.Name}|{skill.CustomName}|{skill.Category}|{skill.Suid}"));
        List<string> tags = [];
        AddTagIf(tags, searchable, "matrix-specialist", "hacking", "cybercombat", "computer", "electronics", "cracking");
        AddTagIf(tags, searchable, "face", "negotiation", "con", "etiquette", "leadership");
        AddTagIf(tags, searchable, "rigger", "pilot", "gunnery", "vehicle");
        AddTagIf(tags, searchable, "infiltrator", "stealth", "sneaking", "locksmith");
        AddTagIf(tags, searchable, "support", "first aid", "medicine", "instruction");
        AddTagIf(tags, searchable, "street-samurai", "firearms", "close combat", "blades", "automatics");
        if (awakening.MagEnabled || awakening.Adept || awakening.Magician)
        {
            tags.Add("astral");
        }
        if (awakening.ResEnabled || awakening.Technomancer)
        {
            tags.Add("matrix-specialist");
        }
        if (tags.Count == 0)
        {
            tags.Add("generalist");
        }

        return tags.Distinct(StringComparer.Ordinal).OrderBy(static tag => tag, StringComparer.Ordinal).ToArray();
    }

    private static void AddTagIf(
        ICollection<string> tags,
        string searchable,
        string tag,
        params string[] needles)
    {
        if (needles.Any(needle => searchable.Contains(needle, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(tag);
        }
    }

    private static void AddDistinct(
        ICollection<(CharacterAttributeDetailSummary Attribute, string Type, int Priority, string Posture)> selected,
        CharacterAttributeDetailSummary candidate,
        string type,
        int priority,
        string posture)
    {
        if (selected.Any(item => string.Equals(item.Attribute.Name, candidate.Name, StringComparison.Ordinal)))
        {
            return;
        }

        selected.Add((candidate, type, priority, posture));
    }

    private static string CreateCharacterId(
        BuildGhostWorkspaceAnalysisContext context,
        CharacterProfileSection profile)
        => $"{StableId(context.WorkspaceId)}:{StableId(FirstNonEmpty(profile.Alias, profile.Name, "runner"))}";

    private static string StableSkillId(CharacterSkillSummary skill)
        => StableId(FirstNonEmpty(skill.Suid, skill.Guid, skill.Name, skill.CustomName, "skill"));

    private static string StableId(string? value)
    {
        string normalized = new((value ?? string.Empty)
            .Trim()
            .ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray());
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-') is { Length: > 0 } result ? result : "unknown";
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Require(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Trim();

    private static string Format(decimal value)
        => value.ToString("0.############################", CultureInfo.InvariantCulture);
}
