using Chummer.Contracts.AI;
using Chummer.Contracts.Owners;

namespace Chummer.Application.AI;

public interface IAiDigestService
{
    AiRuntimeSummaryProjection? GetRuntimeSummary(OwnerScope owner, string runtimeFingerprint, string? rulesetId = null);

    AiCharacterDigestProjection? GetCharacterDigest(OwnerScope owner, string characterId);

    AiSessionDigestProjection? GetSessionDigest(OwnerScope owner, string characterId);
}
