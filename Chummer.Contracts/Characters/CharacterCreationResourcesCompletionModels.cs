namespace Chummer.Contracts.Characters;

public static class CharacterCreationResourcesCompletionSchemas
{
    public const string InputsV1 = "chummer.creation-resources-completion-inputs.v1";
    public const string ProjectionV1 = "chummer.creation-resources-completion-projection.v1";
    public const string SnapshotConflict = "creation-resources-completion-snapshot-conflict";
    public const string UpstreamBudgetUnavailable = "creation-resources-completion-upstream-budget-unavailable";
}

/// <summary>
/// Detached original source and history admitted from a real workspace. Pending
/// Resources and receipts remain historical; they are never rebound to a proposal.
/// This observation is not current ownership or permission to save a workspace.
/// </summary>
public sealed record CharacterCreationResourcesCompletionInputs(
    string Schema,
    CharacterCreationResourcesBinding Binding,
    CharacterCreationResourcesAuthority Authority,
    CharacterCreationPrerequisiteDraft PrerequisiteDraft,
    CharacterCreationResourcePriorityOption PriorityOption,
    CharacterCreationResourcesDraft? PendingDraft,
    IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> Receipts,
    bool PurchaseAuthorityExact,
    decimal KnownPurchaseCost,
    string SnapshotDigest);

/// <summary>
/// Pure cumulative budget projection, not a draft or confirmation capability.
/// KarmaInvestment is the full proposed investment, not an increment to a saved
/// value. A missing upstream allowance produces no projection. Other incomplete
/// Magic selections can remain blockers even when this budget is exact.
/// </summary>
public sealed record CharacterCreationResourcesCompletionProjection(
    string Schema,
    CharacterCreationResourcesBinding Binding,
    string ResourcesSnapshotDigest,
    CharacterCreationCompletionBudgetPreview UpstreamBudget,
    IReadOnlyList<CharacterCreationResourceAllocationOption> Options,
    CharacterCreationResourceAllocationOption SelectedOption,
    CharacterCreationResourcesBudget Budget,
    int RemainingCreationKarma,
    IReadOnlyList<string> Blockers,
    string ProposalDigest);

public static class CharacterCreationResourcesCompletionRules
{
    public static string ComputeInputsDigest(CharacterCreationResourcesCompletionInputs value)
        => CharacterCreationResourcesRules.Compute(value with { SnapshotDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationResourcesCompletionProjection value)
        => CharacterCreationResourcesRules.Compute(value with { ProposalDigest = string.Empty });
}
