using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

internal static partial class CharacterCreationLifeModuleSkillsRules
{
    // Consume only the effect vocabulary compiled by the source-backed planners.
    // Unknown skill/cost effects cannot silently produce an apparently exact quote.
    private static bool TryReadModifiers(CharacterCreationFoundationSequenceWritePlan effects,
        CharacterCreationLifeModuleMetatypeWritePlan racial, CharacterCreationLifeModuleTalentWritePlan talent,
        CharacterCreationSkillsCatalog catalog, out SkillModifiers result)
    {
        result = new();
        var skillNames = catalog.ActiveSkills.Where(row => !row.IsExotic).Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var groupNames = catalog.SkillGroups.Select(row => row.Name).ToHashSet(StringComparer.Ordinal);
        var categories = catalog.ActiveSkills.Concat(catalog.KnowledgeSkills).Select(row => row.Category).ToHashSet(StringComparer.Ordinal);
        foreach (string xml in effects.ImprovementXml.Concat(racial.ImprovementXml).Concat(talent.ImprovementXml))
        {
            XElement item = XElement.Parse(xml);
            string type = item.Element("improvementttype")?.Value ?? string.Empty;
            if (IsSkillIndependent(type)) continue;
            // A pool-only bonus (e.g. Matrix Perception) is retained by the talent
            // plan but changes neither purchased ratings nor their price.
            if (type == "Skill" && item.Element("addtorating")?.Value == "0"
                && item.Element("min")?.Value == "0" && item.Element("max")?.Value == "0") continue;
            if (item.Name != "improvement" || item.Elements().GroupBy(node => node.Name).Any(group => group.Count() != 1)
                || item.Element("enabled")?.Value != "1" || item.Element("addtorating")?.Value != "0"
                || new[] { "condition", "unique", "uniquename", "exclude", "target" }.Any(name => !string.IsNullOrEmpty(item.Element(name)?.Value))
                || new[] { "min", "max", "aug", "augmax" }.Any(name => item.Element(name)?.Value != "0")
                || !decimal.TryParse(item.Element("val")?.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out decimal value)) return false;
            string name = item.Element("improvedname")?.Value ?? string.Empty;
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
                case "SkillCategoryKarmaCostMultiplier":
                case "SkillCategorySpecializationKarmaCostMultiplier":
                case "SkillGroupCategoryKarmaCostMultiplier":
                case "SkillCategoryPointCostMultiplier":
                    if (!categories.Contains(name) || value < 0) return false;
                    result.CostMultipliers.Add((type, name, value / 100m));
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
        return result.SkillLevels.Values.All(value => value >= 0) && result.GroupLevels.Values.All(value => value >= 0);
    }

    private static bool IsSkillIndependent(string type) => type is
        "Attributelevel" or "Attribute" or "FreePositiveQualities" or "FreeNegativeQualities"
        or "QualityLevel" or "SpecificQuality" or "Notoriety" or "TrustFund" or "DamageResistance"
        or "Armor" or "Reach" or "LifestyleCost" or "Gear" or "SpecialTab" or "BlockSpellDescriptor" or "LimitSpellCategory"
        or "PathogenContactResist" or "PathogenIngestionResist" or "PathogenInhalationResist" or "PathogenInjectionResist"
        or "ToxinContactResist" or "ToxinIngestionResist" or "ToxinInhalationResist" or "ToxinInjectionResist";

    private sealed class SkillModifiers
    {
        internal Dictionary<string, int> SkillLevels { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> GroupLevels { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> DisabledGroupCategories { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> Unlocks { get; } = new(StringComparer.Ordinal);
        internal List<(string Type, string Category, decimal Factor)> CostMultipliers { get; } = [];
        internal decimal KnowledgePoints { get; set; }

        internal decimal Multiplier(string type, IEnumerable<string> categories)
        {
            var selected = categories.ToHashSet(StringComparer.Ordinal);
            return CostMultipliers.Where(item => item.Type == type && selected.Contains(item.Category))
                .Aggregate(1m, (factor, item) => checked(factor * item.Factor));
        }
    }
}
