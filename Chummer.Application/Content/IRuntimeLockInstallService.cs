using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuntimeLockInstallService
{
    RuntimeLockInstallPreviewReceipt? Preview(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null);

    RuntimeLockInstallReceipt? Apply(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null);
}
