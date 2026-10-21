using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Workspaces;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    public void Draft56_one_revision_keeps_exact_pending_budget_choices_and_original_history()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var choices = UnsavedChoices(inputs);
        var before = store.Get(context.WorkspaceId).Value!;
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, reviewed.Value!.Binding.AuxiliaryStateDigest);
        Assert.IsTrue(CharacterCareerReputationTransaction.IsDigest(reviewed.Value.Binding.AuxiliaryStateDigest),
            "The source-reviewed binding must retain the workspace auxiliary digest format.");
        var command = Draft56Command(reviewed.Value, "draft56-exact-save");
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(
            CharacterCreationCompletionDraftTransaction.Freeze(command)),
            "Whole-command capture must retain a confirmable source-reviewed binding.");
        var result = service.ApplyDraft(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        Assert.AreEqual(0, owners.ActiveLeases);
        var cold = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.AreEqual(before.ContentRevision + 1, cold.ContentRevision);
        Assert.AreEqual(cold.ContentRevision, cold.SavedRevision);
        Assert.AreEqual(before.Document.Content, cold.Document.Content);
        Assert.IsFalse(bool.Parse(XDocument.Parse(cold.Document.Content).Root!.Element("created")!.Value));
        Assert.IsNull(cold.Document.AuxiliaryState.CharacterCreationFinalizationArchive);
        Assert.IsNull(cold.Document.AuxiliaryState.CharacterCreationFinalizationReceipts);
        var entry = cold.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single();
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            before.Document.AuxiliaryState, entry.Baseline.OriginalState));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            choices, entry.Baseline.Review.Review.Request.Selections));
        Assert.AreEqual(7, entry.Baseline.Review.Review.Balances.RemainingCreationKarma);
        Assert.AreEqual(53900m, entry.Baseline.Review.Review.Balances.RemainingNuyen);
        Assert.AreEqual(result.Value!.ReceiptDigest, entry.Receipt.ReceiptDigest);
    }

    [TestMethod]
    public void Draft56_same_key_exact_lookup_and_cross_mode_conflict_do_not_mutate()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var inputs = Inputs(context, context.Store, owners);
        var choices = UnsavedChoices(inputs);
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var careerReview = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, careerReview.Outcome);
        var command = Draft56Command(reviewed.Value!, "draft56-mode-key");
        var first = service.ApplyDraft(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, first.Outcome, string.Join(",", first.Blockers));
        var files = Snapshot(context);
        var cold = Service(context, new FileWorkspaceStore(context.Directory), owners);
        Assert.AreEqual(first.Value, cold.ApplyDraft(owners.Capture(), command).Value);
        var lookup = CharacterCreationCompletionDraftTransaction.LookupRequest(command);
        Assert.AreEqual(first.Value, cold.LookupDraft(owners.Capture(), lookup).Value);
        Assert.IsNull(cold.LookupDraft(owners.Capture(), lookup with { CommandDigest = new string('0', 64) }).Value);
        Assert.IsNull(cold.LookupDraft(owners.Capture(), lookup with { Schema = CharacterCreationCompletionDraftApplyRequest.SchemaV1 }).Value);
        Assert.IsNull(cold.ApplyDraft(owners.Capture(), command with { Schema = "career" }).Value);
        var otherMode = cold.Apply(owners.Capture(), Command(careerReview.Value!, command.IdempotencyKey));
        CollectionAssert.Contains(otherMode.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Draft56_native_faults_settle_only_through_exact_cold_lookup(bool afterRename)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe
        { ThrowAt = afterRename ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed };
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var review = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, UnsavedChoices(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome);
        var command = Draft56Command(review.Value!, "draft56-atomic-fault");
        var before = Snapshot(context);
        probe.Armed = true;
        var applied = service.ApplyDraft(owners.Capture(), command);
        Assert.AreEqual(1, probe.Throws, "The admitted command must reach the native fault stage: "
            + string.Join(",", applied.Blockers));
        Assert.AreEqual(afterRename ? 1 : 0, probe.Replacements);
        var cold = Service(context, new FileWorkspaceStore(context.Directory), owners);
        var lookedUp = cold.LookupDraft(owners.Capture(), CharacterCreationCompletionDraftTransaction.LookupRequest(command));
        if (afterRename)
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, lookedUp.Outcome, string.Join(",", lookedUp.Blockers));
            Assert.AreEqual(applied.Value, lookedUp.Value);
        }
        else
        {
            Assert.IsNull(applied.Value);
            Assert.IsNull(lookedUp.Value);
            AssertUnchanged(context, before);
        }
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Draft56_owner_ABA_and_saved_revision_drift_refuse_without_write()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var inputs = Inputs(context, context.Store, owners);
        var stamp = owners.Capture();
        var reviewed = service.ReviewDraft(stamp, context.WorkspaceId, new(inputs.SnapshotDigest, UnsavedChoices(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        var command = Draft56Command(reviewed.Value!, "draft56-owner-drift");
        var before = Snapshot(context);
        owners.Set(new OwnerScope("draft56-other"));
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.IsNull(service.ApplyDraft(stamp, command).Value);
        Assert.IsNull(service.ApplyDraft(owners.Capture(), command).Value);
        AssertUnchanged(context, before);
        var current = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, UnsavedChoices(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, current.Outcome);
        MagicResonanceCompletionTests.SaveAttributes(context, [new("MAG", 1, 0)]);
        var changed = Snapshot(context);
        Assert.IsNull(service.ApplyDraft(owners.Capture(), Draft56Command(current.Value!, "draft56-stale-revision")).Value);
        AssertUnchanged(context, changed);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Draft56_paid_purchases_stay_separate_from_legacy_point_drafts(bool knowledge)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var loaded = new OwnerBoundCharacterCreationCompletionReader(context.Store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId) { IncludeKarmaPurchasePolicy = true });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var inputs = loaded.Value!;
        var option = knowledge ? inputs.Skills.Authority.KnowledgeSkills.First(row => !row.CanBeNativeLanguage && !row.IsExotic)
            : inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var choices = UnsavedChoices(inputs) with
        {
            QualityOptionIds = [], TotalKarmaInvestment = 0,
            AttributeAllocations = [new("MAG", 1, 0), new("BOD", 0, 1)],
            ChangeableAttributeIds = ["MAG", "BOD"],
            SkillKarmaPurchases = [new(option.SourceSkillId, option.Kind, option.SourceNodeDigest, knowledge ? 1 : 3)]
        };
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        Assert.IsTrue(reviewed.Value!.Review.Skills!.Skills.Sum(row => row.KarmaCost) > 0);
        var saved = service.ApplyDraft(owners.Capture(), Draft56Command(reviewed.Value, "draft56-paid-" + knowledge));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, saved.Outcome, string.Join(",", saved.Blockers));
        var cold = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        Assert.IsNull(cold.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        var pending = cold.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Baseline.Review.Review;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(choices.SkillKarmaPurchases,
            pending.Request.Selections.SkillKarmaPurchases));
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            reviewed.Value.Review.Skills, pending.Skills));
        Assert.AreEqual(reviewed.Value.Review.Balances, pending.Balances);
    }

    // Required integration assertion: deliberately not ignored. A durable ledger
    // alone cannot satisfy cold ordinary editing or a fresh Finish operation.
    [TestMethod]
    public void Draft56_cold_ordinary_edit_and_new_finish_must_consume_the_composite_baseline()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var inputs = Inputs(context, context.Store, owners);
        var choices = UnsavedChoices(inputs);
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        var applied = service.ApplyDraft(owners.Capture(), Draft56Command(reviewed.Value!, "draft56-editor-baseline"));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome);
        var cold = new FileWorkspaceStore(context.Directory);
        var attributes = new CharacterCreationAttributesService(cold, context.Resolver);
        var loaded = attributes.Load(new(context.WorkspaceId));
        Assert.IsNotNull(loaded.Value!.PendingDraft, "The cold ordinary editor must expose the actual saved composite attributes.");
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            choices.AttributeAllocations, loaded.Value.PendingDraft.Allocations));
        var editedAllocations = choices.AttributeAllocations.Append(new CharacterCreationAttributeAllocation("BOD", 1, 0)).ToArray();
        var preview = attributes.Preview(new(loaded.Value.Binding, editedAllocations));
        Assert.IsTrue(preview.Value!.CanConfirm, string.Join(",", preview.Blockers));
        var edited = attributes.Confirm(new(loaded.Value.Binding, editedAllocations, preview.Value.PreviewDigest, true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, edited.Outcome, string.Join(",", edited.Blockers));
        var reopened = Inputs(context, new FileWorkspaceStore(context.Directory), owners);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            choices.SkillAllocations, reopened.Skills.Allocations));
        var fresh = Service(context, cold, owners).ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(reopened.SnapshotDigest, choices with { AttributeAllocations = editedAllocations }));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, fresh.Outcome);
        Assert.AreEqual(reviewed.Value!.Review.Balances, fresh.Value!.Review.Balances);
        var careerChoices = fresh.Value.Review.Request.Selections;
        var careerService = Service(context, new FileWorkspaceStore(context.Directory), owners);
        var career = careerService.Review(owners.Capture(), context.WorkspaceId,
            fresh.Value.Review.Request, CashChoice(context, reopened, careerChoices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, career.Outcome, string.Join(",", career.Blockers));
        var command = Command(career.Value!, "draft56-eventual-career");
        var finalized = careerService.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, finalized.Outcome, string.Join(",", finalized.Blockers));
        var finalFiles = Snapshot(context);
        Assert.AreEqual(finalized.Value, careerService.Apply(owners.Capture(), command).Value);
        Assert.AreEqual(applied.Value, careerService.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(Draft56Command(reviewed.Value!, "draft56-editor-baseline"))).Value);
        AssertUnchanged(context, finalFiles);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Draft56_selected_skill_source_drift_before_or_after_flush_cannot_commit(bool afterFlush)
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        string path = Path.Combine(context.Directory, "source", "data", "skills.xml");
        var files = Snapshot(context);
        int sourceChanges = 0;
        void ChangeSource()
        {
            var source = XDocument.Load(path);
            source.Root!.Element("skills")!.Elements("skill").Single(row => row.Element("name")!.Value == "Pistols")
                .SetElementValue("attribute", "REA");
            source.Save(path);
            files[Path.GetRelativePath(context.Directory, path)] = File.ReadAllBytes(path);
            sourceChanges++;
        }
        if (afterFlush) probe.AfterTempFileFlushed = ChangeSource;
        else ChangeSource();
        var applied = service.ApplyDraft(owners.Capture(), Draft56Command(reviewed.Value!, "draft56-source-drift"));
        Assert.AreEqual(1, sourceChanges, "The selected source must actually change at the requested stage: "
            + string.Join(",", applied.Blockers));
        Assert.IsNull(applied.Value);
        Assert.AreEqual(0, probe.Replacements);
        AssertUnchanged(context, files);
    }

    [TestMethod]
    public void Draft67_successive_saves_cold_reads_and_lookups_replay_each_entry_once()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var store = new FileWorkspaceStore(context.Directory);
        var service = Service(context, store, owners);
        var choices = UnsavedChoices(Inputs(context, store, owners));
        CharacterCreationCompletionDraftApplyRequest? firstCommand = null;
        CharacterCreationCompletionDraftReceipt? firstReceipt = null;
        int previousBytes = 0;
        int? firstDepth = null;
        const int saves = 12;
        for (int count = 1; count <= saves; count++)
        {
            var inputs = Inputs(context, store, owners);
            var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
                new(inputs.SnapshotDigest, choices));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome,
                string.Join(",", reviewed.Blockers));
            var command = Draft56Command(reviewed.Value!, "draft67-successive-" + count);
            var applied = Draft67ReplayWork(count, () => service.ApplyDraft(owners.Capture(), command));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome,
                string.Join(",", applied.Blockers));
            firstCommand ??= command;
            firstReceipt ??= applied.Value!;
            var coldStore = new FileWorkspaceStore(context.Directory);
            var coldRead = Draft67ReplayWork(count, () => coldStore.Get(context.WorkspaceId));
            Assert.IsTrue(coldRead.Success);
            var saved = coldRead.Value!;
            var entries = saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!;
            Assert.AreEqual(count, entries.Count);
            Assert.AreEqual(firstReceipt, entries[0].Receipt);
            for (int index = 0; index < entries.Count; index++)
            {
                Assert.IsNull(entries[index].Baseline.OriginalState.CharacterCreationCompletionDrafts);
                var prefix = entries[index].Baseline.OriginalState with
                {
                    CharacterCreationCompletionDrafts = index == 0 ? null : entries.Take(index).ToArray()
                };
                Assert.AreEqual(entries[index].Receipt.PreviousAuxiliaryStateDigest,
                    WorkspaceDocumentAuxiliaryStateDigest.Compute(prefix));
            }
            var coldService = Service(context, coldStore, owners);
            var lookup = Draft67ReplayWork(count, () => coldService.LookupDraft(owners.Capture(),
                CharacterCreationCompletionDraftTransaction.LookupRequest(firstCommand)));
            Assert.AreEqual(firstReceipt, lookup.Value);
            var replay = Draft67ReplayWork(count, () => coldService.ApplyDraft(owners.Capture(), firstCommand));
            Assert.AreEqual(firstReceipt, replay.Value);

            // Actual persisted history through the real codec. This is a measured
            // test caller budget, not a new production transport/save policy.
            var measurement = Draft67MeasureContinuation(context, owners, count);
            Assert.IsTrue(measurement.Bytes > previousBytes);
            firstDepth ??= measurement.Depth;
            Assert.AreEqual(firstDepth.Value, measurement.Depth,
                "Appending a flat ledger must not increase JSON nesting.");
            previousBytes = measurement.Bytes;
        }
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Draft67_scope_callback_cannot_change_the_captured_command_or_its_replay()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var store = new FileWorkspaceStore(context.Directory);
        var inputs = Inputs(context, store, owners);
        var reviewed = Service(context, store, owners).ReviewDraft(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, UnsavedChoices(inputs)));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome);
        var original = reviewed.Value!;
        var allocations = original.Review.Request.Selections.AttributeAllocations.ToList();
        var blockers = original.Review.Blockers.ToList();
        var callerReview = original with
        {
            Review = original.Review with
            {
                Blockers = blockers,
                Request = original.Review.Request with
                {
                    Selections = original.Review.Request.Selections with { AttributeAllocations = allocations }
                }
            }
        };
        var command = Draft56Command(callerReview, "draft67-callback-mutation");
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(command));
        var captured = CharacterCreationCompletionDraftTransaction.Freeze(command);
        string digest = CharacterCreationCompletionDraftRules.CommandDigest(captured);
        int mutations = 0;
        var resolver = new Draft67CallbackResolver(context.Resolver, () =>
        {
            mutations++;
            allocations.Clear();
            blockers.Add("caller-mutated-after-public-capture");
        });
        var service = new OwnerBoundCharacterCreationCompletionService(store, owners, resolver, context.Queries);
        var applied = service.ApplyDraft(owners.Capture(), command);
        Assert.AreEqual(1, mutations);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.IsFalse(CharacterCreationCompletionDraftTransaction.IsConfirmed(command));
        Assert.AreEqual(digest, applied.Value!.CommandDigest);
        var coldStore = new FileWorkspaceStore(context.Directory);
        var saved = coldStore.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(captured.Review,
            saved.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Baseline.Review));
        var beforeReplay = Snapshot(context);
        var coldService = Service(context, coldStore, owners);
        Assert.AreEqual(applied.Value, coldService.LookupDraft(owners.Capture(),
            CharacterCreationCompletionDraftTransaction.LookupRequest(captured)).Value);
        Assert.AreEqual(applied.Value, coldService.ApplyDraft(owners.Capture(), captured).Value);
        AssertUnchanged(context, beforeReplay);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Draft67_operation_memo_requires_actual_entry_and_prefix_bytes_and_replays_forgery()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var choices = UnsavedChoices(Inputs(context, context.Store, owners));
        for (int index = 0; index < 2; index++)
        {
            var inputs = Inputs(context, context.Store, owners);
            var review = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
                new(inputs.SnapshotDigest, choices));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success,
                service.ApplyDraft(owners.Capture(), Draft56Command(review.Value!, "draft67-integrity-" + index)).Outcome);
        }
        var saved = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        var state = saved.Document.AuxiliaryState;
        var entries = state.CharacterCreationCompletionDrafts!;
        using (CharacterCreationCompletionDraftTransaction.BeginValidationOperation())
        {
            Assert.IsTrue(Draft67ReplayWork(2, () => CharacterCreationCompletionDraftTransaction.IsValidLedger(
                saved.Id, saved.ContentRevision, state)));
            Assert.IsTrue(Draft67ReplayWork(0, () => CharacterCreationCompletionDraftTransaction.IsValidLedger(
                saved.Id, saved.ContentRevision, state)));
            Assert.IsFalse(CharacterCreationCompletionDraftTransaction.IsValidLedger(
                saved.Id, saved.ContentRevision - 1, state));

            // All hash envelopes are repaired: the selected replay must still
            // reject invented arithmetic in an already-observed operation.
            var forged = Draft67Resign(entries[1], entries[1].Baseline.Review.Review with
            {
                Balances = entries[1].Baseline.Review.Review.Balances with
                { RemainingNuyen = entries[1].Baseline.Review.Review.Balances.RemainingNuyen + 1m }
            });
            Assert.IsFalse(Draft67ReplayWork(1, () => CharacterCreationCompletionDraftTransaction.IsValidLedger(
                saved.Id, saved.ContentRevision, state with { CharacterCreationCompletionDrafts = [entries[0], forged] })));
            Assert.IsFalse(CharacterCreationCompletionDraftTransaction.IsValidLedger(saved.Id, saved.ContentRevision,
                state with { CharacterCreationCompletionDrafts = [entries[1]] }));
            var nested = entries[0] with
            {
                Baseline = entries[0].Baseline with
                { OriginalState = entries[0].Baseline.OriginalState with { CharacterCreationCompletionDrafts = [entries[0]] } }
            };
            Assert.IsFalse(CharacterCreationCompletionDraftTransaction.IsValidLedger(saved.Id, saved.ContentRevision,
                state with { CharacterCreationCompletionDrafts = [nested, entries[1]] }));
        }
        // The same content is fully replayed again after the operation ends.
        Assert.IsTrue(Draft67ReplayWork(2, () => CharacterCreationCompletionDraftTransaction.IsValidLedger(
            saved.Id, saved.ContentRevision, state)));
    }

    private static T Draft67ReplayWork<T>(int expected, Func<T> action)
    {
        int thread = Environment.CurrentManagedThreadId;
        int replays = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name ==
                CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (Environment.CurrentManagedThreadId != thread || activity.OperationName != "DraftHistoryReplay") return;
                Assert.IsTrue(++replays <= expected,
                    "Historical replay exceeded the deterministic operation bound.");
                Assert.AreEqual(0, activity.TagObjects.Count(), "Historical replay spans must be content-free.");
            }
        };
        ActivitySource.AddActivityListener(listener);
        T result = action();
        Assert.AreEqual(expected, replays, "Every uncached historical entry must execute its semantic replay exactly once.");
        return result;
    }

    private static (int Bytes, int Depth) Draft67MeasureContinuation(ReadyContext context,
        CharacterCreationAttributesServiceTests.AllocationOwners owners, int count)
    {
        var exported = new WorkspaceContinuationExportService(new FileWorkspaceStore(context.Directory), owners)
            .Export(owners.Capture(), context.WorkspaceId);
        Assert.IsTrue(exported.Success);
        var export = exported.Value!;
        // Like the production codec envelope, these properties must be init-only:
        // an anonymous getter-only wrapper becomes {} under these state options.
        byte[] measured = JsonSerializer.SerializeToUtf8Bytes(new Draft67ContinuationEnvelope(
            WorkspaceContinuationSnapshot.ContractName, export.Snapshot, export.SnapshotDigest),
            new JsonSerializerOptions { IgnoreReadOnlyProperties = true, MaxDepth = 128 });
        Assert.IsTrue(measured.Length > 2, "The measured envelope must contain the actual continuation state.");
        // Typed DateTimeOffset tokens can contain '+'; the codec's JsonElement
        // writer pass escapes it as "\u002B". Measure that pass without retaining
        // another complete envelope. Property sorting does not change byte count.
        int maximumBytes;
        using (var document = JsonDocument.Parse(measured, new JsonDocumentOptions { MaxDepth = 128 }))
        using (var writer = new Utf8JsonWriter(System.IO.Stream.Null, new JsonWriterOptions { MaxDepth = 128 }))
        {
            document.RootElement.WriteTo(writer);
            writer.Flush();
            maximumBytes = checked((int)writer.BytesCommitted);
        }
        Assert.IsTrue(maximumBytes >= measured.Length,
            "The measured budget must cover both codec serialization passes.");
        Console.WriteLine($"continuation.typed.bytes={measured.Length}; continuation.measured.bytes={maximumBytes}");
        byte[] encoded = WorkspaceContinuationCodec.Encode(export, maximumBytes);
        Assert.AreEqual(maximumBytes, encoded.Length,
            "The measured writer byte count must match the canonical codec output.");
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(encoded, encoded.Length, out var decoded));
        Assert.AreEqual(count, decoded!.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Count);
        Assert.AreEqual(export.SnapshotDigest, decoded.SnapshotDigest);
        Assert.ThrowsExactly<JsonException>(() => WorkspaceContinuationCodec.Encode(export, encoded.Length - 1));
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(encoded, encoded.Length - 1, out _));
        var reader = new Utf8JsonReader(encoded, new JsonReaderOptions { MaxDepth = 128 });
        int depth = 0;
        while (reader.Read()) depth = Math.Max(depth, reader.CurrentDepth);
        Assert.IsTrue(depth < 128);
        Console.WriteLine($"draft_history.entries={count}; continuation.bytes={encoded.Length}; json.depth={depth}");
        return (encoded.Length, depth);
    }

    private sealed record Draft67ContinuationEnvelope(
        string ContractName, WorkspaceContinuationSnapshot Snapshot, string SnapshotDigest);

    private static CharacterCreationCompletionDraftEntry Draft67Resign(
        CharacterCreationCompletionDraftEntry entry, CharacterCreationCompletionReview review)
    {
        review = review with { ReviewDigest = CharacterCreationCompletionReviewRules.ComputeReviewDigest(review) };
        var draftReview = entry.Baseline.Review with { Review = review };
        draftReview = draftReview with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(draftReview) };
        var baseline = entry.Baseline with { Review = draftReview };
        baseline = baseline with { BaselineDigest = CharacterCreationCompletionDraftRules.BaselineDigest(baseline) };
        string commandDigest = CharacterCreationCompletionDraftRules.CommandDigest(
            CharacterCreationCompletionDraftApplyRequest.SchemaV1, draftReview, entry.Receipt.IdempotencyKeyDigest, true);
        var receipt = entry.Receipt with
        {
            CommandDigest = commandDigest,
            DraftReviewDigest = draftReview.DraftReviewDigest,
            BaselineDigest = baseline.BaselineDigest,
            ReceiptId = CharacterCreationFinalizationDigest.Compute(new
            {
                Schema = CharacterCreationCompletionDraftReceipt.SchemaV1, entry.Receipt.WorkspaceId,
                Key = entry.Receipt.IdempotencyKeyDigest, Command = commandDigest
            })
        };
        receipt = receipt with { ReceiptDigest = CharacterCreationCompletionDraftRules.ReceiptDigest(receipt) };
        return new(baseline, receipt);
    }

    private sealed class Draft67CallbackResolver(ICharacterSourceDataResolver inner, Action callback)
        : ICharacterSourceDataResolver, ICharacterSourceDataResolverOperationScopeFactory
    {
        private Action? onScope = callback;
        public ICharacterSourceDataContext? TryCreateContext(string xml) => inner.TryCreateContext(xml);
        public ICharacterSourceDataResolverOperationScope CreateOperationScope()
        {
            var action = onScope;
            onScope = null;
            action?.Invoke();
            var scope = (inner as ICharacterSourceDataResolverOperationScopeFactory)?.CreateOperationScope();
            return new Draft67ResolverScope(scope ?? inner, scope);
        }
    }

    private sealed class Draft67ResolverScope(ICharacterSourceDataResolver resolver, IDisposable? scope)
        : ICharacterSourceDataResolverOperationScope
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml) => resolver.TryCreateContext(xml);
        public void Dispose() => scope?.Dispose();
    }

    private static CharacterCreationCompletionDraftApplyRequest Draft56Command(
        CharacterCreationCompletionDraftReview review, string key)
    {
        var command = new CharacterCreationCompletionDraftApplyRequest(
            CharacterCreationCompletionDraftApplyRequest.SchemaV1, review, key, true);
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(command),
            "A source-reviewed draft must pass confirmation before testing writes, replay or authority drift.");
        return command;
    }
}
