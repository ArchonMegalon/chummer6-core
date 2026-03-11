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

    bool Close(CharacterWorkspaceId id);

    bool Close(OwnerScope owner, CharacterWorkspaceId id);

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

    CommandResult<CharacterProfileSection> UpdateMetadata(CharacterWorkspaceId id, UpdateWorkspaceMetadata command);

    CommandResult<CharacterProfileSection> UpdateMetadata(OwnerScope owner, CharacterWorkspaceId id, UpdateWorkspaceMetadata command);

    CommandResult<WorkspaceSaveReceipt> Save(CharacterWorkspaceId id);

    CommandResult<WorkspaceSaveReceipt> Save(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspaceDownloadReceipt> Download(CharacterWorkspaceId id);

    CommandResult<WorkspaceDownloadReceipt> Download(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspaceExportReceipt> Export(CharacterWorkspaceId id);

    CommandResult<WorkspaceExportReceipt> Export(OwnerScope owner, CharacterWorkspaceId id);

    CommandResult<WorkspacePrintReceipt> Print(CharacterWorkspaceId id);

    CommandResult<WorkspacePrintReceipt> Print(OwnerScope owner, CharacterWorkspaceId id);
}
