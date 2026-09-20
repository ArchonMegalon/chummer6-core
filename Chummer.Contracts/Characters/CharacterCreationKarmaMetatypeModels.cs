using Chummer.Contracts.Workspaces;
using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationKarmaMetatypeSchemas
{
    public const string SnapshotV1 = "chummer.character_creation_karma_metatype_snapshot.v1";
    public const string QuoteV1 = "chummer.character_creation_karma_metatype_quote.v1";
    public const string DecisionV1 = "chummer.character_creation_karma_metatype_decision.v1";
}

public static class CharacterCreationKarmaMetatypeBlockers
{
    public const string WorkspaceUnavailable = "creation-karma-workspace-unavailable";
    public const string PendingKarmaBootstrapRequired = "creation-karma-pending-bootstrap-required";
    public const string BudgetAuthorityRequired = "creation-karma-budget-authority-required";
    public const string MetatypeAuthorityRequired = "creation-karma-metatype-authority-required";
    public const string StaleBinding = "creation-karma-stale-binding";
    public const string OptionUnavailable = "creation-karma-metatype-option-unavailable";
    public const string BudgetExceeded = "creation-karma-metatype-budget-exceeded";
    public const string ConfirmationRequired = "creation-karma-confirmation-required";
    public const string PersistenceUnavailable = "creation-karma-persistence-unavailable";
    public const string IdempotencyConflict = "creation-karma-idempotency-conflict";
    public const string HistoryInvalid = "creation-karma-history-invalid";
    public const string TalentAuthorityRequired = "creation-karma-talent-authority-required";
    public const string TalentSelectionRequired = "creation-karma-talent-selection-required";
    public const string AttributeSelectionRequired = "creation-karma-attribute-selection-required";
    public const string SkillsSelectionRequired = "creation-karma-skills-selection-required";
    public const string SkillsAuthorityRequired = "creation-karma-skills-authority-required";
    public const string ResourcesAuthorityRequired = "creation-karma-resources-authority-required";
    public const string ResourcesSelectionRequired = "creation-karma-resources-selection-required";
    public const string ResourceInvestmentInvalid = "creation-karma-resources-investment-invalid";
    public const string QualitiesAuthorityRequired = "creation-karma-qualities-authority-required";
    public const string QualitiesSelectionRequired = "creation-karma-qualities-selection-required";
    public const string GearAuthorityRequired = "creation-karma-gear-authority-required";
    public const string GearSelectionRequired = "creation-karma-gear-selection-required";
}

public sealed record CharacterCreationKarmaMetatypeBinding(
    CharacterWorkspaceId WorkspaceId,
    long ContentRevision,
    long SavedRevision,
    string RawCharacterXmlDigest,
    string AuxiliaryStateDigest,
    string BootstrapBindingDigest,
    string SourceProfileDigest,
    string MetatypeAuthorityDigest,
    string? TalentAuthorityDigest = null,
    string? AttributePolicyDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkillsPolicyDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SkillsCatalogDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ResourcesPolicyDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QualitiesPolicyDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? QualitiesCatalogDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? GearAuthorityDigest = null);

public sealed record CharacterCreationKarmaMetatypeState(
    string Schema,
    CharacterCreationKarmaMetatypeBinding Binding,
    string SettingsProfileId,
    CharacterCreationBudgetState KarmaBudget,
    IReadOnlyList<CharacterCreationMetatypeOptionProjection> Options,
    IReadOnlyList<string> SourceAnchorIds,
    string SnapshotDigest,
    CharacterCreationKarmaMetatypeDecision? Selection = null,
    CharacterCreationAttributePolicy? AttributePolicy = null,
    CharacterCreationKarmaTalentCatalog? Talents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaSkillsPolicy? SkillsPolicy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationSkillsCatalog? SkillsCatalog = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaResourcesPolicy? ResourcesPolicy = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaQualitiesCatalog? QualitiesCatalog = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationGearAuthority? GearAuthority = null);

/// <summary>
/// A freshly loaded wizard and, if previously saved, its newly evaluated review.
/// This read-only result does not grant permission to persist or finalize.
/// </summary>
public sealed record CharacterCreationKarmaMetatypeOpen(
    CharacterCreationKarmaMetatypeState State,
    CharacterCreationKarmaMetatypeQuote? Quote);

/// <summary>
/// Read-only first-step quote, not a draft, mutation command or authorization to
/// finalize a runner. A missing Talent is an incomplete foundation, never Mundane.
/// </summary>
public sealed record CharacterCreationKarmaMetatypeQuote(
    string Schema,
    CharacterCreationKarmaMetatypeBinding Binding,
    string SnapshotDigest,
    CharacterCreationMetatypeOptionProjection Metatype,
    CharacterCreationBudgetState KarmaBudget,
    bool CanSelect,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string QuoteDigest,
    CharacterCreationKarmaTalentOption? Talent = null,
    CharacterCreationKarmaAttributesQuote? Attributes = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaSkillsQuote? Skills = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaResourcesQuote? Resources = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaQualitiesQuote? Qualities = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaGearQuote? Gear = null);

public sealed record CharacterCreationKarmaMetatypeConfirmRequest(
    CharacterCreationKarmaMetatypeBinding Binding,
    string MetatypeOptionId,
    string QuoteDigest,
    Guid OperationId,
    bool ExplicitlyConfirmed,
    string? TalentOptionId = null,
    IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? AttributeAllocations = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaSkillsSelection? SkillsSelection = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? ResourceKarmaInvestment = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? QualityOptionIds = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<CharacterCreationGearSelection>? GearSelections = null);

/// <summary>Pending selection only: no character effects or finalization.</summary>
public sealed record CharacterCreationKarmaMetatypeDecision(
    string Schema,
    CharacterCreationKarmaMetatypeConfirmRequest Command,
    CharacterCreationKarmaMetatypeQuote Quote,
    long DraftRevision,
    long CommittedContentRevision,
    string DecisionDigest);

public sealed record CharacterCreationKarmaMetatypeCommit(
    CharacterCreationKarmaMetatypeDecision Decision,
    bool Replayed);
