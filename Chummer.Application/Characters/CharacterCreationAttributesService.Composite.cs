using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationAttributesService
{
    // A view only. This draft is never installed in auxiliary state by Load and
    // never gets a page receipt. Confirm records the actual subsequent edit.
    internal static CharacterCreationAttributesState? ProjectCompositeAttributes(
        CharacterCreationAttributesState state, CharacterCreationCompositeBaseline composite,
        CharacterCreationAttributesDraft? ordinary)
    {
        if (state.PrerequisiteDraft is null || state.Blockers.Any(blocker =>
                blocker != CharacterCreationAttributesBlockers.PersistenceAuthorityRequired)) return null;
        var blockers = new List<string>();
        var evaluated = EvaluateAllocations(state.PrerequisiteDraft, state.MaxNumberMaxAttributesCreate,
            state.KarmaAttribute, composite.Selections.AttributeAllocations, blockers, state.HouseRules,
            state.SeparateMagicPolicy);
        if (blockers.Count != 0) return null;
        if (ordinary is not null && ordinary.BaseContentRevision >= composite.ContentRevision
            && (!CharacterCreationAttributesDraftIntegrity.IsStructurallyValidPending(ordinary,
                    state.Binding.WorkspaceId, state.Binding.ContentRevision, state.Binding.RawCharacterXmlDigest,
                    state.PrerequisiteDraft)
                || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(ordinary.Attributes, evaluated.Attributes)
                || ordinary.NormalPointTotal != evaluated.NormalBudget.Total
                || ordinary.NormalPointUsed != evaluated.NormalBudget.Used
                || ordinary.SpecialPointTotal != evaluated.SpecialBudget.Total
                || ordinary.SpecialPointUsed != evaluated.SpecialBudget.Used
                || ordinary.CreationKarmaTotal != evaluated.KarmaBudget.Total
                || ordinary.CreationKarmaUsed != evaluated.KarmaBudget.Used))
            return null;
        CharacterCreationAttributesDraft? pending = ordinary;
        if (ordinary is null || ordinary.BaseContentRevision < composite.ContentRevision
            && !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                ordinary.Allocations, composite.Selections.AttributeAllocations)
            || composite.Selections.PlayerChosenAttributeIds is not null
                && !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    ordinary.PlayerChosenAttributeIds, composite.Selections.PlayerChosenAttributeIds))
        {
            pending = BuildDraft(state with { PendingDraft = ordinary }, evaluated, null) with
            {
                BaseContentRevision = composite.ContentRevision - 1,
                Allocations = composite.Selections.AttributeAllocations.ToArray(),
                PlayerChosenAttributeIds = composite.Selections.PlayerChosenAttributeIds,
                DraftDigest = string.Empty
            };
            pending = pending with { DraftDigest = CharacterCreationAttributesDraftIntegrity.ComputeDigest(pending) };
        }
        else if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                     ordinary.Allocations, composite.Selections.AttributeAllocations)) return null;
        return state with
        {
            CompositeBaseline = composite, PendingDraft = pending,
            Attributes = evaluated.Attributes, NormalPointBudget = evaluated.NormalBudget,
            SpecialPointBudget = evaluated.SpecialBudget, CreationKarmaBudget = evaluated.KarmaBudget
        };
    }
}
