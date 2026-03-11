using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRulePackInstallService
{
    RulePackInstallPreviewReceipt? Preview(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null);

    RulePackInstallReceipt? Apply(OwnerScope owner, string packId, RuleProfileApplyTarget target, string? rulesetId = null);
}
