using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCreationKarmaMetatypeService
{
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen> Open(CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        CharacterCreationKarmaMetatypeBinding binding, string optionId, string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
        IReadOnlyList<string>? qualityOptionIds = null,
        IReadOnlyList<CharacterCreationGearSelection>? gearSelections = null);
    CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        CharacterCreationKarmaMetatypeConfirmRequest request);
    CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> PreviewFinalizationBudget(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal);
    CharacterCreationFoundationResult<CharacterCreationFinalizationReview> ReviewFinalization(
        CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal);
    CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ConfirmFinalization(
        CharacterCreationKarmaFinalizationConfirmRequest request);
}
