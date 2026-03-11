using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Chummer.Application.AI;
using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Infrastructure.AI;

public sealed class HttpAiProviderTransportClient : IAiProviderTransportClient, IDisposable
{
    private readonly IAiProviderCredentialCatalog _credentialCatalog;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public HttpAiProviderTransportClient(
        IAiProviderCredentialCatalog credentialCatalog,
        HttpClient? httpClient = null)
    {
        _credentialCatalog = credentialCatalog ?? throw new ArgumentNullException(nameof(credentialCatalog));
        _httpClient = httpClient ?? new HttpClient();
        _ownsHttpClient = httpClient is null;
    }

    public AiProviderTransportResponse Execute(OwnerScope owner, AiProviderTransportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        AiProviderTransportResponse scaffoldResponse = new NotImplementedAiProviderTransportClient().Execute(owner, request);

        try
        {
            string apiKey = ResolveApiKey(request);
            using HttpRequestMessage httpRequest = CreateHttpRequest(request, apiKey);
            using HttpResponseMessage response = _httpClient.SendAsync(httpRequest).GetAwaiter().GetResult();
            string responseContent = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                return CreateFailureResponse(
                    request,
                    scaffoldResponse,
                    $"Provider relay returned HTTP {(int)response.StatusCode}.");
            }

            string? answer = ParseAnswer(request.ProviderId, responseContent);
            if (string.IsNullOrWhiteSpace(answer))
            {
                return CreateFailureResponse(
                    request,
                    scaffoldResponse,
                    "Provider relay returned no assistant content.");
            }

            string conversationId = ParseConversationId(request.ProviderId, responseContent)
                ?? request.ConversationId
                ?? $"{request.RouteType}-{request.ProviderId}";

            return new AiProviderTransportResponse(
                ProviderId: request.ProviderId,
                RouteType: request.RouteType,
                ConversationId: conversationId,
                TransportState: AiProviderTransportStates.Completed,
                Answer: answer.Trim(),
                Citations: scaffoldResponse.Citations,
                SuggestedActions: scaffoldResponse.SuggestedActions,
                ToolInvocations: scaffoldResponse.ToolInvocations);
        }
        catch (Exception ex)
        {
            return CreateFailureResponse(request, scaffoldResponse, ex.Message);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private HttpRequestMessage CreateHttpRequest(AiProviderTransportRequest request, string apiKey)
        => request.ProviderId switch
        {
            AiProviderIds.OneMinAi => CreateOneMinAiRequest(request, apiKey),
            AiProviderIds.AiMagicx => CreateAiMagicxRequest(request, apiKey),
            _ => throw new InvalidOperationException($"Unsupported AI provider '{request.ProviderId}'.")
        };

    private static HttpRequestMessage CreateOneMinAiRequest(AiProviderTransportRequest request, string apiKey)
    {
        string endpoint = ResolveOneMinAiEndpoint(request.BaseUrl);
        JsonObject payload = new()
        {
            ["type"] = "UNIFY_CHAT_WITH_AI",
            ["model"] = ResolveRequiredModelId(request, "1minAI"),
            ["promptObject"] = CreateOneMinAiPromptObject(request)
        };

        HttpRequestMessage httpRequest = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.TryAddWithoutValidation("API-KEY", apiKey);
        return httpRequest;
    }

    private static HttpRequestMessage CreateAiMagicxRequest(AiProviderTransportRequest request, string apiKey)
    {
        string endpoint = ResolveAiMagicxEndpoint(request.BaseUrl);
        JsonObject payload = new()
        {
            ["model"] = ResolveRequiredModelId(request, "AI Magicx"),
            ["messages"] = new JsonArray(
                new JsonObject
                {
                    ["role"] = AiConversationRoles.System,
                    ["content"] = request.SystemPrompt
                },
                new JsonObject
                {
                    ["role"] = AiConversationRoles.User,
                    ["content"] = request.UserMessage
                }),
            ["stream"] = request.Stream
        };
        JsonArray tools = CreateAiMagicxTools(request.AllowedTools);
        if (tools.Count > 0)
        {
            payload["tools"] = tools;
        }

        HttpRequestMessage httpRequest = new(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return httpRequest;
    }

    private string ResolveApiKey(AiProviderTransportRequest request)
    {
        IReadOnlyDictionary<string, AiProviderCredentialSet> configuredSets = _credentialCatalog.GetConfiguredCredentialSets();
        if (!configuredSets.TryGetValue(request.ProviderId, out AiProviderCredentialSet? credentialSet))
        {
            throw new InvalidOperationException($"No credential catalog entry was found for provider '{request.ProviderId}'.");
        }

        IReadOnlyList<string> preferredCredentials = request.CredentialTier switch
        {
            AiProviderCredentialTiers.Fallback => credentialSet.FallbackCredentials,
            _ => credentialSet.PrimaryCredentials
        };

        if (TryResolveCredential(preferredCredentials, request.CredentialSlotIndex, out string? resolvedCredential))
        {
            return resolvedCredential!;
        }

        if (TryResolveCredential(credentialSet.PrimaryCredentials, 0, out resolvedCredential))
        {
            return resolvedCredential!;
        }

        if (TryResolveCredential(credentialSet.FallbackCredentials, 0, out resolvedCredential))
        {
            return resolvedCredential!;
        }

        throw new InvalidOperationException($"No configured API key is available for provider '{request.ProviderId}'.");
    }

    private static bool TryResolveCredential(IReadOnlyList<string> credentials, int? slotIndex, out string? credential)
    {
        if (credentials.Count == 0)
        {
            credential = null;
            return false;
        }

        int requestedIndex = slotIndex.GetValueOrDefault();
        if (requestedIndex >= 0 && requestedIndex < credentials.Count)
        {
            credential = credentials[requestedIndex];
            return true;
        }

        credential = credentials[0];
        return true;
    }

    private static JsonObject CreateOneMinAiPromptObject(AiProviderTransportRequest request)
    {
        JsonObject promptObject = new()
        {
            ["prompt"] = CombineSystemAndUserPrompt(request),
            ["settings"] = new JsonObject
            {
                ["historySettings"] = new JsonObject
                {
                    ["historyMessageLimit"] = 10,
                    ["isMixed"] = false
                },
                ["withMemories"] = false
            }
        };

        if (!string.IsNullOrWhiteSpace(request.ConversationId))
        {
            promptObject["conversationId"] = request.ConversationId;
        }

        if (request.AttachmentIds.Count > 0)
        {
            promptObject["attachments"] = new JsonObject
            {
                ["files"] = CreateStringArray(request.AttachmentIds)
            };
        }

        return promptObject;
    }

    private static JsonArray CreateAiMagicxTools(IReadOnlyList<AiToolDescriptor> allowedTools)
        => new(allowedTools
            .GroupBy(static tool => tool.ToolId, StringComparer.Ordinal)
            .Select(static group => group.Last())
            .Select(tool => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.ToolId,
                    ["description"] = tool.Description,
                    ["parameters"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject(),
                        ["additionalProperties"] = false
                    }
                }
            })
            .ToArray());

    private static JsonArray CreateStringArray(IReadOnlyList<string> values)
        => new(values.Select(static value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static string CombineSystemAndUserPrompt(AiProviderTransportRequest request)
        => $"System instructions:\n{request.SystemPrompt}\n\nUser message:\n{request.UserMessage}";

    private static string ResolveOneMinAiEndpoint(string baseUrl)
    {
        string trimmed = baseUrl.TrimEnd('/');
        return trimmed.EndsWith("/api/chat-with-ai", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/api/chat-with-ai";
    }

    private static string ResolveAiMagicxEndpoint(string baseUrl)
    {
        string trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/api/v1/chat", StringComparison.OrdinalIgnoreCase)
            || trimmed.EndsWith("/chat", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (trimmed.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{trimmed}/chat";
        }

        return $"{trimmed}/api/v1/chat";
    }

    private static string ResolveRequiredModelId(AiProviderTransportRequest request, string providerDisplayName)
    {
        if (string.IsNullOrWhiteSpace(request.ModelId))
        {
            throw new InvalidOperationException($"{providerDisplayName} transport requires a configured model id.");
        }

        return request.ModelId.Trim();
    }

    private static string? ParseConversationId(string providerId, string responseContent)
    {
        JsonNode? response = ParseJson(responseContent);
        if (response is null)
        {
            return null;
        }

        return providerId switch
        {
            AiProviderIds.OneMinAi => TryGetString(response["aiRecord"]?["conversationId"]),
            AiProviderIds.AiMagicx => TryGetString(response["id"]),
            _ => null
        };
    }

    private static string? ParseAnswer(string providerId, string responseContent)
    {
        JsonNode? response = ParseJson(responseContent);
        if (response is null)
        {
            return null;
        }

        return providerId switch
        {
            AiProviderIds.OneMinAi => ExtractString(
                response["aiRecord"]?["aiRecordDetail"]?["resultObject"]
                ?? response["resultObject"]
                ?? response),
            AiProviderIds.AiMagicx => ExtractString(
                response["choices"]?[0]?["message"]?["content"]
                ?? response["choices"]?[0]?["text"]
                ?? response["message"]?["content"]
                ?? response["answer"]
                ?? response),
            _ => null
        };
    }

    private static JsonNode? ParseJson(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent))
        {
            return null;
        }

        return JsonNode.Parse(responseContent);
    }

    private static string? TryGetString(JsonNode? node)
        => node is null
            ? null
            : node is JsonValue value && value.TryGetValue(out string? raw)
                ? raw
                : null;

    private static string? ExtractString(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue(out string? stringValue) && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue;
            }

            return null;
        }

        if (node is JsonObject obj)
        {
            foreach (string key in new[] { "content", "message", "text", "summary", "answer" })
            {
                string? keyedValue = ExtractString(obj[key]);
                if (!string.IsNullOrWhiteSpace(keyedValue))
                {
                    return keyedValue;
                }
            }

            foreach (JsonNode? child in obj.Select(static pair => pair.Value))
            {
                string? childValue = ExtractString(child);
                if (!string.IsNullOrWhiteSpace(childValue))
                {
                    return childValue;
                }
            }

            return null;
        }

        if (node is JsonArray array)
        {
            foreach (JsonNode? item in array)
            {
                string? itemValue = ExtractString(item);
                if (!string.IsNullOrWhiteSpace(itemValue))
                {
                    return itemValue;
                }
            }
        }

        return null;
    }

    private static AiProviderTransportResponse CreateFailureResponse(
        AiProviderTransportRequest request,
        AiProviderTransportResponse scaffoldResponse,
        string reason)
        => new(
            ProviderId: request.ProviderId,
            RouteType: request.RouteType,
            ConversationId: request.ConversationId,
            TransportState: AiProviderTransportStates.Failed,
            Answer: $"Live provider relay failed: {reason}",
            Citations: scaffoldResponse.Citations,
            SuggestedActions: scaffoldResponse.SuggestedActions,
            ToolInvocations: scaffoldResponse.ToolInvocations,
            FlavorLine: "The external relay failed, so this answer stayed on the grounded Chummer scaffold.");
}
