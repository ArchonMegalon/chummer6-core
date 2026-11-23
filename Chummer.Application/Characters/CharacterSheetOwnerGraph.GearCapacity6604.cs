using System.Globalization;
using System.Xml.Linq;
using System.Xml.XPath;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Calculation evidence, never acquisition authority. Both declarations come
    // from the exact reconciled owner/source, with live whole-graph fences.
    internal sealed record StandaloneGearCapacity6604(Guid InstanceId, Guid SourceId,
        int Rating, decimal Quantity, decimal Remaining, decimal PluginCapacity,
        decimal ArmorCapacity, decimal ArmorPluginCapacity);

    internal bool TryEvaluateStandaloneGearCapacity6604(XElement calculation, XElement node,
        out StandaloneGearCapacity6604? capacity)
    {
        capacity = null;
        if (node.Name != "gear" || node.Parent?.Name != "gears"
            || !ReferenceEquals(node.Parent.Parent, calculation)
            || !MatchesAmmunitionGraph5701(calculation)) return false;
        try
        {
            var matches = owners.Where(owner => owner.Kind == "Gear"
                && owner.Id == Id(node, "guid")).ToArray();
            if (matches is not [var owner]
                || !ReferenceEquals(owner.Saved.Parent?.Parent, root)
                || CharacterCreationOwnedGearGraph1233.GraphDigest(owner.Saved)
                    != CharacterCreationOwnedGearGraph1233.GraphDigest(node)
                || Id(node, "sourceid") != owner.SourceId
                || Node(owner.Source, "requireparent") is not null
                || Text(node, "parentid", "") is not ("" or "00000000-0000-0000-0000-000000000000")) return false;

            // No child allocations, generated children, attachments or installed
            // owners are inferred from an empty saved collection alone.
            foreach (string field in new[] { "children", "gears", "gear", "addgears", "weapons",
                "addweapon", "mods", "armormods", "accessories", "cyberwares", "biowares" })
                if (!Empty3474(node, field) || !Empty3474(owner.Source, field)) return false;
            decimal quantity = Dec(node, "qty");
            if (quantity <= 0m || quantity > CharacterGearQuantityRules.MaximumQuantity) return false;
            string declared = Text(owner.Source, "capacity", "");
            string armorDeclared = Text(owner.Source, "armorcapacity", "");
            if (Text(node, "capacity") != declared || Text(node, "armorcapacity") != armorDeclared
                || !TryStandaloneCapacityDeclaration6604(declared, owner.Rating, out decimal remaining, out decimal plugin)
                || !TryStandaloneCapacityDeclaration6604(armorDeclared, owner.Rating, out decimal armor, out decimal armorPlugin, armorDeclaration: true)
                || !MatchesAmmunitionGraph5701(calculation)) return false;

            // Gear.CapacityRemaining is per item, not multiplied by the stack
            // quantity. Bracketed costs apply to an installed parent only; this
            // proven standalone owner has none. Preserve those costs as evidence.
            capacity = new(owner.Id, owner.SourceId, owner.Rating, quantity,
                remaining, plugin, armor, armorPlugin);
            return true;
        }
        catch (Exception error) when (error is InvalidDataException or FormatException
            or OverflowException or DivideByZeroException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    private static bool TryStandaloneCapacityDeclaration6604(string declaration, int rating,
        out decimal remaining, out decimal plugin, bool armorDeclaration = false)
    {
        remaining = plugin = 0m;
        if (declaration.Length == 0) return true; // Gear's explicit absent-capacity branch.
        // Legacy classifies brackets on the raw declaration. Whitespace outside
        // brackets must not turn an unsupported spelling into a plugin cost.
        string expression = declaration;
        bool consumes = expression.StartsWith('[') && expression.EndsWith(']');
        if (consumes) expression = expression[1..^1];
        // Single scalar reservoirs/costs use the existing bounded Core Rating
        // expression evaluator. Dual capacities, wildcards and FixedValues can
        // change the reservoir/cost shape and need their own supported branch.
        if (expression.Length == 0 || expression.Contains('[') || expression.Contains(']')
            || expression.Contains("FixedValues", StringComparison.Ordinal)) return false;
        decimal value = Expression(expression, rating);
        // The bounded evaluator accepts unary '+', while legacy strips only
        // leading '+' before using XPath syntax. Preserve the native numeric
        // branch (including its whitespace), and only compile expression syntax.
        if (expression.Contains('+') || !decimal.TryParse(expression.Trim(), NumberStyles.Number,
            CultureInfo.InvariantCulture, out _))
        {
            string ratingValue = rating.ToString(CultureInfo.InvariantCulture);
            try
            {
                XPathExpression.Compile(expression.Replace("{Rating}", ratingValue, StringComparison.Ordinal)
                    .Replace("Rating", ratingValue, StringComparison.Ordinal)
                    .Replace("/", " div ", StringComparison.Ordinal).TrimStart('+'));
            }
            catch (XPathException) { return false; }
        }
        if (value < 0m) return false;
        // Match Gear.GetCalculatedCapacity's decimal formatting before the
        // remaining/plugin value is read back. Armor's plain numeric branch
        // retains its decimal precision; bracketed and evaluated armor values
        // use the same two-decimal formatting as ordinary gear capacity.
        if (!armorDeclaration || consumes || !decimal.TryParse(expression, NumberStyles.Number,
            CultureInfo.InvariantCulture, out _))
            value = decimal.Parse(value.ToString("#,0.##", CultureInfo.InvariantCulture),
                NumberStyles.Number, CultureInfo.InvariantCulture);
        if (consumes) plugin = value;
        else remaining = value;
        return true;
    }
}
