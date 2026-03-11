using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public sealed class DefaultPromptAssembler : IPromptAssembler
{
    public string AssembleSystemPrompt(string routeType, AiGroundingBundle grounding, AiProviderRouteDecision routeDecision)
    {
        ArgumentNullException.ThrowIfNull(routeType);
        ArgumentNullException.ThrowIfNull(grounding);
        ArgumentNullException.ThrowIfNull(routeDecision);

        AiRoutePolicyDescriptor routePolicy = AiGatewayDefaults.ResolveRoutePolicy(routeType);
        AiPersonaDescriptor persona = AiGatewayDefaults.ResolvePersona(routePolicy.PersonaId);
        IReadOnlyList<AiGroundingSection> sections = CreateGroundingSections(grounding);
        var builder = new StringBuilder();
        builder.AppendLine("Structured Chummer data first, prose documents second.");
        builder.AppendLine($"route: {routeType}");
        builder.AppendLine($"route_class: {routePolicy.RouteClassId}");
        builder.AppendLine($"provider: {routeDecision.ProviderId}");
        builder.AppendLine($"persona: {persona.PersonaId}");
        builder.AppendLine($"tooling_enabled: {routeDecision.ToolingEnabled}");
        builder.AppendLine("persona_rules:");
        builder.AppendLine($"- {persona.Summary}");
        builder.AppendLine($"- Keep flavor between {persona.MinFlavorPercent}% and {persona.MaxFlavorPercent}% of the final answer.");
        builder.AppendLine("- Never claim a tool, write, or mutation succeeded unless it actually did.");
        foreach (AiGroundingSection section in sections)
        {
            builder.AppendLine($"{section.SectionId}:");
            foreach (string line in section.Lines)
            {
                builder.AppendLine($"- {line}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public AiProviderTurnPlan AssembleTurnPlan(AiConversationTurnRequest request, AiGroundingBundle grounding, AiProviderRouteDecision routeDecision, AiBudgetSnapshot budget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(grounding);
        ArgumentNullException.ThrowIfNull(routeDecision);
        ArgumentNullException.ThrowIfNull(budget);

        string systemPrompt = AssembleSystemPrompt(routeDecision.RouteType, grounding, routeDecision);
        IReadOnlyList<AiGroundingSection> sections = CreateGroundingSections(grounding);

        return new AiProviderTurnPlan(
            ProviderId: routeDecision.ProviderId,
            RouteType: routeDecision.RouteType,
            ConversationId: request.ConversationId,
            UserMessage: request.Message,
            SystemPrompt: systemPrompt,
            Stream: request.Stream,
            AttachmentIds: request.AttachmentIds ?? Array.Empty<string>(),
            RetrievalCorpusIds: grounding.RetrievedItems.Select(item => item.CorpusId).Distinct(StringComparer.Ordinal).ToArray(),
            AllowedTools: grounding.AllowedTools,
            GroundingSections: sections,
            RouteDecision: routeDecision,
            Grounding: grounding,
            Budget: budget,
            WorkspaceId: grounding.WorkspaceId ?? request.WorkspaceId);
    }

    private static IReadOnlyList<AiGroundingSection> CreateGroundingSections(AiGroundingBundle grounding)
    {
        List<AiGroundingSection> sections =
        [
            new(
                SectionId: AiGroundingSectionIds.Runtime,
                Title: "Runtime",
                Lines: grounding.RuntimeFacts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}: {pair.Value}")
                    .ToArray()),
            new(
                SectionId: AiGroundingSectionIds.Character,
                Title: "Character",
                Lines: grounding.CharacterFacts
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(static pair => $"{pair.Key}: {pair.Value}")
                    .ToArray()),
            new(
                SectionId: AiGroundingSectionIds.Constraints,
                Title: "Constraints",
                Lines: grounding.Constraints.ToArray(),
                Structured: false),
            new(
                SectionId: AiGroundingSectionIds.RetrievedItems,
                Title: "Retrieved Items",
                Lines: grounding.RetrievedItems
                    .Select(static item => $"{item.CorpusId}: {item.Title} - {item.Summary}")
                    .ToArray(),
                Structured: false),
            new(
                SectionId: AiGroundingSectionIds.AllowedTools,
                Title: "Allowed Tools",
                Lines: grounding.AllowedTools
                    .Select(static tool => $"{tool.ToolId}: {tool.Title}")
                    .ToArray(),
                Structured: false)
        ];

        return sections;
    }
}
