using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationLifeModuleSkillsRules
{
    // Consume only the effect vocabulary compiled by the source-backed planners.
    // Unknown skill/cost effects cannot silently produce an apparently exact quote.
    private static bool TryReadModifiers(CharacterCreationFoundationSequenceWritePlan effects,
        CharacterCreationLifeModuleMetatypeWritePlan racial, CharacterCreationLifeModuleTalentWritePlan? talent,
        CharacterCreationSkillsCatalog catalog, out SkillModifiers result)
    {
        result = new();
        var skillNames = catalog.ActiveSkills.Where(row => !row.IsExotic).Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var groupNames = catalog.SkillGroups.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var categories = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).Select(row => row.Category).ToHashSet(StringComparer.Ordinal);
        // These are freshly source-compiled write plans, not saved character improvements.
        // Preserve owner identity when lowering module/racial/talent costs to the ordinary kernel.
        var owners = effects.QualityXml.Concat(racial.QualityXml).Concat(talent?.QualityXml ?? [])
            .Select(XElement.Parse).ToDictionary(row => row.Element("guid")!.Value, StringComparer.Ordinal);
        var nativeOwners = racial.QualityXml.Concat(talent?.QualityXml ?? []).Select(XElement.Parse)
            .ToDictionary(row => row.Element("guid")!.Value, StringComparer.Ordinal);
        if (!CharacterCreationKarmaGrantsLegacyProjector.TryCompileSkillCosts2568(racial.Metatype,
            talent?.Talent, talent?.Selection.SkillUnlock, new(racial.Sources, talent?.Source), out var nativeCosts,
            talent?.Selection.Restrictions, allowLifeModuleRacialDice: true)) return false;
        var costs = new List<CharacterCreationLegacySourceProjector.SourceQualityEffect>(nativeCosts);
        int nativePosition = 0;
        // New unconditional category rows require an exact native source owner
        // and occurrence match. Keep the historical module/conditional path.
        var unconditionalCategories = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var source in racial.Sources.Concat(talent?.Source is { } talentSource ? new[] { talentSource } : []))
        {
            XElement definition = XElement.Parse(source.CanonicalSourceXml);
            var categoriesFromSource = definition.Element("bonus")?.Elements("skillcategory")
                .Where(effect => string.IsNullOrEmpty(effect.Element("condition")?.Value)).ToArray() ?? [];
            if (categoriesFromSource.Length == 0) continue;
            var matchingOwners = nativeOwners.Values.Where(quality => quality.Element("sourceid")?.Value == source.SourceId).ToArray();
            if (matchingOwners.Length != 1) return false;
            foreach (var quality in matchingOwners)
            {
                if (!Guid.TryParseExact(quality.Element("guid")?.Value, "D", out Guid ownerId) || ownerId == Guid.Empty
                    || quality.Element("name")?.Value != source.Name
                    || quality.Element("qualitysource")?.Value != (racial.Sources.Contains(source) ? "Metatype" : "Selected")
                    || !XNode.DeepEquals(quality.Element("bonus"), definition.Element("bonus"))) return false;
                foreach (var effect in categoriesFromSource)
                {
                    if (!CharacterCreationAwakenedLegacyProjector.IsDiceOnlySkillCategoryGrant(effect)) return false;
                    XElement expected = CharacterCreationAwakenedLegacyProjector.Improvement("SkillCategory",
                        effect.Element("name")!.Value, quality.Element("guid")!.Value, "Quality",
                        int.Parse(effect.Element("bonus")!.Value, CultureInfo.InvariantCulture));
                    // Match CompileBonus even for an empty condition: assigning Value
                    // preserves its explicit end tag in the exact serialized row key.
                    expected.Element("condition")!.Value = effect.Element("condition")?.Value ?? string.Empty;
                    string key = expected.ToString(SaveOptions.DisableFormatting);
                    unconditionalCategories[key] = unconditionalCategories.GetValueOrDefault(key) + 1;
                }
            }
        }
        string costSourceDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
            { effects.PlanDigest, Racial = racial.PlanDigest, Talent = talent?.PlanDigest });
        foreach (var (xml, nativeRow) in effects.ImprovementXml.Select(xml => (xml, false))
            .Concat(racial.ImprovementXml.Concat(talent?.ImprovementXml ?? []).Select(xml => (xml, true))))
        {
            XElement item = XElement.Parse(xml);
            string type = item.Element("improvementttype")?.Value ?? string.Empty;
            if (IsSkillIndependent(type)) continue;
            // A pool-only bonus (e.g. Matrix Perception) is retained by the talent
            // plan but changes neither purchased ratings nor their price.
            if (type == "Skill" && item.Element("addtorating")?.Value == "0"
                && item.Element("min")?.Value == "0" && item.Element("max")?.Value == "0")
            {
                if (nativeRow && !skillNames.Contains(item.Element("improvedname")?.Value ?? string.Empty)) return false;
                continue;
            }
            // A source-validated pool bonus, conditional or unconditional, is retained in the saved
            // graph, but must not increase purchased ratings or discount them.
            if (type == "SkillCategory")
            {
                if (item.Name != "improvement" || item.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                    || item.Element("enabled")?.Value != "1" || item.Element("addtorating")?.Value != "0"
                    || item.Element("condition") is null
                    || !categories.Contains(item.Element("improvedname")?.Value ?? string.Empty)
                    || new[] { "min", "max", "aug", "augmax" }.Any(name => item.Element(name)?.Value != "0")
                    || new[] { "unique", "uniquename", "exclude", "target" }.Any(name => !string.IsNullOrEmpty(item.Element(name)?.Value))
                    || !decimal.TryParse(item.Element("val")?.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out _)) return false;
                if (item.Element("condition")!.Value.Length == 0)
                {
                    string key = item.ToString(SaveOptions.DisableFormatting);
                    if (!nativeRow || !unconditionalCategories.TryGetValue(key, out int remaining) || remaining <= 0) return false;
                    unconditionalCategories[key] = remaining - 1;
                }
                continue;
            }
            if (item.Name != "improvement" || item.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                || item.Element("enabled")?.Value != "1" || item.Element("addtorating")?.Value != "0"
                || new[] { "condition", "unique", "uniquename", "exclude", "target" }.Any(name => !string.IsNullOrEmpty(item.Element(name)?.Value))
                || new[] { "min", "max", "aug", "augmax" }.Any(name => item.Element(name)?.Value != "0")
                || !decimal.TryParse(item.Element("val")?.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value)) return false;
            string name = item.Element("improvedname")?.Value ?? string.Empty;
            if (CharacterCreationSkillKarmaModifierRules.IsSupportedType(type))
            {
                string owner = item.Element("sourcename")?.Value ?? string.Empty;
                if (!owners.TryGetValue(owner, out var quality)
                    || !Guid.TryParse(quality.Element("sourceid")?.Value, out Guid sourceId) || sourceId == Guid.Empty)
                    return false;
                if (nativeRow)
                {
                    // A retained native numeric row cannot issue a price. Recompile its
                    // owning source and reconcile every occurrence, including omissions.
                    if (!nativeOwners.ContainsKey(owner) || nativePosition >= nativeCosts.Count) return false;
                    var expected = nativeCosts[nativePosition++];
                    if (expected.SourceId != sourceId || expected.ImprovementType != type
                        || expected.ImprovedName != name || expected.Value != value) return false;
                    continue;
                }
                costs.Add(new(type, name, value, 0, 0, string.Empty, sourceId, costSourceDigest,
                    "life-module:" + owner, 1, "bonus", costs.Count, ["qualities.xml#quality:" + sourceId.ToString("D")]));
                continue;
            }
            switch (type)
            {
                case "SkillLevel":
                case "SkillGroupLevel":
                    if (value != decimal.Truncate(value) || !(type == "SkillLevel" ? skillNames : groupNames).Contains(name)) return false;
                    var levels = type == "SkillLevel" ? result.SkillLevels : result.GroupLevels;
                    levels[name] = checked(levels.GetValueOrDefault(name) + (int)value);
                    break;
                case "FreeKnowledgeSkills":
                    if (name.Length != 0) return false;
                    result.KnowledgePoints = checked(result.KnowledgePoints + value);
                    break;
                case "SkillGroupCategoryDisable":
                    if (!categories.Contains(name) || value != 0) return false;
                    result.DisabledGroupCategories.Add(name);
                    break;
                case "BlockSkillCategoryDefault":
                    if (!categories.Contains(name) || value != 0) return false;
                    break; // Defaulting is not a purchased rating; preserve its saved effect.
                case "SpecialSkills":
                    if (value != 0 || name is not ("Magician" or "Adept" or "Aware" or "Explorer"
                        or "Technomancer" or "Sorcery" or "Conjuring" or "Enchanting" or "Spellcasting")) return false;
                    result.Unlocks.Add(name);
                    break;
                default: return false;
            }
        }
        if (unconditionalCategories.Values.Any(remaining => remaining != 0) || nativePosition != nativeCosts.Count
            || !CharacterCreationLegacySourceProjector.TryBindSkillKarmaModifiers(costs, catalog.ActiveSkills,
                catalog.KnowledgeSkills, catalog.SkillGroups, out var bound, allowActiveCategoryPointCosts: true)) return false;
        result.Costs = bound;
        return result.SkillLevels.Values.All(value => value >= 0) && result.GroupLevels.Values.All(value => value >= 0);
    }

    private static bool IsSkillIndependent(string type) => type is
        "Attributelevel" or "Attribute" or "FreePositiveQualities" or "FreeNegativeQualities"
        or "QualityLevel" or "SpecificQuality" or "Notoriety" or "TrustFund" or "DamageResistance" or "NuyenMaxBP"
        or "Armor" or "SpellResistance" or "Reach" or "LifestyleCost" or "Gear" or "SpecialTab" or "BlockSpellDescriptor" or "LimitSpellCategory" or "LimitSpiritCategory"
        or "PathogenContactResist" or "PathogenIngestionResist" or "PathogenInhalationResist" or "PathogenInjectionResist"
        or "ToxinContactResist" or "ToxinIngestionResist" or "ToxinInhalationResist" or "ToxinInjectionResist";

    private sealed class SkillModifiers
    {
        internal Dictionary<string, int> SkillLevels { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> GroupLevels { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> DisabledGroupCategories { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Unlocks { get; } = new(StringComparer.Ordinal);
        internal IReadOnlyList<CharacterCreationSkillKarmaEffect> Costs { get; set; } = [];
        internal decimal KnowledgePoints { get; set; }

    }
}
