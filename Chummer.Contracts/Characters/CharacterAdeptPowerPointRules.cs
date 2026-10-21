namespace Chummer.Contracts.Characters;

/// <summary>
/// Evaluated engine inputs, not request-side authority. EffectiveAdeptMagic is the
/// resolved MAGAdept value, including the selected separate-Magic policy.
/// PurchasedMysticPoints is the saved purchased pool, not an initiation grade.
/// </summary>
public sealed record CharacterAdeptPowerPointPoolInput(
    bool IsAdept,
    bool IsMagician,
    bool UsesSeparateAdeptMagic,
    int EffectiveAdeptMagic,
    int PurchasedMysticPoints,
    decimal PowerPointImprovements);

/// <summary>
/// One power after source, prerequisite, Way and improvement evaluation.
/// MaximumTotalRating includes source, Magic and applicable skill/house-rule caps.
/// FreeLevelMagicLimit is the resolved Magic cap on free levels (also when
/// IgnoreRules bypasses the purchased-rating cap). FreePointQuarterUnits is the
/// matching AdeptPowerFreePoints improvement Rating sum, not a decimal PP amount.
/// </summary>
public sealed record CharacterAdeptPowerPointInput(
    int PaidRating,
    bool LevelsEnabled,
    int MaximumTotalRating,
    int FreeLevelMagicLimit,
    decimal PointsPerLevel,
    decimal ExtraPointCost,
    decimal AppliedWayDiscount,
    int GrantedLevels,
    int FreePointQuarterUnits);

public sealed record CharacterAdeptPowerPointCost(
    int PaidRating,
    int FreeLevels,
    int TotalRating,
    decimal FreePoints,
    decimal UsedPoints);

public sealed record CharacterAdeptPowerPointIncreaseQuote(
    CharacterAdeptPowerPointCost Current,
    CharacterAdeptPowerPointCost Proposed,
    decimal TotalPoints,
    decimal OtherPowersUsedPoints,
    decimal IncrementalCost,
    decimal CurrentRemainingPoints,
    decimal ProposedRemainingPoints,
    bool HasEnoughPoints);

/// <summary>
/// Pure SR5 point arithmetic extracted from Character.PowerPointsTotal and
/// Power.FreeLevels/PowerPoints/TotalRating. This is not source admission, an
/// effect reducer, a Career mutation command or permission to use a Creation
/// budget. An owner-bound executor must resolve all inputs, revalidate source
/// eligibility and recalculate effects before persisting a proposed rating.
/// </summary>
public static class CharacterAdeptPowerPointRules
{
    public static bool TryCalculatePool(CharacterAdeptPowerPointPoolInput input, out decimal total)
    {
        total = 0;
        if (input is null || !input.IsAdept
            || input.EffectiveAdeptMagic < 0 || input.PurchasedMysticPoints < 0)
            return false;
        try
        {
            // Character.UseMysticAdeptPPs does not equate Magic or grade to
            // purchased mystic adept points. A separate MAGAdept profile does.
            decimal basis = input.IsMagician && !input.UsesSeparateAdeptMagic
                ? input.PurchasedMysticPoints : input.EffectiveAdeptMagic;
            total = Math.Max(checked(basis + input.PowerPointImprovements), 0m);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    public static bool TryCalculateCost(CharacterAdeptPowerPointInput input,
        out CharacterAdeptPowerPointCost? cost)
    {
        cost = null;
        if (!Valid(input)) return false;
        try
        {
            decimal freePoints = input.FreePointQuarterUnits * 0.25m;
            decimal freeLevels = input.GrantedLevels;
            if (input.PaidRating == 0 && input.GrantedLevels == 0 && input.ExtraPointCost > 0)
            {
                // Legacy Power.FreeLevels first buys the entry fee and first
                // level. Its extra-cost branch then uses whole-point steps.
                // Preserve this behavior; do not silently replace it with the
                // per-level conversion used by powers without an entry fee.
                decimal remainder = checked(freePoints - input.PointsPerLevel - input.ExtraPointCost);
                if (remainder >= 0)
                    freeLevels = checked(1m + decimal.Floor(remainder));
            }
            else if (input.PointsPerLevel > 0)
            {
                // If the quotient exceeds the Magic cap, division could itself
                // overflow for a valid tiny decimal source cost. Cap first.
                decimal levelsNeeded = Math.Max(0m, input.FreeLevelMagicLimit - freeLevels);
                bool fillsCap;
                try { fillsCap = freePoints >= checked(levelsNeeded * input.PointsPerLevel); }
                catch (OverflowException) { fillsCap = false; }
                freeLevels = fillsCap ? input.FreeLevelMagicLimit
                    : checked(freeLevels + decimal.Floor(freePoints / input.PointsPerLevel));
            }
            int granted = (int)Math.Min(freeLevels, input.FreeLevelMagicLimit);
            int totalRating = (int)Math.Min((long)input.PaidRating + granted, input.MaximumTotalRating);
            decimal used = 0m;
            if (input.PaidRating != 0 && (input.LevelsEnabled || granted == 0))
            {
                decimal basis = checked(input.ExtraPointCost - input.AppliedWayDiscount);
                decimal paid = checked(granted * input.PointsPerLevel) >= freePoints
                    ? checked(input.PaidRating * input.PointsPerLevel)
                    : checked(totalRating * input.PointsPerLevel - freePoints);
                used = Math.Max(checked(basis + paid), 0m);
            }
            cost = new(input.PaidRating, granted, totalRating, freePoints, used);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// Quotes an increase for the SAME admitted power under unchanged source,
    /// grant and discount inputs. Re-evaluates free-level conversion at the new
    /// paid rating. Does not issue a mutation token or implement receipt replay.
    /// False rejects decreases, duplicates, ineffective increases and malformed
    /// inputs. An unaffordable valid quote remains inspectable.
    /// </summary>
    public static bool TryQuoteIncrease(CharacterAdeptPowerPointInput current,
        int proposedPaidRating, decimal totalPoints, decimal otherPowersUsedPoints,
        out CharacterAdeptPowerPointIncreaseQuote? quote)
    {
        quote = null;
        if (current is null || proposedPaidRating <= current.PaidRating
            || totalPoints < 0 || otherPowersUsedPoints < 0
            || !TryCalculateCost(current, out var before) || before is null
            || !TryCalculateCost(current with { PaidRating = proposedPaidRating }, out var after)
            || after is null || after.TotalRating <= before.TotalRating)
            return false;
        try
        {
            decimal increment = checked(after.UsedPoints - before.UsedPoints);
            if (increment < 0) return false; // An increase is never a refund/respec.
            decimal beforeRemaining = checked(totalPoints - otherPowersUsedPoints - before.UsedPoints);
            decimal afterRemaining = checked(totalPoints - otherPowersUsedPoints - after.UsedPoints);
            quote = new(before, after, totalPoints, otherPowersUsedPoints, increment,
                beforeRemaining, afterRemaining, beforeRemaining >= 0 && afterRemaining >= 0);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool Valid(CharacterAdeptPowerPointInput? input) =>
        input is not null && input.PaidRating >= 0 && input.MaximumTotalRating >= 0
        && input.PaidRating <= input.MaximumTotalRating
        && (input.LevelsEnabled || input.MaximumTotalRating <= 1)
        && input.FreeLevelMagicLimit >= 0 && input.PointsPerLevel >= 0
        && input.ExtraPointCost >= 0 && input.AppliedWayDiscount >= 0
        && input.GrantedLevels >= 0 && input.FreePointQuarterUnits >= 0;
}
