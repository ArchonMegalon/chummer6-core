using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using System.Text;
using System.Xml.Linq;

namespace Chummer.Rulesets.Sr5;

public sealed class Sr5WorkspaceCodec : IRulesetWorkspaceCodec
{
    public const int SchemaVersion = 1;
    public const string Sr5PayloadKind = "sr5/chum5-xml";
    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSectionQueries _characterSectionQueries;
    private readonly ICharacterMetadataCommands _characterMetadataCommands;

    public Sr5WorkspaceCodec(
        ICharacterFileQueries characterFileQueries,
        ICharacterSectionQueries characterSectionQueries,
        ICharacterMetadataCommands characterMetadataCommands)
    {
        _characterFileQueries = characterFileQueries;
        _characterSectionQueries = characterSectionQueries;
        _characterMetadataCommands = characterMetadataCommands;
    }

    public string RulesetId => RulesetDefaults.Sr5;

    int IRulesetWorkspaceCodec.SchemaVersion => SchemaVersion;

    public string PayloadKind => Sr5PayloadKind;

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
        return _characterSectionQueries.ParseSection(sectionId, new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));
    }

    public CharacterOverviewProjection ParseOverview(WorkspacePayloadEnvelope envelope)
        => _characterSectionQueries.ParseOverview(
            new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));

    public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
    {
        return _characterFileQueries.Validate(new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));
    }

    public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
    {
        UpdateCharacterMetadataResult result = _characterMetadataCommands.UpdateMetadata(new UpdateCharacterMetadataCommand(
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
            WorkspaceDocumentFormat.NativeXml => $"{id.Value}.chum5",
            _ => throw new InvalidOperationException($"Workspace format '{format}' is not supported.")
        };

        return new WorkspaceDownloadReceipt(
            Id: id,
            Format: format,
            ContentBase64: contentBase64,
            FileName: fileName,
            DocumentLength: xml.Length,
            RulesetId: RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr5);
    }

    public DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope)
    {
        string xml = ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml);
        if (TryBuildSinglePassExportBundle(xml, out DataExportBundle? bundle) && bundle is not null)
        {
            return bundle with
            {
                Lifestyles = TryParseExportSection<CharacterLifestylesSection>("lifestyles", envelope)
            };
        }

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

    private static bool TryBuildSinglePassExportBundle(string xml, out DataExportBundle? bundle)
    {
        try
        {
            XElement character = XDocument.Parse(xml, LoadOptions.None).Root
                ?? throw new InvalidOperationException("SR5 workspace export XML did not contain a root element.");
            CharacterProfileSection profile = BuildProfileSection(character);
            CharacterProgressSection progress = BuildProgressSection(character);
            CharacterFileSummary summary = new(
                Name: profile.Name,
                Alias: profile.Alias,
                Metatype: profile.Metatype,
                BuildMethod: profile.BuildMethod,
                CreatedVersion: profile.CreatedVersion,
                AppVersion: profile.AppVersion,
                Karma: progress.Karma,
                Nuyen: progress.Nuyen,
                Created: profile.Created);

            bundle = new DataExportBundle(
                Summary: summary,
                Profile: profile,
                Progress: progress,
                Attributes: BuildAttributesSection(character),
                Skills: BuildSkillsSection(character),
                Inventory: BuildInventorySection(character),
                Qualities: BuildQualitiesSection(character),
                Contacts: BuildContactsSection(character));
            return true;
        }
        catch
        {
            bundle = null;
            return false;
        }
    }

    private static CharacterProfileSection BuildProfileSection(XElement character)
    {
        string alias = ReadValue(character, "alias");
        string name = ReadValue(character, "name");
        return new CharacterProfileSection(
            Name: string.IsNullOrWhiteSpace(name) ? alias : name,
            Alias: alias,
            PlayerName: ReadValue(character, "playername"),
            Metatype: ReadValue(character, "metatype"),
            Metavariant: ReadValue(character, "metavariant"),
            Sex: ReadValue(character, "sex"),
            Age: ReadValue(character, "age"),
            Height: ReadValue(character, "height"),
            Weight: ReadValue(character, "weight"),
            Hair: ReadValue(character, "hair"),
            Eyes: ReadValue(character, "eyes"),
            Skin: ReadValue(character, "skin"),
            Concept: ReadValue(character, "concept"),
            Description: ReadValue(character, "description"),
            Background: ReadValue(character, "background"),
            CreatedVersion: ReadValue(character, "createdversion"),
            AppVersion: ReadValue(character, "appversion"),
            BuildMethod: ReadValue(character, "buildmethod"),
            GameplayOption: ReadValue(character, "gameplayoption"),
            Created: ParseBool(ReadValue(character, "created")),
            Adept: ParseBool(ReadValue(character, "adept")),
            Magician: ParseBool(ReadValue(character, "magician")),
            Technomancer: ParseBool(ReadValue(character, "technomancer")),
            AI: ParseBool(ReadValue(character, "ai")),
            MainMugshotIndex: ParseInt(ReadValue(character, "mainmugshotindex")),
            MugshotCount: character.Element("mugshots")?.Elements("mugshot").Count() ?? 0)
        {
            CharacterNotes = ReadValue(character, "notes"),
            GameNotes = ReadValue(character, "gamenotes"),
            GroupNotes = ReadValue(character, "groupnotes")
        };
    }

    private static CharacterProgressSection BuildProgressSection(XElement character)
        => new(
            Karma: ParseDecimal(ReadValue(character, "karma")),
            Nuyen: ParseDecimal(ReadValue(character, "nuyen")),
            StartingNuyen: ParseDecimal(ReadValue(character, "startingnuyen")),
            StreetCred: ParseInt(ReadValue(character, "streetcred")),
            Notoriety: ParseInt(ReadValue(character, "notoriety")),
            PublicAwareness: ParseInt(ReadValue(character, "publicawareness")),
            BurntStreetCred: ParseInt(ReadValue(character, "burntstreetcred")),
            BuildKarma: ParseInt(ReadValue(character, "buildkarma")),
            TotalAttributes: ParseInt(ReadValue(character, "totalattributes")),
            TotalSpecial: ParseInt(ReadValue(character, "totalspecial")),
            PhysicalCmFilled: ParseInt(ReadValue(character, "physicalcmfilled")),
            StunCmFilled: ParseInt(ReadValue(character, "stuncmfilled")),
            TotalEssence: ParseDecimal(ReadValue(character, "totaless")),
            InitiateGrade: ParseInt(ReadValue(character, "initiategrade")),
            SubmersionGrade: ParseInt(ReadValue(character, "submersiongrade")),
            MagEnabled: ParseBool(ReadValue(character, "magenabled")),
            ResEnabled: ParseBool(ReadValue(character, "resenabled")),
            DepEnabled: ParseBool(ReadValue(character, "depenabled")));

    private static CharacterAttributesSection BuildAttributesSection(XElement character)
    {
        CharacterAttributeSummary[] attributes = character
            .Element("attributes")?
            .Elements("attribute")
            .Select(attribute => new CharacterAttributeSummary(
                Name: ReadValue(attribute, "name"),
                BaseValue: ParseInt(ReadValue(attribute, "base")),
                TotalValue: ParseInt(ReadValue(attribute, "totalvalue"))))
            .ToArray()
            ?? [];

        return new CharacterAttributesSection(attributes.Length, attributes);
    }

    private static CharacterSkillsSection BuildSkillsSection(XElement character)
    {
        CharacterSkillSummary[] skills = character
            .Element("newskills")?
            .Element("skills")?
            .Elements("skill")
            .Select(skill => new CharacterSkillSummary(
                Guid: ReadValue(skill, "guid"),
                Suid: ReadValue(skill, "suid"),
                Category: ReadValue(skill, "skillcategory"),
                IsKnowledge: ParseBool(ReadValue(skill, "isknowledge")),
                BaseValue: ParseInt(ReadValue(skill, "base")),
                KarmaValue: ParseInt(ReadValue(skill, "karma")),
                Specializations: skill.Element("specs")?
                    .Elements("spec")
                    .Select(spec => ReadValue(spec, "name"))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToArray() ?? [],
                Name: FirstNonBlank(ReadValue(skill, "name"), ReadValue(skill, "suid"))))
            .ToArray()
            ?? [];

        return new CharacterSkillsSection(
            Count: skills.Length,
            KnowledgeCount: skills.Count(skill => skill.IsKnowledge),
            Skills: skills);
    }

    private static CharacterInventorySection BuildInventorySection(XElement character)
    {
        string[] gears = ReadItemNames(character, "gears", "gear");
        string[] weapons = ReadItemNames(character, "weapons", "weapon");
        string[] armors = ReadItemNames(character, "armors", "armor");
        string[] cyberwares = ReadItemNames(character, "cyberwares", "cyberware");
        string[] vehicles = ReadItemNames(character, "vehicles", "vehicle");

        return new CharacterInventorySection(
            GearCount: gears.Length,
            WeaponCount: weapons.Length,
            ArmorCount: armors.Length,
            CyberwareCount: cyberwares.Length,
            VehicleCount: vehicles.Length,
            GearNames: gears,
            WeaponNames: weapons,
            ArmorNames: armors,
            CyberwareNames: cyberwares,
            VehicleNames: vehicles);
    }

    private static CharacterQualitiesSection BuildQualitiesSection(XElement character)
    {
        CharacterQualitySummary[] qualities = character
            .Element("qualities")?
            .Elements("quality")
            .Select(quality => new CharacterQualitySummary(
                Name: ReadValue(quality, "name"),
                Source: ReadValue(quality, "source"),
                BP: ParseInt(ReadValue(quality, "bp"))))
            .ToArray()
            ?? [];

        return new CharacterQualitiesSection(qualities.Length, qualities);
    }

    private static CharacterContactsSection BuildContactsSection(XElement character)
    {
        CharacterContactSummary[] contacts = character
            .Element("contacts")?
            .Elements("contact")
            .Select(contact => new CharacterContactSummary(
                Name: ReadValue(contact, "name"),
                Role: ReadValue(contact, "role"),
                Location: ReadValue(contact, "location"),
                Connection: ParseInt(ReadValue(contact, "connection")),
                Loyalty: ParseInt(ReadValue(contact, "loyalty"))))
            .ToArray()
            ?? [];

        return new CharacterContactsSection(contacts.Length, contacts);
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

    private static string[] ReadItemNames(XElement character, string collectionName, string itemName)
        => character
            .Element(collectionName)?
            .Elements(itemName)
            .Select(item => ReadValue(item, "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray()
        ?? [];

    private static string ReadValue(XElement element, string childName)
        => element.Element(childName)?.Value.Trim() ?? string.Empty;

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static int ParseInt(string value)
        => int.TryParse(value, out int parsed) ? parsed : 0;

    private static decimal ParseDecimal(string value)
        => decimal.TryParse(value, out decimal parsed) ? parsed : 0m;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed;
}
