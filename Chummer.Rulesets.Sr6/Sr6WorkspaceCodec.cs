using System.Text;
using System.Xml.Linq;
using Chummer.Application.BuildLab;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Rulesets.Sr6;

public sealed class Sr6WorkspaceCodec : IRulesetWorkspaceCodec
{
    public const int SchemaVersion = 1;
    public const string Sr6PayloadKind = "sr6/chum6-xml";
    private static readonly HashSet<string> SharedSectionIds = new(StringComparer.Ordinal)
    {
        "profile",
        "progress",
        "karmasummary",
        "conditionmonitor",
        "rules",
        "build",
        "movement",
        "awakening",
        "skills",
        "attributes",
        "attributedetails",
        "limitmodifiers",
        "inventory",
        "gear",
        "weapons",
        "weaponaccessories",
        "armors",
        "armormods",
        "cyberwares",
        "vehicles",
        "vehiclemods",
        "gearlocations",
        "armorlocations",
        "weaponlocations",
        "vehiclelocations",
        "drugs",
        "spells",
        "powers",
        "complexforms",
        "spirits",
        "sprites",
        "foci",
        "aiprograms",
        "martialarts",
        "metamagics",
        "arts",
        "initiationgrades",
        "critterpowers",
        "mentorspirits",
        "qualities",
        "contacts",
        "relationships",
        "enemies",
        "pets",
        "lifestyles",
        "sources",
        "expenses",
        "calendar",
        "improvements",
        "customdatadirectorynames",
        "spelldefense"
    };

    private readonly ICharacterFileQueries _characterFileQueries;
    private readonly ICharacterSectionQueries _sectionQueries;
    private readonly ICharacterMetadataCommands _metadataCommands;

    public Sr6WorkspaceCodec(
        ICharacterFileQueries characterFileQueries,
        ICharacterSectionQueries sectionQueries,
        ICharacterMetadataCommands metadataCommands)
    {
        _characterFileQueries = characterFileQueries;
        _sectionQueries = sectionQueries;
        _metadataCommands = metadataCommands;
    }

    public string RulesetId => RulesetDefaults.Sr6;

    int IRulesetWorkspaceCodec.SchemaVersion => SchemaVersion;

    public string PayloadKind => Sr6PayloadKind;

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
        => _characterFileQueries.ParseSummary(new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));

    public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
    {
        string xml = ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml);
        CharacterDocument document = new(xml);
        string normalizedSectionId = NormalizeSectionId(sectionId);

        return normalizedSectionId switch
        {
            "build-lab" => BuildLabWorkspaceProjectionFactory.Create(
                profile: ParseRequiredSection<CharacterProfileSection>("profile", document),
                progress: ParseRequiredSection<CharacterProgressSection>("progress", document),
                rules: ParseRequiredSection<CharacterRulesSection>("rules", document),
                build: ParseRequiredSection<CharacterBuildSection>("build", document),
                skills: ParseRequiredSection<CharacterSkillsSection>("skills", document),
                awakening: ParseRequiredSection<CharacterAwakeningSection>("awakening", document),
                rulesetId: RulesetDefaults.Sr6),
            _ => SharedSectionIds.Contains(normalizedSectionId)
                ? _sectionQueries.ParseSection(normalizedSectionId, document)
                : CreateFallbackSection(sectionId)
        };
    }

    public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
        => _characterFileQueries.Validate(new CharacterDocument(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml)));

    public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
    {
        string normalizedXml = EnsureMetadataContractFields(ToXmlContent(envelope.Payload, WorkspaceDocumentFormat.NativeXml));
        UpdateCharacterMetadataResult result = _metadataCommands.UpdateMetadata(new UpdateCharacterMetadataCommand(
            Document: new CharacterDocument(normalizedXml),
            Update: new CharacterMetadataUpdate(
                Name: command.Name?.Trim() ?? string.Empty,
                Alias: command.Alias?.Trim() ?? string.Empty,
                Notes: command.Notes?.Trim() ?? string.Empty)));

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
            WorkspaceDocumentFormat.NativeXml => $"{id.Value}.chum6",
            _ => throw new InvalidOperationException($"Workspace format '{format}' is not supported.")
        };

        return new WorkspaceDownloadReceipt(
            Id: id,
            Format: format,
            ContentBase64: contentBase64,
            FileName: fileName,
            DocumentLength: xml.Length,
            RulesetId: RulesetDefaults.NormalizeOptional(envelope.RulesetId) ?? RulesetDefaults.Sr6);
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

    private TSection ParseRequiredSection<TSection>(string sectionId, CharacterDocument document)
        where TSection : class
        => _sectionQueries.ParseSection(sectionId, document) as TSection
           ?? throw new InvalidOperationException($"SR6 section '{sectionId}' did not project '{typeof(TSection).Name}'.");

    private static Dictionary<string, object?> CreateFallbackSection(string sectionId)
        => new(StringComparer.Ordinal)
        {
            ["sectionId"] = sectionId,
            ["rulesetId"] = RulesetDefaults.Sr6
        };

    private static string NormalizeSectionId(string? sectionId)
        => (sectionId ?? string.Empty).Trim().ToLowerInvariant();

    private static string EnsureMetadataContractFields(string xml)
    {
        XDocument document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        XElement root = document.Root
            ?? throw new InvalidOperationException("Root node must be <character>.");
        if (!string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Root node must be <character>.");
        }

        EnsureElement(root, "name", string.Empty);
        EnsureElement(root, "alias", string.Empty);
        EnsureElement(root, "metatype", string.Empty);
        EnsureElement(root, "buildmethod", string.Empty);
        EnsureElement(root, "createdversion", string.Empty);
        EnsureElement(root, "appversion", string.Empty);
        EnsureElement(root, "karma", "0");
        EnsureElement(root, "nuyen", "0");
        EnsureElement(root, "created", "False");

        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static void EnsureElement(XElement root, string name, string fallbackValue)
    {
        XElement? element = root.Element(name);
        if (element is null)
        {
            root.Add(new XElement(name, fallbackValue));
            return;
        }

        if (string.IsNullOrWhiteSpace(element.Value))
        {
            element.Value = fallbackValue;
        }
    }

    private static string ToXmlContent(string content, WorkspaceDocumentFormat format)
    {
        if (format == WorkspaceDocumentFormat.Json)
        {
            return HeroLabShadowrunImporter.ConvertOnlineJsonToNativeXml(content, "workspace-import.json");
        }

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
