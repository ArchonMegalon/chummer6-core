using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class OwnerBoundCharacterCreationKarmaMetatypeService(
    IWorkspaceStore store, IOwnerContextAccessor ownerContext, ICharacterSourceDataResolver sourceResolver)
    : IOwnerBoundCharacterCreationKarmaMetatypeService
{
    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeState> Load(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false)
        => Invoke(expectedOwner, id, service => service.Load(id, includeSkills, includeQualities, includeGear));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeOpen> Open(
        OwnerContextStamp expectedOwner, CharacterWorkspaceId id, bool includeSkills = false, bool includeQualities = false, bool includeGear = false)
        => Invoke(expectedOwner, id, service => service.Open(id, includeSkills, includeQualities, includeGear));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeQuote> Preview(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string optionId,
        string? talentOptionId = null,
        IReadOnlyList<CharacterCreationKarmaAttributeAllocation>? attributeAllocations = null,
        CharacterCreationKarmaSkillsSelection? skillsSelection = null, decimal? resourceKarmaInvestment = null,
        IReadOnlyList<string>? qualityOptionIds = null,
        IReadOnlyList<CharacterCreationGearSelection>? gearSelections = null)
        => Invoke(expectedOwner, binding.WorkspaceId, service => service.Preview(binding, optionId, talentOptionId,
            attributeAllocations, skillsSelection, resourceKarmaInvestment, qualityOptionIds, gearSelections));

    public CharacterCreationFoundationResult<CharacterCreationKarmaMetatypeCommit> Confirm(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeConfirmRequest request)
        => Invoke(expectedOwner, request.Binding.WorkspaceId, service => service.Confirm(request));

    public CharacterCreationFoundationResult<CharacterCreationKarmaFinalizationBudgetQuote> PreviewFinalizationBudget(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal)
        => Invoke(expectedOwner, binding.WorkspaceId, service => service.PreviewFinalizationBudget(binding, foundationQuoteDigest, diceTotal));

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReview> ReviewFinalization(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaMetatypeBinding binding, string foundationQuoteDigest, int diceTotal)
        => Invoke(expectedOwner, binding.WorkspaceId, service => service.ReviewFinalization(binding, foundationQuoteDigest, diceTotal));

    public CharacterCreationFoundationResult<CharacterCreationFinalizationReceipt> ConfirmFinalization(
        OwnerContextStamp expectedOwner, CharacterCreationKarmaFinalizationConfirmRequest request)
        => Invoke(expectedOwner, request.Confirmation.Binding.WorkspaceId, service => service.ConfirmFinalization(request));

    private CharacterCreationFoundationResult<T> Invoke<T>(OwnerContextStamp expectedOwner,
        CharacterWorkspaceId id, Func<CharacterCreationKarmaMetatypeService, CharacterCreationFoundationResult<T>> action)
        where T : class
    {
        if ((expectedOwner.Owner.UsesLocalSingleUserValue && !expectedOwner.Owner.IsLocalSingleUser)
            || !OwnerContextAdmission.TryAcquire(ownerContext, expectedOwner, out var lease))
            return new(CharacterCreationFoundationOutcomes.Blocked, null, [CharacterCreationKarmaMetatypeBlockers.WorkspaceUnavailable]);
        using (lease)
            return action(new(new OwnerBoundCreationWorkspaceStore(store, lease, expectedOwner, id), sourceResolver));
    }
}
