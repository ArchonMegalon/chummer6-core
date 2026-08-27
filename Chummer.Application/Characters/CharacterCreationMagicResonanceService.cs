using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Deterministic, draft-only SR5 Standard Priority Magic/Resonance step. This service
/// advances only the workspace auxiliary ledger; character XML is never a mutation target.
/// </summary>
public sealed class CharacterCreationMagicResonanceService : ICharacterCreationMagicResonanceService
{
    private readonly IWorkspaceStore _store;
    private readonly ICharacterSourceDataResolver _resolver;

    public CharacterCreationMagicResonanceService(
        IWorkspaceStore store,
        ICharacterSourceDataResolver resolver)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> Load(
        CharacterCreationMagicResonanceLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _store.Get(request.WorkspaceId);
        return read.Success && read.Value is { } workspace
            ? BuildState(workspace)
            : Blocked<CharacterCreationMagicResonanceState>(
                read.Outcome == WorkspaceOperationOutcome.Missing
                    ? CharacterCreationFoundationOutcomes.Missing
                    : CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable);
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Preview(
        CharacterCreationMagicResonancePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Evaluate(request).Result;
    }

    public CharacterCreationFoundationResult<CharacterCreationMagicResonanceReceipt> Confirm(
        CharacterCreationMagicResonanceConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.ExplicitConfirmationRequired);
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey)
            || request.IdempotencyKey.Length > 200
            || !string.Equals(request.IdempotencyKey, request.IdempotencyKey.Trim(), StringComparison.Ordinal))
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.IdempotencyKeyInvalid);

        WorkspaceStoreReadResult initial = _store.Get(request.Binding.WorkspaceId);
        if (!initial.Success || initial.Value is not { } currentWorkspace)
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable);

        string keyDigest = CharacterCreationMagicResonanceDigest.ComputeUtf8(request.IdempotencyKey);
        string commandDigest = ComputeCommandDigest(request);
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? ledger = currentWorkspace.Document
            .AuxiliaryState.CharacterCreationMagicResonanceReceipts;
        if (!CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                ledger, currentWorkspace.Id, currentWorkspace.ContentRevision))
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.ReceiptLedgerInvalid);
        CharacterCreationMagicResonanceReceipt? replay = ledger?.SingleOrDefault(receipt =>
            CharacterCreationMagicResonanceDigest.EqualsFixedTime(receipt.IdempotencyKeyDigest, keyDigest));
        if (replay is not null)
            return CharacterCreationMagicResonanceDigest.EqualsFixedTime(replay.CommandDigest, commandDigest)
                ? new(CharacterCreationFoundationOutcomes.Success, replay, [])
                : Blocked<CharacterCreationMagicResonanceReceipt>(
                    CharacterCreationFoundationOutcomes.Conflict,
                    CharacterCreationMagicResonanceBlockers.IdempotencyConflict);

        PreviewEvaluation evaluation = Evaluate(new CharacterCreationMagicResonancePreviewRequest(
            request.Binding,
            request.Selections));
        if (evaluation.Result.Value is not { } preview
            || evaluation.Workspace is not { } workspace
            || evaluation.Draft is not { } draft)
            return new(evaluation.Result.Outcome, null, evaluation.Result.Blockers);
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationMagicResonanceBlockers.PreviewDigestMismatch);
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
            return new(CharacterCreationFoundationOutcomes.Blocked, null, preview.Blockers);
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomic)
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Blocked,
                CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);

        draft = draft with
        {
            LastIdempotencyKeyDigest = keyDigest,
            LastPreviewDigest = preview.PreviewDigest,
            LastCommandDigest = commandDigest,
            DraftDigest = string.Empty
        };
        draft = draft with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(draft) };
        long nextContentRevision;
        try { nextContentRevision = checked(workspace.ContentRevision + 1); }
        catch (OverflowException)
        {
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationMagicResonanceBlockers.DraftConflict);
        }

        var receipt = new CharacterCreationMagicResonanceReceipt(
            CharacterCreationMagicResonanceSchemas.ReceiptV1,
            workspace.Id,
            workspace.ContentRevision,
            nextContentRevision,
            nextContentRevision,
            draft.DraftRevision,
            draft.DraftDigest,
            preview.PreviewDigest,
            keyDigest,
            commandDigest,
            ledger is { Count: > 0 }
                ? ledger[^1].ReceiptDigest
                : CharacterCreationMagicResonanceDigest.ReceiptLedgerRootDigest,
            draft.AuthorityDigest,
            draft.SourceInputsDigest,
            draft.CustomDataInputsDigest,
            draft.GmPolicyDigest,
            draft.RuntimeDigest,
            draft.TalentKind,
            preview.AdeptPowerPointBudget.Remaining,
            DecimalToInt(preview.SpellBudget.Remaining),
            DecimalToInt(preview.ComplexFormBudget.Remaining),
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationMagicResonanceDigest.ComputeReceipt(receipt)
        };
        CharacterCreationMagicResonanceReceipt[] replacementLedger = [.. ledger ?? [], receipt];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationMagicResonanceDraft = draft,
                    CharacterCreationMagicResonanceReceipts = replacementLedger
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
                IReadOnlyList<CharacterCreationMagicResonanceReceipt>? racedLedger = racedRead.Value?
                    .Document.AuxiliaryState.CharacterCreationMagicResonanceReceipts;
                if (racedRead.Success
                    && racedRead.Value is { } racedWorkspace
                    && CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                        racedLedger, racedWorkspace.Id, racedWorkspace.ContentRevision))
                {
                    CharacterCreationMagicResonanceReceipt? racedReplay = racedLedger?.SingleOrDefault(candidate =>
                        CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                            candidate.IdempotencyKeyDigest, keyDigest));
                    if (racedReplay is not null)
                        return CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                                racedReplay.CommandDigest, commandDigest)
                            ? new(CharacterCreationFoundationOutcomes.Success, racedReplay, [])
                            : Blocked<CharacterCreationMagicResonanceReceipt>(
                                CharacterCreationFoundationOutcomes.Conflict,
                                CharacterCreationMagicResonanceBlockers.IdempotencyConflict);
                }
            }
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationFoundationOutcomes.Conflict
                    : CharacterCreationFoundationOutcomes.Invalid,
                mutation.Outcome == WorkspaceOperationOutcome.Conflict
                    ? CharacterCreationMagicResonanceBlockers.DraftConflict
                    : CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);
        }
        if (entry.ContentRevision != receipt.ContentRevision || entry.SavedRevision != receipt.SavedRevision)
            return Blocked<CharacterCreationMagicResonanceReceipt>(
                CharacterCreationFoundationOutcomes.Invalid,
                CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);
        return new(CharacterCreationFoundationOutcomes.Success, receipt, []);
    }

    private PreviewEvaluation Evaluate(CharacterCreationMagicResonancePreviewRequest request)
    {
        WorkspaceStoreReadResult read = _store.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not { } workspace)
            return new(Blocked<CharacterCreationMagicResonancePreview>(
                CharacterCreationFoundationOutcomes.Missing,
                CharacterCreationMagicResonanceBlockers.WorkspaceUnavailable), null, null);
        if (workspace.ContentRevision != request.Binding.ContentRevision
            || workspace.SavedRevision != request.Binding.SavedRevision)
            return new(Blocked<CharacterCreationMagicResonancePreview>(
                CharacterCreationFoundationOutcomes.Conflict,
                CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision), null, null);

        CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> stateResult = BuildState(workspace);
        if (stateResult.Value is not { } state
            || state.PrerequisiteDraft is not { } prerequisite
            || state.AttributesDraft is not { } attributes
            || state.SelectedTalent is not { } talent)
            return new(new(
                stateResult.Outcome == CharacterCreationFoundationOutcomes.Success
                    ? CharacterCreationFoundationOutcomes.Blocked
                    : stateResult.Outcome,
                null,
                stateResult.Blockers), null, null);
        string? mismatch = CompareBinding(state.Binding, request.Binding);
        if (mismatch is not null)
            return new(Blocked<CharacterCreationMagicResonancePreview>(
                CharacterCreationFoundationOutcomes.Conflict, mismatch), null, null);

        var blockers = new List<string>(state.Blockers);
        SelectionEvaluation projected = EvaluateSelections(state.Authority, talent, request.Selections, blockers);
        CharacterCreationMagicResonanceDraft? draft = blockers.Count == 0
            ? BuildDraft(
                workspace,
                prerequisite,
                attributes,
                state.Authority,
                talent,
                projected,
                blockers)
            : null;
        if (draft is not null && state.PendingDraft is { } current)
        {
            if (current.DraftRevision == long.MaxValue)
                blockers.Add(CharacterCreationMagicResonanceBlockers.DraftConflict);
            else if (CharacterCreationMagicResonanceDraftIntegrity.HasSameLogicalPayload(current, draft))
                blockers.Add(CharacterCreationMagicResonanceBlockers.DraftDuplicate);
        }
        string[] normalized = Normalize(blockers);
        var preview = new CharacterCreationMagicResonancePreview(
            CharacterCreationMagicResonanceSchemas.PreviewV1,
            state.Binding,
            talent,
            projected.Selections,
            projected.TraditionBudget,
            projected.StreamBudget,
            projected.PowerBudget,
            projected.SpellBudget,
            projected.FormBudget,
            projected.SourceAnchorIds,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0 && draft is not null,
            PreviewDigest: string.Empty);
        preview = preview with { FinalizationContribution = draft?.FinalizationContribution };
        preview = preview with
        {
            PreviewDigest = CharacterCreationMagicResonanceDigest.Compute(
                preview with { PreviewDigest = string.Empty })
        };
        return new(new(
            normalized.Length == 0
                ? CharacterCreationFoundationOutcomes.Success
                : CharacterCreationFoundationOutcomes.Blocked,
            preview,
            normalized), workspace, draft);
    }

    private CharacterCreationFoundationResult<CharacterCreationMagicResonanceState> BuildState(
        WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            { SupportsWorkspaceAuxiliaryStateAtomicCommit: true })
            blockers.Add(CharacterCreationMagicResonanceBlockers.PersistenceAuthorityRequired);

        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        ICharacterSourceDataContext? context = _resolver.TryCreateContext(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority = CharacterCreationPrerequisiteAuthority.Unavailable;
        CharacterCreationMagicResonanceAuthority authority = CharacterCreationMagicResonanceAuthority.Unavailable;
        bool authorityReady = context is not null
            && context.TryResolveCreationPrerequisiteAuthority(out prerequisiteAuthority)
            && context.TryResolveCreationMagicResonanceAuthority(out authority)
            && CharacterCreationMagicResonanceDraftIntegrity.IsValidAuthority(authority);
        if (!authorityReady)
            blockers.Add(CharacterCreationMagicResonanceBlockers.AuthorityUnavailable);
        else if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                     authority.PrerequisiteAuthorityDigest,
                     prerequisiteAuthority.AuthorityDigest))
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteSourceDrift);

        CharacterCreationPrerequisiteDraft? prerequisite = workspace.Document.AuxiliaryState
            .CharacterCreationPrerequisiteDraft;
        if (prerequisite is null)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteDraftRequired);
        }
        else if (!CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                     prerequisite,
                     workspace.Id,
                     workspace.ContentRevision,
                     rawDigest,
                     prerequisiteAuthority))
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.PrerequisiteDraftInvalid);
            prerequisite = null;
        }

        CharacterCreationAttributesState? attributeState =
            new CharacterCreationAttributesService(_store, _resolver)
                .Load(new CharacterCreationAttributesLoadRequest(workspace.Id)).Value;
        CharacterCreationAttributesDraft? attributes = attributeState?.PendingDraft;
        if (attributes is null)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.AttributesDraftRequired);
        }
        else if (!attributeState!.CanEdit || attributeState.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid);
            attributes = null;
        }

        CharacterCreationMagicResonanceTalentOption? talent = null;
        if (prerequisite is not null)
            talent = ResolveSelectedTalent(prerequisite, authority, blockers);

        CharacterCreationMagicResonanceDraft? pending = workspace.Document.AuxiliaryState
            .CharacterCreationMagicResonanceDraft;
        IReadOnlyList<CharacterCreationMagicResonanceReceipt>? receipts = workspace.Document
            .AuxiliaryState.CharacterCreationMagicResonanceReceipts;
        if (!CharacterCreationMagicResonanceDraftIntegrity.IsValidReceiptLedger(
                receipts, workspace.Id, workspace.ContentRevision))
            blockers.Add(CharacterCreationMagicResonanceBlockers.ReceiptLedgerInvalid);
        if (pending is not null
            && (prerequisite is null
                || attributes is null
                || !CharacterCreationMagicResonanceDraftIntegrity.IsStructurallyValidPending(
                    pending, workspace.Id, workspace.ContentRevision, rawDigest,
                    prerequisite, attributes, authority)))
            blockers.Add(CharacterCreationMagicResonanceBlockers.DraftInvalid);

        SelectionEvaluation projected = EmptySelectionEvaluation(talent);
        if (talent is not null)
        {
            var pendingBlockers = new List<string>();
            projected = EvaluateSelections(authority, talent, pending?.Selections, pendingBlockers);
            if (pending is not null
                && (pendingBlockers.Count != 0
                    || pending.TalentIdentity != talent.Identity
                    || !string.Equals(pending.TalentKind, talent.Kind, StringComparison.Ordinal)
                    || pending.AssignedMagic != talent.Magic
                    || pending.AssignedResonance != talent.Resonance
                    || pending.AssignedDepth != talent.Depth
                    || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                        CharacterCreationMagicResonanceDigest.Compute(pending.Selections),
                        CharacterCreationMagicResonanceDigest.Compute(projected.Selections))
                    || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                        CharacterCreationMagicResonanceDigest.Compute(new[]
                        {
                            pending.TraditionBudget, pending.StreamBudget, pending.AdeptPowerPointBudget,
                            pending.SpellBudget, pending.ComplexFormBudget
                        }),
                        CharacterCreationMagicResonanceDigest.Compute(new[]
                        {
                            projected.TraditionBudget, projected.StreamBudget, projected.PowerBudget,
                            projected.SpellBudget, projected.FormBudget
                        }))
                    || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                        CharacterCreationMagicResonanceDigest.Compute(pending.SourceAnchorIds),
                        CharacterCreationMagicResonanceDigest.Compute(projected.SourceAnchorIds))))
                blockers.Add(CharacterCreationMagicResonanceBlockers.DraftInvalid);
        }

        var binding = new CharacterCreationMagicResonanceBinding(
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
            authority.SourceInputsDigest,
            authority.CustomDataInputsDigest,
            authority.GmPolicyDigest,
            authority.RuntimeDigest);
        string[] normalized = Normalize(blockers.Concat(authority.Blockers));
        var state = new CharacterCreationMagicResonanceState(
            CharacterCreationMagicResonanceSchemas.SnapshotV1,
            binding,
            authority,
            prerequisite,
            attributes,
            talent,
            pending,
            projected.TraditionBudget,
            projected.StreamBudget,
            projected.PowerBudget,
            projected.SpellBudget,
            projected.FormBudget,
            normalized,
            CanEdit: prerequisite is not null
                     && attributes is not null
                     && talent is not null
                     && normalized.Length == 0,
            SnapshotDigest: string.Empty);
        state = state with
        {
            SnapshotDigest = CharacterCreationMagicResonanceDigest.Compute(
                state with { SnapshotDigest = string.Empty })
        };
        return new(CharacterCreationFoundationOutcomes.Success, state, normalized);
    }

    private static CharacterCreationMagicResonanceTalentOption? ResolveSelectedTalent(
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationMagicResonanceAuthority authority,
        ICollection<string> blockers)
    {
        CharacterCreationPriorityAssignment? assignment = prerequisite.Assignments.SingleOrDefault(item =>
            string.Equals(item.CategoryId, CharacterCreationPriorityCategoryIds.Talent, StringComparison.Ordinal));
        CharacterCreationPriorityTalentSelection? selected = prerequisite.TalentSelection;
        CharacterCreationMagicResonanceTalentOption[] matches = assignment is null || selected is null
            ? []
            : authority.Talents.Where(item =>
                    string.Equals(item.Identity.PrioritySourceId, assignment.SourceId, StringComparison.Ordinal)
                    && string.Equals(item.Identity.TalentSelectionId, selected.SelectionId, StringComparison.Ordinal)
                    && string.Equals(item.Identity.TalentValue, selected.Value, StringComparison.Ordinal)
                    && string.Equals(item.Rank, assignment.Rank, StringComparison.Ordinal)
                    && CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                        item.SourceNodeDigest, selected.PriorityChildNodeDigest))
                .Take(2)
                .ToArray();
        if (matches.Length != 1)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.PriorityAssignmentMismatch);
            return null;
        }
        CharacterCreationMagicResonanceTalentOption talent = matches[0];
        if (!talent.IsEnabled || talent.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.TalentUnsupported);
            AddAll(blockers, talent.Blockers);
        }
        CharacterCreationMagicResonanceMetatypeCapability[] metatypes = authority.Metatypes
            .Where(item => string.Equals(
                item.MetatypeSourceId,
                prerequisite.HeritageSelection?.MetatypeSourceId,
                StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (metatypes.Length != 1)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.MetatypePrerequisiteUnresolved);
            return talent;
        }
        CharacterCreationMagicResonanceMetatypeCapability metatype = metatypes[0];
        bool requiredName = talent.RequiredMetatypeNames.Count == 0
            || talent.RequiredMetatypeNames.Contains(metatype.MetatypeName, StringComparer.Ordinal);
        bool requiredCategory = talent.RequiredMetatypeCategories.Count == 0
            || talent.RequiredMetatypeCategories.Contains(metatype.MetatypeCategory, StringComparer.Ordinal);
        bool forbidden = talent.ForbiddenMetatypeNames.Contains(metatype.MetatypeName, StringComparer.Ordinal);
        if (!requiredName || !requiredCategory || forbidden)
            blockers.Add(CharacterCreationMagicResonanceBlockers.MetatypeForbidden);
        return talent;
    }

    private static SelectionEvaluation EvaluateSelections(
        CharacterCreationMagicResonanceAuthority authority,
        CharacterCreationMagicResonanceTalentOption talent,
        CharacterCreationMagicResonanceSelections? requested,
        ICollection<string> blockers)
    {
        requested ??= new(null, null, [], [], []);
        CharacterCreationMagicResonanceSelections selections = NormalizeSelections(requested);

        CharacterCreationMagicResonanceCatalogOption? tradition = ResolveSingle(
            selections.Tradition,
            authority.Traditions,
            CharacterCreationMagicResonanceKinds.Tradition,
            CharacterCreationMagicResonanceBlockers.TraditionInvalid,
            blockers);
        if (talent.RequiresTradition && tradition is null)
            blockers.Add(CharacterCreationMagicResonanceBlockers.TraditionRequired);
        if (!talent.RequiresTradition && selections.Tradition is not null)
            blockers.Add(CharacterCreationMagicResonanceBlockers.TraditionInvalid);

        CharacterCreationMagicResonanceCatalogOption? stream = ResolveSingle(
            selections.Stream,
            authority.Streams,
            CharacterCreationMagicResonanceKinds.Stream,
            CharacterCreationMagicResonanceBlockers.StreamInvalid,
            blockers);
        if (talent.RequiresStream && stream is null)
            blockers.Add(CharacterCreationMagicResonanceBlockers.StreamRequired);
        if (!talent.RequiresStream && selections.Stream is not null)
            blockers.Add(CharacterCreationMagicResonanceBlockers.StreamInvalid);

        decimal powerUsed = 0m;
        var seenPowers = new HashSet<CharacterCreationMagicResonanceOptionIdentity>();
        foreach (CharacterCreationAdeptPowerAllocation allocation in selections.AdeptPowers)
        {
            if (!seenPowers.Add(allocation.Identity))
            {
                blockers.Add(CharacterCreationMagicResonanceBlockers.OptionDuplicate);
                continue;
            }
            CharacterCreationMagicResonanceCatalogOption? source = ResolveSingle(
                allocation.Identity,
                authority.AdeptPowers,
                CharacterCreationMagicResonanceKinds.AdeptPower,
                CharacterCreationMagicResonanceBlockers.OptionInvalid,
                blockers);
            if (!talent.AllowsAdeptPowers)
                blockers.Add(CharacterCreationMagicResonanceBlockers.PowerSelectionNotAllowed);
            if (source is null || allocation.Levels < 1 || allocation.Levels > source.MaximumLevels)
            {
                blockers.Add(CharacterCreationMagicResonanceBlockers.OptionInvalid);
                continue;
            }
            try { powerUsed = checked(powerUsed + source.PointCost * allocation.Levels); }
            catch (OverflowException)
            {
                blockers.Add(CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded);
                powerUsed = decimal.MaxValue;
            }
        }
        if (talent.Kind == CharacterCreationMagicResonanceKinds.MysticAdept
            && selections.AdeptPowers.Count != 0)
            blockers.Add(CharacterCreationMagicResonanceBlockers.PowerBudgetUnsupported);

        int spellUsed = ValidateFlatSelections(
            selections.Spells,
            authority.Spells,
            CharacterCreationMagicResonanceKinds.Spell,
            talent.AllowsSpells,
            CharacterCreationMagicResonanceBlockers.SpellSelectionNotAllowed,
            blockers);
        int formUsed = ValidateFlatSelections(
            selections.ComplexForms,
            authority.ComplexForms,
            CharacterCreationMagicResonanceKinds.ComplexForm,
            talent.AllowsComplexForms,
            CharacterCreationMagicResonanceBlockers.ComplexFormSelectionNotAllowed,
            blockers);

        CharacterCreationMagicResonanceBudgetState traditionBudget = Budget(
            CharacterCreationMagicResonanceKinds.Tradition,
            talent.RequiresTradition ? 1m : 0m,
            tradition is null ? 0m : 1m,
            CharacterCreationMagicResonanceBlockers.TraditionRequired);
        CharacterCreationMagicResonanceBudgetState streamBudget = Budget(
            CharacterCreationMagicResonanceKinds.Stream,
            talent.RequiresStream ? 1m : 0m,
            stream is null ? 0m : 1m,
            CharacterCreationMagicResonanceBlockers.StreamRequired);
        CharacterCreationMagicResonanceBudgetState powerBudget = Budget(
            CharacterCreationMagicResonanceKinds.AdeptPower,
            talent.AdeptPowerPointBudget,
            powerUsed,
            CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded);
        CharacterCreationMagicResonanceBudgetState spellBudget = Budget(
            CharacterCreationMagicResonanceKinds.Spell,
            talent.SpellBudget,
            spellUsed,
            CharacterCreationMagicResonanceBlockers.SpellBudgetExceeded);
        CharacterCreationMagicResonanceBudgetState formBudget = Budget(
            CharacterCreationMagicResonanceKinds.ComplexForm,
            talent.ComplexFormBudget,
            formUsed,
            CharacterCreationMagicResonanceBlockers.ComplexFormBudgetExceeded);
        if (talent.AllowsAdeptPowers
            && talent.AdeptPowerPointBudget > 0m
            && powerBudget.Remaining != 0m)
            blockers.Add(CharacterCreationMagicResonanceBlockers.PowerBudgetIncomplete);
        if (talent.AllowsSpells && spellBudget.Remaining != 0m)
            blockers.Add(CharacterCreationMagicResonanceBlockers.SpellBudgetIncomplete);
        if (talent.AllowsComplexForms && formBudget.Remaining != 0m)
            blockers.Add(CharacterCreationMagicResonanceBlockers.ComplexFormBudgetIncomplete);
        AddAll(blockers, traditionBudget.Blockers);
        AddAll(blockers, streamBudget.Blockers);
        AddAll(blockers, powerBudget.Blockers);
        AddAll(blockers, spellBudget.Blockers);
        AddAll(blockers, formBudget.Blockers);

        string[] anchors = new[] { tradition, stream }
            .Where(item => item is not null)
            .SelectMany(item => item!.SourceAnchorIds)
            .Concat(selections.AdeptPowers.SelectMany(allocation => authority.AdeptPowers
                .Where(item => item.Identity == allocation.Identity)
                .SelectMany(item => item.SourceAnchorIds)))
            .Concat(selections.Spells.SelectMany(identity => authority.Spells
                .Where(item => item.Identity == identity)
                .SelectMany(item => item.SourceAnchorIds)))
            .Concat(selections.ComplexForms.SelectMany(identity => authority.ComplexForms
                .Where(item => item.Identity == identity)
                .SelectMany(item => item.SourceAnchorIds)))
            .Concat(talent.SourceAnchorIds)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return new(selections, traditionBudget, streamBudget, powerBudget, spellBudget, formBudget, anchors);
    }

    private static CharacterCreationMagicResonanceCatalogOption? ResolveSingle(
        CharacterCreationMagicResonanceOptionIdentity? identity,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog,
        string expectedKind,
        string invalidBlocker,
        ICollection<string> blockers)
    {
        if (identity is null)
            return null;
        CharacterCreationMagicResonanceCatalogOption[] matches = catalog.Where(item =>
                item.Identity == identity
                && string.Equals(identity.Kind, expectedKind, StringComparison.Ordinal))
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            blockers.Add(invalidBlocker);
            return null;
        }
        CharacterCreationMagicResonanceCatalogOption source = matches[0];
        if (!source.IsEnabled || source.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationMagicResonanceBlockers.OptionDisabled);
            AddAll(blockers, source.Blockers);
        }
        return source;
    }

    private static int ValidateFlatSelections(
        IReadOnlyList<CharacterCreationMagicResonanceOptionIdentity> selections,
        IReadOnlyList<CharacterCreationMagicResonanceCatalogOption> catalog,
        string expectedKind,
        bool allowed,
        string notAllowedBlocker,
        ICollection<string> blockers)
    {
        if (!allowed && selections.Count != 0)
            blockers.Add(notAllowedBlocker);
        if (selections.Distinct().Count() != selections.Count)
            blockers.Add(CharacterCreationMagicResonanceBlockers.OptionDuplicate);
        foreach (CharacterCreationMagicResonanceOptionIdentity identity in selections.Distinct())
            _ = ResolveSingle(identity, catalog, expectedKind,
                CharacterCreationMagicResonanceBlockers.OptionInvalid, blockers);
        return selections.Count;
    }

    private static CharacterCreationMagicResonanceDraft? BuildDraft(
        WorkspaceStoredDocument workspace,
        CharacterCreationPrerequisiteDraft prerequisite,
        CharacterCreationAttributesDraft attributes,
        CharacterCreationMagicResonanceAuthority authority,
        CharacterCreationMagicResonanceTalentOption talent,
        SelectionEvaluation evaluation,
        ICollection<string> blockers)
    {
        CharacterCreationMagicResonanceDraft? current = workspace.Document.AuxiliaryState
            .CharacterCreationMagicResonanceDraft;
        long next = current is null || current.DraftRevision == long.MaxValue
            ? current is null ? 1 : long.MaxValue
            : current.DraftRevision + 1;
        string rawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        if (!CharacterCreationMagicResonanceFinalizationRules.TryCreate(
                rawCharacterXmlDigest,
                prerequisite.DraftRevision,
                prerequisite.DraftDigest,
                attributes.DraftRevision,
                attributes.DraftDigest,
                authority,
                talent,
                evaluation.Selections,
                out CharacterCreationMagicResonanceFinalizationContribution contribution,
                out string[] contributionBlockers))
        {
            AddAll(blockers, contributionBlockers);
            return null;
        }
        var draft = new CharacterCreationMagicResonanceDraft(
            CharacterCreationMagicResonanceSchemas.DraftV1,
            workspace.Id,
            next,
            workspace.ContentRevision,
            rawCharacterXmlDigest,
            prerequisite.DraftRevision,
            prerequisite.DraftDigest,
            prerequisite.AuthorityDigest,
            attributes.DraftRevision,
            attributes.DraftDigest,
            authority.AuthorityDigest,
            authority.SourceInputsDigest,
            authority.CustomDataInputsDigest,
            authority.GmPolicyDigest,
            authority.RuntimeDigest,
            talent.Identity,
            talent.Kind,
            talent.Magic,
            talent.Resonance,
            talent.Depth,
            evaluation.Selections,
            evaluation.TraditionBudget,
            evaluation.StreamBudget,
            evaluation.PowerBudget,
            evaluation.SpellBudget,
            evaluation.FormBudget,
            evaluation.SourceAnchorIds,
            CharacterEffectsApplied: false,
            LastIdempotencyKeyDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("pending"),
            LastPreviewDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("pending"),
            LastCommandDigest: CharacterCreationMagicResonanceDigest.ComputeUtf8("pending"),
            DraftDigest: string.Empty);
        draft = draft with { FinalizationContribution = contribution };
        return draft with { DraftDigest = CharacterCreationMagicResonanceDraftIntegrity.ComputeDigest(draft) };
    }

    private static string ComputeCommandDigest(CharacterCreationMagicResonanceConfirmRequest request) =>
        CharacterCreationMagicResonanceDigest.Compute(new
        {
            Schema = "chummer.character_creation_magic_resonance_command.v1",
            request.Binding,
            Selections = NormalizeSelections(request.Selections),
            request.PreviewDigest,
            ExplicitlyConfirmed = true
        });

    private static string? CompareBinding(
        CharacterCreationMagicResonanceBinding current,
        CharacterCreationMagicResonanceBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationMagicResonanceBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.RawCharacterXmlDigest, requested.RawCharacterXmlDigest))
            return CharacterCreationMagicResonanceBlockers.StaleRawCharacterXmlDigest;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.AuxiliaryStateDigest, requested.AuxiliaryStateDigest))
            return CharacterCreationMagicResonanceBlockers.DraftConflict;
        if (current.PrerequisiteDraftRevision != requested.PrerequisiteDraftRevision
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.PrerequisiteDraftDigest, requested.PrerequisiteDraftDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.PrerequisiteAuthorityDigest, requested.PrerequisiteAuthorityDigest))
            return CharacterCreationMagicResonanceBlockers.PrerequisiteSourceDrift;
        if (current.AttributesDraftRevision != requested.AttributesDraftRevision
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.AttributesDraftDigest, requested.AttributesDraftDigest))
            return CharacterCreationMagicResonanceBlockers.AttributesDraftInvalid;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.AuthorityDigest, requested.AuthorityDigest)
            || !CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.SourceInputsDigest, requested.SourceInputsDigest))
            return CharacterCreationMagicResonanceBlockers.SourceDrift;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.CustomDataInputsDigest, requested.CustomDataInputsDigest))
            return CharacterCreationMagicResonanceBlockers.CustomDataDrift;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.GmPolicyDigest, requested.GmPolicyDigest))
            return CharacterCreationMagicResonanceBlockers.GmPolicyDrift;
        if (!CharacterCreationMagicResonanceDigest.EqualsFixedTime(
                current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationMagicResonanceBlockers.RuntimeDrift;
        return null;
    }

    private static CharacterCreationMagicResonanceSelections NormalizeSelections(
        CharacterCreationMagicResonanceSelections? selections)
    {
        selections ??= new(null, null, [], [], []);
        return new(
            selections.Tradition,
            selections.Stream,
            (selections.AdeptPowers ?? []).OrderBy(item => item.Identity.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.Identity.SourceId, StringComparer.Ordinal)
                .ThenBy(item => item.Levels).ToArray(),
            (selections.Spells ?? []).OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray(),
            (selections.ComplexForms ?? []).OrderBy(item => item.Kind, StringComparer.Ordinal)
                .ThenBy(item => item.SourceId, StringComparer.Ordinal).ToArray());
    }

    private static CharacterCreationMagicResonanceBudgetState Budget(
        string kind,
        decimal total,
        decimal used,
        string blocker)
    {
        bool valid = total >= 0m && used >= 0m && used <= total;
        return new(kind, Math.Max(0m, total), Math.Max(0m, used),
            valid ? total - used : 0m, valid ? [] : [blocker]);
    }

    private static SelectionEvaluation EmptySelectionEvaluation(
        CharacterCreationMagicResonanceTalentOption? talent) =>
        new(
            new(null, null, [], [], []),
            Budget(CharacterCreationMagicResonanceKinds.Tradition,
                talent?.RequiresTradition == true ? 1m : 0m, 0m,
                CharacterCreationMagicResonanceBlockers.TraditionRequired),
            Budget(CharacterCreationMagicResonanceKinds.Stream,
                talent?.RequiresStream == true ? 1m : 0m, 0m,
                CharacterCreationMagicResonanceBlockers.StreamRequired),
            Budget(CharacterCreationMagicResonanceKinds.AdeptPower,
                talent?.AdeptPowerPointBudget ?? 0m, 0m,
                CharacterCreationMagicResonanceBlockers.PowerBudgetExceeded),
            Budget(CharacterCreationMagicResonanceKinds.Spell,
                talent?.SpellBudget ?? 0, 0m,
                CharacterCreationMagicResonanceBlockers.SpellBudgetExceeded),
            Budget(CharacterCreationMagicResonanceKinds.ComplexForm,
                talent?.ComplexFormBudget ?? 0, 0m,
                CharacterCreationMagicResonanceBlockers.ComplexFormBudgetExceeded),
            talent?.SourceAnchorIds ?? []);

    private static int DecimalToInt(decimal value) =>
        value is >= 0m and <= int.MaxValue ? decimal.ToInt32(value) : 0;

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(item => !string.IsNullOrWhiteSpace(item))
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
        CharacterCreationFoundationResult<CharacterCreationMagicResonancePreview> Result,
        WorkspaceStoredDocument? Workspace,
        CharacterCreationMagicResonanceDraft? Draft);

    private sealed record SelectionEvaluation(
        CharacterCreationMagicResonanceSelections Selections,
        CharacterCreationMagicResonanceBudgetState TraditionBudget,
        CharacterCreationMagicResonanceBudgetState StreamBudget,
        CharacterCreationMagicResonanceBudgetState PowerBudget,
        CharacterCreationMagicResonanceBudgetState SpellBudget,
        CharacterCreationMagicResonanceBudgetState FormBudget,
        IReadOnlyList<string> SourceAnchorIds);
}
