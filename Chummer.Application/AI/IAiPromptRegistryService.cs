using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiPromptRegistryService
{
    AiPromptCatalog ListPrompts(OwnerScope owner, AiPromptCatalogQuery? query);

    AiPromptDescriptor? GetPrompt(OwnerScope owner, string promptId);
}
