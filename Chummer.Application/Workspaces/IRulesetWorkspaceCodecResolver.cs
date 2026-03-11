namespace Chummer.Application.Workspaces;

public interface IRulesetWorkspaceCodecResolver
{
    IRulesetWorkspaceCodec Resolve(string? rulesetId);
}
