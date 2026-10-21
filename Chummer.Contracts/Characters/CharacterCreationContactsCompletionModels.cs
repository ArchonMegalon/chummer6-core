namespace Chummer.Contracts.Characters;

public static class CharacterCreationContactsCompletionSchemas
{
    public const string InputsV1 = "chummer.character_creation_contacts.completion_inputs.v1";
    public const string ProjectionV1 = "chummer.character_creation_contacts.completion_projection.v1";
    public const string SnapshotConflict = "creation-contacts-completion-snapshot-conflict";
    // Pricing failed closed. A known but illegal price retains its specific blockers.
    public const string KarmaContributionUnavailable = "creation-contacts-karma-contribution-unavailable";
}

/// <summary>Canonical arithmetic for selected paid groups. The enclosing projection
/// binds these outputs to its admitted source inputs and proposed selections; this
/// is not save authority or a replacement for final graph validation.</summary>
public sealed record CharacterCreationContactGroupKarmaEvaluation(
    int BaseGroupKarma,
    CharacterCreationQualityCostTotals BeforeGroups,
    CharacterCreationQualityCostTotals WithGroups,
    int AdditionalKarmaCost);

/// <summary>Detached original source/history observations; no proposed draft or save authority.</summary>
public sealed record CharacterCreationContactsCompletionInputs(
    string Schema,
    CharacterCreationContactBinding Binding,
    CharacterCreationQualitiesCompletionInputs Qualities,
    CharacterCreationBootstrapBinding BootstrapBinding,
    CharacterCreationKarmaContactsPolicy Policy,
    CharacterCreationKarmaCarryoverPolicy CarryoverPolicy,
    CharacterCreationContactsDraft? PendingDraft,
    IReadOnlyList<CharacterCreationContactReceiptLedgerEntry> Receipts,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Contacts,
    IReadOnlyList<CharacterContactRuleEffect> OriginalEffects,
    string SnapshotDigest)
{
    /// <summary>Actual independently resolved talent grant definitions, never synthesized selections.</summary>
    public IReadOnlyList<CharacterCreationTalentQualitySource> TalentQualitySources { get; init; } = [];
}

/// <summary>Hypothetical point budgets. A null Karma cost is unavailable, never zero.</summary>
public sealed record CharacterCreationContactsCompletionProjection(
    string Schema,
    CharacterCreationContactBinding Binding,
    string ContactsSnapshotDigest,
    CharacterCreationAttributeQualitiesPreview Upstream,
    IReadOnlyList<CharacterCreationContactProjection> Contacts,
    CharacterCreationContactBudget ContactBudget,
    CharacterCreationContactBudget HighPlacesBudget,
    int? AdditionalKarmaCost,
    IReadOnlyList<string> Blockers,
    string ProposalDigest)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationContactGroupKarmaEvaluation? GroupKarma { get; init; }
}

public static class CharacterCreationContactsCompletionRules
{
    public static string ComputeInputsDigest(CharacterCreationContactsCompletionInputs inputs)
        => CharacterCreationFinalizationDigest.Compute(inputs with { SnapshotDigest = string.Empty });

    public static string ComputeProjectionDigest(CharacterCreationContactsCompletionProjection projection)
        => CharacterCreationFinalizationDigest.Compute(projection with { ProposalDigest = string.Empty });
}
