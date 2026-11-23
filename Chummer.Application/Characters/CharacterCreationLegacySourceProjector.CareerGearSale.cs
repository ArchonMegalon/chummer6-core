using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    // Read the exact canonical source/serializer without invoking purchase pricing or charging a purchase.
    internal static bool TryReadCareerSaleShape(CharacterCareerGearPurchaseSource authority, XElement installed,
        out CharacterGearCostSnapshot? cost, out string error)
        => TryReadCareerInstalledGearShape(authority, installed, true, out cost, out error);

    internal static bool TryReadCareerRemovalShape(CharacterCareerGearPurchaseSource authority, XElement installed,
        out string error)
    {
        // The caller proves the complete child tree separately. Compare each node's scalar
        // serializer against its own source; never discard an unexamined child from admission.
        var scalar = new XElement(installed);
        scalar.Element("children")?.RemoveNodes();
        return TryReadCareerInstalledGearShape(authority, scalar, false, out _, out error);
    }

    private static bool TryReadCareerInstalledGearShape(CharacterCareerGearPurchaseSource authority, XElement installed,
        bool requirePrice, out CharacterGearCostSnapshot? cost, out string error)
    {
        cost = null;
        error = "gear_sale_saved_shape_unsupported";
        if (installed.Name != "gear" || installed.HasAttributes
            || installed.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))
            || installed.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
            || installed.Elements().Any(e => e.HasAttributes || e.HasElements)
            || !Guid.TryParseExact((string?)installed.Element("guid"), "D", out var id) || id == Guid.Empty
            || !int.TryParse((string?)installed.Element("rating"), NumberStyles.None, CultureInfo.InvariantCulture, out int rating)
            || !decimal.TryParse((string?)installed.Element("qty"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal quantity)
            || !bool.TryParse((string?)installed.Element("discountedcost"), out bool discounted)
            || !TryReadCareerGearSource(authority, out var source, out error)
            || !TryReadCareerGearRatings(source!, out int minimum, out int maximum, out var ratingExpression, out var minimumExpression, out error)
            || rating < minimum || rating > maximum) return false;
        // Remove never evaluates price, including a saved chosen/unsupported price expression.
        // Only the price field is neutralized for the existing source-shape serializer. All
        // non-price source fields, rating, availability and deletion effects retain their checks.
        string? savedPrice = (string?)installed.Element("cost");
        if (!requirePrice)
        {
            if (savedPrice is null || source!.Element("cost") is null) return false;
            source.Element("cost")!.Value = "0";
        }
        if (!TryReadCareerGearDefinition(source!, rating, out var definition, out var expression, out var availability,
            out _, out _, out error)) return false;
        if (!requirePrice) expression = savedPrice!;
        if (definition.Category is "Foci" or "Metamagic Foci" or "Stacked Focus" or "Currency"
            || definition.Name.StartsWith("Nuyen", StringComparison.Ordinal))
        { error = "gear_sale_category_unsupported"; return false; }
        if (!CharacterGearQuantityRules.IsValidAmount(quantity, 1m))
        { error = "gear_sale_quantity_unsupported"; return false; }
        var expected = BuildSavedGear(definition, id.ToString("D"), 1);
        expected.Element("rating")!.Value = rating.ToString(CultureInfo.InvariantCulture);
        expected.Element("minrating")!.Value = minimumExpression;
        expected.Element("maxrating")!.Value = maximum == 0 ? "" : ratingExpression;
        expected.Element("qty")!.Value = quantity.ToString(CultureInfo.InvariantCulture);
        expected.Element("cost")!.Value = expression;
        expected.Element("avail")!.Value = availability;
        expected.Element("discountedcost")!.Value = discounted ? "True" : "False";
        // These saved scalar choices carry no cost or deletion effect. Every other field must match source-backed serialization.
        foreach (string field in new[] { "equipped", "stolen" })
        {
            if (!bool.TryParse((string?)installed.Element(field), out bool flag))
            { error = "gear_sale_saved_boolean_invalid"; return false; }
            expected.Element(field)!.Value = flag ? "True" : "False";
        }
        foreach (string field in new[] { "notes", "notesColor", "gearname", "sortorder", "location" })
        {
            if (installed.Element(field) is not { } scalar) return false;
            if (field == "sortorder" && !int.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
            expected.Element(field)!.Value = scalar.Value;
        }
        // Compare by field identity, preserving original XML order and content in the evidence.
        if (installed.Elements().Count() != expected.Elements().Count()
            || expected.Elements().Any(e => !XNode.DeepEquals(e, installed.Element(e.Name))))
        { error = "gear_sale_saved_source_shape_unsupported"; return false; }
        cost = new(rating, quantity, expression, definition.PackageQuantity, discounted, definition.ChildCostMultiplier, []);
        if (requirePrice && !CharacterGearQuantityRules.TryCalculatePurchaseTotalCost(cost, out _))
        { cost = null; error = "gear_sale_cost_unavailable"; return false; }
        error = "";
        return true;
    }
}
