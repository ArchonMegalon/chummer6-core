using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static class CharacterCreationAttributePolicyAuthority
{
    public static string ComputeDigest(CharacterCreationAttributePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            policy with { AuthorityDigest = string.Empty });
    }
}
