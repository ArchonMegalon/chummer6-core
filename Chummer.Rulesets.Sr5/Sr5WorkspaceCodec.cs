using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using System.Text;

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
                Notes: command.Notes)));

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
        return new DataExportBundle(
            Summary: ParseSummary(envelope),
            Profile: TryParseExportSection<CharacterProfileSection>("profile", envelope),
            Progress: TryParseExportSection<CharacterProgressSection>("progress", envelope),
            Attributes: TryParseExportSection<CharacterAttributesSection>("attributes", envelope),
            Skills: TryParseExportSection<CharacterSkillsSection>("skills", envelope),
            Inventory: TryParseExportSection<CharacterInventorySection>("inventory", envelope),
            Qualities: TryParseExportSection<CharacterQualitiesSection>("qualities", envelope),
            Contacts: TryParseExportSection<CharacterContactsSection>("contacts", envelope));
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
}
