using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

// Internal arithmetic inputs, not persisted authority, saved state or reusable
// admission tokens. Ordinary callers validate their complete live envelopes;
// historical replay must independently validate selected witnesses and original
// history before calling these same tails. No method here authorizes a write.
internal sealed record SelectedCompletionResourcesPricingInput(
    CharacterCreationResourcesAuthority Authority,
    CharacterCreationResourcePriorityOption PriorityOption,
    int AvailableKarma,
    int TotalKarmaInvestment,
    bool PurchaseAuthorityExact,
    decimal FullPurchaseCost,
    IReadOnlyList<string> UpstreamBlockers);

internal sealed record SelectedCompletionResourcesPrice(
    IReadOnlyList<CharacterCreationResourceAllocationOption> Options,
    CharacterCreationResourceAllocationOption SelectedOption,
    CharacterCreationResourcesBudget Budget,
    int RemainingCreationKarma,
    IReadOnlyList<string> Blockers);

internal sealed record SelectedCompletionLifestylesPricingInput(
    CharacterCreationLifestylesAuthority Authority,
    CharacterCreationPrerequisiteDraft Prerequisite,
    IReadOnlyList<CharacterCreationTalentQualitySource> TalentQualitySources,
    IReadOnlyList<CharacterCreationQualitySelection> QualitySelections,
    string QualityProjectionProvenanceSeed,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Lifestyles,
    Guid? StartingLifestyleId,
    decimal RemainingNuyenAfterGear,
    IReadOnlyList<string> GearSourceAnchorIds,
    IReadOnlyList<string> GearBlockersBeforeReplacement);

internal sealed record SelectedCompletionLifestylesPrice(
    CharacterCreationLifestylesAuthority Authority,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lifestyles,
    Guid? StartingLifestyleId,
    decimal FullLifestyleCost,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers);

internal sealed record SelectedCompletionContactsPricingInput(
    CharacterCreationKarmaContactsPolicy Policy,
    CharacterCreationPrerequisiteDraft Prerequisite,
    IReadOnlyList<CharacterCreationTalentQualitySource> TalentQualitySources,
    IReadOnlyList<CharacterContactRuleEffect> OriginalEffects,
    IReadOnlyList<CharacterCreationKarmaContactSelection> OriginalContacts,
    IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
    IReadOnlyList<CharacterCreationQualitySelection> QualitySelections,
    IReadOnlyList<CharacterCreationKarmaContactSelection> Contacts,
    string SourceProvenanceSeed,
    IReadOnlyList<string> UpstreamBlockers,
    CharacterCreationQualityCostPolicy QualityCostPolicy,
    int QualityKarmaLimit,
    bool MayExceedPositiveQualityLimit);

internal sealed record SelectedCompletionContactsPrice(
    IReadOnlyList<CharacterCreationContactProjection> Contacts,
    CharacterCreationContactBudget ContactBudget,
    CharacterCreationContactBudget HighPlacesBudget,
    int? AdditionalKarmaCost,
    IReadOnlyList<string> Blockers)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationContactGroupKarmaEvaluation? GroupKarma { get; init; }
}
