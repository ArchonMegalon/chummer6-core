using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Keep the original owner stamp across display, review and confirmation.</summary>
public interface IOwnerBoundCharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false, bool includeLifestyles = false);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen> Open(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false, bool includeLifestyles = false);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
        string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
        IReadOnlyList<string>? qualityOptionIds = null,
        IReadOnlyList<CharacterCreationGearSelection>? gearSelections = null,
        IReadOnlyList<CharacterCreationKarmaContactSelection>? contactSelections = null,
        IReadOnlyList<CharacterCreationLifestyleConfiguration>? lifestyleSelections = null, Guid? startingLifestyleId = null);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeConfirmRequest request);
    CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> PreviewFinalizationBudget(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal);
    CharacterCreationFoundationResult<CharacterCreationStartingNuyenSource> LoadFinalizationStartingCash(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest);
    CharacterCreationFoundationResult<CharacterCreationFinalizationReview> ReviewFinalization(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal,
        string? startingCashAuthorityDigest = null);
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ConfirmFinalization(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaFinalizationConfirmRequest request);
}
