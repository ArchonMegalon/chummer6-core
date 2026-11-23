using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    internal CharacterSpriteSource5751.Observation? Sprite5751 { get; private set; }
    private readonly HashSet<Guid> spriteOptionalPowers5751 = [];
    private readonly HashSet<XElement> spriteOptionalContainers5751 = [];
    private readonly HashSet<string> spriteSkills5751 = new(StringComparer.Ordinal);

    private void ObserveSprite5751(XElement metatype)
    {
        if (!CharacterSpriteSource5751.IsSource(metatype)) return;
        Sprite5751 = CharacterSpriteSource5751.Observe(root, metatype);
        // Creation-time range modifiers, alternate bodies, ware/essence loss and
        // grade history need their own authority; no synthetic Priority talent.
        Need(!Flag(root, "magenabled", false) && !Flag(root, "adept", false) && !Flag(root, "magician", false)
            && !Flag(root, "depenabled", false) && Int(root, "initiategrade", 0) == 0 && Int(root, "submersiongrade", 0) == 0
            && Int(root, "specialattburn", 0) == 0 && Dec(root, "essencehole", 0) == 0
            && Dec(root, "essencepenalty", 0) == 0
            && Node(root, "cyberwares")?.HasElements != true && Node(root, "biowares")?.HasElements != true,
            "sprite-alternate-attribute-authority-unavailable", "attributes");
        sourceEvidence.Add(Sprite5751.Digest);
    }

    private void BindSpriteSkills5751(XElement metatype, Guid parent)
    {
        if (Sprite5751 is null) return;
        var owner = new Owner("Metatype", parent, Id(metatype, "id"), new XElement("metatype"), metatype, 1, "", true, "metatype/skills");
        foreach (var grant in Node(metatype, "skills")?.Elements() ?? [])
        {
            CharacterSheetOwnerSource? selected = null;
            Need(sources is not null && sources.TryResolveSheetNamedSource("Skill", grant.Value, out selected)
                && selected is not null, "sprite-skill-source-unavailable", owner.Path);
            var captured = selected!;
            Need(context.TryResolveActiveSkillSource(captured.SourceId.ToString("D"), out var skill)
                && skill.Name == grant.Value && !skill.IsExotic && spriteSkills5751.Add(skill.Name),
                "sprite-skill-source-unsupported", owner.Path);
            // Skill.WriteTo persists the catalog SkillId in suid; guid is the
            // individual skill instance consumed by the derived sheet.
            var saved = Node(Node(root, "newskills") ?? new XElement("newskills"), "skills")?.Elements()
                .Where(s => Text(s, "suid", "") == captured.SourceId.ToString("D")).ToArray() ?? [];
            Need(saved.Length == 1 && saved[0].Name == "skill" && !Flag(saved[0], "isknowledge", false),
                "sprite-generated-skill-missing-or-duplicate", owner.Path);
            int rating = CharacterSpriteSource5751.Evaluate(grant.Attribute("rating")!.Value, Sprite5751.Level, 0);
            var effect = Improvement(owner, "", true, "SkillLevel", skill.Name, rating, 0, 0, 0, 0, false, "", 1, "", "");
            effect.SetElementValue("sourcename", "");
            effect.SetElementValue("improvementsource", "Metatype");
            expected.Add(effect);
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
            checks.Add(() => sources!.TryResolveSheetNamedSource("Skill", grant.Value, out var final) && final == captured);
        }
    }

    private void BindSpriteOptionalPowers5751(XElement metatype, Guid parent, HashSet<Guid> granted)
    {
        if (Sprite5751 is null) return;
        foreach (var node in Node(metatype, "bonus")?.Elements("optionalpowers") ?? [])
        {
            Need(node.Attributes().All(a => a.Name == "count")
                && int.TryParse(node.Attribute("count")?.Value ?? "1", out int count) && count > 0
                && node.Elements().Any() && node.Elements().All(e => e.Name == "optionalpower" && !e.HasElements
                    && e.Attributes().All(a => a.Name == "select") && e.Value.Length > 0),
                "sprite-optional-power-source-shape", "metatype/bonus/optionalpowers");
            // count is parsed separately: Need throws but is not a C# flow annotation.
            int required = int.Parse(node.Attribute("count")?.Value ?? "1", System.Globalization.CultureInfo.InvariantCulture);
            var selected = owners.Where(o => o.Kind == "CritterPower" && !granted.Contains(o.Id)
                && Int(o.Saved, "grade", 0) == -1 && node.Elements().Any(g => g.Value == Text(o.Source, "name")
                    || Guid.TryParse(g.Value, out var id) && id == o.SourceId)).ToArray();
            Need(selected.Length == required, "sprite-optional-power-selection-count", "critterpowers");
            foreach (var selectedOwner in selected)
            {
                var choices = node.Elements().Where(g => g.Value == Text(selectedOwner.Source, "name")
                    || Guid.TryParse(g.Value, out var id) && id == selectedOwner.SourceId).ToArray();
                Need(choices.Length == 1, "sprite-optional-power-selection-ambiguous", "critterpowers");
                var owner = BindGrant("CritterPower", choices[0], granted, parent, Id(metatype, "id"), 0);
                Need(Int(owner.Saved, "grade", 0) == -1 && Flag(owner.Saved, "counttowardslimit", true)
                    && Dec(owner.Saved, "points", 0) == 0 && spriteOptionalPowers5751.Add(owner.Id),
                    "sprite-optional-power-native-state", owner.Path);
                // AddImprovement.optionalpowers creates Grade=-1, preserves the
                // default CountTowardsLimit=true/points=0, and uses parent GUID.
                var marker = Improvement(owner, "", true, "CritterPower", owner.Id.ToString("D"), 0, 0, 0, 0, 0, false, "", 1, "", "");
                marker.SetElementValue("sourcename", parent.ToString("D")); marker.SetElementValue("improvementsource", "Metatype");
                marker.SetElementValue("unique", Node(metatype, "bonus")?.Attribute("unique")?.Value ?? "");
                expected.Add(marker);
            }
            spriteOptionalContainers5751.Add(node);
        }
    }

    private void CompileSpriteResonance5751(XElement settings)
    {
        var metatype = Sprite5751!.Evaluated;
        var rows = Node(root, "attributes")!.Elements("attribute").Where(a => Text(a, "name") == "RES").ToArray();
        Need(rows.Length == 1, "sprite-resonance-identity", "attributes/RES");
        var row = rows[0];
        int min = Int(metatype, "resmin"), max = Int(metatype, "resmax"), aug = Int(metatype, "resaug");
        var value = Attribute("RES", min, max, aug, Int(row, "base"), Int(row, "karma"), Flag(settings, "unclampattributeminimum", false));
        Need(Int(row, "base") >= 0 && Int(row, "karma") >= 0 && (long)min + Int(row, "base") + Int(row, "karma") <= value.Maximum,
            "sprite-resonance-allocation", "attributes/RES");
        SpecialAttributes = [new("RES", value.Current) { Unaugmented = value.Value,
            AllocatedBase = Int(row, "base"), AllocatedKarma = Int(row, "karma"), AugmentedModifier = value.Current - value.Value }];
        SpecialResults5699 = [new("RES", 0, 0, 0, value.Current, value.Maximum, value.Minimum,
            value.RawMinimum, value.Value, value.AugmentedMaximum, value.Current)];
    }

    internal void ValidateSpriteValues5751(XElement settings)
    {
        if (Sprite5751 is null) return;
        var rows = Node(root, "attributes")!.Elements().ToArray();
        Need(rows.Length is 13 or 14 && rows.Select(row => Text(row, "name")).Distinct().Count() == rows.Length
            && rows.All(row => Text(row, "name") == "MAGAdept" || CharacterSpriteSource5751.Attributes.Contains(Text(row, "name"))), "sprite-attribute-closure", "attributes");
        foreach (var row in rows)
        {
            string name = Text(row, "name"), prefix = name == "MAGAdept" ? "mag" : name.ToLowerInvariant();
            int min = Int(Sprite5751.Evaluated, prefix + "min");
            var value = Attribute(name, min, Int(Sprite5751.Evaluated, prefix + "max"), Int(Sprite5751.Evaluated, prefix + "aug"),
                Int(row, "base"), Int(row, "karma"), Flag(settings, "unclampattributeminimum", false));
            // totalvalue is a native display cache, not a force witness. In
            // particular a newly added Denial bonus may legitimately change it.
            Need(Int(row, "base") >= 0 && Int(row, "karma") >= 0 && (long)min + Int(row, "base") + Int(row, "karma") <= value.Maximum,
                "sprite-attribute-allocation", "attributes/" + name);
        }
        // The admitted powers are source-generated and Denial. Native excludes
        // direct grants from this budget. Optional source grants retain their
        // native counted, zero-point state. Purchased Free Sprite powers remain outside this
        // conversion; accepting their saved point totals would grant authority.
        Need(owners.Where(o => o.Kind == "CritterPower").All(o => !Flag(o.Saved, "counttowardslimit", true)
                || spriteOptionalPowers5751.Contains(o.Id) && Dec(o.Saved, "points", 0) == 0),
            "sprite-paid-power-authority-unavailable", "critterpowers");
    }

    private void AddSpriteValues5751(Dictionary<string, int> values, IReadOnlyDictionary<string, int> attributes)
    {
        if (Sprite5751 is null) return;
        values.Add("SpriteLevel", Sprite5751.Level);
        if (Text(root, "metatypecategory") != CharacterFreeSpriteConversionRules.FreeSpriteCategory) return;
        // Character.CalculateFreeSpritePowerPointsAsync: EDG + rounded source
        // FreeSpiritPowerPoints, less counted points (source-created optional powers have zero).
        int total = checked(attributes["EDG"] + Rounded("FreeSpiritPowerPoints"));
        values.Add("FreeSpritePowerPoints", total);
        values.Add("FreeSpritePowerPointsRemaining", total);
    }
}
