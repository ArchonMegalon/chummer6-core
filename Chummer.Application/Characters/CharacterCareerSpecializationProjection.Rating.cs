using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using static Chummer.Application.Characters.SpecializationXmlReader;

namespace Chummer.Application.Characters;

// Source admission only. Free-point, cap and group-break arithmetic belongs to Core.
internal static class SpecializationRatingProjection
{
    internal sealed record Member(Guid Id, CharacterActiveSkillSource Source,
        CharacterCareerSkillRatingPoints Points, bool Enabled);

    internal sealed record Group(CharacterCareerSkillRatingPoints Points,
        CharacterCareerSkillGroupRating Rating, IReadOnlyList<Member> Members, bool Disabled);

    internal static bool TryReadPoints(XElement? saved, XElement? improvements, string name,
        bool group, out CharacterCareerSkillRatingPoints points)
    {
        points = null!;
        if (!CharacterCareerSkillRatingRules.TryReadFreePoints(improvements, name, group, out var free))
            return false;
        points = new(saved is null ? 0 : ReadRequiredNonNegativeInt(saved, "base"),
            saved is null ? 0 : ReadRequiredNonNegativeInt(saved, "karma"), free.Base, free.Levels);
        return true;
    }

    internal static bool TryReadGroup(XElement? savedGroup, string name,
        IEnumerable<XElement> savedSkills, XElement? improvements, ICharacterSourceDataContext sourceContext,
        int maximum, out Group group)
    {
        group = null!;
        if (!TryReadPoints(savedGroup, improvements, name, true, out var groupPoints)) return false;
        List<Member> members = [];
        HashSet<Guid> sourceIds = [];
        foreach (XElement saved in savedSkills)
        {
            if (ReadRequiredBool(saved, "isknowledge")) return false;
            Guid id = ReadRequiredGuid(saved, "guid", "An active skill");
            Guid sourceId = ReadRequiredGuid(saved, "suid", "An active skill");
            if (!sourceContext.TryResolveActiveSkillSource(sourceId.ToString("D"), out var source)
                || !Guid.TryParse(source.SourceSkillId, out Guid resolved) || resolved != sourceId) return false;
            if (source.SkillGroup != name) continue;
            if (!sourceIds.Add(sourceId) || source.IsExotic
                || ReadRequiredText(saved, "skillcategory", "An active skill") != source.SkillCategory
                || HasUnsupportedRatingAuthority(improvements, source, source.Name)
                || !TryReadPoints(saved, improvements, source.Name, false, out var points)) return false;
            members.Add(new(id, source, points,
                !SpecializationGroupProjection.IsSkillDisabled(improvements, source.Name, source.SkillCategory)));
        }
        if (members.Count == 0) return false;
        bool disabled = SpecializationGroupProjection.IsGroupDisabled(improvements, name,
            members.Select(m => m.Source.SkillCategory).Distinct(StringComparer.Ordinal).ToArray());
        if (!CharacterCareerSkillRatingRules.TryProjectGroup(groupPoints, members.Select(m => m.Points).ToArray(),
                disabled, maximum, out var rating)) return false;
        group = new(groupPoints, rating, members, disabled);
        return true;
    }

    internal static bool HasUnsupportedRatingAuthority(XElement? improvements,
        CharacterActiveSkillSource source, string dictionaryKey)
    {
        foreach (XElement row in improvements?.Elements("improvement") ?? [])
        {
            string condition = ReadOptionalText(row, "condition", string.Empty);
            if (!ReadOptionalBool(row, "enabled", true)
                || !string.IsNullOrWhiteSpace(condition) && condition != "career") continue;
            string type = ReadOptionalText(row, "improvementttype", string.Empty);
            string target = ReadOptionalText(row, "improvedname", string.Empty);
            bool addToRating = ReadOptionalBool(row, "addtorating", false);
            // Keep unprojected rating/attribute swaps fail-closed, including the
            // category/attribute effects which a Skill-only check would miss.
            bool relevant = type switch
            {
                "Skill" => Matches(target, dictionaryKey),
                "SkillGroup" => source.SkillGroup.Length != 0 && Matches(target, source.SkillGroup),
                "SkillBase" or "SkillLevel" => addToRating && Matches(target, dictionaryKey),
                "SkillGroupBase" or "SkillGroupLevel" => addToRating
                    && source.SkillGroup.Length != 0 && Matches(target, source.SkillGroup),
                "SkillCategory" => addToRating && Matches(target, source.SkillCategory),
                "SkillAttribute" or "SkillLinkedAttribute" => addToRating && Matches(target, source.DefaultAttribute),
                "SwapSkillAttribute" or "SwapSkillSpecAttribute" =>
                    ReadOptionalText(row, "target", string.Empty) == dictionaryKey,
                "EnhancedArticulation" => addToRating && source.SkillCategory == "Physical Active",
                _ => false
            };
            if (relevant) return true;
        }
        return false;
    }

    private static bool Matches(string target, string expected)
        => target.Length == 0 || string.Equals(target, expected, StringComparison.Ordinal);
}
