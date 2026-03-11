using System.Collections.Concurrent;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class InMemoryAiResponseCacheStore : IAiResponseCacheStore
{
    private readonly ConcurrentDictionary<string, AiCachedConversationTurn> _entries = new(StringComparer.Ordinal);

    public AiCachedConversationTurn? Get(OwnerScope owner, AiResponseCacheLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        _entries.TryGetValue(CreateOwnerKey(owner, AiResponseCacheKeys.CreateCacheKey(lookup)), out AiCachedConversationTurn? entry);
        return entry;
    }

    public void Upsert(OwnerScope owner, AiCachedConversationTurn cachedTurn)
    {
        ArgumentNullException.ThrowIfNull(cachedTurn);
        _entries[CreateOwnerKey(owner, cachedTurn.CacheKey)] = cachedTurn;
    }

    private static string CreateOwnerKey(OwnerScope owner, string cacheKey)
        => $"{owner.NormalizedValue}:{cacheKey}";
}
