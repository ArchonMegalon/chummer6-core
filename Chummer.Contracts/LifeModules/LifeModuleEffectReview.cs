namespace Chummer.Contracts.LifeModules;

/// <summary>
/// One compiler-resolved contribution, not a before/after character rating.
/// Quality levels compete cumulatively; source metadata is descriptive only.
/// Unsupported effects deliberately have no claimed amount.
/// </summary>
public sealed record LifeModuleEffectContribution(
    string EffectId,
    string Kind,
    string TargetId,
    string TargetName,
    decimal? Amount,
    string SelectionText,
    IReadOnlyDictionary<string, string> DescriptiveMetadata,
    IReadOnlyList<string> SourceAnchorIds,
    string CompilationStatus,
    string? Blocker);

/// <summary>
/// Fresh read-only review of a selected module. Kept separate from the historic
/// choice/acceptance ledger, whose raw-source projections must not be rewritten.
/// </summary>
public sealed record LifeModuleEffectReview(
    LifeModuleDecisionInputRequest Request,
    string SourcePreviewDigest,
    string CompilerRuntimeDigest,
    string CompilationDigest,
    IReadOnlyList<LifeModuleEffectContribution> Contributions,
    string ReviewDigest);
