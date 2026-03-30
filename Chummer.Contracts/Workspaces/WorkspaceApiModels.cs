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
    WorkspacePortabilityReceipt? Portability = null);

public sealed record WorkspaceImportResponse(
    string Id,
    CharacterFileSummary Summary,
    string RulesetId,
    string ImportReceiptId = "",
    DateTimeOffset ImportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null);

public sealed record WorkspaceListItemResponse(
    string Id,
    CharacterFileSummary Summary,
    DateTimeOffset LastUpdatedUtc,
    string RulesetId,
    bool HasSavedWorkspace = false);

public sealed record WorkspaceListResponse(
    int Count,
    IReadOnlyList<WorkspaceListItemResponse> Workspaces);

public sealed record WorkspaceMetadataResponse(
    CharacterProfileSection Profile);

public sealed record WorkspaceSaveResponse(
    string Id,
    int DocumentLength,
    string RulesetId);

public sealed record WorkspaceDownloadResponse(
    string Id,
    string Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId);

public sealed record WorkspaceExportResponse(
    string Id,
    string Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId,
    string PackageId = "",
    DateTimeOffset ExportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null);

public sealed record WorkspacePrintResponse(
    string Id,
    string ContentBase64,
    string FileName,
    string MimeType,
    int DocumentLength,
    string Title,
    string RulesetId);
