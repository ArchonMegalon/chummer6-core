using Chummer.Application.Owners;
using Chummer.Contracts.BuildGhost;

namespace Chummer.Application.Explain;

/// <summary>
/// Validates one provider-shaped answer against a fresh, owner-bound Core rule
/// observation. This seam never invokes a provider and never authorizes a write.
/// </summary>
public interface IWorkspaceRuleProviderAnswerService
{
    BuildGhostProviderValidationResult Validate(
        OwnerContextStamp expectedOwner,
        WorkspaceRuleQuestionRequest request,
        string expectedRequestId,
        BuildGhostProviderAnswer? answer);
}
