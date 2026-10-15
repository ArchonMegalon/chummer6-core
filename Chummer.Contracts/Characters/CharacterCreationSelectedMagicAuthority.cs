namespace Chummer.Contracts.Characters;

/// <summary>Selected historical replay payload, NOT a truncated full authority.
/// ProvenanceAuthorityDigest names the admitted original complete catalogue;
/// PayloadDigest checks this selected representation. Neither digest authenticates
/// current sources or authorizes a save. Archive admission binds actual history.</summary>
public sealed record CharacterCreationSelectedMagicAuthority(
    string Schema,
    string ProvenanceAuthorityDigest,
    string OriginalInputsSnapshotDigest,
    string SettingsProfileId,
    string PrerequisiteAuthorityDigest,
    string SourceInputsDigest,
    string CustomDataInputsDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    long PrerequisiteDraftRevision,
    string PrerequisiteDraftDigest,
    string OriginalAttributesDigest,
    bool OriginalHadSavedMagic,
    CharacterCreationMagicResonanceTalentOption Talent,
    CharacterCreationMagicResonanceMetatypeCapability Metatype,
    CharacterCreationMysticAdeptPowerPointPolicy? MysticAdeptPowerPointPolicy,
    CharacterCreationMagicResonanceSelections OriginalSelections,
    CharacterCreationMagicResonanceSelections ProposedSelections,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Traditions,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Streams,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> AdeptPowers,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> Spells,
    IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> ComplexForms,
    IReadOnlyList<string> SourceAnchorIds,
    string PayloadDigest);

public sealed record CharacterCreationSelectedMagicEvaluation(
    CharacterCreationMagicResonanceSelections Selections,
    CharacterCreationMagicResonanceEffectiveAttributes EffectiveAttributes,
    CharacterCreationMagicResonanceBudgetState TraditionBudget,
    CharacterCreationMagicResonanceBudgetState StreamBudget,
    CharacterCreationMagicResonanceBudgetState AdeptPowerPointBudget,
    CharacterCreationMagicResonanceBudgetState SpellBudget,
    CharacterCreationMagicResonanceBudgetState ComplexFormBudget,
    CharacterCreationMysticAdeptPowerPointAllocation? MysticAdeptPowerPoints,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

/// <summary>Repricing of two separate attribute/choice states. An incomplete
/// unsaved original can be legitimate; it is never evaluated using future values.
/// This result does not grant confirmation, source admission or persistence.</summary>
public sealed record CharacterCreationSelectedMagicReplay(
    string WitnessPayloadDigest,
    string OriginalAttributesDigest,
    string ProposedAttributesDigest,
    CharacterCreationSelectedMagicEvaluation Original,
    CharacterCreationSelectedMagicEvaluation Proposed,
    string ReplayDigest);

public static class CharacterCreationSelectedMagicRules
{
    public const string Schema = "chummer.creation-selected-magic-authority.v1";
    public const string Invalid = "creation-selected-magic-invalid";
    public const string HistoryMismatch = "creation-selected-magic-history-mismatch";
    public const string ManualChoiceConflict = "creation-selected-magic-manual-choice-conflict";
    public static string ComputePayloadDigest(CharacterCreationSelectedMagicAuthority witness)
        => CharacterCreationMagicResonanceDigest.Compute(witness with { PayloadDigest = string.Empty });
    public static string ComputeAttributesDigest(IReadOnlyList<CharacterCreationAttributeProjection> attributes)
        => CharacterCreationMagicResonanceDigest.Compute(attributes);
    public static string ComputeReplayDigest(CharacterCreationSelectedMagicReplay replay)
        => CharacterCreationMagicResonanceDigest.Compute(replay with { ReplayDigest = string.Empty });
}
