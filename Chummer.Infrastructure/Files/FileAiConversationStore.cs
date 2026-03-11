using System.Text.Json;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.Files;

public sealed class FileAiConversationStore : IConversationStore
{
    private readonly string _stateDirectory;

    public FileAiConversationStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public AiConversationCatalogPage List(OwnerScope owner, AiConversationCatalogQuery query)
    {
        AiConversationCatalogQuery normalizedQuery = NormalizeQuery(query);
        AiConversationSnapshot[] matched = Load(owner)
            .Where(snapshot => Matches(snapshot, normalizedQuery))
            .OrderByDescending(GetLastUpdatedAtUtc)
            .ThenBy(static snapshot => snapshot.ConversationId, StringComparer.Ordinal)
            .ToArray();

        return new AiConversationCatalogPage(
            Items: matched.Take(normalizedQuery.MaxCount).ToArray(),
            TotalCount: matched.Length);
    }

    public AiConversationSnapshot? Get(OwnerScope owner, string conversationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationId);
        string normalizedConversationId = conversationId.Trim();

        return Load(owner).FirstOrDefault(snapshot =>
            string.Equals(snapshot.ConversationId, normalizedConversationId, StringComparison.Ordinal));
    }

    public void Upsert(OwnerScope owner, AiConversationSnapshot conversation)
    {
        ArgumentNullException.ThrowIfNull(conversation);

        AiConversationSnapshot normalizedConversation = conversation with
        {
            ConversationId = conversation.ConversationId.Trim(),
            RouteType = conversation.RouteType.Trim().ToLowerInvariant(),
            RuntimeFingerprint = NormalizeOptional(conversation.RuntimeFingerprint),
            CharacterId = NormalizeOptional(conversation.CharacterId),
            WorkspaceId = NormalizeOptional(conversation.WorkspaceId),
            Messages = conversation.Messages
                .Select(static message => new AiConversationMessage(
                    MessageId: message.MessageId.Trim(),
                    Role: message.Role.Trim().ToLowerInvariant(),
                    Content: message.Content.Trim(),
                    CreatedAtUtc: message.CreatedAtUtc,
                    ProviderId: string.IsNullOrWhiteSpace(message.ProviderId) ? null : message.ProviderId.Trim()))
                .ToArray(),
            Turns = (conversation.Turns ?? [])
                .Select(turn => new AiConversationTurnRecord(
                    TurnId: turn.TurnId.Trim(),
                    RouteType: turn.RouteType.Trim().ToLowerInvariant(),
                    ProviderId: turn.ProviderId.Trim(),
                    CreatedAtUtc: turn.CreatedAtUtc,
                    UserMessage: turn.UserMessage.Trim(),
                    AssistantAnswer: turn.AssistantAnswer.Trim(),
                    ToolInvocations: turn.ToolInvocations
                        .Select(static invocation => new AiToolInvocation(
                            ToolId: invocation.ToolId.Trim(),
                            Status: invocation.Status.Trim().ToLowerInvariant(),
                            Summary: invocation.Summary.Trim(),
                            ReferenceId: string.IsNullOrWhiteSpace(invocation.ReferenceId) ? null : invocation.ReferenceId.Trim()))
                        .ToArray(),
                    Citations: turn.Citations
                        .Select(static citation => new AiCitation(
                            Kind: citation.Kind.Trim().ToLowerInvariant(),
                            Title: citation.Title.Trim(),
                            ReferenceId: citation.ReferenceId.Trim(),
                            Source: string.IsNullOrWhiteSpace(citation.Source) ? null : citation.Source.Trim()))
                        .ToArray(),
                    StructuredAnswer: turn.StructuredAnswer,
                    RuntimeFingerprint: NormalizeOptional(turn.RuntimeFingerprint),
                    CharacterId: NormalizeOptional(turn.CharacterId),
                    WorkspaceId: NormalizeOptional(turn.WorkspaceId),
                    SuggestedActions: turn.SuggestedActions?.Select(static action => new AiSuggestedAction(
                        ActionId: action.ActionId.Trim(),
                        Title: action.Title.Trim(),
                        Description: action.Description.Trim(),
                        RequiresConfirmation: action.RequiresConfirmation,
                        RuntimeFingerprint: string.IsNullOrWhiteSpace(action.RuntimeFingerprint) ? null : action.RuntimeFingerprint.Trim(),
                        CharacterId: string.IsNullOrWhiteSpace(action.CharacterId) ? null : action.CharacterId.Trim(),
                        WorkspaceId: string.IsNullOrWhiteSpace(action.WorkspaceId) ? null : action.WorkspaceId.Trim()))
                        .ToArray(),
                    FlavorLine: NormalizeOptional(turn.FlavorLine),
                    Budget: turn.Budget is null
                        ? null
                        : new AiBudgetSnapshot(
                            BudgetUnit: turn.Budget.BudgetUnit.Trim(),
                            MonthlyAllowance: turn.Budget.MonthlyAllowance,
                            MonthlyConsumed: turn.Budget.MonthlyConsumed,
                            BurstLimitPerMinute: turn.Budget.BurstLimitPerMinute,
                            CurrentBurstConsumed: turn.Budget.CurrentBurstConsumed,
                            IsLimited: turn.Budget.IsLimited),
                    Cache: turn.Cache is null
                        ? null
                        : new AiCacheMetadata(
                            Status: turn.Cache.Status.Trim().ToLowerInvariant(),
                            CacheKey: turn.Cache.CacheKey.Trim(),
                            CachedAtUtc: turn.Cache.CachedAtUtc,
                            NormalizedPrompt: NormalizeOptional(turn.Cache.NormalizedPrompt),
                            RuntimeFingerprint: NormalizeOptional(turn.Cache.RuntimeFingerprint),
                            CharacterId: NormalizeOptional(turn.Cache.CharacterId),
                            WorkspaceId: NormalizeOptional(turn.Cache.WorkspaceId)),
                    RouteDecision: turn.RouteDecision is null
                        ? null
                        : new AiProviderRouteDecision(
                            RouteType: turn.RouteDecision.RouteType.Trim().ToLowerInvariant(),
                            ProviderId: turn.RouteDecision.ProviderId.Trim(),
                            Reason: turn.RouteDecision.Reason.Trim(),
                            BudgetUnit: turn.RouteDecision.BudgetUnit.Trim().ToLowerInvariant(),
                            ToolingEnabled: turn.RouteDecision.ToolingEnabled,
                            RetrievalEnabled: turn.RouteDecision.RetrievalEnabled,
                            CredentialTier: turn.RouteDecision.CredentialTier.Trim().ToLowerInvariant(),
                            CredentialSlotIndex: turn.RouteDecision.CredentialSlotIndex),
                    GroundingCoverage: turn.GroundingCoverage is null
                        ? null
                        : new AiGroundingCoverage(
                            ScorePercent: turn.GroundingCoverage.ScorePercent,
                            Summary: turn.GroundingCoverage.Summary.Trim(),
                            PresentSignals: turn.GroundingCoverage.PresentSignals
                                .Select(static signal => signal.Trim().ToLowerInvariant())
                                .ToArray(),
                            MissingSignals: turn.GroundingCoverage.MissingSignals
                                .Select(static signal => signal.Trim().ToLowerInvariant())
                                .ToArray(),
                            RetrievedCorpusIds: turn.GroundingCoverage.RetrievedCorpusIds
                                .Select(static corpusId => corpusId.Trim().ToLowerInvariant())
                                .ToArray())))
                .ToArray()
        };

        List<AiConversationSnapshot> conversations = Load(owner).ToList();
        int existingIndex = conversations.FindIndex(snapshot =>
            string.Equals(snapshot.ConversationId, normalizedConversation.ConversationId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            conversations[existingIndex] = normalizedConversation;
        }
        else
        {
            conversations.Add(normalizedConversation);
        }

        Save(owner, conversations);
    }

    private IReadOnlyList<AiConversationSnapshot> Load(OwnerScope owner)
    {
        string path = GetPath(owner);
        if (!File.Exists(path))
        {
            return [];
        }

        List<AiConversationSnapshot>? conversations = JsonSerializer.Deserialize<List<AiConversationSnapshot>>(File.ReadAllText(path));
        return conversations?
            .Select(snapshot => snapshot with
            {
                Turns = snapshot.Turns ?? []
            })
            .ToArray()
            ?? [];
    }

    private void Save(OwnerScope owner, IReadOnlyList<AiConversationSnapshot> conversations)
    {
        string path = GetPath(owner);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(conversations));
    }

    private string GetPath(OwnerScope owner)
    {
        string ownerDirectory = OwnerScopedStatePath.ResolveOwnerDirectory(_stateDirectory, owner);
        Directory.CreateDirectory(ownerDirectory);
        return Path.Combine(ownerDirectory, "ai", "conversations.json");
    }

    private static bool Matches(AiConversationSnapshot snapshot, AiConversationCatalogQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.ConversationId)
            && !string.Equals(snapshot.ConversationId, query.ConversationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.RouteType)
            && !string.Equals(snapshot.RouteType, query.RouteType, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.CharacterId)
            && !string.Equals(snapshot.CharacterId, query.CharacterId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.RuntimeFingerprint)
            && !string.Equals(snapshot.RuntimeFingerprint, query.RuntimeFingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        string? workspaceId = snapshot.WorkspaceId ?? snapshot.Turns?.LastOrDefault()?.WorkspaceId;
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

    private static DateTimeOffset GetLastUpdatedAtUtc(AiConversationSnapshot snapshot)
        => snapshot.Messages.Count == 0
            ? DateTimeOffset.MinValue
            : snapshot.Messages.Max(message => message.CreatedAtUtc);

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
}
