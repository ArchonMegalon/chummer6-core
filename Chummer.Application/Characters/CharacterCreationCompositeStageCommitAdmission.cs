using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

// The store invokes these concrete admissions under its workspace lease, including
// after flushing the temp file. Preview digests are comparisons, never authority.
public static class CharacterCreationCompositeStageCommitAdmission
{
    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationMagicResonancePreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        if (!preview.CanConfirm || preview.Blockers.Count != 0 || preview.CompositeReviewDigest is null
            || preview.PreviewDigest != CharacterCreationMagicResonanceDigest.Compute(preview with { PreviewDigest = string.Empty })
            || state.CharacterCreationMagicResonanceDraft is not { } draft
            || draft.LastPreviewDigest != preview.PreviewDigest || !Same(draft.Selections, preview.Selections)
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
            { AuxiliaryState = workspace.Document.AuxiliaryState with
            { CharacterCreationMagicResonanceDraft = draft,
                CharacterCreationMagicResonanceReceipts = state.CharacterCreationMagicResonanceReceipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, scope ?? resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs || loaded.Blockers.Count != 0
            || inputs.Magic.Binding != preview.Binding) return false;
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { MagicSelections = preview.Selections }, CharacterCreationCompositeEditStage.Magic);
        return reviewed.Outcome == CharacterCreationFoundationOutcomes.Success && reviewed.Blockers.Count == 0
            && reviewed.Value?.ReviewDigest == preview.CompositeReviewDigest;
    }

    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationQualitiesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        var selections = preview.Selections.Select(row => row.OptionId).ToArray();
        if (!preview.CanConfirm || preview.Blockers.Count != 0 || preview.CompositeReviewDigest is null
            || preview.PreviewDigest != CharacterCreationQualitiesRules.ComputePreviewDigest(preview)
            || state.CharacterCreationQualitiesDraft is not { } draft
            || draft.LastPreviewDigest != preview.PreviewDigest || !Same(draft.SelectedOptionIds, selections)
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
            { AuxiliaryState = workspace.Document.AuxiliaryState with
            { CharacterCreationQualitiesDraft = draft,
                CharacterCreationQualitiesReceipts = state.CharacterCreationQualitiesReceipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, scope ?? resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs || loaded.Blockers.Count != 0
            || inputs.Lifestyles.Qualities.Binding != preview.Binding) return false;
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { QualityOptionIds = selections }, CharacterCreationCompositeEditStage.Qualities);
        return reviewed.Outcome == CharacterCreationFoundationOutcomes.Success && reviewed.Blockers.Count == 0
            && reviewed.Value?.ReviewDigest == preview.CompositeReviewDigest;
    }


    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationResourcesPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        if (!preview.CanConfirm || preview.Blockers.Count != 0 || preview.CompositeReviewDigest is null
            || preview.PreviewDigest != CharacterCreationResourcesRules.ComputePreviewDigest(preview)
            || state.CharacterCreationResourcesDraft is not { } draft
            || draft.LastPreviewDigest != preview.PreviewDigest
            || !Same(draft, preview.After with
                { LastIdempotencyKeyDigest = draft.LastIdempotencyKeyDigest,
                  LastPreviewDigest = draft.LastPreviewDigest, LastCommandDigest = draft.LastCommandDigest,
                  DraftDigest = draft.DraftDigest })
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
                { AuxiliaryState = workspace.Document.AuxiliaryState with
                    { CharacterCreationResourcesDraft = draft, CharacterCreationResourcesReceipts = state.CharacterCreationResourcesReceipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, scope ?? resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs || loaded.Blockers.Count != 0
            || inputs.Lifestyles.Gear.Resources.Binding != preview.Binding) return false;
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { TotalKarmaInvestment = draft.KarmaInvestment }, CharacterCreationCompositeEditStage.Resources);
        return reviewed.Outcome == CharacterCreationFoundationOutcomes.Success && reviewed.Blockers.Count == 0
            && reviewed.Value is { Lifestyles: not null } review && review.ReviewDigest == preview.CompositeReviewDigest
            && Same(preview.SelectedOption, review.Lifestyles.Gear.Resources.SelectedOption)
            && Same(draft.Budget, review.Lifestyles.Gear.Resources.Budget)
            && draft.FinalizationContribution.PriorityRank == inputs.Lifestyles.Gear.Resources.PriorityOption.Rank
            && draft.FinalizationContribution.PrioritySourceId == inputs.Lifestyles.Gear.Resources.PriorityOption.SourceId
            && draft.AuthorityDigest == preview.Binding.AuthorityDigest
            && draft.SourceDigest == preview.Binding.SourceDigest
            && draft.RulesDigest == preview.Binding.RulesDigest
            && draft.RuntimeDigest == preview.Binding.RuntimeDigest;
    }


    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationGearPreview preview, ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        if (!preview.CanConfirm || preview.Blockers.Count != 0 || preview.CompositeReviewDigest is null
            || preview.PreviewDigest != CharacterCreationGearRules.ComputePreviewDigest(preview)
            || state.CharacterCreationGearDraft is not { } draft
            || draft.LastPreviewDigest != preview.PreviewDigest
            || !Same(draft, preview.After with
                { LastIdempotencyKeyDigest = draft.LastIdempotencyKeyDigest,
                  LastPreviewDigest = draft.LastPreviewDigest, LastCommandDigest = draft.LastCommandDigest,
                  DraftDigest = draft.DraftDigest })
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
                { AuxiliaryState = workspace.Document.AuxiliaryState with
                    { CharacterCreationGearDraft = draft, CharacterCreationGearReceipts = state.CharacterCreationGearReceipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        var loaded = OwnerBoundCharacterCreationCompletionReader.LoadForCompositeEdit(workspace, scope ?? resolver, queries);
        if (loaded.Value is not { Skills.CompositeBaseline: { } baseline } inputs || loaded.Blockers.Count != 0
            || inputs.Lifestyles.Gear.State.Binding != preview.Binding
            || !CharacterCreationGearRules.TryCaptureSelectedAuthority(inputs.Lifestyles.Gear.State.Authority,
                [], draft.Lines.Select(row => row.OptionId).ToArray(), out var authority)
            || !Same(authority, preview.CompositeAuthority)
            || state.CharacterCreationGearReceipts is not { Count: > 0 } receipts
            || !Same(receipts[^1].Receipt.CompositeAuthority, authority)
            || !Same(receipts[^1].Receipt.CompositeLines, draft.Lines)) return false;
        var reviewed = OwnerBoundCharacterCreationCompletionReader.ReviewCompositeEdit(workspace, inputs,
            baseline.Selections with { Gear = draft.Lines.Select(row => new CharacterCreationGearSelection(row.OptionId, row.Quantity)).ToArray() }, CharacterCreationCompositeEditStage.Gear);
        return reviewed.Outcome == CharacterCreationFoundationOutcomes.Success && reviewed.Blockers.Count == 0
            && reviewed.Value is { Lifestyles: not null } review && review.ReviewDigest == preview.CompositeReviewDigest
            && Same(draft.Lines, review.Lifestyles.Gear.Lines) && Same(draft.Budget, review.Lifestyles.Gear.Budget)
            && Same(draft.CompositePurchase, CharacterCreationCompositePurchaseEdit.Binding(workspace, baseline, review))
            && draft.AuthorityDigest == preview.Binding.AuthorityDigest
            && draft.SourceDigest == preview.Binding.SourceDigest
            && draft.RulesDigest == preview.Binding.RulesDigest
            && draft.RuntimeDigest == preview.Binding.RuntimeDigest;
    }

    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationContactPreview preview, IWorkspaceStore store,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        var receipts = state.CharacterCreationContactReceipts;
        if (store is not ICharacterCreationCompositeStageAtomicCommitCapability
                { SupportsCompositeStageAtomicCommit: true }
            || !preview.CanConfirm || preview.Blockers.Count != 0
            || !CharacterCreationFinalizationDigest.IsCanonical(preview.WritePlan.CompositeReviewDigest)
            || workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts is not { Count: > 0 }
            || state.CharacterCreationContactsDraft is not { } draft
            || !Same(draft, preview.WritePlan.PendingDraft)
            || receipts is not { Count: > 0 }
            || !Same(receipts[^1].Receipt.WritePlan, preview.WritePlan)
            || !CharacterCreationContactReceiptLedgerIntegrity.IsValidAppendTransition(
                workspace.Id, workspace.ContentRevision, workspace.SavedRevision, workspace.ContentRevision + 1,
                workspace.Document.AuxiliaryState.CharacterCreationContactReceipts, receipts)
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
            { AuxiliaryState = workspace.Document.AuxiliaryState with
            { CharacterCreationContactsDraft = draft, CharacterCreationContactReceipts = receipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        if (!CharacterCreationContactReceiptLedgerIntegrity.HasValidContentTransition(
                receipts[^1], workspace.Document, replacement)) return false;
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        // The real store remains the capability owner; this service performs no read or write.
        return new CharacterCreationContactsService(store, scope ?? resolver, queries)
            .AdmitsCompositeCommit(workspace, preview);
    }

    public static bool IsCurrent(WorkspaceStoredDocument workspace, WorkspaceDocument replacement,
        CharacterCreationLifestylePreview preview, IWorkspaceStore store,
        ICharacterSourceDataResolver resolver, ICharacterFileQueries queries)
    {
        var state = replacement.AuxiliaryState;
        var receipts = state.CharacterCreationLifestyleReceipts;
        if (store is not ICharacterCreationCompositeStageAtomicCommitCapability
                { SupportsCompositeStageAtomicCommit: true }
            || !preview.CanConfirm || preview.Blockers.Count != 0
            || !CharacterCreationFinalizationDigest.IsCanonical(preview.WritePlan.CompositeReviewDigest)
            || workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts is not { Count: > 0 }
            || state.CharacterCreationLifestylesDraft is not { } draft
            || !Same(draft, preview.WritePlan.PendingDraft)
            || receipts is not { Count: > 0 }
            || !Same(receipts[^1].Receipt.WritePlan, preview.WritePlan)
            || !CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidAppendTransition(
                workspace.Id, workspace.ContentRevision, workspace.SavedRevision, workspace.ContentRevision + 1,
                workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts, receipts)
            || !Same(replacement, workspace.Document with { State = workspace.Document.State with
            { AuxiliaryState = workspace.Document.AuxiliaryState with
            { CharacterCreationLifestylesDraft = draft, CharacterCreationLifestyleReceipts = receipts } } }))
            return false;
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        if (!CharacterCreationLifestyleReceiptLedgerIntegrity.HasValidContentTransition(
                receipts[^1], workspace.Document, replacement)) return false;
        using ICharacterSourceDataResolverOperationScope? scope =
            (resolver as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
        // The real store remains the capability owner; this service performs no read or write.
        return new CharacterCreationLifestylesService(store, scope ?? resolver, queries)
            .AdmitsCompositeCommit(workspace, preview);
    }

    // Generic persistence cannot change composite-owned stage state without the
    // corresponding typed live-source admission. Keep canonical comparison here
    // in Application; Infrastructure must not access the internal ledger helper.
    public static bool RequiresTypedSourceAdmission(WorkspaceDocumentAuxiliaryState current,
        WorkspaceDocumentAuxiliaryState replacement)
        => current.CharacterCreationCompletionDrafts is not null
        && (!Same(
                current.CharacterCreationMagicResonanceDraft,
                replacement.CharacterCreationMagicResonanceDraft)
            || !Same(
                current.CharacterCreationMagicResonanceReceipts,
                replacement.CharacterCreationMagicResonanceReceipts)
            || !Same(
                current.CharacterCreationQualitiesDraft,
                replacement.CharacterCreationQualitiesDraft)
            || !Same(
                current.CharacterCreationQualitiesReceipts,
                replacement.CharacterCreationQualitiesReceipts)
            || !Same(
                current.CharacterCreationResourcesDraft,
                replacement.CharacterCreationResourcesDraft)
            || !Same(
                current.CharacterCreationResourcesReceipts,
                replacement.CharacterCreationResourcesReceipts)
            || !Same(
                current.CharacterCreationGearDraft,
                replacement.CharacterCreationGearDraft)
            || !Same(
                current.CharacterCreationGearReceipts,
                replacement.CharacterCreationGearReceipts)
            || !Same(
                current.CharacterCreationLoadoutDraft,
                replacement.CharacterCreationLoadoutDraft)
            || !Same(
                current.CharacterCreationLoadoutReceipts,
                replacement.CharacterCreationLoadoutReceipts)
            || !Same(
                current.CharacterCreationContactsDraft,
                replacement.CharacterCreationContactsDraft)
            || !Same(
                current.CharacterCreationContactReceipts,
                replacement.CharacterCreationContactReceipts)
            || !Same(
                current.CharacterCreationLifestylesDraft,
                replacement.CharacterCreationLifestylesDraft)
            || !Same(
                current.CharacterCreationLifestyleReceipts,
                replacement.CharacterCreationLifestyleReceipts));

    private static bool Same<T>(T left, T right)
        => CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(left, right);
}
