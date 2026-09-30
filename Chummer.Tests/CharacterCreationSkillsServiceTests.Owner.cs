using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationSkillsServiceTests
{
    [TestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void Owner_bound_skills_preserve_partition_identity_and_cold_replay(string method)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"chummer-owner-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileWorkspaceStore(directory);
            var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
            var ownerA = new OwnerScope("skills-owner-a");
            var ownerB = new OwnerScope("skills-owner-b");
            owners.Set(ownerA);
            OwnerContextStamp original = owners.Capture();
            var id = new CharacterWorkspaceId("scoped-skills");
            string xml = ReadyXml.Replace("<buildmethod>Priority</buildmethod>", $"<buildmethod>{method}</buildmethod>");
            var document = new WorkspaceDocument(xml, RulesetDefaults.Sr5);
            Assert.IsTrue(store.CreateWorkspaceDocument(ownerA, id, document).Success);
            Assert.IsTrue(store.CreateWorkspaceDocument(ownerB, id, document).Success);
            var priorityAuthority = CreatePrerequisiteAuthority(method);
            var resolver = CreateResolver(priorityAuthority, CreateSkillsAuthority(priorityAuthority, xml));
            var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(store, owners,
                new CharacterCreationAttributesServiceTests.StubCharacterQueries(), resolver);
            var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
            var p = prerequisites.Preview(original, new(prerequisites.Load(original, new(id)).Value!.Binding, ranks)
                { HeritageSelectionId = "human", TalentSelectionId = "mundane" }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisites.Confirm(original,
                new(p.Binding, ranks, p.PreviewDigest, true) { HeritageSelectionId = "human", TalentSelectionId = "mundane" }).Outcome);
            var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, resolver);
            CharacterCreationAttributeAllocation[] points = [new("INT", 2, 0), new("LOG", 1, 0)];
            var a = attributes.Preview(original, new(attributes.Load(original, new(id)).Value!.Binding, points)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                attributes.Confirm(original, new(a.Binding, points, a.PreviewDigest, true)).Outcome);
            var before = store.Get(ownerA, id).Value!;
            var other = store.Get(ownerB, id).Value!;
            Assert.IsFalse(store.Get(id).Success, "No legacy record may mask wrong-partition reads.");
            Assert.IsNull(new CharacterCreationSkillsService(store, resolver).Load(new(id)).Value);
            var service = new OwnerBoundCharacterCreationSkillsService(store, owners, resolver);
            var state = service.Load(original, new(id)).Value!;
            Assert.IsTrue(state.CanEdit, string.Join(",", state.Blockers));
            CharacterCreationSkillAllocation[] choices =
            [new(LanguageId, CharacterCreationSkillKinds.Knowledge, null, null, true),
             new(ActiveIds[0], CharacterCreationSkillKinds.Active, 1, null, false)];
            var preview = service.Preview(original, new(state.Binding, choices, [])).Value!;
            Assert.IsTrue(preview.CanConfirm, string.Join(",", preview.Blockers));
            var command = new CharacterCreationSkillsConfirmRequest(preview.Binding, choices, [],
                preview.PreviewDigest, "owner-bound-skills", true);
            foreach (var denied in new[] { default(OwnerContextStamp), original with { Owner = ownerB },
                         original with { AuthorityInstanceId = "foreign" } })
            {
                Assert.IsNull(service.Load(denied, new(id)).Value);
                Assert.IsNull(service.Preview(denied, new(state.Binding, choices, [])).Value);
                Assert.IsNull(service.Confirm(denied, command).Value);
            }
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(original, command with { ExplicitlyConfirmed = false }).Outcome);
            Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success,
                service.Confirm(original, command with { PreviewDigest = Digest('f') }).Outcome);
            owners.Set(ownerB);
            owners.Set(ownerA);
            Assert.IsNull(service.Confirm(original, command).Value, "A→B→A must not revive an old operation.");
            Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
            var fresh = owners.Capture();
            var saved = service.Confirm(fresh, command);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
            var cold = new FileWorkspaceStore(directory);
            var reopened = new OwnerBoundCharacterCreationSkillsService(cold, owners, resolver);
            var current = reopened.Load(fresh, new(id)).Value!;
            Assert.AreEqual(1, current.Skills.Single(x => x.SourceSkillId == ActiveIds[0]).Rating);
            Assert.AreEqual(saved.Value!.ReceiptDigest, reopened.Confirm(fresh, command).Value!.ReceiptDigest);
            var after = cold.Get(ownerA, id).Value!;
            Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
            Assert.AreEqual(after.ContentRevision, after.SavedRevision);
            Assert.AreEqual(xml, after.Document.Content);
            Assert.AreEqual(before.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
                after.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
            Assert.AreEqual(other.Document.AuxiliaryStateDigest, cold.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
            Assert.AreEqual(other.ContentRevision, cold.Get(ownerB, id).Value!.ContentRevision);
            Assert.IsFalse(cold.Get(id).Success);
            Assert.AreEqual(0, owners.ActiveLeases);

            // A later Attributes edit invalidates the saved Skills binding. The
            // review must use the same real owner partition, never legacy Get(id).
            CharacterCreationAttributeAllocation[] changedPoints = [new("INT", 3, 0), new("LOG", 2, 0)];
            var nextAttributes = attributes.Preview(fresh, new(attributes.Load(fresh, new(id)).Value!.Binding, changedPoints)).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attributes.Confirm(fresh,
                new(nextAttributes.Binding, changedPoints, nextAttributes.PreviewDigest, true)).Outcome);
            var beforeReview = store.Get(ownerA, id).Value!;
            IOwnerBoundCharacterCreationSkillsReReviewService reviewService = reopened;
            var review = reviewService.LoadReReview(fresh, new(id));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
            Assert.IsNull(new CharacterCreationSkillsService(cold, resolver).LoadReReview(new(id)).Value);
            var proposal = reviewService.PreviewReReview(fresh, new(review.Value!.Binding, choices, []));
            Assert.IsTrue(proposal.Value!.CurrentPreview.CanConfirm, string.Join(",", proposal.Blockers));
            var reviewCommand = new CharacterCreationSkillsReReviewConfirmRequest(review.Value.Binding, choices, [],
                proposal.Value.PreviewDigest, "owner-bound-skills-rereview", true, true);
            foreach (var denied in new[] { default(OwnerContextStamp), fresh with { Owner = ownerB },
                         fresh with { AuthorityInstanceId = "foreign" } })
            {
                Assert.IsNull(reviewService.LoadReReview(denied, new(id)).Value);
                Assert.IsNull(reviewService.PreviewReReview(denied, new(review.Value.Binding, choices, [])).Value);
                Assert.IsNull(reviewService.ConfirmReReview(denied, reviewCommand).Value);
            }
            Assert.IsNull(reviewService.ConfirmReReview(fresh, reviewCommand with { ExplicitlyReviewedChanges = false }).Value);
            owners.Set(ownerB);
            owners.Set(ownerA);
            Assert.IsNull(reviewService.LoadReReview(fresh, new(id)).Value);
            Assert.IsNull(reviewService.PreviewReReview(fresh, new(review.Value.Binding, choices, [])).Value);
            Assert.IsNull(reviewService.ConfirmReReview(fresh, reviewCommand).Value,
                "An old review must not survive an A→B→A owner transition.");
            Assert.AreEqual(beforeReview.Document.AuxiliaryStateDigest, store.Get(ownerA, id).Value!.Document.AuxiliaryStateDigest);
            var reviewOwner = owners.Capture();
            var reviewed = reviewService.ConfirmReReview(reviewOwner, reviewCommand);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
            var reviewedCold = new FileWorkspaceStore(directory);
            var coldReviewService = new OwnerBoundCharacterCreationSkillsService(reviewedCold, owners, resolver);
            Assert.AreEqual(reviewed.Value!.ReceiptDigest, coldReviewService.ConfirmReReview(reviewOwner, reviewCommand).Value!.ReceiptDigest);
            var final = reviewedCold.Get(ownerA, id).Value!;
            Assert.AreEqual(beforeReview.ContentRevision + 1, final.ContentRevision);
            Assert.AreEqual(final.ContentRevision, final.SavedRevision);
            Assert.AreEqual(saved.Value.ReceiptDigest, final.Document.AuxiliaryState.CharacterCreationSkillsReceipts![0].ReceiptDigest);
            Assert.AreEqual(beforeReview.Document.Content, final.Document.Content);
            Assert.AreEqual(beforeReview.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest,
                final.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
            Assert.AreEqual(other.Document.AuxiliaryStateDigest, reviewedCold.Get(ownerB, id).Value!.Document.AuxiliaryStateDigest);
            Assert.IsFalse(reviewedCold.Get(id).Success);
            Assert.AreEqual(0, owners.ActiveLeases);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
