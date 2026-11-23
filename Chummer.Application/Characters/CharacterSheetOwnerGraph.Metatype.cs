using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal sealed partial class CharacterSheetOwnerGraph
{
    // Character.Create resolves qualities by canonical name/id and saves their origin;
    // metatype powers are CritterPower owners, not paid adept Power instances.
    private void BindMetatypeGrants(XElement metatype, Guid parentId)
    {
        Guid id = Id(metatype, "id");
        Need(parentId != Guid.Empty, "metatype-parent-identity", "metatype");
        foreach (string field in Sprite5751 is null ? new[] { "naturalweapons", "skills" } : new[] { "naturalweapons" })
            Need(Node(metatype, field)?.HasElements != true, "metatype-generated-mechanic-unconsumed", "metatype/" + field);
        Need(Node(metatype, "forcecreature") is null || Sprite5751 is not null, "metatype-force-scope-unconsumed", "metatype/forcecreature");
        Need(Text(metatype, "category", "") != "Shapeshifter", "metatype-dual-form-attributes-unconsumed", "metatype/category");
        var granted = new HashSet<Guid>();
        var qualities = Node(metatype, "qualities");
        if (qualities is not null)
        {
            Need(!qualities.HasAttributes && qualities.Elements().All(e => e.Name is var n && (n == "positive" || n == "negative")),
                "metatype-quality-container", "metatype/qualities");
            foreach (var category in qualities.Elements())
            {
                Need(!category.HasAttributes, "metatype-quality-category", "metatype/qualities");
                foreach (var grant in category.Elements())
                {
                    Need(grant.Name == "quality" && !grant.HasElements && grant.Attributes().All(a => a.Name == "select" || a.Name == "removable"),
                        "metatype-quality-grant-shape", "metatype/qualities");
                    bool removable = false;
                    if (grant.Attribute("removable") is { } r)
                        Need(bool.TryParse(r.Value, out removable), "metatype-removable-shape", "metatype/qualities");
                    var owner = BindGrant("Quality", grant, granted, parentId, id,
                        qualityOrigin: removable ? "MetatypeRemovable" : "Metatype");
                    Need(Text(owner.Source, "category") == (category.Name == "positive" ? "Positive" : "Negative")
                        && Text(owner.Saved, "qualitysource", "") == (removable ? "MetatypeRemovable" : "Metatype"),
                        "metatype-quality-origin", owner.Path);
                }
            }
        }
        var powers = Node(metatype, "powers");
        if (powers is not null)
        {
            Need(!powers.HasAttributes, "metatype-power-container", "metatype/powers");
            foreach (var grant in powers.Elements())
            {
                Need(grant.Name == "power" && !grant.HasElements && grant.Attributes().All(a => a.Name == "select" || a.Name == "rating"),
                    "metatype-power-grant-shape", "metatype/powers");
                int rating = Sprite5751 is not null && !string.IsNullOrWhiteSpace(grant.Attribute("rating")?.Value)
                    ? CharacterSpriteSource5751.Evaluate(grant.Attribute("rating")!.Value, Sprite5751.Level, 0) : GrantRating(grant);
                var owner = BindGrant("CritterPower", grant, granted, parentId, id, rating);
                Need(rating == owner.Rating && !Flag(owner.Saved, "counttowardslimit", true),
                    "metatype-power-rating-or-limit-unconsumed", owner.Path);
                if (Sprite5751 is not null)
                    Need(Int(owner.Saved, "grade", 0) == 0 && Dec(owner.Saved, "points", 0) == 0,
                        "sprite-generated-power-native-state", owner.Path);
                var marker = Improvement(owner, "", true, "CritterPower", owner.Id.ToString("D"), 0, 0, 0, 0, 0, false, "", 1, "", "");
                marker.SetElementValue("sourcename", "");
                marker.SetElementValue("improvementsource", "Metatype");
                expected.Add(marker);
            }
        }
        BindSpriteSkills5751(metatype, parentId);
        BindSpriteOptionalPowers5751(metatype, parentId, granted);
        BindFreeSpriteDenial5732(metatype, granted);
        foreach (var owner in owners)
        {
            bool claimsGrant = owner.Kind == "CritterPower" || owner.Kind == "Quality"
                && Text(owner.Saved, "qualitysource", "").StartsWith("Metatype", StringComparison.Ordinal);
            Need(!claimsGrant || granted.Contains(owner.Id), "forged-or-removed-metatype-grant", owner.Path);
        }
        // Direct bonus rows use Metatype source + selected metatype GUID in legacy saves.
        var saved = new XElement("metatype", new XElement("guid", parentId));
        owners.Add(new Owner("Metatype", parentId, id, saved, metatype, 1, "", true, "metatype"));
    }

    private void BindQualityGrants()
    {
        var first = new HashSet<Guid>();
        var consumed = new HashSet<Guid>();
        foreach (var parent in owners.Where(o => finalizedModules?.OwnsQuality(o.Saved) == true))
        {
            foreach (var link in finalizedModules!.EffectsForOwner(parent.Id)
                .Where(e => Text(e, "improvementttype") == "SpecificQuality"))
            {
                Guid childId = Id(link, "improvedname");
                var child = owners.SingleOrDefault(o => o.Id == childId && o.Kind == "Quality");
                Need(child is not null && finalizedModules.OwnsQuality(child.Saved)
                    && child.Id != parent.Id && consumed.Add(child.Id)
                    && qualityGrantParents.TryAdd(child.Id, parent.Id), "finalized-module-quality-grant-mismatch", parent.Path);
            }
        }
        foreach (var parent in owners)
        {
            if (finalizedModules?.OwnsQuality(parent.Saved) == true) continue;
            var containers = new List<XElement?> { Node(parent.Source, "bonus") };
            if (parent.Kind == "Quality" && first.Add(parent.SourceId)) containers.Add(Node(parent.Source, "firstlevelbonus"));
            foreach (var node in containers.Where(c => c is not null).SelectMany(c => c!.Elements("addqualities")))
            {
                Need(parent.Kind == "Quality" && !node.HasAttributes && node.Elements().Any()
                    && !node.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value)),
                    "quality-grant-container-shape", parent.Path);
                var children = new List<Guid>();
                foreach (var grant in node.Elements())
                {
                    // More complex conditional/forced/count/cost creation needs a separate
                    // acquisition contract. The ordinary Glamour grant is unconditional.
                    Need(grant.Name == "addquality" && !grant.HasElements
                        && grant.Attributes().All(a => a.Name == "select") && grant.Value.Length > 0,
                        "quality-grant-shape", parent.Path);
                    CharacterSheetOwnerSource? receipt = null;
                    Need(sources is not null && sources.TryResolveSheetGrantSource("Quality", grant.Value, out receipt)
                        && receipt is not null, "quality-grant-source-disabled-missing-or-ambiguous", parent.Path);
                    var captured = receipt!;
                    var canonical = Parse(captured.SourceXml);
                    string forced = grant.Attribute("select")?.Value ?? "";
                    bool selects = new[] { "bonus", "firstlevelbonus" }.Any(c => Node(canonical, c)?.Elements()
                        .Any(e => e.Name.LocalName.StartsWith("select", StringComparison.Ordinal)) == true);
                    var links = (Node(root, "improvements")?.Elements() ?? []).Where(e =>
                        Text(e, "improvementttype", "") == "SpecificQuality"
                        && Text(e, "sourcename", "") == parent.Id.ToString("D")
                        && Text(e, "improvementsource", "") == parent.Kind).ToArray();
                    var candidates = owners.Where(o => o.Kind == "Quality" && o.SourceId == captured.SourceId
                        && !consumed.Contains(o.Id) && Text(o.Saved, "qualitysource", "") == "Improvement"
                        && (selects && forced.Length == 0 || o.Extra == forced)
                        && links.Count(e => Text(e, "improvedname", "") == o.Id.ToString("D")) == 1)
                        .OrderBy(o => o.Id).ToArray();
                    Need(candidates.Length > 0, "quality-grant-owner-missing-or-forged", parent.Path);
                    var child = candidates[0];
                    Need(child.Id != parent.Id && child.Enabled && XNode.DeepEquals(child.Source, canonical)
                        && Int(child.Saved, "bp") == 0 && !Flag(child.Saved, "contributetolimit"),
                        "quality-grant-owner-mismatch", child.Path);
                    consumed.Add(child.Id); qualityGrantParents.Add(child.Id, parent.Id); children.Add(child.Id);
                    if (!selects) boundChoices.Add(child.Id);
                    checks.Add(() => sources!.TryResolveSheetGrantSource("Quality", grant.Value, out var final) && final == captured);
                    sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
                }
                qualityGrants.Add(node, children);
            }
        }
        foreach (var child in owners.Where(o => o.Kind == "Quality" && Text(o.Saved, "qualitysource", "") == "Improvement"))
        {
            Need(consumed.Contains(child.Id), "unbound-improvement-quality", child.Path);
            var ancestry = new HashSet<Guid> { child.Id }; Guid current = child.Id;
            while (qualityGrantParents.TryGetValue(current, out Guid parent))
            {
                Need(ancestry.Count < 16 && ancestry.Add(parent), "quality-grant-cycle", child.Path);
                current = parent;
            }
            var ancestor = GrantedTraits.SingleOrDefault(t => t.OwnerId == current);
            GrantedTraits.Add(new("Quality", child.SourceId, Text(child.Source, "name"), Text(child.Source, "source"), TraitActionUnavailableReason(child))
            { OwnerId = child.Id, ParentMetatypeId = ancestor?.ParentMetatypeId ?? Guid.Empty,
                SelectedMetatypeId = ancestor?.SelectedMetatypeId ?? Guid.Empty, Selection = child.Extra, Rating = child.Rating });
        }
    }

    private void BindTraitWeapons()
    {
        var bound = new HashSet<Guid>();
        foreach (var owner in owners.Where(o => o.Kind == "Quality"))
        {
            var grant = Node(owner.Source, "addweapon");
            if (grant is null) continue;
            Need(!grant.HasElements && grant.Attributes().All(a => a.Name == "rating") && grant.Value.Length > 0,
                "trait-weapon-grant-shape", owner.Path + "/addweapon");
            Need(int.TryParse(grant.Attribute("rating")?.Value ?? "0", NumberStyles.None,
                CultureInfo.InvariantCulture, out int rating), "trait-weapon-rating-unconsumed", owner.Path);
            CharacterSheetOwnerSource? receipt = null;
            Need(sources is not null && sources.TryResolveSheetNamedSource("Weapon", grant.Value, out receipt) && receipt is not null,
                "trait-weapon-source-unavailable", owner.Path);
            var captured = receipt!;
            var children = owners.Where(o => o.Kind == "Weapon" && Text(o.Saved, "parentid", "") == owner.Id.ToString("D")).ToArray();
            Need(children is [var child] && child.SourceId == captured.SourceId && child.Rating == rating
                && XNode.DeepEquals(child.Source, Parse(captured.SourceXml)) && Dec(child.Saved, "cost") == 0
                && Id(owner.Saved, "weaponguid") == child.Id && bound.Add(child.Id),
                "trait-weapon-owner-closure", owner.Path);
            checks.Add(() => sources!.TryResolveSheetNamedSource("Weapon", grant.Value, out var final) && final == captured);
            sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
        }
        foreach (var weapon in owners.Where(o => o.Kind == "Weapon"))
            Need(!owners.Any(o => o.Kind == "Quality" && Text(weapon.Saved, "parentid", "") == o.Id.ToString("D"))
                || bound.Contains(weapon.Id), "forged-trait-weapon-owner", weapon.Path);
    }

    private static int GrantRating(XElement grant)
    {
        string expression = grant.Attribute("rating")?.Value ?? "0";
        if (string.IsNullOrWhiteSpace(expression)) return 0; // Native ExpressionToInt's absent/empty default.
        try
        {
            // Character.Create uses ExpressionToInt(..., force, 0, 0), which
            // rounds and clamps at zero. Ordinary runners have no Force scope.
            // Do not substitute owner.Rating, cached attributes or arbitrary root
            // values for F/1D6/2D6 or other unresolved creation-time variables.
            decimal value = CharacterCreationEssenceExpression1222.Evaluate(expression,
                new Dictionary<string, decimal>(StringComparer.Ordinal));
            return Math.Max(0, CharacterCreationEssenceRules1222.Round(value));
        }
        catch (Exception e) when (e is InvalidDataException or FormatException or OverflowException or DivideByZeroException)
        { throw Error("metatype-power-rating-expression-unconsumed", "metatype/powers/rating=" + expression); }
    }

    private Owner BindGrant(string kind, XElement grant, HashSet<Guid> granted, Guid parentId, Guid selectedId, int rating = 1,
        string? qualityOrigin = null)
    {
        CharacterSheetOwnerSource? receipt = null;
        Need(sources is not null && sources.TryResolveSheetGrantSource(kind, grant.Value, out receipt) && receipt is not null,
            "metatype-grant-source-disabled-missing-or-ambiguous", "metatype/" + kind);
        var captured = receipt!;
        var canonical = Parse(captured.SourceXml);
        bool selects = new[] { "bonus", "firstlevelbonus" }.Any(c => Node(canonical, c)?.Elements()
            .Any(e => e.Name.LocalName.StartsWith("select", StringComparison.Ordinal)) == true);
        string forced = grant.Attribute("select")?.Value ?? "";
        string? extra = selects ? (forced.Length > 0 ? forced : null)
            : kind == "Quality" ? forced
            : rating != 0 ? rating.ToString(CultureInfo.InvariantCulture)
            : Node(canonical, "bonus") is null ? forced : "";
        var candidates = owners.Where(o => o.Kind == kind && o.SourceId == captured.SourceId
            && (extra is null || o.Extra == extra) && !granted.Contains(o.Id)
            && (kind != "CritterPower" || o.Rating == rating)
            && (kind != "Quality" || Text(o.Saved, "qualitysource", "") == qualityOrigin))
            .OrderBy(o => o.Id).ToArray();
        // Repeated canonical grants (e.g. multiple levels) consume distinct
        // instances. The final unmatched-grant check rejects every excess owner.
        Need(candidates.Length > 0, "metatype-grant-missing-or-duplicate", "metatype/" + kind);
        var owner = candidates[0];
        Need(owner.Enabled && XNode.DeepEquals(owner.Source, canonical), "metatype-grant-source-mismatch", owner.Path);
        checks.Add(() => sources!.TryResolveSheetGrantSource(kind, grant.Value, out var final) && final == captured);
        sourceEvidence.Add(CharacterCreationFinalizationDigest.Compute(captured));
        granted.Add(owner.Id);
        if (!selects && extra is not null) boundChoices.Add(owner.Id);
        GrantedTraits.Add(new(kind, owner.SourceId, Text(owner.Source, "name"), Text(owner.Source, "source"), TraitActionUnavailableReason(owner))
        { OwnerId = owner.Id, ParentMetatypeId = parentId, SelectedMetatypeId = selectedId,
            Selection = owner.Extra, Rating = owner.Rating });
        return owner;
    }

    private static string? TraitActionUnavailableReason(Owner owner)
    {
        if (owner.Kind == "CritterPower") return "Critter-power actions require a separate action authority.";
        if (Node(owner.Source, "addweapon") is not null) return "Granted weapon attacks require a separate attack authority.";
        if (new[] { "bonus", "firstlevelbonus" }.All(c => Node(owner.Source, c)?.HasElements != true))
            return "This source has no typed action mechanics; situational trait effects require separate action authority.";
        return null;
    }
}
