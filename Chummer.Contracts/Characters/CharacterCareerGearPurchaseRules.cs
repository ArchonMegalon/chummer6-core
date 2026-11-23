using System.Globalization;
using System.Xml;
using System.Xml.Linq;

// Application alone consumes the opaque parsed policy inside a current-source catalog evaluation.
// Keep the policy-taking pricing seam off the public contracts surface.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Chummer.Application")]

namespace Chummer.Contracts.Characters;

/// <summary>Career purchase multipliers from PickGear; gear unit costs remain owned by CharacterGearQuantityRules.</summary>
public static class CharacterCareerGearPurchaseRules
{
    public const decimal MinimumMarkupPercent = -99.99m;
    public const decimal MaximumMarkupPercent = 1000m;
    public const decimal MarkupIncrement = 0.01m;

    public static bool IsMarkupValid(decimal value) => value >= MinimumMarkupPercent && value <= MaximumMarkupPercent
        && value % MarkupIncrement == 0m;

    public static bool TryPrice(CharacterGearCostSnapshot gear, decimal quantity, decimal markup,
        string legality, string settingsXml, out decimal unitCost, out decimal total, out int? maximumNuyenDecimals)
        => TryPriceCore(gear, quantity, markup, legality, settingsXml, null,
            out unitCost, out total, out maximumNuyenDecimals);

    private static bool TryPriceCore(CharacterGearCostSnapshot gear, decimal quantity, decimal markup,
        string legality, string? settingsXml, PricePolicy? policy,
        out decimal unitCost, out decimal total, out int? maximumNuyenDecimals)
    {
        unitCost = total = 0;
        maximumNuyenDecimals = null;
        if (!IsMarkupValid(markup) || legality is not ("" or "R" or "F") || string.IsNullOrWhiteSpace(gear.CostExpression)
            || !CharacterGearQuantityRules.TryCalculatePurchaseUnitCost(gear, out unitCost)
            || !CharacterGearQuantityRules.TryCalculatePurchaseTotalCost(gear with { Quantity = quantity }, out decimal quantityCost)) return false;
        try
        {
            // Preserve the public string path's legality rejection before parsing settings.
            if (legality is not ("" or "R" or "F")) return false;
            if (!(policy ?? PricePolicy.Parse(settingsXml!)).TryRead(legality, out decimal multiplier, out maximumNuyenDecimals)) return false;
            total = checked(quantityCost * (1m + markup / 100m) * multiplier);
            return total >= 0m;
        }
        catch (OverflowException) { return false; }
    }

    public static bool TryReadPricePolicy(string legality, string settingsXml,
        out decimal multiplier, out int? maximumNuyenDecimals)
    {
        multiplier = 1m;
        maximumNuyenDecimals = null;
        if (legality is not ("" or "R" or "F")) return false;
        return PricePolicy.Parse(settingsXml).TryRead(legality, out multiplier, out maximumNuyenDecimals);
    }

    // Only Parse can issue this immutable numeric/boolean value. It retains no XML, source
    // authority or operation result. Invalid R/F settings do not invalidate ordinary prices.
    internal sealed class PricePolicy
    {
        private readonly bool ordinaryValid;
        private readonly bool restrictedValid;
        private readonly decimal restrictedMultiplier;
        private readonly bool forbiddenValid;
        private readonly decimal forbiddenMultiplier;
        private readonly int? maximumNuyenDecimals;

        private PricePolicy(bool ordinaryValid, bool restrictedValid, decimal restrictedMultiplier,
            bool forbiddenValid, decimal forbiddenMultiplier, int? maximumNuyenDecimals)
        {
            this.ordinaryValid = ordinaryValid;
            this.restrictedValid = restrictedValid;
            this.restrictedMultiplier = restrictedMultiplier;
            this.forbiddenValid = forbiddenValid;
            this.forbiddenMultiplier = forbiddenMultiplier;
            this.maximumNuyenDecimals = maximumNuyenDecimals;
        }

        internal static PricePolicy Parse(string settingsXml)
        {
            try
            {
                using var reader = XmlReader.Create(new StringReader(settingsXml), new XmlReaderSettings
                    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
                var root = XElement.Load(reader);
                if (root.Name != "settings" || root.HasAttributes) return Invalid();
                string? Scalar(string name) => root.Elements(name).ToArray() is [var node]
                    && !node.HasAttributes && !node.HasElements ? node.Value : null;
                bool ReadMultiplier(string flag, string name, out decimal multiplier)
                {
                    multiplier = 1m;
                    if (!bool.TryParse(Scalar(flag), out bool enabled)) return false;
                    if (!enabled) return true; // A disabled multiplier never requires a factor.
                    if (!int.TryParse(Scalar(name), NumberStyles.None, CultureInfo.InvariantCulture, out int factor)
                        || factor < 0) return false;
                    multiplier = factor;
                    return true;
                }
                bool restricted = ReadMultiplier("multiplyrestrictedcost", "restrictedcostmultiplier", out decimal restrictedCost);
                bool forbidden = ReadMultiplier("multiplyforbiddencost", "forbiddencostmultiplier", out decimal forbiddenCost);
                int? decimalsCount = null;
                string? format = Scalar("nuyenformat");
                // Canonical Nuyen quantities use MaximumNuyenDecimals. Unsupported custom formats stay unresolved.
                if (!string.IsNullOrWhiteSpace(format) && format.All(c => c is '#' or '0' or ',' or '.') && format.Count(c => c == '.') <= 1)
                {
                    int point = format.IndexOf('.');
                    string decimals = point < 0 ? "" : format[(point + 1)..];
                    if (decimals.Length <= 28 && decimals.All(c => c is '#' or '0')) decimalsCount = decimals.Length;
                }
                return new(true, restricted, restrictedCost, forbidden, forbiddenCost, decimalsCount);
            }
            catch (Exception ex) when (ex is XmlException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
            { return Invalid(); }
        }

        private static PricePolicy Invalid() => new(false, false, 1m, false, 1m, null);

        internal bool TryRead(string legality, out decimal multiplier, out int? decimals)
        {
            multiplier = 1m;
            decimals = null;
            switch (legality)
            {
                case "" when ordinaryValid: break;
                case "R" when restrictedValid: multiplier = restrictedMultiplier; break;
                case "F" when forbiddenValid: multiplier = forbiddenMultiplier; break;
                default: return false;
            }
            decimals = maximumNuyenDecimals;
            return true;
        }

        internal bool TryPrice(CharacterGearCostSnapshot gear, decimal quantity, decimal markup, string legality,
            out decimal unitCost, out decimal total, out int? decimals)
            => TryPriceCore(gear, quantity, markup, legality, null, this, out unitCost, out total, out decimals);
    }
}
