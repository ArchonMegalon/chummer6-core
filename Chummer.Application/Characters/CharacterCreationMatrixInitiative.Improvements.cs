using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationMatrixInitiative
{
    private static bool MatrixType(string type)
    {
        if (type is "MatrixInitiativeDice" or "MatrixInitiativeDiceAdd") return true;
        if (string.Equals(type, "MatrixInitiativeDice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, "MatrixInitiativeDiceAdd", StringComparison.OrdinalIgnoreCase)) throw Invalid();
        return false;
    }

    private static XElement Improvements(XElement graph)
    {
        var container = One(graph, "improvements") ?? throw Invalid();
        if (container.HasAttributes || container.Nodes().Any(node => node is not XElement
                && (node.GetType() != typeof(XText) || !string.IsNullOrWhiteSpace(((XText)node).Value)))
            || container.Elements().Any(row => row.Name != "improvement" || row.HasAttributes)) throw Invalid();
        return container;
    }

    // This exact channel is also executed before physical initiative excludes
    // matrix effects. Unknown effect types still go through its original guards.
    internal static IReadOnlyList<XElement> ConsumedEffects(XElement graph, bool created)
    {
        var rows = Improvements(graph).Elements().Where(row => MatrixType(Scalar(row, "improvementttype"))).ToArray();
        if (rows.Length != 0) _ = MatrixChannels(graph, created, NeedsGearFilter(graph) ? ActiveCommlink(graph) : null);
        return rows;
    }

    private static bool NeedsGearFilter(XElement graph)
        => Improvements(graph).Elements().Any(row => MatrixType(Scalar(row, "improvementttype"))
            && Scalar(row, "improvementsource") == "Gear");

    private static (decimal Dice, decimal Additive) MatrixChannels(XElement graph, bool created, XElement? device)
    {
        var original = Improvements(graph);
        var relevant = original.Elements().Where(row => MatrixType(Scalar(row, "improvementttype"))).ToArray();
        foreach (var row in relevant)
        {
            // Read every operative scalar through duplicate/namespace/shape guards
            // before the shared native enabled/phase/unique-name resolver runs.
            foreach (string field in new[] { "unique", "improvedname", "condition" }) _ = Scalar(row, field, "");
            if (!decimal.TryParse(Scalar(row, "val"), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out _)) throw Invalid();
            _ = Integer(row, "enabled", 1);
            _ = Integer(row, "addtorating", 0);
            _ = Boolean(row, "custom", false);
            if (!NativeSources.Contains(Scalar(row, "improvementsource"))) throw Invalid();
        }
        bool living = device is not null && device.Name == "gear" && Scalar(device, "name") == "Living Persona";
        var eligible = relevant.Where(row => !living || Scalar(row, "improvementsource") != "Gear");
        var input = new XElement("character", new XElement("improvements", eligible.Select(row => new XElement(row))));
        // ImprovementManager.ValueOf also filters Gear for an active Living Persona.
        // The shared resolver implements custom partitions, precedence groups,
        // enabled/AddToRating and create/career conditions, then sums by name.
        return (CharacterCareerReputationProjector.ResolvePhysicalChannel886(input, "MatrixInitiativeDice", created),
            CharacterCareerReputationProjector.ResolvePhysicalChannel886(input, "MatrixInitiativeDiceAdd", created));
    }

    private static XElement? ActiveCommlink(XElement graph)
    {
        XElement? active = null;
        foreach (var device in graph.Descendants().Where(node => node.Name.LocalName is "gear" or "vehicle"))
        {
            if (device.Name.Namespace != XNamespace.None || device.HasAttributes) throw Invalid();
            var top = device.AncestorsAndSelf().First(node => ReferenceEquals(node.Parent, graph));
            if (top.Name.LocalName is not ("gears" or "vehicles" or "armors" or "weapons" or "cyberwares")
                || top.Name.Namespace != XNamespace.None) throw Invalid();
            // Gear/Vehicle loaders initialize active to false if the field is absent.
            if (!Boolean(device, "active", false)) continue;
            // SetActiveCommlink also requires IsCommlink. Active vehicle matrix
            // attribute expressions need their own evaluator; never guess here.
            if (device.Name != "gear") throw Invalid();
            bool commlink = Scalar(device, "canformpersona").Contains("Self", StringComparison.Ordinal);
            if (!commlink && One(device, "children") is { } children)
            {
                if (children.HasAttributes || children.Elements().Any(child => child.Name != "gear")) throw Invalid();
                commlink = children.Elements().Any(child => Scalar(child, "canformpersona").Contains("Parent", StringComparison.Ordinal));
            }
            if (!commlink) continue;
            if (active is not null) throw Invalid(); // ambiguous loader-order state
            _ = Scalar(device, "name");
            active = device;
        }
        return active;
    }

    // Reviewed Improvement.ImprovementSource names. Unknown/future sources cannot
    // silently take the non-Gear branch. This is data parsing, not source admission.
    private static readonly HashSet<string> NativeSources = new(StringComparer.Ordinal)
    {
        "Quality", "Power", "Metatype", "Cyberware", "Metavariant", "Bioware", "ArmorEncumbrance",
        "Gear", "VehicleMod", "Spell", "Initiation", "Submersion", "Metamagic", "Echo", "Armor",
        "ArmorMod", "EssenceLoss", "EssenceLossChargen", "CritterPower", "ComplexForm", "MutantCritter",
        "Cyberzombie", "StackedFocus", "AttributeLoss", "Art", "Enhancement", "Custom", "Heritage",
        "MartialArt", "MartialArtTechnique", "AIProgram", "SpiritFettering", "MentorSpirit", "Drug",
        "Tradition", "Weapon", "WeaponAccessory", "AstralReputation", "CyberadeptDaemon", "BurnedEdge", "Encumbrance"
    };
}
