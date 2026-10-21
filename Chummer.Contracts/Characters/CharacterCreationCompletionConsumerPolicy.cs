using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Admitted means the named policy was observed in the consumer's original inputs.
/// It never means all selected effects were evaluated or that Finish Character is supported.</summary>
public enum CharacterCreationConsumerPolicyDisposition { Admitted, Unsupported, Missing, NotApplicable }

public enum CharacterCreationConsumerPolicyOperation
{
    AttributeSpending, SkillPurchases, QualityCostsAndLimits, ContactPointAllowance,
    ContactGroupKarma, MysticAdeptExchange, ResourcesAndAvailability, Gear,
    LifestylesAndFreeGrid, Loadout, SelectedEffects, Amendments, Errata,
    CharacterMechanics, Campaign, UnclassifiedSettings,
    // Append only: the original numeric wire values remain stable.
    MagicAndResonance, AttributeHouseRules, SkillPointRules, SkillKarmaModifiers,
    MysticAdeptSeparateMagic,
    // General selected-quality cost effects; racial costs have separate coverage below.
    LifestyleCostEffects, LifestyleGrantEffects,
    SkillGroupKarmaCompensation, LoadoutLegalityExclusions, LoadoutLegalityMultiplierPricing,
    // Unconditional global Metatype, Metavariant and Heritage cost effects only.
    LifestyleMetatypeCost,
    KnowledgeCategoryPricing
}

public enum CharacterCreationConsumerPolicyReason
{
    MatchedConsumerInputs, PolicyProjectionUnavailable, GroupContactKarmaUnavailable,
    SelectedEffectsRequireEvaluation, AmendValidationUnavailable, ErrataAuthorityUnavailable,
    MechanicsCoverageUnavailable, CampaignApplicabilityUnavailable, UnclassifiedLegacySetting,
    ConsumerOperationUnavailable, OriginalInputUnavailable, SourcePolicyUnavailable,
    CareerPurchaseOnly
}

public sealed record CharacterCreationConsumerPolicyCoverage(
    CharacterCreationConsumerPolicyOperation Operation,
    CharacterCreationConsumerPolicyDisposition Disposition,
    CharacterCreationConsumerPolicyReason Reason);

/// <summary>Policy projection of the existing Qualities authority. Catalog options and evaluated
/// purchases stay in their original inputs/results; arithmetic uses the canonical cost policy.</summary>
public sealed record CharacterCreationCompletionQualityPolicy(
    string AuthorityDigest, string SourceDigest, string ProfileDigest, string GmPolicyDigest, string RuntimeDigest,
    int QualityKarmaLimit, bool MayExceedPositiveQualityLimit, bool MayExceedNegativeQualityLimit,
    int MetagenicLimit, CharacterCreationQualityCostPolicy CostPolicy);

/// <summary>
/// A versioned projection of policies actually supplied to completion consumers. OriginalInputsDigest
/// binds the entire original observation without EffectivePolicy, preventing a recursive digest.
/// Contacts retains the canonical policy, including its group rate; coverage explicitly records
/// the presence of source-priced group inputs, separately from selected legality. No catalog, selection or save authority
/// is synthesized here. Missing projections remain gaps even if another editor supports them.
/// </summary>
public sealed record CharacterCreationCompletionConsumerPolicy(
    string Schema, string OriginalInputsDigest,
    CharacterCreationCompletionQualityPolicy Qualities,
    CharacterCreationKarmaContactsPolicy Contacts,
    IReadOnlyList<CharacterCreationConsumerPolicyCoverage> Coverage)
{
    public const string SchemaV1 = "chummer.creation-completion-consumer-policy.v1";
    public const string SchemaV2 = "chummer.creation-completion-consumer-policy.v2";
    public const string SchemaV3 = "chummer.creation-completion-consumer-policy.v3";
    // Configured skill-group compensation input coverage; V1-V3 keep their original bytes.
    public const string SchemaV4 = "chummer.creation-completion-consumer-policy.v4";
    public const string SchemaV5 = "chummer.creation-completion-consumer-policy.v5";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationCompletionDomainPolicies? Domains { get; init; }
}
