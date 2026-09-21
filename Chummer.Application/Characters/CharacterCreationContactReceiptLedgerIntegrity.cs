using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Workspaces;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Application.Characters;

/// <summary>
/// Persistence-boundary validation for creation-contact receipts. The ledger is
/// append-only and lives outside the downloadable character payload.
/// </summary>
public static class CharacterCreationContactReceiptLedgerIntegrity
{
    public const int MaximumEntries = 4096;
    private const int MaximumTextLength = 32_767;
    internal static string AbsentContactDigest { get; } =
        CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(new { ContactAbsent = true });

    private static readonly IReadOnlyDictionary<string, ContactWriteField> s_WriteFields =
        new Dictionary<string, ContactWriteField>(StringComparer.Ordinal)
        {
            [CharacterCreationContactFieldIds.Name] = new("name", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Role] = new("role", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Location] = new("location", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Notes] = new("notes", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.CustomName] = new("extra", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Metatype] = new("metatype", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Gender] = new("gender", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Age] = new("age", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.ContactType] = new("contacttype", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.PreferredPayment] = new("preferredpayment", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.HobbiesVice] = new("hobbiesvice", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.PersonalLife] = new("personallife", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.GroupName] = new("groupname", ContactWriteValueKind.Text),
            [CharacterCreationContactFieldIds.Connection] = new("connection", ContactWriteValueKind.Integer),
            [CharacterCreationContactFieldIds.Loyalty] = new("loyalty", ContactWriteValueKind.Integer),
            [CharacterCreationContactFieldIds.Group] = new("group", ContactWriteValueKind.Boolean),
            [CharacterCreationContactFieldIds.Free] = new("free", ContactWriteValueKind.Boolean),
            [CharacterCreationContactFieldIds.Family] = new("family", ContactWriteValueKind.Boolean),
            [CharacterCreationContactFieldIds.Blackmail] = new("blackmail", ContactWriteValueKind.Boolean)
        };

    private static readonly HashSet<string> s_EditableElementNames =
        s_WriteFields.Values.Select(field => field.ElementName).ToHashSet(StringComparer.Ordinal);

    public static string ComputeReceiptDigest(CharacterCreationContactReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            receipt with { ReceiptDigest = string.Empty });
    }

    public static string ComputeContentDigest(string content)
        => "sha256:" + Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(content ?? string.Empty)));

    public static bool IsValidLedger(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? entries)
    {
        if (string.IsNullOrWhiteSpace(workspaceId.Value)
            || currentContentRevision <= 0
            || entries is null
            || entries.Count > MaximumEntries)
        {
            return false;
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var receiptIds = new HashSet<string>(StringComparer.Ordinal);
        long previousReceiptRevision = 0;
        foreach (CharacterCreationContactReceiptLedgerEntry? entry in entries)
        {
            if (!IsValidPersistedEntry(workspaceId, currentContentRevision, entry)
                || !keys.Add(entry!.IdempotencyKeyDigest)
                || !receiptIds.Add(entry.Receipt.ReceiptId)
                || entry.Receipt.ContentRevision < previousReceiptRevision)
            {
                return false;
            }
            previousReceiptRevision = entry.Receipt.ContentRevision;
        }

        return true;
    }

    public static bool IsValidAppendTransition(
        CharacterWorkspaceId workspaceId,
        long previousContentRevision,
        long previousSavedRevision,
        long nextContentRevision,
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? current,
        IReadOnlyList<CharacterCreationContactReceiptLedgerEntry>? replacement)
    {
        current ??= [];
        replacement ??= [];
        if (nextContentRevision != previousContentRevision + 1
            || replacement.Count != current.Count + 1
            || replacement.Count > MaximumEntries
            || !IsValidLedger(workspaceId, nextContentRevision, replacement))
        {
            return false;
        }

        for (int index = 0; index < current.Count; index++)
        {
            if (!CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
                    current[index], replacement[index]))
            {
                return false;
            }
        }

        return IsValidForCommit(
            workspaceId,
            previousContentRevision,
            previousSavedRevision,
            replacement[^1]);
    }

    public static bool HasValidContentTransition(
        CharacterCreationContactReceiptLedgerEntry entry,
        WorkspaceDocument currentDocument,
        WorkspaceDocument replacementDocument)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(currentDocument);
        ArgumentNullException.ThrowIfNull(replacementDocument);
        CharacterCreationContactReceipt receipt = entry.Receipt;
        string currentContent = currentDocument.Content;
        string replacementContent = replacementDocument.Content;
        if (!FixedEquals(receipt.ContentDigestBefore, ComputeContentDigest(currentContent))
            || !FixedEquals(receipt.ContentDigestAfter, ComputeContentDigest(replacementContent))
            || !FixedEquals(receipt.WritePlan.ContentDigestBefore, receipt.ContentDigestBefore)
            || !FixedEquals(receipt.WritePlan.ContentDigestAfter, receipt.ContentDigestAfter)
            || currentDocument.Format != replacementDocument.Format
            || !string.Equals(currentDocument.RulesetId, replacementDocument.RulesetId, StringComparison.Ordinal)
            || currentDocument.SchemaVersion != replacementDocument.SchemaVersion
            || !string.Equals(currentDocument.PayloadKind, replacementDocument.PayloadKind, StringComparison.Ordinal))
        {
            return false;
        }

        if (receipt.WritePlan.PendingDraft is not null)
            return HasValidDraftTransition(entry, currentDocument, replacementDocument);
        if (currentDocument.AuxiliaryState.CharacterCreationContactsDraft is not null
            || replacementDocument.AuxiliaryState.CharacterCreationContactsDraft is not null)
            return false;

        CharacterCreationContactsAuthoritySnapshot currentAuthority =
            CharacterCreationContactsAuthorityEvaluator.Evaluate(currentDocument);
        CharacterCreationContactsAuthoritySnapshot replacementAuthority =
            CharacterCreationContactsAuthorityEvaluator.Evaluate(replacementDocument);
        if (!HasExactReceiptAuthority(receipt, currentAuthority, replacementAuthority))
            return false;

        try
        {
            XDocument current = XDocument.Parse(currentContent, LoadOptions.PreserveWhitespace);
            XDocument replacement = XDocument.Parse(replacementContent, LoadOptions.PreserveWhitespace);
            if (current.Root is not XElement currentRoot
                || replacement.Root is not XElement replacementRoot
                || !string.Equals(currentRoot.Name.LocalName, "character", StringComparison.Ordinal)
                || !string.Equals(replacementRoot.Name.LocalName, "character", StringComparison.Ordinal)
                || ParseBool(ReadValue(currentRoot, "created"))
                || ParseBool(ReadValue(replacementRoot, "created")))
            {
                return false;
            }

            XElement? currentContact = FindUniqueContact(currentRoot, receipt.ContactId);
            XElement? replacementContact = FindUniqueContact(replacementRoot, receipt.ContactId);
            if (receipt.WritePlan.ChangeKind != CharacterCreationContactChangeKind.Edit)
                return HasValidCollectionTransition(receipt.WritePlan, current, replacement);
            if (currentContact is null
                || replacementContact is null
                || !CharacterContactEditSemanticsResolver.TryResolve(
                    currentRoot,
                    currentContact,
                    out CharacterContactEditSemantics currentSemantics)
                || !CharacterContactEditSemanticsResolver.TryResolve(
                    replacementRoot,
                    replacementContact,
                    out CharacterContactEditSemantics replacementSemantics))
            {
                return false;
            }

            XDocument expected = new(current);
            XElement? expectedContact = FindUniqueContact(expected.Root!, receipt.ContactId);
            if (expectedContact is null)
                return false;
            var changedFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (CharacterCreationContactWriteOperation operation in receipt.WritePlan.Operations)
            {
                if (!changedFields.Add(operation.FieldId)
                    || !s_WriteFields.TryGetValue(operation.FieldId, out ContactWriteField field)
                    || !operation.SourceAnchorIds.SequenceEqual(CharacterCreationContactSourceAnchors.All)
                    || !IsEditable(operation.FieldId, currentSemantics)
                    || !TryNormalizeExistingValue(
                        currentContact,
                        currentSemantics,
                        operation.FieldId,
                        field,
                        out string before)
                    || !string.Equals(before, operation.BeforeValue, StringComparison.Ordinal)
                    || !TryValidateAfterValue(
                        operation.FieldId,
                        field,
                        currentSemantics,
                        operation.AfterValue)
                    || !AfterSemanticMatches(
                        operation.FieldId,
                        operation.AfterValue,
                        replacementSemantics))
                {
                    return false;
                }

                XElement? element = expectedContact.Element(field.ElementName);
                if (element is null)
                    expectedContact.Add(new XElement(field.ElementName, operation.AfterValue));
                else
                    element.Value = operation.AfterValue;
            }

            string siblingsBefore = ComputeUntouchedSiblingDigest(currentRoot, receipt.ContactId);
            string siblingsAfter = ComputeUntouchedSiblingDigest(replacementRoot, receipt.ContactId);
            string nestedBefore = ComputeNestedStateDigest(currentContact);
            string nestedAfter = ComputeNestedStateDigest(replacementContact);
            return XNode.DeepEquals(expected, replacement)
                   && FixedEquals(receipt.WritePlan.UntouchedSiblingDigestBefore, siblingsBefore)
                   && FixedEquals(receipt.WritePlan.UntouchedSiblingDigestAfter, siblingsAfter)
                   && FixedEquals(receipt.WritePlan.NestedStateDigestBefore, nestedBefore)
                   && FixedEquals(receipt.WritePlan.NestedStateDigestAfter, nestedAfter);
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static bool HasValidDraftTransition(CharacterCreationContactReceiptLedgerEntry entry,
        WorkspaceDocument current, WorkspaceDocument replacement)
    {
        var receipt = entry.Receipt;
        var plan = receipt.WritePlan;
        var draft = plan.PendingDraft!;
        if (plan.Schema != CharacterCreationContactsSchemas.DraftWritePlanV1
            || current.Content != replacement.Content
            || !CharacterCreationContactsDraftRules.Equal(draft, replacement.AuxiliaryState.CharacterCreationContactsDraft)
            || !CharacterCreationContactsDraftRules.IsValidShape(receipt.WorkspaceId, receipt.ContentRevision, draft)
            || draft.BaseContentRevision != receipt.PreviousContentRevision)
            return false;
        try
        {
            var workspace = new WorkspaceStoredDocument(receipt.WorkspaceId, current,
                receipt.PreviousContentRevision, receipt.PreviousSavedRevision, DateTimeOffset.UnixEpoch);
            var oldDraft = current.AuxiliaryState.CharacterCreationContactsDraft;
            var beforeDraft = CharacterCreationContactsDraftRules.Create(workspace,
                draft.Policy, draft.CarryoverPolicy, oldDraft?.Contacts ?? []);
            if (beforeDraft is null || !CharacterCreationContactsDraftRules.InputsMatch(beforeDraft, draft)
                || oldDraft is not null && !CharacterCreationContactsDraftRules.InputsMatch(oldDraft, draft)
                || !CharacterCreationContactsDraftRules.TryProject(workspace, beforeDraft, out var logicalBefore)
                || logicalBefore is null) return false;

            // Reconstruct precisely the proposed field operations. The existing
            // XML transition validator below still checks editable fields,
            // before values, semantic constraints, budgets and untouched data.
            XDocument expected = XDocument.Parse(logicalBefore.Content, LoadOptions.PreserveWhitespace);
            XElement root = expected.Root!;
            XElement? target = FindUniqueContact(root, plan.ContactId);
            if (plan.ChangeKind == CharacterCreationContactChangeKind.Add)
            {
                if (target is not null) return false;
                target = CharacterCreationContactsService.CreateDefaultContact(plan.ContactId);
                root.Element("contacts")!.Add(target);
            }
            if (target is null) return false;
            if (plan.ChangeKind == CharacterCreationContactChangeKind.Remove) target.Remove();
            else
                foreach (var operation in plan.Operations)
                {
                    if (operation.FieldId == CharacterCreationContactFieldIds.Presence) continue;
                    if (!s_WriteFields.TryGetValue(operation.FieldId, out var field)) return false;
                    target.SetElementValue(field.ElementName, operation.AfterValue);
                }
            if (!CharacterCreationContactsDraftRules.TryReadSelections(root, out var selections)
                || !CharacterCreationContactsDraftRules.Equal(selections, draft.Contacts)) return false;

            var logicalAfter = logicalBefore with
            {
                State = logicalBefore.State with { Payload = expected.ToString(SaveOptions.DisableFormatting) }
            };
            if (CharacterCreationContactsDraftRules.ValidatePointOnlySelection(logicalAfter).Length != 0)
                return false;
            var logicalPlan = plan with
            {
                PendingDraft = null,
                Schema = plan.ChangeKind == CharacterCreationContactChangeKind.Edit
                    ? CharacterCreationContactsSchemas.WritePlanV1 : CharacterCreationContactsSchemas.WritePlanV2,
                ContentDigestBefore = ComputeContentDigest(logicalBefore.Content),
                ContentDigestAfter = ComputeContentDigest(logicalAfter.Content)
            };
            // Pure validation inputs, never persisted or exposed as receipts.
            // The real receipt keeps identical raw-XML digests and binds the
            // auxiliary draft; only the semantic projection changes here.
            var logicalEntry = entry with
            {
                Receipt = receipt with
                {
                    WritePlan = logicalPlan,
                    ContentDigestBefore = logicalPlan.ContentDigestBefore,
                    ContentDigestAfter = logicalPlan.ContentDigestAfter
                }
            };
            return HasValidContentTransition(logicalEntry, logicalBefore, logicalAfter);
        }
        catch (Exception error) when (error is XmlException or ArgumentException or InvalidOperationException
            or IOException or OverflowException) { return false; }
    }

    private static bool HasValidCollectionTransition(
        CharacterCreationContactAtomicWritePlan plan, XDocument current, XDocument replacement)
    {
        XElement currentRoot = current.Root!;
        XElement replacementRoot = replacement.Root!;
        // Multiple containers or duplicate identities are never a collection edit.
        if (currentRoot.Elements("contacts").Count() > 1 || replacementRoot.Elements("contacts").Count() != 1
            || !string.Equals(plan.Schema, CharacterCreationContactsSchemas.WritePlanV2, StringComparison.Ordinal)
            || plan.Operations.Count == 0)
            return false;

        CharacterCreationContactWriteOperation presence = plan.Operations[0];
        bool adding = plan.ChangeKind == CharacterCreationContactChangeKind.Add;
        if ((!adding && plan.ChangeKind != CharacterCreationContactChangeKind.Remove)
            || presence.FieldId != CharacterCreationContactFieldIds.Presence
            || presence.BeforeValue != (adding ? "absent" : "present")
            || presence.AfterValue != (adding ? "present" : "absent")
            || !presence.SourceAnchorIds.SequenceEqual(CharacterCreationContactSourceAnchors.All))
            return false;

        XElement? before = FindUniqueContact(currentRoot, plan.ContactId);
        XElement? after = FindUniqueContact(replacementRoot, plan.ContactId);
        XDocument expected = new(current);
        if (adding)
        {
            if ((currentRoot.Element("contacts")?.Elements("contact") ?? []).Any(contact =>
                    Guid.TryParse(ReadValue(contact, "guid"), out Guid id) && id == plan.ContactId)
                || after is null)
                return false;
            XElement baseline = CharacterCreationContactsService.CreateDefaultContact(plan.ContactId);
            XElement added = new(baseline);
            XElement? collection = expected.Root!.Element("contacts");
            if (collection is null)
            {
                collection = new XElement("contacts");
                expected.Root.Add(collection);
            }
            collection.Add(added);
            if (!CharacterContactEditSemanticsResolver.TryResolve(currentRoot, baseline, out var semantics)
                || !CharacterContactEditSemanticsResolver.TryResolve(replacementRoot, after, out var afterSemantics))
                return false;
            var changed = new HashSet<string>(StringComparer.Ordinal);
            foreach (var operation in plan.Operations.Skip(1))
            {
                if (!changed.Add(operation.FieldId)
                    || !s_WriteFields.TryGetValue(operation.FieldId, out var field)
                    || !operation.SourceAnchorIds.SequenceEqual(CharacterCreationContactSourceAnchors.All)
                    || !IsEditable(operation.FieldId, semantics)
                    || !TryNormalizeExistingValue(baseline, semantics, operation.FieldId, field, out string value)
                    || value != operation.BeforeValue
                    || !TryValidateAfterValue(operation.FieldId, field, semantics, operation.AfterValue)
                    || !AfterSemanticMatches(operation.FieldId, operation.AfterValue, afterSemantics))
                    return false;
                XElement? element = added.Element(field.ElementName);
                if (element is null)
                    added.Add(new XElement(field.ElementName, operation.AfterValue));
                else
                    element.Value = operation.AfterValue;
            }
        }
        else
        {
            if (before is null || after is not null || plan.Operations.Count != 1
                || ReadValue(before, "type").Length != 0
                   && !string.Equals(ReadValue(before, "type"), "Contact", StringComparison.OrdinalIgnoreCase)
                || !CharacterContactEditSemanticsResolver.TryResolve(currentRoot, before, out var semantics)
                || !semantics.CanDelete)
                return false;
            FindUniqueContact(expected.Root!, plan.ContactId)!.Remove();
        }

        string nestedBefore = adding ? AbsentContactDigest : ComputeNestedStateDigest(before!);
        string nestedAfter = adding ? ComputeNestedStateDigest(after!) : AbsentContactDigest;
        // Removing an element joins adjacent whitespace text nodes on the next XML parse.
        // Compare their exact serialized bytes, not parser-specific node segmentation.
        return string.Equals(expected.ToString(SaveOptions.DisableFormatting), replacement.ToString(SaveOptions.DisableFormatting), StringComparison.Ordinal)
               && FixedEquals(plan.UntouchedSiblingDigestBefore, ComputeCollectionSiblingDigest(currentRoot, plan.ContactId))
               && FixedEquals(plan.UntouchedSiblingDigestAfter, ComputeCollectionSiblingDigest(replacementRoot, plan.ContactId))
               && FixedEquals(plan.NestedStateDigestBefore, nestedBefore)
               && FixedEquals(plan.NestedStateDigestAfter, nestedAfter)
               && plan.PreservesNestedState == FixedEquals(nestedBefore, nestedAfter);
    }

    internal static string ComputeCollectionSiblingDigest(XElement root, Guid contactId)
    {
        XElement collection = root.Element("contacts") is { } contacts ? new(contacts) : new("contacts");
        foreach (XElement contact in collection.Elements("contact").Where(contact =>
                     Guid.TryParse(ReadValue(contact, "guid"), out Guid id) && id == contactId).ToArray())
            contact.Remove();
        return CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            collection.ToString(SaveOptions.DisableFormatting));
    }

    private static bool HasExactReceiptAuthority(
        CharacterCreationContactReceipt receipt,
        CharacterCreationContactsAuthoritySnapshot current,
        CharacterCreationContactsAuthoritySnapshot replacement)
    {
        if (current.AuthorityBlockers.Count != 0
            || replacement.AuthorityBlockers.Count != 0
            || !current.ContactBudget.IsExact
            || !current.HighPlacesBudget.IsExact
            || !replacement.ContactBudget.IsExact
            || !replacement.HighPlacesBudget.IsExact
            || replacement.ContactBudget.Overspend != 0
            || replacement.HighPlacesBudget.Overspend != 0
            || !FixedEquals(current.SourceDigest, replacement.SourceDigest)
            || !FixedEquals(current.RulesDigest, replacement.RulesDigest)
            || !FixedEquals(current.RuntimeDigest, replacement.RuntimeDigest)
            || !FixedEquals(receipt.SourceDigest, current.SourceDigest)
            || !FixedEquals(receipt.RulesDigest, current.RulesDigest)
            || !FixedEquals(receipt.RuntimeDigest, current.RuntimeDigest))
        {
            return false;
        }

        return receipt.ContactPointsBefore == current.ContactBudget.Used
               && receipt.ContactPointsAfter == replacement.ContactBudget.Used
               && receipt.ContactPointsRemaining == replacement.ContactBudget.Remaining
               && receipt.HighPlacesPointsBefore == current.HighPlacesBudget.Used
               && receipt.HighPlacesPointsAfter == replacement.HighPlacesBudget.Used
               && receipt.HighPlacesPointsRemaining == replacement.HighPlacesBudget.Remaining;
    }

    public static bool IsValidForCommit(
        CharacterWorkspaceId workspaceId,
        long expectedContentRevision,
        long expectedSavedRevision,
        CharacterCreationContactReceiptLedgerEntry? entry)
    {
        return IsStructurallyValid(entry)
            && entry!.Receipt.WorkspaceId == workspaceId
            && entry.Receipt.PreviousWorkspaceRevision == expectedContentRevision
            && entry.Receipt.WorkspaceRevision == expectedContentRevision + 1
            && entry.Receipt.PreviousContentRevision == expectedContentRevision
            && entry.Receipt.ContentRevision == expectedContentRevision + 1
            && entry.Receipt.PreviousSavedRevision == expectedSavedRevision
            && entry.Receipt.SavedRevision == expectedContentRevision + 1;
    }

    public static bool IsValidPersistedEntry(
        CharacterWorkspaceId workspaceId,
        long currentContentRevision,
        CharacterCreationContactReceiptLedgerEntry? entry)
    {
        return IsStructurallyValid(entry)
            && entry!.Receipt.WorkspaceId == workspaceId
            && entry.Receipt.ContentRevision <= currentContentRevision;
    }

    public static bool IsCanonicalDigest(string? value)
        => CharacterCreationFoundationDraftLedgerIntegrity.IsCanonicalDigest(value);

    private static bool IsStructurallyValid(
        CharacterCreationContactReceiptLedgerEntry? entry)
    {
        if (entry?.Receipt is not CharacterCreationContactReceipt receipt
            || !IsCanonicalDigest(entry.IdempotencyKeyDigest)
            || !IsCanonicalDigest(entry.CommandDigest)
            || !string.Equals(entry.IdempotencyKeyDigest, receipt.IdempotencyKeyDigest, StringComparison.Ordinal)
            || !string.Equals(entry.CommandDigest, receipt.CommandDigest, StringComparison.Ordinal)
            || !string.Equals(receipt.Schema, CharacterCreationContactsSchemas.ReceiptV1, StringComparison.Ordinal)
            || !string.Equals(receipt.StepId, CharacterCreationWizardStepIds.ContactsLifestyles, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(receipt.WorkspaceId.Value)
            || receipt.ContactId == Guid.Empty
            || string.IsNullOrWhiteSpace(receipt.ReceiptId)
            || !receipt.ReceiptId.StartsWith("creation-contact-", StringComparison.Ordinal)
            || receipt.ReceiptId.Length != 41
            || receipt.PreviousWorkspaceRevision <= 0
            || receipt.WorkspaceRevision != receipt.PreviousWorkspaceRevision + 1
            || receipt.PreviousContentRevision <= 0
            || receipt.ContentRevision != receipt.PreviousContentRevision + 1
            || receipt.PreviousWorkspaceRevision != receipt.PreviousContentRevision
            || receipt.WorkspaceRevision != receipt.ContentRevision
            || receipt.PreviousSavedRevision < 0
            || receipt.PreviousSavedRevision > receipt.PreviousContentRevision
            || receipt.SavedRevision != receipt.ContentRevision
            || !IsCanonicalDigest(receipt.ContentDigestBefore)
            || !IsCanonicalDigest(receipt.ContentDigestAfter)
            || (receipt.WritePlan?.PendingDraft is null
                ? FixedEquals(receipt.ContentDigestBefore, receipt.ContentDigestAfter)
                : !FixedEquals(receipt.ContentDigestBefore, receipt.ContentDigestAfter))
            || !IsCanonicalDigest(receipt.SourceDigest)
            || !IsCanonicalDigest(receipt.RulesDigest)
            || !IsCanonicalDigest(receipt.RuntimeDigest)
            || receipt.ContactPointsBefore < 0
            || receipt.ContactPointsAfter < 0
            || receipt.ContactPointsRemaining < 0
            || receipt.HighPlacesPointsBefore < 0
            || receipt.HighPlacesPointsAfter < 0
            || receipt.HighPlacesPointsRemaining < 0
            || !IsValidWritePlan(receipt.WritePlan, receipt)
            || !IsCanonicalDigest(receipt.ReceiptDigest)
            || !FixedEquals(receipt.ReceiptDigest, ComputeReceiptDigest(receipt)))
        {
            return false;
        }

        string expectedReceiptId = "creation-contact-"
            + entry.CommandDigest["sha256:".Length..][..24];
        return string.Equals(receipt.ReceiptId, expectedReceiptId, StringComparison.Ordinal);
    }

    private static bool IsValidWritePlan(
        CharacterCreationContactAtomicWritePlan? plan,
        CharacterCreationContactReceipt receipt)
    {
        if (plan is null
            || !Enum.IsDefined(plan.ChangeKind)
            || !string.Equals(plan.Schema, plan.PendingDraft is not null
                ? CharacterCreationContactsSchemas.DraftWritePlanV1 : plan.ChangeKind == CharacterCreationContactChangeKind.Edit
                ? CharacterCreationContactsSchemas.WritePlanV1 : CharacterCreationContactsSchemas.WritePlanV2, StringComparison.Ordinal)
            || plan.PendingDraft is { } draft && (
                !CharacterCreationContactsDraftRules.IsValidShape(receipt.WorkspaceId, receipt.ContentRevision, draft)
                || draft.BaseContentRevision != receipt.PreviousContentRevision
                || !FixedEquals(draft.RawCharacterXmlDigest, receipt.ContentDigestBefore))
            || !string.Equals(plan.StepId, CharacterCreationWizardStepIds.ContactsLifestyles, StringComparison.Ordinal)
            || plan.ContactId != receipt.ContactId
            || plan.Operations is not { Count: > 0 }
            || plan.Operations.Any(operation => operation is null)
            || plan.Operations.Select(operation => operation.Order)
                .SequenceEqual(Enumerable.Range(1, plan.Operations.Count)) is false
            || plan.Operations.Any(operation =>
                string.IsNullOrWhiteSpace(operation.FieldId)
                || operation.BeforeValue is null
                || operation.AfterValue is null
                || string.Equals(operation.BeforeValue, operation.AfterValue, StringComparison.Ordinal)
                || operation.SourceAnchorIds is not { Count: > 0 })
            || !FixedEquals(plan.ContentDigestBefore, receipt.ContentDigestBefore)
            || !FixedEquals(plan.ContentDigestAfter, receipt.ContentDigestAfter)
            || !IsCanonicalDigest(plan.UntouchedSiblingDigestBefore)
            || !FixedEquals(plan.UntouchedSiblingDigestBefore, plan.UntouchedSiblingDigestAfter)
            || !IsCanonicalDigest(plan.NestedStateDigestBefore)
            || !IsCanonicalDigest(plan.NestedStateDigestAfter)
            || plan.PreservesNestedState != FixedEquals(plan.NestedStateDigestBefore, plan.NestedStateDigestAfter)
            || !plan.PreservesUntouchedSiblingState
            || plan.ChangeKind == CharacterCreationContactChangeKind.Edit && !plan.PreservesNestedState
            || !IsCanonicalDigest(plan.PlanDigest))
        {
            return false;
        }

        string expected = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            plan with { PlanDigest = string.Empty });
        return FixedEquals(plan.PlanDigest, expected);
    }

    private static XElement? FindUniqueContact(XElement root, Guid contactId)
    {
        XElement[] matches = (root.Element("contacts")?.Elements("contact") ?? [])
            .Where(contact => Guid.TryParseExact(ReadValue(contact, "guid"), "D", out Guid id)
                              && id == contactId)
            .Take(2)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool TryNormalizeExistingValue(
        XElement contact,
        CharacterContactEditSemantics semantics,
        string fieldId,
        ContactWriteField field,
        out string value)
    {
        switch (fieldId)
        {
            case CharacterCreationContactFieldIds.Connection:
                value = semantics.Connection.ToString(CultureInfo.InvariantCulture);
                return true;
            case CharacterCreationContactFieldIds.Loyalty:
                value = semantics.Loyalty.ToString(CultureInfo.InvariantCulture);
                return true;
            case CharacterCreationContactFieldIds.Group:
                value = semantics.IsGroup.ToString(CultureInfo.InvariantCulture);
                return true;
            case CharacterCreationContactFieldIds.Free:
                value = semantics.Free.ToString(CultureInfo.InvariantCulture);
                return true;
            case CharacterCreationContactFieldIds.Family:
                value = semantics.Family.ToString(CultureInfo.InvariantCulture);
                return true;
            case CharacterCreationContactFieldIds.Blackmail:
                value = semantics.Blackmail.ToString(CultureInfo.InvariantCulture);
                return true;
        }

        string raw = ReadValue(contact, field.ElementName);
        value = raw;
        return field.Kind == ContactWriteValueKind.Text;
    }

    private static bool TryValidateAfterValue(
        string fieldId,
        ContactWriteField field,
        CharacterContactEditSemantics semantics,
        string value)
    {
        switch (field.Kind)
        {
            case ContactWriteValueKind.Text:
                if (value.Length > MaximumTextLength
                    || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
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
            case ContactWriteValueKind.Integer:
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer)
                       && string.Equals(integer.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal)
                       && (fieldId == CharacterCreationContactFieldIds.Connection
                           ? integer is >= 1 && integer <= semantics.ConnectionMaximum
                           : fieldId == CharacterCreationContactFieldIds.Loyalty
                             && integer is >= 1 and <= 6);
            case ContactWriteValueKind.Boolean:
                return string.Equals(value, bool.TrueString, StringComparison.Ordinal)
                       || string.Equals(value, bool.FalseString, StringComparison.Ordinal);
            default:
                return false;
        }
    }

    private static bool IsEditable(
        string fieldId,
        CharacterContactEditSemantics semantics) => fieldId switch
    {
        CharacterCreationContactFieldIds.Connection => semantics.ConnectionEditable,
        CharacterCreationContactFieldIds.Loyalty => semantics.LoyaltyEditable,
        CharacterCreationContactFieldIds.Group => semantics.GroupEditable,
        CharacterCreationContactFieldIds.Free => semantics.FreeEditable,
        CharacterCreationContactFieldIds.Family => semantics.FamilyEditable,
        CharacterCreationContactFieldIds.Blackmail => semantics.BlackmailEditable,
        _ => semantics.IdentityEditable
    };

    private static bool AfterSemanticMatches(
        string fieldId,
        string value,
        CharacterContactEditSemantics semantics) => fieldId switch
    {
        CharacterCreationContactFieldIds.Connection =>
            string.Equals(semantics.Connection.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        CharacterCreationContactFieldIds.Loyalty =>
            string.Equals(semantics.Loyalty.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        CharacterCreationContactFieldIds.Group =>
            string.Equals(semantics.IsGroup.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        CharacterCreationContactFieldIds.Free =>
            string.Equals(semantics.Free.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        CharacterCreationContactFieldIds.Family =>
            string.Equals(semantics.Family.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        CharacterCreationContactFieldIds.Blackmail =>
            string.Equals(semantics.Blackmail.ToString(CultureInfo.InvariantCulture), value, StringComparison.Ordinal),
        _ => true
    };

    private static string ComputeUntouchedSiblingDigest(XElement root, Guid contactId)
    {
        XElement? contacts = root.Element("contacts");
        XElement? target = FindUniqueContact(root, contactId);
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

    private static string ReadValue(XElement parent, string name)
        => parent.Element(name)?.Value.Trim() ?? string.Empty;

    private static bool ParseBool(string value)
        => bool.TryParse(value, out bool parsed) && parsed
           || string.Equals(value, "1", StringComparison.Ordinal);

    private static bool FixedEquals(string left, string right)
        => CharacterCreationPrerequisiteAuthorityDigest.EqualsFixedTime(left, right);

    private enum ContactWriteValueKind
    {
        Text,
        Integer,
        Boolean
    }

    private readonly record struct ContactWriteField(
        string ElementName,
        ContactWriteValueKind Kind);
}
