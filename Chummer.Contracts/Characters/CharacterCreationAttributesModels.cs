using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationAttributesSchemas
{
    public const string SnapshotV1 = "chummer.character_creation_attributes_snapshot.v1";
    public const string PreviewV1 = "chummer.character_creation_attributes_preview.v1";
    public const string DraftV1 = "chummer.character_creation_attributes_draft.v1";
}

public static class CharacterCreationAttributeCategories
{
    public const string Normal = "normal";
    public const string Special = "special";
}

public static class CharacterCreationAttributesBlockers
{
    public const string AllocationDuplicate = "creation-attributes-allocation-duplicate";
    public const string AllocationInvalid = "creation-attributes-allocation-invalid";
    public const string AttributeDisabled = "creation-attributes-attribute-disabled";
    public const string AuthorityUnavailable = "creation-attributes-authority-unavailable";
    public const string DraftConflict = "creation-attributes-draft-conflict";
    public const string DraftDuplicate = "creation-attributes-draft-duplicate";
    public const string DraftInvalid = "creation-attributes-draft-invalid";
    public const string EssenceNotSpendable = "creation-attributes-essence-not-spendable";
    public const string ExceptionalAttributeAuthorityRequired = "creation-attributes-exceptional-attribute-authority-required";
    public const string ExplicitConfirmationRequired = "creation-attributes-explicit-confirmation-required";
    public const string GlobalKarmaExceeded = "creation-attributes-global-karma-exceeded";
    public const string HouseRuleUnsupported = "creation-attributes-house-rule-unsupported";
    public const string LegacyAttributeStateRequiresImport = "creation-attributes-legacy-state-requires-import";
    public const string MaximumAttributeCountExceeded = "creation-attributes-maximum-count-exceeded";
    public const string MetatypeAuthorityIncomplete = "creation-attributes-metatype-authority-incomplete";
    public const string NormalPointsExceeded = "creation-attributes-normal-points-exceeded";
    public const string PersistenceAuthorityRequired = "creation-attributes-persistence-authority-required";
    public const string PrerequisiteDraftRequired = "creation-attributes-prerequisite-draft-required";
    public const string PrerequisiteSourceDrift = "creation-attributes-prerequisite-source-drift";
    public const string PreviewDigestMismatch = "creation-attributes-preview-digest-mismatch";
    public const string SpecialAttributeAuthorityIncomplete = "creation-attributes-special-authority-incomplete";
    public const string SpecialAttributeNotEnabled = "creation-attributes-special-not-enabled";
    public const string SpecialPointsExceeded = "creation-attributes-special-points-exceeded";
    public const string StaleRawCharacterXmlDigest = "creation-attributes-stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "creation-attributes-stale-workspace-revision";
    public const string WorkspaceUnavailable = "creation-attributes-workspace-unavailable";
}

public sealed record CharacterCreationAttributeAllocation(
    string AttributeId,
    int PriorityPoints,
    int KarmaLevels);

public sealed record CharacterCreationAttributeProjection(
    string AttributeId,
    string Category,
    int Minimum,
    int Maximum,
    int AugmentedMaximum,
    int Current,
    int PriorityPointsSpent,
    int KarmaLevels,
    int PriorityPointCost,
    int KarmaCost,
    bool IsEnabled,
    IReadOnlyList<string> DisableReasons,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationAttributesBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string PrerequisiteAuthorityDigest);

public sealed record CharacterCreationAttributesLoadRequest(CharacterWorkspaceId WorkspaceId);

public sealed record CharacterCreationAttributesPreviewRequest(
    CharacterCreationAttributesBinding Binding,
    IReadOnlyList<CharacterCreationAttributeAllocation> Allocations);

public sealed record CharacterCreationAttributesConfirmRequest(
    CharacterCreationAttributesBinding Binding,
    IReadOnlyList<CharacterCreationAttributeAllocation> Allocations,
    string PreviewDigest,
    bool ExplicitlyConfirmed);

public sealed record CharacterCreationAttributesDraft(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string PrerequisiteAuthorityDigest,
    string MetatypeSourceId,
    string MetatypeSourceNodeDigest,
    bool HalvesNormalAttributePoints,
    int NormalPointTotal,
    int NormalPointUsed,
    int SpecialPointTotal,
    int SpecialPointUsed,
    int CreationKarmaTotal,
    int CreationKarmaUsed,
    IReadOnlyList<CharacterCreationAttributeAllocation> Allocations,
    IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
    IReadOnlyList<string> SourceAnchorIds,
    bool CharacterEffectsApplied,
    string DraftDigest);

public sealed record CharacterCreationAttributesState(
    string Schema,
    CharacterCreationAttributesBinding Binding,
    CharacterCreationPrerequisiteDraft? PrerequisiteDraft,
    CharacterCreationAttributesDraft? PendingDraft,
    IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
    CharacterCreationBudgetState NormalPointBudget,
    CharacterCreationBudgetState SpecialPointBudget,
    CharacterCreationBudgetState CreationKarmaBudget,
    int MaxNumberMaxAttributesCreate,
    IReadOnlyList<string> Blockers,
    bool CanEdit,
    string SnapshotDigest)
{
    public int KarmaAttribute { get; init; }
}

public sealed record CharacterCreationAttributesPreview(
    string Schema,
    CharacterCreationAttributesBinding Binding,
    IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
    CharacterCreationBudgetState NormalPointBudget,
    CharacterCreationBudgetState SpecialPointBudget,
    CharacterCreationBudgetState CreationKarmaBudget,
    IReadOnlyList<string> Blockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    string PreviewDigest);

public sealed record CharacterCreationAttributesReceipt(
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    long DraftRevision,
    string DraftDigest,
    int NormalPointsRemaining,
    int SpecialPointsRemaining,
    int CreationKarmaRemaining,
    bool CharacterDocumentChanged);
