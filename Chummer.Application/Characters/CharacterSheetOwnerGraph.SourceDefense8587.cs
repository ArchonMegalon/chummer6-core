using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private readonly List<(Owner Owner, int Value)> sourceDefense8587 = [];
    private readonly HashSet<Guid> sourceDefenseOwners8587 = [];

    private IEnumerable<(Owner Owner, int Value)> ActiveSourceDefense8587() =>
        sourceDefense8587.Where(e => e.Owner.Enabled && !suppressedOwners.Contains(e.Owner.Id));

    internal int SourceDefensePenalty8587 => ActiveSourceDefense8587().Sum(e => e.Value);

    private void CompileSourceDefense8587(Owner owner, XElement container, XElement node, bool enabled, string suffix)
    {
        string path = owner.Path + "/" + container.Name.LocalName + "/defensetest";
        Need(node.Name == "defensetest" && !node.HasElements && !node.HasAttributes
            && node.Nodes().All(n => n is XText or XComment)
            && (container.Attribute("unique")?.Value ?? "").Length == 0,
            "defense-test-shape", path);

        // Bounded portable interpretation: an unconditional, literal integral
        // penalty in a quality's ordinary source bonus (stock Cyclopean Eye: -1).
        // Expressions, positive bonuses, selection/unique, first-level, wireless
        // and non-quality semantics retain the existing unavailable behavior.
        if (owner.Kind != "Quality" || suffix.Length != 0 || container.Name != "bonus"
            || container.HasAttributes || owner.Extra.Length != 0
            || !int.TryParse(node.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            || value >= 0 || node.Value != value.ToString(CultureInfo.InvariantCulture))
        {
            if (enabled) unresolvedDefense.Add((owner, Expression(node.Value, owner.Rating)));
            return;
        }

        // ResolveSource already binds the current receipt and registers its final
        // recheck. Consume that exact source tree and its retained saved bonus;
        // neither saved labels, an arbitrary XML node nor an improvement is authority.
        Need(owner.Source.Name == "quality" && Id(owner.Source, "id") == owner.SourceId
            && Id(owner.Saved, "sourceid") == owner.SourceId && Id(owner.Saved, "guid") == owner.Id
            && ReferenceEquals(container, Node(owner.Source, "bonus"))
            && ReferenceEquals(node.Parent, container) && container.Elements("defensetest").Count() == 1,
            "defense-test-source-owner-mismatch", path);
        var retained = Node(owner.Saved, "bonus");
        Need(retained is not null && XNode.DeepEquals(Parse(retained.ToString(SaveOptions.DisableFormatting)),
            Parse(container.ToString(SaveOptions.DisableFormatting))), "defense-test-retained-source-mismatch", path);
        Need(sourceDefenseOwners8587.Add(owner.Id), "duplicate-source-defense-owner", path);
        if (enabled) sourceDefense8587.Add((owner, value));
    }

    private void CompileFinalizedSourceDefense8587(Owner owner)
    {
        // The finalized module planner owns all legacy rows, including the
        // distinct Combat Active and Gunnery modifiers. Do not dispatch them again.
        // Inspect only retained defense nodes; unsupported placement cannot vanish
        // behind that planner's ordinary-effect short circuit.
        foreach (var node in owner.Source.Descendants("defensetest"))
        {
            var bonus = Node(owner.Source, "bonus");
            Need(owner.Kind == "Quality" && bonus is not null && ReferenceEquals(node.Parent, bonus),
                "finalized-defense-test-shape-unconsumed", owner.Path);
            CompileSourceDefense8587(owner, bonus!, node, owner.Enabled, "");
        }
    }
}
