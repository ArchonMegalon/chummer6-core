using System.Xml.Linq;

using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationFocusKarma1220
{
    internal sealed record InventoryNode1239(string Path, string Kind, string Id, string SourceId,
        string Name, string XmlDigest, bool DerivedStackOwner1253);
    internal sealed record Inventory1239(string GraphDigest, IReadOnlyList<string> Containers,
        IReadOnlyList<InventoryNode1239> Nodes, string? Admission1253);
    internal sealed record Applicability1239(string Field, string LeafDigest, string InventoryDigest,
        IReadOnlyList<string> Targets, string Decision);

    // This is a traversal, not an admission API. Only the original selected-effects
    // compiler can supply a production graph. The retained resolver subsequently
    // admits every source in Nodes before it executes any of the sixteen leaves.
    private static Inventory1239 ReadInventory1239(XElement graph, string? admission)
    {
        string[] names = ["gears", "armors", "weapons", "cyberwares", "vehicles", "foci", "stackedfoci"];
        string[] entries = ["gear", "armor", "weapon", "cyberware", "vehicle", "focus", "stackedfocus"];
        var containers = new List<string>();
        var roots = new List<XElement>();
        for (int index = 0; index < names.Length; index++)
        {
            var container = Container(graph, names[index]);
            if (container is null)
            {
                if (!CharacterCreationFinalizationDigest.IsCanonical(admission)) throw Invalid();
                containers.Add(CharacterCreationFinalizationDigest.Compute(new { Name = names[index],
                    State = "absent-observed-in-original-and-compiled-inventory", Admission = admission }));
                continue;
            }
            if (container.Elements().Any(row => row.Name != entries[index] || row.HasAttributes)) throw Invalid();
            containers.Add(Digest(container));
            roots.Add(container);
        }
        // Do not accept a missing/opaque effects inventory as an empty one either.
        _ = Container(graph, "improvements") ?? throw Invalid();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new List<InventoryNode1239>();
        foreach (var row in graph.Descendants())
        {
            if (row.Ancestors().Take(130).Count() > 128) throw Invalid();
            if (!entries.Contains(row.Name.LocalName, StringComparer.Ordinal)) continue;
            if (row.Name.NamespaceName.Length != 0 || row.HasAttributes
                || !roots.Any(root => row.Ancestors().Contains(root))) throw Invalid();
            // Native item fields can share an inventory entry name. Validate the
            // exact scalar slot; do not skip item-shaped or opaque descendants.
            if (row.Parent is { } owner
                && (row.Name == "cyberware" && owner.Name == "weapon"
                    || row.Name == "armor" && owner.Name == "armor"))
            {
                string value = Scalar(owner, row.Name.LocalName);
                if (row.Name == "cyberware" && !bool.TryParse(value, out _)) throw Invalid();
                // Armor is native source text (which can be an expression).
                continue;
            }
            // A focus link has an independent identity; its owner is accounted as
            // gear. Stacked component gear is included in this same exhaustive set.
            string id = Id(row, "guid");
            if (row.Name.LocalName is "focus" or "stackedfocus") continue;
            if (!ids.Add(id) || ids.Count > 100000) throw Invalid();
            bool derived = row.Name == "gear" && Scalar(row, "category", optional: true) == "Stacked Focus";
            string source = derived ? Scalar(row, "sourceid") : Id(row, "sourceid");
            if (derived && (source != Guid.Empty.ToString("D")
                || !Rows(graph, "stackedfoci", "stackedfocus").Any(s => Id(s, "gearid") == id))) throw Invalid();
            nodes.Add(new(RelativePath1239(graph, row), row.Name.LocalName, id,
                source, Scalar(row, "name"), Digest(row), derived));
        }
        return new(Digest(graph), containers.AsReadOnly(), nodes.AsReadOnly(), admission);
    }

    private static IReadOnlyList<Applicability1239> Decide1239(IReadOnlyList<Price> prices,
        Inventory1239 inventory, IReadOnlyList<Charge> charges)
    {
        string traversed = CharacterCreationFinalizationDigest.Compute(inventory);
        return Array.AsReadOnly(prices.Select(price =>
        {
            var targets = charges.Where(charge => charge.Bonded).SelectMany(charge => charge.Components
                .Where(component => component.Priced && component.Field == price.Field)
                .Select(component => charge.FocusId + "/" + component.GearId)).ToArray();
            return new Applicability1239(price.Field, price.LeafDigest, traversed, Array.AsReadOnly(targets),
                targets.Length == 0 ? "complete-inventory-no-bonded-target" : "priced-bonded-targets");
        }).ToArray());
    }

    internal static string RelativePath1239(XElement root, XElement row)
        => ReferenceEquals(root, row) ? string.Empty : RelativePath1239(root, row.Parent!)
            + "/" + row.Name + "[" + row.Parent!.Nodes().TakeWhile(node => !ReferenceEquals(node, row)).Count() + "]";

    internal static int RelativeOrder1239(XElement root, XElement leaf)
    {
        int order = 0;
        int found = -1;
        Walk(root);
        if (found < 0) throw Invalid();
        return found;
        void Walk(XElement row)
        {
            if (ReferenceEquals(row, leaf)) found = order;
            order = checked(order + 1 + row.Attributes().Count());
            foreach (var node in row.Nodes())
                if (node is XElement child) Walk(child);
                else if (node is XText text && !string.IsNullOrWhiteSpace(text.Value)
                    || node is XProcessingInstruction) order = checked(order + 1);
        }
    }
}
