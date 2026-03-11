using System;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiProvider(string providerId) : IAiProvider
{
    public string ProviderId { get; } = providerId;

    public AiProviderExecutionPolicy ExecutionPolicy { get; } = AiProviderExecutionPolicies.Resolve(providerId);

    public string AdapterKind => AiProviderAdapterKinds.Stub;

    public bool LiveExecutionEnabled => false;

    public AiConversationTurnResponse CompleteTurn(OwnerScope owner, AiProviderTurnPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        AiTurnScaffoldFactory.AiScaffoldTurnArtifacts artifacts = AiTurnScaffoldFactory.CreateProviderArtifacts(ProviderId, plan);

        return new AiConversationTurnResponse(
            ConversationId: string.IsNullOrWhiteSpace(plan.ConversationId) ? $"{plan.RouteType}-stub" : plan.ConversationId,
            RouteType: plan.RouteType,
            ProviderId: ProviderId,
            Answer: artifacts.Answer,
            RouteDecision: plan.RouteDecision,
            Grounding: plan.Grounding,
            Budget: plan.Budget,
            Citations: artifacts.Citations,
            SuggestedActions: artifacts.SuggestedActions,
            ToolInvocations: artifacts.ToolInvocations,
            FlavorLine: artifacts.FlavorLine,
            StructuredAnswer: artifacts.StructuredAnswer);
    }
}
