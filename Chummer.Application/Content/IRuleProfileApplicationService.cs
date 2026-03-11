using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleProfileApplicationService
{
    RuleProfilePreviewReceipt? Preview(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null);

    RuleProfileApplyReceipt? Apply(OwnerScope owner, string profileId, RuleProfileApplyTarget target, string? rulesetId = null);
}
