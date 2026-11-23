using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationAwakenedLegacyProjector
{
    // Native physical initiative reads REA/INT, wounds and its three initiative
    // channels. Enabling MAG/RES/DEP, learned skills, skill-roll modifiers, spell
    // restrictions, resistance pools and plain granted gear do not change them.
    // Only successful canonical TryApply/TryApplySelected executions can record
    // these exact occurrences. This is private, in-memory provenance, not a new
    // serialized receipt or authority for Complete/persistence.
    private sealed record PhysicalTalentRow1279(int Index, XElement Node, XElement Snapshot);
    private sealed record PhysicalTalent1279(XElement Root, PhysicalTalentRow1279[] Rows, XElement[] Dependencies);

    private static XElement[] PhysicalDependencies1279(XElement root)
        => root.Elements().Where(node => node.Name.LocalName is
            "attributes" or "newskills" or "qualities" or "gears"
            or "magenabled" or "resenabled" or "depenabled"
            or "magician" or "adept" or "technomancer").ToArray();

    private static XElement[] BeginPhysicalTalent1279(XElement root)
    {
        root.RemoveAnnotations<PhysicalTalent1279>();
        return root.Element("improvements")?.Elements().ToArray() ?? [];
    }

    private static bool CompletePhysicalTalent1279(XElement root, XElement[] before, bool success)
    {
        if (!success) return false;
        var rows = (root.Element("improvements")?.Elements().ToArray() ?? [])
            .Select((row, index) => new PhysicalTalentRow1279(index, row, new XElement(row)))
            .Where(row => !before.Contains(row.Node) && IsPhysicalIndependentTalent1279(row.Node)).ToArray();
        if (rows.Length != 0)
            root.AddAnnotation(new PhysicalTalent1279(root, rows,
                PhysicalDependencies1279(root).Select(node => new XElement(node)).ToArray()));
        return true;
    }

    private static bool MatchesPhysicalTalent1279(XElement root, PhysicalTalent1279 execution)
    {
        if (!ReferenceEquals(root, execution.Root)) return false;
        var rows = root.Elements("improvements").SingleOrDefault()?.Elements().ToArray() ?? [];
        var dependencies = PhysicalDependencies1279(root);
        return dependencies.Length == execution.Dependencies.Length
            && dependencies.Select((node, index) => XNode.DeepEquals(node, execution.Dependencies[index])).All(same => same)
            && execution.Rows.All(row => row.Index < rows.Length && ReferenceEquals(rows[row.Index], row.Node)
                && XNode.DeepEquals(row.Node, row.Snapshot));
    }

    internal static bool TryReadPhysicalTalent1279(XElement root, out IReadOnlyList<XElement> rows)
    {
        rows = [];
        if (root.Annotation<PhysicalTalent1279>() is not { } execution) return true;
        if (!MatchesPhysicalTalent1279(root, execution)) return false;
        rows = execution.Rows.Select(row => row.Node).ToArray();
        return true;
    }

    // Used only by the existing private selected-effects consumer's two copy
    // boundaries. XML round-trips and ordinary XElement copies deliberately
    // lose this provenance. The owning consumer's authority/request binding is
    // unchanged; cold and selected replays must execute the producer afresh.
    internal static XElement CopyPhysicalTalentGraph1279(XElement root)
    {
        var copy = new XElement(root);
        if (root.Annotation<PhysicalTalent1279>() is { } execution)
        {
            if (!MatchesPhysicalTalent1279(root, execution))
                throw new InvalidDataException(CharacterCreationPhysicalInitiative886.Blocker);
            var rows = copy.Element("improvements")!.Elements().ToArray();
            copy.AddAnnotation(new PhysicalTalent1279(copy, execution.Rows.Select(row =>
                new PhysicalTalentRow1279(row.Index, rows[row.Index], new XElement(row.Snapshot))).ToArray(),
                execution.Dependencies.Select(node => new XElement(node)).ToArray()));
        }
        return copy;
    }

    // Compare the complete emitted shape, not just the improvement type. The
    // successful producer already validated the source, grant plan, skill
    // projections, enabled attributes and gear source/parent. The snapshots
    // above bind those dependencies and reject replacement/duplicate/mutated
    // rows. Future compiler operations are unresolved until explicitly modeled.
    private static bool IsPhysicalIndependentTalent1279(XElement row)
    {
        string type = row.Element("improvementttype")?.Value ?? "";
        string name = row.Element("improvedname")?.Value ?? "";
        string source = row.Element("sourcename")?.Value ?? "";
        if (!int.TryParse(row.Element("val")?.Value, NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out int value)) return false;
        XElement expected;
        switch (type)
        {
            case "Attribute" when name is "MAG" or "RES" or "DEP":
                expected = Improvement(type, name, source, "Quality", 0, "enableattribute", 0);
                break;
            case "SpecialTab" when name is "Magician" or "Adept" or "Technomancer":
                expected = Improvement(type, name, source, "Quality", 0, "enabletab", 0);
                break;
            case "SkillBase" or "SkillGroupBase" when value >= 0 && name.Length > 0:
                expected = Improvement(type, name, "", "Heritage", value);
                break;
            case "Skill" when name.Length > 0:
                expected = Improvement(type, name, source, "Quality", value);
                expected.Element("condition")!.Value = row.Element("condition")?.Value ?? "";
                string addToRating = row.Element("addtorating")?.Value ?? "";
                if (addToRating is not ("0" or "1")) return false;
                expected.Element("addtorating")!.Value = addToRating;
                break;
            case "Gear" or "SpecialSkills" or "LimitSpellDescriptor" or "BlockSpellDescriptor"
                or "LimitSpellCategory" or "LimitSpiritCategory" when name.Length > 0:
                expected = Improvement(type, name, source, "Quality");
                break;
            case "PathogenContactResist" or "PathogenIngestionResist" or "PathogenInhalationResist"
                or "PathogenInjectionResist" or "ToxinContactResist" or "ToxinIngestionResist"
                or "ToxinInhalationResist" or "ToxinInjectionResist" when value >= 0:
                expected = Improvement(type, "", source, "Quality", value);
                break;
            default:
                return false;
        }
        return XNode.DeepEquals(row, expected);
    }
}
