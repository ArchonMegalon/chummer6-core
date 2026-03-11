using System;
using System.Collections.Generic;
using Chummer.Contracts.AI;

namespace Chummer.Application.AI;

public sealed class RoundRobinAiProviderCredentialSelector(IAiProviderCredentialCatalog credentialCatalog) : IAiProviderCredentialSelector
{
    private readonly IAiProviderCredentialCatalog _credentialCatalog = credentialCatalog;
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _primaryIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _fallbackIndexes = new(StringComparer.Ordinal);

    public AiProviderCredentialSelection? SelectCredential(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        IReadOnlyDictionary<string, AiProviderCredentialSet> configuredCredentialSets = _credentialCatalog.GetConfiguredCredentialSets();
        if (!configuredCredentialSets.TryGetValue(providerId, out AiProviderCredentialSet? configuredCredentials))
        {
            return null;
        }

        if (configuredCredentials.PrimaryCredentials.Count > 0)
        {
            return SelectCredential(providerId, configuredCredentials.PrimaryCredentials, AiProviderCredentialTiers.Primary, _primaryIndexes);
        }

        if (configuredCredentials.FallbackCredentials.Count > 0)
        {
            return SelectCredential(providerId, configuredCredentials.FallbackCredentials, AiProviderCredentialTiers.Fallback, _fallbackIndexes);
        }

        return null;
    }

    private AiProviderCredentialSelection SelectCredential(
        string providerId,
        IReadOnlyList<string> credentials,
        string credentialTier,
        Dictionary<string, int> indexes)
    {
        lock (_gate)
        {
            int currentIndex = indexes.TryGetValue(providerId, out int nextIndex)
                ? nextIndex
                : 0;
            int selectedIndex = currentIndex % credentials.Count;
            indexes[providerId] = (selectedIndex + 1) % credentials.Count;

            return new AiProviderCredentialSelection(
                ProviderId: providerId,
                CredentialTier: credentialTier,
                SlotIndex: selectedIndex,
                ApiKey: credentials[selectedIndex]);
        }
    }
}
