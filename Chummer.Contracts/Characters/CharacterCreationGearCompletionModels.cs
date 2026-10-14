namespace Chummer.Contracts.Characters;

public static class CharacterCreationGearCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-gear-completion-inputs.v1";
    public const string ProjectionV1 = "chummer.creation-gear-completion-projection.v1";
    public const string SnapshotConflict = "creation-gear-completion-snapshot-conflict";
    public const string UpstreamBudgetUnavailable = "creation-gear-completion-upstream-budget-unavailable";
}

/// <summary>
/// Source-admitted original Gear and Resources observations. Saved bindings,
/// baskets and receipts remain historical; no intermediate draft is invented.
/// Detached inputs confer neither current ownership nor permission to save.
/// </summary>
public sealed record CharacterCreationGearCompletionInputs(
    string Schema,
    CharacterCreationGearState State,
    CharacterCreationResourcesCompletionInputs Resources,
    IReadOnlyList<CharacterCreationGearReceiptLedgerEntry> Receipts,
    string SnapshotDigest);

/// <summary>
/// Read-only full-basket replacement priced from source and the recomputed full
/// Resources investment. Budget.IsExact concerns Gear pricing only; all upstream
/// blockers are retained separately. This is not a draft or confirmation token.
/// </summary>
public sealed record CharacterCreationGearCompletionProjection(
    string Schema,
    CharacterCreationGearBinding Binding,
    string GearSnapshotDigest,
    CharacterCreationResourcesCompletionProjection Resources,
    IReadOnlyList<CharacterCreationGearLine> Lines,
    CharacterCreationGearBudget Budget,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);

public static class CharacterCreationGearCompletionRules
{
    public static string ComputeInputsDigest(CharacterCreationGearCompletionInputs value)
        => CharacterCreationGearRules.Compute(value with { SnapshotDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationGearCompletionProjection value)
        => CharacterCreationGearRules.Compute(value with { ProposalDigest = string.Empty });
}
