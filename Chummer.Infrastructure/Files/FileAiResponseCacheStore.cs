using System.Text.Json;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileAiResponseCacheStore : IAiResponseCacheStore
{
    private readonly string _stateDirectory;

    public FileAiResponseCacheStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public AiCachedConversationTurn? Get(OwnerScope owner, AiResponseCacheLookup lookup)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        string cacheKey = AiResponseCacheKeys.CreateCacheKey(lookup);
        return Load(owner).FirstOrDefault(entry => string.Equals(entry.CacheKey, cacheKey, StringComparison.Ordinal));
    }

    public void Upsert(OwnerScope owner, AiCachedConversationTurn cachedTurn)
    {
        ArgumentNullException.ThrowIfNull(cachedTurn);

        AiCachedConversationTurn normalized = Normalize(cachedTurn);
        List<AiCachedConversationTurn> entries = Load(owner).ToList();
        int existingIndex = entries.FindIndex(entry => string.Equals(entry.CacheKey, normalized.CacheKey, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            entries[existingIndex] = normalized;
        }
        else
        {
            entries.Add(normalized);
        }

        Save(owner, entries);
    }

    private IReadOnlyList<AiCachedConversationTurn> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<AiCachedConversationTurn>>(File.ReadAllText(path))
            ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<AiCachedConversationTurn> entries)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(entries));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "ai", "response-cache.json");
    }

    private static AiCachedConversationTurn Normalize(AiCachedConversationTurn entry)
    {
        AiConversationTurnResponse normalizedResponse = entry.Response with
        {
            ConversationId = AiResponseCacheKeys.NormalizeRequired(entry.Response.ConversationId),
            RouteType = AiResponseCacheKeys.NormalizeRequired(entry.Response.RouteType).ToLowerInvariant(),
            ProviderId = AiResponseCacheKeys.NormalizeRequired(entry.Response.ProviderId),
            Answer = AiResponseCacheKeys.NormalizeRequired(entry.Response.Answer),
            Cache = entry.Response.Cache is null
                ? null
                : new AiCacheMetadata(
                    Status: AiResponseCacheKeys.NormalizeRequired(entry.Response.Cache.Status).ToLowerInvariant(),
                    CacheKey: AiResponseCacheKeys.NormalizeRequired(entry.Response.Cache.CacheKey),
                    CachedAtUtc: entry.Response.Cache.CachedAtUtc,
                    NormalizedPrompt: AiResponseCacheKeys.NormalizeOptional(entry.Response.Cache.NormalizedPrompt),
                    RuntimeFingerprint: AiResponseCacheKeys.NormalizeOptional(entry.Response.Cache.RuntimeFingerprint),
                    CharacterId: AiResponseCacheKeys.NormalizeOptional(entry.Response.Cache.CharacterId),
                    WorkspaceId: AiResponseCacheKeys.NormalizeOptional(entry.Response.Cache.WorkspaceId))
        };

        return new AiCachedConversationTurn(
            CacheKey: AiResponseCacheKeys.NormalizeRequired(entry.CacheKey),
            RouteType: AiResponseCacheKeys.NormalizeRequired(entry.RouteType).ToLowerInvariant(),
            NormalizedPrompt: AiResponseCacheKeys.NormalizeRequired(entry.NormalizedPrompt),
            RuntimeFingerprint: AiResponseCacheKeys.NormalizeOptional(entry.RuntimeFingerprint),
            CharacterId: AiResponseCacheKeys.NormalizeOptional(entry.CharacterId),
            AttachmentKey: AiResponseCacheKeys.NormalizeOptional(entry.AttachmentKey),
            CachedAtUtc: entry.CachedAtUtc,
            Response: normalizedResponse,
            WorkspaceId: AiResponseCacheKeys.NormalizeOptional(entry.WorkspaceId));
    }
}
