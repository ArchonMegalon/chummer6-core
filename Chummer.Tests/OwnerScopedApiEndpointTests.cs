#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Chummer.Api.Endpoints;
using Chummer.Api.Owners;
using Chummer.Application.AI;
using Chummer.Application.Hub;
using Chummer.Application.Owners;
using Chummer.Application.Tools;
using Chummer.Application.Workspaces;
using Chummer.Contracts.AI;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class OwnerScopedApiEndpointTests
{
    private const string OwnerHeaderName = "X-Chummer-Owner";

    [TestMethod]
    public async Task Settings_endpoints_isolate_owner_scoped_payloads_when_forwarded_owner_header_is_enabled()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        await PostRequiredJsonObject(client, "/api/tools/settings/global", new JsonObject
        {
            ["theme"] = "alpha"
        }, "alice@example.com");
        await PostRequiredJsonObject(client, "/api/tools/settings/global", new JsonObject
        {
            ["theme"] = "beta"
        }, "bob@example.com");

        JsonObject alice = await GetRequiredJsonObject(client, "/api/tools/settings/global", "alice@example.com");
        JsonObject bob = await GetRequiredJsonObject(client, "/api/tools/settings/global", "bob@example.com");
        JsonObject local = await GetRequiredJsonObject(client, "/api/tools/settings/global");

        Assert.AreEqual("alpha", alice["settings"]?["theme"]?.GetValue<string>());
        Assert.AreEqual("beta", bob["settings"]?["theme"]?.GetValue<string>());
        Assert.IsNotNull(local["settings"]);
        Assert.IsNull(local["settings"]?["theme"]);
    }

    [TestMethod]
    public async Task Roster_endpoints_isolate_entries_by_forwarded_owner_header()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        await PostRequiredJsonObject(client, "/api/tools/roster", new JsonObject
        {
            ["name"] = "Alpha",
            ["alias"] = "A"
        }, "alice@example.com");
        await PostRequiredJsonObject(client, "/api/tools/roster", new JsonObject
        {
            ["name"] = "Beta",
            ["alias"] = "B"
        }, "bob@example.com");

        JsonObject alice = await GetRequiredJsonObject(client, "/api/tools/roster", "alice@example.com");
        JsonObject bob = await GetRequiredJsonObject(client, "/api/tools/roster", "bob@example.com");
        JsonObject local = await GetRequiredJsonObject(client, "/api/tools/roster");

        Assert.AreEqual(1, alice["count"]?.GetValue<int>());
        Assert.AreEqual("Alpha", alice["entries"]?[0]?["name"]?.GetValue<string>());
        Assert.AreEqual(1, bob["count"]?.GetValue<int>());
        Assert.AreEqual("Beta", bob["entries"]?[0]?["name"]?.GetValue<string>());
        Assert.AreEqual(0, local["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Workspace_endpoints_isolate_imported_workspaces_by_forwarded_owner_header()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject import = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = "<character><name>Owner Runner</name></character>",
            ["rulesetId"] = "sr5"
        }, "alice@example.com");
        string workspaceId = import["id"]?.GetValue<string>() ?? string.Empty;

        JsonObject alice = await GetRequiredJsonObject(client, "/api/workspaces", "alice@example.com");
        JsonObject bob = await GetRequiredJsonObject(client, "/api/workspaces", "bob@example.com");
        JsonObject local = await GetRequiredJsonObject(client, "/api/workspaces");

        Assert.AreEqual(1, alice["count"]?.GetValue<int>());
        Assert.AreEqual(workspaceId, alice["workspaces"]?[0]?["id"]?.GetValue<string>());
        Assert.AreEqual(0, bob["count"]?.GetValue<int>());
        Assert.AreEqual(0, local["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Hub_publication_endpoints_isolate_drafts_by_forwarded_owner_header()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "alice.pack",
            ["rulesetId"] = "sr5",
            ["title"] = "Alice Pack"
        }, "alice@example.com");
        await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "bob.pack",
            ["rulesetId"] = "sr5",
            ["title"] = "Bob Pack"
        }, "bob@example.com");

        JsonObject alice = await GetRequiredJsonObject(client, "/api/hub/publish/drafts?ruleset=sr5", "alice@example.com");
        JsonObject bob = await GetRequiredJsonObject(client, "/api/hub/publish/drafts?ruleset=sr5", "bob@example.com");
        JsonObject local = await GetRequiredJsonObject(client, "/api/hub/publish/drafts?ruleset=sr5");

        Assert.AreEqual(1, alice["items"]?.AsArray().Count);
        Assert.AreEqual("alice.pack", alice["items"]?[0]?["projectId"]?.GetValue<string>());
        Assert.AreEqual(1, bob["items"]?.AsArray().Count);
        Assert.AreEqual("bob.pack", bob["items"]?[0]?["projectId"]?.GetValue<string>());
        Assert.AreEqual(0, local["items"]?.AsArray().Count);
    }

    [TestMethod]
    public async Task Hub_publication_detail_endpoint_respects_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject aliceDraft = await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "alice.pack.detail",
            ["rulesetId"] = "sr5",
            ["title"] = "Alice Pack Detail"
        }, "alice@example.com");
        string draftId = aliceDraft["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject alice = await GetRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}", "alice@example.com");
        Assert.AreEqual(draftId, alice["draft"]?["draftId"]?.GetValue<string>());

        using HttpRequestMessage request = new(HttpMethod.Get, $"/api/hub/publish/drafts/{draftId}");
        request.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task Hub_publication_update_endpoint_respects_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject aliceDraft = await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "alice.pack.update",
            ["rulesetId"] = "sr5",
            ["title"] = "Alice Pack Update"
        }, "alice@example.com");
        string draftId = aliceDraft["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject updated = await PutRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}", new JsonObject
        {
            ["title"] = "Alice Pack Updated",
            ["summary"] = "Street-level runtime",
            ["description"] = "Campaign-specific SR5 publication draft."
        }, "alice@example.com");
        Assert.AreEqual("Alice Pack Updated", updated["title"]?.GetValue<string>());
        Assert.AreEqual("Street-level runtime", updated["summary"]?.GetValue<string>());

        using HttpRequestMessage request = new(HttpMethod.Put, $"/api/hub/publish/drafts/{draftId}")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["title"] = "Bob Cannot Update This",
                ["summary"] = "blocked"
            })
        };
        request.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task Hub_publication_archive_and_delete_endpoints_respect_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject aliceDraft = await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "alice.pack.lifecycle",
            ["rulesetId"] = "sr5",
            ["title"] = "Alice Pack Lifecycle"
        }, "alice@example.com");
        string draftId = aliceDraft["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject archived = await PostRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}/archive", new JsonObject(), "alice@example.com");
        Assert.AreEqual(HubPublicationStates.Archived, archived["state"]?.GetValue<string>());

        using HttpRequestMessage archiveRequest = new(HttpMethod.Post, $"/api/hub/publish/drafts/{draftId}/archive")
        {
            Content = JsonContent.Create(new JsonObject())
        };
        archiveRequest.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage archiveResponse = await client.SendAsync(archiveRequest);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)archiveResponse.StatusCode);

        using HttpRequestMessage deleteRequest = new(HttpMethod.Delete, $"/api/hub/publish/drafts/{draftId}");
        deleteRequest.Headers.Add(OwnerHeaderName, "alice@example.com");
        using HttpResponseMessage deleteResponse = await client.SendAsync(deleteRequest);
        Assert.AreEqual(StatusCodes.Status204NoContent, (int)deleteResponse.StatusCode);

        using HttpRequestMessage bobDeleteRequest = new(HttpMethod.Delete, $"/api/hub/publish/drafts/{draftId}");
        bobDeleteRequest.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage bobDeleteResponse = await client.SendAsync(bobDeleteRequest);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)bobDeleteResponse.StatusCode);
    }

    [TestMethod]
    public async Task Hub_moderation_action_endpoints_respect_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        await PostRequiredJsonObject(client, "/api/hub/publish/drafts", new JsonObject
        {
            ["projectKind"] = HubCatalogItemKinds.RulePack,
            ["projectId"] = "alice.pack.moderation",
            ["rulesetId"] = "sr5",
            ["title"] = "Alice Pack Moderation"
        }, "alice@example.com");
        JsonObject submission = await PostRequiredJsonObject(client, "/api/hub/publish/rulepack/alice.pack.moderation/submit?ruleset=sr5", new JsonObject
        {
            ["notes"] = "ready"
        }, "alice@example.com");
        string caseId = submission["caseId"]?.GetValue<string>() ?? string.Empty;

        JsonObject approved = await PostRequiredJsonObject(client, $"/api/hub/moderation/queue/{caseId}/approve", new JsonObject
        {
            ["notes"] = "approved"
        }, "alice@example.com");
        Assert.AreEqual(HubModerationStates.Approved, approved["state"]?.GetValue<string>());

        using HttpRequestMessage rejectRequest = new(HttpMethod.Post, $"/api/hub/moderation/queue/{caseId}/reject")
        {
            Content = JsonContent.Create(new JsonObject
            {
                ["notes"] = "bob cannot update this"
            })
        };
        rejectRequest.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage rejectResponse = await client.SendAsync(rejectRequest);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)rejectResponse.StatusCode);
    }

    [TestMethod]
    public async Task Hub_publisher_endpoints_respect_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject alicePublisher = await PutRequiredJsonObject(client, "/api/hub/publishers/shadowops", new JsonObject
        {
            ["displayName"] = "ShadowOps",
            ["slug"] = "shadowops",
            ["description"] = "Campaign runtime publisher"
        }, "alice@example.com");
        Assert.AreEqual("shadowops", alicePublisher["publisherId"]?.GetValue<string>());

        JsonObject aliceList = await GetRequiredJsonObject(client, "/api/hub/publishers", "alice@example.com");
        JsonObject bobList = await GetRequiredJsonObject(client, "/api/hub/publishers", "bob@example.com");

        Assert.AreEqual(1, aliceList["items"]?.AsArray().Count);
        Assert.AreEqual(0, bobList["items"]?.AsArray().Count);

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/hub/publishers/shadowops");
        request.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(StatusCodes.Status404NotFound, (int)response.StatusCode);
    }

    [TestMethod]
    public async Task Hub_review_endpoints_respect_forwarded_owner_scope()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject aliceReview = await PutRequiredJsonObject(client, "/api/hub/reviews/rulepack/alice.pack", new JsonObject
        {
            ["rulesetId"] = "sr5",
            ["recommendationState"] = HubRecommendationStates.Recommended,
            ["stars"] = 5,
            ["reviewText"] = "Great pack",
            ["usedAtTable"] = true
        }, "alice@example.com");
        Assert.AreEqual(HubRecommendationStates.Recommended, aliceReview["recommendationState"]?.GetValue<string>());

        JsonObject aliceList = await GetRequiredJsonObject(client, "/api/hub/reviews?kind=rulepack&itemId=alice.pack&ruleset=sr5", "alice@example.com");
        JsonObject bobList = await GetRequiredJsonObject(client, "/api/hub/reviews?kind=rulepack&itemId=alice.pack&ruleset=sr5", "bob@example.com");

        Assert.AreEqual(1, aliceList["items"]?.AsArray().Count);
        Assert.AreEqual(0, bobList["items"]?.AsArray().Count);
    }

    [TestMethod]
    public async Task Ai_gateway_endpoints_preserve_forwarded_owner_scope_for_recorded_conversations()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Recommend a next karma spend.",
                ConversationId: "conv-owner-1",
                RuntimeFingerprint: "sha256:runtime-owner",
                CharacterId: "char-owner-1"))
        };
        request.Headers.Add(OwnerHeaderName, "alice@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(StatusCodes.Status200OK, (int)response.StatusCode);

        JsonObject payload = await ParseRequiredJsonObject(response);
        Assert.AreEqual(AiRouteTypes.Coach, payload["routeType"]?.GetValue<string>());
        Assert.AreEqual("conv-owner-1", payload["conversationId"]?.GetValue<string>());

        JsonObject aliceConversation = await GetRequiredJsonObject(client, "/api/ai/conversations/conv-owner-1", "alice@example.com");
        Assert.AreEqual("char-owner-1", aliceConversation["characterId"]?.GetValue<string>());

        using HttpRequestMessage otherOwnerRequest = new(HttpMethod.Get, "/api/ai/conversations/conv-owner-1");
        otherOwnerRequest.Headers.Add(OwnerHeaderName, "bob@example.com");
        using HttpResponseMessage otherOwnerResponse = await client.SendAsync(otherOwnerRequest);
        Assert.AreEqual(StatusCodes.Status501NotImplemented, (int)otherOwnerResponse.StatusCode);
        JsonObject otherOwnerPayload = await ParseRequiredJsonObject(otherOwnerResponse);
        Assert.AreEqual("bob@example.com", otherOwnerPayload["ownerId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_preview_endpoint_preserves_workspace_scope_in_owner_scoped_grounding_payloads()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/ai/preview/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Recommend a grounded advancement path.",
                ConversationId: "conv-preview-owner",
                RuntimeFingerprint: "sha256:runtime-owner",
                CharacterId: "char-owner-1",
                WorkspaceId: "ws-owner-preview"))
        };
        request.Headers.Add(OwnerHeaderName, "alice@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);
        Assert.AreEqual(StatusCodes.Status200OK, (int)response.StatusCode);

        JsonObject payload = await ParseRequiredJsonObject(response);
        Assert.AreEqual("ws-owner-preview", payload["grounding"]?["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("ws-owner-preview", payload["grounding"]?["characterFacts"]?["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("ws-owner-preview", payload["providerRequest"]?["workspaceId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_action_preview_endpoints_preserve_workspace_scope_in_owner_scoped_receipts()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        JsonObject karmaReceipt = await PostRequiredJsonObject(client, "/api/ai/preview/karma-spend", new JsonObject
        {
            ["characterId"] = "char-owner-1",
            ["runtimeFingerprint"] = "sha256:runtime-owner",
            ["workspaceId"] = "ws-owner-preview",
            ["steps"] = new JsonArray
            {
                new JsonObject
                {
                    ["stepId"] = "step-1",
                    ["title"] = "Raise Sneaking",
                    ["amount"] = 10
                }
            }
        }, "alice@example.com");
        JsonObject applyReceipt = await PostRequiredJsonObject(client, "/api/ai/apply-preview", new JsonObject
        {
            ["characterId"] = "char-owner-1",
            ["runtimeFingerprint"] = "sha256:runtime-owner",
            ["workspaceId"] = "ws-owner-preview",
            ["actionDraft"] = new JsonObject
            {
                ["actionId"] = AiSuggestedActionIds.PreviewApplyPlan,
                ["title"] = "Preview Apply Plan",
                ["description"] = "Keep the scoped preview grounded.",
                ["workspaceId"] = "ws-owner-preview"
            }
        }, "alice@example.com");

        Assert.AreEqual("ws-owner-preview", karmaReceipt["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("ws-owner-preview", applyReceipt["workspaceId"]?.GetValue<string>());
        Assert.IsTrue(karmaReceipt["evidence"] is JsonArray karmaEvidence
            && karmaEvidence.OfType<JsonObject>().Any(item => string.Equals(item["referenceId"]?.GetValue<string>(), "ws-owner-preview", StringComparison.Ordinal)));
        Assert.IsTrue(applyReceipt["preparedEffects"] is JsonArray applyEffects
            && applyEffects.OfType<JsonValue>().Any(item => string.Equals(item.GetValue<string>(), "Workbench origin preserved from workspace ws-owner-preview.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Ai_conversation_catalog_and_audit_endpoints_filter_workspace_scope_for_owner_scoped_replays()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient client = app.GetTestClient();

        using HttpRequestMessage alphaRequest = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Coach alpha workspace.",
                ConversationId: "conv-workspace-alpha",
                RuntimeFingerprint: "sha256:runtime-owner",
                CharacterId: "char-owner-1",
                WorkspaceId: "ws-alpha"))
        };
        alphaRequest.Headers.Add(OwnerHeaderName, "alice@example.com");
        using HttpResponseMessage alphaResponse = await client.SendAsync(alphaRequest);
        Assert.AreEqual(StatusCodes.Status200OK, (int)alphaResponse.StatusCode);

        using HttpRequestMessage betaRequest = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Coach beta workspace.",
                ConversationId: "conv-workspace-beta",
                RuntimeFingerprint: "sha256:runtime-owner",
                CharacterId: "char-owner-1",
                WorkspaceId: "ws-beta"))
        };
        betaRequest.Headers.Add(OwnerHeaderName, "alice@example.com");
        using HttpResponseMessage betaResponse = await client.SendAsync(betaRequest);
        Assert.AreEqual(StatusCodes.Status200OK, (int)betaResponse.StatusCode);

        JsonObject alphaCatalog = await GetRequiredJsonObject(
            client,
            $"/api/ai/conversations?routeType={AiRouteTypes.Coach}&characterId=char-owner-1&runtimeFingerprint=sha256:runtime-owner&workspaceId=ws-alpha&maxCount=10",
            "alice@example.com");
        JsonObject alphaAuditCatalog = await GetRequiredJsonObject(
            client,
            $"/api/ai/conversation-audits?routeType={AiRouteTypes.Coach}&characterId=char-owner-1&runtimeFingerprint=sha256:runtime-owner&workspaceId=ws-alpha&maxCount=10",
            "alice@example.com");
        JsonObject betaCatalog = await GetRequiredJsonObject(
            client,
            $"/api/ai/conversations?routeType={AiRouteTypes.Coach}&characterId=char-owner-1&runtimeFingerprint=sha256:runtime-owner&workspaceId=ws-beta&maxCount=10",
            "alice@example.com");

        JsonArray alphaItems = alphaCatalog["items"]?.AsArray()
            ?? throw new AssertFailedException("Conversation catalog did not include an items array.");
        JsonArray alphaAudits = alphaAuditCatalog["items"]?.AsArray()
            ?? throw new AssertFailedException("Conversation audit catalog did not include an items array.");
        JsonArray betaItems = betaCatalog["items"]?.AsArray()
            ?? throw new AssertFailedException("Conversation catalog did not include an items array.");

        Assert.AreEqual(1, alphaCatalog["totalCount"]?.GetValue<int>());
        Assert.AreEqual(1, alphaAuditCatalog["totalCount"]?.GetValue<int>());
        Assert.AreEqual(1, betaCatalog["totalCount"]?.GetValue<int>());
        Assert.AreEqual("conv-workspace-alpha", alphaItems[0]?["conversationId"]?.GetValue<string>());
        Assert.AreEqual("ws-alpha", alphaItems[0]?["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("conv-workspace-alpha", alphaAudits[0]?["conversationId"]?.GetValue<string>());
        Assert.AreEqual("ws-alpha", alphaAudits[0]?["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("conv-workspace-beta", betaItems[0]?["conversationId"]?.GetValue<string>());
        Assert.AreEqual("ws-beta", betaItems[0]?["workspaceId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_turn_endpoints_return_owner_scoped_quota_receipts_when_monthly_budget_is_exhausted()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        usageLedgerStore.RecordUsage(new OwnerScope("alice@example.com"), AiRouteTypes.Coach, 1, DateTimeOffset.UtcNow);
        await using WebApplication app = await CreateAppAsync(new NotImplementedAiGatewayService(
            routeBudgetPolicyCatalog: new StaticAiRouteBudgetPolicyCatalog(
                new AiRouteBudgetPolicyDescriptor(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 1,
                    BurstLimitPerMinute: 4,
                    Notes: "Exhausted coach lane.")),
            usageLedgerStore: usageLedgerStore));
        using HttpClient client = app.GetTestClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Should fail on quota.",
                ConversationId: "conv-quota-owner"))
        };
        request.Headers.Add(OwnerHeaderName, "alice@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        JsonObject payload = (JsonObject)(JsonNode.Parse(content) ?? throw new AssertFailedException("Expected AI quota payload."));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.AreEqual("ai_quota_exceeded", payload["error"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Coach, payload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiBudgetLimitKinds.MonthlyAllowance, payload["limitKind"]?.GetValue<string>());
        Assert.AreEqual(1, payload["budget"]?["monthlyConsumed"]?.GetValue<int>());
        Assert.AreEqual(1, usageLedgerStore.GetMonthlyConsumed(new OwnerScope("alice@example.com"), AiRouteTypes.Coach, DateTimeOffset.UtcNow));
    }

    [TestMethod]
    public async Task Ai_turn_endpoints_return_burst_quota_receipts_when_per_minute_budget_is_exhausted()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        usageLedgerStore.RecordUsage(new OwnerScope("alice@example.com"), AiRouteTypes.Coach, 1, now.AddSeconds(-40));
        usageLedgerStore.RecordUsage(new OwnerScope("alice@example.com"), AiRouteTypes.Coach, 1, now.AddSeconds(-5));
        await using WebApplication app = await CreateAppAsync(new NotImplementedAiGatewayService(
            routeBudgetPolicyCatalog: new StaticAiRouteBudgetPolicyCatalog(
                new AiRouteBudgetPolicyDescriptor(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 10,
                    BurstLimitPerMinute: 2,
                    Notes: "Burst limited coach lane.")),
            usageLedgerStore: usageLedgerStore));
        using HttpClient client = app.GetTestClient();

        using HttpRequestMessage request = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "Should fail on burst quota.",
                ConversationId: "conv-burst-owner"))
        };
        request.Headers.Add(OwnerHeaderName, "alice@example.com");

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        JsonObject payload = (JsonObject)(JsonNode.Parse(content) ?? throw new AssertFailedException("Expected AI burst quota payload."));

        Assert.AreEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.AreEqual("ai_quota_exceeded", payload["error"]?.GetValue<string>());
        Assert.AreEqual(AiBudgetLimitKinds.BurstLimitPerMinute, payload["limitKind"]?.GetValue<string>());
        Assert.AreEqual(2, payload["budget"]?["currentBurstConsumed"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Ai_route_budget_status_endpoint_isolates_live_consumption_by_forwarded_owner_header()
    {
        InMemoryAiUsageLedgerStore usageLedgerStore = new();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        usageLedgerStore.RecordUsage(new OwnerScope("alice@example.com"), AiRouteTypes.Coach, 3, now.AddSeconds(-10));
        usageLedgerStore.RecordUsage(new OwnerScope("bob@example.com"), AiRouteTypes.Coach, 1, now.AddSeconds(-5));
        await using WebApplication app = await CreateAppAsync(new NotImplementedAiGatewayService(
            routeBudgetPolicyCatalog: new StaticAiRouteBudgetPolicyCatalog(
                new AiRouteBudgetPolicyDescriptor(
                    RouteType: AiRouteTypes.Coach,
                    BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                    MonthlyAllowance: 10,
                    BurstLimitPerMinute: 4,
                    Notes: "Coach lane.")),
            usageLedgerStore: usageLedgerStore));
        using HttpClient client = app.GetTestClient();

        JsonArray alice = await GetRequiredJsonArray(client, $"/api/ai/route-budget-statuses?routeType={AiRouteTypes.Coach}", "alice@example.com");
        JsonArray bob = await GetRequiredJsonArray(client, $"/api/ai/route-budget-statuses?routeType={AiRouteTypes.Coach}", "bob@example.com");

        Assert.AreEqual(1, alice.Count);
        Assert.AreEqual(1, bob.Count);
        Assert.AreEqual(3, alice[0]?["monthlyConsumed"]?.GetValue<int>());
        Assert.AreEqual(3, alice[0]?["currentBurstConsumed"]?.GetValue<int>());
        Assert.AreEqual(1, bob[0]?["monthlyConsumed"]?.GetValue<int>());
        Assert.AreEqual(1, bob[0]?["currentBurstConsumed"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Ai_turn_cache_isolated_by_forwarded_owner_header()
    {
        await using WebApplication app = await CreateAppAsync(new NotImplementedAiGatewayService(
            responseCacheStore: new InMemoryAiResponseCacheStore()));
        using HttpClient client = app.GetTestClient();

        JsonObject aliceMiss = await PostRequiredJsonObject(client, "/api/ai/docs/query", new JsonObject
        {
            ["message"] = "How do I open coach from the launcher?",
            ["conversationId"] = "alice-cache-1",
            ["runtimeFingerprint"] = "sha256:owner-cache"
        }, "alice@example.com");
        JsonObject aliceHit = await PostRequiredJsonObject(client, "/api/ai/docs/query", new JsonObject
        {
            ["message"] = "how do i open   coach from the launcher?",
            ["conversationId"] = "alice-cache-2",
            ["runtimeFingerprint"] = "sha256:owner-cache"
        }, "alice@example.com");
        JsonObject bobMiss = await PostRequiredJsonObject(client, "/api/ai/docs/query", new JsonObject
        {
            ["message"] = "How do I open coach from the launcher?",
            ["conversationId"] = "bob-cache-1",
            ["runtimeFingerprint"] = "sha256:owner-cache"
        }, "bob@example.com");

        Assert.AreEqual(AiCacheStatuses.Miss, aliceMiss["cache"]?["status"]?.GetValue<string>());
        Assert.AreEqual(AiCacheStatuses.Hit, aliceHit["cache"]?["status"]?.GetValue<string>());
        Assert.AreEqual(AiCacheStatuses.Miss, bobMiss["cache"]?["status"]?.GetValue<string>());
        Assert.AreEqual(aliceMiss["cache"]?["cacheKey"]?.GetValue<string>(), aliceHit["cache"]?["cacheKey"]?.GetValue<string>());
    }

    private sealed class StubAiDigestService : IAiDigestService
    {
        public AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null)
            => string.IsNullOrWhiteSpace(runtimeFingerprint)
                ? null
                : new AiRuntimeSummaryProjection(
                    RuntimeFingerprint: runtimeFingerprint,
                    RulesetId: string.IsNullOrWhiteSpace(rulesetId) ? "sr5" : rulesetId,
                    Title: "Owner Scoped Runtime",
                    CatalogKind: "owner-scoped",
                    EngineApiVersion: "1.0.0",
                    ContentBundles: ["official.sr5.core@1.0.0"],
                    RulePacks: ["campaign.owner@1.0.0"],
                    ProviderBindings: new Dictionary<string, string>
                    {
                        ["availability.item"] = "official.sr5.core:availability.item"
                    });

        public AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId)
            => string.IsNullOrWhiteSpace(characterId)
                ? null
                : new AiCharacterDigestProjection(
                    CharacterId: characterId,
                    DisplayName: $"{owner.NormalizedValue}:{characterId}",
                    RulesetId: "sr5",
                    RuntimeFingerprint: "sha256:runtime-owner",
                    Summary: new CharacterFileSummary(
                        Name: characterId,
                        Alias: owner.NormalizedValue,
                        Metatype: "Human",
                        BuildMethod: "Priority",
                        CreatedVersion: "5",
                        AppVersion: "10",
                        Karma: 18m,
                        Nuyen: 12000m,
                        Created: true),
                    LastUpdatedUtc: new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.Zero),
                    HasSavedWorkspace: true);

        public AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId)
            => string.IsNullOrWhiteSpace(characterId)
                ? null
                : new AiSessionDigestProjection(
                    CharacterId: characterId,
                    DisplayName: $"{owner.NormalizedValue}:{characterId}",
                    RulesetId: "sr5",
                    RuntimeFingerprint: "sha256:runtime-owner",
                    SelectionState: "selected",
                    SessionReady: true,
                    BundleFreshness: "current",
                    RequiresBundleRefresh: false,
                    ProfileId: "official.sr5.core",
                    ProfileTitle: "Official SR5 Core");
    }

    private static async Task<WebApplication> CreateAppAsync(IAiGatewayService? aiGatewayService = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IOwnerContextAccessor>(provider =>
            new RequestOwnerContextAccessor(
                provider.GetRequiredService<IHttpContextAccessor>(),
                OwnerHeaderName));
        builder.Services.AddSingleton<IHubPublisherStore, InMemoryHubPublisherStore>();
        builder.Services.AddSingleton<IHubPublisherService, DefaultHubPublisherService>();
        builder.Services.AddSingleton<IHubReviewStore, InMemoryHubReviewStore>();
        builder.Services.AddSingleton<IHubReviewService, DefaultHubReviewService>();
        builder.Services.AddSingleton<IHubDraftStore, InMemoryHubDraftStore>();
        builder.Services.AddSingleton<IHubModerationCaseStore, InMemoryHubModerationCaseStore>();
        builder.Services.AddSingleton<IHubPublicationService, DefaultHubPublicationService>();
        builder.Services.AddSingleton<IHubModerationService, DefaultHubModerationService>();
        builder.Services.AddSingleton<IAiGatewayService>(aiGatewayService ?? new NotImplementedAiGatewayService());
        builder.Services.AddSingleton<IAiDigestService, StubAiDigestService>();
        builder.Services.AddSingleton<IAiActionPreviewService, DefaultAiActionPreviewService>();
        builder.Services.AddSingleton<ISettingsStore, InMemorySettingsStore>();
        builder.Services.AddSingleton<IRosterStore, InMemoryRosterStore>();
        builder.Services.AddSingleton<IWorkspaceService, InMemoryWorkspaceService>();

        WebApplication app = builder.Build();
        app.MapAiEndpoints();
        app.MapHubPublisherEndpoints();
        app.MapHubReviewEndpoints();
        app.MapHubPublicationEndpoints();
        app.MapSettingsEndpoints();
        app.MapRosterEndpoints();
        app.MapWorkspaceEndpoints();
        await app.StartAsync();
        return app;
    }

    private static async Task<JsonObject> GetRequiredJsonObject(HttpClient client, string relativePath, string? owner = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, relativePath);
        if (!string.IsNullOrWhiteSpace(owner))
        {
            request.Headers.Add(OwnerHeaderName, owner);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"GET {relativePath} failed with {(int)response.StatusCode}: {content}");
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed;
    }

    private static async Task<JsonObject> PostRequiredJsonObject(HttpClient client, string relativePath, JsonObject payload, string? owner = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrWhiteSpace(owner))
        {
            request.Headers.Add(OwnerHeaderName, owner);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"POST {relativePath} failed with {(int)response.StatusCode}: {content}");
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed;
    }

    private static async Task<JsonArray> GetRequiredJsonArray(HttpClient client, string relativePath, string? owner = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, relativePath);
        if (!string.IsNullOrWhiteSpace(owner))
        {
            request.Headers.Add(OwnerHeaderName, owner);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"GET {relativePath} failed with {(int)response.StatusCode}: {content}");
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonArray>(parsed);
        return (JsonArray)parsed;
    }

    private static async Task<JsonObject> PutRequiredJsonObject(HttpClient client, string relativePath, JsonObject payload, string? owner = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, relativePath)
        {
            Content = JsonContent.Create(payload)
        };
        if (!string.IsNullOrWhiteSpace(owner))
        {
            request.Headers.Add(OwnerHeaderName, owner);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"PUT {relativePath} failed with {(int)response.StatusCode}: {content}");
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed;
    }

    [TestMethod]
    public async Task Ai_conversation_store_is_owner_scoped_when_turn_attempts_are_recorded()
    {
        await using WebApplication app = await CreateAppAsync();
        using HttpClient ownerAClient = app.GetTestClient();
        using HttpClient ownerBClient = app.GetTestClient();

        using HttpRequestMessage ownerASend = new(HttpMethod.Post, "/api/ai/coach")
        {
            Content = JsonContent.Create(new AiConversationTurnRequest(
                Message: "coach me",
                ConversationId: "conv-owner",
                RuntimeFingerprint: "sha256:a",
                CharacterId: "char-a"))
        };
        ownerASend.Headers.Add(OwnerHeaderName, "owner-alpha");
        using HttpResponseMessage ownerASendResponse = await ownerAClient.SendAsync(ownerASend);
        Assert.AreEqual(HttpStatusCode.OK, ownerASendResponse.StatusCode);

        JsonObject ownerACatalog = await GetRequiredJsonObject(ownerAClient, "/api/ai/conversations?conversationId=conv-owner&maxCount=5", "owner-alpha");
        Assert.AreEqual(1, ownerACatalog["totalCount"]?.GetValue<int>());
        Assert.AreEqual("conv-owner", ownerACatalog["items"]?[0]?["conversationId"]?.GetValue<string>());

        using HttpRequestMessage ownerAGet = new(HttpMethod.Get, "/api/ai/conversations/conv-owner");
        ownerAGet.Headers.Add(OwnerHeaderName, "owner-alpha");
        JsonObject ownerAConversation = await ParseRequiredJsonObject(await ownerAClient.SendAsync(ownerAGet));
        Assert.AreEqual("char-a", ownerAConversation["characterId"]?.GetValue<string>());

        JsonObject ownerBCatalog = await GetRequiredJsonObject(ownerBClient, "/api/ai/conversations?conversationId=conv-owner&maxCount=5", "owner-beta");
        Assert.AreEqual(0, ownerBCatalog["totalCount"]?.GetValue<int>());

        using HttpRequestMessage ownerBGet = new(HttpMethod.Get, "/api/ai/conversations/conv-owner");
        ownerBGet.Headers.Add(OwnerHeaderName, "owner-beta");
        using HttpResponseMessage ownerBConversationResponse = await ownerBClient.SendAsync(ownerBGet);
        Assert.AreEqual(HttpStatusCode.NotImplemented, ownerBConversationResponse.StatusCode);
    }

    private static async Task<JsonObject> ParseRequiredJsonObject(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed;
    }

    private sealed class StaticAiRouteBudgetPolicyCatalog(params AiRouteBudgetPolicyDescriptor[] overriddenPolicies) : IAiRouteBudgetPolicyCatalog
    {
        private readonly IReadOnlyList<AiRouteBudgetPolicyDescriptor> _policies = AiGatewayDefaults.CreateRouteBudgets()
            .Select(policy => overriddenPolicies.FirstOrDefault(overridePolicy => string.Equals(overridePolicy.RouteType, policy.RouteType, StringComparison.Ordinal)) ?? policy)
            .ToArray();

        public IReadOnlyList<AiRouteBudgetPolicyDescriptor> ListPolicies()
            => _policies;

        public AiRouteBudgetPolicyDescriptor GetPolicy(string routeType)
            => _policies.Single(policy => string.Equals(policy.RouteType, routeType, StringComparison.Ordinal));
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<(string Owner, string Scope), JsonObject> _store = new();

        public JsonObject Load(string scope) => Load(OwnerScope.LocalSingleUser, scope);

        public JsonObject Load(OwnerScope owner, string scope)
        {
            return _store.TryGetValue((owner.NormalizedValue, scope), out JsonObject? settings)
                ? JsonNode.Parse(settings.ToJsonString())?.AsObject() ?? new JsonObject()
                : new JsonObject();
        }

        public void Save(string scope, JsonObject settings) => Save(OwnerScope.LocalSingleUser, scope, settings);

        public void Save(OwnerScope owner, string scope, JsonObject settings)
        {
            _store[(owner.NormalizedValue, scope)] = JsonNode.Parse(settings.ToJsonString())?.AsObject() ?? new JsonObject();
        }
    }

    private sealed class InMemoryRosterStore : IRosterStore
    {
        private readonly Dictionary<string, List<RosterEntry>> _entriesByOwner = new(StringComparer.Ordinal);

        public IReadOnlyList<RosterEntry> Load() => Load(OwnerScope.LocalSingleUser);

        public IReadOnlyList<RosterEntry> Load(OwnerScope owner)
        {
            return _entriesByOwner.TryGetValue(owner.NormalizedValue, out List<RosterEntry>? entries)
                ? entries.ToArray()
                : Array.Empty<RosterEntry>();
        }

        public IReadOnlyList<RosterEntry> Upsert(RosterEntry entry) => Upsert(OwnerScope.LocalSingleUser, entry);

        public IReadOnlyList<RosterEntry> Upsert(OwnerScope owner, RosterEntry entry)
        {
            if (!_entriesByOwner.TryGetValue(owner.NormalizedValue, out List<RosterEntry>? entries))
            {
                entries = [];
                _entriesByOwner[owner.NormalizedValue] = entries;
            }

            int existingIndex = entries.FindIndex(candidate =>
                string.Equals(candidate.Name, entry.Name, StringComparison.Ordinal)
                && string.Equals(candidate.Alias, entry.Alias, StringComparison.Ordinal));
            if (existingIndex >= 0)
            {
                entries[existingIndex] = entry;
            }
            else
            {
                entries.Add(entry);
            }

            return entries.ToArray();
        }
    }

    private sealed class InMemoryWorkspaceService : IWorkspaceService
    {
        private readonly Dictionary<string, List<WorkspaceListItem>> _workspacesByOwner = new(StringComparer.Ordinal);

        public WorkspaceImportResult Import(WorkspaceImportDocument document) => Import(OwnerScope.LocalSingleUser, document);

        public WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document)
        {
            string ownerKey = owner.NormalizedValue;
            if (!_workspacesByOwner.TryGetValue(ownerKey, out List<WorkspaceListItem>? workspaces))
            {
                workspaces = [];
                _workspacesByOwner[ownerKey] = workspaces;
            }

            CharacterWorkspaceId id = new(Guid.NewGuid().ToString("N"));
            CharacterFileSummary summary = new(
                Name: "Owner Runner",
                Alias: string.Empty,
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "5",
                AppVersion: "5",
                Karma: 0m,
                Nuyen: 0m,
                Created: true);
            workspaces.Add(new WorkspaceListItem(
                Id: id,
                Summary: summary,
                LastUpdatedUtc: DateTimeOffset.UtcNow,
                RulesetId: document.RulesetId));

            return new WorkspaceImportResult(id, summary, document.RulesetId);
        }

        public IReadOnlyList<WorkspaceListItem> List(int? maxCount = null) => List(OwnerScope.LocalSingleUser, maxCount);

        public IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null)
        {
            if (!_workspacesByOwner.TryGetValue(owner.NormalizedValue, out List<WorkspaceListItem>? workspaces))
            {
                return Array.Empty<WorkspaceListItem>();
            }

            return maxCount is > 0
                ? workspaces.Take(maxCount.Value).ToArray()
                : workspaces.ToArray();
        }

        public bool Close(CharacterWorkspaceId id) => Close(OwnerScope.LocalSingleUser, id);

        public bool Close(OwnerScope owner, CharacterWorkspaceId id)
        {
            if (!_workspacesByOwner.TryGetValue(owner.NormalizedValue, out List<WorkspaceListItem>? workspaces))
            {
                return false;
            }

            return workspaces.RemoveAll(workspace => string.Equals(workspace.Id.Value, id.Value, StringComparison.Ordinal)) > 0;
        }

        public object? GetSection(CharacterWorkspaceId id, string sectionId) => throw new NotSupportedException();

        public object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId) => throw new NotSupportedException();

        public CharacterFileSummary? GetSummary(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterValidationResult? Validate(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProfileSection? GetProfile(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProgressSection? GetProgress(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterSkillsSection? GetSkills(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterRulesSection? GetRules(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterBuildSection? GetBuild(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterMovementSection? GetMovement(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => throw new NotSupportedException();

        public CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command) => throw new NotSupportedException();

        public CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id) => throw new NotSupportedException();

        public CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id) => throw new NotSupportedException();
    }

    private sealed class InMemoryHubDraftStore : IHubDraftStore
    {
        private readonly Dictionary<string, List<HubDraftRecord>> _recordsByOwner = new(StringComparer.Ordinal);

        public IReadOnlyList<HubDraftRecord> List(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null)
        {
            return _recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubDraftRecord>? records)
                ? records
                    .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                    .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                    .Where(record => state is null || string.Equals(record.State, state, StringComparison.Ordinal))
                    .ToArray()
                : Array.Empty<HubDraftRecord>();
        }

        public HubDraftRecord? Get(OwnerScope owner, string kind, string projectId, string rulesetId)
        {
            return List(owner, kind, rulesetId).FirstOrDefault(record => string.Equals(record.ProjectId, projectId, StringComparison.Ordinal));
        }

        public HubDraftRecord? Get(OwnerScope owner, string draftId)
        {
            return List(owner).FirstOrDefault(record => string.Equals(record.DraftId, draftId, StringComparison.Ordinal));
        }

        public HubDraftRecord Upsert(OwnerScope owner, HubDraftRecord record)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubDraftRecord>? records))
            {
                records = [];
                _recordsByOwner[owner.NormalizedValue] = records;
            }

            int existingIndex = records.FindIndex(current =>
                string.Equals(current.ProjectKind, record.ProjectKind, StringComparison.Ordinal)
                && string.Equals(current.ProjectId, record.ProjectId, StringComparison.Ordinal)
                && string.Equals(current.RulesetId, record.RulesetId, StringComparison.Ordinal));
            HubDraftRecord normalizedRecord = record with { OwnerId = owner.NormalizedValue };
            if (existingIndex >= 0)
            {
                records[existingIndex] = normalizedRecord;
            }
            else
            {
                records.Add(normalizedRecord);
            }

            return normalizedRecord;
        }

        public bool Delete(OwnerScope owner, string draftId)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubDraftRecord>? records))
            {
                return false;
            }

            return records.RemoveAll(record => string.Equals(record.DraftId, draftId, StringComparison.Ordinal)) > 0;
        }
    }

    private sealed class InMemoryHubPublisherStore : IHubPublisherStore
    {
        private readonly Dictionary<string, List<HubPublisherRecord>> _recordsByOwner = new(StringComparer.Ordinal);

        public IReadOnlyList<HubPublisherRecord> List(OwnerScope owner)
        {
            return _recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubPublisherRecord>? records)
                ? records.ToArray()
                : Array.Empty<HubPublisherRecord>();
        }

        public HubPublisherRecord? Get(OwnerScope owner, string publisherId)
        {
            return List(owner).FirstOrDefault(record => string.Equals(record.PublisherId, publisherId, StringComparison.Ordinal));
        }

        public HubPublisherRecord Upsert(OwnerScope owner, HubPublisherRecord record)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubPublisherRecord>? records))
            {
                records = [];
                _recordsByOwner[owner.NormalizedValue] = records;
            }

            int existingIndex = records.FindIndex(current =>
                string.Equals(current.PublisherId, record.PublisherId, StringComparison.Ordinal));
            HubPublisherRecord normalizedRecord = record with { OwnerId = owner.NormalizedValue };
            if (existingIndex >= 0)
            {
                records[existingIndex] = normalizedRecord;
            }
            else
            {
                records.Add(normalizedRecord);
            }

            return normalizedRecord;
        }
    }

    private sealed class InMemoryHubModerationCaseStore : IHubModerationCaseStore
    {
        private readonly Dictionary<string, List<HubModerationCaseRecord>> _recordsByOwner = new(StringComparer.Ordinal);

        public IReadOnlyList<HubModerationCaseRecord> List(OwnerScope owner, string? kind = null, string? rulesetId = null, string? state = null)
        {
            return _recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubModerationCaseRecord>? records)
                ? records
                    .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                    .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                    .Where(record => state is null || string.Equals(record.State, state, StringComparison.Ordinal))
                    .ToArray()
                : Array.Empty<HubModerationCaseRecord>();
        }

        public HubModerationCaseRecord? Get(OwnerScope owner, string kind, string projectId, string rulesetId)
        {
            return List(owner, kind, rulesetId).FirstOrDefault(record => string.Equals(record.ProjectId, projectId, StringComparison.Ordinal));
        }

        public HubModerationCaseRecord? GetByCaseId(OwnerScope owner, string caseId)
        {
            return List(owner).FirstOrDefault(record => string.Equals(record.CaseId, caseId, StringComparison.Ordinal));
        }

        public HubModerationCaseRecord? GetByDraftId(OwnerScope owner, string draftId)
        {
            return List(owner).FirstOrDefault(record => string.Equals(record.DraftId, draftId, StringComparison.Ordinal));
        }

        public HubModerationCaseRecord Upsert(OwnerScope owner, HubModerationCaseRecord record)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubModerationCaseRecord>? records))
            {
                records = [];
                _recordsByOwner[owner.NormalizedValue] = records;
            }

            int existingIndex = records.FindIndex(current =>
                string.Equals(current.ProjectKind, record.ProjectKind, StringComparison.Ordinal)
                && string.Equals(current.ProjectId, record.ProjectId, StringComparison.Ordinal)
                && string.Equals(current.RulesetId, record.RulesetId, StringComparison.Ordinal));
            HubModerationCaseRecord normalizedRecord = record with { OwnerId = owner.NormalizedValue };
            if (existingIndex >= 0)
            {
                records[existingIndex] = normalizedRecord;
            }
            else
            {
                records.Add(normalizedRecord);
            }

            return normalizedRecord;
        }

        public bool DeleteByDraftId(OwnerScope owner, string draftId)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubModerationCaseRecord>? records))
            {
                return false;
            }

            return records.RemoveAll(record => string.Equals(record.DraftId, draftId, StringComparison.Ordinal)) > 0;
        }
    }

    private sealed class InMemoryHubReviewStore : IHubReviewStore
    {
        private readonly Dictionary<string, List<HubReviewRecord>> _recordsByOwner = new(StringComparer.Ordinal);

        public IReadOnlyList<HubReviewRecord> List(OwnerScope owner, string? kind = null, string? itemId = null, string? rulesetId = null)
        {
            return _recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubReviewRecord>? records)
                ? records
                    .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                    .Where(record => itemId is null || string.Equals(record.ProjectId, itemId, StringComparison.Ordinal))
                    .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                    .ToArray()
                : Array.Empty<HubReviewRecord>();
        }

        public IReadOnlyList<HubReviewRecord> ListAll(string? kind = null, string? itemId = null, string? rulesetId = null)
        {
            return _recordsByOwner.Values
                .SelectMany(static records => records)
                .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                .Where(record => itemId is null || string.Equals(record.ProjectId, itemId, StringComparison.Ordinal))
                .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                .ToArray();
        }

        public HubReviewRecord? Get(OwnerScope owner, string kind, string itemId, string rulesetId)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubReviewRecord>? records))
            {
                return null;
            }

            return records.Find(record =>
                string.Equals(record.ProjectKind, kind, StringComparison.Ordinal)
                && string.Equals(record.ProjectId, itemId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public HubReviewRecord Upsert(OwnerScope owner, HubReviewRecord record)
        {
            if (!_recordsByOwner.TryGetValue(owner.NormalizedValue, out List<HubReviewRecord>? records))
            {
                records = [];
                _recordsByOwner[owner.NormalizedValue] = records;
            }

            int existingIndex = records.FindIndex(current =>
                string.Equals(current.ProjectKind, record.ProjectKind, StringComparison.Ordinal)
                && string.Equals(current.ProjectId, record.ProjectId, StringComparison.Ordinal)
                && string.Equals(current.RulesetId, record.RulesetId, StringComparison.Ordinal));
            HubReviewRecord normalizedRecord = record with { OwnerId = owner.NormalizedValue };
            if (existingIndex >= 0)
            {
                records[existingIndex] = normalizedRecord;
            }
            else
            {
                records.Add(normalizedRecord);
            }

            return normalizedRecord;
        }
    }
}
