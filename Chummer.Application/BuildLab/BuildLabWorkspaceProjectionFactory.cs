using System.Globalization;
using Chummer.Contracts.BuildLab;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.BuildLab;

public static class BuildLabWorkspaceProjectionFactory
{
    private const string PendingWorkspaceId = "pending-workspace";
    private const string DefaultWorkflowId = "workflow.build-lab";
    private static readonly string[] KnownRoleTags =
    [
        "matrix-specialist",
        "street-samurai",
        "infiltrator",
        "generalist",
        "astral",
        "rigger",
        "face"
    ];

    public static BuildLabConceptIntakeProjection Create(
        CharacterProfileSection profile,
        CharacterProgressSection? progress = null,
        CharacterRulesSection? rules = null,
        CharacterBuildSection? build = null,
        CharacterSkillsSection? skills = null,
        CharacterAwakeningSection? awakening = null,
        string? rulesetId = null,
        string? workspaceId = null,
        string? workflowId = null,
        string? sourceDocumentId = null,
        IBuildLabService? buildLabService = null)
    {
        ArgumentNullException.ThrowIfNull(profile);

        string effectiveRulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? RulesetDefaults.Sr5;
        string effectiveWorkspaceId = string.IsNullOrWhiteSpace(workspaceId) ? PendingWorkspaceId : workspaceId.Trim();
        string effectiveWorkflowId = string.IsNullOrWhiteSpace(workflowId) ? DefaultWorkflowId : workflowId.Trim();
        string effectiveBuildMethod = FirstNonEmpty(build?.BuildMethod, profile.BuildMethod, "Priority");
        string characterId = CreateCharacterId(profile, effectiveRulesetId);
        IBuildLabService service = buildLabService ?? new DefaultBuildLabService();

        string[] roleTags = InferRoleTags(profile, progress, skills, awakening);
        string[] requiredRoleTags = InferRequiredRoleTags(roleTags, progress, awakening);
        IReadOnlyList<BuildVariantProjection> variants = service.GenerateBuildVariants(characterId, roleTags);
        IReadOnlyList<KarmaSpendProjection> progressionPaths = service.PlanProgressionPaths(characterId, roleTags, [], requiredRoleTags);
        BuildTeamCoverageProjection teamCoverage = service.EvaluateTeamCoverage(
            characterId,
            variants.Select(static variant => variant.VariantId).ToArray(),
            requiredRoleTags);
        Dictionary<string, KarmaSpendProjection> progressionByVariant = progressionPaths
            .GroupBy(static projection => projection.VariantId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        BuildLabVariantProjection[] variantCards = variants
            .Select(variant => CreateVariantProjection(
                characterId,
                variant,
                progressionByVariant.TryGetValue(variant.VariantId, out KarmaSpendProjection? path) ? path : null,
                teamCoverage,
                service))
            .ToArray();
        BuildLabProgressionTimeline[] timelines = progressionPaths
            .Select(path => CreateTimeline(path, sourceDocumentId))
            .ToArray();
        BuildLabTeamCoverageProjection teamCoverageProjection = CreateTeamCoverageProjection(teamCoverage);

        BuildLabVariantProjection topVariant = variantCards.FirstOrDefault()
            ?? new BuildLabVariantProjection(
                VariantId: $"{characterId}-generalist-1",
                Label: "Generalist Lane",
                Summary: "Deterministic Build Lab intake is ready for the first grounded handoff.",
                TableFit: "Grounded in current dossier truth.",
                RoleBadges: [],
                Metrics: [],
                Warnings: [],
                OverlapBadges: [],
                Actions: []);
        BuildLabProgressionTimeline? topTimeline = timelines.FirstOrDefault();
        string[] missingRequiredRoles = requiredRoleTags
            .Except(roleTags, StringComparer.Ordinal)
            .OrderBy(static tag => RoleOrder(tag))
            .ThenBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();

        BuildLabBadge[] roleBadges = roleTags
            .Select(static tag => new BuildLabBadge(
                BadgeId: tag,
                Label: FormatRoleTag(tag),
                Kind: BuildLabBadgeKinds.Role,
                Emphasized: true))
            .ToArray();
        BuildLabBadge[] constraintBadges = BuildConstraintBadges(missingRequiredRoles, rules);
        BuildLabBadge[] provenanceBadges = BuildProvenanceBadges(profile, rules, effectiveRulesetId, effectiveBuildMethod);

        string displayName = FirstNonEmpty(profile.Name, profile.Alias, "Runner");
        string runtimeSummary = CreateRuntimeCompatibilitySummary(rules, effectiveRulesetId);
        string campaignFitSummary = CreateCampaignFitSummary(teamCoverageProjection);
        string supportClosureSummary = CreateSupportClosureSummary(topTimeline, topVariant);
        string[] watchouts = BuildWatchouts(variantCards, teamCoverageProjection, topTimeline);
        BuildLabExportPayload[] exportPayloads = BuildExportPayloads(
            displayName,
            effectiveBuildMethod,
            profile,
            topVariant,
            topTimeline,
            teamCoverageProjection,
            runtimeSummary,
            sourceDocumentId);
        BuildLabExportTarget[] exportTargets = BuildExportTargets();
        BuildLabActionDescriptor[] actions =
        [
            new BuildLabActionDescriptor(
                ActionId: "next-variants",
                Label: "Hand Off",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.build-idea-card"),
            new BuildLabActionDescriptor(
                ActionId: "save-template",
                Label: "Save Template",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.character-template"),
            new BuildLabActionDescriptor(
                ActionId: "open-foundry-export",
                Label: "Export Foundry JSON",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.foundry-export"),
            new BuildLabActionDescriptor(
                ActionId: "open-json-exchange",
                Label: "Export JSON Exchange",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.json-exchange"),
            new BuildLabActionDescriptor(
                ActionId: "open-sheet-viewer",
                Label: "Open Sheet Viewer",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.sheet-viewer"),
            new BuildLabActionDescriptor(
                ActionId: "open-print-pdf-export",
                Label: "Export Print PDF",
                SurfaceId: BuildLabSurfaceIds.ExportRail,
                Enabled: true,
                TargetId: "target.print-pdf-export")
        ];

        return new BuildLabConceptIntakeProjection(
            WorkspaceId: effectiveWorkspaceId,
            WorkflowId: effectiveWorkflowId,
            Title: $"{displayName} Build Lab Intake",
            Summary: $"Ground deterministic build variants, progression checkpoints, and crew-fit tradeoffs on the current {FormatRulesetLabel(effectiveRulesetId)} dossier.",
            RulesetId: effectiveRulesetId,
            BuildMethod: effectiveBuildMethod,
            IntakeFields:
            [
                new BuildLabIntakeField(
                    FieldId: "concept",
                    Label: "Concept",
                    Kind: BuildLabFieldKinds.Text,
                    Value: FirstNonEmpty(profile.Concept, profile.Alias, profile.Name, "Runner concept"),
                    HelpText: "Current dossier truth seeds the first deterministic Build Lab pass.",
                    Required: true),
                new BuildLabIntakeField(
                    FieldId: "table-constraints",
                    Label: "Campaign Need",
                    Kind: BuildLabFieldKinds.Multiline,
                    Value: campaignFitSummary,
                    HelpText: "Crew-fit pressure stays explicit so handoff gaps remain auditable.")
            ],
            RoleBadges: roleBadges,
            ConstraintBadges: constraintBadges,
            ProvenanceBadges: provenanceBadges,
            Variants: variantCards,
            ProgressionTimelines: timelines,
            ExportPayloads: exportPayloads,
            ExportTargets: exportTargets,
            Actions: actions,
            ExplainEntryId: $"buildlab.intake.{characterId}",
            SourceDocumentId: FirstNonEmpty(sourceDocumentId, CreateSourceDocumentId(rules, effectiveRulesetId)),
            CanContinue: variantCards.Length > 0,
            NextSafeAction: "Review the strongest grounded variant before exporting a governed handoff payload.",
            RuntimeCompatibilitySummary: runtimeSummary,
            CampaignFitSummary: campaignFitSummary,
            SupportClosureSummary: supportClosureSummary,
            Watchouts: watchouts,
            TeamCoverage: teamCoverageProjection);
    }

    public static BuildLabConceptIntakeProjection BindWorkspaceId(BuildLabConceptIntakeProjection projection, string workspaceId)
    {
        ArgumentNullException.ThrowIfNull(projection);

        if (string.IsNullOrWhiteSpace(workspaceId))
        {
            return projection;
        }

        return projection with
        {
            WorkspaceId = workspaceId.Trim()
        };
    }

    private static BuildLabVariantProjection CreateVariantProjection(
        string characterId,
        BuildVariantProjection variant,
        KarmaSpendProjection? path,
        BuildTeamCoverageProjection teamCoverage,
        IBuildLabService service)
    {
        IReadOnlyList<BuildTrapChoice> traps = service.DetectTrapChoices(characterId, variant.VariantId);
        BuildCorePackageSuggestion[] packages = service.SuggestCorePackages(characterId, variant.VariantId).ToArray();
        BuildLabVariantMetric[] metrics =
        [
            new BuildLabVariantMetric(
                MetricId: "rank",
                Label: "Rank",
                Value: FormatDecimal(variant.Rank),
                Emphasized: variant.Rank >= 85m),
            new BuildLabVariantMetric(
                MetricId: "constraint-coverage",
                Label: "Constraint coverage",
                Value: path?.ConstraintCoverageScore is decimal coverageScore ? $"{FormatDecimal(coverageScore)}%" : "Grounded",
                Delta: DescribeConstraintCoverageDelta(path),
                Emphasized: (path?.ConstraintCoverageScore ?? 0m) >= 100m),
            new BuildLabVariantMetric(
                MetricId: "role-pressure",
                Label: "Role pressure",
                Value: $"{FormatDecimal(teamCoverage.RolePressureScore)}%",
                Delta: teamCoverage.MissingRoleTags.Count > 0
                    ? $"{teamCoverage.MissingRoleTags.Count} missing"
                    : "Crew-aligned",
                Emphasized: teamCoverage.RolePressureScore >= 70m),
            new BuildLabVariantMetric(
                MetricId: "core-packages",
                Label: "Core packages",
                Value: packages.Length.ToString(CultureInfo.InvariantCulture),
                Emphasized: packages.Length > 0)
        ];

        BuildLabVariantWarning[] warnings = BuildVariantWarnings(variant, path, traps);
        BuildLabBadge[] overlapBadges = BuildOverlapBadges(variant, teamCoverage);
        string tagLabel = FormatRoleTag(variant.RoleTags.FirstOrDefault() ?? "generalist");
        string tableFit = path?.ConstraintCoverageScore is decimal coverageScoreValue
            ? $"{FormatDecimal(coverageScoreValue)}% of current crew asks stay covered for the next handoff."
            : "Current crew asks are still being derived from the dossier.";

        return new BuildLabVariantProjection(
            VariantId: variant.VariantId,
            Label: $"{tagLabel} Lane",
            Summary: $"{tagLabel} path keeps deterministic tradeoffs visible with rank {FormatDecimal(variant.Rank)} and {warnings.Length} surfaced watchout(s).",
            TableFit: tableFit,
            RoleBadges: variant.RoleTags
                .Select(static tag => new BuildLabBadge(
                    BadgeId: tag,
                    Label: FormatRoleTag(tag),
                    Kind: BuildLabBadgeKinds.Role,
                    Emphasized: true))
                .ToArray(),
            Metrics: metrics,
            Warnings: warnings,
            OverlapBadges: overlapBadges,
            Actions:
            [
                new BuildLabActionDescriptor(
                    ActionId: $"inspect-{variant.VariantId}",
                    Label: "Inspect Timeline",
                    SurfaceId: BuildLabSurfaceIds.ProgressionTimelineRail,
                    Enabled: true,
                    TargetId: variant.VariantId)
            ],
            ExplainEntryId: variant.ExplainEntryId);
    }

    private static BuildLabProgressionTimeline CreateTimeline(KarmaSpendProjection path, string? sourceDocumentId)
    {
        string tag = ExtractTag(path.VariantId);
        return new BuildLabProgressionTimeline(
            TimelineId: $"timeline.{path.VariantId}",
            Title: $"{FormatRoleTag(tag)} Progression",
            Summary: CreateTimelineSummary(path),
            VariantId: path.VariantId,
            Steps: path.Steps
                .Select((step, index) => CreateTimelineStep(step, path, index))
                .ToArray(),
            SourceDocumentId: sourceDocumentId);
    }

    private static BuildLabProgressionStep CreateTimelineStep(KarmaSpendStep step, KarmaSpendProjection path, int index)
    {
        List<BuildLabVariantMetric> outcomes = step.Scores
            .Select(score => new BuildLabVariantMetric(
                MetricId: score.MetricId,
                Label: HumanizeIdentifier(score.MetricId),
                Value: FormatDecimal(score.Value),
                Emphasized: score.Value >= 75m))
            .ToList();

        if (index == path.Steps.Count - 1 && path.ConstraintCoverageScore is decimal coverageScore)
        {
            outcomes.Add(new BuildLabVariantMetric(
                MetricId: "constraint-coverage",
                Label: "Constraint coverage",
                Value: $"{FormatDecimal(coverageScore)}%",
                Emphasized: coverageScore >= 100m));
        }

        BuildLabBadge[] riskBadges = BuildRiskBadges(step, path, index);

        return new BuildLabProgressionStep(
            StepId: step.StepId,
            KarmaTarget: step.KarmaTotal,
            Label: BuildCheckpointLabel(index, step.KarmaTotal),
            Summary: CreateStepSummary(step),
            Outcomes: outcomes,
            MilestoneBadges:
            [
                new BuildLabBadge(
                    BadgeId: $"karma-{step.KarmaTotal}",
                    Label: $"{step.KarmaTotal} Karma",
                    Kind: BuildLabBadgeKinds.Milestone,
                    Emphasized: true)
            ],
            RiskBadges: riskBadges,
            ExplainEntryId: step.ExplainEntryId);
    }

    private static BuildLabTeamCoverageProjection CreateTeamCoverageProjection(BuildTeamCoverageProjection teamCoverage)
    {
        int requiredRoleCount = ReadIntParameter(teamCoverage.SummaryParameters, "requiredRoleCount");
        int coveredRoleCount = ReadIntParameter(teamCoverage.SummaryParameters, "coveredRoleCount");
        string coveredRoles = teamCoverage.CoveredRoleTags is { Count: > 0 }
            ? FormatRoleTags(teamCoverage.CoveredRoleTags)
            : "none";
        string missingRoles = teamCoverage.MissingRoleTags.Count > 0
            ? FormatRoleTags(teamCoverage.MissingRoleTags)
            : "none";
        string duplicateRoles = teamCoverage.DuplicateRoleTags is { Count: > 0 }
            ? FormatRoleTags(teamCoverage.DuplicateRoleTags)
            : "none";

        return new BuildLabTeamCoverageProjection(
            Summary: $"{coveredRoleCount} of {requiredRoleCount} required crew roles stay covered before handoff; missing and duplicate pressure remains explicit instead of hidden.",
            CoverageSummary: $"Coverage score is {FormatDecimal(teamCoverage.CoverageScore)}% with {coveredRoles} already covered and {missingRoles} still missing.",
            RolePressureSummary: $"Role pressure is {FormatDecimal(teamCoverage.RolePressureScore)}%; duplicate lanes stay visible as {duplicateRoles}.",
            MissingRoleTags: teamCoverage.MissingRoleTags.ToArray(),
            CoveredRoleTags: teamCoverage.CoveredRoleTags?.ToArray() ?? [],
            DuplicateRoleTags: teamCoverage.DuplicateRoleTags?.ToArray() ?? [],
            ExplainEntryId: teamCoverage.ExplainEntryId);
    }

    private static BuildLabVariantWarning[] BuildVariantWarnings(
        BuildVariantProjection variant,
        KarmaSpendProjection? path,
        IReadOnlyList<BuildTrapChoice> traps)
    {
        List<BuildLabVariantWarning> warnings = [];

        foreach (BuildTrapChoice trap in traps)
        {
            warnings.Add(new BuildLabVariantWarning(
                WarningId: trap.ChoiceId,
                Label: HumanizeExplainKey(trap.ReasonKey),
                Detail: CreateTrapWarningDetail(trap),
                Kind: BuildLabWarningKinds.Trap,
                Emphasized: string.Equals(trap.Severity, RulesetCapabilityDiagnosticSeverities.Warning, StringComparison.OrdinalIgnoreCase),
                ExplainEntryId: trap.ExplainEntryId));
        }

        foreach (BuildVariantConstraint constraint in variant.Constraints)
        {
            warnings.Add(new BuildLabVariantWarning(
                WarningId: constraint.ConstraintId,
                Label: HumanizeExplainKey(constraint.ConstraintKey),
                Detail: "Secondary-role breadth stays explicit so the lane does not masquerade as a pure primary-role plan.",
                Kind: BuildLabWarningKinds.Trap,
                Emphasized: true));
        }

        if (path?.MissingConstraintTags is { Count: > 0 } missingConstraintTags)
        {
            warnings.Add(new BuildLabVariantWarning(
                WarningId: $"{path.VariantId}:constraint-gap",
                Label: "Constraint gap",
                Detail: $"Still missing crew asks: {FormatRoleTags(missingConstraintTags)}.",
                Kind: BuildLabWarningKinds.Trap,
                Emphasized: true,
                ExplainEntryId: path.ExplainEntryId));
        }

        return warnings
            .GroupBy(static warning => warning.WarningId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Take(3)
            .ToArray();
    }

    private static BuildLabBadge[] BuildOverlapBadges(BuildVariantProjection variant, BuildTeamCoverageProjection teamCoverage)
    {
        List<BuildLabBadge> badges = [];
        foreach (BuildRoleOverlap overlap in teamCoverage.RoleOverlaps)
        {
            string? peerVariantId = null;
            if (string.Equals(overlap.LeftVariantId, variant.VariantId, StringComparison.Ordinal))
            {
                peerVariantId = overlap.RightVariantId;
            }
            else if (string.Equals(overlap.RightVariantId, variant.VariantId, StringComparison.Ordinal))
            {
                peerVariantId = overlap.LeftVariantId;
            }

            if (peerVariantId is null || overlap.OverlapScore < 0.6m)
            {
                continue;
            }

            badges.Add(new BuildLabBadge(
                BadgeId: $"{variant.VariantId}:{peerVariantId}:overlap",
                Label: $"{FormatRoleTag(ExtractTag(peerVariantId))} overlap {FormatDecimal(overlap.OverlapScore * 100m)}%",
                Kind: BuildLabBadgeKinds.Overlap,
                Emphasized: overlap.OverlapScore >= 0.85m));
        }

        return badges.ToArray();
    }

    private static BuildLabBadge[] BuildConstraintBadges(string[] missingRequiredRoles, CharacterRulesSection? rules)
    {
        List<BuildLabBadge> badges = missingRequiredRoles
            .Select(static tag => new BuildLabBadge(
                BadgeId: $"need-{tag}",
                Label: $"Need {FormatRoleTag(tag)}",
                Kind: BuildLabBadgeKinds.Constraint,
                Emphasized: true))
            .ToList();

        if (badges.Count == 0 && rules is not null)
        {
            string gameplayOption = FirstNonEmpty(rules.GameplayOption, rules.Settings, rules.GameEdition);
            if (!string.IsNullOrWhiteSpace(gameplayOption))
            {
                badges.Add(new BuildLabBadge(
                    BadgeId: "rule-environment",
                    Label: gameplayOption,
                    Kind: BuildLabBadgeKinds.Constraint,
                    Emphasized: false));
            }
        }

        return badges.ToArray();
    }

    private static BuildLabBadge[] BuildProvenanceBadges(
        CharacterProfileSection profile,
        CharacterRulesSection? rules,
        string rulesetId,
        string buildMethod)
    {
        List<BuildLabBadge> badges =
        [
            new BuildLabBadge(
                BadgeId: $"ruleset-{rulesetId}",
                Label: FormatRulesetLabel(rulesetId),
                Kind: BuildLabBadgeKinds.Provenance,
                Emphasized: true),
            new BuildLabBadge(
                BadgeId: $"build-method-{buildMethod}",
                Label: buildMethod,
                Kind: BuildLabBadgeKinds.Provenance,
                Emphasized: false),
            new BuildLabBadge(
                BadgeId: profile.Created ? "career-dossier" : "creation-draft",
                Label: profile.Created ? "Career dossier" : "Creation draft",
                Kind: BuildLabBadgeKinds.Provenance,
                Emphasized: profile.Created)
        ];

        if (!string.IsNullOrWhiteSpace(rules?.Settings))
        {
            badges.Add(new BuildLabBadge(
                BadgeId: "settings",
                Label: rules.Settings,
                Kind: BuildLabBadgeKinds.Provenance,
                Emphasized: false));
        }

        return badges.ToArray();
    }

    private static BuildLabExportPayload[] BuildExportPayloads(
        string displayName,
        string buildMethod,
        CharacterProfileSection profile,
        BuildLabVariantProjection topVariant,
        BuildLabProgressionTimeline? topTimeline,
        BuildLabTeamCoverageProjection teamCoverage,
        string runtimeSummary,
        string? sourceDocumentId)
    {
        return
        [
            new BuildLabExportPayload(
                PayloadId: "payload.build-lab-handoff",
                Title: $"{displayName} Build Lab Handoff",
                Summary: "Governed payload for Build Idea Card and downstream dossier or campaign handoff flows.",
                PayloadKind: "build-lab-handoff",
                VariantId: topVariant.VariantId,
                TimelineId: topTimeline?.TimelineId,
                QueryText: $"{displayName} {topVariant.Label} {buildMethod}".Trim(),
                SourceDocumentId: sourceDocumentId,
                Fields:
                [
                    new BuildLabExportField(
                        FieldId: "concept",
                        Label: "Concept",
                        Value: FirstNonEmpty(profile.Concept, profile.Alias, profile.Name)),
                    new BuildLabExportField(
                        FieldId: "variant",
                        Label: "Variant",
                        Value: topVariant.Label,
                        Emphasized: true),
                    new BuildLabExportField(
                        FieldId: "campaign-fit",
                        Label: "Campaign fit",
                        Value: teamCoverage.CoverageSummary),
                    new BuildLabExportField(
                        FieldId: "rule-environment",
                        Label: "Rule environment",
                        Value: runtimeSummary),
                    new BuildLabExportField(
                        FieldId: "explain-receipt",
                        Label: "Explain receipt",
                        Value: CreateExplainReceiptFieldValue(topVariant, topTimeline))
                ])
        ];
    }

    private static string CreateExplainReceiptFieldValue(
        BuildLabVariantProjection topVariant,
        BuildLabProgressionTimeline? topTimeline)
    {
        string candidate = FirstNonEmpty(
            topVariant.ExplainEntryId,
            topTimeline?.Steps.LastOrDefault()?.ExplainEntryId,
            "buildlab.intake.ungrounded");

        if (candidate.Contains("buildlab", StringComparison.OrdinalIgnoreCase))
        {
            return candidate;
        }

        return $"buildlab.receipt.{candidate}";
    }

    private static BuildLabExportTarget[] BuildExportTargets()
    {
        return
        [
            new BuildLabExportTarget(
                TargetId: "target.build-idea-card",
                Label: "Build Idea Card",
                TargetKind: BuildLabExportTargetKinds.BuildIdeaCard,
                WorkflowId: "workflow.coach.build-ideas",
                Enabled: true,
                Description: "Open grounded Build Idea Card search with the current Build Lab payload.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "next-variants",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "explicit-handoff",
                        Label: "Governed handoff",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: true)
                ]),
            new BuildLabExportTarget(
                TargetId: "target.character-template",
                Label: "Character Template",
                TargetKind: BuildLabExportTargetKinds.CharacterTemplate,
                WorkflowId: "workflow.templates.character",
                Enabled: true,
                Description: "Save this deterministic lane as a local reusable template without re-entering Build Lab intake.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "save-template",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "template-ready",
                        Label: "Template-ready",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: false)
                ]),
            new BuildLabExportTarget(
                TargetId: "target.foundry-export",
                Label: "Foundry JSON Export",
                TargetKind: BuildLabExportTargetKinds.Workflow,
                WorkflowId: "workflow.exchange.foundry",
                Enabled: true,
                Description: "Prepare a governed Foundry-class exchange payload from this Build Lab handoff without forking dossier truth.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "open-foundry-export",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "exchange-governed",
                        Label: "Exchange-governed",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: true)
                ]),
            new BuildLabExportTarget(
                TargetId: "target.json-exchange",
                Label: "JSON Exchange Export",
                TargetKind: BuildLabExportTargetKinds.Workflow,
                WorkflowId: "workflow.exchange.json",
                Enabled: true,
                Description: "Prepare a governed JSON exchange payload from this Build Lab handoff before import, compare, or publication follow-through.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "open-json-exchange",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "json-exchange-governed",
                        Label: "JSON-governed",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: true)
                ]),
            new BuildLabExportTarget(
                TargetId: "target.sheet-viewer",
                Label: "Sheet Viewer",
                TargetKind: BuildLabExportTargetKinds.Workflow,
                WorkflowId: "workflow.viewer.sheet",
                Enabled: true,
                Description: "Open the current Build Lab handoff in the governed sheet viewer before print/export decisions.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "open-sheet-viewer",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "viewer-safe",
                        Label: "Viewer-safe",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: false)
                ]),
            new BuildLabExportTarget(
                TargetId: "target.print-pdf-export",
                Label: "Print PDF Export",
                TargetKind: BuildLabExportTargetKinds.Workflow,
                WorkflowId: "workflow.export.pdf",
                Enabled: true,
                Description: "Prepare a governed print-ready PDF export from this Build Lab handoff on the same rules and explain lane.",
                PayloadId: "payload.build-lab-handoff",
                ActionId: "open-print-pdf-export",
                Badges:
                [
                    new BuildLabBadge(
                        BadgeId: "print-ready",
                        Label: "Print-ready",
                        Kind: BuildLabBadgeKinds.Export,
                        Emphasized: false)
                ])
        ];
    }

    private static BuildLabBadge[] BuildRiskBadges(KarmaSpendStep step, KarmaSpendProjection path, int index)
    {
        List<BuildLabBadge> badges = step.Diagnostics?
            .Select(static diagnostic => new BuildLabBadge(
                BadgeId: diagnostic.Code,
                Label: HumanizeExplainKey(FirstNonEmpty(diagnostic.MessageKey, diagnostic.Message, diagnostic.Code)),
                Kind: BuildLabBadgeKinds.Risk,
                Emphasized: string.Equals(diagnostic.Severity, RulesetCapabilityDiagnosticSeverities.Warning, StringComparison.OrdinalIgnoreCase)))
            .ToList()
            ?? [];

        if (index == path.Steps.Count - 1 && path.MissingConstraintTags is { Count: > 0 } missingConstraintTags)
        {
            badges.Add(new BuildLabBadge(
                BadgeId: $"{path.VariantId}:missing-crew-asks",
                Label: $"Missing {FormatRoleTags(missingConstraintTags)}",
                Kind: BuildLabBadgeKinds.Risk,
                Emphasized: true));
        }

        return badges.ToArray();
    }

    private static string[] BuildWatchouts(
        IReadOnlyList<BuildLabVariantProjection> variants,
        BuildLabTeamCoverageProjection teamCoverage,
        BuildLabProgressionTimeline? topTimeline)
    {
        List<string> watchouts = [];

        if (teamCoverage.MissingRoleTags.Count > 0)
        {
            watchouts.Add($"Crew still needs {FormatRoleTags(teamCoverage.MissingRoleTags)} before the handoff is fully role-safe.");
        }

        if (teamCoverage.DuplicateRoleTags is { Count: > 0 } duplicateRoleTags)
        {
            watchouts.Add($"Duplicate lane pressure remains visible on {FormatRoleTags(duplicateRoleTags)}.");
        }

        if (topTimeline is not null)
        {
            BuildLabProgressionStep? riskyStep = topTimeline.Steps.LastOrDefault(static step => step.RiskBadges.Count > 0);
            if (riskyStep is not null)
            {
                watchouts.Add($"{topTimeline.Title}: {riskyStep.KarmaTarget} Karma checkpoint still carries {riskyStep.RiskBadges.Count} explicit risk badge(s).");
            }
        }

        foreach (BuildLabVariantWarning warning in variants.SelectMany(static variant => variant.Warnings))
        {
            watchouts.Add($"{warning.Label}: {warning.Detail}");
        }

        return watchouts
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToArray();
    }

    private static string CreateRuntimeCompatibilitySummary(CharacterRulesSection? rules, string rulesetId)
    {
        string rulesetLabel = FormatRulesetLabel(rulesetId);
        string rulesEnvironment = FirstNonEmpty(rules?.Settings, rules?.GameplayOption, rules?.GameEdition, rulesetLabel);
        return $"Grounded in {rulesEnvironment} under the {rulesetLabel} runtime surface.";
    }

    private static string CreateCampaignFitSummary(BuildLabTeamCoverageProjection teamCoverage)
    {
        return string.IsNullOrWhiteSpace(teamCoverage.CoverageSummary)
            ? teamCoverage.Summary
            : teamCoverage.CoverageSummary;
    }

    private static string CreateSupportClosureSummary(BuildLabProgressionTimeline? topTimeline, BuildLabVariantProjection topVariant)
    {
        if (topTimeline is null)
        {
            return $"Top lane {topVariant.Label} is ready for compare-first review before export.";
        }

        BuildLabProgressionStep finalStep = topTimeline.Steps.Last();
        return $"{topTimeline.Title} reaches a governed {finalStep.KarmaTarget} Karma handoff checkpoint with explicit receipts instead of a hidden plan.";
    }

    private static string CreateTimelineSummary(KarmaSpendProjection path)
    {
        string coverageSummary = path.ConstraintCoverageScore is decimal coverageScore
            ? $"{FormatDecimal(coverageScore)}% crew coverage"
            : "grounded crew coverage";
        string tradeoff = string.IsNullOrWhiteSpace(path.TradeoffSummaryKey)
            ? "tradeoffs stay explicit"
            : HumanizeExplainKey(path.TradeoffSummaryKey);
        return $"{coverageSummary}; {tradeoff.ToLowerInvariant()} across {path.Steps.Count} checkpoint(s).";
    }

    private static string CreateStepSummary(KarmaSpendStep step)
    {
        decimal consistency = step.Scores
            .FirstOrDefault(static score => string.Equals(score.MetricId, "consistency", StringComparison.Ordinal))?.Value ?? 0m;
        decimal ceiling = step.Scores
            .FirstOrDefault(static score => string.Equals(score.MetricId, "ceiling", StringComparison.Ordinal))?.Value ?? 0m;
        return $"Consistency {FormatDecimal(consistency)} with late ceiling {FormatDecimal(ceiling)}.";
    }

    private static string CreateTrapWarningDetail(BuildTrapChoice trap)
    {
        return trap.ReasonKey switch
        {
            "buildlab.trap.resource-overcommit" => "This lane leans hard on both nuyen and karma, so the early build can outrun the table budget if it is left unchecked.",
            _ => "This lane carries an explicit deterministic watchout that should stay visible before handoff."
        };
    }

    private static string DescribeConstraintCoverageDelta(KarmaSpendProjection? path)
    {
        if (path is null)
        {
            return null!;
        }

        if (path.MissingConstraintTags is { Count: > 0 } missingConstraintTags)
        {
            return $"{missingConstraintTags.Count} missing";
        }

        if (path.MatchedConstraintTags is { Count: > 0 } matchedConstraintTags)
        {
            return $"{matchedConstraintTags.Count} aligned";
        }

        return "Unconstrained";
    }

    private static string BuildCheckpointLabel(int index, int karmaTotal)
    {
        return index switch
        {
            0 => "Opener",
            1 => "Reliability",
            2 => "Anchor",
            _ => $"{karmaTotal} Karma"
        };
    }

    private static string[] InferRoleTags(
        CharacterProfileSection profile,
        CharacterProgressSection? progress,
        CharacterSkillsSection? skills,
        CharacterAwakeningSection? awakening)
    {
        HashSet<string> tags = [];
        string searchableText = string.Join(
            ' ',
            new[]
            {
                profile.Name,
                profile.Alias,
                profile.Concept,
                profile.Description,
                profile.Background
            }.Where(static value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        if (ContainsAny(searchableText, "face", "social", "con artist", "negotiator", "diplomat"))
        {
            tags.Add("face");
        }

        if (ContainsAny(searchableText, "street samurai", "samurai", "combat", "gun", "mercenary", "shooter"))
        {
            tags.Add("street-samurai");
        }

        if (ContainsAny(searchableText, "matrix", "decker", "hacker", "technomancer", "cyberdeck")
            || progress?.ResEnabled == true
            || awakening?.Technomancer == true
            || profile.Technomancer)
        {
            tags.Add("matrix-specialist");
        }

        if (ContainsAny(searchableText, "mage", "magic", "shaman", "astral", "adept", "spell")
            || progress?.MagEnabled == true
            || awakening?.Magician == true
            || awakening?.Adept == true
            || profile.Magician
            || profile.Adept)
        {
            tags.Add("astral");
        }

        if (ContainsAny(searchableText, "rigger", "drone", "pilot", "vehicle"))
        {
            tags.Add("rigger");
        }

        if (ContainsAny(searchableText, "infiltrator", "stealth", "sneak", "scout"))
        {
            tags.Add("infiltrator");
        }

        if (skills is not null)
        {
            foreach (CharacterSkillSummary skill in skills.Skills)
            {
                string category = skill.Category?.Trim() ?? string.Empty;
                if (category.Contains("social", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("face");
                }

                if (category.Contains("combat", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("street-samurai");
                }

                if (category.Contains("vehicle", StringComparison.OrdinalIgnoreCase) || category.Contains("piloting", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("rigger");
                }

                if (category.Contains("magic", StringComparison.OrdinalIgnoreCase) || category.Contains("sorcery", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("astral");
                }

                if (category.Contains("matrix", StringComparison.OrdinalIgnoreCase) || category.Contains("resonance", StringComparison.OrdinalIgnoreCase))
                {
                    tags.Add("matrix-specialist");
                }
            }
        }

        if (tags.Count == 0)
        {
            tags.Add("generalist");
        }

        return tags
            .OrderBy(static tag => RoleOrder(tag))
            .ThenBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] InferRequiredRoleTags(
        IReadOnlyList<string> roleTags,
        CharacterProgressSection? progress,
        CharacterAwakeningSection? awakening)
    {
        HashSet<string> required = roleTags.ToHashSet(StringComparer.Ordinal);
        string primaryRole = roleTags.FirstOrDefault() ?? "generalist";

        switch (primaryRole)
        {
            case "face":
                required.Add("street-samurai");
                required.Add("matrix-specialist");
                break;
            case "street-samurai":
                required.Add("face");
                required.Add("matrix-specialist");
                break;
            case "matrix-specialist":
                required.Add("face");
                required.Add("street-samurai");
                break;
            case "astral":
                required.Add("face");
                required.Add("street-samurai");
                break;
            default:
                required.Add("face");
                required.Add("street-samurai");
                required.Add("matrix-specialist");
                break;
        }

        if (progress?.MagEnabled == true || awakening?.Magician == true || awakening?.Adept == true)
        {
            required.Add("astral");
        }

        if (progress?.ResEnabled == true || awakening?.Technomancer == true)
        {
            required.Add("matrix-specialist");
        }

        return required
            .OrderBy(static tag => RoleOrder(tag))
            .ThenBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
    }

    private static int ReadIntParameter(IReadOnlyList<RulesetExplainParameter> parameters, string name)
    {
        RulesetExplainParameter? parameter = parameters.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (parameter is null)
        {
            return 0;
        }

        if (parameter.Value.IntegerValue is long integerValue)
        {
            return checked((int)integerValue);
        }

        if (parameter.Value.DecimalValue is decimal decimalValue)
        {
            return decimal.ToInt32(decimalValue);
        }

        if (parameter.Value.NumberValue is double numberValue)
        {
            return Convert.ToInt32(numberValue, CultureInfo.InvariantCulture);
        }

        return int.TryParse(parameter.Value.StringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;
    }

    private static string CreateCharacterId(CharacterProfileSection profile, string rulesetId)
    {
        string raw = FirstNonEmpty(profile.Name, profile.Alias, $"{rulesetId}-runner");
        string normalized = new string(raw
            .ToLowerInvariant()
            .Select(static character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? $"{rulesetId}-runner" : normalized;
    }

    private static string CreateSourceDocumentId(CharacterRulesSection? rules, string rulesetId)
    {
        return string.IsNullOrWhiteSpace(rules?.Settings)
            ? $"workspace:{rulesetId}"
            : $"workspace:{rules.Settings}";
    }

    private static bool ContainsAny(string text, params string[] candidates)
    {
        return candidates.Any(candidate => text.Contains(candidate, StringComparison.Ordinal));
    }

    private static string ExtractTag(string variantId)
    {
        if (string.IsNullOrWhiteSpace(variantId))
        {
            return "generalist";
        }

        int lastDash = variantId.LastIndexOf('-');
        if (lastDash <= 0)
        {
            return "generalist";
        }

        string stem = variantId[..lastDash];
        string? matchedTag = KnownRoleTags
            .FirstOrDefault(tag => stem.EndsWith($"-{tag}", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(matchedTag))
        {
            return "generalist";
        }

        return matchedTag;
    }

    private static string FormatRulesetLabel(string rulesetId)
    {
        return rulesetId.ToUpperInvariant() switch
        {
            "SR4" => "SR4",
            "SR5" => "SR5",
            "SR6" => "SR6",
            _ => rulesetId.ToUpperInvariant()
        };
    }

    private static string FormatRoleTag(string roleTag)
    {
        string normalized = roleTag.Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? roleTag
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string FormatRoleTags(IEnumerable<string> roleTags)
    {
        return string.Join(" | ", roleTags.Select(FormatRoleTag));
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string HumanizeExplainKey(string? key)
    {
        string effectiveKey = FirstNonEmpty(key, "detail");
        string token = effectiveKey[(effectiveKey.LastIndexOf('.') + 1)..];
        return HumanizeIdentifier(token);
    }

    private static string HumanizeIdentifier(string identifier)
    {
        string normalized = identifier.Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? identifier
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static int RoleOrder(string roleTag)
    {
        return roleTag switch
        {
            "face" => 0,
            "street-samurai" => 1,
            "matrix-specialist" => 2,
            "astral" => 3,
            "rigger" => 4,
            "infiltrator" => 5,
            "generalist" => 6,
            _ => 100
        };
    }
}
