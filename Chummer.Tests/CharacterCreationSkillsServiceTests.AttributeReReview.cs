using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
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
