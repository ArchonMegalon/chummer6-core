using System.Text;
using System.Xml.Linq;
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
    {
        XElement root = ParseRoot(envelope.Payload);
        return new CharacterFileSummary(
            Name: ElementValue(root, "name"),
            Alias: ElementValue(root, "alias"),
            Metatype: ElementValue(root, "metatype"),
            BuildMethod: ElementValue(root, "buildmethod"),
            CreatedVersion: ElementValue(root, "createdversion"),
            AppVersion: ElementValue(root, "appversion"),
            Karma: DecimalValue(root, "karma"),
            Nuyen: DecimalValue(root, "nuyen"),
            Created: BoolValue(root, "created"));
    }

    public object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope)
    {
        CharacterFileSummary summary = ParseSummary(envelope);
        return sectionId switch
        {
            "profile" => new CharacterProfileSection(
                Name: summary.Name,
                Alias: summary.Alias,
                PlayerName: string.Empty,
                Metatype: summary.Metatype,
                Metavariant: string.Empty,
                Sex: string.Empty,
                Age: string.Empty,
                Height: string.Empty,
                Weight: string.Empty,
                Hair: string.Empty,
                Eyes: string.Empty,
                Skin: string.Empty,
                Concept: string.Empty,
                Description: string.Empty,
                Background: string.Empty,
                CreatedVersion: summary.CreatedVersion,
                AppVersion: summary.AppVersion,
                BuildMethod: summary.BuildMethod,
                GameplayOption: string.Empty,
                Created: summary.Created,
                Adept: false,
                Magician: false,
                Technomancer: false,
                AI: false,
                MainMugshotIndex: -1,
                MugshotCount: 0),
            "progress" => new CharacterProgressSection(
                Karma: summary.Karma,
                Nuyen: summary.Nuyen,
                StartingNuyen: 0m,
                StreetCred: 0,
                Notoriety: 0,
                PublicAwareness: 0,
                BurntStreetCred: 0,
                BuildKarma: 0,
                TotalAttributes: 0,
                TotalSpecial: 0,
                PhysicalCmFilled: 0,
                StunCmFilled: 0,
                TotalEssence: 0m,
                InitiateGrade: 0,
                SubmersionGrade: 0,
                MagEnabled: false,
                ResEnabled: false,
                DepEnabled: false),
            "attributes" => new CharacterAttributesSection(0, Array.Empty<CharacterAttributeSummary>()),
            "skills" => new CharacterSkillsSection(0, 0, Array.Empty<CharacterSkillSummary>()),
            "inventory" => new CharacterInventorySection(
                GearCount: 0,
                WeaponCount: 0,
                ArmorCount: 0,
                CyberwareCount: 0,
                VehicleCount: 0,
                GearNames: Array.Empty<string>(),
                WeaponNames: Array.Empty<string>(),
                ArmorNames: Array.Empty<string>(),
                CyberwareNames: Array.Empty<string>(),
                VehicleNames: Array.Empty<string>()),
            "qualities" => new CharacterQualitiesSection(0, Array.Empty<CharacterQualitySummary>()),
            "contacts" => new CharacterContactsSection(0, Array.Empty<CharacterContactSummary>()),
            "rules" => new CharacterRulesSection(
                GameEdition: "SR6",
                Settings: string.Empty,
                GameplayOption: string.Empty,
                GameplayOptionQualityLimit: 0,
                MaxNuyen: 0,
                MaxKarma: 0,
                ContactMultiplier: 0,
                BannedWareGrades: Array.Empty<string>()),
            _ => new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["sectionId"] = sectionId,
                ["rulesetId"] = RulesetDefaults.Sr6
            }
        };
    }

    public CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope)
    {
        try
        {
            ParseRoot(envelope.Payload);
            return new CharacterValidationResult(true, Array.Empty<CharacterValidationIssue>());
        }
        catch (Exception ex)
        {
            return new CharacterValidationResult(
                false,
                [new CharacterValidationIssue("error", "sr6.invalid_xml", ex.Message, "/character")]);
        }
    }

    public WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command)
    {
        XElement root = ParseRoot(envelope.Payload);
        SetElementValue(root, "name", command.Name);
        SetElementValue(root, "alias", command.Alias);
        SetElementValue(root, "notes", command.Notes);

        return envelope with
        {
            SchemaVersion = envelope.SchemaVersion > 0 ? envelope.SchemaVersion : SchemaVersion,
            PayloadKind = string.IsNullOrWhiteSpace(envelope.PayloadKind) ? PayloadKind : envelope.PayloadKind,
            Payload = root.ToString(SaveOptions.DisableFormatting)
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
            Profile: ParseSection("profile", envelope) as CharacterProfileSection,
            Progress: ParseSection("progress", envelope) as CharacterProgressSection,
            Attributes: ParseSection("attributes", envelope) as CharacterAttributesSection,
            Skills: ParseSection("skills", envelope) as CharacterSkillsSection,
            Inventory: ParseSection("inventory", envelope) as CharacterInventorySection,
            Qualities: ParseSection("qualities", envelope) as CharacterQualitiesSection,
            Contacts: ParseSection("contacts", envelope) as CharacterContactsSection);
    }

    private static XElement ParseRoot(string payload)
    {
        string xml = string.IsNullOrWhiteSpace(payload)
            ? "<character />"
            : payload;
        return XElement.Parse(xml, LoadOptions.PreserveWhitespace);
    }

    private static string ElementValue(XElement root, string name)
    {
        return root.Element(name)?.Value?.Trim() ?? string.Empty;
    }

    private static decimal DecimalValue(XElement root, string name)
    {
        return decimal.TryParse(ElementValue(root, name), out decimal value)
            ? value
            : 0m;
    }

    private static bool BoolValue(XElement root, string name)
    {
        return bool.TryParse(ElementValue(root, name), out bool value) && value;
    }

    private static void SetElementValue(XElement root, string name, string? value)
    {
        XElement? element = root.Element(name);
        if (element is null)
        {
            element = new XElement(name);
            root.Add(element);
        }

        element.Value = value?.Trim() ?? string.Empty;
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
