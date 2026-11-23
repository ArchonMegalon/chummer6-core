using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private readonly Dictionary<Guid, XElement[]> retainedFocusEffects6511 = [];

    private void BindRetainedFocusOwner6511(Owner owner)
    {
        Need(context.TryResolveCareerGearPurchaseSource(owner.SourceId, out var receipt) && receipt is not null
            && CharacterCareerFocusBondingTransaction.TryReadRetainedFocusSource6511(receipt, out var original)
            && XNode.DeepEquals(Parse(original!.ToString(SaveOptions.DisableFormatting)), owner.Source),
            "focus-source-unconsumed", owner.Path);
        Need(CharacterCareerFocusBondingTransaction.TryCompileRetainedFocus6511(root, owner.Saved, owner.Source,
            out var effects), "focus-binding-or-effects-unconsumed", owner.Path);
        retainedFocusEffects6511.Add(owner.Id, effects);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(receipt));
        checks.Add(() => context.TryResolveCareerGearPurchaseSource(owner.SourceId, out var final) && final == receipt);
    }

    private void ReadRetainedFocusLinks6511()
    {
        var links = Node(root, "foci");
        if (links is null) return;
        Need(!links.HasAttributes && links.Nodes().All(n => n is XElement || n is XText t && string.IsNullOrWhiteSpace(t.Value)),
            "focus-links-shape", "foci");
        foreach (var link in links.Elements())
        {
            Need(link.Name == "focus" && !link.HasAttributes && link.Elements().Count() == 2
                && retainedFocusEffects6511.ContainsKey(Id(link, "gearid")) && instances.Add(Id(link, "guid")),
                "focus-link-owner-unconsumed", "foci");
        }
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(links.ToString(SaveOptions.DisableFormatting)));
    }

    private bool CompileRetainedFocusOwner6511(Owner owner)
    {
        if (!retainedFocusEffects6511.TryGetValue(owner.Id, out var effects)) return false;
        expected.AddRange(effects.Select(e => new XElement(e)));
        boundChoices.Add(owner.Id);
        return true;
    }
    // Capacity is read from the exact source-owned focus inventory, not inferred
    // from a category label or made empty by deleting gear from the calculation.
    internal bool IsCapacityFreeRetainedFocus6511(XElement calculation, XElement node)
    {
        if (node.Name != "gear" || node.Parent?.Name != "gears"
            || !ReferenceEquals(node.Parent.Parent, calculation)
            || !Guid.TryParse(node.Element("guid")?.Value, out var id)
            || !retainedFocusEffects6511.ContainsKey(id)
            || new[] { "capacity", "armorcapacity" }.Any(field => node.Element(field) is { } value
                && (value.HasAttributes || value.HasElements || value.Value.Length != 0))) return false;
        return MatchesCurrentStockLoadout5696(calculation)
            && owners.Any(owner => owner.Id == id && owner.Kind == "Gear"
                && CharacterCreationOwnedGearGraph1233.GraphDigest(owner.Saved)
                    == CharacterCreationOwnedGearGraph1233.GraphDigest(node));
    }

    internal bool HasOnlyCapacityFreeRetainedFoci6511(XElement calculation)
    {
        var containers = calculation.Descendants().Where(n => n.Name.LocalName.Equals("gears", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (containers is not [var container] || container.Name != "gears" || container.Parent != calculation
            || container.HasAttributes || !container.HasElements
            || container.Nodes().Any(n => n is not XElement && (n is not XText text || !string.IsNullOrWhiteSpace(text.Value)))) return false;
        var gear = container.Elements().ToArray();
        return calculation.Descendants().Where(n => n.Name.LocalName.Equals("gear", StringComparison.OrdinalIgnoreCase)).SequenceEqual(gear)
            && gear.All(node => IsCapacityFreeRetainedFocus6511(calculation, node))
            && MatchesCurrentStockLoadout5696(calculation);
    }

}
