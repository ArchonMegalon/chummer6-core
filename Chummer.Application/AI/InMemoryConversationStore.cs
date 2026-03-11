using System;
using System.Collections.Concurrent;
using System.Linq;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, AiConversationSnapshot> _conversations = new(StringComparer.Ordinal);

    public AiConversationCatalogPage List(OwnerScope owner, AiConversationCatalogQuery query)
    {
        AiConversationCatalogQuery normalizedQuery = NormalizeQuery(query);
        AiConversationSnapshot[] matched = _conversations
            .Where(entry => entry.Key.StartsWith($"{owner.NormalizedValue}:", StringComparison.Ordinal))
            .Select(static entry => entry.Value)
            .Where(conversation => Matches(conversation, normalizedQuery))
            .OrderByDescending(static conversation => GetLastUpdatedAtUtc(conversation))
            .ThenBy(static conversation => conversation.ConversationId, StringComparer.Ordinal)
            .ToArray();

        return new AiConversationCatalogPage(
            Items: matched.Take(normalizedQuery.MaxCount).ToArray(),
            TotalCount: matched.Length);
    }

    public AiConversationSnapshot? Get(OwnerScope owner, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(conversationId);
        _conversations.TryGetValue(CreateKey(owner, conversationId), out AiConversationSnapshot? conversation);
        return conversation;
    }

    public void Upsert(OwnerScope owner, AiConversationSnapshot conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        _conversations[CreateKey(owner, conversation.ConversationId)] = conversation;
    }

    private static bool Matches(AiConversationSnapshot conversation, AiConversationCatalogQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.ConversationId)
            && !string.Equals(conversation.ConversationId, query.ConversationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.RouteType)
            && !string.Equals(conversation.RouteType, query.RouteType, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.CharacterId)
            && !string.Equals(conversation.CharacterId, query.CharacterId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.RuntimeFingerprint)
            && !string.Equals(conversation.RuntimeFingerprint, query.RuntimeFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        string? workspaceId = conversation.WorkspaceId ?? conversation.Turns?.LastOrDefault()?.WorkspaceId;
        if (!string.IsNullOrWhiteSpace(query.WorkspaceId)
            && !string.Equals(workspaceId, query.WorkspaceId, StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static AiConversationCatalogQuery NormalizeQuery(AiConversationCatalogQuery query)
        => new(
            ConversationId: NormalizeOptional(query.ConversationId),
            RouteType: NormalizeOptional(query.RouteType)?.ToLowerInvariant(),
            CharacterId: NormalizeOptional(query.CharacterId),
            RuntimeFingerprint: NormalizeOptional(query.RuntimeFingerprint),
            MaxCount: Math.Max(1, query.MaxCount),
            WorkspaceId: NormalizeOptional(query.WorkspaceId));

    private static DateTimeOffset GetLastUpdatedAtUtc(AiConversationSnapshot conversation)
        => conversation.Messages.Count == 0
            ? DateTimeOffset.MinValue
            : conversation.Messages.Max(message => message.CreatedAtUtc);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();

    private static string CreateKey(OwnerScope owner, string conversationId)
        => $"{owner.NormalizedValue}:{conversationId}";
}
