using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

/// <summary>
/// Automatic, exact-child Life Modules grants. Callers resolve the metatype and
/// the complete ordered quality sources from the same current authority, and
/// recheck that authority before committing. This is not a purchase admission.
/// </summary>
public static class CharacterCreationLifeModuleRacialGrants8590
{
    public static bool TryProject(CharacterCreationMetatypeOptionProjection metatype,
        IReadOnlyList<CharacterCreationTalentQualitySource> sources, string seed,
        out XElement[] qualities, out XElement[] improvements)
    {
        qualities = []; improvements = [];
        try
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(seed)
                || sources is not { Count: > 0 and <= 32 }
                || sources.Count != metatype.GrantedQualities.Count
                || sources.Select(source => source.SourceId).Distinct(StringComparer.Ordinal).Count() != sources.Count)
                return false;
            var saved = new List<XElement>();
            var effects = new List<XElement>();
            for (int index = 0; index < sources.Count; index++)
            {
                var source = sources[index];
                if (source.Name != metatype.GrantedQualities[index].Name) return false;
                string owner = CharacterCreationFinalizationProjector.StableGuid(
                    $"life-module-racial:{seed}:{source.SourceId}").ToString("D");
                if (!TryCompile(metatype, source, owner, out var quality, out var owned)) return false;
                saved.Add(quality); effects.AddRange(owned);
            }
            var empty = new XElement("character", new XElement("qualities"));
            foreach (var source in sources)
                CheckRestrictions(metatype, source, empty, saved, new HashSet<string>(StringComparer.Ordinal));
            qualities = saved.ToArray(); improvements = effects.ToArray();
            return true;
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidDataException
            or InvalidOperationException or OverflowException or FormatException) { return false; }
    }

    internal static bool IsBoundGrant(CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationTalentQualitySource source)
    {
        if (metatype is not { IsEnabled: true, Blockers.Count: 0, Category: "Metavariant",
                MetatypeIdentity.MetavariantOptionId: not null }
            || !CharacterCreationMetatypeSelectionRules.IsValid(metatype.MetatypeIdentity)
            || metatype.OptionId != metatype.MetatypeIdentity.MetavariantOptionId
            || string.IsNullOrWhiteSpace(metatype.ParentMetatypeLabel)
            || !CharacterCreationTalentQualitySourceRules.IsValidSource(source)
            || source.Reference != source.Name || source.ForcedSelection.Length != 0
            || source.GrantedGearSources is { Count: > 0 }) return false;
        string anchor = $"metatypes.xml#metatype:{metatype.MetatypeIdentity.ParentMetatypeOptionId}/metavariant:{metatype.OptionId}";
        var declarations = metatype.GrantedQualities.Where(row => row.Name == source.Name).ToArray();
        if (!metatype.SourceAnchorIds.Contains(anchor, StringComparer.Ordinal)
            || declarations is not [var declaration]
            || declaration.Polarity is not ("positive" or "negative")
            || !metatype.SourceAnchorIds.Contains($"metatypes.xml#metatype:{metatype.MetatypeIdentity.ParentMetatypeOptionId}", StringComparer.Ordinal)
            || declaration.SourceAnchorIds.Count != 1
            || declaration.SourceAnchorIds[0] != $"{anchor}/qualities/{declaration.Polarity}:{source.Name}")
            return false;
        var definition = XElement.Parse(source.CanonicalSourceXml);
        return string.Equals(definition.Element("category")?.Value, declaration.Polarity, StringComparison.OrdinalIgnoreCase);
    }

    // Legacy Character.Create grants directly from charNode/qualities; purchase
    // RequirementsMet belongs to SelectQuality. Admit only this exact purchase
    // gate on an actual bound metagenic grant, not arbitrary required clauses.
    internal static bool IsPurchaseOnlySurgeGrant(CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationTalentQualitySource source)
    {
        if (!IsBoundGrant(metatype, source)) return false;
        var definition = XElement.Parse(source.CanonicalSourceXml);
        var required = definition.Elements("required").ToArray();
        var metagenic = definition.Elements("metagenic").ToArray();
        var exact = new XElement("required", new XElement("oneof", new[] { "I", "II", "III" }
            .Select(level => new XElement("quality", $"Changeling (Class {level} SURGE)"))));
        return metagenic is [var flag] && !flag.HasAttributes && !flag.HasElements
            && bool.TryParse(flag.Value, out bool enabled) && enabled
            && !definition.Elements("metagenetic").Any()
            && required is [var gate] && XNode.DeepEquals(gate, exact);
    }

    internal static void CheckRestrictions(CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationTalentQualitySource source, XElement other, IReadOnlyList<XElement> granted,
        IReadOnlySet<string> flags)
    {
        if (!IsBoundGrant(metatype, source)) throw new InvalidDataException("Unbound Life Modules racial grant.");
        var definition = XElement.Parse(source.CanonicalSourceXml);
        if (IsPurchaseOnlySurgeGrant(metatype, source)) definition.Elements("required").Remove();
        // The original source and its digests are never rewritten. Only the
        // identified purchase gate is excluded from this automatic-grant check.
        // All forbidden clauses, malformed gates and unknown requirements remain.
        CharacterCreationAwakenedLegacyProjector.CheckRestrictions(definition, other, granted, flags);
    }

    internal static bool TryCompile(CharacterCreationMetatypeOptionProjection metatype,
        CharacterCreationTalentQualitySource source, string owner, out XElement quality, out XElement[] effects)
    {
        quality = new XElement("quality"); effects = [];
        if (!IsBoundGrant(metatype, source) || !Guid.TryParseExact(owner, "D", out Guid id) || id == Guid.Empty)
            return false;
        // Preserve the existing complete grammar and exact two Cyclopean rows,
        // including the portable defense8587 source channel, without widening it.
        if (CharacterCreationPriorityRacialQualities.TryProjectCurrentNativeSkillCosts([source],
            source.SourceNodeDigest, out var original, out var existing, allowLifeModuleRacialDice: true))
        {
            if (original is not [var saved] || existing.Any(row => row.Element("improvementsource")?.Value != "Quality"
                || row.Element("sourcename")?.Value != saved.Element("guid")?.Value)) return false;
            saved.Element("guid")!.Value = owner;
            foreach (var row in existing) row.Element("sourcename")!.Value = owner;
            quality = saved; effects = existing;
            return true;
        }
        var definition = XElement.Parse(source.CanonicalSourceXml);
        if (definition.Elements("required").Any() && !IsPurchaseOnlySurgeGrant(metatype, source)) return false;
        var bonus = definition.Element("bonus");
        if (definition.Elements("bonus").Count() > 1 || bonus is null || bonus.HasAttributes
            || bonus.Nodes().Any(node => node is not XElement and not XComment
                && (node is not XText text || !string.IsNullOrWhiteSpace(text.Value)))
            || definition.Elements("firstlevelbonus").Any(node => node.HasElements)) return false;
        // One literal effect or an empty source bonus. Composite, prompted,
        // conditional, generated and first-level mechanics need their own closure.
        XElement[] nodes = bonus.Elements().ToArray();
        if (nodes.Length > 1) return false;
        if (nodes is [var effect])
        {
            string type, target = string.Empty, unique = string.Empty;
            XElement expected;
            string literal = effect.Name == "specificskill" ? effect.Element("bonus")?.Value ?? "" : effect.Value;
            if (!int.TryParse(literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
                || value is < 1 or > 100) return false;
            switch (effect.Name.LocalName)
            {
                case "armor":
                    type = "Armor"; unique = "group0";
                    expected = new XElement("armor", new XAttribute("group", "0"), value);
                    break;
                case "spellresistance":
                    type = "SpellResistance"; expected = new XElement("spellresistance", value);
                    break;
                case "specificskill":
                    type = "Skill"; target = effect.Element("name")?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(target) || target != target.Trim()) return false;
                    expected = new XElement("specificskill", new XElement("name", target), new XElement("bonus", value));
                    break;
                default: return false;
            }
            if (!XNode.DeepEquals(effect, expected)) return false;
            effects = [CharacterCreationAwakenedLegacyProjector.Improvement(type, target, owner, "Quality", value, unique)];
        }
        return CharacterCreationLegacySourceProjector.TryBuildLifeModuleRacialQualityInstance8590(source, owner, metatype, out quality);
    }
}
