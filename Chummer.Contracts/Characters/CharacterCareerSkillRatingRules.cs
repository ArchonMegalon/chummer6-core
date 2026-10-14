using System.Globalization;
using System.Xml.Linq;

namespace Chummer.Contracts.Characters;

/// <summary>Saved allocations and retained improvements are separate quantities.
/// Advancing changes KarmaPoints, never the free contribution.</summary>
public sealed record CharacterCareerSkillRatingPoints(
    int BasePoints, int KarmaPoints, int FreeBase, int FreeLevels);

public sealed record CharacterCareerSkillFreePoints(int Base, int Levels);

public sealed record CharacterCareerSkillGroupRating(
    int Base, int Karma, bool KarmaUnbroken)
{
    public int Rating => Base + Karma;
}

public sealed record CharacterCareerSkillRating(int Base, int Karma)
{
    public int TotalBaseRating => Base + Karma;
}

/// <summary>
/// Career learned-rating projection from Chummer5 Skill.Base/Karma and
/// SkillGroup.Base/Karma/KarmaUnbroken. This is not a dice-pool projection or
/// mutation permission. Callers must separately admit exact source identities,
/// rating modifiers, enabled members, costs, owner and stored revision.
/// </summary>
public static class CharacterCareerSkillRatingRules
{
    private const int Maximum = CharacterCareerActiveSkillAdvanceRules.MaximumRating;
    private static readonly string[] UnsupportedTextFields = ["unique", "uniquename", "exclude", "target"];
    private static readonly string[] ZeroOnlyFields = ["min", "max", "aug", "augmax"];

    /// <summary>
    /// Reads the Career-applicable, non-unique integer grants used by native Life
    /// Modules. ValueOf(..., false, exactName) does not match a blank target and
    /// accepts empty or exact "career" conditions and excludes add-to-rating
    /// entries, matching ImprovementManager.ValueOf in Career. Complex stacking remains
    /// unavailable instead of silently assigning a lower advancement price.
    /// </summary>
    public static bool TryReadFreePoints(XElement? improvements, string exactTarget,
        bool group, out CharacterCareerSkillFreePoints points)
    {
        points = new(0, 0);
        if (string.IsNullOrWhiteSpace(exactTarget) || exactTarget.Length > 512
            || improvements is not null && improvements.Name != "improvements") return false;
        int freeBase = 0;
        int freeLevels = 0;
        string baseType = group ? "SkillGroupBase" : "SkillBase";
        string levelType = group ? "SkillGroupLevel" : "SkillLevel";
        foreach (XElement row in improvements?.Elements("improvement") ?? [])
        {
            // Check every possible type/target before selecting a row: duplicate
            // elements must not hide a relevant grant behind an unrelated first one.
            if (!row.Elements("improvementttype").Any(e => e.Value == baseType || e.Value == levelType)
                || !row.Elements("improvedname").Any(e => e.Value == exactTarget)) continue;
            if (row.Elements().GroupBy(e => e.Name).Any(g => g.Count() != 1)
                || !TryBoolean(row, "enabled", true, out bool enabled)) return false;
            if (!enabled) continue;
            if (!TryBoolean(row, "addtorating", false, out bool addToRating)) return false;
            string? condition = row.Element("condition")?.Value;
            if (addToRating || !string.IsNullOrEmpty(condition) && condition != "career") continue;
            if (UnsupportedTextFields
                    .Any(name => !string.IsNullOrEmpty(row.Element(name)?.Value))
                || ZeroOnlyFields
                    .Any(name => row.Element(name) is { } value && value.Value != "0")
                || !int.TryParse(row.Element("val")?.Value, NumberStyles.None,
                    CultureInfo.InvariantCulture, out int grant) || grant > Maximum) return false;
            if (row.Element("improvementttype")!.Value == baseType) freeBase += grant;
            else freeLevels += grant;
            if (freeBase > Maximum || freeLevels > Maximum) return false;
        }
        points = new(freeBase, freeLevels);
        return true;
    }

    public static bool TryProjectGroup(CharacterCareerSkillRatingPoints? points,
        IReadOnlyList<CharacterCareerSkillRatingPoints>? members, bool disabled,
        int maximumRating, out CharacterCareerSkillGroupRating rating)
    {
        rating = new(0, 0, false);
        if (!Valid(points) || members is null || members.Count > Maximum
            || members.Any(member => !Valid(member)) || maximumRating is < 0 or > Maximum) return false;
        if (disabled) return true;
        int highestBase = members.Count == 0 ? 0 : members.Max(m => m.BasePoints + m.FreeBase);
        bool karmaUnbroken = members.Count != 0 && members.All(m =>
            m.BasePoints + m.FreeBase + m.KarmaPoints + m.FreeLevels >= highestBase);
        rating = new(Math.Min(points!.BasePoints + points.FreeBase, maximumRating),
            Math.Min((karmaUnbroken ? points.KarmaPoints : 0) + points.FreeLevels, maximumRating),
            karmaUnbroken);
        return true;
    }

    public static bool TryProjectSkill(CharacterCareerSkillRatingPoints? points,
        CharacterCareerSkillGroupRating? group, bool usePointsOnBrokenGroups,
        int maximumRating, out CharacterCareerSkillRating rating)
    {
        rating = new(0, 0);
        if (!Valid(points) || maximumRating is < 0 or > Maximum
            || group is { Base: < 0 or > Maximum } or { Karma: < 0 or > Maximum }) return false;
        int groupBase = group?.Base ?? 0;
        int basePoints = groupBase > 0 && !usePointsOnBrokenGroups ? 0 : points!.BasePoints;
        rating = new(Math.Min(groupBase + basePoints + points!.FreeBase, maximumRating),
            Math.Min((group?.Karma ?? 0) + points.KarmaPoints + points.FreeLevels, maximumRating));
        return true;
    }

    private static bool Valid(CharacterCareerSkillRatingPoints? points) => points is
    {
        BasePoints: >= 0 and <= Maximum, KarmaPoints: >= 0 and <= Maximum,
        FreeBase: >= 0 and <= Maximum, FreeLevels: >= 0 and <= Maximum
    };

    private static bool TryBoolean(XElement row, string name, bool fallback, out bool value)
    {
        string? text = row.Element(name)?.Value;
        value = fallback;
        if (text is null) return true;
        if (text is "1" or "true" or "True") { value = true; return true; }
        if (text is "0" or "false" or "False") { value = false; return true; }
        return false;
    }
}
