using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Governed SR5 Priority/Sum-to-Ten starting-resource allocator. It persists only
/// an authority-bound finalization contribution, keeping all raw XML byte-identical
/// until the single creation finalizer can compose every wizard lane atomically.
/// </summary>
public sealed class CharacterCreationResourcesService : ICharacterCreationResourcesService
{
    private const int MaximumIdempotencyKeyLength = 200;

    private static readonly string[] s_UnsupportedPurchaseContainers =
    [
        "cyberwares", "armors", "weapons", "gears", "vehicles", "drugs",
        "lifestyles", "initiationgrades"
    ];

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterSourceDataResolver _sourceData;

    public CharacterCreationResourcesService(
        IWorkspaceStore workspaceStore,
        ICharacterSourceDataResolver sourceData)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesState> Load(
        CharacterCreationResourcesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationResourcesState>(read);

        AuthorityContext context = BuildContext(workspace);
        string[] blockers = Normalize(context.Blockers.Concat(context.Budget.Blockers));
        var candidate = new CharacterCreationResourcesState(
            CharacterCreationResourcesSchemas.StateV1,
            CharacterCreationWizardStepIds.Resources,
            context.Binding,
            context.Authority,
            context.Prerequisite,
            context.PendingDraft,
            context.Options,
            context.Budget,
            blockers,
            CanEdit: blockers.Length == 0,
            SnapshotDigest: string.Empty);
        CharacterCreationResourcesState state = candidate with
        {
            SnapshotDigest = CharacterCreationResourcesRules.ComputeStateDigest(candidate)
        };
        return new CharacterCreationResourcesResult<CharacterCreationResourcesState>(
            CharacterCreationResourcesOutcomes.Available,
            state,
            blockers);
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Preview(
        CharacterCreationResourcesPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || string.IsNullOrWhiteSpace(request.OptionId))
            return Blocked<CharacterCreationResourcesPreview>(
                CharacterCreationResourcesOutcomes.Invalid,
                CharacterCreationResourcesBlockers.InvalidOption);
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> Confirm(
        CharacterCreationResourcesConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || string.IsNullOrWhiteSpace(request.OptionId))
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Invalid,
                CharacterCreationResourcesBlockers.InvalidOption);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Invalid,
                CharacterCreationResourcesBlockers.IdempotencyKeyInvalid);

        string keyDigest = CharacterCreationResourcesRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-resources.idempotency.v1\0" + key);
        string commandDigest = CharacterCreationResourcesRules.ComputeCommandDigest(request);
        WorkspaceStoreReadResult initialRead = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!initialRead.Success || initialRead.Value is not WorkspaceStoredDocument initial)
            return ReadFailure<CharacterCreationResourcesReceipt>(initialRead);
        CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>? replay = ResolveReplay(
            initial,
            keyDigest,
            commandDigest);
        if (replay is not null)
            return replay;
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Blocked,
                CharacterCreationResourcesBlockers.ExplicitConfirmationRequired);

        PreviewEvaluation evaluation = EvaluatePreview(new CharacterCreationResourcesPreviewRequest(
            request.Binding,
            request.OptionId));
        if (evaluation.Result.Value is not CharacterCreationResourcesPreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace)
        {
            return new CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!CharacterCreationResourcesRules.DigestsEqual(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Conflict,
                CharacterCreationResourcesBlockers.PreviewDigestMismatch);
        if (!preview.CanConfirm || preview.SelectedOption is null || preview.Blockers.Count != 0)
            return new CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Blocked,
                null,
                preview.Blockers);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore
            || workspace.ContentRevision == long.MaxValue)
        {
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Unavailable,
                CharacterCreationResourcesBlockers.PersistenceAuthorityRequired);
        }

        long nextRevision = workspace.ContentRevision + 1;
        long draftRevision = (workspace.Document.AuxiliaryState.CharacterCreationResourcesDraft
            ?.DraftRevision ?? 0) + 1;
        CharacterCreationResourcesDraft draftCandidate = preview.After with
        {
            DraftRevision = draftRevision,
            BaseContentRevision = workspace.ContentRevision,
            LastIdempotencyKeyDigest = keyDigest,
            LastPreviewDigest = preview.PreviewDigest,
            LastCommandDigest = commandDigest,
            DraftDigest = string.Empty
        };
        CharacterCreationResourcesDraft draft = draftCandidate with
        {
            DraftDigest = CharacterCreationResourcesRules.ComputeDraftDigest(draftCandidate)
        };
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> existing =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [];
        string previousReceiptDigest = existing.Count == 0
            ? CharacterCreationResourcesRules.ReceiptLedgerRootDigest
            : existing[^1].Receipt.ReceiptDigest;
        string receiptId = "creation-resources-" + commandDigest["sha256:".Length..][..24];
        var receiptCandidate = new CharacterCreationResourcesReceipt(
            CharacterCreationResourcesSchemas.ReceiptV1,
            receiptId,
            workspace.Id,
            keyDigest,
            commandDigest,
            workspace.ContentRevision,
            nextRevision,
            workspace.SavedRevision,
            nextRevision,
            preview.Binding.RawCharacterXmlDigest,
            preview.Binding.PrerequisiteDraftDigest,
            preview.Binding.AuthorityDigest,
            preview.Binding.SourceDigest,
            preview.Binding.RulesDigest,
            preview.Binding.RuntimeDigest,
            preview.SelectedOption.OptionId,
            preview.SelectedOption.KarmaInvestment,
            preview.BudgetAfter.TotalStartingNuyen,
            preview.BudgetAfter.RemainingNuyen,
            draft.DraftRevision,
            draft.DraftDigest,
            preview.PreviewDigest,
            previousReceiptDigest,
            CharacterDocumentChanged: false,
            ReceiptDigest: string.Empty);
        CharacterCreationResourcesReceipt receipt = receiptCandidate with
        {
            ReceiptDigest = CharacterCreationResourcesRules.ComputeReceiptDigest(receiptCandidate)
        };
        CharacterCreationResourcesReceiptLedgerEntry[] ledger =
        [
            .. existing,
            new CharacterCreationResourcesReceiptLedgerEntry(keyDigest, commandDigest, receipt)
        ];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationResourcesDraft = draft,
                    CharacterCreationResourcesReceipts = ledger
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
            return new CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Applied,
                receipt,
                []);
        }
        if (committed.Outcome == WorkspaceOperationOutcome.Conflict)
        {
            WorkspaceStoreReadResult racedRead = _workspaceStore.Get(workspace.Id);
            if (racedRead.Success && racedRead.Value is WorkspaceStoredDocument raced)
            {
                CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>? racedReplay =
                    ResolveReplay(raced, keyDigest, commandDigest);
                if (racedReplay is not null)
                    return racedReplay;
            }
        }
        return Blocked<CharacterCreationResourcesReceipt>(
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationResourcesOutcomes.Conflict
                : CharacterCreationResourcesOutcomes.Unavailable,
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationResourcesBlockers.StaleWorkspaceRevision
                : CharacterCreationResourcesBlockers.PersistenceAuthorityRequired);
    }

    public CharacterCreationResourcesResult<CharacterCreationResourcesReceipt> LookupReceipt(
        CharacterCreationResourcesReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Invalid,
                CharacterCreationResourcesBlockers.IdempotencyKeyInvalid);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationResourcesReceipt>(read);
        CharacterCreationResourcesDraft? draft =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [];
        if (!CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                draft,
                ledger))
        {
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Corrupt,
                CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        }
        string digest = CharacterCreationResourcesRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-resources.idempotency.v1\0" + key);
        CharacterCreationResourcesReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            CharacterCreationResourcesRules.DigestsEqual(entry.IdempotencyKeyDigest, digest));
        return found is null
            ? Blocked<CharacterCreationResourcesReceipt>(CharacterCreationResourcesOutcomes.NotFound)
            : new CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Available,
                found.Receipt,
                []);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationResourcesPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return new PreviewEvaluation(ReadFailure<CharacterCreationResourcesPreview>(read), null);
        AuthorityContext context = BuildContext(workspace);
        string? bindingBlocker = CompareBinding(context.Binding, request.Binding);
        if (bindingBlocker is not null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationResourcesPreview>(
                    CharacterCreationResourcesOutcomes.Conflict,
                    bindingBlocker),
                null);
        }

        var blockers = new List<string>(context.Blockers);
        blockers.AddRange(context.Budget.Blockers);
        CharacterCreationResourceAllocationOption? option = context.Options.SingleOrDefault(item =>
            string.Equals(item.OptionId, request.OptionId, StringComparison.Ordinal));
        if (option is null)
            blockers.Add(CharacterCreationResourcesBlockers.InvalidOption);
        else if (!option.IsEnabled)
            blockers.AddRange(option.Blockers.Count == 0
                ? [CharacterCreationResourcesBlockers.InvalidOption]
                : option.Blockers);
        if (context.PendingDraft is not null
            && option is not null
            && string.Equals(
                context.PendingDraft.SelectedOptionId,
                option.OptionId,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationResourcesBlockers.NoChange);

        CharacterCreationResourcesBudget budgetAfter = option is null
            ? context.Budget
            : BuildBudget(
                context.Authority,
                context.PriorityOption,
                option,
                context.PurchaseAuthorityExact,
                blockers);
        blockers.AddRange(budgetAfter.Blockers);
        string rawDigest = context.Binding.RawCharacterXmlDigest;
        var contributionCandidate = new CharacterCreationResourcesFinalizationContribution(
            CharacterCreationResourcesSchemas.ContributionV1,
            context.PriorityOption?.Rank ?? string.Empty,
            context.PriorityOption?.SourceId ?? string.Empty,
            context.PriorityOption?.BasePriorityNuyen ?? 0m,
            option?.KarmaInvestment ?? 0,
            rawDigest,
            context.PriorityOption?.SourceAnchorIds
                .Concat(CharacterCreationResourcesSourceAnchors.All)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(anchor => anchor, StringComparer.Ordinal)
                .ToArray() ?? CharacterCreationResourcesSourceAnchors.All,
            ContributionDigest: string.Empty);
        CharacterCreationResourcesFinalizationContribution contribution = contributionCandidate with
        {
            ContributionDigest = CharacterCreationResourcesRules.ComputeContributionDigest(
                contributionCandidate)
        };
        var draftCandidate = new CharacterCreationResourcesDraft(
            CharacterCreationResourcesSchemas.DraftV1,
            workspace.Id,
            DraftRevision: (context.PendingDraft?.DraftRevision ?? 0) + 1,
            BaseContentRevision: workspace.ContentRevision,
            BaseRawCharacterXmlDigest: rawDigest,
            PrerequisiteDraftRevision: context.Prerequisite?.DraftRevision ?? 0,
            PrerequisiteDraftDigest: context.Prerequisite?.DraftDigest ?? string.Empty,
            AuthorityDigest: context.Authority.AuthorityDigest,
            SourceDigest: context.Authority.SourceDigest,
            RulesDigest: context.Authority.RulesDigest,
            RuntimeDigest: context.Authority.RuntimeDigest,
            SelectedOptionId: option?.OptionId ?? request.OptionId,
            KarmaInvestment: option?.KarmaInvestment ?? 0,
            Budget: budgetAfter,
            FinalizationContribution: contribution,
            SourceAnchorIds: contribution.SourceAnchorIds,
            CharacterEffectsApplied: false,
            LastIdempotencyKeyDigest: string.Empty,
            LastPreviewDigest: string.Empty,
            LastCommandDigest: string.Empty,
            DraftDigest: string.Empty);
        string[] normalized = Normalize(blockers);
        var previewCandidate = new CharacterCreationResourcesPreview(
            CharacterCreationResourcesSchemas.PreviewV1,
            CharacterCreationWizardStepIds.Resources,
            context.Binding,
            context.PendingDraft,
            draftCandidate,
            option,
            context.Budget,
            budgetAfter,
            contribution,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0,
            PreviewDigest: string.Empty);
        CharacterCreationResourcesPreview preview = previewCandidate with
        {
            PreviewDigest = CharacterCreationResourcesRules.ComputePreviewDigest(previewCandidate)
        };
        return new PreviewEvaluation(
            new CharacterCreationResourcesResult<CharacterCreationResourcesPreview>(
                normalized.Length == 0
                    ? CharacterCreationResourcesOutcomes.Available
                    : CharacterCreationResourcesOutcomes.Blocked,
                preview,
                normalized),
            workspace);
    }

    private AuthorityContext BuildContext(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        XDocument document;
        XElement root;
        try
        {
            document = XDocument.Parse(workspace.Document.Content, LoadOptions.PreserveWhitespace);
            root = document.Root ?? throw new XmlException();
            if (!string.Equals(root.Name.LocalName, "character", StringComparison.Ordinal))
                throw new XmlException();
        }
        catch (XmlException)
        {
            document = new XDocument(new XElement("character"));
            root = document.Root!;
            blockers.Add(CharacterCreationResourcesBlockers.CharacterDocumentInvalid);
        }
        if (!string.Equals(workspace.Document.RulesetId, RulesetDefaults.Sr5, StringComparison.Ordinal))
            blockers.Add(CharacterCreationResourcesBlockers.RulesetSr5Required);
        if (ParseBool(ReadValue(root, "created")))
            blockers.Add(CharacterCreationResourcesBlockers.CareerModeRejected);

        ICharacterSourceDataContext? source = _sourceData.TryCreateContext(workspace.Document.Content);
        CharacterCreationPrerequisiteAuthority prerequisiteAuthority =
            CharacterCreationPrerequisiteAuthority.Unavailable;
        CharacterCreationResourcesAuthority authority = CharacterCreationResourcesAuthority.Unavailable;
        if (source is null
            || !source.TryResolveCreationPrerequisiteAuthority(out prerequisiteAuthority)
            || !source.TryResolveCreationResourcesAuthority(out authority)
            || !CharacterCreationResourcesRules.IsValidAuthority(authority))
        {
            authority = CharacterCreationResourcesAuthority.Unavailable;
            blockers.Add(CharacterCreationResourcesBlockers.AuthorityUnavailable);
        }
        if (authority.BuildMethod is not (CharacterCreationBuildMethods.Priority
            or CharacterCreationBuildMethods.SumToTen))
            blockers.Add(CharacterCreationResourcesBlockers.BuildMethodUnsupported);

        string rawDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        CharacterCreationPrerequisiteDraft? prerequisite =
            workspace.Document.AuxiliaryState.CharacterCreationPrerequisiteDraft;
        if (prerequisite is null)
            blockers.Add(CharacterCreationResourcesBlockers.PrerequisiteDraftRequired);
        else if (!CharacterCreationPrerequisiteDraftIntegrity.IsValidPending(
                     prerequisite,
                     workspace.Id,
                     workspace.ContentRevision,
                     rawDigest,
                     prerequisiteAuthority))
            blockers.Add(CharacterCreationResourcesBlockers.PrerequisiteDraftStale);

        CharacterCreationResourcePriorityOption? priorityOption = null;
        if (prerequisite is not null)
        {
            CharacterCreationPriorityAssignment? assignment = prerequisite.Assignments
                .SingleOrDefault(item => string.Equals(
                    item.CategoryId,
                    CharacterCreationPriorityCategoryIds.Resources,
                    StringComparison.Ordinal));
            priorityOption = assignment is null
                ? null
                : authority.PriorityOptions.SingleOrDefault(option => string.Equals(
                    option.SourceId,
                    assignment.SourceId,
                    StringComparison.Ordinal));
            if (priorityOption is null
                || !string.Equals(priorityOption.Rank, assignment?.Rank, StringComparison.Ordinal))
                blockers.Add(CharacterCreationResourcesBlockers.ResourceAssignmentInvalid);
        }

        CharacterCreationResourcesDraft? pending =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [];
        if (!CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                pending,
                ledger))
            blockers.Add(CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        else if (ledger.Count >= CharacterCreationResourcesReceiptLedgerIntegrity.MaximumEntries)
            blockers.Add(CharacterCreationResourcesBlockers.PersistenceAuthorityRequired);
        if (pending is not null
            && (prerequisite is null
                || pending.PrerequisiteDraftRevision != prerequisite.DraftRevision
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.PrerequisiteDraftDigest,
                    prerequisite.DraftDigest)
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.BaseRawCharacterXmlDigest,
                    rawDigest)
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.AuthorityDigest,
                    authority.AuthorityDigest)
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.SourceDigest,
                    authority.SourceDigest)
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.RulesDigest,
                    authority.RulesDigest)
                || !CharacterCreationResourcesRules.DigestsEqual(
                    pending.RuntimeDigest,
                    authority.RuntimeDigest)))
            blockers.Add(CharacterCreationResourcesBlockers.StalePrerequisiteDraft);

        bool purchaseAuthorityExact = !HasUnsupportedPurchases(root);
        if (!purchaseAuthorityExact)
            blockers.Add(CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        int availableKarma = ResolveAvailableCreationKarma(
            workspace.Document.AuxiliaryState,
            prerequisite,
            blockers);
        CharacterCreationResourceAllocationOption[] options = BuildOptions(
            authority,
            priorityOption,
            availableKarma);
        CharacterCreationResourceAllocationOption? selected = pending is null
            ? options.SingleOrDefault(option => option.KarmaInvestment == 0)
            : options.SingleOrDefault(option => string.Equals(
                option.OptionId,
                pending.SelectedOptionId,
                StringComparison.Ordinal));
        if (pending is not null && selected is null)
            blockers.Add(CharacterCreationResourcesBlockers.InvalidOption);
        CharacterCreationResourcesBudget budget = selected is null
            ? EmptyBudget(authority, priorityOption, purchaseAuthorityExact)
            : BuildBudget(authority, priorityOption, selected, purchaseAuthorityExact, blockers: null);

        var binding = new CharacterCreationResourcesBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.ContentRevision,
            workspace.SavedRevision,
            rawDigest,
            workspace.Document.AuxiliaryStateDigest,
            prerequisite?.DraftRevision ?? 0,
            prerequisite?.DraftDigest ?? string.Empty,
            authority.AuthorityDigest,
            authority.SourceDigest,
            authority.RulesDigest,
            authority.RuntimeDigest);
        return new AuthorityContext(
            workspace,
            document,
            root,
            prerequisite,
            pending,
            authority,
            priorityOption,
            options,
            budget,
            purchaseAuthorityExact,
            binding,
            Normalize(blockers));
    }

    private static CharacterCreationResourceAllocationOption[] BuildOptions(
        CharacterCreationResourcesAuthority authority,
        CharacterCreationResourcePriorityOption? priority,
        int availableKarma)
    {
        if (!CharacterCreationResourcesRules.IsValidAuthority(authority) || priority is null)
            return [];
        var result = new List<CharacterCreationResourceAllocationOption>();
        for (int investment = 0; investment <= authority.MaximumKarmaInvestment; investment++)
        {
            decimal converted;
            decimal total;
            try
            {
                converted = checked(investment * authority.KarmaToNuyenRate);
                total = checked(priority.BasePriorityNuyen + converted);
            }
            catch (OverflowException)
            {
                break;
            }
            string[] blockers = investment <= availableKarma
                ? []
                : [CharacterCreationResourcesBlockers.InsufficientCreationKarma];
            var candidate = new CharacterCreationResourceAllocationOption(
                $"karma:{investment.ToString(CultureInfo.InvariantCulture)}",
                investment,
                converted,
                total,
                IsEnabled: blockers.Length == 0,
                Blockers: blockers,
                SourceAnchorIds: priority.SourceAnchorIds
                    .Concat(CharacterCreationResourcesSourceAnchors.All)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(anchor => anchor, StringComparer.Ordinal)
                    .ToArray(),
                OptionDigest: string.Empty);
            result.Add(candidate with
            {
                OptionDigest = CharacterCreationResourcesRules.ComputeAllocationOptionDigest(candidate)
            });
        }
        return result.ToArray();
    }

    private static CharacterCreationResourcesBudget BuildBudget(
        CharacterCreationResourcesAuthority authority,
        CharacterCreationResourcePriorityOption? priority,
        CharacterCreationResourceAllocationOption option,
        bool purchaseAuthorityExact,
        List<string>? blockers)
    {
        var findings = new List<string>();
        if (priority is null)
            findings.Add(CharacterCreationResourcesBlockers.ResourceAssignmentInvalid);
        if (!purchaseAuthorityExact)
            findings.Add(CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired);
        decimal priorityNuyen = priority?.BasePriorityNuyen ?? 0m;
        decimal knownPurchaseCost = 0m;
        decimal remaining = option.TotalStartingNuyen;
        decimal carryover = Math.Max(0m, authority.NuyenCarryover);
        string[] normalized = Normalize(findings);
        blockers?.AddRange(normalized);
        return new CharacterCreationResourcesBudget(
            priorityNuyen,
            option.KarmaInvestment,
            option.NuyenFromKarma,
            option.TotalStartingNuyen,
            knownPurchaseCost,
            remaining,
            Overspend: 0m,
            CarryoverLimit: carryover,
            CarryoverExcess: Math.Max(0m, remaining - carryover),
            IsExact: normalized.Length == 0,
            Blockers: normalized,
            SourceAnchorIds: option.SourceAnchorIds);
    }

    private static CharacterCreationResourcesBudget EmptyBudget(
        CharacterCreationResourcesAuthority authority,
        CharacterCreationResourcePriorityOption? priority,
        bool exact)
    {
        decimal total = priority?.BasePriorityNuyen ?? 0m;
        string[] blockers = exact
            ? []
            : [CharacterCreationResourcesBlockers.PurchaseCostAuthorityRequired];
        decimal excess = Math.Max(0m, total - Math.Max(0m, authority.NuyenCarryover));
        return new CharacterCreationResourcesBudget(
            priority?.BasePriorityNuyen ?? 0m,
            0,
            0m,
            total,
            0m,
            total,
            0m,
            Math.Max(0m, authority.NuyenCarryover),
            excess,
            exact,
            blockers,
            CharacterCreationResourcesSourceAnchors.All);
    }

    private static int ResolveAvailableCreationKarma(
        WorkspaceDocumentAuxiliaryState state,
        CharacterCreationPrerequisiteDraft? prerequisite,
        List<string> blockers)
    {
        if (state.CharacterCreationQualitiesDraft is CharacterCreationQualitiesDraft qualities)
            return Math.Max(0, qualities.KarmaRemaining);
        if (state.CharacterCreationAttributesDraft is CharacterCreationAttributesDraft attributes)
            return Math.Max(0, attributes.CreationKarmaTotal - attributes.CreationKarmaUsed);
        if (prerequisite is not null)
            return Math.Max(0, prerequisite.CreationKarmaTotal - prerequisite.CreationKarmaUsed);
        blockers.Add(CharacterCreationResourcesBlockers.PrerequisiteDraftRequired);
        return 0;
    }

    private static bool HasUnsupportedPurchases(XElement root)
    {
        foreach (string containerName in s_UnsupportedPurchaseContainers)
        {
            XElement[] containers = root.Elements(containerName).Take(2).ToArray();
            if (containers.Length > 1 || containers.Any(container => container.Elements().Any()))
                return true;
        }
        foreach (XElement improvement in root.Element("improvements")?.Elements("improvement") ?? [])
        {
            string kind = ReadValue(improvement, "improvetype");
            if (string.IsNullOrEmpty(kind))
                kind = ReadValue(improvement, "improvementtype");
            if (kind is "Nuyen" or "NuyenMaxBP")
                return true;
        }
        return false;
    }

    private CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>? ResolveReplay(
        WorkspaceStoredDocument workspace,
        string keyDigest,
        string commandDigest)
    {
        CharacterCreationResourcesDraft? draft =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesDraft;
        IReadOnlyList<CharacterCreationResourcesReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationResourcesReceipts ?? [];
        if (!CharacterCreationResourcesReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                draft,
                ledger))
            return Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Corrupt,
                CharacterCreationResourcesBlockers.ReceiptLedgerCorrupt);
        CharacterCreationResourcesReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            CharacterCreationResourcesRules.DigestsEqual(entry.IdempotencyKeyDigest, keyDigest));
        if (found is null)
            return null;
        return CharacterCreationResourcesRules.DigestsEqual(found.CommandDigest, commandDigest)
            ? new CharacterCreationResourcesResult<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Replayed,
                found.Receipt,
                [])
            : Blocked<CharacterCreationResourcesReceipt>(
                CharacterCreationResourcesOutcomes.Conflict,
                CharacterCreationResourcesBlockers.IdempotencyConflict);
    }

    private static string? CompareBinding(
        CharacterCreationResourcesBinding current,
        CharacterCreationResourcesBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.WorkspaceRevision != requested.WorkspaceRevision
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision)
            return CharacterCreationResourcesBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationResourcesRules.DigestsEqual(
                current.RawCharacterXmlDigest,
                requested.RawCharacterXmlDigest))
            return CharacterCreationResourcesBlockers.StaleContentDigest;
        if (!CharacterCreationResourcesRules.DigestsEqual(
                current.AuxiliaryStateDigest,
                requested.AuxiliaryStateDigest))
            return CharacterCreationResourcesBlockers.StaleAuxiliaryStateDigest;
        if (current.PrerequisiteDraftRevision != requested.PrerequisiteDraftRevision
            || !CharacterCreationResourcesRules.DigestsEqual(
                current.PrerequisiteDraftDigest,
                requested.PrerequisiteDraftDigest))
            return CharacterCreationResourcesBlockers.StalePrerequisiteDraft;
        if (!CharacterCreationResourcesRules.DigestsEqual(
                current.AuthorityDigest,
                requested.AuthorityDigest))
            return CharacterCreationResourcesBlockers.AuthorityUnavailable;
        if (!CharacterCreationResourcesRules.DigestsEqual(current.SourceDigest, requested.SourceDigest))
            return CharacterCreationResourcesBlockers.StaleSourceDigest;
        if (!CharacterCreationResourcesRules.DigestsEqual(current.RulesDigest, requested.RulesDigest))
            return CharacterCreationResourcesBlockers.StaleRulesDigest;
        if (!CharacterCreationResourcesRules.DigestsEqual(current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationResourcesBlockers.StaleRuntimeDigest;
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

    private static CharacterCreationResourcesResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, Normalize(blockers));

    private static CharacterCreationResourcesResult<T> ReadFailure<T>(WorkspaceStoreReadResult read)
        where T : class => Blocked<T>(
            read.Outcome == WorkspaceOperationOutcome.Missing
                ? CharacterCreationResourcesOutcomes.NotFound
                : CharacterCreationResourcesOutcomes.Unavailable,
            read.Outcome == WorkspaceOperationOutcome.Missing
                ? CharacterCreationResourcesBlockers.WorkspaceUnavailable
                : CharacterCreationResourcesBlockers.PersistenceAuthorityRequired);

    private sealed record AuthorityContext(
        WorkspaceStoredDocument Workspace,
        XDocument Document,
        XElement Root,
        CharacterCreationPrerequisiteDraft? Prerequisite,
        CharacterCreationResourcesDraft? PendingDraft,
        CharacterCreationResourcesAuthority Authority,
        CharacterCreationResourcePriorityOption? PriorityOption,
        IReadOnlyList<CharacterCreationResourceAllocationOption> Options,
        CharacterCreationResourcesBudget Budget,
        bool PurchaseAuthorityExact,
        CharacterCreationResourcesBinding Binding,
        IReadOnlyList<string> Blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationResourcesResult<CharacterCreationResourcesPreview> Result,
        WorkspaceStoredDocument? Workspace);
}
