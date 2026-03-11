#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class AiProviderTransportClientTests
{
    [TestMethod]
    public void Remote_http_provider_builds_typed_transport_request_and_maps_transport_response_when_live_execution_is_enabled()
    {
        RecordingTransportClient transportClient = new();
        RemoteHttpAiProvider provider = new(
            new AiProviderTransportOptions(
                ProviderId: AiProviderIds.AiMagicx,
                BaseUrl: "https://beta.aimagicx.com/api/v1",
                DefaultModelId: "magicx-coach",
                TransportConfigured: true,
                RemoteExecutionEnabled: true),
            transportClient);

        AiConversationTurnResponse response = provider.CompleteTurn(OwnerScope.LocalSingleUser, CreateTurnPlan());

        Assert.IsNotNull(transportClient.LastRequest);
        Assert.AreEqual("https://beta.aimagicx.com/api/v1", transportClient.LastRequest.BaseUrl);
        Assert.AreEqual("magicx-coach", transportClient.LastRequest.ModelId);
        Assert.AreEqual(AiProviderIds.AiMagicx, transportClient.LastRequest.ProviderId);
        Assert.AreEqual(AiRouteTypes.Coach, transportClient.LastRequest.RouteType);
        Assert.AreEqual(AiProviderCredentialTiers.Primary, transportClient.LastRequest.CredentialTier);
        Assert.AreEqual(0, transportClient.LastRequest.CredentialSlotIndex);
        Assert.IsTrue(transportClient.LastRequest.AllowedTools.Any(tool => tool.ToolId == AiToolIds.ExplainDerivedValue));
        Assert.AreEqual("transport response", response.Answer);
        Assert.AreEqual("Clean relay. The transport scaffold is keeping this grounded while the live hop stays disabled.", response.FlavorLine);
        Assert.IsNotNull(response.StructuredAnswer);
        Assert.AreEqual(AiConfidenceLevels.Grounded, response.StructuredAnswer!.Confidence);
        Assert.HasCount(1, response.Citations);
        Assert.HasCount(1, response.ToolInvocations);
    }

    [TestMethod]
    public void Remote_http_provider_uses_scaffold_transport_when_live_execution_is_disabled()
    {
        RecordingTransportClient transportClient = new();
        RemoteHttpAiProvider provider = new(
            new AiProviderTransportOptions(
                ProviderId: AiProviderIds.AiMagicx,
                BaseUrl: "https://beta.aimagicx.com/api/v1",
                DefaultModelId: "magicx-coach",
                TransportConfigured: true,
                RemoteExecutionEnabled: false),
            transportClient);

        AiConversationTurnResponse response = provider.CompleteTurn(OwnerScope.LocalSingleUser, CreateTurnPlan());

        Assert.IsNull(transportClient.LastRequest);
        StringAssert.Contains(response.Answer, "scaffold stayed server-side");
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.FlavorLine));
        Assert.IsNotNull(response.StructuredAnswer);
        Assert.AreEqual(AiConfidenceLevels.Scaffolded, response.StructuredAnswer!.Confidence);
    }

    [TestMethod]
    public void Not_implemented_transport_client_returns_explicit_transport_state()
    {
        NotImplementedAiProviderTransportClient client = new();

        AiProviderTransportResponse response = client.Execute(
            OwnerScope.LocalSingleUser,
            new AiProviderTransportRequest(
                ProviderId: AiProviderIds.OneMinAi,
                RouteType: AiRouteTypes.Chat,
                ConversationId: "conv-transport",
                BaseUrl: "https://api.1min.ai/api/chat-with-ai",
                ModelId: "1minai-chat",
                UserMessage: "Summarize the session.",
                SystemPrompt: "Structured Chummer data first.",
                Stream: true,
                AttachmentIds: Array.Empty<string>(),
                RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime],
                AllowedTools: Array.Empty<AiToolDescriptor>(),
                CredentialTier: AiProviderCredentialTiers.Fallback,
                CredentialSlotIndex: 1,
                RuntimeFingerprint: "sha256:transport",
                CharacterId: "char-transport",
                WorkspaceId: "ws-transport"));

        Assert.AreEqual(AiProviderTransportStates.NotImplemented, response.TransportState);
        StringAssert.Contains(response.Answer, "scaffold stayed server-side");
        Assert.IsFalse(string.IsNullOrWhiteSpace(response.FlavorLine));
        Assert.IsNotNull(response.StructuredAnswer);
        Assert.IsTrue(response.StructuredAnswer!.ActionDrafts.Any(draft => draft.ActionId == AiSuggestedActionIds.OpenRuntimeInspector));
        Assert.IsTrue(response.StructuredAnswer.ActionDrafts.Any(draft =>
            draft.ActionId == AiSuggestedActionIds.OpenRuntimeInspector
            && draft.RuntimeFingerprint == "sha256:transport"
            && draft.CharacterId == "char-transport"
            && draft.WorkspaceId == "ws-transport"));
        Assert.IsTrue(response.Citations.Any(citation => citation.Kind == AiCitationKinds.Runtime));
        Assert.IsTrue(response.Citations.Any(citation => citation.Kind == AiCitationKinds.Corpus));
        Assert.IsTrue(response.SuggestedActions.Any(action => action.ActionId == AiSuggestedActionIds.OpenRuntimeInspector));
        Assert.IsTrue(response.SuggestedActions.Any(action =>
            action.ActionId == AiSuggestedActionIds.OpenRuntimeInspector
            && action.RuntimeFingerprint == "sha256:transport"
            && action.CharacterId == "char-transport"
            && action.WorkspaceId == "ws-transport"));
    }

    [TestMethod]
    public void Http_transport_client_maps_1minai_response_and_uses_selected_fallback_key()
    {
        RecordingHttpMessageHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
{
  "aiRecord": {
    "conversationId": "conv-1min",
    "aiRecordDetail": {
      "resultObject": [
        {
          "type": "answer",
          "content": "The Chummer-grounded relay says you should bank the karma until after the next run."
        }
      ]
    }
  }
}
""")
        });
        using HttpClient httpClient = new(handler);
        HttpAiProviderTransportClient client = new(CreateCredentialCatalog(), httpClient);

        AiProviderTransportResponse response = client.Execute(
            OwnerScope.LocalSingleUser,
            new AiProviderTransportRequest(
                ProviderId: AiProviderIds.OneMinAi,
                RouteType: AiRouteTypes.Docs,
                ConversationId: "conv-request",
                BaseUrl: "https://api.1min.ai",
                ModelId: "gpt-4.1-mini",
                UserMessage: "Summarize the grounded rules answer.",
                SystemPrompt: "Structured Chummer data first.",
                Stream: false,
                AttachmentIds: ["note-1"],
                RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime],
                AllowedTools: Array.Empty<AiToolDescriptor>(),
                CredentialTier: AiProviderCredentialTiers.Fallback,
                CredentialSlotIndex: 0,
                RuntimeFingerprint: "sha256:transport"));

        Assert.AreEqual(AiProviderTransportStates.Completed, response.TransportState);
        Assert.AreEqual("conv-1min", response.ConversationId);
        StringAssert.Contains(response.Answer, "bank the karma");
        Assert.AreEqual("https://api.1min.ai/api/chat-with-ai", handler.LastRequest!.RequestUri!.ToString());
        Assert.AreEqual("fallback-1min", handler.LastRequest.Headers.GetValues("API-KEY").Single());
        string body = handler.LastBody ?? string.Empty;
        StringAssert.Contains(body, "\"type\":\"UNIFY_CHAT_WITH_AI\"");
        StringAssert.Contains(body, "\"conversationId\":\"conv-request\"");
        StringAssert.Contains(body, "\"attachments\":{\"files\":[\"note-1\"]}");
        Assert.IsTrue(response.Citations.Any(citation => citation.Kind == AiCitationKinds.Runtime));
    }

    [TestMethod]
    public void Http_transport_client_maps_aimagicx_response_and_uses_bearer_auth()
    {
        RecordingHttpMessageHandler handler = new((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
{
  "id": "chatcmpl-magicx",
  "choices": [
    {
      "message": {
        "role": "assistant",
        "content": "Spend the karma on Reaction first; the runtime lock says it improves your weakest derived defense."
      }
    }
  ]
}
""")
        });
        using HttpClient httpClient = new(handler);
        HttpAiProviderTransportClient client = new(CreateCredentialCatalog(), httpClient);

        AiProviderTransportResponse response = client.Execute(
            OwnerScope.LocalSingleUser,
            new AiProviderTransportRequest(
                ProviderId: AiProviderIds.AiMagicx,
                RouteType: AiRouteTypes.Coach,
                ConversationId: "conv-magicx",
                BaseUrl: "https://beta.aimagicx.com/api/v1",
                ModelId: "magicx-coach",
                UserMessage: "What should I spend 18 Karma on next?",
                SystemPrompt: "Structured Chummer data first.",
                Stream: true,
                AttachmentIds: Array.Empty<string>(),
                RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
                AllowedTools:
                [
                    AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.ExplainDerivedValue),
                    AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.SearchBuildIdeas)
                ],
                CredentialTier: AiProviderCredentialTiers.Primary,
                CredentialSlotIndex: 0,
                RuntimeFingerprint: "sha256:transport"));

        Assert.AreEqual(AiProviderTransportStates.Completed, response.TransportState);
        Assert.AreEqual("chatcmpl-magicx", response.ConversationId);
        StringAssert.Contains(response.Answer, "Spend the karma on Reaction first");
        Assert.AreEqual("https://beta.aimagicx.com/api/v1/chat", handler.LastRequest!.RequestUri!.ToString());
        Assert.AreEqual("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.AreEqual("primary-magicx", handler.LastRequest.Headers.Authorization.Parameter);
        string body = handler.LastBody ?? string.Empty;
        StringAssert.Contains(body, "\"messages\"");
        StringAssert.Contains(body, "\"tools\"");
        StringAssert.Contains(body, "\"name\":\"explain_value\"");
        Assert.IsTrue(response.ToolInvocations.Any(invocation => invocation.ToolId == AiToolIds.ExplainDerivedValue));
    }

    private static AiProviderTurnPlan CreateTurnPlan()
    {
        AiProviderRouteDecision routeDecision = new(
            RouteType: AiRouteTypes.Coach,
            ProviderId: AiProviderIds.AiMagicx,
            Reason: "remote transport test",
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            ToolingEnabled: true,
            RetrievalEnabled: true,
            CredentialTier: AiProviderCredentialTiers.Primary,
            CredentialSlotIndex: 0);
        AiGroundingBundle grounding = new(
            RouteType: AiRouteTypes.Coach,
            RuntimeFingerprint: "sha256:transport",
            CharacterId: "char-transport",
            ConversationId: "conv-transport",
            RuntimeFacts: new Dictionary<string, string> { ["runtimeFingerprint"] = "sha256:transport" },
            CharacterFacts: new Dictionary<string, string> { ["characterId"] = "char-transport" },
            Constraints: ["No mutation."],
            RetrievedItems: [],
            AllowedTools: [AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.ExplainDerivedValue)]);
        AiBudgetSnapshot budget = new(
            BudgetUnit: AiBudgetUnits.ChummerAiUnits,
            MonthlyAllowance: 180,
            MonthlyConsumed: 5,
            BurstLimitPerMinute: 8);

        return new AiProviderTurnPlan(
            ProviderId: AiProviderIds.AiMagicx,
            RouteType: AiRouteTypes.Coach,
            ConversationId: "conv-transport",
            UserMessage: "What should I spend 18 Karma on next?",
            SystemPrompt: "Structured Chummer data first.",
            Stream: true,
            AttachmentIds: Array.Empty<string>(),
            RetrievalCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community],
            AllowedTools:
            [
                AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.ExplainDerivedValue),
                AiGatewayDefaults.ResolveToolDescriptor(AiToolIds.SearchBuildIdeas)
            ],
            GroundingSections:
            [
                new AiGroundingSection(AiGroundingSectionIds.Runtime, "Runtime", ["runtimeFingerprint: sha256:transport"]),
                new AiGroundingSection(AiGroundingSectionIds.Character, "Character", ["characterId: char-transport"])
            ],
            RouteDecision: routeDecision,
            Grounding: grounding,
            Budget: budget);
    }

    private sealed class RecordingTransportClient : IAiProviderTransportClient
    {
        public AiProviderTransportRequest? LastRequest { get; private set; }

        public AiProviderTransportResponse Execute(OwnerScope owner, AiProviderTransportRequest request)
        {
            LastRequest = request;
            return new AiProviderTransportResponse(
                ProviderId: request.ProviderId,
                RouteType: request.RouteType,
                ConversationId: request.ConversationId,
                TransportState: AiProviderTransportStates.Completed,
                Answer: "transport response",
                Citations: [new AiCitation("runtime", "Runtime Lock", "lock-1")],
                SuggestedActions: Array.Empty<AiSuggestedAction>(),
                ToolInvocations: [new AiToolInvocation(AiToolIds.ExplainDerivedValue, "prepared", "Explain tool would be available.")],
                FlavorLine: "Clean relay. The transport scaffold is keeping this grounded while the live hop stays disabled.",
                StructuredAnswer: new AiStructuredAnswer(
                    Summary: "Transport adapter returned a grounded placeholder response.",
                    Recommendations:
                    [
                        new AiRecommendation(
                            RecommendationId: "transport-preview",
                            Title: "Review transport preview",
                            Reason: "This response was synthesized by the recording transport client.",
                            ExpectedEffect: "Confirms the typed transport mapping stays intact.")
                    ],
                    Evidence:
                    [
                        new AiEvidenceEntry("Runtime Lock", "Transport response carried the runtime citation.", "lock-1", "runtime")
                    ],
                    Risks:
                    [
                        new AiRiskEntry(AiRiskSeverities.Note, "Live transport disabled", "Remote execution is still disabled for this path.")
                    ],
                    Confidence: AiConfidenceLevels.Grounded,
                    RuntimeFingerprint: request.RuntimeFingerprint,
                    Sources:
                    [
                        new AiSourceReference(AiCitationKinds.Runtime, "Runtime Lock", "lock-1", "runtime")
                    ],
                    ActionDrafts: Array.Empty<AiActionDraft>()));
        }
    }

    private static StaticCredentialCatalog CreateCredentialCatalog()
        => new StaticCredentialCatalog(
            new Dictionary<string, AiProviderCredentialSet>(StringComparer.Ordinal)
            {
                [AiProviderIds.AiMagicx] = new(
                    PrimaryCredentials: ["primary-magicx"],
                    FallbackCredentials: ["fallback-magicx"]),
                [AiProviderIds.OneMinAi] = new(
                    PrimaryCredentials: ["primary-1min"],
                    FallbackCredentials: ["fallback-1min"])
            });

    private sealed class StaticCredentialCatalog(IReadOnlyDictionary<string, AiProviderCredentialSet> credentialSets) : IAiProviderCredentialCatalog
    {
        private readonly IReadOnlyDictionary<string, AiProviderCredentialSet> _credentialSets = credentialSets;

        public IReadOnlyDictionary<string, AiProviderCredentialCounts> GetConfiguredCredentialCounts()
            => _credentialSets.ToDictionary(
                static pair => pair.Key,
                static pair => new AiProviderCredentialCounts(
                    pair.Value.PrimaryCredentials.Count,
                    pair.Value.FallbackCredentials.Count),
                StringComparer.Ordinal);

        public IReadOnlyDictionary<string, AiProviderCredentialSet> GetConfiguredCredentialSets()
            => _credentialSets;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> responder) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _responder = responder;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(_responder(request, LastBody));
        }
    }
}
