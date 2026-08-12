using System.Text;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Workspaces;

public readonly record struct CharacterWorkspaceId(string Value)
{
    public override string ToString() => Value;
}

public enum WorkspaceOperationOutcome
{
    Success = 0,
    Missing = 1,
    Conflict = 2,
    Corrupt = 3,
    Unavailable = 4
}

public enum WorkspaceDocumentFormat
{
    NativeXml = 0,
    Chum5Xml = NativeXml,
    Json = 1
}

public sealed record WorkspaceDocumentState
{
    public WorkspaceDocumentState(
        string rulesetId,
        int schemaVersion,
        string payloadKind,
        string payload)
    {
        RulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty;
        SchemaVersion = schemaVersion;
        PayloadKind = payloadKind;
        Payload = payload;
    }

    public WorkspaceDocumentState(WorkspacePayloadEnvelope envelope)
        : this(envelope.RulesetId, envelope.SchemaVersion, envelope.PayloadKind, envelope.Payload)
    {
    }

    public string RulesetId { get; init; }

    public int SchemaVersion { get; init; }

    public string PayloadKind { get; init; }

    public string Payload { get; init; }

    public WorkspacePayloadEnvelope ToEnvelope()
    {
        return new WorkspacePayloadEnvelope(
            RulesetId,
            SchemaVersion,
            PayloadKind,
            Payload);
    }
}

public sealed record WorkspaceDocument(
    WorkspaceDocumentState State,
    WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml)
{
    public WorkspaceDocument(
        WorkspacePayloadEnvelope PayloadEnvelope,
        WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml)
        : this(new WorkspaceDocumentState(PayloadEnvelope), Format)
    {
    }

    public WorkspaceDocument(
        string Content,
        string RulesetId,
        WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml)
        : this(
            new WorkspaceDocumentState(
                rulesetId: RulesetId,
                schemaVersion: 1,
                payloadKind: "workspace",
                payload: Content),
            Format)
    {
    }

    public WorkspacePayloadEnvelope PayloadEnvelope => State.ToEnvelope();

    public string Content => State.Payload;

    public string RulesetId => State.RulesetId;

    public int SchemaVersion => State.SchemaVersion;

    public string PayloadKind => State.PayloadKind;
}

public sealed record WorkspaceImportDocument(
    string Content,
    string RulesetId,
    WorkspaceDocumentFormat Format = WorkspaceDocumentFormat.NativeXml)
{
    public static WorkspaceImportDocument FromUtf8Bytes(
        byte[] contentBytes,
        string rulesetId,
        WorkspaceDocumentFormat format = WorkspaceDocumentFormat.NativeXml)
    {
        string content = Encoding.UTF8.GetString(contentBytes);
        return new WorkspaceImportDocument(content, rulesetId, format);
    }
}

public sealed record WorkspaceWorkflowDeterministicReceipt(
    string ParityFamilyId,
    string ReceiptId,
    string ReceiptScopeId,
    string WorkspaceId,
    string RulesetId,
    string WorkflowStatePosture,
    int CoveragePercent,
    int InitiateGrade,
    int ContactCount,
    int LifestyleCount,
    IReadOnlyList<string> CoveredWorkflowRouteIds,
    IReadOnlyList<string> MissingWorkflowRouteIds,
    bool HasNotesField,
    bool HasGameNotesField,
    bool HasNotesContent,
    bool HasGameNotesContent);

public sealed record WorkspaceExchangeDeterministicReceipt(
    string ParityFamilyId,
    string ReceiptId,
    string SurfaceKind,
    string OutputDescriptor,
    string RulesetId,
    string RuleEnvironmentPosture,
    string RuleEnvironmentSummary,
    string RuleEnvironmentFingerprint,
    string SettingsProfile,
    string GameplayOption,
    string GameEdition,
    IReadOnlyList<string> BannedWareGrades);

public sealed record WorkspaceSaveReceipt(
    CharacterWorkspaceId Id,
    int DocumentLength,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceDownloadReceipt(
    CharacterWorkspaceId Id,
    WorkspaceDocumentFormat Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null);

public sealed record WorkspaceExportReceipt(
    CharacterWorkspaceId Id,
    WorkspaceDocumentFormat Format,
    string ContentBase64,
    string FileName,
    int DocumentLength,
    string RulesetId,
    string PackageId = "",
    DateTimeOffset ExportedAtUtc = default,
    WorkspacePortabilityReceipt? Portability = null,
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    WorkspaceExchangeDeterministicReceipt? ExchangeDeterministicReceipt = null);

public sealed record WorkspacePrintReceipt(
    CharacterWorkspaceId Id,
    string ContentBase64,
    string FileName,
    string MimeType,
    int DocumentLength,
    string Title,
    string RulesetId,
    string ReceiptId = "",
    WorkspaceWorkflowDeterministicReceipt? WorkflowDeterministicReceipt = null,
    WorkspaceExchangeDeterministicReceipt? ExchangeDeterministicReceipt = null);

public sealed record WorkspaceListItem(
    CharacterWorkspaceId Id,
    CharacterFileSummary Summary,
    DateTimeOffset LastUpdatedUtc,
    string RulesetId,
    bool HasSavedWorkspace = false,
    long ContentRevision = 0,
    long SavedRevision = 0);

public sealed record WorkspaceDocumentSnapshot(
    CharacterWorkspaceId Id,
    WorkspaceDocument Document,
    DateTimeOffset LastUpdatedUtc,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceRevisionReceipt(
    CharacterWorkspaceId Id,
    long ContentRevision,
    long SavedRevision);

public sealed record WorkspaceMetadataResult(
    CharacterProfileSection Profile,
    long ContentRevision,
    long SavedRevision);

public static class WorkspaceRevisionEtag
{
    public static string Format(long contentRevision)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(contentRevision);

        return $"\"{contentRevision}\"";
    }

    public static bool TryParseStrong(string? value, out long contentRevision)
    {
        contentRevision = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        ReadOnlySpan<char> candidate = value.AsSpan().Trim();
        if (candidate.Length < 3
            || candidate[0] != '"'
            || candidate[^1] != '"'
            || candidate.Contains(','))
        {
            return false;
        }

        ReadOnlySpan<char> opaqueTag = candidate[1..^1];
        if (opaqueTag.IsEmpty || opaqueTag[0] == '0')
        {
            return false;
        }

        foreach (char character in opaqueTag)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return long.TryParse(opaqueTag, out contentRevision)
               && contentRevision > 0;
    }
}

public sealed record UpdateWorkspaceMetadata(
    string? Name,
    string? Alias,
    string? Notes);

public sealed record CommandResult<T>(
    bool Success,
    T? Value,
    string? Error,
    WorkspaceOperationOutcome? OperationOutcome = null)
    where T : class
{
    public WorkspaceOperationOutcome Outcome => OperationOutcome
        ?? (Success ? WorkspaceOperationOutcome.Success : WorkspaceOperationOutcome.Unavailable);
}
