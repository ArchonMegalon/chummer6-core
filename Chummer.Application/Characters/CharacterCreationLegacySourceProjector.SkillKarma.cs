using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLegacySourceProjector
{
    internal static CharacterCreationQualitiesAuthority AdmitSkillKarmaQualityAuthority(CharacterCreationQualitiesAuthority original)
    {
        var authority = original with { Options = original.Options.Select(option =>
        {
            // Preserve every earlier source/book/prerequisite/choice blocker.
            if (option.IsSelectable || option.DisableReasonKey != CharacterCreationQualitiesBlockers.EffectsNotProjectable
                || !IsSkillKarmaCompletionSource(option.SourceNodeXml, option.ResolvedArmCount)) return option;
            var admitted = option with { IsSelectable = true, EligibilityIsExact = true, DisableReasonKey = null };
            return admitted with { OptionDigest = CharacterCreationQualitiesRules.ComputeOptionDigest(admitted) };
        }).ToArray() };
        return authority with { AuthorityDigest = CharacterCreationQualitiesRules.ComputeAuthorityDigest(authority) };
    }

    internal static bool IsSkillKarmaCompletionSource(string xml, int? arms)
    {
        if (xml is not { Length: > 0 and <= CharacterCreationQualitiesRules.MaximumSourceNodeLength }) return false;
        var source = ParseBoundedSource(xml, "quality", s_QualitySourceChildren);
        return source is not null && TryReadQualityDefinition(source, out var definition, arms)
            && definition.BonusEffects.Concat(definition.FirstLevelEffects)
                .Any(effect => CharacterCreationSkillKarmaModifierRules.IsSupportedType(effect.ImprovementType));
    }

    internal static bool TrySkillKarmaModifiers(IReadOnlyList<CharacterCreationQualitySelection> selections,
        CharacterCreationSkillsAuthority authority, out IReadOnlyList<CharacterCreationSkillKarmaEffect> effects)
        => TrySkillKarmaModifiers(selections, authority.ActiveSkills, authority.KnowledgeSkills, authority.SkillGroups, out effects);

    internal static bool TrySkillKarmaModifiers(IReadOnlyList<CharacterCreationQualitySelection> selections,
        IReadOnlyList<CharacterCreationSkillCatalogEntry> active, IReadOnlyList<CharacterCreationSkillCatalogEntry> knowledge,
        IReadOnlyList<CharacterCreationSkillGroupCatalogEntry> groups, out IReadOnlyList<CharacterCreationSkillKarmaEffect> effects)
    {
        effects = [];
        // Bound expansion before the shared occurrence compiler iterates ratings.
        if (selections is not { Count: <= 256 } || selections.Any(s => s is null || s.Rating is < 1 or > 256)
            || selections.Sum(s => (long)s.Rating) > 256
            || !TryProjectCompletionQualityEffects(selections, out var compiled)) return false;
        var result = new List<CharacterCreationSkillKarmaEffect>();
        foreach (var effect in compiled.Where(e => CharacterCreationSkillKarmaModifierRules.IsSupportedType(e.ImprovementType)))
        {
            CharacterCreationSkillKarmaTarget[] targets;
            string type = effect.ImprovementType;
            if (type.StartsWith("SkillGroupCategory", StringComparison.Ordinal))
            {
                var members = active.Where(s => s.Category == effect.ImprovedName).ToArray();
                if (members.Length == 0 || knowledge.Any(s => s.Category == effect.ImprovedName)) return false;
                // Bind all category members. SkillGroup.GetRelevantSkillCategories
                // includes disabled members, unlike its paid-rating interval.
                targets = members.Select(Target).ToArray();
            }
            else if (type.StartsWith("SkillGroup", StringComparison.Ordinal))
            {
                var matches = groups.Where(g => g.Name == effect.ImprovedName).ToArray();
                if (matches.Length != 1) return false;
                targets = [new("group", matches[0].GroupId, matches[0].GroupDigest)];
            }
            else if (type.StartsWith("SkillCategory", StringComparison.Ordinal))
            {
                var a = active.Where(s => s.Category == effect.ImprovedName).ToArray();
                var k = knowledge.Where(s => s.Category == effect.ImprovedName).ToArray();
                if (a.Length + k.Length == 0 || a.Length != 0 && k.Length != 0
                    || type == "SkillCategoryPointCostMultiplier" && a.Length != 0) return false;
                targets = a.Concat(k).Select(Target).ToArray();
            }
            else
            {
                var matches = (type.StartsWith("Active", StringComparison.Ordinal) ? active : knowledge)
                    .Where(s => s.Name == effect.ImprovedName).ToArray();
                if (matches.Length != 1 || matches[0].IsExotic) return false;
                targets = [Target(matches[0])];
            }
            result.Add(new(type, effect.Value, effect.Minimum, effect.Maximum, effect.Condition,
                effect.SourceId, effect.SourceNodeDigest, effect.OptionId, effect.Level, effect.Container,
                effect.Position, targets, effect.SourceAnchorIds));
        }
        if (!CharacterCreationSkillKarmaModifierRules.IsValid(result)) return false;
        effects = result.ToArray();
        return true;
    }

    internal static CharacterCreationSkillKarmaTarget Target(CharacterCreationSkillCatalogEntry skill)
        => new(skill.Kind, skill.SourceSkillId, skill.SourceNodeDigest);
}
