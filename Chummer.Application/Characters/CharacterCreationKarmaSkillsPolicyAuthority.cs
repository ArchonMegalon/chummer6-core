using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationKarmaSkillsPolicyAuthority
{
    public static string ComputeDigest(CharacterCreationKarmaSkillsPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            policy with { AuthorityDigest = string.Empty });
    }
}
