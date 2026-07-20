using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

/// <summary>
/// Trust adapter implemented by the campaign authority owner. Implementations
/// must validate the current campaign grant, not trust fields from the command.
/// </summary>
public interface ICampaignGmCharacterEditAuthorizer
{
    CampaignGmCharacterEditAuthorization Authorize(
        CampaignGmCharacterEditAuthorizationRequest request);
}

public interface IDelegatedGmCharacterEditService
{
    DelegatedGmCharacterEditResult Execute(DelegatedGmCharacterEditCommand command);
}
