using System.Text.Json;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>Manual, bounded weapon/armor purchases. XML stays unchanged until finalization.</summary>
public sealed class CharacterCreationLoadoutService(IWorkspaceStore store, ICharacterSourceDataResolver resolver)
    : ICharacterCreationLoadoutService
{
    public CharacterCreationFoundationResult<CharacterCreationLoadoutState> Prepare(CharacterWorkspaceId workspaceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = store.Get(workspaceId);
        var result = read.Success && read.Value is { } workspace ? PrepareObservation(store, workspace, resolver)
            : Reject<CharacterCreationLoadoutState>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static CharacterCreationFoundationResult<CharacterCreationLoadoutState> PrepareObservation(
        IWorkspaceStore store, WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver)
    {
        if (workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive is not null
            || workspace.Document.AuxiliaryState.CharacterCreationLoadoutReceipts?.Count >= CharacterCreationLoadoutReceiptLedgerIntegrity.MaximumEntries)
            return Reject<CharacterCreationLoadoutState>(CharacterCreationLoadoutCompletionSchemas.RequestInvalid);
        var loadout = CharacterCreationLoadoutCompletionService.LoadFromObservation(workspace, resolver, true);
        if (loadout.Value is null) return new(loadout.Outcome, null, loadout.Blockers);
        var gear = new CharacterCreationGearService(store, resolver).LoadCompletionInputsFromObservation(workspace);
        if (gear.Value is null) return new(CharacterCreationFoundationOutcomes.Blocked, null, gear.Blockers);
        if (gear.Value.Resources.PendingDraft is null)
            return Reject<CharacterCreationLoadoutState>(CharacterCreationGearBlockers.ResourcesDraftRequired);
        if (loadout.Value.Authority.SettingsProfileId != gear.Value.Resources.Authority.SettingsProfileId
            || loadout.Value.Authority.ProfileDigest != gear.Value.Resources.Authority.ProfileDigest)
            return Reject<CharacterCreationLoadoutState>(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
        var budget = CharacterCreationResourcesService.ReadSavedPurchaseBudget(gear.Value.Resources);
        if (budget is null)
            return Reject<CharacterCreationLoadoutState>(CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        if (!budget.IsExact || budget.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, budget.Blockers);
        return new(CharacterCreationFoundationOutcomes.Success, new(loadout.Value, gear.Value.Resources, budget), []);
    }

    public CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> Preview(CharacterCreationLoadoutPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request = Freeze(request);
        if (request?.Binding is null) return Reject<CharacterCreationLoadoutPreview>(CharacterCreationLoadoutCompletionSchemas.RequestInvalid);
        var read = store.Get(request.Binding.WorkspaceId);
        var result = read.Success && read.Value is { } workspace ? PreviewObservation(store, workspace, resolver, request)
            : Reject<CharacterCreationLoadoutPreview>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static CharacterCreationFoundationResult<CharacterCreationLoadoutPreview> PreviewObservation(IWorkspaceStore store,
        WorkspaceStoredDocument workspace, ICharacterSourceDataResolver resolver, CharacterCreationLoadoutPreviewRequest request)
    {
        var prepared = PrepareObservation(store, workspace, resolver);
        if (prepared.Value is not { } state) return new(prepared.Outcome, null, prepared.Blockers);
        if (request.Binding != state.Inputs.Binding || request.SnapshotDigest != state.Inputs.SnapshotDigest)
            return Reject<CharacterCreationLoadoutPreview>(CharacterCreationLoadoutCompletionSchemas.SnapshotConflict);
        var priced = CharacterCreationLoadoutCompletionService.Preview(state.Inputs, request.Selections);
        if (priced.Value is not { } projection) return new(priced.Outcome, null, priced.Blockers);
        if (CharacterCreationLoadoutReceiptLedgerIntegrity.Same(state.Inputs.OriginalSelections, request.Selections))
            return Reject<CharacterCreationLoadoutPreview>(CharacterCreationGearBlockers.NoChange);
        decimal total = state.Resources.PendingDraft!.Budget.TotalStartingNuyen;
        decimal cost;
        try { cost = checked(state.Resources.KnownPurchaseCost - (state.Inputs.PendingDraft?.PurchaseCost ?? 0m) + projection.PurchaseCost); }
        catch (OverflowException) { return Reject<CharacterCreationLoadoutPreview>(CharacterCreationLoadoutBlockers.InvalidBasket); }
        if (cost > total) return Reject<CharacterCreationLoadoutPreview>(CharacterCreationResourcesBlockers.InsufficientNuyen);
        var preview = new CharacterCreationLoadoutPreview(request, projection, total, cost, total - cost, string.Empty);
        preview = preview with { PreviewDigest = CharacterCreationLoadoutDraftRules.PreviewDigest(preview) };
        return new(CharacterCreationFoundationOutcomes.Success, Freeze(preview), []);
    }

    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> Confirm(CharacterCreationLoadoutConfirmRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfirmed(request)) return Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.ExplicitConfirmationRequired);
        return store is ICharacterCreationLoadoutAtomicCommitCapability commit
            ? commit.CommitLoadout(OwnerScope.LocalSingleUser, Freeze(request), resolver, cancellationToken)
            : Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.AtomicPersistenceRejected);
    }

    public CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt> LookupReceipt(CharacterWorkspaceId workspaceId, string idempotencyKey)
    {
        var read = store.Get(workspaceId);
        return read.Success && read.Value is { } saved ? Lookup(saved, idempotencyKey)
            ?? Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable)
            : Reject<CharacterCreationLoadoutReceipt>(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
    }

    public static CharacterCreationFoundationResult<CharacterCreationLoadoutReceipt>? Lookup(WorkspaceStoredDocument saved,
        string key, string? commandDigest = null)
    {
        if (!ValidKey(key) || !CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(saved, out var history, out long revision)
            || !CharacterCreationLoadoutReceiptLedgerIntegrity.IsValidLedger(saved.Id, revision,
                history.CharacterCreationLoadoutDraft, history.CharacterCreationLoadoutReceipts))
            return Reject<CharacterCreationLoadoutReceipt>(CharacterCreationGearBlockers.ReceiptLedgerCorrupt);
        var receipt = history.CharacterCreationLoadoutReceipts?.SingleOrDefault(row => row.IdempotencyKeyDigest == CharacterCreationLoadoutDraftRules.KeyDigest(key));
        if (receipt is null) return null;
        return saved.CanReplayReceipt(receipt.ContentRevision) && (commandDigest is null || receipt.CommandDigest == commandDigest)
            ? new(CharacterCreationFoundationOutcomes.Success, Freeze(receipt), [])
            : Reject<CharacterCreationLoadoutReceipt>(CharacterCreationGearBlockers.IdempotencyConflict);
    }

    public static bool IsConfirmed(CharacterCreationLoadoutConfirmRequest? request)
        => request is { ExplicitlyConfirmed: true, Request.Binding: not null } && ValidKey(request.IdempotencyKey)
            && CharacterCreationFinalizationDigest.IsCanonical(request.PreviewDigest);

    // Produces a reviewable candidate only. The generic auxiliary writer refuses this lane.
    // The dedicated store operation repeats this evaluation immediately before atomic rename.
    public static bool TryBuild(IWorkspaceStore store, WorkspaceStoredDocument saved, ICharacterSourceDataResolver resolver,
        CharacterCreationLoadoutConfirmRequest request, CancellationToken cancellationToken,
        out WorkspaceDocument? replacement, out CharacterCreationLoadoutReceipt? receipt)
    {
        replacement = null; receipt = null;
        cancellationToken.ThrowIfCancellationRequested();
        if (!IsConfirmed(request) || saved.ContentRevision == long.MaxValue) return false;
        var evaluated = PreviewObservation(store, saved, resolver, request.Request);
        if (evaluated.Value is not { } preview || preview.PreviewDigest != request.PreviewDigest) return false;
        var state = saved.Document.AuxiliaryState;
        var observed = CharacterCreationLoadoutCompletionService.LoadFromObservation(saved, resolver, true);
        if (observed.Value is not { } inputs || inputs.SnapshotDigest != request.Request.SnapshotDigest
            || state.CharacterCreationResourcesDraft is not { } funding
            || !CharacterCreationLoadoutRules.TryCaptureSelectedAuthority(inputs.Authority, inputs.OriginalSelections,
                request.Request.Selections, out var selected)) return false;
        var draft = new CharacterCreationLoadoutDraft(CharacterCreationLoadoutDraft.SchemaV1, saved.Id,
            (state.CharacterCreationLoadoutDraft?.DraftRevision ?? 0) + 1, saved.ContentRevision,
            inputs.Binding.RawCharacterXmlDigest, funding.DraftRevision, funding.DraftDigest, selected!, request.Request.Selections,
            preview.Projection.Lines, CharacterCreationLoadoutReceiptLedgerIntegrity.CreateInstances(saved.Id, saved.ContentRevision,
                preview.Projection.Lines, state.CharacterCreationLoadoutDraft?.Instances), preview.Projection.PurchaseCost, string.Empty);
        draft = draft with { DraftDigest = CharacterCreationLoadoutDraftRules.DraftDigest(draft) };
        var history = state.CharacterCreationLoadoutReceipts ?? [];
        receipt = new(CharacterCreationLoadoutReceipt.SchemaV1, saved.Id, saved.ContentRevision, saved.ContentRevision + 1,
            saved.SavedRevision, saved.ContentRevision + 1, saved.Document.AuxiliaryStateDigest,
            CharacterCreationLoadoutDraftRules.KeyDigest(request.IdempotencyKey), CharacterCreationLoadoutRules.Compute(request),
            preview.PreviewDigest, history.LastOrDefault()?.ReceiptDigest ?? CharacterCreationLoadoutDraftRules.RootReceiptDigest,
            draft, string.Empty);
        receipt = receipt with { ReceiptDigest = CharacterCreationLoadoutDraftRules.ReceiptDigest(receipt) };
        var next = state with { CharacterCreationLoadoutDraft = draft, CharacterCreationLoadoutReceipts = [.. history, receipt] };
        if (!WorkspaceAuxiliaryStateIntegrity.IsValidShape(saved.Id, receipt.ContentRevision, next)) return false;
        replacement = saved.Document with { State = saved.Document.State with { AuxiliaryState = next } };
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    private static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Length <= 200 && !key.Any(char.IsControl);
    public static T Freeze<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.SerializeToUtf8Bytes(value))!;
    public static CharacterCreationFoundationResult<T> Reject<T>(string blocker) where T : class
        => new(CharacterCreationFoundationOutcomes.Blocked, null, [blocker]);
}
