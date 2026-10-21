namespace Chummer.Contracts.Characters;

/// <summary>Typed views of the original consumers, not evaluated selections or new authority.
/// Catalogs, saved choices, grants and budgets remain in the digest-bound original inputs.</summary>
public sealed record CharacterCreationCompletionDomainPolicies(
    CharacterCreationCompletionAttributeConsumerPolicy Attributes,
    CharacterCreationCompletionSkillConsumerPolicy Skills,
    CharacterCreationCompletionMagicConsumerPolicy Magic,
    CharacterCreationCompletionResourceConsumerPolicy Resources,
    CharacterCreationCatalogPurchasePolicy Gear,
    CharacterCreationCompletionLifestyleConsumerPolicy Lifestyles,
    CharacterCreationCatalogPurchasePolicy? Loadout);

public sealed record CharacterCreationCompletionAttributeConsumerPolicy(
    CharacterCreationAttributePolicy? Spending,
    int EffectiveKarmaAttribute,
    int EffectiveMaximumAttributesAtCap,
    string PrerequisiteAuthorityDigest,
    IReadOnlyList<CharacterCreationAttributeProjection> OriginalAttributes);

public sealed record CharacterCreationCompletionSkillConsumerPolicy(
    string AuthorityDigest, string ProfileInputsDigest, string EffectiveSkillsInputsDigest, string RuntimeDigest,
    string KnowledgePointsExpression, int MaxActiveSkillRatingCreate, int MaxKnowledgeSkillRatingCreate,
    int MaxSkillGroupRatingCreate, int BaseNativeLanguageLimit, bool UsePointsOnBrokenGroups,
    bool StrictSkillGroupsInCreateMode, bool SpecializationsBreakSkillGroups,
    CharacterCreationKarmaSkillsPolicy? Purchases, CharacterCreationTalentSkillAccess? TalentAccess,
    IReadOnlyList<CharacterCreationKnowledgePointContribution> KnowledgePointContributions,
    CharacterCreationMovementCapability MovementCapability, int SelectedActiveSkillPoints, int SelectedSkillGroupPoints);

public sealed record CharacterCreationCompletionMagicConsumerPolicy(
    string AuthorityDigest, string PrerequisiteAuthorityDigest, string SourceInputsDigest,
    string CustomDataInputsDigest, string GmPolicyDigest, string RuntimeDigest,
    CharacterCreationMagicResonanceTalentIdentity TalentIdentity, string SelectedTalentDigest,
    int GrantedMagic, int GrantedResonance, int GrantedDepth, int SourceSpellBudget,
    int SourceComplexFormBudget, decimal SourceAdeptPowerPointBudget,
    bool RequiresTradition, bool RequiresStream, bool AllowsAdeptPowers, bool AllowsSpells, bool AllowsComplexForms,
    CharacterCreationMysticAdeptPowerPointPolicy? MysticAdeptExchange);

public sealed record CharacterCreationCompletionResourceConsumerPolicy(
    string AuthorityDigest, string SourceDigest, string ProfileDigest, string RulesDigest, string RuntimeDigest,
    decimal KarmaToNuyenRate, int MaximumKarmaInvestment, decimal NuyenCarryover,
    int MaximumAvailability, bool UnrestrictedNuyen, CharacterCreationResourcePriorityOption PriorityOption,
    bool PurchaseAuthorityExact, decimal KnownPurchaseCost);

/// <summary>Reusable policy shared by the Gear and typed Loadout consumers. These limits do
/// not assert that a catalog option is selectable, exactly priced or mechanically supported.
/// CatalogDigest includes the actual option flags, expressions/source nodes and blockers.</summary>
public sealed record CharacterCreationCatalogPurchasePolicy(
    string AuthorityDigest, string SourceDigest, string ProfileDigest, string RulesDigest, string RuntimeDigest,
    int MaximumAvailability, int MaximumBasketLines, int MaximumQuantityPerLine, string CatalogDigest)
{
    // Gear does not consume this policy. Missing Loadout policy stays explicit.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationLoadoutLegalityPolicy? LoadoutLegality { get; init; }
}

public sealed record CharacterCreationCompletionLifestyleConsumerPolicy(
    string AuthorityDigest, string SourceDigest, string ProfileDigest, string GmPolicyDigest, string RuntimeDigest,
    int TrustFundLevel, bool FreeGridsEnabled, decimal MetatypeCostPercent,
    string LifestyleCatalogDigest, string QualityCatalogDigest);
