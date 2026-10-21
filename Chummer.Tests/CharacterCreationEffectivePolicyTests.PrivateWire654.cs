using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners654 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using ReadyContext654 = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void PrivateWire670_local_character_path_notes_do_not_block_attribute_edits_or_cold_reopen()
    {
        const string note = "/home/runner/private C:\\Users\\runner\\private";
        using var context = ReadyContext654.Create(includeGearReview: false,
            beforeDrafts: root => root.SetElementValue("notes", note));
        var owners = new AllocationOwners654();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var attributes = new OwnerBoundCharacterCreationAttributesService(store, owners, context.Resolver);
        var state = Success425(attributes.Load(owners.Capture(), new(context.WorkspaceId)));
        CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
        var preview = Success425(attributes.Preview(owners.Capture(), new(state.Binding, allocations)));
        Assert.IsTrue(preview.CanConfirm);
        Success425(attributes.Confirm(owners.Capture(), new(state.Binding, allocations, preview.PreviewDigest, true)));
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success);
        Assert.IsTrue(JsonSerializer.Serialize(reopened.Value!.Document).Contains("/home/runner/private", StringComparison.Ordinal));
        var loaded = Success425(new OwnerBoundCharacterCreationAttributesService(cold, owners, context.Resolver)
            .Load(owners.Capture(), new(context.WorkspaceId)));
        Assert.IsNotNull(loaded);
        byte[] record = File.ReadAllBytes(Record418(context));
        bool refused = false;
        try { cold.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024); }
        catch (InvalidOperationException) { refused = true; }
        Assert.IsTrue(refused);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
        Assert.IsTrue(cold.Get(context.WorkspaceId).Success);
    }

    // AUTHORED, UNEXECUTED. Actual Core wire, not a hosted/authenticated adapter test.
    [TestMethod]
    public void PrivateWire654_real_second_Draft_after_edit_and_Career_preserve_exact_history_through_private_wire()
    {
        using var context = ReputationContext414(preserved: false);
        var owners = new AllocationOwners654();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = PrepareDraft550(context, store, owners);
        var firstReceipt = Success425(DraftService550(context, store, owners).ApplyDraftWithCampaign(
            owners.Capture(), first.Command,
            CharacterCreationCampaignExecutionContext.Capture(first.Authority, CharacterCreationCampaignPhase.Apply)));
        string firstEntry = JsonSerializer.Serialize(store.Get(context.WorkspaceId).Value!
            .Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single());
        string firstHistory = Manifest418(context)["CommittedDraftCampaigns"]![0]!.ToJsonString();
        var paid = first.Command.Review.Review.Request.Selections.SkillKarmaPurchases;
        Assert.IsTrue(paid is { Count: > 0 });
        Assert.IsFalse(new WorkspaceContinuationExportService(store, owners).Export(
            owners.Capture(), context.WorkspaceId).Success);

        var beforeEdit = ObservePrivate654(context, store, owners);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var attributes = new OwnerBoundCharacterCreationAttributesService(cold, owners, context.Resolver);
        var state = Success425(attributes.Load(owners.Capture(), new(context.WorkspaceId)));
        CharacterCreationAttributeAllocation[] allocations = [new("BOD", 1, 0)];
        var preview = Success425(attributes.Preview(owners.Capture(), new(state.Binding, allocations)));
        Assert.IsTrue(preview.CanConfirm);
        Success425(attributes.Confirm(owners.Capture(), new(state.Binding, allocations, preview.PreviewDigest, true)));
        var edited = cold.Get(context.WorkspaceId).Value!;
        Assert.IsNull(DraftService550(context, cold, owners).ReviewDraftWithCampaign(owners.Capture(),
            context.WorkspaceId, beforeEdit.Proposal, CharacterCreationCampaignExecutionContext.Capture(
                beforeEdit.Authority, CharacterCreationCampaignPhase.Review)).Value);
        var second = ObservePrivate654(context, cold, owners);
        Assert.AreNotEqual(beforeEdit.Binding.ClosureDigest, second.Binding.ClosureDigest);
        Assert.AreNotEqual(beforeEdit.Proposal.InputsSnapshotDigest, second.Proposal.InputsSnapshotDigest);
        Assert.AreEqual(edited.ContentRevision, second.Authority.Binding!.RetainedIdentity.ContentRevision);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            allocations, second.Proposal.Selections.AttributeAllocations));
        Assert.AreEqual(JsonSerializer.Serialize(paid), JsonSerializer.Serialize(second.Proposal.Selections.SkillKarmaPurchases));
        var service = DraftService550(context, cold, owners);
        second.Authority.State = CharacterCreationCampaignState.Denied;
        second.Authority.Failure = CharacterCreationCampaignFailure.Denied;
        Assert.IsNull(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
            second.Proposal, CharacterCreationCampaignExecutionContext.Capture(
                second.Authority, CharacterCreationCampaignPhase.Review)).Value);
        second.Authority.State = CharacterCreationCampaignState.AdmittedUnchangedPolicy;
        second.Authority.Failure = CharacterCreationCampaignFailure.None;
        var review = Success425(service.ReviewDraftWithCampaign(owners.Capture(), context.WorkspaceId,
            second.Proposal, CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Review)));
        var command = new CharacterCreationCompletionDraftApplyRequest(
            CharacterCreationCompletionDraftApplyRequest.SchemaV1, review, "private654-second", true);
        var secondReceipt = Success425(service.ApplyDraftWithCampaign(owners.Capture(), command,
            CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply)));
        var reopened = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var entries = reopened.Get(context.WorkspaceId).Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(firstEntry, JsonSerializer.Serialize(entries[0]));
        CollectionAssert.AreEqual(new[] { firstReceipt, secondReceipt }, entries.Select(row => row.Receipt).ToArray());
        Assert.IsTrue(Manifest418(context)["CommittedDraftCampaigns"]!.AsArray()
            .Any(row => row!.ToJsonString() == firstHistory));
        var afterSecond = ObservePrivate654(context, reopened, owners);
        Assert.AreEqual(2, afterSecond.Binding.CampaignCount);
        Assert.AreNotEqual(second.Binding.ClosureDigest, afterSecond.Binding.ClosureDigest);

        var career = Prepare518(context, reopened);
        Finish518(context, reopened, career);
        var finalStore = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        Assert.IsTrue(finalStore.Get(context.WorkspaceId).Success);
        Assert.IsNotNull(Manifest418(context)["CommittedCampaign"]);
        Assert.AreEqual(2, Manifest418(context)["CommittedDraftCampaigns"]!.AsArray().Count);
        byte[] finalRecord = File.ReadAllBytes(Record418(context));
        second.Authority.State = CharacterCreationCampaignState.Unavailable;
        second.Authority.Failure = CharacterCreationCampaignFailure.Unavailable;
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var careerExport = finalStore.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, 4 * 1024 * 1024);
        var careerImport = WorkspacePrivateRuleSourceClosure.Import(careerExport.ExportPrivateBytes(),
            careerExport.Binding, 4 * 1024 * 1024, context.Queries);
        Assert.AreEqual(3, careerImport.Binding.CampaignCount);
        CollectionAssert.AreEqual(finalRecord, File.ReadAllBytes(Record418(context)));
        int reads = second.Authority.Reads;
        var unavailable = CharacterCreationCampaignExecutionContext.Capture(second.Authority, CharacterCreationCampaignPhase.Apply);
        var finalService = DraftService550(context, finalStore, owners);
        Assert.AreEqual(secondReceipt, Success425(finalService.ApplyDraftWithCampaign(owners.Capture(), command, unavailable)));
        Assert.AreEqual(firstReceipt, Success425(finalService.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(first.Command))));
        Assert.AreEqual(reads, second.Authority.Reads);
        Assert.IsNull(finalService.ApplyDraftWithCampaign(owners.Capture(),
            command with { IdempotencyKey = "private654-different-command" }, unavailable).Value);
        CollectionAssert.AreEqual(finalRecord, File.ReadAllBytes(Record418(context)));
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    private static (CharacterCreationCompletionReviewRequest Proposal, CampaignAuthority518 Authority,
        WorkspacePrivateRuleSourceClosureBinding Binding) ObservePrivate654(
        ReadyContext654 context, FileWorkspaceStore store, AllocationOwners654 owners,
        int maximumBytes = 4 * 1024 * 1024)
    {
        CharacterCreationCompletionReviewInputs? detached = null;
        CharacterCreationRuleSourceExecutionContext? detachedSources = null;
        var exported = store.ExportPrivateRuleSourceClosure(owners, owners.Capture(), context.WorkspaceId, maximumBytes);
        byte[] wire = exported.ExportPrivateBytes();
        Assert.IsFalse(System.Text.Encoding.UTF8.GetString(wire).Contains(context.Directory, StringComparison.Ordinal));
        var imported = WorkspacePrivateRuleSourceClosure.Import(wire, exported.Binding, maximumBytes, context.Queries);
        var result = imported.EvaluateCompletionInputs(owners, owners.Capture(), context.Resolver,
            context.Queries, LifeModules654(), CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
            (fresh, sources, binding) =>
            {
                Assert.IsTrue(fresh.Success, string.Join(",", fresh.AuthorityBlockers));
                Assert.IsNotNull(sources);
                Assert.AreEqual(1, owners.ActiveLeases); // No manufactured or nested owner lease.
                Assert.AreEqual(WorkspacePrivateRuleSourceClosureBinding.SchemaV1, binding.Schema);
                Assert.IsTrue(binding.ArchiveCount > 0);
                Assert.IsTrue(binding.ArchiveBytes > 0);
                detached = fresh.Inputs!;
                detachedSources = sources;
                Assert.IsTrue(CharacterCreationOriginalCharacterSource.TryFromFull(detached, out _));
                var campaignBinding = new CharacterCreationCampaignBinding(CharacterCreationCampaignBinding.SchemaV1,
                    fresh.Identity!, Hash518("private654-current"));
                var authority = new CampaignAuthority518(campaignBinding, CharacterCreationCampaignState.AdmittedUnchangedPolicy);
                var request = Request(detached, CharacterCreationPolicyRequirement.None) with
                {
                    Selections = detached.CompositeBaseline!.Selections,
                    CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
                    OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
                    RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV2,
                    RuleSourceCaptureDigest = detached.RuleSourceCaptureDigest,
                    CampaignAuthority = campaignBinding
                };
                Success425(CharacterCreationCompletionReviewService.Review(detached, request, ruleSources: sources,
                    campaign: CharacterCreationCampaignExecutionContext.Capture(authority, CharacterCreationCampaignPhase.Preparation)));
                return (Proposal: request, Authority: authority, Binding: binding);
            });
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(detached!, out _));
        var serialized = JsonSerializer.Deserialize<CharacterCreationCompletionReviewInputs>(JsonSerializer.Serialize(detached))!;
        Assert.IsFalse(CharacterCreationOriginalCharacterSource.TryFromFull(serialized, out _));
        Assert.IsNull(CharacterCreationCompletionReviewService.Review(detached!, result.Proposal,
            ruleSources: detachedSources, campaign: CharacterCreationCampaignExecutionContext.Capture(
                result.Authority, CharacterCreationCampaignPhase.Preparation)).Value);
        return result;
    }


    private static Chummer.Application.LifeModules.ILifeModulesCatalogService LifeModules654()
    {
        for (DirectoryInfo? directory = new(AppDomain.CurrentDomain.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(candidate)) return new Chummer.Infrastructure.Xml.XmlLifeModulesCatalogService(candidate);
        }
        throw new InvalidOperationException("The existing Life Modules test fixture is missing.");
    }
}
