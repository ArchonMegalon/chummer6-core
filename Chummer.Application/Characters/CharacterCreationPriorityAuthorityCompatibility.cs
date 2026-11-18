using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Recognizes the exact former Human-only and Elf/Ork-era catalogs from
/// independently resolved current sources. This is not a source-drift
/// exemption or a workspace rewrite. Only admission of Elf/Ork/Dwarf racial
/// qualities is removed for the comparison; source/profile hashes, ranks,
/// costs, talents and every other field remain.
/// </summary>
internal static class CharacterCreationPriorityAuthorityCompatibility
{
    internal static bool MatchesLegacyHumanCatalog(string digest, CharacterCreationPrerequisiteAuthority current)
    {
        if (!current.IsAuthoritative || current.Blockers.Count != 0
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(digest)
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(current.AuthorityDigest,
                CharacterCreationPrerequisiteAuthorityDigest.Compute(current))) return false;
        var historical = current with
        {
            Options = current.Options.Select(row => row with
            {
                HeritageOptions = row.HeritageOptions.Select(option =>
                    option.MetatypeName is "Elf" or "Ork" or "Dwarf" && option.MetavariantSourceId is null
                        && option.IsEnabled && option.Blockers.Count == 0 && option.RacialQualitySources is { Count: > 0 }
                    ? option with { IsEnabled = false, RacialQualitySources = null,
                        BaseBonuses = null,
                        Blockers = [CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported] }
                    : option).ToArray()
            }).ToArray()
        };
        return CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(digest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(historical));
    }
}
