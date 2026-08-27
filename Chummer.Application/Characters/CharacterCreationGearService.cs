using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Governed SR5 creation Gear basket. Catalog facts come only from the effective source
/// profile. Confirmation persists a finalization contribution and leaves character XML
/// byte-identical so all creation lanes can be finalized once, atomically, later.
/// </summary>
public sealed class CharacterCreationGearService : ICharacterCreationGearService
{
    private const int MaximumIdempotencyKeyLength = 200;

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterSourceDataResolver _sourceData;

    public CharacterCreationGearService(
        IWorkspaceStore workspaceStore,
        ICharacterSourceDataResolver sourceData)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
    }

    public CharacterCreationGearResult<CharacterCreationGearState> Load(
        CharacterCreationGearLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationGearState>(read);

        AuthorityContext context = BuildContext(workspace);
        var candidate = new CharacterCreationGearState(
            CharacterCreationGearSchemas.StateV1,
            CharacterCreationWizardStepIds.Resources,
            context.Binding,
            context.Authority,
            context.ResourcesDraft,
            context.PendingDraft,
            context.Budget,
            context.Blockers,
            CanEdit: context.Blockers.Count == 0,
            SnapshotDigest: string.Empty);
        CharacterCreationGearState state = candidate with
        {
            SnapshotDigest = CharacterCreationGearRules.ComputeStateDigest(candidate)
        };
        return new CharacterCreationGearResult<CharacterCreationGearState>(
            CharacterCreationGearOutcomes.Available,
            state,
            context.Blockers);
    }

    public CharacterCreationGearResult<CharacterCreationGearPreview> Preview(
        CharacterCreationGearPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Basket is null)
            return Blocked<CharacterCreationGearPreview>(
                CharacterCreationGearOutcomes.Invalid,
                CharacterCreationGearBlockers.InvalidBasket);
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationGearResult<CharacterCreationGearReceipt> Confirm(
        CharacterCreationGearConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Basket is null)
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Invalid,
                CharacterCreationGearBlockers.InvalidBasket);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Invalid,
                CharacterCreationGearBlockers.IdempotencyKeyInvalid);

        string keyDigest = CharacterCreationGearRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-gear.idempotency.v1\0" + key);
        string commandDigest = CharacterCreationGearRules.ComputeCommandDigest(request);
        WorkspaceStoreReadResult initialRead = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!initialRead.Success || initialRead.Value is not WorkspaceStoredDocument initial)
            return ReadFailure<CharacterCreationGearReceipt>(initialRead);
        CharacterCreationGearResult<CharacterCreationGearReceipt>? replay = ResolveReplay(
            initial,
            keyDigest,
            commandDigest);
        if (replay is not null)
            return replay;
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Blocked,
                CharacterCreationGearBlockers.ExplicitConfirmationRequired);

        PreviewEvaluation evaluation = EvaluatePreview(new CharacterCreationGearPreviewRequest(
            request.Binding,
            request.Basket));
        if (evaluation.Result.Value is not CharacterCreationGearPreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace)
        {
            return new CharacterCreationGearResult<CharacterCreationGearReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!CharacterCreationGearRules.DigestsEqual(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Conflict,
                CharacterCreationGearBlockers.PreviewDigestMismatch);
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
            return new CharacterCreationGearResult<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Blocked,
                null,
                preview.Blockers);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore
            || workspace.ContentRevision == long.MaxValue)
        {
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Unavailable,
                CharacterCreationGearBlockers.PersistenceAuthorityRequired);
        }

        long nextRevision = workspace.ContentRevision + 1;
        long draftRevision = (workspace.Document.AuxiliaryState.CharacterCreationGearDraft
            ?.DraftRevision ?? 0) + 1;
        CharacterCreationGearDraft draftCandidate = preview.After with
        {
            DraftRevision = draftRevision,
            BaseContentRevision = workspace.ContentRevision,
            LastIdempotencyKeyDigest = keyDigest,
            LastPreviewDigest = preview.PreviewDigest,
            LastCommandDigest = commandDigest,
            DraftDigest = string.Empty
        };
        CharacterCreationGearDraft draft = draftCandidate with
        {
            DraftDigest = CharacterCreationGearRules.ComputeDraftDigest(draftCandidate)
        };
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry> currentLedger =
            workspace.Document.AuxiliaryState.CharacterCreationGearReceipts ?? [];
        string previousReceiptDigest = currentLedger.Count == 0
            ? CharacterCreationGearRules.ReceiptLedgerRootDigest
            : currentLedger[^1].Receipt.ReceiptDigest;
        string receiptId = "creation-gear-" + commandDigest["sha256:".Length..][..24];
        var receiptCandidate = new CharacterCreationGearReceipt(
            CharacterCreationGearSchemas.ReceiptV1,
            receiptId,
            workspace.Id,
            keyDigest,
            commandDigest,
            workspace.ContentRevision,
            nextRevision,
            workspace.SavedRevision,
            nextRevision,
            draft.BaseRawCharacterXmlDigest,
            draft.ResourcesDraftRevision,
            draft.ResourcesDraftDigest,
            draft.AuthorityDigest,
            draft.SourceDigest,
            draft.RulesDigest,
            draft.RuntimeDigest,
            draft.Lines.Count,
            draft.Budget.BasketCost,
            draft.Budget.RemainingNuyen,
            draft.DraftRevision,
            draft.DraftDigest,
            preview.PreviewDigest,
            previousReceiptDigest,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        CharacterCreationGearReceipt receipt = receiptCandidate with
        {
            ReceiptDigest = CharacterCreationGearRules.ComputeReceiptDigest(receiptCandidate)
        };
        CharacterCreationGearReceiptLedgerEntry[] replacementLedger =
        [
            .. currentLedger,
            new CharacterCreationGearReceiptLedgerEntry(keyDigest, commandDigest, receipt)
        ];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationGearDraft = draft,
                    CharacterCreationGearReceipts = replacementLedger
                }
            }
        };
        WorkspaceStoreMutationResult committed =
            atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                workspace.Id,
                workspace.ContentRevision,
                workspace.Document.AuxiliaryStateDigest,
                replacement);
        if (committed.Success
            && committed.Entry is WorkspaceStoreEntry entry
            && entry.ContentRevision == nextRevision
            && entry.SavedRevision == nextRevision)
        {
            return new CharacterCreationGearResult<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Applied,
                receipt,
                []);
        }
        if (committed.Outcome == WorkspaceOperationOutcome.Conflict)
        {
            WorkspaceStoreReadResult racedRead = _workspaceStore.Get(workspace.Id);
            if (racedRead.Success && racedRead.Value is WorkspaceStoredDocument raced)
            {
                CharacterCreationGearResult<CharacterCreationGearReceipt>? racedReplay =
                    ResolveReplay(raced, keyDigest, commandDigest);
                if (racedReplay is not null)
                    return racedReplay;
            }
        }
        return Blocked<CharacterCreationGearReceipt>(
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationGearOutcomes.Conflict
                : CharacterCreationGearOutcomes.Unavailable,
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationGearBlockers.StaleWorkspaceRevision
                : CharacterCreationGearBlockers.PersistenceAuthorityRequired);
    }

    public CharacterCreationGearResult<CharacterCreationGearReceipt> LookupReceipt(
        CharacterCreationGearReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Invalid,
                CharacterCreationGearBlockers.IdempotencyKeyInvalid);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationGearReceipt>(read);
        CharacterCreationGearDraft? draft =
            workspace.Document.AuxiliaryState.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationGearReceipts ?? [];
        if (!CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                draft,
                ledger))
        {
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Corrupt,
                CharacterCreationGearBlockers.ReceiptLedgerCorrupt);
        }
        string digest = CharacterCreationGearRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-gear.idempotency.v1\0" + key);
        CharacterCreationGearReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            CharacterCreationGearRules.DigestsEqual(entry.IdempotencyKeyDigest, digest));
        return found is null
            ? Blocked<CharacterCreationGearReceipt>(CharacterCreationGearOutcomes.NotFound)
            : new CharacterCreationGearResult<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Available,
                found.Receipt,
                []);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationGearPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return new PreviewEvaluation(ReadFailure<CharacterCreationGearPreview>(read), null);
        AuthorityContext context = BuildContext(workspace);
        string? bindingBlocker = CompareBinding(context.Binding, request.Binding);
        if (bindingBlocker is not null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationGearPreview>(
                    CharacterCreationGearOutcomes.Conflict,
                    bindingBlocker),
                null);
        }

        var blockers = new List<string>(context.Blockers);
        CharacterCreationGearRules.TryProjectBasket(
            request.Basket,
            context.Authority,
            context.ResourcesDraft?.Budget.TotalStartingNuyen ?? 0m,
            out CharacterCreationGearLine[] lines,
            out CharacterCreationGearBudget budgetAfter,
            out string[] projectionBlockers);
        blockers.AddRange(projectionBlockers);
        if (context.PendingDraft is not null
               && context.PendingDraft.Lines.Select(item => item.LineDigest)
                   .SequenceEqual(lines.Select(item => item.LineDigest), StringComparer.Ordinal))
            blockers.Add(CharacterCreationGearBlockers.NoChange);

        IReadOnlyList<string> anchors = lines.SelectMany(item => item.SourceAnchorIds)
            .Concat(CharacterCreationGearSourceAnchors.All)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        var contributionCandidate = new CharacterCreationGearFinalizationContribution(
            CharacterCreationGearSchemas.ContributionV1,
            context.Binding.RawCharacterXmlDigest,
            context.ResourcesDraft?.DraftRevision ?? 0,
            context.ResourcesDraft?.DraftDigest ?? string.Empty,
            lines,
            budgetAfter.BasketCost,
            anchors,
            string.Empty);
        CharacterCreationGearFinalizationContribution contribution = contributionCandidate with
        {
            ContributionDigest = CharacterCreationGearRules.ComputeContributionDigest(
                contributionCandidate)
        };
        var draftCandidate = new CharacterCreationGearDraft(
            CharacterCreationGearSchemas.DraftV1,
            workspace.Id,
            (context.PendingDraft?.DraftRevision ?? 0) + 1,
            workspace.ContentRevision,
            context.Binding.RawCharacterXmlDigest,
            context.ResourcesDraft?.DraftRevision ?? 0,
            context.ResourcesDraft?.DraftDigest ?? string.Empty,
            context.Authority.AuthorityDigest,
            context.Authority.SourceDigest,
            context.Authority.RulesDigest,
            context.Authority.RuntimeDigest,
            lines,
            budgetAfter,
            contribution,
            CharacterEffectsApplied: false,
            LastIdempotencyKeyDigest: string.Empty,
            LastPreviewDigest: string.Empty,
            LastCommandDigest: string.Empty,
            DraftDigest: string.Empty);
        string[] normalized = Normalize(blockers);
        var previewCandidate = new CharacterCreationGearPreview(
            CharacterCreationGearSchemas.PreviewV1,
            CharacterCreationWizardStepIds.Resources,
            context.Binding,
            context.PendingDraft,
            draftCandidate,
            context.Budget,
            budgetAfter,
            contribution,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0,
            PreviewDigest: string.Empty);
        CharacterCreationGearPreview preview = previewCandidate with
        {
            PreviewDigest = CharacterCreationGearRules.ComputePreviewDigest(previewCandidate)
        };
        return new PreviewEvaluation(
            new CharacterCreationGearResult<CharacterCreationGearPreview>(
                normalized.Length == 0
                    ? CharacterCreationGearOutcomes.Available
                    : CharacterCreationGearOutcomes.Blocked,
                preview,
                normalized),
            workspace);
    }

    private AuthorityContext BuildContext(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        XElement root;
        try
        {
            XDocument document = XDocument.Parse(workspace.Document.Content, LoadOptions.PreserveWhitespace);
            root = document.Root ?? throw new XmlException();
            if (!string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
                throw new XmlException();
        }
        catch (XmlException)
        {
            root = new XElement("character");
            blockers.Add(CharacterCreationGearBlockers.CharacterDocumentInvalid);
        }
        if (!string.Equals(workspace.Document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
            blockers.Add(CharacterCreationGearBlockers.RulesetSr5Required);
        if (ParseBool(ReadValue(root, "created")))
            blockers.Add(CharacterCreationGearBlockers.CareerModeRejected);

        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        ICharacterSourceDataContext? source = _sourceData.TryCreateContext(workspace.Document.Content);
        CharacterCreationGearAuthority authority = CharacterCreationGearAuthority.Unavailable;
        CharacterCreationResourcesAuthority resourcesAuthority = CharacterCreationResourcesAuthority.Unavailable;
        if (source is null
            || !source.TryResolveCreationGearAuthority(out authority)
            || !CharacterCreationGearRules.IsValidAuthority(authority))
        {
            authority = CharacterCreationGearAuthority.Unavailable;
            blockers.Add(CharacterCreationGearBlockers.AuthorityUnavailable);
        }
        if (source is null
            || !source.TryResolveCreationResourcesAuthority(out resourcesAuthority)
            || !CharacterCreationResourcesRules.IsValidAuthority(resourcesAuthority))
        {
            blockers.Add(CharacterCreationGearBlockers.ResourcesDraftStale);
        }

        CharacterCreationResourcesDraft? resourcesDraft =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> resourcesReceipts =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [];
        if (resourcesDraft is null)
        {
            blockers.Add(CharacterCreationGearBlockers.ResourcesDraftRequired);
        }
        else if (!CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                     workspace.Id,
                     workspace.ContentRevision,
                     resourcesDraft,
                     resourcesReceipts)
                 || !CharacterCreationGearRules.DigestsEqual(
                     resourcesDraft.BaseRawCharacterXmlDigest,
                     rawDigest)
                 || !CharacterCreationGearRules.DigestsEqual(
                     resourcesDraft.AuthorityDigest,
                     resourcesAuthority.AuthorityDigest)
                 || !resourcesDraft.Budget.IsExact
                 || resourcesDraft.Budget.Blockers.Count != 0)
        {
            blockers.Add(CharacterCreationGearBlockers.ResourcesDraftStale);
        }

        CharacterCreationGearDraft? pending =
            workspace.Document.AuxiliaryState.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationGearReceipts ?? [];
        if (!CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                pending,
                ledger))
        {
            blockers.Add(CharacterCreationGearBlockers.ReceiptLedgerCorrupt);
        }
        else if (ledger.Count >= CharacterCreationGearReceiptLedgerIntegrity.MaximumEntries)
        {
            blockers.Add(CharacterCreationGearBlockers.PersistenceAuthorityRequired);
        }
        if (pending is not null
            && (resourcesDraft is null
                || pending.ResourcesDraftRevision != resourcesDraft.DraftRevision
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.ResourcesDraftDigest,
                    resourcesDraft.DraftDigest)
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.BaseRawCharacterXmlDigest,
                    rawDigest)
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.AuthorityDigest,
                    authority.AuthorityDigest)
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.SourceDigest,
                    authority.SourceDigest)
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.RulesDigest,
                    authority.RulesDigest)
                || !CharacterCreationGearRules.DigestsEqual(
                    pending.RuntimeDigest,
                    authority.RuntimeDigest)))
        {
            blockers.Add(CharacterCreationGearBlockers.StaleResourcesDraft);
        }

        decimal total = resourcesDraft?.Budget.TotalStartingNuyen ?? 0m;
        decimal cost = pending?.Budget.BasketCost ?? 0m;
        CharacterCreationGearBudget budget = new(
            total,
            cost,
            Math.Max(0m, total - cost),
            Math.Max(0m, cost - total),
            blockers.Count == 0,
            Normalize(blockers));
        var binding = new CharacterCreationGearBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            resourcesDraft?.DraftRevision ?? 0,
            resourcesDraft?.DraftDigest ?? string.Empty,
            authority.AuthorityDigest,
            authority.SourceDigest,
            authority.RulesDigest,
            authority.RuntimeDigest);
        return new AuthorityContext(
            resourcesDraft,
            pending,
            authority,
            budget,
            binding,
            Normalize(blockers));
    }

    private CharacterCreationGearResult<CharacterCreationGearReceipt>? ResolveReplay(
        WorkspaceStoredDocument workspace,
        string keyDigest,
        string commandDigest)
    {
        CharacterCreationGearDraft? draft =
            workspace.Document.AuxiliaryState.CharacterCreationGearDraft;
        IReadOnlyList<CharacterCreationGearReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationGearReceipts ?? [];
        if (!CharacterCreationGearReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                draft,
                ledger))
        {
            return Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Corrupt,
                CharacterCreationGearBlockers.ReceiptLedgerCorrupt);
        }
        CharacterCreationGearReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            CharacterCreationGearRules.DigestsEqual(entry.IdempotencyKeyDigest, keyDigest));
        if (found is null)
            return null;
        return CharacterCreationGearRules.DigestsEqual(found.CommandDigest, commandDigest)
            ? new CharacterCreationGearResult<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Replayed,
                found.Receipt,
                [])
            : Blocked<CharacterCreationGearReceipt>(
                CharacterCreationGearOutcomes.Conflict,
                CharacterCreationGearBlockers.IdempotencyConflict);
    }

    private static string? CompareBinding(
        CharacterCreationGearBinding current,
        CharacterCreationGearBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.WorkspaceRevision != requested.WorkspaceRevision
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationGearBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationGearRules.DigestsEqual(
                current.RawCharacterXmlDigest,
                requested.RawCharacterXmlDigest))
            return CharacterCreationGearBlockers.StaleContentDigest;
        if (!CharacterCreationGearRules.DigestsEqual(
                current.AuxiliaryStateDigest,
                requested.AuxiliaryStateDigest))
            return CharacterCreationGearBlockers.StaleAuxiliaryStateDigest;
        if (current.ResourcesDraftRevision != requested.ResourcesDraftRevision
            || !CharacterCreationGearRules.DigestsEqual(
                current.ResourcesDraftDigest,
                requested.ResourcesDraftDigest))
            return CharacterCreationGearBlockers.StaleResourcesDraft;
        if (!CharacterCreationGearRules.DigestsEqual(
                current.AuthorityDigest,
                requested.AuthorityDigest))
            return CharacterCreationGearBlockers.AuthorityUnavailable;
        if (!CharacterCreationGearRules.DigestsEqual(current.SourceDigest, requested.SourceDigest))
            return CharacterCreationGearBlockers.StaleSourceDigest;
        if (!CharacterCreationGearRules.DigestsEqual(current.RulesDigest, requested.RulesDigest))
            return CharacterCreationGearBlockers.StaleRulesDigest;
        if (!CharacterCreationGearRules.DigestsEqual(current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationGearBlockers.StaleRuntimeDigest;
        return null;
    }

    private static bool TryNormalizeIdempotencyKey(string value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumIdempotencyKeyLength
               && string.Equals(value, normalized, StringComparison.Ordinal);
    }

    private static bool ParseBool(string value) =>
        bool.TryParse(value, out bool parsed) && parsed;

    private static string ReadValue(XElement root, string name)
    {
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        return matches.Length == 1 ? matches[0].Value.Trim() : string.Empty;
    }

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();

    private static CharacterCreationGearResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, Normalize(blockers));

    private static CharacterCreationGearResult<T> ReadFailure<T>(WorkspaceStoreReadResult read)
        where T : class => Blocked<T>(
            read.Outcome == WorkspaceOperationOutcome.Missing
                ? CharacterCreationGearOutcomes.NotFound
                : CharacterCreationGearOutcomes.Unavailable,
            read.Outcome == WorkspaceOperationOutcome.Missing
                ? CharacterCreationGearBlockers.WorkspaceUnavailable
                : CharacterCreationGearBlockers.PersistenceAuthorityRequired);

    private sealed record AuthorityContext(
        CharacterCreationResourcesDraft? ResourcesDraft,
        CharacterCreationGearDraft? PendingDraft,
        CharacterCreationGearAuthority Authority,
        CharacterCreationGearBudget Budget,
        CharacterCreationGearBinding Binding,
        IReadOnlyList<string> Blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationGearResult<CharacterCreationGearPreview> Result,
        WorkspaceStoredDocument? Workspace);
}
