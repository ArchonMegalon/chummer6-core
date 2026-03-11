using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public static class AiProviderTransportStates
{
    public const string NotImplemented = "not-implemented";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public sealed record AiProviderTransportRequest(
    string ProviderId,
    string RouteType,
    string? ConversationId,
    string BaseUrl,
    string? ModelId,
    string UserMessage,
    string SystemPrompt,
    bool Stream,
    IReadOnlyList<string> AttachmentIds,
    IReadOnlyList<string> RetrievalCorpusIds,
    IReadOnlyList<AiToolDescriptor> AllowedTools,
    string CredentialTier,
    int? CredentialSlotIndex,
    string? RuntimeFingerprint,
    string? CharacterId = null,
    string? WorkspaceId = null);

public sealed record AiProviderTransportResponse(
    string ProviderId,
    string RouteType,
    string? ConversationId,
    string TransportState,
    string Answer,
    IReadOnlyList<AiCitation> Citations,
    IReadOnlyList<AiSuggestedAction> SuggestedActions,
    IReadOnlyList<AiToolInvocation> ToolInvocations,
    string? FlavorLine = null,
    AiStructuredAnswer? StructuredAnswer = null);
