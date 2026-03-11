namespace Chummer.Application.AI;

public sealed record AiProviderCredentialSet(
    IReadOnlyList<string> PrimaryCredentials,
    IReadOnlyList<string> FallbackCredentials);

public sealed record AiProviderCredentialSelection(
    string ProviderId,
    string CredentialTier,
    int SlotIndex,
    string ApiKey);
