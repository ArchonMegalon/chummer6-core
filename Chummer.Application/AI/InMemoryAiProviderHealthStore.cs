using System.Collections.Concurrent;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public sealed class InMemoryAiProviderHealthStore : IAiProviderHealthStore
{
    private readonly ConcurrentDictionary<string, AiProviderHealthSnapshot> _snapshots = new(StringComparer.Ordinal);

    public IReadOnlyList<AiProviderHealthSnapshot> List()
        => _snapshots.Values
            .OrderBy(snapshot => snapshot.ProviderId, StringComparer.Ordinal)
            .ToArray();

    public AiProviderHealthSnapshot Get(string providerId)
    {
        string normalizedProviderId = AiResponseCacheKeys.NormalizeRequired(providerId);
        return _snapshots.TryGetValue(normalizedProviderId, out AiProviderHealthSnapshot? snapshot)
            ? snapshot
            : new AiProviderHealthSnapshot(normalizedProviderId);
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
        _snapshots.AddOrUpdate(
            normalizedProviderId,
            _ => new AiProviderHealthSnapshot(
                ProviderId: normalizedProviderId,
                ConsecutiveFailureCount: 0,
                LastSuccessAtUtc: occurredAtUtc,
                LastRouteType: normalizedRouteType,
                LastCredentialTier: normalizedCredentialTier,
                LastCredentialSlotIndex: credentialSlotIndex),
            (_, existing) => existing with
            {
                ConsecutiveFailureCount = 0,
                LastSuccessAtUtc = occurredAtUtc,
                LastFailureMessage = null,
                LastRouteType = normalizedRouteType,
                LastCredentialTier = normalizedCredentialTier,
                LastCredentialSlotIndex = credentialSlotIndex
            });
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
        _snapshots.AddOrUpdate(
            normalizedProviderId,
            _ => new AiProviderHealthSnapshot(
                ProviderId: normalizedProviderId,
                ConsecutiveFailureCount: 1,
                LastFailureAtUtc: occurredAtUtc,
                LastFailureMessage: AiResponseCacheKeys.NormalizeOptional(failureMessage),
                LastRouteType: normalizedRouteType,
                LastCredentialTier: normalizedCredentialTier,
                LastCredentialSlotIndex: credentialSlotIndex),
            (_, existing) => existing with
            {
                ConsecutiveFailureCount = existing.ConsecutiveFailureCount + 1,
                LastFailureAtUtc = occurredAtUtc,
                LastFailureMessage = AiResponseCacheKeys.NormalizeOptional(failureMessage),
                LastRouteType = normalizedRouteType,
                LastCredentialTier = normalizedCredentialTier,
                LastCredentialSlotIndex = credentialSlotIndex
            });
    }
}
