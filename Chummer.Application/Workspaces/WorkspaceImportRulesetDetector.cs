using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Workspaces;

public sealed class WorkspaceImportRulesetDetector : IWorkspaceImportRulesetDetector
{
    public string? Detect(WorkspaceImportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return WorkspaceRulesetDetection.Detect(payloadKind: null, payload: document.Content);
    }
}
