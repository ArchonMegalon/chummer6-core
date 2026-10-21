using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationCompositePurchaseEdit
{
    // Shape only; admission additionally checks the real replayed ancestor and
    // independently prices the actual Resources receipt at purchase revision.
    internal static bool IsValid(CharacterCreationCompositePurchaseBinding? binding)
        => binding is not null && CharacterCreationFinalizationDigest.IsCanonical(binding.BaselineDigest)
            && CharacterCreationFinalizationDigest.IsCanonical(binding.BaselineReviewDigest)
            && binding.TotalStartingNuyen >= 0m;

    internal static bool Matches(CharacterCreationCompletionDraftEntry entry, CharacterCreationCompositePurchaseBinding? binding)
        => IsValid(binding) && entry.Baseline.BaselineDigest == binding!.BaselineDigest
            && entry.Baseline.Review.Review.ReviewDigest == binding.BaselineReviewDigest;

    internal static bool IsHistorical(WorkspaceStoredDocument workspace, long revision,
        CharacterCreationCompositePurchaseBinding? binding, long resourcesRevision, string resourcesDigest)
        => TryFunding(workspace.Document, revision, binding, resourcesRevision, resourcesDigest, out _);

    // Enclosing history admission semantically replays this baseline first.
    // Its selected authority supplies the rate/priority; chronological ordinary
    // receipts supply investment. A later receipt cannot fund an earlier purchase.
    internal static bool TryFunding(WorkspaceDocument document, long revision,
        CharacterCreationCompositePurchaseBinding? binding, long resourcesRevision, string resourcesDigest,
        out decimal total)
        => TryFunding(document.AuxiliaryState, revision,
            CharacterCreationFinalizationDigest.ComputeUtf8(document.Content), binding, resourcesRevision, resourcesDigest, out total);

    private static bool TryFunding(WorkspaceDocumentAuxiliaryState state, long revision, string rawDigest,
        CharacterCreationCompositePurchaseBinding? binding, long resourcesRevision, string resourcesDigest, out decimal total)
    {
        total = 0m;
        var receipt = state.CharacterCreationResourcesReceipts?
            .LastOrDefault(row => row.Receipt.WorkspaceRevision <= revision)?.Receipt;
        return resourcesRevision == (receipt?.DraftRevision ?? 0)
            && resourcesDigest == (receipt?.DraftDigest ?? string.Empty)
            && TryEffectiveFunding(state, revision, rawDigest, out var entry, out total)
            && Matches(entry!, binding) && total == binding!.TotalStartingNuyen;
    }

    // Called inside semantic composite-history admission. Funding comes from the
    // replayed source/priority and the real investment at this revision, never
    // from a purchase's claimed total or a later Resources receipt.
    private static bool TryEffectiveFunding(WorkspaceDocumentAuxiliaryState state, long revision, string rawDigest,
        out CharacterCreationCompletionDraftEntry? entry, out decimal total)
    {
        entry = null; total = 0m;
        try
        {
            entry = state.CharacterCreationCompletionDrafts?
                .LastOrDefault(row => row.Receipt.ContentRevision <= revision);
            if (entry is null || rawDigest != entry.Receipt.RawCharacterXmlDigest
                || !CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var witness))
                return false;
            var resources = witness.Resources;
            var receipt = state.CharacterCreationResourcesReceipts?
                .LastOrDefault(row => row.Receipt.WorkspaceRevision <= revision)?.Receipt;
            int investment = entry.Baseline.Review.Review.Request.Selections.TotalKarmaInvestment;
            if (receipt is not null)
            {
                if (receipt.RawCharacterXmlDigest != entry.Receipt.RawCharacterXmlDigest
                    || receipt.PrerequisiteDraftDigest != resources.PrerequisiteDraft.DraftDigest
                    || receipt.AuthorityDigest != resources.Authority.AuthorityDigest
                    || receipt.SourceDigest != resources.Authority.SourceDigest
                    || receipt.RulesDigest != resources.Authority.RulesDigest
                    || receipt.RuntimeDigest != resources.Authority.RuntimeDigest) return false;
                var historical = CharacterCreationResourcesService.PriceSelectedCompletion(new(resources.Authority,
                    resources.PriorityOption, int.MaxValue, receipt.KarmaInvestment, true, 0m, []));
                if (historical.Value is not { Blockers.Count: 0 } priced
                    || receipt.OptionId != priced.SelectedOption.OptionId
                    || receipt.TotalStartingNuyen != priced.Budget.TotalStartingNuyen) return false;
                if (receipt.WorkspaceRevision > entry.Receipt.ContentRevision) investment = receipt.KarmaInvestment;
            }
            var result = CharacterCreationResourcesService.PriceSelectedCompletion(new(resources.Authority,
                resources.PriorityOption, int.MaxValue, investment, true, 0m, []));
            if (result.Value is not { Blockers.Count: 0 } funding) return false;
            total = funding.Budget.TotalStartingNuyen;
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or OverflowException or NullReferenceException) { return false; }
    }

    // Receipt summaries do not contain enough evidence to replay an older Gear
    // basket. New composite receipts retain only the selected source rows/lines;
    // the typed store fence independently captures those same rows from live sources.
    private static bool TryGearCost(WorkspaceDocumentAuxiliaryState state, CharacterCreationGearReceipt receipt,
        out decimal cost)
    {
        cost = 0m;
        if (receipt.CompositeAuthority is not { } authority || receipt.CompositeLines is not { } saved
            || authority.ProvenanceAuthorityDigest != receipt.AuthorityDigest
            || authority.SourceDigest != receipt.SourceDigest || authority.RulesDigest != receipt.RulesDigest
            || authority.RuntimeDigest != receipt.RuntimeDigest
            || !TryFunding(state, receipt.PreviousWorkspaceRevision, receipt.RawCharacterXmlDigest,
                receipt.CompositePurchase, receipt.ResourcesDraftRevision, receipt.ResourcesDraftDigest, out decimal total)
            || !CharacterCreationGearRules.TryProjectSelectedBasket(saved.Select(row =>
                new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray(), authority, total,
                out var lines, out var budget, out _)
            || !lines.Select(line => line.OptionId).SequenceEqual(authority.ProposedOptionIds, StringComparer.Ordinal)
            || !CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(lines, saved)
            // Projected lines copy the selected option's economics and source node.
            // The existing finalization projector independently parses that node,
            // checks its supported fixed price/package and identity against the line,
            // and checks the quantity-derived total. Rehashing scalars cannot satisfy it.
            || lines.Any(line => !CharacterCreationLegacySourceProjector.TryBuildGear(line, receipt.DraftDigest, out _))
            || receipt.LineCount != lines.Length || receipt.BasketCost != budget.BasketCost
            || receipt.RemainingNuyen != budget.RemainingNuyen) return false;
        cost = budget.BasketCost;
        return true;
    }

    private static bool TryLoadoutCost(WorkspaceDocumentAuxiliaryState state, CharacterCreationLoadoutDraft draft,
        out decimal cost)
    {
        cost = 0m;
        return TryFunding(state, draft.BaseContentRevision, draft.RawCharacterXmlDigest, draft.CompositePurchase,
                draft.ResourcesDraftRevision, draft.ResourcesDraftDigest, out decimal total)
            && CharacterCreationLoadoutRules.TryProjectSelectedBasket(draft.Selections, draft.Authority,
                out var lines, out cost, out _)
            && cost == draft.PurchaseCost && cost <= total
            && CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(lines, draft.Lines);
    }

    internal static bool TryLifestyleFunds(WorkspaceDocumentAuxiliaryState state, CharacterCreationLifestylesDraft draft,
        out decimal total)
    {
        total = 0m;
        try
        {
            long revision = draft.BaseContentRevision;
            if (!TryEffectiveFunding(state, revision, draft.InputAuthority.RawCharacterXmlDigest,
                    out var entry, out decimal funding)) return false;
            var resources = state.CharacterCreationResourcesReceipts?
                .LastOrDefault(row => row.Receipt.WorkspaceRevision <= revision)?.Receipt;
            var gear = state.CharacterCreationGearReceipts?
                .LastOrDefault(row => row.Receipt.WorkspaceRevision <= revision)?.Receipt;
            var loadout = state.CharacterCreationLoadoutReceipts?
                .LastOrDefault(row => row.ContentRevision <= revision);
            if (draft.ResourcesDraftDigest != (resources?.DraftDigest ?? entry!.Baseline.BaselineDigest)
                || draft.GearDraftDigest != (gear?.DraftDigest ?? entry!.Baseline.BaselineDigest)) return false;
            // These initial costs were compared with semantic ApplyDraft replay by
            // the enclosing admission. Only a subsequent real purchase replaces them.
            var balances = entry!.Baseline.Review.Review.Balances;
            if (balances.GearCost is not decimal gearCost) return false;
            decimal loadoutCost = balances.LoadoutCost ?? 0m;
            if (gear is not null && gear.WorkspaceRevision > entry.Receipt.ContentRevision
                && !TryGearCost(state, gear, out gearCost)) return false;
            if (loadout is not null && loadout.ContentRevision > entry.Receipt.ContentRevision
                && !TryLoadoutCost(state, loadout.Draft, out loadoutCost)) return false;
            total = checked(funding - gearCost - loadoutCost);
            return total >= 0m;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or OverflowException or NullReferenceException) { return false; }
    }

    internal static bool HasValidHistory(WorkspaceDocumentAuxiliaryState state)
    {
        try
        {
            foreach (var row in state.CharacterCreationGearReceipts ?? [])
            {
                if (row?.Receipt is not { } receipt) return false;
                bool composite = state.CharacterCreationCompletionDrafts?.Any(entry =>
                    entry.Receipt.ContentRevision <= receipt.PreviousWorkspaceRevision) == true;
                if (composite ? !TryGearCost(state, receipt, out _)
                    : receipt.CompositePurchase is not null || receipt.CompositeAuthority is not null || receipt.CompositeLines is not null)
                    return false;
            }
            foreach (var receipt in state.CharacterCreationLoadoutReceipts ?? [])
            {
                if (receipt?.Draft is not { } draft) return false;
                bool composite = state.CharacterCreationCompletionDrafts?.Any(entry =>
                    entry.Receipt.ContentRevision <= draft.BaseContentRevision) == true;
                if (composite ? !TryLoadoutCost(state, draft, out _) : draft.CompositePurchase is not null) return false;
            }
            foreach (var row in state.CharacterCreationLifestyleReceipts ?? [])
            {
                if (row?.Receipt is not { } receipt) return false;
                bool composite = state.CharacterCreationCompletionDrafts?.Any(entry =>
                    entry.Receipt.ContentRevision <= receipt.PreviousContentRevision) == true;
                if (!composite)
                {
                    if (receipt.WritePlan.CompositeReviewDigest is not null) return false;
                    continue; // Actual ordinary history predating every ApplyDraft.
                }
                if (!CharacterCreationFinalizationDigest.IsCanonical(receipt.WritePlan.CompositeReviewDigest)
                    || receipt.WritePlan.PendingDraft is not { } draft
                    || draft.BaseContentRevision != receipt.PreviousContentRevision
                    || !TryLifestyleFunds(state, draft, out decimal total)) return false;
                decimal used = 0m;
                foreach (var selection in draft.Lifestyles)
                {
                    if (!CharacterCreationLifestylesRules.TryProject(selection, draft.ProjectionAuthority, out var priced, out _))
                        return false;
                    used = checked(used + priced.Economics.TotalCost);
                }
                if (used > total || receipt.LifestyleCostAfter != used
                    || receipt.LifestyleBudgetRemaining != total - used) return false;
            }
            return true;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException
            or OverflowException or NullReferenceException) { return false; }
    }

    // BaselineReviewDigest identifies the actual applied review, not a fabricated
    // full ordinary review. Live ordinary review binding remains on the preview.
    internal static CharacterCreationCompositePurchaseBinding Binding(WorkspaceStoredDocument workspace,
        CharacterCreationCompositeBaseline baseline, CharacterCreationCompletionReview review)
        => new(baseline.BaselineDigest,
            workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1].Baseline.Review.Review.ReviewDigest,
            review.Lifestyles!.Gear.Resources.Budget.TotalStartingNuyen);
}
