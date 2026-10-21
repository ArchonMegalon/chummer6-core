using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using HistoryContext989 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    [DataRow("Magician")]
    [DataRow("Mundane")]
    public void Attribute_change_skills_rereview_preserves_choices_and_requires_explicit_current_confirmation(string talent)
    {
        WithRealTalentSkills(talent, (directory, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] choices =
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var preview = service.Preview(new(initial.Binding, choices, [])).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            var originalCommand = new CharacterCreationSkillsConfirmRequest(preview.Binding, choices, [],
                preview.PreviewDigest, "before-attributes-change", true);
            var originalReceipt = service.Confirm(originalCommand).Value!;
            Assert.IsNotNull(originalReceipt);
            var historical = store.Get(id).Value!.Document.AuxiliaryState.CharacterCreationSkillsDraft!;

            SaveSkillsReviewAttributes(store, resolver, id, 3, 5);
            var before = store.Get(id).Value!;
            Assert.IsFalse(Load(service, id).CanEdit, "A stale draft must not become silently editable.");
            var reviewService = new CharacterCreationSkillsService(store, resolver);
            var loaded = reviewService.LoadReReview(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
            var review = loaded.Value!;
            Assert.AreEqual(historical.DraftDigest, review.HistoricalDraft.DraftDigest);
            Assert.AreEqual(4m, (decimal)historical.KnowledgePointTotal);
            Assert.AreEqual(20m, review.InitialPreview.CurrentPreview.KnowledgeSkillPointBudget.Total);
            Assert.IsTrue(review.InitialPreview.CurrentPreview.CanConfirm, string.Join(",", review.InitialPreview.CurrentPreview.Blockers));
            Assert.AreEqual(CharacterCreationSkillsDigest.Compute(historical.Allocations),
                CharacterCreationSkillsDigest.Compute(review.InitialPreview.CurrentPreview.Skills.Select(item =>
                    new CharacterCreationSkillAllocation(item.SourceSkillId, item.Kind, item.Rating,
                        item.SpecializationOptionId, item.IsNativeLanguage)).ToArray()));
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest,
                "Opening or abandoning a re-review is read-only.");

            var command = new CharacterCreationSkillsReReviewConfirmRequest(review.Binding,
                historical.Allocations, historical.GroupAllocations, review.InitialPreview.PreviewDigest,
                "after-attributes-change", true, true);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid,
                reviewService.ConfirmReReview(command with { ExplicitlyReviewedChanges = false }).Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Invalid,
                reviewService.ConfirmReReview(command with { ExplicitlyConfirmed = false }).Outcome);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
            var confirmed = reviewService.ConfirmReReview(command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));

            var coldStore = new FileWorkspaceStore(directory);
            var cold = new CharacterCreationSkillsService(coldStore, resolver);
            var saved = coldStore.Get(id).Value!;
            Assert.AreEqual(before.ContentRevision + 1, saved.ContentRevision);
            Assert.AreEqual(before.Document.Content, saved.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
                saved.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
            var draft = saved.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
            Assert.AreEqual(20, draft.KnowledgePointTotal);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft.DraftRevision, draft.AttributesDraftRevision);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft.DraftDigest, draft.AttributesDraftDigest);
            Assert.AreEqual(historical.DraftRevision + 1, draft.DraftRevision);
            var receipts = saved.Document.AuxiliaryState.CharacterCreationSkillsReceipts!;
            Assert.HasCount(2, receipts);
            Assert.AreEqual(originalReceipt.ReceiptDigest, receipts[0].ReceiptDigest);
            Assert.IsTrue(cold.Load(new(id)).Value!.CanEdit);
            Assert.AreEqual(originalReceipt.ReceiptDigest, cold.Confirm(originalCommand).Value!.ReceiptDigest);
            Assert.AreEqual(confirmed.Value!.ReceiptDigest, cold.ConfirmReReview(command).Value!.ReceiptDigest);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, cold.LoadReReview(new(id)).Outcome);
            Assert.AreEqual(saved.Document.AuxiliaryStateDigest, coldStore.Get(id).Value!.Document.AuxiliaryStateDigest);
        });
    }

    [TestMethod]
    public void Attribute_change_skills_rereview_keeps_overbudget_choices_until_explicit_correction_and_rejects_stale_confirmation()
    {
        WithRealTalentSkills("Mundane", (directory, store, resolver, service, id) =>
        {
            SaveSkillsReviewAttributes(store, resolver, id, 3, 5);
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            var active = initial.Authority.ActiveSkills.Where(item => !item.IsExotic
                && CharacterCreationSkillsAccessRules.IsSkillAvailable(initial.Authority, item.SourceSkillId)
                && !item.RequiresGroundMovement && !item.RequiresSwimMovement && !item.RequiresFlyMovement).Take(5).ToArray();
            Assert.HasCount(5, active);
            var knowledge = initial.Authority.KnowledgeSkills.Where(item => item.SourceSkillId != native.SourceSkillId)
                .Take(2).ToArray();
            CharacterCreationSkillAllocation[] choices =
            [
                new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                .. active.Select((item, index) => new CharacterCreationSkillAllocation(item.SourceSkillId,
                    CharacterCreationSkillKinds.Active, index == 4 ? 4 : 6, null, false)),
                .. knowledge.Select(item => new CharacterCreationSkillAllocation(item.SourceSkillId,
                    CharacterCreationSkillKinds.Knowledge, 6, null, false))
            ];
            var preview = service.Preview(new(initial.Binding, choices, [])).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(new(preview.Binding, choices, [], preview.PreviewDigest, "full-skills", true)).Outcome);
            SaveSkillsReviewAttributes(store, resolver, id, 0, 0);
            var before = store.Get(id).Value!;
            var reviewService = new CharacterCreationSkillsService(store, resolver);
            var loaded = reviewService.LoadReReview(new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
            var review = loaded.Value!;
            Assert.AreEqual(4m, review.InitialPreview.CurrentPreview.KnowledgeSkillPointBudget.Total);
            Assert.AreEqual(8, review.InitialPreview.CurrentPreview.KnowledgePointOverflowToActive);
            Assert.IsFalse(review.InitialPreview.CurrentPreview.CanConfirm);
            Assert.AreEqual(choices.Length, review.InitialPreview.CurrentPreview.Skills.Count,
                "A decreased budget must not silently delete the user's choices.");
            var command = new CharacterCreationSkillsReReviewConfirmRequest(review.Binding, choices, [],
                review.InitialPreview.PreviewDigest, "overbudget-review", true, true);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, reviewService.ConfirmReReview(command).Outcome);
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);

            var corrected = choices.Where(item => item.Kind != CharacterCreationSkillKinds.Knowledge || item.IsNativeLanguage).ToArray();
            var legal = reviewService.PreviewReReview(new(review.Binding, corrected, [])).Value!;
            Assert.IsTrue(legal.CurrentPreview.CanConfirm, string.Join(",", legal.CurrentPreview.Blockers));
            SaveSkillsReviewAttributes(store, resolver, id, 1, 0);
            var afterAttributes = store.Get(id).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, reviewService.ConfirmReReview(command with
            { Allocations = corrected, PreviewDigest = legal.PreviewDigest, IdempotencyKey = "stale-attribute-review" }).Outcome);
            Assert.AreEqual(afterAttributes.Document.AuxiliaryStateDigest, store.Get(id).Value!.Document.AuxiliaryStateDigest);
            var fresh = reviewService.LoadReReview(new(id)).Value!;
            Assert.IsNotNull(fresh);
            var freshPreview = reviewService.PreviewReReview(new(fresh.Binding, corrected, [])).Value!;
            var confirmed = reviewService.ConfirmReReview(new(fresh.Binding, corrected, [], freshPreview.PreviewDigest,
                "corrected-attribute-review", true, true));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
            Assert.IsTrue(new CharacterCreationSkillsService(new FileWorkspaceStore(directory), resolver).Load(new(id)).Value!.CanEdit);
        });
    }

    [TestMethod]
    public void Attribute_change_skills_rereview_rejects_rehashed_source_or_budget_corruption_without_writes()
    {
        WithRealTalentSkills("Mundane", (directory, store, resolver, service, id) =>
        {
            var initial = Load(service, id);
            var native = initial.Authority.KnowledgeSkills.First(item => item.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] choices =
                [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var preview = service.Preview(new(initial.Binding, choices, [])).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            var receipt = service.Confirm(new(preview.Binding, choices, [], preview.PreviewDigest, "before-change", true)).Value!;
            Assert.IsNotNull(receipt);
            SaveSkillsReviewAttributes(store, resolver, id, 3, 5);
            var snapshot = store.Get(id).Value!;
            var old = snapshot.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
            string path = Directory.GetFiles(directory, id.Value + ".json", SearchOption.AllDirectories).Single();
            byte[] original = File.ReadAllBytes(path);
            Func<CharacterCreationSkillsDraft, CharacterCreationSkillsDraft>[] corruptions =
            [
                draft => draft with { SkillsAuthorityDigest = Digest('f') },
                draft => draft with { RuntimeDigest = Digest('f') },
                draft => draft with { BaseRawCharacterXmlDigest = Digest('f') },
                draft => draft with { KnowledgePointTotal = 1000 },
                draft => draft with { KnowledgePointTotal = 5 },
                draft => draft with { AttributesDraftRevision = snapshot.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftRevision },
                draft => draft with { SourceAnchorIds = ["invented-source"] }
            ];
            foreach (var corrupt in corruptions)
            {
                var changed = corrupt(old);
                changed = changed with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(changed) };
                var changedReceipt = receipt with
                {
                    DraftDigest = changed.DraftDigest, SkillsAuthorityDigest = changed.SkillsAuthorityDigest,
                    RuntimeDigest = changed.RuntimeDigest,
                    KnowledgePointsRemaining = changed.KnowledgePointTotal - changed.KnowledgePointUsed,
                    ReceiptDigest = string.Empty
                };
                changedReceipt = changedReceipt with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(changedReceipt) };
                var record = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
                record["AuxiliaryState"] = System.Text.Json.JsonSerializer.SerializeToNode(snapshot.Document.AuxiliaryState with
                    { CharacterCreationSkillsDraft = changed, CharacterCreationSkillsReceipts = [changedReceipt] });
                File.WriteAllText(path, record.ToJsonString());
                byte[] hostile = File.ReadAllBytes(path);
                Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                    new CharacterCreationSkillsService(new FileWorkspaceStore(directory), resolver).LoadReReview(new(id)).Outcome);
                CollectionAssert.AreEqual(hostile, File.ReadAllBytes(path));
            }
            File.WriteAllBytes(path, original);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                new CharacterCreationSkillsService(new FileWorkspaceStore(directory), resolver).LoadReReview(new(id)).Outcome);
        });
    }

    [TestMethod]
    [DataRow("{BOD} + {AGI} * 2 + 0.5", "BOD", 6)]
    [DataRow("{BOD} + {AGI} * 2 + 0.5", "REA", 4)]
    [DataRow("{BODUnaug} + {AGI} div 2", "BOD", 4)]
    [DataRow("7", "REA", 7)]
    public void KnowledgeHistory989_saved_attribute_edit_rereviews_exact_rows_and_preserves_choices(
        string expression, string changedAttribute, int expectedBudget)
    {
        using var context = HistoryContext989.Create(false, includeSkillPurchase: true,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", expression));
        var id = context.WorkspaceId;
        var service = new CharacterCreationSkillsService(context.Store, context.Resolver);
        var state = service.Load(new(id)).Value!;
        var knowledge = state.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage);
        CharacterCreationSkillAllocation[] choices = [.. state.PendingDraft!.Allocations,
            new(knowledge.SourceSkillId, CharacterCreationSkillKinds.Knowledge, 2, null, false)];
        var groups = state.PendingDraft.GroupAllocations;
        var preview = service.Preview(new(state.Binding, choices, groups)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var receipt = service.Confirm(new(preview.Binding, choices, groups, preview.PreviewDigest,
            "knowledge-history989-points", true)).Value!;
        Assert.IsNotNull(receipt);
        var original = context.Store.Get(id).Value!;
        var historical = original.Document.AuxiliaryState.CharacterCreationSkillsDraft!;
        Assert.AreEqual(JsonSerializer.Serialize(original.Document.AuxiliaryState.CharacterCreationAttributesDraft),
            JsonSerializer.Serialize(receipt.AttributesSnapshot), "Evidence must be the actual saved draft, not reconstructed rows.");
        SaveHistoryAttributes989(context, changedAttribute, 2);
        var edited = context.Store.Get(id).Value!;
        string path = HistoryRecord989(context);
        byte[] before = File.ReadAllBytes(path);
        var coldStore = new FileWorkspaceStore(context.Directory);
        var cold = new CharacterCreationSkillsService(coldStore, HistoryResolver989(context));
        Assert.IsFalse(cold.Load(new(id)).Value!.CanEdit);
        var loaded = cold.LoadReReview(new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var review = loaded.Value!;
        var current = review.InitialPreview.CurrentPreview;
        Assert.AreEqual((decimal)expectedBudget, current.KnowledgeSkillPointBudget.Total);
        Assert.AreEqual(CharacterCreationSkillsDigest.Compute(historical.Allocations),
            CharacterCreationSkillsDigest.Compute(current.Skills.Select(row => new CharacterCreationSkillAllocation(
                row.SourceSkillId, row.Kind, row.Rating, row.SpecializationOptionId, row.IsNativeLanguage)).ToArray()));
        Assert.AreEqual(CharacterCreationSkillsDigest.Compute(historical.GroupAllocations),
            CharacterCreationSkillsDigest.Compute(current.SkillGroups.Select(row => new CharacterCreationSkillGroupAllocation(
                row.GroupId, row.Rating)).ToArray()));
        Assert.IsTrue(current.Skills.All(row => row.KarmaLevels == 0 && row.KarmaCost == 0 && !row.KarmaSpecialization));
        Assert.IsTrue(current.CanConfirm, string.Join(",", current.Blockers));
        var command = new CharacterCreationSkillsReReviewConfirmRequest(review.Binding, historical.Allocations,
            historical.GroupAllocations, review.InitialPreview.PreviewDigest, "knowledge-history989-review", true, true);
        Assert.IsNull(cold.ConfirmReReview(command with { ExplicitlyReviewedChanges = false }).Value);
        Assert.IsNull(cold.ConfirmReReview(command with { ExplicitlyConfirmed = false }).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        var confirmed = cold.ConfirmReReview(command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, confirmed.Outcome, string.Join(",", confirmed.Blockers));
        var reopenedStore = new FileWorkspaceStore(context.Directory);
        var reopened = new CharacterCreationSkillsService(reopenedStore, HistoryResolver989(context));
        var saved = reopenedStore.Get(id).Value!;
        Assert.AreEqual(edited.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(edited.Document.Content, saved.Document.Content);
        Assert.AreEqual(JsonSerializer.Serialize(edited.Document.AuxiliaryState.CharacterCreationAttributesDraft),
            JsonSerializer.Serialize(confirmed.Value!.AttributesSnapshot));
        CollectionAssert.AreEqual(original.Document.AuxiliaryState.CharacterCreationSkillsReceipts!
                .Select(row => JsonSerializer.Serialize(row)).ToArray(),
            saved.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Take(2).Select(row => JsonSerializer.Serialize(row)).ToArray());
        Assert.AreEqual(CharacterCreationSkillsDigest.Compute(historical.Allocations),
            CharacterCreationSkillsDigest.Compute(saved.Document.AuxiliaryState.CharacterCreationSkillsDraft!.Allocations));
        Assert.IsTrue(reopened.Load(new(id)).Value!.CanEdit);
        Assert.AreEqual(confirmed.Value.ReceiptDigest, reopened.ConfirmReReview(command).Value!.ReceiptDigest);
        Assert.AreEqual(saved.ContentRevision, reopenedStore.Get(id).Value!.ContentRevision);
    }

    [TestMethod]
    public void KnowledgeHistory989_lower_budget_keeps_choices_and_rejects_overbudget_and_stale_confirmation()
    {
        using var context = HistoryContext989.Create(false,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", "16 - {BOD} * 4"));
        var id = context.WorkspaceId;
        var service = new CharacterCreationSkillsService(context.Store, context.Resolver);
        var state = service.Load(new(id)).Value!;
        var choices = state.PendingDraft!.Allocations.ToList();
        int remaining = state.SelectedActiveSkillPoints;
        foreach (var skill in state.Authority.ActiveSkills.Where(row => !row.IsExotic
                     && CharacterCreationSkillsAccessRules.IsSkillAvailable(state.Authority, row.SourceSkillId)
                     && !row.RequiresGroundMovement && !row.RequiresSwimMovement && !row.RequiresFlyMovement))
        {
            if (remaining == 0) break;
            int rating = Math.Min(remaining, state.Authority.MaxActiveSkillRatingCreate);
            choices.Add(new(skill.SourceSkillId, CharacterCreationSkillKinds.Active, rating, null, false));
            remaining -= rating;
        }
        Assert.AreEqual(0, remaining);
        choices.AddRange(state.Authority.KnowledgeSkills.Where(row => !row.CanBeNativeLanguage).Take(2)
            .Select(row => new CharacterCreationSkillAllocation(row.SourceSkillId, CharacterCreationSkillKinds.Knowledge, 6, null, false)));
        var preview = service.Preview(new(state.Binding, choices, [])).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsNotNull(service.Confirm(new(preview.Binding, choices, [], preview.PreviewDigest,
            "knowledge-history989-full", true)).Value);
        SaveHistoryAttributes989(context, "BOD", 2);
        var cold = new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context));
        var review = cold.LoadReReview(new(id)).Value!;
        Assert.IsNotNull(review);
        Assert.AreEqual(4m, review.InitialPreview.CurrentPreview.KnowledgeSkillPointBudget.Total);
        Assert.AreEqual(8, review.InitialPreview.CurrentPreview.KnowledgePointOverflowToActive);
        Assert.IsTrue(review.InitialPreview.CurrentPreview.Blockers.Contains(CharacterCreationSkillsBlockers.ActiveBudgetExceeded));
        Assert.IsFalse(review.InitialPreview.CurrentPreview.CanConfirm);
        Assert.AreEqual(choices.Count, review.InitialPreview.CurrentPreview.Skills.Count);
        byte[] before = File.ReadAllBytes(HistoryRecord989(context));
        var command = new CharacterCreationSkillsReReviewConfirmRequest(review.Binding, choices, [],
            review.InitialPreview.PreviewDigest, "knowledge-history989-overbudget", true, true);
        Assert.IsNull(cold.ConfirmReReview(command).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(HistoryRecord989(context)));
        var corrected = choices.Where(row => row.Kind != CharacterCreationSkillKinds.Knowledge || row.IsNativeLanguage).ToArray();
        var legal = cold.PreviewReReview(new(review.Binding, corrected, [])).Value!;
        Assert.IsTrue(legal.CurrentPreview.CanConfirm);
        SaveHistoryAttributes989(context, "BOD", 1);
        byte[] after = File.ReadAllBytes(HistoryRecord989(context));
        Assert.IsNull(cold.ConfirmReReview(command with { Allocations = corrected, PreviewDigest = legal.PreviewDigest }).Value);
        CollectionAssert.AreEqual(after, File.ReadAllBytes(HistoryRecord989(context)));
        var fresh = cold.LoadReReview(new(id)).Value!;
        var freshPreview = cold.PreviewReReview(new(fresh.Binding, corrected, [])).Value!;
        Assert.IsNotNull(cold.ConfirmReReview(new(fresh.Binding, corrected, [], freshPreview.PreviewDigest,
            "knowledge-history989-corrected", true, true)).Value);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("rows")]
    [DataRow("rows-rehashed")]
    [DataRow("current-snapshot")]
    [DataRow("raw-character")]
    [DataRow("prerequisite")]
    [DataRow("receipt-draft")]
    public void KnowledgeHistory989_missing_or_rebound_evidence_fails_closed_without_rewriting(string change)
    {
        using var context = HistoryContext989.Create(false,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", "{BOD} * 3"));
        SaveHistoryAttributes989(context, "BOD", 2);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var receipt = saved.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        var evidence = receipt.AttributesSnapshot!;
        Assert.IsNotNull(evidence);
        var changed = change switch
        {
            "missing" => null,
            "rows" or "rows-rehashed" => evidence with { Attributes = evidence.Attributes.Select(row =>
                row.AttributeId == "BOD" ? row with { Current = row.UnaugmentedCurrent + 1 } : row).ToArray() },
            "current-snapshot" => saved.Document.AuxiliaryState.CharacterCreationAttributesDraft,
            "raw-character" => evidence with { BaseRawCharacterXmlDigest = Digest('e') },
            "prerequisite" => evidence with { PrerequisiteAuthorityDigest = Digest('e') },
            _ => evidence
        };
        if (changed is not null && change is ("rows-rehashed" or "raw-character" or "prerequisite"))
            changed = changed with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
                changed with { DraftDigest = string.Empty }) };
        receipt = receipt with { AttributesSnapshot = changed,
            DraftDigest = change == "receipt-draft" ? Digest('e') : receipt.DraftDigest };
        WriteHistoryReceipt989(context, saved, receipt);
        byte[] hostile = File.ReadAllBytes(HistoryRecord989(context));
        Assert.IsNull(new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context))
            .LoadReReview(new(context.WorkspaceId)).Value);
        CollectionAssert.AreEqual(hostile, File.ReadAllBytes(HistoryRecord989(context)));
    }

    [TestMethod]
    public void KnowledgeHistory989_copied_character_evidence_and_changed_profile_are_not_authority()
    {
        using var context = HistoryContext989.Create(false,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", "{BOD} * 3"));
        using var other = HistoryContext989.Create(false,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", "{BOD} * 3"));
        SaveHistoryAttributes989(context, "BOD", 2);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var receipt = saved.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        var otherReceipt = other.Store.Get(other.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        Assert.AreNotEqual(context.WorkspaceId, other.WorkspaceId);
        string path = HistoryRecord989(context);
        byte[] original = File.ReadAllBytes(path);
        WriteHistoryReceipt989(context, saved, receipt with { AttributesSnapshot = otherReceipt.AttributesSnapshot });
        byte[] copied = File.ReadAllBytes(path);
        Assert.IsNull(new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context))
            .LoadReReview(new(context.WorkspaceId)).Value);
        CollectionAssert.AreEqual(copied, File.ReadAllBytes(path));
        File.WriteAllBytes(path, original);
        var review = new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context))
            .LoadReReview(new(context.WorkspaceId)).Value!;
        Assert.IsNotNull(review);
        string settingsPath = Path.Combine(context.Directory, "source", "data",
            CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId));
        var settings = XDocument.Load(settingsPath);
        settings.Root!.Element("settings")!.Elements("setting").Single(row =>
                row.Element("id")!.Value == CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
            .SetElementValue("knowledgepointsexpression", "{BOD} * 4");
        settings.Save(settingsPath);
        var drifted = new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context));
        Assert.IsNull(drifted.LoadReReview(new(context.WorkspaceId)).Value);
        Assert.IsNull(drifted.ConfirmReReview(new(review.Binding, review.HistoricalDraft.Allocations,
            review.HistoricalDraft.GroupAllocations, review.InitialPreview.PreviewDigest,
            "knowledge-history989-source-drift", true, true)).Value);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
    }

    [TestMethod]
    public void KnowledgeHistory989_legacy_default_receipt_bytes_and_rereview_stay_unchanged()
    {
        using var context = HistoryContext989.Create(false);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var receipt = before.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        Assert.IsNull(receipt.AttributesSnapshot);
        string bytes = JsonSerializer.Serialize(receipt);
        Assert.IsFalse(bytes.Contains("AttributesSnapshot", StringComparison.Ordinal));
        var legacy = JsonNode.Parse(bytes)!.AsObject();
        legacy.Remove("AttributesSnapshot");
        legacy["ReceiptDigest"] = string.Empty;
        Assert.AreEqual(receipt.ReceiptDigest, CharacterCreationSkillsDigest.Compute(legacy));
        SaveSkillsReviewAttributes(context.Store, context.Resolver, context.WorkspaceId, 3, 5);
        byte[] saved = File.ReadAllBytes(HistoryRecord989(context));
        var service = new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), context.Resolver);
        Assert.IsNotNull(service.LoadReReview(new(context.WorkspaceId)).Value);
        CollectionAssert.AreEqual(saved, File.ReadAllBytes(HistoryRecord989(context)));
        Assert.AreEqual(bytes, JsonSerializer.Serialize(context.Store.Get(context.WorkspaceId).Value!
            .Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single()));
    }

    [TestMethod]
    public void KnowledgeHistory996_null_first_receipt_is_invalid()
    {
        Assert.IsFalse(CharacterCreationSkillsDraftIntegrity.IsValidReceiptLedger(
            [null!], new CharacterWorkspaceId(Guid.NewGuid().ToString("D")), 1));
    }

    [TestMethod]
    public void KnowledgeHistory996_rehashed_inconsistent_attribute_rows_are_not_history()
    {
        using var context = HistoryContext989.Create(false,
            amendSettings: profile => profile.SetElementValue("knowledgepointsexpression", "7"));
        SaveHistoryAttributes989(context, "BOD", 2);
        var saved = context.Store.Get(context.WorkspaceId).Value!;
        var receipt = saved.Document.AuxiliaryState.CharacterCreationSkillsReceipts!.Single();
        var evidence = receipt.AttributesSnapshot!;
        // Keep the old allocations and budget, but falsify a projected row.
        // The constant expression keeps the complete Skills projection unchanged.
        evidence = evidence with { Attributes = evidence.Attributes.Select(row =>
            row.AttributeId == "REA" ? row with { Current = row.Current + 1 } : row).ToArray() };
        evidence = evidence with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(
            evidence with { DraftDigest = string.Empty }) };
        var historical = saved.Document.AuxiliaryState.CharacterCreationSkillsDraft! with
        {
            AttributesDraftDigest = evidence.DraftDigest
        };
        historical = historical with { DraftDigest = CharacterCreationSkillsDraftIntegrity.ComputeDigest(historical) };
        receipt = receipt with { AttributesSnapshot = evidence, DraftDigest = historical.DraftDigest };
        receipt = receipt with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(receipt) };
        string path = HistoryRecord989(context);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(saved.Document.AuxiliaryState with
        {
            CharacterCreationSkillsDraft = historical,
            CharacterCreationSkillsReceipts = [receipt]
        });
        File.WriteAllText(path, record.ToJsonString());
        byte[] hostile = File.ReadAllBytes(path);
        var cold = new CharacterCreationSkillsService(new FileWorkspaceStore(context.Directory), HistoryResolver989(context));
        Assert.IsNull(cold.LoadReReview(new(context.WorkspaceId)).Value);
        CollectionAssert.AreEqual(hostile, File.ReadAllBytes(path));
    }

    private static ICharacterSourceDataResolver HistoryResolver989(HistoryContext989 context)
    {
        string root = Path.Combine(context.Directory, "source");
        return new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
    }

    private static string HistoryRecord989(HistoryContext989 context) =>
        Directory.GetFiles(context.Directory, context.WorkspaceId.Value + ".json", SearchOption.AllDirectories).Single();

    private static void SaveHistoryAttributes989(HistoryContext989 context, string attribute, int points)
    {
        var service = new CharacterCreationAttributesService(context.Store, context.Resolver);
        var state = service.Load(new(context.WorkspaceId)).Value!;
        CharacterCreationAttributeAllocation[] allocations = [new(attribute, points, 0)];
        var preview = service.Preview(new(state.Binding, allocations)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        Assert.IsNotNull(service.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true)).Value);
    }

    private static void WriteHistoryReceipt989(HistoryContext989 context, WorkspaceStoredDocument saved,
        CharacterCreationSkillsReceipt receipt)
    {
        receipt = receipt with { ReceiptDigest = CharacterCreationSkillsDigest.ComputeReceipt(receipt) };
        string path = HistoryRecord989(context);
        var record = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        record["AuxiliaryState"] = JsonSerializer.SerializeToNode(saved.Document.AuxiliaryState with
            { CharacterCreationSkillsReceipts = [receipt] });
        File.WriteAllText(path, record.ToJsonString());
    }

    private static void SaveSkillsReviewAttributes(FileWorkspaceStore store, ICharacterSourceDataResolver resolver,
        CharacterWorkspaceId id, int intuitionPoints, int logicPoints)
    {
        var attributes = new CharacterCreationAttributesService(store, resolver);
        var state = attributes.Load(new(id)).Value!;
        CharacterCreationAttributeAllocation[] allocations =
            [new("INT", intuitionPoints, 0), new("LOG", logicPoints, 0)];
        var preview = attributes.Preview(new(state.Binding, allocations)).Value!;
        Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
        var saved = attributes.Confirm(new(preview.Binding, allocations, preview.PreviewDigest, true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
    }
}
