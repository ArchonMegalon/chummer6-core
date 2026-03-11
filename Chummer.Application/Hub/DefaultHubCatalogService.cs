using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Hub;

public sealed class DefaultHubCatalogService : IHubCatalogService
{
    private readonly IRulesetPluginRegistry _rulesetPluginRegistry;
    private readonly IRulePackInstallHistoryStore _rulePackInstallHistoryStore;
    private readonly IRulePackRegistryService _rulePackRegistryService;
    private readonly IRuleProfileInstallHistoryStore _ruleProfileInstallHistoryStore;
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IBuildKitRegistryService _buildKitRegistryService;
    private readonly IHubReviewService _hubReviewService;
    private readonly IHubPublisherStore _hubPublisherStore;
    private readonly INpcVaultRegistryService _npcVaultRegistryService;
    private readonly IRuntimeLockInstallHistoryStore _runtimeLockInstallHistoryStore;
    private readonly IRuntimeLockRegistryService _runtimeLockRegistryService;

    public DefaultHubCatalogService(
        IRulesetPluginRegistry rulesetPluginRegistry,
        IRulePackInstallHistoryStore rulePackInstallHistoryStore,
        IRulePackRegistryService rulePackRegistryService,
        IRuleProfileInstallHistoryStore ruleProfileInstallHistoryStore,
        IRuleProfileRegistryService ruleProfileRegistryService,
        IBuildKitRegistryService buildKitRegistryService,
        IHubReviewService hubReviewService,
        IHubPublisherStore hubPublisherStore,
        INpcVaultRegistryService npcVaultRegistryService,
        IRuntimeLockInstallHistoryStore runtimeLockInstallHistoryStore,
        IRuntimeLockRegistryService runtimeLockRegistryService)
    {
        _rulesetPluginRegistry = rulesetPluginRegistry;
        _rulePackInstallHistoryStore = rulePackInstallHistoryStore;
        _rulePackRegistryService = rulePackRegistryService;
        _ruleProfileInstallHistoryStore = ruleProfileInstallHistoryStore;
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _buildKitRegistryService = buildKitRegistryService;
        _hubReviewService = hubReviewService;
        _hubPublisherStore = hubPublisherStore;
        _npcVaultRegistryService = npcVaultRegistryService;
        _runtimeLockInstallHistoryStore = runtimeLockInstallHistoryStore;
        _runtimeLockRegistryService = runtimeLockRegistryService;
    }

    public HubCatalogResultPage Search(OwnerScope owner, BrowseQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        HubCatalogItem[] allItems = EnumerateAllItems(owner).ToArray();
        HubCatalogItem[] filtered = allItems
            .Where(item => MatchesQueryText(item, query.QueryText))
            .Where(item => MatchesFacets(item, query.FacetSelections))
            .ToArray();
        HubCatalogItem[] sorted = Sort(filtered, query).ToArray();
        HubCatalogItem[] paged = sorted
            .Skip(Math.Max(0, query.Offset))
            .Take(Math.Max(1, query.Limit))
            .ToArray();

        return new HubCatalogResultPage(
            Query: query,
            Items: paged,
            Facets: BuildFacets(filtered, query),
            Sorts: BuildSorts(query),
            TotalCount: filtered.Length);
    }

    public HubProjectDetailProjection? GetProjectDetail(OwnerScope owner, string kind, string itemId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);

        string normalizedKind = HubCatalogItemKinds.NormalizeRequired(kind);

        return normalizedKind switch
        {
            HubCatalogItemKinds.RulePack => GetRulePackDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.RuleProfile => GetRuleProfileDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.BuildKit => GetBuildKitDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.NpcEntry => GetNpcEntryDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.NpcPack => GetNpcPackDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.EncounterPack => GetEncounterPackDetail(owner, itemId, rulesetId),
            HubCatalogItemKinds.RuntimeLock => GetRuntimeLockDetail(owner, itemId, rulesetId),
            _ => null
        };
    }

    private IEnumerable<HubCatalogItem> EnumerateAllItems(OwnerScope owner)
    {
        foreach (IRulesetPlugin plugin in _rulesetPluginRegistry.All)
        {
            string rulesetId = plugin.Id.NormalizedValue;

            foreach (RulePackRegistryEntry entry in _rulePackRegistryService.List(owner, rulesetId))
            {
                HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RulePack, entry.Manifest.PackId, rulesetId);
                HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RulePack, entry.Manifest.PackId, rulesetId);
                HubPublisherSummary? publisher = ResolvePublisherSummary(entry.Publication.OwnerId, entry.Publication.PublisherId);
                yield return ToCatalogItem(
                    rulesetId,
                    entry,
                    ownerReview,
                    aggregateReview,
                    publisher);
            }

            foreach (BuildKitRegistryEntry entry in _buildKitRegistryService.List(owner, rulesetId))
            {
                HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.BuildKit, entry.Manifest.BuildKitId, rulesetId);
                HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.BuildKit, entry.Manifest.BuildKitId, rulesetId);
                yield return ToCatalogItem(
                    rulesetId,
                    entry,
                    ownerReview,
                    aggregateReview);
            }

            foreach (NpcEntryRegistryEntry entry in _npcVaultRegistryService.ListEntries(owner, rulesetId))
            {
                HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.NpcEntry, entry.Manifest.EntryId, rulesetId);
                HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.NpcEntry, entry.Manifest.EntryId, rulesetId);
                yield return ToCatalogItem(entry, ownerReview, aggregateReview);
            }

            foreach (NpcPackRegistryEntry entry in _npcVaultRegistryService.ListPacks(owner, rulesetId))
            {
                HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.NpcPack, entry.Manifest.PackId, rulesetId);
                HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.NpcPack, entry.Manifest.PackId, rulesetId);
                yield return ToCatalogItem(entry, ownerReview, aggregateReview);
            }

            foreach (EncounterPackRegistryEntry entry in _npcVaultRegistryService.ListEncounterPacks(owner, rulesetId))
            {
                HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.EncounterPack, entry.Manifest.EncounterPackId, rulesetId);
                HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.EncounterPack, entry.Manifest.EncounterPackId, rulesetId);
                yield return ToCatalogItem(entry, ownerReview, aggregateReview);
            }
        }

        foreach (RuleProfileRegistryEntry entry in _ruleProfileRegistryService.List(owner))
        {
            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RuleProfile, entry.Manifest.ProfileId, entry.Manifest.RulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RuleProfile, entry.Manifest.ProfileId, entry.Manifest.RulesetId);
            HubPublisherSummary? publisher = ResolvePublisherSummary(entry.Publication.OwnerId, entry.Publication.PublisherId);
            yield return ToCatalogItem(
                entry,
                ownerReview,
                aggregateReview,
                publisher);
        }

        foreach (RuntimeLockRegistryEntry entry in _runtimeLockRegistryService.List(owner).Entries)
        {
            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RuntimeLock, entry.LockId, entry.RuntimeLock.RulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RuntimeLock, entry.LockId, entry.RuntimeLock.RulesetId);
            yield return ToCatalogItem(
                entry,
                ownerReview,
                aggregateReview);
        }
    }

    private HubProjectDetailProjection? GetRulePackDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            RulePackRegistryEntry? entry = _rulePackRegistryService.Get(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            HubProjectDetailFact[] installHistoryFacts = CreateInstallHistoryFacts(
                _rulePackInstallHistoryStore.GetHistory(owner, entry.Manifest.PackId, entry.Manifest.Version, candidateRulesetId)
                    .Select(record => record.Entry));
            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RulePack, entry.Manifest.PackId, candidateRulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RulePack, entry.Manifest.PackId, candidateRulesetId);
            HubPublisherSummary? publisher = ResolvePublisherSummary(entry.Publication.OwnerId, entry.Publication.PublisherId);

            return new HubProjectDetailProjection(
                Summary: ToCatalogItem(
                    candidateRulesetId,
                    entry,
                    ownerReview,
                    aggregateReview,
                    publisher),
                OwnerId: entry.Publication.OwnerId,
                CatalogKind: null,
                PublicationStatus: entry.Publication.PublicationStatus,
                ReviewState: entry.Publication.Review.State,
                RuntimeFingerprint: null,
                OwnerReview: ownerReview,
                AggregateReview: aggregateReview,
                Facts:
                [
                    new HubProjectDetailFact("source-kind", "Source Kind", entry.SourceKind),
                    new HubProjectDetailFact("install-state", "Install State", entry.Install.State),
                    .. installHistoryFacts,
                    new HubProjectDetailFact("engine-api", "Engine API", entry.Manifest.EngineApiVersion),
                    new HubProjectDetailFact("asset-count", "Assets", entry.Manifest.Assets.Count.ToString()),
                    new HubProjectDetailFact("capability-count", "Capabilities", entry.Manifest.Capabilities.Count.ToString()),
                    new HubProjectDetailFact("execution-policy-count", "Execution Policies", entry.Manifest.ExecutionPolicies.Count.ToString())
                ],
                Dependencies:
                [
                    .. entry.Manifest.DependsOn.Select(reference =>
                        new HubProjectDependency(HubProjectDependencyKinds.DependsOn, HubCatalogItemKinds.RulePack, reference.Id, reference.Version)),
                    .. entry.Manifest.ConflictsWith.Select(reference =>
                        new HubProjectDependency(HubProjectDependencyKinds.ConflictsWith, HubCatalogItemKinds.RulePack, reference.Id, reference.Version))
                ],
                Actions:
                [
                    new HubProjectAction("install-rulepack", "Install", HubProjectActionKinds.Install, LinkTarget: $"/hub/rulepacks/{entry.Manifest.PackId}/install"),
                    new HubProjectAction("open-rulepack", "Open Registry Entry", HubProjectActionKinds.OpenRegistry, LinkTarget: $"/hub/rulepacks/{entry.Manifest.PackId}")
                ],
                Capabilities: BuildRulePackCapabilities(candidateRulesetId, entry),
                Publisher: publisher);
        }

        return null;
    }

    private HubProjectDetailProjection? GetRuleProfileDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        RuleProfileRegistryEntry? entry = _ruleProfileRegistryService.Get(owner, itemId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        HubProjectDetailFact[] installHistoryFacts = CreateInstallHistoryFacts(
            _ruleProfileInstallHistoryStore.GetHistory(owner, entry.Manifest.ProfileId, entry.Manifest.RulesetId)
                .Select(record => record.Entry));
        HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RuleProfile, entry.Manifest.ProfileId, entry.Manifest.RulesetId);
        HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RuleProfile, entry.Manifest.ProfileId, entry.Manifest.RulesetId);
        HubPublisherSummary? publisher = ResolvePublisherSummary(entry.Publication.OwnerId, entry.Publication.PublisherId);

        return new HubProjectDetailProjection(
            Summary: ToCatalogItem(
                entry,
                ownerReview,
                aggregateReview,
                publisher),
            OwnerId: entry.Publication.OwnerId,
            CatalogKind: entry.Manifest.CatalogKind,
            PublicationStatus: entry.Publication.PublicationStatus,
            ReviewState: entry.Publication.Review.State,
            RuntimeFingerprint: entry.Manifest.RuntimeLock.RuntimeFingerprint,
            OwnerReview: ownerReview,
            AggregateReview: aggregateReview,
            Facts:
            [
                new HubProjectDetailFact("source-kind", "Source Kind", entry.SourceKind),
                new HubProjectDetailFact("install-state", "Install State", entry.Install.State),
                .. installHistoryFacts,
                new HubProjectDetailFact("audience", "Audience", entry.Manifest.Audience),
                new HubProjectDetailFact("update-channel", "Update Channel", entry.Manifest.UpdateChannel),
                new HubProjectDetailFact("default-toggle-count", "Default Toggles", entry.Manifest.DefaultToggles.Count.ToString()),
                new HubProjectDetailFact("runtime-fingerprint", "Runtime Fingerprint", entry.Manifest.RuntimeLock.RuntimeFingerprint)
            ],
            Dependencies:
            [
                .. entry.Manifest.RulePacks.Select(selection =>
                    new HubProjectDependency(
                        HubProjectDependencyKinds.IncludesRulePack,
                        HubCatalogItemKinds.RulePack,
                        selection.RulePack.Id,
                        selection.RulePack.Version,
                        Notes: selection.Required ? "required" : "optional"))
            ],
            Actions:
            [
                new HubProjectAction("preview-profile-runtime", "Preview Runtime", HubProjectActionKinds.PreviewRuntime, LinkTarget: $"/api/profiles/{entry.Manifest.ProfileId}/preview"),
                new HubProjectAction("apply-profile", "Install & Apply", HubProjectActionKinds.Apply, LinkTarget: $"/hub/profiles/{entry.Manifest.ProfileId}/apply"),
                new HubProjectAction("inspect-profile-runtime", "Inspect Runtime", HubProjectActionKinds.InspectRuntime, LinkTarget: $"/hub/runtime/profiles/{entry.Manifest.ProfileId}")
            ],
            Capabilities: BuildRuntimeCapabilities(
                entry.Manifest.RulesetId,
                entry.Manifest.RuntimeLock.ProviderBindings,
                entry.Manifest.RuntimeLock.RulePacks.Select(reference => reference.Id)),
            Publisher: publisher);
    }

    private HubProjectDetailProjection? GetBuildKitDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            BuildKitRegistryEntry? entry = _buildKitRegistryService.Get(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }
            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.BuildKit, entry.Manifest.BuildKitId, candidateRulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.BuildKit, entry.Manifest.BuildKitId, candidateRulesetId);

            return new HubProjectDetailProjection(
                Summary: ToCatalogItem(
                    candidateRulesetId,
                    entry,
                    ownerReview,
                    aggregateReview),
                OwnerId: entry.Owner.NormalizedValue,
                CatalogKind: null,
                PublicationStatus: entry.PublicationStatus,
                ReviewState: null,
                RuntimeFingerprint: null,
                OwnerReview: ownerReview,
                AggregateReview: aggregateReview,
                Facts:
                [
                    new HubProjectDetailFact("prompt-count", "Prompts", entry.Manifest.Prompts.Count.ToString()),
                    new HubProjectDetailFact("action-count", "Actions", entry.Manifest.Actions.Count.ToString()),
                    new HubProjectDetailFact("runtime-requirement-count", "Runtime Requirements", entry.Manifest.RuntimeRequirements.Count.ToString())
                ],
                Dependencies:
                [
                    .. entry.Manifest.RuntimeRequirements.SelectMany(requirement => requirement.RequiredRulePacks.Select(reference =>
                        new HubProjectDependency(
                            HubProjectDependencyKinds.RequiresRulePack,
                            HubCatalogItemKinds.RulePack,
                            reference.Id,
                            reference.Version,
                            Notes: requirement.RulesetId))),
                    .. entry.Manifest.RuntimeRequirements.SelectMany(requirement => requirement.RequiredRuntimeFingerprints.Select(fingerprint =>
                        new HubProjectDependency(
                            HubProjectDependencyKinds.RequiresRuntimeFingerprint,
                            HubCatalogItemKinds.RuntimeLock,
                            fingerprint,
                            fingerprint,
                            Notes: requirement.RulesetId)))
                ],
                Actions:
                [
                    new HubProjectAction("apply-buildkit", "Apply BuildKit", HubProjectActionKinds.Apply, LinkTarget: $"/hub/buildkits/{entry.Manifest.BuildKitId}/apply"),
                    new HubProjectAction("open-buildkit", "Open Registry Entry", HubProjectActionKinds.OpenRegistry, LinkTarget: $"/hub/buildkits/{entry.Manifest.BuildKitId}")
                ],
                Capabilities: []);
        }

        return null;
    }

    private HubProjectDetailProjection? GetRuntimeLockDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        RuntimeLockRegistryEntry? entry = _runtimeLockRegistryService.Get(owner, itemId, rulesetId);
        if (entry is null)
        {
            return null;
        }

        HubProjectDetailFact[] installHistoryFacts = CreateInstallHistoryFacts(
            _runtimeLockInstallHistoryStore.GetHistory(owner, entry.LockId, entry.RuntimeLock.RulesetId)
                .Select(record => record.Entry));
        HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.RuntimeLock, entry.LockId, entry.RuntimeLock.RulesetId);
        HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.RuntimeLock, entry.LockId, entry.RuntimeLock.RulesetId);

        return new HubProjectDetailProjection(
            Summary: ToCatalogItem(
                entry,
                ownerReview,
                aggregateReview),
            OwnerId: entry.Owner.NormalizedValue,
            CatalogKind: entry.CatalogKind,
            PublicationStatus: null,
            ReviewState: null,
            RuntimeFingerprint: entry.RuntimeLock.RuntimeFingerprint,
            OwnerReview: ownerReview,
            AggregateReview: aggregateReview,
            Facts:
            [
                new HubProjectDetailFact("install-state", "Install State", entry.Install.State),
                .. installHistoryFacts,
                new HubProjectDetailFact("engine-api", "Engine API", entry.RuntimeLock.EngineApiVersion),
                new HubProjectDetailFact("content-bundle-count", "Content Bundles", entry.RuntimeLock.ContentBundles.Count.ToString()),
                new HubProjectDetailFact("rulepack-count", "RulePacks", entry.RuntimeLock.RulePacks.Count.ToString()),
                new HubProjectDetailFact("provider-binding-count", "Provider Bindings", entry.RuntimeLock.ProviderBindings.Count.ToString())
            ],
            Dependencies:
            [
                .. entry.RuntimeLock.RulePacks.Select(reference =>
                    new HubProjectDependency(HubProjectDependencyKinds.IncludesRulePack, HubCatalogItemKinds.RulePack, reference.Id, reference.Version))
            ],
            Actions:
            [
                new HubProjectAction("install-runtime-lock", "Install Runtime Lock", HubProjectActionKinds.Install, LinkTarget: $"/hub/runtime-locks/{entry.LockId}/install"),
                new HubProjectAction("inspect-runtime-lock", "Inspect Runtime", HubProjectActionKinds.InspectRuntime, LinkTarget: $"/hub/runtime-locks/{entry.LockId}")
            ],
            Capabilities: BuildRuntimeCapabilities(
                entry.RuntimeLock.RulesetId,
                entry.RuntimeLock.ProviderBindings,
                entry.RuntimeLock.RulePacks.Select(reference => reference.Id)));
    }

    private HubProjectDetailProjection? GetNpcEntryDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            NpcEntryRegistryEntry? entry = _npcVaultRegistryService.GetEntry(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.NpcEntry, entry.Manifest.EntryId, candidateRulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.NpcEntry, entry.Manifest.EntryId, candidateRulesetId);
            return new HubProjectDetailProjection(
                Summary: ToCatalogItem(entry, ownerReview, aggregateReview),
                OwnerId: entry.Owner.NormalizedValue,
                CatalogKind: null,
                PublicationStatus: entry.PublicationStatus,
                ReviewState: null,
                RuntimeFingerprint: entry.Manifest.RuntimeFingerprint,
                OwnerReview: ownerReview,
                AggregateReview: aggregateReview,
                Facts:
                [
                    new HubProjectDetailFact("threat-tier", "Threat Tier", entry.Manifest.ThreatTier),
                    new HubProjectDetailFact("session-ready", "Session Ready", entry.Manifest.SessionReady ? "true" : "false"),
                    new HubProjectDetailFact("gm-board-ready", "GM Board Ready", entry.Manifest.GmBoardReady ? "true" : "false"),
                    new HubProjectDetailFact("tag-count", "Tags", (entry.Manifest.Tags?.Count ?? 0).ToString())
                ],
                Dependencies: [],
                Actions:
                [
                    new HubProjectAction("clone-npc-entry", "Clone to Library", HubProjectActionKinds.CloneToLibrary, LinkTarget: $"/hub/npcs/{entry.Manifest.EntryId}/clone"),
                    new HubProjectAction("open-npc-entry", "Open Registry Entry", HubProjectActionKinds.OpenRegistry, LinkTarget: $"/hub/npcs/{entry.Manifest.EntryId}")
                ],
                Capabilities: []);
        }

        return null;
    }

    private HubProjectDetailProjection? GetNpcPackDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            NpcPackRegistryEntry? entry = _npcVaultRegistryService.GetPack(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.NpcPack, entry.Manifest.PackId, candidateRulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.NpcPack, entry.Manifest.PackId, candidateRulesetId);
            return new HubProjectDetailProjection(
                Summary: ToCatalogItem(entry, ownerReview, aggregateReview),
                OwnerId: entry.Owner.NormalizedValue,
                CatalogKind: null,
                PublicationStatus: entry.PublicationStatus,
                ReviewState: null,
                RuntimeFingerprint: null,
                OwnerReview: ownerReview,
                AggregateReview: aggregateReview,
                Facts:
                [
                    new HubProjectDetailFact("entry-count", "Entries", entry.Manifest.Entries.Count.ToString()),
                    new HubProjectDetailFact("session-ready", "Session Ready", entry.Manifest.SessionReady ? "true" : "false"),
                    new HubProjectDetailFact("gm-board-ready", "GM Board Ready", entry.Manifest.GmBoardReady ? "true" : "false"),
                    new HubProjectDetailFact("tag-count", "Tags", (entry.Manifest.Tags?.Count ?? 0).ToString())
                ],
                Dependencies:
                [
                    .. entry.Manifest.Entries.Select(reference =>
                        new HubProjectDependency(HubProjectDependencyKinds.IncludesNpcEntry, HubCatalogItemKinds.NpcEntry, reference.EntryId, reference.Quantity.ToString()))
                ],
                Actions:
                [
                    new HubProjectAction("clone-npc-pack", "Clone to Library", HubProjectActionKinds.CloneToLibrary, LinkTarget: $"/hub/npc-packs/{entry.Manifest.PackId}/clone"),
                    new HubProjectAction("open-npc-pack", "Open Registry Entry", HubProjectActionKinds.OpenRegistry, LinkTarget: $"/hub/npc-packs/{entry.Manifest.PackId}")
                ],
                Capabilities: []);
        }

        return null;
    }

    private HubProjectDetailProjection? GetEncounterPackDetail(OwnerScope owner, string itemId, string? rulesetId)
    {
        foreach (string candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            EncounterPackRegistryEntry? entry = _npcVaultRegistryService.GetEncounterPack(owner, itemId, candidateRulesetId);
            if (entry is null)
            {
                continue;
            }

            HubReviewSummary? ownerReview = GetOwnerReviewSummary(owner, HubCatalogItemKinds.EncounterPack, entry.Manifest.EncounterPackId, candidateRulesetId);
            HubReviewAggregateSummary? aggregateReview = GetAggregateReviewSummary(HubCatalogItemKinds.EncounterPack, entry.Manifest.EncounterPackId, candidateRulesetId);
            return new HubProjectDetailProjection(
                Summary: ToCatalogItem(entry, ownerReview, aggregateReview),
                OwnerId: entry.Owner.NormalizedValue,
                CatalogKind: null,
                PublicationStatus: entry.PublicationStatus,
                ReviewState: null,
                RuntimeFingerprint: null,
                OwnerReview: ownerReview,
                AggregateReview: aggregateReview,
                Facts:
                [
                    new HubProjectDetailFact("participant-count", "Participants", entry.Manifest.Participants.Count.ToString()),
                    new HubProjectDetailFact("session-ready", "Session Ready", entry.Manifest.SessionReady ? "true" : "false"),
                    new HubProjectDetailFact("gm-board-ready", "GM Board Ready", entry.Manifest.GmBoardReady ? "true" : "false"),
                    new HubProjectDetailFact("tag-count", "Tags", (entry.Manifest.Tags?.Count ?? 0).ToString())
                ],
                Dependencies:
                [
                    .. entry.Manifest.Participants.Select(reference =>
                        new HubProjectDependency(HubProjectDependencyKinds.IncludesNpcEntry, HubCatalogItemKinds.NpcEntry, reference.EntryId, reference.Quantity.ToString(), reference.Role))
                ],
                Actions:
                [
                    new HubProjectAction("clone-encounter-pack", "Clone to Library", HubProjectActionKinds.CloneToLibrary, LinkTarget: $"/hub/encounters/{entry.Manifest.EncounterPackId}/clone"),
                    new HubProjectAction("open-encounter-pack", "Open Registry Entry", HubProjectActionKinds.OpenRegistry, LinkTarget: $"/hub/encounters/{entry.Manifest.EncounterPackId}")
                ],
                Capabilities: []);
        }

        return null;
    }

    private IEnumerable<string> EnumerateRulesetIds(string? rulesetId)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        if (normalizedRulesetId is not null)
        {
            yield return normalizedRulesetId;
            yield break;
        }

        foreach (IRulesetPlugin plugin in _rulesetPluginRegistry.All)
        {
            yield return plugin.Id.NormalizedValue;
        }
    }

    private HubReviewSummary? GetOwnerReviewSummary(OwnerScope owner, string kind, string itemId, string? rulesetId)
    {
        HubReviewCatalog? catalog = _hubReviewService.ListReviews(owner, kind, itemId, rulesetId).Payload;
        if (catalog?.Items is null || catalog.Items.Count == 0)
        {
            return null;
        }

        HubReviewReceipt review = catalog.Items[0];
        return new HubReviewSummary(
            RecommendationState: review.RecommendationState,
            Stars: review.Stars,
            UsedAtTable: review.UsedAtTable,
            ReviewText: review.ReviewText,
            UpdatedAtUtc: review.UpdatedAtUtc);
    }

    private HubReviewAggregateSummary? GetAggregateReviewSummary(string kind, string itemId, string? rulesetId)
    {
        HubReviewAggregateSummary? summary = _hubReviewService.GetAggregateSummary(kind, itemId, rulesetId).Payload;
        return summary is null || summary.TotalReviews == 0
            ? null
            : summary;
    }

    private HubPublisherSummary? ResolvePublisherSummary(string? ownerId, string? publisherId)
    {
        if (string.IsNullOrWhiteSpace(ownerId) || string.IsNullOrWhiteSpace(publisherId))
        {
            return null;
        }

        HubPublisherRecord? publisher = _hubPublisherStore.Get(new OwnerScope(ownerId), publisherId);
        return publisher is null
            ? null
            : new HubPublisherSummary(
                PublisherId: publisher.PublisherId,
                DisplayName: publisher.DisplayName,
                Slug: publisher.Slug,
                VerificationState: publisher.VerificationState,
                LinkTarget: $"/hub/publishers/{publisher.PublisherId}");
    }

    private static HubCatalogItem ToCatalogItem(
        string rulesetId,
        RulePackRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null,
        HubPublisherSummary? publisher = null) => new(
        ItemId: entry.Manifest.PackId,
        Kind: HubCatalogItemKinds.RulePack,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: rulesetId,
        Visibility: entry.Publication.Visibility,
        TrustTier: entry.Manifest.TrustTier,
        LinkTarget: $"/hub/rulepacks/{entry.Manifest.PackId}",
        Version: entry.Manifest.Version,
        InstallState: entry.Install.State,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview,
        Publisher: publisher);

    private static HubCatalogItem ToCatalogItem(
        string rulesetId,
        BuildKitRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null) => new(
        ItemId: entry.Manifest.BuildKitId,
        Kind: HubCatalogItemKinds.BuildKit,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: rulesetId,
        Visibility: entry.Visibility,
        TrustTier: entry.Manifest.TrustTier,
        LinkTarget: $"/hub/buildkits/{entry.Manifest.BuildKitId}",
        Version: entry.Manifest.Version,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview);

    private static HubCatalogItem ToCatalogItem(
        RuleProfileRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null,
        HubPublisherSummary? publisher = null) => new(
        ItemId: entry.Manifest.ProfileId,
        Kind: HubCatalogItemKinds.RuleProfile,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: entry.Manifest.RulesetId,
        Visibility: entry.Publication.Visibility,
        TrustTier: ResolveTrustTier(entry.Publication.Visibility),
        LinkTarget: $"/hub/profiles/{entry.Manifest.ProfileId}",
        Version: entry.Manifest.RuntimeLock.RuntimeFingerprint,
        InstallState: entry.Install.State,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview,
        Publisher: publisher);

    private static HubCatalogItem ToCatalogItem(
        RuntimeLockRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null) => new(
        ItemId: entry.LockId,
        Kind: HubCatalogItemKinds.RuntimeLock,
        Title: entry.Title,
        Description: entry.Description ?? string.Empty,
        RulesetId: entry.RuntimeLock.RulesetId,
        Visibility: entry.Visibility,
        TrustTier: ResolveTrustTier(entry.Visibility),
        LinkTarget: $"/hub/runtime-locks/{entry.LockId}",
        Version: entry.RuntimeLock.RuntimeFingerprint,
        Installable: true,
        InstallState: entry.Install.State,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview);

    private static HubCatalogItem ToCatalogItem(
        NpcEntryRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null) => new(
        ItemId: entry.Manifest.EntryId,
        Kind: HubCatalogItemKinds.NpcEntry,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: entry.Manifest.RulesetId,
        Visibility: entry.Manifest.Visibility,
        TrustTier: entry.Manifest.TrustTier,
        LinkTarget: $"/hub/npcs/{entry.Manifest.EntryId}",
        Version: entry.Manifest.Version,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview);

    private static HubCatalogItem ToCatalogItem(
        NpcPackRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null) => new(
        ItemId: entry.Manifest.PackId,
        Kind: HubCatalogItemKinds.NpcPack,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: entry.Manifest.RulesetId,
        Visibility: entry.Manifest.Visibility,
        TrustTier: entry.Manifest.TrustTier,
        LinkTarget: $"/hub/npc-packs/{entry.Manifest.PackId}",
        Version: entry.Manifest.Version,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview);

    private static HubCatalogItem ToCatalogItem(
        EncounterPackRegistryEntry entry,
        HubReviewSummary? ownerReview = null,
        HubReviewAggregateSummary? aggregateReview = null) => new(
        ItemId: entry.Manifest.EncounterPackId,
        Kind: HubCatalogItemKinds.EncounterPack,
        Title: entry.Manifest.Title,
        Description: entry.Manifest.Description,
        RulesetId: entry.Manifest.RulesetId,
        Visibility: entry.Manifest.Visibility,
        TrustTier: entry.Manifest.TrustTier,
        LinkTarget: $"/hub/encounters/{entry.Manifest.EncounterPackId}",
        Version: entry.Manifest.Version,
        OwnerReview: ownerReview,
        AggregateReview: aggregateReview);

    private static string ResolveTrustTier(string visibility) =>
        string.Equals(visibility, ArtifactVisibilityModes.Public, StringComparison.Ordinal)
            ? ArtifactTrustTiers.Curated
            : ArtifactTrustTiers.LocalOnly;

    private HubProjectCapabilityDescriptorProjection[] BuildRulePackCapabilities(string rulesetId, RulePackRegistryEntry entry)
    {
        IReadOnlyDictionary<string, RulesetCapabilityDescriptor> rulesetDescriptors = GetRulesetCapabilityDescriptors(rulesetId);

        return entry.Manifest.Capabilities
            .OrderBy(capability => capability.CapabilityId, StringComparer.Ordinal)
            .Select(capability =>
            {
                rulesetDescriptors.TryGetValue(capability.CapabilityId, out RulesetCapabilityDescriptor? descriptor);
                return new HubProjectCapabilityDescriptorProjection(
                    CapabilityId: capability.CapabilityId,
                    InvocationKind: descriptor?.InvocationKind,
                    Title: descriptor?.Title,
                    Explainable: capability.Explainable || descriptor?.Explainable == true,
                    SessionSafe: capability.SessionSafe || descriptor?.SessionSafe == true,
                    DefaultGasBudget: descriptor?.DefaultGasBudget,
                    MaximumGasBudget: descriptor?.MaximumGasBudget,
                    PackId: entry.Manifest.PackId,
                    AssetKind: capability.AssetKind,
                    AssetMode: capability.AssetMode,
                    TitleKey: descriptor is null ? null : RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor),
                    TitleParameters: descriptor is null ? null : RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor));
            })
            .ToArray();
    }

    private HubProjectCapabilityDescriptorProjection[] BuildRuntimeCapabilities(
        string rulesetId,
        IReadOnlyDictionary<string, string> providerBindings,
        IEnumerable<string> packIds)
    {
        return GetRulesetCapabilityDescriptors(rulesetId)
            .Values
            .OrderBy(descriptor => descriptor.CapabilityId, StringComparer.Ordinal)
            .Select(descriptor =>
            {
                string? providerId = providerBindings.GetValueOrDefault(descriptor.CapabilityId);
                return new HubProjectCapabilityDescriptorProjection(
                    CapabilityId: descriptor.CapabilityId,
                    InvocationKind: descriptor.InvocationKind,
                    Title: descriptor.Title,
                    Explainable: descriptor.Explainable,
                    SessionSafe: descriptor.SessionSafe,
                    DefaultGasBudget: descriptor.DefaultGasBudget,
                    MaximumGasBudget: descriptor.MaximumGasBudget,
                    ProviderId: providerId,
                    PackId: providerId is null ? null : TryResolvePackId(providerId, packIds),
                    TitleKey: RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor),
                    TitleParameters: RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor));
            })
            .ToArray();
    }

    private IReadOnlyDictionary<string, RulesetCapabilityDescriptor> GetRulesetCapabilityDescriptors(string rulesetId)
    {
        IRulesetPlugin? plugin = _rulesetPluginRegistry.Resolve(rulesetId);
        if (plugin is null)
        {
            return new Dictionary<string, RulesetCapabilityDescriptor>(StringComparer.Ordinal);
        }

        return plugin.CapabilityDescriptors
            .GetCapabilityDescriptors()
            .ToDictionary(descriptor => descriptor.CapabilityId, descriptor => descriptor, StringComparer.Ordinal);
    }

    private static HubProjectDetailFact[] CreateInstallHistoryFacts(IEnumerable<ArtifactInstallHistoryEntry> historyEntries)
    {
        ArtifactInstallHistoryEntry[] history = historyEntries
            .OrderByDescending(entry => entry.AppliedAtUtc)
            .ToArray();
        if (history.Length == 0)
        {
            return [];
        }

        ArtifactInstallHistoryEntry latest = history[0];
        List<HubProjectDetailFact> facts =
        [
            new HubProjectDetailFact("install-history-count", "Install History", history.Length.ToString()),
            new HubProjectDetailFact("last-install-operation", "Last Install Operation", latest.Operation),
            new HubProjectDetailFact("last-install-at", "Last Install At", latest.AppliedAtUtc.ToString("O"))
        ];

        if (!string.IsNullOrWhiteSpace(latest.Install.InstalledTargetId))
        {
            facts.Add(new HubProjectDetailFact("last-install-target", "Last Install Target", latest.Install.InstalledTargetId));
        }

        return facts.ToArray();
    }

    private static string? TryResolvePackId(string providerId, IEnumerable<string> packIds)
    {
        foreach (string packId in packIds)
        {
            if (providerId.StartsWith($"{packId}/", StringComparison.Ordinal))
            {
                return packId;
            }
        }

        return null;
    }

    private static bool MatchesQueryText(HubCatalogItem item, string queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return true;
        }

        return item.Title.Contains(queryText, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(queryText, StringComparison.OrdinalIgnoreCase)
            || item.ItemId.Contains(queryText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesFacets(HubCatalogItem item, IReadOnlyDictionary<string, IReadOnlyList<string>> selections)
    {
        return MatchesFacet(item.Kind, selections, HubCatalogFacetIds.Kind)
            && MatchesFacet(item.RulesetId, selections, HubCatalogFacetIds.Ruleset)
            && MatchesFacet(item.Visibility, selections, HubCatalogFacetIds.Visibility)
            && MatchesFacet(item.TrustTier, selections, HubCatalogFacetIds.Trust);
    }

    private static bool MatchesFacet(string value, IReadOnlyDictionary<string, IReadOnlyList<string>> selections, string facetId)
    {
        if (!selections.TryGetValue(facetId, out IReadOnlyList<string>? selectedValues) || selectedValues.Count == 0)
        {
            return true;
        }

        return selectedValues.Contains(value, StringComparer.Ordinal);
    }

    private static IEnumerable<HubCatalogItem> Sort(IEnumerable<HubCatalogItem> items, BrowseQuery query)
    {
        Func<HubCatalogItem, string> keySelector = query.SortId switch
        {
            HubCatalogSortIds.Kind => item => item.Kind,
            HubCatalogSortIds.Ruleset => item => item.RulesetId,
            _ => item => item.Title
        };

        return string.Equals(query.SortDirection, BrowseSortDirections.Descending, StringComparison.Ordinal)
            ? items.OrderByDescending(keySelector, StringComparer.Ordinal)
            : items.OrderBy(keySelector, StringComparer.Ordinal);
    }

    private static IReadOnlyList<FacetDefinition> BuildFacets(IReadOnlyList<HubCatalogItem> filtered, BrowseQuery query)
    {
        return
        [
            BuildFacet(HubCatalogFacetIds.Kind, "Kind", filtered, query, item => item.Kind),
            BuildFacet(HubCatalogFacetIds.Ruleset, "Ruleset", filtered, query, item => item.RulesetId),
            BuildFacet(HubCatalogFacetIds.Visibility, "Visibility", filtered, query, item => item.Visibility),
            BuildFacet(HubCatalogFacetIds.Trust, "Trust", filtered, query, item => item.TrustTier)
        ];
    }

    private static FacetDefinition BuildFacet(
        string facetId,
        string label,
        IReadOnlyList<HubCatalogItem> items,
        BrowseQuery query,
        Func<HubCatalogItem, string> selector)
    {
        IReadOnlyList<string> selectedValues = query.FacetSelections.TryGetValue(facetId, out IReadOnlyList<string>? values)
            ? values
            : [];
        FacetOptionDefinition[] options = items
            .GroupBy(selector, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new FacetOptionDefinition(
                Value: group.Key,
                Label: group.Key,
                Count: group.Count(),
                Selected: selectedValues.Contains(group.Key, StringComparer.Ordinal)))
            .ToArray();

        return new FacetDefinition(
            FacetId: facetId,
            Label: label,
            Kind: BrowseFacetKinds.MultiSelect,
            Options: options,
            MultiSelect: true);
    }

    private static IReadOnlyList<SortDefinition> BuildSorts(BrowseQuery query)
    {
        return
        [
            new SortDefinition(HubCatalogSortIds.Title, "Title", BrowseSortDirections.Ascending, IsDefault: string.Equals(query.SortId, HubCatalogSortIds.Title, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(query.SortId)),
            new SortDefinition(HubCatalogSortIds.Kind, "Kind", BrowseSortDirections.Ascending, IsDefault: string.Equals(query.SortId, HubCatalogSortIds.Kind, StringComparison.Ordinal)),
            new SortDefinition(HubCatalogSortIds.Ruleset, "Ruleset", BrowseSortDirections.Ascending, IsDefault: string.Equals(query.SortId, HubCatalogSortIds.Ruleset, StringComparison.Ordinal))
        ];
    }
}
