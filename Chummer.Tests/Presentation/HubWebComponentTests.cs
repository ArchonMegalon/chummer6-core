#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bunit;
using Chummer.Contracts.AI;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Presentation;
using Chummer.Hub.Web;
using Chummer.Hub.Web.Components.Pages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BunitContext = Bunit.BunitContext;

namespace Chummer.Tests.Presentation;

[TestClass]
public sealed class HubWebComponentTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [TestMethod]
    public void Home_renders_live_hub_catalog_detail_compatibility_and_install_preview()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);

        HubCatalogItem item = new(
            ItemId: "profile.street",
            Kind: HubCatalogItemKinds.RuleProfile,
            Title: "Street Session",
            Description: "A curated street-level SR5 profile.",
            RulesetId: "sr5",
            Visibility: ArtifactVisibilityModes.Private,
            TrustTier: ArtifactTrustTiers.Curated,
            LinkTarget: "/hub/profiles/profile.street",
            Version: "1.0.0",
            InstallState: ArtifactInstallStates.Available,
            Publisher: new HubPublisherSummary("pub.street", "Street Publisher", "street-publisher", HubPublisherVerificationStates.Verified, "/hub/publishers/pub.street"));
        SetupJsonResponse(
            context,
            "/api/hub/search",
            new HubCatalogResultPage(
                new BrowseQuery(string.Empty, new Dictionary<string, IReadOnlyList<string>>(), HubCatalogSortIds.Title),
                [item],
                [],
                [],
                1),
            "POST");
        SetupJsonResponse(
            context,
            "/api/hub/projects/ruleprofile/profile.street",
            new HubProjectDetailProjection(
                Summary: item,
                OwnerId: "owner-1",
                CatalogKind: "published",
                PublicationStatus: "published",
                ReviewState: "approved",
                RuntimeFingerprint: "sha256:runtime-profile",
                OwnerReview: null,
                AggregateReview: new HubReviewAggregateSummary(3, 2, 1, 0, UsedAtTableCount: 2, RatedReviewCount: 3, AverageStars: 4.5),
                Facts:
                [
                    new HubProjectDetailFact("audience", "Audience", "street-level")
                ],
                Dependencies:
                [
                    new HubProjectDependency(HubProjectDependencyKinds.IncludesRulePack, HubCatalogItemKinds.RulePack, "pack.alpha", "1.0.0")
                ],
                Actions:
                [
                    new HubProjectAction("apply-profile", "Install & Apply", HubProjectActionKinds.Apply, "/hub/profiles/profile.street/apply")
                ],
                Publisher: item.Publisher));
        SetupJsonResponse(
            context,
            "/api/hub/projects/ruleprofile/profile.street/compatibility",
            new HubProjectCompatibilityMatrix(
                Kind: HubCatalogItemKinds.RuleProfile,
                ItemId: "profile.street",
                Rows:
                [
                    new HubProjectCompatibilityRow(HubProjectCompatibilityRowKinds.Ruleset, "Ruleset", HubProjectCompatibilityStates.Compatible, "sr5"),
                    new HubProjectCompatibilityRow(HubProjectCompatibilityRowKinds.RuntimeFingerprint, "Runtime Fingerprint", HubProjectCompatibilityStates.Informational, "sha256:runtime-profile")
                ],
                GeneratedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 00, 00, TimeSpan.Zero)));
        SetupJsonResponse(
            context,
            "/api/hub/projects/ruleprofile/profile.street/install-preview",
            new HubProjectInstallPreviewReceipt(
                Kind: HubCatalogItemKinds.RuleProfile,
                ItemId: "profile.street",
                Target: new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.GlobalDefaults, "hub-preview"),
                State: HubProjectInstallPreviewStates.Ready,
                Changes:
                [
                    new HubProjectInstallPreviewChange(HubProjectInstallPreviewChangeKinds.RuntimeLockPinned, "Runtime will be pinned.", "sha256:runtime-profile")
                ],
                Diagnostics:
                [
                    new HubProjectInstallPreviewDiagnostic(HubProjectInstallPreviewDiagnosticKinds.Installability, HubProjectInstallPreviewDiagnosticSeverityLevels.Info, "Safe to install.")
                ],
                RuntimeFingerprint: "sha256:runtime-profile"),
            "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Street Session");
            StringAssert.Contains(cut.Markup, "Coach Sidecar");
            StringAssert.Contains(cut.Markup, "Curation Signals");
            StringAssert.Contains(cut.Markup, "AI Magicx");
            StringAssert.Contains(cut.Markup, "Transport: ready · base yes · model yes · keys primary 1 / fallback 0 · route coach · binding primary / slot 0");
            StringAssert.Contains(cut.Markup, "Recent Coach Guidance");
            StringAssert.Contains(cut.Markup, "Signal's clean. Keep the trust readout tight before you push.");
            StringAssert.Contains(cut.Markup, "Budget snapshot: 18 / 400 chummer-ai-units");
            StringAssert.Contains(cut.Markup, "Structured summary: Summarize trust impact, compatibility, and review posture before publication.");
            StringAssert.Contains(cut.Markup, "Recommendations: 1 · Highlight trust footprint");
            StringAssert.Contains(cut.Markup, "Evidence: 1 · Compatibility matrix");
            StringAssert.Contains(cut.Markup, "Risks: 1 · Review posture");
            StringAssert.Contains(cut.Markup, "Sources: 1 sources / 1 action drafts");
            StringAssert.Contains(cut.Markup, "data-testid=\"hub-coach-provider-transport\"");
            StringAssert.Contains(cut.Markup, "data-testid=\"hub-open-coach-thread\"");
            StringAssert.Contains(cut.Markup, "/coach/?routeType=coach&amp;conversationId=conv.hub-coach-1&amp;runtimeFingerprint=sha256%3Aruntime-profile&amp;rulesetId=sr5");
        });

        cut.Find("button[data-hub-item='profile.street']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Street Publisher");
            StringAssert.Contains(cut.Markup, "sha256:runtime-profile");
            StringAssert.Contains(cut.Markup, "street-level");
            StringAssert.Contains(cut.Markup, "data-hub-action=\"open-coach\"");
            StringAssert.Contains(cut.Markup, "/coach/?routeType=coach&amp;runtimeFingerprint=sha256%3Aruntime-profile&amp;rulesetId=sr5");
        });

        cut.FindAll("button").Single(button => button.TextContent.Contains("Preview Install", StringComparison.Ordinal)).Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Runtime will be pinned.");
            StringAssert.Contains(cut.Markup, "Safe to install.");
        });
    }

    [TestMethod]
    public void Home_surfaces_hub_search_errors_when_catalog_request_fails()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);
        SetupFailureResponse(context, "/api/hub/search", "Hub search failed.", "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Hub search failed.");
            StringAssert.Contains(cut.Markup, "error");
        });
    }

    [TestMethod]
    public void Home_loads_owner_backed_hub_drafts_and_draft_detail()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);

        SetupJsonResponse(
            context,
            "/api/hub/search",
            new HubCatalogResultPage(
                new BrowseQuery(string.Empty, new Dictionary<string, IReadOnlyList<string>>(), HubCatalogSortIds.Title),
                [],
                [],
                [],
                0),
            "POST");

        HubPublishDraftReceipt receipt = new(
            DraftId: "draft-1",
            ProjectKind: HubCatalogItemKinds.RuleProfile,
            ProjectId: "profile.street",
            RulesetId: "sr5",
            Title: "Street Session Draft",
            Summary: "Pending campaign review.",
            OwnerId: "owner-1",
            PublisherId: "pub.street",
            State: HubPublicationStates.Draft,
            CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 00, 00, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 30, 00, TimeSpan.Zero));
        SetupJsonResponse(
            context,
            "/api/hub/publish/drafts",
            new HubPublishDraftList([receipt]));
        SetupJsonResponse(
            context,
            "/api/hub/publish/drafts/draft-1",
            new HubDraftDetailProjection(
                Draft: receipt,
                Moderation: new HubModerationQueueItem(
                    CaseId: "case-1",
                    DraftId: "draft-1",
                    ProjectKind: HubCatalogItemKinds.RuleProfile,
                    ProjectId: "profile.street",
                    RulesetId: "sr5",
                    Title: "Street Session Draft",
                    OwnerId: "owner-1",
                    PublisherId: "pub.street",
                    State: HubModerationStates.PendingReview,
                    CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 00, 00, TimeSpan.Zero),
                    Summary: "Pending campaign review."),
                Description: "Initial publication draft.",
                LatestModerationNotes: "Needs publisher attribution."));

        IRenderedComponent<Home> cut = context.Render<Home>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "No hub projects matched the current query."));

        cut.Find("button[data-hub-action='load-drafts']").Click();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Street Session Draft"));

        cut.Find("button[data-hub-draft='draft-1']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Editing owner-backed draft");
            StringAssert.Contains(cut.Markup, "Needs publisher attribution.");
            StringAssert.Contains(cut.Markup, "pub.street");
        });
    }

    [TestMethod]
    public void Home_creates_updates_and_submits_hub_drafts_through_publication_routes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);

        SetupJsonResponse(
            context,
            "/api/hub/search",
            new HubCatalogResultPage(
                new BrowseQuery(string.Empty, new Dictionary<string, IReadOnlyList<string>>(), HubCatalogSortIds.Title),
                [],
                [],
                [],
                0),
            "POST");

        HubPublishDraftReceipt createdReceipt = new(
            DraftId: "draft-2",
            ProjectKind: HubCatalogItemKinds.RuleProfile,
            ProjectId: "profile.street",
            RulesetId: "sr5",
            Title: "Street Session Draft",
            Summary: "Initial summary",
            OwnerId: "owner-1",
            PublisherId: null,
            State: HubPublicationStates.Draft,
            CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 00, 00, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 12, 15, 00, TimeSpan.Zero));
        HubPublishDraftReceipt updatedReceipt = createdReceipt with
        {
            Summary = "Updated summary",
            UpdatedAtUtc = new DateTimeOffset(2026, 03, 07, 12, 45, 00, TimeSpan.Zero)
        };

        SetupJsonResponse(context, "/api/hub/publish/drafts", createdReceipt, "POST");
        SetupJsonResponse(context, "/api/hub/publish/drafts", new HubPublishDraftList([updatedReceipt]));
        SetupJsonResponse(context, "/api/hub/publish/drafts/draft-2", new HubDraftDetailProjection(
            Draft: updatedReceipt with
            {
                State = HubPublicationStates.Submitted,
                SubmittedAtUtc = new DateTimeOffset(2026, 03, 07, 13, 10, 00, TimeSpan.Zero)
            },
            Moderation: new HubModerationQueueItem(
                CaseId: "case-2",
                DraftId: "draft-2",
                ProjectKind: HubCatalogItemKinds.RuleProfile,
                ProjectId: "profile.street",
                RulesetId: "sr5",
                Title: "Street Session Draft",
                OwnerId: "owner-1",
                PublisherId: null,
                State: HubModerationStates.PendingReview,
                CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 10, 00, TimeSpan.Zero),
                Summary: "Updated summary"),
            Description: "Updated description",
            LatestModerationNotes: "Queued for moderation."));
        SetupJsonResponse(context, "/api/hub/publish/drafts/draft-2", updatedReceipt, "PUT");
        SetupJsonResponse(
            context,
            "/api/hub/publish/ruleprofile/profile.street/submit?ruleset=sr5",
            new HubProjectSubmissionReceipt(
                DraftId: "draft-2",
                CaseId: "case-2",
                ProjectKind: HubCatalogItemKinds.RuleProfile,
                ProjectId: "profile.street",
                RulesetId: "sr5",
                OwnerId: "owner-1",
                PublisherId: null,
                State: HubPublicationStates.Submitted,
                ReviewState: HubModerationStates.PendingReview,
                Notes: "Ready for review.",
                SubmittedAtUtc: new DateTimeOffset(2026, 03, 07, 13, 10, 00, TimeSpan.Zero)),
            "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "No hub projects matched the current query."));

        cut.Find("input[data-draft-field='project-id']").Change("profile.street");
        cut.Find("input[data-draft-field='title']").Change("Street Session Draft");
        cut.Find("input[data-draft-field='summary']").Change("Initial summary");
        cut.Find("textarea[data-draft-field='description']").Change("Initial description");
        cut.Find("button[data-hub-action='create-draft']").Click();

        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Created draft 'Street Session Draft'.");
            StringAssert.Contains(cut.Markup, "draft-2");
        });

        cut.Find("input[data-draft-field='summary']").Change("Updated summary");
        cut.Find("textarea[data-draft-field='description']").Change("Updated description");
        cut.Find("button[data-hub-action='save-draft']").Click();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Saved draft 'Street Session Draft'."));

        cut.Find("textarea[data-draft-field='submission-notes']").Change("Ready for review.");
        cut.Find("button[data-hub-action='submit-draft']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Submitted draft 'profile.street' for review.");
            StringAssert.Contains(cut.Markup, "Queued for moderation.");
        });
    }

    [TestMethod]
    public void Home_archives_deletes_and_lists_moderation_queue_items_through_publication_routes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);

        SetupJsonResponse(
            context,
            "/api/hub/search",
            new HubCatalogResultPage(
                new BrowseQuery(string.Empty, new Dictionary<string, IReadOnlyList<string>>(), HubCatalogSortIds.Title),
                [],
                [],
                [],
                0),
            "POST");

        HubPublishDraftReceipt receipt = new(
            DraftId: "draft-3",
            ProjectKind: HubCatalogItemKinds.RulePack,
            ProjectId: "pack.street",
            RulesetId: "sr5",
            Title: "Street Pack Draft",
            Summary: "Queued for review.",
            OwnerId: "owner-1",
            PublisherId: "pub.street",
            State: HubPublicationStates.Draft,
            CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 14, 00, 00, TimeSpan.Zero),
            UpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 14, 10, 00, TimeSpan.Zero));
        HubPublishDraftReceipt archivedReceipt = receipt with
        {
            State = HubPublicationStates.Archived,
            UpdatedAtUtc = new DateTimeOffset(2026, 03, 07, 14, 30, 00, TimeSpan.Zero)
        };

        SetupJsonResponse(
            context,
            "/api/hub/publish/drafts",
            new HubPublishDraftList([receipt]));
        SetupJsonResponse(
            context,
            "/api/hub/publish/drafts/draft-3",
            new HubDraftDetailProjection(
                Draft: receipt,
                Moderation: null,
                Description: "Archived after campaign close."));
        SetupJsonResponse(context, "/api/hub/publish/drafts/draft-3/archive", archivedReceipt, "POST");
        SetupJsonResponse(context, "/api/hub/publish/drafts/draft-3", string.Empty, "DELETE", 204);
        SetupJsonResponse(
            context,
            "/api/hub/moderation/queue?state=pending-review",
            new HubModerationQueue(
                [
                    new HubModerationQueueItem(
                        CaseId: "case-3",
                        DraftId: "draft-3",
                        ProjectKind: HubCatalogItemKinds.RulePack,
                        ProjectId: "pack.street",
                        RulesetId: "sr5",
                        Title: "Street Pack Draft",
                        OwnerId: "owner-1",
                        PublisherId: "pub.street",
                        State: HubModerationStates.PendingReview,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 14, 45, 00, TimeSpan.Zero),
                        Summary: "Queued for review.")
                ]));

        IRenderedComponent<Home> cut = context.Render<Home>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "No hub projects matched the current query."));

        cut.Find("button[data-hub-action='load-drafts']").Click();
        cut.Find("button[data-hub-draft='draft-3']").Click();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Street Pack Draft"));

        cut.Find("button[data-hub-action='archive-draft']").Click();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "Archived draft 'Street Pack Draft'."));

        cut.Find("select[data-moderation-filter='state']").Change(HubModerationStates.PendingReview);
        cut.Find("button[data-hub-action='load-moderation-queue']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "case-3");
            StringAssert.Contains(cut.Markup, HubModerationStates.PendingReview);
        });

        cut.Find("button[data-hub-action='delete-draft']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Deleted draft 'Street Pack Draft'.");
            StringAssert.Contains(cut.Markup, "Create or inspect a draft");
        });
    }

    [TestMethod]
    public void Home_approves_and_rejects_hub_moderation_queue_items_through_publication_routes()
    {
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Strict;
        RegisterHubHeadServices(context);
        SetupCoachSidecarResponses(context);

        SetupJsonResponse(
            context,
            "/api/hub/search",
            new HubCatalogResultPage(
                new BrowseQuery(string.Empty, new Dictionary<string, IReadOnlyList<string>>(), HubCatalogSortIds.Title),
                [],
                [],
                [],
                0),
            "POST");
        SetupJsonResponse(
            context,
            "/api/hub/moderation/queue",
            new HubModerationQueue(
                [
                    new HubModerationQueueItem(
                        CaseId: "case-approve",
                        DraftId: "draft-approve",
                        ProjectKind: HubCatalogItemKinds.RulePack,
                        ProjectId: "pack.alpha",
                        RulesetId: "sr5",
                        Title: "Approve Pack",
                        OwnerId: "owner-1",
                        PublisherId: "pub.alpha",
                        State: HubModerationStates.PendingReview,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 00, 00, TimeSpan.Zero)),
                    new HubModerationQueueItem(
                        CaseId: "case-reject",
                        DraftId: "draft-reject",
                        ProjectKind: HubCatalogItemKinds.RuleProfile,
                        ProjectId: "profile.beta",
                        RulesetId: "sr6",
                        Title: "Reject Profile",
                        OwnerId: "owner-2",
                        PublisherId: "pub.beta",
                        State: HubModerationStates.PendingReview,
                        CreatedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 05, 00, TimeSpan.Zero))
                ]));
        SetupJsonResponse(
            context,
            "/api/hub/moderation/queue/case-approve/approve",
            new HubModerationDecisionReceipt(
                CaseId: "case-approve",
                DraftId: "draft-approve",
                ProjectKind: HubCatalogItemKinds.RulePack,
                ProjectId: "pack.alpha",
                RulesetId: "sr5",
                OwnerId: "owner-1",
                PublisherId: "pub.alpha",
                State: HubModerationStates.Approved,
                Notes: "Looks good.",
                UpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 10, 00, TimeSpan.Zero)),
            "POST");
        SetupJsonResponse(
            context,
            "/api/hub/moderation/queue/case-reject/reject",
            new HubModerationDecisionReceipt(
                CaseId: "case-reject",
                DraftId: "draft-reject",
                ProjectKind: HubCatalogItemKinds.RuleProfile,
                ProjectId: "profile.beta",
                RulesetId: "sr6",
                OwnerId: "owner-2",
                PublisherId: "pub.beta",
                State: HubModerationStates.Rejected,
                Notes: "Needs more work.",
                UpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 15, 15, 00, TimeSpan.Zero)),
            "POST");

        IRenderedComponent<Home> cut = context.Render<Home>();
        cut.WaitForAssertion(() => StringAssert.Contains(cut.Markup, "No hub projects matched the current query."));

        cut.Find("button[data-hub-action='load-moderation-queue']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, "Approve Pack");
            StringAssert.Contains(cut.Markup, "Reject Profile");
        });

        cut.Find("textarea[data-moderation-field='notes']").Change("Looks good.");
        cut.Find("button[data-hub-approve='case-approve']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, HubModerationStates.Approved);
            StringAssert.Contains(cut.Markup, "Approved moderation case 'case-approve'.");
        });

        cut.Find("textarea[data-moderation-field='notes']").Change("Needs more work.");
        cut.Find("button[data-hub-reject='case-reject']").Click();
        cut.WaitForAssertion(() =>
        {
            StringAssert.Contains(cut.Markup, HubModerationStates.Rejected);
            StringAssert.Contains(cut.Markup, "Rejected moderation case 'case-reject'.");
        });
    }

    private static void RegisterHubHeadServices(BunitContext context)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        BrowserHubApiClient client = new(context.JSInterop.JSRuntime, configuration);
        BrowserHubCoachApiClient coachClient = new(context.JSInterop.JSRuntime, configuration);
        context.Services.AddSingleton(client);
        context.Services.AddSingleton(coachClient);
    }

    private static void SetupCoachSidecarResponses(BunitContext context)
    {
        SetupJsonResponse(
            context,
            "/api/ai/status",
            new AiGatewayStatusProjection(
                Status: "scaffolded",
                Routes: [AiRouteTypes.Chat, AiRouteTypes.Coach, AiRouteTypes.Build, AiRouteTypes.Docs, AiRouteTypes.Recap],
                Providers:
                [
                    new AiProviderDescriptor(
                        ProviderId: AiProviderIds.AiMagicx,
                        DisplayName: "AI Magicx",
                        SupportsToolCalling: true,
                        SupportsStreaming: true,
                        SupportsAttachments: false,
                        SupportsConversationMemory: true,
                        AllowedRouteTypes: [AiRouteTypes.Coach, AiRouteTypes.Build],
                        AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                        LiveExecutionEnabled: true,
                        AdapterRegistered: true,
                        IsConfigured: true,
                        PrimaryCredentialCount: 1,
                        TransportBaseUrlConfigured: true,
                        TransportModelConfigured: true,
                        TransportMetadataConfigured: true)
                ],
                Tools: [],
                RoutePolicies: [],
                RouteBudgets:
                [
                    new AiRouteBudgetPolicyDescriptor(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        BurstLimitPerMinute: 6,
                        Notes: "Coach route policy")
                ],
                RetrievalCorpora: [],
                Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6),
                PromptPolicy: "decker-contact evidence-first",
                RouteBudgetStatuses:
                [
                    new AiRouteBudgetStatusProjection(
                        RouteType: AiRouteTypes.Coach,
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        MonthlyAllowance: 400,
                        MonthlyConsumed: 18,
                        MonthlyRemaining: 382,
                        BurstLimitPerMinute: 6,
                        CurrentBurstConsumed: 1,
                        BurstRemaining: 5,
                        Notes: "Coach route budget")
                ]));
        SetupJsonResponse(
            context,
            "/api/ai/provider-health?routeType=coach",
            new[]
            {
                new AiProviderHealthProjection(
                    ProviderId: AiProviderIds.AiMagicx,
                    DisplayName: "AI Magicx",
                    AdapterKind: AiProviderAdapterKinds.RemoteHttp,
                    AdapterRegistered: true,
                    LiveExecutionEnabled: true,
                    AllowedRouteTypes: [AiRouteTypes.Coach, AiRouteTypes.Build],
                    CircuitState: AiProviderCircuitStates.Closed,
                    LastRouteType: AiRouteTypes.Coach,
                    LastCredentialTier: AiProviderCredentialTiers.Primary,
                    LastCredentialSlotIndex: 0,
                    IsConfigured: true,
                    PrimaryCredentialCount: 1,
                    FallbackCredentialCount: 0,
                    TransportBaseUrlConfigured: true,
                    TransportModelConfigured: true,
                    TransportMetadataConfigured: true,
                    LastSuccessAtUtc: new DateTimeOffset(2026, 03, 07, 14, 05, 00, TimeSpan.Zero))
            });
        SetupJsonResponse(
            context,
            "/api/ai/conversation-audits?routeType=coach&maxCount=3",
            new AiConversationAuditCatalogPage(
            [
                new AiConversationAuditSummary(
                    ConversationId: "conv.hub-coach-1",
                    RouteType: AiRouteTypes.Coach,
                    MessageCount: 2,
                    LastUpdatedAtUtc: new DateTimeOffset(2026, 03, 07, 14, 10, 00, TimeSpan.Zero),
                    RuntimeFingerprint: "sha256:runtime-profile",
                    LastAssistantAnswer: "Summarize the trust footprint before publishing.",
                    LastProviderId: AiProviderIds.AiMagicx,
                    Cache: new AiCacheMetadata(
                        Status: AiCacheStatuses.Hit,
                        CacheKey: "cache::hub::coach",
                        CachedAtUtc: new DateTimeOffset(2026, 03, 07, 14, 09, 00, TimeSpan.Zero),
                        NormalizedPrompt: "summarize trust footprint",
                        RuntimeFingerprint: "sha256:runtime-profile"),
                    RouteDecision: new AiProviderRouteDecision(
                        RouteType: AiRouteTypes.Coach,
                        ProviderId: AiProviderIds.AiMagicx,
                        Reason: "Hub curation stayed on the grounded coach lane.",
                        BudgetUnit: AiBudgetUnits.ChummerAiUnits,
                        ToolingEnabled: true,
                        CredentialTier: AiProviderCredentialTiers.Primary,
                        CredentialSlotIndex: 0),
                    GroundingCoverage: new AiGroundingCoverage(
                        ScorePercent: 100,
                        Summary: "runtime and community curation evidence present.",
                        PresentSignals: ["runtime", "retrieved"],
                        MissingSignals: [],
                        RetrievedCorpusIds: [AiRetrievalCorpusIds.Runtime, AiRetrievalCorpusIds.Community]),
                    FlavorLine: "Signal's clean. Keep the trust readout tight before you push.",
                    Budget: new AiBudgetSnapshot(AiBudgetUnits.ChummerAiUnits, 400, 18, 6, CurrentBurstConsumed: 1),
                    StructuredAnswer: new AiStructuredAnswer(
                        Summary: "Summarize trust impact, compatibility, and review posture before publication.",
                        Recommendations:
                        [
                            new AiRecommendation(
                                RecommendationId: "rec.hub.trust-summary",
                                Title: "Highlight trust footprint",
                                Reason: "Users need the capability and moderation posture before install.",
                                ExpectedEffect: "Project detail stays grounded on safety and compatibility.")
                        ],
                        Evidence:
                        [
                            new AiEvidenceEntry(
                                Title: "Compatibility matrix",
                                Summary: "The runtime compatibility lane is already available for the selected profile.")
                        ],
                        Risks:
                        [
                            new AiRiskEntry(
                                Severity: "info",
                                Title: "Review posture",
                                Summary: "Skipping trust disclosure can hide moderation requirements from users.")
                        ],
                        Confidence: "high",
                        RuntimeFingerprint: "sha256:runtime-profile",
                        Sources:
                        [
                            new AiSourceReference(
                                Kind: "runtime",
                                Title: "Runtime compatibility",
                                ReferenceId: "sha256:runtime-profile",
                                Source: "hub")
                        ],
                        ActionDrafts:
                        [
                            new AiActionDraft(
                                ActionId: AiSuggestedActionIds.OpenRuntimeInspector,
                                Title: "Open Runtime Inspector",
                                Description: "Review the grounded runtime impact before publishing.",
                                RuntimeFingerprint: "sha256:runtime-profile")
                        ]))
            ],
            1));
    }

    private static void SetupJsonResponse<T>(BunitContext context, string path, T payload, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerHubApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(200, JsonSerializer.Serialize(payload, JsonOptions)));
    }

    private static void SetupJsonResponse(BunitContext context, string path, string text, string method, int status)
    {
        context.JSInterop
            .Setup<string>(
                "chummerHubApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(status, text));
    }

    private static void SetupFailureResponse(BunitContext context, string path, string message, string method = "GET")
    {
        context.JSInterop
            .Setup<string>(
                "chummerHubApi.send",
                invocation => invocation.Arguments.Count >= 2
                    && string.Equals(invocation.Arguments[0]?.ToString(), path, StringComparison.Ordinal)
                    && string.Equals(invocation.Arguments[1]?.ToString(), method, StringComparison.Ordinal))
            .SetResult(CreateEnvelope(500, JsonSerializer.Serialize(new { message }, JsonOptions)));
    }

    private static string CreateEnvelope(int status, string text)
        => JsonSerializer.Serialize(new
        {
            status,
            text
        }, JsonOptions);
}
