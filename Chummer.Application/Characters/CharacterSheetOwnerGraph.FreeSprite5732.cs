using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Category and source-owned Denial grant are current facts; old receipts never
    // authorize their mechanics. Imported and freshly converted graphs use this same proof.
    private void BindFreeSpriteDenial5732(XElement metatype, HashSet<Guid> granted)
    {
        string savedCategory = Text(root, "metatypecategory", "");
        if (savedCategory != CharacterFreeSpriteConversionRules.FreeSpriteCategory) return;
        string sourceCategory = Text(metatype, "category", "");
        Need(Flag(root, "created", false) && sourceCategory.EndsWith("Sprites", StringComparison.Ordinal),
            "free-sprite-category-source-mismatch", "metatypecategory");
        var matches = owners.Where(o => o.Kind == "CritterPower"
            && o.SourceId == CharacterFreeSpriteConversionRules.DenialSourceId).ToArray();
        Need(matches.Length == 1, "free-sprite-denial-grant-missing-or-ambiguous", "critterpowers");
        var denial = matches[0];
        Need(!granted.Contains(denial.Id) && denial.Rating == 0 && denial.Extra.Length == 0
            && !Flag(denial.Saved, "counttowardslimit", true), "free-sprite-denial-grant-invalid", denial.Path);
        if (Sprite5751 is not null)
            Need(Int(denial.Saved, "grade", 0) == 0 && Dec(denial.Saved, "points", 0) == 0,
                "free-sprite-denial-native-state", denial.Path);
        granted.Add(denial.Id);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(new { sourceCategory, savedCategory, denial.Id, denial.SourceId }));
        // Conversion does not create a Metatype grant improvement. The normal owner
        // compiler below consumes the selected Denial bonus and reconciles saved effects.
    }

    internal static void CreateFreeSpriteDenial5732(XElement root, ICharacterSourceDataContext context, Guid instance,
        out XElement saved, out XElement[] effects, out CharacterSheetOwnerSource source)
    {
        CharacterSheetOwnerSource? selected = null;
        Need(context is ICharacterSheetOwnerSourceContext sources
            && sources.TryResolveSheetOwnerSource("CritterPower", CharacterFreeSpriteConversionRules.DenialSourceId, out selected)
            && selected is not null, "free-sprite-denial-source-unavailable", "critterpowers");
        source = selected!;
        Need(source.Kind == "CritterPower" && source.SourceId == CharacterFreeSpriteConversionRules.DenialSourceId
            && new[] { source.CatalogDigest, source.CustomDataDigest, source.ProfileDigest, source.PolicyDigest }
                .All(CharacterCreationFinalizationDigest.IsCanonical), "free-sprite-denial-source-invalid", "critterpowers");
        var definition = Parse(source.SourceXml);
        Need(definition.Name == "power" && Id(definition, "id") == source.SourceId
            && Text(definition, "name") == source.Name && Text(definition, "source") == source.SourceBook,
            "free-sprite-denial-source-identity", "critterpowers");
        saved = new XElement("critterpower", new XElement("sourceid", source.SourceId.ToString("D")),
            new XElement("guid", instance.ToString("D")), new XElement("name", source.Name), new XElement("extra"),
            new XElement("rating", 0), new XElement("category", Text(definition, "category", "")),
            new XElement("type", Text(definition, "type", "")), new XElement("action", Text(definition, "action", "")),
            new XElement("range", Text(definition, "range", "")), new XElement("duration", Text(definition, "duration", "")),
            new XElement("grade", 0), new XElement("source", source.SourceBook), new XElement("page", Text(definition, "page", "")),
            new XElement("karma", 0), new XElement("points", 0), new XElement("counttowardslimit", false),
            Node(definition, "bonus") is { } bonus ? new XElement(bonus) : new XElement("bonus"),
            new XElement("notes"), new XElement("notesColor", CharacterFreeSpriteConversionRules.DenialDefaultNotesColor), new XElement("sortorder", 0));
        var checks = new List<Func<bool>>();
        var graph = new CharacterSheetOwnerGraph(root, context, checks, null);
        if (CharacterSheetDerivedQuery.TryMetatype(root, context, out var metatypeXml, out _, out _)
            && CharacterSpriteSource5751.IsSource(Parse(metatypeXml)))
            graph.ObserveSprite5751(Parse(metatypeXml));
        var owner = new Owner("CritterPower", instance, source.SourceId, saved, definition, 0, "", true, "critterpowers/Denial");
        graph.owners.Add(owner);
        graph.ValidateSavedMechanics(owner);
        graph.ValidateSourceShape(owner);
        graph.CompileContainer(owner, Node(definition, "bonus"), true, "");
        Need(graph.unresolvedDefense.Count == 0 && graph.qualityGrants.Count == 0
            && graph.UnlockedSkills.Count == 0 && graph.GrantedTraits.Count == 0,
            "free-sprite-denial-choice-or-dependent-grant-unsupported", owner.Path);
        effects = graph.expected.ToArray();
        // Full proposed-character readmission validates all generated effects again
        // in the original owner's complete graph before this proposal can be saved.
    }
}
