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
    private static bool TryReconstruct(
        string digest,
        CharacterCreationPrerequisiteAuthority current,
        Func<CharacterCreationPriorityHeritageOptionProjection, bool> downgrade,
        out CharacterCreationPrerequisiteAuthority? reconstructed)
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
            reconstructed = null;
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
        if (!CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(
            digest,
            CharacterCreationPrerequisiteAuthorityDigest.Compute(historical)))
        {
            reconstructed = null;
            return false;
        }
        reconstructed = historical;
        return true;
    }

    /// <summary>Boolean wrapper over <see cref="TryReconstruct"/> preserving
    /// the exact historical comparison semantics for existing callers.</summary>
    private static bool MatchesHistorical(
        string digest,
        CharacterCreationPrerequisiteAuthority current,
        Func<CharacterCreationPriorityHeritageOptionProjection, bool> downgrade)
    {
        return TryReconstruct(digest, current, downgrade, out _);
    }

    /// <summary>Independent-oracle accessor: reconstructs the immediate
    /// 80584eff baseline (Elf/Ork enabled, Dwarf disabled) from current
    /// sources and returns it only when the supplied digest matches exactly.
    /// Tests use this to prove the production reconstruction is byte-equal
    /// to an independently written 80584 oracle reconstruction.</summary>
    internal static bool TryReconstructLegacyElfOrkEnabledBaseline(
        string digest,
        CharacterCreationPrerequisiteAuthority current,
        out CharacterCreationPrerequisiteAuthority? reconstructed)
    {
        return TryReconstruct(
            digest,
            current,
            option => IsNewlyAdmittedStandardDwarf(option),
            out reconstructed);
    }

    /// <summary>Independent-oracle accessor for the pre-Elf/Ork Human-only
    /// catalog reconstruction (every admitted standard racial row
    /// downgraded).</summary>
    internal static bool TryReconstructLegacyHumanCatalog(
        string digest,
        CharacterCreationPrerequisiteAuthority current,
        out CharacterCreationPrerequisiteAuthority? reconstructed)
    {
        return TryReconstruct(
            digest,
            current,
            option => option.MetatypeName is "Elf" or "Ork" or "Dwarf"
                && option.MetavariantSourceId is null
                && option.IsEnabled
                && option.Blockers.Count == 0
                && option.RacialQualitySources is { Count: > 0 },
            out reconstructed);
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
        // C3 gen3: eligibility binds to the MATCHED historical reconstruction.
        // The digest gate alone would admit a Dwarf draft (newly admitted)
        // claiming either legacy era, or an Elf/Ork draft claiming the
        // Human-only era, because DraftIntegrity canonical equality runs
        // against the CURRENT authority projections. The draft's selected
        // heritage must be an enabled, blocker-free row of the matched
        // reconstruction: Dwarf is refused under both eras, Elf/Ork under
        // the Human-only era, and matching Human/Elf/Ork drafts (Priority
        // and SumToTen) are preserved with their original persisted
        // identities.
        if (TryReconstructLegacyHumanCatalog(draft.AuthorityDigest, authority, out var human)
            && SelectionAdmitted(human, draft.HeritageSelection.SelectionId))
        {
            return true;
        }
        return TryReconstructLegacyElfOrkEnabledBaseline(draft.AuthorityDigest, authority, out var baseline)
            && SelectionAdmitted(baseline, draft.HeritageSelection.SelectionId);
    }

    /// <summary>True when the matched historical reconstruction carries the
    /// given selection id exactly once as an enabled, blocker-free row.</summary>
    private static bool SelectionAdmitted(
        CharacterCreationPrerequisiteAuthority? reconstruction,
        string selectionId)
    {
        if (reconstruction is null || string.IsNullOrEmpty(selectionId))
        {
            return false;
        }
        CharacterCreationPriorityHeritageOptionProjection[] matches = reconstruction.Options
            .SelectMany(row => row.HeritageOptions)
            .Where(option => string.Equals(option.SelectionId, selectionId, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        return matches.Length == 1
            && matches[0].IsEnabled
            && matches[0].Blockers.Count == 0;
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
