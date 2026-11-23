using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationAwakenedLegacyProjector
{
    // Only the exact retained Essence invocation/result may cross this boundary.
    // Preserve the producer's actual improvement instances; XML equality alone
    // must never manufacture physical-talent provenance for a copied graph.
    internal static bool ApplyEssenceGraph1291(XElement root, XElement projected,
        CharacterCreationEssenceInvocation1222 invocation, CharacterCreationEssenceExecution1222 result,
        CharacterCreationRuleSourceResolution resolution)
    {
        if (!result.Result.Valid || projected.Name != root.Name
            || !resolution.HasExecutedEssence1222(invocation, result)
            || root.ToString(SaveOptions.DisableFormatting) != invocation.GraphXml
            || projected.ToString(SaveOptions.DisableFormatting) != result.GraphXml) return false;

        var execution = root.Annotation<PhysicalTalent1279>();
        XElement[] replacements = [];
        if (execution is not null)
        {
            if (!MatchesPhysicalTalent1279(root, execution)) return false;
            // Essence may update only special-attribute totals among physical
            // producer dependencies. All other dependency bytes must agree.
            var expected = new XElement(root);
            foreach (var loss in result.Result.Attributes)
            {
                if (loss.Id is not ("MAG" or "MAGAdept" or "RES" or "DEP")) return false;
                var attributes = expected.Element("attributes")?.Elements("attribute")
                    .Where(row => row.Element("name")?.Value == loss.Id).ToArray();
                if (attributes is not [var attribute]) return false;
                attribute.SetElementValue("totalvalue", loss.Current.ToString(CultureInfo.InvariantCulture));
            }
            var expectedDependencies = PhysicalDependencies1279(expected);
            var actualDependencies = PhysicalDependencies1279(projected);
            if (expectedDependencies.Length != actualDependencies.Length
                || !expectedDependencies.Select((node, index) =>
                    XNode.DeepEquals(node, actualDependencies[index])).All(same => same)) return false;
            replacements = projected.Elements("improvements").SingleOrDefault()?.Elements().ToArray() ?? [];
            if (execution.Rows.Any(row => row.Index >= replacements.Length
                || !XNode.DeepEquals(row.Node, replacements[row.Index]))) return false;
        }

        // All checks precede mutation. Removing first prevents LINQ to XML from
        // cloning a parented node during replacement and losing exact custody.
        if (execution is not null)
            foreach (var row in execution.Rows)
            {
                row.Node.Remove();
                replacements[row.Index].ReplaceWith(row.Node);
            }
        root.ReplaceAttributes(projected.Attributes());
        var nodes = projected.Nodes().ToArray();
        foreach (var node in nodes) node.Remove();
        root.ReplaceNodes(nodes);
        if (execution is not null)
        {
            root.RemoveAnnotations<PhysicalTalent1279>();
            root.AddAnnotation(new PhysicalTalent1279(root, execution.Rows,
                PhysicalDependencies1279(root).Select(node => new XElement(node)).ToArray()));
        }
        return root.ToString(SaveOptions.DisableFormatting) == result.GraphXml
            && (execution is null || MatchesPhysicalTalent1279(root, root.Annotation<PhysicalTalent1279>()!));
    }
}
