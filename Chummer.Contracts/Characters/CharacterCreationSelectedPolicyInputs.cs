namespace Chummer.Contracts.Characters;

/// <summary>
/// Bounded original-input identity, independently reproduced from admitted full inputs
/// and from selected evidence plus validated original history. This is not the full
/// OriginalInputsDigest and does not authenticate current source or owner authority.
/// No proposed choices or evaluated outcomes participate in this identity.
/// </summary>
public sealed record CharacterCreationSelectedPolicyInputs(
    string Schema,
    CharacterCreationSelectedConsumerPolicy Policy,
    string OriginalObservationDigest,
    string InputsDigest)
{
    public const string SchemaV1 = "chummer.creation-selected-policy-inputs.v1";
    public const string SchemaV2 = "chummer.creation-selected-policy-inputs.v2";
    public const string SchemaV3 = "chummer.creation-selected-policy-inputs.v3";
    public static string ComputeDigest(CharacterCreationSelectedPolicyInputs inputs)
        => CharacterCreationFinalizationDigest.Compute(inputs with { InputsDigest = string.Empty });
}

/// <summary>
/// Replay verifies every field here against the selected consumer evidence. Full
/// OriginalInputsDigest and full Gear/Loadout CatalogDigest remain exclusively in
/// the effective policy as provenance: only full capture/live recapture verifies
/// them. Full lifestyle catalogs are retained and their digests ARE replay verified.
/// Source/authority identities below bind observations; they are not signatures.
/// </summary>
public sealed record CharacterCreationSelectedConsumerPolicy(
    CharacterCreationCompletionQualityPolicy Qualities,
    CharacterCreationKarmaContactsPolicy Contacts,
    CharacterCreationCompletionAttributeConsumerPolicy Attributes,
    CharacterCreationCompletionSkillConsumerPolicy Skills,
    CharacterCreationCompletionMagicConsumerPolicy Magic,
    CharacterCreationCompletionResourceConsumerPolicy Resources,
    CharacterCreationSelectedCatalogPolicy Gear,
    CharacterCreationCompletionLifestyleConsumerPolicy Lifestyles,
    CharacterCreationSelectedCatalogPolicy? Loadout,
    IReadOnlyList<CharacterCreationConsumerPolicyCoverage> Coverage);

/// <summary>Typed purchase policy without a claim to hash an omitted full catalog.</summary>
public sealed record CharacterCreationSelectedCatalogPolicy(
    string AuthorityDigest,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine,
    CharacterCreationLoadoutLegalityPolicy? LoadoutLegality);
