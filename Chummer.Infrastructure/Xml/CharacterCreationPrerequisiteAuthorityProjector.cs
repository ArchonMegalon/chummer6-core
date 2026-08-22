using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Chummer.Contracts.Characters;

namespace Chummer.Infrastructure.Xml;

internal sealed record CharacterCreationPrerequisiteProjectionContext(
    string SettingsProfileId,
    string BuildMethod,
    int? CreationKarmaTotal,
    IReadOnlyList<string> PriorityArray,
    string PriorityTable,
    int? SumToTenTarget,
    string RawProfileInputsDigest,
    string RawPrioritiesXmlDigest,
    string EffectivePrioritiesInputsDigest,
    string SelectedPriorityCustomDataInputsDigest,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Strict source projection of the bounded SelectMetatypePriority prerequisites.
/// It models only rank allocation and the raw Attribute-row grant; metatype
/// adjustments and Talent subchoices remain later authorities.
/// </summary>
internal static class CharacterCreationPrerequisiteAuthorityProjector
{
    private static readonly IReadOnlyDictionary<string, string> s_CategoryIds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Heritage"] = CharacterCreationPriorityCategoryIds.Heritage,
            ["Talent"] = CharacterCreationPriorityCategoryIds.Talent,
            ["Attributes"] = CharacterCreationPriorityCategoryIds.Attributes,
            ["Skills"] = CharacterCreationPriorityCategoryIds.Skills,
            ["Resources"] = CharacterCreationPriorityCategoryIds.Resources
        };

    private static readonly IReadOnlyDictionary<string, int> s_LegacyDefaultWeights =
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["A"] = 4,
            ["B"] = 3,
            ["C"] = 2,
            ["D"] = 1,
            ["E"] = 0
        };

    public static CharacterCreationPrerequisiteAuthority Project(
        XDocument document,
        CharacterCreationPrerequisiteProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(context);
        var blockers = new List<string>(context.Blockers);
        XElement? root = document.Root;
        XElement[] categoryContainers = root?.Elements("categories").Take(2).ToArray() ?? [];
        XElement[] priorityContainers = root?.Elements("priorities").Take(2).ToArray() ?? [];
        XElement[] weightContainers = root?.Elements("priortysumtotenvalues").Take(2).ToArray()
            ?? [];
        if (root is null
            || root.Name.NamespaceName.Length != 0
            || !string.Equals(root.Name.LocalName, "chummer", StringComparison.Ordinal)
            || categoryContainers.Length != 1
            || priorityContainers.Length != 1
            || weightContainers.Length > 1
            || categoryContainers[0].HasAttributes
            || priorityContainers[0].HasAttributes
            || weightContainers.Any(container => container.HasAttributes))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityCategoriesInvalid);
            return Complete(context, [], [], blockers);
        }

        Dictionary<string, string>? categories = ReadCategories(categoryContainers[0]);
        if (categories is null)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityCategoriesInvalid);
            return Complete(context, [], [], blockers);
        }

        Dictionary<string, (int Value, string Anchor)>? weights = ReadWeights(
            weightContainers.SingleOrDefault());
        if (weights is null)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityWeightsInvalid);
            return Complete(context, [], [], blockers);
        }
        foreach (string rank in context.PriorityArray.Distinct(StringComparer.Ordinal))
        {
            if (!weights.ContainsKey(rank))
            {
                // SelectMetatypePriority adds settings-only ranks at zero.
                weights.Add(
                    rank,
                    (0, $"settings.xml#setting:{context.SettingsProfileId}:priorityarray:{rank}"));
            }
        }

        CharacterCreationPriorityRankWeight[] rankWeights = weights
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new CharacterCreationPriorityRankWeight(
                item.Key,
                item.Value.Value,
                [item.Value.Anchor]))
            .ToArray();
        XElement[] sourceRows = priorityContainers[0].Elements().ToArray();
        if (sourceRows.Any(row => row.Name.NamespaceName.Length != 0
                                  || !string.Equals(
                                      row.Name.LocalName,
                                      "priority",
                                      StringComparison.Ordinal)))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);
            return Complete(context, rankWeights, [], blockers);
        }

        var options = new List<CharacterCreationPriorityOptionProjection>();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (string categoryId in CharacterCreationPriorityCategoryIds.Ordered)
        {
            string categoryName = categories.Single(item => string.Equals(
                    item.Value,
                    categoryId,
                    StringComparison.Ordinal))
                .Key;
            foreach (string rank in context.PriorityArray.Distinct(StringComparer.Ordinal))
            {
                XElement[] tableMatches = sourceRows.Where(row =>
                        string.Equals(ReadScalar(row, "category"), categoryName, StringComparison.Ordinal)
                        && string.Equals(ReadScalar(row, "value"), rank, StringComparison.Ordinal)
                        && string.Equals(
                            ReadScalar(row, "prioritytable"),
                            context.PriorityTable,
                            StringComparison.Ordinal))
                    .Take(2)
                    .ToArray();
                XElement[] matches = tableMatches.Length == 0
                    ? sourceRows.Where(row =>
                            string.Equals(ReadScalar(row, "category"), categoryName, StringComparison.Ordinal)
                            && string.Equals(ReadScalar(row, "value"), rank, StringComparison.Ordinal)
                            && row.Elements("prioritytable").Count() == 0)
                        .Take(2)
                        .ToArray()
                    : tableMatches;
                if (matches.Length != 1
                    || !TryProjectOption(
                        matches[0],
                        categoryId,
                        categoryName,
                        rank,
                        weights[rank].Value,
                        sourceIds,
                        out CharacterCreationPriorityOptionProjection? option))
                {
                    blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);
                    continue;
                }

                options.Add(option!);
            }
        }

        int expectedOptionCount = CharacterCreationPriorityCategoryIds.Ordered.Count
                                  * context.PriorityArray.Distinct(StringComparer.Ordinal).Count();
        if (options.Count != expectedOptionCount)
            blockers.Add(CharacterCreationPrerequisiteBlockers.PriorityRowsInvalid);
        return Complete(context, rankWeights, options, blockers);
    }

    private static Dictionary<string, string>? ReadCategories(XElement container)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (XElement category in container.Elements())
        {
            if (category.Name.NamespaceName.Length != 0
                || !string.Equals(category.Name.LocalName, "category", StringComparison.Ordinal)
                || category.HasAttributes
                || category.HasElements
                || !string.Equals(
                    category.Value,
                    category.Value.Trim(),
                    StringComparison.Ordinal)
                || !s_CategoryIds.TryGetValue(category.Value, out string? categoryId)
                || !result.TryAdd(category.Value, categoryId))
            {
                return null;
            }
        }

        return result.Count == s_CategoryIds.Count
               && result.Values.SequenceEqual(
                   CharacterCreationPriorityCategoryIds.Ordered,
                   StringComparer.Ordinal)
            ? result
            : null;
    }

    private static Dictionary<string, (int Value, string Anchor)>? ReadWeights(
        XElement? container)
    {
        var result = new Dictionary<string, (int, string)>(StringComparer.Ordinal);
        if (container is null || !container.Elements().Any())
        {
            foreach ((string rank, int value) in s_LegacyDefaultWeights)
            {
                result.Add(rank, (value, $"legacy-default:priorities.xml:{rank}"));
            }
            return result;
        }

        foreach (XElement weight in container.Elements())
        {
            string rank = weight.Name.LocalName;
            if (weight.Name.NamespaceName.Length != 0
                || !IsRank(rank)
                || weight.HasAttributes
                || weight.HasElements
                || !string.Equals(weight.Value, weight.Value.Trim(), StringComparison.Ordinal)
                || !int.TryParse(
                    weight.Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int value)
                || value < 0
                || !result.TryAdd(
                    rank,
                    (value, $"priorities.xml#priortysumtotenvalues:{rank}")))
            {
                return null;
            }
        }

        return result;
    }

    private static bool TryProjectOption(
        XElement row,
        string categoryId,
        string categoryName,
        string rank,
        int sumToTenValue,
        ISet<string> sourceIds,
        out CharacterCreationPriorityOptionProjection? option)
    {
        option = null;
        XElement[] ids = row.Elements("id").Take(2).ToArray();
        XElement[] names = row.Elements("name").Take(2).ToArray();
        XElement[] values = row.Elements("value").Take(2).ToArray();
        XElement[] categories = row.Elements("category").Take(2).ToArray();
        XElement[] tables = row.Elements("prioritytable").Take(2).ToArray();
        if (row.HasAttributes
            || ids.Length != 1
            || names.Length != 1
            || values.Length != 1
            || categories.Length != 1
            || tables.Length > 1
            || new[] { ids[0], names[0], values[0], categories[0] }
                .Concat(tables)
                .Any(element => element.HasAttributes
                                || element.HasElements
                                || !string.Equals(
                                    element.Value,
                                    element.Value.Trim(),
                                    StringComparison.Ordinal))
            || !Guid.TryParseExact(ids[0].Value, "D", out Guid parsedId)
            || parsedId == Guid.Empty
            || string.IsNullOrWhiteSpace(names[0].Value)
            || !string.Equals(values[0].Value, rank, StringComparison.Ordinal)
            || !string.Equals(categories[0].Value, categoryName, StringComparison.Ordinal))
        {
            return false;
        }

        string sourceId = parsedId.ToString("D");
        if (!sourceIds.Add(sourceId))
            return false;
        int? baseNormalAttributePoints = null;
        XElement[] attributes = row.Elements("attributes").Take(2).ToArray();
        if (string.Equals(
                categoryId,
                CharacterCreationPriorityCategoryIds.Attributes,
                StringComparison.Ordinal))
        {
            if (attributes.Length != 1
                || attributes[0].HasAttributes
                || attributes[0].HasElements
                || !string.Equals(
                    attributes[0].Value,
                    attributes[0].Value.Trim(),
                    StringComparison.Ordinal)
                || !int.TryParse(
                    attributes[0].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int parsedAttributes)
                || parsedAttributes < 0)
            {
                return false;
            }
            baseNormalAttributePoints = parsedAttributes;
        }
        else if (attributes.Length != 0)
        {
            return false;
        }

        string sourceNodeDigest = RawDigest(row.ToString(SaveOptions.DisableFormatting));
        option = new CharacterCreationPriorityOptionProjection(
            categoryId,
            categoryName,
            rank,
            sourceId,
            names[0].Value,
            sumToTenValue,
            baseNormalAttributePoints,
            sourceNodeDigest,
            [$"priorities.xml#priority:{sourceId}"]);
        return true;
    }

    private static CharacterCreationPrerequisiteAuthority Complete(
        CharacterCreationPrerequisiteProjectionContext context,
        IReadOnlyList<CharacterCreationPriorityRankWeight> weights,
        IReadOnlyList<CharacterCreationPriorityOptionProjection> options,
        IEnumerable<string> blockers)
    {
        string[] normalizedBlockers = blockers
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var authority = new CharacterCreationPrerequisiteAuthority(
            CharacterCreationPrerequisiteSchemas.AuthorityV1,
            context.SettingsProfileId,
            context.BuildMethod,
            context.CreationKarmaTotal,
            context.PriorityArray.ToArray(),
            context.PriorityTable,
            context.SumToTenTarget,
            weights.ToArray(),
            options.ToArray(),
            context.RawProfileInputsDigest,
            context.RawPrioritiesXmlDigest,
            context.EffectivePrioritiesInputsDigest,
            context.SelectedPriorityCustomDataInputsDigest,
            context.SourceAnchorIds.Distinct(StringComparer.Ordinal).ToArray(),
            normalizedBlockers,
            normalizedBlockers.Length == 0,
            string.Empty);
        return authority with
        {
            AuthorityDigest = CharacterCreationPrerequisiteAuthorityDigest.Compute(authority)
        };
    }

    private static string ReadScalar(XElement row, string name)
    {
        XElement[] values = row.Elements(name).Take(2).ToArray();
        return values.Length == 1
               && !values[0].HasAttributes
               && !values[0].HasElements
            ? values[0].Value.Trim()
            : string.Empty;
    }

    private static bool IsRank(string value) =>
        value.Length == 1 && value[0] is >= 'A' and <= 'Z';

    private static string RawDigest(string value) =>
        "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

}
