using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationLifestylesSchemas
{
    public const string AuthorityV1 = "chummer.sr5.creation-lifestyles.authority.v1";
    public const string StateV1 = "chummer.sr5.creation-lifestyles.state.v1";
    public const string PreviewV1 = "chummer.sr5.creation-lifestyles.preview.v1";
    public const string WritePlanV1 = "chummer.sr5.creation-lifestyles.write-plan.v1";
    public const string ReceiptV1 = "chummer.sr5.creation-lifestyles.receipt.v1";
    public const string RulesV1 = "chummer.sr5.creation-lifestyles.rules.v1";
    public const string RuntimeV1 = "chummer.sr5.creation-lifestyles.runtime.v1";
}

public static class CharacterCreationLifestyleMutationKinds
{
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Delete = "delete";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Create, Edit, Delete
    });
}

public static class CharacterCreationLifestyleStyleIds
{
    public const string Standard = "standard";
    public const string Advanced = "advanced";
    public const string BoltHole = "bolt-hole";
    public const string Safehouse = "safehouse";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Standard, Advanced, BoltHole, Safehouse
    });
}

public static class CharacterCreationLifestyleIncrementIds
{
    public const string Day = "day";
    public const string Week = "week";
    public const string Month = "month";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Day, Week, Month
    });
}

public static class CharacterCreationLifestyleQualityTypes
{
    public const string Entertainment = "entertainment";
    public const string Positive = "positive";
    public const string Negative = "negative";
    public const string Contracts = "contracts";
}

public static class CharacterCreationLifestylesBlockers
{
    public const string AuthorityUnavailable = "creation-lifestyles-authority-unavailable";
    public const string CareerModeRejected = "creation-lifestyles-career-mode-rejected";
    public const string CharacterDocumentInvalid = "creation-lifestyles-character-document-invalid";
    public const string DuplicateIdentity = "creation-lifestyles-duplicate-identity";
    public const string ExplicitConfirmationRequired = "creation-lifestyles-explicit-confirmation-required";
    public const string IdempotencyConflict = "creation-lifestyles-idempotency-conflict";
    public const string IdempotencyKeyInvalid = "creation-lifestyles-idempotency-key-invalid";
    public const string InsufficientFunds = "creation-lifestyles-insufficient-funds";
    public const string InvalidIdentity = "creation-lifestyles-invalid-identity";
    public const string InvalidMutation = "creation-lifestyles-invalid-mutation";
    public const string InvalidOption = "creation-lifestyles-invalid-option";
    public const string LifestyleNotFound = "creation-lifestyles-not-found";
    public const string LifestylePointsExceeded = "creation-lifestyles-lifestyle-points-exceeded";
    public const string NoChange = "creation-lifestyles-no-change";
    public const string PersistenceAuthorityRequired = "creation-lifestyles-persistence-authority-required";
    public const string PreviewDigestMismatch = "creation-lifestyles-preview-digest-mismatch";
    public const string ReceiptLedgerCorrupt = "creation-lifestyles-receipt-ledger-corrupt";
    public const string RulesetSr5Required = "creation-lifestyles-ruleset-sr5-required";
    public const string SourceDisabled = "creation-lifestyles-source-disabled";
    public const string SourceIdentityMismatch = "creation-lifestyles-source-identity-mismatch";
    public const string StaleAuxiliaryStateDigest = "creation-lifestyles-stale-auxiliary-state-digest";
    public const string StaleContentDigest = "creation-lifestyles-stale-content-digest";
    public const string StaleRulesDigest = "creation-lifestyles-stale-rules-digest";
    public const string StaleRuntimeDigest = "creation-lifestyles-stale-runtime-digest";
    public const string StaleSourceDigest = "creation-lifestyles-stale-source-digest";
    public const string StaleWorkspaceRevision = "creation-lifestyles-stale-workspace-revision";
    public const string UnsupportedSemantics = "creation-lifestyles-unsupported-semantics";
    public const string WorkspaceUnavailable = "creation-lifestyles-workspace-unavailable";
}

public static class CharacterCreationLifestyleOutcomes
{
    public const string Available = "available";
    public const string Applied = "applied";
    public const string Replayed = "replayed";
    public const string NotFound = "not-found";
    public const string Blocked = "blocked";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Missing = "missing";
    public const string Corrupt = "corrupt";
    public const string Unavailable = "unavailable";
}

public static class CharacterCreationLifestyleSourceAnchors
{
    public const string Step = "CharacterCreationWizardStepIds.ContactsLifestyles";
    public const string LegacyModel = "Chummer/Backend/Equipment/Lifestyle.cs";
    public const string LegacyCostPreSplit = "Chummer/Backend/Equipment/Lifestyle.cs#CostPreSplit";
    public const string LegacyMonthlyCost = "Chummer/Backend/Equipment/Lifestyle.cs#GetTotalMonthlyCost";
    public const string LegacyAccept = "Chummer/Forms/Selection Forms/SelectLifestyle.cs#AcceptForm";
    public const string LifestyleCatalog = "lifestyles.xml#lifestyles";
    public const string LifestyleQualityCatalog = "lifestyles.xml#qualities";

    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
    {
        Step,
        LegacyModel,
        LegacyCostPreSplit,
        LegacyMonthlyCost,
        LegacyAccept,
        LifestyleCatalog,
        LifestyleQualityCatalog
    });
}

public sealed record CharacterCreationLifestyleBinding(
    CharacterWorkspaceId WorkspaceId,
    long WorkspaceRevision,
    long ContentRevision,
    long SavedRevision,
    string ContentDigest,
    string AuxiliaryStateDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest);

public sealed record CharacterCreationLifestyleBuiltInQuality(
    string QualityOptionId,
    string Extra,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationLifestyleCatalogOption(
    string OptionId,
    Guid SourceId,
    string Name,
    decimal BaseCost,
    int StartingNuyenDice,
    decimal StartingNuyenMultiplier,
    int LifestylePoints,
    decimal CostPerArea,
    decimal CostPerComfort,
    decimal CostPerSecurity,
    int BaseArea,
    int MaximumArea,
    int BaseComforts,
    int MaximumComforts,
    int BaseSecurity,
    int MaximumSecurity,
    bool AllowsBonusLifestylePoints,
    string DefaultIncrementId,
    string SourceBook,
    string Page,
    IReadOnlyList<CharacterCreationLifestyleBuiltInQuality> BuiltInQualities,
    bool IsSelectable,
    bool EligibilityIsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

public sealed record CharacterCreationLifestyleQualityCatalogOption(
    string OptionId,
    Guid SourceId,
    string Name,
    string Category,
    string SourceBook,
    string Page,
    string QualityType,
    int LifestylePointCost,
    decimal FlatCost,
    decimal CostMultiplierPercent,
    decimal BaseCostMultiplierPercent,
    int Area,
    int Comforts,
    int Security,
    int AreaMaximumModifier,
    int ComfortsMaximumModifier,
    int SecurityMaximumModifier,
    IReadOnlyList<string> AllowedFreeLifestyleNames,
    bool IsSelectable,
    bool EligibilityIsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string OptionDigest);

public sealed record CharacterCreationLifestylesAuthority(
    string Schema,
    string RulesetId,
    string SettingsProfileId,
    IReadOnlyList<CharacterCreationLifestyleCatalogOption> LifestyleOptions,
    IReadOnlyList<CharacterCreationLifestyleQualityCatalogOption> QualityOptions,
    int TrustFundLevel,
    bool FreeGridsEnabled,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string SourceDigest,
    string ProfileDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationLifestylesAuthority Unavailable { get; } = new(
        CharacterCreationLifestylesSchemas.AuthorityV1,
        string.Empty,
        string.Empty,
        [],
        [],
        0,
        false,
        [],
        [CharacterCreationLifestylesBlockers.AuthorityUnavailable],
        false,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);
}

public sealed record CharacterCreationLifestyleQualitySelection(
    Guid InstanceId,
    string OptionId,
    string Extra,
    bool UseLifestylePoints,
    bool IsFree,
    bool IsBuiltIn);

public sealed record CharacterCreationLifestyleConfiguration(
    Guid LifestyleId,
    string BaseLifestyleOptionId,
    string Name,
    string StyleId,
    string IncrementId,
    int Increments,
    decimal Percentage,
    int Roommates,
    bool SplitCostWithRoommates,
    bool TrustFund,
    int Area,
    int Comforts,
    int Security,
    int BonusLifestylePoints,
    string City,
    string District,
    string Borough,
    IReadOnlyList<CharacterCreationLifestyleQualitySelection> Qualities);

public sealed record CharacterCreationLifestyleMutation(
    string MutationKind,
    Guid LifestyleId,
    CharacterCreationLifestyleConfiguration? Configuration);

public sealed record CharacterCreationLifestyleEconomics(
    decimal CostPerIncrement,
    decimal TotalCost,
    int LifestylePointsTotal,
    int LifestylePointsRemaining,
    bool CoveredByTrustFund,
    bool SplitWithRoommates,
    IReadOnlyList<string> Blockers);

public sealed record CharacterCreationLifestyleProjection(
    CharacterCreationLifestyleConfiguration Configuration,
    Guid SourceId,
    string BaseLifestyleName,
    string SourceBook,
    string Page,
    CharacterCreationLifestyleEconomics Economics,
    IReadOnlyList<string> SourceAnchorIds,
    string LifestyleDigest);

public sealed record CharacterCreationLifestyleBudget(
    decimal Total,
    decimal Used,
    decimal Remaining,
    decimal Overspend,
    bool IsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationLifestylesLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationLifestylePreviewRequest(
    CharacterCreationLifestyleBinding Binding,
    CharacterCreationLifestyleMutation Mutation);

public sealed record CharacterCreationLifestyleConfirmRequest(
    CharacterCreationLifestyleBinding Binding,
    CharacterCreationLifestyleMutation Mutation,
    string PreviewDigest,
    string IdempotencyKey,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationLifestyleReceiptLookupRequest(
    CharacterWorkspaceId WorkspaceId,
    string IdempotencyKey);

public sealed record CharacterCreationLifestyleWriteOperation(
    int Order,
    string MutationKind,
    Guid LifestyleId,
    string BeforeDigest,
    string AfterDigest,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationLifestyleAtomicWritePlan(
    string Schema,
    string StepId,
    string MutationKind,
    Guid LifestyleId,
    CharacterCreationLifestyleProjection? Before,
    CharacterCreationLifestyleProjection? After,
    IReadOnlyList<CharacterCreationLifestyleWriteOperation> Operations,
    string ContentDigestBefore,
    string ContentDigestAfter,
    string UntouchedSiblingDigestBefore,
    string UntouchedSiblingDigestAfter,
    string NestedStateDigestBefore,
    string NestedStateDigestAfter,
    bool PreservesUntouchedSiblingState,
    bool PreservesNestedState,
    string PlanDigest);

public sealed record CharacterCreationLifestylesState(
    string Schema,
    string StepId,
    CharacterCreationLifestyleBinding Binding,
    CharacterCreationLifestylesAuthority Authority,
    bool CharacterCreated,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lifestyles,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest);

public sealed record CharacterCreationLifestylePreview(
    string Schema,
    string StepId,
    CharacterCreationLifestyleBinding Binding,
    string MutationKind,
    CharacterCreationLifestyleProjection? Before,
    CharacterCreationLifestyleProjection? After,
    CharacterCreationLifestyleBudget BudgetBefore,
    CharacterCreationLifestyleBudget BudgetAfter,
    CharacterCreationLifestyleAtomicWritePlan WritePlan,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationLifestyleReceipt(
    string Schema,
    string ReceiptId,
    string StepId,
    CharacterWorkspaceId WorkspaceId,
    string MutationKind,
    Guid LifestyleId,
    string IdempotencyKeyDigest,
    string CommandDigest,
    long PreviousWorkspaceRevision,
    long WorkspaceRevision,
    long PreviousContentRevision,
    long ContentRevision,
    long PreviousSavedRevision,
    long SavedRevision,
    string ContentDigestBefore,
    string ContentDigestAfter,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    decimal LifestyleCostBefore,
    decimal LifestyleCostAfter,
    decimal LifestyleBudgetRemaining,
    CharacterCreationLifestyleAtomicWritePlan WritePlan,
    string ReceiptDigest);

public sealed record CharacterCreationLifestyleReceiptLedgerEntry(
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterCreationLifestyleReceipt Receipt);

public sealed record CharacterCreationLifestyleResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class
{
    public bool Success => Outcome is CharacterCreationLifestyleOutcomes.Available
        or CharacterCreationLifestyleOutcomes.Applied
        or CharacterCreationLifestyleOutcomes.Replayed;
}
