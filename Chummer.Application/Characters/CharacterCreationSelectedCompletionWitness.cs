using Chummer.Contracts.Characters;
using System.Text.Json.Serialization;

namespace Chummer.Application.Characters;

/// <summary>
/// Historical, selected-source evidence. The full review digests are explicitly
/// provenance/identity seeds, never hashes of this reduced representation. This
/// cannot authorize a live mutation. Original history remains in the real archive.
/// </summary>
internal sealed record CharacterCreationSelectedCompletionWitness(
    string Schema,
    string ProvenanceInputsSnapshotDigest,
    string ProvenanceReviewDigest,
    CharacterCreationSkillsCompletionInputs Skills,
    CharacterCreationSelectedQualitiesAuthority Qualities,
    CharacterCreationSelectedMagicAuthority Magic,
    CharacterCreationSelectedGearAuthority Gear,
    CharacterCreationResourcesCompletionInputs Resources,
    CharacterCreationLifestylesAuthority Lifestyles,
    CharacterCreationBootstrapBinding BootstrapBinding,
    CharacterCreationKarmaContactsPolicy ContactsPolicy,
    CharacterCreationKarmaCarryoverPolicy CarryoverPolicy,
    string ContactsSourceProvenanceSeed,
    string ProposedQualityProjectionProvenanceSeed,
    string ProposalDigest,
    string WitnessDigest)
{
    internal const string SchemaV1 = "chummer.creation-selected-completion-witness.v1";
    internal const string SchemaV2 = "chummer.creation-selected-completion-witness.v2";
    internal const string SchemaV3 = "chummer.creation-selected-completion-witness.v3";

    internal const string SchemaV4 = "chummer.creation-selected-completion-witness.v4";
    internal const string SchemaV5 = "chummer.creation-selected-completion-witness.v5";
    internal const string SchemaV6 = "chummer.creation-selected-completion-witness.v6";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPhysicalInitiativeAuthority? PhysicalInitiative886 { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuleSourceCaptureDigest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSourcebookSelectionAuthority? SourcebookSelection { get; init; }

    // The archive already retains the original XML and auxiliary state.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalCharacterExecutionSchema { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedPolicyInputs? SelectedPolicyInputs { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OriginalQualityAuthorityDigest { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationAttributePolicy? AttributePolicy { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutLegalityPolicy? LoadoutLegalityPolicy { get; init; }

    // Omission is part of the historical V1 byte contract. A V2 witness
    // explicitly carries the independently replayable selected loadout closure.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedLoadoutAuthority? Loadout { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationEffectivePolicyContext? EffectivePolicy { get; init; }

    // Null means legacy; V3 explicitly retains even the None requirement.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationPolicyRequirement? PolicyRequirement { get; init; }

    // Format admission only. Replay also binds the request digest, validates the
    // retained policy against its consumers and checks original source history.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompleteAssessment? CompleteAssessment { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedPrerequisiteEvaluation? SelectedPrerequisites { get; init; }

    internal bool HasVersionedShape() => (RuleSourceExecutionSchema == CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4
        ? PhysicalInitiative886 is not null : PhysicalInitiative886 is null)
        && (SourcebookSelection is null
        || SourcebookSelection.IsValid() && RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
            && CompleteAssessment?.Schema == CharacterCreationCompleteAssessment.SchemaV4)
        && (RuleSourceExecutionSchema is null ? RuleSourceCaptureDigest is null
            : RuleSourceExecutionSchema is (CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1
                or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV3 or CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV4)
                && CharacterCreationFinalizationDigest.IsCanonical(RuleSourceCaptureDigest)
                && OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1)
        && (OriginalCharacterExecutionSchema is null
            || OriginalCharacterExecutionSchema == CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1
                && Schema == SchemaV6 && CompleteAssessment?.Schema == CharacterCreationCompleteAssessment.SchemaV4
                && EffectivePolicy?.SettingObservations is not null)
        && (SelectedPrerequisites is null || CompleteAssessment is not null
        && SelectedPrerequisites.Schema == CharacterCreationSelectedPrerequisiteEvaluation.SchemaV1)
        && (CompleteAssessment is null
        || Schema is SchemaV4 or SchemaV5 or SchemaV6
            && CompleteAssessment.Schema is CharacterCreationCompleteAssessment.SchemaV1 or CharacterCreationCompleteAssessment.SchemaV2
                    or CharacterCreationCompleteAssessment.SchemaV3 or CharacterCreationCompleteAssessment.SchemaV4)
        && (Schema == SchemaV6 || OriginalQualityAuthorityDigest is null)
        && (Schema is SchemaV4 or SchemaV5 or SchemaV6
        || SelectedPolicyInputs is null && AttributePolicy is null && LoadoutLegalityPolicy is null) && (Schema switch
    {
        SchemaV1 => Loadout is null && EffectivePolicy is null && PolicyRequirement is null,
        SchemaV2 => Loadout is not null && EffectivePolicy is null && PolicyRequirement is null,
        SchemaV3 => EffectivePolicy?.Schema == CharacterCreationEffectivePolicyContext.SchemaV1
            && PolicyRequirement is { } requirement && Enum.IsDefined(requirement),
        SchemaV4 => EffectivePolicy?.Schema == CharacterCreationEffectivePolicyContext.SchemaV2
            && EffectivePolicy.Consumers?.Schema is CharacterCreationCompletionConsumerPolicy.SchemaV2 or CharacterCreationCompletionConsumerPolicy.SchemaV3
            && SelectedPolicyInputs?.Schema == CharacterCreationSelectedPolicyInputs.SchemaV1
            && PolicyRequirement is { } selectedRequirement && Enum.IsDefined(selectedRequirement),
        SchemaV5 => EffectivePolicy?.Schema == CharacterCreationEffectivePolicyContext.SchemaV2
            && EffectivePolicy.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV4
            && SelectedPolicyInputs?.Schema == CharacterCreationSelectedPolicyInputs.SchemaV2
            && PolicyRequirement is { } compensationRequirement && Enum.IsDefined(compensationRequirement),
        SchemaV6 => (OriginalQualityAuthorityDigest is null || CharacterCreationFinalizationDigest.IsCanonical(OriginalQualityAuthorityDigest))
            && EffectivePolicy?.Schema == CharacterCreationEffectivePolicyContext.SchemaV2
            && EffectivePolicy.Consumers?.Schema == CharacterCreationCompletionConsumerPolicy.SchemaV5
            && SelectedPolicyInputs?.Schema == CharacterCreationSelectedPolicyInputs.SchemaV3
            && Skills?.KarmaPurchasePolicy is not null
            && PolicyRequirement is { } knowledgeRequirement && Enum.IsDefined(knowledgeRequirement),
        _ => false
    });
}

/// <summary>Independently recomputed rows, not a fabricated full review or saved draft.</summary>
internal sealed record CharacterCreationSelectedCompletionEvaluation(
    string WitnessDigest,
    CharacterCreationAttributeSkillsPreview Skills,
    CharacterCreationQualitiesCompletionEvaluation OriginalQualities,
    CharacterCreationQualitiesCompletionEvaluation Qualities,
    CharacterCreationSelectedMagicReplay Magic,
    SelectedCompletionResourcesPrice Resources,
    IReadOnlyList<CharacterCreationGearLine> Gear,
    CharacterCreationGearBudget GearBudget,
    IReadOnlyList<string> GearSourceAnchorIds,
    SelectedCompletionLifestylesPrice Lifestyles,
    SelectedCompletionContactsPrice Contacts,
    CharacterCreationCompletionPoolBalances Balances,
    string ReplayDigest)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutCompletionProjection? Loadout { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompleteAssessment? CompleteAssessment { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedPrerequisiteEvaluation? SelectedPrerequisites { get; init; }
}

// Derived from an actual original document (or the admitted full observation),
// never a persisted claim about which player choices may be overwritten.
internal sealed record CharacterCreationCompletionOriginalSelections(
    CharacterCreationMagicResonanceSelections Magic,
    IReadOnlyList<string> QualityOptionIds,
    IReadOnlyList<CharacterCreationGearSelection> Gear,
    int KarmaInvestment,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Contacts,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutSelections? Loadout { get; init; }
}
