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
    string SelectedCustomDataInputsDigest,
    string RawMetatypesXmlDigest,
    string EffectiveMetatypesInputsDigest,
    IReadOnlyList<string> EnabledSourcebooks,
    int? MaxNumberMaxAttributesCreate,
    int? KarmaAttribute,
    bool? AlternateMetatypeAttributeKarma,
    bool? ReverseAttributePriorityOrder,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

/// <summary>
/// Strict source projection of the bounded SelectMetatypePriority prerequisites.
/// Heritage and Talent child choices are source-bound here because their nested
/// values are inputs to the Attribute pools. Unsupported children remain visible
/// as disabled options and can never enter a durable prerequisite draft.
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
        XDocument metatypesDocument,
        CharacterCreationPrerequisiteProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metatypesDocument);
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
                        metatypesDocument,
                        context.EnabledSourcebooks,
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
        XDocument metatypesDocument,
        IReadOnlyList<string> enabledSourcebooks,
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
        var projected = new CharacterCreationPriorityOptionProjection(
            categoryId,
            categoryName,
            rank,
            sourceId,
            names[0].Value,
            sumToTenValue,
            baseNormalAttributePoints,
            sourceNodeDigest,
            [$"priorities.xml#priority:{sourceId}"]);
        if (string.Equals(categoryId, CharacterCreationPriorityCategoryIds.Heritage, StringComparison.Ordinal))
        {
            if (!TryProjectHeritageOptions(
                    row,
                    sourceId,
                    metatypesDocument,
                    enabledSourcebooks,
                    out CharacterCreationPriorityHeritageOptionProjection[] heritageOptions))
            {
                return false;
            }
            projected = projected with { HeritageOptions = heritageOptions };
        }
        else if (string.Equals(categoryId, CharacterCreationPriorityCategoryIds.Talent, StringComparison.Ordinal))
        {
            if (!TryProjectTalentOptions(
                    row,
                    sourceId,
                    out CharacterCreationPriorityTalentOptionProjection[] talentOptions))
            {
                return false;
            }
            projected = projected with { TalentOptions = talentOptions };
        }

        option = projected;
        return true;
    }

    private static bool TryProjectHeritageOptions(
        XElement row,
        string prioritySourceId,
        XDocument metatypesDocument,
        IReadOnlyList<string> enabledSourcebooks,
        out CharacterCreationPriorityHeritageOptionProjection[] options)
    {
        options = [];
        XElement[] containers = row.Elements("metatypes").Take(2).ToArray();
        XElement[] sourceContainers = metatypesDocument.Root?.Elements("metatypes").Take(2).ToArray()
            ?? [];
        if (containers.Length != 1
            || containers[0].HasAttributes
            || sourceContainers.Length != 1
            || sourceContainers[0].HasAttributes)
        {
            return false;
        }

        var projected = new List<CharacterCreationPriorityHeritageOptionProjection>();
        int order = 0;
        foreach (XElement child in containers[0].Elements())
        {
            if (child.Name.NamespaceName.Length != 0
                || !string.Equals(child.Name.LocalName, "metatype", StringComparison.Ordinal)
                || child.HasAttributes
                || !TryReadNormalizedScalar(child, "name", out string metatypeName)
                || !TryReadRequiredNonNegativeInt(child, "value", out int specialPoints)
                || !TryReadOptionalInt(child, "karma", out int karmaCost))
            {
                return false;
            }

            XElement[] matches = sourceContainers[0].Elements("metatype")
                .Where(candidate => string.Equals(ReadScalar(candidate, "name"), metatypeName, StringComparison.Ordinal))
                .Take(2)
                .ToArray();
            CharacterCreationPriorityHeritageOptionProjection baseOption =
                ProjectHeritageChild(
                    child,
                    matches.Length == 1 ? matches[0] : null,
                    prioritySourceId,
                    metatypeName,
                    null,
                    specialPoints,
                    karmaCost,
                    order++,
                    enabledSourcebooks);
            projected.Add(baseOption);

            XElement[] metavariantContainers = child.Elements("metavariants").Take(2).ToArray();
            if (metavariantContainers.Length > 1
                || metavariantContainers.Any(container => container.HasAttributes))
            {
                return false;
            }
            foreach (XElement metavariant in metavariantContainers.SingleOrDefault()?.Elements()
                         ?? Enumerable.Empty<XElement>())
            {
                if (metavariant.Name.NamespaceName.Length != 0
                    || !string.Equals(metavariant.Name.LocalName, "metavariant", StringComparison.Ordinal)
                    || metavariant.HasAttributes
                    || !TryReadNormalizedScalar(metavariant, "name", out string metavariantName)
                    || !TryReadRequiredNonNegativeInt(metavariant, "value", out int variantPoints)
                    || !TryReadOptionalInt(metavariant, "karma", out int variantKarma))
                {
                    return false;
                }
                XElement[] sourceVariantMatches = matches.Length == 1
                    ? matches[0].Element("metavariants")?.Elements("metavariant")
                        .Where(candidate => string.Equals(
                            ReadScalar(candidate, "name"),
                            metavariantName,
                            StringComparison.Ordinal))
                        .Take(2)
                        .ToArray() ?? []
                    : [];
                projected.Add(ProjectHeritageChild(
                    metavariant,
                    sourceVariantMatches.Length == 1 ? sourceVariantMatches[0] : null,
                    prioritySourceId,
                    metatypeName,
                    metavariantName,
                    variantPoints,
                    variantKarma,
                    order++,
                    enabledSourcebooks));
            }
        }

        options = projected.ToArray();
        return options.Length > 0;
    }

    private static CharacterCreationPriorityHeritageOptionProjection ProjectHeritageChild(
        XElement priorityChild,
        XElement? sourceNode,
        string prioritySourceId,
        string metatypeName,
        string? metavariantName,
        int specialPoints,
        int karmaCost,
        int order,
        IReadOnlyList<string> enabledSourcebooks)
    {
        var blockers = new List<string>();
        string metatypeSourceId = string.Empty;
        string? metavariantSourceId = null;
        CharacterCreationMetatypeAttributeProjection[] attributes = [];
        bool halves = false;
        string sourceDigest = string.Empty;
        if (sourceNode is null
            || !TryReadNormalizedScalar(sourceNode, "id", out string rawSourceId)
            || !Guid.TryParseExact(rawSourceId, "D", out Guid parsedSourceId)
            || parsedSourceId == Guid.Empty
            || !TryReadAttributes(sourceNode, out attributes)
            || !TryReadEmptyMarker(sourceNode, "halveattributepoints", out halves))
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
        }
        else
        {
            sourceDigest = RawDigest(sourceNode.ToString(SaveOptions.DisableFormatting));
            if (!TryReadNormalizedScalar(sourceNode, "source", out string sourceBook)
                || !enabledSourcebooks.Contains(sourceBook, StringComparer.OrdinalIgnoreCase))
            {
                blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
            }
            if (metavariantName is null)
                metatypeSourceId = parsedSourceId.ToString("D");
            else
            {
                metavariantSourceId = parsedSourceId.ToString("D");
                // The parent metatype id is resolved independently to preserve both identities.
                XElement? parent = sourceNode.Parent?.Parent;
                if (parent is null
                    || !TryReadNormalizedScalar(parent, "id", out string parentId)
                    || !Guid.TryParseExact(parentId, "D", out Guid parsedParentId)
                    || parsedParentId == Guid.Empty)
                {
                    blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
                }
                else
                {
                    metatypeSourceId = parsedParentId.ToString("D");
                }
            }
        }

        // This bounded slice proves the modifier-free Human base row. Other
        // canonical rows remain visible but disabled until their qualities and
        // bonuses are projected as Attribute authority.
        if (metavariantName is not null
            || !string.Equals(metatypeName, "Human", StringComparison.Ordinal)
            || sourceNode?.HasAttributes == true
            || sourceNode?.Elements().Any(element => element.Name.NamespaceName.Length != 0
                                                     || !s_HeritageSourceFields.Contains(
                                                         element.Name.LocalName)) == true
            || sourceNode?.Elements("qualities").Any() == true
            || sourceNode?.Elements("bonus").Any(element => element.HasAttributes || element.Nodes().Any()) == true)
        {
            blockers.Add(CharacterCreationPrerequisiteBlockers.HeritageSelectionUnsupported);
        }

        string childDigest = RawDigest(priorityChild.ToString(SaveOptions.DisableFormatting));
        string selectionId = $"{prioritySourceId}:heritage:{order}";
        string[] anchors =
        [
            $"priorities.xml#priority:{prioritySourceId}:heritage:{order}",
            string.IsNullOrEmpty(metatypeSourceId)
                ? $"metatypes.xml#unresolved:{metatypeName}"
                : metavariantSourceId is null
                    ? $"metatypes.xml#metatype:{metatypeSourceId}"
                    : $"metatypes.xml#metatype:{metatypeSourceId}:metavariant:{metavariantSourceId}"
        ];
        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return new CharacterCreationPriorityHeritageOptionProjection(
            selectionId,
            metavariantName is null
                ? CharacterCreationPriorityChildKinds.Metatype
                : CharacterCreationPriorityChildKinds.Metavariant,
            metatypeSourceId,
            metavariantSourceId,
            metatypeName,
            metavariantName,
            specialPoints,
            karmaCost,
            halves,
            attributes,
            childDigest,
            sourceDigest,
            IsEnabled: normalized.Length == 0,
            Blockers: normalized,
            SourceAnchorIds: anchors);
    }

    private static bool TryProjectTalentOptions(
        XElement row,
        string prioritySourceId,
        out CharacterCreationPriorityTalentOptionProjection[] options)
    {
        options = [];
        XElement[] containers = row.Elements("talents").Take(2).ToArray();
        if (containers.Length != 1 || containers[0].HasAttributes)
            return false;

        var projected = new List<CharacterCreationPriorityTalentOptionProjection>();
        int order = 0;
        foreach (XElement talent in containers[0].Elements())
        {
            if (talent.Name.NamespaceName.Length != 0
                || !string.Equals(talent.Name.LocalName, "talent", StringComparison.Ordinal)
                || talent.HasAttributes
                || !TryReadNormalizedScalar(talent, "name", out string name)
                || !TryReadNormalizedScalar(talent, "value", out string value)
                || !TryReadOptionalNonNegativeInt(talent, "specialattribpoints", out int specialPoints)
                || !TryReadOptionalNullableNonNegativeInt(talent, "magic", out int? magic)
                || !TryReadOptionalNullableNonNegativeInt(talent, "resonance", out int? resonance)
                || !TryReadOptionalNullableNonNegativeInt(talent, "depth", out int? depth))
            {
                return false;
            }

            string[] qualities = talent.Element("qualities")?.Elements("quality")
                .Select(element => element.Value.Trim())
                .ToArray() ?? [];
            bool exactMundane = string.Equals(name, "Mundane", StringComparison.Ordinal)
                                && string.Equals(value, "Mundane", StringComparison.Ordinal)
                                && specialPoints == 0
                                && magic is null
                                && resonance is null
                                && depth is null
                                && qualities.Length == 0
                                && HasExactMundaneRestriction(talent)
                                && talent.Elements().All(element =>
                                    element.Name.NamespaceName.Length == 0
                                    && (element.Name.LocalName is "name" or "value" or "forbidden"));
            string[] blockers = exactMundane
                ? []
                : [CharacterCreationPrerequisiteBlockers.TalentSelectionUnsupported];
            string selectionId = $"{prioritySourceId}:talent:{order}";
            projected.Add(new CharacterCreationPriorityTalentOptionProjection(
                selectionId,
                name,
                value,
                specialPoints,
                magic,
                resonance,
                depth,
                qualities,
                RawDigest(talent.ToString(SaveOptions.DisableFormatting)),
                IsEnabled: exactMundane,
                Blockers: blockers,
                SourceAnchorIds: [$"priorities.xml#priority:{prioritySourceId}:talent:{order}"]));
            order++;
        }

        options = projected.ToArray();
        return options.Length > 0;
    }

    private static bool HasExactMundaneRestriction(XElement talent)
    {
        XElement[] forbidden = talent.Elements("forbidden").Take(2).ToArray();
        if (forbidden.Length != 1 || forbidden[0].HasAttributes)
            return false;
        XElement[] oneOf = forbidden[0].Elements("oneof").Take(2).ToArray();
        return oneOf.Length == 1
               && !oneOf[0].HasAttributes
               && oneOf[0].Elements().Count() == 1
               && TryReadNormalizedScalar(oneOf[0], "metatype", out string metatype)
               && string.Equals(metatype, "A.I.", StringComparison.Ordinal);
    }

    private static readonly (string Id, string Prefix)[] s_AttributeFields =
    [
        ("BOD", "bod"), ("AGI", "agi"), ("REA", "rea"), ("STR", "str"),
        ("CHA", "cha"), ("INT", "int"), ("LOG", "log"), ("WIL", "wil"),
        ("EDG", "edg"), ("MAG", "mag"), ("RES", "res"), ("ESS", "ess"),
        ("DEP", "dep")
    ];

    private static readonly IReadOnlySet<string> s_HeritageSourceFields = new HashSet<string>(
        new[]
        {
            "id", "name", "karma", "category", "inimin", "inimax", "iniaug",
            "walk", "run", "sprint", "qualities", "bonus", "source", "page",
            "metavariants", "halveattributepoints"
        }.Concat(s_AttributeFields.SelectMany(field => new[]
        {
            $"{field.Prefix}min", $"{field.Prefix}max", $"{field.Prefix}aug"
        })),
        StringComparer.Ordinal);

    private static bool TryReadAttributes(
        XElement sourceNode,
        out CharacterCreationMetatypeAttributeProjection[] attributes)
    {
        var result = new List<CharacterCreationMetatypeAttributeProjection>();
        foreach ((string id, string prefix) in s_AttributeFields)
        {
            if (!TryReadRequiredNonNegativeInt(sourceNode, $"{prefix}min", out int minimum)
                || !TryReadRequiredNonNegativeInt(sourceNode, $"{prefix}max", out int maximum)
                || !TryReadRequiredNonNegativeInt(sourceNode, $"{prefix}aug", out int augmented)
                || minimum > maximum
                || maximum > augmented)
            {
                attributes = [];
                return false;
            }
            result.Add(new CharacterCreationMetatypeAttributeProjection(id, minimum, maximum, augmented));
        }
        attributes = result.ToArray();
        return true;
    }

    private static bool TryReadEmptyMarker(XElement parent, string name, out bool present)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        present = matches.Length == 1;
        return matches.Length <= 1
               && matches.All(element => !element.HasAttributes
                                          && !element.HasElements
                                          && string.IsNullOrWhiteSpace(element.Value));
    }

    private static bool TryReadNormalizedScalar(XElement parent, string name, out string value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = matches.Length == 1 ? matches[0].Value : string.Empty;
        return matches.Length == 1
               && !matches[0].HasAttributes
               && !matches[0].HasElements
               && !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static bool TryReadRequiredNonNegativeInt(XElement parent, string name, out int value)
    {
        value = 0;
        return TryReadNormalizedScalar(parent, name, out string raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
               && value >= 0;
    }

    private static bool TryReadOptionalNonNegativeInt(XElement parent, string name, out int value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = 0;
        return matches.Length == 0 || matches.Length == 1
               && TryReadRequiredNonNegativeInt(parent, name, out value);
    }

    private static bool TryReadOptionalInt(XElement parent, string name, out int value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = 0;
        if (matches.Length == 0)
            return true;
        return matches.Length == 1
               && TryReadNormalizedScalar(parent, name, out string raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalNullableNonNegativeInt(
        XElement parent,
        string name,
        out int? value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = null;
        if (matches.Length == 0)
            return true;
        if (matches.Length != 1
            || !TryReadRequiredNonNegativeInt(parent, name, out int parsed))
            return false;
        value = parsed;
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
        authority = authority with
        {
            SelectedCustomDataInputsDigest = context.SelectedCustomDataInputsDigest,
            RawMetatypesXmlDigest = context.RawMetatypesXmlDigest,
            EffectiveMetatypesInputsDigest = context.EffectiveMetatypesInputsDigest,
            MaxNumberMaxAttributesCreate = context.MaxNumberMaxAttributesCreate,
            KarmaAttribute = context.KarmaAttribute,
            AlternateMetatypeAttributeKarma = context.AlternateMetatypeAttributeKarma,
            ReverseAttributePriorityOrder = context.ReverseAttributePriorityOrder
        };
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
