using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiActionPreviewService
{
    AiActionPreviewReceipt? PreviewKarmaSpend(OwnerScope owner, AiSpendPlanPreviewRequest? request);

    AiActionPreviewReceipt? PreviewNuyenSpend(OwnerScope owner, AiSpendPlanPreviewRequest? request);

    AiActionPreviewReceipt? CreateApplyPreview(OwnerScope owner, AiApplyPreviewRequest? request);
}
