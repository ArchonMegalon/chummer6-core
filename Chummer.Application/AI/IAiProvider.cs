using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiProvider
{
    string ProviderId { get; }

    AiProviderExecutionPolicy ExecutionPolicy { get; }

    string AdapterKind { get; }

    bool LiveExecutionEnabled { get; }

    AiConversationTurnResponse CompleteTurn(OwnerScope owner, AiProviderTurnPlan plan);
}
