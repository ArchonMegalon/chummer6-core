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

    // Omission is part of the historical V1 byte contract. A V2 witness
    // explicitly carries the independently replayable selected loadout closure.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationSelectedLoadoutAuthority? Loadout { get; init; }
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
