using System.Xml.Linq;
namespace Chummer.Application.Workspaces.TypedWizard;
internal static class CareerSkillGroupAdvanceEditorProjector
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
    private static bool IsEnabledCareerImprovement(XElement improvement)
        => IsEnabledInCareer(improvement)
            && !ReadOptionalBool(improvement, "addtorating", false);
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
    private static bool ReadOptionalBool(XElement parent, string name, bool fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length == 0 || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback;
        }
        if (values.Length != 1 || !CareerSettingsProfileResolver.TryParseBoolean(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException(
                $"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return value;
    }
    private static string ReadOptionalText(XElement parent, string name, string fallback)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        return values.Length switch
        {
            0 => fallback,
            1 => values[0].Value.Trim(),
            _ => throw new InvalidOperationException(
                $"The saved runner has duplicate <{name}> values.")
        };
    }
}
