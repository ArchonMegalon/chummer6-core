namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved inputs used by Chummer5's shared SpiritControl Fettered/Pet checkbox.
/// </summary>
public sealed record CharacterSpiritFetteringBasis(
    Guid SpiritId,
    string EntityType,
    int Force,
    int Services,
    bool Bound,
    bool Fettered);

/// <summary>
/// Source-exact state for one stable Spirit or Sprite identity.
/// </summary>
public sealed record CharacterSpiritFetteringState(
    Guid SpiritId,
    string EntityType,
    bool Created,
    bool Fettered,
    int Force,
    int Services,
    bool Bound,
    bool SpriteFetteringAllowed,
    bool ActivationCostExact,
    int ActivationKarmaCost,
    int AvailableKarma,
    bool CanFetter,
    bool CanUnfetter);

public static class CharacterSpiritFetteringRules
{
    /// <summary>
    /// Mirrors Spirit.AllowFettering and Spirit.SetFettered. Saved state must prove every
    /// identity/type/Boolean involved in the one-Fettered-entity and Career unbound-limit rules.
    /// A Career Spirit activation additionally requires the active settings profile's exact
    /// KarmaSpiritFettering value; Sprite activation always costs Force.
    /// </summary>
    public static bool TryProject(
        Guid selectedSpiritId,
        bool created,
        int availableKarma,
        int? karmaSpiritFettering,
        bool? allowSpriteFettering,
        int spiritFetteringImprovementCount,
        IReadOnlyList<CharacterSpiritFetteringBasis> spirits,
        out CharacterSpiritFetteringState? state)
    {
        ArgumentNullException.ThrowIfNull(spirits);
        state = null;
        if (selectedSpiritId == Guid.Empty
            || karmaSpiritFettering is < 0
            || spiritFetteringImprovementCount < 0
            || spirits.Count == 0)
        {
            return false;
        }

        HashSet<Guid> identities = [];
        CharacterSpiritFetteringBasis? selected = null;
        int fetteredCount = 0;
        int fetteredSpiritCount = 0;
        foreach (CharacterSpiritFetteringBasis spirit in spirits)
        {
            if (spirit.SpiritId == Guid.Empty
                || !identities.Add(spirit.SpiritId)
                || spirit.EntityType is not ("Spirit" or "Sprite")
                || spirit.Force < 0
                || spirit.Services < 0)
            {
                return false;
            }

            if (spirit.Fettered)
            {
                if (spirit.EntityType == "Sprite" && allowSpriteFettering is not true)
                {
                    return false;
                }
                fetteredCount++;
                if (spirit.EntityType == "Spirit")
                {
                    fetteredSpiritCount++;
                }
            }
            if (spirit.SpiritId == selectedSpiritId)
            {
                selected = spirit;
            }
        }

        if (selected is null
            || fetteredCount > 1
            || spiritFetteringImprovementCount != fetteredSpiritCount)
        {
            return false;
        }

        bool spriteAllowed = selected.EntityType == "Spirit" || allowSpriteFettering is true;
        bool costExact = !created
            || selected.EntityType == "Sprite"
            || karmaSpiritFettering.HasValue;
        int activationCost = 0;
        if (created && costExact)
        {
            try
            {
                activationCost = selected.EntityType == "Sprite"
                    ? selected.Force
                    : checked(selected.Force * karmaSpiritFettering!.Value);
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        bool canFetter = !selected.Fettered
            && fetteredCount == 0
            && spriteAllowed
            && costExact;
        bool violatesCareerUnboundLimit = created
            && selected.Fettered
            && !selected.Bound
            && selected.Services > 0
            && spirits.Any(candidate =>
                candidate.SpiritId != selected.SpiritId
                && candidate.EntityType == selected.EntityType
                && candidate.Services > 0
                && !candidate.Bound
                && !candidate.Fettered);

        state = new CharacterSpiritFetteringState(
            selected.SpiritId,
            selected.EntityType,
            created,
            selected.Fettered,
            selected.Force,
            selected.Services,
            selected.Bound,
            spriteAllowed,
            costExact,
            activationCost,
            availableKarma,
            canFetter,
            selected.Fettered && !violatesCareerUnboundLimit);
        return true;
    }

    public static bool CanSet(CharacterSpiritFetteringState state, bool requestedFettered)
    {
        ArgumentNullException.ThrowIfNull(state);
        return requestedFettered == state.Fettered
            || (requestedFettered ? state.CanFetter : state.CanUnfetter);
    }
}
