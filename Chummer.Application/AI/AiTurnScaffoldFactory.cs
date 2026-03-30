using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Chummer.Contracts.AI;
using Chummer.Contracts.BuildLab;
using Chummer.Application.BuildLab;

namespace Chummer.Application.AI;

internal static class AiTurnScaffoldFactory
{
    public static AiScaffoldTurnArtifacts CreateProviderArtifacts(string providerId, AiProviderTurnPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        IReadOnlyList<AiCitation> citations = CreateProviderCitations(plan.Grounding);
        IReadOnlyList<AiSuggestedAction> suggestedActions = CreateSuggestedActions(
            plan.RouteType,
            plan.AllowedTools,
            plan.Grounding.RuntimeFingerprint,
            plan.Grounding.CharacterId,
            plan.Grounding.WorkspaceId);
        IReadOnlyList<AiToolInvocation> toolInvocations = CreateToolInvocations(plan.AllowedTools, plan.RouteDecision.ToolingEnabled);

        if (string.Equals(plan.RouteType, AiRouteTypes.Build, StringComparison.Ordinal))
        {
            return CreateDeterministicBuildArtifacts(providerId, plan, citations, suggestedActions, toolInvocations);
        }

        return CreateArtifacts(
            providerId,
            plan.RouteType,
            plan.Grounding.RuntimeFingerprint,
            plan.Grounding.CharacterId,
            plan.Grounding.WorkspaceId,
            plan.Grounding.RetrievedItems,
            citations,
            suggestedActions,
            toolInvocations);
    }

    public static AiScaffoldTurnArtifacts CreateTransportArtifacts(string providerId, AiProviderTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        IReadOnlyList<AiCitation> citations = CreateTransportCitations(request);
        IReadOnlyList<AiSuggestedAction> suggestedActions = CreateSuggestedActions(
            request.RouteType,
            request.AllowedTools,
            request.RuntimeFingerprint,
            request.CharacterId,
            request.WorkspaceId);
        IReadOnlyList<AiToolInvocation> toolInvocations = CreateToolInvocations(request.AllowedTools, request.AllowedTools.Count > 0);
        IReadOnlyList<AiRetrievedItem> retrievedItems = request.RetrievalCorpusIds
            .Distinct(StringComparer.Ordinal)
            .Select(corpusId => new AiRetrievedItem(
                CorpusId: corpusId,
                ItemId: $"{request.RouteType}:{corpusId}",
                Title: GetCorpusTitle(corpusId),
                Summary: $"Prepared transport payload for the {corpusId} corpus."))
            .ToArray();

        return CreateArtifacts(
            providerId,
            request.RouteType,
            request.RuntimeFingerprint,
            request.CharacterId,
            request.WorkspaceId,
            retrievedItems,
            citations,
            suggestedActions,
            toolInvocations);
    }

    public static IReadOnlyList<AiCitation> CreateProviderCitations(AiGroundingBundle grounding)
    {
        ArgumentNullException.ThrowIfNull(grounding);

        List<AiCitation> citations = [];
        if (!string.IsNullOrWhiteSpace(grounding.RuntimeFingerprint))
        {
            citations.Add(new AiCitation(
                AiCitationKinds.Runtime,
                "Runtime Fingerprint",
                grounding.RuntimeFingerprint,
                Source: AiRetrievalCorpusIds.Runtime));
        }

        if (!string.IsNullOrWhiteSpace(grounding.CharacterId))
        {
            citations.Add(new AiCitation(
                AiCitationKinds.Character,
                "Character",
                grounding.CharacterId,
                Source: "character"));
        }

        citations.AddRange(grounding.RetrievedItems
            .Take(3)
            .Select(item => new AiCitation(
                AiCitationKinds.RetrievedItem,
                item.Title,
                item.ItemId,
                item.CorpusId)));

        return citations;
    }

    public static IReadOnlyList<AiCitation> CreateTransportCitations(AiProviderTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<AiCitation> citations = [];
        if (!string.IsNullOrWhiteSpace(request.RuntimeFingerprint))
        {
            citations.Add(new AiCitation(
                AiCitationKinds.Runtime,
                "Runtime Fingerprint",
                request.RuntimeFingerprint,
                Source: AiRetrievalCorpusIds.Runtime));
        }

        citations.AddRange(request.RetrievalCorpusIds
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .Select(corpusId => new AiCitation(
                AiCitationKinds.Corpus,
                GetCorpusTitle(corpusId),
                $"{request.RouteType}:{corpusId}",
                corpusId)));

        return citations;
    }

    public static IReadOnlyList<AiSuggestedAction> CreateSuggestedActions(
        string routeType,
        IReadOnlyList<AiToolDescriptor> allowedTools,
        string? runtimeFingerprint,
        string? characterId = null,
        string? workspaceId = null)
    {
        var actions = new List<AiSuggestedAction>();
        if (!string.IsNullOrWhiteSpace(runtimeFingerprint))
        {
            actions.Add(new AiSuggestedAction(
                AiSuggestedActionIds.OpenRuntimeInspector,
                "Open Runtime Inspector",
                "Inspect the active runtime fingerprint, pack bindings, and compatibility warnings.",
                RuntimeFingerprint: runtimeFingerprint,
                CharacterId: characterId,
                WorkspaceId: workspaceId));
        }

        if (ContainsTool(allowedTools, AiToolIds.SimulateKarmaSpend))
        {
            actions.Add(new AiSuggestedAction(
                AiSuggestedActionIds.PreviewKarmaSpend,
                "Preview Karma Spend",
                $"Run a non-mutating {routeType} preview for the current runtime.",
                RuntimeFingerprint: runtimeFingerprint,
                CharacterId: characterId,
                WorkspaceId: workspaceId));
        }

        if (ContainsTool(allowedTools, AiToolIds.SimulateNuyenSpend))
        {
            actions.Add(new AiSuggestedAction(
                AiSuggestedActionIds.PreviewNuyenSpend,
                "Preview Nuyen Spend",
                $"Run a non-mutating {routeType} nuyen-spend preview against the current runtime.",
                RuntimeFingerprint: runtimeFingerprint,
                CharacterId: characterId,
                WorkspaceId: workspaceId));
        }

        if (ContainsTool(allowedTools, AiToolIds.CreateApplyPreview))
        {
            actions.Add(new AiSuggestedAction(
                AiSuggestedActionIds.PreviewApplyPlan,
                "Preview Apply Plan",
                "Create a non-mutating apply preview for the strongest grounded follow-up action.",
                RuntimeFingerprint: runtimeFingerprint,
                CharacterId: characterId,
                WorkspaceId: workspaceId));
        }

        if (ContainsTool(allowedTools, AiToolIds.SearchBuildIdeas))
        {
            actions.Add(new AiSuggestedAction(
                AiSuggestedActionIds.BrowseBuildIdeas,
                "Browse Build Ideas",
                "Open the Chummer-grounded build idea corpus for related templates and coaching leads.",
                RuntimeFingerprint: runtimeFingerprint,
                CharacterId: characterId,
                WorkspaceId: workspaceId));
        }

        return actions;
    }

    public static IReadOnlyList<AiToolInvocation> CreateToolInvocations(
        IReadOnlyList<AiToolDescriptor> allowedTools,
        bool toolingEnabled)
    {
        if (!toolingEnabled || allowedTools.Count == 0)
        {
            return [];
        }

        return allowedTools
            .GroupBy(static tool => tool.ToolId, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .Select(toolId => new AiToolInvocation(
                toolId.ToolId,
                AiToolInvocationStatuses.Prepared,
                $"{toolId.Title} is available through the grounded Chummer AI scaffold."))
            .ToArray();
    }

    private static AiScaffoldTurnArtifacts CreateArtifacts(
        string providerId,
        string routeType,
        string? runtimeFingerprint,
        string? characterId,
        string? workspaceId,
        IReadOnlyList<AiRetrievedItem> retrievedItems,
        IReadOnlyList<AiCitation> citations,
        IReadOnlyList<AiSuggestedAction> suggestedActions,
        IReadOnlyList<AiToolInvocation> toolInvocations)
    {
        string? flavorLine = CreateFlavorLine(routeType, runtimeFingerprint);
        AiStructuredAnswer structuredAnswer = CreateStructuredAnswer(
            providerId,
            routeType,
            runtimeFingerprint,
            characterId,
            workspaceId,
            retrievedItems,
            citations,
            suggestedActions);

        return new AiScaffoldTurnArtifacts(
            Answer: CreateDisplayAnswer(flavorLine, structuredAnswer),
            FlavorLine: flavorLine,
            StructuredAnswer: structuredAnswer,
            Citations: citations,
            SuggestedActions: suggestedActions,
            ToolInvocations: toolInvocations);
    }

    private static AiScaffoldTurnArtifacts CreateDeterministicBuildArtifacts(
        string providerId,
        AiProviderTurnPlan plan,
        IReadOnlyList<AiCitation> citations,
        IReadOnlyList<AiSuggestedAction> suggestedActions,
        IReadOnlyList<AiToolInvocation> toolInvocations)
    {
        DeterministicBuildRouteModel model = BuildDeterministicBuildRouteModel(plan);
        string? flavorLine = CreateFlavorLine(plan.RouteType, plan.Grounding.RuntimeFingerprint);
        AiStructuredAnswer structuredAnswer = CreateDeterministicBuildStructuredAnswer(
            providerId,
            plan,
            model,
            citations,
            suggestedActions);

        return new AiScaffoldTurnArtifacts(
            Answer: CreateDisplayAnswer(flavorLine, structuredAnswer),
            FlavorLine: flavorLine,
            StructuredAnswer: structuredAnswer,
            Citations: citations,
            SuggestedActions: suggestedActions,
            ToolInvocations: toolInvocations);
    }

    private static AiStructuredAnswer CreateStructuredAnswer(
        string providerId,
        string routeType,
        string? runtimeFingerprint,
        string? characterId,
        string? workspaceId,
        IReadOnlyList<AiRetrievedItem> retrievedItems,
        IReadOnlyList<AiCitation> citations,
        IReadOnlyList<AiSuggestedAction> suggestedActions)
    {
        IReadOnlyList<AiRecommendation> recommendations = CreateRecommendations(routeType, retrievedItems, suggestedActions);
        IReadOnlyList<AiEvidenceEntry> evidence = CreateEvidence(citations);
        IReadOnlyList<AiRiskEntry> risks = CreateRisks(runtimeFingerprint, providerId);
        IReadOnlyList<AiSourceReference> sources = citations
            .Select(static citation => new AiSourceReference(citation.Kind, citation.Title, citation.ReferenceId, citation.Source))
            .ToArray();
        IReadOnlyList<AiActionDraft> actionDrafts = suggestedActions
            .Select(action => new AiActionDraft(
                ActionId: action.ActionId,
                Title: action.Title,
                Description: action.Description,
                Mode: AiActionDraftModes.PreviewOnly,
                RequiresConfirmation: action.RequiresConfirmation,
                RuntimeFingerprint: action.RuntimeFingerprint ?? runtimeFingerprint,
                CharacterId: action.CharacterId ?? characterId,
                WorkspaceId: action.WorkspaceId ?? workspaceId))
            .ToArray();

        string summary = CreateSummary(routeType, runtimeFingerprint, recommendations.Count, actionDrafts.Count, providerId);
        return new AiStructuredAnswer(
            Summary: summary,
            Recommendations: recommendations,
            Evidence: evidence,
            Risks: risks,
            Confidence: AiConfidenceLevels.Scaffolded,
            RuntimeFingerprint: runtimeFingerprint,
            Sources: sources,
            ActionDrafts: actionDrafts);
    }

    private static AiStructuredAnswer CreateDeterministicBuildStructuredAnswer(
        string providerId,
        AiProviderTurnPlan plan,
        DeterministicBuildRouteModel model,
        IReadOnlyList<AiCitation> citations,
        IReadOnlyList<AiSuggestedAction> suggestedActions)
    {
        IReadOnlyList<AiRecommendation> recommendations = CreateDeterministicBuildRecommendations(model);
        IReadOnlyList<AiEvidenceEntry> evidence = CreateDeterministicBuildEvidence(model, citations);
        IReadOnlyList<AiRiskEntry> risks = CreateDeterministicBuildRisks(model, providerId);
        IReadOnlyList<AiSourceReference> sources = citations
            .Select(static citation => new AiSourceReference(citation.Kind, citation.Title, citation.ReferenceId, citation.Source))
            .ToArray();
        IReadOnlyList<AiActionDraft> actionDrafts = CreateActionDrafts(
            suggestedActions,
            model.RuntimeFingerprint,
            model.CharacterId,
            model.WorkspaceId);

        return new AiStructuredAnswer(
            Summary: CreateDeterministicBuildSummary(model),
            Recommendations: recommendations,
            Evidence: evidence,
            Risks: risks,
            Confidence: AiConfidenceLevels.Grounded,
            RuntimeFingerprint: plan.Grounding.RuntimeFingerprint,
            Sources: sources,
            ActionDrafts: actionDrafts);
    }

    private static IReadOnlyList<AiRecommendation> CreateDeterministicBuildRecommendations(DeterministicBuildRouteModel model)
    {
        return model.OrderedPaths
            .Take(3)
            .Select((path, index) =>
            {
                BuildVariantProjection variant = model.VariantsById.GetValueOrDefault(path.VariantId)
                    ?? model.Variants[0];
                string roleLabel = FormatRoleTag(variant.RoleTags.FirstOrDefault() ?? "generalist");
                int matchedCount = path.MatchedConstraintTags?.Count ?? 0;
                int missingCount = path.MissingConstraintTags?.Count ?? 0;
                decimal earlyConsistency = ResolveStepMetric(path.Steps.FirstOrDefault(), "consistency");
                decimal lateCeiling = ResolveStepMetric(path.Steps.LastOrDefault(), "ceiling");
                string reason = matchedCount == 0
                    ? $"{roleLabel} keeps the current dossier lane deterministic while early consistency stays at {FormatDecimal(earlyConsistency)}."
                    : $"{roleLabel} keeps {matchedCount} inferred campaign ask(s) aligned while early consistency stays at {FormatDecimal(earlyConsistency)}.";
                if (missingCount > 0)
                {
                    reason = $"{reason} Missing capability pressure stays explicit on {FormatRoleTags(path.MissingConstraintTags!)}.";
                }

                string expectedEffect = $"25 / 50 / 100 Karma checkpoints finish at ceiling {FormatDecimal(lateCeiling)} with {FormatDecimal(path.ConstraintCoverageScore ?? 0m)}% crew-fit coverage.";
                return new AiRecommendation(
                    RecommendationId: $"build-route-{index + 1}",
                    Title: $"{roleLabel} path",
                    Reason: reason,
                    ExpectedEffect: expectedEffect,
                    RequiresPreview: true);
            })
            .ToArray();
    }

    private static IReadOnlyList<AiEvidenceEntry> CreateDeterministicBuildEvidence(
        DeterministicBuildRouteModel model,
        IReadOnlyList<AiCitation> citations)
    {
        List<AiEvidenceEntry> evidence =
        [
            new(
                Title: "Crew-fit coverage",
                Summary: $"{FormatDecimal(model.TeamCoverage.CoverageScore)}% inferred coverage with {FormatRoleTagsOrNone(model.TeamCoverage.CoveredRoleTags)} already covered and {FormatRoleTagsOrNone(model.TeamCoverage.MissingRoleTags)} still missing.",
                ReferenceId: model.TeamCoverage.ExplainEntryId ?? $"{model.CharacterId}:team-coverage",
                Source: "build-lab"),
            new(
                Title: "Role pressure",
                Summary: $"{FormatDecimal(model.TeamCoverage.RolePressureScore)}% role pressure across {model.Variants.Count} deterministic variant lane(s).",
                ReferenceId: model.TeamCoverage.ExplainEntryId ?? $"{model.CharacterId}:role-pressure",
                Source: "build-lab")
        ];

        KarmaSpendProjection leadPath = model.OrderedPaths[0];
        evidence.Add(new AiEvidenceEntry(
            Title: "Progression path",
            Summary: $"{FormatRoleTag(model.LeadVariant.RoleTags.FirstOrDefault() ?? "generalist")} reaches {FormatDecimal(leadPath.ConstraintCoverageScore ?? 0m)}% inferred crew-fit coverage with explicit 25 / 50 / 100 Karma checkpoints.",
            ReferenceId: leadPath.ExplainEntryId ?? leadPath.VariantId,
            Source: "build-lab"));

        foreach (AiCitation citation in citations.Where(static citation =>
                     string.Equals(citation.Source, AiRetrievalCorpusIds.Community, StringComparison.Ordinal)
                     || string.Equals(citation.Kind, AiCitationKinds.Runtime, StringComparison.Ordinal)).Take(2))
        {
            evidence.Add(new AiEvidenceEntry(
                Title: citation.Title,
                Summary: $"Grounded from {citation.Source ?? citation.Kind}.",
                ReferenceId: citation.ReferenceId,
                Source: citation.Source));
        }

        return evidence
            .GroupBy(static item => item.ReferenceId, StringComparer.Ordinal)
            .Select(static group => group.First())
            .Take(4)
            .ToArray();
    }

    private static IReadOnlyList<AiRiskEntry> CreateDeterministicBuildRisks(
        DeterministicBuildRouteModel model,
        string providerId)
    {
        List<AiRiskEntry> risks =
        [
            new(
                Severity: AiRiskSeverities.Note,
                Title: "Provider transport is still scaffolded",
                Summary: $"The {providerId} adapter is still stub-backed, but the ranking, path checkpoints, and crew-fit pressure come from the deterministic Build Lab service."),
            new(
                Severity: AiRiskSeverities.Note,
                Title: "Mutation remains explicit",
                Summary: "Applying spend or handoff changes still requires a separate explicit preview or approval flow.")
        ];

        if (model.TeamCoverage.MissingRoleTags.Count > 0)
        {
            risks.Insert(0, new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "Missing campaign role coverage",
                Summary: $"The current route still leaves {FormatRoleTags(model.TeamCoverage.MissingRoleTags)} uncovered for the inferred campaign ask."));
        }

        if (model.TeamCoverage.DuplicateRoleTags is { Count: > 0 } duplicateRoleTags)
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "Duplicate role pressure",
                Summary: $"Duplicate lanes remain visible on {FormatRoleTags(duplicateRoleTags)} before the handoff leaves comparison mode."));
        }

        BuildRoleOverlap? highestOverlap = model.TeamCoverage.RoleOverlaps
            .OrderByDescending(static overlap => overlap.OverlapScore)
            .FirstOrDefault();
        if (highestOverlap is not null && highestOverlap.OverlapScore >= 0.6m)
        {
            risks.Add(new AiRiskEntry(
                Severity: highestOverlap.OverlapScore >= 0.85m ? AiRiskSeverities.Warning : AiRiskSeverities.Note,
                Title: "Role overlap remains visible",
                Summary: $"{FormatRoleTag(ResolveVariantTag(model, highestOverlap.LeftVariantId))} and {FormatRoleTag(ResolveVariantTag(model, highestOverlap.RightVariantId))} overlap at {FormatDecimal(highestOverlap.OverlapScore * 100m)}%."));
        }

        if (string.IsNullOrWhiteSpace(model.RuntimeFingerprint))
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "No pinned runtime fingerprint",
                Summary: "The deterministic planner still runs, but runtime-specific compatibility proof is limited until a concrete runtime fingerprint is attached."));
        }

        return risks.ToArray();
    }

    private static string CreateDeterministicBuildSummary(DeterministicBuildRouteModel model)
    {
        IReadOnlyList<string> missingRoles = model.TeamCoverage.MissingRoleTags;
        string missingSummary = missingRoles.Count == 0
            ? "no missing campaign roles were inferred from the current ask"
            : $"missing {FormatRoleTags(missingRoles)}";
        return $"Deterministic Build Lab planner ranked {model.OrderedPaths.Count} path(s) for {model.DisplayName} on {DescribeRuntime(model.RuntimeFingerprint)}; top lane {FormatRoleTag(model.LeadVariant.RoleTags.FirstOrDefault() ?? "generalist")} lands at {FormatDecimal(model.OrderedPaths[0].ConstraintCoverageScore ?? 0m)}% crew-fit coverage with {FormatDecimal(model.TeamCoverage.RolePressureScore)}% role pressure and {missingSummary}.";
    }

    private static string CreateSummary(
        string routeType,
        string? runtimeFingerprint,
        int recommendationCount,
        int actionDraftCount,
        string providerId)
    {
        string runtimeSummary = string.IsNullOrWhiteSpace(runtimeFingerprint)
            ? "without a pinned runtime fingerprint"
            : $"against runtime {runtimeFingerprint}";

        return $"The {providerId} {routeType} scaffold stayed server-side, grounded {runtimeSummary}, prepared {recommendationCount} recommendation(s), and queued {actionDraftCount} preview-only follow-up draft(s).";
    }

    private static string CreateDisplayAnswer(string? flavorLine, AiStructuredAnswer structuredAnswer)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(flavorLine))
        {
            builder.AppendLine(flavorLine);
        }

        builder.Append(structuredAnswer.Summary);
        if (structuredAnswer.Recommendations.Count > 0)
        {
            builder.Append(" Next leads: ");
            builder.Append(string.Join("; ", structuredAnswer.Recommendations
                .Take(2)
                .Select(static recommendation => recommendation.Title)));
            builder.Append('.');
        }

        return builder.ToString().Trim();
    }

    private static IReadOnlyList<AiActionDraft> CreateActionDrafts(
        IReadOnlyList<AiSuggestedAction> suggestedActions,
        string? runtimeFingerprint,
        string? characterId,
        string? workspaceId)
    {
        return suggestedActions
            .Select(action => new AiActionDraft(
                ActionId: action.ActionId,
                Title: action.Title,
                Description: action.Description,
                Mode: AiActionDraftModes.PreviewOnly,
                RequiresConfirmation: action.RequiresConfirmation,
                RuntimeFingerprint: action.RuntimeFingerprint ?? runtimeFingerprint,
                CharacterId: action.CharacterId ?? characterId,
                WorkspaceId: action.WorkspaceId ?? workspaceId))
            .ToArray();
    }

    private static string? CreateFlavorLine(string routeType, string? runtimeFingerprint)
        => routeType switch
        {
            AiRouteTypes.Coach or AiRouteTypes.Build => string.IsNullOrWhiteSpace(runtimeFingerprint)
                ? "Line's thin. I'm sticking to the Chummer evidence I can actually prove."
                : "Line's clean. I'm grounding this against your current Chummer runtime.",
            AiRouteTypes.Docs => "Hold up. I'm keeping the docs line evidence-first and tied to your current Chummer context.",
            AiRouteTypes.Recap => "Traffic's noisy, but the notes are intact. Here's the grounded pull.",
            _ => "Jack in. I'm keeping this tied to Chummer evidence, not bad intel."
        };

    private static IReadOnlyList<AiRecommendation> CreateRecommendations(
        string routeType,
        IReadOnlyList<AiRetrievedItem> retrievedItems,
        IReadOnlyList<AiSuggestedAction> suggestedActions)
    {
        List<AiRecommendation> recommendations = retrievedItems
            .Take(3)
            .Select(item => new AiRecommendation(
                RecommendationId: item.ItemId,
                Title: item.Title,
                Reason: $"Retrieved from {GetCorpusTitle(item.CorpusId)} with {item.Provenance ?? "grounded Chummer metadata"}.",
                ExpectedEffect: DescribeRouteEffect(routeType),
                RequiresPreview: true))
            .ToList();

        if (recommendations.Count == 0)
        {
            recommendations.AddRange(suggestedActions
                .Take(2)
                .Select(action => new AiRecommendation(
                    RecommendationId: action.ActionId,
                    Title: action.Title,
                    Reason: "Prepared from the grounded Chummer scaffold.",
                    ExpectedEffect: action.Description,
                    RequiresPreview: action.RequiresConfirmation)));
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add(new AiRecommendation(
                RecommendationId: $"scaffold:{routeType}",
                Title: "Review grounded Chummer evidence first",
                Reason: "This route is still using the deterministic scaffold.",
                ExpectedEffect: "Use runtime evidence and preview actions before making any mutation.",
                RequiresPreview: true));
        }

        return recommendations;
    }

    private static IReadOnlyList<AiEvidenceEntry> CreateEvidence(IReadOnlyList<AiCitation> citations)
        => citations
            .Take(4)
            .Select(static citation => new AiEvidenceEntry(
                Title: citation.Title,
                Summary: $"Grounded from {citation.Source ?? citation.Kind}.",
                ReferenceId: citation.ReferenceId,
                Source: citation.Source))
            .ToArray();

    private static IReadOnlyList<AiRiskEntry> CreateRisks(string? runtimeFingerprint, string providerId)
    {
        List<AiRiskEntry> risks =
        [
            new(
                Severity: AiRiskSeverities.Warning,
                Title: "Provider execution is still scaffolded",
                Summary: $"The {providerId} adapter is still returning deterministic scaffold data, not a live provider result."),
            new(
                Severity: AiRiskSeverities.Note,
                Title: "Mutation remains explicit",
                Summary: "Any apply path must stay on a separate explicit preview or approval flow.")
        ];

        if (string.IsNullOrWhiteSpace(runtimeFingerprint))
        {
            risks.Add(new AiRiskEntry(
                Severity: AiRiskSeverities.Warning,
                Title: "No pinned runtime fingerprint",
                Summary: "Rules advice is limited until a specific runtime fingerprint is attached."));
        }

        return risks;
    }

    private static string DescribeRouteEffect(string routeType)
        => routeType switch
        {
            AiRouteTypes.Coach => "Use this as a grounded coaching lead before previewing spend changes.",
            AiRouteTypes.Build => "Use this as a build-path lead before previewing template or spend changes.",
            AiRouteTypes.Docs => "Use this as a docs concierge lead before trusting or sharing the answer externally.",
            AiRouteTypes.Recap => "Use this as a recap lead before approving any canonical history update.",
            _ => "Use this as a grounded Chummer lead before taking follow-up action."
        };

    private static DeterministicBuildRouteModel BuildDeterministicBuildRouteModel(AiProviderTurnPlan plan)
    {
        IReadOnlyDictionary<string, string> characterFacts = plan.Grounding.CharacterFacts;
        IReadOnlyDictionary<string, string> runtimeFacts = plan.Grounding.RuntimeFacts;
        string characterId = ResolveFact(characterFacts, "characterId") ?? "build-lab-preview";
        string displayName = ResolveFact(characterFacts, "displayName")
            ?? ResolveFact(characterFacts, "name")
            ?? ResolveFact(characterFacts, "alias")
            ?? "Runner";
        string buildMethod = ResolveFact(characterFacts, "buildMethod") ?? "priority";
        string runtimeFingerprint = plan.Grounding.RuntimeFingerprint
            ?? ResolveFact(characterFacts, "runtimeFingerprint")
            ?? ResolveFact(runtimeFacts, "runtimeFingerprint")
            ?? string.Empty;
        string workspaceId = ResolveFact(characterFacts, "workspaceId") ?? plan.Grounding.WorkspaceId ?? string.Empty;
        string[] roleTags = InferBuildRoleTags(plan, characterFacts);
        string[] requiredRoleTags = InferRequiredRoleTags(plan, roleTags);

        IBuildLabService service = new DefaultBuildLabService();
        IReadOnlyList<BuildVariantProjection> variants = service.GenerateBuildVariants(characterId, roleTags);
        IReadOnlyList<KarmaSpendProjection> progressionPaths = service.PlanProgressionPaths(characterId, roleTags, [25, 50, 100], requiredRoleTags);
        BuildTeamCoverageProjection teamCoverage = service.EvaluateTeamCoverage(
            characterId,
            variants.Select(static variant => variant.VariantId).ToArray(),
            requiredRoleTags);
        Dictionary<string, BuildVariantProjection> variantsById = variants
            .GroupBy(static variant => variant.VariantId, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        KarmaSpendProjection[] orderedPaths = progressionPaths
            .OrderByDescending(static path => path.ConstraintCoverageScore ?? 0m)
            .ThenBy(static path => path.MissingConstraintTags?.Count ?? 0)
            .ThenByDescending(path => variantsById.GetValueOrDefault(path.VariantId)?.Rank ?? 0m)
            .ThenBy(static path => path.VariantId, StringComparer.Ordinal)
            .ToArray();
        BuildVariantProjection leadVariant = orderedPaths
            .Select(path => variantsById.GetValueOrDefault(path.VariantId))
            .FirstOrDefault(static variant => variant is not null)
            ?? variants.First();

        return new DeterministicBuildRouteModel(
            CharacterId: characterId,
            DisplayName: displayName,
            BuildMethod: buildMethod,
            RuntimeFingerprint: runtimeFingerprint,
            WorkspaceId: workspaceId,
            Variants: variants,
            VariantsById: variantsById,
            OrderedPaths: orderedPaths,
            TeamCoverage: teamCoverage,
            LeadVariant: leadVariant,
            RoleTags: roleTags,
            RequiredRoleTags: requiredRoleTags);
    }

    private static string[] InferBuildRoleTags(AiProviderTurnPlan plan, IReadOnlyDictionary<string, string> characterFacts)
    {
        string searchableText = string.Join(
                ' ',
                new[]
                {
                    plan.UserMessage,
                    ResolveFact(characterFacts, "displayName"),
                    ResolveFact(characterFacts, "name"),
                    ResolveFact(characterFacts, "alias"),
                    ResolveFact(characterFacts, "metatype"),
                    string.Join(" ", plan.Grounding.RetrievedItems.Select(static item => $"{item.Title} {item.Summary}".Trim()))
                }.Where(static value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        HashSet<string> tags = [];
        if (ContainsAny(searchableText, "face", "social", "con artist", "negotiator", "diplomat"))
        {
            tags.Add("face");
        }

        if (ContainsAny(searchableText, "street samurai", "samurai", "combat", "automatics", "gun", "mercenary", "shooter"))
        {
            tags.Add("street-samurai");
        }

        if (ContainsAny(searchableText, "matrix", "decker", "hacker", "technomancer", "cyberdeck", "resonance"))
        {
            tags.Add("matrix-specialist");
        }

        if (ContainsAny(searchableText, "mage", "magic", "shaman", "astral", "adept", "spell"))
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

        if (tags.Count == 0)
        {
            tags.Add("generalist");
        }

        return OrderRoleTags(tags);
    }

    private static string[] InferRequiredRoleTags(AiProviderTurnPlan plan, IReadOnlyList<string> roleTags)
    {
        string searchableText = string.Join(
                ' ',
                new[]
                {
                    plan.UserMessage,
                    string.Join(" ", plan.Grounding.RetrievedItems.Select(static item => $"{item.Title} {item.Summary}".Trim()))
                }.Where(static value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();
        bool emphasizeTeamFit = roleTags.Count <= 1 || ContainsAny(searchableText, "crew", "team", "campaign", "role", "fit", "missing", "pressure");
        HashSet<string> required = new(roleTags, StringComparer.Ordinal);

        foreach (string tag in roleTags)
        {
            if (!emphasizeTeamFit && !string.Equals(tag, "generalist", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (string complement in ResolveComplementaryRoles(tag))
            {
                required.Add(complement);
            }
        }

        if (required.Count == 0)
        {
            required.Add("generalist");
        }

        return OrderRoleTags(required);
    }

    private static IEnumerable<string> ResolveComplementaryRoles(string tag)
    {
        return tag switch
        {
            "face" => ["matrix-specialist"],
            "street-samurai" => ["face"],
            "matrix-specialist" => ["face"],
            "astral" => ["face"],
            "rigger" => ["matrix-specialist"],
            "infiltrator" => ["face"],
            _ => ["face", "matrix-specialist"]
        };
    }

    private static string ResolveVariantTag(DeterministicBuildRouteModel model, string variantId)
    {
        return model.VariantsById.TryGetValue(variantId, out BuildVariantProjection? variant)
            ? variant.RoleTags.FirstOrDefault() ?? "generalist"
            : "generalist";
    }

    private static decimal ResolveStepMetric(KarmaSpendStep? step, string metricId)
    {
        if (step is null)
        {
            return 0m;
        }

        return step.Scores.FirstOrDefault(score => string.Equals(score.MetricId, metricId, StringComparison.Ordinal))?.Value ?? 0m;
    }

    private static string ResolveFact(IReadOnlyDictionary<string, string> facts, string key)
    {
        return facts.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null!;
    }

    private static string[] OrderRoleTags(IEnumerable<string> roleTags)
    {
        return roleTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static tag => RoleOrder(tag))
            .ThenBy(static tag => tag, StringComparer.Ordinal)
            .ToArray();
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
            _ => 99
        };
    }

    private static string FormatRoleTags(IReadOnlyList<string> roleTags)
        => string.Join(", ", roleTags.Select(FormatRoleTag));

    private static string FormatRoleTagsOrNone(IReadOnlyList<string>? roleTags)
        => roleTags is { Count: > 0 } ? FormatRoleTags(roleTags) : "none";

    private static string FormatRoleTag(string roleTag)
    {
        string normalized = roleTag.Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? roleTag
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(normalized);
    }

    private static string DescribeRuntime(string runtimeFingerprint)
        => string.IsNullOrWhiteSpace(runtimeFingerprint) ? "no pinned runtime fingerprint" : $"runtime {runtimeFingerprint}";

    private static string FormatDecimal(decimal value)
        => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static bool ContainsAny(string text, params string[] needles)
        => needles.Any(needle => text.Contains(needle, StringComparison.Ordinal));

    internal sealed record AiScaffoldTurnArtifacts(
        string Answer,
        string? FlavorLine,
        AiStructuredAnswer StructuredAnswer,
        IReadOnlyList<AiCitation> Citations,
        IReadOnlyList<AiSuggestedAction> SuggestedActions,
        IReadOnlyList<AiToolInvocation> ToolInvocations);

    private sealed record DeterministicBuildRouteModel(
        string CharacterId,
        string DisplayName,
        string BuildMethod,
        string RuntimeFingerprint,
        string WorkspaceId,
        IReadOnlyList<BuildVariantProjection> Variants,
        IReadOnlyDictionary<string, BuildVariantProjection> VariantsById,
        IReadOnlyList<KarmaSpendProjection> OrderedPaths,
        BuildTeamCoverageProjection TeamCoverage,
        BuildVariantProjection LeadVariant,
        IReadOnlyList<string> RoleTags,
        IReadOnlyList<string> RequiredRoleTags);

    private static string GetCorpusTitle(string corpusId)
        => corpusId switch
        {
            AiRetrievalCorpusIds.Runtime => "Authoritative Runtime",
            AiRetrievalCorpusIds.Private => "Private Notes And Campaign Data",
            AiRetrievalCorpusIds.Community => "Community Build Ideas",
            _ => corpusId
        };

    private static bool ContainsTool(IEnumerable<AiToolDescriptor> tools, string toolId)
        => tools.Any(tool => string.Equals(tool.ToolId, toolId, StringComparison.Ordinal));
}
