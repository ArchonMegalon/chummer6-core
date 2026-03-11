#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Content;
using Chummer.Application.Hub;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Rulesets.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class HubCatalogServiceTests
{
    [TestMethod]
    public void Hub_catalog_service_aggregates_rulepacks_buildkits_profiles_and_runtime_locks()
    {
        DefaultHubCatalogService service = CreateService();

        HubCatalogResultPage page = service.Search(
            OwnerScope.LocalSingleUser,
            new BrowseQuery(
                QueryText: string.Empty,
                FacetSelections: new Dictionary<string, IReadOnlyList<string>>(),
                SortId: HubCatalogSortIds.Title));

        Assert.AreEqual(7, page.TotalCount);
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.RulePack));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.RuleProfile));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.BuildKit));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.NpcEntry));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.NpcPack));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.EncounterPack));
        Assert.IsTrue(page.Items.Any(item => item.Kind == HubCatalogItemKinds.RuntimeLock));
        Assert.IsTrue(page.Items.Any(item =>
            item.Kind == HubCatalogItemKinds.RuleProfile
            && item.ItemId == "official.sr5.core"
            && item.OwnerReview?.RecommendationState == HubRecommendationStates.Recommended
            && item.OwnerReview.Stars == 5
            && item.AggregateReview?.TotalReviews == 2
            && item.AggregateReview.RecommendedCount == 1
            && item.AggregateReview.NotRecommendedCount == 1
            && item.Publisher?.PublisherId == "official-sr5"));
        Assert.IsTrue(page.Items.Any(item =>
            item.Kind == HubCatalogItemKinds.RulePack
            && item.ItemId == "house-rules"
            && item.Publisher?.DisplayName == "ShadowOps"));
        Assert.IsTrue(page.Facets.Any(facet => facet.FacetId == HubCatalogFacetIds.Kind));
    }

    [TestMethod]
    public void Hub_catalog_service_returns_project_details_for_registered_catalog_kinds()
    {
        DefaultHubCatalogService service = CreateService();

        HubProjectDetailProjection? rulePack = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RulePack, "house-rules", RulesetDefaults.Sr5);
        HubProjectDetailProjection? buildKit = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.BuildKit, "street-sam-starter", RulesetDefaults.Sr5);
        HubProjectDetailProjection? npcEntry = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.NpcEntry, "red-samurai", RulesetDefaults.Sr5);
        HubProjectDetailProjection? npcPack = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.NpcPack, "renraku-security", RulesetDefaults.Sr5);
        HubProjectDetailProjection? encounterPack = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.EncounterPack, "renraku-checkpoint", RulesetDefaults.Sr5);
        HubProjectDetailProjection? ruleProfile = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RuleProfile, "official.sr5.core", RulesetDefaults.Sr5);
        HubProjectDetailProjection? runtimeLock = service.GetProjectDetail(OwnerScope.LocalSingleUser, HubCatalogItemKinds.RuntimeLock, "sha256:core", RulesetDefaults.Sr5);

        Assert.IsNotNull(rulePack);
        Assert.AreEqual(HubCatalogItemKinds.RulePack, rulePack.Summary.Kind);
        Assert.AreEqual(RulePackPublicationStatuses.Published, rulePack.PublicationStatus);
        Assert.IsTrue(rulePack.Facts.Any(fact => fact.FactId == "source-kind" && fact.Value == RegistryEntrySourceKinds.PersistedManifest));
        Assert.IsTrue(rulePack.Facts.Any(fact => fact.FactId == "engine-api"));
        Assert.IsTrue(rulePack.Facts.Any(fact => fact.FactId == "install-history-count" && fact.Value == "1"));
        Assert.IsTrue(rulePack.Facts.Any(fact => fact.FactId == "last-install-target" && fact.Value == "workspace-1"));
        Assert.IsNotNull(rulePack.Capabilities);
        Assert.IsNotNull(rulePack.Publisher);
        Assert.AreEqual("shadowops", rulePack.Publisher.PublisherId);
        Assert.IsTrue(rulePack.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.SessionQuickActions
            && capability.AssetMode == RulePackAssetModes.AddProvider
            && capability.SessionSafe
            && capability.TitleKey == "ruleset.capability.session.quick-actions.title"));

        Assert.IsNotNull(buildKit);
        Assert.AreEqual(HubCatalogItemKinds.BuildKit, buildKit.Summary.Kind);
        Assert.AreEqual(BuildKitPublicationStatuses.Published, buildKit.PublicationStatus);
        Assert.IsTrue(buildKit.Dependencies.Any(dependency => dependency.Kind == HubProjectDependencyKinds.RequiresRulePack));

        Assert.IsNotNull(npcEntry);
        Assert.AreEqual(HubCatalogItemKinds.NpcEntry, npcEntry.Summary.Kind);
        Assert.AreEqual(NpcPublicationStatuses.Published, npcEntry.PublicationStatus);
        Assert.IsTrue(npcEntry.Facts.Any(fact => fact.FactId == "threat-tier" && fact.Value == "high"));
        Assert.IsTrue(npcEntry.Actions.Any(action => action.Kind == HubProjectActionKinds.CloneToLibrary));

        Assert.IsNotNull(npcPack);
        Assert.AreEqual(HubCatalogItemKinds.NpcPack, npcPack.Summary.Kind);
        Assert.AreEqual(NpcPublicationStatuses.Published, npcPack.PublicationStatus);
        Assert.IsTrue(npcPack.Dependencies.Any(dependency =>
            dependency.Kind == HubProjectDependencyKinds.IncludesNpcEntry
            && dependency.ItemId == "red-samurai"));

        Assert.IsNotNull(encounterPack);
        Assert.AreEqual(HubCatalogItemKinds.EncounterPack, encounterPack.Summary.Kind);
        Assert.AreEqual(NpcPublicationStatuses.Published, encounterPack.PublicationStatus);
        Assert.IsTrue(encounterPack.Dependencies.Any(dependency =>
            dependency.Kind == HubProjectDependencyKinds.IncludesNpcEntry
            && dependency.Notes == "lead"));

        Assert.IsNotNull(ruleProfile);
        Assert.AreEqual(HubCatalogItemKinds.RuleProfile, ruleProfile.Summary.Kind);
        Assert.AreEqual("sha256:core", ruleProfile.RuntimeFingerprint);
        Assert.AreEqual(ArtifactInstallStates.Available, ruleProfile.Summary.InstallState);
        Assert.IsTrue(ruleProfile.Facts.Any(fact => fact.FactId == "source-kind" && fact.Value == RegistryEntrySourceKinds.PersistedManifest));
        Assert.IsTrue(ruleProfile.Actions.Any(action => action.Kind == HubProjectActionKinds.InspectRuntime));
        Assert.IsTrue(ruleProfile.Facts.Any(fact => fact.FactId == "last-install-operation" && fact.Value == ArtifactInstallHistoryOperations.Pin));
        Assert.IsNotNull(ruleProfile.OwnerReview);
        Assert.AreEqual(HubRecommendationStates.Recommended, ruleProfile.OwnerReview.RecommendationState);
        Assert.AreEqual(5, ruleProfile.OwnerReview.Stars);
        Assert.IsTrue(ruleProfile.OwnerReview.UsedAtTable);
        Assert.IsNotNull(ruleProfile.AggregateReview);
        Assert.AreEqual(2, ruleProfile.AggregateReview.TotalReviews);
        Assert.AreEqual(1, ruleProfile.AggregateReview.RecommendedCount);
        Assert.AreEqual(1, ruleProfile.AggregateReview.NotRecommendedCount);
        Assert.AreEqual(2, ruleProfile.AggregateReview.RatedReviewCount);
        Assert.AreEqual(3.5d, ruleProfile.AggregateReview.AverageStars.GetValueOrDefault(), 0.001d);
        Assert.IsNotNull(ruleProfile.Capabilities);
        Assert.IsNotNull(ruleProfile.Publisher);
        Assert.AreEqual("official-sr5", ruleProfile.Publisher.PublisherId);
        Assert.IsTrue(ruleProfile.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.DeriveStat
            && capability.InvocationKind == RulesetCapabilityInvocationKinds.Rule
            && capability.Explainable
            && capability.TitleKey == "ruleset.capability.derive.stat.title"));

        Assert.IsNotNull(runtimeLock);
        Assert.AreEqual(HubCatalogItemKinds.RuntimeLock, runtimeLock.Summary.Kind);
        Assert.AreEqual(RuntimeLockCatalogKinds.Published, runtimeLock.CatalogKind);
        Assert.AreEqual("sha256:core", runtimeLock.RuntimeFingerprint);
        Assert.AreEqual(ArtifactInstallStates.Installed, runtimeLock.Summary.InstallState);
        Assert.IsTrue(runtimeLock.Facts.Any(fact => fact.FactId == "install-state" && fact.Value == ArtifactInstallStates.Installed));
        Assert.IsTrue(runtimeLock.Facts.Any(fact => fact.FactId == "last-install-at"));
        Assert.IsNotNull(runtimeLock.Capabilities);
        Assert.IsTrue(runtimeLock.Capabilities.Any(capability =>
            capability.CapabilityId == RulePackCapabilityIds.SessionQuickActions
            && capability.SessionSafe
            && capability.TitleKey == "ruleset.capability.session.quick-actions.title"));
    }

    private static DefaultHubCatalogService CreateService() => new(
        new RulesetPluginRegistry(
        [
            new HubRulesetPluginStub(RulesetDefaults.Sr5, "Shadowrun Fifth Edition"),
            new HubRulesetPluginStub(RulesetDefaults.Sr6, "Shadowrun Sixth Edition")
        ]),
        new RulePackInstallHistoryStoreStub(
        [
            new RulePackInstallHistoryRecord(
                PackId: "house-rules",
                Version: "1.0.0",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Install,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:runtime"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00")))
        ]),
        new RulePackRegistryServiceStub(
        [
            new RulePackRegistryEntry(
                new RulePackManifest(
                    PackId: "house-rules",
                    Version: "1.0.0",
                    Title: "House Rules",
                    Author: "GM",
                    Description: "Campaign overlay.",
                    Targets: [RulesetDefaults.Sr5],
                    EngineApiVersion: "rulepack-v1",
                    DependsOn: [],
                    ConflictsWith: [],
                    Visibility: ArtifactVisibilityModes.LocalOnly,
                    TrustTier: ArtifactTrustTiers.LocalOnly,
                    Assets: [],
                    Capabilities:
                    [
                        new RulePackCapabilityDescriptor(
                            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                            AssetKind: RulePackAssetKinds.Lua,
                            AssetMode: RulePackAssetModes.AddProvider,
                            Explainable: true,
                            SessionSafe: true)
                    ],
                    ExecutionPolicies: []),
                new RulePackPublicationMetadata(
                    OwnerId: "local-single-user",
                    Visibility: ArtifactVisibilityModes.LocalOnly,
                    PublicationStatus: RulePackPublicationStatuses.Published,
                    Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                    Shares: [],
                    PublisherId: "shadowops"),
                new ArtifactInstallState(ArtifactInstallStates.Installed))
        ]),
        new RuleProfileInstallHistoryStoreStub(
        [
            new RuleProfileInstallHistoryRecord(
                ProfileId: "official.sr5.core",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Pinned,
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:05:00+00:00")))
        ]),
        new RuleProfileRegistryServiceStub(
        [
            new RuleProfileRegistryEntry(
                new RuleProfileManifest(
                    ProfileId: "official.sr5.core",
                    Title: "Official SR5 Core",
                    Description: "Curated runtime.",
                    RulesetId: RulesetDefaults.Sr5,
                    Audience: RuleProfileAudienceKinds.General,
                    CatalogKind: RuleProfileCatalogKinds.Official,
                    RulePacks: [],
                    DefaultToggles: [],
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:core"),
                    UpdateChannel: RuleProfileUpdateChannels.Stable),
                new RuleProfilePublicationMetadata(
                    OwnerId: "system",
                    Visibility: ArtifactVisibilityModes.Public,
                    PublicationStatus: RuleProfilePublicationStatuses.Published,
                    Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                    Shares: [],
                    PublisherId: "official-sr5"),
                new ArtifactInstallState(ArtifactInstallStates.Available))
        ]),
        new BuildKitRegistryServiceStub(
        [
            new BuildKitRegistryEntry(
                new BuildKitManifest(
                    BuildKitId: "street-sam-starter",
                    Version: "1.0.0",
                    Title: "Street Sam Starter",
                    Description: "Starter template.",
                    Targets: [RulesetDefaults.Sr5],
                    RuntimeRequirements:
                    [
                        new BuildKitRuntimeRequirement(
                            RulesetId: RulesetDefaults.Sr5,
                            RequiredRuntimeFingerprints: ["sha256:core"],
                            RequiredRulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")])
                    ],
                    Prompts:
                    [
                        new BuildKitPromptDescriptor(
                            PromptId: "focus",
                            Kind: BuildKitPromptKinds.Choice,
                            Label: "Combat Focus",
                            Options: [new BuildKitPromptOption("street-sam", "Street Sam")],
                            Required: true)
                    ],
                    Actions:
                    [
                        new BuildKitActionDescriptor(
                            ActionId: "starter-bundle",
                            Kind: BuildKitActionKinds.AddBundle,
                            TargetId: "starter-bundle")
                    ],
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated),
                Owner: new OwnerScope("system"),
                Visibility: ArtifactVisibilityModes.Public,
                PublicationStatus: BuildKitPublicationStatuses.Published,
                UpdatedAtUtc: DateTimeOffset.UtcNow)
        ]),
        new DefaultHubReviewService(
            new HubReviewStoreStub(
            [
                new HubReviewRecord(
                    ReviewId: "review-sr5-core",
                    ProjectKind: HubCatalogItemKinds.RuleProfile,
                    ProjectId: "official.sr5.core",
                    RulesetId: RulesetDefaults.Sr5,
                    OwnerId: OwnerScope.LocalSingleUser.NormalizedValue,
                    RecommendationState: HubRecommendationStates.Recommended,
                    CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:15:00+00:00"),
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:20:00+00:00"),
                    Stars: 5,
                    ReviewText: "Table-ready core runtime.",
                    UsedAtTable: true),
                new HubReviewRecord(
                    ReviewId: "review-sr5-core-bob",
                    ProjectKind: HubCatalogItemKinds.RuleProfile,
                    ProjectId: "official.sr5.core",
                    RulesetId: RulesetDefaults.Sr5,
                    OwnerId: "bob",
                    RecommendationState: HubRecommendationStates.NotRecommended,
                    CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:18:00+00:00"),
                    UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:25:00+00:00"),
                    Stars: 2,
                    ReviewText: "Not for my table.",
                    UsedAtTable: false)
            ])),
        new HubPublisherStoreStub(
        [
            new HubPublisherRecord(
                PublisherId: "shadowops",
                OwnerId: OwnerScope.LocalSingleUser.NormalizedValue,
                DisplayName: "ShadowOps",
                Slug: "shadowops",
                VerificationState: HubPublisherVerificationStates.Verified,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                Description: "Campaign publisher"),
            new HubPublisherRecord(
                PublisherId: "official-sr5",
                OwnerId: "system",
                DisplayName: "Official SR5",
                Slug: "official-sr5",
                VerificationState: HubPublisherVerificationStates.Official,
                CreatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                Description: "Official runtime publisher")
        ]),
        new NpcVaultRegistryServiceStub(
        [
            new NpcEntryRegistryEntry(
                new NpcEntryManifest(
                    EntryId: "red-samurai",
                    Version: "1.0.0",
                    Title: "Red Samurai",
                    Description: "Renraku elite trooper.",
                    RulesetId: RulesetDefaults.Sr5,
                    ThreatTier: "high",
                    Faction: "Renraku",
                    RuntimeFingerprint: "sha256:core",
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: ["elite", "corporate"]),
                OwnerScope.LocalSingleUser,
                NpcPublicationStatuses.Published,
                DateTimeOffset.UtcNow)
        ],
        [
            new NpcPackRegistryEntry(
                new NpcPackManifest(
                    PackId: "renraku-security",
                    Version: "1.0.0",
                    Title: "Renraku Security",
                    Description: "Security roster.",
                    RulesetId: RulesetDefaults.Sr5,
                    Entries:
                    [
                        new NpcPackMemberReference("red-samurai", 2)
                    ],
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: ["security"]),
                OwnerScope.LocalSingleUser,
                NpcPublicationStatuses.Published,
                DateTimeOffset.UtcNow)
        ],
        [
            new EncounterPackRegistryEntry(
                new EncounterPackManifest(
                    EncounterPackId: "renraku-checkpoint",
                    Version: "1.0.0",
                    Title: "Renraku Checkpoint",
                    Description: "Checkpoint encounter.",
                    RulesetId: RulesetDefaults.Sr5,
                    Participants:
                    [
                        new EncounterPackParticipantReference("red-samurai", 1, "lead")
                    ],
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: ["checkpoint"]),
                OwnerScope.LocalSingleUser,
                NpcPublicationStatuses.Published,
                DateTimeOffset.UtcNow)
        ]),
        new RuntimeLockInstallHistoryStoreStub(
        [
            new RuntimeLockInstallHistoryRecord(
                LockId: "sha256:core",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:10:00+00:00")))
        ]),
        new RuntimeLockRegistryServiceStub(
            new RuntimeLockRegistryPage(
            [
                new RuntimeLockRegistryEntry(
                    LockId: "sha256:core",
                    Owner: new OwnerScope("system"),
                    Title: "Official SR5 Core Runtime Lock",
                    Visibility: ArtifactVisibilityModes.Public,
                    CatalogKind: RuntimeLockCatalogKinds.Published,
                    RuntimeLock: new ResolvedRuntimeLock(
                        RulesetId: RulesetDefaults.Sr5,
                        ContentBundles: [],
                        RulePacks: [],
                        ProviderBindings: new Dictionary<string, string>(),
                        EngineApiVersion: "rulepack-v1",
                        RuntimeFingerprint: "sha256:core"),
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"))
            ],
                TotalCount: 1)));

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly IReadOnlyList<RulePackRegistryEntry> _entries;

        public RulePackRegistryServiceStub(IReadOnlyList<RulePackRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
        {
            if (string.IsNullOrWhiteSpace(rulesetId))
            {
                return _entries;
            }

            return _entries.Where(entry => entry.Manifest.Targets.Contains(rulesetId, StringComparer.Ordinal)).ToArray();
        }

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry => entry.Manifest.PackId == packId);
    }

    private sealed class RulePackInstallHistoryStoreStub : IRulePackInstallHistoryStore
    {
        private readonly IReadOnlyList<RulePackInstallHistoryRecord> _records;

        public RulePackInstallHistoryStoreStub(IReadOnlyList<RulePackInstallHistoryRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<RulePackInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => _records;

        public IReadOnlyList<RulePackInstallHistoryRecord> GetHistory(OwnerScope owner, string packId, string version, string rulesetId) =>
            _records.Where(record =>
                string.Equals(record.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Version, version, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal)).ToArray();

        public RulePackInstallHistoryRecord Append(OwnerScope owner, RulePackInstallHistoryRecord record) => throw new NotSupportedException();
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly IReadOnlyList<RuleProfileRegistryEntry> _entries;

        public RuleProfileRegistryServiceStub(IReadOnlyList<RuleProfileRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => _entries;

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry => entry.Manifest.ProfileId == profileId);
    }

    private sealed class RuleProfileInstallHistoryStoreStub : IRuleProfileInstallHistoryStore
    {
        private readonly IReadOnlyList<RuleProfileInstallHistoryRecord> _records;

        public RuleProfileInstallHistoryStoreStub(IReadOnlyList<RuleProfileInstallHistoryRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<RuleProfileInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => _records;

        public IReadOnlyList<RuleProfileInstallHistoryRecord> GetHistory(OwnerScope owner, string profileId, string rulesetId) =>
            _records.Where(record =>
                string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal)).ToArray();

        public RuleProfileInstallHistoryRecord Append(OwnerScope owner, RuleProfileInstallHistoryRecord record) => throw new NotSupportedException();
    }

    private sealed class RuntimeLockRegistryServiceStub : IRuntimeLockRegistryService
    {
        private readonly RuntimeLockRegistryPage _page;

        public RuntimeLockRegistryServiceStub(RuntimeLockRegistryPage page)
        {
            _page = page;
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null) => _page;

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null) =>
            _page.Entries.FirstOrDefault(entry => entry.LockId == lockId);

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request) => throw new NotSupportedException();
    }

    private sealed class RuntimeLockInstallHistoryStoreStub : IRuntimeLockInstallHistoryStore
    {
        private readonly IReadOnlyList<RuntimeLockInstallHistoryRecord> _records;

        public RuntimeLockInstallHistoryStoreStub(IReadOnlyList<RuntimeLockInstallHistoryRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<RuntimeLockInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => _records;

        public IReadOnlyList<RuntimeLockInstallHistoryRecord> GetHistory(OwnerScope owner, string lockId, string rulesetId) =>
            _records.Where(record =>
                string.Equals(record.LockId, lockId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal)).ToArray();

        public RuntimeLockInstallHistoryRecord Append(OwnerScope owner, RuntimeLockInstallHistoryRecord record) => throw new NotSupportedException();
    }

    private sealed class BuildKitRegistryServiceStub : IBuildKitRegistryService
    {
        private readonly IReadOnlyList<BuildKitRegistryEntry> _entries;

        public BuildKitRegistryServiceStub(IReadOnlyList<BuildKitRegistryEntry> entries)
        {
            _entries = entries;
        }

        public IReadOnlyList<BuildKitRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _entries
                : _entries.Where(entry => entry.Manifest.Targets.Contains(normalizedRulesetId, StringComparer.Ordinal)).ToArray();
        }

        public BuildKitRegistryEntry? Get(OwnerScope owner, string buildKitId, string? rulesetId = null) =>
            _entries.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.BuildKitId, buildKitId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(rulesetId)
                    || entry.Manifest.Targets.Contains(RulesetDefaults.NormalizeRequired(rulesetId), StringComparer.Ordinal)));
    }

    private sealed class NpcVaultRegistryServiceStub : INpcVaultRegistryService
    {
        private readonly IReadOnlyList<NpcEntryRegistryEntry> _entries;
        private readonly IReadOnlyList<NpcPackRegistryEntry> _packs;
        private readonly IReadOnlyList<EncounterPackRegistryEntry> _encounters;

        public NpcVaultRegistryServiceStub(
            IReadOnlyList<NpcEntryRegistryEntry> entries,
            IReadOnlyList<NpcPackRegistryEntry> packs,
            IReadOnlyList<EncounterPackRegistryEntry> encounters)
        {
            _entries = entries;
            _packs = packs;
            _encounters = encounters;
        }

        public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _entries
                : _entries.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null)
            => _entries.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.EntryId, entryId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(rulesetId)
                    || string.Equals(entry.Manifest.RulesetId, RulesetDefaults.NormalizeRequired(rulesetId), StringComparison.Ordinal)));

        public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _packs
                : _packs.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null)
            => _packs.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(rulesetId)
                    || string.Equals(entry.Manifest.RulesetId, RulesetDefaults.NormalizeRequired(rulesetId), StringComparison.Ordinal)));

        public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null)
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            return normalizedRulesetId is null
                ? _encounters
                : _encounters.Where(entry => string.Equals(entry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal)).ToArray();
        }

        public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null)
            => _encounters.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.EncounterPackId, encounterPackId, StringComparison.Ordinal)
                && (string.IsNullOrWhiteSpace(rulesetId)
                    || string.Equals(entry.Manifest.RulesetId, RulesetDefaults.NormalizeRequired(rulesetId), StringComparison.Ordinal)));
    }

    private sealed class HubPublisherStoreStub : IHubPublisherStore
    {
        private readonly IReadOnlyList<HubPublisherRecord> _records;

        public HubPublisherStoreStub(IReadOnlyList<HubPublisherRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<HubPublisherRecord> List(OwnerScope owner)
            => _records
                .Where(record => string.Equals(record.OwnerId, owner.NormalizedValue, StringComparison.Ordinal))
                .ToArray();

        public HubPublisherRecord? Get(OwnerScope owner, string publisherId)
            => List(owner).FirstOrDefault(record => string.Equals(record.PublisherId, publisherId, StringComparison.Ordinal));

        public HubPublisherRecord Upsert(OwnerScope owner, HubPublisherRecord record) => throw new NotSupportedException();
    }

    private sealed class HubReviewStoreStub : IHubReviewStore
    {
        private readonly IReadOnlyList<HubReviewRecord> _records;

        public HubReviewStoreStub(IReadOnlyList<HubReviewRecord> records)
        {
            _records = records;
        }

        public IReadOnlyList<HubReviewRecord> List(OwnerScope owner, string? kind = null, string? itemId = null, string? rulesetId = null)
        {
            return _records
                .Where(record => string.Equals(record.OwnerId, owner.NormalizedValue, StringComparison.Ordinal))
                .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                .Where(record => itemId is null || string.Equals(record.ProjectId, itemId, StringComparison.Ordinal))
                .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                .ToArray();
        }

        public IReadOnlyList<HubReviewRecord> ListAll(string? kind = null, string? itemId = null, string? rulesetId = null)
        {
            return _records
                .Where(record => kind is null || string.Equals(record.ProjectKind, kind, StringComparison.Ordinal))
                .Where(record => itemId is null || string.Equals(record.ProjectId, itemId, StringComparison.Ordinal))
                .Where(record => rulesetId is null || string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal))
                .ToArray();
        }

        public HubReviewRecord? Get(OwnerScope owner, string kind, string itemId, string rulesetId)
        {
            return _records.FirstOrDefault(record =>
                string.Equals(record.OwnerId, owner.NormalizedValue, StringComparison.Ordinal)
                && string.Equals(record.ProjectKind, kind, StringComparison.Ordinal)
                && string.Equals(record.ProjectId, itemId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public HubReviewRecord Upsert(OwnerScope owner, HubReviewRecord record) => throw new NotSupportedException();
    }

    private sealed class HubRulesetPluginStub : IRulesetPlugin
    {
        public HubRulesetPluginStub(string rulesetId, string displayName)
        {
            Id = new RulesetId(rulesetId);
            DisplayName = displayName;
            Serializer = new HubRulesetSerializerStub(Id);
            ShellDefinitions = new HubShellDefinitionProviderStub();
            Catalogs = new HubCatalogProviderStub();
            CapabilityDescriptors = new HubCapabilityDescriptorProviderStub();
            Capabilities = new HubCapabilityHostStub();
            Rules = new HubRuleHostStub();
            Scripts = new HubScriptHostStub();
        }

        public RulesetId Id { get; }

        public string DisplayName { get; }

        public IRulesetSerializer Serializer { get; }

        public IRulesetShellDefinitionProvider ShellDefinitions { get; }

        public IRulesetCatalogProvider Catalogs { get; }

        public IRulesetCapabilityDescriptorProvider CapabilityDescriptors { get; }

        public IRulesetCapabilityHost Capabilities { get; }

        public IRulesetRuleHost Rules { get; }

        public IRulesetScriptHost Scripts { get; }
    }

    private sealed class HubRulesetSerializerStub : IRulesetSerializer
    {
        public HubRulesetSerializerStub(RulesetId rulesetId)
        {
            RulesetId = rulesetId;
        }

        public RulesetId RulesetId { get; }

        public int SchemaVersion => 1;

        public WorkspacePayloadEnvelope Wrap(string payloadKind, string payload) => new(RulesetId.NormalizedValue, SchemaVersion, payloadKind, payload);
    }

    private sealed class HubShellDefinitionProviderStub : IRulesetShellDefinitionProvider
    {
        public IReadOnlyList<AppCommandDefinition> GetCommands() => [];

        public IReadOnlyList<NavigationTabDefinition> GetNavigationTabs() => [];
    }

    private sealed class HubCatalogProviderStub : IRulesetCatalogProvider
    {
        public IReadOnlyList<WorkspaceSurfaceActionDefinition> GetWorkspaceActions() => [];
    }

    private sealed class HubRuleHostStub : IRulesetRuleHost
    {
        public ValueTask<RulesetRuleEvaluationResult> EvaluateAsync(RulesetRuleEvaluationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetRuleEvaluationResult(true, new Dictionary<string, object?>(), []));
    }

    private sealed class HubCapabilityHostStub : IRulesetCapabilityHost
    {
        public ValueTask<RulesetCapabilityInvocationResult> InvokeAsync(RulesetCapabilityInvocationRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetCapabilityInvocationResult(
                true,
                new RulesetCapabilityValue(RulesetCapabilityValueKinds.Object, Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)),
                []));
    }

    private sealed class HubCapabilityDescriptorProviderStub : IRulesetCapabilityDescriptorProvider
    {
        public IReadOnlyList<RulesetCapabilityDescriptor> GetCapabilityDescriptors() =>
        [
            new RulesetCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                Title: "Derived Stat Evaluation",
                Explainable: true,
                SessionSafe: false,
                DefaultGasBudget: new RulesetGasBudget(2_000, 5_000, 4_194_304),
                MaximumGasBudget: new RulesetGasBudget(5_000, 10_000, 8_388_608)),
            new RulesetCapabilityDescriptor(
                CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                InvocationKind: RulesetCapabilityInvocationKinds.Script,
                Title: "Session Quick Actions",
                Explainable: true,
                SessionSafe: true,
                DefaultGasBudget: new RulesetGasBudget(2_000, 5_000, 4_194_304),
                MaximumGasBudget: new RulesetGasBudget(5_000, 10_000, 8_388_608))
        ];
    }

    private sealed class HubScriptHostStub : IRulesetScriptHost
    {
        public ValueTask<RulesetScriptExecutionResult> ExecuteAsync(RulesetScriptExecutionRequest request, CancellationToken ct) =>
            ValueTask.FromResult(new RulesetScriptExecutionResult(true, null, new Dictionary<string, object?>()));
    }
}
