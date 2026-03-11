using Chummer.Contracts.Content;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Hub;

public interface IHubInstallPreviewService
{
    HubProjectInstallPreviewReceipt? Preview(OwnerScope owner, string kind, string itemId, RuleProfileApplyTarget target, string? rulesetId = null);
}
