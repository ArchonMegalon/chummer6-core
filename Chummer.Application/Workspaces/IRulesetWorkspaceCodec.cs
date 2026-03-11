using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public interface IRulesetWorkspaceCodec
{
    string RulesetId { get; }

    int SchemaVersion { get; }

    string PayloadKind { get; }

    WorkspacePayloadEnvelope WrapImport(string rulesetId, WorkspaceImportDocument document);

    CharacterFileSummary ParseSummary(WorkspacePayloadEnvelope envelope);

    object ParseSection(string sectionId, WorkspacePayloadEnvelope envelope);

    CharacterValidationResult Validate(WorkspacePayloadEnvelope envelope);

    WorkspacePayloadEnvelope UpdateMetadata(WorkspacePayloadEnvelope envelope, UpdateWorkspaceMetadata command);

    WorkspaceDownloadReceipt BuildDownload(
        CharacterWorkspaceId id,
        WorkspacePayloadEnvelope envelope,
        WorkspaceDocumentFormat format);

    DataExportBundle BuildExportBundle(WorkspacePayloadEnvelope envelope);
}
