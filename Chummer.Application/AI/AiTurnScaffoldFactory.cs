using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chummer.Contracts.AI;

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

    internal sealed record AiScaffoldTurnArtifacts(
        string Answer,
        string? FlavorLine,
        AiStructuredAnswer StructuredAnswer,
        IReadOnlyList<AiCitation> Citations,
        IReadOnlyList<AiSuggestedAction> SuggestedActions,
        IReadOnlyList<AiToolInvocation> ToolInvocations);

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
