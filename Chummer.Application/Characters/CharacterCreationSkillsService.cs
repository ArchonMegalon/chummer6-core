using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed partial class CharacterCreationSkillsService : ICharacterCreationSkillsService, ICharacterCreationSkillsReReviewService
{
    private readonly IWorkspaceStore _store;
    private readonly ICharacterSourceDataResolver _resolver;

    public CharacterCreationSkillsService(IWorkspaceStore store, ICharacterSourceDataResolver resolver)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsState> Load(
        CharacterCreationSkillsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _store.Get(request.WorkspaceId);
        return read.Success && read.Value is { } workspace
            ? BuildState(workspace)
            : Blocked<CharacterCreationSkillsState>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing
                    : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable);
    }

    internal CharacterCreationFoundationResult<CharacterCreationSkillsState> LoadForContinuation(
        CharacterCreationSkillsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _store.Get(request.WorkspaceId);
        return read.Success && read.Value is { } workspace
            ? BuildState(workspace, out _, forContinuation: true)
            : Blocked<CharacterCreationSkillsState>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing
                    : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Preview(
        CharacterCreationSkillsPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Evaluate(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> Confirm(
        CharacterCreationSkillsConfirmRequest request)
        => ConfirmCore(request, null);

    private CharacterCreationFoundationResult<CharacterCreationSkillsReceipt> ConfirmCore(
        CharacterCreationSkillsConfirmRequest request,
        CharacterCreationSkillsReReviewBinding? reReview)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.ExplicitConfirmationRequired);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 200
            || !string.Equals(request.IdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal))
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.IdempotencyKeyInvalid);

        WorkspaceStoreReadResult initial = _store.Get(request.Binding.WorkspaceId);
        if (!initial.Success || initial.Value is not { } currentWorkspace)
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable);

        string keyDigest = CharacterCreationSkillsDigest.ComputeUtf8(request.IdempotencyKey);
        string commandDigest = reReview is null ? ComputeCommandDigest(request)
            : CharacterCreationSkillsDigest.Compute(new
            {
                Schema = CharacterCreationSkillsReReviewSchemas.CommandV1,
                ReReview = reReview,
                CommandDigest = ComputeCommandDigest(request),
                ExplicitlyReviewedChanges = true
            });
        bool historyValid = CharacterCreationFinalizationReceiptLedgerIntegrity.TryReadReceiptHistory(
            currentWorkspace, out var history, out long historyRevision);
        IReadOnlyList<CharacterCreationSkillsReceipt>? ledger = history.CharacterCreationSkillsReceipts;
        if (!historyValid || !CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                ledger,
                currentWorkspace.Id,
                historyRevision))
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.ReceiptLedgerInvalid);
        CharacterCreationSkillsReceipt? replay = ledger?.SingleOrDefault(receipt =>
            CharacterCreationSkillsDigest.EqualsFixedTime(receipt.IdempotencyKeyDigest, keyDigest));
        if (replay is not null)
            return CharacterCreationSkillsDigest.EqualsFixedTime(replay.CommandDigest, commandDigest)
                ? new(CharacterCreationFoundationOutcomes.Success, replay, [])
                : Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationSkillsBlockers.IdempotencyConflict);

        PreviewEvaluation evaluation = Evaluate(new CharacterCreationSkillsPreviewRequest(
            request.Binding,
            request.Allocations,
            request.GroupAllocations), reReview);
        if (evaluation.Result.Value is not { } preview
            || evaluation.Workspace is not { } workspace
            || evaluation.Draft is not { } draft)
            return new(evaluation.Result.Outcome, null, evaluation.Result.Blockers);
        string expectedPreviewDigest = reReview is null ? preview.PreviewDigest
            : BuildReReviewPreview(reReview, workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft!, preview).PreviewDigest;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(expectedPreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationSkillsBlockers.PreviewDigestMismatch);
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, preview.Blockers);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);

        draft = draft with
        {
            LastIdempotencyKeyDigest = keyDigest,
            LastPreviewDigest = expectedPreviewDigest,
            LastCommandDigest = commandDigest,
            DraftDigest = string.Empty
        };
        draft = draft with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(draft) };
        long nextContent;
        try { nextContent = checked(workspace.ContentRevision + 1); }
        catch (OverflowException)
        {
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationSkillsBlockers.DraftConflict);
        }
        var receipt = new CharacterCreationSkillsReceipt(
            CharacterCreationSkillsSchemas.ReceiptV1,
            workspace.Id,
            workspace.ContentRevision,
            nextContent,
            nextContent,
            draft.DraftRevision,
            draft.DraftDigest,
            expectedPreviewDigest,
            keyDigest,
            commandDigest,
            ledger is { Count: > 0 }
                ? ledger[^1].ReceiptDigest
                : CharacterCreationSkillsDigest.ReceiptLedgerRootDigest,
            draft.SkillsAuthorityDigest,
            draft.RuntimeDigest,
            (int)preview.ActiveSkillPointBudget.Remaining,
            (int)preview.SkillGroupPointBudget.Remaining,
            (int)preview.KnowledgeSkillPointBudget.Remaining,
            preview.KnowledgePointOverflowToActive,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(receipt) };
        CharacterCreationSkillsReceipt[] replacementLedger = [.. ledger ?? [], receipt];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationSkillsDraft = draft,
                    CharacterCreationSkillsReceipts = replacementLedger
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
                WorkspaceStoreReadResult racedRead = _store.Get(workspace.Id);
                IReadOnlyList<CharacterCreationSkillsReceipt>? racedLedger = racedRead.Value?.Document
                    .AuxiliaryState.CharacterCreationSkillsReceipts;
                if (racedRead.Success
                    && racedRead.Value is { } racedWorkspace
                    && CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                        racedLedger,
                        racedWorkspace.Id,
                        racedWorkspace.ContentRevision))
                {
                    CharacterCreationSkillsReceipt? racedReplay = racedLedger?.SingleOrDefault(candidate =>
                        CharacterCreationSkillsDigest.EqualsFixedTime(candidate.IdempotencyKeyDigest, keyDigest));
                    if (racedReplay is not null)
                        return CharacterCreationSkillsDigest.EqualsFixedTime(
                                racedReplay.CommandDigest,
                                commandDigest)
                            ? new(CharacterCreationFoundationOutcomes.Success, racedReplay, [])
                            : Blocked<CharacterCreationSkillsReceipt>(
                                CharacterCreationFoundationOutcomes.Conflict,
                                CharacterCreationSkillsBlockers.IdempotencyConflict);
                }
            }
            return Blocked<CharacterCreationSkillsReceipt>(
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict
                    : CharacterCreationFoundationOutcomes.Invalid,
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationSkillsBlockers.DraftConflict
                    : CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
        }
        if (entry.ContentRevision != receipt.ContentRevision || entry.SavedRevision != receipt.SavedRevision)
            return Blocked<CharacterCreationSkillsReceipt>(CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
        return new(CharacterCreationFoundationOutcomes.Success, receipt, []);
    }

    private PreviewEvaluation Evaluate(CharacterCreationSkillsPreviewRequest request,
        CharacterCreationSkillsReReviewBinding? reReview = null)
    {
        WorkspaceStoreReadResult read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return new(Blocked<CharacterCreationSkillsPreview>(CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationSkillsBlockers.WorkspaceUnavailable), null, null);
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
            return new(Blocked<CharacterCreationSkillsPreview>(CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationSkillsBlockers.StaleWorkspaceRevision), null, null);
        CharacterCreationFoundationResult<CharacterCreationSkillsState> stateResult = BuildState(workspace, out var catalogAuthority);
        if (stateResult.Value is not { } state
            || state.PrerequisiteDraft is not { } prerequisite
            || state.AttributesDraft is not { } attributes)
            return new(new(
                stateResult.Outcome == CharacterCreationFoundationOutcomes.Success
                    ? CharacterCreationFoundationOutcomes.Blocked
                    : stateResult.Outcome,
                null,
                stateResult.Blockers), null, null);
        string? mismatch = CompareBinding(state.Binding, request.Binding);
        if (mismatch is not null)
            return new(Blocked<CharacterCreationSkillsPreview>(CharacterCreationFoundationOutcomes.Conflict, mismatch), null, null);

        var blockers = new List<string>(state.Blockers);
        if (reReview is not null)
        {
            if (!TryPrepareReReview(workspace, state, catalogAuthority, out var currentReview)
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(reReview), CharacterCreationSkillsDigest.Compute(currentReview)))
                return new(Blocked<CharacterCreationSkillsPreview>(CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationSkillsReReviewSchemas.Stale), null, null);
            blockers.RemoveAll(item => item is CharacterCreationSkillsBlockers.DraftInvalid
                or CharacterCreationSkillsBlockers.TalentAccessRequired);
        }
        SkillEvaluation projected = EvaluateAllocations(
            state.Authority,
            prerequisite,
            state.SelectedActiveSkillPoints,
            state.SelectedSkillGroupPoints,
            state.IntuitionUnaugmented,
            state.LogicUnaugmented,
            state.MovementCapability,
            request.Allocations,
            request.GroupAllocations,
            blockers);
        CharacterCreationSkillsDraft? draft = blockers.Count == 0
            ? BuildDraft(workspace, prerequisite, attributes, state.Authority,
                state.Binding.ContributionInputsDigest, projected)
            : null;
        if (draft is not null && state.PendingDraft is { } current)
        {
            if (current.DraftRevision == long.MaxValue)
                blockers.Add(CharacterCreationSkillsBlockers.DraftConflict);
            else if (CharacterCreationSkillsDraftIntegrity.HasSameLogicalPayload(current, draft))
                blockers.Add(CharacterCreationSkillsBlockers.DraftDuplicate);
        }
        string[] normalized = Normalize(blockers);
        var preview = new CharacterCreationSkillsPreview(
            CharacterCreationSkillsSchemas.PreviewV1,
            state.Binding,
            projected.Skills,
            projected.Groups,
            state.KnowledgePointContributions,
            projected.ActiveBudget,
            projected.GroupBudget,
            projected.KnowledgeBudget,
            projected.KnowledgeOverflow,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0 && draft is not null,
            PreviewDigest: string.Empty);
        preview = preview with
        {
            PreviewDigest = CharacterCreationSkillsDigest.Compute(preview with { PreviewDigest = string.Empty })
        };
        return new(new(
                normalized.Length == 0
                    ? CharacterCreationFoundationOutcomes.Success
                    : CharacterCreationFoundationOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            draft);
    }

    private CharacterCreationFoundationResult<CharacterCreationSkillsState> BuildState(
        WorkspaceStoredDocument workspace)
        => BuildState(workspace, out _);

    private CharacterCreationFoundationResult<CharacterCreationSkillsState> BuildState(
        WorkspaceStoredDocument workspace, out CharacterCreationSkillsAuthority catalogAuthority,
        bool forContinuation = false)
    {
        var blockers = new List<string>();
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true })
            blockers.Add(CharacterCreationSkillsBlockers.PersistenceAuthorityRequired);
        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        ICharacterSourceDataContext? context = _resolver.TryCreateContext(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CharacterCreationPrerequisiteAuthority.Unavailable;
        CharacterCreationSkillsAuthority authority = CharacterCreationSkillsAuthority.Unavailable;
        bool authorityReady = context is not null
            && context.TryResolveCreationPrerequisiteAuthority(out prerequisiteAuthority)
            && context.TryResolveCreationSkillsAuthority(out authority)
            && CharacterCreationSkillsDraftIntegrity.IsValidAuthority(authority);
        if (!authorityReady)
            blockers.Add(CharacterCreationSkillsBlockers.AuthorityUnavailable);
        else if (!string.Equals(
                     authority.SettingsProfileId,
                     prerequisiteAuthority.SettingsProfileId,
                     StringComparison.Ordinal)
                 || !CharacterCreationSkillsDigest.EqualsFixedTime(
                     authority.RawProfileInputsDigest,
                     prerequisiteAuthority.RawProfileInputsDigest)
                 || !CharacterCreationSkillsDigest.EqualsFixedTime(
                     authority.EffectiveSkillsInputsDigest,
                     prerequisiteAuthority.EffectiveSkillsInputsDigest))
            blockers.Add(CharacterCreationSkillsBlockers.SkillsSourceDrift);
        else if (authority.KnowledgePointContributions.Any(contribution =>
                     !CharacterCreationSkillsDigest.EqualsFixedTime(
                         contribution.SourceCharacterXmlDigest,
                         rawDigest)))
            blockers.Add(CharacterCreationSkillsBlockers.KnowledgeContributionAuthorityUnsupported);

        CharacterCreationPrerequisiteDraft? prerequisite =
            workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (prerequisite is null
            || !CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                prerequisite,
                workspace.Id,
                workspace.ContentRevision,
                rawDigest,
                prerequisiteAuthority))
        {
            blockers.Add(CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
            prerequisite = null;
        }
        catalogAuthority = authority;
        if (prerequisite is not null && authorityReady
            && !CharacterCreationSkillsAccessRules.TryBind(prerequisite, context!, authority, out authority))
            blockers.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
        CharacterCreationAttributesState? attributeState =
            new CharacterCreationAttributesService(_store, _resolver)
                .Load(new CharacterCreationAttributesLoadRequest(workspace.Id)).Value;
        CharacterCreationAttributesDraft? attributes = attributeState?.PendingDraft;
        // Attributes.Load already recomputed the pending draft. A read-only
        // continuation view cannot write, but that alone does not invalidate it.
        if (attributes is null || (forContinuation
                ? attributeState!.Blockers.Any(blocker =>
                    blocker != CharacterCreationAttributesBlockers.PersistenceAuthorityRequired)
                : !attributeState!.CanEdit || attributeState.Blockers.Count != 0))
        {
            blockers.Add(attributes is null
                ? CharacterCreationSkillsBlockers.AttributesDraftRequired
                : CharacterCreationSkillsBlockers.AttributesDraftInvalid);
            attributes = null;
        }
        int intuition = attributes?.Attributes.SingleOrDefault(item => item.AttributeId == "INT")?.UnaugmentedCurrent ?? 0;
        int logic = attributes?.Attributes.SingleOrDefault(item => item.AttributeId == "LOG")?.UnaugmentedCurrent ?? 0;
        CharacterCreationMovementCapability movementCapability = ResolveMovementCapability(
            prerequisite?.HeritageSelection?.Movement);
        CharacterCreationKnowledgePointContribution[] contributions = authority.KnowledgePointContributions
            .OrderBy(item => item.ContributionId, StringComparer.Ordinal)
            .ToArray();
        string contributionDigest = CharacterCreationSkillsDigest.Compute(contributions);

        int activeTotal = 0;
        int groupTotal = 0;
        if (prerequisite is not null)
        {
            CharacterCreationPriorityAssignment? assignment = prerequisite.Assignments.SingleOrDefault(item =>
                item.CategoryId == CharacterCreationPriorityCategoryIds.Skills);
            CharacterCreationPriorityOptionProjection? option = assignment is null
                ? null
                : prerequisiteAuthority.Options.SingleOrDefault(item =>
                    item.CategoryId == CharacterCreationPriorityCategoryIds.Skills
                    && item.SourceId == assignment.SourceId
                    && item.Rank == assignment.Rank
                    && CharacterCreationSkillsDigest.EqualsFixedTime(
                        item.SourceNodeDigest,
                        assignment.SourceNodeDigest));
            if (option?.BaseActiveSkillPoints is not int active
                || option.BaseSkillGroupPoints is not int groups
                || !string.Equals(prerequisite.BuildMethod, CharacterCreationBuildMethods.Priority, StringComparison.Ordinal)
                || !string.Equals(prerequisite.PriorityTable, "Standard", StringComparison.Ordinal)
                || !CharacterCreationStandardPrioritySkillsRules.HasExactBudgetTable(prerequisiteAuthority.Options)
                || !CharacterCreationStandardPrioritySkillsRules.TryGetBudget(
                    assignment?.Rank,
                    out int expectedActive,
                    out int expectedGroups)
                || active != expectedActive
                || groups != expectedGroups)
                blockers.Add(CharacterCreationSkillsBlockers.SkillsPriorityAuthorityInvalid);
            else
            {
                activeTotal = active;
                groupTotal = groups;
            }
        }

        CharacterCreationSkillsDraft? pending =
            workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft;
        if (prerequisite is not null
            && !CharacterCreationTalentSkillGrants.TryResolve(prerequisite, authority, out _))
            blockers.Add(CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
        IReadOnlyList<CharacterCreationSkillsReceipt>? receipts =
            workspace.Document.AuxiliaryState.CharacterCreationSkillsReceipts;
        if (!CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
                receipts,
                workspace.Id,
                workspace.ContentRevision))
            blockers.Add(CharacterCreationSkillsBlockers.ReceiptLedgerInvalid);
        if (pending is not null
            && (prerequisite is null
                || attributes is null
                || !CharacterCreationSkillsDraftIntegrity.IsStructurallyValidPending(
                    pending,
                    workspace.Id,
                    workspace.ContentRevision,
                    rawDigest,
                    prerequisite,
                    attributes,
                    authority,
                    contributionDigest)))
            blockers.Add(CharacterCreationSkillsBlockers.DraftInvalid);
        var pendingBlockers = new List<string>();
        SkillEvaluation projected = EvaluateAllocations(
            authority,
            prerequisite,
            activeTotal,
            groupTotal,
            intuition,
            logic,
            movementCapability,
            pending?.Allocations ?? [],
            pending?.GroupAllocations ?? [],
            pendingBlockers);
        if (pendingBlockers.Contains(CharacterCreationSkillsBlockers.TalentAccessRequired))
            blockers.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
        if (pending is not null
            && (pendingBlockers.Count != 0
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    pending.SkillsAuthorityDigest,
                    authority.AuthorityDigest)
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(pending.Skills),
                    CharacterCreationSkillsDigest.Compute(projected.Skills))
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(pending.SkillGroups),
                    CharacterCreationSkillsDigest.Compute(projected.Groups))
                || pending.ActivePointTotal != (int)projected.ActiveBudget.Total
                || pending.ActivePointUsed != (int)projected.ActiveBudget.Used
                || pending.SkillGroupPointTotal != (int)projected.GroupBudget.Total
                || pending.SkillGroupPointUsed != (int)projected.GroupBudget.Used
                || pending.KnowledgePointTotal != (int)projected.KnowledgeBudget.Total
                || pending.KnowledgePointUsed != (int)projected.KnowledgeBudget.Used
                || pending.KnowledgePointOverflowToActive != projected.KnowledgeOverflow
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(pending.KnowledgePointContributions),
                    CharacterCreationSkillsDigest.Compute(contributions))
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(pending.Allocations),
                    CharacterCreationSkillsDigest.Compute(projected.Skills.Select(item =>
                        new CharacterCreationSkillAllocation(
                            item.SourceSkillId,
                            item.Kind,
                            item.Rating,
                            item.SpecializationOptionId,
                            item.IsNativeLanguage)).ToArray()))
                || !CharacterCreationSkillsDigest.EqualsFixedTime(
                    CharacterCreationSkillsDigest.Compute(pending.GroupAllocations),
                    CharacterCreationSkillsDigest.Compute(projected.Groups.Select(item =>
                        new CharacterCreationSkillGroupAllocation(item.GroupId, item.Rating)).ToArray()))))
            blockers.Add(CharacterCreationSkillsBlockers.DraftInvalid);

        var binding = new CharacterCreationSkillsBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            prerequisite?.DraftRevision ?? 0,
            prerequisite?.DraftDigest ?? string.Empty,
            prerequisite?.AuthorityDigest ?? string.Empty,
            attributes?.DraftRevision ?? 0,
            attributes?.DraftDigest ?? string.Empty,
            authority.AuthorityDigest,
            authority.RuntimeDigest,
            contributionDigest);
        string[] normalized = Normalize(blockers.Concat(authority.Blockers));
        var state = new CharacterCreationSkillsState(
            CharacterCreationSkillsSchemas.SnapshotV1,
            binding,
            authority,
            prerequisite,
            attributes,
            pending,
            projected.Skills,
            projected.Groups,
            contributions,
            projected.ActiveBudget,
            projected.GroupBudget,
            projected.KnowledgeBudget,
            intuition,
            logic,
            normalized,
            CanEdit: prerequisite is not null && attributes is not null && normalized.Length == 0,
            SnapshotDigest: string.Empty)
        {
            SelectedActiveSkillPoints = activeTotal,
            SelectedSkillGroupPoints = groupTotal,
            MovementCapability = movementCapability
        };
        state = state with
        {
            SnapshotDigest = CharacterCreationSkillsDigest.Compute(state with { SnapshotDigest = string.Empty })
        };
        return new(CharacterCreationFoundationOutcomes.Success, state, normalized);
    }

    private static SkillEvaluation EvaluateAllocations(
        CharacterCreationSkillsAuthority authority,
        CharacterCreationPrerequisiteDraft? prerequisite,
        int activeTotal,
        int groupTotal,
        int intuition,
        int logic,
        CharacterCreationMovementCapability movementCapability,
        IReadOnlyList<CharacterCreationSkillAllocation>? requested,
        IReadOnlyList<CharacterCreationSkillGroupAllocation>? requestedGroups,
        ICollection<string> blockers,
        SkillsEvaluationSemantics semantics = SkillsEvaluationSemantics.CurrentSourceBound)
    {
        requested ??= [];
        requestedGroups ??= [];
        if (!CharacterCreationTalentSkillGrants.TryResolve(prerequisite, authority, out var grants))
            blockers.Add(CharacterCreationSkillsBlockers.PrerequisiteSourceDrift);
        var allocationMap = new Dictionary<(string Kind, string SourceId), CharacterCreationSkillAllocation>();
        foreach (CharacterCreationSkillAllocation? allocation in requested)
        {
            if (allocation is null
                || string.IsNullOrWhiteSpace(allocation.SourceSkillId)
                || allocation.Kind is not (CharacterCreationSkillKinds.Active or CharacterCreationSkillKinds.Knowledge))
            {
                blockers.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
                continue;
            }
            if (!allocationMap.TryAdd((allocation.Kind, allocation.SourceSkillId), allocation))
                blockers.Add(CharacterCreationSkillsBlockers.AllocationDuplicate);
        }

        var groupMap = new Dictionary<string, CharacterCreationSkillGroupAllocation>(StringComparer.Ordinal);
        foreach (CharacterCreationSkillGroupAllocation? allocation in requestedGroups)
        {
            if (allocation is null || string.IsNullOrWhiteSpace(allocation.GroupId))
            {
                blockers.Add(CharacterCreationSkillsBlockers.GroupInvalid);
                continue;
            }
            if (!groupMap.TryAdd(allocation.GroupId, allocation))
                blockers.Add(CharacterCreationSkillsBlockers.GroupAllocationDuplicate);
        }

        var groups = new List<CharacterCreationSkillGroupProjection>();
        // Omitting a talent-granted row never deletes the free rating. Requests
        // describe the desired total rating; only increases consume points.
        foreach (var grant in grants.Skills)
            allocationMap.TryAdd((CharacterCreationSkillKinds.Active, grant.Key),
                new(grant.Key, CharacterCreationSkillKinds.Active, grant.Value.BaseRating, null, false));
        foreach (var grant in grants.Groups)
            groupMap.TryAdd(grant.Key, new(grant.Key, grant.Value.BaseRating));
        int groupUsed = 0;
        foreach (CharacterCreationSkillGroupAllocation allocation in groupMap.Values
                     .OrderBy(item => item.GroupId, StringComparer.Ordinal))
        {
            CharacterCreationSkillGroupCatalogEntry? source = authority.SkillGroups.SingleOrDefault(item =>
                item.GroupId == allocation.GroupId);
            string[] availableMemberIds = source?.MemberSkillSourceIds
                .Where(id => authority.ActiveSkills.SingleOrDefault(skill =>
                    string.Equals(skill.SourceSkillId, id, StringComparison.Ordinal)) is { } skill
                    && IsMovementAvailable(skill, movementCapability)
                    && (semantics == SkillsEvaluationSemantics.PreTalentAccessHistory
                        || CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, id)))
                .ToArray() ?? [];
            var local = new List<string>();
            grants.Groups.TryGetValue(allocation.GroupId, out var grant);
            int grantedRating = grant?.BaseRating ?? 0;
            if (source is null
                || allocation.Rating < 1
                || allocation.Rating < grantedRating
                || allocation.Rating > authority.MaxSkillGroupRatingCreate)
                local.Add(CharacterCreationSkillsBlockers.GroupInvalid);
            if (source is not null && semantics == SkillsEvaluationSemantics.CurrentSourceBound
                && !CharacterCreationSkillsAccessRules.IsGroupAvailable(authority, source.GroupId))
                local.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
            if (source is not null && availableMemberIds.Any(id =>
                    allocationMap.ContainsKey((CharacterCreationSkillKinds.Active, id))))
                local.Add(CharacterCreationSkillsBlockers.GroupBroken);
            AddAll(blockers, local);
            if (source is not null)
                groupUsed = SafeAdd(groupUsed, Math.Max(0, allocation.Rating - grantedRating), blockers);
            groups.Add(new(
                allocation.GroupId,
                source?.Name ?? string.Empty,
                allocation.Rating,
                Math.Max(0, allocation.Rating - grantedRating),
                source?.MemberSkillSourceIds ?? [],
                local.Count == 0,
                Normalize(local),
                CharacterCreationTalentSkillGrants.Anchors(source?.SourceAnchorIds ?? [], grant?.SourceAnchorIds))
                { GrantedRating = grantedRating });
        }

        var skills = new List<CharacterCreationSkillProjection>();
        int activeUsed = 0;
        int knowledgeCost = 0;
        int nativeCount = 0;
        foreach (CharacterCreationSkillAllocation allocation in allocationMap.Values
                     .OrderBy(item => item.Kind, StringComparer.Ordinal)
                     .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal))
        {
            CharacterCreationSkillCatalogEntry? source =
                (allocation.Kind == CharacterCreationSkillKinds.Active
                    ? authority.ActiveSkills
                    : authority.KnowledgeSkills).SingleOrDefault(item =>
                        item.SourceSkillId == allocation.SourceSkillId);
            var local = new List<string>();
            var grant = allocation.Kind == CharacterCreationSkillKinds.Active
                ? grants.Skills.GetValueOrDefault(allocation.SourceSkillId) : null;
            int grantedRating = grant?.BaseRating ?? 0;
            int max = allocation.Kind == CharacterCreationSkillKinds.Active
                ? authority.MaxActiveSkillRatingCreate
                : authority.MaxKnowledgeSkillRatingCreate;
            if (source is null)
                local.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
            if (source?.IsExotic == true)
                local.Add(CharacterCreationSkillsBlockers.ExoticSkillUnsupported);
            if (source is not null && !IsMovementAvailable(source, movementCapability))
                local.Add(CharacterCreationSkillsBlockers.MovementRequirementUnmet);
            if (source is not null && semantics == SkillsEvaluationSemantics.CurrentSourceBound
                && allocation.Kind == CharacterCreationSkillKinds.Active
                && !CharacterCreationSkillsAccessRules.IsSkillAvailable(authority, source.SourceSkillId))
                local.Add(CharacterCreationSkillsBlockers.TalentAccessRequired);
            if (allocation.IsNativeLanguage)
            {
                nativeCount++;
                if (source?.CanBeNativeLanguage != true
                    || allocation.Rating is not null
                    || allocation.SpecializationOptionId is not null)
                    local.Add(CharacterCreationSkillsBlockers.NativeLanguageInvalid);
            }
            else if (allocation.Rating is not int rating || rating < 1 || rating < grantedRating || rating > max)
                local.Add(CharacterCreationSkillsBlockers.RatingInvalid);

            CharacterCreationSkillSpecializationOption? specialization =
                allocation.SpecializationOptionId is null
                    ? null
                    : source?.Specializations.SingleOrDefault(item =>
                        item.OptionId == allocation.SpecializationOptionId);
            if (allocation.SpecializationOptionId is not null && specialization is null)
                local.Add(CharacterCreationSkillsBlockers.SpecializationInvalid);
            if (source?.SkillGroup is not null
                // Chummer5 ignores disabled skills when deciding whether a
                // group is broken, while retaining the canonical membership.
                && IsMovementAvailable(source, movementCapability)
                && groups.Any(group => group.MemberSkillSourceIds.Contains(
                    source.SourceSkillId,
                    StringComparer.Ordinal)))
                local.Add(CharacterCreationSkillsBlockers.GroupBroken);

            int cost = allocation.IsNativeLanguage
                ? 0
                : Math.Max(0, allocation.Rating.GetValueOrDefault() - grantedRating) + (specialization is null ? 0 : 1);
            if (allocation.Kind == CharacterCreationSkillKinds.Active)
                activeUsed = SafeAdd(activeUsed, cost, blockers);
            else
                knowledgeCost = SafeAdd(knowledgeCost, cost, blockers);
            AddAll(blockers, local);
            skills.Add(new(
                allocation.SourceSkillId,
                allocation.Kind,
                source?.Name ?? string.Empty,
                source?.Category ?? string.Empty,
                source?.DefaultAttribute ?? string.Empty,
                source?.SkillGroup,
                allocation.Rating,
                // Native is a distinct, non-rated language state. Core must not
                // invent a numeric rating that no source authority supplied.
                allocation.IsNativeLanguage ? null : allocation.Rating,
                cost,
                allocation.SpecializationOptionId,
                specialization?.Name,
                allocation.IsNativeLanguage,
                local.Count == 0,
                Normalize(local),
                CharacterCreationTalentSkillGrants.Anchors(source?.SourceAnchorIds ?? [], grant?.SourceAnchorIds))
                { GrantedRating = grantedRating });
        }
        if (nativeCount > authority.BaseNativeLanguageLimit)
            blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageLimitExceeded);
        else if (nativeCount < authority.BaseNativeLanguageLimit)
            blockers.Add(CharacterCreationSkillsBlockers.NativeLanguageRequired);

        int contributionPoints = 0;
        foreach (CharacterCreationKnowledgePointContribution contribution in authority.KnowledgePointContributions)
        {
            if (contribution.Points < 0
                || string.IsNullOrWhiteSpace(contribution.ContributionId)
                || !CharacterCreationSkillsDigest.IsCanonical(contribution.SourceDigest))
                blockers.Add(CharacterCreationSkillsBlockers.AuthorityUnavailable);
            contributionPoints = SafeAdd(contributionPoints, contribution.Points, blockers);
        }
        int knowledgeTotal = Math.Max(
            0,
            SafeAdd(
                SafeMultiply(SafeAdd(intuition, logic, blockers), 2, blockers),
                contributionPoints,
                blockers));
        int knowledgeUsed = Math.Min(knowledgeCost, knowledgeTotal);
        int overflow = Math.Max(0, knowledgeCost - knowledgeTotal);
        activeUsed = SafeAdd(activeUsed, overflow, blockers);
        if (activeUsed > activeTotal)
            blockers.Add(CharacterCreationSkillsBlockers.ActiveBudgetExceeded);
        if (groupUsed > groupTotal)
            blockers.Add(CharacterCreationSkillsBlockers.GroupBudgetExceeded);

        return new(
            skills,
            groups,
            Budget("active-skills", "Active Skill Points", activeTotal, activeUsed,
                CharacterCreationSkillsBlockers.ActiveBudgetExceeded),
            Budget("skill-groups", "Skill Group Points", groupTotal, groupUsed,
                CharacterCreationSkillsBlockers.GroupBudgetExceeded),
            Budget("knowledge-skills", "Knowledge Skill Points", knowledgeTotal, knowledgeUsed,
                CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded),
            overflow);
    }

    private static CharacterCreationMovementCapability ResolveMovementCapability(
        CharacterCreationMetatypeMovementProjection? movement)
    {
        if (movement is null || movement.IsSpecial)
            return new CharacterCreationMovementCapability(false, false, false);
        return new CharacterCreationMovementCapability(
            movement.Walk.Ground > 0m || movement.Run.Ground > 0m || movement.Sprint.Ground > 0m,
            movement.Walk.Swim > 0m || movement.Run.Swim > 0m || movement.Sprint.Swim > 0m,
            movement.Walk.Fly > 0m || movement.Run.Fly > 0m || movement.Sprint.Fly > 0m);
    }

    private static bool IsMovementAvailable(
        CharacterCreationSkillCatalogEntry skill,
        CharacterCreationMovementCapability capability)
        => (!skill.RequiresGroundMovement || capability.Ground)
           && (!skill.RequiresSwimMovement || capability.Swim)
           && (!skill.RequiresFlyMovement || capability.Fly);

    private static CharacterCreationSkillsDraft BuildDraft(
        WorkspaceStoredDocument workspace,
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationAttributesDraft attributes,
        CharacterCreationSkillsAuthority authority,
        string contributionDigest,
        SkillEvaluation evaluation)
    {
        CharacterCreationSkillsDraft? current = workspace.Document.AuxiliaryState.CharacterCreationSkillsDraft;
        long next = current is null || current.DraftRevision == long.MaxValue
            ? current is null ? 1 : long.MaxValue
            : current.DraftRevision + 1;
        var draft = new CharacterCreationSkillsDraft(
            CharacterCreationSkillsSchemas.DraftV1,
            workspace.Id,
            next,
            workspace.ContentRevision,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(workspace.Document.Content),
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest,
            attributes.DraftRevision,
            attributes.DraftDigest,
            authority.AuthorityDigest,
            authority.RuntimeDigest,
            contributionDigest,
            (int)evaluation.ActiveBudget.Total,
            (int)evaluation.ActiveBudget.Used,
            (int)evaluation.GroupBudget.Total,
            (int)evaluation.GroupBudget.Used,
            (int)evaluation.KnowledgeBudget.Total,
            (int)evaluation.KnowledgeBudget.Used,
            evaluation.KnowledgeOverflow,
            evaluation.Skills.Select(item => new CharacterCreationSkillAllocation(
                item.SourceSkillId,
                item.Kind,
                item.Rating,
                item.SpecializationOptionId,
                item.IsNativeLanguage)).ToArray(),
            evaluation.Groups.Select(item => new CharacterCreationSkillGroupAllocation(
                item.GroupId,
                item.Rating)).ToArray(),
            evaluation.Skills,
            evaluation.Groups,
            authority.KnowledgePointContributions.OrderBy(item => item.ContributionId, StringComparer.Ordinal).ToArray(),
            prerequisite.SourceAnchorIds.Concat(authority.SourceAnchorIds)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray(),
            CharacterEffectsApplied: false,
            LastIdempotencyKeyDigest: CharacterCreationSkillsDigest.ComputeUtf8("pending"),
            LastPreviewDigest: CharacterCreationSkillsDigest.ComputeUtf8("pending"),
            LastCommandDigest: CharacterCreationSkillsDigest.ComputeUtf8("pending"),
            DraftDigest: string.Empty);
        return draft with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(draft) };
    }

    private static string ComputeCommandDigest(CharacterCreationSkillsConfirmRequest request) =>
        CharacterCreationSkillsDigest.Compute(new
        {
            Schema = "chummer.character_creation_skills_command.v1",
            request.Binding,
            Allocations = (request.Allocations ?? []).OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceSkillId, StringComparer.Ordinal).ToArray(),
            GroupAllocations = (request.GroupAllocations ?? []).OrderBy(item => item.GroupId, StringComparer.Ordinal).ToArray(),
            request.PreviewDigest,
            ExplicitlyConfirmed = true
        });

    private static string? CompareBinding(
        CharacterCreationSkillsBinding current,
        CharacterCreationSkillsBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationSkillsBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(current.RawCharacterXmlDigest, requested.RawCharacterXmlDigest))
            return CharacterCreationSkillsBlockers.StaleRawCharacterXmlDigest;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(current.AuxiliaryStateDigest, requested.AuxiliaryStateDigest))
            return CharacterCreationSkillsBlockers.DraftConflict;
        if (current.PrerequisiteDraftRevision != requested.PrerequisiteDraftRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(current.PrerequisiteDraftDigest, requested.PrerequisiteDraftDigest)
            || !CharacterCreationSkillsDigest.EqualsFixedTime(current.PrerequisiteAuthorityDigest, requested.PrerequisiteAuthorityDigest))
            return CharacterCreationSkillsBlockers.PrerequisiteSourceDrift;
        if (current.AttributesDraftRevision != requested.AttributesDraftRevision
            || !CharacterCreationSkillsDigest.EqualsFixedTime(current.AttributesDraftDigest, requested.AttributesDraftDigest))
            return CharacterCreationSkillsBlockers.AttributesDraftInvalid;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(current.SkillsAuthorityDigest, requested.SkillsAuthorityDigest))
            return CharacterCreationSkillsBlockers.SkillsSourceDrift;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationSkillsBlockers.RuntimeDrift;
        if (!CharacterCreationSkillsDigest.EqualsFixedTime(current.ContributionInputsDigest, requested.ContributionInputsDigest))
            return CharacterCreationSkillsBlockers.SkillsSourceDrift;
        return null;
    }

    private static CharacterCreationBudgetState Budget(
        string id,
        string label,
        int total,
        int used,
        string blocker)
    {
        bool valid = total >= 0 && used >= 0 && used <= total;
        return new(
            id,
            label,
            Math.Max(0, total),
            Math.Max(0, used),
            valid ? total - used : 0,
            valid,
            valid ? [] : [blocker],
            "points");
    }

    private static int SafeAdd(int left, int right, ICollection<string> blockers)
    {
        try { return checked(left + right); }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
            return int.MaxValue;
        }
    }

    private static int SafeMultiply(int left, int right, ICollection<string> blockers)
    {
        try { return checked(left * right); }
        catch (OverflowException)
        {
            blockers.Add(CharacterCreationSkillsBlockers.AllocationInvalid);
            return int.MaxValue;
        }
    }

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();

    private static void AddAll(ICollection<string> target, IEnumerable<string> values)
    {
        foreach (string value in values)
            target.Add(value);
    }

    private static CharacterCreationFoundationResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationFoundationResult<CharacterCreationSkillsPreview> Result,
        WorkspaceStoredDocument? Workspace,
        CharacterCreationSkillsDraft? Draft);

    private sealed record SkillEvaluation(
        IReadOnlyList<CharacterCreationSkillProjection> Skills,
        IReadOnlyList<CharacterCreationSkillGroupProjection> Groups,
        CharacterCreationBudgetState ActiveBudget,
        CharacterCreationBudgetState GroupBudget,
        CharacterCreationBudgetState KnowledgeBudget,
        int KnowledgeOverflow);
}
