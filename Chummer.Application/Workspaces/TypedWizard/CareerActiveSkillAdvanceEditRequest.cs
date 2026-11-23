// Existing typed adapter from the paired Presentation snapshot; Core executes it
// only after store admission. Rules remain in Chummer.Contracts.Characters.
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces.TypedWizard;

internal sealed record CareerActiveSkillAdvanceEditorState(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> Skills,
    int OmittedSkillCount);

internal sealed record CareerActiveSkillAdvanceRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    CharacterCareerActiveSkillAdvanceQuote ExpectedSkill,
    string ExpectedRuleDigest,
    bool Confirmed,
    Guid ExpenseId,
    DateTime ExpenseDateLocal);

internal static class CareerActiveSkillAdvanceEditorProjector
{
    private const int DefaultMaximumActiveSkillRating = 12;

    public static CareerActiveSkillAdvanceEditorState Project(
        string xml,
        CharacterWorkspaceId workspaceId,
        long contentRevision,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver)
    {
        if (string.IsNullOrWhiteSpace(workspaceId.Value))
        {
            throw new InvalidOperationException(
                "A nonblank dossier identity is required for active-skill advancement.");
        }
        if (contentRevision <= 0)
        {
            throw new InvalidOperationException(
                "Dossier revision is unavailable. Reload before advancing a skill.");
        }

        (IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> skills, int omitted) =
            ProjectState(xml, settingsCatalogJson, sourceDataResolver);
        return new CareerActiveSkillAdvanceEditorState(
            workspaceId,
            contentRevision,
            skills,
            omitted);
    }

    internal static (
        IReadOnlyList<CharacterCareerActiveSkillAdvanceQuote> Skills,
        int OmittedSkillCount) ProjectState(
        string xml,
        string? settingsCatalogJson,
        ICharacterSourceDataResolver? sourceDataResolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = RequireCharacterRoot(document);
        if (!ReadRequiredBool(root, "created"))
        {
            throw new InvalidOperationException(
                "Active-skill advancement is available only for career runners.");
        }

        ICharacterSourceDataContext sourceContext = sourceDataResolver?
            .TryCreateContext(xml)
            ?? throw new InvalidOperationException(
                "The exact Chummer5 skill source profile is unavailable.");
        XElement settings = CareerSettingsProfileResolver.Resolve(root, settingsCatalogJson, sourceContext);
        CharacterCareerActiveSkillAdvanceSettings rules = new(
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmanewactiveskill"),
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmaimproveactiveskill"),
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmanewskillgroup"),
            ReadRequiredNonNegativeInt(settings, "karmacost", "karmaimproveskillgroup"),
            ReadOptionalBool(settings, "compensateskillgroupkarmadifference", false));
        int maximumRating = ReadOptionalNonNegativeInt(
            settings,
            "maxskillrating",
            DefaultMaximumActiveSkillRating);
        bool usePointsOnBrokenGroups = ReadOptionalBool(
            settings,
            "usepointsonbrokengroups",
            false);
        int availableKarma = ReadRequiredNonNegativeInt(root, "karma");
        XElement newSkills = RequireSingle(root, "newskills", "The saved runner must have one <newskills> container.");
        XElement skillContainer = RequireSingle(newSkills, "skills", "The saved runner must have one active <skills> container.");
        XElement[] improvementsContainers = root.Elements("improvements").Take(2).ToArray();
        if (improvementsContainers.Length > 1)
        {
            throw new InvalidOperationException("The saved runner has duplicate <improvements> containers.");
        }
        XElement? improvements = improvementsContainers.SingleOrDefault();
        string rawRuleState = CareerSettingsProfileResolver.RuleState(settings)
            + "\n"
            + (improvements?.ToString(SaveOptions.DisableFormatting) ?? "<improvements />");

        XElement[] savedSkills = skillContainer.Elements("skill").ToArray();
        HashSet<Guid> instanceIds = [];
        foreach (XElement savedSkill in savedSkills)
        {
            Guid id = ReadRequiredGuid(savedSkill, "guid", "An active skill");
            if (!instanceIds.Add(id))
            {
                throw new InvalidOperationException(
                    "The saved runner has duplicate active-skill instance GUIDs.");
            }
        }

        List<CharacterCareerActiveSkillAdvanceQuote> projected = [];
        Dictionary<string, CareerSkillRatingSourceProjection.Group?> groupCache = new(StringComparer.Ordinal);
        int omitted = 0;
        foreach (XElement savedSkill in savedSkills)
        {
            if (ReadRequiredBool(savedSkill, "isknowledge"))
            {
                throw new InvalidOperationException(
                    "The active <skills> container contains a knowledge skill.");
            }
            if (!TryProjectSkill(
                    root,
                    newSkills,
                    savedSkill,
                    availableKarma,
                    maximumRating,
                    usePointsOnBrokenGroups,
                    rules,
                    rawRuleState,
                    improvements,
                    sourceContext,
                    groupCache,
                    out CharacterCareerActiveSkillAdvanceQuote quote))
            {
                omitted++;
                continue;
            }
            projected.Add(quote);
        }

        return (
            projected
                .OrderBy(static candidate => candidate.Name, StringComparer.Ordinal)
                .ThenBy(static candidate => candidate.Identity.SkillId)
                .ToArray(),
            omitted);
    }

    private static bool TryProjectSkill(
        XElement root,
        XElement newSkills,
        XElement savedSkill,
        int availableKarma,
        int maximumRating,
        bool usePointsOnBrokenGroups,
        CharacterCareerActiveSkillAdvanceSettings rules,
        string rawRuleState,
        XElement? improvements,
        ICharacterSourceDataContext sourceContext,
        Dictionary<string, CareerSkillRatingSourceProjection.Group?> groupCache,
        out CharacterCareerActiveSkillAdvanceQuote quote)
    {
        quote = null!;
        Guid instanceId = ReadRequiredGuid(savedSkill, "guid", "An active skill");
        Guid sourceId = ReadRequiredGuid(savedSkill, "suid", "An active skill");
        if (!sourceContext.TryResolveActiveSkillSource(
                sourceId.ToString("D"),
                out CharacterActiveSkillSource source)
            || !Guid.TryParse(source.SourceSkillId, out Guid resolvedSourceId)
            || resolvedSourceId != sourceId)
        {
            return false;
        }

        string savedCategory = ReadRequiredText(savedSkill, "skillcategory", "An active skill");
        if (!string.Equals(savedCategory, source.SkillCategory, StringComparison.Ordinal))
        {
            return false;
        }

        string dictionaryKey = source.Name;
        string displayName = source.Name;
        if (source.IsExotic)
        {
            string specific = ReadRequiredText(savedSkill, "specific", "An exotic active skill");
            dictionaryKey = $"{source.Name} ({specific})";
            displayName = dictionaryKey;
        }

        if (CareerSkillRatingSourceProjection.HasUnsupportedRatingAuthority(improvements, source, dictionaryKey)
            || !CareerSkillRatingSourceProjection.TryReadPoints(savedSkill, improvements, dictionaryKey,
                false, out var points))
        {
            return false;
        }

        int basePoints = ReadRequiredNonNegativeInt(savedSkill, "base");
        int karmaPoints = ReadRequiredNonNegativeInt(savedSkill, "karma");
        CareerSkillRatingSourceProjection.Group? groupState = null;
        if (!string.IsNullOrWhiteSpace(source.SkillGroup))
        {
            if (!groupCache.TryGetValue(source.SkillGroup, out groupState))
            {
                XElement? group = ResolveSkillGroup(newSkills, source.SkillGroup);
                if (!CareerSkillRatingSourceProjection.TryReadGroup(group, source.SkillGroup,
                    newSkills.Element("skills")!.Elements("skill"), improvements, sourceContext, maximumRating,
                    out groupState)) groupState = null;
                // A projection-call-local cache: never reused across runner revisions/owners.
                groupCache[source.SkillGroup] = groupState;
            }
            if (groupState is null) return false;
            if (!groupState.Rating.KarmaUnbroken && groupState.Points.KarmaPoints > 0)
                return false; // One paid skill point could also reactivate paid group levels.
            if (rules.CompensateSkillGroupKarmaDifference)
            {
                // Exact peer Enabled/TotalBase authority also depends on movement, attribute,
                // SkillDisable and group-break state. Do not approximate that custom rule.
                return false;
            }
        }

        if (!CharacterCareerSkillRatingRules.TryProjectSkill(points, groupState?.Rating, usePointsOnBrokenGroups,
                maximumRating, out var rating)) return false;
        int totalBaseRating = rating.TotalBaseRating;
        if (totalBaseRating > maximumRating)
        {
            return false;
        }

        if (!TryResolveCostModifiers(
                improvements,
                dictionaryKey,
                savedCategory,
                out IReadOnlyList<CharacterCareerActiveSkillKarmaModifier> modifiers))
        {
            return false;
        }

        CharacterCareerActiveSkillAdvanceInput input = new(
            new CharacterCareerActiveSkillIdentity(instanceId, sourceId),
            Created: true,
            displayName,
            savedCategory,
            dictionaryKey,
            basePoints,
            karmaPoints,
            totalBaseRating,
            maximumRating,
            availableKarma,
            rules,
            OtherGroupMembers: [],
            modifiers,
            RawSourceState: source.RawSourceXml,
            rawRuleState);
        return CharacterCareerActiveSkillAdvanceRules.TryCreateQuote(input, out quote);
    }

    private static bool TryResolveCostModifiers(
        XElement? improvements,
        string dictionaryKey,
        string skillCategory,
        out IReadOnlyList<CharacterCareerActiveSkillKarmaModifier> modifiers)
    {
        List<CharacterCareerActiveSkillKarmaModifier> result = [];
        int ordinal = 0;
        foreach (XElement improvement in improvements?.Elements("improvement") ?? [])
        {
            string type = ReadOptionalText(improvement, "improvementttype", string.Empty);
            if (!TryMapModifierKind(type, out CharacterCareerActiveSkillKarmaModifierKind kind)
                || !IsEnabledCareerImprovement(improvement))
            {
                ordinal++;
                continue;
            }

            string target = ReadOptionalText(improvement, "improvedname", string.Empty);
            string expectedTarget = kind is CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost
                    or CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier
                ? dictionaryKey
                : skillCategory;
            if (!TargetMatches(target, expectedTarget))
            {
                ordinal++;
                continue;
            }
            if (!string.IsNullOrEmpty(ReadOptionalText(improvement, "unique", string.Empty)))
            {
                modifiers = [];
                return false;
            }
            if (!TryReadNonNegativeInt(improvement, "min", 0, out int minimum)
                || !TryReadNonNegativeInt(improvement, "max", 0, out int maximum)
                || maximum != 0 && maximum < minimum
                || !TryReadDecimal(improvement, "val", out decimal value))
            {
                modifiers = [];
                return false;
            }

            string raw = ordinal.ToString(CultureInfo.InvariantCulture)
                + "\0"
                + improvement.ToString(SaveOptions.DisableFormatting);
            string identity = Convert.ToHexString(
                    SHA256.HashData(Encoding.UTF8.GetBytes(raw)))
                .ToLowerInvariant();
            result.Add(new CharacterCareerActiveSkillKarmaModifier(
                identity,
                kind,
                target,
                minimum,
                maximum,
                value));
            ordinal++;
        }

        modifiers = result;
        return true;
    }

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

    private static bool TryMapModifierKind(
        string type,
        out CharacterCareerActiveSkillKarmaModifierKind kind)
    {
        switch (type)
        {
            case "ActiveSkillKarmaCost":
                kind = CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCost;
                return true;
            case "ActiveSkillKarmaCostMultiplier":
                kind = CharacterCareerActiveSkillKarmaModifierKind.ActiveSkillCostMultiplier;
                return true;
            case "SkillCategoryKarmaCost":
                kind = CharacterCareerActiveSkillKarmaModifierKind.SkillCategoryCost;
                return true;
            case "SkillCategoryKarmaCostMultiplier":
                kind = CharacterCareerActiveSkillKarmaModifierKind.SkillCategoryCostMultiplier;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TargetMatches(string target, string expected)
        => string.IsNullOrEmpty(target)
            || string.Equals(target, expected, StringComparison.Ordinal);


    private static XElement? ResolveSkillGroup(XElement newSkills, string groupName)
    {
        XElement[] groupContainers = newSkills.Elements("groups").Take(2).ToArray();
        if (groupContainers.Length > 1)
        {
            throw new InvalidOperationException("The saved runner has duplicate skill-group containers.");
        }
        XElement[] matches = groupContainers.SingleOrDefault()?.Elements("group")
            .Where(group => string.Equals(
                ReadOptionalText(group, "name", string.Empty),
                groupName,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray()
            ?? [];
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException(
                $"The saved runner has duplicate {groupName} skill-group rows.")
        };
    }

    internal static XElement RequireCharacterRoot(XDocument document)
        => document.Root is { } root && root.Name == XName.Get("character")
            ? root
            : throw new InvalidOperationException(
                "Workspace XML must use <character> as the root node.");

    internal static XElement RequireSingle(XElement parent, string name, string error)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(error);
    }

    internal static bool ReadRequiredBool(XElement parent, string name)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || !CareerSettingsProfileResolver.TryParseBoolean(values[0].Value.Trim(), out bool value))
        {
            throw new InvalidOperationException(
                $"The saved runner has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    internal static Guid ReadRequiredGuid(XElement parent, string name, string owner)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1
            || !Guid.TryParse(values[0].Value.Trim(), out Guid value)
            || value == Guid.Empty)
        {
            throw new InvalidOperationException(
                $"{owner} has an invalid or duplicate <{name}> value.");
        }
        return value;
    }

    internal static string ReadRequiredText(XElement parent, string name, string owner)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        if (values.Length != 1 || string.IsNullOrWhiteSpace(values[0].Value))
        {
            throw new InvalidOperationException(
                $"{owner} has an invalid or duplicate <{name}> value.");
        }
        return values[0].Value.Trim();
    }

    internal static int ReadRequiredNonNegativeInt(XElement parent, string name)
        => TryReadNonNegativeInt(parent, name, fallback: -1, out int value) && value >= 0
            ? value
            : throw new InvalidOperationException(
                $"The saved runner has an invalid or duplicate <{name}> value.");

    private static int ReadRequiredNonNegativeInt(
        XElement parent,
        string containerName,
        string name)
    {
        XElement container = RequireSingle(
            parent,
            containerName,
            $"The active settings profile must have one <{containerName}> container.");
        return ReadRequiredNonNegativeInt(container, name);
    }

    private static int ReadOptionalNonNegativeInt(XElement parent, string name, int fallback)
        => TryReadNonNegativeInt(parent, name, fallback, out int value)
            ? value
            : throw new InvalidOperationException(
                $"The active settings profile has an invalid or duplicate <{name}> value.");

    private static bool TryReadNonNegativeInt(
        XElement parent,
        string name,
        int fallback,
        out int value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = fallback;
        if (values.Length == 0 || values.Length == 1 && string.IsNullOrWhiteSpace(values[0].Value))
        {
            return fallback >= 0;
        }
        return values.Length == 1
            && int.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value)
            && value >= 0;
    }

    private static bool TryReadDecimal(XElement parent, string name, out decimal value)
    {
        XElement[] values = parent.Elements(name).Take(2).ToArray();
        value = 0m;
        return values.Length == 1
            && decimal.TryParse(
                values[0].Value.Trim(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    internal static bool ReadOptionalBool(XElement parent, string name, bool fallback)
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

    internal static string ReadOptionalText(XElement parent, string name, string fallback)
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
