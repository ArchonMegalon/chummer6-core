using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public static partial class CharacterCreationLifestyleReceiptLedgerIntegrity
{
    public static bool RequiresAuthorityReviewSourceAdmission(WorkspaceDocumentAuxiliaryState current, WorkspaceDocumentAuxiliaryState next)
        => next.CharacterCreationLifestyleReceipts is { Count: > 0 } receipts
            && receipts.Count > (current.CharacterCreationLifestyleReceipts?.Count ?? 0)
            && receipts[^1].Receipt.WritePlan.AuthorityTransition is not null;

    private static bool IsValidAuthorityTransitionPlan(CharacterCreationLifestyleAtomicWritePlan plan,
        CharacterCreationLifestyleReceipt receipt)
    {
        if (plan.MutationKind != CharacterCreationLifestyleMutationKinds.ReReviewAuthority)
            return plan.AuthorityTransition is null;
        if (plan.AuthorityTransition is not { } review || plan.PendingDraft is not { } next
            || plan.CompositeReviewDigest is not null || plan.LifestyleId != Guid.Empty
            || plan.Before is not null || plan.After is not null
            || review.PreviousCompiler != CharacterCreationLifestyleCompilerVersions.PreLocations1008
            || review.CurrentCompiler != CharacterCreationLifestyleCompilerVersions.Locations1008
            || !CharacterCreationLifestylesRules.IsCanonicalDigest(review.HistoricalReceiptDigest)
            || !CharacterCreationLifestylesDraftRules.IsValidShape(receipt.WorkspaceId, receipt.PreviousContentRevision,
                review.HistoricalDraft)
            || !CharacterCreationLifestylesRules.IsValidAuthority(review.CurrentAuthority)
            || !CharacterCreationLifestylesDraftRules.SameReviewUpstream(review.HistoricalDraft, next)
            || !CharacterCreationLifestylesDraftRules.SameReviewSource(review.HistoricalDraft.ProjectionAuthority,
                review.CurrentAuthority)
            || review.HistoricalDraft.SourceAuthorityDigest == next.SourceAuthorityDigest
            || next.SourceAuthorityDigest != review.CurrentAuthority.AuthorityDigest
            || !CharacterCreationLifestylesDraftRules.EqualReview(next.ProjectionAuthority,
                CharacterCreationKarmaLifestylesRules.ProjectionAuthority(review.CurrentAuthority, next.Lifestyles)))
            return false;
        return plan.UntouchedSiblingDigestBefore == EmptyDigest() && plan.NestedStateDigestBefore == EmptyDigest();
    }

    private static bool HasAuthorityReviewPredecessor(CharacterCreationLifestyleReceipt receipt,
        CharacterCreationLifestyleReceipt? predecessor)
    {
        if (receipt.WritePlan.AuthorityTransition is not { } review) return true;
        try
        {
            return predecessor is not null && predecessor.ReceiptDigest == review.HistoricalReceiptDigest
                && predecessor.ContentRevision <= receipt.PreviousContentRevision
                && CharacterCreationLifestylesDraftRules.EqualReview(predecessor.WritePlan.PendingDraft, review.HistoricalDraft)
                && predecessor.LifestyleCostAfter == receipt.LifestyleCostBefore
                && predecessor.LifestyleCostAfter + predecessor.LifestyleBudgetRemaining
                    == receipt.LifestyleCostAfter + receipt.LifestyleBudgetRemaining;
        }
        catch (OverflowException) { return false; }
    }

    private static bool HasValidAuthorityReviewTransition(CharacterCreationLifestyleReceiptLedgerEntry entry,
        WorkspaceStoredDocument workspace, WorkspaceDocument replacement)
    {
        var receipt = entry.Receipt;
        var plan = receipt.WritePlan;
        var state = workspace.Document.AuxiliaryState;
        if (!IsValidAuthorityTransitionPlan(plan, receipt)
            || state.CharacterCreationCompletionDrafts is not null
            || plan.AuthorityTransition is not { } review || plan.PendingDraft is not { } next
            || !CharacterCreationLifestylesDraftRules.EqualReview(state.CharacterCreationLifestylesDraft, review.HistoricalDraft)
            || state.CharacterCreationLifestyleReceipts is not { Count: > 0 } previous
            || !IsValidLedger(workspace.Id, workspace.ContentRevision, previous)
            || !HasAuthorityReviewPredecessor(receipt, previous[^1].Receipt)
            || !CharacterCreationLifestylesDraftRules.TryProject(workspace, review.HistoricalDraft,
                out _, out _, out var before)
            || !CharacterCreationLifestylesDraftRules.TryProject(workspace, next, out _, out _, out var after)
            || before is null || after is null || !before.IsExact || !after.IsExact
            || before.Total != after.Total || before.Overspend != 0 || after.Overspend != 0
            || before.Used != receipt.LifestyleCostBefore || after.Used != receipt.LifestyleCostAfter
            || after.Remaining != receipt.LifestyleBudgetRemaining
            || !CharacterCreationLifestylesDraftRules.EqualReview(next, replacement.AuxiliaryState.CharacterCreationLifestylesDraft))
            return false;
        return true;
    }
}
