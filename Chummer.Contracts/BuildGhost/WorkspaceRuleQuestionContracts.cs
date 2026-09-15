using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.BuildGhost;

public static class WorkspaceRuleQuestionSchemas
{
    public const string RequestV1 = "chummer.workspace_rule_question.request.v1";
    public const string ResultV1 = "chummer.workspace_rule_question.result.v1";
    public const string BindingV1 = "chummer.workspace_rule_question.binding.v1";
    public const string ExecutingModulesV1 = "chummer.core.executing-modules.v1";
    // Uses the BuildGhostProviderAnswer shape, but PacketDigest binds the complete
    // WorkspaceRuleQuestionResult, not a legacy BuildGhostAnalysisPacket.
    public const string ProviderAnswerV1 = "chummer.workspace_rule_question.provider_answer.v1";
}

public static class WorkspaceRuleQuestionIntents
{
    public const string QualityLevel = "quality-level";
    public const string QualityLevelRuleId = "sr5.quality.level";
}

public static class WorkspaceRuleQuestionStatuses
{
    public const string Resolved = "resolved";
    public const string Unresolved = "unresolved";
}

/// <summary>
/// The caller may identify one saved quality subject and an intent only. Source ids,
/// pages, levels, and explanatory text remain Core-owned observations.
/// </summary>
public sealed record WorkspaceRuleQuestionRequest(
    CharacterWorkspaceId WorkspaceId,
    long ExpectedContentRevision,
    string RulesetId,
    string Intent,
    string SubjectId,
    string Locale)
{
    public string Schema { get; init; } = WorkspaceRuleQuestionSchemas.RequestV1;
}

/// <summary>
/// Identity of a concrete participating module observed by Core. This is not an
/// authenticated package inventory, signature assertion or complete dependency closure.
/// </summary>
public sealed record WorkspaceRuleExecutingModule(
    string Role,
    string ImplementationType,
    string AssemblyName,
    Guid ModuleVersionId);

/// <summary>
/// Exact citation from the effective selected node. No original/custom-source
/// classification or localized book title is inferred from an active profile.
/// A provider adapter must not silently supply those missing provenance claims.
/// </summary>
public sealed record WorkspaceRuleSourceAnchor(
    string AnchorId,
    string RulesetId,
    string SourceBook,
    int Page,
    string QualitySourceId,
    string SourceNodeDigest,
    string SettingsProfileId,
    string SourceProfileDigest,
    IReadOnlyList<string> CalculationTrace);

/// <summary>
/// Binding of one explanation to the live owner authority, complete stored document,
/// active source profile, selected source node, executing Core modules, and intent.
/// This is an observation binding, not an authorization or installed-package receipt.
/// </summary>
public sealed record WorkspaceRuleQuestionBinding(
    string Schema,
    string OwnerId,
    bool TrustedLocalOwner,
    string OwnerAuthorityInstanceId,
    long OwnerTransitionRevision,
    CharacterWorkspaceId WorkspaceId,
    string RulesetId,
    long ContentRevision,
    long SavedRevision,
    string WorkspaceDocumentDigest,
    string SettingsProfileId,
    string SourceProfileDigest,
    string SourceNodeDigest,
    string EngineIdentityKind,
    string EngineFingerprint,
    IReadOnlyList<WorkspaceRuleExecutingModule> ExecutingModules,
    string Intent,
    string SubjectId,
    string Locale);

/// <summary>
/// A current-read observation produced inside Core, not a provider instruction,
/// mutation capability, live context token or deployment-readiness assertion.
/// The digest detects changed bytes; it is not a signature or source of authority.
/// </summary>
public sealed record WorkspaceRuleQuestionResult(
    string Schema,
    string Status,
    BuildGhostRuleExplanation Explanation,
    IReadOnlyList<WorkspaceRuleSourceAnchor> SourceAnchors,
    WorkspaceRuleQuestionBinding? Binding,
    int? Level,
    int? MaximumLevel,
    string ResultDigest,
    string? FailureReason = null)
{
    public bool Resolved => string.Equals(Status, WorkspaceRuleQuestionStatuses.Resolved,
        StringComparison.Ordinal);
}

public static class WorkspaceRuleQuestionIntegrity
{
    public static string ComputeResultDigest(WorkspaceRuleQuestionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return CharacterCreationQualitiesDigest.Compute(result with { ResultDigest = string.Empty });
    }

    public static string ComputeEngineFingerprint(IReadOnlyList<WorkspaceRuleExecutingModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        return CharacterCreationQualitiesDigest.Compute(new
        {
            Schema = WorkspaceRuleQuestionSchemas.ExecutingModulesV1,
            Modules = modules.OrderBy(static module => module.Role, StringComparer.Ordinal).ToArray()
        });
    }
}
