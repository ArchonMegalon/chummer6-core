using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

public sealed class CharacterCreationContactsService : ICharacterCreationContactsService
{
    private const int MaximumTextLength = 32_767;
    private const int MinimumConnection = 1;
    private const int MinimumLoyalty = 1;
    private const int MaximumLoyalty = 6;
    private const int MaximumIdempotencyKeyLength = 200;

    private static readonly string[] s_IdentityElementNames =
    [
        "name", "role", "location", "notes", "extra", "metatype", "gender",
        "age", "contacttype", "preferredpayment", "hobbiesvice", "personallife", "groupname"
    ];

    private static readonly HashSet<string> s_EditableElementNames = new(
        s_IdentityElementNames.Concat(
            ["connection", "loyalty", "group", "free", "family", "blackmail"]),
        StringComparer.Ordinal);

    private readonly IWorkspaceStore _workspaceStore;

    public CharacterCreationContactsService(IWorkspaceStore workspaceStore)
    {
        _workspaceStore = workspaceStore ?? throw new ArgumentNullException(nameof(workspaceStore));
    }

    public CharacterCreationContactResult<CharacterCreationContactsState> Load(
        CharacterCreationContactsLoadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            return ReadFailure<CharacterCreationContactsState>(read);
        }

        AuthorityContext context = BuildContext(workspace);
        string[] blockers = context.AuthorityBlockers
            .Concat(context.ContactBudget.Blockers)
            .Concat(context.HighPlacesBudget.Blockers)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var state = new CharacterCreationContactsState(
            CharacterCreationContactsSchemas.StateV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            context.Binding,
            context.CharacterCreated,
            context.Contacts,
            context.ContactBudget,
            context.HighPlacesBudget,
            blockers,
            CanEdit: context.AuthorityBlockers.Count == 0,
            SnapshotDigest: string.Empty);
        state = state with
        {
            SnapshotDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                state with { SnapshotDigest = string.Empty })
        };
        return new CharacterCreationContactResult<CharacterCreationContactsState>(
            CharacterCreationContactOutcomes.Available,
            state,
            blockers);
    }

    public CharacterCreationContactResult<CharacterCreationContactPreview> Preview(
        CharacterCreationContactPreviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Edit is null)
        {
            return Blocked<CharacterCreationContactPreview>(
                CharacterCreationContactOutcomes.Invalid,
                CharacterCreationContactsBlockers.MutationInvalid);
        }
        return EvaluatePreview(request).Result;
    }

    public CharacterCreationContactResult<CharacterCreationContactReceipt> Confirm(
        CharacterCreationContactConfirmRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Binding is null || request.Edit is null)
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Invalid,
                CharacterCreationContactsBlockers.MutationInvalid);
        }
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string idempotencyKey))
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Invalid,
                CharacterCreationContactsBlockers.IdempotencyKeyInvalid);
        }

        string idempotencyDigest = ComputeIdempotencyDigest(idempotencyKey);
        string commandDigest = ComputeCommandDigest(request);
        WorkspaceStoreReadResult initialRead = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!initialRead.Success || initialRead.Value is not WorkspaceStoredDocument initialWorkspace)
        {
            return ReadFailure<CharacterCreationContactReceipt>(initialRead);
        }

        CharacterCreationContactResult<CharacterCreationContactReceipt>? replay = ResolveReplay(
            initialWorkspace,
            idempotencyDigest,
            commandDigest);
        if (replay is not null)
        {
            return replay;
        }
        if (!request.ExplicitlyConfirmed)
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Blocked,
                CharacterCreationContactsBlockers.ExplicitConfirmationRequired);
        }

        PreviewEvaluation evaluation = EvaluatePreview(
            new CharacterCreationContactPreviewRequest(request.Binding, request.Edit));
        if (evaluation.Result.Value is not CharacterCreationContactPreview preview
            || evaluation.Workspace is not WorkspaceStoredDocument workspace
            || evaluation.ReplacementContent is null)
        {
            return new CharacterCreationContactResult<CharacterCreationContactReceipt>(
                evaluation.Result.Outcome,
                null,
                evaluation.Result.Blockers);
        }
        if (!FixedEquals(preview.PreviewDigest, request.PreviewDigest))
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Conflict,
                CharacterCreationContactsBlockers.PreviewDigestMismatch);
        }
        if (!preview.CanConfirm || preview.Blockers.Count != 0)
        {
            return new CharacterCreationContactResult<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Blocked,
                null,
                preview.Blockers);
        }
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            } atomicStore)
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Unavailable,
                CharacterCreationContactsBlockers.PersistenceAuthorityRequired);
        }
        if (workspace.ContentRevision == long.MaxValue)
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Unavailable,
                CharacterCreationContactsBlockers.PersistenceAuthorityRequired);
        }

        long nextRevision = workspace.ContentRevision + 1;
        string receiptId = "creation-contact-" + commandDigest["sha256:".Length..][..24];
        var receipt = new CharacterCreationContactReceipt(
            CharacterCreationContactsSchemas.ReceiptV1,
            receiptId,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            workspace.Id,
            request.Edit.ContactId,
            idempotencyDigest,
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
            preview.ContactBudgetBefore.Used,
            preview.ContactBudgetAfter.Used,
            preview.ContactBudgetAfter.Remaining,
            preview.HighPlacesBudgetBefore.Used,
            preview.HighPlacesBudgetAfter.Used,
            preview.HighPlacesBudgetAfter.Remaining,
            preview.WritePlan,
            ReceiptDigest: string.Empty);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationContactReceiptLedgerIntegrity.ComputeReceiptDigest(receipt)
        };

        CharacterCreationContactReceiptLedgerEntry[] receipts =
        [
            .. workspace.Document.AuxiliaryState.CharacterCreationContactReceipts ?? [],
            new CharacterCreationContactReceiptLedgerEntry(
                idempotencyDigest,
                commandDigest,
                receipt)
        ];
        WorkspaceDocument replacement = workspace.Document with
        {
            State = workspace.Document.State with
            {
                Payload = evaluation.ReplacementContent,
                AuxiliaryState = workspace.Document.AuxiliaryState with
                {
                    CharacterCreationContactReceipts = receipts
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
            return new CharacterCreationContactResult<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Applied,
                receipt,
                []);
        }

        if (committed.Outcome == WorkspaceOperationOutcome.Conflict)
        {
            WorkspaceStoreReadResult racedRead = _workspaceStore.Get(workspace.Id);
            if (racedRead.Success && racedRead.Value is WorkspaceStoredDocument racedWorkspace)
            {
                CharacterCreationContactResult<CharacterCreationContactReceipt>? racedReplay =
                    ResolveReplay(racedWorkspace, idempotencyDigest, commandDigest);
                if (racedReplay is not null)
                {
                    return racedReplay;
                }
            }
        }

        return Blocked<CharacterCreationContactReceipt>(
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationContactOutcomes.Conflict
                : CharacterCreationContactOutcomes.Unavailable,
            committed.Outcome == WorkspaceOperationOutcome.Conflict
                ? CharacterCreationContactsBlockers.StaleWorkspaceRevision
                : CharacterCreationContactsBlockers.PersistenceAuthorityRequired);
    }

    public CharacterCreationContactResult<CharacterCreationContactReceipt> LookupReceipt(
        CharacterCreationContactReceiptLookupRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!TryNormalizeIdempotencyKey(request.IdempotencyKey, out string idempotencyKey))
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Invalid,
                CharacterCreationContactsBlockers.IdempotencyKeyInvalid);
        }

        WorkspaceStoreReadResult read = _workspaceStore.Get(request.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            return ReadFailure<CharacterCreationContactReceipt>(read);
        }
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationContactReceipts ?? [];
        if (!CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Corrupt,
                CharacterCreationContactsBlockers.ReceiptLedgerCorrupt);
        }

        string digest = ComputeIdempotencyDigest(idempotencyKey);
        CharacterCreationContactReceiptLedgerEntry? found = ledger.FirstOrDefault(entry =>
            FixedEquals(entry.IdempotencyKeyDigest, digest));
        return found is null
            ? Blocked<CharacterCreationContactReceipt>(CharacterCreationContactOutcomes.NotFound)
            : new CharacterCreationContactResult<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Available,
                found.Receipt,
                []);
    }

    private PreviewEvaluation EvaluatePreview(CharacterCreationContactPreviewRequest request)
    {
        WorkspaceStoreReadResult read = _workspaceStore.Get(request.Binding.WorkspaceId);
        if (!read.Success || read.Value is not WorkspaceStoredDocument workspace)
        {
            return new PreviewEvaluation(
                ReadFailure<CharacterCreationContactPreview>(read),
                null,
                null);
        }

        AuthorityContext context = BuildContext(workspace);
        string? bindingBlocker = CompareBinding(context.Binding, request.Binding);
        if (bindingBlocker is not null)
        {
            return new PreviewEvaluation(
                Blocked<CharacterCreationContactPreview>(
                    CharacterCreationContactOutcomes.Conflict,
                    bindingBlocker),
                null,
                null);
        }

        var blockers = new List<string>(context.AuthorityBlockers);
        if (blockers.Count != 0)
        {
            return PreviewBlocked(
                context,
                workspace,
                request.Binding,
                request.Edit.ContactId,
                blockers);
        }
        CharacterCreationContactProjection? before = context.Contacts.SingleOrDefault(
            contact => contact.ContactId == request.Edit.ContactId);
        ContactElement? targetEntry = context.ContactElements.SingleOrDefault(
            pair => pair.Id == request.Edit.ContactId);
        XElement? target = targetEntry?.Element;
        if (before is null || target is null)
        {
            blockers.Add(CharacterCreationContactsBlockers.ContactNotFound);
            return PreviewBlocked(context, workspace, request.Binding, request.Edit.ContactId, blockers);
        }

        XDocument replacementDocument = new(context.Document);
        XElement replacementRoot = replacementDocument.Root!;
        XElement replacementTarget = FindContact(replacementRoot, request.Edit.ContactId)!;
        string siblingsBefore = ComputeUntouchedSiblingDigest(context.Root, request.Edit.ContactId);
        string nestedBefore = ComputeNestedStateDigest(target);
        List<CharacterCreationContactWriteOperation> operations = ApplyEdit(
            context.Root,
            replacementRoot,
            target,
            replacementTarget,
            before,
            request.Edit,
            blockers);
        string replacementContent = replacementDocument.ToString(SaveOptions.DisableFormatting);
        string contentAfter = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(replacementContent);

        WorkspaceDocument replacementWorkspaceDocument = workspace.Document with
        {
            State = workspace.Document.State with { Payload = replacementContent }
        };
        CharacterCreationContactsAuthoritySnapshot replacementAuthority =
            CharacterCreationContactsAuthorityEvaluator.Evaluate(replacementWorkspaceDocument);
        blockers.AddRange(replacementAuthority.AuthorityBlockers);
        List<CharacterCreationContactProjection> projectedAfter = ProjectContacts(
            replacementRoot,
            context.CharacterCreated,
            replacementAuthority.Contacts,
            blockers);
        CharacterCreationContactProjection? after = projectedAfter.SingleOrDefault(
            contact => contact.ContactId == request.Edit.ContactId);
        if (!FixedEquals(context.Binding.SourceDigest, replacementAuthority.SourceDigest)
            || !FixedEquals(context.Binding.RulesDigest, replacementAuthority.RulesDigest)
            || !FixedEquals(context.Binding.RuntimeDigest, replacementAuthority.RuntimeDigest))
        {
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
        }
        CharacterCreationContactBudget contactBudgetAfter = replacementAuthority.ContactBudget;
        CharacterCreationContactBudget highPlacesBudgetAfter = replacementAuthority.HighPlacesBudget;
        blockers.AddRange(contactBudgetAfter.Blockers);
        blockers.AddRange(highPlacesBudgetAfter.Blockers);
        if (operations.Count == 0)
            blockers.Add(CharacterCreationContactsBlockers.NoChange);
        if (after is null)
            blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);

        string siblingsAfter = ComputeUntouchedSiblingDigest(replacementRoot, request.Edit.ContactId);
        string nestedAfter = ComputeNestedStateDigest(replacementTarget);
        bool siblingsPreserved = FixedEquals(siblingsBefore, siblingsAfter);
        bool nestedPreserved = FixedEquals(nestedBefore, nestedAfter);
        if (!siblingsPreserved || !nestedPreserved)
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);

        var plan = new CharacterCreationContactAtomicWritePlan(
            CharacterCreationContactsSchemas.WritePlanV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            request.Edit.ContactId,
            operations,
            context.Binding.ContentDigest,
            contentAfter,
            siblingsBefore,
            siblingsAfter,
            nestedBefore,
            nestedAfter,
            siblingsPreserved,
            nestedPreserved,
            PlanDigest: string.Empty);
        plan = plan with
        {
            PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                plan with { PlanDigest = string.Empty })
        };
        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var preview = new CharacterCreationContactPreview(
            CharacterCreationContactsSchemas.PreviewV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            context.Binding,
            before,
            after ?? before,
            context.ContactBudget,
            contactBudgetAfter,
            context.HighPlacesBudget,
            highPlacesBudgetAfter,
            plan,
            normalized,
            RequiresExplicitConfirmation: true,
            CanConfirm: normalized.Length == 0,
            PreviewDigest: string.Empty);
        preview = preview with
        {
            PreviewDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                preview with { PreviewDigest = string.Empty })
        };
        return new PreviewEvaluation(
            new CharacterCreationContactResult<CharacterCreationContactPreview>(
                normalized.Length == 0
                    ? CharacterCreationContactOutcomes.Available
                    : CharacterCreationContactOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            replacementContent);
    }

    private static PreviewEvaluation PreviewBlocked(
        AuthorityContext context,
        WorkspaceStoredDocument workspace,
        CharacterCreationContactBinding binding,
        Guid contactId,
        ICollection<string> blockers)
    {
        CharacterCreationContactProjection placeholder = context.Contacts.FirstOrDefault()
            ?? EmptyContact(contactId);
        var plan = new CharacterCreationContactAtomicWritePlan(
            CharacterCreationContactsSchemas.WritePlanV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            contactId,
            [],
            binding.ContentDigest,
            binding.ContentDigest,
            EmptyDigest(),
            EmptyDigest(),
            EmptyDigest(),
            EmptyDigest(),
            true,
            true,
            PlanDigest: string.Empty);
        plan = plan with
        {
            PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                plan with { PlanDigest = string.Empty })
        };
        string[] normalized = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var preview = new CharacterCreationContactPreview(
            CharacterCreationContactsSchemas.PreviewV1,
            CharacterCreationWizardStepIds.ContactsLifestyles,
            binding,
            placeholder,
            placeholder,
            context.ContactBudget,
            context.ContactBudget,
            context.HighPlacesBudget,
            context.HighPlacesBudget,
            plan,
            normalized,
            true,
            false,
            PreviewDigest: string.Empty);
        preview = preview with
        {
            PreviewDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                preview with { PreviewDigest = string.Empty })
        };
        return new PreviewEvaluation(
            new CharacterCreationContactResult<CharacterCreationContactPreview>(
                CharacterCreationContactOutcomes.Blocked,
                preview,
                normalized),
            workspace,
            null);
    }

    private AuthorityContext BuildContext(WorkspaceStoredDocument workspace)
    {
        var blockers = new List<string>();
        CharacterCreationContactsAuthoritySnapshot authority =
            CharacterCreationContactsAuthorityEvaluator.Evaluate(workspace.Document);
        blockers.AddRange(authority.AuthorityBlockers);
        if (_workspaceStore is not IWorkspaceAuxiliaryStateAtomicCommitCapability
            {
                SupportsWorkspaceAuxiliaryStateAtomicCommit: true
            })
        {
            blockers.Add(CharacterCreationContactsBlockers.PersistenceAuthorityRequired);
        }
        if (!string.Equals(
                RulesetDefaults.NormalizeOptional(workspace.Document.RulesetId),
                RulesetDefaults.Sr5,
                StringComparison.Ordinal))
        {
            blockers.Add(CharacterCreationContactsBlockers.RulesetSr5Required);
        }

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
            blockers.Add(CharacterCreationContactsBlockers.CharacterDocumentInvalid);
        }

        bool created = ParseBool(ReadValue(root, "created"));
        if (created)
            blockers.Add(CharacterCreationContactsBlockers.CareerModeRejected);

        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationContactReceipts ?? [];
        if (!CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
        {
            blockers.Add(CharacterCreationContactsBlockers.ReceiptLedgerCorrupt);
        }
        else if (ledger.Count >= CharacterCreationContactReceiptLedgerIntegrity.MaximumEntries)
        {
            blockers.Add(CharacterCreationContactsBlockers.PersistenceAuthorityRequired);
        }

        List<CharacterCreationContactProjection> contacts = ProjectContacts(
            root,
            created,
            authority.Contacts,
            blockers);
        ContactElement[] elements = EnumerateContactElements(root).ToArray();
        int editableContactCount = (root.Element("contacts")?.Elements("contact") ?? [])
            .Count(contact => ReadValue(contact, "type") is ""
                || string.Equals(ReadValue(contact, "type"), "Contact", StringComparison.OrdinalIgnoreCase));
        if (elements.Length != editableContactCount)
            blockers.Add(CharacterCreationContactsBlockers.ContactInvalid);
        string contentDigest = CharacterCreationFoundationDraftLedgerIntegrity
            .ComputeRawCharacterXmlDigest(workspace.Document.Content);
        var binding = new CharacterCreationContactBinding(
            workspace.Id,
            workspace.ContentRevision,
            workspace.ContentRevision,
            workspace.SavedRevision,
            contentDigest,
            workspace.Document.AuxiliaryStateDigest,
            authority.SourceDigest,
            authority.RulesDigest,
            authority.RuntimeDigest);
        return new AuthorityContext(
            workspace,
            document,
            root,
            created,
            binding,
            contacts,
            elements,
            authority.ContactBudget,
            authority.HighPlacesBudget,
            blockers.Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray());
    }

    private static List<CharacterCreationContactProjection> ProjectContacts(
        XElement root,
        bool created,
        IReadOnlyList<CharacterCreationContactAuthorityCost> authority,
        ICollection<string> blockers)
    {
        var projections = new List<CharacterCreationContactProjection>();
        var identities = new HashSet<Guid>();
        foreach (ContactElement item in EnumerateContactElements(root))
        {
            if (!identities.Add(item.Id))
            {
                blockers.Add(CharacterCreationContactsBlockers.ContactAmbiguous);
                continue;
            }
            CharacterCreationContactAuthorityCost? cost = authority.SingleOrDefault(
                contact => contact.ContactId == item.Id);
            if (!CharacterContactEditSemanticsResolver.TryResolve(
                    root,
                    item.Element,
                    out CharacterContactEditSemantics semantics)
                || cost is null)
            {
                blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
                continue;
            }

            CharacterCreationContactIdentity identity = ReadIdentity(item.Element);
            IReadOnlyList<CharacterCreationContactFieldAuthority> fields = BuildFields(identity, semantics, created);
            var projection = new CharacterCreationContactProjection(
                item.Id,
                identity,
                semantics.Connection,
                semantics.Loyalty,
                semantics.IsGroup,
                semantics.Free,
                semantics.Family,
                semantics.Blackmail,
                cost.ContactPointCost,
                cost.CountsAgainstContactBudget,
                cost.CountsAgainstHighPlacesBudget,
                fields,
                CharacterCreationContactSourceAnchors.All,
                ContactDigest: string.Empty);
            projection = projection with
            {
                ContactDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    projection with { ContactDigest = string.Empty })
            };
            projections.Add(projection);
        }
        return projections.OrderBy(contact => contact.ContactId).ToList();
    }

    private static IEnumerable<ContactElement> EnumerateContactElements(XElement root)
    {
        foreach (XElement contact in root.Element("contacts")?.Elements("contact") ?? [])
        {
            string type = ReadValue(contact, "type");
            if (type.Length != 0 && !string.Equals(type, "Contact", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Guid.TryParseExact(ReadValue(contact, "guid"), "D", out Guid id)
                && id != Guid.Empty)
            {
                yield return new ContactElement(id, contact);
            }
        }
    }

    private static List<CharacterCreationContactWriteOperation> ApplyEdit(
        XElement currentRoot,
        XElement replacementRoot,
        XElement current,
        XElement replacement,
        CharacterCreationContactProjection before,
        CharacterCreationContactEdit edit,
        ICollection<string> blockers)
    {
        var operations = new List<CharacterCreationContactWriteOperation>();
        if (edit.ContactId == Guid.Empty
            || edit.Identity is null
               && edit.Connection is null
               && edit.Loyalty is null
               && edit.IsGroup is null
               && edit.Free is null
               && edit.Family is null
               && edit.Blackmail is null)
        {
            blockers.Add(edit.ContactId == Guid.Empty
                ? CharacterCreationContactsBlockers.ContactInvalid
                : CharacterCreationContactsBlockers.MutationEmpty);
            return operations;
        }
        if (!CharacterContactEditSemanticsResolver.TryResolve(
                currentRoot,
                current,
                out CharacterContactEditSemantics semantics))
        {
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
            return operations;
        }

        if (edit.Identity is CharacterCreationContactIdentity identity)
        {
            if (!semantics.IdentityEditable)
            {
                blockers.Add(CharacterCreationContactsBlockers.FieldNotEditable);
            }
            else if (!IsValidIdentity(identity))
            {
                blockers.Add(CharacterCreationContactsBlockers.MutationInvalid);
            }
            else
            {
                ApplyText(replacement, "name", CharacterCreationContactFieldIds.Name, before.Identity.Name, identity.Name, operations);
                ApplyText(replacement, "role", CharacterCreationContactFieldIds.Role, before.Identity.Role, identity.Role, operations);
                ApplyText(replacement, "location", CharacterCreationContactFieldIds.Location, before.Identity.Location, identity.Location, operations);
                ApplyText(replacement, "notes", CharacterCreationContactFieldIds.Notes, before.Identity.Notes, identity.Notes, operations);
                ApplyText(replacement, "extra", CharacterCreationContactFieldIds.CustomName, before.Identity.CustomName, identity.CustomName, operations);
                ApplyText(replacement, "metatype", CharacterCreationContactFieldIds.Metatype, before.Identity.Metatype, identity.Metatype, operations);
                ApplyText(replacement, "gender", CharacterCreationContactFieldIds.Gender, before.Identity.Gender, identity.Gender, operations);
                ApplyText(replacement, "age", CharacterCreationContactFieldIds.Age, before.Identity.Age, identity.Age, operations);
                ApplyText(replacement, "contacttype", CharacterCreationContactFieldIds.ContactType, before.Identity.ContactType, identity.ContactType, operations);
                ApplyText(replacement, "preferredpayment", CharacterCreationContactFieldIds.PreferredPayment, before.Identity.PreferredPayment, identity.PreferredPayment, operations);
                ApplyText(replacement, "hobbiesvice", CharacterCreationContactFieldIds.HobbiesVice, before.Identity.HobbiesVice, identity.HobbiesVice, operations);
                ApplyText(replacement, "personallife", CharacterCreationContactFieldIds.PersonalLife, before.Identity.PersonalLife, identity.PersonalLife, operations);
                ApplyText(replacement, "groupname", CharacterCreationContactFieldIds.GroupName, before.Identity.GroupName, identity.GroupName, operations);
            }
        }

        ApplyInt(replacement, "connection", CharacterCreationContactFieldIds.Connection, before.Connection,
            edit.Connection, MinimumConnection, semantics.ConnectionMaximum, semantics.ConnectionEditable, operations, blockers);
        ApplyInt(replacement, "loyalty", CharacterCreationContactFieldIds.Loyalty, before.Loyalty,
            edit.Loyalty, MinimumLoyalty, MaximumLoyalty, semantics.LoyaltyEditable, operations, blockers);
        ApplyBool(replacement, "group", CharacterCreationContactFieldIds.Group, before.IsGroup,
            edit.IsGroup, semantics.GroupEditable, operations, blockers);
        ApplyBool(replacement, "free", CharacterCreationContactFieldIds.Free, before.Free,
            edit.Free, semantics.FreeEditable, operations, blockers);
        ApplyBool(replacement, "family", CharacterCreationContactFieldIds.Family, before.Family,
            edit.Family, semantics.FamilyEditable, operations, blockers);
        ApplyBool(replacement, "blackmail", CharacterCreationContactFieldIds.Blackmail, before.Blackmail,
            edit.Blackmail, semantics.BlackmailEditable, operations, blockers);

        if (!CharacterContactEditSemanticsResolver.TryResolve(
                replacementRoot,
                replacement,
                out CharacterContactEditSemantics after))
        {
            blockers.Add(CharacterCreationContactsBlockers.AuthorityUnavailable);
        }
        else if ((edit.Connection is int requestedConnection && after.Connection != requestedConnection)
                 || (edit.Loyalty is int requestedLoyalty && after.Loyalty != requestedLoyalty)
                 || (edit.IsGroup is bool requestedGroup && after.IsGroup != requestedGroup)
                 || (edit.Free is bool requestedFree && after.Free != requestedFree)
                 || (edit.Family is bool requestedFamily && after.Family != requestedFamily)
                 || (edit.Blackmail is bool requestedBlackmail && after.Blackmail != requestedBlackmail))
        {
            blockers.Add(CharacterCreationContactsBlockers.MutationInvalid);
        }

        return operations.Select((operation, index) => operation with { Order = index + 1 }).ToList();
    }

    private static void ApplyText(
        XElement target,
        string elementName,
        string fieldId,
        string before,
        string after,
        ICollection<CharacterCreationContactWriteOperation> operations)
    {
        if (string.Equals(before, after, StringComparison.Ordinal))
            return;
        SetValue(target, elementName, after);
        operations.Add(Operation(fieldId, before, after));
    }

    private static void ApplyInt(
        XElement target,
        string elementName,
        string fieldId,
        int before,
        int? requested,
        int minimum,
        int maximum,
        bool editable,
        ICollection<CharacterCreationContactWriteOperation> operations,
        ICollection<string> blockers)
    {
        if (requested is not int after || after == before)
            return;
        if (!editable)
        {
            blockers.Add(CharacterCreationContactsBlockers.FieldNotEditable);
            return;
        }
        if (after < minimum || after > maximum)
        {
            blockers.Add(CharacterCreationContactsBlockers.MutationInvalid);
            return;
        }
        SetValue(target, elementName, after.ToString(CultureInfo.InvariantCulture));
        operations.Add(Operation(
            fieldId,
            before.ToString(CultureInfo.InvariantCulture),
            after.ToString(CultureInfo.InvariantCulture)));
    }

    private static void ApplyBool(
        XElement target,
        string elementName,
        string fieldId,
        bool before,
        bool? requested,
        bool editable,
        ICollection<CharacterCreationContactWriteOperation> operations,
        ICollection<string> blockers)
    {
        if (requested is not bool after || after == before)
            return;
        if (!editable)
        {
            blockers.Add(CharacterCreationContactsBlockers.FieldNotEditable);
            return;
        }
        string beforeText = before.ToString(CultureInfo.InvariantCulture);
        string afterText = after.ToString(CultureInfo.InvariantCulture);
        SetValue(target, elementName, afterText);
        operations.Add(Operation(fieldId, beforeText, afterText));
    }

    private static CharacterCreationContactWriteOperation Operation(
        string fieldId,
        string before,
        string after) => new(
            Order: 0,
            fieldId,
            before,
            after,
            CharacterCreationContactSourceAnchors.All);

    private static void SetValue(XElement target, string elementName, string value)
    {
        XElement? element = target.Element(elementName);
        if (element is null)
            target.Add(new XElement(elementName, value));
        else
            element.Value = value;
    }

    private static IReadOnlyList<CharacterCreationContactFieldAuthority> BuildFields(
        CharacterCreationContactIdentity identity,
        CharacterContactEditSemantics semantics,
        bool created)
    {
        var fields = new List<CharacterCreationContactFieldAuthority>
        {
            TextField(CharacterCreationContactFieldIds.Name, "Name", identity.Name, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Role, "Role", identity.Role, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Location, "Location", identity.Location, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Notes, "Notes", identity.Notes, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.CustomName, "Custom name", identity.CustomName, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Metatype, "Metatype", identity.Metatype, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Gender, "Gender", identity.Gender, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.Age, "Age", identity.Age, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.ContactType, "Contact type", identity.ContactType, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.PreferredPayment, "Preferred payment", identity.PreferredPayment, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.HobbiesVice, "Hobbies / vice", identity.HobbiesVice, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.PersonalLife, "Personal life", identity.PersonalLife, semantics.IdentityEditable),
            TextField(CharacterCreationContactFieldIds.GroupName, "Group name", identity.GroupName, semantics.IdentityEditable),
            IntegerField(CharacterCreationContactFieldIds.Connection, "Connection", semantics.Connection,
                MinimumConnection, semantics.ConnectionMaximum, semantics.ConnectionEditable),
            IntegerField(CharacterCreationContactFieldIds.Loyalty, "Loyalty", semantics.Loyalty,
                MinimumLoyalty, MaximumLoyalty, semantics.LoyaltyEditable),
            BooleanField(CharacterCreationContactFieldIds.Group, "Group", semantics.IsGroup, semantics.GroupEditable),
            BooleanField(CharacterCreationContactFieldIds.Free, "Free", semantics.Free,
                semantics.FreeEditable && !created),
            BooleanField(CharacterCreationContactFieldIds.Family, "Family", semantics.Family, semantics.FamilyEditable),
            BooleanField(CharacterCreationContactFieldIds.Blackmail, "Blackmail", semantics.Blackmail, semantics.BlackmailEditable)
        };
        return fields;
    }

    private static CharacterCreationContactFieldAuthority TextField(
        string id,
        string label,
        string value,
        bool editable) => new(
            id,
            label,
            CharacterCreationContactValueKinds.Text,
            editable,
            value,
            0,
            MaximumTextLength,
            [],
            editable ? [] : [CharacterCreationContactsBlockers.FieldNotEditable],
            CharacterCreationContactSourceAnchors.All);

    private static CharacterCreationContactFieldAuthority IntegerField(
        string id,
        string label,
        int value,
        int minimum,
        int maximum,
        bool editable) => new(
            id,
            label,
            CharacterCreationContactValueKinds.Integer,
            editable,
            value.ToString(CultureInfo.InvariantCulture),
            minimum,
            maximum,
            Enumerable.Range(minimum, maximum - minimum + 1)
                .Select(option => new CharacterCreationContactOption(
                    option.ToString(CultureInfo.InvariantCulture),
                    option.ToString(CultureInfo.InvariantCulture),
                    option.ToString(CultureInfo.InvariantCulture),
                    editable,
                    editable ? [] : [CharacterCreationContactsBlockers.FieldNotEditable],
                    CharacterCreationContactSourceAnchors.All))
                .ToArray(),
            editable ? [] : [CharacterCreationContactsBlockers.FieldNotEditable],
            CharacterCreationContactSourceAnchors.All);

    private static CharacterCreationContactFieldAuthority BooleanField(
        string id,
        string label,
        bool value,
        bool editable) => new(
            id,
            label,
            CharacterCreationContactValueKinds.Boolean,
            editable,
            value.ToString(CultureInfo.InvariantCulture),
            null,
            null,
            new[] { false, true }.Select(option => new CharacterCreationContactOption(
                option ? "true" : "false",
                option ? "Yes" : "No",
                option.ToString(CultureInfo.InvariantCulture),
                editable,
                editable ? [] : [CharacterCreationContactsBlockers.FieldNotEditable],
                CharacterCreationContactSourceAnchors.All)).ToArray(),
            editable ? [] : [CharacterCreationContactsBlockers.FieldNotEditable],
            CharacterCreationContactSourceAnchors.All);

    private static CharacterCreationContactIdentity ReadIdentity(XElement contact) => new(
        ReadValue(contact, "name"),
        ReadValue(contact, "role"),
        ReadValue(contact, "location"),
        ReadValue(contact, "notes"),
        ReadValue(contact, "extra"),
        ReadValue(contact, "metatype"),
        ReadValue(contact, "gender"),
        ReadValue(contact, "age"),
        ReadValue(contact, "contacttype"),
        ReadValue(contact, "preferredpayment"),
        ReadValue(contact, "hobbiesvice"),
        ReadValue(contact, "personallife"),
        ReadValue(contact, "groupname"));

    private static bool IsValidIdentity(CharacterCreationContactIdentity identity)
        => new[]
        {
            identity.Name, identity.Role, identity.Location, identity.Notes,
            identity.CustomName, identity.Metatype, identity.Gender, identity.Age,
            identity.ContactType, identity.PreferredPayment, identity.HobbiesVice,
            identity.PersonalLife, identity.GroupName
        }.All(IsValidText);

    private static bool IsValidText(string? value)
    {
        if (value is null
            || value.Length > MaximumTextLength
            || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(character => char.IsControl(character)
                && character is not '\r' and not '\n' and not '\t'))
        {
            return false;
        }

        try
        {
            XmlConvert.VerifyXmlChars(value);
            return true;
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static string ComputeUntouchedSiblingDigest(XElement root, Guid targetId)
    {
        XElement? contacts = root.Element("contacts");
        XElement? target = contacts?.Elements("contact")
            .SingleOrDefault(contact => Guid.TryParseExact(ReadValue(contact, "guid"), "D", out Guid id)
                                        && id == targetId);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            (contacts?.Nodes() ?? [])
                .Where(node => !ReferenceEquals(node, target))
                .Select(node => node.ToString(SaveOptions.DisableFormatting))
                .ToArray());
    }

    private static string ComputeNestedStateDigest(XElement contact)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Attributes = contact.Attributes().Select(attribute => attribute.ToString()).ToArray(),
            UntouchedChildren = contact.Nodes()
                .Where(node => node is not XElement element
                               || !s_EditableElementNames.Contains(element.Name.LocalName))
                .Select(node => node.ToString(SaveOptions.DisableFormatting))
                .ToArray()
        });

    private static XElement? FindContact(XElement root, Guid id)
        => EnumerateContactElements(root).SingleOrDefault(item => item.Id == id)?.Element;

    private static string? CompareBinding(
        CharacterCreationContactBinding current,
        CharacterCreationContactBinding requested)
    {
        if (current.WorkspaceId != requested.WorkspaceId
            || current.WorkspaceRevision != requested.WorkspaceRevision
            || current.ContentRevision != requested.ContentRevision
            || current.SavedRevision != requested.SavedRevision
            || requested.WorkspaceRevision != requested.ContentRevision)
        {
            return CharacterCreationContactsBlockers.StaleWorkspaceRevision;
        }
        if (!FixedEquals(current.ContentDigest, requested.ContentDigest))
            return CharacterCreationContactsBlockers.StaleContentDigest;
        if (!string.Equals(current.AuxiliaryStateDigest, requested.AuxiliaryStateDigest, StringComparison.Ordinal))
            return CharacterCreationContactsBlockers.StaleAuxiliaryStateDigest;
        if (!FixedEquals(current.SourceDigest, requested.SourceDigest))
            return CharacterCreationContactsBlockers.StaleSourceDigest;
        if (!FixedEquals(current.RulesDigest, requested.RulesDigest))
            return CharacterCreationContactsBlockers.StaleRulesDigest;
        if (!FixedEquals(current.RuntimeDigest, requested.RuntimeDigest))
            return CharacterCreationContactsBlockers.StaleRuntimeDigest;
        return null;
    }

    private static CharacterCreationContactResult<CharacterCreationContactReceipt>? ResolveReplay(
        WorkspaceStoredDocument workspace,
        string idempotencyDigest,
        string commandDigest)
    {
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry> ledger =
            workspace.Document.AuxiliaryState.CharacterCreationContactReceipts ?? [];
        if (!CharacterCreationContactReceiptLedgerIntegrity.IsValidLedger(
                workspace.Id,
                workspace.ContentRevision,
                ledger))
        {
            return Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Corrupt,
                CharacterCreationContactsBlockers.ReceiptLedgerCorrupt);
        }
        CharacterCreationContactReceiptLedgerEntry? existing = ledger.FirstOrDefault(entry =>
            FixedEquals(entry.IdempotencyKeyDigest, idempotencyDigest));
        if (existing is null)
            return null;
        return FixedEquals(existing.CommandDigest, commandDigest)
            ? new CharacterCreationContactResult<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Replayed,
                existing.Receipt,
                [])
            : Blocked<CharacterCreationContactReceipt>(
                CharacterCreationContactOutcomes.Conflict,
                CharacterCreationContactsBlockers.IdempotencyConflict);
    }

    private static string ComputeCommandDigest(CharacterCreationContactConfirmRequest request)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Schema = CharacterCreationContactsSchemas.ReceiptV1,
            request.Binding,
            request.Edit,
            request.PreviewDigest
        });

    private static string ComputeIdempotencyDigest(string key)
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new
        {
            Schema = "chummer.character_creation_contacts.idempotency.v1",
            Key = key
        });

    private static bool TryNormalizeIdempotencyKey(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 and <= MaximumIdempotencyKeyLength
               && string.Equals(normalized, value, StringComparison.Ordinal)
               && normalized.All(character => char.IsLetterOrDigit(character)
                   || character is '-' or '_' or '.' or ':' or '/');
    }

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
           || string.Equals(value, "1", StringComparison.Ordinal);

    private static string ReadValue(XElement parent, string name)
        => parent.Element(name)?.Value.Trim() ?? string.Empty;

    private static string EmptyDigest()
        => CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(Array.Empty<string>());

    private static bool FixedEquals(string left, string right)
        => CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(left, right);

    private static CharacterCreationContactResult<T> ReadFailure<T>(WorkspaceStoreReadResult read)
        where T : class => Blocked<T>(
            read.Outcome switch
            {
                WorkspaceOperationOutcome.Missing => CharacterCreationContactOutcomes.Missing,
                WorkspaceOperationOutcome.Corrupt => CharacterCreationContactOutcomes.Corrupt,
                _ => CharacterCreationContactOutcomes.Unavailable
            },
            CharacterCreationContactsBlockers.WorkspaceUnavailable);

    private static CharacterCreationContactResult<T> Blocked<T>(
        string outcome,
        params string[] blockers)
        where T : class => new(outcome, null, blockers);

    private static CharacterCreationContactProjection EmptyContact(Guid id)
    {
        var identity = new CharacterCreationContactIdentity(
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
            string.Empty, string.Empty, string.Empty);
        return new CharacterCreationContactProjection(
            id,
            identity,
            0,
            0,
            false,
            false,
            false,
            false,
            0,
            false,
            false,
            [],
            CharacterCreationContactSourceAnchors.All,
            EmptyDigest());
    }

    private sealed record ContactElement(Guid Id, XElement Element);

    private sealed record AuthorityContext(
        WorkspaceStoredDocument Workspace,
        XDocument Document,
        XElement Root,
        bool CharacterCreated,
        CharacterCreationContactBinding Binding,
        IReadOnlyList<CharacterCreationContactProjection> Contacts,
        IReadOnlyList<ContactElement> ContactElements,
        CharacterCreationContactBudget ContactBudget,
        CharacterCreationContactBudget HighPlacesBudget,
        IReadOnlyList<string> AuthorityBlockers);

    private sealed record PreviewEvaluation(
        CharacterCreationContactResult<CharacterCreationContactPreview> Result,
        WorkspaceStoredDocument? Workspace,
        string? ReplacementContent);
}
