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
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Add_then_remove_contact_is_previewed_atomic_and_replays_after_cold_reopen(string buildMethod)
    {
        WithService((store, service, id, directory) =>
        {
            var initial = Load(service, id);
            Assert.IsNotNull(initial.NewContactTemplate);
            Guid addedId = Guid.Parse("44444444-5555-4666-8777-888888888888");
            var add = new CharacterCreationContactEdit(addedId,
                initial.NewContactTemplate.Identity with { Name = "Street Doc", Role = "Medic" },
                Connection: 2, Loyalty: 2) { ChangeKind = CharacterCreationContactChangeKind.Add };
            var preview = Preview(service, initial.Binding, add);
            Assert.IsTrue(preview.ContactBefore.IsAbsent);
            Assert.IsFalse(preview.ContactAfter.IsAbsent);
            Assert.AreEqual(4, preview.ContactAfter.ContactPointCost);
            Assert.AreEqual(12, preview.ContactBudgetAfter.Used);
            Assert.AreEqual(CharacterCreationContactsSchemas.WritePlanV2, preview.WritePlan.Schema);
            Assert.IsFalse(preview.WritePlan.PreservesNestedState);
            Assert.IsTrue(preview.WritePlan.PreservesUntouchedSiblingState);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision, "Preview must not write.");
            var addRequest = new CharacterCreationContactConfirmRequest(initial.Binding, add,
                preview.PreviewDigest, "contact-add", false);
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, service.Confirm(addRequest).Outcome);
            var added = service.Confirm(addRequest with { ExplicitlyConfirmed = true });
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, added.Outcome, string.Join(",", added.Blockers));

            var cold = new CharacterCreationContactsService(new FileWorkspaceStore(directory));
            var reopened = Load(cold, id);
            Assert.AreEqual(3, reopened.Contacts.Count);
            Assert.AreEqual("Street Doc", reopened.Contacts.Single(c => c.ContactId == addedId).Identity.Name);
            Assert.AreEqual(2L, reopened.Binding.SavedRevision);
            Assert.AreEqual(CharacterCreationContactOutcomes.Replayed,
                cold.Confirm(addRequest with { ExplicitlyConfirmed = true }).Outcome);

            var remove = new CharacterCreationContactEdit(addedId) { ChangeKind = CharacterCreationContactChangeKind.Remove };
            var removal = Preview(cold, reopened.Binding, remove);
            Assert.IsTrue(removal.ContactAfter.IsAbsent);
            Assert.AreEqual(8, removal.ContactBudgetAfter.Used);
            var removeRequest = new CharacterCreationContactConfirmRequest(reopened.Binding, remove,
                removal.PreviewDigest, "contact-remove", true);
            var removed = cold.Confirm(removeRequest);
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, removed.Outcome, string.Join(",", removed.Blockers));
            var twiceCold = new CharacterCreationContactsService(new FileWorkspaceStore(directory));
            var final = Load(twiceCold, id);
            Assert.AreEqual(2, final.Contacts.Count);
            Assert.AreEqual(3L, final.Binding.SavedRevision);
            Assert.AreEqual(CharacterCreationContactOutcomes.Replayed, twiceCold.Confirm(removeRequest).Outcome);
            Assert.AreEqual(CharacterCreationContactOutcomes.Replayed,
                twiceCold.Confirm(addRequest with { ExplicitlyConfirmed = true }).Outcome);
            Assert.AreEqual(2, Load(twiceCold, id).Contacts.Count, "Replaying old add must not resurrect a deleted Contact.");
            XDocument original = XDocument.Parse(Fixture().Replace("<buildmethod>Priority</buildmethod>",
                $"<buildmethod>{buildMethod}</buildmethod>", StringComparison.Ordinal), LoadOptions.PreserveWhitespace);
            XDocument finalXml = XDocument.Parse(new FileWorkspaceStore(directory).Get(id).Value!.Document.Content,
                LoadOptions.PreserveWhitespace);
            Assert.IsTrue(XNode.DeepEquals(original, finalXml), "All unrelated XML and nested state must survive.");
        }, Fixture().Replace("<buildmethod>Priority</buildmethod>",
            $"<buildmethod>{buildMethod}</buildmethod>", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Add_first_contact_without_a_container_and_remove_last_contact_preserves_the_runner()
    {
        XDocument empty = XDocument.Parse(Fixture(), LoadOptions.PreserveWhitespace);
        empty.Root!.Element("contacts")!.Remove();
        WithService((_, service, id, _) =>
        {
            var initial = Load(service, id);
            var add = new CharacterCreationContactEdit(ContactId,
                initial.NewContactTemplate!.Identity with { Name = "Fixer" }) { ChangeKind = CharacterCreationContactChangeKind.Add };
            var preview = Preview(service, initial.Binding, add);
            var result = service.Confirm(new(initial.Binding, add, preview.PreviewDigest, "first-contact", true));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, result.Outcome, string.Join(",", result.Blockers));
            var withContact = Load(service, id);
            Assert.AreEqual(1, withContact.Contacts.Count);
            var remove = new CharacterCreationContactEdit(ContactId) { ChangeKind = CharacterCreationContactChangeKind.Remove };
            var removal = Preview(service, withContact.Binding, remove);
            var removed = service.Confirm(new(withContact.Binding, remove, removal.PreviewDigest, "last-contact", true));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, removed.Outcome, string.Join(",", removed.Blockers));
            Assert.AreEqual(0, Load(service, id).Contacts.Count);
        }, empty.ToString(SaveOptions.DisableFormatting));
    }

    [TestMethod]
    public void Collection_changes_reject_collision_overspend_locked_deletion_and_mixed_intent()
    {
        WithService((store, service, id, _) =>
        {
            var state = Load(service, id);
            var identity = state.NewContactTemplate!.Identity with { Name = "New" };
            foreach (Guid collision in new[] { ContactId, PetId })
                Assert.IsFalse(service.Preview(new(state.Binding,
                    new(collision, identity) { ChangeKind = CharacterCreationContactChangeKind.Add })).Success);
            Assert.IsFalse(service.Preview(new(state.Binding,
                new(Guid.NewGuid(), identity, 6, 6) { ChangeKind = CharacterCreationContactChangeKind.Add })).Success);
            Assert.IsFalse(service.Preview(new(state.Binding,
                new(ContactId, Free: true) { ChangeKind = CharacterCreationContactChangeKind.Remove })).Success);
            Assert.IsFalse(service.Preview(new(state.Binding,
                new(PetId) { ChangeKind = CharacterCreationContactChangeKind.Remove })).Success);
            Assert.IsFalse(service.Preview(new(state.Binding,
                new(ContactId) { ChangeKind = (CharacterCreationContactChangeKind)999 })).Success);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
        });
        WithService((_, service, id, _) =>
        {
            var state = Load(service, id);
            Assert.IsFalse(state.Contacts.Single(c => c.ContactId == ContactId).CanDelete);
            var result = service.Preview(new(state.Binding,
                new(ContactId) { ChangeKind = CharacterCreationContactChangeKind.Remove }));
            CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationContactsBlockers.FieldNotEditable);
        }, Fixture().Replace($"<guid>{ContactId:D}</guid>", $"<guid>{ContactId:D}</guid><readonly />", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Historical_edit_command_serialization_omits_new_collection_discriminator()
    {
        var edit = new CharacterCreationContactEdit(ContactId, Free: true);
        var legacyShape = new { edit.ContactId, edit.Identity, edit.Connection, edit.Loyalty,
            edit.IsGroup, edit.Free, edit.Family, edit.Blackmail };
        Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(legacyShape),
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(edit));
    }

    [TestMethod]
    public void Collection_receipts_cannot_authorize_unrelated_writes_even_with_recomputed_hashes()
    {
        foreach (var kind in new[] { CharacterCreationContactChangeKind.Add, CharacterCreationContactChangeKind.Remove })
        WithService((store, service, id, directory) =>
        {
            var original = store.Get(id).Value!;
            var state = Load(service, id);
            var edit = kind == CharacterCreationContactChangeKind.Add
                ? new CharacterCreationContactEdit(Guid.NewGuid(), state.NewContactTemplate!.Identity with { Name = "Medic" }) { ChangeKind = kind }
                : new CharacterCreationContactEdit(ContactId) { ChangeKind = kind };
            var preview = Preview(service, state.Binding, edit);
            var applied = service.Confirm(new(state.Binding, edit, preview.PreviewDigest, "collection-forgery", true));
            Assert.AreEqual(CharacterCreationContactOutcomes.Applied, applied.Outcome, kind + ": " + string.Join(",", applied.Blockers));
            var committed = store.Get(id).Value!.Document;
            XDocument forgedXml = XDocument.Parse(committed.Content, LoadOptions.PreserveWhitespace);
            forgedXml.Root!.Element("root-sentinel")!.SetElementValue("value", "unauthorized");
            string forgedContent = forgedXml.ToString(SaveOptions.DisableFormatting);
            string digest = CharacterCreationContactReceiptLedgerIntegrity.ComputeContentDigest(forgedContent);
            var plan = applied.Value!.WritePlan with { ContentDigestAfter = digest, PlanDigest = string.Empty };
            plan = plan with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(plan) };
            var receipt = applied.Value with { ContentDigestAfter = digest, WritePlan = plan, ReceiptDigest = string.Empty };
            receipt = receipt with { ReceiptDigest = CharacterCreationContactReceiptLedgerIntegrity.ComputeReceiptDigest(receipt) };
            var forged = committed with
            {
                State = committed.State with
                {
                    Payload = forgedContent,
                    AuxiliaryState = committed.AuxiliaryState with
                    {
                        CharacterCreationContactReceipts = [new(receipt.IdempotencyKeyDigest, receipt.CommandDigest, receipt)]
                    }
                }
            };
            var boundary = new FileWorkspaceStore(Path.Combine(directory, "forgery"));
            Assert.IsTrue(boundary.CreateWorkspaceDocument(id, original.Document).Success);
            Assert.IsFalse(boundary.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(id,
                1, original.Document.AuxiliaryStateDigest, forged).Success);
            Assert.AreEqual(original.Document.Content, boundary.Get(id).Value!.Document.Content);
        });
    }

    [TestMethod]
    public void Fractional_contact_modifier_is_rounded_up_before_budget_admission()
    {
        string xml = Fixture().Replace("<improvements />", "<improvements><improvement><improvementttype>ContactKarmaDiscount</improvementttype><val>0.1</val><enabled>1</enabled><condition>create</condition></improvement></improvements>", StringComparison.Ordinal);
        WithService((_, service, id, _) =>
        {
            var state = Load(service, id);
            Assert.AreEqual(9, state.Contacts.Single(contact => contact.ContactId == ContactId).ContactPointCost);
            Assert.AreEqual(9, state.ContactBudget.Used);
        }, xml);
    }

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
    public void Persistence_boundary_recomputes_every_authority_digest_and_budget_receipt_field()
    {
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactPreview preview = Preview(
                service,
                state.Binding,
                new CharacterCreationContactEdit(ContactId, Free: true));

            (string Name, Func<CharacterCreationContactReceipt, CharacterCreationContactReceipt> Forge)[] forgeries =
            [
                ("source", receipt => receipt with { SourceDigest = Sha('a') }),
                ("rules", receipt => receipt with { RulesDigest = Sha('b') }),
                ("runtime", receipt => receipt with { RuntimeDigest = Sha('c') }),
                ("contact-before", receipt => receipt with { ContactPointsBefore = receipt.ContactPointsBefore + 1 }),
                ("contact-after", receipt => receipt with { ContactPointsAfter = receipt.ContactPointsAfter + 1 }),
                ("contact-remaining", receipt => receipt with { ContactPointsRemaining = receipt.ContactPointsRemaining + 1 }),
                ("high-before", receipt => receipt with { HighPlacesPointsBefore = receipt.HighPlacesPointsBefore + 1 }),
                ("high-after", receipt => receipt with { HighPlacesPointsAfter = receipt.HighPlacesPointsAfter + 1 }),
                ("high-remaining", receipt => receipt with { HighPlacesPointsRemaining = receipt.HighPlacesPointsRemaining + 1 })
            ];

            foreach ((string name, Func<CharacterCreationContactReceipt, CharacterCreationContactReceipt> forge) in forgeries)
            {
                WorkspaceStoreMutationResult rejected = DirectCommit(
                    store,
                    id,
                    preview,
                    forge,
                    idempotencySeed: 'd',
                    commandSeed: 'e');
                Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, rejected.Outcome, name);
                Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision, name);
            }
        });
    }

    [TestMethod]
    public void Persistence_boundary_rejects_a_self_consistent_overspend_receipt()
    {
        string smallBudget = Fixture()
            .Replace("<contactpoints>15</contactpoints>", "<contactpoints>4</contactpoints>", StringComparison.Ordinal)
            .Replace("<free>False</free>", "<free>True</free>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactResult<CharacterCreationContactPreview> result = service.Preview(
                new CharacterCreationContactPreviewRequest(
                    state.Binding,
                    new CharacterCreationContactEdit(ContactId, Free: false)));
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, result.Outcome);
            Assert.AreEqual(4, result.Value!.ContactBudgetAfter.Overspend);

            WorkspaceStoreMutationResult rejected = DirectCommit(
                store,
                id,
                result.Value,
                receipt => receipt,
                idempotencySeed: 'f',
                commandSeed: '1');

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, rejected.Outcome);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);
        }, smallBudget);
    }

    [TestMethod]
    public void Persistence_boundary_recomputes_FIH_reclassification_and_accepts_only_the_exact_receipt()
    {
        string highPlaces = Fixture()
            .Replace(
                "<improvements />",
                "<improvements><improvement><improvementttype>FriendsInHighPlaces</improvementttype><enabled>1</enabled><condition>create</condition></improvement></improvements>",
                StringComparison.Ordinal)
            .Replace(
                "<contacts>",
                "<attributes><attribute><name>CHA</name><totalvalue>5</totalvalue></attribute></attributes><contacts>",
                StringComparison.Ordinal)
            .Replace("<connection>3</connection>", "<connection>7</connection>", StringComparison.Ordinal);
        WithService((store, service, id, _) =>
        {
            CharacterCreationContactsState state = Load(service, id);
            CharacterCreationContactPreview preview = Preview(
                service,
                state.Binding,
                new CharacterCreationContactEdit(ContactId, Connection: 8));
            Assert.AreEqual(12, preview.ContactBudgetBefore.Used);
            Assert.AreEqual(0, preview.ContactBudgetAfter.Used);
            Assert.AreEqual(0, preview.HighPlacesBudgetBefore.Used);
            Assert.AreEqual(13, preview.HighPlacesBudgetAfter.Used);
            Assert.AreEqual(7, preview.HighPlacesBudgetAfter.Remaining);

            WorkspaceStoreMutationResult forged = DirectCommit(
                store,
                id,
                preview,
                receipt => receipt with { HighPlacesPointsAfter = receipt.HighPlacesPointsAfter + 1 },
                idempotencySeed: '2',
                commandSeed: '3');
            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, forged.Outcome);
            Assert.AreEqual(1L, store.Get(id).Value!.ContentRevision);

            WorkspaceStoreMutationResult exact = DirectCommit(
                store,
                id,
                preview,
                receipt => receipt,
                idempotencySeed: '4',
                commandSeed: '5');
            Assert.AreEqual(WorkspaceOperationOutcome.Success, exact.Outcome);
            Assert.AreEqual(2L, store.Get(id).Value!.ContentRevision);
        }, highPlaces);
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

    private static WorkspaceStoreMutationResult DirectCommit(
        FileWorkspaceStore store,
        CharacterWorkspaceId id,
        CharacterCreationContactPreview preview,
        Func<CharacterCreationContactReceipt, CharacterCreationContactReceipt> transform,
        char idempotencySeed,
        char commandSeed)
    {
        WorkspaceStoredDocument current = store.Get(id).Value!;
        string replacementContent = ApplyPreview(current.Document.Content, preview);
        Assert.AreEqual(preview.WritePlan.ContentDigestAfter,
            CharacterCreationContactReceiptLedgerIntegrity.ComputeContentDigest(replacementContent));
        string idempotencyDigest = Sha(idempotencySeed);
        string commandDigest = Sha(commandSeed);
        long nextRevision = current.ContentRevision + 1;
        var receipt = new CharacterCreationContactReceipt(
            CharacterCreationContactsSchemas.ReceiptV1,
            "creation-contact-" + commandDigest["sha256:".Length..][..24],
            CharacterCreationWizardStepIds.ContactsLifestyles,
            id,
            preview.ContactAfter.ContactId,
            idempotencyDigest,
            commandDigest,
            current.ContentRevision,
            nextRevision,
            current.ContentRevision,
            nextRevision,
            current.SavedRevision,
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
        receipt = transform(receipt);
        receipt = receipt with
        {
            ReceiptDigest = CharacterCreationContactReceiptLedgerIntegrity.ComputeReceiptDigest(receipt)
        };
        var entry = new CharacterCreationContactReceiptLedgerEntry(
            idempotencyDigest,
            commandDigest,
            receipt);
        WorkspaceDocument replacement = current.Document with
        {
            State = current.Document.State with
            {
                Payload = replacementContent,
                AuxiliaryState = current.Document.AuxiliaryState with
                {
                    CharacterCreationContactReceipts = [entry]
                }
            }
        };
        return store.ReplaceWorkspaceDocumentAndAuxiliaryStateAndCheckpoint(
            id,
            current.ContentRevision,
            current.Document.AuxiliaryStateDigest,
            replacement);
    }

    private static string ApplyPreview(
        string content,
        CharacterCreationContactPreview preview)
    {
        XDocument replacement = XDocument.Parse(content, LoadOptions.PreserveWhitespace);
        XElement contact = Contact(replacement, preview.ContactAfter.ContactId);
        foreach (CharacterCreationContactWriteOperation operation in preview.WritePlan.Operations)
        {
            string elementName = operation.FieldId switch
            {
                CharacterCreationContactFieldIds.Free => "free",
                CharacterCreationContactFieldIds.Connection => "connection",
                _ => throw new InvalidOperationException("Direct authority fixture only supports Free and Connection.")
            };
            XElement? element = contact.Element(elementName);
            if (element is null)
                contact.Add(new XElement(elementName, operation.AfterValue));
            else
                element.Value = operation.AfterValue;
        }
        return replacement.ToString(SaveOptions.DisableFormatting);
    }

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
