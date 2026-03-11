namespace Chummer.Application.AI;

public interface IAiProviderCredentialSelector
{
    AiProviderCredentialSelection? SelectCredential(string providerId);
}
