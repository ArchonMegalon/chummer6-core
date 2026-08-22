using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Chummer.Application.LifeModules;
using Chummer.Contracts.LifeModules;

namespace Chummer.Infrastructure.Xml;

public sealed class XmlLifeModulesCatalogService : ILifeModulesCatalogService
{
    public const string CharacterEligibilityAuthorityRequired = "character-eligibility-authority-required";
    public const string EffectApplicationAuthorityRequired = "effect-application-authority-required";

    private static readonly Regex s_FollowUpPlaceholder = new(
        @"^\[[^\]]+\]$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly IReadOnlyDictionary<string, string> s_EffectDomains =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["attributelevel"] = "attribute",
            ["skilllevel"] = "active-skill",
            ["skillgrouplevel"] = "skill-group",
            ["knowledgeskilllevel"] = "knowledge-skill",
            ["addskillspecialization"] = "skill-specialization",
            ["addqualities"] = "quality",
            ["qualitylevel"] = "quality",
            ["selectquality"] = "quality",
            ["addcontact"] = "contact",
            ["nuyenamt"] = "nuyen",
            ["freenegativequalities"] = "negative-quality-karma",
            ["freepositivequalities"] = "positive-quality-karma",
            ["selectskill"] = "active-skill",
            ["pushtext"] = "story"
        };

    private sealed record CatalogSnapshot(XDocument Document, string RawXmlDigest);

    private readonly Lazy<CatalogSnapshot> _snapshot;

    public XmlLifeModulesCatalogService(string lifeModulesPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lifeModulesPath);
        _snapshot = new Lazy<CatalogSnapshot>(() => LoadSnapshot(lifeModulesPath));
    }

    public LifeModuleCatalogAuthorityDto GetAuthority() => new(
        Schema: LifeModuleJourneySchemas.CatalogAuthorityV1,
        RawXmlDigest: _snapshot.Value.RawXmlDigest,
        SourceAnchorIds: ["lifemodules.xml"]);

    public IReadOnlyList<LifeModuleStageDto> GetStages()
    {
        return _snapshot.Value.Document.Root!
            .Element("stages")!
            .Elements("stage")
            .Select(stage => new LifeModuleStageDto(
                int.TryParse(stage.Attribute("order")?.Value, out int order) ? order : -1,
                (stage.Value ?? string.Empty).Trim()))
            .OrderBy(stage => stage.Order)
            .ToArray();
    }

    public IReadOnlyList<LifeModuleSummaryDto> GetModules(string? stage = null)
    {
        IEnumerable<XElement> modules = _snapshot.Value.Document.Root!
            .Element("modules")!
            .Elements("module");

        if (!string.IsNullOrWhiteSpace(stage))
        {
            string normalizedStage = stage.Trim();
            modules = modules.Where(module =>
                string.Equals((module.Element("stage")?.Value ?? string.Empty).Trim(), normalizedStage, StringComparison.Ordinal));
        }

        return modules.Select(module => new LifeModuleSummaryDto(
            Id: (module.Element("id")?.Value ?? string.Empty).Trim(),
            Stage: (module.Element("stage")?.Value ?? string.Empty).Trim(),
            Name: (module.Element("name")?.Value ?? string.Empty).Trim(),
            Karma: (module.Element("karma")?.Value ?? string.Empty).Trim(),
            Source: (module.Element("source")?.Value ?? string.Empty).Trim(),
            Page: (module.Element("page")?.Value ?? string.Empty).Trim(),
            Story: (module.Element("story")?.Value ?? string.Empty).Trim()))
            .ToArray();
    }

    public IReadOnlyList<LifeModuleLegalOptionDto> GetOptionProjections(
        string? stage = null,
        IReadOnlyCollection<string>? enabledSources = null)
    {
        IReadOnlyDictionary<string, LifeModuleStageDto> stagesByName = GetStages()
            .ToDictionary(item => item.Name, StringComparer.Ordinal);
        HashSet<string>? enabledSourceSet = enabledSources is null
            ? null
            : enabledSources
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        IEnumerable<XElement> modules = _snapshot.Value.Document.Root!
            .Element("modules")!
            .Elements("module");

        if (!string.IsNullOrWhiteSpace(stage))
        {
            string normalizedStage = stage.Trim();
            modules = modules.Where(module => string.Equals(
                ReadValue(module, "stage"),
                normalizedStage,
                StringComparison.Ordinal));
        }

        if (enabledSourceSet is not null)
        {
            modules = modules
                .Where(module => enabledSourceSet.Contains(ReadValue(module, "source")))
                .Where(module => module.Element("versions") is not XElement versions
                                 || versions.Elements("version").Any(version =>
                                     enabledSourceSet.Contains(ReadInheritedValue(
                                         version,
                                         module,
                                         "source"))));
        }

        return modules
            .Select(module => ProjectModule(module, stagesByName, enabledSourceSet))
            .OrderBy(module => module.StageOrder)
            .ThenBy(module => module.Name, StringComparer.Ordinal)
            .ThenBy(module => module.ModuleId, StringComparer.Ordinal)
            .ToArray();
    }

    private static LifeModuleLegalOptionDto ProjectModule(
        XElement module,
        IReadOnlyDictionary<string, LifeModuleStageDto> stagesByName,
        IReadOnlySet<string>? enabledSources)
    {
        string moduleId = ReadValue(module, "id");
        string stageId = ReadValue(module, "stage");
        int stageOrder = stagesByName.TryGetValue(stageId, out LifeModuleStageDto? stage)
            ? stage.Order
            : -1;
        string source = ReadValue(module, "source");
        string pageReference = ReadValue(module, "page");
        int? page = ParsePage(pageReference);
        string karmaRaw = ReadValue(module, "karma");
        bool karmaIsExact = TryParseDecimal(karmaRaw, out decimal karmaCost);
        IReadOnlyList<string> anchors = CreateSourceAnchors(moduleId, source, pageReference);
        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements = ProjectRequirements(
            moduleId,
            module.Element("required"),
            anchors);
        (IReadOnlyList<LifeModuleEffectProjectionDto> effects,
            IReadOnlyList<LifeModuleFollowUpPromptDto> followUps) = ProjectBonus(
                moduleId,
                module.Element("bonus"),
                anchors);

        var authorityBlockers = new List<string>();
        if (string.IsNullOrWhiteSpace(moduleId))
            authorityBlockers.Add("module-identity-missing");
        if (stageOrder < 0)
            authorityBlockers.Add("module-stage-authority-missing");
        if (!karmaIsExact)
            authorityBlockers.Add("module-karma-cost-invalid");
        if (requirements.Count > 0)
            authorityBlockers.Add(CharacterEligibilityAuthorityRequired);

        IReadOnlyList<LifeModuleVersionProjectionDto> versions = module
            .Element("versions")?
            .Elements("version")
            .Select((version, index) => (Version: version, Index: index))
            .Where(item => enabledSources is null
                           || enabledSources.Contains(ReadInheritedValue(
                               item.Version,
                               module,
                               "source")))
            .Select(item => ProjectVersion(
                module,
                item.Version,
                item.Index,
                moduleId,
                karmaRaw,
                karmaCost,
                karmaIsExact,
                source,
                pageReference,
                page,
                anchors,
                requirements.Count > 0,
                authorityBlockers))
            .ToArray()
            ?? [];

        bool hasStructurallyEnabledVersion = versions.Count == 0
                                             || versions.Any(version => version.IsEnabled);
        bool isEnabled = authorityBlockers.Count == 0 && hasStructurallyEnabledVersion;
        if (versions.Count > 0 && !hasStructurallyEnabledVersion
            && !authorityBlockers.Contains(CharacterEligibilityAuthorityRequired, StringComparer.Ordinal))
        {
            authorityBlockers.Add(CharacterEligibilityAuthorityRequired);
        }

        return new LifeModuleLegalOptionDto(
            ModuleId: moduleId,
            StageOrder: stageOrder,
            Name: ReadValue(module, "name"),
            KarmaCost: karmaCost,
            Source: source,
            Page: page,
            StoryTemplate: ReadValue(module, "story"),
            IsEnabled: isEnabled,
            Requirements: requirements,
            Versions: versions,
            Effects: effects,
            FollowUps: followUps,
            SourceAnchorIds: anchors,
            StageId: stageId,
            CanRepeat: stage?.CanRepeat ?? false,
            KarmaRaw: karmaRaw,
            KarmaIsExact: karmaIsExact,
            PageReference: pageReference,
            AuthorityBlockers: authorityBlockers.ToArray());
    }

    private static LifeModuleVersionProjectionDto ProjectVersion(
        XElement module,
        XElement version,
        int versionIndex,
        string moduleId,
        string moduleKarmaRaw,
        decimal moduleKarmaCost,
        bool moduleKarmaIsExact,
        string moduleSource,
        string modulePageReference,
        int? modulePage,
        IReadOnlyList<string> moduleAnchors,
        bool moduleRequiresCharacterAuthority,
        IReadOnlyList<string> moduleAuthorityBlockers)
    {
        string versionId = ReadValue(version, "id");
        string ownerId = string.IsNullOrWhiteSpace(versionId)
            ? $"{moduleId}:version:{versionIndex + 1}"
            : versionId;
        string source = ReadValue(version, "source");
        if (string.IsNullOrWhiteSpace(source))
            source = moduleSource;
        string pageReference = ReadValue(version, "page");
        int? page = ParsePage(pageReference);
        if (string.IsNullOrWhiteSpace(pageReference))
        {
            pageReference = modulePageReference;
            page = modulePage;
        }

        string karmaRaw = ReadValue(version, "karma");
        bool karmaIsExact;
        decimal karmaCost;
        if (string.IsNullOrWhiteSpace(karmaRaw))
        {
            karmaRaw = moduleKarmaRaw;
            karmaCost = moduleKarmaCost;
            karmaIsExact = moduleKarmaIsExact;
        }
        else
        {
            karmaIsExact = TryParseDecimal(karmaRaw, out karmaCost);
        }

        IReadOnlyList<string> anchors = moduleAnchors
            .Append($"lifemodules.xml#version:{ownerId}")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        IReadOnlyList<LifeModuleRequirementProjectionDto> requirements = ProjectRequirements(
            ownerId,
            version.Element("required"),
            anchors);
        (IReadOnlyList<LifeModuleEffectProjectionDto> effects,
            IReadOnlyList<LifeModuleFollowUpPromptDto> followUps) = ProjectBonus(
                ownerId,
                version.Element("bonus"),
                anchors);

        var authorityBlockers = new List<string>(moduleAuthorityBlockers);
        if (string.IsNullOrWhiteSpace(versionId))
            authorityBlockers.Add("version-identity-missing");
        if (!karmaIsExact)
            authorityBlockers.Add("version-karma-cost-invalid");
        if (requirements.Count > 0 && !authorityBlockers.Contains(
                CharacterEligibilityAuthorityRequired,
                StringComparer.Ordinal))
        {
            authorityBlockers.Add(CharacterEligibilityAuthorityRequired);
        }

        string story = ReadValue(version, "story");
        if (string.IsNullOrWhiteSpace(story))
            story = ReadValue(module, "story");

        return new LifeModuleVersionProjectionDto(
            VersionId: ownerId,
            Label: ReadValue(version, "name"),
            IsEnabled: !moduleRequiresCharacterAuthority && authorityBlockers.Count == 0,
            Requirements: requirements,
            Effects: effects,
            FollowUps: followUps,
            SourceAnchorIds: anchors,
            StoryTemplate: story,
            KarmaCost: karmaCost,
            KarmaRaw: karmaRaw,
            KarmaIsExact: karmaIsExact,
            Source: source,
            Page: page,
            PageReference: pageReference,
            AuthorityBlockers: authorityBlockers.Distinct(StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyList<LifeModuleRequirementProjectionDto> ProjectRequirements(
        string ownerId,
        XElement? required,
        IReadOnlyList<string> sourceAnchors)
    {
        if (required is null)
            return [];

        XElement[] clauses = required.Elements().ToArray();
        if (clauses.Length == 0)
            clauses = [required];

        return clauses.Select((clause, index) =>
        {
            XElement[] values = clause.DescendantsAndSelf()
                .Where(item => !item.HasElements && !string.IsNullOrWhiteSpace(item.Value))
                .ToArray();
            string[] kinds = values
                .Select(item => item.Name.LocalName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string subjectKind = kinds.Length == 1 ? kinds[0] : "mixed";
            string[] acceptedValues = values
                .Select(item => item.Value.Trim())
                .ToArray();
            string operation = ReferenceEquals(clause, required)
                ? "raw"
                : clause.Name.LocalName;
            string label = acceptedValues.Length == 0
                ? operation
                : $"{operation} {subjectKind}: {string.Join(" | ", acceptedValues)}";

            return new LifeModuleRequirementProjectionDto(
                RequirementId: $"{ownerId}:requirement:{index + 1}",
                Label: label,
                IsMet: false,
                DisableReasonKey: CharacterEligibilityAuthorityRequired,
                DisableReasonArguments: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["operator"] = operation,
                    ["subjectKind"] = subjectKind,
                    ["acceptedValues"] = string.Join("|", acceptedValues)
                },
                SourceAnchorIds: sourceAnchors,
                Operator: operation,
                SubjectKind: subjectKind,
                AcceptedValues: acceptedValues,
                RawXml: clause.ToString(SaveOptions.DisableFormatting),
                RequiresCharacterAuthority: true);
        }).ToArray();
    }

    private static (
        IReadOnlyList<LifeModuleEffectProjectionDto> Effects,
        IReadOnlyList<LifeModuleFollowUpPromptDto> FollowUps) ProjectBonus(
            string ownerId,
            XElement? bonus,
            IReadOnlyList<string> sourceAnchors)
    {
        if (bonus is null)
            return ([], []);

        var effects = new List<LifeModuleEffectProjectionDto>();
        var followUps = new List<LifeModuleFollowUpPromptDto>();
        int effectIndex = 0;
        foreach (XElement effectElement in bonus.Elements())
        {
            effectIndex++;
            string effectId = $"{ownerId}:effect:{effectIndex}";
            string effectKind = effectElement.Name.LocalName;
            string targetId = ResolveTargetId(effectElement);
            IReadOnlyDictionary<string, string> parameters = ReadParameters(effectElement);
            string? afterValue = parameters.TryGetValue("val", out string? configuredValue)
                ? configuredValue
                : !effectElement.HasElements
                    ? effectElement.Value.Trim()
                    : null;
            string? budgetId = ResolveBudgetId(effectKind);
            decimal budgetDelta = budgetId is not null
                                  && TryParseDecimal(effectElement.Value.Trim(), out decimal parsedBudgetDelta)
                ? parsedBudgetDelta
                : 0m;
            bool isFullyTyped = s_EffectDomains.TryGetValue(effectKind, out string? domain);
            domain ??= $"xml:{effectKind}";

            effects.Add(new LifeModuleEffectProjectionDto(
                EffectId: effectId,
                Domain: domain,
                TargetId: targetId,
                BeforeValue: null,
                AfterValue: afterValue,
                BudgetId: budgetId,
                BudgetDelta: budgetDelta,
                SourceAnchorIds: sourceAnchors,
                Parameters: parameters,
                RawXml: effectElement.ToString(SaveOptions.DisableFormatting),
                IsFullyTyped: isFullyTyped,
                AuthorityBlocker: EffectApplicationAuthorityRequired));

            followUps.AddRange(ProjectFollowUps(effectId, effectElement, sourceAnchors));
        }

        return (effects.ToArray(), followUps.ToArray());
    }

    private static IReadOnlyList<LifeModuleFollowUpPromptDto> ProjectFollowUps(
        string effectId,
        XElement effect,
        IReadOnlyList<string> sourceAnchors)
    {
        var prompts = new List<LifeModuleFollowUpPromptDto>();
        int promptIndex = 0;

        foreach (XElement optionsNode in effect.Descendants()
                     .Where(item => item.Name.LocalName is "options" or "option")
                     .Where(item => item.HasElements))
        {
            AddChoicePrompt(optionsNode, optionsNode.Elements());
        }

        if (effect.Name.LocalName.Equals("selectquality", StringComparison.OrdinalIgnoreCase))
            AddChoicePrompt(effect, effect.Elements("quality"));

        if (effect.Name.LocalName.Equals("selectskill", StringComparison.OrdinalIgnoreCase))
        {
            string limitedSkills = (effect.Attribute("limittoskill")?.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(limitedSkills))
            {
                AddChoicePrompt(
                    effect,
                    limitedSkills.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select((value, index) => new XElement($"option{index + 1}", value)));
            }
        }

        foreach (XElement placeholder in effect.DescendantsAndSelf()
                     .Where(item => !item.HasElements)
                     .Where(item => s_FollowUpPlaceholder.IsMatch(item.Value.Trim()))
                     .Where(item => !item.Ancestors().Any(ancestor =>
                         ancestor.Name.LocalName is "options" or "option")))
        {
            promptIndex++;
            string value = placeholder.Value.Trim();
            prompts.Add(new LifeModuleFollowUpPromptDto(
                PromptId: $"{effectId}:follow-up:{promptIndex}",
                Label: value.Trim('[', ']'),
                InputKind: "text",
                IsRequired: true,
                Options: [],
                SourceAnchorIds: sourceAnchors,
                EffectId: effectId,
                ValuePath: BuildValuePath(effect, placeholder)));
        }

        return prompts.ToArray();

        void AddChoicePrompt(XElement valueNode, IEnumerable<XElement> optionElements)
        {
            XElement[] optionArray = optionElements.ToArray();
            if (optionArray.Length == 0)
                return;

            promptIndex++;
            var duplicateIds = optionArray
                .GroupBy(item => item.Name.LocalName, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<LifeModuleFollowUpOptionDto> options = optionArray
                .Select((item, index) =>
                {
                    string sourceValue = item.Value.Trim();
                    string optionId = duplicateIds.Contains(item.Name.LocalName)
                        ? $"{item.Name.LocalName}:{index + 1}"
                        : item.Name.LocalName;
                    return new LifeModuleFollowUpOptionDto(
                        OptionId: optionId,
                        Label: sourceValue,
                        IsEnabled: true,
                        DisableReasonKey: null,
                        DisableReasonArguments: new Dictionary<string, string>(),
                        SourceValue: sourceValue);
                })
                .ToArray();
            prompts.Add(new LifeModuleFollowUpPromptDto(
                PromptId: $"{effectId}:follow-up:{promptIndex}",
                Label: ResolveTargetId(effect),
                InputKind: "single-select",
                IsRequired: true,
                Options: options,
                SourceAnchorIds: sourceAnchors,
                EffectId: effectId,
                ValuePath: BuildValuePath(effect, valueNode)));
        }
    }

    private static IReadOnlyDictionary<string, string> ReadParameters(XElement effect)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (XAttribute attribute in effect.Attributes())
            parameters[$"@{attribute.Name.LocalName}"] = attribute.Value.Trim();

        foreach (IGrouping<string, XElement> group in effect.Elements()
                     .Where(item => !item.HasElements)
                     .GroupBy(item => item.Name.LocalName, StringComparer.OrdinalIgnoreCase))
        {
            parameters[group.Key] = string.Join("|", group.Select(item => item.Value.Trim()));
        }

        return parameters;
    }

    private static string ResolveTargetId(XElement effect)
    {
        foreach (string childName in new[] { "name", "skill", "quality", "group", "spec" })
        {
            string value = ReadValue(effect, childName);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        string limitedSkills = (effect.Attribute("limittoskill")?.Value ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(limitedSkills))
            return limitedSkills;

        if (!effect.HasElements)
            return effect.Value.Trim();

        return effect.Name.LocalName;
    }

    private static string? ResolveBudgetId(string effectKind)
    {
        return effectKind.ToLowerInvariant() switch
        {
            "nuyenamt" => "nuyen",
            "freenegativequalities" => "negative-quality-karma",
            "freepositivequalities" => "positive-quality-karma",
            _ => null
        };
    }

    private static IReadOnlyList<string> CreateSourceAnchors(
        string moduleId,
        string source,
        string pageReference)
    {
        var anchors = new List<string> { $"lifemodules.xml#module:{moduleId}" };
        if (!string.IsNullOrWhiteSpace(source))
        {
            anchors.Add(string.IsNullOrWhiteSpace(pageReference)
                ? $"source:{source}"
                : $"source:{source}:page:{pageReference}");
        }

        return anchors;
    }

    private static string BuildValuePath(XElement root, XElement item)
    {
        if (ReferenceEquals(root, item))
            return root.Name.LocalName;

        var segments = item.AncestorsAndSelf()
            .TakeWhile(element => !ReferenceEquals(element, root.Parent))
            .Reverse()
            .Select(element => element.Name.LocalName);
        return string.Join('/', segments);
    }

    private static string ReadValue(XElement parent, string childName) =>
        (parent.Element(childName)?.Value ?? string.Empty).Trim();

    private static string ReadInheritedValue(XElement child, XElement parent, string elementName)
    {
        string value = ReadValue(child, elementName);
        return string.IsNullOrWhiteSpace(value) ? ReadValue(parent, elementName) : value;
    }

    private static int? ParsePage(string raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int page)
            ? page
            : null;

    private static bool TryParseDecimal(string raw, out decimal value) =>
        decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static CatalogSnapshot LoadSnapshot(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        using var stream = new MemoryStream(bytes, writable: false);
        XDocument document = XDocument.Load(stream, LoadOptions.None);
        string digest = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new CatalogSnapshot(document, digest);
    }
}
