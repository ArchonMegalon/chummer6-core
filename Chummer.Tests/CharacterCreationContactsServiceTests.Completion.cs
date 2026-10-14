using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class ContactsCompletionTests
{
    [TestMethod]
    public void Contacts_completion_before_saved_stages_uses_proposed_charisma_without_writing_drafts()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationQualitiesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationResourcesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationGearDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationContactsDraft);
        var service = Service(context);
        Assert.IsFalse(service.Load(new(context.WorkspaceId)).Value!.CanEdit,
            "The ordinary Contacts editor still requires the saved creation graph.");
        var inputs = LoadInputs(context);
        Assert.IsNull(inputs.PendingDraft);
        Assert.AreEqual(0, inputs.Receipts.Count);
        Assert.AreEqual(0, inputs.Contacts.Count);
        Assert.AreEqual(inputs.SnapshotDigest, CharacterCreationContactsCompletionRules.ComputeInputsDigest(inputs));
        var contact = Contact("Street Medic", connection: 2, loyalty: 2);
        var baseline = CharacterCreationContactsService.PreviewCompletion(inputs, [], [], [contact]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, baseline.Outcome);
        Assert.IsNotNull(baseline.Value);
        Assert.AreEqual(3, baseline.Value!.ContactBudget.Total);
        Assert.AreEqual(4, baseline.Value.ContactBudget.Used);
        CollectionAssert.Contains(baseline.Blockers.ToArray(), CharacterCreationContactsBlockers.BudgetExceeded);

        CharacterCreationAttributeAllocation[] attributes = [new("CHA", 2, 0)];
        var result = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], [contact]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        Assert.AreEqual(3, projection.Upstream.Attributes.Attributes.Single(item => item.AttributeId == "CHA").Current);
        Assert.AreEqual(9, projection.ContactBudget.Total);
        Assert.AreEqual(4, projection.ContactBudget.Used);
        Assert.AreEqual(5, projection.ContactBudget.Remaining);
        Assert.AreEqual(0, projection.HighPlacesBudget.Total);
        Assert.AreEqual((int?)0, projection.AdditionalKarmaCost);
        Assert.AreEqual(contact.ContactId, projection.Contacts.Single().ContactId);
        Assert.AreEqual(contact.Identity, projection.Contacts.Single().Identity);
        Assert.IsNull(typeof(CharacterCreationContactsCompletionProjection).GetProperty("CanConfirm"));
        Assert.AreEqual(projection.ProposalDigest, CharacterCreationContactsCompletionRules.ComputeProjectionDigest(projection));
        Assert.AreEqual(projection.ProposalDigest,
            CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], [contact]).Value!.ProposalDigest);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Contacts_completion_applies_real_friends_in_high_places_and_rejects_unpriced_or_invalid_choices()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputs = LoadInputs(context);
        var qualities = MagicResonanceCompletionTests.CompletionQualities(context)
            .LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Outcome, string.Join(",", qualities.Blockers));
        var highPlaces = qualities.Value!.Authority.Options.Single(item =>
            item.SourceId == Guid.Parse("e07fc29c-55cc-46b3-af56-3b1de76f0a89") && item.Rating == 1);
        Assert.AreEqual("Friends in High Places", highPlaces.Name);
        Assert.IsTrue(highPlaces.IsSelectable, highPlaces.DisableReasonKey);
        Assert.AreEqual(8, highPlaces.KarmaCost);
        CharacterCreationAttributeAllocation[] attributes = [new("CHA", 2, 0)];
        var executive = Contact("Corporate Patron", connection: 8, loyalty: 1);
        var withoutQuality = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], [executive]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, withoutQuality.Outcome);

        var result = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [highPlaces.OptionId], [executive]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        Assert.AreEqual(9, projection.ContactBudget.Total);
        Assert.AreEqual(0, projection.ContactBudget.Used);
        Assert.AreEqual(12, projection.HighPlacesBudget.Total);
        Assert.AreEqual(9, projection.HighPlacesBudget.Used);
        Assert.AreEqual(3, projection.HighPlacesBudget.Remaining);
        Assert.IsTrue(projection.Contacts.Single().CountsAgainstHighPlacesBudget);
        Assert.IsFalse(projection.Contacts.Single().CountsAgainstContactBudget);
        Assert.AreEqual((int?)0, projection.AdditionalKarmaCost);
        Assert.AreEqual(inputs.Qualities.Binding.CreationKarmaTotal - 8,
            projection.Upstream.Qualities.KarmaRemaining);

        var overBudget = CharacterCreationContactsService.PreviewCompletion(inputs, attributes,
            [highPlaces.OptionId], [executive, Contact("Second Patron", connection: 8, loyalty: 1)]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, overBudget.Outcome);
        Assert.AreEqual(18, overBudget.Value!.HighPlacesBudget.Used);
        CollectionAssert.Contains(overBudget.Blockers.ToArray(), CharacterCreationContactsBlockers.HighPlacesBudgetExceeded);

        var group = Contact("Neighborhood Association", connection: 1, loyalty: 1) with { IsGroup = true };
        var unpriced = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], [group]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, unpriced.Outcome);
        Assert.IsNull(unpriced.Value!.AdditionalKarmaCost,
            "Unsupported paid groups must not be represented as free completion choices.");
        CollectionAssert.Contains(unpriced.Blockers.ToArray(), CharacterCreationContactsCompletionSchemas.KarmaContributionUnavailable);

        foreach (var outOfRange in new[]
        {
            Contact("Invalid Connection", connection: 999, loyalty: 1),
            Contact("Invalid Loyalty", connection: 1, loyalty: 999)
        })
        {
            var invalid = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], [outOfRange]);
            Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, invalid.Outcome);
            Assert.IsTrue(invalid.Blockers.Count > 0,
                "Out-of-range proposed ratings must be rejected, not silently clamped into an allowed selection.");
        }

        var duplicate = CharacterCreationContactsService.PreviewCompletion(inputs, attributes,
            [highPlaces.OptionId], [executive, executive]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, duplicate.Outcome);
        Assert.IsTrue(duplicate.Blockers.Count > 0);
        var unknownQuality = CharacterCreationContactsService.PreviewCompletion(inputs, attributes,
            ["not-an-admitted-source-quality"], [Contact("Street Medic", 1, 1)]);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, unknownQuality.Outcome);
        Assert.IsTrue(unknownQuality.Blockers.Count > 0);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Saved_contacts_survive_cold_completion_and_proposal_digest_cannot_confirm_or_repair_stale_history()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true);
        var service = Service(context);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
        var contactId = Guid.NewGuid();
        var add = new CharacterCreationContactEdit(contactId,
            state.NewContactTemplate!.Identity with { Name = "Street Medic" }, Connection: 1, Loyalty: 1)
        { ChangeKind = CharacterCreationContactChangeKind.Add };
        var review = service.Preview(new(state.Binding, add)).Value!;
        Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
        var command = new CharacterCreationContactConfirmRequest(state.Binding, add, review.PreviewDigest,
            "completion-contacts-real-save", true);
        var receipt = service.Confirm(command);
        Assert.AreEqual(CharacterCreationContactOutcomes.Applied, receipt.Outcome, string.Join(",", receipt.Blockers));

        using ReadyContext cold = context.Restart();
        service = Service(cold);
        var before = cold.Store.Get(cold.WorkspaceId).Value!;
        var files = CaptureFiles(cold);
        var saved = before.Document.AuxiliaryState.CharacterCreationContactsDraft!;
        var inputs = LoadInputs(cold);
        Assert.AreEqual(saved.DraftDigest, inputs.PendingDraft!.DraftDigest);
        Assert.AreEqual(saved.Contacts.Single(), inputs.Contacts.Single());
        Assert.AreEqual(receipt.Value!.ReceiptDigest, inputs.Receipts.Single().Receipt.ReceiptDigest);
        var attributes = before.Document.AuxiliaryState.CharacterCreationAttributesDraft!.Allocations;
        var selectedQualityIds = MagicResonanceCompletionTests.CompletionQualities(cold)
            .LoadCompletionInputs(new(cold.WorkspaceId)).Value!.SelectedOptionIds;
        var result = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, selectedQualityIds, saved.Contacts);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(contactId, result.Value!.Contacts.Single().ContactId);
        Assert.AreEqual("Street Medic", result.Value.Contacts.Single().Identity.Name);
        Assert.AreEqual(2, result.Value.ContactBudget.Used);
        Assert.AreEqual(1, before.Document.AuxiliaryState.CharacterCreationContactReceipts!.Count);
        Assert.AreEqual(receipt.Value!.ReceiptDigest,
            before.Document.AuxiliaryState.CharacterCreationContactReceipts!.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(receipt.Value.ReceiptDigest,
            service.LookupReceipt(new(cold.WorkspaceId, command.IdempotencyKey)).Value!.ReceiptDigest);
        Assert.AreEqual(CharacterCreationContactOutcomes.Replayed, service.Confirm(command).Outcome);
        AssertWorkspaceUnchanged(cold, before, files);

        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(inputs.PendingDraft),
            CharacterCreationFinalizationDigest.Compute(inputs.Receipts[^1].Receipt.WritePlan.PendingDraft));
        var missingLedger = inputs with { Receipts = [] };
        missingLedger = missingLedger with
        {
            SnapshotDigest = CharacterCreationContactsCompletionRules.ComputeInputsDigest(missingLedger)
        };
        var withoutHistory = CharacterCreationContactsService.PreviewCompletion(
            missingLedger, attributes, selectedQualityIds, saved.Contacts);
        Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, withoutHistory.Outcome);
        Assert.IsNull(withoutHistory.Value,
            "Rehashing a retained draft without its real receipt ledger must not manufacture completion authority.");
        CollectionAssert.Contains(withoutHistory.Blockers.ToArray(), CharacterCreationContactsCompletionSchemas.SnapshotConflict);
        AssertWorkspaceUnchanged(cold, before, files);

        var renamed = saved.Contacts.Single() with { Identity = saved.Contacts.Single().Identity with { Name = "Trusted Medic" } };
        var proposal = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, selectedQualityIds, [renamed]).Value!;
        var edit = new CharacterCreationContactEdit(contactId, Identity: renamed.Identity);
        var ordinaryBinding = service.Load(new(cold.WorkspaceId)).Value!.Binding;
        var rejected = service.Confirm(new(ordinaryBinding, edit, proposal.ProposalDigest,
            "completion-projection-not-contact-authority", true));
        Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationContactsBlockers.PreviewDigestMismatch);
        AssertWorkspaceUnchanged(cold, before, files);

        MagicResonanceCompletionTests.SaveAttributes(cold, [new("CHA", 1, 0)]);
        before = cold.Store.Get(cold.WorkspaceId).Value!;
        files = CaptureFiles(cold);
        var stale = service.Confirm(new(ordinaryBinding, edit, proposal.ProposalDigest,
            "completion-stale-contact-authority", true));
        Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, stale.Outcome);
        Assert.IsNull(stale.Value);
        CollectionAssert.Contains(stale.Blockers.ToArray(), CharacterCreationContactsBlockers.StaleWorkspaceRevision);
        var staleHistory = service.LoadCompletionInputs(new(cold.WorkspaceId));
        Assert.AreNotEqual(CharacterCreationContactOutcomes.Available, staleHistory.Outcome);
        Assert.IsNull(staleHistory.Value, "Completion must not repair saved Contacts whose Attributes dependency changed.");
        Assert.AreEqual(saved.DraftDigest,
            cold.Store.Get(cold.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationContactsDraft!.DraftDigest);
        AssertWorkspaceUnchanged(cold, before, files);
    }

    [TestMethod]
    public void Contacts_completion_cancellation_bad_digest_and_detached_proposals_preserve_exact_workspace_files()
    {
        using ReadyContext context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var inputs = LoadInputs(context);
        CharacterCreationAttributeAllocation[] attributes = [new("CHA", 2, 0)];
        var contacts = new List<CharacterCreationKarmaContactSelection> { Contact("Street Medic", 2, 2) };
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool cancellationObserved = false;
        try
        {
            CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], contacts, canceled.Token);
        }
        catch (OperationCanceledException)
        {
            cancellationObserved = true;
        }
        Assert.IsTrue(cancellationObserved);
        var corrupt = CharacterCreationContactsService.PreviewCompletion(
            inputs with { SnapshotDigest = CharacterCreationFinalizationDigest.ComputeUtf8("not-admitted") },
            attributes, [], contacts);
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, corrupt.Outcome);
        Assert.IsNull(corrupt.Value);
        CollectionAssert.Contains(corrupt.Blockers.ToArray(), CharacterCreationContactsBlockers.AuthorityUnavailable);

        var result = CharacterCreationContactsService.PreviewCompletion(inputs, attributes, [], contacts);
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        var projection = result.Value!;
        string captured = CharacterCreationFinalizationDigest.Compute(projection);
        attributes[0] = new("CHA", 100, 100);
        contacts[0] = contacts[0] with { Connection = 100, Identity = contacts[0].Identity with { Name = "Changed caller" } };
        contacts.Clear();
        Assert.AreEqual(captured, CharacterCreationFinalizationDigest.Compute(projection));
        Assert.AreEqual("Street Medic", projection.Contacts.Single().Identity.Name);
        Assert.AreEqual(4, projection.ContactBudget.Used);
        Assert.AreEqual(inputs.SnapshotDigest, LoadInputs(context).SnapshotDigest);
        AssertWorkspaceUnchanged(context, before, files);

        // Combine two genuine observations across an actual saved Attributes
        // transition. Rehashing the outer envelope cannot make the old binding current.
        MagicResonanceCompletionTests.SaveAttributes(context, [new("CHA", 1, 0)]);
        var current = LoadInputs(context);
        Assert.AreNotEqual(inputs.Binding.ContentRevision, current.Binding.ContentRevision);
        var mixed = inputs with { Qualities = current.Qualities };
        mixed = mixed with { SnapshotDigest = CharacterCreationContactsCompletionRules.ComputeInputsDigest(mixed) };
        before = context.Store.Get(context.WorkspaceId).Value!;
        files = CaptureFiles(context);
        var refused = CharacterCreationContactsService.PreviewCompletion(mixed, [new("CHA", 2, 0)], [], []);
        Assert.AreEqual(CharacterCreationContactOutcomes.Conflict, refused.Outcome);
        Assert.IsNull(refused.Value);
        CollectionAssert.Contains(refused.Blockers.ToArray(), CharacterCreationContactsCompletionSchemas.SnapshotConflict);
        AssertWorkspaceUnchanged(context, before, files);
    }

    [TestMethod]
    public void Completion_refuses_foreign_readonly_linked_contacts_without_dropping_original_xml()
    {
        using ReadyContext context = ReadyContext.Create(includeGearReview: true, beforeDrafts: root =>
            root.Element("contacts")!.Add(new XElement("contact",
                new XElement("guid", Guid.Parse("6deaf65c-1ad1-452d-91ba-64b845039580").ToString("D")),
                new XElement("name", "Retained linked contact"),
                new XElement("connection", 1), new XElement("loyalty", 1),
                new XElement("readonly"), new XElement("file", "retained-contact.chum5"),
                new XElement("type", "Contact"),
                new XElement("original-metadata", "preserve"))));
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = CaptureFiles(context);
        var result = Service(context).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationContactOutcomes.Blocked, result.Outcome);
        Assert.IsNull(result.Value,
            "A bootstrap completion proposal must not silently replace original readonly or linked contacts.");
        Assert.IsTrue(result.Blockers.Count > 0);
        AssertWorkspaceUnchanged(context, before, files);
    }

    private static CharacterCreationContactsService Service(ReadyContext context)
        => new(context.Store, context.Resolver, context.Queries);

    private static CharacterCreationContactsCompletionInputs LoadInputs(ReadyContext context)
    {
        var result = Service(context).LoadCompletionInputs(new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationContactOutcomes.Available, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        return result.Value!;
    }

    private static CharacterCreationKarmaContactSelection Contact(string name, int connection, int loyalty)
        => new(Guid.NewGuid(), new CharacterCreationContactIdentity(name, "Professional", "Seattle", "", "",
            "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), connection, loyalty);

    private static IReadOnlyDictionary<string, byte[]> CaptureFiles(ReadyContext context)
        => Directory.GetFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertWorkspaceUnchanged(ReadyContext context, WorkspaceStoredDocument before,
        IReadOnlyDictionary<string, byte[]> files)
    {
        var after = context.Store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision, after.ContentRevision);
        Assert.AreEqual(before.SavedRevision, after.SavedRevision);
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, after.Document.AuxiliaryStateDigest);
        var current = CaptureFiles(context);
        CollectionAssert.AreEquivalent(files.Keys.ToArray(), current.Keys.ToArray());
        foreach (var file in files)
            CollectionAssert.AreEqual(file.Value, current[file.Key], file.Key);
    }
}
