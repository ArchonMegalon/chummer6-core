using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public interface IWorkspaceService
{
    WorkspaceImportResult Import(WorkspaceImportDocument document);

    WorkspaceImportResult Import(OwnerScope owner, WorkspaceImportDocument document);

    IReadOnlyList<WorkspaceListItem> List(int? maxCount = null);

    IReadOnlyList<WorkspaceListItem> List(OwnerScope owner, int? maxCount = null);

    CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(CharacterWorkspaceId id)
    {
        return new CommandResult<WorkspaceDocumentSnapshot>(
            false,
            null,
            "Revision-aware workspace reads are unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceDocumentSnapshot> GetWorkspace(OwnerScope owner, CharacterWorkspaceId id)
    {
        return new CommandResult<WorkspaceDocumentSnapshot>(
            false,
            null,
            "Revision-aware workspace reads are unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceOverviewProjection> GetOverview(CharacterWorkspaceId id)
    {
        return new CommandResult<WorkspaceOverviewProjection>(
            false,
            null,
            "Snapshot-bound workspace overview projection is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceOverviewProjection> GetOverview(OwnerScope owner, CharacterWorkspaceId id)
    {
        return new CommandResult<WorkspaceOverviewProjection>(
            false,
            null,
            "Snapshot-bound workspace overview projection is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    [Obsolete("Compatibility close reads once and performs one CAS delete. Pass expectedContentRevision; removal is queued for Stage C.")]
    bool Close(CharacterWorkspaceId id);

    [Obsolete("Compatibility close reads once and performs one CAS delete. Pass expectedContentRevision; removal is queued for Stage C.")]
    bool Close(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspaceRevisionReceipt> Close(CharacterWorkspaceId id, long expectedContentRevision)
    {
        return new CommandResult<WorkspaceRevisionReceipt>(
            false,
            null,
            "Revision-aware close is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceRevisionReceipt> Close(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
    {
        return new CommandResult<WorkspaceRevisionReceipt>(
            false,
            null,
            "Revision-aware close is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    object? GetSection(CharacterWorkspaceId id, string sectionId);

    object? GetSection(OwnerScope owner, CharacterWorkspaceId id, string sectionId);

    CharacterFileSummary? GetSummary(CharacterWorkspaceId id);

    CharacterFileSummary? GetSummary(OwnerScope owner, CharacterWorkspaceId id);

    CharacterValidationResult? Validate(CharacterWorkspaceId id);

    CharacterValidationResult? Validate(OwnerScope owner, CharacterWorkspaceId id);

    CharacterProfileSection? GetProfile(CharacterWorkspaceId id);

    CharacterProfileSection? GetProfile(OwnerScope owner, CharacterWorkspaceId id);

    CharacterProgressSection? GetProgress(CharacterWorkspaceId id);

    CharacterProgressSection? GetProgress(OwnerScope owner, CharacterWorkspaceId id);

    CharacterSkillsSection? GetSkills(CharacterWorkspaceId id);

    CharacterSkillsSection? GetSkills(OwnerScope owner, CharacterWorkspaceId id);

    CharacterRulesSection? GetRules(CharacterWorkspaceId id);

    CharacterRulesSection? GetRules(OwnerScope owner, CharacterWorkspaceId id);

    CharacterBuildSection? GetBuild(CharacterWorkspaceId id);

    CharacterBuildSection? GetBuild(OwnerScope owner, CharacterWorkspaceId id);

    CharacterMovementSection? GetMovement(CharacterWorkspaceId id);

    CharacterMovementSection? GetMovement(OwnerScope owner, CharacterWorkspaceId id);

    CharacterAwakeningSection? GetAwakening(CharacterWorkspaceId id);

    CharacterAwakeningSection? GetAwakening(OwnerScope owner, CharacterWorkspaceId id);

    [Obsolete("Compatibility metadata update reads once and performs one CAS replace. Pass expectedContentRevision.")]
    CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command);

    [Obsolete("Compatibility metadata update reads once and performs one CAS replace. Pass expectedContentRevision.")]
    CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command);

    CommandResult<WorkspaceMetadataResult> UpdateMetadata(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        return new CommandResult<WorkspaceMetadataResult>(
            false,
            null,
            "Revision-aware metadata update is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceMetadataResult> UpdateMetadata(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        UpdateWorkspaceMetadata command)
    {
        return new CommandResult<WorkspaceMetadataResult>(
            false,
            null,
            "Revision-aware metadata update is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceRevisionReceipt> ReplaceWorkspaceDocument(
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return new CommandResult<WorkspaceRevisionReceipt>(
            false,
            null,
            "Revision-aware replacement is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceRevisionReceipt> ReplaceWorkspaceDocument(
        OwnerScope owner,
        CharacterWorkspaceId id,
        long expectedContentRevision,
        WorkspaceDocument document)
    {
        return new CommandResult<WorkspaceRevisionReceipt>(
            false,
            null,
            "Revision-aware replacement is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    [Obsolete("Compatibility save reads once and performs one CAS checkpoint. Pass expectedContentRevision.")]
    CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id);

    [Obsolete("Compatibility save reads once and performs one CAS checkpoint. Pass expectedContentRevision.")]
    CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id, long expectedContentRevision)
    {
        return new CommandResult<WorkspaceSaveReceipt>(
            false,
            null,
            "Revision-aware save is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id, long expectedContentRevision)
    {
        return new CommandResult<WorkspaceSaveReceipt>(
            false,
            null,
            "Revision-aware save is unavailable on this compatibility implementation.",
            WorkspaceOperationOutcome.Unavailable);
    }

    CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id);

    CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id);

    CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id);

    CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id);
}
