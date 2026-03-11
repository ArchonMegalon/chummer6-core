using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class DefaultAiPromptRegistryService : IAiPromptRegistryService
{
    public AiPromptCatalog ListPrompts(OwnerScope owner, AiPromptCatalogQuery? query)
    {
        AiPromptCatalogQuery effectiveQuery = query ?? new AiPromptCatalogQuery();
        IEnumerable<AiPromptDescriptor> items = BuildPromptCatalog();

        if (!string.IsNullOrWhiteSpace(effectiveQuery.RouteType))
        {
            items = items.Where(item => string.Equals(item.RouteType, effectiveQuery.RouteType, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(effectiveQuery.PersonaId))
        {
            items = items.Where(item => string.Equals(item.PersonaId, effectiveQuery.PersonaId, StringComparison.Ordinal));
        }

        AiPromptDescriptor[] materialized = items
            .Take(Math.Max(1, effectiveQuery.MaxCount))
            .ToArray();

        return new AiPromptCatalog(
            Items: materialized,
            TotalCount: materialized.Length);
    }

    public AiPromptDescriptor? GetPrompt(OwnerScope owner, string promptId)
        => BuildPromptCatalog()
            .FirstOrDefault(item => string.Equals(item.PromptId, promptId, StringComparison.Ordinal));

    private static IReadOnlyList<AiPromptDescriptor> BuildPromptCatalog()
        => AiGatewayDefaults.CreateRoutePolicies()
            .Select(CreatePromptDescriptor)
            .ToArray();

    private static AiPromptDescriptor CreatePromptDescriptor(AiRoutePolicyDescriptor policy)
    {
        AiPersonaDescriptor persona = AiGatewayDefaults.ResolvePersona(policy.PersonaId);
        string[] baseInstructions =
        [
            "Structured Chummer data first, prose documents second.",
            $"{persona.DisplayName} is a light flavor layer, not a replacement for factual answers.",
            $"Keep flavor between {persona.MinFlavorPercent}% and {persona.MaxFlavorPercent}% of the final answer.",
            "Never claim a tool, write, or mutation succeeded unless it actually did.",
            policy.ToolingEnabled
                ? "Use the allowed Chummer tools before speculating when the runtime can calculate the answer."
                : "Do not convert this route into an implicit mutation path."
        ];

        List<string> requiredGroundingSections =
        [
            AiGroundingSectionIds.Runtime,
            AiGroundingSectionIds.Character,
            AiGroundingSectionIds.Constraints
        ];

        if (policy.RetrievalCorpusIds.Count > 0)
        {
            requiredGroundingSections.Add(AiGroundingSectionIds.RetrievedItems);
        }

        if (policy.AllowedTools.Count > 0)
        {
            requiredGroundingSections.Add(AiGroundingSectionIds.AllowedTools);
        }

        return new AiPromptDescriptor(
            PromptId: policy.RouteType,
            PromptKind: AiPromptKinds.RouteSystem,
            RouteType: policy.RouteType,
            RouteClassId: policy.RouteClassId,
            PersonaId: persona.PersonaId,
            Title: GetTitle(policy.RouteType),
            Summary: GetSummary(policy.RouteType),
            BaseInstructions: baseInstructions,
            RequiredGroundingSectionIds: requiredGroundingSections,
            RetrievalCorpusIds: policy.RetrievalCorpusIds.ToArray(),
            AllowedToolIds: policy.AllowedTools.Select(tool => tool.ToolId).ToArray(),
            EvidenceFirst: persona.EvidenceFirst,
            MinFlavorPercent: persona.MinFlavorPercent,
            MaxFlavorPercent: persona.MaxFlavorPercent);
    }

    private static string GetTitle(string routeType)
        => routeType switch
        {
            AiRouteTypes.Chat => "Chat Route System Prompt",
            AiRouteTypes.Coach => "Coach Route System Prompt",
            AiRouteTypes.Build => "Build Lab Route System Prompt",
            AiRouteTypes.Docs => "Docs Route System Prompt",
            AiRouteTypes.Recap => "Session Recap Route System Prompt",
            _ => $"{routeType} Route System Prompt"
        };

    private static string GetSummary(string routeType)
        => routeType switch
        {
            AiRouteTypes.Chat => "Cheap general chat path with Chummer-grounded runtime and private context.",
            AiRouteTypes.Coach => "Evidence-first coaching path with runtime, private, and community retrieval plus tool access.",
            AiRouteTypes.Build => "Build-planning path with runtime and community idea retrieval plus simulation tools.",
            AiRouteTypes.Docs => "Documentation concierge path with runtime-aware explanation support.",
            AiRouteTypes.Recap => "Session recap path for summary and chronology drafting without direct mutation.",
            _ => "Route-linked system prompt descriptor."
        };
}
