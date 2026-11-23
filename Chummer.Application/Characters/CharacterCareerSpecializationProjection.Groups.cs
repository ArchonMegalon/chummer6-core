using System.Xml.Linq;
using static Chummer.Application.Characters.SpecializationXmlReader;

namespace Chummer.Application.Characters;

internal static class SpecializationGroupProjection
{
    internal static bool IsSkillDisabled(
        XElement? improvements,
        string skillName,
        string category)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ((ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            skillName))
                    || (ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillCategoryDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            category))));

    internal static bool IsGroupDisabled(
        XElement? improvements,
        string groupName,
        IReadOnlyCollection<string> categories)
        => (improvements?.Elements("improvement") ?? [])
            .Any(improvement => IsEnabledInCareer(improvement)
                && ((ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillGroupDisable"
                        && TargetMatches(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            groupName))
                    || (ReadOptionalText(improvement, "improvementttype", string.Empty) == "SkillGroupCategoryDisable"
                        && categories.Contains(
                            ReadOptionalText(improvement, "improvedname", string.Empty),
                            StringComparer.Ordinal))));

    private static bool IsEnabledInCareer(XElement improvement)
    {
        string condition = ReadOptionalText(improvement, "condition", string.Empty);
        return ReadOptionalBool(improvement, "enabled", true)
            && (string.IsNullOrEmpty(condition)
                || string.Equals(condition, "career", StringComparison.Ordinal));
    }

    private static bool TargetMatches(string target, string expected)
        => string.IsNullOrEmpty(target)
            || string.Equals(target, expected, StringComparison.Ordinal);


}
