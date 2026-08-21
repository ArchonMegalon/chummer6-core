namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact saved state behind Chummer5's "Counts towards Critter Power limit" checkbox.
/// </summary>
public sealed record CharacterCritterPowerCountState(
    Guid CritterPowerId,
    bool CountsTowardsLimit);

public static class CharacterCritterPowerCountRules
{
    public const bool LegacyDefault = true;

    /// <summary>
    /// Projects the legacy state only when the saved critter power has one stable identity and
    /// at most one valid Boolean value. Chummer5 defaults a missing counttowardslimit element to
    /// true, because the backing field is initialized to true before loading.
    /// </summary>
    public static bool TryProject(
        IReadOnlyList<string> savedIdentities,
        IReadOnlyList<string> savedValues,
        out CharacterCritterPowerCountState? state)
    {
        state = null;
        if (savedIdentities.Count != 1
            || !Guid.TryParseExact(savedIdentities[0].Trim(), "D", out Guid critterPowerId)
            || critterPowerId == Guid.Empty
            || savedValues.Count > 1)
        {
            return false;
        }

        bool countsTowardsLimit = LegacyDefault;
        if (savedValues.Count == 1
            && !bool.TryParse(savedValues[0].Trim(), out countsTowardsLimit))
        {
            return false;
        }

        state = new CharacterCritterPowerCountState(critterPowerId, countsTowardsLimit);
        return true;
    }
}
