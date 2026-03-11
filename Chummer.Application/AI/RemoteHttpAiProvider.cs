using System;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public sealed class RemoteHttpAiProvider(AiProviderTransportOptions transportOptions, IAiProviderTransportClient? transportClient = null) : IAiProvider
{
    private readonly IAiProviderTransportClient _transportClient = transportClient ?? new NotImplementedAiProviderTransportClient();
    private readonly NotImplementedAiProviderTransportClient _scaffoldTransportClient = new();

    public string ProviderId { get; } = transportOptions.ProviderId;

    public AiProviderExecutionPolicy ExecutionPolicy { get; } = AiProviderExecutionPolicies.Resolve(transportOptions.ProviderId);

    public string BaseUrl { get; } = transportOptions.BaseUrl ?? string.Empty;

    public string DefaultModelId { get; } = transportOptions.DefaultModelId ?? string.Empty;

    public string AdapterKind => AiProviderAdapterKinds.RemoteHttp;

    public bool LiveExecutionEnabled { get; } = transportOptions.RemoteExecutionEnabled;

    public AiConversationTurnResponse CompleteTurn(OwnerScope owner, AiProviderTurnPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        AiProviderTransportRequest request = new(
            ProviderId: ProviderId,
            RouteType: plan.RouteType,
            ConversationId: plan.ConversationId,
            BaseUrl: BaseUrl,
            ModelId: string.IsNullOrWhiteSpace(DefaultModelId) ? null : DefaultModelId,
            UserMessage: plan.UserMessage,
            SystemPrompt: plan.SystemPrompt,
            Stream: plan.Stream,
            AttachmentIds: plan.AttachmentIds,
            RetrievalCorpusIds: plan.RetrievalCorpusIds,
            AllowedTools: plan.AllowedTools,
            CredentialTier: plan.RouteDecision.CredentialTier,
            CredentialSlotIndex: plan.RouteDecision.CredentialSlotIndex,
            RuntimeFingerprint: plan.Grounding.RuntimeFingerprint,
            CharacterId: plan.Grounding.CharacterId,
            WorkspaceId: plan.Grounding.WorkspaceId);
        AiProviderTransportResponse response = LiveExecutionEnabled
            ? _transportClient.Execute(owner, request)
            : _scaffoldTransportClient.Execute(owner, request);
        AiTurnScaffoldFactory.AiScaffoldTurnArtifacts scaffoldArtifacts = AiTurnScaffoldFactory.CreateTransportArtifacts(ProviderId, request);

        return new AiConversationTurnResponse(
            ConversationId: string.IsNullOrWhiteSpace(plan.ConversationId) ? $"{plan.RouteType}-remote-http" : plan.ConversationId,
            RouteType: plan.RouteType,
            ProviderId: ProviderId,
            Answer: response.Answer,
            RouteDecision: plan.RouteDecision,
            Grounding: plan.Grounding,
            Budget: plan.Budget,
            Citations: response.Citations,
            SuggestedActions: response.SuggestedActions,
            ToolInvocations: response.ToolInvocations,
            FlavorLine: response.FlavorLine ?? scaffoldArtifacts.FlavorLine,
            StructuredAnswer: response.StructuredAnswer ?? scaffoldArtifacts.StructuredAnswer);
    }
}
