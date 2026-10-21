using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using AllocationOwners425 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void OwnerRecovery425_Successive_cold_draft_reviews_recover_saved_rule_source_history()
    {
        using var context = RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        var proposal = OwnerProposal425(context, store, owners);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        byte[] record = File.ReadAllBytes(Record418(context));

        // No validation scope surrounds either public call. A prior Get or proposal
        // preparation must not be needed to keep the history alive for this handler.
        var first = Success425(service.ReviewDraft(stamp, context.WorkspaceId, proposal));
        Assert.AreEqual(0, owners.ActiveLeases);
        var second = Success425(service.ReviewDraft(stamp, context.WorkspaceId, proposal));

        Assert.AreEqual(saved.Receipt.ContentRevision, first.Binding.ContentRevision);
        Assert.AreEqual(first.DraftReviewDigest, second.DraftReviewDigest);
        Assert.AreEqual(proposal.RuleSourceCaptureDigest, second.Review.Request.RuleSourceCaptureDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(stamp), second.OwnerStampDigest);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void OwnerRecovery425_Cash_review_and_real_finalization_preserve_owner_draft_lookup()
    {
        // This fixture has generated reputation effects and real native purchases. Its
        // ordinary proposal is not the amended skill-cap probe from RuleContext410.
        using var context = ReputationContext414(preserved: false);
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        var proposal = OwnerProposal425(context, store, owners);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);
        byte[] draftRecord = File.ReadAllBytes(Record418(context));

        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stamp, lookup)).ReceiptDigest);
        var cash = Success425(service.LoadStartingCash(stamp, context.WorkspaceId, proposal));
        var review = Success425(service.Review(stamp, context.WorkspaceId, proposal,
            new(cash.AuthorityDigest, cash.Dice * 3)));
        Assert.AreEqual(saved.Receipt.ContentRevision, review.Projection.Binding.ContentRevision);
        CollectionAssert.AreEqual(draftRecord, File.ReadAllBytes(Record418(context)));
        var finished = Success425(service.Apply(stamp, Command(review)));
        Assert.AreEqual(0, owners.ActiveLeases);

        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var finalizedStore = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = finalizedStore.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success);
        Assert.AreEqual(finished.ReceiptDigest,
            reopened.Value!.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(saved.Receipt.ReceiptDigest, reopened.Value.Document.AuxiliaryState
            .CharacterCreationFinalizationArchive!.State.CharacterCreationCompletionDrafts!.Single().Receipt.ReceiptDigest);
        byte[] finalRecord = File.ReadAllBytes(Record418(context));
        byte[] finalSource = File.ReadAllBytes(Artifact418(context));
        string pendingWrite = Record418(context) + ".tmp.lookup-read-only";
        byte[] pendingBytes = [1, 2, 3];
        File.WriteAllBytes(pendingWrite, pendingBytes);
        var finalizedService = new OwnerBoundCharacterCreationCompletionService(
            finalizedStore, owners, context.Resolver, context.Queries);

        // Lookup already opens a draft-validation scope, but its finalized archive
        // also needs retained source recovery; a draft-prefix memo alone is insufficient.
        Assert.AreEqual(saved.Receipt, Success425(finalizedService.LookupDraft(stamp, lookup)));
        var differentCommand = finalizedService.LookupDraft(stamp, lookup with
        {
            CommandDigest = CharacterCreationFinalizationDigest.Compute("different-completion-draft-command")
        });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, differentCommand.Outcome);
        Assert.IsNull(differentCommand.Value);
        CollectionAssert.Contains(differentCommand.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(finalRecord, File.ReadAllBytes(Record418(context)));
        CollectionAssert.AreEqual(finalSource, File.ReadAllBytes(Artifact418(context)));
        CollectionAssert.AreEqual(pendingBytes, File.ReadAllBytes(pendingWrite));
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void OwnerRecovery425_Next_lookup_rechecks_same_length_tampered_or_missing_sidecar(bool finalized, bool missing)
    {
        using var context = finalized ? ReputationContext414(preserved: false) : RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        if (finalized)
        {
            var proposal = OwnerProposal425(context, store, owners);
            var writer = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
            var admitted = owners.Capture();
            var cash = Success425(writer.LoadStartingCash(admitted, context.WorkspaceId, proposal));
            var review = Success425(writer.Review(admitted, context.WorkspaceId, proposal,
                new(cash.AuthorityDigest, cash.Dice * 3)));
            Success425(writer.Apply(admitted, Command(review)));
        }
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);
        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stamp, lookup)).ReceiptDigest);
        Assert.AreEqual(0, owners.ActiveLeases);
        byte[] record = File.ReadAllBytes(Record418(context));
        string artifact = Artifact418(context);
        byte[] original = File.ReadAllBytes(artifact);
        var timestamp = File.GetLastWriteTimeUtc(artifact);

        if (missing) File.Delete(artifact);
        else
        {
            byte[] changed = (byte[])original.Clone();
            changed[^1] ^= 1;
            File.WriteAllBytes(artifact, changed);
            File.SetLastWriteTimeUtc(artifact, timestamp);
            Assert.AreEqual(original.LongLength, new FileInfo(artifact).Length);
            Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(artifact));
        }

        // Reuse the same public service and store that just succeeded. Neither
        // record-cache hits nor the preceding handler's memo may admit these bytes.
        var rejected = service.LookupDraft(stamp, lookup);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));

        File.WriteAllBytes(artifact, original);
        File.SetLastWriteTimeUtc(artifact, timestamp);
        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stamp, lookup)).ReceiptDigest);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void OwnerRecovery425_Legacy_lookup_fallback_is_not_used_after_supported_lookup_rejection()
    {
        using var context = RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);
        var stamp = owners.Capture();
        byte[] record = File.ReadAllBytes(Record418(context));

        // The old adapter exposes only Get. Its successful pending-history lookup
        // proves that fallback is available, without lending a scope to the service.
        var legacy = new LegacyLookupStore425(store);
        var legacyService = new OwnerBoundCharacterCreationCompletionService(legacy, owners, context.Resolver, context.Queries);
        Assert.AreEqual(saved.Receipt, Success425(legacyService.LookupDraft(stamp, lookup)));
        Assert.AreEqual(1, legacy.Reads);

        var supported = new RejectedLookupStore425(store);
        var service = new OwnerBoundCharacterCreationCompletionService(supported, owners, context.Resolver, context.Queries);
        var rejected = service.LookupDraft(stamp, lookup);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        Assert.AreEqual(1, supported.Lookups);
        Assert.AreEqual(0, supported.Reads);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    [DataRow("OwnerId")]
    [DataRow("WorkspaceId")]
    [DataRow("IncarnationId")]
    public void OwnerRecovery425_Lookup_rechecks_persisted_retained_identity_after_success(string field)
    {
        using var context = RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);
        Assert.AreEqual(saved.Receipt, Success425(service.LookupDraft(stamp, lookup)));
        string path = Record418(context);
        byte[] original = File.ReadAllBytes(path);
        byte[] artifact = File.ReadAllBytes(Artifact418(context));
        var timestamp = File.GetLastWriteTimeUtc(path);
        var record = System.Text.Json.Nodes.JsonNode.Parse(original)!;
        string identity = record["RetainedRuleSources"]![field]!.GetValue<string>();
        record["RetainedRuleSources"]![field] = (identity[0] == 'a' ? "b" : "a") + identity[1..];
        File.WriteAllText(path, record.ToJsonString());
        File.SetLastWriteTimeUtc(path, timestamp);
        byte[] changed = File.ReadAllBytes(path);

        var rejected = service.LookupDraft(stamp, lookup);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        CollectionAssert.Contains(rejected.Blockers.ToArray(), CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(changed, File.ReadAllBytes(path));

        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.AreEqual(saved.Receipt, Success425(service.LookupDraft(stamp, lookup)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        CollectionAssert.AreEqual(artifact, File.ReadAllBytes(Artifact418(context)));
    }

    private class LegacyLookupStore425(IWorkspaceStore inner) : IWorkspaceStore
    {
        public int Reads { get; private set; }
        public WorkspaceStoreReadResult Get(CharacterWorkspaceId id)
        {
            Reads++;
            return inner.Get(id);
        }
        public WorkspaceStoreReadResult Get(OwnerScope owner, CharacterWorkspaceId id)
        {
            Reads++;
            return inner.Get(owner, id);
        }
        public IReadOnlyList<WorkspaceStoreEntry> List() => throw new AssertFailedException("Lookup must only read its workspace.");
        public IReadOnlyList<WorkspaceStoreEntry> List(OwnerScope owner) => throw new AssertFailedException("Lookup must only read its workspace.");
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(WorkspaceDocument document) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult CreateWorkspaceDocument(OwnerScope owner, WorkspaceDocument document) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(CharacterWorkspaceId id, long revision, WorkspaceDocument document) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult ReplaceWorkspaceDocument(OwnerScope owner, CharacterWorkspaceId id, long revision, WorkspaceDocument document) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult SaveCheckpoint(CharacterWorkspaceId id, long revision) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult SaveCheckpoint(OwnerScope owner, CharacterWorkspaceId id, long revision) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult Delete(CharacterWorkspaceId id, long revision) => throw new AssertFailedException("Lookup must not write.");
        public WorkspaceStoreMutationResult Delete(OwnerScope owner, CharacterWorkspaceId id, long revision) => throw new AssertFailedException("Lookup must not write.");
    }

    private sealed class RejectedLookupStore425(IWorkspaceStore inner) : LegacyLookupStore425(inner),
        ICharacterCreationCompletionDraftReceiptLookupCapability
    {
        public int Lookups { get; private set; }
        public CharacterCreationFoundationResult<CharacterCreationCompletionDraftReceipt> LookupCompletionDraftReceipt(
            OwnerScope owner, CharacterCreationCompletionDraftReceiptLookup request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Lookups++;
            return CharacterCreationCompletionDraftTransaction.Blocked(CharacterCreationFinalizationBlockers.WorkspaceUnavailable);
        }
    }

    [TestMethod]
    public void OwnerRecovery425_Source_free_history_lookup_does_not_authorize_a_new_proposal()
    {
        using var context = RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var live = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var initialProposal = OwnerProposal425(context, store, owners);
        var cash = Success425(live.LoadStartingCash(stamp, context.WorkspaceId, initialProposal));
        var saved = SaveOwnerDraft425(context, store, owners);
        // Bind to the committed revision, not the now-stale initial proposal.
        var proposal = OwnerProposal425(context, store, owners);
        byte[] record = File.ReadAllBytes(Record418(context));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);

        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stamp, lookup)).ReceiptDigest);
        var draft = service.ReviewDraft(stamp, context.WorkspaceId, proposal);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, draft.Outcome);
        Assert.IsNull(draft.Value);
        Assert.IsTrue(draft.Blockers.Count > 0);
        var startingCash = service.LoadStartingCash(stamp, context.WorkspaceId, proposal);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, startingCash.Outcome);
        Assert.IsNull(startingCash.Value);
        Assert.IsTrue(startingCash.Blockers.Count > 0);
        var completion = service.Review(stamp, context.WorkspaceId, proposal,
            new(cash.AuthorityDigest, cash.Dice * 3));
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, completion.Outcome);
        Assert.IsNull(completion.Value);
        Assert.IsTrue(completion.Blockers.Count > 0);
        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stamp, lookup)).ReceiptDigest);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void OwnerRecovery425_Stale_owner_transition_cannot_reuse_a_warm_history_or_draft_command()
    {
        using var context = RuleContext410();
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = SaveOwnerDraft425(context, store, owners);
        var proposal = OwnerProposal425(context, store, owners);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var stale = owners.Capture();
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command);
        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(stale, lookup)).ReceiptDigest);
        byte[] record = File.ReadAllBytes(Record418(context));

        // Same owner value, new transition: matching scope or saved receipt identity
        // must not substitute for the exact current owner stamp.
        owners.Set(OwnerScope.LocalSingleUser);
        var current = owners.Capture();
        Assert.AreEqual(stale.Owner, current.Owner);
        Assert.AreEqual(stale.TransitionRevision + 1, current.TransitionRevision);
        var draft = service.ReviewDraft(stale, context.WorkspaceId, proposal);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Conflict, draft.Outcome);
        Assert.IsNull(draft.Value);
        CollectionAssert.Contains(draft.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        var receipt = service.LookupDraft(stale, lookup);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, receipt.Outcome);
        Assert.IsNull(receipt.Value);
        CollectionAssert.Contains(receipt.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);
        var replay = service.ApplyDraft(stale, saved.Command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, replay.Outcome);
        Assert.IsNull(replay.Value);
        CollectionAssert.Contains(replay.Blockers.ToArray(), CharacterCreationCompletionReviewSchemas.SnapshotConflict);

        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(current, lookup)).ReceiptDigest);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void OwnerRecovery425_Native_academic_allocation_is_retained_and_duplicate_proposal_is_rejected()
    {
        using var context = ReadyContext.Create(false, includeNonEmptyPurchases: true,
            includeSkillPurchase: true, prepareSource: _ => { });
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var skills = new CharacterCreationSkillsService(store, context.Resolver);
        var state = Success425(skills.Load(new(context.WorkspaceId)));
        var academic = state.Authority.KnowledgeSkills.First(row => row.Category == "Academic" && row.Specializations.Count > 0);
        Assert.IsFalse(state.PendingDraft!.Allocations.Any(row => row.Kind == academic.Kind && row.SourceSkillId == academic.SourceSkillId));
        CharacterCreationSkillAllocation[] allocations = [.. state.PendingDraft.Allocations,
            new(academic.SourceSkillId, academic.Kind, 2, null, false)];
        var groups = state.PendingDraft.GroupAllocations;
        var preview = Success425(skills.Preview(new(state.Binding, allocations, groups)));
        Success425(skills.Confirm(new(preview.Binding, allocations, groups, preview.PreviewDigest,
            "owner425-native-academic", ExplicitlyConfirmed: true)));

        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
        var inputs = Success425(reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId))).Inputs;
        Assert.IsNull(inputs.CompositeBaseline);
        Assert.AreEqual(allocations[^1], inputs.Skills.Allocations.Single(row => row.Kind == academic.Kind && row.SourceSkillId == academic.SourceSkillId));
        var proposal = OwnerProposal425(context, store, owners);
        CollectionAssert.AreEqual(inputs.Skills.Allocations.ToArray(), proposal.Selections.SkillAllocations.ToArray());
        CollectionAssert.AreEqual(inputs.Skills.GroupAllocations.ToArray(), proposal.Selections.GroupAllocations.ToArray());
        CollectionAssert.AreEqual(inputs.Lifestyles.Qualities.SelectedOptionIds.ToArray(), proposal.Selections.QualityOptionIds.ToArray());
        Assert.AreEqual(inputs.Lifestyles.Gear.Resources.PendingDraft!.KarmaInvestment, proposal.Selections.TotalKarmaInvestment);
        Assert.AreEqual(0, proposal.Selections.SkillKarmaPurchases!.Count);
        Assert.AreEqual(0, proposal.Selections.ChangeableSkillTargets.Count);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        byte[] record = File.ReadAllBytes(Record418(context));
        Success425(service.ReviewDraft(owners.Capture(), context.WorkspaceId, proposal));
        var malformed = proposal with { Selections = proposal.Selections with
        {
            SkillAllocations = [.. proposal.Selections.SkillAllocations,
                proposal.Selections.SkillAllocations.Single(row => row.Kind == academic.Kind && row.SourceSkillId == academic.SourceSkillId)]
        } };
        var rejected = service.ReviewDraft(owners.Capture(), context.WorkspaceId, malformed);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        Assert.IsTrue(rejected.Blockers.Count > 0);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void OwnerRecovery425_Saved_nondefault_purchases_targets_and_budgets_survive_rebinding()
    {
        using var context = ReadyContext.Create(false, includeNonEmptyPurchases: true,
            includeSkillPurchase: true, prepareSource: _ => { });
        var owners = new AllocationOwners425();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var skills = Success425(new CharacterCreationSkillsService(store, context.Resolver).Load(new(context.WorkspaceId)));
        var pistols = skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var proposal = OwnerProposal425(context, store, owners);
        var academic = proposal.Selections.SkillKarmaPurchases!.Single();
        proposal = proposal with { Selections = proposal.Selections with
        {
            SkillKarmaPurchases = [academic with { Rating = 2, SpecializationOptionId = null },
                new(pistols.SourceSkillId, pistols.Kind, pistols.SourceNodeDigest, 3)],
            ChangeableSkillTargets = [new(academic.Kind, academic.SourceSkillId), new(pistols.Kind, pistols.SourceSkillId)],
            ChangeableAttributeIds = ["BOD"],
            KarmaToReserve = 1,
            TotalKarmaInvestment = 3
        } };
        var saved = SaveOwnerDraft425(context, store, owners, proposal);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        byte[] record = File.ReadAllBytes(Record418(context));
        var rebound = OwnerProposal425(context, cold, owners);
        Assert.AreNotEqual(proposal.InputsSnapshotDigest, rebound.InputsSnapshotDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(proposal.Selections),
            CharacterCreationFinalizationDigest.Compute(rebound.Selections));
        var service = new OwnerBoundCharacterCreationCompletionService(cold, owners, context.Resolver, context.Queries);
        var review = Success425(service.ReviewDraft(owners.Capture(), context.WorkspaceId, rebound));
        Assert.AreEqual(saved.Receipt.ContentRevision, review.Binding.ContentRevision);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(proposal.Selections),
            CharacterCreationFinalizationDigest.Compute(review.Review.Request.Selections));
        Assert.AreEqual(saved.Receipt.ReceiptDigest, Success425(service.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(saved.Command))).ReceiptDigest);
        Assert.AreEqual(0, owners.ActiveLeases);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    private static (CharacterCreationCompletionDraftApplyRequest Command, CharacterCreationCompletionDraftReceipt Receipt)
        SaveOwnerDraft425(ReadyContext context, FileWorkspaceStore store, AllocationOwners425 owners,
            CharacterCreationCompletionReviewRequest? proposal = null)
    {
        proposal ??= OwnerProposal425(context, store, owners);
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
        var stamp = owners.Capture();
        var reviewed = Success425(service.ReviewDraft(stamp, context.WorkspaceId, proposal));
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(stamp), reviewed.OwnerStampDigest);
        Assert.AreEqual(CharacterCreationFinalizationDigest.Compute(stamp.Owner), reviewed.OwnerScopeDigest);
        Assert.AreEqual(proposal.RuleSourceCaptureDigest, reviewed.Review.Request.RuleSourceCaptureDigest);
        var command = new CharacterCreationCompletionDraftApplyRequest(
            CharacterCreationCompletionDraftApplyRequest.SchemaV1, reviewed, "owner-recovery425-real-draft", true);
        var receipt = Success425(service.ApplyDraft(stamp, command));
        Assert.AreEqual(0, owners.ActiveLeases);
        return (command, receipt);
    }

    private static CharacterCreationCompletionReviewRequest OwnerProposal425(
        ReadyContext context, FileWorkspaceStore store, AllocationOwners425 owners)
    {
        // Setup only: load a real proposal against the committed history. Dispose
        // before returning so this helper cannot repair any handler under test.
        using var preparation = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        var reader = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries);
        var prepared = Success425(reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId)));
        Assert.AreEqual(0, owners.ActiveLeases);
        var inputs = prepared.Inputs;
        var request = Request(inputs, CharacterCreationPolicyRequirement.None);
        return request with
        {
            // Completion inputs already use the real composite baseline reader,
            // including later ordinary commits. Never seed a saved Finish draft again.
            Selections = inputs.CompositeBaseline?.Selections ?? SeedOwnerSelections425(inputs, request.Selections),
            CompleteAssessmentSchema = CharacterCreationCompleteAssessment.SchemaV4,
            OriginalCharacterExecutionSchema = CharacterCreationCompletionReviewSchemas.OriginalCharacterExecutionV1,
            RuleSourceExecutionSchema = CharacterCreationCompletionReviewSchemas.RuleSourceExecutionV1,
            RuleSourceCaptureDigest = inputs.RuleSourceCaptureDigest
        };
    }

    private static CharacterCreationCompletionSelections SeedOwnerSelections425(
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections choices)
    {
        var academic = inputs.Skills.Authority.KnowledgeSkills.First(row => row.Category == "Academic" && row.Specializations.Count > 0);
        // Only absent native targets may be seeded. An existing allocation keeps
        // its exact rating, specialization and purchase intent (including none).
        if (!choices.SkillAllocations.Any(row => row.Kind == academic.Kind && row.SourceSkillId == academic.SourceSkillId))
        {
            Assert.IsFalse(choices.SkillKarmaPurchases?.Any(row => row.Kind == academic.Kind && row.SourceSkillId == academic.SourceSkillId) ?? false);
            // Seed this fixture's paired modifier only with its new Academic target.
            // Display names are not identities and may describe several options.
            var quality = inputs.Lifestyles.Qualities.Authority.Options.Single(row =>
                row.SourceId == CharacterCreationCompletionPlannerTests.CollegeEducationId && row.Rating == 1);
            if (!choices.QualityOptionIds.Contains(quality.OptionId))
            {
                Assert.IsTrue(quality.IsSelectable);
                choices = choices with { QualityOptionIds = [.. choices.QualityOptionIds, quality.OptionId] };
            }
            choices = choices with
            {
                SkillAllocations = [.. choices.SkillAllocations, new(academic.SourceSkillId, academic.Kind, 1, null, false)],
                SkillKarmaPurchases = [.. (choices.SkillKarmaPurchases ?? []),
                    new(academic.SourceSkillId, academic.Kind, academic.SourceNodeDigest, 3, academic.Specializations[0].OptionId)]
            };
        }
        if (choices.Lifestyles.Count == 0)
        {
            Assert.IsNull(choices.StartingLifestyleId);
            var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.Name == "Low" && row.IsSelectable);
            var home = new CharacterCreationLifestyleConfiguration(Guid.Parse("59396d22-1265-4ddd-bf27-11c7a316b342"),
                option.OptionId, "Manual baseline apartment", CharacterCreationLifestyleStyleIds.Standard,
                option.DefaultIncrementId, 1, 100m, 0, false, false, 0, 0, 0, 0, "Seattle", "", "", []);
            choices = choices with { Lifestyles = [home], StartingLifestyleId = home.LifestyleId };
        }
        return choices;
    }

    private static T Success425<T>(CharacterCreationFoundationResult<T> result) where T : class
    {
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsNotNull(result.Value);
        Assert.AreEqual(0, result.Blockers.Count);
        return result.Value!;
    }
}
