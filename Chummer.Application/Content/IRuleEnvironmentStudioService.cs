using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;

namespace Chummer.Application.Content;

public interface IRuleEnvironmentStudioService
{
    RuleEnvironmentStudioProjection? GetProfileProjection(
        OwnerScope owner,
        string profileId,
        RuleProfileApplyTarget target,
        string? rulesetId = null);
}
