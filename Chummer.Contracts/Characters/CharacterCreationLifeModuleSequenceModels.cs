using Chummer.Contracts.Workspaces;
using Chummer.Contracts.LifeModules;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Automatic racial contribution, not a talent choice or permission to finalize.</summary>
public sealed record CharacterCreationLifeModuleMetatypeWriteSummary(string MetatypeId, string MetatypeName,
    int MetatypeKarmaCost, int GrantedQualityCount, int ImprovementCount, int GrantedGearCount, string PlanDigest);

/// <summary>
/// The entire source-revalidated selection, including repeated Real Life modules.
/// This is a compilation preview, not a write plan or a finalization receipt.
/// </summary>
public sealed record CharacterCreationLifeModuleSequenceCompilation(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    string DraftDigest,
    string SourceDigest,
    string SourceContextDigest,
    bool SelectionFinished,
    IReadOnlyList<CharacterCreationLifeModuleOccurrenceCompilation> Occurrences,
    IReadOnlyList<CharacterCreationLifeModuleQualityLevelResolution> QualityLevels,
    IReadOnlyList<string> Blockers,
    string CompilationDigest);

/// <summary>
/// Effect/consumer IDs inside Compilation are local to this occurrence. They
/// must not be deduplicated across purchases of the same repeatable module.
/// Answers are retained verbatim; retaining an answer does not apply its effect.
/// </summary>
public sealed record CharacterCreationLifeModuleOccurrenceCompilation(
    int Order,
    string OccurrenceId,
    int StageOrder,
    CharacterCreationFoundationSelection Selection,
    IReadOnlyDictionary<string, string> FollowUpValues,
    IReadOnlyList<string> SourceAnchorIds,
    CharacterCreationFoundationEffectCompilation Compilation);

public sealed record CharacterCreationLifeModuleQualityLevelContribution(
    string OccurrenceId,
    string EffectId,
    string InstructionDigest,
    int Level);

/// <summary>
/// Highest source-defined tier, not the sum of contributions. Existing runner
/// qualities and required instance text must still be reconciled before writing.
/// </summary>
public sealed record CharacterCreationLifeModuleQualityLevelResolution(
    string Group,
    int Level,
    CharacterCreationFoundationEffectTargetBinding Target,
    string QualityLevelsSourceDigest,
    IReadOnlyList<CharacterCreationLifeModuleQualityLevelContribution> Contributions,
    IReadOnlyList<string> SourceAnchorIds)
{
    /// <summary>Source-owned selecttext required by the winning quality, not a lower tier.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LifeModuleFollowUpPromptDto? InstancePrompt { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceValue { get; init; }

    /// <summary>Present only when the selected value came from an unconsumed source push.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstancePushOccurrenceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationFoundationSelectionPushInstruction? InstancePush { get; init; }
}

/// <summary>Summary of a complete headless effect plan, not permission to persist it or enter Career.</summary>
public sealed record CharacterCreationLifeModuleEffectWriteSummary(
    int ModuleCount,
    int ImprovementCount,
    int DependentQualityCount,
    int GroupQualityCount,
    decimal ModuleKarmaCost,
    string PlanDigest);
