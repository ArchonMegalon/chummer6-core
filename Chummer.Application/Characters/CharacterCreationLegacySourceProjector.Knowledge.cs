using System.Globalization;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    private static bool IsKnowledgeCategoryElement(string name) => name is
        "skillcategorykarmacost" or "skillcategorykarmacostmultiplier"
        or "skillcategorypointcostmultiplier" or "skillcategoryspecializationkarmacostmultiplier"
        or "skillcategoryspecializationkarmacost" or "activeskillkarmacost" or "activeskillkarmacostmultiplier"
        or "knowledgeskillkarmacost" or "knowledgeskillkarmacostmultiplier"
        or "skillgroupkarmacost" or "skillgroupkarmacostmultiplier"
        or "skillgroupcategorykarmacost" or "skillgroupcategorykarmacostmultiplier";

    private static string NativeCondition(CompiledEffect effect)
        => CharacterCreationKnowledgeCategoryRules.TryLowerNativeCondition(effect.Condition, out string nativeCondition)
            ? nativeCondition
            : throw new InvalidOperationException("Unsupported compiled lifecycle condition.");

    private static bool IsKnowledgeCategoryEffect(CompiledEffect effect)
        => CharacterCreationSkillKarmaModifierRules.IsSupportedType(effect.ImprovementType);

    internal static bool IsKnowledgeCompletionSource(string xml, int? arms)
    {
        if (xml is not { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength }) return false;
        var source = ParseBoundedSource(xml, "quality", s_QualitySourceChildren);
        return source is not null && TryReadQualityDefinition(source, out var definition, arms)
            && definition.BonusEffects.Concat(definition.FirstLevelEffects)
                .Any(effect => CharacterCreationKnowledgeCategoryRules.IsSupportedType(effect.ImprovementType));
    }

    private static bool TryReadKnowledgeCategoryEffect(XElement node, out CompiledEffect effect)
    {
        effect = null!;
        if (node.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value))
            || node.Elements().Any(child => child.Name.Namespace != XNamespace.None
                || child.Name.LocalName is not ("name" or "val" or "min" or "max" or "condition")
                || child.HasAttributes || child.HasElements)
            || node.Elements().GroupBy(child => child.Name).Any(group => group.Count() != 1))
            return false;
        string? name = node.Element("name")?.Value;
        string? value = node.Element("val")?.Value;
        string condition = node.Element("condition")?.Value ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name != name.Trim()
            || !decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal amount)
            || !Bound("min", out int minimum) || !Bound("max", out int maximum)
            || maximum != 0 && maximum < minimum
            || !CharacterCreationKnowledgeCategoryRules.IsSupportedCondition(condition))
            return false;
        string type = node.Name.LocalName switch
        {
            "skillcategorykarmacost" => "SkillCategoryKarmaCost",
            "skillcategorykarmacostmultiplier" => "SkillCategoryKarmaCostMultiplier",
            "skillcategorypointcostmultiplier" => "SkillCategoryPointCostMultiplier",
            "skillcategoryspecializationkarmacostmultiplier" => "SkillCategorySpecializationKarmaCostMultiplier",
            "skillcategoryspecializationkarmacost" => "SkillCategorySpecializationKarmaCost",
            "activeskillkarmacost" => "ActiveSkillKarmaCost",
            "activeskillkarmacostmultiplier" => "ActiveSkillKarmaCostMultiplier",
            "knowledgeskillkarmacost" => "KnowledgeSkillKarmaCost",
            "knowledgeskillkarmacostmultiplier" => "KnowledgeSkillKarmaCostMultiplier",
            "skillgroupkarmacost" => "SkillGroupKarmaCost",
            "skillgroupkarmacostmultiplier" => "SkillGroupKarmaCostMultiplier",
            "skillgroupcategorykarmacost" => "SkillGroupCategoryKarmaCost",
            "skillgroupcategorykarmacostmultiplier" => "SkillGroupCategoryKarmaCostMultiplier",
            _ => string.Empty
        };
        if (type.Length == 0 || type.EndsWith("Multiplier", StringComparison.Ordinal) && amount < 0) return false;
        effect = new(type, name, amount, minimum, maximum, condition);
        return true;

        bool Bound(string field, out int result)
        {
            result = 0;
            return node.Element(field) is not { } child
                || int.TryParse(child.Value, NumberStyles.None, CultureInfo.InvariantCulture, out result) && result >= 0;
        }
    }

    internal static bool TryKnowledgeModifiers(IReadOnlyList<CharacterCreationQualitySelection> selections,
        CharacterCreationSkillsAuthority authority, out IReadOnlyList<CharacterCreationKnowledgeCategoryEffect> effects)
    {
        effects = [];
        if (!TrySkillKarmaModifiers(selections, authority, out _) || !TryProjectCompletionQualityEffects(selections, out var compiled)) return false;
        var result = new List<CharacterCreationKnowledgeCategoryEffect>();
        foreach (var effect in compiled.Where(row => CharacterCreationKnowledgeCategoryRules.IsSupportedType(row.ImprovementType)))
        {
            // The shared compiler has already rejected unknown/ambiguous targets.
            // Keep the existing knowledge-only point and receipt consumer unchanged.
            if (!authority.KnowledgeSkills.Any(row => row.Category == effect.ImprovedName)) continue;
            result.Add(new(effect.ImprovementType, effect.ImprovedName, effect.Value, effect.Minimum, effect.Maximum,
                effect.Condition, effect.SourceId, effect.SourceNodeDigest, effect.OptionId, effect.Level,
                effect.Container, effect.Position, effect.SourceAnchorIds));
        }
        effects = result;
        return true;
    }
}
