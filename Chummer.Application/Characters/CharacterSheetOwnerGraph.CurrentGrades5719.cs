using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal CharacterCareerInitiationProjection.CurrentGrades5719? RetainedGrades5719 { get; private set; }

    private void ReadImportedGrades5719(CharacterCreationPriorityGrantAuthority3563? priority, XElement settings)
    {
        if (priority?.IsImported5693 != true) return;
        Need(Node(root, "arts")?.HasElements != true, "grade-art-source-authority-unavailable", "arts");
        bool selected = Int(root, "initiategrade", 0) != 0 || Int(root, "submersiongrade", 0) != 0
            || Node(root, "initiationgrades")?.HasElements == true || Node(root, "metamagics")?.HasElements == true
            || (Node(root, "improvements")?.Elements() ?? []).Any(x => Text(x, "improvementsource", "") is "Initiation" or "Submersion");
        if (!selected) return;
        var proof = CharacterCareerInitiationProjection.ReadCurrentGrades5719(root, settings, context);
        RetainedGrades5719 = proof;
        string digest = CharacterCreationFinalizationDigest.Compute(proof);
        sourceEvidence.Add(digest);
        // The entire saved history, including metadata, stays in calculation evidence.
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(new XElement("grade-history",
            new XElement(Node(root, "initiationgrades")!), new XElement(Node(root, "metamagics") ?? new XElement("metamagics")))
            .ToString(SaveOptions.DisableFormatting)));
        checks.Add(() => CharacterCreationFinalizationDigest.Compute(
            CharacterCareerInitiationProjection.ReadCurrentGrades5719(root, settings, context)) == digest);
        expected.AddRange(proof.Caps.Select(Parse));
        foreach (var row in proof.Owners)
        {
            var saved = Node(root, "metamagics")!.Elements().Single(x => Id(x, "guid") == row.Id);
            Need(instances.Add(row.Id) && owners.Count < 4096, "duplicate-owner-instance", "metamagics");
            owners.Add(new Owner(proof.Resonance ? "Echo" : "Metamagic", row.Id, row.SourceId,
                saved, Parse(row.SourceXml), proof.Grade, "", true, "metamagics/" + row.Id.ToString("D")));
        }
    }

    private bool CompileImportedGradeOwner5719(Owner owner)
    {
        if (owner.Kind is not ("Metamagic" or "Echo") || RetainedGrades5719 is null) return false;
        var proof = RetainedGrades5719.Owners.Single(x => x.Id == owner.Id);
        expected.AddRange(proof.Effects.Select(Parse));
        return true;
    }
}
