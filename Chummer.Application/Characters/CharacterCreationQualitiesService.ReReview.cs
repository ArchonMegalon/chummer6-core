using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationQualitiesService
{
    public CharacterCreationFoundationResult<CharacterCreationQualitiesReReviewState> LoadReReview(
        CharacterCreationQualitiesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var read = _store.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesReReviewState>(
                CharacterCreationFoundationOutcomes.Missing, CharacterCreationQualitiesBlockers.RevisionConflict);
        var loaded = BuildState(workspace);
        return loaded.Value is { } state && TryPrepareReReview(workspace, state, out var review)
            ? new(CharacterCreationFoundationOutcomes.Success, review, [])
            : Blocked<CharacterCreationQualitiesReReviewState>(
                CharacterCreationFoundationOutcomes.Blocked, CharacterCreationQualitiesBlockers.DraftInvalid);
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> ConfirmReReview(
        CharacterCreationQualitiesReReviewConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Binding);
        if (!request.ExplicitlyReviewedChanges)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid, CharacterCreationQualitiesBlockers.ExplicitConfirmationRequired);
        if (!CharacterCreationQualitiesRules.IsCanonicalDigest(request.HistoricalReceiptDigest)
            || request.SelectedOptionIds is null || request.SelectedOptionIds.Any(string.IsNullOrWhiteSpace))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid, CharacterCreationQualitiesBlockers.DraftInvalid);
        var read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Missing, CharacterCreationQualitiesBlockers.RevisionConflict);
        // Reuse the original command's immutable choices on an exact retry even
        // after later edits; never reconstruct a retry from a newer draft.
        bool historyValid = CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(
            workspace, out var history, out long historyRevision);
        var ledger = history.CharacterCreationQualitiesReceipts ?? [];
        if (!historyValid || !IsValidLedger(ledger, workspace.Id, historyRevision)
            || !ledger.Any(item => CharacterCreationQualitiesRules.DigestsEqual(
                item.ReceiptDigest, request.HistoricalReceiptDigest)))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid, CharacterCreationQualitiesBlockers.ReceiptLedgerInvalid);
        return ConfirmCore(new(request.Binding, request.SelectedOptionIds, request.PreviewDigest,
            request.IdempotencyKey, request.TransactionId, true), request.HistoricalReceiptDigest);
    }

    private bool TryPrepareReReview(WorkspaceStoredDocument workspace, CharacterCreationQualitiesState state,
        out CharacterCreationQualitiesReReviewState? review)
    {
        review = null;
        var historical = state.PendingDraft;
        var attributes = state.AttributesDraft;
        var ledger = workspace.Document.AuxiliaryState.CharacterCreationQualitiesReceipts;
        if (historical is null || attributes is null || state.PrerequisiteDraft is null
            || historical.DraftRevision == long.MaxValue
            || !state.Blockers.SequenceEqual([CharacterCreationQualitiesBlockers.DraftInvalid])
            || !state.Preview.CanConfirm || state.Preview.Blockers.Count != 0
            || historical.AttributesDraftRevision <= 0
            || historical.AttributesDraftRevision >= attributes.DraftRevision
            || !CharacterCreationQualitiesRules.IsCanonicalDigest(historical.AttributesDraftDigest)
            || CharacterCreationQualitiesRules.DigestsEqual(historical.AttributesDraftDigest, attributes.DraftDigest)
            || ledger is not { Count: > 0 } || !IsValidLedger(ledger, workspace.Id, workspace.ContentRevision))
            return false;
        var last = ledger[^1];
        if (last.PreviousContentRevision != historical.BaseContentRevision
            || last.ContentRevision > attributes.BaseContentRevision
            || !Same(last.DraftDigest, historical.DraftDigest)
            || !Same(last.AuthorityDigest, historical.AuthorityDigest)
            || !Same(last.RuntimeDigest, historical.RuntimeDigest)
            || !Same(last.PreviewDigest, historical.LastPreviewDigest)
            || !Same(last.CommandDigest, historical.LastCommandDigest)
            || !Same(last.IdempotencyKeyDigest, historical.LastIdempotencyKeyDigest))
            return false;
        var currentAttributes = _attributes.Load(new(workspace.Id)).Value;
        if (currentAttributes is not { CanEdit: true, Blockers.Count: 0, PendingDraft: { } admittedAttributes }
            || !Same(admittedAttributes.DraftDigest, attributes.DraftDigest))
            return false;
        // Old attribute bytes are not retained. Verify the receipted qualities
        // against their recorded remaining budget, never promote that budget to
        // current authority. Prices, effects, prerequisites and sources must all
        // still reproduce exactly; only Attributes identity and remaining Karma
        // may differ. The current preview independently enforces today's budget.
        long previousKarmaUsed = (long)state.Binding.CreationKarmaUsedBeforeQualities
            + state.Preview.KarmaRemaining - historical.KarmaRemaining;
        if (previousKarmaUsed < 0 || previousKarmaUsed > state.Binding.CreationKarmaTotal) return false;
        var oldBinding = state.Binding with
        {
            AttributesDraftRevision = historical.AttributesDraftRevision,
            AttributesDraftDigest = historical.AttributesDraftDigest,
            CreationKarmaUsedBeforeQualities = (int)previousKarmaUsed
        };
        var oldPreview = CharacterCreationQualitiesRules.Evaluate(new(oldBinding, state.Authority,
            historical.SelectedOptionIds));
        if (!oldPreview.CanConfirm || oldPreview.Blockers.Count != 0
            || !IsValidPending(historical, workspace, oldBinding, oldPreview)) return false;
        review = new(state, last.ReceiptDigest);
        return true;
    }

    private static bool Same(string left, string right) => CharacterCreationQualitiesRules.DigestsEqual(left, right);
}
