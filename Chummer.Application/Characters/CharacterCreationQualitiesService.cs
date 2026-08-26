using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public interface ICharacterCreationQualitiesService
{
    CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(
        CharacterCreationQualitiesLoadRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(
        CharacterCreationQualitiesPreviewRequest request);

    CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(
        CharacterCreationQualitiesConfirmRequest request);
}

/// <summary>
/// Workspace service for the SR5 Priority qualities draft. Confirmation updates auxiliary
/// creation state and its receipt ledger atomically; it never mutates the character payload.
/// </summary>
public sealed class CharacterCreationQualitiesService : ICharacterCreationQualitiesService
{
    private readonly IWorkspaceStore _store;
    private readonly ICharacterSourceDataResolver _resolver;
    private readonly ICharacterCreationPrerequisiteService _prerequisites;
    private readonly ICharacterCreationAttributesService _attributes;

    public CharacterCreationQualitiesService(
        IWorkspaceStore store,
        ICharacterSourceDataResolver resolver,
        ICharacterCreationPrerequisiteService prerequisites,
        ICharacterCreationAttributesService attributes)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _prerequisites = prerequisites ?? throw new ArgumentNullException(nameof(prerequisites));
        _attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesState> Load(
        CharacterCreationQualitiesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _store.Get(request.WorkspaceId);
        return read.Success && read.Value is { } workspace
            ? BuildState(workspace)
            : Blocked<CharacterCreationQualitiesState>(
                CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationQualitiesBlockers.RevisionConflict);
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesPreview> Preview(
        CharacterCreationQualitiesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesPreview>(
                CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationQualitiesBlockers.RevisionConflict);
        CharacterCreationFoundationResult<CharacterCreationQualitiesState> stateResult = BuildState(workspace);
        if (stateResult.Value is not { } state)
            return new(stateResult.Outcome, null, stateResult.Blockers);

        if (state.Binding != request.Binding)
            return Blocked<CharacterCreationQualitiesPreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesBlockers.RevisionConflict);
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            state.Binding,
            state.Authority,
            request.SelectedOptionIds));
        string[] blockers = Normalize(state.Blockers.Concat(preview.Blockers));
        return new(
            blockers.Length == 0
                ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked,
            preview,
            blockers);
    }

    public CharacterCreationFoundationResult<CharacterCreationQualitiesDraftReceipt> Confirm(
        CharacterCreationQualitiesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.ExplicitConfirmationRequired);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 200
            || !string.Equals(request.IdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.IdempotencyKeyInvalid);

        WorkspaceStoreReadResult read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationQualitiesBlockers.RevisionConflict);

        IReadOnlyList<CharacterCreationQualitiesDraftReceipt> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationQualitiesReceipts ?? [];
        if (!IsValidLedger(ledger, workspace.Id, workspace.ContentRevision))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.ReceiptLedgerInvalid);
        string keyDigest = CharacterCreationQualitiesRules.ComputeIdempotencyKeyDigest(request.IdempotencyKey);
        string requestCommandDigest = CharacterCreationQualitiesRules.ComputeCommandDigest(
            request.Binding,
            request.SelectedOptionIds,
            request.PreviewDigest);
        CharacterCreationQualitiesDraftReceipt? replay = ledger.SingleOrDefault(receipt =>
            CharacterCreationQualitiesRules.DigestsEqual(receipt.IdempotencyKeyDigest, keyDigest));
        if (replay is not null)
            return CharacterCreationQualitiesRules.DigestsEqual(replay.CommandDigest, requestCommandDigest)
                ? new(CharacterCreationFoundationOutcomes.Success, replay, [])
                : Blocked<CharacterCreationQualitiesDraftReceipt>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationQualitiesBlockers.IdempotencyConflict);

        CharacterCreationFoundationResult<CharacterCreationQualitiesState> stateResult = BuildState(workspace);
        if (stateResult.Value is not { } state)
            return new(stateResult.Outcome, null, stateResult.Blockers);
        if (state.Binding != request.Binding)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesBlockers.RevisionConflict);

        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            state.Binding,
            state.Authority,
            request.SelectedOptionIds));
        string[] previewBlockers = Normalize(state.Blockers.Concat(preview.Blockers));
        if (previewBlockers.Length != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, previewBlockers);
        if (!CharacterCreationQualitiesRules.DigestsEqual(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesBlockers.PreviewChanged);

        string commandDigest = CharacterCreationQualitiesRules.ComputeCommandDigest(preview);
        if (!CharacterCreationQualitiesRules.DigestsEqual(commandDigest, requestCommandDigest))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesBlockers.PreviewChanged);
        bool transactionExists = ledger.Any(receipt => receipt.TransactionId == request.TransactionId);
        if (!CharacterCreationQualitiesRules.TryPlan(
                preview,
                request.PreviewDigest,
                request.IdempotencyKey,
                request.ExplicitlyConfirmed,
                transactionExists,
                request.TransactionId,
                out CharacterCreationQualitiesDraftPlan plan))
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                transactionExists
                    ? CharacterCreationQualitiesBlockers.IdempotencyConflict
                    : CharacterCreationQualitiesBlockers.PreviewChanged);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired);

        long draftRevision = state.PendingDraft is null
            ? 1
            : state.PendingDraft.DraftRevision == long.MaxValue
                ? long.MaxValue
                : state.PendingDraft.DraftRevision + 1;
        if (draftRevision == long.MaxValue)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationQualitiesBlockers.RevisionConflict);
        var draft = new CharacterCreationQualitiesDraft(
            CharacterCreationQualitiesSchemas.DraftV1,
            workspace.Id,
            draftRevision,
            workspace.ContentRevision,
            plan.ExpectedRawCharacterXmlDigest,
            state.Binding.PrerequisiteDraftRevision,
            state.Binding.PrerequisiteDraftDigest,
            state.Binding.AttributesDraftRevision,
            state.Binding.AttributesDraftDigest,
            plan.AuthorityDigest,
            plan.RuntimeDigest,
            plan.Selections.Select(static selection => selection.OptionId).ToArray(),
            plan.Selections,
            plan.PositiveKarmaUsed,
            plan.NegativeKarmaUsed,
            plan.KarmaRemaining,
            preview.SourceAnchorIds,
            CharacterEffectsApplied: false,
            LastIdempotencyKeyDigest: plan.IdempotencyKeyDigest,
            LastPreviewDigest: plan.PreviewDigest,
            LastCommandDigest: plan.CommandDigest,
            DraftDigest: string.Empty);
        draft = draft with
        {
            DraftDigest = CharacterCreationQualitiesRules.ComputeDraftDigest(draft)
        };
        var receipt = new CharacterCreationQualitiesDraftReceipt(
            CharacterCreationQualitiesSchemas.ReceiptV1,
            plan.TransactionId,
            plan.WorkspaceId,
            plan.ExpectedContentRevision,
            plan.TargetContentRevision,
            plan.ExpectedSavedRevision,
            plan.TargetSavedRevision,
            plan.AuthorityDigest,
            plan.RuntimeDigest,
            plan.PreviewDigest,
            plan.IdempotencyKeyDigest,
            plan.CommandDigest,
            plan.PlanDigest,
            draft.DraftDigest,
            ledger.Count == 0 ? CharacterCreationQualitiesRules.ReceiptLedgerRootDigest : ledger[^1].ReceiptDigest,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationQualitiesRules.ComputeReceiptDigest(receipt)
        };
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationQualitiesDraft = draft,
                    CharacterCreationQualitiesReceipts = [.. ledger, receipt]
                }
            }
        };
        WorkspaceStoreMutationResult mutation = atomic.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            workspace.Id,
            workspace.ContentRevision,
            workspace.Document.AuxiliaryStateDigest,
            replacement);
        if (!mutation.Success || mutation.Entry is not { } entry)
        {
            if (mutation.Outcome == WorkspaceOperationOutcome.Conflict)
            {
                WorkspaceStoreReadResult raced = _store.Get(workspace.Id);
                IReadOnlyList<CharacterCreationQualitiesDraftReceipt> racedLedger =
                    raced.Value?.Document.AuxiliaryState.CharacterCreationQualitiesReceipts ?? [];
                CharacterCreationQualitiesDraftReceipt? racedReplay = raced.Success
                    && raced.Value is { } racedWorkspace
                    && IsValidLedger(racedLedger, racedWorkspace.Id, racedWorkspace.ContentRevision)
                    ? racedLedger.SingleOrDefault(candidate =>
                        CharacterCreationQualitiesRules.DigestsEqual(
                            candidate.IdempotencyKeyDigest,
                            keyDigest))
                    : null;
                if (racedReplay is not null)
                    return CharacterCreationQualitiesRules.DigestsEqual(racedReplay.CommandDigest, commandDigest)
                        ? new(CharacterCreationFoundationOutcomes.Success, racedReplay, [])
                        : Blocked<CharacterCreationQualitiesDraftReceipt>(
                            CharacterCreationFoundationOutcomes.Conflict,
                            CharacterCreationQualitiesBlockers.IdempotencyConflict);
            }
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict
                    : CharacterCreationFoundationOutcomes.Invalid,
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationQualitiesBlockers.RevisionConflict
                    : CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired);
        }
        if (entry.ContentRevision != receipt.ContentRevision
            || entry.SavedRevision != receipt.SavedRevision)
            return Blocked<CharacterCreationQualitiesDraftReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired);
        return new(CharacterCreationFoundationOutcomes.Success, receipt, []);
    }

    private CharacterCreationFoundationResult<CharacterCreationQualitiesState> BuildState(
        WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true })
            blockers.Add(CharacterCreationQualitiesBlockers.PersistenceAuthorityRequired);
        CharacterCreationPrerequisiteState? prerequisiteState =
            _prerequisites.Load(new CharacterCreationPrerequisiteLoadRequest(workspace.Id)).Value;
        CharacterCreationPrerequisiteDraft? prerequisite = prerequisiteState?.PendingDraft;
        if (prerequisite is null)
        {
            blockers.Add(CharacterCreationQualitiesBlockers.PrerequisiteDraftRequired);
            prerequisite = null;
        }
        if (prerequisite?.TalentSelection?.GrantedQualities.Count > 0)
        {
            // Priority Talent grants are free/origin-sensitive instances. Until their
            // stable source identities and limit-contribution flags are projected, an
            // empty grant list would undercount the quality and Karma budgets.
            blockers.Add(CharacterCreationQualitiesBlockers.AuthorityUnavailable);
        }
        CharacterCreationAttributesState? attributesState =
            _attributes.Load(new CharacterCreationAttributesLoadRequest(workspace.Id)).Value;
        CharacterCreationAttributesDraft? attributes = attributesState?.PendingDraft;
        if (attributes is null)
        {
            blockers.Add(CharacterCreationQualitiesBlockers.AttributesDraftRequired);
            attributes = null;
        }
        ICharacterSourceDataContext? context = _resolver.TryCreateContext(workspace.Document.Content);
        CharacterCreationQualitiesAuthority authority = CharacterCreationQualitiesAuthority.Unavailable;
        if (context is null
            || !context.TryResolveCreationQualitiesAuthority(out authority)
            || !authority.IsAuthoritative
            || authority.Blockers.Count != 0
            || prerequisiteState is null
            || !CharacterCreationQualitiesRules.DigestsEqual(
                authority.ProfileDigest,
                prerequisiteState.Authority.RawProfileInputsDigest))
            blockers.Add(CharacterCreationQualitiesBlockers.AuthorityUnavailable);

        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        var binding = new CharacterCreationQualitiesBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            prerequisite?.DraftRevision ?? 0,
            prerequisite?.DraftDigest ?? string.Empty,
            attributes?.DraftRevision ?? 0,
            attributes?.DraftDigest ?? string.Empty,
            prerequisiteState?.RulesetId ?? workspace.Document.RulesetId,
            prerequisite?.BuildMethod ?? prerequisiteState?.BuildMethod ?? string.Empty,
            prerequisiteState?.CharacterCreated ?? true,
            prerequisite?.CreationKarmaTotal ?? 0,
            attributes?.CreationKarmaUsed ?? 0,
            authority.AuthorityDigest,
            authority.RuntimeDigest);
        CharacterCreationQualitiesDraft? pending =
            workspace.Document.AuxiliaryState.CharacterCreationQualitiesDraft;
        CharacterCreationQualitiesPreview preview = CharacterCreationQualitiesRules.Evaluate(new(
            binding,
            authority,
            pending?.SelectedOptionIds ?? []));
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt> receipts =
            workspace.Document.AuxiliaryState.CharacterCreationQualitiesReceipts ?? [];
        if (!IsValidLedger(receipts, workspace.Id, workspace.ContentRevision))
            blockers.Add(CharacterCreationQualitiesBlockers.ReceiptLedgerInvalid);
        if (pending is not null && !IsValidPending(
                pending,
                workspace,
                binding,
                preview))
            blockers.Add(CharacterCreationQualitiesBlockers.DraftInvalid);
        string[] normalized = Normalize(blockers.Concat(preview.Blockers));
        var state = new CharacterCreationQualitiesState(
            CharacterCreationQualitiesSchemas.StateV1,
            binding,
            authority,
            prerequisite,
            attributes,
            pending,
            preview,
            normalized,
            CanEdit: normalized.Length == 0,
            SnapshotDigest: string.Empty);
        state = state with
        {
            SnapshotDigest = CharacterCreationQualitiesRules.ComputeStateDigest(state)
        };
        return new(CharacterCreationFoundationOutcomes.Success, state, normalized);
    }

    private static bool IsValidPending(
        CharacterCreationQualitiesDraft draft,
        WorkspaceStoredDocument workspace,
        CharacterCreationQualitiesBinding binding,
        CharacterCreationQualitiesPreview preview)
        => string.Equals(draft.Schema, CharacterCreationQualitiesSchemas.DraftV1, StringComparison.Ordinal)
           && draft.WorkspaceId == workspace.Id
           && draft.DraftRevision > 0
           && draft.BaseContentRevision > 0
           && draft.BaseContentRevision < workspace.ContentRevision
           && CharacterCreationQualitiesRules.DigestsEqual(
               draft.BaseRawCharacterXmlDigest,
               binding.RawCharacterXmlDigest)
           && draft.PrerequisiteDraftRevision == binding.PrerequisiteDraftRevision
           && CharacterCreationQualitiesRules.DigestsEqual(
               draft.PrerequisiteDraftDigest,
               binding.PrerequisiteDraftDigest)
           && draft.AttributesDraftRevision == binding.AttributesDraftRevision
           && CharacterCreationQualitiesRules.DigestsEqual(
               draft.AttributesDraftDigest,
               binding.AttributesDraftDigest)
           && CharacterCreationQualitiesRules.DigestsEqual(draft.AuthorityDigest, binding.AuthorityDigest)
           && CharacterCreationQualitiesRules.DigestsEqual(draft.RuntimeDigest, binding.RuntimeDigest)
           && !draft.CharacterEffectsApplied
           && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastIdempotencyKeyDigest)
           && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastPreviewDigest)
           && CharacterCreationQualitiesRules.IsCanonicalDigest(draft.LastCommandDigest)
           && CharacterCreationQualitiesRules.DigestsEqual(
               draft.DraftDigest,
               CharacterCreationQualitiesRules.ComputeDraftDigest(draft))
           && draft.PositiveKarmaUsed == preview.PositiveQualityBudget.Used
           && draft.NegativeKarmaUsed == preview.NegativeQualityBudget.Used
           && draft.KarmaRemaining == preview.KarmaRemaining
           && draft.SelectedOptionIds.SequenceEqual(
               preview.Selections.Select(static selection => selection.OptionId),
               StringComparer.Ordinal);

    private static bool IsValidLedger(
        IReadOnlyList<CharacterCreationQualitiesDraftReceipt> ledger,
        CharacterWorkspaceId workspaceId,
        long contentRevision)
    {
        if (ledger.Count > 16_384
            || ledger.Select(static receipt => receipt.TransactionId).Distinct().Count() != ledger.Count
            || ledger.Select(static receipt => receipt.IdempotencyKeyDigest)
                .Distinct(StringComparer.Ordinal).Count() != ledger.Count)
            return false;
        string previous = CharacterCreationQualitiesRules.ReceiptLedgerRootDigest;
        foreach (CharacterCreationQualitiesDraftReceipt receipt in ledger)
        {
            if (!CharacterCreationQualitiesRules.IsStructurallyValidReceipt(
                    receipt,
                    workspaceId,
                    contentRevision)
                || !CharacterCreationQualitiesRules.DigestsEqual(receipt.PreviousReceiptDigest, previous))
                return false;
            previous = receipt.ReceiptDigest;
        }
        return true;
    }

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(static blocker => !string.IsNullOrWhiteSpace(blocker))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(static blocker => blocker, StringComparer.Ordinal)
        .ToArray();

    private static CharacterCreationFoundationResult<T> Blocked<T>(
        string outcome,
        string blocker) where T : class => new(outcome, default, [blocker]);
}
