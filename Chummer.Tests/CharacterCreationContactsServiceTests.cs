using System.Reflection;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationContactsServiceTests
{
    private static readonly Guid ContactId = Guid.Parse("87796157-0366-4154-836a-034326e8e924");
    private static readonly Guid SiblingId = Guid.Parse("11111111-2222-4333-8444-555555555555");
    private static readonly Guid PetId = Guid.Parse("22222222-3333-4444-8555-666666666666");

    [TestMethod]
    public void Load_projects_exact_creation_authority_budget_fields_options_and_digests()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactResult<CharacterCreationContactsState> result =
                service.Load(new CharacterCreationContactsLoadRequest(id));

            Assert.IsTrue(result.Success);
            Assert.AreEqual(CharacterCreationWizardStepIds.ContactsLifestyles, result.Value!.StepId);
            Assert.AreEqual(1L, result.Value.Binding.WorkspaceRevision);
            Assert.AreEqual(1L, result.Value.Binding.ContentRevision);
            Assert.AreEqual(0L, result.Value.Binding.SavedRevision);
            AssertDigests(
                result.Value.Binding.ContentDigest,
                result.Value.Binding.SourceDigest,
                result.Value.Binding.RulesDigest,
                result.Value.Binding.RuntimeDigest,
                result.Value.SnapshotDigest);
            Assert.AreEqual(15, result.Value.ContactBudget.Total);
            Assert.AreEqual(CharacterCreationContactBudgetIds.Contacts, result.Value.ContactBudget.BudgetId);
            Assert.AreEqual(8, result.Value.ContactBudget.Used);
            Assert.AreEqual(7, result.Value.ContactBudget.Remaining);
            Assert.IsTrue(result.Value.ContactBudget.IsExact);
            Assert.AreEqual(0, result.Value.HighPlacesBudget.Total);
            Assert.AreEqual(CharacterCreationContactBudgetIds.FriendsInHighPlaces, result.Value.HighPlacesBudget.BudgetId);
            Assert.AreEqual(2, result.Value.Contacts.Count);

            CharacterCreationContactProjection contact = result.Value.Contacts.Single(item => item.ContactId == ContactId);
            Assert.AreEqual(8, contact.ContactPointCost);
            Assert.IsTrue(contact.CountsAgainstContactBudget);
            Assert.AreEqual(19, contact.Fields.Count);
            CharacterCreationContactFieldAuthority free = contact.Fields.Single(
                field => field.FieldId == CharacterCreationContactFieldIds.Free);
            Assert.IsTrue(free.IsEditable);
            CollectionAssert.AreEqual(new[] { "false", "true" }, free.LegalOptions.Select(option => option.OptionId).ToArray());
            Assert.IsTrue(free.LegalOptions.All(option => option.SourceAnchorIds.Count > 0));
        });
    }

    [TestMethod]
    public void Preview_free_toggle_emits_one_typed_plan_and_preserves_sibling_and_nested_state()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactResult<CharacterCreationContactPreview> result = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: true)));

            Assert.IsTrue(result.Success);
            CharacterCreationContactPreview preview = result.Value!;
            Assert.IsTrue(preview.CanConfirm);
            Assert.IsFalse(preview.ContactBefore.Free);
            Assert.IsTrue(preview.ContactAfter.Free);
            Assert.AreEqual(8, preview.ContactBudgetBefore.Used);
            Assert.AreEqual(0, preview.ContactBudgetAfter.Used);
            Assert.AreEqual(15, preview.ContactBudgetAfter.Remaining);
            Assert.AreEqual(1, preview.WritePlan.Operations.Count);
            Assert.AreEqual(CharacterCreationContactFieldIds.Free, preview.WritePlan.Operations[0].FieldId);
            Assert.AreEqual("False", preview.WritePlan.Operations[0].BeforeValue);
            Assert.AreEqual("True", preview.WritePlan.Operations[0].AfterValue);
            Assert.IsTrue(preview.WritePlan.PreservesUntouchedSiblingState);
            Assert.IsTrue(preview.WritePlan.PreservesNestedState);
            Assert.AreEqual(preview.WritePlan.UntouchedSiblingDigestBefore, preview.WritePlan.UntouchedSiblingDigestAfter);
            Assert.AreEqual(preview.WritePlan.NestedStateDigestBefore, preview.WritePlan.NestedStateDigestAfter);
            AssertDigests(preview.WritePlan.PlanDigest, preview.PreviewDigest);
        });
    }

    [TestMethod]
    public void Confirm_is_explicit_atomic_restart_safe_idempotent_and_receipt_lookup_recovers_unknown_outcome()
    {
        WithService((store, service, id, stateDirectory) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactEdit edit = new(ContactId, Free: true);
            CharacterCreationContactPreview preview = Preview(service, state.Binding, edit);
            var unconfirmed = new CharacterCreationContactConfirmRequest(
                state.Binding,
                edit,
                preview.PreviewDigest,
                "android-create-contact-free-001",
                ExplicitlyConfirmed: false);

            CharacterCreationContactResult<CharacterCreationContactReceipt> rejected = service.Confirm(unconfirmed);
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, rejected.Outcome);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationContactsBlockers.ExplicitConfirmationRequired);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);

            CharacterCreationContactResult<CharacterCreationContactReceipt> digestRejected = service.Confirm(
                unconfirmed with
                {
                    PreviewDigest = Sha('e'),
                    ExplicitlyConfirmed = true
                });
            Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, digestRejected.Outcome);
            CollectionAssert.Contains(digestRejected.Blockers.ToArray(), CharacterCreationContactsBlockers.PreviewDigestMismatch);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);

            CharacterCreationContactResult<CharacterCreationContactReceipt> applied = service.Confirm(
                unconfirmed with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, applied.Outcome);
            CharacterCreationContactReceipt receipt = applied.Value!;
            Assert.AreEqual(1L, receipt.PreviousWorkspaceRevision);
            Assert.AreEqual(2L, receipt.WorkspaceRevision);
            Assert.AreEqual(1L, receipt.PreviousContentRevision);
            Assert.AreEqual(2L, receipt.ContentRevision);
            Assert.AreEqual(0L, receipt.PreviousSavedRevision);
            Assert.AreEqual(2L, receipt.SavedRevision);
            Assert.AreEqual(8, receipt.ContactPointsBefore);
            Assert.AreEqual(0, receipt.ContactPointsAfter);
            Assert.AreEqual(15, receipt.ContactPointsRemaining);
            AssertDigests(receipt.IdempotencyKeyDigest, receipt.CommandDigest, receipt.ReceiptDigest);

            WorkspaceStoredDocument persisted = store.Get(id).Value!;
            Assert.AreEqual(2L, persisted.ContentRevision);
            Assert.AreEqual(2L, persisted.SavedRevision);
            Assert.AreEqual(1, persisted.Document.AuxiliaryState.CharacterCreationContactReceipts!.Count);
            XDocument persistedXml = XDocument.Parse(persisted.Document.Content);
            XElement persistedContact = Contact(persistedXml, ContactId);
            Assert.AreEqual("True", persistedContact.Element("free")!.Value);
            Assert.AreEqual("keep me", persistedContact.Element("chummercomplete")!.Element("sentinel")!.Value);
            Assert.AreEqual("Sibling", Contact(persistedXml, SiblingId).Element("name")!.Value);
            Assert.AreEqual("Critter", Contact(persistedXml, PetId).Element("name")!.Value);

            var restarted = new CharacterCreationContactsService(new FileWorkspaceStore(stateDirectory));
            CharacterCreationContactsState reopened = Load(restarted, id);
            Assert.IsTrue(reopened.Contacts.Single(item => item.ContactId == ContactId).Free);
            CharacterCreationContactResult<CharacterCreationContactReceipt> lookup = restarted.LookupReceipt(
                new CharacterCreationContactReceiptLookupRequest(id, "android-create-contact-free-001"));
            Assert.AreEqual(CharacterCreationContactOutcomes.Available, lookup.Outcome);
            Assert.AreEqual(receipt.ReceiptId, lookup.Value!.ReceiptId);

            CharacterCreationContactResult<CharacterCreationContactReceipt> replayed = restarted.Confirm(
                unconfirmed with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCreationContactOutcomes.Replayed, replayed.Outcome);
            Assert.AreEqual(receipt.ReceiptId, replayed.Value!.ReceiptId);
            Assert.AreEqual(2L, new FileWorkspaceStore(stateDirectory).Get(id).Value!.ContentRevision);

            CharacterCreationContactResult<CharacterCreationContactReceipt> conflict = restarted.Confirm(
                unconfirmed with
                {
                    Edit = new CharacterCreationContactEdit(ContactId, Family: false),
                    ExplicitlyConfirmed = true
                });
            Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, conflict.Outcome);
            CollectionAssert.Contains(conflict.Blockers.ToArray(), CharacterCreationContactsBlockers.IdempotencyConflict);
        });
    }

    [TestMethod]
    public void Invalid_or_duplicate_contact_identity_blocks_state_and_preview_without_throwing()
    {
        string invalid = Fixture().Replace($"<guid>{ContactId:D}</guid>", "<guid>not-a-guid</guid>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactInvalid);
            CharacterCreationContactResult<CharacterCreationContactPreview> preview = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: true)));
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, preview.Outcome);
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactInvalid);
        }, invalid);

        string duplicate = Fixture().Replace(SiblingId.ToString("D"), ContactId.ToString("D"), StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationContactsBlockers.ContactAmbiguous);
            CharacterCreationContactResult<CharacterCreationContactPreview> preview = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: true)));
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, preview.Outcome);
        }, duplicate);
    }

    [TestMethod]
    public void Aggregate_contact_cost_overflow_fails_closed_without_throwing()
    {
        string overflow = Fixture()
            .Replace(
                "<improvements />",
                "<improvements><improvement><improvementttype>ContactKarmaDiscount</improvementttype><val>2147483639</val><enabled>1</enabled><condition>create</condition></improvement></improvements>",
                StringComparison.Ordinal)
            .Replace("<contactpoints>15</contactpoints>", "<contactpoints>2147483647</contactpoints>", StringComparison.Ordinal)
            .Replace("<group>True</group>", "<group>False</group>", StringComparison.Ordinal);

        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);

            Assert.IsFalse(state.CanEdit);
            Assert.IsFalse(state.ContactBudget.IsExact);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationContactsBlockers.AuthorityUnavailable);
            CharacterCreationContactResult<CharacterCreationContactPreview> preview = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: true)));
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, preview.Outcome);
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationContactsBlockers.AuthorityUnavailable);
        }, overflow);
    }

    [TestMethod]
    public void Persistence_boundary_rejects_a_self_consistent_receipt_bound_to_different_content()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactEdit edit = new(ContactId, Free: true);
            CharacterCreationContactPreview preview = Preview(service, state.Binding, edit);
            CharacterCreationContactReceipt first = service.Confirm(
                new CharacterCreationContactConfirmRequest(
                    state.Binding,
                    edit,
                    preview.PreviewDigest,
                    "valid-first",
                    ExplicitlyConfirmed: true)).Value!;
            WorkspaceStoredDocument current = store.Get(id).Value!;
            string forgedCommand = Sha('a');
            string forgedIdempotency = Sha('b');
            string forgedContent = current.Document.Content.Replace(
                "<name>Critter</name>",
                "<name>Mutated pet</name>",
                StringComparison.Ordinal);
            string forgedAfter = CharacterCreationContactReceiptLedgerIntegrity.ComputeContentDigest(
                forgedContent);
            CharacterCreationContactAtomicWritePlan forgedPlan = first.WritePlan with
            {
                ContentDigestBefore = CharacterCreationContactReceiptLedgerIntegrity.ComputeContentDigest(
                    current.Document.Content),
                ContentDigestAfter = forgedAfter,
                PlanDigest = string.Empty
            };
            forgedPlan = forgedPlan with
            {
                PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                    forgedPlan with { PlanDigest = string.Empty })
            };
            CharacterCreationContactReceipt forged = first with
            {
                ReceiptId = "creation-contact-" + forgedCommand["sha256:".Length..][..24],
                IdempotencyKeyDigest = forgedIdempotency,
                CommandDigest = forgedCommand,
                PreviousWorkspaceRevision = 2,
                WorkspaceRevision = 3,
                PreviousContentRevision = 2,
                ContentRevision = 3,
                PreviousSavedRevision = 2,
                SavedRevision = 3,
                ContentDigestBefore = forgedPlan.ContentDigestBefore,
                ContentDigestAfter = forgedAfter,
                WritePlan = forgedPlan,
                ReceiptDigest = string.Empty
            };
            forged = forged with
            {
                ReceiptDigest = CharacterCreationContactReceiptLedgerIntegrity.ComputeReceiptDigest(forged)
            };
            CharacterCreationContactReceiptLedgerEntry[] forgedLedger =
            [
                .. current.Document.AuxiliaryState.CharacterCreationContactReceipts!,
                new CharacterCreationContactReceiptLedgerEntry(forgedIdempotency, forgedCommand, forged)
            ];
            WorkspaceDocument forgedReplacement = current.Document with
            {
                State = current.Document.State with
                {
                    Payload = forgedContent,
                    AuxiliaryState = current.Document.AuxiliaryState with
                    {
                        CharacterCreationContactReceipts = forgedLedger
                    }
                }
            };

            WorkspaceStoreMutationResult rejected = store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
                id,
                current.ContentRevision,
                current.Document.AuxiliaryStateDigest,
                forgedReplacement);

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, rejected.Outcome);
            WorkspaceStoredDocument unchanged = store.Get(id).Value!;
            Assert.AreEqual(2L, unchanged.ContentRevision);
            Assert.AreEqual(2L, unchanged.SavedRevision);
            Assert.AreEqual(1, unchanged.Document.AuxiliaryState.CharacterCreationContactReceipts!.Count);
        });
    }

    [TestMethod]
    public void Career_mode_and_free_from_improvement_fail_closed()
    {
        string career = Fixture().Replace("<created>False</created>", "<created>True</created>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            Assert.IsTrue(state.CharacterCreated);
            Assert.IsFalse(state.CanEdit);
            CollectionAssert.Contains(state.Blockers.ToArray(), CharacterCreationContactsBlockers.CareerModeRejected);
            Assert.IsFalse(state.Contacts.Single(item => item.ContactId == ContactId).Fields
                .Single(field => field.FieldId == CharacterCreationContactFieldIds.Free).IsEditable);
            CharacterCreationContactResult<CharacterCreationContactPreview> preview = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: true)));
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, preview.Outcome);
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationContactsBlockers.CareerModeRejected);
        }, career);

        string forcedFree = Fixture().Replace(
            "<improvements />",
            $"<improvements><improvement><improvementttype>ContactMakeFree</improvementttype><improvedname>{ContactId:D}</improvedname><enabled>1</enabled><condition>create</condition></improvement></improvements>",
            StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactProjection contact = state.Contacts.Single(item => item.ContactId == ContactId);
            Assert.IsTrue(contact.Free);
            Assert.IsFalse(contact.Fields.Single(field => field.FieldId == CharacterCreationContactFieldIds.Free).IsEditable);
            CharacterCreationContactResult<CharacterCreationContactPreview> preview = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: false)));
            CollectionAssert.Contains(preview.Blockers.ToArray(), CharacterCreationContactsBlockers.FieldNotEditable);
        }, forcedFree);
    }

    [TestMethod]
    public void Bounds_budget_overspend_and_stale_revision_or_digest_are_rejected()
    {
        string smallBudget = Fixture().Replace("<contactpoints>15</contactpoints>", "<contactpoints>4</contactpoints>", StringComparison.Ordinal)
            .Replace("<free>False</free>", "<free>True</free>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactResult<CharacterCreationContactPreview> tooHigh = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Connection: 7)));
            CollectionAssert.Contains(tooHigh.Blockers.ToArray(), CharacterCreationContactsBlockers.MutationInvalid);

            CharacterCreationContactIdentity invalidXml = state.Contacts
                .Single(item => item.ContactId == ContactId)
                .Identity with { Name = "\ud800" };
            CharacterCreationContactResult<CharacterCreationContactPreview> invalidText = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Identity: invalidXml)));
            CollectionAssert.Contains(invalidText.Blockers.ToArray(), CharacterCreationContactsBlockers.MutationInvalid);

            CharacterCreationContactResult<CharacterCreationContactPreview> overspent = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: false)));
            CollectionAssert.Contains(overspent.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetExceeded);
            Assert.IsFalse(overspent.Value!.CanConfirm);

            foreach (CharacterCreationContactBinding stale in new[]
            {
                state.Binding with { WorkspaceRevision = 2, ContentRevision = 2 },
                state.Binding with { ContentDigest = Sha('1') },
                state.Binding with { SourceDigest = Sha('2') },
                state.Binding with { RulesDigest = Sha('3') },
                state.Binding with { RuntimeDigest = Sha('4') },
                state.Binding with { AuxiliaryStateDigest = new string('5', 64) }
            })
            {
                CharacterCreationContactResult<CharacterCreationContactPreview> conflict = service.Preview(
                    new CharacterCreationContactPreviewRequest(
                        stale,
                        new CharacterCreationContactEdit(ContactId, Free: false)));
                Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, conflict.Outcome);
            }
        }, smallBudget);
    }

    [TestMethod]
    public void Group_edit_uses_Chummer5_budget_exclusion_and_locked_or_linked_fields_cannot_change()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactPreview grouped = Preview(
                service,
                state.Binding,
                new CharacterCreationContactEdit(ContactId, IsGroup: true));
            Assert.AreEqual(0, grouped.ContactBudgetAfter.Used);
            Assert.IsTrue(grouped.ContactAfter.IsGroup);
            Assert.AreEqual(1, grouped.ContactAfter.Loyalty);
        });

        string locked = Fixture()
            .Replace("<file></file>", "<file>linked.chum5</file>", StringComparison.Ordinal)
            .Replace("<type>Contact</type>", "<readonly /><type>Contact</type>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactIdentity renamed = state.Contacts.Single(item => item.ContactId == ContactId).Identity with
            {
                Name = "Forged"
            };
            CharacterCreationContactResult<CharacterCreationContactPreview> blocked = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, renamed, Connection: 4)));
            CollectionAssert.Contains(blocked.Blockers.ToArray(), CharacterCreationContactsBlockers.FieldNotEditable);
            Assert.IsFalse(blocked.Value!.CanConfirm);
        }, locked);
    }

    [TestMethod]
    public void Atomic_write_fault_preserves_old_document_revision_checkpoint_and_receipt_absence()
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            var normalStore = new FileWorkspaceStore(stateDirectory);
            CharacterWorkspaceId id = new("creation-contact-fault");
            Assert.IsTrue(normalStore.CreateWorkspaceDocument(id, Document(Fixture())).Success);
            var normalService = new CharacterCreationContactsService(normalStore);
            CharacterCreationContactsState state = Load(normalService, id);
            CharacterCreationContactEdit edit = new(ContactId, Free: true);
            CharacterCreationContactPreview preview = Preview(normalService, state.Binding, edit);
            string path = Path.Combine(stateDirectory, "workspaces", id.Value + ".json");
            byte[] before = File.ReadAllBytes(path);
            var failingStore = new FileWorkspaceStore(
                stateDirectory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));
            var failingService = new CharacterCreationContactsService(failingStore);

            CharacterCreationContactResult<CharacterCreationContactReceipt> failed = failingService.Confirm(
                new CharacterCreationContactConfirmRequest(
                    state.Binding,
                    edit,
                    preview.PreviewDigest,
                    "fault-001",
                    ExplicitlyConfirmed: true));

            Assert.AreEqual(CharacterCreationContactOutcomes.Unavailable, failed.Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            WorkspaceStoredDocument reopened = new FileWorkspaceStore(stateDirectory).Get(id).Value!;
            Assert.AreEqual(1L, reopened.ContentRevision);
            Assert.AreEqual(0L, reopened.SavedRevision);
            Assert.IsFalse(ParseFree(reopened.Document.Content, ContactId));
            Assert.IsNull(reopened.Document.AuxiliaryState.CharacterCreationContactReceipts);
            Assert.IsFalse(Directory.EnumerateFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".tmp.*").Any());
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Public_contract_boundary_contains_no_generic_dictionary_or_XML_mutation_input()
    {
        Type[] requestTypes =
        [
            typeof(CharacterCreationContactsLoadRequest),
            typeof(CharacterCreationContactPreviewRequest),
            typeof(CharacterCreationContactConfirmRequest),
            typeof(CharacterCreationContactReceiptLookupRequest),
            typeof(CharacterCreationContactEdit)
        ];
        foreach (Type type in requestTypes)
        {
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.IsFalse(typeof(XNode).IsAssignableFrom(property.PropertyType), $"{type.Name}.{property.Name}");
                Assert.IsFalse(property.PropertyType.IsGenericType
                    && property.PropertyType.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>),
                    $"{type.Name}.{property.Name}");
            }
        }
        Assert.IsFalse(typeof(CharacterCreationContactPreviewRequest).GetProperties()
            .Any(property => property.PropertyType == typeof(CharacterCreationContactAtomicWritePlan)));
        Assert.IsFalse(typeof(CharacterCreationContactConfirmRequest).GetProperties()
            .Any(property => property.PropertyType == typeof(CharacterCreationContactAtomicWritePlan)));
        Assert.IsNotNull(typeof(ICharacterCreationContactsService).GetMethod(nameof(ICharacterCreationContactsService.LookupReceipt)));
        Assert.AreEqual(19, CharacterCreationContactFieldIds.All.Count);
        Assert.AreEqual(19, CharacterCreationContactFieldIds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(CharacterCreationBudgetIds.Contacts, CharacterCreationContactBudgetIds.Contacts);
        Assert.AreEqual("friends-in-high-places-contacts", CharacterCreationContactBudgetIds.FriendsInHighPlaces);
    }

    private static CharacterCreationContactsState Load(
        ICharacterCreationContactsService service,
        CharacterWorkspaceId id)
    {
        CharacterCreationContactResult<CharacterCreationContactsState> result = service.Load(
            new CharacterCreationContactsLoadRequest(id));
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static CharacterCreationContactPreview Preview(
        ICharacterCreationContactsService service,
        CharacterCreationContactBinding binding,
        CharacterCreationContactEdit edit)
    {
        CharacterCreationContactResult<CharacterCreationContactPreview> result = service.Preview(
            new CharacterCreationContactPreviewRequest(binding, edit));
        Assert.IsTrue(result.Success, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static void WithService(
        Action<FileWorkspaceStore, CharacterCreationContactsService, CharacterWorkspaceId, string> action,
        string? xml = null)
    {
        string stateDirectory = CreateStateDirectory();
        try
        {
            var store = new FileWorkspaceStore(stateDirectory);
            CharacterWorkspaceId id = new("creation-contact-authority");
            Assert.IsTrue(store.CreateWorkspaceDocument(id, Document(xml ?? Fixture())).Success);
            action(store, new CharacterCreationContactsService(store), id, stateDirectory);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static WorkspaceDocument Document(string xml) => new(xml, "sr5", WorkspaceDocumentFormat.Chum5Xml);

    private static string Fixture() => $"""
<character>
  <created>False</created>
  <gameedition>SR5</gameedition>
  <settings>default.xml</settings>
  <buildmethod>Priority</buildmethod>
  <contactpoints>15</contactpoints>
  <improvements />
  <contacts>
    <contact>
      <guid>{ContactId:D}</guid><name>Fixer</name><role>Broker</role><location>Vienna</location>
      <notes>trusted</notes><extra>Neon</extra><metatype>Human</metatype><gender>Female</gender><age>38</age>
      <contacttype>Professional</contacttype><preferredpayment>Nuyen</preferredpayment>
      <hobbiesvice>Chess</hobbiesvice><personallife>Private</personallife><groupname></groupname>
      <connection>3</connection><loyalty>2</loyalty><group>False</group><free>False</free>
      <family>True</family><blackmail>True</blackmail><file></file><relative></relative><type>Contact</type>
      <chummercomplete><sentinel>keep me</sentinel><nested><value>42</value></nested></chummercomplete>
    </contact>
    <contact>
      <guid>{SiblingId:D}</guid><name>Sibling</name><connection>4</connection><loyalty>4</loyalty>
      <group>True</group><free>False</free><family>False</family><blackmail>False</blackmail><type>Contact</type>
      <sibling-sentinel><value>unchanged</value></sibling-sentinel>
    </contact>
    <contact>
      <guid>{PetId:D}</guid><name>Critter</name><connection>1</connection><loyalty>1</loyalty>
      <group>False</group><free>False</free><family>False</family><blackmail>False</blackmail><type>Pet</type>
      <pet-sentinel><value>unchanged</value></pet-sentinel>
    </contact>
    <contact>
      <guid>33333333-4444-4555-8666-777777777777</guid><name>Rival</name><type>Enemy</type>
      <enemy-sentinel><value>unchanged</value></enemy-sentinel>
    </contact>
  </contacts>
  <root-sentinel><value>untouched</value></root-sentinel>
</character>
""";

    private static XElement Contact(XDocument document, Guid id) => document.Root!.Element("contacts")!
        .Elements("contact").Single(contact => contact.Element("guid")?.Value == id.ToString("D"));

    private static bool ParseFree(string xml, Guid id)
        => bool.Parse(Contact(XDocument.Parse(xml), id).Element("free")!.Value);

    private static string Sha(char value) => "sha256:" + new string(value, 64);

    private static void AssertDigests(params string[] values)
    {
        foreach (string value in values)
        {
            Assert.AreEqual(71, value.Length);
            StringAssert.StartsWith(value, "sha256:");
        }
    }

    private static string CreateStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-creation-contacts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ThrowingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _stage;

        public ThrowingFaultInjector(FileWorkspaceStoreFaultStage stage)
        {
            _stage = stage;
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == _stage)
                throw new IOException("Injected creation-contact commit failure.");
        }
    }
}
