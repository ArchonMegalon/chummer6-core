using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class OverlayRulePackRegistryService : IRulePackRegistryService
{
    private const string SystemOwnerId = "system";
    private readonly IRulePackManifestStore _manifestStore;
    private readonly IContentOverlayCatalogService _overlays;
    private readonly IRulePackInstallStateStore _installStateStore;
    private readonly IRulePackPublicationStore _publicationStore;
    private readonly IRulesetSelectionPolicy _rulesetSelectionPolicy;

    public OverlayRulePackRegistryService(
        IRulePackManifestStore manifestStore,
        IContentOverlayCatalogService overlays,
        IRulesetSelectionPolicy rulesetSelectionPolicy,
        IRulePackPublicationStore publicationStore,
        IRulePackInstallStateStore installStateStore)
    {
        _manifestStore = manifestStore;
        _overlays = overlays;
        _rulesetSelectionPolicy = rulesetSelectionPolicy;
        _publicationStore = publicationStore;
        _installStateStore = installStateStore;
    }

    public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null)
    {
        string effectiveRulesetId = RulesetDefaults.NormalizeOptional(rulesetId)
            ?? _rulesetSelectionPolicy.GetDefaultRulesetId();
        Dictionary<string, ArtifactInstallState> installStateLookup = _installStateStore.List(owner, effectiveRulesetId)
            .ToDictionary(
                record => CreatePublicationKey(record.PackId, record.Version, record.RulesetId),
                record => record.Install,
                StringComparer.Ordinal);
        Dictionary<string, RulePackPublicationMetadata> publicationLookup = _publicationStore.List(owner, effectiveRulesetId)
            .ToDictionary(
                record => CreatePublicationKey(record.PackId, record.Version, record.RulesetId),
                record => record.Publication,
                StringComparer.Ordinal);
        List<RulePackRegistryEntry> entries = _overlays.GetCatalog().Overlays
            .Select(overlay => ToRegistryEntry(overlay, effectiveRulesetId, publicationLookup, installStateLookup))
            .ToList();
        foreach (RulePackManifestRecord record in _manifestStore.List(owner, effectiveRulesetId))
        {
            UpsertEntry(
                entries,
                ToRegistryEntry(record.Manifest, owner, effectiveRulesetId, publicationLookup, installStateLookup),
                effectiveRulesetId);
        }

        return entries
            .OrderBy(static entry => entry.Manifest.PackId, StringComparer.Ordinal)
            .ThenBy(static entry => entry.Manifest.Version, StringComparer.Ordinal)
            .ToArray();
    }

    public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packId);

        return List(owner, rulesetId)
            .FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));
    }

    private static RulePackRegistryEntry ToRegistryEntry(
        ContentOverlayPack overlay,
        string rulesetId,
        IReadOnlyDictionary<string, RulePackPublicationMetadata> publicationLookup,
        IReadOnlyDictionary<string, ArtifactInstallState> installStateLookup)
    {
        RulePackManifest manifest = overlay.ToRulePackManifest(rulesetId);
        string key = CreatePublicationKey(manifest.PackId, manifest.Version, rulesetId);
        RulePackPublicationMetadata publication = publicationLookup.TryGetValue(
            key,
            out RulePackPublicationMetadata? persisted)
            ? persisted
            : new RulePackPublicationMetadata(
                OwnerId: SystemOwnerId,
                Visibility: manifest.Visibility,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []);
        ArtifactInstallState install = installStateLookup.TryGetValue(key, out ArtifactInstallState? persistedInstall)
            ? persistedInstall
            : new ArtifactInstallState(
                State: ArtifactInstallStates.Installed,
                InstalledTargetKind: RuleProfileApplyTargetKinds.GlobalDefaults,
                InstalledTargetId: OwnerScope.LocalSingleUser.NormalizedValue);

        return new RulePackRegistryEntry(
            manifest,
            publication,
            install,
            RegistryEntrySourceKinds.OverlayCatalogBridge);
    }

    private static RulePackRegistryEntry ToRegistryEntry(
        RulePackManifest manifest,
        OwnerScope owner,
        string rulesetId,
        IReadOnlyDictionary<string, RulePackPublicationMetadata> publicationLookup,
        IReadOnlyDictionary<string, ArtifactInstallState> installStateLookup)
    {
        string key = CreatePublicationKey(manifest.PackId, manifest.Version, rulesetId);
        RulePackPublicationMetadata publication = publicationLookup.TryGetValue(
            key,
            out RulePackPublicationMetadata? persisted)
            ? persisted
            : new RulePackPublicationMetadata(
                OwnerId: owner.NormalizedValue,
                Visibility: manifest.Visibility,
                PublicationStatus: RulePackPublicationStatuses.Draft,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []);
        ArtifactInstallState install = installStateLookup.TryGetValue(key, out ArtifactInstallState? persistedInstall)
            ? persistedInstall
            : new ArtifactInstallState(ArtifactInstallStates.Available);

        return new RulePackRegistryEntry(
            manifest,
            publication,
            install,
            RegistryEntrySourceKinds.PersistedManifest);
    }

    private static void UpsertEntry(List<RulePackRegistryEntry> entries, RulePackRegistryEntry entry, string rulesetId)
    {
        string key = CreatePublicationKey(entry.Manifest.PackId, entry.Manifest.Version, rulesetId);
        int existingIndex = entries.FindIndex(current =>
            string.Equals(
                CreatePublicationKey(current.Manifest.PackId, current.Manifest.Version, rulesetId),
                key,
                StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            entries[existingIndex] = entry;
        }
        else
        {
            entries.Add(entry);
        }
    }

    private static string CreatePublicationKey(string packId, string version, string rulesetId)
    {
        return $"{packId}|{version}|{rulesetId}";
    }
}
