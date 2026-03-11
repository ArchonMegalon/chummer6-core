using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiPortraitPromptService
{
    AiPortraitPromptProjection? CreatePortraitPrompt(OwnerScope owner, AiPortraitPromptRequest request);
}
