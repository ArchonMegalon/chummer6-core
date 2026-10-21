using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners747 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using ReadyContext747 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Controller-run acceptance proposals. These use the existing real source,
    // owner-bound Draft/attribute services and private recovery fixture.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HistoryPricing747_second_draft_prices_current_attributes_and_preserves_exact_cold_history(bool savedMagic)
    {
        using var context = PricingContext747(savedMagic);
        var owners = new AllocationOwners747();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        var firstReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(
            owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var original = store.Get(context.WorkspaceId).Value!;
        var oldQuality = original.Document.AuxiliaryState.CharacterCreationQualitiesDraft!;
        var oldMagic = original.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft;
        Assert.IsTrue(oldQuality.SelectedOptionIds.Count > 0);
        Assert.AreEqual(savedMagic, oldMagic is not null);
        byte[] firstEntry = JsonSerializer.SerializeToUtf8Bytes(original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]);
        byte[] firstSources = File.ReadAllBytes(Artifact418(context));
        string firstCampaign = Manifest418(context)["CommittedDraftCampaigns"]![0]!.ToJsonString();

        decimal attributeCost = EditAttributes747(context, store, owners, karmaLevels: 1);
        var edited = store.Get(context.WorkspaceId).Value!;
        Assert.AreNotEqual(oldQuality.AttributesDraftDigest,
            edited.Document.AuxiliaryState.CharacterCreationAttributesDraft!.DraftDigest);
        Assert.AreEqual(JsonSerializer.Serialize(oldQuality),
            JsonSerializer.Serialize(edited.Document.AuxiliaryState.CharacterCreationQualitiesDraft));
        Assert.AreEqual(JsonSerializer.Serialize(oldMagic),
            JsonSerializer.Serialize(edited.Document.AuxiliaryState.CharacterCreationMagicResonanceDraft));
        byte[] recordBeforeReview = File.ReadAllBytes(Record418(context));
        var second = ReviewPricing747(context, store, owners);
        var review = second.Command.Review.Review;
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, review.Request.RuleSourceExecutionSchema);
        Assert.AreEqual(attributeCost, review.Skills!.Attributes.CreationKarmaBudget.Used);
        decimal oldAttributeCost = first.Command.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used;
        Assert.IsTrue(attributeCost > oldAttributeCost);
        Assert.AreEqual((decimal)first.Command.Review.Review.Balances.RemainingCreationKarma - attributeCost + oldAttributeCost,
            (decimal)review.Balances.RemainingCreationKarma);
        Assert.AreEqual(first.Command.Review.Review.Balances.RemainingNuyen, review.Balances.RemainingNuyen);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            first.Command.Review.Review.Request.Selections,
            review.Request.Selections with { AttributeAllocations = first.Command.Review.Review.Request.Selections.AttributeAllocations }));
        CollectionAssert.AreEqual(recordBeforeReview, File.ReadAllBytes(Record418(context)));
        var secondReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(
            owners.Capture(), second.Command,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)));
        var secondEntry = store.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts![1];
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(secondEntry.Baseline.SelectedReplay, out _, out var witness));
        byte[] secondBytes = JsonSerializer.SerializeToUtf8Bytes(secondEntry);
        byte[] witnessBytes = JsonSerializer.SerializeToUtf8Bytes(witness);
        var beforeClosure = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        var sourceArchives = ReadWire654(beforeClosure.ExportPrivateBytes()).Archives;

        // A further review must walk past the second snapshot to the same owning
        // witness. The saved V2 request, full review and compressed witness stay exact.
        var third = ReviewPricing747(context, store, owners);
        var thirdReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(
            owners.Capture(), third.Command,
            CharacterCreationCampaignExecutionContext.Capture(third.Authority, CharacterCreationCampaignPhase.Apply)));
        Assert.AreEqual(review.Balances, third.Command.Review.Review.Balances);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        third.Authority.State = CharacterCreationCampaignState.Unavailable;
        third.Authority.Failure = CharacterCreationCampaignFailure.Unavailable;
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, "stage=history747-cold;outcome=reopen-refused");
        var entries = reopened.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        Assert.AreEqual(3, entries.Count);
        CollectionAssert.AreEqual(new[] { firstReceipt, secondReceipt, thirdReceipt }, entries.Select(row => row.Receipt).ToArray());
        CollectionAssert.AreEqual(firstEntry, JsonSerializer.SerializeToUtf8Bytes(entries[0]));
        CollectionAssert.AreEqual(secondBytes, JsonSerializer.SerializeToUtf8Bytes(entries[1]));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entries[1].Baseline.SelectedReplay, out _, out var reopenedWitness));
        CollectionAssert.AreEqual(witnessBytes, JsonSerializer.SerializeToUtf8Bytes(reopenedWitness));
        CollectionAssert.AreEqual(firstSources, File.ReadAllBytes(Artifact418(context)));
        Assert.IsTrue(Manifest418(context)["CommittedDraftCampaigns"]!.AsArray()
            .Any(row => row!.ToJsonString() == firstCampaign));
        var coldClosure = cold.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        var coldArchives = ReadWire654(coldClosure.ExportPrivateBytes()).Archives;
        Assert.IsTrue(sourceArchives.Count > 0);
        foreach (byte[] archive in sourceArchives)
            Assert.IsTrue(coldArchives.Any(value => value.AsSpan().SequenceEqual(archive)),
                "stage=history747-cold;outcome=source-archive-changed");
        byte[] record = File.ReadAllBytes(Record418(context));
        var service = DraftService550(context, cold, owners);
        Assert.AreEqual(secondReceipt, Success425(service.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(second.Command))));
        Assert.IsNull(service.ApplyDraftWithCampaign(owners.Capture(),
            third.Command with { IdempotencyKey = "history747-fresh-without-current-authority" },
            CharacterCreationCampaignExecutionContext.Capture(third.Authority, CharacterCreationCampaignPhase.Apply)).Value);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void HistoryPricing747_altered_references_or_historical_snapshot_refuse_actual_review_and_apply()
    {
        using var context = PricingContext747(savedMagic: true);
        var owners = new AllocationOwners747();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        EditAttributes747(context, store, owners, karmaLevels: 1);
        var second = ReviewPricing747(context, store, owners);
        string original = File.ReadAllText(Record418(context));
        Action<JsonNode>[] corruptions =
        [
            state => state["CharacterCreationQualitiesDraft"]!["AttributesDraftDigest"] = Hash518("history747-altered"),
            state => state["CharacterCreationMagicResonanceDraft"]!["AttributesDraftDigest"] = Hash518("history747-altered"),
            state =>
            {
                // Repointing a retained stage to today's valid attributes is also forbidden.
                state["CharacterCreationQualitiesDraft"]!["AttributesDraftRevision"] =
                    state["CharacterCreationAttributesDraft"]!["DraftRevision"]!.DeepClone();
                state["CharacterCreationQualitiesDraft"]!["AttributesDraftDigest"] =
                    state["CharacterCreationAttributesDraft"]!["DraftDigest"]!.DeepClone();
            },
            state => state["CharacterCreationCompletionDrafts"]![0]!["Baseline"]!["OriginalState"]!["CharacterCreationAttributesDraft"] =
                state["CharacterCreationAttributesDraft"]!.DeepClone(),
            state => state["CharacterCreationCompletionDrafts"]![0]!["Baseline"]!["Schema"] = "unknown-history747"
        ];
        foreach (var corrupt in corruptions)
        {
            var record = JsonNode.Parse(original)!;
            corrupt(record["AuxiliaryState"]!);
            File.WriteAllText(Record418(context), record.ToJsonString());
            byte[] tampered = File.ReadAllBytes(Record418(context));
            var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
            Assert.IsFalse(cold.Get(context.WorkspaceId).Success);
            var service = DraftService550(context, cold, owners);
            Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
                second.Command.Review.Review.Request,
                CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Review)).Value);
            Assert.IsNull(service.ApplyDraftWithCampaign(owners.Capture(), second.Command,
                CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)).Value);
            CollectionAssert.AreEqual(tampered, File.ReadAllBytes(Record418(context)));
        }
        File.WriteAllText(Record418(context), original);
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void HistoryPricing747_history_does_not_authorize_current_campaign_source_owner_or_revision()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new AllocationOwners747();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        EditAttributes747(context, store, owners, karmaLevels: 1);
        var second = ReviewPricing747(context, store, owners);
        var service = DraftService550(context, store, owners);
        byte[] record = File.ReadAllBytes(Record418(context));
        void RefuseCurrent()
        {
            Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
                second.Command.Review.Review.Request,
                CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Review)).Value);
            Assert.IsNull(service.ApplyDraftWithCampaign(owners.Capture(), second.Command,
                CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)).Value);
            CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        }
        second.Authority.State = CharacterCreationCampaignState.Denied;
        second.Authority.Failure = CharacterCreationCampaignFailure.Denied;
        RefuseCurrent();
        second.Authority.State = CharacterCreationCampaignState.AdmittedUnchangedPolicy;
        second.Authority.Failure = CharacterCreationCampaignFailure.None;
        Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
            second.Command.Review.Review.Request,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Review)).Value);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));

        string sourcePath = Path.Combine(context.Directory, "source", "data", "qualities.xml");
        byte[] source = File.ReadAllBytes(sourcePath);
        File.AppendAllText(sourcePath, "\n<!-- history747 current source drift -->\n");
        RefuseCurrent();
        File.WriteAllBytes(sourcePath, source);
        // Prove the drift check was a current-source refusal, not broken setup.
        second = ReviewPricing747(context, store, owners);
        EditAttributes747(context, store, owners, karmaLevels: 0);
        record = File.ReadAllBytes(Record418(context));
        RefuseCurrent();
        second = ReviewPricing747(context, store, owners);
        var staleOwner = owners.Capture();
        owners.Set(new OwnerScope("history747-other"));
        Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
            second.Command.Review.Review.Request,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Review)).Value);
        owners.Set(staleOwner.Owner);
        Assert.IsNull(service.ReviewDraftWithCampaign(staleOwner, context.WorkspaceId,
            second.Command.Review.Review.Request,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Review)).Value);
        Assert.IsNull(service.ApplyDraftWithCampaign(staleOwner, second.Command,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)).Value);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static ReadyContext747 PricingContext747(bool savedMagic)
        => ReadyContext747.Create(includeGearReview: false, includeNonEmptyPurchases: true,
            talentValue: savedMagic ? "Mystic Adept" : null, mysticPowerPoints: savedMagic ? 1 : 0,
            qualityName: "Ambidextrous", prepareSource: _ => { });

    private static decimal EditAttributes747(ReadyContext747 context, FileWorkspaceStore store,
        AllocationOwners747 owners, int karmaLevels)
    {
        var service = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver);
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent,
            row.AttributeId == "BOD" ? karmaLevels : row.KarmaLevels)).ToArray();
        var preview = Success425(service.Preview(owners.Capture(), new(state.Binding, allocations)));
        Assert.IsTrue(preview.CanConfirm, "stage=history747-edit;outcome=preview-refused");
        Success425(service.Confirm(owners.Capture(), new(state.Binding, allocations, preview.PreviewDigest, true)));
        return preview.CreationKarmaBudget.Used;
    }

    private static (CharacterCreationCompletionDraftApplyRequest Command, CampaignAuthority518 Authority) ReviewPricing747(
        ReadyContext747 context, FileWorkspaceStore store, AllocationOwners747 owners,
        int maximumBytes = 4 * 1024 * 1024)
    {
        var observation = ObservePrivate654(context, store, owners, maximumBytes);
        var review = Success425(DraftService550(context, store, owners).ReviewDraftWithCampaign(owners.Capture(),
            context.WorkspaceId, observation.Proposal,
            CharacterCreationCampaignExecutionContext.Capture(observation.Authority, CharacterCreationCampaignPhase.Review)));
        return (new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, review,
            "history747-draft-" + review.Binding.ContentRevision, true), observation.Authority);
    }
}
