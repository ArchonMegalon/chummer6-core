using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Application.BuildLab;

namespace Chummer.Application.Workspaces;

public sealed class WorkspaceService : IWorkspaceService
{
    private const string DenseWorkbenchParityFamilyId = "family:initiative_action_notes_and_workflow_state";
    private const string ExchangeParityFamilyId = "family:sheet_export_print_viewer_and_exchange";
    private static readonly string[] CanonicalWorkflowRouteIds =
    [
        "workflow:workflow-state",
        "workflow:contacts",
        "workflow:lifestyles",
        "workflow:notes"
    ];
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IRulesetWorkspaceCodecResolver _workspaceCodecResolver;
    private readonly IWorkspaceImportRulesetDetector _workspaceImportRulesetDetector;

    public WorkspaceService(
        IWorkspaceStore workspaceStore,
        IRulesetWorkspaceCodecResolver workspaceCodecResolver,
        IWorkspaceImportRulesetDetector workspaceImportRulesetDetector)
    {
        _workspaceStore = workspaceStore;
        _workspaceCodecResolver = workspaceCodecResolver;
        _workspaceImportRulesetDetector = workspaceImportRulesetDetector;
    }

    public WorkspaceImportResult Import(WorkspaceImportDocument document)
    {
        return ImportCore(LocalStoreAccess(), document);
    }

    public WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document)
    {
        return ImportCore(ScopedStoreAccess(owner), document);
    }

    private WorkspaceImportResult ImportCore(
        WorkspaceStoreAccess access,
        WorkspaceImportDocument document)
    {
        string? rulesetId = RulesetDefaults.NormalizeOptional(document.RulesetId)
            ?? _workspaceImportRulesetDetector.Detect(document);
        if (rulesetId is null)
            throw new InvalidOperationException("Workspace ruleset is required or must be detectable from import content.");

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(rulesetId);
        WorkspacePayloadEnvelope envelope = codec.WrapImport(rulesetId, document);
        CharacterFileSummary summary = codec.ParseSummary(envelope);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        CharacterWorkspaceId id = new(Guid.NewGuid().ToString("N"));
        DateTimeOffset importedAtUtc = DateTimeOffset.UtcNow;
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(document.Content));
        string importReceiptId = BuildReceiptId("import", id.Value, payloadSha256);
        WorkspacePortabilityReceipt portability = BuildImportPortabilityReceipt(
            id,
            document,
            envelope.RulesetId,
            summary,
            importedAtUtc,
            payloadSha256);
        WorkspaceWorkflowDeterministicReceipt workflowReceipt = BuildWorkflowDeterministicReceipt(
            importReceiptId,
            id,
            envelope.RulesetId,
            bundle,
            envelope.Payload);

        WorkspaceStoreMutationResult created = access.CreateWorkspaceDocument(
            id,
            new WorkspaceDocument(
                PayloadEnvelope: envelope,
                Format: document.Format));
        if (!created.Success || created.Entry is not WorkspaceStoreEntry createdEntry)
        {
            throw new InvalidOperationException(created.Error ?? "Workspace could not be created.");
        }

        return new WorkspaceImportResult(
            Id: id,
            Summary: summary,
            RulesetId: envelope.RulesetId,
            ImportReceiptId: importReceiptId,
            ImportedAtUtc: importedAtUtc,
            Portability: portability,
            WorkflowDeterministicReceipt: workflowReceipt,
            ContentRevision: createdEntry.ContentRevision,
            SavedRevision: createdEntry.SavedRevision);
    }

    public IReadOnlyList<WorkspaceListItem> List(int? maxCount = null)
    {
        return ListCore(LocalStoreAccess(), maxCount);
    }

    public IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null)
    {
        return ListCore(ScopedStoreAccess(owner), maxCount);
    }

    private IReadOnlyList<WorkspaceListItem> ListCore(
        WorkspaceStoreAccess access,
        int? maxCount)
    {
        List<WorkspaceListItem> workspaces = [];
        int? normalizedMaxCount = maxCount is > 0 ? maxCount : null;

        foreach (WorkspaceStoreEntry entry in access.List())
        {
            if (normalizedMaxCount is not null && workspaces.Count >= normalizedMaxCount.Value)
            {
                break;
            }

            CharacterWorkspaceId id = entry.Id;
            WorkspaceStoreReadResult read = access.Get(id);
            if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
            {
                continue;
            }

            WorkspaceDocument document = stored.Document;

            WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
            CharacterFileSummary summary;
            try
            {
                IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
                summary = codec.ParseSummary(envelope);
            }
            catch
            {
                summary = new CharacterFileSummary(
                    Name: $"Workspace {id.Value}",
                    Alias: string.Empty,
                    Metatype: string.Empty,
                    BuildMethod: string.Empty,
                    CreatedVersion: string.Empty,
                    AppVersion: string.Empty,
                    Karma: 0m,
                    Nuyen: 0m,
                    Created: false);
            }

            workspaces.Add(new WorkspaceListItem(
                Id: id,
                Summary: summary,
                LastUpdatedUtc: stored.LastUpdatedUtc,
                RulesetId: envelope.RulesetId,
                HasSavedWorkspace: stored.SavedRevision > 0,
                ContentRevision: stored.ContentRevision,
                SavedRevision: stored.SavedRevision));
        }

        return workspaces;
    }

    public CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(CharacterWorkspaceId id)
    {
        return GetWorkspaceCore(LocalStoreAccess(), id);
    }

    public CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(OwnerScope owner, CharacterWorkspaceId id)
    {
        return GetWorkspaceCore(ScopedStoreAccess(owner), id);
    }

    private static CommandResult<WorkspaceDocumentSnapshot> GetWorkspaceCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            return StoreFailure<WorkspaceDocumentSnapshot>(read);
        }

        return new CommandResult<WorkspaceDocumentSnapshot>(
            Success: true,
            Value: new WorkspaceDocumentSnapshot(
                Id: id,
                Document: stored.Document,
                LastUpdatedUtc: stored.LastUpdatedUtc,
                ContentRevision: stored.ContentRevision,
                SavedRevision: stored.SavedRevision),
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    [Obsolete("Compatibility close reads once and performs one CAS delete. Pass expectedContentRevision; removal is queued for Stage C.")]
    public bool Close(CharacterWorkspaceId id)
    {
        return CloseCompatibility(LocalStoreAccess(), id);
    }

    [Obsolete("Compatibility close reads once and performs one CAS delete. Pass expectedContentRevision; removal is queued for Stage C.")]
    public bool Close(OwnerScope owner, CharacterWorkspaceId id)
    {
        return CloseCompatibility(ScopedStoreAccess(owner), id);
    }

    private static bool CloseCompatibility(WorkspaceStoreAccess access, CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return false;
        }

        return access.Delete(id, current.ContentRevision).Success;
    }

    public CommandResult<WorkspaceRevisionReceipt> Close(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return CloseCore(LocalStoreAccess(), id, expectedContentRevision);
    }

    public CommandResult<WorkspaceRevisionReceipt> Close(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return CloseCore(ScopedStoreAccess(owner), id, expectedContentRevision);
    }

    private static CommandResult<WorkspaceRevisionReceipt> CloseCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return StoreFailure<WorkspaceRevisionReceipt>(read);
        }

        return CloseCurrent(access, id, current, expectedContentRevision);
    }

    private static CommandResult<WorkspaceRevisionReceipt> CloseCurrent(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        WorkspaceStoredDocument current,
        long expectedContentRevision)
    {
        if (current.ContentRevision != expectedContentRevision)
        {
            return ConflictFailure<WorkspaceRevisionReceipt>();
        }

        WorkspaceRevisionReceipt receipt = new(
            id,
            current.ContentRevision,
            current.SavedRevision);
        WorkspaceStoreMutationResult deleted = access.Delete(id, expectedContentRevision);
        if (!deleted.Success)
        {
            return StoreFailure<WorkspaceRevisionReceipt>(deleted);
        }

        return new CommandResult<WorkspaceRevisionReceipt>(
            Success: true,
            Value: receipt,
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    public object? GetSection(CharacterWorkspaceId id, string sectionId)
    {
        return GetSectionCore(LocalStoreAccess(), id, sectionId);
    }

    public object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId)
    {
        return GetSectionCore(ScopedStoreAccess(owner), id, sectionId);
    }

    private object? GetSectionCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        string sectionId)
    {
        if (!TryResolveEnvelope(access, id, out WorkspacePayloadEnvelope envelope))
        {
            return null;
        }

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        object? section = codec.ParseSection(sectionId, envelope);
        return section is BuildLabConceptIntakeProjection projection
            ? BuildLabWorkspaceProjectionFactory.BindWorkspaceId(projection, id.Value)
            : section;
    }

    public CharacterFileSummary? GetSummary(CharacterWorkspaceId id)
    {
        return GetSummaryCore(LocalStoreAccess(), id);
    }

    public CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id)
    {
        return GetSummaryCore(ScopedStoreAccess(owner), id);
    }

    private CharacterFileSummary? GetSummaryCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        if (!TryResolveEnvelope(access, id, out WorkspacePayloadEnvelope envelope))
        {
            return null;
        }

        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        return codec.ParseSummary(envelope);
    }

    public CharacterValidationResult? Validate(CharacterWorkspaceId id)
    {
        return ValidateCore(LocalStoreAccess(), id);
    }

    public CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id)
    {
        return ValidateCore(ScopedStoreAccess(owner), id);
    }

    private CharacterValidationResult? ValidateCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            return null;
        }

        WorkspaceDocument document = stored.Document;
        try
        {
            if (document.State is null || !Enum.IsDefined(document.Format))
            {
                return InvalidDocument("workspace_envelope", "Workspace document envelope is not canonical.");
            }

            WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
            IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
            if (!string.Equals(envelope.RulesetId, codec.RulesetId, StringComparison.Ordinal)
                || envelope.SchemaVersion != codec.SchemaVersion
                || !string.Equals(envelope.PayloadKind, codec.PayloadKind, StringComparison.Ordinal))
            {
                return InvalidDocument(
                    "workspace_codec_contract",
                    "Workspace ruleset, schema, or payload kind does not match the canonical codec contract.");
            }

            // Exercise the same ruleset parser, schema validator, and download
            // materializer used by real open/export flows. A payload which is
            // merely well-formed text is not an acceptable recovery document.
            _ = codec.ParseSummary(envelope);
            CharacterValidationResult validation = codec.Validate(envelope);
            if (!validation.IsValid)
                return validation;

            _ = codec.BuildDownload(id, envelope, document.Format);
            return validation;
        }
        catch (Exception ex) when (ex is ArgumentException
            or FormatException
            or InvalidDataException
            or InvalidOperationException
            or JsonException)
        {
            return InvalidDocument("workspace_canonical_validation", "Workspace document cannot be opened by its canonical ruleset codec.");
        }
    }

    private static CharacterValidationResult InvalidDocument(string code, string message)
        => new(
            IsValid: false,
            Issues:
            [
                new CharacterValidationIssue(
                    Severity: "error",
                    Code: code,
                    Message: message,
                    Path: "workspace")
            ]);

    public CharacterProfileSection? GetProfile(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProfileSection>(LocalStoreAccess(), id, "profile");
    }

    public CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProfileSection>(ScopedStoreAccess(owner), id, "profile");
    }

    public CharacterProgressSection? GetProgress(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProgressSection>(LocalStoreAccess(), id, "progress");
    }

    public CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterProgressSection>(ScopedStoreAccess(owner), id, "progress");
    }

    public CharacterSkillsSection? GetSkills(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterSkillsSection>(LocalStoreAccess(), id, "skills");
    }

    public CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterSkillsSection>(ScopedStoreAccess(owner), id, "skills");
    }

    public CharacterRulesSection? GetRules(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterRulesSection>(LocalStoreAccess(), id, "rules");
    }

    public CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterRulesSection>(ScopedStoreAccess(owner), id, "rules");
    }

    public CharacterBuildSection? GetBuild(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterBuildSection>(LocalStoreAccess(), id, "build");
    }

    public CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterBuildSection>(ScopedStoreAccess(owner), id, "build");
    }

    public CharacterMovementSection? GetMovement(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterMovementSection>(LocalStoreAccess(), id, "movement");
    }

    public CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterMovementSection>(ScopedStoreAccess(owner), id, "movement");
    }

    public CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterAwakeningSection>(LocalStoreAccess(), id, "awakening");
    }

    public CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id)
    {
        return TryParseSection<CharacterAwakeningSection>(ScopedStoreAccess(owner), id, "awakening");
    }

    [Obsolete("Compatibility metadata update reads once and performs one CAS replace. Pass expectedContentRevision.")]
    public CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command)
    {
        return UpdateMetadataCompatibility(LocalStoreAccess(), id, command);
    }

    [Obsolete("Compatibility metadata update reads once and performs one CAS replace. Pass expectedContentRevision.")]
    public CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command)
    {
        return UpdateMetadataCompatibility(ScopedStoreAccess(owner), id, command);
    }

    private CommandResult<CharacterProfileSection> UpdateMetadataCompatibility(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        UpdateWorkspaceMetadata command)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return new CommandResult<CharacterProfileSection>(
                Success: false,
                Value: null,
                Error: read.Error,
                OperationOutcome: read.Outcome);
        }

        CommandResult<WorkspaceMetadataResult> result = UpdateMetadataCore(
            access,
            id,
            current,
            current.ContentRevision,
            command);
        return new CommandResult<CharacterProfileSection>(
            result.Success,
            result.Value?.Profile,
            result.Error,
            result.Outcome);
    }

    public CommandResult<WorkspaceMetadataResult> UpdateMetadata(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        return UpdateMetadataCore(LocalStoreAccess(), id, expectedContentRevision, command);
    }

    public CommandResult<WorkspaceMetadataResult> UpdateMetadata(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        return UpdateMetadataCore(
            ScopedStoreAccess(owner),
            id,
            expectedContentRevision,
            command);
    }

    private CommandResult<WorkspaceMetadataResult> UpdateMetadataCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return StoreFailure<WorkspaceMetadataResult>(read);
        }

        return UpdateMetadataCore(access, id, current, expectedContentRevision, command);
    }

    private CommandResult<WorkspaceMetadataResult> UpdateMetadataCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        WorkspaceStoredDocument current,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        if (current.ContentRevision != expectedContentRevision)
        {
            return ConflictFailure<WorkspaceMetadataResult>();
        }

        WorkspaceDocument document = current.Document;
        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        WorkspacePayloadEnvelope updatedEnvelope = codec.UpdateMetadata(envelope, command);

        // Complete every pluggable/data-dependent projection before the CAS. A codec that
        // returns an unusable updated payload must not commit bytes and then report failure.
        _ = codec.ParseSummary(updatedEnvelope);
        _ = codec.Validate(updatedEnvelope);
        CharacterProfileSection? profile = codec.ParseSection("profile", updatedEnvelope) as CharacterProfileSection;
        if (profile is null)
        {
            return new CommandResult<WorkspaceMetadataResult>(
                Success: false,
                Value: null,
                Error: "Profile section was not available after metadata update.",
                OperationOutcome: WorkspaceOperationOutcome.Corrupt);
        }

        WorkspaceStoreMutationResult replaced = access.ReplaceWorkspaceDocument(
            id,
            expectedContentRevision,
            CreateUpdatedDocument(document, updatedEnvelope));
        if (!replaced.Success || replaced.Entry is not WorkspaceStoreEntry entry)
        {
            return StoreFailure<WorkspaceMetadataResult>(replaced);
        }

        return new CommandResult<WorkspaceMetadataResult>(
            Success: true,
            Value: new WorkspaceMetadataResult(
                profile,
                entry.ContentRevision,
                entry.SavedRevision),
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    public CommandResult<WorkspaceRevisionReceipt> ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentCore(
            LocalStoreAccess(),
            id,
            expectedContentRevision,
            document);
    }

    public CommandResult<WorkspaceRevisionReceipt> ReplaceWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return ReplaceWorkspaceDocumentCore(
            ScopedStoreAccess(owner),
            id,
            expectedContentRevision,
            document);
    }

    private CommandResult<WorkspaceRevisionReceipt> ReplaceWorkspaceDocumentCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        try
        {
            if (document.State is null
                || !Enum.IsDefined(document.Format)
                || string.IsNullOrWhiteSpace(document.RulesetId)
                || document.SchemaVersion <= 0
                || string.IsNullOrWhiteSpace(document.PayloadKind))
            {
                return CorruptFailure<WorkspaceRevisionReceipt>();
            }

            WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
            IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
            _ = codec.ParseSummary(envelope);
            _ = codec.Validate(envelope);
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidDataException
                                   or InvalidOperationException
                                   or JsonException
                                   or System.Xml.XmlException)
        {
            return CorruptFailure<WorkspaceRevisionReceipt>();
        }

        WorkspaceStoreMutationResult replaced = access.ReplaceWorkspaceDocument(
            id,
            expectedContentRevision,
            document);
        if (!replaced.Success || replaced.Entry is not WorkspaceStoreEntry entry)
        {
            return StoreFailure<WorkspaceRevisionReceipt>(replaced);
        }

        return new CommandResult<WorkspaceRevisionReceipt>(
            Success: true,
            Value: new WorkspaceRevisionReceipt(
                entry.Id,
                entry.ContentRevision,
                entry.SavedRevision),
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    [Obsolete("Compatibility save reads once and performs one CAS checkpoint. Pass expectedContentRevision.")]
    public CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id)
    {
        return SaveCompatibility(LocalStoreAccess(), id);
    }

    [Obsolete("Compatibility save reads once and performs one CAS checkpoint. Pass expectedContentRevision.")]
    public CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id)
    {
        return SaveCompatibility(ScopedStoreAccess(owner), id);
    }

    private CommandResult<WorkspaceSaveReceipt> SaveCompatibility(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return StoreFailure<WorkspaceSaveReceipt>(read);
        }

        return SaveCore(access, id, current, current.ContentRevision);
    }

    public CommandResult<WorkspaceSaveReceipt> Save(
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return SaveCore(LocalStoreAccess(), id, expectedContentRevision);
    }

    public CommandResult<WorkspaceSaveReceipt> Save(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        return SaveCore(ScopedStoreAccess(owner), id, expectedContentRevision);
    }

    private CommandResult<WorkspaceSaveReceipt> SaveCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        long expectedContentRevision)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument current)
        {
            return StoreFailure<WorkspaceSaveReceipt>(read);
        }

        return SaveCore(access, id, current, expectedContentRevision);
    }

    private CommandResult<WorkspaceSaveReceipt> SaveCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        WorkspaceStoredDocument current,
        long expectedContentRevision)
    {
        if (current.ContentRevision != expectedContentRevision)
        {
            return ConflictFailure<WorkspaceSaveReceipt>();
        }

        WorkspaceDocument document = current.Document;
        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(envelope.Payload));
        string receiptId = BuildReceiptId("save", id.Value, payloadSha256);
        WorkspaceSaveReceipt receipt = new(
            Id: id,
            DocumentLength: envelope.Payload.Length,
            RulesetId: envelope.RulesetId,
            ReceiptId: receiptId,
            WorkflowDeterministicReceipt: BuildWorkflowDeterministicReceipt(
                receiptId,
                id,
                envelope.RulesetId,
                bundle,
                envelope.Payload),
            ContentRevision: current.ContentRevision,
            SavedRevision: current.ContentRevision);
        WorkspaceStoreMutationResult checkpoint = access.SaveCheckpoint(
            id,
            expectedContentRevision);
        if (!checkpoint.Success || checkpoint.Entry is not WorkspaceStoreEntry checkpointEntry)
        {
            return StoreFailure<WorkspaceSaveReceipt>(checkpoint);
        }

        return new CommandResult<WorkspaceSaveReceipt>(
                Success: true,
                Value: receipt with
                {
                    ContentRevision = checkpointEntry.ContentRevision,
                    SavedRevision = checkpointEntry.SavedRevision
                },
                Error: null,
                OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    public CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id)
    {
        return DownloadCore(LocalStoreAccess(), id);
    }

    public CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id)
    {
        return DownloadCore(ScopedStoreAccess(owner), id);
    }

    private CommandResult<WorkspaceDownloadReceipt> DownloadCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            return StoreFailure<WorkspaceDownloadReceipt>(read);
        }

        WorkspaceDocument document = stored.Document;
        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(envelope.Payload));
        string receiptId = BuildReceiptId("download", id.Value, payloadSha256);
        WorkspaceDownloadReceipt receipt = codec.BuildDownload(id, envelope, document.Format) with
        {
            ReceiptId = receiptId,
            WorkflowDeterministicReceipt = BuildWorkflowDeterministicReceipt(
                receiptId,
                id,
                envelope.RulesetId,
                bundle,
                envelope.Payload)
        };

        return new CommandResult<WorkspaceDownloadReceipt>(
            Success: true,
            Value: receipt,
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    public CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id)
    {
        return ExportCore(LocalStoreAccess(), id);
    }

    public CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id)
    {
        return ExportCore(ScopedStoreAccess(owner), id);
    }

    private CommandResult<WorkspaceExportReceipt> ExportCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            return StoreFailure<WorkspaceExportReceipt>(read);
        }

        WorkspaceDocument document = stored.Document;
        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(envelope.Payload));
        WorkspaceExportReceipt receipt = BuildExportReceipt(id, envelope.RulesetId, bundle, payloadSha256, envelope.Payload);

        return new CommandResult<WorkspaceExportReceipt>(
            Success: true,
            Value: receipt,
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    public CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id)
    {
        return PrintCore(LocalStoreAccess(), id);
    }

    public CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id)
    {
        return PrintCore(ScopedStoreAccess(owner), id);
    }

    private CommandResult<WorkspacePrintReceipt> PrintCore(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            return StoreFailure<WorkspacePrintReceipt>(read);
        }

        WorkspaceDocument document = stored.Document;
        WorkspacePayloadEnvelope envelope = ResolveEnvelope(document);
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(envelope.RulesetId);
        DataExportBundle bundle = codec.BuildExportBundle(envelope);
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(envelope.Payload));
        WorkspacePrintReceipt receipt = BuildPrintReceipt(id, envelope.RulesetId, bundle, payloadSha256, envelope.Payload);

        return new CommandResult<WorkspacePrintReceipt>(
            Success: true,
            Value: receipt,
            Error: null,
            OperationOutcome: WorkspaceOperationOutcome.Success);
    }

    private TSection? TryParseSection<TSection>(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        string sectionId)
        where TSection : class
    {
        return GetSectionCore(access, id, sectionId) as TSection;
    }

    private static WorkspaceExportReceipt BuildExportReceipt(
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle,
        string payloadSha256,
        string payload)
    {
        string json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string packageSha256 = ComputeSha256(bytes);
        string baseFileName = string.IsNullOrWhiteSpace(bundle.Summary.Name) ? id.Value : bundle.Summary.Name;
        string fileName = $"{SanitizeFileName(baseFileName)}-export.json";
        DateTimeOffset exportedAtUtc = DateTimeOffset.UtcNow;
        string packageId = BuildReceiptId("portable", id.Value, packageSha256);

        return new WorkspaceExportReceipt(
            Id: id,
            Format: WorkspaceDocumentFormat.Json,
            ContentBase64: Convert.ToBase64String(bytes),
            FileName: fileName,
            DocumentLength: bytes.Length,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
            PackageId: packageId,
            ExportedAtUtc: exportedAtUtc,
            Portability: BuildExportPortabilityReceipt(
                id,
                rulesetId,
                bundle,
                exportedAtUtc,
                packageSha256),
            WorkflowDeterministicReceipt: BuildWorkflowDeterministicReceipt(
                packageId,
                id,
                rulesetId,
                bundle,
                payload),
            ExchangeDeterministicReceipt: BuildExchangeDeterministicReceipt(
                surfaceKind: "export",
                outputDescriptor: WorkspaceDocumentFormat.Json.ToString(),
                receiptId: packageId,
                rulesetId: rulesetId,
                payload: payload));
    }

    private static WorkspacePrintReceipt BuildPrintReceipt(
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle,
        string payloadSha256,
        string payload)
    {
        string title = string.IsNullOrWhiteSpace(bundle.Summary.Name)
            ? $"Character {id.Value}"
            : bundle.Summary.Name;
        string html = BuildPrintHtml(bundle, title);
        byte[] bytes = Encoding.UTF8.GetBytes(html);
        string receiptId = BuildReceiptId("print", id.Value, payloadSha256);

        return new WorkspacePrintReceipt(
            Id: id,
            ContentBase64: Convert.ToBase64String(bytes),
            FileName: $"{SanitizeFileName(title)}-print.html",
            MimeType: "text/html",
            DocumentLength: bytes.Length,
            Title: title,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
            ReceiptId: receiptId,
            WorkflowDeterministicReceipt: BuildWorkflowDeterministicReceipt(
                receiptId,
                id,
                rulesetId,
                bundle,
                payload),
            ExchangeDeterministicReceipt: BuildExchangeDeterministicReceipt(
                surfaceKind: "print",
                outputDescriptor: "text/html",
                receiptId: receiptId,
                rulesetId: rulesetId,
                payload: payload));
    }

    private static string BuildPrintHtml(DataExportBundle bundle, string title)
    {
        string encodedTitle = WebUtility.HtmlEncode(title);
        string alias = WebUtility.HtmlEncode(bundle.Profile?.Alias ?? bundle.Summary.Alias);
        string metatype = WebUtility.HtmlEncode(bundle.Profile?.Metatype ?? bundle.Summary.Metatype);
        string buildMethod = WebUtility.HtmlEncode(bundle.Profile?.BuildMethod ?? bundle.Summary.BuildMethod);
        string playerName = WebUtility.HtmlEncode(bundle.Profile?.PlayerName ?? string.Empty);
        string concept = WebUtility.HtmlEncode(bundle.Profile?.Concept ?? string.Empty);
        string karma = bundle.Progress?.Karma.ToString("0.##") ?? bundle.Summary.Karma.ToString("0.##");
        string nuyen = bundle.Progress?.Nuyen.ToString("0.##") ?? bundle.Summary.Nuyen.ToString("0.##");
        string streetCred = bundle.Progress?.StreetCred.ToString() ?? "0";
        string initiative = bundle.Progress?.InitiateGrade.ToString() ?? "0";
        string attributeCount = bundle.Attributes?.Count.ToString() ?? "0";
        string skillCount = bundle.Skills?.Count.ToString() ?? "0";
        string gearCount = bundle.Inventory?.GearCount.ToString() ?? "0";
        string contactCount = bundle.Contacts?.Count.ToString() ?? "0";

        StringBuilder html = new();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("  <meta charset=\"utf-8\" />");
        html.AppendLine($"  <title>{encodedTitle}</title>");
        html.AppendLine("  <style>");
        html.AppendLine("    body { font-family: 'Segoe UI', sans-serif; margin: 2rem; color: #111827; }");
        html.AppendLine("    h1, h2 { margin-bottom: 0.5rem; }");
        html.AppendLine("    .grid { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 0.75rem 1.5rem; }");
        html.AppendLine("    .card { border: 1px solid #d1d5db; border-radius: 12px; padding: 1rem 1.25rem; margin-bottom: 1rem; }");
        html.AppendLine("    dt { font-weight: 700; }");
        html.AppendLine("    dd { margin: 0 0 0.5rem 0; }");
        html.AppendLine("  </style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"  <h1>{encodedTitle}</h1>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Profile</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Alias</dt><dd>{alias}</dd>");
        html.AppendLine($"      <dt>Metatype</dt><dd>{metatype}</dd>");
        html.AppendLine($"      <dt>Build Method</dt><dd>{buildMethod}</dd>");
        html.AppendLine($"      <dt>Player</dt><dd>{playerName}</dd>");
        html.AppendLine($"      <dt>Concept</dt><dd>{concept}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Progress</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Karma</dt><dd>{WebUtility.HtmlEncode(karma)}</dd>");
        html.AppendLine($"      <dt>Nuyen</dt><dd>{WebUtility.HtmlEncode(nuyen)}</dd>");
        html.AppendLine($"      <dt>Street Cred</dt><dd>{WebUtility.HtmlEncode(streetCred)}</dd>");
        html.AppendLine($"      <dt>Initiate Grade</dt><dd>{WebUtility.HtmlEncode(initiative)}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("  <div class=\"card\">");
        html.AppendLine("    <h2>Coverage</h2>");
        html.AppendLine("    <dl class=\"grid\">");
        html.AppendLine($"      <dt>Attributes</dt><dd>{WebUtility.HtmlEncode(attributeCount)}</dd>");
        html.AppendLine($"      <dt>Skills</dt><dd>{WebUtility.HtmlEncode(skillCount)}</dd>");
        html.AppendLine($"      <dt>Gear</dt><dd>{WebUtility.HtmlEncode(gearCount)}</dd>");
        html.AppendLine($"      <dt>Contacts</dt><dd>{WebUtility.HtmlEncode(contactCount)}</dd>");
        html.AppendLine("    </dl>");
        html.AppendLine("  </div>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static string SanitizeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        StringBuilder builder = new(value.Length);
        foreach (char character in value)
        {
            builder.Append(invalidChars.Contains(character) ? '_' : character);
        }

        string sanitized = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "workspace" : sanitized;
    }

    private static WorkspacePortabilityReceipt BuildImportPortabilityReceipt(
        CharacterWorkspaceId id,
        WorkspaceImportDocument document,
        string rulesetId,
        CharacterFileSummary summary,
        DateTimeOffset importedAtUtc,
        string payloadSha256)
    {
        string displayName = string.IsNullOrWhiteSpace(summary.Name) ? id.Value : summary.Name;
        bool needsReview = document.Format != WorkspaceDocumentFormat.NativeXml;
        string receiptId = BuildReceiptId("import", id.Value, payloadSha256);
        string outputKind = document.Format == WorkspaceDocumentFormat.Json
            ? WorkspacePortabilityOutputKinds.PortableDossier
            : WorkspacePortabilityOutputKinds.NativeWorkspaceXml;
        WorkspacePortabilityNote formatNote = new(
            Code: "format-identity",
            Severity: needsReview
                ? WorkspacePortabilityNoteSeverities.Warning
                : WorkspacePortabilityNoteSeverities.Info,
            Summary: needsReview
                ? $"Imported {document.Format} content on the governed dossier rail. Inspect the workspace before you use it for governed replace on another surface."
                : $"Imported native workspace XML on the governed dossier rail for {rulesetId}.");
        WorkspacePortabilityNote rulesetNote = new(
            Code: "ruleset-context",
            Severity: WorkspacePortabilityNoteSeverities.Info,
            Summary: $"{displayName} now resolves under the governed {rulesetId} ruleset context instead of an install-local backup slot.");
        WorkspacePortabilityNote provenanceNote = new(
            Code: "provenance-payload",
            Severity: WorkspacePortabilityNoteSeverities.Info,
            Summary: $"Import receipt {receiptId} captured payload hash {payloadSha256[..12]} at {importedAtUtc:O}.");

        return new WorkspacePortabilityReceipt(
            FormatId: document.Format == WorkspaceDocumentFormat.Json
                ? WorkspacePortabilityFormatIds.PortableDossierV1
                : WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
            CompatibilityState: needsReview
                ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                : WorkspacePortabilityCompatibilityStates.Compatible,
            ContextSummary: $"{displayName} was imported into governed dossier truth on {rulesetId}.",
            ReceiptSummary: needsReview
                ? "Portable import completed with compatibility notes; inspect the workspace before you hand it off across surfaces."
                : "Portable import completed as governed dossier truth and is ready for normal use or portable export.",
            ProvenanceSummary: $"Imported workspace {id.Value} from {document.Format} with payload hash {payloadSha256[..12]} on {rulesetId}.",
            PayloadSha256: payloadSha256,
            NextSafeAction: needsReview
                ? "Open the Rules and Profile tabs, then export a fresh portable package before you authorize merge or replace elsewhere."
                : "Use the workspace normally or export a portable package when you need a governed cross-surface handoff.",
            SupportedExchangeModes:
            [
                WorkspacePortabilityExchangeModes.InspectOnly,
                WorkspacePortabilityExchangeModes.Merge,
                WorkspacePortabilityExchangeModes.Replace
            ],
            Notes:
            [
                formatNote,
                rulesetNote,
                provenanceNote
            ],
            OutputKind: outputKind,
            Lineage:
            [
                new WorkspacePortabilityLineageEntry(
                    StageId: "import-source",
                    ArtifactId: $"{document.Format}:{payloadSha256[..12]}",
                    FormatId: document.Format == WorkspaceDocumentFormat.Json
                        ? WorkspacePortabilityFormatIds.PortableDossierV1
                        : WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                    Summary: $"Imported source payload arrived on the {outputKind} rail."),
                new WorkspacePortabilityLineageEntry(
                    StageId: "governed-workspace",
                    ArtifactId: id.Value,
                    FormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                    Summary: $"{displayName} now lives as governed workspace truth on {rulesetId}.")
            ],
            Compatibility: new WorkspacePortabilityCompatibilityReceipt(
                SourceRulesetId: rulesetId,
                TargetRulesetId: rulesetId,
                State: needsReview
                    ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                    : WorkspacePortabilityCompatibilityStates.Compatible,
                WarningCodes: needsReview ? ["format-review-required"] : [],
                BlockingCodes: []),
            Loss: new WorkspacePortabilityLossReceipt(
                State: needsReview
                    ? WorkspacePortabilityLossStates.BoundedLoss
                    : WorkspacePortabilityLossStates.None,
                Summary: needsReview
                    ? "Portable import may omit native-only detail until the governed workspace is re-exported."
                    : "No governed content loss was detected on import.",
                AffectedSections: needsReview ? ["native-workspace-review"] : []),
            Provenance: new WorkspacePortabilityProvenanceReceipt(
                ReceiptId: receiptId,
                GeneratedAtUtc: importedAtUtc,
                SourceArtifactId: id.Value,
                SourceFormatId: document.Format == WorkspaceDocumentFormat.Json
                    ? WorkspacePortabilityFormatIds.PortableDossierV1
                    : WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                PayloadSha256: payloadSha256),
            PortabilityEnvelope: new WorkspacePortabilityEnvelopeReceipt(
                OutputKind: outputKind,
                PortabilityPosture: needsReview
                    ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                    : WorkspacePortabilityCompatibilityStates.Compatible,
                Summary: needsReview
                    ? "Inspect-first import posture: use inspect-only semantics before broader governed handoff."
                    : "Governed workspace posture is portable and ready for standard dossier exchange.",
                SupportedExchangeModes:
                [
                    WorkspacePortabilityExchangeModes.InspectOnly,
                    WorkspacePortabilityExchangeModes.Merge,
                    WorkspacePortabilityExchangeModes.Replace
                ]),
            Revocation: BuildRevocationReceipt(
                outputKind,
                artifactId: id.Value,
                summary: needsReview
                    ? "Imported governed workspace stays active but should only supersede downstream artifacts after inspect-first review."
                    : "Imported governed workspace stays active as the replace-authoritative source for downstream dossier and exchange artifacts.",
                supersedesArtifactIds: []));
    }

    private static WorkspacePortabilityReceipt BuildExportPortabilityReceipt(
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle,
        DateTimeOffset exportedAtUtc,
        string payloadSha256)
    {
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty;
        string displayName = string.IsNullOrWhiteSpace(bundle.Summary.Name) ? id.Value : bundle.Summary.Name;
        string[] missingSections = GetMissingPortableSections(bundle);
        bool hasWarnings = missingSections.Length > 0;
        string receiptId = BuildReceiptId("portable", id.Value, payloadSha256);
        string sectionCoverageSummary = hasWarnings
            ? $"Portable package is missing {string.Join(", ", missingSections)}; receiving surfaces should inspect before governed replace."
            : "Portable package keeps profile, progress, attributes, skills, inventory, qualities, and contacts on the same governed receipt.";
        WorkspacePortabilityCompatibilityReceipt compatibility = new(
            SourceRulesetId: normalizedRulesetId,
            TargetRulesetId: normalizedRulesetId,
            State: hasWarnings
                ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                : WorkspacePortabilityCompatibilityStates.Compatible,
            WarningCodes: hasWarnings ? ["missing-sections"] : [],
            BlockingCodes: []);
        WorkspacePortabilityLossReceipt loss = new(
            State: hasWarnings
                ? WorkspacePortabilityLossStates.BoundedLoss
                : WorkspacePortabilityLossStates.None,
            Summary: hasWarnings
                ? $"Portable dossier omitted {string.Join(", ", missingSections)} and needs inspect-only review before governed replace."
                : "Portable dossier preserved the governed export sections without bounded loss.",
            AffectedSections: missingSections);
        WorkspacePortabilityProvenanceReceipt provenance = new(
            ReceiptId: receiptId,
            GeneratedAtUtc: exportedAtUtc,
            SourceArtifactId: id.Value,
            SourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
            PayloadSha256: payloadSha256);
        WorkspacePortabilityEnvelopeReceipt portabilityEnvelope = new(
            OutputKind: WorkspacePortabilityOutputKinds.PortableDossier,
            PortabilityPosture: hasWarnings
                ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                : WorkspacePortabilityCompatibilityStates.Compatible,
            Summary: hasWarnings
                ? "Inspect-first dossier portability because one or more governed sections are absent from the export."
                : "Portable dossier package can seed dossier, campaign, replay, recap, and external exchange consumers from one governed receipt.",
            SupportedExchangeModes:
            [
                WorkspacePortabilityExchangeModes.InspectOnly,
                WorkspacePortabilityExchangeModes.Merge,
                WorkspacePortabilityExchangeModes.Replace
            ]);
        WorkspacePortabilityRevocationReceipt revocation = BuildRevocationReceipt(
            WorkspacePortabilityOutputKinds.PortableDossier,
            artifactId: receiptId,
            summary: hasWarnings
                ? "Portable dossier package stays revocable and should only supersede downstream artifacts after inspect-first review."
                : "Portable dossier package stays revocable and can supersede downstream dossier-family artifacts on governed replace.",
            supersedesArtifactIds:
            [
                id.Value
            ]);
        WorkspacePortabilityLineageEntry workspaceLineage = new(
            StageId: "governed-workspace",
            ArtifactId: id.Value,
            FormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
            Summary: $"{displayName} starts from governed workspace truth on {normalizedRulesetId}.");
        WorkspacePortabilityLineageEntry portablePackageLineage = new(
            StageId: "portable-package",
            ArtifactId: receiptId,
            FormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
            Summary: $"Portable dossier package is ready for dossier, campaign, replay, recap, and external exchange follow-through.");
        WorkspacePortabilityRelatedOutputReceipt[] relatedOutputs =
        [
            BuildRelatedOutputReceipt(
                outputKind: WorkspacePortabilityOutputKinds.PortableDossier,
                workflowId: "workflow.portability.dossier",
                summary: hasWarnings
                    ? "Portable dossier handoff stays inspect-first until the missing governed sections are reviewed."
                    : "Portable dossier handoff stays ready for governed inspect-only, merge, or replace.",
                stageId: "portable-dossier-output",
                artifactId: receiptId,
                formatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                lineageSummary: "Portable dossier output remains the canonical handoff package for governed character exchange.",
                governedSourceArtifactId: id.Value,
                governedSourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                parentArtifactId: receiptId,
                parentFormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                compatibility: compatibility,
                loss: loss,
                provenance: provenance,
                revocation: revocation,
                supportedExchangeModes: portabilityEnvelope.SupportedExchangeModes),
            BuildRelatedOutputReceipt(
                outputKind: WorkspacePortabilityOutputKinds.CampaignBundle,
                workflowId: "workflow.campaign.bundle",
                summary: hasWarnings
                    ? "Campaign federation should stay inspect-first until receiving surfaces confirm the omitted governed sections."
                    : "Campaign federation can consume the portable dossier without inventing a separate campaign portability schema.",
                stageId: "campaign-bundle-output",
                artifactId: $"{receiptId}:campaign",
                formatId: WorkspacePortabilityFormatIds.CampaignBundleV1,
                lineageSummary: "Campaign bundle posture derives from the governed portable dossier receipt instead of a campaign-local export heuristic.",
                governedSourceArtifactId: id.Value,
                governedSourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                parentArtifactId: receiptId,
                parentFormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                compatibility: compatibility,
                loss: loss,
                provenance: provenance with
                {
                    ReceiptId = $"{receiptId}:campaign",
                    SourceArtifactId = receiptId,
                    SourceFormatId = WorkspacePortabilityFormatIds.PortableDossierV1
                },
                revocation: BuildRevocationReceipt(
                    WorkspacePortabilityOutputKinds.CampaignBundle,
                    artifactId: $"{receiptId}:campaign",
                    summary: hasWarnings
                        ? "Campaign bundle artifacts stay revocable and should only supersede downstream campaign copies after inspect-first review."
                        : "Campaign bundle artifacts stay revocable and can supersede older campaign-bundle siblings without mutating canonical campaign truth.",
                    supersedesArtifactIds:
                    [
                        receiptId
                    ]),
                supportedExchangeModes: portabilityEnvelope.SupportedExchangeModes),
            BuildRelatedOutputReceipt(
                outputKind: WorkspacePortabilityOutputKinds.ReplayTimeline,
                workflowId: "workflow.replay.timeline",
                summary: hasWarnings
                    ? "Replay timelines should open inspect-first because omitted dossier sections may weaken governed replay context."
                    : "Replay timelines can reuse the governed portable dossier receipt without losing lineage or portability posture.",
                stageId: "replay-timeline-output",
                artifactId: $"{receiptId}:replay",
                formatId: WorkspacePortabilityFormatIds.ReplayTimelineV1,
                lineageSummary: "Replay timeline posture stays pinned to the portable dossier receipt so replay exports inherit governed lineage and loss truth.",
                governedSourceArtifactId: id.Value,
                governedSourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                parentArtifactId: receiptId,
                parentFormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                compatibility: compatibility,
                loss: loss,
                provenance: provenance with
                {
                    ReceiptId = $"{receiptId}:replay",
                    SourceArtifactId = receiptId,
                    SourceFormatId = WorkspacePortabilityFormatIds.PortableDossierV1
                },
                revocation: BuildRevocationReceipt(
                    WorkspacePortabilityOutputKinds.ReplayTimeline,
                    artifactId: $"{receiptId}:replay",
                    summary: hasWarnings
                        ? "Replay timelines stay revocable and should not replace downstream replay artifacts until inspect-first review clears bounded loss."
                        : "Replay timelines stay revocable and can supersede older replay-timeline siblings without changing canonical rules truth.",
                    supersedesArtifactIds:
                    [
                        receiptId
                    ]),
                supportedExchangeModes:
                [
                    WorkspacePortabilityExchangeModes.InspectOnly,
                    WorkspacePortabilityExchangeModes.Merge
                ]),
            BuildRelatedOutputReceipt(
                outputKind: WorkspacePortabilityOutputKinds.SessionRecap,
                workflowId: "workflow.recap.session",
                summary: hasWarnings
                    ? "Session recap payloads should stay inspect-first until the receiving surface confirms the omitted governed sections."
                    : "Session recap payloads can publish from the same governed receipt family as dossier export.",
                stageId: "session-recap-output",
                artifactId: $"{receiptId}:recap",
                formatId: WorkspacePortabilityFormatIds.SessionRecapV1,
                lineageSummary: "Session recap posture inherits governed dossier lineage instead of synthesizing recap-local compatibility or loss truth.",
                governedSourceArtifactId: id.Value,
                governedSourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                parentArtifactId: receiptId,
                parentFormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                compatibility: compatibility,
                loss: loss,
                provenance: provenance with
                {
                    ReceiptId = $"{receiptId}:recap",
                    SourceArtifactId = receiptId,
                    SourceFormatId = WorkspacePortabilityFormatIds.PortableDossierV1
                },
                revocation: BuildRevocationReceipt(
                    WorkspacePortabilityOutputKinds.SessionRecap,
                    artifactId: $"{receiptId}:recap",
                    summary: hasWarnings
                        ? "Session recap artifacts stay revocable and should only supersede downstream recap copies after inspect-first review."
                        : "Session recap artifacts stay revocable and can supersede older recap siblings without mutating canonical campaign truth.",
                    supersedesArtifactIds:
                    [
                        receiptId
                    ]),
                supportedExchangeModes:
                [
                    WorkspacePortabilityExchangeModes.InspectOnly,
                    WorkspacePortabilityExchangeModes.Merge
                ]),
            BuildRelatedOutputReceipt(
                outputKind: WorkspacePortabilityOutputKinds.ExternalExchange,
                workflowId: "workflow.exchange.external",
                summary: hasWarnings
                    ? "External exchange consumers should inspect-first before replace because bounded-loss posture is already present in the governed dossier receipt."
                    : "External exchange consumers can inherit governed compatibility, provenance, and portability posture directly from the dossier receipt.",
                stageId: "external-exchange-output",
                artifactId: $"{receiptId}:external",
                formatId: WorkspacePortabilityFormatIds.ExternalExchangeV1,
                lineageSummary: "External exchange posture stays downstream of the governed dossier receipt instead of mutating canonical campaign or rules truth.",
                governedSourceArtifactId: id.Value,
                governedSourceFormatId: WorkspacePortabilityFormatIds.NativeWorkspaceXmlV1,
                parentArtifactId: receiptId,
                parentFormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
                compatibility: compatibility,
                loss: loss,
                provenance: provenance with
                {
                    ReceiptId = $"{receiptId}:external",
                    SourceArtifactId = receiptId,
                    SourceFormatId = WorkspacePortabilityFormatIds.PortableDossierV1
                },
                revocation: BuildRevocationReceipt(
                    WorkspacePortabilityOutputKinds.ExternalExchange,
                    artifactId: $"{receiptId}:external",
                    summary: hasWarnings
                        ? "External exchange artifacts stay revocable and should only supersede downstream exchange copies after inspect-first review."
                        : "External exchange artifacts stay revocable and can supersede older exchange siblings without mutating canonical publication truth.",
                    supersedesArtifactIds:
                    [
                        receiptId
                    ]),
                supportedExchangeModes:
                [
                    WorkspacePortabilityExchangeModes.InspectOnly,
                    WorkspacePortabilityExchangeModes.Merge,
                    WorkspacePortabilityExchangeModes.Replace
                ])
        ];

        return new WorkspacePortabilityReceipt(
            FormatId: WorkspacePortabilityFormatIds.PortableDossierV1,
            CompatibilityState: hasWarnings
                ? WorkspacePortabilityCompatibilityStates.CompatibleWithWarnings
                : WorkspacePortabilityCompatibilityStates.Compatible,
            ContextSummary: $"{displayName} is packaged as a portable dossier on {normalizedRulesetId}.",
            ReceiptSummary: hasWarnings
                ? "Portable export is ready, but inspect the package before merge or governed replace on a receiving surface."
                : "Portable export is ready for inspect-only, merge, or governed replace on a receiving surface.",
            ProvenanceSummary: $"Portable package {receiptId} captured payload hash {payloadSha256[..12]} from workspace {id.Value} at {exportedAtUtc:O}.",
            PayloadSha256: payloadSha256,
            NextSafeAction: hasWarnings
                ? "Open inspect-only first on the receiving surface and verify the missing sections before merge or replace."
                : "Share the package or open inspect-only first before merge or replace on the receiving surface.",
            SupportedExchangeModes:
            [
                WorkspacePortabilityExchangeModes.InspectOnly,
                WorkspacePortabilityExchangeModes.Merge,
                WorkspacePortabilityExchangeModes.Replace
            ],
            Notes:
            [
                new WorkspacePortabilityNote(
                    Code: "format-identity",
                    Severity: WorkspacePortabilityNoteSeverities.Info,
                    Summary: $"Package format {WorkspacePortabilityFormatIds.PortableDossierV1} stays attached to {normalizedRulesetId} dossier truth."),
                new WorkspacePortabilityNote(
                    Code: "section-coverage",
                    Severity: hasWarnings
                        ? WorkspacePortabilityNoteSeverities.Warning
                        : WorkspacePortabilityNoteSeverities.Info,
                    Summary: sectionCoverageSummary),
                new WorkspacePortabilityNote(
                    Code: "provenance-payload",
                    Severity: WorkspacePortabilityNoteSeverities.Info,
                    Summary: $"Workspace {id.Value} export captured payload hash {payloadSha256[..12]} at {exportedAtUtc:O}.")
            ],
            OutputKind: WorkspacePortabilityOutputKinds.PortableDossier,
            Lineage: [workspaceLineage, portablePackageLineage],
            Compatibility: compatibility,
            Loss: loss,
            Provenance: provenance,
            PortabilityEnvelope: portabilityEnvelope,
            Revocation: revocation,
            RelatedOutputs: relatedOutputs);
    }

    private static WorkspacePortabilityRelatedOutputReceipt BuildRelatedOutputReceipt(
        string outputKind,
        string workflowId,
        string summary,
        string stageId,
        string artifactId,
        string formatId,
        string lineageSummary,
        string governedSourceArtifactId,
        string governedSourceFormatId,
        string parentArtifactId,
        string parentFormatId,
        WorkspacePortabilityCompatibilityReceipt compatibility,
        WorkspacePortabilityLossReceipt loss,
        WorkspacePortabilityProvenanceReceipt provenance,
        WorkspacePortabilityRevocationReceipt revocation,
        IReadOnlyList<string> supportedExchangeModes)
    {
        WorkspacePortabilityLineageEntry[] lineage =
        [
            new(
                StageId: "governed-workspace",
                ArtifactId: governedSourceArtifactId,
                FormatId: governedSourceFormatId,
                Summary: $"Governed workspace {governedSourceArtifactId} remains the canonical source artifact."),
            new(
                StageId: "portable-package",
                ArtifactId: parentArtifactId,
                FormatId: parentFormatId,
                Summary: "Portable dossier package holds the canonical governed exchange payload."),
            new(
                StageId: stageId,
                ArtifactId: artifactId,
                FormatId: formatId,
                Summary: lineageSummary)
        ];

        return new WorkspacePortabilityRelatedOutputReceipt(
            OutputKind: outputKind,
            WorkflowId: workflowId,
            Summary: summary,
            Lineage: lineage,
            Compatibility: compatibility,
            Loss: loss,
            Provenance: provenance,
            PortabilityEnvelope: new WorkspacePortabilityEnvelopeReceipt(
                OutputKind: outputKind,
                PortabilityPosture: compatibility.State,
                Summary: summary,
                SupportedExchangeModes: supportedExchangeModes),
            Revocation: revocation);
    }

    private static WorkspacePortabilityRevocationReceipt BuildRevocationReceipt(
        string outputKind,
        string artifactId,
        string summary,
        IReadOnlyList<string> supersedesArtifactIds)
    {
        return new WorkspacePortabilityRevocationReceipt(
            State: WorkspacePortabilityRevocationStates.Revocable,
            FamilyId: $"workspace-portability:{outputKind}",
            ArtifactId: artifactId,
            Scope: "governed-replace",
            Summary: summary,
            SupersedesArtifactIds: supersedesArtifactIds);
    }

    private static string[] GetMissingPortableSections(DataExportBundle bundle)
    {
        List<string> missing = [];

        if (bundle.Profile is null)
        {
            missing.Add("profile");
        }

        if (bundle.Progress is null)
        {
            missing.Add("progress");
        }

        if (bundle.Attributes is null)
        {
            missing.Add("attributes");
        }

        if (bundle.Skills is null)
        {
            missing.Add("skills");
        }

        if (bundle.Inventory is null)
        {
            missing.Add("inventory");
        }

        if (bundle.Qualities is null)
        {
            missing.Add("qualities");
        }

        if (bundle.Contacts is null)
        {
            missing.Add("contacts");
        }

        return missing.ToArray();
    }

    private static WorkspaceWorkflowDeterministicReceipt BuildWorkflowDeterministicReceipt(
        string receiptId,
        CharacterWorkspaceId id,
        string rulesetId,
        DataExportBundle bundle,
        string payload)
    {
        string payloadSha256 = ComputeSha256(Encoding.UTF8.GetBytes(payload));
        WorkspaceNoteFieldSummary noteSummary = BuildNoteFieldSummary(payload);
        bool hasProgress = bundle.Progress is not null;
        bool hasContacts = bundle.Contacts is not null;
        bool hasLifestyles = bundle.Lifestyles is not null;
        bool hasNotesSurface = noteSummary.Parsed;
        string[] coveredWorkflowRouteIds = CanonicalWorkflowRouteIds
            .Where(routeId => routeId switch
            {
                "workflow:workflow-state" => hasProgress,
                "workflow:contacts" => hasContacts,
                "workflow:lifestyles" => hasLifestyles,
                "workflow:notes" => hasNotesSurface,
                _ => false
            })
            .ToArray();
        string[] missingWorkflowRouteIds = CanonicalWorkflowRouteIds
            .Except(coveredWorkflowRouteIds, StringComparer.Ordinal)
            .ToArray();
        int coveredSurfaceCount =
            (hasProgress ? 1 : 0)
            + (hasContacts ? 1 : 0)
            + (hasLifestyles ? 1 : 0)
            + (hasNotesSurface ? 1 : 0);
        int coveragePercent = CalculateCoveragePercent(coveredSurfaceCount, 4);
        string workflowStatePosture = coveredSurfaceCount <= 0
            ? "missing"
            : coveredSurfaceCount < 4
                ? "stale"
                : "governed";

        return new WorkspaceWorkflowDeterministicReceipt(
            ParityFamilyId: DenseWorkbenchParityFamilyId,
            ReceiptId: receiptId,
            ReceiptScopeId: BuildWorkflowReceiptScopeId(rulesetId, payloadSha256),
            WorkspaceId: id.Value,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
            WorkflowStatePosture: workflowStatePosture,
            CoveragePercent: coveragePercent,
            InitiateGrade: bundle.Progress?.InitiateGrade ?? 0,
            ContactCount: bundle.Contacts?.Count ?? 0,
            LifestyleCount: bundle.Lifestyles?.Count ?? 0,
            CoveredWorkflowRouteIds: coveredWorkflowRouteIds,
            MissingWorkflowRouteIds: missingWorkflowRouteIds,
            HasNotesField: noteSummary.HasNotesField,
            HasGameNotesField: noteSummary.HasGameNotesField,
            HasNotesContent: noteSummary.HasNotesContent,
            HasGameNotesContent: noteSummary.HasGameNotesContent);
    }

    private static string BuildWorkflowReceiptScopeId(string rulesetId, string payloadSha256)
    {
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? "ruleset";
        string normalizedHash = string.IsNullOrWhiteSpace(payloadSha256) ? "payload" : payloadSha256.Trim().ToLowerInvariant();
        string truncatedHash = normalizedHash.Length <= 12 ? normalizedHash : normalizedHash[..12];
        return $"workflow-state-{normalizedRulesetId}-{truncatedHash}";
    }

    private static WorkspaceExchangeDeterministicReceipt BuildExchangeDeterministicReceipt(
        string surfaceKind,
        string outputDescriptor,
        string receiptId,
        string rulesetId,
        string payload)
    {
        WorkspaceRuleEnvironmentReceipt ruleEnvironment = BuildRuleEnvironmentReceipt(rulesetId, payload);
        return new WorkspaceExchangeDeterministicReceipt(
            ParityFamilyId: ExchangeParityFamilyId,
            ReceiptId: receiptId,
            SurfaceKind: surfaceKind,
            OutputDescriptor: outputDescriptor,
            RulesetId: RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty,
            RuleEnvironmentPosture: ruleEnvironment.Posture,
            RuleEnvironmentSummary: ruleEnvironment.Summary,
            RuleEnvironmentFingerprint: ruleEnvironment.Fingerprint,
            SettingsProfile: ruleEnvironment.SettingsProfile,
            GameplayOption: ruleEnvironment.GameplayOption,
            GameEdition: ruleEnvironment.GameEdition,
            BannedWareGrades: ruleEnvironment.BannedWareGrades);
    }

    private static WorkspaceNoteFieldSummary BuildNoteFieldSummary(string payload)
    {
        try
        {
            XDocument document = XDocument.Parse(payload, LoadOptions.None);
            XElement? root = document.Root;
            XElement? notesNode = root?.Element("notes");
            XElement? gameNotesNode = root?.Element("gamenotes");
            string notes = notesNode?.Value?.Trim() ?? string.Empty;
            string gameNotes = gameNotesNode?.Value?.Trim() ?? string.Empty;
            return new WorkspaceNoteFieldSummary(
                Parsed: true,
                HasNotesField: notesNode is not null,
                HasGameNotesField: gameNotesNode is not null,
                HasNotesContent: notes.Length > 0,
                HasGameNotesContent: gameNotes.Length > 0);
        }
        catch
        {
            return new WorkspaceNoteFieldSummary(
                Parsed: false,
                HasNotesField: false,
                HasGameNotesField: false,
                HasNotesContent: false,
                HasGameNotesContent: false);
        }
    }

    private static WorkspaceRuleEnvironmentReceipt BuildRuleEnvironmentReceipt(string rulesetId, string payload)
    {
        string normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId) ?? string.Empty;

        try
        {
            XDocument document = XDocument.Parse(payload, LoadOptions.None);
            XElement? root = document.Root;
            string settingsProfile = ReadElementValue(root, "settings");
            string gameplayOption = ReadElementValue(root, "gameplayoption");
            string gameEdition = ReadElementValue(root, "gameedition");
            string gameplayOptionQualityLimit = ReadElementValue(root, "gameplayoptionqualitylimit");
            string maxNuyen = ReadElementValue(root, "maxnuyen");
            string maxKarma = ReadElementValue(root, "maxkarma");
            string contactMultiplier = ReadElementValue(root, "contactmultiplier");
            string[] bannedWareGrades = (root?.Element("bannedwaregrades")?.Elements("bannedwaregrade")
                    ?? Enumerable.Empty<XElement>())
                .Select(entry => entry.Value?.Trim() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            string posture =
                string.IsNullOrWhiteSpace(settingsProfile)
                && string.IsNullOrWhiteSpace(gameplayOption)
                && string.IsNullOrWhiteSpace(gameEdition)
                && bannedWareGrades.Length == 0
                    ? "missing"
                    : "governed";

            return new WorkspaceRuleEnvironmentReceipt(
                Posture: posture,
                Summary: BuildRuleEnvironmentSummary(
                    normalizedRulesetId,
                    settingsProfile,
                    gameplayOption,
                    gameEdition,
                    gameplayOptionQualityLimit,
                    maxNuyen,
                    maxKarma,
                    contactMultiplier,
                    bannedWareGrades),
                Fingerprint: BuildRuleEnvironmentFingerprint(
                    normalizedRulesetId,
                    settingsProfile,
                    gameplayOption,
                    gameEdition,
                    gameplayOptionQualityLimit,
                    maxNuyen,
                    maxKarma,
                    contactMultiplier,
                    bannedWareGrades),
                SettingsProfile: settingsProfile,
                GameplayOption: gameplayOption,
                GameEdition: gameEdition,
                BannedWareGrades: bannedWareGrades);
        }
        catch
        {
            return new WorkspaceRuleEnvironmentReceipt(
                Posture: "stale",
                Summary: $"Workspace payload could not be parsed into a deterministic rule-environment receipt for {FormatRuleEnvironmentValue(normalizedRulesetId)}.",
                Fingerprint: ComputeSha256(Encoding.UTF8.GetBytes($"unparsed\nruleset={normalizedRulesetId}\npayload={payload}")),
                SettingsProfile: string.Empty,
                GameplayOption: string.Empty,
                GameEdition: string.Empty,
                BannedWareGrades: Array.Empty<string>());
        }
    }

    private static string BuildRuleEnvironmentSummary(
        string rulesetId,
        string settingsProfile,
        string gameplayOption,
        string gameEdition,
        string gameplayOptionQualityLimit,
        string maxNuyen,
        string maxKarma,
        string contactMultiplier,
        IReadOnlyList<string> bannedWareGrades)
    {
        if (string.IsNullOrWhiteSpace(settingsProfile)
            && string.IsNullOrWhiteSpace(gameplayOption)
            && string.IsNullOrWhiteSpace(gameEdition)
            && bannedWareGrades.Count == 0)
        {
            return $"No rule-environment fields were discovered for {FormatRuleEnvironmentValue(rulesetId)}.";
        }

        string label = FirstNonEmpty(gameplayOption, settingsProfile, gameEdition, rulesetId);
        return $"{label}; settings={FormatRuleEnvironmentValue(settingsProfile)}; game-edition={FormatRuleEnvironmentValue(gameEdition)}; quality-limit={FormatRuleEnvironmentValue(gameplayOptionQualityLimit)}; max-nuyen={FormatRuleEnvironmentValue(maxNuyen)}; max-karma={FormatRuleEnvironmentValue(maxKarma)}; contact-multiplier={FormatRuleEnvironmentValue(contactMultiplier)}; banned-ware-grades={bannedWareGrades.Count}.";
    }

    private static string BuildRuleEnvironmentFingerprint(
        string rulesetId,
        string settingsProfile,
        string gameplayOption,
        string gameEdition,
        string gameplayOptionQualityLimit,
        string maxNuyen,
        string maxKarma,
        string contactMultiplier,
        IReadOnlyList<string> bannedWareGrades)
    {
        StringBuilder builder = new();
        builder.Append("ruleset=").Append(rulesetId).Append('\n');
        builder.Append("settings=").Append(settingsProfile).Append('\n');
        builder.Append("gameplay-option=").Append(gameplayOption).Append('\n');
        builder.Append("game-edition=").Append(gameEdition).Append('\n');
        builder.Append("quality-limit=").Append(gameplayOptionQualityLimit).Append('\n');
        builder.Append("max-nuyen=").Append(maxNuyen).Append('\n');
        builder.Append("max-karma=").Append(maxKarma).Append('\n');
        builder.Append("contact-multiplier=").Append(contactMultiplier).Append('\n');
        builder.Append("banned-ware-grades=").Append(string.Join("|", bannedWareGrades)).Append('\n');
        return ComputeSha256(Encoding.UTF8.GetBytes(builder.ToString()));
    }

    private static string ReadElementValue(XElement? parent, string elementName)
    {
        return parent?.Element(elementName)?.Value?.Trim() ?? string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (string value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string FormatRuleEnvironmentValue(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "none" : value.Trim();
    }

    private static int CalculateCoveragePercent(int coveredSurfaceCount, int expectedSurfaceCount)
    {
        if (expectedSurfaceCount <= 0)
        {
            return 0;
        }

        return (int)Math.Round(coveredSurfaceCount * 100d / expectedSurfaceCount, MidpointRounding.AwayFromZero);
    }

    private static string BuildReceiptId(string prefix, string entityId, string payloadSha256)
    {
        string normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "receipt" : prefix.Trim().ToLowerInvariant();
        string normalizedEntityId = NormalizeReceiptEntityId(entityId);
        string truncatedHash = payloadSha256.Length <= 12
            ? payloadSha256
            : payloadSha256[..12];
        return $"{normalizedPrefix}-{normalizedEntityId}-{truncatedHash}";
    }

    private static string NormalizeReceiptEntityId(string entityId)
    {
        string normalizedEntityId = string.IsNullOrWhiteSpace(entityId) ? "workspace" : entityId.Trim().ToLowerInvariant();
        return LooksLikeTransientWorkspaceId(normalizedEntityId)
            ? "workspace"
            : normalizedEntityId;
    }

    private static bool LooksLikeTransientWorkspaceId(string value)
    {
        return value.Length == 32 && value.All(static character => Uri.IsHexDigit(character))
            || Guid.TryParse(value, out _);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private readonly record struct WorkspaceNoteFieldSummary(
        bool Parsed,
        bool HasNotesField,
        bool HasGameNotesField,
        bool HasNotesContent,
        bool HasGameNotesContent);

    private readonly record struct WorkspaceRuleEnvironmentReceipt(
        string Posture,
        string Summary,
        string Fingerprint,
        string SettingsProfile,
        string GameplayOption,
        string GameEdition,
        IReadOnlyList<string> BannedWareGrades);

    private static CommandResult<T> StoreFailure<T>(WorkspaceStoreReadResult result)
        where T : class
    {
        return new CommandResult<T>(
            Success: false,
            Value: null,
            Error: result.Error ?? DescribeOutcome(result.Outcome),
            OperationOutcome: result.Outcome);
    }

    private static CommandResult<T> StoreFailure<T>(WorkspaceStoreMutationResult result)
        where T : class
    {
        return new CommandResult<T>(
            Success: false,
            Value: null,
            Error: result.Error ?? DescribeOutcome(result.Outcome),
            OperationOutcome: result.Outcome);
    }

    private static CommandResult<T> ConflictFailure<T>()
        where T : class
    {
        return new CommandResult<T>(
            Success: false,
            Value: null,
            Error: DescribeOutcome(WorkspaceOperationOutcome.Conflict),
            OperationOutcome: WorkspaceOperationOutcome.Conflict);
    }

    private static CommandResult<T> CorruptFailure<T>()
        where T : class
    {
        return new CommandResult<T>(
            Success: false,
            Value: null,
            Error: DescribeOutcome(WorkspaceOperationOutcome.Corrupt),
            OperationOutcome: WorkspaceOperationOutcome.Corrupt);
    }

    private static string DescribeOutcome(WorkspaceOperationOutcome outcome)
    {
        return outcome switch
        {
            WorkspaceOperationOutcome.Missing => "Workspace not found.",
            WorkspaceOperationOutcome.Conflict => "Workspace changed since the expected revision.",
            WorkspaceOperationOutcome.Corrupt => "Workspace data is corrupt.",
            WorkspaceOperationOutcome.Unavailable => "Workspace storage is unavailable.",
            _ => "Workspace operation failed."
        };
    }

    private bool TryResolveEnvelope(
        WorkspaceStoreAccess access,
        CharacterWorkspaceId id,
        out WorkspacePayloadEnvelope envelope)
    {
        WorkspaceStoreReadResult read = access.Get(id);
        if (!read.Success || read.Value is not WorkspaceStoredDocument stored)
        {
            envelope = default!;
            return false;
        }

        envelope = ResolveEnvelope(stored.Document);
        return true;
    }

    private WorkspaceStoreAccess LocalStoreAccess()
    {
        return new WorkspaceStoreAccess(_workspaceStore, OwnerScope.LocalSingleUser, IsLocal: true);
    }

    private WorkspaceStoreAccess ScopedStoreAccess(OwnerScope owner)
    {
        return new WorkspaceStoreAccess(_workspaceStore, owner, IsLocal: false);
    }

    private readonly record struct WorkspaceStoreAccess(
        IWorkspaceStore Store,
        OwnerScope Owner,
        bool IsLocal)
    {
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document)
            => IsLocal
                ? Store.CreateWorkspaceDocument(document)
                : Store.CreateWorkspaceDocument(Owner, document);

        public WorkspaceStoreMutationResult CreateWorkspaceDocument(
            CharacterWorkspaceId id,
            WorkspaceDocument document)
            => IsLocal
                ? Store.CreateWorkspaceDocument(id, document)
                : Store.CreateWorkspaceDocument(Owner, id, document);

        public IReadOnlyList<WorkspaceStoreEntry> List()
            => IsLocal ? Store.List() : Store.List(Owner);

        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
            => IsLocal ? Store.Get(id) : Store.Get(Owner, id);

        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(
            CharacterWorkspaceId id,
            long expectedContentRevision,
            WorkspaceDocument document)
            => IsLocal
                ? Store.ReplaceWorkspaceDocument(id, expectedContentRevision, document)
                : Store.ReplaceWorkspaceDocument(Owner, id, expectedContentRevision, document);

        public WorkspaceStoreMutationResult SaveCheckpoint(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => IsLocal
                ? Store.SaveCheckpoint(id, expectedContentRevision)
                : Store.SaveCheckpoint(Owner, id, expectedContentRevision);

        public WorkspaceStoreMutationResult Delete(
            CharacterWorkspaceId id,
            long expectedContentRevision)
            => IsLocal
                ? Store.Delete(id, expectedContentRevision)
                : Store.Delete(Owner, id, expectedContentRevision);
    }

    private WorkspacePayloadEnvelope ResolveEnvelope(WorkspaceDocument document)
    {
        WorkspaceDocumentState state = document.State;
        string normalizedRulesetId = state.RulesetId;
        IRulesetWorkspaceCodec codec = _workspaceCodecResolver.Resolve(normalizedRulesetId);
        int schemaVersion = state.SchemaVersion > 0
            ? state.SchemaVersion
            : codec.SchemaVersion;
        string payloadKind = string.IsNullOrWhiteSpace(state.PayloadKind)
            ? codec.PayloadKind
            : state.PayloadKind;
        return new WorkspacePayloadEnvelope(
            RulesetId: normalizedRulesetId,
            SchemaVersion: schemaVersion,
            PayloadKind: payloadKind,
            Payload: state.Payload);
    }

    private static WorkspaceDocument CreateUpdatedDocument(WorkspaceDocument current, WorkspacePayloadEnvelope updatedEnvelope)
    {
        return new WorkspaceDocument(
            State: new WorkspaceDocumentState(updatedEnvelope),
            Format: current.Format);
    }
}
