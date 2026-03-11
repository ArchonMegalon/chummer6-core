using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public interface IWorkspaceImportRulesetDetector
{
    string? Detect(WorkspaceImportDocument document);
}
