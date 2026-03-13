using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class DefaultRuleProfileRegistryService : IRuleProfileRegistryService
{
    private const string EngineApiVersion = "rulepack-v1";
    private const string SystemOwnerId = "system";
    private readonly IRuleProfileInstallStateStore _installStateStore;
    private readonly IRuleProfileManifestStore _manifestStore;
    private readonly IRulesetPluginRegistry _pluginRegistry;
    private readonly IRuleProfilePublicationStore _publicationStore;
    private readonly IRulePackRegistryService _rulePackRegistryService;
    private readonly IRuntimeFingerprintService _runtimeFingerprintService;

    public DefaultRuleProfileRegistryService(
        IRulesetPluginRegistry pluginRegistry,
        IRulePackRegistryService rulePackRegistryService,
        IRuleProfileManifestStore manifestStore,
        IRuleProfilePublicationStore publicationStore,
        IRuleProfileInstallStateStore installStateStore,
        IRuntimeFingerprintService runtimeFingerprintService)
    {
        _pluginRegistry = pluginRegistry;
        _rulePackRegistryService = rulePackRegistryService;
        _manifestStore = manifestStore;
        _publicationStore = publicationStore;
        _installStateStore = installStateStore;
        _runtimeFingerprintService = runtimeFingerprintService;
    }

    public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
    {
        IReadOnlyList<IRulesetPlugin> plugins = SelectPlugins(rulesetId);

        return plugins
            .SelectMany(plugin => BuildProfiles(plugin, owner))
            .OrderBy(static entry => entry.Manifest.RulesetId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.ProfileId, StringComparer.Ordinal)
            .ToArray();
    }

    public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);

        return List(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.ProfileId, profileId, StringComparison.Ordinal));
    }

    private IReadOnlyList<IRulesetPlugin> SelectPlugins(string? rulesetId)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        if (normalizedRulesetId is null)
        {
            return _pluginRegistry.All;
        }

        IRulesetPlugin? plugin = _pluginRegistry.Resolve(normalizedRulesetId);
        return plugin is null ? [] : [plugin];
    }

    private IReadOnlyList<RuleProfileRegistryEntry> BuildProfiles(IRulesetPlugin plugin, OwnerScope owner)
    {
        string rulesetId = plugin.Id.NormalizedValue;
        RulePackRegistryEntry[] rulePacks = _rulePackRegistryService.List(owner, rulesetId)
            .OrderBy(static entry => entry.Manifest.PackId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Version, StringComparer.Ordinal)
            .ToArray();
        Dictionary<string, ArtifactInstallState> installStateLookup = _installStateStore.List(owner, rulesetId)
            .ToDictionary(
                record => CreatePublicationKey(record.ProfileId, record.RulesetId),
                record => record.Install,
                StringComparer.Ordinal);
        Dictionary<string, RuleProfilePublicationMetadata> publicationLookup = _publicationStore.List(owner, rulesetId)
            .ToDictionary(
                record => CreatePublicationKey(record.ProfileId, record.RulesetId),
                record => record.Publication,
                StringComparer.Ordinal);
        List<RuleProfileRegistryEntry> entries =
        [
            CreateCoreProfile(plugin, publicationLookup, installStateLookup)
        ];

        if (rulePacks.Length > 0)
        {
            entries.Add(CreateOverlayProfile(plugin, owner, rulePacks, publicationLookup, installStateLookup));
        }

        foreach (RuleProfileManifestRecord record in _manifestStore.List(owner, rulesetId)
                     .OrderBy(static record => record.Manifest.ProfileId, StringComparer.Ordinal))
        {
            UpsertEntry(
                entries,
                CreatePersistedProfile(record.Manifest, owner, publicationLookup, installStateLookup));
        }

        return entries;
    }

    private RuleProfileRegistryEntry CreateCoreProfile(
        IRulesetPlugin plugin,
        IReadOnlyDictionary<string, RuleProfilePublicationMetadata> publicationLookup,
        IReadOnlyDictionary<string, ArtifactInstallState> installStateLookup)
    {
        string rulesetId = plugin.Id.NormalizedValue;
        string profileId = $"official.{rulesetId}.core";
        ResolvedRuntimeLock runtimeLock = CreateRuntimeLock(
            plugin,
            rulePacks: []);

        RuleProfileManifest manifest = new(
            ProfileId: profileId,
            Title: $"{plugin.DisplayName} Core",
            Description: $"Curated core runtime for {plugin.DisplayName}.",
            RulesetId: rulesetId,
            Audience: RuleProfileAudienceKinds.General,
            CatalogKind: RuleProfileCatalogKinds.Official,
            RulePacks: [],
            DefaultToggles: [],
            RuntimeLock: runtimeLock,
            UpdateChannel: RuleProfileUpdateChannels.Stable,
            Notes: "Built-in baseline runtime profile.");
        RuleProfilePublicationMetadata publication = publicationLookup.TryGetValue(
            CreatePublicationKey(profileId, rulesetId),
            out RuleProfilePublicationMetadata? persisted)
            ? persisted
            : new RuleProfilePublicationMetadata(
                OwnerId: SystemOwnerId,
                Visibility: ArtifactVisibilityModes.Public,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares:
                [
                    new RulePackShareGrant(
                        SubjectKind: RulePackShareSubjectKinds.PublicCatalog,
                        SubjectId: "profiles",
                        AccessLevel: RulePackShareAccessLevels.Install)
                ]);
        ArtifactInstallState install = installStateLookup.TryGetValue(
            CreatePublicationKey(profileId, rulesetId),
            out ArtifactInstallState? persistedInstall)
            ? persistedInstall
            : new ArtifactInstallState(ArtifactInstallStates.Available);

        return new RuleProfileRegistryEntry(
            manifest,
            publication,
            install,
            RegistryEntrySourceKinds.BuiltInCoreProfile);
    }

    private RuleProfileRegistryEntry CreateOverlayProfile(
        IRulesetPlugin plugin,
        OwnerScope owner,
        IReadOnlyList<RulePackRegistryEntry> rulePacks,
        IReadOnlyDictionary<string, RuleProfilePublicationMetadata> publicationLookup,
        IReadOnlyDictionary<string, ArtifactInstallState> installStateLookup)
    {
        string rulesetId = plugin.Id.NormalizedValue;
        string profileId = $"local.{rulesetId}.current-overlays";
        RuleProfilePackSelection[] selections = rulePacks
            .Select(static rulePack => new RuleProfilePackSelection(
                RulePack: new ArtifactVersionReference(rulePack.Manifest.PackId, rulePack.Manifest.Version),
                Required: true,
                EnabledByDefault: true))
            .ToArray();
        ResolvedRuntimeLock runtimeLock = CreateRuntimeLock(plugin, rulePacks);
        RuleProfileManifest manifest = new(
            ProfileId: profileId,
            Title: $"{plugin.DisplayName} Local Overlay Catalog",
            Description: $"Runtime profile composed from the current discovered RulePacks for {plugin.DisplayName}.",
            RulesetId: rulesetId,
            Audience: RuleProfileAudienceKinds.Advanced,
            CatalogKind: RuleProfileCatalogKinds.Personal,
            RulePacks: selections,
            DefaultToggles: [],
            RuntimeLock: runtimeLock,
            UpdateChannel: RuleProfileUpdateChannels.Preview,
            Notes: "Derived from the current local RulePack registry.");
        RuleProfilePublicationMetadata publication = publicationLookup.TryGetValue(
            CreatePublicationKey(profileId, rulesetId),
            out RuleProfilePublicationMetadata? persisted)
            ? persisted
            : new RuleProfilePublicationMetadata(
                OwnerId: owner.NormalizedValue,
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []);
        ArtifactInstallState install = installStateLookup.TryGetValue(
            CreatePublicationKey(profileId, rulesetId),
            out ArtifactInstallState? persistedInstall)
            ? persistedInstall
            : new ArtifactInstallState(
                ArtifactInstallStates.Available,
                RuntimeFingerprint: runtimeLock.RuntimeFingerprint);

        return new RuleProfileRegistryEntry(
            manifest,
            publication,
            install,
            RegistryEntrySourceKinds.OverlayDerivedProfile);
    }

    private static RuleProfileRegistryEntry CreatePersistedProfile(
        RuleProfileManifest manifest,
        OwnerScope owner,
        IReadOnlyDictionary<string, RuleProfilePublicationMetadata> publicationLookup,
        IReadOnlyDictionary<string, ArtifactInstallState> installStateLookup)
    {
        string key = CreatePublicationKey(manifest.ProfileId, manifest.RulesetId);
        RuleProfilePublicationMetadata publication = publicationLookup.TryGetValue(
            key,
            out RuleProfilePublicationMetadata? persisted)
            ? persisted
            : new RuleProfilePublicationMetadata(
                OwnerId: owner.NormalizedValue,
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RuleProfilePublicationStatuses.Draft,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []);
        ArtifactInstallState install = installStateLookup.TryGetValue(
            key,
            out ArtifactInstallState? persistedInstall)
            ? persistedInstall
            : new ArtifactInstallState(
                ArtifactInstallStates.Available,
                RuntimeFingerprint: manifest.RuntimeLock.RuntimeFingerprint);

        return new RuleProfileRegistryEntry(
            manifest,
            publication,
            install,
            RegistryEntrySourceKinds.PersistedManifest);
    }

    private static void UpsertEntry(List<RuleProfileRegistryEntry> entries, RuleProfileRegistryEntry entry)
    {
        int existingIndex = entries.FindIndex(current =>
            string.Equals(current.Manifest.ProfileId, entry.Manifest.ProfileId, StringComparison.Ordinal)
            && string.Equals(current.Manifest.RulesetId, entry.Manifest.RulesetId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            entries[existingIndex] = entry;
        }
        else
        {
            entries.Add(entry);
        }
    }

    private ResolvedRuntimeLock CreateRuntimeLock(
        IRulesetPlugin plugin,
        IReadOnlyList<RulePackRegistryEntry> rulePacks)
    {
        string rulesetId = plugin.Id.NormalizedValue;
        RulePackRegistryEntry[] orderedRulePacks = ResolveRulePackCompileOrder(rulePacks);
        ContentBundleDescriptor bundle = new(
            BundleId: $"official.{rulesetId}.base",
            RulesetId: rulesetId,
            Version: $"schema-{plugin.Serializer.SchemaVersion}",
            Title: $"{plugin.DisplayName} Base Content",
            Description: $"Built-in base content bundle for {plugin.DisplayName}.",
            AssetPaths: ["data/", "lang/"]);
        ArtifactVersionReference[] runtimeRulePacks = orderedRulePacks
            .Select(static rulePack => new ArtifactVersionReference(rulePack.Manifest.PackId, rulePack.Manifest.Version))
            .ToArray();
        Dictionary<string, string> providerBindings = CreateProviderBindings(orderedRulePacks);
        Dictionary<string, string> capabilityAbiVersions = RulesetTypedCapabilityCatalog.Descriptors
            .ToDictionary(
                static descriptor => descriptor.CapabilityId,
                static descriptor => $"{descriptor.InputSchemaId}|{descriptor.OutputSchemaId}",
                StringComparer.Ordinal);
        string runtimeFingerprint = _runtimeFingerprintService.ComputeResolvedRuntimeFingerprint(
            rulesetId,
            [bundle],
            orderedRulePacks,
            providerBindings,
            EngineApiVersion,
            capabilityAbiVersions);

        return new ResolvedRuntimeLock(
            RulesetId: rulesetId,
            ContentBundles: [bundle],
            RulePacks: runtimeRulePacks,
            ProviderBindings: providerBindings,
            EngineApiVersion: EngineApiVersion,
            RuntimeFingerprint: runtimeFingerprint);
    }

    private static Dictionary<string, string> CreateProviderBindings(IReadOnlyList<RulePackRegistryEntry> rulePacks)
    {
        Dictionary<string, string> providerBindings = [];
        foreach (RulePackRegistryEntry rulePack in rulePacks)
        {
            foreach (RulePackCapabilityDescriptor capability in rulePack.Manifest.Capabilities
                         .OrderBy(static candidate => candidate.CapabilityId, StringComparer.Ordinal))
            {
                providerBindings[capability.CapabilityId] = $"{rulePack.Manifest.PackId}/{capability.CapabilityId}";
            }
        }

        return providerBindings;
    }

    private static RulePackRegistryEntry[] ResolveRulePackCompileOrder(IReadOnlyList<RulePackRegistryEntry> rulePacks)
    {
        static string GetPackKey(string packId, string version)
        {
            return $"{packId}@{version}";
        }

        RulePackRegistryEntry[] orderedInputs = rulePacks
            .OrderBy(static rulePack => rulePack.Manifest.PackId, StringComparer.Ordinal)
            .ThenBy(static rulePack => rulePack.Manifest.Version, StringComparer.Ordinal)
            .ToArray();

        Dictionary<string, RulePackRegistryEntry> rulePacksById = orderedInputs
            .ToDictionary(
                rulePack => GetPackKey(rulePack.Manifest.PackId, rulePack.Manifest.Version),
                static rulePack => rulePack,
                StringComparer.Ordinal);
        Dictionary<string, int> visitState = [];
        Dictionary<string, int> activePathIndex = [];
        List<string> activePath = [];
        List<RulePackRegistryEntry> ordered = [];

        void Visit(RulePackRegistryEntry rulePack)
        {
            string key = GetPackKey(rulePack.Manifest.PackId, rulePack.Manifest.Version);
            if (visitState.GetValueOrDefault(key) == 2)
            {
                return;
            }

            if (visitState.GetValueOrDefault(key) == 1)
            {
                int cycleStartIndex = activePathIndex.GetValueOrDefault(key, 0);
                string cyclePath = string.Join(
                    " -> ",
                    activePath.Skip(cycleStartIndex).Append(key));
                throw new InvalidOperationException($"RulePack dependency cycle detected: {cyclePath}");
            }

            visitState[key] = 1;
            activePathIndex[key] = activePath.Count;
            activePath.Add(key);

            foreach (ArtifactVersionReference dependency in rulePack.Manifest.DependsOn
                         .DistinctBy(candidate => GetPackKey(candidate.Id, candidate.Version), StringComparer.Ordinal)
                         .OrderBy(candidate => candidate.Id, StringComparer.Ordinal)
                         .ThenBy(candidate => candidate.Version, StringComparer.Ordinal))
            {
                string dependencyKey = GetPackKey(dependency.Id, dependency.Version);
                if (rulePacksById.TryGetValue(dependencyKey, out RulePackRegistryEntry? dependencyPack))
                {
                    Visit(dependencyPack);
                }
            }

            visitState[key] = 2;
            activePath.RemoveAt(activePath.Count - 1);
            activePathIndex.Remove(key);
            ordered.Add(rulePack);
        }

        foreach (RulePackRegistryEntry rulePack in orderedInputs)
        {
            Visit(rulePack);
        }

        return ordered.ToArray();
    }

    private static string CreatePublicationKey(string profileId, string rulesetId)
    {
        return $"{profileId}|{rulesetId}";
    }
}
