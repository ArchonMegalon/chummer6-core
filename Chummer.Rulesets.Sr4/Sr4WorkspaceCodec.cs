using System.Text;
using System.Xml.Linq;
using Chummer.Application.BuildLab;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Xml;

namespace Chummer.Rulesets.Sr4;

public sealed class Sr4WorkspaceCodec : IRulesetWorkspaceCodec
{
    public const int SchemaVersion = 1;
    public const string Sr4PayloadKind = "sr4/chum4-xml";
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSectionQueries _sectionQueries;
    private readonly ICharacterMetadataCommands _metadataCommands;

    public Sr4WorkspaceCodec()
        : this(
            new XmlCharacterFileQueries(new CharacterFileService()),
            new XmlCharacterSectionQueries(new CharacterSectionService()),
            new XmlCharacterMetadataCommands(new CharacterFileService()))
    {
    }

    public Sr4WorkspaceCodec(
        ICharacterFileQueries characterFileQueries,
        ICharacterSectionQueries sectionQueries,
        ICharacterMetadataCommands metadataCommands)
    {
        _characterFileQueries = characterFileQueries;
        _sectionQueries = sectionQueries;
        _metadataCommands = metadataCommands;
    }

    public string RulesetId => RulesetDefaults.Sr4;

    int IRulesetWorkspaceCodec.SchemaVersion => SchemaVersion;

    public string PayloadKind => Sr4PayloadKind;

    public WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document)
    {
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
        string xml = ToXmlContent(document.Content, document.Format);
        return new WorkspacePayloadEnvelope(
            RulesetId: normalizedRulesetId,
            SchemaVersion: SchemaVersion,
            PayloadKind: PayloadKind,
            Payload: xml);
    }

    public CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope)
    {
        return _characterFileQueries.ParseSummary(new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));
    }

    public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
    {
        string xml = ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml);
        CharacterDocument document = new(xml);
        XElement root = ParseRoot(xml);
        string normalizedSectionId = NormalizeSectionId(sectionId);

        return normalizedSectionId switch
        {
            "attributes" => ParseSr4Attributes(root),
            "attributedetails" => ParseSr4AttributeDetails(root),
            "skills" => ParseSr4Skills(root),
            "contacts" => ParseSr4Contacts(root),
            "rules" => ParseSr4Rules(root),
            "build" => ParseSr4Build(root),
            "armors" => ParseSr4Armors(root),
            "calendar" => ParseSr4Calendar(root),
            "powers" => ParseSr4Powers(root),
            "lifestyles" => ParseSr4Lifestyles(root),
            "build-lab" => BuildLabWorkspaceProjectionFactory.Create(
                profile: (CharacterProfileSection)ParseSection("profile", envelope),
                progress: (CharacterProgressSection)ParseSection("progress", envelope),
                rules: (CharacterRulesSection)ParseSection("rules", envelope),
                build: (CharacterBuildSection)ParseSection("build", envelope),
                skills: (CharacterSkillsSection)ParseSection("skills", envelope),
                awakening: (CharacterAwakeningSection)ParseSection("awakening", envelope),
                rulesetId: RulesetDefaults.Sr4),
            _ => TryParseSharedSection(normalizedSectionId, document)
        };
    }

    public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
    {
        return _characterFileQueries.Validate(new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));
    }

    public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
    {
        UpdateCharacterMetadataResult result = _metadataCommands.UpdateMetadata(new UpdateCharacterMetadataCommand(
            Document: new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)),
            Update: new CharacterMetadataUpdate(
                Name: command.Name,
                Alias: command.Alias,
                Notes: command.Notes)
            {
                GameNotes = command.GameNotes,
                GroupNotes = command.GroupNotes
            }));

        return envelope with
        {
            SchemaVersion = envelope.SchemaVersion > 0 ? envelope.SchemaVersion : SchemaVersion,
            PayloadKind = string.IsNullOrWhiteSpace(envelope.PayloadKind) ? PayloadKind : envelope.PayloadKind,
            Payload = result.UpdatedDocument.Content
        };
    }

    public WorkspaceDownloadReceipt BuildDownload(
        CharacterWorkspaceId id,
        WorkspacePayloadEnvelope envelope,
        WorkspaceDocumentFormat format)
    {
        string xml = ToXmlContent(envelope.Payload, format);
        byte[] contentBytes = Encoding.UTF8.GetBytes(xml);
        string contentBase64 = Convert.ToBase64String(contentBytes);
        string fileName = format switch
        {
            WorkspaceDocumentFormat.NativeXml => $"{id.Value}.chum4",
            _ => throw new InvalidOperationException($"Workspace format '{format}' is not supported.")
        };

        return new WorkspaceDownloadReceipt(
            Id: id,
            Format: format,
            ContentBase64: contentBase64,
            FileName: fileName,
            DocumentLength: xml.Length,
            RulesetId: RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr4);
    }

    public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
    {
        return new DataExportBundle(
            Summary: ParseSummary(envelope),
            Profile: TryParseExportSection<CharacterProfileSection>("profile", envelope),
            Progress: TryParseExportSection<CharacterProgressSection>("progress", envelope),
            Attributes: TryParseExportSection<CharacterAttributesSection>("attributes", envelope),
            Skills: TryParseExportSection<CharacterSkillsSection>("skills", envelope),
            Inventory: TryParseExportSection<CharacterInventorySection>("inventory", envelope),
            Qualities: TryParseExportSection<CharacterQualitiesSection>("qualities", envelope),
            Contacts: TryParseExportSection<CharacterContactsSection>("contacts", envelope),
            Lifestyles: TryParseExportSection<CharacterLifestylesSection>("lifestyles", envelope));
    }

    private TSection? TryParseExportSection<TSection>(string sectionId, WorkspacePayloadEnvelope envelope)
        where TSection : class
    {
        try
        {
            return ParseSection(sectionId, envelope) as TSection;
        }
        catch
        {
            return null;
        }
    }

    private static CharacterAttributesSection ParseSr4Attributes(XElement root)
    {
        IReadOnlyList<CharacterAttributeSummary> attributes = root
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => new CharacterAttributeSummary(
                Name: ReadValue(attribute, "name"),
                BaseValue: ParseInt(ReadValue(attribute, "value", "base")),
                TotalValue: ParseInt(ReadValue(attribute, "totalvalue", "value", "base"))))
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Name))
            .ToArray()
            ?? Array.Empty<CharacterAttributeSummary>();

        return new CharacterAttributesSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    private static CharacterAttributeDetailsSection ParseSr4AttributeDetails(XElement root)
    {
        IReadOnlyList<CharacterAttributeDetailSummary> attributes = root
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => new CharacterAttributeDetailSummary(
                Name: ReadValue(attribute, "name"),
                MetatypeMin: ParseInt(ReadValue(attribute, "metatypemin")),
                MetatypeMax: ParseInt(ReadValue(attribute, "metatypemax")),
                MetatypeAugMax: ParseInt(ReadValue(attribute, "metatypeaugmax")),
                BaseValue: ParseInt(ReadValue(attribute, "value", "base")),
                KarmaValue: ParseInt(ReadValue(attribute, "karma")),
                TotalValue: ParseInt(ReadValue(attribute, "totalvalue", "value", "base")),
                MetatypeCategory: ReadValue(attribute, "metatypecategory", "name")))
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.Name))
            .ToArray()
            ?? Array.Empty<CharacterAttributeDetailSummary>();

        return new CharacterAttributeDetailsSection(
            Count: attributes.Count,
            Attributes: attributes);
    }

    private static CharacterSkillsSection ParseSr4Skills(XElement root)
    {
        IEnumerable<XElement> skillElements = root.Element("skills")?.Elements("skill")
            ?? root.Element("newskills")?.Element("skills")?.Elements("skill")
            ?? Array.Empty<XElement>();

        IReadOnlyList<CharacterSkillSummary> skills = skillElements
            .Select(skill => new CharacterSkillSummary(
                Guid: ReadValue(skill, "guid"),
                Suid: ReadValue(skill, "suid", "name"),
                Category: ReadValue(skill, "skillcategory"),
                IsKnowledge: ParseBool(ReadValue(skill, "knowledge", "isknowledge")),
                BaseValue: ParseInt(ReadValue(skill, "rating", "base")),
                KarmaValue: ParseInt(ReadValue(skill, "karma")),
                Specializations: ReadSkillSpecializations(skill),
                Name: ReadValue(skill, "name", "suid")))
            .Where(skill => !string.IsNullOrWhiteSpace(skill.Suid) || !string.IsNullOrWhiteSpace(skill.Guid))
            .ToArray();

        return new CharacterSkillsSection(
            Count: skills.Count,
            KnowledgeCount: skills.Count(skill => skill.IsKnowledge),
            Skills: skills);
    }

    private static CharacterContactsSection ParseSr4Contacts(XElement root)
    {
        IReadOnlyList<CharacterContactSummary> contacts = root
            .Element("contacts")?
            .Elements("contact")
            .Select(contact => new CharacterContactSummary(
                Name: ReadValue(contact, "name"),
                Role: ReadValue(contact, "role", "type"),
                Location: ReadValue(contact, "location", "groupname"),
                Connection: ParseInt(ReadValue(contact, "connection")),
                Loyalty: ParseInt(ReadValue(contact, "loyalty"))))
            .Where(contact => !string.IsNullOrWhiteSpace(contact.Name))
            .ToArray()
            ?? Array.Empty<CharacterContactSummary>();

        return new CharacterContactsSection(
            Count: contacts.Count,
            Contacts: contacts);
    }

    private static CharacterRulesSection ParseSr4Rules(XElement root)
    {
        IReadOnlyList<string> bannedWareGrades = root
            .Element("bannedwaregrades")?
            .Elements("grade")
            .Select(static grade => grade.Value.Trim())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray()
            ?? Array.Empty<string>();

        string gameEdition = ReadValue(root, "gameedition");
        if (string.IsNullOrWhiteSpace(gameEdition))
        {
            gameEdition = "SR4";
        }

        return new CharacterRulesSection(
            GameEdition: gameEdition,
            Settings: ReadValue(root, "settings"),
            GameplayOption: ReadValue(root, "gameplayoption"),
            GameplayOptionQualityLimit: ParseInt(ReadValue(root, "gameplayoptionqualitylimit")),
            MaxNuyen: ParseInt(ReadValue(root, "maxnuyen", "nuyenmaxbp")),
            MaxKarma: ParseInt(ReadValue(root, "maxkarma")),
            ContactMultiplier: ParseInt(ReadValue(root, "contactmultiplier")),
            BannedWareGrades: bannedWareGrades);
    }

    private static CharacterBuildSection ParseSr4Build(XElement root)
    {
        return new CharacterBuildSection(
            BuildMethod: ReadValue(root, "buildmethod"),
            PriorityMetatype: ReadValue(root, "prioritymetatype"),
            PriorityAttributes: ReadValue(root, "priorityattributes"),
            PrioritySpecial: ReadValue(root, "priorityspecial"),
            PrioritySkills: ReadValue(root, "priorityskills"),
            PriorityResources: ReadValue(root, "priorityresources"),
            PriorityTalent: ReadValue(root, "prioritytalent"),
            SumToTen: ParseInt(ReadValue(root, "sumtoten")),
            Special: ParseInt(ReadValue(root, "special")),
            TotalSpecial: ParseInt(ReadValue(root, "totalspecial")),
            TotalAttributes: ParseInt(ReadValue(root, "totalattributes")),
            ContactPoints: ParseInt(ReadValue(root, "contactpoints")),
            ContactPointsUsed: ParseInt(ReadValue(root, "contactpointsused")));
    }

    private static CharacterArmorsSection ParseSr4Armors(XElement root)
    {
        IReadOnlyList<CharacterArmorSummary> armors = root
            .Element("armors")?
            .Elements("armor")
            .Select(armor => new CharacterArmorSummary(
                Guid: ReadValue(armor, "guid"),
                Name: ReadValue(armor, "name"),
                Category: ReadValue(armor, "category"),
                ArmorValue: FormatArmorValue(armor),
                Rating: ReadValue(armor, "rating", "armorcapacity"),
                Cost: ReadValue(armor, "cost"),
                Equipped: ParseBool(ReadValue(armor, "equipped"))))
            .Where(armor => !string.IsNullOrWhiteSpace(armor.Name))
            .ToArray()
            ?? Array.Empty<CharacterArmorSummary>();

        return new CharacterArmorsSection(
            Count: armors.Count,
            Armors: armors);
    }

    private static CharacterCalendarSection ParseSr4Calendar(XElement root)
    {
        XElement? calendar = root.Element("calendar");
        if (calendar is null)
        {
            return new CharacterCalendarSection(0, Array.Empty<CharacterCalendarEntrySummary>());
        }

        IReadOnlyList<CharacterCalendarEntrySummary> entries = calendar
            .Elements("week")
            .Select(week => new CharacterCalendarEntrySummary(
                Date: FormatWeekDate(week),
                Name: ReadValue(week, "name") switch
                {
                    { Length: > 0 } explicitName => explicitName,
                    _ => $"Week {ReadValue(week, "week")}"
                },
                Notes: ReadValue(week, "notes")))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Date) || !string.IsNullOrWhiteSpace(entry.Notes))
            .ToArray();

        if (entries.Count > 0)
        {
            return new CharacterCalendarSection(
                Count: entries.Count,
                Entries: entries);
        }

        return new CharacterCalendarSection(
            Count: 0,
            Entries: Array.Empty<CharacterCalendarEntrySummary>());
    }

    private static CharacterPowersSection ParseSr4Powers(XElement root)
    {
        IReadOnlyList<CharacterPowerSummary> powers = root
            .Element("powers")?
            .Elements("power")
            .Select(power => new CharacterPowerSummary(
                Name: ReadValue(power, "name"),
                Rating: ParseInt(ReadValue(power, "rating")),
                Source: ReadValue(power, "source"),
                PointsPerLevel: ParseDecimal(ReadValue(power, "pointsperlevel", "points"))))
            .Where(power => !string.IsNullOrWhiteSpace(power.Name))
            .ToArray()
            ?? Array.Empty<CharacterPowerSummary>();

        return new CharacterPowersSection(
            Count: powers.Count,
            Powers: powers);
    }

    private static CharacterLifestylesSection ParseSr4Lifestyles(XElement root)
    {
        IReadOnlyList<CharacterLifestyleSummary> lifestyles = root
            .Element("lifestyles")?
            .Elements("lifestyle")
            .Select(lifestyle => new CharacterLifestyleSummary(
                Name: ReadValue(lifestyle, "name"),
                BaseLifestyle: ReadValue(lifestyle, "baselifestyle", "lifestylename"),
                Source: ReadValue(lifestyle, "source"),
                Cost: ParseDecimal(ReadValue(lifestyle, "cost")),
                Months: ParseInt(ReadValue(lifestyle, "months"))))
            .Where(lifestyle => !string.IsNullOrWhiteSpace(lifestyle.Name))
            .ToArray()
            ?? Array.Empty<CharacterLifestyleSummary>();

        return new CharacterLifestylesSection(
            Count: lifestyles.Count,
            Lifestyles: lifestyles);
    }

    private object TryParseSharedSection(string sectionId, CharacterDocument document)
    {
        try
        {
            return _sectionQueries.ParseSection(sectionId, document);
        }
        catch (InvalidOperationException)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sectionId"] = sectionId,
                ["rulesetId"] = RulesetDefaults.Sr4
            };
        }
    }

    private static XElement ParseRoot(string payload)
    {
        string xml = string.IsNullOrWhiteSpace(payload)
            ? "<character />"
            : payload;
        return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
    }

    private static string NormalizeSectionId(string sectionId)
        => (sectionId ?? string.Empty).Trim().ToLowerInvariant();

    private static string ReadValue(XElement root, params string[] names)
    {
        foreach (string name in names)
        {
            string value = root.Element(name)?.Value?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ReadSkillSpecializations(XElement skill)
    {
        List<string> specializations = skill.Element("specs")?
            .Elements("spec")
            .Select(spec => ReadValue(spec, "name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToList()
            ?? [];

        string legacySpec = ReadValue(skill, "spec");
        if (!string.IsNullOrWhiteSpace(legacySpec))
        {
            specializations.Add(legacySpec);
        }

        return specializations.Count == 0
            ? Array.Empty<string>()
            : specializations
                .Distinct(StringComparer.Ordinal)
                .ToArray();
    }

    private static string FormatArmorValue(XElement armor)
    {
        string directArmorValue = ReadValue(armor, "armor");
        if (!string.IsNullOrWhiteSpace(directArmorValue))
        {
            return directArmorValue;
        }

        string ballistic = ReadValue(armor, "b");
        string impact = ReadValue(armor, "i");
        if (string.IsNullOrWhiteSpace(ballistic) && string.IsNullOrWhiteSpace(impact))
        {
            return string.Empty;
        }

        return $"{ballistic}/{impact}".Trim('/');
    }

    private static string FormatWeekDate(XElement week)
    {
        string year = ReadValue(week, "year");
        string weekNumber = ReadValue(week, "week");
        if (string.IsNullOrWhiteSpace(year) && string.IsNullOrWhiteSpace(weekNumber))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(year))
        {
            return $"W{weekNumber}";
        }

        if (string.IsNullOrWhiteSpace(weekNumber))
        {
            return year;
        }

        return $"{year}-W{weekNumber}";
    }

    private static int ParseInt(string value)
    {
        return int.TryParse(value, out int parsed)
            ? parsed
            : 0;
    }

    private static decimal ParseDecimal(string value)
    {
        return decimal.TryParse(value, out decimal parsed)
            ? parsed
            : 0m;
    }

    private static bool ParseBool(string value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }

    private static string ToXmlContent(string content, WorkspaceDocumentFormat format)
    {
        if (format != WorkspaceDocumentFormat.NativeXml)
        {
            throw new InvalidOperationException($"Workspace format '{format}' is not supported.");
        }

        if (!string.IsNullOrEmpty(content) && content[0] == '\uFEFF')
        {
            return content[1..];
        }

        return content;
    }
}
