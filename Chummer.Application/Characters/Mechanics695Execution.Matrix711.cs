using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class Mechanics695Execution
{
    internal sealed record MatrixAttribute711(string Attribute, string Expression,
        int Base, int Children, int Overclocker, int Total);
    internal sealed record MatrixDevice711(IReadOnlyList<Guid> Path, Guid SourceId, string NodeDigest,
        CharacterGearOverclockerTarget Target, bool Equipped, IReadOnlyList<MatrixAttribute711> Attributes);

    // Gear.GetBaseMatrixAttribute/GetBonusMatrixAttribute semantics, with the
    // existing rating-expression consumer and the saved target parser. Unsupported
    // expression grammars and other matrix-owner kinds remain rejected.
    internal static bool TryMatrix711(XElement graph, IReadOnlyDictionary<string, int> attributes,
        bool overclocker, out MatrixDevice711[] devices)
    {
        devices = [];
        try
        {
            if (graph.Name != "character" || graph.HasAttributes
                || graph.Elements().GroupBy(row => row.Name).Any(group => group.Count() != 1)) return false;
            foreach (string scope in new[] { "weapons", "armors", "cyberwares", "vehicles" })
                if (graph.Element(scope) is { } container
                    && (container.HasAttributes || container.HasElements || !string.IsNullOrWhiteSpace(container.Value)))
                    return false;
            var seen = new HashSet<Guid>();
            var result = new List<MatrixDevice711>();
            foreach (var gear in Rows(graph, "gears", "gear")) Evaluate(gear, []);
            devices = result.ToArray();
            return true;

            MatrixDevice711 Evaluate(XElement node, IReadOnlyList<Guid> parent)
            {
                if (parent.Count >= 16 || seen.Count >= 4096 || node.HasAttributes
                    || node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
                    || node.Elements().GroupBy(row => row.Name).Any(group => group.Count() != 1)
                    || node.Elements().Any(row => row.Name.Namespace != XNamespace.None)
                    || !Guid.TryParseExact(Scalar(node, "guid"), "D", out var id) || id == Guid.Empty || !seen.Add(id)
                    || !Guid.TryParseExact(Scalar(node, "sourceid"), "D", out var source) || source == Guid.Empty
                    || !int.TryParse(Scalar(node, "rating"), NumberStyles.None, CultureInfo.InvariantCulture, out int rating)
                    || !bool.TryParse(Scalar(node, "equipped"), out bool equipped)
                    || !CharacterGearOverclockerRules.TryParseAttribute(Optional711(node, "overclocked"), out var target))
                    throw new InvalidDataException();
                foreach (string bonus in new[] { "bonus", "wirelessbonus", "weaponbonus", "flechetteweaponbonus" })
                    if (node.Element(bonus) is { } container
                        && (container.HasAttributes || container.HasElements || !string.IsNullOrWhiteSpace(container.Value)))
                        throw new InvalidDataException();
                if (node.Element("matrixcmfilled") is not null && Integer(node, "matrixcmfilled") != 0)
                    throw new InvalidDataException();
                var path = parent.Append(id).ToArray();
                if (!CharacterGearOverclockerRules.IsValidIdentity(new(path))) throw new InvalidDataException();
                var childNodes = Rows(node, "children", "gear");
                bool commlink = Optional711(node, "canformpersona").Contains("Self", StringComparison.Ordinal)
                    || childNodes.Any(child => Optional711(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
                var children = childNodes.Select(child => Evaluate(child, path)).ToArray();
                var values = new List<MatrixAttribute711>();
                foreach (var (label, field) in new[] { ("Attack", "attack"), ("Sleaze", "sleaze"),
                    ("Data Processing", "dataprocessing"), ("Firewall", "firewall"),
                    ("Device Rating", "devicerating"), ("Program Limit", "programlimit") })
                {
                    string expression = Optional711(node, field);
                    if (expression.Length == 0)
                    {
                        expression = label switch
                        {
                            "Device Rating" => commlink ? "2" : "0",
                            "Data Processing" or "Firewall" => Optional711(node, "devicerating"),
                            "Program Limit" when commlink => Optional711(node, "devicerating") is { Length: > 0 } device ? device : "2",
                            _ => "0"
                        };
                    }
                    int basis = Resolve(expression, rating);
                    int childBonus = children.Where(child => child.Equipped)
                        .Aggregate(0, (sum, child) => checked(sum + child.Attributes.Single(row => row.Attribute == "Mod " + label).Total));
                    int bonus = overclocker && CharacterGearOverclockerRules.ToSavedValue(target) == label ? 1 : 0;
                    values.Add(new(label, expression, basis, childBonus, bonus, checked(basis + childBonus + bonus)));
                    string modExpression = Optional711(node, "mod" + field);
                    int mod = Resolve(modExpression, rating);
                    int childMod = children.Where(child => child.Equipped)
                        .Aggregate(0, (sum, child) => checked(sum + child.Attributes.Single(row => row.Attribute == "Mod " + label).Total));
                    values.Add(new("Mod " + label, modExpression, mod, childMod, 0, checked(mod + childMod)));
                }
                var deviceResult = new MatrixDevice711(path, source, NodeDigest711(node), target, equipped, values);
                result.Add(deviceResult);
                return deviceResult;
            }
            int Resolve(string expression, int rating)
            {
                if (expression.Contains("FixedValues", StringComparison.Ordinal)
                    || expression.Contains("{Children ", StringComparison.Ordinal)
                    || expression.Contains("{Parent ", StringComparison.Ordinal)
                    || expression.Contains("{Gear ", StringComparison.Ordinal)
                    || !CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(expression, rating, attributes, out int value))
                    throw new InvalidDataException();
                return value;
            }
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException
            or OverflowException or FormatException) { return false; }
    }
}
