using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiHistoryDraftService
{
    AiHistoryDraftProjection? CreateHistoryDraft(OwnerScope owner, AiHistoryDraftRequest request);
}
