using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiGatewayService
{
    AiApiResult<AiGatewayStatusProjection> GetStatus(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiProviderDescriptor>> ListProviders(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiProviderHealthProjection>> ListProviderHealth(OwnerScope owner);

    AiApiResult<AiConversationCatalogPage> ListConversations(OwnerScope owner, AiConversationCatalogQuery? query = null);

    AiApiResult<AiConversationAuditCatalogPage> ListConversationAudits(OwnerScope owner, AiConversationCatalogQuery? query = null);

    AiApiResult<IReadOnlyList<AiToolDescriptor>> ListTools(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiRetrievalCorpusDescriptor>> ListRetrievalCorpora(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiRoutePolicyDescriptor>> ListRoutePolicies(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiRouteBudgetPolicyDescriptor>> ListRouteBudgets(OwnerScope owner);

    AiApiResult<IReadOnlyList<AiRouteBudgetStatusProjection>> ListRouteBudgetStatuses(OwnerScope owner);

    AiApiResult<AiConversationTurnPreview> PreviewTurn(OwnerScope owner, string routeType, AiConversationTurnRequest? request);

    AiApiResult<AiConversationSnapshot> GetConversation(OwnerScope owner, string conversationId);

    AiApiResult<AiConversationTurnResponse> SendChatTurn(OwnerScope owner, AiConversationTurnRequest? request);

    AiApiResult<AiConversationTurnResponse> SendCoachTurn(OwnerScope owner, AiConversationTurnRequest? request);

    AiApiResult<AiConversationTurnResponse> SendBuildTurn(OwnerScope owner, AiConversationTurnRequest? request);

    AiApiResult<AiConversationTurnResponse> SendDocsTurn(OwnerScope owner, AiConversationTurnRequest? request);

    AiApiResult<AiConversationTurnResponse> SendRecapTurn(OwnerScope owner, AiConversationTurnRequest? request);
}
