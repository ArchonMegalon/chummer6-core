using Chummer.Contracts.Campaign;

namespace Chummer.Application.Campaign;

public interface ICampaignAdvanceReceiptService
{
    CampaignAdvanceReceiptBundle Build(CampaignAdvanceReceiptInput input);
}
