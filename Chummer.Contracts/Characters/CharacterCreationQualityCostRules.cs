namespace Chummer.Contracts.Characters;

/// <summary>
/// Profile-owned creation quality arithmetic. Source Karma/BP values remain unmodified
/// in selections and legacy instances; the multiplier is applied only to budgets.
/// </summary>
public sealed record CharacterCreationQualityCostPolicy(
    int KarmaMultiplier,
    bool DoublePositiveExcess,
    bool CapNegativeRebate)
{
    public static CharacterCreationQualityCostPolicy Default { get; } = new(1, false, false);
}

public sealed record CharacterCreationQualityCostItem(
    int SourceKarma,
    bool CountsAgainstQualityLimit,
    bool CountsAgainstKarma,
    bool CountsAgainstMetagenicLimit);

public sealed record CharacterCreationQualityCostTotals(
    int PositiveLimitKarma,
    int NegativeLimitKarma,
    int MetagenicPositiveKarma,
    int MetagenicNegativeKarma,
    int PositiveKarmaSpent,
    int NegativeKarmaGranted,
    int NetKarmaSpent);

/// <summary>
/// Method-independent arithmetic for already-resolved quality instances. This does not
/// admit options, resolve improvements/contacts or authorize a purchase. Callers must
/// resolve those mechanics separately, or reject unsupported sources before evaluation.
/// Mirrors Character.Positive/NegativeQualityKarma and QualityLimitKarma: cap-exempt
/// purchases are added after excess rules; metagenic balance uses unmultiplied BP.
/// </summary>
public static class CharacterCreationQualityCostRules
{
    public static bool TryCalculate(
        CharacterCreationQualityCostPolicy policy,
        int qualityKarmaLimit,
        IReadOnlyList<CharacterCreationQualityCostItem> items,
        out CharacterCreationQualityCostTotals totals)
        => TryCalculate(policy, qualityKarmaLimit, items, 0, out totals);

    /// <summary>
    /// Group-contact cost is already multiplied by the profile's KarmaContact,
    /// not KarmaQuality. It enters the positive cap before the excess rule, as
    /// in Character.PositiveQualityKarma/PositiveQualityLimitKarma.
    /// </summary>
    public static bool TryCalculate(
        CharacterCreationQualityCostPolicy policy,
        int qualityKarmaLimit,
        IReadOnlyList<CharacterCreationQualityCostItem> items,
        int groupContactKarma,
        out CharacterCreationQualityCostTotals totals)
        => TryCalculate(policy, qualityKarmaLimit, items, groupContactKarma, 0m, 0m, out totals);

    /// <summary>
    /// Already-admitted free-quality improvements are summed before rounding.
    /// Mirrors the async creation path: negative rebate scales the free value by
    /// KarmaQuality, while NegativeQualityLimitKarma uses its unscaled value.
    /// StandardRound rounds every fraction away from zero, not just midpoints.
    /// </summary>
    public static bool TryCalculate(
        CharacterCreationQualityCostPolicy policy,
        int qualityKarmaLimit,
        IReadOnlyList<CharacterCreationQualityCostItem> items,
        int groupContactKarma,
        decimal freePositiveQualities,
        decimal freeNegativeQualities,
        out CharacterCreationQualityCostTotals totals)
    {
        totals = new(0, 0, 0, 0, 0, 0, 0);
        if (policy is null || policy.KarmaMultiplier < 0 || qualityKarmaLimit < 0
            || items is null || items.Count > 131_072 || groupContactKarma < 0)
            return false;

        try
        {
            checked
            {
                long positiveLimit = 0, negativeLimit = 0;
                long positiveCapped = 0, negativeCapped = 0;
                long positiveExempt = 0, negativeExempt = 0;
                long metagenicPositive = 0, metagenicNegative = 0;
                foreach (CharacterCreationQualityCostItem item in items)
                {
                    if (item is null) return false;
                    long value = item.SourceKarma;
                    if (item.CountsAgainstQualityLimit)
                    {
                        if (value > 0) positiveLimit += value;
                        else negativeLimit -= value;
                    }
                    if (item.CountsAgainstKarma)
                    {
                        if (item.CountsAgainstQualityLimit)
                        {
                            if (value > 0) positiveCapped += value;
                            else negativeCapped -= value;
                        }
                        else
                        {
                            if (value > 0) positiveExempt += value;
                            else negativeExempt -= value;
                        }
                    }
                    if (item.CountsAgainstMetagenicLimit)
                    {
                        if (value > 0) metagenicPositive += value;
                        else metagenicNegative -= value;
                    }
                }

                positiveLimit *= policy.KarmaMultiplier;
                negativeLimit *= policy.KarmaMultiplier;
                positiveCapped *= policy.KarmaMultiplier;
                negativeCapped *= policy.KarmaMultiplier;
                positiveLimit += groupContactKarma;
                positiveCapped += groupContactKarma;
                long positiveAllowance = RoundAway(freePositiveQualities * policy.KarmaMultiplier);
                positiveLimit -= positiveAllowance;
                positiveCapped -= positiveAllowance;
                negativeLimit += RoundAway(freeNegativeQualities);
                negativeCapped += RoundAway(freeNegativeQualities * policy.KarmaMultiplier);
                metagenicNegative += RoundAway(freeNegativeQualities);
                if (policy.DoublePositiveExcess)
                {
                    positiveLimit += Math.Max(0, positiveLimit - qualityKarmaLimit);
                    positiveCapped += Math.Max(0, positiveCapped - qualityKarmaLimit);
                }
                if (policy.CapNegativeRebate)
                {
                    negativeLimit = Math.Min(negativeLimit, qualityKarmaLimit);
                    negativeCapped = Math.Min(negativeCapped, qualityKarmaLimit);
                }

                long positiveSpent = positiveCapped + positiveExempt * policy.KarmaMultiplier;
                long negativeGranted = negativeCapped + negativeExempt * policy.KarmaMultiplier;
                totals = new((int)positiveLimit, (int)negativeLimit,
                    (int)metagenicPositive, (int)metagenicNegative,
                    (int)positiveSpent, (int)negativeGranted, (int)(positiveSpent - negativeGranted));
                return true;
            }
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static long RoundAway(decimal value)
        => checked((long)(value < 0 ? decimal.Floor(value) : decimal.Ceiling(value)));
}
