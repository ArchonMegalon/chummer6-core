using System;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class NotImplementedAiProviderTransportClient : IAiProviderTransportClient
{
    public AiProviderTransportResponse Execute(OwnerScope owner, AiProviderTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        AiTurnScaffoldFactory.AiScaffoldTurnArtifacts artifacts = AiTurnScaffoldFactory.CreateTransportArtifacts(request.ProviderId, request);

        return new AiProviderTransportResponse(
            ProviderId: request.ProviderId,
            RouteType: request.RouteType,
            ConversationId: request.ConversationId,
            TransportState: AiProviderTransportStates.NotImplemented,
            Answer: artifacts.Answer,
            Citations: artifacts.Citations,
            SuggestedActions: artifacts.SuggestedActions,
            ToolInvocations: artifacts.ToolInvocations,
            FlavorLine: artifacts.FlavorLine,
            StructuredAnswer: artifacts.StructuredAnswer);
    }
}
