using System.Text.Json;
using Chummer.Application.AI;
using Chummer.Contracts.AI;

namespace Chummer.Infrastructure.Files;

public sealed class FileAiProviderHealthStore : IAiProviderHealthStore
{
    private readonly string _stateDirectory;

    public FileAiProviderHealthStore(string? stateDirectory = null)
    {
        _stateDirectory = stateDirectory ?? Path.Combine(Path.GetTempPath(), "chummer-state");
        Directory.CreateDirectory(_stateDirectory);
    }

    public IReadOnlyList<AiProviderHealthSnapshot> List()
        => Load()
            .OrderBy(snapshot => snapshot.ProviderId, StringComparer.Ordinal)
            .ToArray();

    public AiProviderHealthSnapshot Get(string providerId)
    {
        string normalizedProviderId = AiResponseCacheKeys.NormalizeRequired(providerId);
        return Load().FirstOrDefault(snapshot => string.Equals(snapshot.ProviderId, normalizedProviderId, StringComparison.Ordinal))
            ?? new AiProviderHealthSnapshot(normalizedProviderId);
    }

    public void RecordSuccess(
        string providerId,
        DateTimeOffset occurredAtUtc,
        string? routeType = null,
        string? credentialTier = null,
        int? credentialSlotIndex = null)
    {
        string normalizedProviderId = AiResponseCacheKeys.NormalizeRequired(providerId);
        string? normalizedRouteType = AiResponseCacheKeys.NormalizeOptional(routeType);
        string? normalizedCredentialTier = AiResponseCacheKeys.NormalizeOptional(credentialTier);
        List<AiProviderHealthSnapshot> snapshots = Load().ToList();
        int existingIndex = snapshots.FindIndex(snapshot => string.Equals(snapshot.ProviderId, normalizedProviderId, StringComparison.Ordinal));
        AiProviderHealthSnapshot updated = existingIndex >= 0
            ? snapshots[existingIndex] with
            {
                ConsecutiveFailureCount = 0,
                LastSuccessAtUtc = occurredAtUtc,
                LastFailureMessage = null,
                LastRouteType = normalizedRouteType,
                LastCredentialTier = normalizedCredentialTier,
                LastCredentialSlotIndex = credentialSlotIndex
            }
            : new AiProviderHealthSnapshot(
                ProviderId: normalizedProviderId,
                ConsecutiveFailureCount: 0,
                LastSuccessAtUtc: occurredAtUtc,
                LastRouteType: normalizedRouteType,
                LastCredentialTier: normalizedCredentialTier,
                LastCredentialSlotIndex: credentialSlotIndex);
        UpsertAndSave(snapshots, existingIndex, updated);
    }

    public void RecordFailure(
        string providerId,
        string? failureMessage,
        DateTimeOffset occurredAtUtc,
        string? routeType = null,
        string? credentialTier = null,
        int? credentialSlotIndex = null)
    {
        string normalizedProviderId = AiResponseCacheKeys.NormalizeRequired(providerId);
        string? normalizedRouteType = AiResponseCacheKeys.NormalizeOptional(routeType);
        string? normalizedCredentialTier = AiResponseCacheKeys.NormalizeOptional(credentialTier);
        List<AiProviderHealthSnapshot> snapshots = Load().ToList();
        int existingIndex = snapshots.FindIndex(snapshot => string.Equals(snapshot.ProviderId, normalizedProviderId, StringComparison.Ordinal));
        AiProviderHealthSnapshot updated = existingIndex >= 0
            ? snapshots[existingIndex] with
            {
                ConsecutiveFailureCount = snapshots[existingIndex].ConsecutiveFailureCount + 1,
                LastFailureAtUtc = occurredAtUtc,
                LastFailureMessage = AiResponseCacheKeys.NormalizeOptional(failureMessage),
                LastRouteType = normalizedRouteType,
                LastCredentialTier = normalizedCredentialTier,
                LastCredentialSlotIndex = credentialSlotIndex
            }
            : new AiProviderHealthSnapshot(
                ProviderId: normalizedProviderId,
                ConsecutiveFailureCount: 1,
                LastFailureAtUtc: occurredAtUtc,
                LastFailureMessage: AiResponseCacheKeys.NormalizeOptional(failureMessage),
                LastRouteType: normalizedRouteType,
                LastCredentialTier: normalizedCredentialTier,
                LastCredentialSlotIndex: credentialSlotIndex);
        UpsertAndSave(snapshots, existingIndex, updated);
    }

    private IReadOnlyList<AiProviderHealthSnapshot> Load()
    {
        string path = GetPath();
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<AiProviderHealthSnapshot>>(File.ReadAllText(path))
            ?? [];
    }

    private void UpsertAndSave(List<AiProviderHealthSnapshot> snapshots, int existingIndex, AiProviderHealthSnapshot updated)
    {
        if (existingIndex >= 0)
        {
            snapshots[existingIndex] = Normalize(updated);
        }
        else
        {
            snapshots.Add(Normalize(updated));
        }

        string path = GetPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshots));
    }

    private string GetPath()
        => Path.Combine(_stateDirectory, "ai", "provider-health.json");

    private static AiProviderHealthSnapshot Normalize(AiProviderHealthSnapshot snapshot)
        => snapshot with
        {
            ProviderId = AiResponseCacheKeys.NormalizeRequired(snapshot.ProviderId),
            LastFailureMessage = AiResponseCacheKeys.NormalizeOptional(snapshot.LastFailureMessage),
            LastRouteType = AiResponseCacheKeys.NormalizeOptional(snapshot.LastRouteType),
            LastCredentialTier = AiResponseCacheKeys.NormalizeOptional(snapshot.LastCredentialTier)
        };
}
