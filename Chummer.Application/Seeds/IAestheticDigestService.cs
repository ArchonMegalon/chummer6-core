using Chummer.Contracts.Seeds;

namespace Chummer.Application.Seeds;

public interface IAestheticDigestService
{
    CharacterAestheticDigest BuildDigest(string characterId, string rulesetId);
}
