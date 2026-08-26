using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

/// <summary>
/// Governed SR5 creation-mode lifestyle authority. Catalog identity and prices come from
/// <see cref="ICharacterSourceDataResolver"/>; callers submit stable option ids only.
/// </summary>
public sealed class CharacterCreationLifestylesService : ICharacterCreationLifestylesService
{
    private const int MaximumIdempotencyKeyLength = 200;

    private static readonly HashSet<string> s_KnownLifestyleElements = new(StringComparer.Ordinal)
    {
        "sourceid", "guid", "name", "cost", "dice", "lp", "baselifestyle", "multiplier",
        "months", "roommates", "percentage", "area", "comforts", "security", "basearea",
        "basecomforts", "basesecurity", "maxarea", "maxcomforts", "maxsecurity", "costforearea",
        "costforarea", "costforcomforts", "costforsecurity", "allowbonuslp", "bonuslp", "source",
        "page", "trustfund", "splitcostwithroommates", "type", "increment", "city", "district",
        "borough", "lifestylequalities"
    };

    private static readonly HashSet<string> s_KnownQualityElements = new(StringComparer.Ordinal)
    {
        "sourceid", "guid", "name", "category", "extra", "cost", "multiplier", "basemultiplier",
        "lp", "areamaximum", "comfortsmaximum", "securitymaximum", "area", "comforts", "security",
        "uselpcost", "print", "lifestylequalitytype", "lifestylequalitysource", "free", "isfreegrid",
        "source", "page", "allowed"
    };

    private readonly IWorkspaceStore _workspaceStore;
    private readonly ICharacterSourceDataResolver _sourceData;

    public CharacterCreationLifestylesService(
        IWorkspaceStore workspaceStore,
        ICharacterSourceDataResolver sourceData)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
        _sourceData = sourceData ?? throw new ArgumentNullException(nameof(sourceData));
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestylesState> Load(
        CharacterCreationLifestylesLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationLifestylesState>(read);

        AuthorityContext context = BuildContext(workspace);
        string[] blockers = Normalize(context.Blockers.Concat(context.Budget.Blockers));
        var candidate = new CharacterCreationLifestylesState(
            CharacterCreationLifestylesSchemas.StateV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            context.Binding,
            context.Authority,
            context.CharacterCreated,
            context.Projections,
            context.Budget,
            blockers,
            CanEdit: blockers.Length == 0,
            SnapshotDigest: string.Empty);
        CharacterCreationLifestylesState state = candidate with
        {
            SnapshotDigest = CharacterCreationLifestylesRules.ComputeStateDigest(candidate)
        };
        return new CharacterCreationLifestyleResult<CharacterCreationLifestylesState>(
            CharacterCreationLifestyleOutcomes.Available,
            state,
            blockers);
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> Preview(
        CharacterCreationLifestylePreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Mutation is null)
            return Blocked<CharacterCreationLifestylePreview>(
                CharacterCreationLifestyleOutcomes.Invalid,
                CharacterCreationLifestylesBlockers.InvalidMutation);
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> Confirm(
        CharacterCreationLifestyleConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Mutation is null)
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Invalid,
                CharacterCreationLifestylesBlockers.InvalidMutation);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Invalid,
                CharacterCreationLifestylesBlockers.IdempotencyKeyInvalid);

        string keyDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-lifestyles.idempotency.v1\0" + key);
        string commandDigest = CharacterCreationLifestylesRules.ComputeCommandDigest(request);
        WorkspaceStoreReadResult initialRead = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!initialRead.Success || initialRead.Value is not WorkspaceStoredDocument initial)
            return ReadFailure<CharacterCreationLifestyleReceipt>(initialRead);
        CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>? replay = ResolveReplay(
            initial,
            keyDigest,
            commandDigest);
        if (replay is not null)
            return replay;
        if (!request.ExplicitlyConfirmed)
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Blocked,
                CharacterCreationLifestylesBlockers.ExplicitConfirmationRequired);

        PreviewEvaluation evaluation = EvaluatePreview(new CharacterCreationLifestylePreviewRequest(
            request.Binding,
            request.Mutation));
        if (evaluation.Result.Value is not CharacterCreationLifestylePreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace
            || evaluation.ReplacementContent is null)
        {
            return new CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!CharacterCreationLifestylesRules.DigestsEqual(preview.PreviewDigest, request.PreviewDigest))
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Conflict,
                CharacterCreationLifestylesBlockers.PreviewDigestMismatch);
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
            return new CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Blocked,
                null,
                preview.Blockers);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore
            || workspace.ContentRevision == long.MaxValue)
        {
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Unavailable,
                CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired);
        }

        long nextRevision = workspace.ContentRevision + 1;
        string receiptId = "creation-lifestyle-" + commandDigest["sha256:".Length..][..24];
        var candidate = new CharacterCreationLifestyleReceipt(
            CharacterCreationLifestylesSchemas.ReceiptV1,
            receiptId,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            workspace.Id,
            request.Mutation.MutationKind,
            request.Mutation.LifestyleId,
            keyDigest,
            commandDigest,
            workspace.ContentRevision,
            nextRevision,
            workspace.ContentRevision,
            nextRevision,
            workspace.SavedRevision,
            nextRevision,
            preview.WritePlan.ContentDigestBefore,
            preview.WritePlan.ContentDigestAfter,
            preview.Binding.SourceDigest,
            preview.Binding.RulesDigest,
            preview.Binding.RuntimeDigest,
            preview.BudgetBefore.Used,
            preview.BudgetAfter.Used,
            preview.BudgetAfter.Remaining,
            preview.WritePlan,
            ReceiptDigest: string.Empty);
        CharacterCreationLifestyleReceipt receipt = candidate with
        {
            ReceiptDigest = CharacterCreationLifestylesRules.ComputeReceiptDigest(candidate)
        };
        CharacterCreationLifestyleReceiptLedgerEntry[] ledger =
        [
            .. workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? [],
            new CharacterCreationLifestyleReceiptLedgerEntry(keyDigest, commandDigest, receipt)
        ];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                Payload = evaluation.ReplacementContent,
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationLifestyleReceipts = ledger
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
            return new CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Applied,
                receipt,
                []);
        }

        if (committed.Outcome == WorkspaceOperationOutcome.Conflict)
        {
            WorkspaceStoreReadResult racedRead = _workspaceStore.Get(workspace.Id);
            if (racedRead.Success && racedRead.Value is WorkspaceStoredDocument raced)
            {
                CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>? racedReplay =
                    ResolveReplay(raced, keyDigest, commandDigest);
                if (racedReplay is not null)
                    return racedReplay;
            }
        }
        return Blocked<CharacterCreationLifestyleReceipt>(
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationLifestyleOutcomes.Conflict
                : CharacterCreationLifestyleOutcomes.Unavailable,
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationLifestylesBlockers.StaleWorkspaceRevision
                : CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired);
    }

    public CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt> LookupReceipt(
        CharacterCreationLifestyleReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string key))
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Invalid,
                CharacterCreationLifestylesBlockers.IdempotencyKeyInvalid);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return ReadFailure<CharacterCreationLifestyleReceipt>(read);
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? [];
        if (!CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
        {
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Corrupt,
                CharacterCreationLifestylesBlockers.ReceiptLedgerCorrupt);
        }
        string digest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest(
            "chummer.sr5.creation-lifestyles.idempotency.v1\0" + key);
        CharacterCreationLifestyleReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            CharacterCreationLifestylesRules.DigestsEqual(entry.IdempotencyKeyDigest, digest));
        return found is null
            ? Blocked<CharacterCreationLifestyleReceipt>(CharacterCreationLifestyleOutcomes.NotFound)
            : new CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Available,
                found.Receipt,
                []);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationLifestylePreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
            return new PreviewEvaluation(ReadFailure<CharacterCreationLifestylePreview>(read), null, null);
        AuthorityContext context = BuildContext(workspace);
        string? bindingBlocker = CompareBinding(context.Binding, request.Binding);
        if (bindingBlocker is not null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationLifestylePreview>(
                    CharacterCreationLifestyleOutcomes.Conflict,
                    bindingBlocker),
                null,
                null);
        }

        var blockers = new List<string>(context.Blockers);
        blockers.AddRange(context.Budget.Blockers);
        CharacterCreationLifestyleMutation mutation = request.Mutation;
        if (!CharacterCreationLifestyleMutationKinds.All.Contains(mutation.MutationKind)
            || mutation.LifestyleId == Guid.Empty
            || mutation.MutationKind == CharacterCreationLifestyleMutationKinds.Delete
               && mutation.Configuration is not null
            || mutation.MutationKind != CharacterCreationLifestyleMutationKinds.Delete
               && (mutation.Configuration is null
                   || mutation.Configuration.LifestyleId != mutation.LifestyleId))
        {
            blockers.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        }
        LifestyleElement[] matches = context.Elements.Where(item => item.Id == mutation.LifestyleId).ToArray();
        if (matches.Length > 1)
            blockers.Add(CharacterCreationLifestylesBlockers.DuplicateIdentity);
        bool create = mutation.MutationKind == CharacterCreationLifestyleMutationKinds.Create;
        bool delete = mutation.MutationKind == CharacterCreationLifestyleMutationKinds.Delete;
        if (create && matches.Length != 0)
            blockers.Add(CharacterCreationLifestylesBlockers.InvalidIdentity);
        if (!create && matches.Length != 1)
            blockers.Add(CharacterCreationLifestylesBlockers.LifestyleNotFound);

        CharacterCreationLifestyleProjection? before = context.Projections.SingleOrDefault(
            item => item.Configuration.LifestyleId == mutation.LifestyleId);
        CharacterCreationLifestyleProjection? after = null;
        if (!delete && mutation.Configuration is not null
            && !CharacterCreationLifestylesRules.TryProject(
                mutation.Configuration,
                context.Authority,
                out after,
                out IReadOnlyList<string> projectionBlockers))
        {
            blockers.AddRange(projectionBlockers);
        }
        if (!create && before is null)
            blockers.Add(CharacterCreationLifestylesBlockers.InvalidIdentity);

        if (blockers.Count != 0)
            return BlockedPreview(context, workspace, mutation, before, after, blockers);

        XDocument replacementDocument = new(context.Document);
        XElement replacementRoot = replacementDocument.Root!;
        XElement? currentTarget = matches.SingleOrDefault()?.Element;
        XElement? replacementTarget = FindUniqueLifestyle(replacementRoot, mutation.LifestyleId);
        IReadOnlySet<Guid> retainedQualityIds = after?.Configuration.Qualities
            .Select(item => item.InstanceId)
            .ToHashSet() ?? new HashSet<Guid>();
        string siblingsBefore = ComputeUntouchedSiblingDigest(context.Root, mutation.LifestyleId);
        string nestedBefore = currentTarget is null || delete
            ? EmptyDigest()
            : ComputeNestedStateDigest(currentTarget, retainedQualityIds);

        if (create)
        {
            XElement container = replacementRoot.Element("lifestyles")
                ?? AddAndReturn(replacementRoot, new XElement("lifestyles"));
            replacementTarget = BuildLifestyleElement(after!, null, context.Authority);
            container.Add(replacementTarget);
        }
        else if (delete)
        {
            replacementTarget!.Remove();
            replacementTarget = null;
        }
        else
        {
            XElement rewritten = BuildLifestyleElement(after!, replacementTarget, context.Authority);
            replacementTarget!.ReplaceWith(rewritten);
            replacementTarget = rewritten;
        }

        string replacementContent = replacementDocument.ToString(SaveOptions.DisableFormatting);
        string contentAfter = CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeContentDigest(
            replacementContent);
        string siblingsAfter = ComputeUntouchedSiblingDigest(replacementRoot, mutation.LifestyleId);
        string nestedAfter = replacementTarget is null || create
            ? EmptyDigest()
            : ComputeNestedStateDigest(replacementTarget, retainedQualityIds);
        bool siblingsPreserved = CharacterCreationLifestylesRules.DigestsEqual(
            siblingsBefore,
            siblingsAfter);
        bool nestedPreserved = CharacterCreationLifestylesRules.DigestsEqual(nestedBefore, nestedAfter);
        if (!siblingsPreserved || !nestedPreserved)
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);

        List<CharacterCreationLifestyleProjection> afterRows = context.Projections
            .Where(item => item.Configuration.LifestyleId != mutation.LifestyleId)
            .ToList();
        if (after is not null)
            afterRows.Add(after);
        CharacterCreationLifestyleBudget budgetAfter = ComputeBudget(context.Root, afterRows);
        blockers.AddRange(budgetAfter.Blockers);
        if (budgetAfter.Overspend > 0m)
            blockers.Add(CharacterCreationLifestylesBlockers.InsufficientFunds);
        string beforeDigest = before?.LifestyleDigest ?? EmptyDigest();
        string afterDigest = after?.LifestyleDigest ?? EmptyDigest();
        if (CharacterCreationLifestylesRules.DigestsEqual(beforeDigest, afterDigest))
            blockers.Add(CharacterCreationLifestylesBlockers.NoChange);
        var operation = new CharacterCreationLifestyleWriteOperation(
            1,
            mutation.MutationKind,
            mutation.LifestyleId,
            beforeDigest,
            afterDigest,
            CharacterCreationLifestyleSourceAnchors.All);
        var planCandidate = new CharacterCreationLifestyleAtomicWritePlan(
            CharacterCreationLifestylesSchemas.WritePlanV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            mutation.MutationKind,
            mutation.LifestyleId,
            before,
            after,
            [operation],
            context.Binding.ContentDigest,
            contentAfter,
            siblingsBefore,
            siblingsAfter,
            nestedBefore,
            nestedAfter,
            siblingsPreserved,
            nestedPreserved,
            PlanDigest: string.Empty);
        CharacterCreationLifestyleAtomicWritePlan plan = planCandidate with
        {
            PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(planCandidate)
        };
        string[] normalized = Normalize(blockers);
        var previewCandidate = new CharacterCreationLifestylePreview(
            CharacterCreationLifestylesSchemas.PreviewV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            context.Binding,
            mutation.MutationKind,
            before,
            after,
            context.Budget,
            budgetAfter,
            plan,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0,
            PreviewDigest: string.Empty);
        CharacterCreationLifestylePreview preview = previewCandidate with
        {
            PreviewDigest = CharacterCreationLifestylesRules.ComputePreviewDigest(previewCandidate)
        };
        return new PreviewEvaluation(
            new CharacterCreationLifestyleResult<CharacterCreationLifestylePreview>(
                normalized.Length == 0
                    ? CharacterCreationLifestyleOutcomes.Available
                    : CharacterCreationLifestyleOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            replacementContent);
    }

    private static PreviewEvaluation BlockedPreview(
        AuthorityContext context,
        WorkspaceStoredDocument workspace,
        CharacterCreationLifestyleMutation mutation,
        CharacterCreationLifestyleProjection? before,
        CharacterCreationLifestyleProjection? after,
        IEnumerable<string> blockers)
    {
        string empty = EmptyDigest();
        var planCandidate = new CharacterCreationLifestyleAtomicWritePlan(
            CharacterCreationLifestylesSchemas.WritePlanV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            mutation.MutationKind,
            mutation.LifestyleId,
            before,
            after,
            [],
            context.Binding.ContentDigest,
            context.Binding.ContentDigest,
            empty,
            empty,
            empty,
            empty,
            true,
            true,
            PlanDigest: string.Empty);
        CharacterCreationLifestyleAtomicWritePlan plan = planCandidate with
        {
            PlanDigest = CharacterCreationLifestylesRules.ComputePlanDigest(planCandidate)
        };
        string[] normalized = Normalize(blockers);
        var previewCandidate = new CharacterCreationLifestylePreview(
            CharacterCreationLifestylesSchemas.PreviewV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            context.Binding,
            mutation.MutationKind,
            before,
            after,
            context.Budget,
            context.Budget,
            plan,
            normalized,
            true,
            false,
            PreviewDigest: string.Empty);
        CharacterCreationLifestylePreview preview = previewCandidate with
        {
            PreviewDigest = CharacterCreationLifestylesRules.ComputePreviewDigest(previewCandidate)
        };
        return new PreviewEvaluation(
            new CharacterCreationLifestyleResult<CharacterCreationLifestylePreview>(
                CharacterCreationLifestyleOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            null);
    }

    private AuthorityContext BuildContext(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            })
            blockers.Add(CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired);
        if (!string.Equals(
                RulesetDefaults.NormalizeOptional(workspace.Document.RulesetId),
                RulesetDefaults.Sr5,
                StringComparison.Ordinal))
            blockers.Add(CharacterCreationLifestylesBlockers.RulesetSr5Required);

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
            blockers.Add(CharacterCreationLifestylesBlockers.CharacterDocumentInvalid);
        }
        bool created = ParseBool(ReadValue(root, "created"));
        if (created)
            blockers.Add(CharacterCreationLifestylesBlockers.CareerModeRejected);

        CharacterCreationLifestylesAuthority authority = CharacterCreationLifestylesAuthority.Unavailable;
        ICharacterSourceDataContext? source = _sourceData.TryCreateContext(workspace.Document.Content);
        if (source is null
            || !source.TryResolveCreationLifestylesAuthority(out authority)
            || !CharacterCreationLifestylesRules.IsValidAuthority(authority))
        {
            authority = CharacterCreationLifestylesAuthority.Unavailable;
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        }
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? [];
        if (!CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
            blockers.Add(CharacterCreationLifestylesBlockers.ReceiptLedgerCorrupt);
        else if (ledger.Count >= CharacterCreationLifestyleReceiptLedgerIntegrity.MaximumEntries)
            blockers.Add(CharacterCreationLifestylesBlockers.PersistenceAuthorityRequired);

        LifestyleElement[] elements = EnumerateLifestyleElements(root, blockers).ToArray();
        List<CharacterCreationLifestyleProjection> projections = Project(
            elements,
            authority,
            blockers);
        CharacterCreationLifestyleBudget budget = ComputeBudget(root, projections);
        string contentDigest = CharacterCreationLifestyleReceiptLedgerIntegrity.ComputeContentDigest(
            workspace.Document.Content);
        string rulesDigest = CharacterCreationLifestylesDigestForApplication(new
        {
            CharacterCreationLifestylesSchemas.RulesV1,
            authority.ProfileDigest,
            authority.GmPolicyDigest,
            CharacterCreationLifestyleSourceAnchors.All
        });
        var binding = new CharacterCreationLifestyleBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.ContentRevision,
            workspace.SavedRevision,
            contentDigest,
            workspace.Document.AuxiliaryStateDigest,
            authority.SourceDigest,
            rulesDigest,
            authority.RuntimeDigest);
        return new AuthorityContext(
            workspace,
            document,
            root,
            created,
            authority,
            binding,
            projections,
            elements,
            budget,
            Normalize(blockers));
    }

    private static List<CharacterCreationLifestyleProjection> Project(
        IEnumerable<LifestyleElement> elements,
        CharacterCreationLifestylesAuthority authority,
        List<string> blockers)
    {
        var result = new List<CharacterCreationLifestyleProjection>();
        var identities = new HashSet<Guid>();
        foreach (LifestyleElement entry in elements)
        {
            if (!identities.Add(entry.Id))
            {
                blockers.Add(CharacterCreationLifestylesBlockers.DuplicateIdentity);
                continue;
            }
            if (!TryReadConfiguration(
                    entry.Element,
                    entry.Id,
                    authority,
                    out CharacterCreationLifestyleConfiguration configuration,
                    out string[] findings))
            {
                blockers.AddRange(findings);
                continue;
            }
            if (!CharacterCreationLifestylesRules.TryProject(
                    configuration,
                    authority,
                    out CharacterCreationLifestyleProjection projection,
                    out IReadOnlyList<string> projectionBlockers))
            {
                blockers.AddRange(findings);
                blockers.AddRange(projectionBlockers);
                continue;
            }
            result.Add(projection);
        }
        return result.OrderBy(item => item.Configuration.LifestyleId).ToList();
    }

    private static bool TryReadConfiguration(
        XElement lifestyle,
        Guid id,
        CharacterCreationLifestylesAuthority authority,
        out CharacterCreationLifestyleConfiguration configuration,
        out string[] blockers)
    {
        var findings = new List<string>();
        Guid[] sourceIds = lifestyle.Elements("sourceid")
            .Select(element => Guid.TryParse(element.Value.Trim(), out Guid parsed) ? parsed : Guid.Empty)
            .Distinct()
            .ToArray();
        CharacterCreationLifestyleCatalogOption? baseOption = sourceIds.Length == 1
            ? authority.LifestyleOptions.SingleOrDefault(option => option.SourceId == sourceIds[0])
            : null;
        if (baseOption is null)
            findings.Add(CharacterCreationLifestylesBlockers.SourceIdentityMismatch);
        else if (!baseOption.IsSelectable)
            findings.AddRange(baseOption.Blockers.Count == 0
                ? [CharacterCreationLifestylesBlockers.SourceDisabled]
                : baseOption.Blockers);

        var qualities = new List<CharacterCreationLifestyleQualitySelection>();
        var qualityIds = new HashSet<Guid>();
        foreach (XElement row in lifestyle.Element("lifestylequalities")?.Elements("lifestylequality") ?? [])
        {
            if (!Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid qualityId)
                || qualityId == Guid.Empty
                || !qualityIds.Add(qualityId)
                || !TryReadUniqueSourceId(row, out Guid qualitySourceId))
            {
                findings.Add(CharacterCreationLifestylesBlockers.InvalidIdentity);
                continue;
            }
            CharacterCreationLifestyleQualityCatalogOption? qualityOption = authority.QualityOptions
                .SingleOrDefault(option => option.SourceId == qualitySourceId);
            if (qualityOption is null)
            {
                findings.Add(CharacterCreationLifestylesBlockers.SourceIdentityMismatch);
                continue;
            }
            bool builtIn = ParseBool(ReadValue(row, "isfreegrid"))
                || string.Equals(
                    ReadValue(row, "lifestylequalitysource"),
                    "BuiltIn",
                    StringComparison.OrdinalIgnoreCase);
            qualities.Add(new CharacterCreationLifestyleQualitySelection(
                qualityId,
                qualityOption.OptionId,
                ReadValue(row, "extra"),
                ParseBool(ReadValue(row, "uselpcost")),
                ParseBool(ReadValue(row, "free")),
                builtIn));
        }

        string style = ReadValue(lifestyle, "type").ToLowerInvariant() switch
        {
            "advanced" => CharacterCreationLifestyleStyleIds.Advanced,
            "bolthole" => CharacterCreationLifestyleStyleIds.BoltHole,
            "safehouse" => CharacterCreationLifestyleStyleIds.Safehouse,
            "standard" or "" => CharacterCreationLifestyleStyleIds.Standard,
            _ => string.Empty
        };
        string increment = ReadValue(lifestyle, "increment").ToLowerInvariant() switch
        {
            "day" => CharacterCreationLifestyleIncrementIds.Day,
            "week" => CharacterCreationLifestyleIncrementIds.Week,
            "month" or "" => CharacterCreationLifestyleIncrementIds.Month,
            _ => string.Empty
        };
        int increments = 0;
        decimal percentage = 0m;
        int roommates = 0;
        int area = 0;
        int comforts = 0;
        int security = 0;
        int bonusLp = 0;
        bool numeric = TryReadInt(lifestyle, "months", out increments);
        numeric &= TryReadDecimal(lifestyle, "percentage", out percentage, 100m);
        numeric &= TryReadInt(lifestyle, "roommates", out roommates, 0);
        numeric &= TryReadInt(lifestyle, "area", out area, 0);
        numeric &= TryReadInt(lifestyle, "comforts", out comforts, 0);
        numeric &= TryReadInt(lifestyle, "security", out security, 0);
        numeric &= TryReadInt(lifestyle, "bonuslp", out bonusLp, 0);
        if (!numeric || string.IsNullOrWhiteSpace(style) || string.IsNullOrWhiteSpace(increment))
            findings.Add(CharacterCreationLifestylesBlockers.InvalidMutation);
        configuration = new CharacterCreationLifestyleConfiguration(
            id,
            baseOption?.OptionId ?? string.Empty,
            ReadValue(lifestyle, "name"),
            style,
            increment,
            increments,
            percentage,
            roommates,
            ParseBool(ReadValue(lifestyle, "splitcostwithroommates")),
            ParseBool(ReadValue(lifestyle, "trustfund")),
            area,
            comforts,
            security,
            bonusLp,
            ReadValue(lifestyle, "city"),
            ReadValue(lifestyle, "district"),
            ReadValue(lifestyle, "borough"),
            qualities);
        blockers = Normalize(findings);
        return blockers.Length == 0;
    }

    private static CharacterCreationLifestyleBudget ComputeBudget(
        XElement root,
        IReadOnlyList<CharacterCreationLifestyleProjection> lifestyles)
    {
        var blockers = new List<string>();
        if (!decimal.TryParse(
                ReadValue(root, "startingnuyen"),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal total)
            || total < 0m)
        {
            total = 0m;
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        }
        decimal used;
        try
        {
            used = lifestyles.Sum(item => item.Economics.TotalCost);
        }
        catch (OverflowException)
        {
            used = decimal.MaxValue;
            blockers.Add(CharacterCreationLifestylesBlockers.AuthorityUnavailable);
        }
        decimal remaining = used <= total ? total - used : 0m;
        decimal overspend = used > total ? used - total : 0m;
        return new CharacterCreationLifestyleBudget(
            total,
            used,
            remaining,
            overspend,
            IsExact: blockers.Count == 0,
            Normalize(blockers),
            [CharacterCreationLifestyleSourceAnchors.LegacyMonthlyCost]);
    }

    private static XElement BuildLifestyleElement(
        CharacterCreationLifestyleProjection projection,
        XElement? existing,
        CharacterCreationLifestylesAuthority authority)
    {
        CharacterCreationLifestyleConfiguration config = projection.Configuration;
        CharacterCreationLifestyleCatalogOption option = authority.LifestyleOptions.Single(item =>
            string.Equals(item.OptionId, config.BaseLifestyleOptionId, StringComparison.Ordinal));
        XElement lifestyle = existing is null ? new XElement("lifestyle") : new XElement(existing);
        SetSingle(lifestyle, "sourceid", option.SourceId.ToString("D"));
        SetSingle(lifestyle, "guid", config.LifestyleId.ToString("D"));
        SetSingle(lifestyle, "name", config.Name);
        SetSingle(lifestyle, "cost", option.BaseCost.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "dice", option.StartingNuyenDice.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "lp", option.LifestylePoints.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "baselifestyle", option.Name);
        SetSingle(lifestyle, "multiplier", option.StartingNuyenMultiplier.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "months", config.Increments.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "roommates", config.Roommates.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "percentage", config.Percentage.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "area", config.Area.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "comforts", config.Comforts.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "security", config.Security.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "basearea", option.BaseArea.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "basecomforts", option.BaseComforts.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "basesecurity", option.BaseSecurity.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "maxarea", option.MaximumArea.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "maxcomforts", option.MaximumComforts.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "maxsecurity", option.MaximumSecurity.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "costforarea", option.CostPerArea.ToString(CultureInfo.InvariantCulture));
        lifestyle.Elements("costforearea").Remove();
        SetSingle(lifestyle, "costforcomforts", option.CostPerComfort.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "costforsecurity", option.CostPerSecurity.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "allowbonuslp", option.AllowsBonusLifestylePoints.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "bonuslp", config.BonusLifestylePoints.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "source", option.SourceBook);
        SetSingle(lifestyle, "page", option.Page);
        SetSingle(lifestyle, "trustfund", config.TrustFund.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "splitcostwithroommates", config.SplitCostWithRoommates.ToString(CultureInfo.InvariantCulture));
        SetSingle(lifestyle, "type", LegacyStyle(config.StyleId));
        SetSingle(lifestyle, "increment", LegacyIncrement(config.IncrementId));
        SetSingle(lifestyle, "city", config.City);
        SetSingle(lifestyle, "district", config.District);
        SetSingle(lifestyle, "borough", config.Borough);

        XElement? existingQualities = lifestyle.Element("lifestylequalities");
        XElement qualities = existingQualities is null
            ? new XElement("lifestylequalities")
            : new XElement(existingQualities);
        Dictionary<Guid, XElement> existingById = (existingQualities?.Elements("lifestylequality") ?? [])
            .Where(row => Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid id) && id != Guid.Empty)
            .GroupBy(row => Guid.Parse(ReadValue(row, "guid")))
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.Single());
        qualities.Elements("lifestylequality").Remove();
        foreach (CharacterCreationLifestyleQualitySelection selection in config.Qualities.OrderBy(item => item.InstanceId))
        {
            CharacterCreationLifestyleQualityCatalogOption qualityOption = authority.QualityOptions.Single(item =>
                string.Equals(item.OptionId, selection.OptionId, StringComparison.Ordinal));
            XElement quality = existingById.TryGetValue(selection.InstanceId, out XElement? saved)
                ? new XElement(saved)
                : new XElement("lifestylequality");
            SetSingle(quality, "sourceid", qualityOption.SourceId.ToString("D"));
            SetSingle(quality, "guid", selection.InstanceId.ToString("D"));
            SetSingle(quality, "name", qualityOption.Name);
            SetSingle(quality, "category", qualityOption.Category);
            SetSingle(quality, "extra", selection.Extra);
            SetSingle(quality, "cost", qualityOption.FlatCost.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "multiplier", qualityOption.CostMultiplierPercent.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "basemultiplier", qualityOption.BaseCostMultiplierPercent.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "lp", qualityOption.LifestylePointCost.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "areamaximum", qualityOption.AreaMaximumModifier.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "comfortsmaximum", qualityOption.ComfortsMaximumModifier.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "securitymaximum", qualityOption.SecurityMaximumModifier.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "area", qualityOption.Area.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "comforts", qualityOption.Comforts.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "security", qualityOption.Security.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "uselpcost", selection.UseLifestylePoints.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "print", bool.TrueString);
            SetSingle(quality, "lifestylequalitytype", LegacyQualityType(qualityOption.QualityType));
            SetSingle(quality, "lifestylequalitysource", selection.IsBuiltIn ? "BuiltIn" : "Selected");
            SetSingle(quality, "free", selection.IsFree.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "isfreegrid", selection.IsBuiltIn.ToString(CultureInfo.InvariantCulture));
            SetSingle(quality, "source", qualityOption.SourceBook);
            SetSingle(quality, "page", qualityOption.Page);
            SetSingle(quality, "allowed", string.Join(',', qualityOption.AllowedFreeLifestyleNames));
            qualities.Add(quality);
        }
        if (existingQualities is null)
            lifestyle.Add(qualities);
        else
            existingQualities.ReplaceWith(qualities);
        return lifestyle;
    }

    private static IEnumerable<LifestyleElement> EnumerateLifestyleElements(
        XElement root,
        ICollection<string> blockers)
    {
        foreach (XElement row in root.Element("lifestyles")?.Elements("lifestyle") ?? [])
        {
            if (!Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid id) || id == Guid.Empty)
            {
                blockers.Add(CharacterCreationLifestylesBlockers.InvalidIdentity);
                continue;
            }
            yield return new LifestyleElement(id, row);
        }
    }

    private static string ComputeUntouchedSiblingDigest(XElement root, Guid targetId)
    {
        XElement? target = FindUniqueLifestyle(root, targetId);
        return CharacterCreationLifestylesDigestForApplication(
            (root.Element("lifestyles")?.Nodes() ?? [])
                .Where(node => !ReferenceEquals(node, target))
                .Where(node => node is not XText text || !string.IsNullOrWhiteSpace(text.Value))
                .Select(node => node.ToString(SaveOptions.DisableFormatting))
                .ToArray());
    }

    private static string ComputeNestedStateDigest(XElement lifestyle, IReadOnlySet<Guid> retainedQualityIds)
    {
        string[] qualityState = (lifestyle.Element("lifestylequalities")?.Elements("lifestylequality") ?? [])
            .Where(row => Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid id)
                          && retainedQualityIds.Contains(id))
            .Select(row => new
            {
                Id = ReadValue(row, "guid"),
                Attributes = row.Attributes().Select(attribute => attribute.ToString()).ToArray(),
                Unknown = row.Nodes()
                    .Where(node => node is not XElement element
                                   || !s_KnownQualityElements.Contains(element.Name.LocalName))
                    .Select(node => node.ToString(SaveOptions.DisableFormatting))
                    .ToArray()
            })
            .Where(item => item.Attributes.Length != 0 || item.Unknown.Length != 0)
            .Select(item => CharacterCreationLifestylesDigestForApplication(item))
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return CharacterCreationLifestylesDigestForApplication(new
        {
            Attributes = lifestyle.Attributes().Select(attribute => attribute.ToString()).ToArray(),
            Unknown = lifestyle.Nodes()
                .Where(node => node is not XElement element
                               || !s_KnownLifestyleElements.Contains(element.Name.LocalName))
                .Select(node => node.ToString(SaveOptions.DisableFormatting))
                .ToArray(),
            QualityState = qualityState
        });
    }

    private static XElement? FindUniqueLifestyle(XElement root, Guid id)
    {
        XElement[] matches = (root.Element("lifestyles")?.Elements("lifestyle") ?? [])
            .Where(row => Guid.TryParseExact(ReadValue(row, "guid"), "D", out Guid parsed) && parsed == id)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? CompareBinding(
        CharacterCreationLifestyleBinding current,
        CharacterCreationLifestyleBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.WorkspaceRevision != requested.WorkspaceRevision
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision
            || requested.WorkspaceRevision != requested.ContentRevision)
            return CharacterCreationLifestylesBlockers.StaleWorkspaceRevision;
        if (!CharacterCreationLifestylesRules.DigestsEqual(current.ContentDigest, requested.ContentDigest))
            return CharacterCreationLifestylesBlockers.StaleContentDigest;
        if (!string.Equals(current.AuxiliaryStateDigest, requested.AuxiliaryStateDigest, StringComparison.Ordinal))
            return CharacterCreationLifestylesBlockers.StaleAuxiliaryStateDigest;
        if (!CharacterCreationLifestylesRules.DigestsEqual(current.SourceDigest, requested.SourceDigest))
            return CharacterCreationLifestylesBlockers.StaleSourceDigest;
        if (!CharacterCreationLifestylesRules.DigestsEqual(current.RulesDigest, requested.RulesDigest))
            return CharacterCreationLifestylesBlockers.StaleRulesDigest;
        if (!CharacterCreationLifestylesRules.DigestsEqual(current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationLifestylesBlockers.StaleRuntimeDigest;
        return null;
    }

    private static CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>? ResolveReplay(
        WorkspaceStoredDocument workspace,
        string keyDigest,
        string commandDigest)
    {
        IReadOnlyList<CharacterCreationLifestyleReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationLifestyleReceipts ?? [];
        if (!CharacterCreationLifestyleReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
            return Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Corrupt,
                CharacterCreationLifestylesBlockers.ReceiptLedgerCorrupt);
        CharacterCreationLifestyleReceiptLedgerEntry? existing = ledger.FirstOrDefault(entry =>
            CharacterCreationLifestylesRules.DigestsEqual(entry.IdempotencyKeyDigest, keyDigest));
        if (existing is null)
            return null;
        return CharacterCreationLifestylesRules.DigestsEqual(existing.CommandDigest, commandDigest)
            ? new CharacterCreationLifestyleResult<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Replayed,
                existing.Receipt,
                [])
            : Blocked<CharacterCreationLifestyleReceipt>(
                CharacterCreationLifestyleOutcomes.Conflict,
                CharacterCreationLifestylesBlockers.IdempotencyConflict);
    }

    private static bool TryNormalizeIdempotencyKey(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumIdempotencyKeyLength
            && string.Equals(normalized, value, StringComparison.Ordinal)
            && normalized.All(character => char.IsLetterOrDigit(character)
                || character is '-' or '_' or '.' or ':' or '/');
    }

    private static bool TryReadUniqueSourceId(XElement row, out Guid sourceId)
    {
        Guid[] ids = row.Elements("sourceid")
            .Select(element => Guid.TryParse(element.Value.Trim(), out Guid parsed) ? parsed : Guid.Empty)
            .Distinct()
            .ToArray();
        sourceId = ids.Length == 1 ? ids[0] : Guid.Empty;
        return sourceId != Guid.Empty;
    }

    private static bool TryReadInt(XElement row, string name, out int value, int? fallback = null)
    {
        string text = ReadValue(row, name);
        if (text.Length == 0 && fallback.HasValue)
        {
            value = fallback.Value;
            return true;
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadDecimal(XElement row, string name, out decimal value, decimal? fallback = null)
    {
        string text = ReadValue(row, name);
        if (text.Length == 0 && fallback.HasValue)
        {
            value = fallback.Value;
            return true;
        }
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    private static void SetSingle(XElement parent, string name, string value)
    {
        XElement[] existing = parent.Elements(name).ToArray();
        if (existing.Length == 0)
            parent.Add(new XElement(name, value));
        else
        {
            existing[0].Value = value;
            foreach (XElement duplicate in existing.Skip(1))
                duplicate.Remove();
        }
    }

    private static XElement AddAndReturn(XElement parent, XElement child)
    {
        parent.Add(child);
        return child;
    }

    private static string LegacyStyle(string style) => style switch
    {
        CharacterCreationLifestyleStyleIds.Advanced => "Advanced",
        CharacterCreationLifestyleStyleIds.BoltHole => "BoltHole",
        CharacterCreationLifestyleStyleIds.Safehouse => "Safehouse",
        _ => "Standard"
    };

    private static string LegacyIncrement(string increment) => increment switch
    {
        CharacterCreationLifestyleIncrementIds.Day => "Day",
        CharacterCreationLifestyleIncrementIds.Week => "Week",
        _ => "Month"
    };

    private static string LegacyQualityType(string qualityType) => qualityType switch
    {
        CharacterCreationLifestyleQualityTypes.Entertainment => "Entertainment",
        CharacterCreationLifestyleQualityTypes.Negative => "Negative",
        CharacterCreationLifestyleQualityTypes.Contracts => "Contracts",
        _ => "Positive"
    };

    private static string CharacterCreationLifestylesDigestForApplication<T>(T value)
    {
        string canonical = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(value);
        return canonical.StartsWith("sha256:", StringComparison.Ordinal)
            ? canonical
            : "sha256:" + canonical;
    }

    private static string ReadValue(XElement parent, string name)
        => parent.Element(name)?.Value.Trim() ?? string.Empty;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
            || string.Equals(value, "1", StringComparison.Ordinal);

    private static string EmptyDigest()
        => CharacterCreationLifestylesDigestForApplication(Array.Empty<string>());

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static CharacterCreationLifestyleResult<T> ReadFailure<T>(WorkspaceStoreReadResult read)
        where T : class => Blocked<T>(
            read.Outcome switch
            {
                WorkspaceOperationOutcome.Missing => CharacterCreationLifestyleOutcomes.Missing,
                WorkspaceOperationOutcome.Corrupt => CharacterCreationLifestyleOutcomes.Corrupt,
                _ => CharacterCreationLifestyleOutcomes.Unavailable
            },
            CharacterCreationLifestylesBlockers.WorkspaceUnavailable);

    private static CharacterCreationLifestyleResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, blockers);

    private sealed record LifestyleElement(Guid Id, XElement Element);

    private sealed record AuthorityContext(
        WorkspaceStoredDocument Workspace,
        XDocument Document,
        XElement Root,
        bool CharacterCreated,
        CharacterCreationLifestylesAuthority Authority,
        CharacterCreationLifestyleBinding Binding,
        IReadOnlyList<CharacterCreationLifestyleProjection> Projections,
        IReadOnlyList<LifestyleElement> Elements,
        CharacterCreationLifestyleBudget Budget,
        IReadOnlyList<string> Blockers);

    private sealed record PreviewEvaluation(
        CharacterCreationLifestyleResult<CharacterCreationLifestylePreview> Result,
        WorkspaceStoredDocument? Workspace,
        string? ReplacementContent);
}
