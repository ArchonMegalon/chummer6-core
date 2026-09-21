namespace Chummer.Contracts.LifeModules;

/// <summary>Player answers to a source-defined form; never client effects.</summary>
public sealed record LifeModuleDecisionInputRequest(
    string WorkspaceId,
    long WorkspaceRevision,
    string ChoiceId,
    string DecisionDigest,
    string DecisionCommandDigest,
    IReadOnlyDictionary<string, string> Values);

/// <summary>
/// A read-only Core preview bound to the unchanged decision turn and the exact
/// answers. It neither accepts the module nor advances the story by itself.
/// </summary>
public sealed record LifeModuleDecisionInputResolution(
    string WorkspaceId,
    long WorkspaceRevision,
    string ChoiceId,
    string DecisionDigest,
    string DecisionCommandDigest,
    IReadOnlyDictionary<string, string> Values,
    string ResolvedPreviewDigest,
    LifeModuleMechanicsPreview MechanicsPreview,
    string ResolutionDigest);
