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
    string RawSkillsXmlDigest,
    string EffectiveSkillsInputsDigest,
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
        XDocument skillsDocument,
        CharacterCreationPrerequisiteProjectionContext context)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(metatypesDocument);
        ArgumentNullException.ThrowIfNull(skillsDocument);
        ArgumentNullException.ThrowIfNull(context);
        var blockers = new List<string>(context.Blockers);
        TalentSkillCatalog? talentSkillCatalog = TryProjectTalentSkillCatalog(
            skillsDocument,
            context.EffectiveSkillsInputsDigest,
            out TalentSkillCatalog? projectedSkillCatalog)
            ? projectedSkillCatalog
            : null;
        if (talentSkillCatalog is null)
            blockers.Add(CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported);
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
                        talentSkillCatalog,
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
        TalentSkillCatalog? talentSkillCatalog,
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
                    talentSkillCatalog,
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

    private static bool TryProjectActiveSkillGrant(
        XElement talent,
        TalentSkillCatalog? catalog,
        string talentAnchor,
        out CharacterCreationTalentActiveSkillGrantProjection? projection,
        out bool branchPresent)
    {
        projection = null;
        XElement[] quantityNodes = talent.Elements("skillqty").Take(2).ToArray();
        XElement[] ratingNodes = talent.Elements("skillval").Take(2).ToArray();
        XElement[] typeNodes = talent.Elements("skilltype").Take(2).ToArray();
        XElement[] choiceContainers = talent.Elements("skillchoices").Take(2).ToArray();
        branchPresent = quantityNodes.Length != 0
                        || ratingNodes.Length != 0
                        || typeNodes.Length != 0
                        || choiceContainers.Length != 0;
        if (!branchPresent)
            return true;
        if (quantityNodes.Length == 0)
            return true;
        if (quantityNodes.Length != 1)
            return false;
        if (!TryReadIntScalar(talent, "skillqty", out int sourceQuantity)
            || sourceQuantity <= 0)
        {
            // SelectMetatypePriority opens no prompt for a non-positive or
            // unparseable quantity; the Talent row itself remains usable.
            return true;
        }
        if (ratingNodes.Length != 1
            || !TryReadRequiredNonNegativeInt(talent, "skillval", out int rating)
            || !TryReadSkillType(talent, out string rawSkillType, out string skillTypeQuery)
            || !TryReadSpecificSkillChoices(
                talent,
                out string[] sourceSpecificSkillChoiceNames))
        {
            return false;
        }
        int quantity = Math.Min(
            sourceQuantity,
            CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
        string skillType = rawSkillType.ToLowerInvariant();

        bool exactXPath = string.Equals(
            skillTypeQuery,
            CharacterCreationTalentSkillGrantTypes.PinnedXPathPredicate,
            StringComparison.Ordinal);
        bool simpleType = skillType is CharacterCreationTalentSkillGrantTypes.Active
            or CharacterCreationTalentSkillGrantTypes.Default
            or CharacterCreationTalentSkillGrantTypes.Magic
            or CharacterCreationTalentSkillGrantTypes.Resonance
            or CharacterCreationTalentSkillGrantTypes.Matrix;
        bool specificType = string.Equals(
            skillType,
            CharacterCreationTalentSkillGrantTypes.Specific,
            StringComparison.Ordinal);
        string[] specificSkillChoiceNames = specificType
            ? sourceSpecificSkillChoiceNames
            : [];
        bool validSimpleRule = simpleType && string.IsNullOrEmpty(skillTypeQuery);
        bool validSpecificRule = specificType && string.IsNullOrEmpty(skillTypeQuery);
        bool validXPathRule = string.Equals(
                                  skillType,
                                  CharacterCreationTalentSkillGrantTypes.XPath,
                                  StringComparison.Ordinal)
                              && exactXPath;
        bool allSpecificChoicesResolved = catalog is not null;
        var selectedSkills = new List<TalentActiveSkillDefinition>();
        if (catalog is not null)
        {
            if (validSpecificRule && specificSkillChoiceNames.Length > 0)
            {
                foreach (string requestedName in specificSkillChoiceNames)
                {
                    TalentActiveSkillDefinition[] matches = catalog.ActiveSkills
                        .Where(skill => string.Equals(
                            skill.CanonicalName,
                            requestedName,
                            StringComparison.Ordinal))
                        .Take(2)
                        .ToArray();
                    if (matches.Length != 1)
                    {
                        allSpecificChoicesResolved = false;
                        continue;
                    }
                    selectedSkills.Add(matches[0]);
                }
            }
            else if (validSpecificRule)
            {
                selectedSkills.AddRange(catalog.ActiveSkills
                    .OrderBy(skill => skill.CanonicalName, StringComparer.Ordinal)
                    .ThenBy(skill => skill.SourceId, StringComparer.Ordinal));
            }
            else if (validSimpleRule || validXPathRule)
            {
                selectedSkills.AddRange(catalog.ActiveSkills
                    .Where(skill => IsEligibleActiveSkill(
                        skillType,
                        exactXPath,
                        skill))
                    .OrderBy(skill => skill.CanonicalName, StringComparer.Ordinal)
                    .ThenBy(skill => skill.SourceId, StringComparer.Ordinal));
            }
        }
        CharacterCreationTalentActiveSkillChoiceProjection[] options = catalog is null
            ? []
            : selectedSkills.Select(skill => ProjectActiveSkillChoice(skill, catalog.SourceDigest))
                .ToArray();
        bool supportedType = validSimpleRule
                             || (validSpecificRule && allSpecificChoicesResolved)
                             || validXPathRule;
        bool supported = catalog is not null
                         && supportedType
                         && options.Count(option => option.IsEnabled) >= quantity;
        string[] blockers = supported
            ? []
            : [CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported];
        string sourceDigest = catalog?.SourceDigest ?? string.Empty;
        projection = new CharacterCreationTalentActiveSkillGrantProjection(
            quantity,
            rating,
            skillType,
            options,
            CharacterCreationTalentGrantAuthorityDigest.ComputeActiveGrant(
                quantity,
                rating,
                skillType,
                sourceDigest,
                options.Select(option => option.SelectionId)),
            supported,
            blockers,
            [talentAnchor, "skills.xml"])
        {
            SkillTypeQuery = skillTypeQuery,
            SpecificSkillChoiceNames = specificSkillChoiceNames
        };
        return true;
    }

    private static CharacterCreationTalentActiveSkillChoiceProjection ProjectActiveSkillChoice(
        TalentActiveSkillDefinition skill,
        string sourceDigest) =>
        new(
            SelectionId: skill.SourceId,
            SourceId: skill.SourceId,
            CanonicalName: skill.CanonicalName,
            Category: skill.Category,
            SkillGroup: skill.SkillGroup,
            SourceNodeDigest: skill.SourceNodeDigest,
            SkillsSourceDigest: sourceDigest,
            SourceAnchorIds: [$"skills.xml#skill:{skill.SourceId}"])
        {
            Attribute = skill.Attribute,
            IsExotic = skill.IsExotic,
            IsEnabled = !skill.IsExotic,
            Blockers = skill.IsExotic
                ? [CharacterCreationPrerequisiteBlockers
                    .TalentExoticSkillSpecializationRequired]
                : []
        };

    private static bool IsEligibleActiveSkill(
        string skillType,
        bool exactXPath,
        TalentActiveSkillDefinition skill) =>
        skillType switch
        {
            CharacterCreationTalentSkillGrantTypes.Active => true,
            CharacterCreationTalentSkillGrantTypes.Default => true,
            CharacterCreationTalentSkillGrantTypes.Magic => skill.Category is
                "Magical Active" or "Pseudo-Magical Active",
            CharacterCreationTalentSkillGrantTypes.Resonance => string.Equals(
                    skill.Category,
                    "Resonance Active",
                    StringComparison.Ordinal)
                || skill.SkillGroup is "Cracking" or "Electronics",
            CharacterCreationTalentSkillGrantTypes.Matrix => skill.SkillGroup is
                "Cracking" or "Electronics",
            CharacterCreationTalentSkillGrantTypes.XPath when exactXPath =>
                skill.Attribute is not ("RES" or "DEP")
                && (!string.Equals(
                        skill.Category,
                        "Magical Active",
                        StringComparison.Ordinal)
                    || string.IsNullOrEmpty(skill.SkillGroup)),
            _ => false
        };

    private static bool TryReadSkillType(
        XElement talent,
        out string skillType,
        out string skillTypeQuery)
    {
        skillType = string.Empty;
        skillTypeQuery = string.Empty;
        XElement[] matches = talent.Elements("skilltype").Take(2).ToArray();
        if (matches.Length == 0)
        {
            skillType = CharacterCreationTalentSkillGrantTypes.Default;
            return true;
        }
        if (matches.Length != 1
            || matches[0].HasElements
            || string.IsNullOrWhiteSpace(matches[0].Value)
            || !string.Equals(
                matches[0].Value,
                matches[0].Value.Trim(),
                StringComparison.Ordinal))
        {
            return false;
        }
        XAttribute[] attributes = matches[0].Attributes().Take(2).ToArray();
        if (attributes.Length > 1
            || attributes.Any(attribute => attribute.Name.NamespaceName.Length != 0
                                           || !string.Equals(
                                               attribute.Name.LocalName,
                                               "xpath",
                                               StringComparison.Ordinal)
                                           || string.IsNullOrWhiteSpace(attribute.Value)
                                           || !string.Equals(
                                               attribute.Value,
                                               attribute.Value.Trim(),
                                               StringComparison.Ordinal)))
        {
            return false;
        }
        skillType = matches[0].Value;
        skillTypeQuery = attributes.SingleOrDefault()?.Value ?? string.Empty;
        return true;
    }

    private static bool TryReadSpecificSkillChoices(
        XElement talent,
        out string[] names)
    {
        names = [];
        XElement[] containers = talent.Elements("skillchoices").Take(2).ToArray();
        if (containers.Length > 1 || containers.Any(container => container.HasAttributes))
            return false;
        var projected = new List<string>();
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement choice in containers.SingleOrDefault()?.Elements()
                 ?? Enumerable.Empty<XElement>())
        {
            if (choice.Name.NamespaceName.Length != 0
                || !string.Equals(choice.Name.LocalName, "skill", StringComparison.Ordinal)
                || choice.HasAttributes
                || choice.HasElements
                || string.IsNullOrWhiteSpace(choice.Value)
                || !string.Equals(choice.Value, choice.Value.Trim(), StringComparison.Ordinal)
                || !unique.Add(choice.Value))
            {
                return false;
            }
            projected.Add(choice.Value);
        }
        names = projected.ToArray();
        return true;
    }

    private static bool TryProjectSkillGroupGrant(
        XElement talent,
        TalentSkillCatalog? catalog,
        string talentAnchor,
        out CharacterCreationTalentSkillGroupGrantProjection? projection)
    {
        projection = null;
        XElement[] quantityNodes = talent.Elements("skillgroupqty").Take(2).ToArray();
        XElement[] ratingNodes = talent.Elements("skillgroupval").Take(2).ToArray();
        XElement[] typeNodes = talent.Elements("skillgrouptype").Take(2).ToArray();
        XElement[] choiceContainers = talent.Elements("skillgroupchoices").Take(2).ToArray();
        if (quantityNodes.Length == 0
            && ratingNodes.Length == 0
            && typeNodes.Length == 0
            && choiceContainers.Length == 0)
        {
            return true;
        }
        if (quantityNodes.Length == 0)
            return true;
        if (quantityNodes.Length != 1)
            return false;
        if (!TryReadIntScalar(talent, "skillgroupqty", out int sourceQuantity)
            || sourceQuantity <= 0)
        {
            return true;
        }
        if (ratingNodes.Length != 1
            || typeNodes.Length != 1
            || choiceContainers.Length > 1
            || choiceContainers.Any(container => container.HasAttributes)
            || !TryReadRequiredNonNegativeInt(talent, "skillgroupval", out int rating)
            || !TryReadNormalizedScalar(talent, "skillgrouptype", out string rawSkillGroupType))
        {
            return false;
        }
        int quantity = Math.Min(
            sourceQuantity,
            CharacterCreationTalentSkillGrantTypes.MaximumPromptSlots);
        string skillGroupType = rawSkillGroupType.ToLowerInvariant();

        var requestedNames = new List<string>();
        var uniqueNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement choice in choiceContainers.SingleOrDefault()?.Elements()
                 ?? Enumerable.Empty<XElement>())
        {
            if (choice.Name.NamespaceName.Length != 0
                || !string.Equals(choice.Name.LocalName, "skillgroup", StringComparison.Ordinal)
                || choice.HasAttributes
                || choice.HasElements
                || string.IsNullOrWhiteSpace(choice.Value)
                || !string.Equals(choice.Value, choice.Value.Trim(), StringComparison.Ordinal)
                || !uniqueNames.Add(choice.Value))
            {
                return false;
            }
            requestedNames.Add(choice.Value);
        }

        bool supportedType = skillGroupType is CharacterCreationTalentSkillGrantTypes.Choices
            or CharacterCreationTalentSkillGrantTypes.Grouped;
        var options = new List<CharacterCreationTalentSkillGroupChoiceProjection>();
        bool allChoicesResolved = catalog is not null;
        if (catalog is not null)
        {
            IEnumerable<string> projectedNames = requestedNames.Count == 0 && supportedType
                ? catalog.SkillGroups.Keys.OrderBy(name => name, StringComparer.Ordinal)
                : requestedNames;
            foreach (string name in projectedNames)
            {
                if (!catalog.SkillGroups.TryGetValue(name, out TalentSkillGroupDefinition? group))
                {
                    allChoicesResolved = false;
                    continue;
                }
                options.Add(new CharacterCreationTalentSkillGroupChoiceProjection(
                    group.SelectionId,
                    group.CanonicalName,
                    group.MemberSkillSourceIds,
                    group.GroupDigest,
                    catalog.SourceDigest,
                    [$"skills.xml#skillgroup:{group.CanonicalName}"]));
            }
        }
        // Current Core data is the corrected "grouped" form. The pinned fe435
        // corpus still has the pre-9ead69da "choices" token, which is accepted
        // only as an explicitly marked compatibility alias for the intended
        // group picker. Every other branch remains visible but unsupported.
        bool supported = catalog is not null
                         && supportedType
                         && allChoicesResolved
                         && options.Count >= quantity;
        string[] blockers = supported
            ? []
            : [CharacterCreationPrerequisiteBlockers.TalentSkillGrantAuthorityUnsupported];
        string sourceDigest = catalog?.SourceDigest ?? string.Empty;
        bool legacyChoicesAlias = string.Equals(
            skillGroupType,
            CharacterCreationTalentSkillGrantTypes.Choices,
            StringComparison.Ordinal);
        string compatibilityMarker = legacyChoicesAlias
            ? CharacterCreationTalentSkillGrantTypes.GroupChoiceAliasCompatibility
            : string.Empty;
        string[] anchors = legacyChoicesAlias
            ? [talentAnchor, "skills.xml", $"compatibility:{compatibilityMarker}"]
            : [talentAnchor, "skills.xml"];
        projection = new CharacterCreationTalentSkillGroupGrantProjection(
            quantity,
            rating,
            skillGroupType,
            options,
            CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroupGrant(
                quantity,
                rating,
                skillGroupType,
                sourceDigest,
                options.Select(option => option.SelectionId)),
            supported,
            blockers,
            anchors)
        {
            CompatibilityMarker = compatibilityMarker,
            RequestedGroupNames = requestedNames.ToArray()
        };
        return true;
    }

    private static bool TryProjectTalentSkillCatalog(
        XDocument document,
        string sourceDigest,
        out TalentSkillCatalog? catalog)
    {
        catalog = null;
        XElement? root = document.Root;
        XElement[] skillContainers = root?.Elements("skills").Take(2).ToArray() ?? [];
        XElement[] groupContainers = root?.Elements("skillgroups").Take(2).ToArray() ?? [];
        if (root is null
            || root.Name.NamespaceName.Length != 0
            || !string.Equals(root.Name.LocalName, "chummer", StringComparison.Ordinal)
            || !CharacterCreationPrerequisiteAuthorityDigest.IsCanonical(sourceDigest)
            || skillContainers.Length != 1
            || groupContainers.Length != 1
            || skillContainers[0].HasAttributes
            || groupContainers[0].HasAttributes)
        {
            return false;
        }

        var groupNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement group in groupContainers[0].Elements())
        {
            if (group.Name.NamespaceName.Length != 0
                || !string.Equals(group.Name.LocalName, "name", StringComparison.Ordinal)
                || group.HasAttributes
                || group.HasElements
                || string.IsNullOrWhiteSpace(group.Value)
                || !string.Equals(group.Value, group.Value.Trim(), StringComparison.Ordinal)
                || !groupNames.Add(group.Value))
            {
                return false;
            }
        }

        var activeSkills = new List<TalentActiveSkillDefinition>();
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var ordinaryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (XElement skill in skillContainers[0].Elements())
        {
            if (skill.Name.NamespaceName.Length != 0
                || !string.Equals(skill.Name.LocalName, "skill", StringComparison.Ordinal)
                || !TryReadNormalizedScalar(skill, "id", out string rawSourceId)
                || !Guid.TryParseExact(rawSourceId, "D", out Guid parsedSourceId)
                || parsedSourceId == Guid.Empty
                || !TryReadCanonicalSkillName(skill, out string canonicalName)
                || !TryReadOptionalNormalizedScalar(skill, "attribute", out string? attribute)
                || !TryReadNormalizedScalar(skill, "category", out string category)
                || !TryReadNormalizedScalar(skill, "source", out _)
                || !TryReadOptionalNormalizedScalar(skill, "skillgroup", out string? skillGroup)
                || skillGroup is not null && !groupNames.Contains(skillGroup)
                || !TryReadOptionalStrictBool(skill, "exotic", out bool isExotic))
            {
                return false;
            }
            string sourceId = parsedSourceId.ToString("D");
            if (!string.Equals(sourceId, rawSourceId, StringComparison.Ordinal)
                || !sourceIds.Add(sourceId)
                || !isExotic && !ordinaryNames.Add(canonicalName))
            {
                return false;
            }
            // SelectMetatypePriority does not apply BookXPath to this prompt.
            // The saved profile still binds the skills input digest, but it does
            // not silently narrow Talent-granted skill or group choices.
            activeSkills.Add(new TalentActiveSkillDefinition(
                sourceId,
                canonicalName,
                attribute ?? string.Empty,
                category,
                skillGroup,
                isExotic,
                RawDigest(skill.ToString(SaveOptions.DisableFormatting))));
        }
        if (activeSkills.Count == 0)
            return false;

        var skillGroups = new Dictionary<string, TalentSkillGroupDefinition>(StringComparer.Ordinal);
        foreach (string groupName in groupNames.OrderBy(name => name, StringComparer.Ordinal))
        {
            string[] memberIds = activeSkills
                .Where(skill => string.Equals(skill.SkillGroup, groupName, StringComparison.Ordinal))
                .Select(skill => skill.SourceId)
                .OrderBy(sourceId => sourceId, StringComparer.Ordinal)
                .ToArray();
            if (memberIds.Length == 0)
                continue;
            string groupDigest = CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroup(
                sourceDigest,
                groupName,
                memberIds);
            skillGroups.Add(groupName, new TalentSkillGroupDefinition(
                CharacterCreationTalentGrantAuthorityDigest.ComputeSkillGroupSelectionId(
                    groupDigest),
                groupName,
                memberIds,
                groupDigest));
        }

        catalog = new TalentSkillCatalog(
            sourceDigest,
            activeSkills.ToArray(),
            skillGroups);
        return true;
    }

    private static bool TryReadCanonicalSkillName(XElement skill, out string name)
    {
        XElement[] matches = skill.Elements("name").ToArray();
        name = matches.FirstOrDefault()?.Value ?? string.Empty;
        // The current canonical skills corpus contains an exact duplicate name
        // element for Artisan. Treat identical repeated scalars as one source
        // value, while distinct values and cross-skill ordinary-name collisions
        // remain fail-closed.
        return matches.Length > 0
               && !string.IsNullOrWhiteSpace(name)
               && string.Equals(name, name.Trim(), StringComparison.Ordinal)
               && matches.All(element => !element.HasAttributes
                                         && !element.HasElements
                                         && string.Equals(
                                             element.Value,
                                             name,
                                             StringComparison.Ordinal));
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
        TalentSkillCatalog? skillCatalog,
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
            CharacterCreationPriorityTalentOptionProjection projection = new(
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
                SourceAnchorIds: [$"priorities.xml#priority:{prioritySourceId}:talent:{order}"]);
            if (!TryProjectActiveSkillGrant(
                    talent,
                    skillCatalog,
                    projection.SourceAnchorIds[0],
                    out CharacterCreationTalentActiveSkillGrantProjection? activeSkillGrant,
                    out bool activeBranchPresent))
            {
                return false;
            }
            CharacterCreationTalentSkillGroupGrantProjection? skillGroupGrant = null;
            if (!activeBranchPresent
                && !TryProjectSkillGroupGrant(
                    talent,
                    skillCatalog,
                    projection.SourceAnchorIds[0],
                    out skillGroupGrant))
            {
                return false;
            }
            projected.Add(projection with
            {
                ActiveSkillGrant = activeSkillGrant,
                SkillGroupGrant = skillGroupGrant
            });
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

    private static bool TryReadIntScalar(XElement parent, string name, out int value)
    {
        value = 0;
        return TryReadNormalizedScalar(parent, name, out string raw)
               && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadOptionalNormalizedScalar(
        XElement parent,
        string name,
        out string? value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = null;
        if (matches.Length == 0)
            return true;
        if (matches.Length != 1
            || matches[0].HasAttributes
            || matches[0].HasElements
            || !string.Equals(matches[0].Value, matches[0].Value.Trim(), StringComparison.Ordinal))
        {
            return false;
        }
        value = string.IsNullOrEmpty(matches[0].Value) ? null : matches[0].Value;
        return true;
    }

    private static bool TryReadOptionalStrictBool(
        XElement parent,
        string name,
        out bool value)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        value = false;
        return matches.Length == 0
               || matches.Length == 1
               && !matches[0].HasAttributes
               && !matches[0].HasElements
               && string.Equals(
                   matches[0].Value,
                   matches[0].Value.Trim(),
                   StringComparison.Ordinal)
               && bool.TryParse(matches[0].Value, out value);
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
            ReverseAttributePriorityOrder = context.ReverseAttributePriorityOrder,
            RawSkillsXmlDigest = context.RawSkillsXmlDigest,
            EffectiveSkillsInputsDigest = context.EffectiveSkillsInputsDigest
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

    private sealed record TalentSkillCatalog(
        string SourceDigest,
        IReadOnlyList<TalentActiveSkillDefinition> ActiveSkills,
        IReadOnlyDictionary<string, TalentSkillGroupDefinition> SkillGroups);

    private sealed record TalentActiveSkillDefinition(
        string SourceId,
        string CanonicalName,
        string Attribute,
        string Category,
        string? SkillGroup,
        bool IsExotic,
        string SourceNodeDigest);

    private sealed record TalentSkillGroupDefinition(
        string SelectionId,
        string CanonicalName,
        IReadOnlyList<string> MemberSkillSourceIds,
        string GroupDigest);

}
