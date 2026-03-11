using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public interface IAiProviderHealthStore
{
    IReadOnlyList<AiProviderHealthSnapshot> List();

    AiProviderHealthSnapshot Get(string providerId);

    void RecordSuccess(
        string providerId,
        DateTimeOffset occurredAtUtc,
        string? routeType = null,
        string? credentialTier = null,
        int? credentialSlotIndex = null);

    void RecordFailure(
        string providerId,
        string? failureMessage,
        DateTimeOffset occurredAtUtc,
        string? routeType = null,
        string? credentialTier = null,
        int? credentialSlotIndex = null);
}
