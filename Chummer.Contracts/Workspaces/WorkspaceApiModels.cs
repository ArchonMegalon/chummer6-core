using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Workspaces;

public sealed record WorkspaceImportRequest(
    string? ContentBase64,
    string? Format,
    string? Xml,
    string? RulesetId = null);

public sealed record WorkspaceImportResult(
    CharacterWorkspaceId Id,
    CharacterFileSummary Summary,
    string RulesetId,
    string ImportReceiptId = "",
    DateTimeOffset ImportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null,
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceImportResponse(
    string Id,
    CharacterFileSummary Summary,
    string RulesetId,
    string ImportReceiptId = "",
    DateTimeOffset ImportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null,
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceListItemResponse(
    string Id,
    CharacterFileSummary Summary,
    DateTimeOffset LastUpdatedUtc,
    string RulesetId,
    bool HasSavedWorkspace = false,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceListResponse(
    int Count,
    IReadOnlyList<WorkspaceListItemResponse> Workspaces);

public sealed record WorkspaceDocumentResponse(
    string Id,
    string Format,
    string ContentBase64,
    string RulesetId,
    int SchemaVersion,
    string PayloadKind,
    DateTimeOffset LastUpdatedUtc,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceDocumentReplaceRequest(
    string? ContentBase64,
    string? Format,
    string? RulesetId,
    int? SchemaVersion = null,
    string? PayloadKind = null);

public sealed record WorkspaceRevisionResponse(
    string Id,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceMetadataResponse(
    CharacterProfileSection Profile,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceSaveResponse(
    string Id,
    int DocumentLength,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceDownloadResponse(
    string Id,
    string Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null);

public sealed record WorkspaceExportResponse(
    string Id,
    string Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId,
    string PackageId = "",
    DateTimeOffset ExportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null,
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    WorkspaceExchangeDeterministicReceipt? ExchangeDeterministicReceipt = null);

public sealed record WorkspacePrintResponse(
    string Id,
    string ContentBase64,
    string FileName,
    string MimeType,
    int DocumentLength,
    string Title,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    WorkspaceExchangeDeterministicReceipt? ExchangeDeterministicReceipt = null);
