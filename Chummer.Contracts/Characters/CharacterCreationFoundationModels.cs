using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationFoundationSchemas
{
    public const string SnapshotV1 = "chummer.character_creation_foundation.v1";
    public const string PreviewV1 = "chummer.character_creation_foundation_preview.v1";
    public const string DraftLedgerV1 = "chummer.character_creation_foundation_draft_ledger.v1";
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
    public const string LifeModuleBuildMethodRequired = "life-module-build-method-required";
    public const string LifeModuleCatalogAuthorityRequired = "life-module-catalog-authority-required";
    public const string LifeModuleBudgetAuthorityRequired = "life-module-budget-authority-required";
    public const string LifeModuleEffectApplicationAuthorityRequired = "life-module-effect-application-authority-required";
    public const string LifeModuleFollowUpOptionInvalid = "life-module-follow-up-option-invalid";
    public const string LifeModuleFollowUpRequired = "life-module-follow-up-required";
    public const string LifeModuleFollowUpUnknown = "life-module-follow-up-unknown";
    public const string LifeModuleRequirementNotMet = "life-module-requirement-not-met";
    public const string MetatypeCatalogAuthorityRequired = "metatype-catalog-authority-required";
    public const string MetatypeLegalityAuthorityRequired = "metatype-legality-authority-required";
    public const string NationalityModuleNotFound = "nationality-module-not-found";
    public const string NationalityVersionNotApplicable = "nationality-version-not-applicable";
    public const string NationalityVersionNotFound = "nationality-version-not-found";
    public const string NationalityVersionRequired = "nationality-version-required";
    public const string PreviewDigestMismatch = "preview-digest-mismatch";
    public const string RulesetSr5Required = "ruleset-sr5-required";
    public const string SourceDigestConflict = "source-digest-conflict";
    public const string StaleRawCharacterXmlDigest = "stale-raw-character-xml-digest";
    public const string StaleWorkspaceRevision = "stale-workspace-revision";
    public const string WizardStatePersistenceAuthorityRequired = "wizard-state-persistence-authority-required";
    public const string WorkspaceUnavailable = "workspace-unavailable";
}

public static class CharacterCreationFoundationResumeStatuses
{
    public const string AuthorityRequired = "authority-required";
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
    IReadOnlyDictionary<string, string>? FollowUpValues = null);

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
    bool CharacterEffectsApplied);

public sealed record CharacterCreationFoundationResult<T>(
    string Outcome,
    T? Value,
    IReadOnlyList<string> Blockers)
    where T : class;
