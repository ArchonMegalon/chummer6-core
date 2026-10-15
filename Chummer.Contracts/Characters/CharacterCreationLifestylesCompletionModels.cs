namespace Chummer.Contracts.Characters;

public static class CharacterCreationLifestylesCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-lifestyles-completion-inputs.v1";
    public const string ProjectionV1 = "chummer.creation-lifestyles-completion-projection.v1";
    public const string SnapshotConflict = "creation-lifestyles-completion-snapshot-conflict";
    public const string UpstreamBudgetUnavailable = "creation-lifestyles-completion-upstream-budget-unavailable";
    public const string StartingLifestyleRequired = "creation-lifestyles-completion-starting-lifestyle-required";
}

/// <summary>
/// Original source-admitted inputs. Gear.Resources retains the actual Lifestyle
/// draft and receipts; an absent draft is never replaced with a hypothetical one.
/// This observation carries no current owner or confirmation authority.
/// </summary>
public sealed record CharacterCreationLifestylesCompletionInputs(
    string Schema,
    CharacterCreationLifestyleBinding Binding,
    CharacterCreationLifestylesAuthority Authority,
    CharacterCreationGearCompletionInputs Gear,
    CharacterCreationQualitiesCompletionInputs Qualities,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles,
    string SnapshotDigest);

/// <summary>
/// Source-priced full replacement list over proposed Gear and Qualities.
/// StartingLifestyleId identifies a selected lifestyle only: no dice result,
/// future starting cash, saved draft or confirmation permission is invented.
/// </summary>
public sealed record CharacterCreationLifestylesCompletionProjection(
    string Schema,
    CharacterCreationLifestyleBinding Binding,
    string LifestylesSnapshotDigest,
    CharacterCreationGearCompletionProjection Gear,
    CharacterCreationAttributeQualitiesPreview Qualities,
    CharacterCreationLifestylesAuthority Authority,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lifestyles,
    Guid? StartingLifestyleId,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);

public static class CharacterCreationLifestylesCompletionRules
{
    public static string ComputeInputsDigest(CharacterCreationLifestylesCompletionInputs inputs)
        => CharacterCreationFinalizationDigest.Compute(inputs with { SnapshotDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationLifestylesCompletionProjection projection)
        => CharacterCreationFinalizationDigest.Compute(projection with { ProposalDigest = string.Empty });
}
