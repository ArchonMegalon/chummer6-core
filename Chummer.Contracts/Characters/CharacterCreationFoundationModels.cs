using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationFoundationSchemas
{
    public const string SnapshotV1 = "chummer.character_creation_foundation.v1";
    public const string PreviewV1 = "chummer.character_creation_foundation_preview.v1";
    public const string DraftLedgerV1 = "chummer.character_creation_foundation_draft_ledger.v1";
    public const string EffectCompilationV1 = "chummer.character_creation_foundation_effect_compilation.v1";
    public const string FinalizationPreviewV1 = "chummer.character_creation_foundation_finalization_preview.v1";
}

public static class CharacterCreationFoundationDigestSemantics
{
    public const string RawCharacterXmlSha256 = "raw-character-xml-utf8-sha256";
    public const string RawSourceInputsSha256 = "raw-source-inputs-sha256";
}

public static class CharacterCreationFoundationDiffPhases
{
    public const string DraftLedger = "draft-ledger";
    public const string CharacterFinalization = "character-finalization";
}

public static class CharacterCreationFoundationOutcomes
{
    public const string Success = "success";
    public const string Missing = "missing";
    public const string Conflict = "conflict";
    public const string Invalid = "invalid";
    public const string Blocked = "blocked";
}

public static class CharacterCreationFoundationBlockers
{
    public const string CharacterAlreadyCreated = "character-already-created";
    public const string CharacterDocumentInvalid = "character-document-invalid";
    public const string CharacterEligibilityAuthorityRequired = "character-eligibility-authority-required";
    public const string EnabledSourceAuthorityRequired = "enabled-source-authority-required";
    public const string ExplicitConfirmationRequired = "explicit-confirmation-required";
    public const string FinalizationDraftDigestConflict = "finalization-draft-digest-conflict";
    public const string FinalizationDraftRevisionConflict = "finalization-draft-revision-conflict";
    public const string FinalizationEffectLedgerConflict = "finalization-effect-ledger-conflict";
    public const string FinalizationEffectUnsupported = "finalization-effect-unsupported";
    public const string FinalizationPreviewDigestMismatch = "finalization-preview-digest-mismatch";
    public const string FinalizationPromptRequired = "finalization-prompt-required";
    public const string FinalizationRequiredStagesIncomplete = "finalization-required-stages-incomplete";
    public const string FinalizationRequirementUnsupported = "finalization-requirement-unsupported";
    public const string FinalizationRuntimeAuthorityRequired = "finalization-runtime-authority-required";
    public const string LifeModuleBuildMethodRequired = "life-module-build-method-required";
    public const string LifeModuleCatalogAuthorityRequired = "life-module-catalog-authority-required";
    public const string LifeModuleBudgetAuthorityRequired = "life-module-budget-authority-required";
    public const string LifeModuleBudgetExceeded = "life-module-budget-exceeded";
    public const string LifeModuleBudgetExistingSelectionAuthorityRequired = "life-module-budget-existing-selection-authority-required";
    public const string LifeModuleBudgetPendingDraftAuthorityRequired = "life-module-budget-pending-draft-authority-required";
    public const string LifeModuleBudgetProfileBuildMethodInvalid = "life-module-budget-profile-build-method-invalid";
    public const string LifeModuleBudgetProfileBuildMethodMismatch = "life-module-budget-profile-build-method-mismatch";
    public const string LifeModuleBudgetProfileBuildPointsInvalid = "life-module-budget-profile-build-points-invalid";
    public const string LifeModuleEffectApplicationAuthorityRequired = "life-module-effect-application-authority-required";
    public const string LifeModuleFollowUpOptionInvalid = "life-module-follow-up-option-invalid";
    public const string LifeModuleFollowUpRequired = "life-module-follow-up-required";
    public const string LifeModuleFollowUpUnknown = "life-module-follow-up-unknown";
    public const string LifeModuleRequirementNotMet = "life-module-requirement-not-met";
    public const string MetatypeCatalogAuthorityRequired = "metatype-catalog-authority-required";
    public const string MetatypeLegalityAuthorityRequired = "metatype-legality-authority-required";
    public const string MetatypeOptionNotFound = "metatype-option-not-found";
    public const string NationalityModuleNotFound = "nationality-module-not-found";
    public const string NationalityVersionNotApplicable = "nationality-version-not-applicable";
    public const string NationalityVersionNotFound = "nationality-version-not-found";
    public const string NationalityVersionRequired = "nationality-version-required";
    public const string PreviewDigestMismatch = "preview-digest-mismatch";
    public const string PendingDraftConflict = "pending-draft-conflict";
    public const string PendingDraftDuplicate = "pending-draft-duplicate";
    public const string PendingDraftInvalid = "pending-draft-invalid";
    public const string RulesetSr5Required = "ruleset-sr5-required";
    public const string SourceDigestConflict = "source-digest-conflict";
    public const string StaleRawCharacterXmlDigest = "stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "stale-workspace-revision";
    public const string WizardStatePersistenceAuthorityRequired = "wizard-state-persistence-authority-required";
    public const string WorkspaceUnavailable = "workspace-unavailable";
}

public static class CharacterCreationFoundationEffectCompilationStatuses
{
    public const string Supported = "supported";
    public const string Unsupported = "unsupported";
    public const string PromptRequired = "prompt-required";
}

public static class CharacterCreationFoundationEffectSourcePhases
{
    public const string Version = "version";
    public const string Module = "module";
}

public static class CharacterCreationFoundationResumeStatuses
{
    public const string AuthorityRequired = "authority-required";
    public const string PendingDraft = "pending-draft";
}

public static class CharacterCreationFoundationDraftStatuses
{
    public const string PendingFinalization = "pending-finalization";
}

public sealed record CharacterCreationFoundationBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string CharacterDigestSemantics,
    string SourceDigest,
    string SourceDigestSemantics,
    bool SourceFilterApplied,
    IReadOnlyList<string> EnabledSources);

public sealed record CharacterCreationFoundationLoadRequest(
    CharacterWorkspaceId WorkspaceId,
    IReadOnlyCollection<string>? EnabledSources = null);

public sealed record CharacterCreationFoundationSelection(
    string ModuleId,
    string? VersionId);

public sealed record CharacterCreationFoundationDraftLedger(
    string Schema,
    CharacterWorkspaceId WorkspaceId,
    long DraftRevision,
    long BaseContentRevision,
    string BaseRawCharacterXmlDigest,
    string SourceDigest,
    string RequestedMetatype,
    CharacterCreationFoundationSelection Selection,
    IReadOnlyList<LifeModuleRequirementProjectionDto> RequirementEvaluations,
    IReadOnlyList<LifeModuleEffectProjectionDto> ProjectedEffects,
    IReadOnlyDictionary<string, string> FollowUpValues,
    IReadOnlyList<string> SourceAnchorIds,
    string CompilationStatus,
    bool CharacterEffectsApplied,
    string DraftDigest);

public sealed record CharacterCreationFoundationState(
    string Schema,
    CharacterCreationFoundationBinding Binding,
    string RulesetId,
    string CurrentMetatype,
    string BuildMethod,
    bool CharacterCreated,
    IReadOnlyList<CharacterCreationLegalOption> MetatypeOptions,
    IReadOnlyList<LifeModuleLegalOptionDto> NationalityOptions,
    CharacterCreationBudgetState LifeModuleBudget,
    CharacterCreationFoundationDraftLedger? PendingDraft,
    string ResumeStatus,
    IReadOnlyList<string> AuthorityBlockers,
    string SnapshotDigest);

public sealed record CharacterCreationFoundationPreviewRequest(
    CharacterCreationFoundationBinding Binding,
    string RequestedMetatype,
    CharacterCreationFoundationSelection Selection,
    IReadOnlyDictionary<string, string>? FollowUpValues = null);

public sealed record CharacterCreationFoundationDiffEntry(
    string DiffId,
    string Domain,
    string TargetId,
    string? BeforeValue,
    string? AfterValue,
    string Phase,
    bool AppliesToCharacterDocument,
    bool IsAuthoritative,
    bool CanApply,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationFoundationPreview(
    string Schema,
    CharacterCreationFoundationBinding Binding,
    string RequestedMetatype,
    CharacterCreationFoundationSelection Selection,
    LifeModuleLegalOptionDto? Nationality,
    LifeModuleVersionProjectionDto? NationalityVersion,
    IReadOnlyList<LifeModuleRequirementProjectionDto> RequirementEvaluations,
    IReadOnlyDictionary<string, string> FollowUpValues,
    CharacterCreationBudgetState LifeModuleBudgetBefore,
    CharacterCreationChoiceCost SelectionCost,
    CharacterCreationBudgetState LifeModuleBudgetAfter,
    IReadOnlyList<CharacterCreationFoundationDiffEntry> Diff,
    IReadOnlyList<string> AuthorityBlockers,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    bool CanApply,
    bool CharacterEffectsApplied,
    string PreviewDigest);

public sealed record CharacterCreationFoundationConfirmRequest(
    CharacterCreationFoundationBinding Binding,
    string RequestedMetatype,
    CharacterCreationFoundationSelection Selection,
    string PreviewDigest,
    bool ExplicitlyConfirmed,
    IReadOnlyDictionary<string, string>? FollowUpValues = null)
{
    /// <summary>
    /// Optional sealed Origin decision command.  Foundation remains the sole
    /// mechanics authority; this binding only lets the same atomic commit append
    /// the restart/idempotency receipt used by the narrative projection.
    /// </summary>
    public LifeModuleDecisionAcceptanceCommand? OriginDecisionCommand { get; init; }

    public LifeModuleDecisionAuthorityStep? OriginDecisionStep { get; init; }
}

public sealed record CharacterCreationFoundationFinalizationPreviewRequest(
    CharacterCreationFoundationBinding Binding,
    long DraftRevision,
    string DraftDigest);

public sealed record CharacterCreationFoundationFinalizationConfirmRequest(
    CharacterCreationFoundationBinding Binding,
    long DraftRevision,
    string DraftDigest,
    string PreviewDigest,
    bool ExplicitlyConfirmed);

/// <summary>
/// One deterministic compiler instruction derived from the persisted draft and
/// the currently-authoritative source projection.  Unsupported instructions are
/// reviewable but can never be partially applied.
/// </summary>
public sealed record CharacterCreationFoundationEffectInstruction(
    int Order,
    string EffectId,
    string SourcePhase,
    string EffectKind,
    string Domain,
    string TargetId,
    IReadOnlyDictionary<string, string> Parameters,
    IReadOnlyList<string> PromptIds,
    IReadOnlyList<string> SourceAnchorIds,
    string CompilationStatus,
    string? Blocker,
    string InstructionDigest)
{
    /// <summary>
    /// Exact source-data or runtime-domain identity used when an effect targets
    /// a catalog entity or a typed aggregate such as the free knowledge-skill
    /// pool. The legacy character XML may store a canonical English name or an
    /// empty improved name; this binding prevents an ambiguous or stale
    /// projection from authorizing the write plan.
    /// </summary>
    public CharacterCreationFoundationEffectTargetBinding? TargetBinding { get; init; }

    /// <summary>
    /// Source values which Chummer5 deliberately ignores for this effect. They
    /// remain digest-bound and reviewable instead of being silently discarded.
    /// </summary>
    public IReadOnlyDictionary<string, string> IgnoredSourceMetadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record CharacterCreationFoundationEffectTargetBinding(
    string TargetKind,
    string SourceId,
    string CanonicalName,
    string SourceDigest);

public sealed record CharacterCreationFoundationRequirementInstruction(
    int Order,
    string RequirementId,
    string Operator,
    string SubjectKind,
    IReadOnlyList<string> AcceptedValues,
    IReadOnlyList<string> SourceAnchorIds,
    string CompilationStatus,
    string? Blocker,
    string InstructionDigest);

public sealed record CharacterCreationFoundationEffectCompilation(
    string Schema,
    string CompilerRuntimeDigest,
    long DraftRevision,
    string DraftDigest,
    IReadOnlyList<CharacterCreationFoundationRequirementInstruction> Requirements,
    IReadOnlyList<CharacterCreationFoundationEffectInstruction> Effects,
    IReadOnlyList<string> Blockers,
    bool IsCompleteLedgerSupported,
    string CompilationDigest)
{
    public IReadOnlyList<CharacterCreationFoundationSelectionPushInstruction>
        SelectionPushes { get; init; } = [];

    public IReadOnlyList<CharacterCreationFoundationSelectionConsumerInstruction>
        SelectionConsumers { get; init; } = [];

    public IReadOnlyList<CharacterCreationFoundationSelectionBinding>
        SelectionBindings { get; init; } = [];

    public IReadOnlyList<CharacterCreationFoundationDependentQualityInstruction>
        DependentQualities { get; init; } = [];
}

/// <summary>
/// A transient legacy pushtext stack entry. It is compilation provenance and
/// never a serialized Improvement.
/// </summary>
public sealed record CharacterCreationFoundationSelectionPushInstruction(
    int Order,
    string EffectId,
    string SourcePhase,
    string Literal,
    string SourceDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string InstructionDigest);

/// <summary>
/// One dependent Quality selecttext pre-pass which consumes the current top of
/// the transient pushtext stack before that Quality's bonus is interpreted.
/// </summary>
public sealed record CharacterCreationFoundationSelectionConsumerInstruction(
    int Order,
    string ConsumerId,
    string EffectId,
    int AddQualityIndex,
    string OwnerSourceDigest,
    CharacterCreationFoundationEffectTargetBinding TargetBinding,
    string SourceNodeDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string InstructionDigest);

public sealed record CharacterCreationFoundationSelectionBinding(
    string PushEffectId,
    string ConsumerId,
    string Literal,
    string PushInstructionDigest,
    string ConsumerInstructionDigest,
    string BindingDigest);

/// <summary>
/// Exact qualities.xml identity and source position of every Quality created by
/// one addqualities effect, including qualities which do not consume text.
/// </summary>
public sealed record CharacterCreationFoundationDependentQualityInstruction(
    int Order,
    string EffectId,
    int AddQualityIndex,
    string OwnerSourceDigest,
    CharacterCreationFoundationEffectTargetBinding TargetBinding,
    string SourceNodeDigest,
    string? SelectionConsumerId,
    bool HasRuntimeRequirements,
    string CompilationStatus,
    string? Blocker,
    string InstructionDigest);

public sealed record CharacterCreationFoundationFinalizationPreview(
    string Schema,
    CharacterCreationFoundationBinding Binding,
    CharacterCreationFoundationEffectCompilation Compilation,
    IReadOnlyList<string> FinalizationBlocked,
    bool RequiresExplicitConfirmation,
    bool CanConfirm,
    bool CanApply,
    bool CharacterEffectsApplied,
    bool CharacterCreated,
    string PreviewDigest);

public sealed record CharacterCreationFoundationFinalizationReceipt(
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string SourceDigest,
    string CompilerRuntimeDigest,
    long DraftRevision,
    string DraftDigest,
    string CompilationDigest,
    string PreviewDigest,
    bool CharacterEffectsApplied,
    bool CharacterCreated,
    bool RequiresFreshCareerReopen);

public sealed record CharacterCreationFoundationApplyReceipt(
    CharacterWorkspaceId WorkspaceId,
    long PreviousContentRevision,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string SourceDigest,
    string PreviewDigest,
    CharacterCreationFoundationSelection Selection,
    string Metatype,
    long DraftRevision,
    string DraftDigest,
    bool CharacterEffectsApplied)
{
    public LifeModuleDecisionAcceptance? OriginDecisionAcceptance { get; init; }
}

public sealed record CharacterCreationFoundationResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class;
