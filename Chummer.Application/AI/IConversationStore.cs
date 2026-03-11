using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IConversationStore
{
    AiConversationCatalogPage List(OwnerScope owner, AiConversationCatalogQuery query);

    AiConversationSnapshot? Get(OwnerScope owner, string conversationId);

    void Upsert(OwnerScope owner, AiConversationSnapshot conversation);
}
