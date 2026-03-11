using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiRecapDraftService
{
    AiApiResult<AiRecapDraftCatalog> ListRecapDrafts(OwnerScope owner, AiRecapDraftQuery? query);

    AiApiResult<AiRecapDraftReceipt> CreateRecapDraft(OwnerScope owner, AiRecapDraftRequest? request);
}
