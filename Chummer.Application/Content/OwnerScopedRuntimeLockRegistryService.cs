using System.Linq;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Content;

public sealed class OwnerScopedRuntimeLockRegistryService : IRuntimeLockRegistryService
{
    private readonly IRuleProfileRegistryService _ruleProfileRegistryService;
    private readonly IRuntimeLockStore _runtimeLockStore;

    public OwnerScopedRuntimeLockRegistryService(
        IRuleProfileRegistryService ruleProfileRegistryService,
        IRuntimeLockStore runtimeLockStore)
    {
        _ruleProfileRegistryService = ruleProfileRegistryService;
        _runtimeLockStore = runtimeLockStore;
    }

    public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null)
    {
        Dictionary<string, RuntimeLockRegistryEntry> entries = _ruleProfileRegistryService.List(owner, rulesetId)
            .GroupBy(profile => profile.Manifest.RuntimeLock.RuntimeFingerprint, StringComparer.Ordinal)
            .Select(group => ToRegistryEntry(group
                .OrderBy(static profile => profile.Manifest.ProfileId, StringComparer.Ordinal)
                .First()))
            .ToDictionary(entry => entry.LockId, StringComparer.Ordinal);

        foreach (RuntimeLockRegistryEntry persisted in _runtimeLockStore.List(owner, rulesetId).Entries)
        {
            entries[persisted.LockId] = persisted;
        }

        RuntimeLockRegistryEntry[] orderedEntries = entries.Values
            .OrderBy(entry => entry.Title, StringComparer.Ordinal)
            .ToArray();

        return new RuntimeLockRegistryPage(
            Entries: orderedEntries,
            TotalCount: orderedEntries.Length);
    }

    public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);

        return List(owner, rulesetId).Entries
            .FirstOrDefault(entry => string.Equals(entry.LockId, lockId, StringComparison.Ordinal));
    }

    public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockId);
        ArgumentNullException.ThrowIfNull(request);

        string normalizedFingerprint = request.RuntimeLock.RuntimeFingerprint?.Trim() ?? string.Empty;
        if (!string.Equals(lockId, normalizedFingerprint, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Runtime lock id '{lockId}' must match runtime fingerprint '{normalizedFingerprint}'.",
                nameof(lockId));
        }

        RuntimeLockRegistryEntry? existingEntry = _runtimeLockStore.Get(owner, lockId, request.RuntimeLock.RulesetId)
            ?? Get(owner, lockId, request.RuntimeLock.RulesetId);
        ArtifactInstallState install = NormalizeInstall(
            request.Install ?? existingEntry?.Install ?? new ArtifactInstallState(ArtifactInstallStates.Available),
            normalizedFingerprint);
        string visibility = string.IsNullOrWhiteSpace(request.Visibility)
            ? ArtifactVisibilityModes.LocalOnly
            : request.Visibility;
        string title = string.IsNullOrWhiteSpace(request.Title)
            ? existingEntry?.Title ?? $"{RulesetDefaults.NormalizeRequired(request.RuntimeLock.RulesetId).ToUpperInvariant()} Runtime Lock"
            : request.Title.Trim();

        RuntimeLockRegistryEntry entry = new(
            LockId: lockId,
            Owner: owner,
            Title: title,
            Visibility: visibility,
            CatalogKind: RuntimeLockCatalogKinds.Saved,
            RuntimeLock: request.RuntimeLock with
            {
                RulesetId = RulesetDefaults.NormalizeRequired(request.RuntimeLock.RulesetId)
            },
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Description: request.Description ?? existingEntry?.Description,
            Install: install);

        return _runtimeLockStore.Upsert(owner, entry);
    }

    private static RuntimeLockRegistryEntry ToRegistryEntry(RuleProfileRegistryEntry profile)
    {
        string ownerId = string.IsNullOrWhiteSpace(profile.Publication.OwnerId)
            ? OwnerScope.LocalSingleUser.NormalizedValue
            : profile.Publication.OwnerId;

        return new RuntimeLockRegistryEntry(
            LockId: profile.Manifest.RuntimeLock.RuntimeFingerprint,
            Owner: new OwnerScope(ownerId),
            Title: $"{profile.Manifest.Title} Runtime Lock",
            Visibility: profile.Publication.Visibility,
            CatalogKind: ResolveCatalogKind(profile),
            RuntimeLock: profile.Manifest.RuntimeLock,
            UpdatedAtUtc: profile.Publication.PublishedAtUtc ?? DateTimeOffset.UtcNow,
            Description: profile.Manifest.Description,
            Install: NormalizeInstall(profile.Install, profile.Manifest.RuntimeLock.RuntimeFingerprint));
    }

    private static string ResolveCatalogKind(RuleProfileRegistryEntry profile)
    {
        return string.Equals(profile.Publication.Visibility, ArtifactVisibilityModes.Public, StringComparison.Ordinal)
            ? RuntimeLockCatalogKinds.Published
            : RuntimeLockCatalogKinds.Derived;
    }

    private static ArtifactInstallState NormalizeInstall(ArtifactInstallState install, string runtimeFingerprint)
    {
        return string.IsNullOrWhiteSpace(install.RuntimeFingerprint)
            ? install with { RuntimeFingerprint = runtimeFingerprint }
            : install;
    }
}
