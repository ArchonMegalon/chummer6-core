using Chummer.Contracts.BuildGhost;
using Chummer.Application.Owners;

namespace Chummer.Application.Explain;

public interface IWorkspaceRuleQuestionService
{
    /// <summary>
    /// Resolves a supported rule intent under the actual issuing owner's synchronous
    /// lease. The returned revision-bound observation does not authorize later writes.
    /// </summary>
    WorkspaceRuleQuestionResult Resolve(
        OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request);
}
