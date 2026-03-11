using Chummer.Application.Workspaces;
using Chummer.Contracts.Rulesets;

namespace Chummer.Rulesets.Hosting;

public sealed class RulesetWorkspaceCodecResolver : IRulesetWorkspaceCodecResolver
{
    private readonly IReadOnlyDictionary<string, IRulesetWorkspaceCodec> _codecsByRuleset;

    public RulesetWorkspaceCodecResolver(IEnumerable<IRulesetWorkspaceCodec> codecs)
    {
        Dictionary<string, IRulesetWorkspaceCodec> map = new(StringComparer.Ordinal);
        foreach (IRulesetWorkspaceCodec codec in codecs)
        {
            string normalizedRulesetId = RulesetDefaults.NormalizeRequired(codec.RulesetId);
            map[normalizedRulesetId] = codec;
        }

        _codecsByRuleset = map;
    }

    public IRulesetWorkspaceCodec Resolve(string? rulesetId)
    {
        string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
        if (normalizedRulesetId is null)
        {
            throw new InvalidOperationException("Workspace ruleset id is required to resolve a workspace codec.");
        }

        if (_codecsByRuleset.TryGetValue(normalizedRulesetId, out IRulesetWorkspaceCodec? codec))
        {
            return codec;
        }

        throw new InvalidOperationException($"No workspace codec is registered for ruleset '{normalizedRulesetId}'.");
    }
}
