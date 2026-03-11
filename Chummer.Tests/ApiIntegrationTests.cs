#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Chummer.Contracts.AI;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Presentation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class ApiIntegrationTests
{
    private static readonly Uri BaseUri = ResolveBaseUri();
    private static readonly string? ApiKey = ResolveApiKey();
    private static readonly string? ExpectedAmendId = ResolveExpectedAmendId();
    private static readonly TimeSpan HttpTimeout = ResolveHttpTimeout();
    private static readonly string[] AllSectionIds =
    {
        "attributes",
        "attributedetails",
        "inventory",
        "profile",
        "progress",
        "rules",
        "build",
        "movement",
        "awakening",
        "gear",
        "weapons",
        "weaponaccessories",
        "armors",
        "armormods",
        "cyberwares",
        "vehicles",
        "vehiclemods",
        "skills",
        "qualities",
        "contacts",
        "spells",
        "powers",
        "complexforms",
        "spirits",
        "foci",
        "aiprograms",
        "martialarts",
        "limitmodifiers",
        "lifestyles",
        "metamagics",
        "arts",
        "initiationgrades",
        "critterpowers",
        "mentorspirits",
        "expenses",
        "sources",
        "gearlocations",
        "armorlocations",
        "weaponlocations",
        "vehiclelocations",
        "calendar",
        "improvements",
        "customdatadirectorynames",
        "drugs"
    };

    [TestMethod]
    public async Task Info_endpoint_reports_chummer_service()
    {
        using var client = CreateClient();

        JsonObject info = await GetRequiredJsonObject(client, "/api/info");

        Assert.AreEqual("Chummer", info["service"]?.GetValue<string>());
        Assert.AreEqual("running", info["status"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Info_endpoint_reports_content_overlay_metadata()
    {
        using var client = CreateClient();

        JsonObject info = await GetRequiredJsonObject(client, "/api/info");

        Assert.IsInstanceOfType<JsonObject>(info["content"]);
        JsonObject content = (JsonObject)info["content"]!;
        Assert.IsNotNull(content["baseDataPath"]);
        Assert.IsNotNull(content["baseLanguagePath"]);
        Assert.IsInstanceOfType<JsonArray>(content["overlays"]);
    }

    [TestMethod]
    public async Task Content_overlays_endpoint_reports_catalog_and_expected_overlay_when_configured()
    {
        using var client = CreateClient();

        JsonObject overlays = await GetRequiredJsonObject(client, "/api/content/overlays");
        Assert.IsNotNull(overlays["baseDataPath"]);
        Assert.IsNotNull(overlays["baseLanguagePath"]);
        Assert.IsInstanceOfType<JsonArray>(overlays["overlays"]);

        if (!string.IsNullOrWhiteSpace(ExpectedAmendId))
        {
            JsonArray items = (JsonArray)overlays["overlays"]!;
            bool found = items.OfType<JsonObject>()
                .Any(item => string.Equals(item["id"]?.GetValue<string>(), ExpectedAmendId, StringComparison.Ordinal));
            Assert.IsTrue(found, $"Expected overlay id '{ExpectedAmendId}' was not found.");
        }
    }

    [TestMethod]
    public async Task Hub_search_endpoint_returns_mixed_catalog_items_for_rulepacks_profiles_and_runtime_locks()
    {
        using var client = CreateClient();
        BrowseQuery query = new(
            QueryText: string.Empty,
            FacetSelections: new Dictionary<string, IReadOnlyList<string>>(),
            SortId: "title");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/hub/search", query);
        response.EnsureSuccessStatusCode();
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.IsNotNull(payload["totalCount"]);
        Assert.IsInstanceOfType<JsonArray>(payload["items"]);
        Assert.IsInstanceOfType<JsonArray>(payload["facets"]);
        Assert.IsInstanceOfType<JsonArray>(payload["sorts"]);
    }

    [TestMethod]
    public async Task Hub_project_detail_endpoint_returns_registered_profile_projection()
    {
        using var client = CreateClient();

        JsonObject payload = await GetRequiredJsonObject(client, "/api/hub/projects/ruleprofile/official.sr5.core?ruleset=sr5");

        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["summary"]?["kind"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", payload["summary"]?["itemId"]?.GetValue<string>());
        Assert.IsNotNull(payload["runtimeFingerprint"]);
        Assert.IsInstanceOfType<JsonArray>(payload["facts"]);
        Assert.IsInstanceOfType<JsonArray>(payload["actions"]);
    }

    [TestMethod]
    public async Task Hub_project_detail_endpoint_surfaces_owner_review_summary_when_present()
    {
        using var client = CreateClient();

        await PutRequiredJsonObject(client, "/api/hub/reviews/ruleprofile/official.sr5.core", new JsonObject
        {
            ["rulesetId"] = RulesetDefaults.Sr5,
            ["recommendationState"] = HubRecommendationStates.Recommended,
            ["stars"] = 5,
            ["reviewText"] = "Stable runtime",
            ["usedAtTable"] = true
        });

        JsonObject payload = await GetRequiredJsonObject(client, "/api/hub/projects/ruleprofile/official.sr5.core?ruleset=sr5");

        Assert.AreEqual(HubRecommendationStates.Recommended, payload["ownerReview"]?["recommendationState"]?.GetValue<string>());
        Assert.AreEqual(5, payload["ownerReview"]?["stars"]?.GetValue<int>());
        Assert.IsTrue(payload["ownerReview"]?["usedAtTable"]?.GetValue<bool>() ?? false);
    }

    [TestMethod]
    public async Task Hub_project_detail_endpoint_returns_not_found_for_unknown_project()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/projects/ruleprofile/missing-profile?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["kind"]?.GetValue<string>());
        Assert.AreEqual("missing-profile", payload["itemId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_project_detail_endpoint_returns_bad_request_for_unknown_project_kind()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/projects/not-a-kind/missing-profile?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
        Assert.IsTrue(payload["allowedKinds"]?.AsArray().OfType<JsonValue>()
            .Select(value => value.GetValue<string>())
            .Contains(HubCatalogItemKinds.RuleProfile, StringComparer.Ordinal) ?? false);
    }

    [TestMethod]
    public async Task Hub_project_install_preview_endpoint_returns_registered_profile_preview()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/hub/projects/ruleprofile/official.sr5.core/install-preview?ruleset=sr5", target);
        response.EnsureSuccessStatusCode();
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["kind"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", payload["itemId"]?.GetValue<string>());
        Assert.AreEqual("ready", payload["state"]?.GetValue<string>());
        Assert.IsNotNull(payload["runtimeFingerprint"]);
        Assert.IsInstanceOfType<JsonArray>(payload["changes"]);
        Assert.IsInstanceOfType<JsonArray>(payload["diagnostics"]);
    }

    [TestMethod]
    public async Task Hub_project_install_preview_endpoint_returns_not_found_for_unknown_project()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/hub/projects/ruleprofile/missing-profile/install-preview?ruleset=sr5", target);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["kind"]?.GetValue<string>());
        Assert.AreEqual("missing-profile", payload["itemId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_project_install_preview_endpoint_returns_bad_request_for_unknown_project_kind()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/hub/projects/not-a-kind/missing-profile/install-preview?ruleset=sr5", target);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_project_compatibility_endpoint_returns_registered_profile_matrix()
    {
        using var client = CreateClient();

        JsonObject payload = await GetRequiredJsonObject(client, "/api/hub/projects/ruleprofile/official.sr5.core/compatibility?ruleset=sr5");

        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["kind"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", payload["itemId"]?.GetValue<string>());
        Assert.IsInstanceOfType<JsonArray>(payload["rows"]);
    }

    [TestMethod]
    public async Task Hub_project_compatibility_endpoint_returns_bad_request_for_unknown_project_kind()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/projects/not-a-kind/missing-profile/compatibility?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publisher_endpoints_upsert_and_list_owner_profiles()
    {
        using var client = CreateClient();
        string publisherId = $"shadowops-{Guid.NewGuid():N}";

        JsonObject updated = await PutRequiredJsonObject(client, $"/api/hub/publishers/{publisherId}", new JsonObject
        {
            ["displayName"] = "ShadowOps",
            ["slug"] = "shadowops",
            ["description"] = "Campaign runtime publisher",
            ["websiteUrl"] = "https://example.invalid/shadowops"
        });
        JsonObject detail = await GetRequiredJsonObject(client, $"/api/hub/publishers/{publisherId}");
        JsonObject list = await GetRequiredJsonObject(client, "/api/hub/publishers");

        Assert.AreEqual(publisherId, updated["publisherId"]?.GetValue<string>());
        Assert.AreEqual("ShadowOps", detail["displayName"]?.GetValue<string>());
        Assert.AreEqual(HubPublisherVerificationStates.Unverified, detail["verificationState"]?.GetValue<string>());
        bool found = list["items"]?.AsArray().OfType<JsonObject>()
            .Any(item => string.Equals(item["publisherId"]?.GetValue<string>(), publisherId, StringComparison.Ordinal))
            ?? false;
        Assert.IsTrue(found, $"Expected publisher catalog to include '{publisherId}'.");
    }

    [TestMethod]
    public async Task Hub_review_endpoints_upsert_and_list_owner_reviews()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        JsonObject review = await PutRequiredJsonObject(client, $"/api/hub/reviews/rulepack/{projectId}", new JsonObject
        {
            ["rulesetId"] = RulesetDefaults.Sr5,
            ["recommendationState"] = HubRecommendationStates.Recommended,
            ["stars"] = 5,
            ["reviewText"] = "Great pack",
            ["usedAtTable"] = true
        });
        JsonObject list = await GetRequiredJsonObject(client, $"/api/hub/reviews?kind=rulepack&itemId={projectId}&ruleset=sr5");

        Assert.AreEqual(HubRecommendationStates.Recommended, review["recommendationState"]?.GetValue<string>());
        Assert.AreEqual(5, review["stars"]?.GetValue<int>());
        bool found = list["items"]?.AsArray().OfType<JsonObject>()
            .Any(item => string.Equals(item["projectId"]?.GetValue<string>(), projectId, StringComparison.Ordinal))
            ?? false;
        Assert.IsTrue(found, $"Expected hub review catalog to include '{projectId}'.");
    }

    [TestMethod]
    public async Task Hub_review_endpoint_returns_bad_request_for_unknown_project_kind()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage response = await client.PutAsJsonAsync($"/api/hub/reviews/not-a-kind/{projectId}", new JsonObject
        {
            ["rulesetId"] = RulesetDefaults.Sr5,
            ["recommendationState"] = HubRecommendationStates.Recommended
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_draft_endpoint_persists_owner_draft_receipt()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";
        HubPublishDraftRequest request = new(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps",
            Summary: "Street-level runtime",
            Description: "Campaign-specific SR5 publication draft.");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/hub/publish/drafts", request);
        response.EnsureSuccessStatusCode();
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.IsNotNull(payload["draftId"]?.GetValue<string>());
        Assert.AreEqual(HubCatalogItemKinds.RulePack, payload["projectKind"]?.GetValue<string>());
        Assert.AreEqual(projectId, payload["projectId"]?.GetValue<string>());
        Assert.AreEqual("Street-level runtime", payload["summary"]?.GetValue<string>());
        Assert.AreEqual(HubPublicationStates.Draft, payload["state"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_drafts_endpoint_returns_bad_request_for_unknown_kind_filter()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/publish/drafts?kind=not-a-kind&ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_and_moderation_endpoints_preserve_bound_publisher_identity()
    {
        using var client = CreateClient();
        string publisherId = $"shadowops-{Guid.NewGuid():N}";
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        await PutRequiredJsonObject(client, $"/api/hub/publishers/{publisherId}", new JsonObject
        {
            ["displayName"] = "ShadowOps",
            ["slug"] = publisherId,
            ["description"] = "Campaign runtime publisher"
        });

        JsonObject created = await PostRequiredJsonObject(
            client,
            "/api/hub/publish/drafts",
            new JsonObject
            {
                ["projectKind"] = HubCatalogItemKinds.RulePack,
                ["projectId"] = projectId,
                ["rulesetId"] = RulesetDefaults.Sr5,
                ["title"] = "Campaign ShadowOps",
                ["publisherId"] = publisherId
            });
        string draftId = created["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject detail = await GetRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}");
        JsonObject submission = await PostRequiredJsonObject(
            client,
            $"/api/hub/publish/rulepack/{projectId}/submit?ruleset=sr5",
            new JsonObject
            {
                ["notes"] = "ready for moderation"
            });
        string caseId = submission["caseId"]?.GetValue<string>() ?? string.Empty;

        JsonObject queue = await GetRequiredJsonObject(client, "/api/hub/moderation/queue?state=pending-review");
        JsonObject approved = await PostRequiredJsonObject(
            client,
            $"/api/hub/moderation/queue/{caseId}/approve",
            new JsonObject
            {
                ["notes"] = "approved"
            });

        bool queued = queue["items"]?.AsArray().OfType<JsonObject>()
            .Any(item =>
                string.Equals(item["caseId"]?.GetValue<string>(), caseId, StringComparison.Ordinal)
                && string.Equals(item["publisherId"]?.GetValue<string>(), publisherId, StringComparison.Ordinal))
            ?? false;

        Assert.AreEqual(publisherId, created["publisherId"]?.GetValue<string>());
        Assert.AreEqual(publisherId, detail["draft"]?["publisherId"]?.GetValue<string>());
        Assert.AreEqual(publisherId, submission["publisherId"]?.GetValue<string>());
        Assert.IsTrue(queued, $"Expected moderation queue to include publisher '{publisherId}'.");
        Assert.AreEqual(publisherId, approved["publisherId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_drafts_endpoint_lists_persisted_owner_drafts()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps",
            Summary: "Street-level runtime",
            Description: "Campaign-specific SR5 publication draft."));
        createResponse.EnsureSuccessStatusCode();

        JsonObject payload = await GetRequiredJsonObject(client, "/api/hub/publish/drafts?kind=rulepack&ruleset=sr5");
        JsonArray items = (JsonArray)payload["items"]!;
        bool found = items.OfType<JsonObject>()
            .Any(item => string.Equals(item["projectId"]?.GetValue<string>(), projectId, StringComparison.Ordinal));

        Assert.IsTrue(found, $"Expected owner draft '{projectId}' to be listed.");
    }

    [TestMethod]
    public async Task Hub_publish_draft_detail_endpoint_returns_persisted_draft_projection()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps",
            Summary: "Street-level runtime",
            Description: "Campaign-specific SR5 publication draft."));
        createResponse.EnsureSuccessStatusCode();
        JsonObject created = await ParseRequiredJsonObject(createResponse);
        string draftId = created["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject detail = await GetRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}");

        Assert.AreEqual(draftId, detail["draft"]?["draftId"]?.GetValue<string>());
        Assert.AreEqual(projectId, detail["draft"]?["projectId"]?.GetValue<string>());
        Assert.AreEqual("Street-level runtime", detail["draft"]?["summary"]?.GetValue<string>());
        Assert.AreEqual("Campaign-specific SR5 publication draft.", detail["description"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_draft_update_endpoint_updates_persisted_draft_metadata()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps"));
        createResponse.EnsureSuccessStatusCode();
        JsonObject created = await ParseRequiredJsonObject(createResponse);
        string draftId = created["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject updated = await PutRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}", new JsonObject
        {
            ["title"] = "Campaign ShadowOps Updated",
            ["summary"] = "Street-level runtime",
            ["description"] = "Campaign-specific SR5 publication draft."
        });
        JsonObject detail = await GetRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}");

        Assert.AreEqual("Campaign ShadowOps Updated", updated["title"]?.GetValue<string>());
        Assert.AreEqual("Street-level runtime", updated["summary"]?.GetValue<string>());
        Assert.AreEqual("Campaign ShadowOps Updated", detail["draft"]?["title"]?.GetValue<string>());
        Assert.AreEqual("Street-level runtime", detail["draft"]?["summary"]?.GetValue<string>());
        Assert.AreEqual("Campaign-specific SR5 publication draft.", detail["description"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_publish_draft_archive_and_delete_endpoints_manage_lifecycle_state()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps"));
        createResponse.EnsureSuccessStatusCode();
        JsonObject created = await ParseRequiredJsonObject(createResponse);
        string draftId = created["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject archived = await PostRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}/archive", new JsonObject());
        Assert.AreEqual(HubPublicationStates.Archived, archived["state"]?.GetValue<string>());

        using HttpResponseMessage deleteResponse = await client.DeleteAsync($"/api/hub/publish/drafts/{draftId}");
        Assert.AreEqual(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using HttpResponseMessage detailResponse = await client.GetAsync($"/api/hub/publish/drafts/{draftId}");
        Assert.AreEqual(HttpStatusCode.NotFound, detailResponse.StatusCode);
    }

    [TestMethod]
    public async Task Hub_publish_submit_endpoint_persists_submission_receipt_and_queue_entry()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";
        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps"));
        createResponse.EnsureSuccessStatusCode();
        HubSubmitProjectRequest request = new("submit for moderation");

        using HttpResponseMessage response = await client.PostAsJsonAsync($"/api/hub/publish/rulepack/{projectId}/submit?ruleset=sr5", request);
        response.EnsureSuccessStatusCode();
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.IsNotNull(payload["draftId"]?.GetValue<string>());
        Assert.IsNotNull(payload["caseId"]?.GetValue<string>());
        Assert.AreEqual(HubCatalogItemKinds.RulePack, payload["projectKind"]?.GetValue<string>());
        Assert.AreEqual(projectId, payload["projectId"]?.GetValue<string>());
        Assert.AreEqual(HubPublicationStates.Submitted, payload["state"]?.GetValue<string>());
        Assert.AreEqual(HubModerationStates.PendingReview, payload["reviewState"]?.GetValue<string>());

        JsonObject queue = await GetRequiredJsonObject(client, "/api/hub/moderation/queue?state=pending-review");
        JsonArray items = (JsonArray)queue["items"]!;
        bool found = items.OfType<JsonObject>()
            .Any(item => string.Equals(item["projectId"]?.GetValue<string>(), projectId, StringComparison.Ordinal));
        Assert.IsTrue(found, $"Expected moderation queue to include '{projectId}'.");
    }

    [TestMethod]
    public async Task Hub_publish_submit_endpoint_returns_bad_request_for_unknown_project_kind()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/hub/publish/not-a-kind/{projectId}/submit?ruleset=sr5",
            new HubSubmitProjectRequest("ready for moderation"));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.AreEqual("hub_project_kind_invalid", payload["error"]?.GetValue<string>());
        Assert.AreEqual("not-a-kind", payload["kind"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Hub_moderation_queue_endpoint_returns_queue_payload()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/moderation/queue");
        response.EnsureSuccessStatusCode();
        JsonObject payload = await ParseRequiredJsonObject(response);

        Assert.IsInstanceOfType<JsonArray>(payload["items"]);
    }

    [TestMethod]
    public async Task Hub_moderation_action_endpoints_update_owner_case_state()
    {
        using var client = CreateClient();
        string projectId = $"campaign.shadowops.{Guid.NewGuid():N}";

        using HttpResponseMessage createResponse = await client.PostAsJsonAsync("/api/hub/publish/drafts", new HubPublishDraftRequest(
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: projectId,
            RulesetId: RulesetDefaults.Sr5,
            Title: "Campaign ShadowOps Moderation"));
        createResponse.EnsureSuccessStatusCode();

        JsonObject submission = await PostRequiredJsonObject(
            client,
            $"/api/hub/publish/rulepack/{projectId}/submit?ruleset=sr5",
            new JsonObject
            {
                ["notes"] = "ready for approval"
            });
        string caseId = submission["caseId"]?.GetValue<string>() ?? string.Empty;
        string draftId = submission["draftId"]?.GetValue<string>() ?? string.Empty;

        JsonObject approved = await PostRequiredJsonObject(
            client,
            $"/api/hub/moderation/queue/{caseId}/approve",
            new JsonObject
            {
                ["notes"] = "approved"
            });
        Assert.AreEqual(HubModerationStates.Approved, approved["state"]?.GetValue<string>());
        Assert.AreEqual("approved", approved["notes"]?.GetValue<string>());

        JsonObject detail = await GetRequiredJsonObject(client, $"/api/hub/publish/drafts/{draftId}");
        Assert.AreEqual(HubModerationStates.Approved, detail["moderation"]?["state"]?.GetValue<string>());
        Assert.AreEqual("approved", detail["latestModerationNotes"]?.GetValue<string>());

        JsonObject rejected = await PostRequiredJsonObject(
            client,
            $"/api/hub/moderation/queue/{caseId}/reject",
            new JsonObject
            {
                ["notes"] = "needs more work"
            });
        Assert.AreEqual(HubModerationStates.Rejected, rejected["state"]?.GetValue<string>());
        Assert.AreEqual("needs more work", rejected["notes"]?.GetValue<string>());

        JsonObject rejectedQueue = await GetRequiredJsonObject(client, "/api/hub/moderation/queue?state=rejected");
        bool found = rejectedQueue["items"]?.AsArray().OfType<JsonObject>()
            .Any(item => string.Equals(item["caseId"]?.GetValue<string>(), caseId, StringComparison.Ordinal))
            ?? false;
        Assert.IsTrue(found, $"Expected rejected moderation queue to include '{caseId}'.");
    }

    [TestMethod]
    public async Task Hub_project_compatibility_endpoint_returns_not_found_for_unknown_project()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/hub/projects/ruleprofile/missing-profile/compatibility?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("hub_project_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, payload["kind"]?.GetValue<string>());
        Assert.AreEqual("missing-profile", payload["itemId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Buildkits_endpoint_reports_registry_entries_for_registered_rulesets()
    {
        using var client = CreateClient();

        JsonObject buildkits = await GetRequiredJsonObject(client, "/api/buildkits?ruleset=sr5");
        Assert.IsNotNull(buildkits["count"]);
        Assert.IsInstanceOfType<JsonArray>(buildkits["entries"]);
    }

    [TestMethod]
    public async Task Buildkit_detail_endpoint_returns_not_found_for_unknown_buildkit()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/buildkits/missing-buildkit?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("buildkit_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual("missing-buildkit", payload["buildKitId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Rulepacks_endpoint_reports_registry_entries_and_expected_overlay_pack_when_configured()
    {
        using var client = CreateClient();

        JsonObject rulepacks = await GetRequiredJsonObject(client, "/api/rulepacks?ruleset=sr5");
        Assert.IsNotNull(rulepacks["count"]);
        Assert.IsInstanceOfType<JsonArray>(rulepacks["entries"]);

        if (!string.IsNullOrWhiteSpace(ExpectedAmendId))
        {
            JsonArray items = (JsonArray)rulepacks["entries"]!;
            bool found = items.OfType<JsonObject>()
                .Any(item => string.Equals(item["manifest"]?["packId"]?.GetValue<string>(), ExpectedAmendId, StringComparison.Ordinal));
            Assert.IsTrue(found, $"Expected rulepack id '{ExpectedAmendId}' was not found.");
        }
    }

    [TestMethod]
    public async Task Rulepack_detail_endpoint_returns_not_found_for_unknown_pack()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/rulepacks/missing-pack?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("rulepack_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual("missing-pack", payload["packId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Rulepack_install_endpoints_return_not_found_for_unknown_pack()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage previewResponse = await client.PostAsJsonAsync("/api/rulepacks/missing-pack/install-preview?ruleset=sr5", target);
        Assert.AreEqual(HttpStatusCode.NotFound, previewResponse.StatusCode);
        JsonNode previewParsed = JsonNode.Parse(await previewResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(previewParsed);
        JsonObject previewPayload = (JsonObject)previewParsed!;
        Assert.AreEqual("rulepack_not_found", previewPayload["error"]?.GetValue<string>());

        using HttpResponseMessage installResponse = await client.PostAsJsonAsync("/api/rulepacks/missing-pack/install?ruleset=sr5", target);
        Assert.AreEqual(HttpStatusCode.NotFound, installResponse.StatusCode);
        JsonNode installParsed = JsonNode.Parse(await installResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(installParsed);
        JsonObject installPayload = (JsonObject)installParsed!;
        Assert.AreEqual("rulepack_not_found", installPayload["error"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Profiles_endpoint_reports_curated_install_targets_for_registered_rulesets()
    {
        using var client = CreateClient();

        JsonObject profiles = await GetRequiredJsonObject(client, "/api/profiles?ruleset=sr5");
        Assert.IsNotNull(profiles["count"]);
        Assert.IsInstanceOfType<JsonArray>(profiles["entries"]);

        JsonArray items = (JsonArray)profiles["entries"]!;
        bool found = items.OfType<JsonObject>()
            .Any(item => string.Equals(item["manifest"]?["profileId"]?.GetValue<string>(), "official.sr5.core", StringComparison.Ordinal));
        Assert.IsTrue(found, "Expected default RuleProfile id 'official.sr5.core' was not found.");
    }

    [TestMethod]
    public async Task Profile_detail_endpoint_returns_not_found_for_unknown_profile()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/profiles/missing-profile?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("ruleprofile_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual("missing-profile", payload["profileId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Profile_preview_endpoint_returns_runtime_lock_preview_for_registered_profile()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/profiles/official.sr5.core/preview?ruleset=sr5", target);
        response.EnsureSuccessStatusCode();
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("official.sr5.core", payload["profileId"]?.GetValue<string>());
        Assert.AreEqual("workspace-1", payload["target"]?["targetId"]?.GetValue<string>());
        Assert.IsNotNull(payload["runtimeLock"]?["runtimeFingerprint"]);
    }

    [TestMethod]
    public async Task Profile_apply_endpoint_returns_applied_receipt_for_registered_profile()
    {
        using var client = CreateClient();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Character, "character-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/profiles/official.sr5.core/apply?ruleset=sr5", target);
        response.EnsureSuccessStatusCode();
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual(RuleProfileApplyOutcomes.Applied, payload["outcome"]?.GetValue<string>());
        Assert.IsNull(payload["deferredReason"]);
        Assert.AreEqual("character-1", payload["target"]?["targetId"]?.GetValue<string>());
        Assert.AreEqual("character-1", payload["installReceipt"]?["targetId"]?.GetValue<string>());
        Assert.IsNotNull(payload["installReceipt"]?["runtimeLock"]?["runtimeFingerprint"]);
    }

    [TestMethod]
    public async Task Runtime_profile_endpoint_returns_runtime_inspector_projection_for_registered_profile()
    {
        using var client = CreateClient();

        JsonObject payload = await GetRequiredJsonObject(client, "/api/runtime/profiles/official.sr5.core?ruleset=sr5");

        Assert.AreEqual("runtime-lock", payload["targetKind"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", payload["targetId"]?.GetValue<string>());
        Assert.IsInstanceOfType<JsonObject>(payload["runtimeLock"]);
        Assert.IsInstanceOfType<JsonArray>(payload["resolvedRulePacks"]);
        Assert.IsInstanceOfType<JsonArray>(payload["compatibilityDiagnostics"]);
    }

    [TestMethod]
    public async Task Runtime_locks_endpoint_returns_runtime_lock_catalog_for_registered_profiles()
    {
        using var client = CreateClient();

        JsonObject payload = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");

        Assert.IsNotNull(payload["count"]);
        Assert.IsInstanceOfType<JsonArray>(payload["entries"]);
    }

    [TestMethod]
    public async Task Runtime_lock_detail_endpoint_returns_not_found_for_unknown_lock()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/runtime/locks/missing-lock?ruleset=sr5");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("runtime_lock_not_found", payload["error"]?.GetValue<string>());
        Assert.AreEqual("missing-lock", payload["lockId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Runtime_lock_install_endpoints_preview_and_persist_owner_install_state()
    {
        using var client = CreateClient();
        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray items = (JsonArray)runtimeLocks["entries"]!;
        JsonObject first = items.OfType<JsonObject>().First();
        string lockId = first["lockId"]!.GetValue<string>();
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage previewResponse = await client.PostAsJsonAsync($"/api/runtime/locks/{lockId}/install-preview?ruleset=sr5", target);
        previewResponse.EnsureSuccessStatusCode();
        JsonNode previewParsed = JsonNode.Parse(await previewResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(previewParsed);
        JsonObject previewPayload = (JsonObject)previewParsed!;
        Assert.AreEqual(lockId, previewPayload["lockId"]?.GetValue<string>());
        Assert.IsInstanceOfType<JsonArray>(previewPayload["changes"]);

        using HttpResponseMessage installResponse = await client.PostAsJsonAsync($"/api/runtime/locks/{lockId}/install?ruleset=sr5", target);
        installResponse.EnsureSuccessStatusCode();
        JsonNode installParsed = JsonNode.Parse(await installResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(installParsed);
        JsonObject installPayload = (JsonObject)installParsed!;
        string? outcome = installPayload["outcome"]?.GetValue<string>();
        Assert.IsTrue(
            string.Equals(outcome, RuntimeLockInstallOutcomes.Installed, StringComparison.Ordinal)
            || string.Equals(outcome, RuntimeLockInstallOutcomes.Updated, StringComparison.Ordinal)
            || string.Equals(outcome, RuntimeLockInstallOutcomes.Unchanged, StringComparison.Ordinal),
            $"Unexpected runtime lock install outcome '{outcome}'.");

        JsonObject detailPayload = await GetRequiredJsonObject(client, $"/api/runtime/locks/{lockId}?ruleset=sr5");
        Assert.AreEqual(RuntimeLockCatalogKinds.Saved, detailPayload["catalogKind"]?.GetValue<string>());
        Assert.AreEqual(ArtifactInstallStates.Pinned, detailPayload["install"]?["state"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Runtime_lock_put_endpoint_persists_owner_runtime_lock_entry()
    {
        using var client = CreateClient();
        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray items = (JsonArray)runtimeLocks["entries"]!;
        JsonObject first = items.OfType<JsonObject>().First();
        string lockId = first["lockId"]!.GetValue<string>();
        JsonObject runtimeLock = (JsonObject)first["runtimeLock"]!;

        using HttpResponseMessage saveResponse = await client.PutAsJsonAsync($"/api/runtime/locks/{lockId}", new
        {
            title = "Saved Runtime Lock",
            runtimeLock,
            visibility = ArtifactVisibilityModes.Private,
            description = "Owner-persisted runtime lock."
        });
        saveResponse.EnsureSuccessStatusCode();

        JsonObject detailPayload = await GetRequiredJsonObject(client, $"/api/runtime/locks/{lockId}?ruleset=sr5");
        Assert.AreEqual(RuntimeLockCatalogKinds.Saved, detailPayload["catalogKind"]?.GetValue<string>());
        Assert.AreEqual(ArtifactVisibilityModes.Private, detailPayload["visibility"]?.GetValue<string>());
        Assert.AreEqual("Saved Runtime Lock", detailPayload["title"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Health_endpoint_reports_ok()
    {
        using var client = CreateClient();

        JsonObject health = await GetRequiredJsonObject(client, "/api/health");

        Assert.IsTrue(health["ok"]?.GetValue<bool>() ?? false);
    }

    [TestMethod]
    public async Task Root_endpoint_reports_api_service_document()
    {
        using var client = CreateClient();

        JsonObject payload = await GetRequiredJsonObject(client, "/");

        Assert.AreEqual("Chummer.Api", payload["service"]?.GetValue<string>());
        Assert.AreEqual("running", payload["status"]?.GetValue<string>());
        Assert.IsTrue(payload["docs"] is JsonArray);
    }

    [TestMethod]
    public async Task Public_endpoints_remain_accessible_without_api_key_header_when_auth_is_enabled()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return;

        using var client = CreateClient(includeApiKey: false);

        JsonObject health = await GetRequiredJsonObject(client, "/api/health");
        Assert.IsTrue(health["ok"]?.GetValue<bool>() ?? false);

        JsonObject info = await GetRequiredJsonObject(client, "/api/info");
        Assert.AreEqual("Chummer", info["service"]?.GetValue<string>());

        BrowseQuery hubQuery = new(
            QueryText: string.Empty,
            FacetSelections: new Dictionary<string, IReadOnlyList<string>>(),
            SortId: "title");
        using HttpResponseMessage hubResponse = await client.PostAsJsonAsync("/api/hub/search", hubQuery);
        hubResponse.EnsureSuccessStatusCode();

        JsonObject rulepacks = await GetRequiredJsonObject(client, "/api/rulepacks?ruleset=sr5");
        Assert.IsNotNull(rulepacks["count"]);

        JsonObject profiles = await GetRequiredJsonObject(client, "/api/profiles?ruleset=sr5");
        Assert.IsNotNull(profiles["count"]);

        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");
        using HttpResponseMessage previewResponse = await client.PostAsJsonAsync("/api/profiles/official.sr5.core/preview?ruleset=sr5", target);
        previewResponse.EnsureSuccessStatusCode();

        JsonObject runtime = await GetRequiredJsonObject(client, "/api/runtime/profiles/official.sr5.core?ruleset=sr5");
        Assert.AreEqual("official.sr5.core", runtime["targetId"]?.GetValue<string>());

        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        Assert.IsNotNull(runtimeLocks["count"]);

        using HttpResponseMessage hubPreviewResponse = await client.PostAsJsonAsync(
            "/api/hub/projects/ruleprofile/official.sr5.core/install-preview?ruleset=sr5",
            target);
        hubPreviewResponse.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task Ai_gateway_status_and_provider_catalog_are_exposed_through_protected_api_surface()
    {
        using var client = CreateClient();

        JsonObject status = await GetRequiredJsonObject(client, "/api/ai/status");
        using HttpResponseMessage providersResponse = await client.GetAsync("/api/ai/providers");
        string providersContent = await providersResponse.Content.ReadAsStringAsync();
        Assert.IsTrue(providersResponse.IsSuccessStatusCode, $"GET /api/ai/providers failed with {(int)providersResponse.StatusCode}: {providersContent}");
        JsonArray providers = ParseRequiredJsonArray(providersContent);
        using HttpResponseMessage toolsResponse = await client.GetAsync("/api/ai/tools");
        toolsResponse.EnsureSuccessStatusCode();
        JsonArray tools = ParseRequiredJsonArray(await toolsResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage corporaResponse = await client.GetAsync("/api/ai/retrieval-corpora");
        corporaResponse.EnsureSuccessStatusCode();
        JsonArray corpora = ParseRequiredJsonArray(await corporaResponse.Content.ReadAsStringAsync());

        Assert.AreEqual("scaffolded", status["status"]?.GetValue<string>());
        Assert.IsTrue(status["routes"]?.AsArray().Any(node => string.Equals(node?.GetValue<string>(), AiRouteTypes.Coach, StringComparison.Ordinal)) ?? false);
        Assert.IsTrue(status["routes"]?.AsArray().Any(node => string.Equals(node?.GetValue<string>(), AiRouteTypes.Docs, StringComparison.Ordinal)) ?? false);
        Assert.IsTrue(status["tools"]?.AsArray().Any(node => string.Equals(node?["toolId"]?.GetValue<string>(), AiToolIds.ExplainValue, StringComparison.Ordinal)) ?? false);
        Assert.IsTrue(status["providers"]?.AsArray().All(node => node?["adapterRegistered"]?.GetValue<bool>() ?? false) ?? false);
        Assert.AreEqual(AiPersonaIds.DeckerContact, status["defaultPersonaId"]?.GetValue<string>());
        Assert.IsTrue(status["personas"]?.AsArray().Any(node => string.Equals(node?["personaId"]?.GetValue<string>(), AiPersonaIds.DeckerContact, StringComparison.Ordinal)) ?? false);
        Assert.IsTrue(providers.Any(node => string.Equals(node?["providerId"]?.GetValue<string>(), AiProviderIds.AiMagicx, StringComparison.Ordinal)));
        Assert.IsTrue(providers.Any(node => string.Equals(node?["providerId"]?.GetValue<string>(), AiProviderIds.OneMinAi, StringComparison.Ordinal)));
        Assert.IsTrue(providers.All(node => node?["adapterRegistered"]?.GetValue<bool>() ?? false));
        Assert.IsTrue(tools.Any(node => string.Equals(node?["toolId"]?.GetValue<string>(), AiToolIds.SearchBuildIdeas, StringComparison.Ordinal)));
        Assert.IsTrue(tools.Any(node => string.Equals(node?["toolId"]?.GetValue<string>(), AiToolIds.SearchHubProjects, StringComparison.Ordinal)));
        Assert.IsTrue(tools.Any(node => string.Equals(node?["toolId"]?.GetValue<string>(), AiToolIds.CreateApplyPreview, StringComparison.Ordinal)));
        Assert.IsTrue(corpora.Any(node => string.Equals(node?["corpusId"]?.GetValue<string>(), "runtime", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Ai_gateway_route_policy_and_budget_catalogs_are_exposed_through_protected_api_surface()
    {
        using var client = CreateClient();

        using HttpResponseMessage routePolicyResponse = await client.GetAsync("/api/ai/route-policies");
        routePolicyResponse.EnsureSuccessStatusCode();
        JsonArray routePolicies = ParseRequiredJsonArray(await routePolicyResponse.Content.ReadAsStringAsync());
        Assert.IsTrue(routePolicies.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Coach, StringComparison.Ordinal)
            && string.Equals(policy["primaryProviderId"]?.GetValue<string>(), AiProviderIds.AiMagicx, StringComparison.Ordinal)
            && string.Equals(policy["routeClassId"]?.GetValue<string>(), AiRouteClassIds.GroundedRulesChat, StringComparison.Ordinal)
            && string.Equals(policy["personaId"]?.GetValue<string>(), AiPersonaIds.DeckerContact, StringComparison.Ordinal)));
        Assert.IsTrue(routePolicies.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Coach, StringComparison.Ordinal)
            && policy["allowedTools"] is JsonArray tools
            && tools.OfType<JsonObject>().Any(tool => string.Equals(tool["toolId"]?.GetValue<string>(), AiToolIds.CreateApplyPreview, StringComparison.Ordinal))));
        Assert.IsTrue(routePolicies.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Chat, StringComparison.Ordinal)
            && string.Equals(policy["primaryProviderId"]?.GetValue<string>(), AiProviderIds.OneMinAi, StringComparison.Ordinal)));
        Assert.IsTrue(routePolicies.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Docs, StringComparison.Ordinal)
            && string.Equals(policy["primaryProviderId"]?.GetValue<string>(), AiProviderIds.OneMinAi, StringComparison.Ordinal)));
        Assert.IsTrue(routePolicies.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Recap, StringComparison.Ordinal)
            && (policy["toolingEnabled"]?.GetValue<bool>() ?? false)
            && policy["allowedTools"] is JsonArray recapTools
            && recapTools.OfType<JsonObject>().Any(tool => string.Equals(tool["toolId"]?.GetValue<string>(), AiToolIds.DraftHistoryEntries, StringComparison.Ordinal))));

        using HttpResponseMessage routeBudgetResponse = await client.GetAsync("/api/ai/route-budgets");
        routeBudgetResponse.EnsureSuccessStatusCode();
        JsonArray routeBudgets = ParseRequiredJsonArray(await routeBudgetResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage routeBudgetStatusesResponse = await client.GetAsync("/api/ai/route-budget-statuses");
        routeBudgetStatusesResponse.EnsureSuccessStatusCode();
        JsonArray routeBudgetStatuses = ParseRequiredJsonArray(await routeBudgetStatusesResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage filteredRouteBudgetStatusesResponse = await client.GetAsync($"/api/ai/route-budget-statuses?routeType={AiRouteTypes.Coach}");
        filteredRouteBudgetStatusesResponse.EnsureSuccessStatusCode();
        JsonArray filteredRouteBudgetStatuses = ParseRequiredJsonArray(await filteredRouteBudgetStatusesResponse.Content.ReadAsStringAsync());
        Assert.IsTrue(routeBudgets.OfType<JsonObject>().Any(policy =>
            string.Equals(policy["routeType"]?.GetValue<string>(), AiRouteTypes.Build, StringComparison.Ordinal)
            && string.Equals(policy["budgetUnit"]?.GetValue<string>(), AiBudgetUnits.ChummerAiUnits, StringComparison.Ordinal)));
        Assert.IsTrue(routeBudgetStatuses.OfType<JsonObject>().Any(status =>
            string.Equals(status["routeType"]?.GetValue<string>(), AiRouteTypes.Coach, StringComparison.Ordinal)));
        Assert.AreEqual(1, filteredRouteBudgetStatuses.Count);
        Assert.AreEqual(AiRouteTypes.Coach, filteredRouteBudgetStatuses[0]?["routeType"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_gateway_provider_health_surface_reports_live_circuit_state()
    {
        using var client = CreateClient();

        await PostRequiredAiTurnObject(client, "/api/ai/docs/query", new AiConversationTurnRequest(
            Message: $"Provider health warm-up {Guid.NewGuid():N}",
            ConversationId: $"conv-provider-health-{Guid.NewGuid():N}",
            RuntimeFingerprint: "sha256:provider-health"));

        using HttpResponseMessage providerHealthResponse = await client.GetAsync("/api/ai/provider-health");
        providerHealthResponse.EnsureSuccessStatusCode();
        JsonArray providerHealth = ParseRequiredJsonArray(await providerHealthResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage filteredProviderHealthResponse = await client.GetAsync($"/api/ai/provider-health?providerId={AiProviderIds.OneMinAi}");
        filteredProviderHealthResponse.EnsureSuccessStatusCode();
        JsonArray filteredProviderHealth = ParseRequiredJsonArray(await filteredProviderHealthResponse.Content.ReadAsStringAsync());
        using HttpResponseMessage routeFilteredProviderHealthResponse = await client.GetAsync($"/api/ai/provider-health?routeType={AiRouteTypes.Docs}");
        routeFilteredProviderHealthResponse.EnsureSuccessStatusCode();
        JsonArray routeFilteredProviderHealth = ParseRequiredJsonArray(await routeFilteredProviderHealthResponse.Content.ReadAsStringAsync());

        Assert.IsTrue(providerHealth.OfType<JsonObject>().Any(item =>
            string.Equals(item["providerId"]?.GetValue<string>(), AiProviderIds.OneMinAi, StringComparison.Ordinal)
            && string.Equals(item["circuitState"]?.GetValue<string>(), AiProviderCircuitStates.Closed, StringComparison.Ordinal)));
        Assert.AreEqual(1, filteredProviderHealth.Count);
        Assert.AreEqual(AiProviderIds.OneMinAi, filteredProviderHealth[0]?["providerId"]?.GetValue<string>());
        Assert.IsNotNull(filteredProviderHealth[0]?["lastSuccessAtUtc"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Docs, filteredProviderHealth[0]?["lastRouteType"]?.GetValue<string>());
        Assert.IsNotNull(filteredProviderHealth[0]?["primaryCredentialCount"]);
        Assert.IsNotNull(filteredProviderHealth[0]?["transportMetadataConfigured"]);
        Assert.IsNotNull(filteredProviderHealth[0]?["lastCredentialTier"]?.GetValue<string>());
        Assert.IsTrue(routeFilteredProviderHealth.OfType<JsonObject>().Any(item =>
            string.Equals(item["providerId"]?.GetValue<string>(), AiProviderIds.OneMinAi, StringComparison.Ordinal)));
        Assert.IsTrue(routeFilteredProviderHealth.OfType<JsonObject>().All(item =>
            (item["allowedRouteTypes"] as JsonArray)?.OfType<JsonValue>().Any(value =>
                string.Equals(value.GetValue<string>(), AiRouteTypes.Docs, StringComparison.Ordinal)) == true));
    }

    [TestMethod]
    public async Task Ai_prompt_registry_surfaces_current_route_prompt_descriptors()
    {
        using var client = CreateClient();

        JsonObject catalog = await GetRequiredJsonObject(client, "/api/ai/prompts");
        JsonObject prompt = await GetRequiredJsonObject(client, $"/api/ai/prompts/{AiRouteTypes.Coach}");
        using HttpResponseMessage missingResponse = await client.GetAsync("/api/ai/prompts/missing-route");
        JsonArray items = catalog["items"] as JsonArray
            ?? throw new AssertFailedException("Prompt catalog did not include an items array.");

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.IsNotNull(catalog["totalCount"]);
        Assert.IsNotNull(items);
        Assert.AreEqual(AiRouteTypes.Coach, prompt["promptId"]?.GetValue<string>());
        Assert.AreEqual(AiPromptKinds.RouteSystem, prompt["promptKind"]?.GetValue<string>());
        Assert.AreEqual(AiPersonaIds.DeckerContact, prompt["personaId"]?.GetValue<string>());
        Assert.IsTrue(prompt["requiredGroundingSectionIds"] is JsonArray requiredSections
            && requiredSections.OfType<JsonValue>().Any(section => string.Equals(section.GetValue<string>(), AiGroundingSectionIds.Runtime, StringComparison.Ordinal)));
        Assert.IsTrue(prompt["allowedToolIds"] is JsonArray allowedTools
            && allowedTools.OfType<JsonValue>().Any(tool => string.Equals(tool.GetValue<string>(), AiToolIds.CreateApplyPreview, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Ai_build_idea_catalog_surfaces_search_and_detail_routes()
    {
        using var client = CreateClient();

        JsonObject catalog = await GetRequiredJsonObject(client, $"/api/ai/build-ideas?routeType={AiRouteTypes.Coach}&queryText=social&rulesetId=sr5&maxCount=3");
        JsonArray items = catalog["items"] as JsonArray
            ?? throw new AssertFailedException("Build idea catalog did not include an items array.");
        JsonObject detail = await GetRequiredJsonObject(client, "/api/ai/build-ideas/sr5.face-legwork-hybrid");
        using HttpResponseMessage missingResponse = await client.GetAsync("/api/ai/build-ideas/missing-idea");

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.IsGreaterThan(0, items.Count);
        Assert.AreEqual("sr5.face-legwork-hybrid", items[0]?["ideaId"]?.GetValue<string>());
        Assert.AreEqual("sr5.face-legwork-hybrid", detail["ideaId"]?.GetValue<string>());
        Assert.AreEqual("sr5", detail["rulesetId"]?.GetValue<string>());
        Assert.IsTrue(detail["roleTags"] is JsonArray roleTags
            && roleTags.OfType<JsonValue>().Any(tag => string.Equals(tag.GetValue<string>(), "social", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Ai_hub_project_search_endpoints_surface_catalog_and_detail_routes()
    {
        using var client = CreateClient();

        JsonObject catalog = await GetRequiredJsonObject(client, "/api/ai/hub/projects?queryText=core&type=ruleprofile&rulesetId=sr5&maxCount=5");
        JsonArray items = catalog["items"] as JsonArray
            ?? throw new AssertFailedException("AI hub catalog did not include an items array.");
        JsonObject detail = await GetRequiredJsonObject(client, "/api/ai/hub/projects/ruleprofile/official.sr5.core?rulesetId=sr5");
        using HttpResponseMessage missingResponse = await client.GetAsync("/api/ai/hub/projects/ruleprofile/missing-profile?rulesetId=sr5");

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.IsGreaterThan(0, items.Count);
        Assert.AreEqual("official.sr5.core", items[0]?["projectId"]?.GetValue<string>());
        Assert.AreEqual("ruleprofile", items[0]?["kind"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", detail["summary"]?["projectId"]?.GetValue<string>());
        Assert.IsTrue(detail["facts"] is JsonArray { Count: > 0 });
        Assert.IsTrue(detail["actions"] is JsonArray { Count: > 0 });
    }

    [TestMethod]
    public async Task Ai_explain_lookup_route_surfaces_capability_backed_projection()
    {
        using var client = CreateClient();

        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray runtimeEntries = runtimeLocks["entries"] as JsonArray
            ?? throw new AssertFailedException("Runtime lock catalog did not include an entries array.");
        string runtimeFingerprint = runtimeEntries.OfType<JsonObject>().First()["lockId"]?.GetValue<string>()
            ?? throw new AssertFailedException("Runtime lock catalog did not include a lock id.");

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        JsonObject explain = await GetRequiredJsonObject(client, $"/api/ai/explain?runtimeFingerprint={runtimeFingerprint}&characterId={characterId}&capabilityId={RulePackCapabilityIds.SessionQuickActions}&rulesetId=sr5");
        using HttpResponseMessage missingResponse = await client.GetAsync($"/api/ai/explain?runtimeFingerprint={runtimeFingerprint}&characterId={characterId}&capabilityId=missing.capability&rulesetId=sr5");

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.AreEqual(runtimeFingerprint, explain["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual(characterId, explain["characterId"]?.GetValue<string>());
        Assert.AreEqual(RulePackCapabilityIds.SessionQuickActions, explain["capabilityId"]?.GetValue<string>());
        Assert.AreEqual("Session Quick Actions", explain["title"]?.GetValue<string>());
        Assert.IsTrue(explain["fragments"] is JsonArray { Count: > 0 });
        Assert.IsTrue(explain["diagnostics"] is JsonArray { Count: > 0 });
    }

    [TestMethod]
    public async Task Ai_digest_lookup_routes_surface_runtime_character_and_session_context()
    {
        using var client = CreateClient();

        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray runtimeEntries = runtimeLocks["entries"] as JsonArray
            ?? throw new AssertFailedException("Runtime lock catalog did not include an entries array.");
        string runtimeFingerprint = runtimeEntries.OfType<JsonObject>().First()["lockId"]?.GetValue<string>()
            ?? throw new AssertFailedException("Runtime lock catalog did not include a lock id.");

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        JsonObject runtimeSummary = await GetRequiredJsonObject(client, $"/api/ai/runtime/{runtimeFingerprint}/summary?rulesetId=sr5");
        JsonObject characterDigest = await GetRequiredJsonObject(client, $"/api/ai/characters/{characterId}/digest");
        JsonObject sessionDigest = await GetRequiredJsonObject(client, $"/api/ai/session/characters/{characterId}/digest");
        using HttpResponseMessage missingRuntimeResponse = await client.GetAsync("/api/ai/runtime/missing-runtime/summary?rulesetId=sr5");
        using HttpResponseMessage missingCharacterResponse = await client.GetAsync("/api/ai/characters/missing-character/digest");
        using HttpResponseMessage missingSessionResponse = await client.GetAsync("/api/ai/session/characters/missing-character/digest");

        Assert.AreEqual(HttpStatusCode.NotFound, missingRuntimeResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingCharacterResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, missingSessionResponse.StatusCode);
        Assert.AreEqual(runtimeFingerprint, runtimeSummary["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("sr5", runtimeSummary["rulesetId"]?.GetValue<string>());
        Assert.IsTrue(runtimeSummary["contentBundles"] is JsonArray { Count: > 0 });
        Assert.IsTrue(runtimeSummary["providerBindings"] is JsonObject);
        Assert.AreEqual(characterId, characterDigest["characterId"]?.GetValue<string>());
        Assert.AreEqual("sr5", characterDigest["rulesetId"]?.GetValue<string>());
        Assert.IsTrue(characterDigest["summary"] is JsonObject summary
            && string.Equals(summary["name"]?.GetValue<string>(), "Troy Simmons", StringComparison.Ordinal));
        Assert.AreEqual(characterId, sessionDigest["characterId"]?.GetValue<string>());
        Assert.AreEqual(SessionRuntimeSelectionStates.Unselected, sessionDigest["selectionState"]?.GetValue<string>());
        Assert.IsNotNull(sessionDigest["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("sr5", sessionDigest["rulesetId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_gateway_preview_endpoint_returns_route_decision_budget_and_grounding_bundle()
    {
        using var client = CreateClient();

        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray runtimeEntries = runtimeLocks["entries"] as JsonArray
            ?? throw new AssertFailedException("Runtime lock catalog did not include an entries array.");
        string runtimeFingerprint = runtimeEntries.OfType<JsonObject>().First()["lockId"]?.GetValue<string>()
            ?? throw new AssertFailedException("Runtime lock catalog did not include a lock id.");
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/ai/preview/coach", new AiConversationTurnRequest(
            Message: "What should I spend 18 Karma on?",
            ConversationId: "conv-preview",
            RuntimeFingerprint: runtimeFingerprint,
            CharacterId: characterId,
            WorkspaceId: "ws-preview"));
        response.EnsureSuccessStatusCode();
        JsonObject payload = ParseRequiredJsonObject(await response.Content.ReadAsStringAsync());

        Assert.AreEqual(AiRouteTypes.Coach, payload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiProviderIds.AiMagicx, payload["routeDecision"]?["providerId"]?.GetValue<string>());
        Assert.AreEqual(AiBudgetUnits.ChummerAiUnits, payload["budget"]?["budgetUnit"]?.GetValue<string>());
        Assert.AreEqual(runtimeFingerprint, payload["grounding"]?["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("ws-preview", payload["grounding"]?["workspaceId"]?.GetValue<string>());
        Assert.AreEqual(AiProviderIds.AiMagicx, payload["providerRequest"]?["providerId"]?.GetValue<string>());
        Assert.AreEqual("conv-preview", payload["providerRequest"]?["conversationId"]?.GetValue<string>());
        Assert.AreEqual("ws-preview", payload["providerRequest"]?["workspaceId"]?.GetValue<string>());
        Assert.IsTrue(payload["grounding"]?["runtimeFacts"] is JsonObject runtimeFacts
            && string.Equals(runtimeFacts["runtimeFingerprint"]?.GetValue<string>(), runtimeFingerprint, StringComparison.Ordinal)
            && string.Equals(runtimeFacts["rulesetId"]?.GetValue<string>(), "sr5", StringComparison.Ordinal));
        Assert.IsTrue(payload["grounding"]?["characterFacts"] is JsonObject characterFacts
            && string.Equals(characterFacts["characterId"]?.GetValue<string>(), characterId, StringComparison.Ordinal)
            && string.Equals(characterFacts["displayName"]?.GetValue<string>(), "Troy Simmons (BLUE)", StringComparison.Ordinal)
            && string.Equals(characterFacts["workspaceId"]?.GetValue<string>(), "ws-preview", StringComparison.Ordinal)
            && string.Equals(characterFacts["sessionSelectionState"]?.GetValue<string>(), SessionRuntimeSelectionStates.Unselected, StringComparison.Ordinal));
        Assert.IsTrue(payload["grounding"]?["retrievedItems"] is JsonArray retrievedItems
            && retrievedItems.OfType<JsonObject>().Any(item => string.Equals(item["corpusId"]?.GetValue<string>(), AiRetrievalCorpusIds.Runtime, StringComparison.Ordinal)
                && string.Equals(item["itemId"]?.GetValue<string>(), runtimeFingerprint, StringComparison.Ordinal))
            && retrievedItems.OfType<JsonObject>().Any(item => string.Equals(item["corpusId"]?.GetValue<string>(), AiRetrievalCorpusIds.Private, StringComparison.Ordinal)
                && string.Equals(item["provenance"]?.GetValue<string>(), "character-digest", StringComparison.Ordinal))
            && retrievedItems.OfType<JsonObject>().Any(item => string.Equals(item["corpusId"]?.GetValue<string>(), AiRetrievalCorpusIds.Private, StringComparison.Ordinal)
                && string.Equals(item["provenance"]?.GetValue<string>(), "session-digest", StringComparison.Ordinal)));
        Assert.IsTrue(payload["providerRequest"]?["allowedTools"] is JsonArray allowedTools
            && allowedTools.OfType<JsonObject>().Any(tool => string.Equals(tool["toolId"]?.GetValue<string>(), AiToolIds.CreateApplyPreview, StringComparison.Ordinal)));
        Assert.IsTrue(payload["providerRequest"]?["groundingSections"] is JsonArray { Count: > 0 });
        StringAssert.Contains(payload["systemPrompt"]?.GetValue<string>() ?? string.Empty, "Structured Chummer data first");
        StringAssert.Contains(payload["systemPrompt"]?.GetValue<string>() ?? string.Empty, $"route_class: {AiRouteClassIds.GroundedRulesChat}");
        StringAssert.Contains(payload["systemPrompt"]?.GetValue<string>() ?? string.Empty, $"persona: {AiPersonaIds.DeckerContact}");
    }

    [TestMethod]
    public async Task Ai_action_preview_endpoints_surface_scaffolded_preview_receipts()
    {
        using var client = CreateClient();

        JsonObject runtimeLocks = await GetRequiredJsonObject(client, "/api/runtime/locks?ruleset=sr5");
        JsonArray runtimeEntries = runtimeLocks["entries"] as JsonArray
            ?? throw new AssertFailedException("Runtime lock catalog did not include an entries array.");
        string runtimeFingerprint = runtimeEntries.OfType<JsonObject>().First()["lockId"]?.GetValue<string>()
            ?? throw new AssertFailedException("Runtime lock catalog did not include a lock id.");
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        using HttpResponseMessage karmaResponse = await client.PostAsJsonAsync("/api/ai/preview/karma-spend", new AiSpendPlanPreviewRequest(
            CharacterId: characterId,
            RuntimeFingerprint: runtimeFingerprint,
            Steps:
            [
                new AiSpendPlanStep("step-1", "Raise Sneaking", Amount: 10m),
                new AiSpendPlanStep("step-2", "Raise Etiquette", Amount: 8m)
            ],
            Goal: "Advance stealth and social coverage",
            WorkspaceId: "ws-preview"));
        karmaResponse.EnsureSuccessStatusCode();
        JsonObject karmaPayload = ParseRequiredJsonObject(await karmaResponse.Content.ReadAsStringAsync());

        using HttpResponseMessage nuyenResponse = await client.PostAsJsonAsync("/api/ai/preview/nuyen-spend", new AiSpendPlanPreviewRequest(
            CharacterId: characterId,
            RuntimeFingerprint: runtimeFingerprint,
            Steps:
            [
                new AiSpendPlanStep("step-1", "Upgrade fake SIN", Amount: 12000m)
            ],
            Goal: "Cover heat",
            WorkspaceId: "ws-preview"));
        nuyenResponse.EnsureSuccessStatusCode();
        JsonObject nuyenPayload = ParseRequiredJsonObject(await nuyenResponse.Content.ReadAsStringAsync());

        using HttpResponseMessage applyResponse = await client.PostAsJsonAsync("/api/ai/apply-preview", new AiApplyPreviewRequest(
            CharacterId: characterId,
            RuntimeFingerprint: runtimeFingerprint,
            ActionDraft: new AiActionDraft(
                ActionId: AiSuggestedActionIds.PreviewApplyPlan,
                Title: "Apply stealth plan",
                Description: "Preview the strongest grounded follow-up action.",
                WorkspaceId: "ws-preview"),
            WorkspaceId: "ws-preview"));
        applyResponse.EnsureSuccessStatusCode();
        JsonObject applyPayload = ParseRequiredJsonObject(await applyResponse.Content.ReadAsStringAsync());

        using HttpResponseMessage missingKarmaResponse = await client.PostAsJsonAsync("/api/ai/preview/karma-spend", new AiSpendPlanPreviewRequest(
            CharacterId: "missing-character",
            RuntimeFingerprint: runtimeFingerprint,
            Steps: []));

        Assert.AreEqual(HttpStatusCode.NotFound, missingKarmaResponse.StatusCode);
        Assert.AreEqual(AiActionPreviewApiOperations.PreviewKarmaSpend, karmaPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiActionPreviewKinds.KarmaSpend, karmaPayload["previewKind"]?.GetValue<string>());
        Assert.AreEqual(characterId, karmaPayload["characterId"]?.GetValue<string>());
        Assert.AreEqual(runtimeFingerprint, karmaPayload["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("ws-preview", karmaPayload["workspaceId"]?.GetValue<string>());
        Assert.AreEqual(2, karmaPayload["stepCount"]?.GetValue<int>());
        Assert.AreEqual(18m, karmaPayload["totalRequested"]?.GetValue<decimal>());
        Assert.AreEqual("karma", karmaPayload["unit"]?.GetValue<string>());
        Assert.IsTrue(karmaPayload["evidence"] is JsonArray { Count: > 1 });
        Assert.IsTrue(karmaPayload["risks"] is JsonArray { Count: > 0 });

        Assert.AreEqual(AiActionPreviewApiOperations.PreviewNuyenSpend, nuyenPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiActionPreviewKinds.NuyenSpend, nuyenPayload["previewKind"]?.GetValue<string>());
        Assert.AreEqual("ws-preview", nuyenPayload["workspaceId"]?.GetValue<string>());
        Assert.AreEqual(1, nuyenPayload["stepCount"]?.GetValue<int>());
        Assert.AreEqual(12000m, nuyenPayload["totalRequested"]?.GetValue<decimal>());
        Assert.AreEqual("nuyen", nuyenPayload["unit"]?.GetValue<string>());

        Assert.AreEqual(AiActionPreviewApiOperations.CreateApplyPreview, applyPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiActionPreviewKinds.ApplyPreview, applyPayload["previewKind"]?.GetValue<string>());
        Assert.AreEqual("ws-preview", applyPayload["workspaceId"]?.GetValue<string>());
        Assert.AreEqual(1, applyPayload["stepCount"]?.GetValue<int>());
        Assert.IsTrue(applyPayload["preparedEffects"] is JsonArray { Count: > 1 });
    }

    [TestMethod]
    public async Task Ai_gateway_turn_attempts_are_retrievable_through_owner_scoped_conversation_catalog()
    {
        using var client = CreateClient();
        string conversationId = $"conv-store-{Guid.NewGuid():N}";

        using HttpResponseMessage sendResponse = await client.PostAsJsonAsync("/api/ai/coach", new AiConversationTurnRequest(
            Message: "What should I spend 18 Karma on?",
            ConversationId: conversationId,
            RuntimeFingerprint: "sha256:runtime",
            CharacterId: "char-9",
            WorkspaceId: "ws-9"));
        sendResponse.EnsureSuccessStatusCode();

        using HttpResponseMessage listResponse = await client.GetAsync($"/api/ai/conversations?conversationId={conversationId}&routeType=coach&characterId=char-9&runtimeFingerprint=sha256:runtime&workspaceId=ws-9&maxCount=5");
        listResponse.EnsureSuccessStatusCode();
        using HttpResponseMessage auditResponse = await client.GetAsync($"/api/ai/conversation-audits?conversationId={conversationId}&routeType=coach&characterId=char-9&runtimeFingerprint=sha256:runtime&workspaceId=ws-9&maxCount=5");
        auditResponse.EnsureSuccessStatusCode();
        JsonObject catalog = ParseRequiredJsonObject(await listResponse.Content.ReadAsStringAsync());
        JsonObject auditCatalog = ParseRequiredJsonObject(await auditResponse.Content.ReadAsStringAsync());
        JsonArray conversations = catalog["items"] as JsonArray
            ?? throw new AssertFailedException("Conversation catalog did not include an items array.");
        JsonArray audits = auditCatalog["items"] as JsonArray
            ?? throw new AssertFailedException("Conversation audit catalog did not include an items array.");
        JsonObject payload = await GetRequiredJsonObject(client, $"/api/ai/conversations/{conversationId}");
        Assert.IsGreaterThanOrEqualTo(1, catalog["totalCount"]?.GetValue<int>() ?? 0);
        Assert.IsGreaterThanOrEqualTo(1, auditCatalog["totalCount"]?.GetValue<int>() ?? 0);
        Assert.IsLessThanOrEqualTo(5, conversations.Count);
        Assert.IsLessThanOrEqualTo(5, audits.Count);
        Assert.IsTrue(conversations.OfType<JsonObject>().Any(conversation => string.Equals(conversation["conversationId"]?.GetValue<string>(), conversationId, StringComparison.Ordinal)));
        JsonObject audit = audits.OfType<JsonObject>().FirstOrDefault(item => string.Equals(item["conversationId"]?.GetValue<string>(), conversationId, StringComparison.Ordinal))
            ?? throw new AssertFailedException("Conversation audit catalog did not include the stored conversation.");
        Assert.AreEqual(AiRouteTypes.Coach, payload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Coach, audit["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiProviderIds.AiMagicx, audit["lastProviderId"]?.GetValue<string>());
        Assert.IsInstanceOfType<JsonObject>(audit["routeDecision"]);
        Assert.IsInstanceOfType<JsonObject>(audit["groundingCoverage"]);
        Assert.AreEqual("sha256:runtime", payload["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("char-9", payload["characterId"]?.GetValue<string>());
        Assert.AreEqual("ws-9", payload["workspaceId"]?.GetValue<string>());
        Assert.AreEqual("ws-9", audit["workspaceId"]?.GetValue<string>());
        Assert.IsTrue(payload["messages"] is JsonArray { Count: 3 });
        JsonArray turns = payload["turns"] as JsonArray
            ?? throw new AssertFailedException("Conversation detail did not include a turns array.");
        Assert.AreEqual(1, turns.Count);
        Assert.AreEqual(AiProviderIds.AiMagicx, turns[0]?["providerId"]?.GetValue<string>());
        Assert.AreEqual("ws-9", turns[0]?["workspaceId"]?.GetValue<string>());
        Assert.IsTrue(turns[0]?["toolInvocations"] is JsonArray toolInvocations
            && toolInvocations.OfType<JsonObject>().Any(invocation => string.Equals(invocation["toolId"]?.GetValue<string>(), AiToolIds.ExplainValue, StringComparison.Ordinal)));
        Assert.IsTrue(turns[0]?["citations"] is JsonArray citations
            && citations.OfType<JsonObject>().Any(citation => string.Equals(citation["kind"]?.GetValue<string>(), AiCitationKinds.Runtime, StringComparison.Ordinal)));
        Assert.AreEqual(AiConversationRoles.System, payload["messages"]?[0]?["role"]?.GetValue<string>());
        Assert.AreEqual(AiConversationRoles.User, payload["messages"]?[1]?["role"]?.GetValue<string>());
        Assert.AreEqual(AiConversationRoles.Assistant, payload["messages"]?[2]?["role"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_gateway_turn_routes_return_provider_backed_scaffold_responses()
    {
        using var client = CreateClient();
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/ai/coach",
            new AiConversationTurnRequest(
                Message: "What should I spend 18 Karma on next?",
                ConversationId: "conv-api-1",
                RuntimeFingerprint: "sha256:runtime"));

        response.EnsureSuccessStatusCode();
        JsonObject payload = await ParseRequiredJsonObject(response);
        Assert.AreEqual(AiProviderIds.AiMagicx, payload["providerId"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Coach, payload["routeType"]?.GetValue<string>());
        Assert.AreEqual("conv-api-1", payload["conversationId"]?.GetValue<string>());
        Assert.IsTrue(payload["answer"]?.GetValue<string>()?.Contains("scaffold stayed server-side", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(string.IsNullOrWhiteSpace(payload["flavorLine"]?.GetValue<string>()));
        Assert.IsInstanceOfType<JsonObject>(payload["structuredAnswer"]);
        Assert.IsTrue(payload["structuredAnswer"]?["summary"]?.GetValue<string>()?.Contains("scaffold stayed server-side", StringComparison.OrdinalIgnoreCase));
        Assert.IsTrue(payload["structuredAnswer"]?["recommendations"] is JsonArray { Count: > 0 });
        Assert.AreEqual(AiConfidenceLevels.Scaffolded, payload["structuredAnswer"]?["confidence"]?.GetValue<string>());
        Assert.IsTrue(payload["structuredAnswer"]?["actionDrafts"] is JsonArray { Count: > 0 });
        Assert.IsTrue(payload["citations"] is JsonArray { Count: > 0 });
        Assert.IsTrue(payload["suggestedActions"] is JsonArray { Count: > 0 });
        Assert.IsTrue(payload["toolInvocations"] is JsonArray { Count: > 0 });
    }

    [TestMethod]
    public async Task Ai_gateway_turn_routes_surface_cache_hits_without_spending_additional_budget()
    {
        using var client = CreateClient();
        string prompt = $"How do I open the coach launcher panel? {Guid.NewGuid():N}";

        JsonObject first = await PostRequiredAiTurnObject(client, "/api/ai/docs/query", new AiConversationTurnRequest(
            Message: prompt,
            ConversationId: $"conv-cache-first-{Guid.NewGuid():N}",
            RuntimeFingerprint: "sha256:cache-runtime",
            WorkspaceId: "ws-cache"));
        JsonObject second = await PostRequiredAiTurnObject(client, "/api/ai/docs/query", new AiConversationTurnRequest(
            Message: $"  {prompt.ToUpperInvariant()}  ",
            ConversationId: $"conv-cache-second-{Guid.NewGuid():N}",
            RuntimeFingerprint: "sha256:cache-runtime",
            WorkspaceId: "ws-cache"));

        Assert.AreEqual(AiCacheStatuses.Miss, first["cache"]?["status"]?.GetValue<string>());
        Assert.AreEqual(AiCacheStatuses.Hit, second["cache"]?["status"]?.GetValue<string>());
        Assert.AreEqual(first["cache"]?["cacheKey"]?.GetValue<string>(), second["cache"]?["cacheKey"]?.GetValue<string>());
        Assert.AreEqual(first["answer"]?.GetValue<string>(), second["answer"]?.GetValue<string>());
        Assert.AreEqual(first["budget"]?["monthlyConsumed"]?.GetValue<int>(), second["budget"]?["monthlyConsumed"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Ai_gateway_v1_1_alias_routes_return_expected_route_shapes()
    {
        using var client = CreateClient();

        JsonObject coachPayload = await PostRequiredAiTurnObject(client, "/api/ai/coach/query", new AiConversationTurnRequest(
            Message: "Coach alias",
            ConversationId: "conv-alias-coach",
            RuntimeFingerprint: "sha256:coach"));
        JsonObject buildPayload = await PostRequiredAiTurnObject(client, "/api/ai/build-lab/query", new AiConversationTurnRequest(
            Message: "Build alias",
            ConversationId: "conv-alias-build",
            RuntimeFingerprint: "sha256:build"));
        JsonObject recapPayload = await PostRequiredAiTurnObject(client, "/api/ai/session/recap", new AiConversationTurnRequest(
            Message: "Recap alias",
            ConversationId: "conv-alias-recap",
            RuntimeFingerprint: "sha256:recap"));
        JsonObject docsPayload = await PostRequiredAiTurnObject(client, "/api/ai/docs/query", new AiConversationTurnRequest(
            Message: "Docs alias",
            ConversationId: "conv-alias-docs",
            RuntimeFingerprint: "sha256:docs"));

        Assert.AreEqual(AiRouteTypes.Coach, coachPayload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Build, buildPayload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Recap, recapPayload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiRouteTypes.Docs, docsPayload["routeType"]?.GetValue<string>());
        Assert.AreEqual(AiProviderIds.OneMinAi, docsPayload["providerId"]?.GetValue<string>());
        Assert.IsFalse(string.IsNullOrWhiteSpace(docsPayload["flavorLine"]?.GetValue<string>()));
    }

    [TestMethod]
    public async Task Ai_media_and_admin_surfaces_expose_explicit_not_implemented_boundaries()
    {
        using var client = CreateClient();

        JsonObject portraitPayload = await PostAiNotImplementedObject(client, "/api/ai/media/portrait", new AiMediaJobRequest(
            Prompt: "Portrait prompt",
            CharacterId: "char-portrait",
            RuntimeFingerprint: "sha256:portrait"));
        JsonObject dossierPayload = await PostAiNotImplementedObject(client, "/api/ai/media/dossier", new AiMediaJobRequest(
            Prompt: "Dossier prompt",
            CharacterId: "char-dossier"));
        JsonObject routeVideoPayload = await PostAiNotImplementedObject(client, "/api/ai/media/route-video", new AiMediaJobRequest(
            Prompt: "Route prompt"));

        using HttpResponseMessage evalsResponse = await client.GetAsync("/api/ai/admin/evals?routeType=coach&maxCount=5");
        Assert.AreEqual(HttpStatusCode.NotImplemented, evalsResponse.StatusCode);
        JsonObject evalsPayload = await ParseRequiredJsonObject(evalsResponse);

        Assert.AreEqual("ai_not_implemented", portraitPayload["error"]?.GetValue<string>());
        Assert.AreEqual(AiMediaApiOperations.QueuePortraitJob, portraitPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiMediaApiOperations.QueueDossierJob, dossierPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiMediaApiOperations.QueueRouteVideoJob, routeVideoPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiEvaluationApiOperations.ListEvaluations, evalsPayload["operation"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_portrait_prompt_route_surfaces_grounded_prompt_projection()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        JsonObject characterDigest = await GetRequiredJsonObject(client, $"/api/ai/characters/{characterId}/digest");
        string runtimeFingerprint = characterDigest["runtimeFingerprint"]?.GetValue<string>()
            ?? throw new AssertFailedException("Character digest did not include a runtime fingerprint.");

        JsonObject prompt = await PostRequiredJsonObject(client, "/api/ai/media/portrait/prompt", new JsonObject
        {
            ["characterId"] = characterId,
            ["runtimeFingerprint"] = runtimeFingerprint,
            ["stylePackId"] = "neo-noir"
        });
        using HttpResponseMessage missingResponse = await client.PostAsJsonAsync("/api/ai/media/portrait/prompt", new AiPortraitPromptRequest(
            CharacterId: "missing-character",
            RuntimeFingerprint: runtimeFingerprint,
            StylePackId: "neo-noir"));

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.AreEqual(characterId, prompt["characterId"]?.GetValue<string>());
        Assert.AreEqual(runtimeFingerprint, prompt["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("sr5", prompt["rulesetId"]?.GetValue<string>());
        Assert.AreEqual("neo-noir", prompt["stylePackId"]?.GetValue<string>());
        Assert.AreEqual(4, (prompt["variants"] as JsonArray)?.Count);
        StringAssert.Contains(prompt["prompt"]?.GetValue<string>() ?? string.Empty, "Troy Simmons");
    }

    [TestMethod]
    public async Task Ai_history_draft_route_surfaces_grounded_draft_projection()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        JsonObject characterDigest = await GetRequiredJsonObject(client, $"/api/ai/characters/{characterId}/digest");
        string runtimeFingerprint = characterDigest["runtimeFingerprint"]?.GetValue<string>()
            ?? throw new AssertFailedException("Character digest did not include a runtime fingerprint.");

        JsonObject draft = await PostRequiredJsonObject(client, "/api/ai/history/drafts", new JsonObject
        {
            ["characterId"] = characterId,
            ["runtimeFingerprint"] = runtimeFingerprint,
            ["sessionId"] = "session-blue",
            ["focus"] = "downtime fallout"
        });
        using HttpResponseMessage missingResponse = await client.PostAsJsonAsync("/api/ai/history/drafts", new AiHistoryDraftRequest(
            CharacterId: "missing-character",
            RuntimeFingerprint: runtimeFingerprint,
            SessionId: "session-blue"));

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.AreEqual(AiHistoryDraftApiOperations.CreateHistoryDraft, draft["operation"]?.GetValue<string>());
        Assert.AreEqual(characterId, draft["characterId"]?.GetValue<string>());
        Assert.AreEqual(runtimeFingerprint, draft["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("sr5", draft["rulesetId"]?.GetValue<string>());
        Assert.AreEqual(AiHistoryDraftSourceKinds.Session, draft["sourceKind"]?.GetValue<string>());
        Assert.AreEqual("session-blue", draft["sourceId"]?.GetValue<string>());
        Assert.AreEqual(4, (draft["entries"] as JsonArray)?.Count);
        JsonArray evidence = draft["evidence"] as JsonArray
            ?? throw new AssertFailedException("History draft response did not include an evidence array.");
        Assert.IsGreaterThan(1, evidence.Count);
        StringAssert.Contains(draft["summary"]?.GetValue<string>() ?? string.Empty, "Troy Simmons");
    }

    [TestMethod]
    public async Task Ai_media_queue_route_surfaces_grounded_receipt_projection()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importedWorkspace = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string characterId = importedWorkspace["id"]?.GetValue<string>()
            ?? throw new AssertFailedException("Workspace import did not return an id.");

        JsonObject characterDigest = await GetRequiredJsonObject(client, $"/api/ai/characters/{characterId}/digest");
        string runtimeFingerprint = characterDigest["runtimeFingerprint"]?.GetValue<string>()
            ?? throw new AssertFailedException("Character digest did not include a runtime fingerprint.");

        JsonObject receipt = await PostRequiredJsonObject(client, "/api/ai/media/queue", new JsonObject
        {
            ["jobType"] = AiMediaJobTypes.Portrait,
            ["characterId"] = characterId,
            ["runtimeFingerprint"] = runtimeFingerprint,
            ["stylePackId"] = "neo-noir"
        });
        using HttpResponseMessage missingResponse = await client.PostAsJsonAsync("/api/ai/media/queue", new AiMediaQueueRequest(
            JobType: AiMediaJobTypes.Dossier,
            CharacterId: "missing-character",
            RuntimeFingerprint: runtimeFingerprint));
        using HttpResponseMessage badRequestResponse = await client.PostAsJsonAsync("/api/ai/media/queue", new AiMediaQueueRequest(
            JobType: "unknown",
            CharacterId: characterId,
            RuntimeFingerprint: runtimeFingerprint));

        Assert.AreEqual(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.BadRequest, badRequestResponse.StatusCode);
        Assert.AreEqual(AiMediaQueueApiOperations.QueueMediaJob, receipt["operation"]?.GetValue<string>());
        Assert.AreEqual(AiMediaJobTypes.Portrait, receipt["jobType"]?.GetValue<string>());
        Assert.AreEqual(AiMediaQueueStates.Scaffolded, receipt["state"]?.GetValue<string>());
        Assert.AreEqual(characterId, receipt["characterId"]?.GetValue<string>());
        Assert.AreEqual(runtimeFingerprint, receipt["runtimeFingerprint"]?.GetValue<string>());
        Assert.AreEqual("sr5", receipt["rulesetId"]?.GetValue<string>());
        Assert.AreEqual(AiMediaApiOperations.QueuePortraitJob, receipt["underlyingOperation"]?.GetValue<string>());
        StringAssert.Contains(receipt["prompt"]?.GetValue<string>() ?? string.Empty, "Troy Simmons");
    }

    [TestMethod]
    public async Task Ai_media_asset_catalog_surfaces_expose_explicit_not_implemented_boundaries()
    {
        using var client = CreateClient();

        using HttpResponseMessage listResponse = await client.GetAsync("/api/ai/media/assets?assetKind=portrait&characterId=char-1&state=pending-review&maxCount=5");
        Assert.AreEqual(HttpStatusCode.NotImplemented, listResponse.StatusCode);
        JsonObject listPayload = await ParseRequiredJsonObject(listResponse);
        using HttpResponseMessage detailResponse = await client.GetAsync("/api/ai/media/assets/asset-1");
        Assert.AreEqual(HttpStatusCode.NotImplemented, detailResponse.StatusCode);
        JsonObject detailPayload = await ParseRequiredJsonObject(detailResponse);

        Assert.AreEqual(AiMediaAssetApiOperations.ListMediaAssets, listPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiMediaAssetApiOperations.GetMediaAsset, detailPayload["operation"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_approval_surfaces_expose_explicit_not_implemented_boundaries()
    {
        using var client = CreateClient();

        using HttpResponseMessage approvalsResponse = await client.GetAsync("/api/ai/approvals?state=pending-review&targetKind=media-job&maxCount=5");
        Assert.AreEqual(HttpStatusCode.NotImplemented, approvalsResponse.StatusCode);
        JsonObject approvalsPayload = await ParseRequiredJsonObject(approvalsResponse);
        JsonObject submitPayload = await PostAiNotImplementedObject(client, "/api/ai/approvals", new AiApprovalSubmitRequest(
            TargetKind: AiApprovalTargetKinds.RecapDraft,
            TargetId: "recap-1",
            Title: "Review recap draft",
            Summary: "Approval request"));
        JsonObject resolvePayload = await PostAiNotImplementedObject(client, "/api/ai/approvals/approval-1/resolve", new AiApprovalResolveRequest(
            Decision: AiApprovalDecisionKinds.Approve,
            FinalState: AiApprovalStates.ApprovedCanonical));

        Assert.AreEqual(AiApprovalApiOperations.ListApprovals, approvalsPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiApprovalApiOperations.SubmitApproval, submitPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiApprovalApiOperations.ResolveApproval, resolvePayload["operation"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Ai_session_memory_surfaces_expose_explicit_not_implemented_boundaries()
    {
        using var client = CreateClient();

        JsonObject transcriptPayload = await PostAiNotImplementedObject(client, "/api/ai/session/transcripts", new AiTranscriptSubmissionRequest(
            FileName: "session-audio.wav",
            ContentType: "audio/wav",
            SessionId: "session-1"));
        using HttpResponseMessage transcriptDetailResponse = await client.GetAsync("/api/ai/session/transcripts/transcript-1");
        Assert.AreEqual(HttpStatusCode.NotImplemented, transcriptDetailResponse.StatusCode);
        JsonObject transcriptDetailPayload = await ParseRequiredJsonObject(transcriptDetailResponse);
        using HttpResponseMessage recapListResponse = await client.GetAsync("/api/ai/session/recap-drafts?sessionId=session-1&maxCount=5");
        Assert.AreEqual(HttpStatusCode.NotImplemented, recapListResponse.StatusCode);
        JsonObject recapListPayload = await ParseRequiredJsonObject(recapListResponse);
        JsonObject recapCreatePayload = await PostAiNotImplementedObject(client, "/api/ai/session/recap-drafts", new AiRecapDraftRequest(
            SourceKind: "transcript",
            SourceId: "transcript-1",
            Title: "Session recap",
            SessionId: "session-1"));

        Assert.AreEqual(AiTranscriptApiOperations.SubmitTranscript, transcriptPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiTranscriptApiOperations.GetTranscript, transcriptDetailPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiRecapDraftApiOperations.ListRecapDrafts, recapListPayload["operation"]?.GetValue<string>());
        Assert.AreEqual(AiRecapDraftApiOperations.CreateRecapDraft, recapCreatePayload["operation"]?.GetValue<string>());
    }

    private static async Task<JsonObject> PostRequiredAiTurnObject(HttpClient client, string path, AiConversationTurnRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return await ParseRequiredJsonObject(response);
    }

    private static async Task<JsonObject> PostAiNotImplementedObject<TRequest>(HttpClient client, string path, TRequest request)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(path, request);
        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        return await ParseRequiredJsonObject(response);
    }

    [TestMethod]
    public async Task Session_http_client_lists_workspace_backed_session_characters()
    {
        using var http = CreateClient();
        await ClearAllWorkspacesAsync(http);
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject importResponse = await PostRequiredJsonObject(http, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "SR5"
        });
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        JsonObject workspaceList = await GetRequiredJsonObject(http, "/api/workspaces?maxCount=20");
        JsonArray importedWorkspaces = workspaceList["workspaces"] as JsonArray ?? [];
        Assert.IsTrue(
            importedWorkspaces.Any(node => string.Equals(node?["id"]?.GetValue<string>(), workspaceId, StringComparison.Ordinal)),
            "Expected imported workspace to be listed before resolving session characters.");
        HttpSessionClient sessionClient = new(http);

        SessionApiResult<SessionCharacterCatalog> result = await sessionClient.ListCharactersAsync(default);

        Assert.IsTrue(result.IsImplemented);
        Assert.IsNotNull(result.Payload);
        Assert.IsGreaterThan(0, result.Payload.Characters.Count);
        Assert.AreEqual("sr5", result.Payload.Characters[0].RulesetId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Payload.Characters[0].CharacterId));
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.Payload.Characters[0].DisplayName));
    }

    [TestMethod]
    public async Task Session_http_client_uses_explicit_not_implemented_boundary_for_sync()
    {
        using var http = CreateClient();
        HttpSessionClient sessionClient = new(http);
        SessionSyncBatch batch = new(
            OverlayId: "overlay-1",
            BaseCharacterVersion: new("char-1", "ver-1", "sr5", "runtime-1"),
            Events: [],
            ClientCursor: "cursor-1");

        SessionApiResult<SessionSyncReceipt> result = await sessionClient.SyncCharacterLedgerAsync("char-1", batch, default);

        Assert.IsFalse(result.IsImplemented);
        Assert.IsNotNull(result.NotImplemented);
        Assert.AreEqual("session_not_implemented", result.NotImplemented.Error);
        Assert.AreEqual(SessionApiOperations.SyncCharacterLedger, result.NotImplemented.Operation);
        Assert.AreEqual("char-1", result.NotImplemented.CharacterId);
    }

    [TestMethod]
    public async Task Session_http_client_exposes_owner_backed_profile_selection_and_runtime_bundle_receipts()
    {
        using var http = CreateClient();
        HttpSessionClient sessionClient = new(http);
        string characterId = $"char-{Guid.NewGuid():N}";
        SessionApiResult<SessionProfileCatalog> profilesResult = await sessionClient.ListProfilesAsync(default);
        SessionApiResult<RulePackCatalog> rulePackResult = await sessionClient.ListRulePacksAsync(default);

        SessionApiResult<SessionProfileSelectionReceipt> profileResult = await sessionClient.SelectProfileAsync(
            characterId,
            new SessionProfileSelectionRequest("official.sr5.core"),
            default);
        SessionApiResult<SessionRuntimeStatusProjection> runtimeStateBeforeBundle = await sessionClient.GetRuntimeStateAsync(characterId, default);
        SessionApiResult<SessionRuntimeBundleIssueReceipt> bundleResult = await sessionClient.GetRuntimeBundleAsync(characterId, default);
        SessionApiResult<SessionRuntimeBundleRefreshReceipt> refreshResult = await sessionClient.RefreshRuntimeBundleAsync(characterId, default);
        SessionApiResult<SessionRuntimeStatusProjection> runtimeStateAfterBundle = await sessionClient.GetRuntimeStateAsync(characterId, default);

        Assert.IsTrue(profilesResult.IsImplemented);
        Assert.IsNotNull(profilesResult.Payload);
        Assert.IsNotEmpty(profilesResult.Payload.Profiles);
        Assert.AreEqual("official.sr5.core", profilesResult.Payload.ActiveProfileId);

        Assert.IsTrue(rulePackResult.IsImplemented);
        Assert.IsNotNull(rulePackResult.Payload);

        Assert.IsTrue(profileResult.IsImplemented);
        Assert.IsNotNull(profileResult.Payload);
        Assert.AreEqual(SessionProfileSelectionOutcomes.Selected, profileResult.Payload.Outcome);
        Assert.AreEqual(characterId, profileResult.Payload.CharacterId);
        Assert.AreEqual("official.sr5.core", profileResult.Payload.ProfileId);

        Assert.IsTrue(runtimeStateBeforeBundle.IsImplemented);
        Assert.IsNotNull(runtimeStateBeforeBundle.Payload);
        Assert.AreEqual(SessionRuntimeSelectionStates.Selected, runtimeStateBeforeBundle.Payload.SelectionState);
        Assert.AreEqual(SessionRuntimeBundleFreshnessStates.Missing, runtimeStateBeforeBundle.Payload.BundleFreshness);
        Assert.IsTrue(runtimeStateBeforeBundle.Payload.RequiresBundleRefresh);

        Assert.IsTrue(bundleResult.IsImplemented);
        Assert.IsNotNull(bundleResult.Payload);
        Assert.AreEqual(SessionRuntimeBundleIssueOutcomes.Issued, bundleResult.Payload.Outcome);
        Assert.AreEqual(characterId, bundleResult.Payload.Bundle.BaseCharacterVersion.CharacterId);
        Assert.AreEqual(profileResult.Payload.RuntimeFingerprint, bundleResult.Payload.Bundle.BaseCharacterVersion.RuntimeFingerprint);

        Assert.IsTrue(refreshResult.IsImplemented);
        Assert.IsNotNull(refreshResult.Payload);
        Assert.AreEqual(SessionRuntimeBundleRefreshOutcomes.Unchanged, refreshResult.Payload.Outcome);
        Assert.AreEqual(bundleResult.Payload.Bundle.BundleId, refreshResult.Payload.CurrentBundleId);

        Assert.IsTrue(runtimeStateAfterBundle.IsImplemented);
        Assert.IsNotNull(runtimeStateAfterBundle.Payload);
        Assert.AreEqual(SessionRuntimeBundleFreshnessStates.Current, runtimeStateAfterBundle.Payload.BundleFreshness);
        Assert.IsFalse(runtimeStateAfterBundle.Payload.RequiresBundleRefresh);
        Assert.AreEqual(bundleResult.Payload.Bundle.BundleId, runtimeStateAfterBundle.Payload.BundleId);
    }

    [TestMethod]
    public async Task Protected_endpoint_requires_valid_api_key_when_auth_is_enabled()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return;

        using var client = CreateClient(includeApiKey: false);

        using HttpResponseMessage response = await client.GetAsync("/api/tools/master-index");
        string content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(401, (int)response.StatusCode, content);
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.AreEqual("missing_or_invalid_api_key", parsed?["error"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Profile_apply_endpoint_requires_valid_api_key_when_auth_is_enabled()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return;

        using var client = CreateClient(includeApiKey: false);
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Character, "character-1");

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/profiles/official.sr5.core/apply?ruleset=sr5", target);
        string content = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(401, (int)response.StatusCode, content);
        JsonNode? parsed = JsonNode.Parse(content);
        Assert.AreEqual("missing_or_invalid_api_key", parsed?["error"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Rulepack_and_runtime_lock_install_endpoints_require_valid_api_key_when_auth_is_enabled()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            return;

        using var client = CreateClient(includeApiKey: false);
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");

        using HttpResponseMessage rulePackPreviewResponse = await client.PostAsJsonAsync("/api/rulepacks/missing-pack/install-preview?ruleset=sr5", target);
        using HttpResponseMessage rulePackInstallResponse = await client.PostAsJsonAsync("/api/rulepacks/missing-pack/install?ruleset=sr5", target);
        using HttpResponseMessage runtimePreviewResponse = await client.PostAsJsonAsync("/api/runtime/locks/missing-lock/install-preview?ruleset=sr5", target);
        using HttpResponseMessage runtimeInstallResponse = await client.PostAsJsonAsync("/api/runtime/locks/missing-lock/install?ruleset=sr5", target);

        Assert.AreEqual(HttpStatusCode.Unauthorized, rulePackPreviewResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, rulePackInstallResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, runtimePreviewResponse.StatusCode);
        Assert.AreEqual(HttpStatusCode.Unauthorized, runtimeInstallResponse.StatusCode);
    }

    [TestMethod]
    public async Task Contacts_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><contacts><contact><name>A</name><role>B</role><location>C</location><connection>3</connection><loyalty>2</loyalty></contact></contacts></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/contacts", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Attribute_details_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><attributes><attribute><name>BOD</name><metatypemin>1</metatypemin><metatypemax>6</metatypemax><metatypeaugmax>9</metatypeaugmax><base>3</base><karma>1</karma><totalvalue>4</totalvalue><metatypecategory>Standard</metatypecategory></attribute></attributes></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/attributedetails", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Vehicles_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><vehicles><vehicle><guid>v1</guid><name>Roadmaster</name><category>Truck</category><handling>3</handling><speed>4</speed><body>18</body><armor>16</armor><sensor>3</sensor><seats>6</seats><cost>120000</cost><mods><mod><name>GridLink Override</name></mod></mods><weapons><weapon><name>LMG</name></weapon></weapons></vehicle></vehicles></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/vehicles", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Profile_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><name>Neo</name><alias>The One</alias><playername>T</playername><metatype>Human</metatype><sex>Male</sex><age>29</age><buildmethod>Priority</buildmethod><created>True</created><adept>False</adept><magician>True</magician><technomancer>False</technomancer><ai>False</ai><mainmugshotindex>0</mainmugshotindex><mugshots><mugshot>a</mugshot></mugshots></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/profile", body);
        Assert.AreEqual("Neo", response["name"]?.GetValue<string>());
        Assert.AreEqual("Human", response["metatype"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Progress_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><karma>15</karma><nuyen>2500</nuyen><startingnuyen>6000</startingnuyen><streetcred>2</streetcred><notoriety>1</notoriety><publicawareness>0</publicawareness><burntstreetcred>0</burntstreetcred><buildkarma>25</buildkarma><totalattributes>18</totalattributes><totalspecial>2</totalspecial><physicalcmfilled>1</physicalcmfilled><stuncmfilled>3</stuncmfilled><totaless>5.25</totaless><initiategrade>0</initiategrade><submersiongrade>0</submersiongrade><magenabled>True</magenabled><resenabled>False</resenabled><depenabled>False</depenabled></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/progress", body);
        Assert.AreEqual(15m, response["karma"]?.GetValue<decimal>());
        Assert.AreEqual(2500m, response["nuyen"]?.GetValue<decimal>());
    }

    [TestMethod]
    public async Task Rules_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><gameedition>SR5</gameedition><settings>default.xml</settings><gameplayoption>Standard</gameplayoption><gameplayoptionqualitylimit>25</gameplayoptionqualitylimit><maxnuyen>10</maxnuyen><maxkarma>25</maxkarma><contactmultiplier>3</contactmultiplier><bannedwaregrades><grade>Betaware</grade><grade>Deltaware</grade></bannedwaregrades></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/rules", body);
        Assert.AreEqual("SR5", response["gameEdition"]?.GetValue<string>());
        Assert.AreEqual(2, response["bannedWareGrades"]?.AsArray().Count);
    }

    [TestMethod]
    public async Task Build_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><buildmethod>SumtoTen</buildmethod><prioritymetatype>C,2</prioritymetatype><priorityattributes>E,0</priorityattributes><priorityspecial>A,4</priorityspecial><priorityskills>B,3</priorityskills><priorityresources>D,1</priorityresources><prioritytalent>Mundane</prioritytalent><sumtoten>10</sumtoten><special>1</special><totalspecial>4</totalspecial><totalattributes>20</totalattributes><contactpoints>15</contactpoints><contactpointsused>8</contactpointsused></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/build", body);
        Assert.AreEqual("SumtoTen", response["buildMethod"]?.GetValue<string>());
        Assert.AreEqual(10, response["sumToTen"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Movement_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><walk>2/1/0</walk><run>4/0/0</run><sprint>2/1/0</sprint><walkalt>2/1/0</walkalt><runalt>4/0/0</runalt><sprintalt>2/1/0</sprintalt><physicalcmfilled>1</physicalcmfilled><stuncmfilled>3</stuncmfilled></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/movement", body);
        Assert.AreEqual("2/1/0", response["walk"]?.GetValue<string>());
        Assert.AreEqual(3, response["stunCmFilled"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Weapon_accessories_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><weapons><weapon><guid>w1</guid><name>Ares Predator</name><accessories><accessory><guid>a1</guid><name>Smartgun System</name><mount>Internal</mount><extramount>None</extramount><rating>0</rating><cost>500</cost><equipped>True</equipped></accessory></accessories></weapon></weapons></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/weaponaccessories", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Armor_mods_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><armors><armor><guid>ar1</guid><name>Armor Jacket</name><armormods><armormod><guid>m1</guid><name>Nonconductivity</name><category>General</category><rating>6</rating><cost>6000</cost><equipped>True</equipped></armormod></armormods></armor></armors></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/armormods", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Vehicle_mods_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><vehicles><vehicle><guid>v1</guid><name>Roadmaster</name><mods><mod><guid>vm1</guid><name>GridLink Override</name><category>Electromagnetic</category><slots>1</slots><rating>0</rating><cost>1000</cost><equipped>True</equipped></mod></mods></vehicle></vehicles></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/vehiclemods", body);
        Assert.AreEqual(1, response["count"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Awakening_section_endpoint_parses_payload()
    {
        using var client = CreateClient();

        const string xml = "<character><magenabled>True</magenabled><resenabled>False</resenabled><depenabled>False</depenabled><adept>False</adept><magician>True</magician><technomancer>False</technomancer><ai>False</ai><initiategrade>2</initiategrade><submersiongrade>0</submersiongrade><tradition>Hermetic</tradition><traditionname>Hermetic</traditionname><traditiondrain>LOG + WIL</traditiondrain><spiritcombat>Fire</spiritcombat><spiritdetection>Air</spiritdetection><spirithealth>Water</spirithealth><spiritillusion>Earth</spiritillusion><spiritmanipulation>Man</spiritmanipulation><stream></stream><streamdrain></streamdrain><currentcounterspellingdice>3</currentcounterspellingdice><spelllimit>12</spelllimit><cfplimit>0</cfplimit><ainormalprogramlimit>0</ainormalprogramlimit><aiadvancedprogramlimit>0</aiadvancedprogramlimit></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/characters/sections/awakening", body);
        Assert.IsTrue(response["magEnabled"]?.GetValue<bool>() ?? false);
        Assert.AreEqual(2, response["initiateGrade"]?.GetValue<int>());
    }

    [TestMethod]
    public async Task Dice_roll_endpoint_returns_rolls()
    {
        using var client = CreateClient();

        JsonObject body = new()
        {
            ["expression"] = "8d6+2"
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/tools/dice/roll", body);
        Assert.AreEqual(8, response["rolls"]?.AsArray().Count);
        Assert.IsGreaterThanOrEqualTo(10, response["total"]?.GetValue<int>() ?? 0);
    }

    [TestMethod]
    public async Task Data_export_endpoint_returns_bundle()
    {
        using var client = CreateClient();

        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><karma>15</karma><nuyen>2500</nuyen><attributes><attribute><name>BOD</name><base>3</base><karma>1</karma><metatypecategory>Standard</metatypecategory><totalvalue>4</totalvalue></attribute></attributes><skills><skill><name>Pistols</name></skill></skills><contacts><contact><name>Fixer</name></contact></contacts></character>";
        JsonObject body = new()
        {
            ["xml"] = xml
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/tools/data-export", body);
        Assert.IsNotNull(response["summary"]);
        Assert.IsNotNull(response["profile"]);
        Assert.IsNotNull(response["attributes"]);
    }

    [TestMethod]
    public async Task Master_index_endpoint_returns_data()
    {
        using var client = CreateClient();

        JsonObject response = await GetRequiredJsonObject(client, "/api/tools/master-index");
        Assert.IsGreaterThan(0, response["count"]?.GetValue<int>() ?? 0);
        Assert.IsTrue(response["files"] is JsonArray);
    }

    [TestMethod]
    public async Task Translator_languages_endpoint_returns_data()
    {
        using var client = CreateClient();

        JsonObject response = await GetRequiredJsonObject(client, "/api/tools/translator/languages");
        Assert.IsGreaterThan(0, response["count"]?.GetValue<int>() ?? 0);
        Assert.IsTrue(response["languages"] is JsonArray);
    }

    [TestMethod]
    public async Task Settings_endpoints_roundtrip()
    {
        using var client = CreateClient();

        JsonObject saveBody = new()
        {
            ["uiScale"] = 110,
            ["theme"] = "classic"
        };

        JsonObject saveResponse = await PostRequiredJsonObject(client, "/api/tools/settings/global", saveBody);
        Assert.IsTrue(saveResponse["saved"]?.GetValue<bool>() ?? false);

        JsonObject getResponse = await GetRequiredJsonObject(client, "/api/tools/settings/global");
        Assert.IsNotNull(getResponse["settings"]);
    }

    [TestMethod]
    public async Task Roster_endpoints_accept_entry()
    {
        using var client = CreateClient();

        JsonObject body = new()
        {
            ["name"] = "BLUE",
            ["alias"] = "Troy",
            ["metatype"] = "Ork",
            ["lastOpenedUtc"] = DateTimeOffset.UtcNow.ToString("O")
        };

        JsonObject response = await PostRequiredJsonObject(client, "/api/tools/roster", body);
        Assert.IsGreaterThan(0, response["count"]?.GetValue<int>() ?? 0);
        Assert.IsTrue(response["entries"] is JsonArray);
    }

    [TestMethod]
    public async Task Life_modules_stages_endpoint_returns_data()
    {
        using var client = CreateClient();

        JsonNode stages = await client.GetFromJsonAsync<JsonNode>("/api/lifemodules/stages");
        Assert.IsNotNull(stages);
        Assert.IsInstanceOfType<JsonArray>(stages);
        Assert.IsGreaterThan(0, ((JsonArray)stages).Count);
    }

    [TestMethod]
    public async Task Commands_endpoint_returns_catalog()
    {
        using var client = CreateClient();

        JsonObject response = await GetRequiredJsonObject(client, "/api/commands?ruleset=sr5");
        JsonObject defaultResponse = await GetRequiredJsonObject(client, "/api/commands");

        Assert.IsGreaterThan(0, response["count"]?.GetValue<int>() ?? 0);
        Assert.IsTrue(response["commands"] is JsonArray);
        Assert.AreEqual(response.ToJsonString(), defaultResponse.ToJsonString());
    }

    [TestMethod]
    public async Task Commands_endpoint_returns_empty_catalog_for_unknown_ruleset()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/commands?ruleset=shadowrun-x");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("unknown_ruleset", payload["error"]?.GetValue<string>());
        Assert.AreEqual("shadowrun-x", payload["rulesetId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Navigation_tabs_endpoint_returns_catalog()
    {
        using var client = CreateClient();

        JsonObject response = await GetRequiredJsonObject(client, "/api/navigation-tabs?ruleset=sr5");
        JsonObject defaultResponse = await GetRequiredJsonObject(client, "/api/navigation-tabs");

        Assert.IsGreaterThanOrEqualTo(16, response["count"]?.GetValue<int>() ?? 0);
        Assert.IsTrue(response["tabs"] is JsonArray);
        Assert.IsTrue((response["tabs"] as JsonArray)?.Any(node => string.Equals(node?["id"]?.GetValue<string>(), "tab-info", StringComparison.Ordinal)) ?? false);
        Assert.IsTrue((response["tabs"] as JsonArray)?.All(node => !string.IsNullOrWhiteSpace(node?["sectionId"]?.GetValue<string>())) ?? false);
        Assert.AreEqual(response.ToJsonString(), defaultResponse.ToJsonString());
    }

    [TestMethod]
    public async Task Navigation_tabs_endpoint_returns_empty_catalog_for_unknown_ruleset()
    {
        using var client = CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/api/navigation-tabs?ruleset=shadowrun-x");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("unknown_ruleset", payload["error"]?.GetValue<string>());
        Assert.AreEqual("shadowrun-x", payload["rulesetId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Session_characters_endpoint_returns_workspace_backed_catalog()
    {
        using var client = CreateClient();
        await ClearAllWorkspacesAsync(client);
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject importResponse = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "SR5"
        });
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        JsonObject workspaceList = await GetRequiredJsonObject(client, "/api/workspaces?maxCount=20");
        JsonArray importedWorkspaces = workspaceList["workspaces"] as JsonArray ?? [];
        Assert.IsTrue(
            importedWorkspaces.Any(node => string.Equals(node?["id"]?.GetValue<string>(), workspaceId, StringComparison.Ordinal)),
            "Expected imported workspace to be listed before resolving session characters.");
        using HttpResponseMessage response = await client.GetAsync("/api/session/characters");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        string content = await response.Content.ReadAsStringAsync();
        JsonNode parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;
        Assert.IsInstanceOfType<JsonArray>(payload["characters"]);
        JsonArray characters = payload["characters"]!.AsArray();
        Assert.IsGreaterThan(0, characters.Count, content);
        Assert.AreEqual("sr5", characters[0]?["rulesetId"]?.GetValue<string>());
        Assert.IsNotNull(characters[0]?["characterId"]);
        Assert.IsNotNull(characters[0]?["displayName"]);
    }

    [TestMethod]
    public async Task Session_character_sync_endpoint_returns_not_implemented_receipt()
    {
        using var client = CreateClient();

        using var request = new StringContent("{}", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/session/characters/char-1/sync", request);
        Assert.AreEqual(HttpStatusCode.NotImplemented, response.StatusCode);
        JsonNode parsed = JsonNode.Parse(await response.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(parsed);
        JsonObject payload = (JsonObject)parsed!;

        Assert.AreEqual("session_not_implemented", payload["error"]?.GetValue<string>());
        Assert.AreEqual("sync-character-ledger", payload["operation"]?.GetValue<string>());
        Assert.AreEqual("char-1", payload["characterId"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Session_runtime_bundle_and_profile_endpoints_return_owner_backed_receipts()
    {
        using var client = CreateClient();
        string characterId = $"session-char-{Guid.NewGuid():N}";

        using HttpResponseMessage profilesResponse = await client.GetAsync("/api/session/profiles");
        Assert.AreEqual(HttpStatusCode.OK, profilesResponse.StatusCode);
        JsonNode profilesParsed = JsonNode.Parse(await profilesResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(profilesParsed);
        JsonObject profilesPayload = (JsonObject)profilesParsed!;
        Assert.IsNotNull(profilesPayload["profiles"]);
        Assert.AreEqual("official.sr5.core", profilesPayload["activeProfileId"]?.GetValue<string>());

        using HttpResponseMessage runtimeStateResponse = await client.GetAsync($"/api/session/characters/{characterId}/runtime-state");
        Assert.AreEqual(HttpStatusCode.OK, runtimeStateResponse.StatusCode);
        JsonNode runtimeStateParsed = JsonNode.Parse(await runtimeStateResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(runtimeStateParsed);
        JsonObject runtimeStatePayload = (JsonObject)runtimeStateParsed!;
        Assert.AreEqual("unselected", runtimeStatePayload["selectionState"]?.GetValue<string>());
        Assert.AreEqual("missing", runtimeStatePayload["bundleFreshness"]?.GetValue<string>());
        Assert.IsTrue(runtimeStatePayload["requiresBundleRefresh"]?.GetValue<bool>() ?? false);

        using HttpResponseMessage bundleResponse = await client.GetAsync($"/api/session/characters/{characterId}/runtime-bundle");
        Assert.AreEqual(HttpStatusCode.OK, bundleResponse.StatusCode);
        JsonNode bundleParsed = JsonNode.Parse(await bundleResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(bundleParsed);
        JsonObject bundlePayload = (JsonObject)bundleParsed!;
        Assert.AreEqual("blocked", bundlePayload["outcome"]?.GetValue<string>());
        Assert.AreEqual("inline", bundlePayload["deliveryMode"]?.GetValue<string>());
        Assert.IsNotNull(bundlePayload["diagnostics"]);

        using var profileRequest = new StringContent("{\"profileId\":\"official.sr5.core\"}", Encoding.UTF8, "application/json");
        using HttpResponseMessage profileResponse = await client.PostAsync($"/api/session/characters/{characterId}/profile", profileRequest);
        Assert.AreEqual(HttpStatusCode.OK, profileResponse.StatusCode);
        JsonNode profileParsed = JsonNode.Parse(await profileResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(profileParsed);
        JsonObject profilePayload = (JsonObject)profileParsed!;
        Assert.AreEqual("selected", profilePayload["outcome"]?.GetValue<string>());
        Assert.AreEqual(characterId, profilePayload["characterId"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", profilePayload["profileId"]?.GetValue<string>());

        using HttpResponseMessage selectedRuntimeStateResponse = await client.GetAsync($"/api/session/characters/{characterId}/runtime-state");
        Assert.AreEqual(HttpStatusCode.OK, selectedRuntimeStateResponse.StatusCode);
        JsonNode selectedRuntimeStateParsed = JsonNode.Parse(await selectedRuntimeStateResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(selectedRuntimeStateParsed);
        JsonObject selectedRuntimeStatePayload = (JsonObject)selectedRuntimeStateParsed!;
        Assert.AreEqual("selected", selectedRuntimeStatePayload["selectionState"]?.GetValue<string>());
        Assert.AreEqual("official.sr5.core", selectedRuntimeStatePayload["profileId"]?.GetValue<string>());
        Assert.AreEqual("missing", selectedRuntimeStatePayload["bundleFreshness"]?.GetValue<string>());
        Assert.IsTrue(selectedRuntimeStatePayload["requiresBundleRefresh"]?.GetValue<bool>() ?? false);

        using HttpResponseMessage selectedBundleResponse = await client.GetAsync($"/api/session/characters/{characterId}/runtime-bundle");
        Assert.AreEqual(HttpStatusCode.OK, selectedBundleResponse.StatusCode);
        JsonNode selectedBundleParsed = JsonNode.Parse(await selectedBundleResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(selectedBundleParsed);
        JsonObject selectedBundlePayload = (JsonObject)selectedBundleParsed!;
        Assert.AreEqual("issued", selectedBundlePayload["outcome"]?.GetValue<string>());
        Assert.IsNotNull(selectedBundlePayload["bundle"]);
        Assert.AreEqual(characterId, selectedBundlePayload["bundle"]?["baseCharacterVersion"]?["characterId"]?.GetValue<string>());

        using HttpResponseMessage bundledRuntimeStateResponse = await client.GetAsync($"/api/session/characters/{characterId}/runtime-state");
        Assert.AreEqual(HttpStatusCode.OK, bundledRuntimeStateResponse.StatusCode);
        JsonNode bundledRuntimeStateParsed = JsonNode.Parse(await bundledRuntimeStateResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(bundledRuntimeStateParsed);
        JsonObject bundledRuntimeStatePayload = (JsonObject)bundledRuntimeStateParsed!;
        Assert.AreEqual("current", bundledRuntimeStatePayload["bundleFreshness"]?.GetValue<string>());
        Assert.AreEqual(selectedBundlePayload["bundle"]?["bundleId"]?.GetValue<string>(), bundledRuntimeStatePayload["bundleId"]?.GetValue<string>());
        Assert.IsFalse(bundledRuntimeStatePayload["requiresBundleRefresh"]?.GetValue<bool>() ?? true);

        using HttpResponseMessage refreshBundleResponse = await client.PostAsync($"/api/session/characters/{characterId}/runtime-bundle/refresh", null);
        Assert.AreEqual(HttpStatusCode.OK, refreshBundleResponse.StatusCode);
        JsonNode refreshBundleParsed = JsonNode.Parse(await refreshBundleResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(refreshBundleParsed);
        JsonObject refreshBundlePayload = (JsonObject)refreshBundleParsed!;
        Assert.AreEqual("unchanged", refreshBundlePayload["outcome"]?.GetValue<string>());
        Assert.AreEqual(
            selectedBundlePayload["bundle"]?["bundleId"]?.GetValue<string>(),
            refreshBundlePayload["currentBundleId"]?.GetValue<string>());

        using HttpResponseMessage rulePacksResponse = await client.GetAsync("/api/session/rulepacks");
        Assert.AreEqual(HttpStatusCode.OK, rulePacksResponse.StatusCode);
        JsonNode rulePacksParsed = JsonNode.Parse(await rulePacksResponse.Content.ReadAsStringAsync());
        Assert.IsInstanceOfType<JsonObject>(rulePacksParsed);
        JsonObject rulePacksPayload = (JsonObject)rulePacksParsed!;
        Assert.IsInstanceOfType<JsonArray>(rulePacksPayload["installedRulePacks"]);
        JsonArray installedRulePacks = rulePacksPayload["installedRulePacks"]!.AsArray();
        if (installedRulePacks.Count > 0)
        {
            Assert.IsNotNull(installedRulePacks[0]?["packId"]);
            Assert.IsNotNull(installedRulePacks[0]?["version"]);
        }
    }

    [TestMethod]
    public async Task Shell_bootstrap_endpoint_returns_ruleset_catalog_and_workspace_snapshot()
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        await ClearAllWorkspacesAsync(client);
        await PostRequiredJsonObject(client, "/api/shell/preferences", new JsonObject
        {
            ["preferredRulesetId"] = "sr5"
        });

        JsonObject response = await GetRequiredJsonObject(client, "/api/shell/bootstrap?ruleset=sr5");

        Assert.AreEqual("sr5", (response["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["preferredRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["activeRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.IsNull(response["activeWorkspaceId"]);
        Assert.AreEqual("official.sr5.core", response["activeRuntime"]?["profileId"]?.GetValue<string>());
        Assert.IsNotNull(response["activeRuntime"]?["runtimeFingerprint"]);
        Assert.IsTrue(response["commands"] is JsonArray commands && commands.Count > 0);
        Assert.IsTrue(response["navigationTabs"] is JsonArray tabs && tabs.Count > 0);
        Assert.IsTrue(response["workflowDefinitions"] is JsonArray workflowDefinitions && workflowDefinitions.Count > 0);
        Assert.IsTrue(response["workflowSurfaces"] is JsonArray workflowSurfaces && workflowSurfaces.Count > 0);
        Assert.IsTrue(response["workspaces"] is JsonArray);
    }

    [TestMethod]
    public async Task Shell_bootstrap_endpoint_uses_preferred_ruleset_when_no_active_workspace_is_saved_even_if_workspaces_exist()
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        await ClearAllWorkspacesAsync(client);
        await PostRequiredJsonObject(client, "/api/shell/preferences", new JsonObject
        {
            ["preferredRulesetId"] = "sr5"
        });

        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject importBody = new()
        {
            ["xml"] = xml,
            ["rulesetId"] = "SR6"
        };

        await PostRequiredJsonObject(client, "/api/workspaces/import", importBody);
        JsonObject response = await GetRequiredJsonObject(client, "/api/shell/bootstrap");

        Assert.AreEqual("sr5", (response["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["preferredRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["activeRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.IsNull(response["activeWorkspaceId"]);
    }

    [TestMethod]
    public async Task Shell_session_endpoint_roundtrips_active_workspace_selection()
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);

        await PostRequiredJsonObject(client, "/api/shell/session", new JsonObject
        {
            ["activeWorkspaceId"] = "ws-test",
            ["activeTabId"] = "tab-rules",
            ["activeTabsByWorkspace"] = new JsonObject
            {
                ["ws-test"] = "tab-rules"
            }
        });

        JsonObject response = await GetRequiredJsonObject(client, "/api/shell/session");
        Assert.AreEqual("ws-test", response["activeWorkspaceId"]?.GetValue<string>());
        Assert.AreEqual("tab-rules", response["activeTabId"]?.GetValue<string>());
        Assert.AreEqual("tab-rules", response["activeTabsByWorkspace"]?["ws-test"]?.GetValue<string>());

        await PostRequiredJsonObject(client, "/api/shell/session", new JsonObject());
        JsonObject cleared = await GetRequiredJsonObject(client, "/api/shell/session");
        Assert.IsNull(cleared["activeWorkspaceId"]);
        Assert.IsNull(cleared["activeTabId"]);
        Assert.IsNull(cleared["activeTabsByWorkspace"]);
    }

    [TestMethod]
    public async Task Shell_bootstrap_endpoint_uses_saved_preferred_ruleset_when_no_workspace_is_open()
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);

        await ClearAllWorkspacesAsync(client);
        await PostRequiredJsonObject(client, "/api/shell/preferences", new JsonObject
        {
            ["preferredRulesetId"] = "sr6"
        });

        JsonObject response = await GetRequiredJsonObject(client, "/api/shell/bootstrap");

        Assert.AreEqual("sr6", (response["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.IsNull(response["activeWorkspaceId"]);
    }

    [TestMethod]
    public async Task Shell_bootstrap_endpoint_restores_saved_active_workspace_when_present()
    {
        using var client = CreateClient();
        client.Timeout = TimeSpan.FromSeconds(180);
        await ClearAllWorkspacesAsync(client);

        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject sr5Import = await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        });
        string sr5WorkspaceId = sr5Import["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(sr5WorkspaceId));

        await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr6"
        });

        await PostRequiredJsonObject(client, "/api/shell/preferences", new JsonObject
        {
            ["preferredRulesetId"] = "sr6"
        });
        await PostRequiredJsonObject(client, "/api/shell/session", new JsonObject
        {
            ["activeWorkspaceId"] = sr5WorkspaceId,
            ["activeTabId"] = "tab-rules",
            ["activeTabsByWorkspace"] = new JsonObject
            {
                [sr5WorkspaceId] = "tab-rules"
            }
        });

        JsonObject response = await GetRequiredJsonObject(client, "/api/shell/bootstrap");

        Assert.AreEqual(sr5WorkspaceId, response["activeWorkspaceId"]?.GetValue<string>());
        Assert.AreEqual("tab-rules", response["activeTabId"]?.GetValue<string>());
        Assert.AreEqual("tab-rules", response["activeTabsByWorkspace"]?[sr5WorkspaceId]?.GetValue<string>());
        Assert.AreEqual("sr6", (response["preferredRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["activeRulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.AreEqual("sr5", (response["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
    }

    [TestMethod]
    public async Task Workspace_endpoints_import_read_update_and_save_character()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject importBody = new()
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        };

        JsonObject importResponse = await PostRequiredJsonObject(client, "/api/workspaces/import", importBody);
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId));
        Assert.AreEqual("sr5", (importResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject summary = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/summary");
        Assert.AreEqual("Cerri", summary["name"]?.GetValue<string>());
        Assert.AreEqual("Apex", summary["alias"]?.GetValue<string>());

        JsonObject validation = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/validate");
        Assert.IsTrue(validation["isValid"]?.GetValue<bool>() ?? false);
        Assert.IsTrue(validation["issues"] is JsonArray);

        JsonObject profile = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/profile");
        Assert.AreEqual("Cerri", profile["name"]?.GetValue<string>());
        Assert.AreEqual("Apex", profile["alias"]?.GetValue<string>());

        JsonObject skills = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/skills");
        Assert.IsGreaterThan(0, skills["count"]?.GetValue<int>() ?? 0);

        JsonObject rules = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/rules");
        Assert.IsFalse(string.IsNullOrWhiteSpace(rules["gameEdition"]?.GetValue<string>()));

        JsonObject build = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/build");
        Assert.AreEqual("SumtoTen", build["buildMethod"]?.GetValue<string>());

        JsonObject movement = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/movement");
        Assert.IsFalse(string.IsNullOrWhiteSpace(movement["walk"]?.GetValue<string>()));

        JsonObject awakening = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/awakening");
        Assert.IsNotNull(awakening["magEnabled"]);

        JsonObject patchBody = new()
        {
            ["name"] = "Updated Name",
            ["alias"] = "Updated Alias",
            ["notes"] = "Updated notes"
        };

        JsonObject patchResponse = await PatchRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/metadata", patchBody);
        Assert.AreEqual("Updated Name", patchResponse["profile"]?["name"]?.GetValue<string>());

        JsonObject saveResponse = await PostRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/save", new JsonObject());
        Assert.AreEqual(workspaceId, saveResponse["id"]?.GetValue<string>());
        Assert.IsGreaterThan(0, saveResponse["documentLength"]?.GetValue<int>() ?? 0);
        Assert.AreEqual("sr5", (saveResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject downloadResponse = await PostRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/download", new JsonObject());
        Assert.AreEqual(workspaceId, downloadResponse["id"]?.GetValue<string>());
        Assert.AreEqual("NativeXml", downloadResponse["format"]?.GetValue<string>());
        Assert.AreEqual("sr5", (downloadResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
        Assert.IsTrue((downloadResponse["fileName"]?.GetValue<string>() ?? string.Empty).EndsWith(".chum5", StringComparison.Ordinal));
        string contentBase64 = downloadResponse["contentBase64"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(contentBase64));
        Assert.IsGreaterThan(0, Convert.FromBase64String(contentBase64).Length);
    }

    [TestMethod]
    public async Task Workspace_import_accepts_content_base64_payload_with_utf8_bom()
    {
        using var client = CreateClient();

        byte[] xmlBytes = File.ReadAllBytes(FindTestFilePath("BLUE.chum5"));
        JsonObject importBody = new()
        {
            ["contentBase64"] = Convert.ToBase64String(xmlBytes),
            ["format"] = "NativeXml",
            ["rulesetId"] = "sr5"
        };

        JsonObject importResponse = await PostRequiredJsonObject(client, "/api/workspaces/import", importBody);
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId));
        Assert.AreEqual("sr5", (importResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject summary = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/summary");
        Assert.AreEqual("Troy Simmons", summary["name"]?.GetValue<string>());
        Assert.AreEqual("BLUE", summary["alias"]?.GetValue<string>());
    }

    [TestMethod]
    public async Task Workspace_endpoints_preserve_ruleset_id_from_import_request()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        JsonObject importBody = new()
        {
            ["xml"] = xml,
            ["rulesetId"] = "SR6"
        };

        JsonObject importResponse = await PostRequiredJsonObject(client, "/api/workspaces/import", importBody);
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId));
        Assert.AreEqual("sr6", (importResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject listed = await GetRequiredJsonObject(client, "/api/workspaces");
        JsonArray listedWorkspaces = listed["workspaces"]?.AsArray() ?? [];
        JsonObject listedItem = listedWorkspaces
            .Select(node => node as JsonObject)
            .FirstOrDefault(node => string.Equals(node?["id"]?.GetValue<string>(), workspaceId, StringComparison.Ordinal))
            ?? new JsonObject();
        Assert.IsGreaterThan(0, listedItem.Count, "Expected workspace list entry for imported workspace.");
        Assert.AreEqual("sr6", (listedItem["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject saveResponse = await PostRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/save", new JsonObject());
        Assert.AreEqual("sr6", (saveResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());

        JsonObject downloadResponse = await PostRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/download", new JsonObject());
        Assert.AreEqual("sr6", (downloadResponse["rulesetId"]?.GetValue<string>() ?? string.Empty).ToLowerInvariant());
    }

    [TestMethod]
    public async Task Workspace_list_and_close_endpoints_manage_open_workspace_collection()
    {
        using var client = CreateClient();

        string xmlA = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        string xmlB = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject importBodyA = new() { ["xml"] = xmlA, ["rulesetId"] = "sr5" };
        JsonObject importBodyB = new() { ["xml"] = xmlB, ["rulesetId"] = "sr5" };

        JsonObject importA = await PostRequiredJsonObject(client, "/api/workspaces/import", importBodyA);
        JsonObject importB = await PostRequiredJsonObject(client, "/api/workspaces/import", importBodyB);
        string workspaceA = importA["id"]?.GetValue<string>() ?? string.Empty;
        string workspaceB = importB["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceA));
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceB));

        JsonObject listed = await GetRequiredJsonObject(client, "/api/workspaces");
        Assert.IsGreaterThanOrEqualTo(2, listed["count"]?.GetValue<int>() ?? 0);
        JsonArray listedWorkspaces = listed["workspaces"]?.AsArray() ?? [];
        CollectionAssert.IsSubsetOf(
            new[] { workspaceA, workspaceB },
            listedWorkspaces
                .Select(node => node?["id"]?.GetValue<string>() ?? string.Empty)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToArray());

        using HttpResponseMessage closeResponse = await client.DeleteAsync($"/api/workspaces/{workspaceA}");
        Assert.AreEqual(204, (int)closeResponse.StatusCode);

        JsonObject listedAfterClose = await GetRequiredJsonObject(client, "/api/workspaces");
        JsonArray listedAfterCloseItems = listedAfterClose["workspaces"]?.AsArray() ?? [];
        Assert.IsFalse(listedAfterCloseItems.Any(node => string.Equals(node?["id"]?.GetValue<string>(), workspaceA, StringComparison.Ordinal)));
        Assert.IsTrue(listedAfterCloseItems.Any(node => string.Equals(node?["id"]?.GetValue<string>(), workspaceB, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Workspace_list_endpoint_honors_maxCount_query_parameter()
    {
        using var client = CreateClient();

        string xmlA = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        string xmlB = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject { ["xml"] = xmlA, ["rulesetId"] = "sr5" });
        await PostRequiredJsonObject(client, "/api/workspaces/import", new JsonObject { ["xml"] = xmlB, ["rulesetId"] = "sr5" });

        JsonObject listed = await GetRequiredJsonObject(client, "/api/workspaces?maxCount=1");
        Assert.AreEqual(1, listed["count"]?.GetValue<int>());
        JsonArray listedWorkspaces = listed["workspaces"]?.AsArray() ?? [];
        Assert.HasCount(1, listedWorkspaces);
    }

    [TestMethod]
    public async Task Workspace_import_returns_bad_request_for_invalid_summary_payload()
    {
        using var client = CreateClient();

        JsonObject payload = new()
        {
            ["xml"] = "<character><name>Broken</name><alias>X</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>not-a-number</karma><nuyen>2500</nuyen><created>True</created></character>",
            ["rulesetId"] = "sr5"
        };

        using StringContent request = new(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/workspaces/import", request);
        string body = await response.Content.ReadAsStringAsync();

        Assert.AreEqual(400, (int)response.StatusCode, body);
        StringAssert.Contains(body, "error");
    }

    [TestMethod]
    public async Task Workspace_section_endpoint_matches_legacy_section_payload_for_all_sections()
    {
        using var client = CreateClient();

        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        JsonObject payload = new()
        {
            ["xml"] = xml,
            ["rulesetId"] = "sr5"
        };

        JsonObject importResponse = await PostRequiredJsonObject(client, "/api/workspaces/import", payload);
        string workspaceId = importResponse["id"]?.GetValue<string>() ?? string.Empty;
        Assert.IsFalse(string.IsNullOrWhiteSpace(workspaceId));

        foreach (string sectionId in AllSectionIds)
        {
            JsonObject legacySection = await PostRequiredJsonObject(client, $"/api/characters/sections/{sectionId}", payload);
            JsonObject workspaceSection = await GetRequiredJsonObject(client, $"/api/workspaces/{workspaceId}/sections/{sectionId}");

            Assert.AreEqual(legacySection.ToJsonString(), workspaceSection.ToJsonString(), $"Section mismatch for '{sectionId}'.");
        }
    }

    private static string FindTestFilePath(string fileName)
    {
        string? root = Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        string[] candidates =
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Chummer.Tests", "TestFiles", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "TestFiles", fileName),
            Path.Combine(AppContext.BaseDirectory, "TestFiles", fileName),
            Path.Combine("/src", "Chummer.Tests", "TestFiles", fileName),
            string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, "Chummer.Tests", "TestFiles", fileName)
        };

        string? match = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (match is null)
            throw new FileNotFoundException("Could not locate test file.", fileName);

        return match;
    }

    private static HttpClient CreateClient(bool includeApiKey = true)
    {
        var client = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = HttpTimeout
        };

        if (includeApiKey && !string.IsNullOrWhiteSpace(ApiKey))
        {
            client.DefaultRequestHeaders.Remove("X-Api-Key");
            client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }

        return client;
    }

    private static async Task<JsonObject> GetRequiredJsonObject(HttpClient client, string relativePath)
    {
        using HttpResponseMessage response = await client.GetAsync(relativePath);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"GET {relativePath} failed with {(int)response.StatusCode}: {content}");

        return ParseRequiredJsonObject(content);
    }

    private static async Task<JsonObject> PostRequiredJsonObject(HttpClient client, string relativePath, JsonObject payload)
    {
        using var request = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync(relativePath, request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"POST {relativePath} failed with {(int)response.StatusCode}: {content}");

        return ParseRequiredJsonObject(content);
    }

    private static async Task<JsonObject> PatchRequiredJsonObject(HttpClient client, string relativePath, JsonObject payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, relativePath)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"PATCH {relativePath} failed with {(int)response.StatusCode}: {content}");

        return ParseRequiredJsonObject(content);
    }

    private static async Task<JsonObject> PutRequiredJsonObject(HttpClient client, string relativePath, JsonObject payload)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, relativePath)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
        };
        using HttpResponseMessage response = await client.SendAsync(request);
        string content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(response.IsSuccessStatusCode, $"PUT {relativePath} failed with {(int)response.StatusCode}: {content}");

        return ParseRequiredJsonObject(content);
    }

    private static async Task<JsonObject> ParseRequiredJsonObject(HttpResponseMessage response)
    {
        string content = await response.Content.ReadAsStringAsync();
        return ParseRequiredJsonObject(content);
    }

    private static JsonObject ParseRequiredJsonObject(string content)
    {
        JsonNode parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonObject>(parsed);
        return (JsonObject)parsed!;
    }

    private static JsonArray ParseRequiredJsonArray(string content)
    {
        JsonNode parsed = JsonNode.Parse(content);
        Assert.IsInstanceOfType<JsonArray>(parsed);
        return (JsonArray)parsed!;
    }

    private static async Task ClearAllWorkspacesAsync(HttpClient client)
    {
        const int maxAttempts = 20;
        const int batchSize = 500;

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            JsonObject listed = await GetRequiredJsonObject(client, $"/api/workspaces?maxCount={batchSize}");
            JsonArray workspaces = listed["workspaces"] as JsonArray ?? [];
            if (workspaces.Count == 0)
            {
                return;
            }

            int deletedCount = 0;
            foreach (JsonNode? node in workspaces)
            {
                string workspaceId = node?["id"]?.GetValue<string>() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(workspaceId))
                {
                    continue;
                }

                using HttpResponseMessage response = await client.DeleteAsync($"/api/workspaces/{workspaceId}");
                Assert.IsTrue(
                    response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound,
                    $"DELETE /api/workspaces/{workspaceId} failed with {(int)response.StatusCode}");
                deletedCount++;
            }

            if (deletedCount == 0)
            {
                break;
            }
        }

        JsonObject remaining = await GetRequiredJsonObject(client, "/api/workspaces?maxCount=1");
        JsonArray remainingWorkspaces = remaining["workspaces"] as JsonArray ?? [];
        Assert.IsEmpty(remainingWorkspaces, "Unable to clear all persisted workspaces before running test.");
    }

    private static Uri ResolveBaseUri()
    {
        string? raw = Environment.GetEnvironmentVariable("CHUMMER_API_BASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            raw = Environment.GetEnvironmentVariable("CHUMMER_WEB_BASE_URL");
        if (string.IsNullOrWhiteSpace(raw))
            raw = "http://chummer-api:8080";

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            throw new InvalidOperationException($"Invalid CHUMMER_API_BASE_URL/CHUMMER_WEB_BASE_URL: '{raw}'");

        return uri;
    }

    private static string? ResolveApiKey()
    {
        return Environment.GetEnvironmentVariable("CHUMMER_API_KEY");
    }

    private static string? ResolveExpectedAmendId()
    {
        string? configured = Environment.GetEnvironmentVariable("CHUMMER_AMENDS_EXPECTED_ID");
        if (string.IsNullOrWhiteSpace(configured))
            return null;

        return configured;
    }

    private static TimeSpan ResolveHttpTimeout()
    {
        string? raw = Environment.GetEnvironmentVariable("CHUMMER_API_TEST_TIMEOUT_SECONDS");
        if (int.TryParse(raw, out int seconds) && seconds > 0)
            return TimeSpan.FromSeconds(seconds);

        return TimeSpan.FromSeconds(45);
    }
}
