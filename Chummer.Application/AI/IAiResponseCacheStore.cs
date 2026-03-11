using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiResponseCacheStore
{
    AiCachedConversationTurn? Get(OwnerScope owner, AiResponseCacheLookup lookup);

    void Upsert(OwnerScope owner, AiCachedConversationTurn cachedTurn);
}
