using Chummer.Application.LifeModules;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationFoundationService
{
    public CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishPreview> PreviewFinishSelection(
        CharacterCreationLifeModuleFinishRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return EvaluateFinishSelection(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishReceipt> ConfirmFinishSelection(
        CharacterCreationLifeModuleFinishConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationLifeModuleFinishReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationFoundationBlockers.ExplicitConfirmationRequired);
        var evaluation = EvaluateFinishSelection(request.Request);
        if (evaluation.Result.Value is not { CanConfirm: true } preview || evaluation.Workspace is not { } workspace)
            return new(evaluation.Result.Outcome, null, evaluation.Result.Blockers);
        if (!DigestEquals(request.PreviewDigest, preview.PreviewDigest))
            return Blocked<CharacterCreationLifeModuleFinishReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.PreviewDigestMismatch);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationLifeModuleFinishReceipt>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationFoundationBlockers.WizardStatePersistenceAuthorityRequired);

        var current = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        var proposed = current with
        {
            DraftRevision = current.DraftRevision + 1, BaseContentRevision = workspace.ContentRevision,
            ModuleSelectionFinished = true, DraftDigest = string.Empty
        };
        proposed = proposed with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(proposed) };
        var history = workspace.Document.AuxiliaryState.LifeModuleDecisionAcceptances;
        LifeModuleDecisionAcceptance? acceptance = null;
        if (history is { Count: > 0 } || request.OriginDecisionCommand is not null || request.OriginDecisionStep is not null)
        {
            acceptance = CharacterCreationFoundationLifeModuleDecisionAuthority.CreateFinishAcceptance(
                this, workspace, proposed, preview, request.OriginDecisionCommand, request.OriginDecisionStep);
            if (acceptance is null)
                return Blocked<CharacterCreationLifeModuleFinishReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                    LifeModuleOriginDossierBlockers.DecisionStale);
            history = [.. history ?? [], acceptance];
        }
        var replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                { CharacterCreationFoundationDraft = proposed, LifeModuleDecisionAcceptances = history }
            }
        };
        var result = atomic.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(workspace.Id,
            workspace.ContentRevision, workspace.Document.AuxiliaryStateDigest, replacement);
        if (!result.Success || result.Entry is not { } entry)
            return Blocked<CharacterCreationLifeModuleFinishReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision);
        return new(CharacterCreationFoundationOutcomes.Success,
            new(preview.Request.Binding, workspace.ContentRevision, entry.ContentRevision, entry.SavedRevision,
                proposed.DraftRevision, proposed.DraftDigest, CharacterEffectsApplied: false)
            { OriginDecisionAcceptance = acceptance }, []);
    }

    private (CharacterCreationFoundationResult<CharacterCreationLifeModuleFinishPreview> Result,
        WorkspaceStoredDocument? Workspace) EvaluateFinishSelection(CharacterCreationLifeModuleFinishRequest request)
    {
        var read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return (ReadFailure<CharacterCreationLifeModuleFinishPreview>(read), null);
        var loaded = BuildJourney(workspace, request.Binding.EnabledSources, request.Binding.SourceFilterApplied);
        if (loaded.Value is not { } state)
            return (new(loaded.Outcome, null, loaded.Blockers), null);
        if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(state.Binding, request.Binding)
            || state.DraftRevision != request.DraftRevision || !DigestEquals(state.DraftDigest, request.DraftDigest))
            return (Blocked<CharacterCreationLifeModuleFinishPreview>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationFoundationBlockers.StaleWorkspaceRevision), null);
        var preview = ProjectFinishSelection(workspace, state);
        return (new(preview.CanConfirm ? CharacterCreationFoundationOutcomes.Success
            : CharacterCreationFoundationOutcomes.Blocked, preview, preview.Blockers), workspace);
    }

    internal static CharacterCreationLifeModuleFinishPreview ProjectFinishSelection(
        WorkspaceStoredDocument workspace, CharacterCreationLifeModuleJourneyState state)
    {
        var draft = workspace.Document.AuxiliaryState.CharacterCreationFoundationDraft!;
        var blockers = new List<string>();
        if (state.SelectionFinished)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleSelectionFinished);
        else if (!state.CanFinishSelection)
            blockers.Add(CharacterCreationFoundationBlockers.FinalizationRequiredStagesIncomplete);
        if (!state.Budget.IsExact || state.Budget.Remaining < 0 || state.Budget.Blockers.Count > 0)
            blockers.Add(CharacterCreationFoundationBlockers.LifeModuleBudgetAuthorityRequired);
        if (draft.DraftRevision == long.MaxValue || workspace.ContentRevision == long.MaxValue)
            blockers.Add(CharacterCreationFoundationBlockers.PendingDraftConflict);
        var anchors = draft.SourceAnchorIds.Concat(state.AdditionalModules.SelectMany(entry => entry.SourceAnchorIds))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var preview = new CharacterCreationLifeModuleFinishPreview(new(state.Binding, state.DraftRevision, state.DraftDigest),
            state.Budget, anchors, blockers, blockers.Count == 0, string.Empty);
        return preview with { PreviewDigest = Digest(preview) };
    }
}
