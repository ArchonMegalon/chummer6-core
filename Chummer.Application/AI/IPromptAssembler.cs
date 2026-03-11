using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public interface IPromptAssembler
{
    string AssembleSystemPrompt(string routeType, AiGroundingBundle grounding, AiProviderRouteDecision routeDecision);

    AiProviderTurnPlan AssembleTurnPlan(AiConversationTurnRequest request, AiGroundingBundle grounding, AiProviderRouteDecision routeDecision, AiBudgetSnapshot budget);
}
