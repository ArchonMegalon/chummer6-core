using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Chummer.Contracts.Characters;

public enum CharacterCyberwareCommerceAction
{
    Upgrade,
    Sell
}

public sealed record CharacterCyberwareGradeOption(
    string Id,
    string Name,
    decimal CostMultiplier,
    decimal EssenceMultiplier);

public sealed record CharacterCyberwareCommerceSnapshot(
    Guid CyberwareId,
    string Name,
    string ParentGuid,
    string Capacity,
    int CurrentRating,
    int MinimumRating,
    int MaximumRating,
    string CurrentGradeId,
    string CurrentGradeName,
    string CostExpression,
    string EssenceExpression,
    bool DiscountedCost,
    bool AddToParentEssence,
    decimal ExtraEssenceAdditiveMultiplier,
    decimal ExtraEssenceMultiplicativeMultiplier,
    decimal EssenceDiscountPercent,
    int EssenceDecimals,
    bool DoNotRoundEssenceInternally,
    decimal AvailableNuyen,
    int? EssenceHoleRating,
    int? EssenceAntiHoleRating,
    IReadOnlyList<CharacterCyberwareGradeOption> GradeOptions);

public sealed record CharacterCyberwareCommerceSemantics(
    bool UpgradeExact,
    string UpgradeBlockReason,
    bool SellExact,
    string SellBlockReason,
    CharacterCyberwareCommerceSnapshot? Snapshot);

public sealed record CharacterCyberwareCommerceQuote(
    CharacterCyberwareCommerceAction Action,
    bool Exact,
    string BlockReason,
    Guid CyberwareId,
    string GradeId,
    string GradeName,
    int Rating,
    decimal RefundPercentage,
    decimal RefundRatio,
    bool FreeCost,
    decimal CurrentTotalCost,
    decimal NewTotalCost,
    decimal SaleCredit,
    decimal NetNuyenCost,
    decimal NuyenDelta,
    decimal CurrentEssence,
    decimal NewEssence,
    decimal EssenceDelta,
    int? NewEssenceHoleRating,
    int? NewEssenceAntiHoleRating,
    bool RatingReplayRequired,
    bool GradeReplayRequired,
    string QuoteDigest);

/// <summary>
/// Pure, typed Chummer5-compatible quote authority for the bounded Cyberware
/// Career commerce path. Callers must not mutate when Exact is false.
/// </summary>
public static class CharacterCyberwareCommerceRules
{
    public const decimal DefaultRefundPercentage = 50m;
    public const decimal MinimumRefundPercentage = 0m;
    public const decimal MaximumRefundPercentage = 9_999.99m;
    public const int RefundPercentageDecimalPlaces = 2;

    public static CharacterCyberwareCommerceQuote QuoteUpgrade(
        CharacterCyberwareCommerceSemantics semantics,
        string gradeId,
        int rating,
        decimal refundPercentage,
        bool freeCost)
    {
        if (!semantics.UpgradeExact || semantics.Snapshot is null)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                semantics.Snapshot?.CyberwareId ?? Guid.Empty,
                semantics.UpgradeBlockReason);
        }

        CharacterCyberwareCommerceSnapshot snapshot = semantics.Snapshot;
        if (!TryNormalizeRefundPercentage(refundPercentage, out decimal refundRatio))
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                snapshot.CyberwareId,
                "Refund percentage must be an exact value from 0.00 through 9999.99 with at most two decimal places.");
        }

        CharacterCyberwareGradeOption? grade = snapshot.GradeOptions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, gradeId, StringComparison.OrdinalIgnoreCase));
        if (grade is null)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                snapshot.CyberwareId,
                "The selected Cyberware grade is unavailable from the exact saved source profile.");
        }
        if (rating < snapshot.MinimumRating || rating > snapshot.MaximumRating)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                snapshot.CyberwareId,
                $"Cyberware rating must be between {snapshot.MinimumRating.ToString(CultureInfo.InvariantCulture)} and {snapshot.MaximumRating.ToString(CultureInfo.InvariantCulture)}.");
        }

        CharacterCyberwareGradeOption? currentGrade = snapshot.GradeOptions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, snapshot.CurrentGradeId, StringComparison.OrdinalIgnoreCase));
        if (currentGrade is null
            || !TryCalculateTotalCost(snapshot, snapshot.CurrentRating, currentGrade, out decimal currentTotal)
            || !TryCalculateTotalCost(snapshot, rating, grade, out decimal newTotal)
            || !TryCalculateEssence(snapshot, snapshot.CurrentRating, currentGrade, out decimal currentEssence)
            || !TryCalculateEssence(snapshot, rating, grade, out decimal newEssence))
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                snapshot.CyberwareId,
                "The exact Chummer5 cost or Essence expression could not be evaluated.");
        }

        try
        {
            decimal saleCredit = checked(currentTotal * refundRatio);
            decimal netCost = freeCost ? 0m : checked(newTotal - saleCredit);
            if (netCost > snapshot.AvailableNuyen)
            {
                return Blocked(
                    CharacterCyberwareCommerceAction.Upgrade,
                    snapshot.CyberwareId,
                    $"Cyberware upgrade costs {netCost.ToString(CultureInfo.InvariantCulture)} Nuyen but only {snapshot.AvailableNuyen.ToString(CultureInfo.InvariantCulture)} is available.");
            }

            decimal essenceDelta = checked(newEssence - currentEssence);
            if (!TryPlanEssenceHole(
                    snapshot,
                    essenceDelta,
                    out int? newHoleRating,
                    out int? newAntiHoleRating,
                    out string essenceBlockReason))
            {
                return Blocked(
                    CharacterCyberwareCommerceAction.Upgrade,
                    snapshot.CyberwareId,
                    essenceBlockReason);
            }

            var unsigned = new CharacterCyberwareCommerceQuote(
                CharacterCyberwareCommerceAction.Upgrade,
                Exact: true,
                BlockReason: string.Empty,
                snapshot.CyberwareId,
                grade.Id,
                grade.Name,
                rating,
                refundPercentage,
                refundRatio,
                freeCost,
                currentTotal,
                newTotal,
                saleCredit,
                netCost,
                NuyenDelta: -netCost,
                currentEssence,
                newEssence,
                essenceDelta,
                newHoleRating,
                newAntiHoleRating,
                RatingReplayRequired: snapshot.CurrentRating != rating,
                GradeReplayRequired: currentGrade.EssenceMultiplier != grade.EssenceMultiplier,
                QuoteDigest: string.Empty);
            return unsigned with { QuoteDigest = Digest(unsigned) };
        }
        catch (OverflowException)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Upgrade,
                snapshot.CyberwareId,
                "Cyberware upgrade arithmetic exceeded the exact saved-data range.");
        }
    }

    public static CharacterCyberwareCommerceQuote QuoteSale(
        CharacterCyberwareCommerceSemantics semantics,
        decimal refundPercentage)
    {
        if (!semantics.SellExact || semantics.Snapshot is null)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Sell,
                semantics.Snapshot?.CyberwareId ?? Guid.Empty,
                semantics.SellBlockReason);
        }

        CharacterCyberwareCommerceSnapshot snapshot = semantics.Snapshot;
        if (!TryNormalizeRefundPercentage(refundPercentage, out decimal refundRatio))
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Sell,
                snapshot.CyberwareId,
                "Sale percentage must be an exact value from 0.00 through 9999.99 with at most two decimal places.");
        }

        CharacterCyberwareGradeOption? currentGrade = snapshot.GradeOptions.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, snapshot.CurrentGradeId, StringComparison.OrdinalIgnoreCase));
        if (currentGrade is null
            || !TryCalculateTotalCost(snapshot, snapshot.CurrentRating, currentGrade, out decimal currentTotal)
            || !TryCalculateEssence(snapshot, snapshot.CurrentRating, currentGrade, out decimal currentEssence))
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Sell,
                snapshot.CyberwareId,
                "The exact Chummer5 sale cost could not be evaluated.");
        }

        try
        {
            // The bounded path admits only childless ware and an exact parent
            // child-cost multiplier of one, so this is both the top-level
            // (DeleteCyberware + original total) and parent delta result.
            decimal proceeds = checked(currentTotal * refundRatio);
            decimal essenceDelta = string.IsNullOrEmpty(snapshot.ParentGuid) ? -currentEssence : 0m;
            if (!TryPlanEssenceHole(
                    snapshot,
                    essenceDelta,
                    out int? newHoleRating,
                    out int? newAntiHoleRating,
                    out string essenceBlockReason))
            {
                return Blocked(
                    CharacterCyberwareCommerceAction.Sell,
                    snapshot.CyberwareId,
                    essenceBlockReason);
            }
            var unsigned = new CharacterCyberwareCommerceQuote(
                CharacterCyberwareCommerceAction.Sell,
                Exact: true,
                BlockReason: string.Empty,
                snapshot.CyberwareId,
                snapshot.CurrentGradeId,
                snapshot.CurrentGradeName,
                snapshot.CurrentRating,
                refundPercentage,
                refundRatio,
                FreeCost: false,
                currentTotal,
                NewTotalCost: 0m,
                SaleCredit: proceeds,
                NetNuyenCost: 0m,
                NuyenDelta: proceeds,
                currentEssence,
                NewEssence: 0m,
                essenceDelta,
                newHoleRating,
                newAntiHoleRating,
                RatingReplayRequired: false,
                GradeReplayRequired: false,
                QuoteDigest: string.Empty);
            return unsigned with { QuoteDigest = Digest(unsigned) };
        }
        catch (OverflowException)
        {
            return Blocked(
                CharacterCyberwareCommerceAction.Sell,
                snapshot.CyberwareId,
                "Cyberware sale arithmetic exceeded the exact saved-data range.");
        }
    }

    public static bool TryNormalizeRefundPercentage(decimal percentage, out decimal ratio)
    {
        ratio = 0m;
        if (percentage < MinimumRefundPercentage
            || percentage > MaximumRefundPercentage
            || decimal.Round(percentage, RefundPercentageDecimalPlaces, MidpointRounding.AwayFromZero) != percentage)
        {
            return false;
        }

        ratio = percentage / 100m;
        return true;
    }

    public static bool TryEvaluateRatingExpression(
        string? expression,
        int rating,
        int minimumRating,
        out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        string normalized = expression
            .Replace("{MinRating}", minimumRating.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("MinRating", minimumRating.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{Rating}", "Rating", StringComparison.Ordinal);
        return CharacterGearQuantityRules.TryEvaluateCostExpression(normalized, rating, out value);
    }

    private static bool TryCalculateTotalCost(
        CharacterCyberwareCommerceSnapshot snapshot,
        int rating,
        CharacterCyberwareGradeOption grade,
        out decimal value)
    {
        value = 0m;
        if (!TryEvaluateRatingExpression(
                snapshot.CostExpression,
                rating,
                snapshot.MinimumRating,
                out decimal baseCost))
        {
            return false;
        }

        try
        {
            value = checked(baseCost * grade.CostMultiplier);
            if (snapshot.DiscountedCost)
            {
                value = checked(value * 0.9m);
            }
            return value >= 0m;
        }
        catch (OverflowException)
        {
            value = 0m;
            return false;
        }
    }

    private static bool TryCalculateEssence(
        CharacterCyberwareCommerceSnapshot snapshot,
        int rating,
        CharacterCyberwareGradeOption grade,
        out decimal value)
    {
        value = 0m;
        if (!snapshot.AddToParentEssence && !string.IsNullOrEmpty(snapshot.ParentGuid))
        {
            return true;
        }
        if (!TryEvaluateRatingExpression(
                snapshot.EssenceExpression,
                rating,
                snapshot.MinimumRating,
                out decimal baseEssence))
        {
            return false;
        }

        try
        {
            decimal additive = grade.EssenceMultiplier + snapshot.ExtraEssenceAdditiveMultiplier;
            decimal totalMultiplier = Math.Max(0m, checked(additive * snapshot.ExtraEssenceMultiplicativeMultiplier));
            if (snapshot.EssenceDiscountPercent != 0m)
            {
                totalMultiplier = checked(totalMultiplier * (1m - snapshot.EssenceDiscountPercent / 100m));
            }
            value = checked(baseEssence * totalMultiplier);
            if (!snapshot.DoNotRoundEssenceInternally)
            {
                value = decimal.Round(value, snapshot.EssenceDecimals, MidpointRounding.AwayFromZero);
            }
            return value >= 0m;
        }
        catch (Exception exception) when (exception is OverflowException or ArgumentOutOfRangeException)
        {
            value = 0m;
            return false;
        }
    }

    private static bool TryPlanEssenceHole(
        CharacterCyberwareCommerceSnapshot snapshot,
        decimal essenceDelta,
        out int? newHoleRating,
        out int? newAntiHoleRating,
        out string blockReason)
    {
        newHoleRating = snapshot.EssenceHoleRating;
        newAntiHoleRating = snapshot.EssenceAntiHoleRating;
        blockReason = string.Empty;
        if (essenceDelta == 0m)
        {
            return true;
        }

        int centiEssence;
        try
        {
            centiEssence = decimal.ToInt32(decimal.Ceiling(decimal.Abs(essenceDelta) * 100m));
        }
        catch (OverflowException)
        {
            blockReason = "Essence Hole adjustment exceeded the exact Chummer5 centi-Essence range.";
            return false;
        }

        if (essenceDelta > 0m)
        {
            int hole = newHoleRating.GetValueOrDefault();
            int consumed = Math.Min(hole, centiEssence);
            hole -= consumed;
            centiEssence -= consumed;
            newHoleRating = snapshot.EssenceHoleRating.HasValue ? hole : null;
            if (centiEssence > 0)
            {
                if (!newAntiHoleRating.HasValue)
                {
                    blockReason = "This upgrade would need a new Essence Anti-Hole object; the bounded path refuses source-generated objects.";
                    return false;
                }
                newAntiHoleRating = checked(newAntiHoleRating.Value + centiEssence);
            }
        }
        else
        {
            int antiHole = newAntiHoleRating.GetValueOrDefault();
            int consumed = Math.Min(antiHole, centiEssence);
            antiHole -= consumed;
            centiEssence -= consumed;
            newAntiHoleRating = snapshot.EssenceAntiHoleRating.HasValue ? antiHole : null;
            if (centiEssence > 0)
            {
                if (!newHoleRating.HasValue)
                {
                    blockReason = "This upgrade would need a new Essence Hole object; the bounded path refuses source-generated objects.";
                    return false;
                }
                newHoleRating = checked(newHoleRating.Value + centiEssence);
            }
        }

        return true;
    }

    private static CharacterCyberwareCommerceQuote Blocked(
        CharacterCyberwareCommerceAction action,
        Guid cyberwareId,
        string? reason)
        => new(
            action,
            Exact: false,
            BlockReason: string.IsNullOrWhiteSpace(reason)
                ? "Exact Cyberware commerce semantics are unavailable."
                : reason,
            cyberwareId,
            GradeId: string.Empty,
            GradeName: string.Empty,
            Rating: 0,
            RefundPercentage: 0m,
            RefundRatio: 0m,
            FreeCost: false,
            CurrentTotalCost: 0m,
            NewTotalCost: 0m,
            SaleCredit: 0m,
            NetNuyenCost: 0m,
            NuyenDelta: 0m,
            CurrentEssence: 0m,
            NewEssence: 0m,
            EssenceDelta: 0m,
            NewEssenceHoleRating: null,
            NewEssenceAntiHoleRating: null,
            RatingReplayRequired: false,
            GradeReplayRequired: false,
            QuoteDigest: string.Empty);

    private static string Digest(CharacterCyberwareCommerceQuote quote)
    {
        string canonical = string.Join(
            "\n",
            "cyberware-commerce-v1",
            quote.Action.ToString(),
            quote.CyberwareId.ToString("D"),
            quote.GradeId,
            quote.GradeName,
            quote.Rating.ToString(CultureInfo.InvariantCulture),
            quote.RefundPercentage.ToString(CultureInfo.InvariantCulture),
            quote.RefundRatio.ToString(CultureInfo.InvariantCulture),
            quote.FreeCost.ToString(CultureInfo.InvariantCulture),
            quote.CurrentTotalCost.ToString(CultureInfo.InvariantCulture),
            quote.NewTotalCost.ToString(CultureInfo.InvariantCulture),
            quote.SaleCredit.ToString(CultureInfo.InvariantCulture),
            quote.NetNuyenCost.ToString(CultureInfo.InvariantCulture),
            quote.NuyenDelta.ToString(CultureInfo.InvariantCulture),
            quote.CurrentEssence.ToString(CultureInfo.InvariantCulture),
            quote.NewEssence.ToString(CultureInfo.InvariantCulture),
            quote.EssenceDelta.ToString(CultureInfo.InvariantCulture),
            quote.NewEssenceHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            quote.NewEssenceAntiHoleRating?.ToString(CultureInfo.InvariantCulture) ?? "absent",
            quote.RatingReplayRequired.ToString(CultureInfo.InvariantCulture),
            quote.GradeReplayRequired.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
