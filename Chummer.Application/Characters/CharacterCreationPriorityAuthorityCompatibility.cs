using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Recognizes the exact former Human-only catalog and the immediate 80584eff
/// baseline (Elf/Ork enabled, Dwarf disabled) from independently resolved
/// current sources. This is not a source-drift exemption or a workspace
/// rewrite. Only admission of the newly admitted standard Dwarf racial
/// qualities (and the BaseBonuses field, which both earlier eras predate) is
/// removed for the comparison; source/profile hashes, ranks, costs, talents
/// and every other field remain.
/// </summary>
internal static class CharacterCreationPriorityAuthorityCompatibility
{
    /// <summary>
    /// True when the option is a standard (non-metavariant) Dwarf row whose
    /// current catalog admits it with racial sources or base bonuses. Such
    /// rows are the ONLY newly admitted entries relative to both historical
    /// catalogs.
    /// </summary>
    internal static bool IsNewlyAdmittedStandardDwarf(
        CharacterCreationPriorityHeritageOptionProjection option)
    {
        return option.MetatypeName is "Dwarf"
            && option.MetavariantSourceId is null
            && option.IsEnabled
            && option.Blockers.Count == 0
            && (option.RacialQualitySources is { Count: > 0 }
                || option.BaseBonuses is not null);
    }

    /// <summary>
    /// Reconstructs the immediate 80584eff baseline (Elf/Ork enabled with
    /// racial sources, Dwarf disabled) by downgrading ONLY newly admitted
    /// standard Dwarf rows in the current authority, then compares the
    /// historical digest against the reconstruction.
    /// </summary>
    internal static bool MatchesLegacyElfOrkEnabledBaseline(
        string digest,
        CharacterCreationPrerequisiteAuthority current)
    {
        return MatchesHistorical(
            digest,
            current,
            option => IsNewlyAdmittedStandardDwarf(option));
    }

    /// <summary>
    /// Reconstructs the pre-Elf/Ork Human-only catalog by downgrading every
    /// admitted standard racial row (Elf/Ork/Dwarf), then compares the
    /// historical digest against the reconstruction.
    /// </summary>
    internal static bool MatchesLegacyHumanCatalog(
        string digest,
        CharacterCreationPrerequisiteAuthority current)
    {
        return MatchesHistorical(
            digest,
            current,
            option => option.MetatypeName is "Elf" or "Ork" or "Dwarf"
                && option.MetavariantSourceId is null
                && option.IsEnabled
                && option.Blockers.Count == 0
                && option.RacialQualitySources is { Count: > 0 });
    }

    /// <summary>
    /// Shared comparison core: verifies the current authority is
    /// authoritative and self-consistent, applies the supplied downgrade
    /// predicate, and compares the reconstructed digest in fixed time.
    /// </summary>
    private static bool MatchesHistorical(
        string digest,
        CharacterCreationPrerequisiteAuthority current,
        Func<CharacterCreationPriorityHeritageOptionProjection, bool> downgrade)
    {
        if (string.IsNullOrWhiteSpace(digest)
            || current is null
            || !current.IsAuthoritative
            || current.Blockers.Count != 0
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(digest)
            || !CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
                current.AuthorityDigest,
                CharacterCreationPrerequisiteAuthorityDigest.Compute(current)))
        {
            return false;
        }
        var historical = current with
        {
            Options = current.Options.Select(row => row with
            {
                HeritageOptions = row.HeritageOptions.Select(option =>
                    downgrade(option)
                        ? option with
                        {
                            IsEnabled = false,
                            RacialQualitySources = null,
                            BaseBonuses = null,
                            Blockers = [
                                CharacterCreationPrerequisiteBlockers
                                    .HeritageSelectionUnsupported]
                        }
                        : option).ToArray()
            }).ToArray()
        };
        return CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            digest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(historical));
    }

    /// <summary>
    /// Pending-draft compatibility: a draft persisted under an earlier
    /// catalog stays valid when its stored authority digest matches one of
    /// the historical reconstructions (Human-only era, or the 80584eff
    /// Elf/Ork-enabled baseline). Both comparisons are complete
    /// current-source comparisons; the digest itself is the gate, so a
    /// draft persisted while Dwarf was enabled (whose authority digest
    /// embeds an enabled Dwarf row) can never match either reconstruction.
    /// </summary>
    internal static bool MatchesLegacyPendingDraftAuthority(
        CharacterCreationPrerequisiteDraft draft,
        CharacterCreationPrerequisiteAuthority authority)
    {
        if (draft.HeritageSelection is not { MetavariantSourceId: null })
            return false;
        return MatchesLegacyHumanCatalog(draft.AuthorityDigest, authority)
            || MatchesLegacyElfOrkEnabledBaseline(draft.AuthorityDigest, authority);
    }

    /// <summary>
    /// Bootstrap-binding compatibility: an existing binding persisted under
    /// an earlier catalog keeps its original authority digest when the
    /// binding's recorded build shape corresponds to a metatype admitted by
    /// that earlier catalog. Shape and source/profile validation above this
    /// call is unchanged; only the catalog reconstruction differs.
    /// </summary>
    internal static bool MatchesLegacyBootstrapBinding(
        CharacterCreationBootstrapBinding existing,
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority)
    {
        return MatchesLegacyHumanCatalog(
            existing.PrerequisiteAuthorityDigest, prerequisiteAuthority)
            || MatchesLegacyElfOrkEnabledBaseline(
                existing.PrerequisiteAuthorityDigest, prerequisiteAuthority);
    }
}
