using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationResourcesSchemas
{
    public const string AuthorityV1 = "chummer.sr5.creation-resources.authority.v1";
    public const string StateV1 = "chummer.sr5.creation-resources.state.v1";
    public const string PreviewV1 = "chummer.sr5.creation-resources.preview.v1";
    public const string DraftV1 = "chummer.sr5.creation-resources.draft.v1";
    public const string ContributionV1 = "chummer.sr5.creation-resources.finalization-contribution.v1";
    public const string ReceiptV1 = "chummer.sr5.creation-resources.receipt.v1";
    public const string RulesV1 = "chummer.sr5.creation-resources.rules.v1";
    public const string RuntimeV1 = "chummer.sr5.creation-resources.runtime.v1";
}

public static class CharacterCreationResourcesBlockers
{
    public const string AuthorityUnavailable = "creation-resources-authority-unavailable";
    public const string BuildMethodUnsupported = "creation-resources-build-method-unsupported";
    public const string CareerModeRejected = "creation-resources-career-mode-rejected";
    public const string CharacterDocumentInvalid = "creation-resources-character-document-invalid";
    public const string ExplicitConfirmationRequired = "creation-resources-explicit-confirmation-required";
    public const string IdempotencyConflict = "creation-resources-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-resources-idempotency-key-invalid";
    public const string InsufficientCreationKarma = "creation-resources-insufficient-creation-karma";
    public const string InvalidOption = "creation-resources-invalid-option";
    public const string NoChange = "creation-resources-no-change";
    public const string PersistenceAuthorityRequired = "creation-resources-persistence-authority-required";
    public const string PreviewDigestMismatch = "creation-resources-preview-digest-mismatch";
    public const string PrerequisiteDraftRequired = "creation-resources-prerequisite-draft-required";
    public const string PrerequisiteDraftStale = "creation-resources-prerequisite-draft-stale";
    public const string PurchaseCostAuthorityRequired = "creation-resources-purchase-cost-authority-required";
    public const string ReceiptLedgerCorrupt = "creation-resources-receipt-ledger-corrupt";
    public const string ResourceAssignmentInvalid = "creation-resources-resource-assignment-invalid";
    public const string RulesetSr5Required = "creation-resources-ruleset-sr5-required";
    public const string SettingsSemanticsUnsupported = "creation-resources-settings-semantics-unsupported";
    public const string StaleAuxiliaryStateDigest = "creation-resources-stale-auxiliary-state-digest";
    public const string StaleContentDigest = "creation-resources-stale-content-digest";
    public const string StalePrerequisiteDraft = "creation-resources-stale-prerequisite-draft";
    public const string StaleRulesDigest = "creation-resources-stale-rules-digest";
    public const string StaleRuntimeDigest = "creation-resources-stale-runtime-digest";
    public const string StaleSourceDigest = "creation-resources-stale-source-digest";
    public const string StaleWorkspaceRevision = "creation-resources-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-resources-workspace-unavailable";
}

public static class CharacterCreationResourcesOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Corrupt = "corrupt";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationResourcesSourceAnchors
{
    public const string Step = "CharacterCreationWizardStepIds.Resources";
    public const string LegacyTotal = "Chummer/Backend/Characters/Character.cs#TotalStartingNuyen";
    public const string LegacyAvailable = "Chummer/Backend/Characters/Character.cs#CalculateNuyenCreateMode";
    public const string LegacyMaximumInvestment = "Chummer/Backend/Characters/Character.cs#TotalNuyenMaximumBP";
    public const string LegacyCarryover = "Chummer/Forms/Character Forms/CharacterCreate.cs#NuyenCarryover";
    public const string LegacyCarryoverDefault = "Chummer/Backend/Character Settings/CharacterSettings.cs#NuyenCarryoverDefault";
    public const string PriorityCatalog = "priorities.xml#priorities:Resources";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Step,
        LegacyTotal,
        LegacyAvailable,
        LegacyMaximumInvestment,
        LegacyCarryover,
        LegacyCarryoverDefault,
        PriorityCatalog
    });
}

public sealed record CharacterCreationResourcePriorityOption(
    string SourceId,
    string Rank,
    decimal BasePriorityNuyen,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

public sealed record CharacterCreationResourcesAuthority(
    string Schema,
    string RulesetId,
    string SettingsProfileId,
    string BuildMethod,
    decimal KarmaToNuyenRate,
    int MaximumKarmaInvestment,
    decimal NuyenCarryover,
    int MaximumAvailability,
    bool UnrestrictedNuyen,
    IReadOnlyList<CharacterCreationResourcePriorityOption> PriorityOptions,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationResourcesAuthority Unavailable { get; } = new(
        CharacterCreationResourcesSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        string.Empty,
        0m,
        0,
        0m,
        0,
        false,
        [],
        [],
        [CharacterCreationResourcesBlockers.AuthorityUnavailable],
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

public sealed record CharacterCreationResourceAllocationOption(
    string OptionId,
    int KarmaInvestment,
    decimal NuyenFromKarma,
    decimal TotalStartingNuyen,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

public sealed record CharacterCreationResourcesBudget(
    decimal PriorityNuyen,
    int KarmaInvestment,
    decimal NuyenFromKarma,
    decimal TotalStartingNuyen,
    decimal KnownPurchaseCost,
    decimal RemainingNuyen,
    decimal Overspend,
    decimal CarryoverLimit,
    decimal CarryoverExcess,
    bool IsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationResourcesBinding(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest);

public sealed record CharacterCreationResourcesFinalizationContribution(
    string Schema,
    string PriorityRank,
    string PrioritySourceId,
    decimal StartingNuyen,
    int NuyenKarma,
    string ExpectedRawCharacterXmlDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string ContributionDigest);

public sealed record CharacterCreationResourcesDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    string SelectedOptionId,
    int KarmaInvestment,
    CharacterCreationResourcesBudget Budget,
    CharacterCreationResourcesFinalizationContribution FinalizationContribution,
    IReadOnlyList<string> SourceAnchorIds,
    bool CharacterEffectsApplied,
    string LastIdempotencyKeyDigest,
    string LastPreviewDigest,
    string LastCommandDigest,
    string DraftDigest);

public sealed record CharacterCreationResourcesLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationResourcesPreviewRequest(
    CharacterCreationResourcesBinding Binding,
    string OptionId);

public sealed record CharacterCreationResourcesConfirmRequest(
    CharacterCreationResourcesBinding Binding,
    string OptionId,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationResourcesReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationResourcesState(
    string Schema,
    string StepId,
    CharacterCreationResourcesBinding Binding,
    CharacterCreationResourcesAuthority Authority,
    CharacterCreationPrerequisiteDraft? PrerequisiteDraft,
    CharacterCreationResourcesDraft? PendingDraft,
    IReadOnlyList<CharacterCreationResourceAllocationOption> Options,
    CharacterCreationResourcesBudget Budget,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

public sealed record CharacterCreationResourcesPreview(
    string Schema,
    string StepId,
    CharacterCreationResourcesBinding Binding,
    CharacterCreationResourcesDraft? Before,
    CharacterCreationResourcesDraft After,
    CharacterCreationResourceAllocationOption? SelectedOption,
    CharacterCreationResourcesBudget BudgetBefore,
    CharacterCreationResourcesBudget BudgetAfter,
    CharacterCreationResourcesFinalizationContribution FinalizationContribution,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationResourcesReceipt(
    string Schema,
    string ReceiptId,
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKeyDigest,
    string CommandDigest,
    long PreviousWorkspaceRevision,
    long WorkspaceRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string PrerequisiteDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    string OptionId,
    int KarmaInvestment,
    decimal TotalStartingNuyen,
    decimal RemainingNuyen,
    long DraftRevision,
    string DraftDigest,
    string PreviewDigest,
    string PreviousReceiptDigest,
    bool CharacterDocumentChanged,
    string ReceiptDigest);

public sealed record CharacterCreationResourcesReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationResourcesReceipt Receipt);

public sealed record CharacterCreationResourcesResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationResourcesOutcomes.Available
        or CharacterCreationResourcesOutcomes.Applied
        or CharacterCreationResourcesOutcomes.Replayed;
}
