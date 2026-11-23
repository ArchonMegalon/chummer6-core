using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    private CharacterSpiritFetteringBasis[] spirits5724 = [];

    private void ReadNativeSpirits5724()
    {
        var container = Node(root, "spirits");
        if (container is null) return;
        Need(!container.HasAttributes && container.Nodes().All(n => n is XElement
            || n is XText text && string.IsNullOrWhiteSpace(text.Value)), "spirit-container-shape", "spirits");
        if (!container.HasElements) return;
        Need(Flag(root, "created", false), "spirit-career-proof-required", "spirits");
        var traditions = owners.Where(o => o.Kind is "Tradition" or "Stream").ToArray();
        Need(traditions is [_], "spirit-tradition-owner-unavailable", "spirits");
        var tradition = traditions[0];
        string kind = tradition.Kind == "Stream" ? "Sprite" : "Spirit";
        string sourceId = tradition.SourceId.ToString("D");
        Need(context.TryResolveTraditionSpiritNames(kind, sourceId, out var names),
            "spirit-source-selector-unavailable", "spirits");
        Need(context.TryResolveSpiritCatalogNames(kind, out var catalog)
            && names.Count != 0 && catalog.Count != 0
            && catalog.Distinct(StringComparer.Ordinal).Count() == catalog.Count,
            "spirit-source-selector-unavailable", "spirits");
        // Legacy Spirit.Save persists name as the selector, not a source GUID. Bind that
        // exact selector to the selected tradition AND the unambiguous amended catalog.
        // A foreign/new sourceid representation needs its own resolver; never guess it.
        string evidence = CharacterCreationFinalizationDigest.Compute(new { kind, sourceId, Names = names, Catalog = catalog });
        sourceEvidence.Add(evidence);
        checks.Add(() => context.TryResolveTraditionSpiritNames(kind, sourceId, out var finalNames)
            && context.TryResolveSpiritCatalogNames(kind, out var finalCatalog)
            && CharacterCreationFinalizationDigest.Compute(new { kind, sourceId, Names = finalNames, Catalog = finalCatalog }) == evidence);
        var rows = container.Elements().ToArray();
        var ids = rows.Select(row => Id(row, "guid")).ToArray();
        var selected = new List<CharacterSpiritFetteringBasis>();
        foreach (var saved in rows)
        {
            const string path = "spirits/spirit";
            Need(saved.Name == "spirit" && !saved.HasAttributes && Node(saved, "sourceid") is null
                && saved.Elements().GroupBy(n => n.Name).All(g => g.Count() == 1), "spirit-instance-shape-or-sourceid-unsupported", path);
            Guid id = Id(saved, "guid");
            Need(instances.Add(id) && Text(saved, "type") == kind, "spirit-instance-or-type-mismatch", path);
            Need(!saved.Elements().Any(n => n.Name.LocalName is "bonus" or "wirelessbonus" or "improvements")
                && !Flag(saved, "free", false), "spirit-independent-mechanic-unsupported", path);
            string name = Text(saved, "name");
            Need(catalog.Count(n => n == name) == 1
                && CharacterSpiritNameChoiceRules.TryProject(id, ids, kind, name, names, [],
                    Flag(root, "magenabled", false), Flag(root, "resenabled", false), [], [], out var choice)
                && choice is not null && CharacterSpiritNameChoiceRules.CanSet(choice, name),
                "spirit-selected-source-mismatch", path);
            int force = Int(saved, "force", 1), services = Int(saved, "services", 0);
            Need(force >= 0 && services >= 0, "spirit-force-or-services-invalid", path);
            selected.Add(new(id, kind, force, services, Flag(saved, "bound", true), Flag(saved, "fettered", false)));
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(new { id, kind, sourceId, name,
                Saved = saved.ToString(SaveOptions.DisableFormatting), Source = evidence }));
        }
        spirits5724 = selected.ToArray();
    }

    private void CompileSpiritFettering5724(XElement settings)
    {
        if (spirits5724.Length == 0) return; // Saved orphan effects still fail exact reconciliation.
        bool allowSprite = expected.Any(e => Text(e, "improvementttype") == "AllowSpriteFettering" && Active(e));
        int count = spirits5724.Count(s => s.EntityType == "Spirit" && s.Fettered);
        var costs = Node(settings, "karmacost");
        Need(costs is not null, "spirit-profile-cost-unavailable", "settings");
        foreach (var spirit in spirits5724)
            Need(CharacterSpiritFetteringRules.TryProject(spirit.SpiritId, true, Int(root, "karma"),
                Int(costs!, "karmaspiritfettering"), allowSprite, count, spirits5724, out _),
                "spirit-fettering-state-unsupported", "spirits");
        // Reuse the delivered Core writer; normal owner-effect reconciliation proves
        // the exact MAG adjustment. No expense or past operation is synthesized.
        for (int i = 0; i < count; i++) expected.Add(CharacterCareerSpiritFetteringTransaction.CreateSpiritFetteringImprovement());
    }
}
