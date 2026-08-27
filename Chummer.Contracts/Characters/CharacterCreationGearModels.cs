using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationGearSchemas
{
    public const string AuthorityV1 = "chummer.sr5.creation-gear.authority.v1";
    public const string StateV1 = "chummer.sr5.creation-gear.state.v1";
    public const string PreviewV1 = "chummer.sr5.creation-gear.preview.v1";
    public const string DraftV1 = "chummer.sr5.creation-gear.draft.v1";
    public const string ContributionV1 = "chummer.sr5.creation-gear.finalization-contribution.v1";
    public const string ReceiptV1 = "chummer.sr5.creation-gear.receipt.v1";
    public const string RulesV1 = "chummer.sr5.creation-gear.rules.v1";
    public const string RuntimeV1 = "chummer.sr5.creation-gear.runtime.v1";
}

public static class CharacterCreationGearBlockers
{
    public const string AuthorityUnavailable = "creation-gear-authority-unavailable";
    public const string CareerModeRejected = "creation-gear-career-mode-rejected";
    public const string CharacterDocumentInvalid = "creation-gear-character-document-invalid";
    public const string DuplicateOption = "creation-gear-duplicate-option";
    public const string ExplicitConfirmationRequired = "creation-gear-explicit-confirmation-required";
    public const string IdempotencyConflict = "creation-gear-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-gear-idempotency-key-invalid";
    public const string InsufficientFunds = "creation-gear-insufficient-funds";
    public const string InvalidBasket = "creation-gear-invalid-basket";
    public const string InvalidOption = "creation-gear-invalid-option";
    public const string InvalidQuantity = "creation-gear-invalid-quantity";
    public const string NoChange = "creation-gear-no-change";
    public const string PersistenceAuthorityRequired = "creation-gear-persistence-authority-required";
    public const string PreviewDigestMismatch = "creation-gear-preview-digest-mismatch";
    public const string ReceiptLedgerCorrupt = "creation-gear-receipt-ledger-corrupt";
    public const string ResourcesDraftRequired = "creation-gear-resources-draft-required";
    public const string ResourcesDraftStale = "creation-gear-resources-draft-stale";
    public const string RulesetSr5Required = "creation-gear-ruleset-sr5-required";
    public const string SourceDisabled = "creation-gear-source-disabled";
    public const string AvailabilityExceeded = "creation-gear-availability-exceeded";
    public const string UnsupportedSemantics = "creation-gear-unsupported-semantics";
    public const string StaleAuxiliaryStateDigest = "creation-gear-stale-auxiliary-state-digest";
    public const string StaleContentDigest = "creation-gear-stale-content-digest";
    public const string StaleResourcesDraft = "creation-gear-stale-resources-draft";
    public const string StaleRulesDigest = "creation-gear-stale-rules-digest";
    public const string StaleRuntimeDigest = "creation-gear-stale-runtime-digest";
    public const string StaleSourceDigest = "creation-gear-stale-source-digest";
    public const string StaleWorkspaceRevision = "creation-gear-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-gear-workspace-unavailable";
}

public static class CharacterCreationGearOutcomes
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

public static class CharacterCreationGearLegality
{
    public const string Legal = "legal";
    public const string Restricted = "restricted";
    public const string Forbidden = "forbidden";
}

public static class CharacterCreationGearSourceAnchors
{
    public const string Step = "CharacterCreationWizardStepIds.Resources";
    public const string Catalog = "gear.xml#gears";
    public const string LegacyGear = "Chummer/Backend/Equipment/Gear.cs";
    public const string LegacySelect = "Chummer/Forms/Selection Forms/SelectGear.cs";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Step, Catalog, LegacyGear, LegacySelect
    });
}

public sealed record CharacterCreationGearCatalogOption(
    string OptionId,
    Guid SourceId,
    string Name,
    string Category,
    decimal PackageCost,
    int PackageQuantity,
    int Availability,
    string Legality,
    string SourceBook,
    string Page,
    bool IsSelectable,
    bool PricingIsExact,
    bool AvailabilityIsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceNodeXml,
    string SourceNodeDigest,
    string OptionDigest);

public sealed record CharacterCreationGearAuthority(
    string Schema,
    string RulesetId,
    string SettingsProfileId,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine,
    IReadOnlyList<CharacterCreationGearCatalogOption> Options,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationGearAuthority Unavailable { get; } = new(
        CharacterCreationGearSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        0,
        0,
        0,
        [],
        [],
        [CharacterCreationGearBlockers.AuthorityUnavailable],
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

public sealed record CharacterCreationGearSelection(string OptionId, int Quantity);

public sealed record CharacterCreationGearLine(
    string OptionId,
    Guid SourceId,
    string Name,
    string Category,
    int Quantity,
    int PackageQuantity,
    decimal PackageCost,
    decimal TotalCost,
    int Availability,
    string Legality,
    string SourceBook,
    string Page,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceNodeXml,
    string SourceNodeDigest,
    string LineDigest);

public sealed record CharacterCreationGearBudget(
    decimal TotalStartingNuyen,
    decimal BasketCost,
    decimal RemainingNuyen,
    decimal Overspend,
    bool IsExact,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCreationGearBinding(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long ResourcesDraftRevision,
    string ResourcesDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest);

public sealed record CharacterCreationGearFinalizationContribution(
    string Schema,
    string ExpectedRawCharacterXmlDigest,
    long ResourcesDraftRevision,
    string ResourcesDraftDigest,
    IReadOnlyList<CharacterCreationGearLine> Lines,
    decimal TotalCost,
    IReadOnlyList<string> SourceAnchorIds,
    string ContributionDigest);

public sealed record CharacterCreationGearDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    long ResourcesDraftRevision,
    string ResourcesDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    IReadOnlyList<CharacterCreationGearLine> Lines,
    CharacterCreationGearBudget Budget,
    CharacterCreationGearFinalizationContribution FinalizationContribution,
    bool CharacterEffectsApplied,
    string LastIdempotencyKeyDigest,
    string LastPreviewDigest,
    string LastCommandDigest,
    string DraftDigest);

public sealed record CharacterCreationGearLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationGearPreviewRequest(
    CharacterCreationGearBinding Binding,
    IReadOnlyList<CharacterCreationGearSelection> Basket);

public sealed record CharacterCreationGearConfirmRequest(
    CharacterCreationGearBinding Binding,
    IReadOnlyList<CharacterCreationGearSelection> Basket,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationGearReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationGearState(
    string Schema,
    string StepId,
    CharacterCreationGearBinding Binding,
    CharacterCreationGearAuthority Authority,
    CharacterCreationResourcesDraft? ResourcesDraft,
    CharacterCreationGearDraft? PendingDraft,
    CharacterCreationGearBudget Budget,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

public sealed record CharacterCreationGearPreview(
    string Schema,
    string StepId,
    CharacterCreationGearBinding Binding,
    CharacterCreationGearDraft? Before,
    CharacterCreationGearDraft After,
    CharacterCreationGearBudget BudgetBefore,
    CharacterCreationGearBudget BudgetAfter,
    CharacterCreationGearFinalizationContribution FinalizationContribution,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationGearReceipt(
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
    long ResourcesDraftRevision,
    string ResourcesDraftDigest,
    string AuthorityDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    int LineCount,
    decimal BasketCost,
    decimal RemainingNuyen,
    long DraftRevision,
    string DraftDigest,
    string PreviewDigest,
    string PreviousReceiptDigest,
    bool CharacterDocumentChanged,
    string ReceiptDigest);

public sealed record CharacterCreationGearReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationGearReceipt Receipt);

public sealed record CharacterCreationGearResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationGearOutcomes.Available
        or CharacterCreationGearOutcomes.Applied
        or CharacterCreationGearOutcomes.Replayed;
}
