using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal CharacterSustainedSpellPreparationState? SustainedSpellPreparation5064 { get; private set; }
    internal int SustainingPenalty5064 => SustainedSpellPreparation5064?.DicePoolPenalty ?? 0;

    private void ReadNativeSustaining5064(XElement settings)
    {
        // Compile has already authenticated each complete saved owner against its
        // current source and matched the entire improvement graph. Saved labels,
        // duration text and cached totals cannot grant eligibility or a discount.
        CharacterSustainedSpellOption[] options = Flag(root, "created") && Flag(root, "magenabled", false) && Flag(root, "magician", false)
            && SpecialAttributes.Any(a => a.Name == "MAG" && a.Total > 0)
            ? owners.Where(o => o.Kind == "Spell" && o.Enabled && Text(o.Source, "duration", "") == "S"
                && !Flag(o.Saved, "alchemical", false) && !Flag(o.Saved, "barehandedadept", false)
                && Text(o.Source, "category", "") is not ("Rituals" or "Enchantments"))
                .Select(o => new CharacterSustainedSpellOption(o.Id, o.SourceId, Text(o.Saved, "name").Trim(),
                    Text(o.Source, "source"), Text(o.Source, "page", ""))).ToArray()
            : [];
        var container = Node(root, "sustainedobjects");
        if (container is not null)
            Need(!container.HasAttributes && container.Nodes().All(n => n is XElement e && e.Name == "sustainedobject"
                || n is XText text && string.IsNullOrWhiteSpace(text.Value)), "sustained-container-invalid", "sustainedobjects");
        var rows = container?.Elements().ToArray() ?? [];
        Need(rows.Length <= 1024, "sustained-count-bound", "sustainedobjects");
        var next = new Dictionary<Guid, int>();
        var basis = new List<CharacterSustainedObjectBasis>();
        foreach (var row in rows)
        {
            const string path = "sustainedobjects/sustainedobject";
            string[] fields = ["linkedobject", "linkedobjecttype", "force", "nethits", "self"];
            Need(!row.HasAttributes && row.Elements().Count() == fields.Length
                && row.Elements().All(e => fields.Contains(e.Name.LocalName) && e.Name.Namespace == XNamespace.None)
                && row.Nodes().All(n => n is XElement || n is XText text && string.IsNullOrWhiteSpace(text.Value)),
                "sustained-shape-unconsumed", path);
            Need(Text(row, "linkedobjecttype") == "Spell", "sustained-kind-unconsumed", path);
            Guid id = Id(row, "linkedobject");
            var linked = owners.SingleOrDefault(o => o.Id == id && o.Kind == "Spell");
            Need(linked is not null, "sustained-linked-owner-missing", path);
            int occurrence = next.GetValueOrDefault(id);
            next[id] = occurrence + 1;
            basis.Add(new(new("Spell", id, occurrence), Text(linked!.Saved, "name"),
                Int(row, "force"), Int(row, "nethits"), Flag(row, "self"), true));
        }
        Need(CharacterSustainedObjectRules.TryProjectAll(basis, out var states) && states is not null,
            "sustained-values-invalid", "sustainedobjects");
        if (rows.Length == 0 && options.Length == 0) return; // ordinary runners acquire no magic requirement
        // Never default a missing effective setting to stock SR5 rules.
        int perObject = Int(settings, "dicepenaltysustaining");
        Need(perObject >= 0, "sustained-policy-invalid", "settings/dicepenaltysustaining");
        var penalty = CharacterCreationNativeMagic1430.EvaluateSustainingPenalty(root, perObject);
        SustainedSpellPreparation5064 = new(Array.AsReadOnly(options), Array.AsReadOnly(states!.ToArray()),
            penalty.Count, penalty.Penalty);
        if (rows.Length != 0)
            AuthorityDigest = CharacterCreationFinalizationDigest.Compute(new
            {
                Semantics = "chummer.core.sr5-sustained-registration/v1",
                Owners = AuthorityDigest,
                Saved = container!.ToString(SaveOptions.DisableFormatting),
                PerObject = perObject, Psyche = Flag(root, "psyche", false),
                penalty.Count, penalty.Penalty
            });
    }
}
