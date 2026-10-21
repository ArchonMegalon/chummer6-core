using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Owners774 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using Ready774 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    // Controller-run proposals. No runtime outcome is claimed by this packet.
    [TestMethod]
    [DataRow("BOD")]
    [DataRow("LOG")]
    public void Attributes774_full_draft_reprices_requested_attribute_preserves_origins_and_cold_retry(string attribute)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        var firstReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var original = store.Get(context.WorkspaceId).Value!;
        byte[] oldEntry = JsonSerializer.SerializeToUtf8Bytes(original.Document.AuxiliaryState.CharacterCreationCompletionDrafts![0]);
        byte[] oldSource = File.ReadAllBytes(Artifact418(context));
        var oldSources = RetainedSources774(context);
        var oldReferences = Manifest418(context)["References"]!.AsArray().Select(row => row!.ToJsonString()).ToArray();
        string oldCampaign = Manifest418(context)["CommittedDraftCampaigns"]![0]!.ToJsonString();
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var requested = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == attribute ? 1 : 0))).ToArray();
        byte[] before = File.ReadAllBytes(Record418(context));
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, requested)));
        var repeated = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, requested.Reverse().ToArray())));
        Assert.AreEqual(preview.PreviewDigest, repeated.PreviewDigest);
        AssertRows774(requested, preview.Allocations);
        var review = preview.Review.Review;
        AssertPreserved774(first.Command.Review.Review.Request.Selections, review.Request.Selections);
        Assert.AreEqual(CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2, review.Request.RuleSourceExecutionSchema);
        Assert.AreEqual(CharacterCreationCompleteAssessment.SchemaV4, review.Request.CompleteAssessmentSchema);
        decimal oldCost = first.Command.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used;
        decimal newCost = review.Skills!.Attributes.CreationKarmaBudget.Used;
        Assert.IsTrue(newCost > oldCost);
        Assert.AreEqual((decimal)first.Command.Review.Review.Balances.RemainingCreationKarma - newCost + oldCost,
            (decimal)review.Balances.RemainingCreationKarma);
        Assert.AreEqual(first.Command.Review.Review.Balances.RemainingNuyen, review.Balances.RemainingNuyen);
        if (attribute == "LOG")
            Assert.IsTrue(review.Skills.KnowledgeSkillPointBudget.Total > first.Command.Review.Review.Skills!.KnowledgeSkillPointBudget.Total);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        const string key = "attributes774-full-rebase";
        var receipt = Success425(service.ConfirmRebase(owners.Capture(), preview, key, true));
        var saved = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(original.ContentRevision + 1, saved.ContentRevision);
        Assert.AreEqual(saved.ContentRevision, saved.SavedRevision);
        Assert.AreEqual(original.Document.Content, saved.Document.Content); // Stay in Create.
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        var entries = saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            original.Document.AuxiliaryState with { CharacterCreationCompletionDrafts = null }, entries[1].Baseline.OriginalState));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            original.Document.AuxiliaryState with { CharacterCreationCompletionDrafts = null },
            saved.Document.AuxiliaryState with { CharacterCreationCompletionDrafts = null }));
        CollectionAssert.AreEqual(oldEntry, JsonSerializer.SerializeToUtf8Bytes(entries[0]));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entries[1].Baseline.SelectedReplay, out _, out var witness));
        byte[] entryBytes = JsonSerializer.SerializeToUtf8Bytes(entries[1]);
        byte[] witnessBytes = JsonSerializer.SerializeToUtf8Bytes(witness);
        before = File.ReadAllBytes(Record418(context));
        Assert.AreEqual(receipt, Success425(service.ConfirmRebase(owners.Capture(), preview, key, true)));
        Assert.IsNull(service.ConfirmRebase(owners.Capture(), preview, key + "-different", true).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));

        var next = ReviewPricing747(context, store, owners);
        Assert.AreEqual(review.Balances, next.Command.Review.Review.Balances);
        AssertRows774(requested, next.Command.Review.Review.Request.Selections.AttributeAllocations);
        AssertPreserved774(review.Request.Selections, next.Command.Review.Review.Request.Selections);
        var third = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), next.Command,
            CharacterCreationCampaignExecutionContext.Capture(next.Authority, CharacterCreationCampaignPhase.Apply)));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        factory.ForbidObservation = true;
        next.Authority.State = CharacterCreationCampaignState.Unavailable;
        next.Authority.Failure = CharacterCreationCampaignFailure.Unavailable;
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success);
        entries = reopened.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        CollectionAssert.AreEqual(new[] { firstReceipt, receipt, third }, entries.Select(row => row.Receipt).ToArray());
        CollectionAssert.AreEqual(oldEntry, JsonSerializer.SerializeToUtf8Bytes(entries[0]));
        CollectionAssert.AreEqual(entryBytes, JsonSerializer.SerializeToUtf8Bytes(entries[1]));
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entries[1].Baseline.SelectedReplay, out _, out var coldWitness));
        CollectionAssert.AreEqual(witnessBytes, JsonSerializer.SerializeToUtf8Bytes(coldWitness));
        CollectionAssert.AreEqual(oldSource, File.ReadAllBytes(Artifact418(context)));
        foreach (var source in oldSources) CollectionAssert.AreEqual(source.Value, File.ReadAllBytes(source.Key));
        foreach (string reference in oldReferences)
            Assert.IsTrue(Manifest418(context)["References"]!.AsArray().Any(row => row!.ToJsonString() == reference));
        Assert.IsTrue(Manifest418(context)["CommittedDraftCampaigns"]!.AsArray().Any(row => row!.ToJsonString() == oldCampaign));
        before = File.ReadAllBytes(Record418(context));
        Assert.AreEqual(receipt, Success425(Rebase774(context, cold, owners, factory)
            .ConfirmRebase(owners.Capture(), preview, key, true)));
        Assert.AreEqual(receipt, Success425(DraftService550(context, cold, owners).LookupDraft(owners.Capture(), preview.ReceiptLookup(key))));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Attributes774_later_ordinary_skills_receipt_retains_exact_attribute_origin_and_current_projection()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var legacy = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver);
        var state = Success425(legacy.Load(owners.Capture(), new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent + (row.AttributeId == "BOD" ? 1 : 0), row.KarmaLevels)).ToArray();
        var ordinaryPreview = Success425(legacy.Preview(owners.Capture(), new(state.Binding, allocations)));
        Assert.IsTrue(ordinaryPreview.CanConfirm);
        Success425(legacy.Confirm(owners.Capture(), new(state.Binding, allocations, ordinaryPreview.PreviewDigest, true)));
        var skills = new CharacterCreationSkillsService(store, context.Resolver);
        var loaded = Success425(skills.Load(new(context.WorkspaceId)));
        var current = loaded.CompositeInputs!.CompositeBaseline!.Selections;
        var point = loaded.Authority.ActiveSkills.First(row => !row.IsExotic
            && current.SkillAllocations.All(selected => selected.SourceSkillId != row.SourceSkillId)
            && (current.SkillKarmaPurchases ?? []).All(paid => paid.SourceSkillId != row.SourceSkillId)
            && CharacterCreationSkillsAccessRules.IsSkillAvailable(loaded.Authority, row.SourceSkillId));
        var selected = loaded.CompositeInputs.Allocations.Append(
            new CharacterCreationSkillAllocation(point.SourceSkillId, point.Kind, 1, null, false)).ToArray();
        var skillPreview = Success425(skills.Preview(new(loaded.Binding, selected, loaded.CompositeInputs.GroupAllocations)));
        Assert.IsTrue(skillPreview.CanConfirm);
        var skillCommand = new CharacterCreationSkillsConfirmRequest(loaded.Binding, selected, loaded.CompositeInputs.GroupAllocations,
            skillPreview.PreviewDigest, "attributes774-ordinary-skills", true);
        var skillReceipt = Success425(skills.Confirm(skillCommand));
        var prior = store.Get(context.WorkspaceId).Value!;
        var priorState = prior.Document.AuxiliaryState;
        Assert.IsNotNull(priorState.CharacterCreationSkillsDraft);
        Assert.IsNotNull(priorState.CharacterCreationAttributesDraft);
        Assert.AreEqual(priorState.CharacterCreationAttributesDraft!.DraftDigest, priorState.CharacterCreationSkillsDraft!.AttributesDraftDigest);

        state = Success425(legacy.Load(owners.Capture(), new(context.WorkspaceId)));
        allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        Assert.IsNull(legacy.Preview(owners.Capture(), new(state.Binding, allocations)).Value);
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, allocations)));
        Assert.IsTrue(preview.Review.Review.Request.Selections.SkillAllocations.Any(row => row.SourceSkillId == point.SourceSkillId && row.Rating == 1));
        var receipt = Success425(service.ConfirmRebase(owners.Capture(), preview, "attributes774-after-skills", true));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var after = cold.Get(context.WorkspaceId).Value!;
        var origin = after.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1].Baseline.OriginalState;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            priorState with { CharacterCreationCompletionDrafts = null }, origin));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(priorState.CharacterCreationSkillsReceipts,
            after.Document.AuxiliaryState.CharacterCreationSkillsReceipts));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(priorState.CharacterCreationSkillsDraft,
            after.Document.AuxiliaryState.CharacterCreationSkillsDraft));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(priorState.CharacterCreationAttributesDraft,
            after.Document.AuxiliaryState.CharacterCreationAttributesDraft));
        var effective = Success425(new CharacterCreationSkillsService(cold, context.Resolver).Load(new(context.WorkspaceId)));
        Assert.IsTrue(effective.Skills.Any(row => row.SourceSkillId == point.SourceSkillId && row.Rating >= 1));
        var effectiveAttributes = Success425(new OwnerBoundCharacterCreationAttributesService(cold, owners, context.Resolver)
            .Load(owners.Capture(), new(context.WorkspaceId)));
        AssertRows774(allocations, effectiveAttributes.Attributes.Select(row => new CharacterCreationAttributeAllocation(
            row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray());
        Assert.AreEqual(preview.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used, effectiveAttributes.CreationKarmaBudget.Used);
        var next = ReviewPricing747(context, cold, owners);
        Assert.AreEqual(preview.Review.Review.Balances, next.Command.Review.Review.Balances);
        byte[] beforeRetry = File.ReadAllBytes(Record418(context));
        Assert.AreEqual(skillReceipt, Success425(new CharacterCreationSkillsService(cold, context.Resolver).Confirm(skillCommand)));
        Assert.AreEqual(receipt, Success425(Rebase774(context, cold, owners, factory)
            .ConfirmRebase(owners.Capture(), preview, "attributes774-after-skills", true)));
        CollectionAssert.AreEqual(beforeRetry, File.ReadAllBytes(Record418(context)));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    [DataRow("campaign", false)]
    [DataRow("campaign", true)]
    [DataRow("source", false)]
    [DataRow("source", true)]
    [DataRow("owner", false)]
    [DataRow("owner", true)]
    [DataRow("policy", false)]
    [DataRow("policy", true)]
    [DataRow("workspace", false)]
    public void Attributes774_stale_authority_does_not_write(string drift, bool afterFlush)
    {
        if (drift == "owner" && afterFlush)
        {
            Attributes774_commit_lease_serializes_owner_transition();
            return;
        }
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var stamp = owners.Capture();
        var state = Success425(service.Load(stamp, new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        var preview = Success425(service.PreviewRebase(stamp, new(state.Binding, allocations)));
        string sourcePath = Path.Combine(context.Directory, "source", "data", drift == "policy"
            ? CharacterCreationBootstrapProfiles.SettingsSourceFile(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId)
            : "qualities.xml");
        byte[] source = File.ReadAllBytes(sourcePath);
        void Drift()
        {
            if (drift == "campaign")
            {
                factory.Current!.State = CharacterCreationCampaignState.Denied;
                factory.Current.Failure = CharacterCreationCampaignFailure.Denied;
            }
            if (drift == "source") File.AppendAllText(sourcePath, "\n<!-- attributes774 drift -->\n");
            if (drift == "policy")
            {
                var document = System.Xml.Linq.XDocument.Load(sourcePath);
                var profile = document.Root!.Element("settings")!.Elements("setting")
                    .Single(row => row.Element("id")!.Value == CharacterCreationBootstrapProfiles.PrioritySettingsProfileId);
                profile.Element("karmacost")!.SetElementValue("karmaattribute", 6);
                document.Save(sourcePath);
            }
            if (drift == "owner") { owners.Set(new OwnerScope("attributes774-other")); owners.Set(stamp.Owner); }
            if (drift == "workspace")
            {
                var legacy = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver);
                var changed = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
                    row.PriorityPointsSpent + (row.AttributeId == "BOD" ? 1 : 0), row.KarmaLevels)).ToArray();
                var p = Success425(legacy.Preview(stamp, new(state.Binding, changed)));
                Success425(legacy.Confirm(stamp, new(state.Binding, changed, p.PreviewDigest, true)));
            }
        }
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, Drift);
        if (afterFlush)
        {
            store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
            service = Rebase774(context, store, owners, factory);
        }
        else Drift();
        byte[] before = File.ReadAllBytes(Record418(context));
        byte[] archive = File.ReadAllBytes(Artifact418(context));
        Assert.IsNull(service.ConfirmRebase(stamp, preview, "attributes774-drift", true).Value);
        if (drift == "owner") Assert.IsNull(service.ConfirmRebase(owners.Capture(), preview, "attributes774-drift", true).Value);
        Assert.AreEqual(afterFlush, fault.Called);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        CollectionAssert.AreEqual(archive, File.ReadAllBytes(Artifact418(context)));
        File.WriteAllBytes(sourcePath, source);
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId).Success);
        Assert.AreEqual(0, owners.ActiveLeases);
    }


    private void Attributes774_commit_lease_serializes_owner_transition()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var stamp = owners.Capture();
        var state = Success425(service.Load(stamp, new(context.WorkspaceId)));
        var allocations = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        var preview = Success425(service.PreviewRebase(stamp, new(state.Binding, allocations)));
        using var attempted = new ManualResetEventSlim();
        using var transitioned = new ManualResetEventSlim();
        Task? transition = null;
        var fault = new CampaignFault518(FileWorkspaceStoreFaultStage.AfterTempFileFlushed, () =>
        {
            Assert.AreEqual(1, owners.ActiveLeases);
            // Switching is serialized by the lease: another actor can request ABA,
            // but cannot perform it until the current commit has released its lease.
            transition = Task.Run(() =>
            {
                attempted.Set();
                owners.Set(new OwnerScope("attributes774-other"));
                owners.Set(stamp.Owner);
                transitioned.Set();
            });
            Assert.IsTrue(attempted.Wait(TimeSpan.FromSeconds(5)), "Owner actor must request its transition.");
            Assert.IsFalse(transitioned.IsSet, "Owner cannot change inside the commit lease.");
            Assert.AreEqual(stamp, owners.Capture());
        });
        store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        service = Rebase774(context, store, owners, factory);
        CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> result;
        try { result = service.ConfirmRebase(stamp, preview, "attributes774-owner-serialized", true); }
        finally
        {
            if (transition is not null)
                Assert.IsTrue(transition.Wait(TimeSpan.FromSeconds(10)), "Switch must finish after lease release.");
        }
        var receipt = Success425(result);
        Assert.IsTrue(fault.Called);
        Assert.IsTrue(transitioned.IsSet);
        var fresh = owners.Capture();
        Assert.AreEqual(stamp.Owner, fresh.Owner);
        Assert.AreEqual(stamp.TransitionRevision + 2, fresh.TransitionRevision);
        Assert.AreEqual(0, owners.ActiveLeases);
        byte[] committed = File.ReadAllBytes(Record418(context));
        byte[] archive = File.ReadAllBytes(Artifact418(context));
        Assert.IsNull(service.ConfirmRebase(stamp, preview, "attributes774-after-owner-change", true).Value);
        Assert.IsNull(service.ConfirmRebase(fresh, preview, "attributes774-after-owner-change", true).Value);
        // Exact earlier commit readback is not authorization for another edit.
        Assert.AreEqual(receipt, Success425(DraftService550(context, store, owners)
            .LookupDraft(fresh, preview.ReceiptLookup("attributes774-owner-serialized"))));
        CollectionAssert.AreEqual(committed, File.ReadAllBytes(Record418(context)));
        CollectionAssert.AreEqual(archive, File.ReadAllBytes(Artifact418(context)));
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Attributes774_budget_and_contextless_refuse_but_reduction_succeeds()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var requested = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        byte[] before = File.ReadAllBytes(Record418(context));
        var legacy = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver);
        Assert.IsNull(legacy.PreviewRebase(owners.Capture(), new(state.Binding, requested)).Value);
        var old = legacy.Preview(owners.Capture(), new(state.Binding, requested));
        Assert.IsTrue(old.Value is null || !old.Value.CanConfirm);
        // Individually legal low increases across ordinary attributes exceed the
        // shared remaining Karma; this is not an out-of-range rank masquerading as budget coverage.
        var costly = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (new[] { "BOD", "AGI", "REA", "STR", "CHA", "INT", "LOG", "WIL" }
                .Contains(row.AttributeId) ? 1 : 0))).ToArray();
        var overBudget = service.PreviewRebase(owners.Capture(), new(state.Binding, costly));
        Assert.IsNull(overBudget.Value);
        Assert.IsTrue(overBudget.Blockers.Contains(CharacterCreationAttributesBlockers.GlobalKarmaExceeded)
            || overBudget.Blockers.Contains(CharacterCreationFinalizationBlockers.GlobalKarmaExceeded));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, requested)));
        Assert.IsNull(service.ConfirmRebase(owners.Capture(), preview, "attributes774-unconfirmed", false).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Success425(service.ConfirmRebase(owners.Capture(), preview, "attributes774-increase", true));
        state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        before = File.ReadAllBytes(Record418(context));
        var lower = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels - (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        // A new original observation requires a new live campaign adapter.
        service = Rebase774(context, store, owners, new CampaignFactory774(owners));
        var reduction = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, lower)));
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Success425(service.ConfirmRebase(owners.Capture(), reduction, "attributes774-reduction", true));
        AssertRows774(lower, ReviewPricing747(context, store, owners,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes).Command.Review.Review.Request.Selections.AttributeAllocations);
        Assert.AreEqual(0, owners.ActiveLeases);
    }


    [TestMethod]
    [DataRow("reduction")]
    [DataRow("point-exchange")]
    [DataRow("reallocation")]
    [DataRow("retained-zero")]
    public void Attributes774_manual_replacement_preserves_exact_choices_and_replays_cold(string operation)
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        Assert.IsFalse(JsonSerializer.Serialize(first.Command.Review.Review.Request).Contains("AttributesReplacement"));
        Assert.IsFalse(JsonSerializer.Serialize(first.Command.Review.Review.Request.Selections).Contains("PlayerChosenAttributeIds"));
        var service = Rebase774(context, store, owners, new CampaignFactory774(owners));
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        // Seed one paid level without also raising its Priority-priced baseline:
        // doing both exceeded this real fixture's remaining global Karma budget.
        // The existing BOD Priority allocation still supports the exchange cases.
        var seed = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent,
            row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        var seeded = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, seed)
            { PlayerChosenAttributeIds = ["BOD"] }));
        Success425(service.ConfirmRebase(owners.Capture(), seeded, "attributes774-seeded", true));
        service = Rebase774(context, store, owners, new CampaignFactory774(owners));
        state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var target = seed.Select(row => row.AttributeId == "BOD" ? operation switch
        {
            "reduction" => row with { KarmaLevels = row.KarmaLevels - 1 },
            "point-exchange" => row with { PriorityPoints = row.PriorityPoints + 1, KarmaLevels = row.KarmaLevels - 1 },
            "reallocation" => row with { PriorityPoints = row.PriorityPoints - 1 },
            _ => row with { PriorityPoints = 0, KarmaLevels = 0 }
        } : row.AttributeId == "AGI" && operation == "reallocation"
            ? row with { PriorityPoints = row.PriorityPoints + 1 } : row).ToArray();
        var original = store.Get(context.WorkspaceId).Value!;
        byte[] oldHistory = JsonSerializer.SerializeToUtf8Bytes(original.Document.AuxiliaryState.CharacterCreationCompletionDrafts);
        var oldSources = RetainedSources774(context);
        byte[] before = File.ReadAllBytes(Record418(context));
        var request = new CharacterCreationAttributesPreviewRequest(state.Binding, target)
            { PlayerChosenAttributeIds = ["BOD", "AGI"] };
        var preview = Success425(service.PreviewRebase(owners.Capture(), request));
        var repeated = Success425(service.PreviewRebase(owners.Capture(), request with
            { Allocations = target.Reverse().ToArray(), PlayerChosenAttributeIds = ["AGI", "BOD"] }));
        Assert.AreEqual(preview.PreviewDigest, repeated.PreviewDigest);
        var review = preview.Review.Review;
        Assert.IsNotNull(review.Request.AttributesReplacement);
        AssertRows774(target, preview.Allocations);
        AssertPreserved774(seeded.Review.Review.Request.Selections, review.Request.Selections);
        Assert.AreEqual(seeded.Review.Review.Balances.RemainingNuyen, review.Balances.RemainingNuyen);
        Assert.AreEqual((decimal)seeded.Review.Review.Balances.RemainingCreationKarma
            + seeded.Review.Review.Skills!.Attributes.CreationKarmaBudget.Used
            - review.Skills!.Attributes.CreationKarmaBudget.Used, (decimal)review.Balances.RemainingCreationKarma);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        var receipt = Success425(service.ConfirmRebase(owners.Capture(), preview, "attributes774-replaced", true));
        var saved = store.Get(context.WorkspaceId).Value!;
        Assert.AreEqual(original.Document.Content, saved.Document.Content);
        Assert.IsNull(saved.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        var entries = saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        CollectionAssert.AreEqual(oldHistory, JsonSerializer.SerializeToUtf8Bytes(entries.Take(entries.Count - 1).ToArray()));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            original.Document.AuxiliaryState with { CharacterCreationCompletionDrafts = null }, entries[^1].Baseline.OriginalState));
        var current = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        AssertRows774(target, current.Attributes.Select(row => new CharacterCreationAttributeAllocation(
            row.AttributeId, row.PriorityPointsSpent, row.KarmaLevels)).ToArray());
        CollectionAssert.AreEqual(new[] { "AGI", "BOD" }, current.PendingDraft!.PlayerChosenAttributeIds!.ToArray());
        // This multi-edit history exceeds the helper's small 4 MiB fixture budget.
        // Use the existing bounded private-carrier contract; no production limit changes.
        var next = ReviewPricing747(context, store, owners, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        Assert.IsNull(next.Command.Review.Review.Request.AttributesReplacement);
        AssertRows774(target, next.Command.Review.Review.Request.Selections.AttributeAllocations);
        Assert.AreEqual(review.Balances, next.Command.Review.Review.Balances);
        CollectionAssert.AreEqual(new[] { "AGI", "BOD" }, next.Command.Review.Review.Request.Selections.PlayerChosenAttributeIds!.ToArray());
        // The fresh Finish review is separately admitted and can produce another Draft.
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), next.Command,
            CharacterCreationCampaignExecutionContext.Capture(next.Authority, CharacterCreationCampaignPhase.Apply)));
        var careerInputs = ReviewPricing747(context, store, owners, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes);
        var finish = DraftService550(context, store, owners);
        var cash = Success425(finish.LoadStartingCashWithCampaign(owners.Capture(), context.WorkspaceId,
            careerInputs.Command.Review.Review.Request,
            CharacterCreationCampaignExecutionContext.Capture(careerInputs.Authority, CharacterCreationCampaignPhase.Review)));
        var career = Success425(finish.ReviewWithCampaign(owners.Capture(), context.WorkspaceId,
            careerInputs.Command.Review.Review.Request, new(cash.AuthorityDigest, cash.Dice * 3),
            CharacterCreationCampaignExecutionContext.Capture(careerInputs.Authority, CharacterCreationCampaignPhase.Review)));
        Success425(finish.ApplyWithCampaign(owners.Capture(), Command(career),
            CharacterCreationCampaignExecutionContext.Capture(careerInputs.Authority, CharacterCreationCampaignPhase.Apply)));
        var final = store.Get(context.WorkspaceId).Value!;
        Assert.IsNotNull(final.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        var xml = System.Xml.Linq.XElement.Parse(final.Document.Content);
        foreach (var row in target)
        {
            var node = xml.Element("attributes")!.Elements("attribute").Single(value => value.Element("name")!.Value == row.AttributeId);
            Assert.AreEqual(row.PriorityPoints, (int)node.Element("base")!);
            Assert.AreEqual(row.KarmaLevels, (int)node.Element("karma")!);
            Assert.AreEqual(review.Skills!.Attributes.Attributes.Single(value => value.AttributeId == row.AttributeId).Current,
                (int)node.Element("totalvalue")!);
        }
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsTrue(cold.Get(context.WorkspaceId).Success);
        before = File.ReadAllBytes(Record418(context));
        var noLive = Rebase774(context, cold, owners, new CampaignFactory774(owners) { ForbidObservation = true });
        Assert.AreEqual(receipt, Success425(noLive.ConfirmRebase(owners.Capture(), preview, "attributes774-replaced", true)));
        Assert.IsNull(noLive.ConfirmRebase(owners.Capture(), preview, "attributes774-new-command", true).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        foreach (var source in oldSources) CollectionAssert.AreEqual(source.Value, File.ReadAllBytes(source.Key));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Attributes774_unknown_duplicate_targets_and_bare_serialized_intent_do_not_authorize_replacement()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var factory = new CampaignFactory774(owners);
        var service = Rebase774(context, store, owners, factory);
        var state = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var rows = state.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent, row.KarmaLevels + (row.AttributeId == "BOD" ? 1 : 0))).ToArray();
        byte[] before = File.ReadAllBytes(Record418(context));
        var invalid = new[] { new[] { new CharacterCreationAttributeAllocation("UNKNOWN", 0, 0) },
            new[] { rows[0], rows[0] }, new[] { rows[0] with { PriorityPoints = -1 } } };
        foreach (var allocations in invalid)
        {
            var rejected = service.PreviewRebase(owners.Capture(), new(state.Binding, allocations));
            Assert.IsNull(rejected.Value);
            CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.RequestInvalid);
        }
        foreach (var chosen in new[] { new[] { "UNKNOWN" }, new[] { "BOD", "BOD" } })
            Assert.IsNull(service.PreviewRebase(owners.Capture(), new(state.Binding, rows) { PlayerChosenAttributeIds = chosen }).Value);
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(state.Binding, rows)));
        var serialized = JsonSerializer.Deserialize<CharacterCreationCompletionDraftReview>(JsonSerializer.Serialize(preview.Review))!;
        var finish = DraftService550(context, store, owners);
        var forged = finish.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId, serialized.Review.Request,
            CharacterCreationCampaignExecutionContext.Capture(factory.Current!, CharacterCreationCampaignPhase.Review));
        Assert.IsNull(forged.Value);
        CollectionAssert.Contains(forged.Blockers.ToArray(), CharacterCreationAttributesReplacementIntent.Invalid);
        // Even an exact serialized command cannot enter Apply without the live in-process workflow.
        Assert.IsNull(finish.ApplyDraftWithCampaign(owners.Capture(),
            new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, serialized, "attributes774-forged", true),
            CharacterCreationCampaignExecutionContext.Capture(factory.Current!, CharacterCreationCampaignPhase.Apply)).Value);
        var request = serialized.Review.Request;
        var changed = request with { AttributesReplacement = request.AttributesReplacement! with { Schema = "unknown" } };
        Assert.IsNull(finish.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId, changed,
            CharacterCreationCampaignExecutionContext.Capture(factory.Current!, CharacterCreationCampaignPhase.Review)).Value);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        // Mutating copies never mutates the opaque in-process preview.
        Assert.AreEqual(CharacterCreationAttributesReplacementIntent.SchemaV1, preview.Review.Review.Request.AttributesReplacement!.Schema);
        var original = store.Get(context.WorkspaceId).Value!;
        Success425(service.ConfirmRebase(owners.Capture(), preview, "attributes774-real", true));
        var entry = store.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts![^1];
        Assert.IsTrue(CharacterCreationCompletionReplayCodec.TryDecode(entry.Baseline.SelectedReplay, out _, out var witness));
        Assert.IsTrue(CharacterCreationAttributesReplacementExecution.TryEnterReplay(original, entry.Baseline.Review,
            witness, out var replayOnly));
        using (replayOnly)
            Assert.IsFalse(CharacterCreationAttributesReplacementExecution.AuthorizesCommit(owners.Capture().Owner, original,
                new(CharacterCreationCompletionDraftApplyRequest.SchemaV1, entry.Baseline.Review, "attributes774-history-is-not-live", true)));
        var recorded = entry.Baseline.Review.Review.Request.AttributesReplacement!;
        foreach (var alteration in new[]
        {
            recorded with { Schema = "unknown" },
            recorded with { Original = recorded.Original with { ContentRevision = recorded.Original.ContentRevision + 1 } },
            recorded with { OwnerStampDigest = Hash518("attributes774-different-owner") },
            recorded with { OriginalSelectionsDigest = Hash518("attributes774-different-origin") },
            recorded with { TargetSelectionsDigest = Hash518("attributes774-different-target") },
            recorded with { Targets = recorded.Targets.Select((row, index) => index == 0
                ? row with { KarmaLevels = row.KarmaLevels + 1 } : row).ToArray() }
        })
        {
            var alteredRequest = entry.Baseline.Review.Review.Request with { AttributesReplacement = alteration };
            var alteredReview = entry.Baseline.Review with { Review = entry.Baseline.Review.Review with { Request = alteredRequest } };
            Assert.IsFalse(CharacterCreationAttributesReplacementExecution.TryEnterReplay(original, alteredReview, witness, out var denied));
            Assert.IsNull(denied);
        }
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Attributes774_lower_logic_with_retained_point_purchases_reports_exact_budget_blocker_without_write()
    {
        using var context = PricingContext747(savedMagic: false);
        var owners = new Owners774();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        var service = Rebase774(context, store, owners, new CampaignFactory774(owners));
        var attributes = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var higher = attributes.Attributes.Select(row => new CharacterCreationAttributeAllocation(row.AttributeId,
            row.PriorityPointsSpent + (row.AttributeId == "LOG" ? 1 : 0), row.KarmaLevels)).ToArray();
        var preview = Success425(service.PreviewRebase(owners.Capture(), new(attributes.Binding, higher)));
        Success425(service.ConfirmRebase(owners.Capture(), preview, "attributes774-knowledge-origin", true));
        var skills = new CharacterCreationSkillsService(store, context.Resolver);
        var state = Success425(skills.Load(new(context.WorkspaceId)));
        var selections = state.CompositeInputs!.CompositeBaseline!.Selections;
        var allocations = selections.SkillAllocations.ToList();
        void Fill(IReadOnlyList<CharacterCreationSkillCatalogEntry> catalog, int remaining, int cap)
        {
            foreach (var row in catalog.OrderBy(row => row.SourceSkillId, StringComparer.Ordinal))
            {
                if (remaining == 0) break;
                if (row.IsExotic || allocations.Any(value => value.SourceSkillId == row.SourceSkillId)
                    || (selections.SkillKarmaPurchases ?? []).Any(value => value.SourceSkillId == row.SourceSkillId)
                    || selections.GroupAllocations.Any(group => group.Rating > 0
                        && state.Authority.SkillGroups.Any(definition => definition.GroupId == group.GroupId
                            && definition.MemberSkillSourceIds.Contains(row.SourceSkillId)))
                    || row.Kind == CharacterCreationSkillKinds.Active
                        && !CharacterCreationSkillsAccessRules.IsSkillAvailable(state.Authority, row.SourceSkillId)) continue;
                int rating = Math.Min(cap, remaining);
                allocations.Add(new(row.SourceSkillId, row.Kind, rating, null, false));
                remaining -= rating;
            }
            Assert.AreEqual(0, remaining, "Fixture must spend the real source-admitted point pool.");
        }
        Fill(state.Authority.ActiveSkills,
            (int)(state.ActiveSkillPointBudget.Total - state.ActiveSkillPointBudget.Used), state.Authority.MaxActiveSkillRatingCreate);
        Fill(state.Authority.KnowledgeSkills,
            (int)(state.KnowledgeSkillPointBudget.Total - state.KnowledgeSkillPointBudget.Used), state.Authority.MaxKnowledgeSkillRatingCreate);
        var paid = Success425(skills.Preview(new(state.Binding, allocations, selections.GroupAllocations)));
        Assert.IsTrue(paid.CanConfirm, string.Join(",", paid.Blockers));
        Success425(skills.Confirm(new(state.Binding, allocations, selections.GroupAllocations,
            paid.PreviewDigest, "attributes774-retain-dependent-skills", true)));
        service = Rebase774(context, store, owners, new CampaignFactory774(owners));
        attributes = Success425(service.Load(owners.Capture(), new(context.WorkspaceId)));
        var lower = higher.Select(row => row.AttributeId == "LOG"
            ? row with { PriorityPoints = row.PriorityPoints - 1 } : row).ToArray();
        byte[] record = File.ReadAllBytes(Record418(context));
        var blocked = service.PreviewRebase(owners.Capture(), new(attributes.Binding, lower));
        Assert.IsNull(blocked.Value);
        Assert.IsTrue(blocked.Blockers.Contains(CharacterCreationSkillsBlockers.KnowledgeBudgetExceeded)
            || blocked.Blockers.Contains(CharacterCreationSkillsBlockers.ActiveBudgetExceeded), string.Join(",", blocked.Blockers));
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        var retained = Success425(skills.Load(new(context.WorkspaceId)));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            allocations, retained.CompositeInputs!.Allocations));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static OwnerBoundCharacterCreationAttributesService Rebase774(Ready774 context, FileWorkspaceStore store,
        Owners774 owners, CampaignFactory774 campaigns)
        => new(store, owners, context.Resolver, new(context.Queries, campaigns));

    private static Dictionary<string, byte[]> RetainedSources774(Ready774 context)
        => Manifest418(context)["References"]!.AsArray().Select(reference => Path.Combine(context.Directory,
                "workspaces", ".retained-rule-sources", context.WorkspaceId.Value,
                reference!["IncarnationId"]!.GetValue<string>(), reference["CaptureDigest"]!.GetValue<string>()[7..] + ".bin"))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertRows774(IReadOnlyList<CharacterCreationAttributeAllocation> expected,
        IReadOnlyList<CharacterCreationAttributeAllocation> actual)
    {
        Assert.AreEqual(13, expected.Count);
        Assert.AreEqual(13, actual.Count);
        Assert.AreEqual(13, expected.Select(row => row.AttributeId).Distinct(StringComparer.Ordinal).Count());
        Assert.AreEqual(13, actual.Select(row => row.AttributeId).Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEqual(expected.OrderBy(row => row.AttributeId, StringComparer.Ordinal).ToArray(),
            actual.OrderBy(row => row.AttributeId, StringComparer.Ordinal).ToArray());
    }

    private static void AssertPreserved774(CharacterCreationCompletionSelections before, CharacterCreationCompletionSelections after)
        => Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before,
            after with { AttributeAllocations = before.AttributeAllocations, ChangeableAttributeIds = before.ChangeableAttributeIds,
                PlayerChosenAttributeIds = before.PlayerChosenAttributeIds }));

    private sealed class CampaignFactory774(Owners774 owners) : ICharacterCreationAttributesRebaseCampaignFactory
    {
        internal CampaignAuthority518? Current;
        internal bool ForbidObservation;
        public ICharacterCreationCampaignAuthority Observe(CharacterCreationAttributesRebaseObservation observation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsFalse(ForbidObservation);
            Assert.AreEqual(1, owners.ActiveLeases);
            Assert.AreEqual(owners.Capture(), observation.Owner);
            var binding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                new(observation.Owner.Owner.Value, observation.WorkspaceId, observation.ContentRevision, observation.SavedRevision,
                    Hash518("attributes774-continuation")[7..], observation.SourceDigest, observation.RawCharacterXmlDigest,
                    observation.AuxiliaryStateDigest, observation.InputsDigest, observation.PolicyDigest, Hash518("attributes774-observation")),
                Hash518("attributes774-transition-" + observation.ContentRevision));
            if (Current is null || Current.Binding != binding)
                Current = new(binding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
            return Current;
        }
    }
}
