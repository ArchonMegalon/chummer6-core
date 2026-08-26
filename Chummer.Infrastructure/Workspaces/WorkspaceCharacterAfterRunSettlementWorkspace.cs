using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;

namespace Chummer.Infrastructure.Workspaces;

/// <summary>
/// Durable saved-character adapter for governed SR5 After Run settlements. Run
/// proposal/review facts remain owned by the injected host source. The adapter
/// combines those facts with an exact saved-character projection and commits the
/// Chummer mutation plus immutable receipt ledger through one workspace CAS.
/// </summary>
public sealed class WorkspaceCharacterAfterRunSettlementWorkspace :
    ICharacterAfterRunSettlementWorkspace
{
    private const long MaximumCharacterXmlLength = 67_108_864;
    private static readonly ConcurrentDictionary<string, object> WorkspaceGates =
        new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions ComparisonJsonOptions = new();

    private readonly IWorkspaceStore _store;
    private readonly ICharacterAfterRunSettlementProposalProjectionSource _proposals;

    public WorkspaceCharacterAfterRunSettlementWorkspace(
        IWorkspaceStore store,
        ICharacterAfterRunSettlementProposalProjectionSource proposals)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _proposals = proposals ?? throw new ArgumentNullException(nameof(proposals));
    }

    public CharacterAfterRunSettlementWorkspaceReadResult Read(
        CharacterWorkspaceId workspaceId,
        CharacterAfterRunSettlementIdentity identity)
    {
        if (!IsValidWorkspaceId(workspaceId) || !IsValidIdentity(identity))
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                Error: "invalid_workspace_or_after_run_identity");
        }

        lock (Gate(workspaceId))
        {
            WorkspaceStoreReadResult read = ReadStore(workspaceId);
            if (!TryRequireSavedSr5Document(
                    read,
                    out WorkspaceStoredDocument saved,
                    out WorkspaceFailure failure))
            {
                return new(failure.Outcome, failure.Revision, Error: failure.Error);
            }

            try
            {
                XDocument document = ParseDocument(saved.Document.Content);
                IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ledger =
                    ReadLedger(saved);
                ProjectionResult projection = ProjectInput(
                    document,
                    saved,
                    identity,
                    ledger);
                return projection.Input is not null
                    ? new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Available,
                        saved.ContentRevision,
                        projection.Input)
                    : new(
                        projection.Outcome,
                        saved.ContentRevision,
                        Error: projection.Error);
            }
            catch (Exception error) when (IsProjectionFailure(error))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "after_run_workspace_projection_corrupt");
            }
        }
    }

    public CharacterAfterRunSettlementWorkspaceLookupResult Lookup(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
    {
        if (!IsValidWorkspaceId(workspaceId)
            || transactionId == Guid.Empty
            || !CharacterAfterRunSettlementRules.IsCanonicalDigest(commandDigest))
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                Error: "invalid_after_run_replay_lookup");
        }

        lock (Gate(workspaceId))
        {
            return LookupUnderGate(workspaceId, transactionId, commandDigest);
        }
    }

    public CharacterAfterRunSettlementWorkspaceCommitResult Commit(
        CharacterAfterRunSettlementWorkspaceCommitRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValidCommitRequest(request))
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                Error: "invalid_after_run_atomic_commit_request");
        }

        lock (Gate(request.WorkspaceId))
        {
            CharacterAfterRunSettlementWorkspaceLookupResult prior = LookupUnderGate(
                request.WorkspaceId,
                request.Plan.TransactionId,
                request.CommandDigest);
            if (prior.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Replayed)
            {
                return ReplayCommit(prior);
            }
            if (prior.Outcome != CharacterAfterRunSettlementWorkspaceOutcome.NotFound)
            {
                return CommitFromLookup(prior);
            }

            WorkspaceStoreReadResult read = ReadStore(request.WorkspaceId);
            if (!TryRequireSavedSr5Document(
                    read,
                    out WorkspaceStoredDocument saved,
                    out WorkspaceFailure failure))
            {
                return new(failure.Outcome, failure.Revision, Error: failure.Error);
            }
            if (saved.ContentRevision != request.ExpectedWorkspaceRevision)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                    saved.ContentRevision,
                    Error: "stale_workspace_revision");
            }

            try
            {
                XDocument document = ParseDocument(saved.Document.Content);
                IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ledger =
                    ReadLedger(saved);
                if (ledger.Count >=
                    CharacterAfterRunSettlementReceiptLedgerIntegrity.MaximumEntries)
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
                        saved.ContentRevision,
                        Error: "after_run_receipt_ledger_capacity_exhausted");
                }
                if (ledger.Any(entry =>
                        entry.ReviewedQuote.Identity.ProposalId
                            == request.Plan.Identity.ProposalId))
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                        saved.ContentRevision,
                        Error: "after_run_proposal_already_settled");
                }

                ProjectionResult projection = ProjectInput(
                    document,
                    saved,
                    request.Plan.Identity,
                    ledger);
                if (projection.Input is null)
                {
                    return new(
                        projection.Outcome,
                        saved.ContentRevision,
                        Error: projection.Error);
                }
                if (!CharacterAfterRunSettlementRules.TryCreateQuote(
                        projection.Input,
                        out CharacterAfterRunSettlementQuote current)
                    || !CanonicalEquals(current, request.ReviewedQuote)
                    || !CharacterAfterRunSettlementRules.TryCreatePlan(
                        current,
                        current.SourceDigest,
                        current.CustomDataDigest,
                        current.GmPolicyDigest,
                        current.RuntimeDigest,
                        current.LogicalDigest,
                        explicitlyConfirmed: true,
                        transactionIdAlreadyExists: false,
                        request.Plan.TransactionId,
                        out CharacterAfterRunSettlementPlan exactPlan)
                    || !CanonicalEquals(exactPlan, request.Plan))
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                        saved.ContentRevision,
                        Error: "stale_or_forged_after_run_plan");
                }

                string characterPayloadBefore = saved.Document.Content;
                ApplyPlan(document, exactPlan);
                CharacterAfterRunSettlementObservation observation = ObservePlan(
                    document,
                    exactPlan);
                if (!CharacterAfterRunSettlementRules.TryCreateReceipt(
                        exactPlan.TransactionId,
                        current,
                        exactPlan,
                        observation,
                        out CharacterAfterRunSettlementReceipt receipt))
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                        saved.ContentRevision,
                        Error: "after_run_receipt_creation_failed");
                }

                long committedRevision = checked(saved.ContentRevision + 1);
                string characterPayloadAfter = Serialize(document);
                if (!CharacterAfterRunSettlementReceiptLedgerIntegrity.TryCreateEntry(
                    request.WorkspaceId,
                    saved.ContentRevision,
                    committedRevision,
                    request.CommandDigest,
                    characterPayloadBefore,
                    characterPayloadAfter,
                    current,
                    receipt,
                    out CharacterAfterRunSettlementReceiptLedgerEntry ledgerEntry))
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                        saved.ContentRevision,
                        Error: "after_run_receipt_ledger_entry_invalid");
                }
                CharacterAfterRunSettlementReceiptLedgerEntry[] replacementLedger =
                    [.. ledger, ledgerEntry];
                WorkspaceDocument replacement = new(
                    saved.Document.State with
                    {
                        Payload = characterPayloadAfter,
                        AuxiliaryState = saved.Document.AuxiliaryState with
                        {
                            CharacterAfterRunSettlementReceipts = replacementLedger
                        }
                    },
                    saved.Document.Format);

                WorkspaceStoreMutationResult committed;
                try
                {
                    if (_store is not IWorkspaceAuxiliaryStateAtomicCommitCapability
                        { SupportsWorkspaceAuxiliaryStateAtomicCommit: true } atomicStore)
                    {
                        return new(
                            CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
                            saved.ContentRevision,
                            Error: "after_run_atomic_auxiliary_state_commit_unavailable");
                    }
                    committed = atomicStore.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                        request.WorkspaceId,
                        request.ExpectedWorkspaceRevision,
                        saved.Document.AuxiliaryStateDigest,
                        replacement);
                }
                catch (Exception error) when (IsStoreFailure(error))
                {
                    return RecoverUnknownCommit(request, error.Message);
                }

                if (committed.Success
                    && committed.Entry is { } entry
                    && entry.ContentRevision == committedRevision
                    && entry.SavedRevision == committedRevision)
                {
                    return new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Applied,
                        committedRevision,
                        request.CommandDigest,
                        current,
                        receipt);
                }

                CharacterAfterRunSettlementWorkspaceCommitResult recovered =
                    RecoverUnknownCommit(request, committed.Error);
                if (recovered.Outcome is
                    CharacterAfterRunSettlementWorkspaceOutcome.Replayed
                    or CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict
                    or CharacterAfterRunSettlementWorkspaceOutcome.Corrupt
                    or CharacterAfterRunSettlementWorkspaceOutcome.Missing)
                {
                    return recovered;
                }
                return committed.Outcome switch
                {
                    WorkspaceOperationOutcome.Conflict => new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                        recovered.CurrentWorkspaceRevision,
                        Error: committed.Error ?? "workspace_revision_conflict"),
                    WorkspaceOperationOutcome.Missing => new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Missing,
                        Error: committed.Error ?? "workspace_missing"),
                    WorkspaceOperationOutcome.Corrupt => new(
                        CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                        recovered.CurrentWorkspaceRevision,
                        Error: committed.Error ?? "workspace_corrupt"),
                    _ => recovered
                };
            }
            catch (OverflowException)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "workspace_revision_exhausted");
            }
            catch (Exception error) when (IsProjectionFailure(error))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    saved.ContentRevision,
                    Error: "after_run_atomic_projection_corrupt");
            }
        }
    }

    private CharacterAfterRunSettlementWorkspaceCommitResult RecoverUnknownCommit(
        CharacterAfterRunSettlementWorkspaceCommitRequest request,
        string? error)
    {
        CharacterAfterRunSettlementWorkspaceLookupResult lookup = LookupUnderGate(
            request.WorkspaceId,
            request.Plan.TransactionId,
            request.CommandDigest);
        if (lookup.Outcome == CharacterAfterRunSettlementWorkspaceOutcome.Replayed)
        {
            return ReplayCommit(lookup);
        }
        if (lookup.Outcome is
            CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict
            or CharacterAfterRunSettlementWorkspaceOutcome.Corrupt
            or CharacterAfterRunSettlementWorkspaceOutcome.Missing)
        {
            return CommitFromLookup(lookup);
        }
        return new(
            CharacterAfterRunSettlementWorkspaceOutcome.Indeterminate,
            lookup.CurrentWorkspaceRevision,
            Error: error ?? lookup.Error ?? "after_run_atomic_commit_outcome_unknown");
    }

    private CharacterAfterRunSettlementWorkspaceLookupResult LookupUnderGate(
        CharacterWorkspaceId workspaceId,
        Guid transactionId,
        string commandDigest)
    {
        WorkspaceStoreReadResult read = ReadStore(workspaceId);
        if (!TryRequireSavedSr5Document(
                read,
                out WorkspaceStoredDocument saved,
                out WorkspaceFailure failure))
        {
            return new(failure.Outcome, failure.Revision, Error: failure.Error);
        }

        try
        {
            _ = ParseDocument(saved.Document.Content);
            IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ledger =
                ReadLedger(saved);
            CharacterAfterRunSettlementReceiptLedgerEntry? match = ledger.SingleOrDefault(
                entry => entry.TransactionId == transactionId);
            if (match is null)
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.NotFound,
                    saved.ContentRevision);
            }
            if (!FixedEquals(match.CommandDigest, commandDigest))
            {
                return new(
                    CharacterAfterRunSettlementWorkspaceOutcome.IdempotencyConflict,
                    saved.ContentRevision,
                    match.CommandDigest,
                    Error: "transaction_id_claimed_by_different_command");
            }
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Replayed,
                saved.ContentRevision,
                match.CommandDigest,
                match.ReviewedQuote,
                match.Receipt);
        }
        catch (Exception error) when (IsProjectionFailure(error))
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                saved.ContentRevision,
                Error: "after_run_receipt_ledger_corrupt");
        }
    }

    private ProjectionResult ProjectInput(
        XDocument document,
        WorkspaceStoredDocument saved,
        CharacterAfterRunSettlementIdentity identity,
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ledger)
    {
        XElement root = RequireCharacterRoot(document);
        string characterDigest = PayloadDigest(saved.Document.Content);
        CharacterAfterRunSettlementProposalProjectionResult source;
        try
        {
            source = _proposals.Read(
                new CharacterAfterRunSettlementProposalProjectionRequest(
                    saved.Id,
                    saved.ContentRevision,
                    identity,
                    characterDigest));
        }
        catch (Exception error) when (IsStoreFailure(error))
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
                Error: "after_run_proposal_source_unavailable");
        }

        if (source is null)
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
                Error: "after_run_proposal_source_unavailable");
        }
        if (source.Outcome !=
            CharacterAfterRunSettlementProposalProjectionOutcome.Available)
        {
            return new(
                source.Outcome switch
                {
                    CharacterAfterRunSettlementProposalProjectionOutcome.NotFound
                        => CharacterAfterRunSettlementWorkspaceOutcome.NotFound,
                    CharacterAfterRunSettlementProposalProjectionOutcome.Conflict
                        => CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                    CharacterAfterRunSettlementProposalProjectionOutcome.Corrupt
                        => CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    _ => CharacterAfterRunSettlementWorkspaceOutcome.Unavailable
                },
                Error: source.Error ?? "after_run_proposal_projection_unavailable");
        }
        if (source.WorkspaceId != saved.Id
            || source.WorkspaceRevision != saved.ContentRevision
            || !FixedEquals(source.CharacterProjectionDigest, characterDigest)
            || source.Projection is not { } proposal
            || proposal.Identity != identity)
        {
            return new(
                CharacterAfterRunSettlementWorkspaceOutcome.Conflict,
                Error: "stale_or_detached_after_run_proposal_projection");
        }

        var input = new CharacterAfterRunSettlementInput(
            identity,
            Created: ReadRequiredBool(root, "created"),
            saved.Document.RulesetId,
            proposal.TargetOwnedByCharacter,
            proposal.ProjectionIsExact,
            proposal.RunCompleted,
            ProposalAlreadySettled: ledger.Any(entry =>
                entry.ReviewedQuote.Identity.ProposalId == identity.ProposalId),
            proposal.ExpectedGmActorId,
            proposal.ExpectedOwnerActorId,
            proposal.CurrentHeat,
            ReadRequiredNonNegativeInt(root, "streetcred"),
            ReadRequiredNonNegativeInt(root, "notoriety"),
            ReadRequiredNonNegativeInt(root, "publicawareness"),
            ReadRequiredNonNegativeInt(root, "karma"),
            proposal.HeatDelta,
            proposal.StreetCredDelta,
            proposal.NotorietyDelta,
            proposal.PublicAwarenessDelta,
            proposal.Settings,
            proposal.ContactProposals,
            proposal.GmReview,
            proposal.OwnerReview,
            proposal.RawSourceState,
            proposal.RawCustomDataState,
            proposal.RawGmPolicyState,
            proposal.RawRuntimeState);
        return CharacterAfterRunSettlementRules.TryCreateQuote(input, out _)
            ? new(CharacterAfterRunSettlementWorkspaceOutcome.Available, input)
            : new(
                CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                Error: "after_run_combined_projection_invalid");
    }

    private static void ApplyPlan(
        XDocument document,
        CharacterAfterRunSettlementPlan plan)
    {
        XElement root = RequireCharacterRoot(document);
        SetRequiredValue(root, "streetcred", plan.TargetStreetCred);
        SetRequiredValue(root, "notoriety", plan.TargetNotoriety);
        SetRequiredValue(root, "publicawareness", plan.TargetPublicAwareness);
        SetRequiredValue(root, "karma", plan.TargetKarma);
        AddContacts(root, plan.ContactsToAdd);
        AddExpense(root, plan);
    }

    private static void AddContacts(
        XElement root,
        IReadOnlyList<CharacterAfterRunContactSettlement> contactsToAdd)
    {
        XElement contacts = GetOrCreateSingleContainer(root, "contacts");
        HashSet<Guid> existing = [];
        foreach (XElement contact in contacts.Elements("contact"))
        {
            if (!existing.Add(ReadRequiredGuid(contact, "guid")))
            {
                throw new InvalidOperationException("duplicate_contact_identity");
            }
        }

        foreach (CharacterAfterRunContactSettlement contact in contactsToAdd)
        {
            if (!existing.Add(contact.ContactId))
            {
                throw new InvalidOperationException("after_run_contact_already_exists");
            }
            contacts.Add(new XElement(
                "contact",
                new XElement("name", contact.Name),
                new XElement("role", contact.Role),
                new XElement("location", contact.Location),
                new XElement("connection", contact.Connection.ToString(CultureInfo.InvariantCulture)),
                new XElement("loyalty", contact.Loyalty.ToString(CultureInfo.InvariantCulture)),
                new XElement("metatype", string.Empty),
                new XElement("gender", string.Empty),
                new XElement("age", string.Empty),
                new XElement("contacttype", string.Empty),
                new XElement("preferredpayment", string.Empty),
                new XElement("hobbiesvice", string.Empty),
                new XElement("personallife", string.Empty),
                new XElement("type", "Contact"),
                new XElement("file", string.Empty),
                new XElement("relative", string.Empty),
                new XElement("notes", string.Empty),
                new XElement("notesColor", "#000000"),
                new XElement("groupname", string.Empty),
                new XElement("colour", "-16777216"),
                new XElement("group", "False"),
                new XElement("family", "False"),
                new XElement("blackmail", "False"),
                new XElement(
                    "free",
                    contact.Kind == CharacterAfterRunContactProposalKind.RunReward
                        ? "True"
                        : "False"),
                new XElement("groupenabled", "False"),
                new XElement("guid", contact.ContactId.ToString("D"))));
        }
    }

    private static void AddExpense(
        XElement root,
        CharacterAfterRunSettlementPlan plan)
    {
        XElement expenses = GetOrCreateSingleContainer(root, "expenses");
        HashSet<Guid> existing = [];
        foreach (XElement expense in expenses.Elements("expense"))
        {
            if (!existing.Add(ReadRequiredGuid(expense, "guid")))
            {
                throw new InvalidOperationException("duplicate_expense_identity");
            }
        }
        if (plan.ExpenseAmount == 0)
        {
            return;
        }
        if (!existing.Add(plan.ExpenseId))
        {
            throw new InvalidOperationException("after_run_expense_already_exists");
        }

        expenses.Add(new XElement(
            "expense",
            new XElement("guid", plan.ExpenseId.ToString("D")),
            new XElement(
                "date",
                DateTime.Now.ToString("s", CultureInfo.InvariantCulture)),
            new XElement("amount", plan.ExpenseAmount.ToString(CultureInfo.InvariantCulture)),
            new XElement("reason", plan.ExpenseReason),
            new XElement("type", "Karma"),
            new XElement("refund", "False"),
            new XElement("forcecareervisible", "True"),
            new XElement(
                "undo",
                new XElement("karmatype", "ManualSubtract"),
                new XElement("nuyentype", "AddCyberware"),
                new XElement("objectid", string.Empty),
                new XElement("qty", "0"),
                new XElement("extra", string.Empty))));
    }

    private static CharacterAfterRunSettlementObservation ObservePlan(
        XDocument document,
        CharacterAfterRunSettlementPlan plan)
    {
        XElement root = RequireCharacterRoot(document);
        CharacterAfterRunContactSettlement[] contacts = ObserveContacts(
            root,
            plan.ContactsToAdd);
        CharacterAfterRunExpenseObservation expense = ObserveExpense(root, plan);
        return new CharacterAfterRunSettlementObservation(
            MatchingTransactionCount: 1,
            plan.TargetHeat,
            ReadRequiredNonNegativeInt(root, "streetcred"),
            ReadRequiredNonNegativeInt(root, "notoriety"),
            ReadRequiredNonNegativeInt(root, "publicawareness"),
            ReadRequiredNonNegativeInt(root, "karma"),
            contacts,
            expense,
            plan.ExpectedSourceDigest,
            plan.ExpectedCustomDataDigest,
            plan.ExpectedGmPolicyDigest,
            plan.ExpectedRuntimeDigest);
    }

    private static CharacterAfterRunContactSettlement[] ObserveContacts(
        XElement root,
        IReadOnlyList<CharacterAfterRunContactSettlement> expected)
    {
        XElement contacts = RequireSingle(root, "contacts");
        List<CharacterAfterRunContactSettlement> observed = [];
        foreach (CharacterAfterRunContactSettlement contact in expected)
        {
            XElement[] matches = contacts.Elements("contact")
                .Where(candidate => ReadRequiredGuid(candidate, "guid") == contact.ContactId)
                .Take(2)
                .ToArray();
            if (matches.Length != 1)
            {
                throw new InvalidOperationException("after_run_contact_observation_not_unique");
            }
            XElement match = matches[0];
            var projected = contact with
            {
                Name = ReadRequiredText(match, "name"),
                Role = ReadBoundedText(match, "role"),
                Location = ReadBoundedText(match, "location"),
                Connection = ReadRequiredNonNegativeInt(match, "connection"),
                Loyalty = ReadRequiredNonNegativeInt(match, "loyalty")
            };
            if (projected != contact)
            {
                throw new InvalidOperationException("after_run_contact_observation_mismatch");
            }
            observed.Add(projected);
        }
        return observed.OrderBy(static contact => contact.ContactId).ToArray();
    }

    private static CharacterAfterRunExpenseObservation ObserveExpense(
        XElement root,
        CharacterAfterRunSettlementPlan plan)
    {
        if (plan.ExpenseAmount == 0)
        {
            return new CharacterAfterRunExpenseObservation(
                0,
                Guid.Empty,
                0,
                string.Empty,
                string.Empty,
                Refund: false);
        }

        XElement expenses = RequireSingle(root, "expenses");
        XElement[] matches = expenses.Elements("expense")
            .Where(expense => ReadRequiredGuid(expense, "guid") == plan.ExpenseId)
            .Take(2)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("after_run_expense_observation_not_unique");
        }
        XElement match = matches[0];
        return new CharacterAfterRunExpenseObservation(
            1,
            plan.ExpenseId,
            ReadRequiredInt(match, "amount"),
            ReadRequiredText(match, "reason"),
            ReadRequiredText(match, "type"),
            ReadRequiredBool(match, "refund"));
    }

    private static IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ReadLedger(
        WorkspaceStoredDocument saved)
    {
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry>? persisted =
            saved.Document.AuxiliaryState.CharacterAfterRunSettlementReceipts;
        if (!CharacterAfterRunSettlementReceiptLedgerIntegrity.IsValidLedger(
                saved.Id,
                saved.ContentRevision,
                persisted))
        {
            throw new InvalidOperationException("after_run_receipt_ledger_corrupt");
        }
        IReadOnlyList<CharacterAfterRunSettlementReceiptLedgerEntry> ledger =
            persisted ?? [];
        if (ledger.Count > 0
            && ledger[^1].CommittedWorkspaceRevision == saved.ContentRevision
            && !FixedEquals(
                ledger[^1].CharacterPayloadDigestAfter,
                PayloadDigest(saved.Document.Content)))
        {
            throw new InvalidOperationException("after_run_latest_character_binding_invalid");
        }
        return ledger;
    }

    private WorkspaceStoreReadResult ReadStore(CharacterWorkspaceId workspaceId)
    {
        try
        {
            return _store.Get(workspaceId);
        }
        catch (Exception error) when (IsStoreFailure(error))
        {
            return new WorkspaceStoreReadResult(
                WorkspaceOperationOutcome.Unavailable,
                Error: "workspace_read_unavailable");
        }
    }

    private static bool TryRequireSavedSr5Document(
        WorkspaceStoreReadResult read,
        out WorkspaceStoredDocument saved,
        out WorkspaceFailure failure)
    {
        saved = null!;
        failure = default;
        if (!read.Success || read.Value is not { } value)
        {
            failure = new WorkspaceFailure(
                read.Outcome switch
                {
                    WorkspaceOperationOutcome.Missing
                        => CharacterAfterRunSettlementWorkspaceOutcome.Missing,
                    WorkspaceOperationOutcome.Corrupt
                        => CharacterAfterRunSettlementWorkspaceOutcome.Corrupt,
                    _ => CharacterAfterRunSettlementWorkspaceOutcome.Unavailable
                },
                0,
                read.Error ?? "workspace_read_failed");
            return false;
        }
        if (value.ContentRevision <= 0
            || value.SavedRevision != value.ContentRevision
            || value.Document.Format != WorkspaceDocumentFormat.NativeXml
            || !string.Equals(
                value.Document.RulesetId,
                CharacterAfterRunSettlementRules.RulesetId,
                StringComparison.Ordinal))
        {
            failure = new WorkspaceFailure(
                CharacterAfterRunSettlementWorkspaceOutcome.Unavailable,
                value.ContentRevision,
                "workspace_is_not_a_clean_saved_sr5_runner");
            return false;
        }
        saved = value;
        return true;
    }

    private static XDocument ParseDocument(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml) || xml.Length > MaximumCharacterXmlLength)
        {
            throw new InvalidOperationException("character_xml_size_invalid");
        }
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumCharacterXmlLength
        };
        using var stringReader = new StringReader(xml);
        using XmlReader reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static XElement RequireCharacterRoot(XDocument document)
    {
        XElement? root = document.Root;
        if (root is null
            || root.Name.LocalName != "character"
            || root.Name.Namespace != XNamespace.None)
        {
            throw new InvalidOperationException("character_root_invalid");
        }
        return root;
    }

    private static XElement RequireSingle(XElement parent, string name)
    {
        XElement[] matches = parent.Elements(name).Take(2).ToArray();
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException($"missing_or_duplicate_{name}");
    }

    private static XElement GetOrCreateSingleContainer(XElement root, string name)
    {
        XElement[] matches = root.Elements(name).Take(2).ToArray();
        XElement container = matches.Length switch
        {
            0 => new XElement(name),
            1 => matches[0],
            _ => throw new InvalidOperationException($"duplicate_{name}_container")
        };
        if (container.Parent is null)
        {
            root.Add(container);
        }
        return container;
    }

    private static void SetRequiredValue(XElement parent, string name, int value)
        => RequireSingle(parent, name).Value = value.ToString(CultureInfo.InvariantCulture);

    private static string ReadRequiredText(XElement parent, string name)
    {
        string value = ReadBoundedText(parent, name);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");
    }

    private static string ReadBoundedText(XElement parent, string name)
    {
        string value = RequireSingle(parent, name).Value;
        return value.Length <= CharacterAfterRunSettlementRules.MaximumTextLength
            ? value
            : throw new InvalidOperationException($"invalid_{name}");
    }

    private static int ReadRequiredNonNegativeInt(XElement parent, string name)
    {
        int value = ReadRequiredInt(parent, name);
        return value is >= 0 and <= CharacterAfterRunSettlementRules.MaximumValue
            ? value
            : throw new InvalidOperationException($"invalid_{name}");
    }

    private static int ReadRequiredInt(XElement parent, string name)
        => int.TryParse(
            RequireSingle(parent, name).Value.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out int value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");

    private static Guid ReadRequiredGuid(XElement parent, string name)
        => Guid.TryParseExact(RequireSingle(parent, name).Value.Trim(), "D", out Guid value)
           && value != Guid.Empty
            ? value
            : throw new InvalidOperationException($"invalid_{name}");

    private static bool ReadRequiredBool(XElement parent, string name)
        => bool.TryParse(RequireSingle(parent, name).Value.Trim(), out bool value)
            ? value
            : throw new InvalidOperationException($"invalid_{name}");

    private static string PayloadDigest(string payload)
        => Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(payload ?? string.Empty)));

    private static string Serialize(XDocument document)
        => document.ToString(SaveOptions.DisableFormatting);

    private static bool CanonicalEquals<T>(T left, T right)
        => FixedEquals(
            Convert.ToHexStringLower(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(left, ComparisonJsonOptions))),
            Convert.ToHexStringLower(
                SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(right, ComparisonJsonOptions))));

    private static bool IsValidCommitRequest(
        CharacterAfterRunSettlementWorkspaceCommitRequest request)
        => IsValidWorkspaceId(request.WorkspaceId)
            && request.ExpectedWorkspaceRevision is > 0 and < long.MaxValue
            && CharacterAfterRunSettlementRules.IsCanonicalDigest(request.CommandDigest)
            && CharacterAfterRunSettlementRules.IsCoherent(request.ReviewedQuote)
            && CharacterAfterRunSettlementRules.IsCoherent(request.Plan)
            && request.ReviewedQuote.Identity == request.Plan.Identity
            && request.Plan.TransactionId != Guid.Empty;

    private static bool IsValidWorkspaceId(CharacterWorkspaceId workspaceId)
        => CharacterAfterRunSettlementServiceIntegrity.IsValidWorkspaceId(workspaceId);

    private static bool IsValidIdentity(CharacterAfterRunSettlementIdentity identity)
        => identity.ProposalId != Guid.Empty
            && identity.RunId != Guid.Empty
            && identity.CharacterId != Guid.Empty
            && identity.ProposalId != identity.RunId
            && identity.ProposalId != identity.CharacterId;

    private static object Gate(CharacterWorkspaceId workspaceId)
        => WorkspaceGates.GetOrAdd(workspaceId.Value, static _ => new object());

    private static CharacterAfterRunSettlementWorkspaceCommitResult ReplayCommit(
        CharacterAfterRunSettlementWorkspaceLookupResult lookup)
        => new(
            CharacterAfterRunSettlementWorkspaceOutcome.Replayed,
            lookup.CurrentWorkspaceRevision,
            lookup.ExistingCommandDigest,
            lookup.ReviewedQuote,
            lookup.Receipt,
            lookup.Error);

    private static CharacterAfterRunSettlementWorkspaceCommitResult CommitFromLookup(
        CharacterAfterRunSettlementWorkspaceLookupResult lookup)
        => new(
            lookup.Outcome,
            lookup.CurrentWorkspaceRevision,
            lookup.ExistingCommandDigest,
            lookup.ReviewedQuote,
            lookup.Receipt,
            lookup.Error);

    private static bool FixedEquals(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }
        byte[] leftBytes = Encoding.UTF8.GetBytes(left);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool IsProjectionFailure(Exception error)
        => error is InvalidOperationException
            or JsonException
            or XmlException
            or FormatException
            or ArgumentException;

    private static bool IsStoreFailure(Exception error)
        => error is IOException
            or HttpRequestException
            or UnauthorizedAccessException
            or TimeoutException
            or InvalidOperationException;

    private readonly record struct WorkspaceFailure(
        CharacterAfterRunSettlementWorkspaceOutcome Outcome,
        long Revision,
        string Error);

    private sealed record ProjectionResult(
        CharacterAfterRunSettlementWorkspaceOutcome Outcome,
        CharacterAfterRunSettlementInput? Input = null,
        string? Error = null);

}
