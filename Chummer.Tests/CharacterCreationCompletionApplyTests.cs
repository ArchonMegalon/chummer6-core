using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Hosting;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationCompletionApplyTests
{
    [TestMethod]
    public void Completion_apply_commits_and_cold_replays_a_real_linked_owner_workspace_without_a_local_copy()
    {
        using var fixture = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        string directory = Path.Combine(fixture.Directory, "completion-linked-owner");
        var (initialStore, owners, id) = CreateLinkedPrerequisite(fixture, directory);
        var owner = owners.Current;
        var stamp = owners.Capture();
        Assert.IsFalse(initialStore.Get(id).Success, "Never create a local record to conceal a linked-owner admission failure.");

        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(directory, probe);
        var before = store.Get(owner, id).Value!;
        var inputResult = new OwnerBoundCharacterCreationCompletionReader(store, owners, fixture.Resolver, fixture.Queries)
            .LoadCompletionInputs(stamp, new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, inputResult.Outcome, string.Join(",", inputResult.Blockers));
        var inputs = inputResult.Value!;
        var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var selections = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            SkillAllocations = [.. inputs.Skills.Allocations,
                new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)]
        };
        var source = fixture.Resolver.TryCreateContext(before.Document.Content);
        Assert.IsNotNull(source);
        Assert.IsTrue(source!.TryResolveCreationDefaultStartingNuyen(out var cash));
        Assert.IsNotNull(cash);
        var service = Service(fixture, store, owners);
        var files = Snapshot(fixture);
        var review = service.Review(stamp, id, new(inputs.SnapshotDigest, selections), new(cash!.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        AssertUnchanged(fixture, files);
        var command = Command(review.Value!, "completion-linked-cold-replay");
        var applied = service.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        var cold = new FileWorkspaceStore(directory);
        var reopened = cold.Get(owner, id);
        Assert.IsNotNull(reopened.Value, reopened.Error);
        AssertCommitted(before, reopened.Value!, applied.Value!, review.Value!.Projection);
        Assert.IsFalse(cold.Get(id).Success);
        Assert.IsFalse(cold.Get(new OwnerScope("completion-other-owner"), id).Success);
        var committedFiles = Snapshot(fixture);
        var replay = Service(fixture, cold, owners).Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        AssertUnchanged(fixture, committedFiles);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Completion_apply_linked_account_continuation_roundtrip_preserves_archive_without_producer_replay_authority()
    {
        using var fixture = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var (store, owners, id) = CreateLinkedPrerequisite(fixture,
            Path.Combine(fixture.Directory, "completion-continuation-source"));
        var owner = owners.Current;
        var stamp = owners.Capture();
        var before = store.Get(owner, id).Value!;
        var inputResult = new OwnerBoundCharacterCreationCompletionReader(store, owners, fixture.Resolver, fixture.Queries)
            .LoadCompletionInputs(stamp, new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, inputResult.Outcome, string.Join(",", inputResult.Blockers));
        var inputs = inputResult.Value!;
        var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            SkillAllocations = [.. inputs.Skills.Allocations,
                new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)]
        };
        var source = fixture.Resolver.TryCreateContext(before.Document.Content);
        Assert.IsNotNull(source);
        Assert.IsTrue(source!.TryResolveCreationDefaultStartingNuyen(out var cash));
        Assert.IsNotNull(cash);
        var completion = Service(fixture, store, owners);
        var review = completion.Review(stamp, id, new(inputs.SnapshotDigest, choices), new(cash!.AuthorityDigest, cash.Dice * 3));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "completion-linked-continuation");
        var applied = completion.Apply(stamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        var accepted = store.Get(owner, id).Value!;
        AssertCommitted(before, accepted, applied.Value!, review.Value!.Projection);

        var sourceFiles = Snapshot(fixture);
        var exported = new WorkspaceContinuationExportService(store, owners).Export(stamp, id);
        Assert.IsTrue(exported.Success, exported.Error);
        Assert.IsNotNull(exported.Value);
        Assert.AreEqual(owner.NormalizedValue, exported.Value.Snapshot.OwnerId);
        Assert.AreEqual(accepted.Document.AuxiliaryStateDigest, exported.Value.Snapshot.Workspace.Document.AuxiliaryStateDigest);
        // The shipped Presentation InProcessChummerClient.Continuation and Android
        // AndroidAccountLinkService.Continuations / RunnerSessionCoordinator.Continuations
        // all cap the actual complete continuation at 512 KiB. The 768 KiB signed
        // request envelope is NOT a larger allowance for this inner payload.
        const int transportMaximumBytes = 512 * 1024;
        Console.WriteLine($"Completion continuation transport: limit={transportMaximumBytes}, "
            + $"archiveJsonBytes={JsonSerializer.SerializeToUtf8Bytes(accepted.Document.AuxiliaryState.CharacterCreationFinalizationArchive).LongLength}.");
        byte[] wire = WorkspaceContinuationCodec.Encode(exported.Value, transportMaximumBytes);
        Console.WriteLine($"Completion continuation actual wire: bytes={wire.Length}, limit={transportMaximumBytes}.");
        Assert.IsTrue(wire.Length <= transportMaximumBytes);
        var transferredAuthority = accepted.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.CompletionAuthority!;
        Assert.AreEqual(CharacterCreationCompletionAuthority.SchemaV3, transferredAuthority.Schema);
        Assert.IsNotNull(transferredAuthority.SelectedReplay);
        Assert.IsNull(transferredAuthority.Inputs);
        Assert.IsNull(transferredAuthority.BootstrapSourceSnapshot);
        using (var json = JsonDocument.Parse(wire))
        {
            var authorityJson = json.RootElement.GetProperty("Snapshot").GetProperty("Workspace")
                .GetProperty("Document").GetProperty("State").GetProperty("AuxiliaryState")
                .GetProperty("CharacterCreationFinalizationArchive").GetProperty("CompletionAuthority");
            // Nullable positional parameters still require explicit presence in
            // the strict transport; omitting them breaks an otherwise valid V3.
            Assert.AreEqual(JsonValueKind.Null, authorityJson.GetProperty("Inputs").ValueKind);
            Assert.AreEqual(JsonValueKind.Null, authorityJson.GetProperty("BootstrapSourceSnapshot").ValueKind);
        }
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(wire, transportMaximumBytes, out var decoded),
            "The accepted completion must survive the strict real continuation codec, not only local JSON storage.");
        CollectionAssert.AreEqual(wire, WorkspaceContinuationCodec.Encode(decoded!, transportMaximumBytes));
        Assert.AreEqual(exported.Value.SnapshotDigest, decoded!.SnapshotDigest);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(
            accepted.Document.AuxiliaryState.CharacterCreationFinalizationArchive,
            decoded.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive));
        AssertUnchanged(fixture, sourceFiles);

        // A fresh installation of the same linked account, not a local-owner copy
        // and not a transfer to another account. Owner issuer/stamp is independent.
        var targetOwners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        targetOwners.Set(owner);
        var targetStamp = targetOwners.Capture();
        Assert.AreNotEqual(stamp, targetStamp);
        string targetDirectory = Path.Combine(fixture.Directory, "completion-continuation-target");
        var target = new FileWorkspaceStore(targetDirectory);
        Assert.IsFalse(target.Get(owner, id).Success);
        var restore = new WorkspaceContinuationRestoreService(target, targetOwners,
            fixture.Resolver, fixture.Queries, ContinuationCatalog(), transportMaximumBytes);
        using var restoreReview = restore.Review(targetStamp, wire);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, restoreReview.Result.Outcome,
            JsonSerializer.Serialize(restoreReview.Result));
        Assert.IsFalse(restoreReview.Result.Target!.Exists);
        var restored = restore.Confirm(restoreReview, explicitlyConfirmed: true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, restored.Outcome, JsonSerializer.Serialize(restored));
        Assert.IsNotNull(restored.Receipt);

        var cold = new FileWorkspaceStore(targetDirectory);
        var reopened = cold.Get(owner, id);
        Assert.IsTrue(reopened.Success, reopened.Error);
        Assert.IsNotNull(reopened.Value);
        Assert.AreEqual(accepted.Document.Content, reopened.Value.Document.Content);
        Assert.AreEqual(accepted.Document.AuxiliaryStateDigest, reopened.Value.Document.AuxiliaryStateDigest);
        Assert.AreEqual(accepted.ContentRevision, reopened.Value.ContentRevision);
        Assert.AreEqual(accepted.SavedRevision, reopened.Value.SavedRevision);
        Assert.AreEqual(accepted.ContentRevision, reopened.Value.LocalHistory!.ImportedThroughRevision);
        Assert.AreEqual(exported.Value.SnapshotDigest, reopened.Value.LocalHistory.ImportedSnapshotDigest);
        Assert.AreEqual(restored.Receipt, reopened.Value.LocalHistory.LastRestore);
        Assert.IsFalse(reopened.Value.CanReplayReceipt(applied.Value!.ContentRevision));
        Assert.IsFalse(cold.Get(id).Success);
        Assert.IsFalse(cold.Get(new OwnerScope("completion-other-owner"), id).Success);
        var coldExport = new WorkspaceContinuationExportService(cold, targetOwners).Export(targetStamp, id);
        Assert.IsTrue(coldExport.Success, coldExport.Error);
        CollectionAssert.AreEqual(wire, WorkspaceContinuationCodec.Encode(coldExport.Value!, transportMaximumBytes));

        var restoredFiles = Snapshot(fixture);
        var replay = Service(fixture, cold, targetOwners).Apply(targetStamp, command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, replay.Outcome);
        Assert.IsNull(replay.Value, "A copied historical completion receipt is not producer-operation replay authority.");
        CollectionAssert.Contains(replay.Blockers.ToArray(), CharacterCreationFinalizationBlockers.IdempotencyConflict);
        AssertUnchanged(fixture, restoredFiles);
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.AreEqual(0, targetOwners.ActiveLeases);
    }

    [TestMethod]
    public void Completion_review_does_not_grant_persistence_authority_to_an_existing_read_only_view()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, context.Store, owners);
        var choices = UnsavedChoices(inputs);
        var cash = CashChoice(context, inputs, choices);
        var proposal = new CharacterCreationCompletionReviewRequest(inputs.SnapshotDigest, choices);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        var admitted = Service(context, context.Store, owners).Review(owners.Capture(), context.WorkspaceId, proposal, cash);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, admitted.Outcome, string.Join(",", admitted.Blockers));
        // This existing view holds the unchanged real observation but has no
        // live persistence capability. It must not be upgraded into one.
        var readOnly = new WorkspaceContinuationReadView(OwnerScope.LocalSingleUser, before);
        var rejected = Service(context, readOnly, owners).Review(owners.Capture(), context.WorkspaceId, proposal, cash);
        Assert.AreNotEqual(CharacterCreationFoundationOutcomes.Success, rejected.Outcome);
        Assert.IsNull(rejected.Value);
        AssertUnchanged(context, files);
        Assert.AreEqual(0, owners.ActiveLeases);
    }

    [TestMethod]
    public void Completion_apply_commits_actual_unsaved_choices_in_one_revision_and_cold_replays_cash_once()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var choices = UnsavedChoices(inputs);
        var cash = CashChoice(context, inputs, choices);
        var before = store.Get(context.WorkspaceId).Value!;
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationAttributesDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationSkillsDraft);
        Assert.IsNull(before.Document.AuxiliaryState.CharacterCreationGearDraft);
        var files = Snapshot(context);
        var timing = Stopwatch.StartNew();
        var review = service.Review(owners.Capture(), context.WorkspaceId, new(inputs.SnapshotDigest, choices), cash);
        timing.Stop();
        Console.WriteLine(FormattableString.Invariant($"Completion timing (managed host only): phase=Review elapsedMs={timing.Elapsed.TotalMilliseconds:F3}."));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        AssertUnchanged(context, files);
        Assert.AreEqual(0, probe.Replacements);
        var projection = review.Value!.Projection;
        Assert.AreEqual(7, projection.KarmaBeforeCarryover);
        Assert.AreEqual(53900m, projection.NuyenBeforeCarryover);
        Assert.AreEqual(cash.DiceTotal * projection.StartingCash.Source.Multiplier, projection.StartingNuyen);
        Assert.AreEqual(projection.CarriedNuyen + projection.StartingNuyen, projection.NuyenRemaining);
        var command = Command(review.Value, "completion-unsaved-once");
        timing.Restart();
        var applied = service.Apply(owners.Capture(), command);
        timing.Stop();
        Console.WriteLine(FormattableString.Invariant($"Completion timing (managed host only): phase=Apply elapsedMs={timing.Elapsed.TotalMilliseconds:F3}."));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.IsNotNull(applied.Value);
        Assert.AreEqual(1, probe.Replacements, "Do not save each proposed Creation stage before finalization.");
        var after = store.Get(context.WorkspaceId).Value!;
        AssertCommitted(before, after, applied.Value!, projection);
        AssertArchiveCorruptionRejected(after);
        Assert.AreEqual(0, owners.ActiveLeases);
        Assert.IsNotNull(probe.TargetPath);
        byte[] persisted = File.ReadAllBytes(probe.TargetPath!);
        long fullInputsBytes = JsonSerializer.SerializeToUtf8Bytes(inputs).LongLength;
        long archiveBytes = JsonSerializer.SerializeToUtf8Bytes(after.Document.AuxiliaryState.CharacterCreationFinalizationArchive).LongLength;
        Console.WriteLine($"Completion size: inputs={fullInputsBytes}, archive={archiveBytes}, persisted={persisted.LongLength} bytes; "
            + $"archive/inputs={(decimal)archiveBytes / fullInputsBytes:F3}, record/inputs={(decimal)persisted.LongLength / fullInputsBytes:F3}.");
        var archive = after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!;
        var authority = archive.CompletionAuthority!;
        Console.WriteLine($"Completion subtree bytes: skills={JsonSerializer.SerializeToUtf8Bytes(inputs.Skills).LongLength}, "
            + $"magic={JsonSerializer.SerializeToUtf8Bytes(inputs.Magic).LongLength}, "
            + $"lifestyles={JsonSerializer.SerializeToUtf8Bytes(inputs.Lifestyles).LongLength}, "
            + $"contacts={JsonSerializer.SerializeToUtf8Bytes(inputs.Contacts).LongLength}, "
            + $"bootstrap={JsonSerializer.SerializeToUtf8Bytes(authority.BootstrapSourceSnapshot).LongLength}, "
            + $"originalAuxiliary={JsonSerializer.SerializeToUtf8Bytes(archive.State).LongLength}, "
            + $"completionAuthority={JsonSerializer.SerializeToUtf8Bytes(authority).LongLength}.");

        var cold = new FileWorkspaceStore(context.Directory);
        timing.Restart();
        var reopened = cold.Get(context.WorkspaceId);
        timing.Stop();
        Console.WriteLine(FormattableString.Invariant($"Completion timing (managed host only): phase=ColdGet elapsedMs={timing.Elapsed.TotalMilliseconds:F3}."));
        Assert.IsNotNull(reopened.Value, reopened.Error);
        Assert.AreEqual(after.Document.Content, reopened.Value!.Document.Content);
        Assert.AreEqual(after.Document.AuxiliaryStateDigest, reopened.Value.Document.AuxiliaryStateDigest);
        timing.Restart();
        var replay = Service(context, cold, owners).Apply(owners.Capture(), command);
        timing.Stop();
        Console.WriteLine(FormattableString.Invariant($"Completion timing (managed host only): phase=SameKeyReplay elapsedMs={timing.Elapsed.TotalMilliseconds:F3}."));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
        CollectionAssert.AreEqual(persisted, File.ReadAllBytes(probe.TargetPath!));
        var differentCash = Service(context, cold, owners).Apply(owners.Capture(), command with
        {
            StartingCash = command.StartingCash with { DiceTotal = command.StartingCash.DiceTotal + 1 }
        });
        Assert.IsNull(differentCash.Value, "An idempotency key must not replay a different cash decision.");
        CollectionAssert.AreEqual(persisted, File.ReadAllBytes(probe.TargetPath!));
    }

    [TestMethod]
    public void Completion_apply_known_untouched_attributes_keeps_planner_choices_out_of_player_intent_on_cold_replay()
    {
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext([]);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var store = new FileWorkspaceStore(context.Directory);
        var inputs = Inputs(context, store, owners);
        var originalAttributes = inputs.Skills.Attributes;
        Assert.IsNotNull(originalAttributes.PendingDraft!.PlayerChosenAttributeIds);
        Assert.AreEqual(0, originalAttributes.PendingDraft.PlayerChosenAttributeIds.Count);
        var body = originalAttributes.Attributes.Single(item => item.AttributeId == "BOD");
        var planned = CharacterCreationAttributeCompletionPlanner.Plan(originalAttributes,
            new(originalAttributes.Binding, originalAttributes.SnapshotDigest, ["BOD"])
            {
                Targets = [new("BOD", body.UnaugmentedCurrent + 1)]
            });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, planned.Outcome, string.Join(",", planned.Blockers));
        Assert.IsTrue(planned.Value!.HasChanges);
        Assert.AreEqual(0, planned.Value.Preview.PlayerChosenAttributeIds!.Count);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) with
        {
            AttributeAllocations = planned.Value.Allocations,
            ChangeableAttributeIds = ["BOD"]
        };
        var service = Service(context, store, owners);
        var before = store.Get(context.WorkspaceId).Value!;
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, store.Get(context.WorkspaceId).Value!.Document.AuxiliaryStateDigest);
        var command = Command(review.Value!, "completion-untouched-attributes");
        var applied = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));

        var cold = new FileWorkspaceStore(context.Directory);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success, reopened.Error);
        var after = reopened.Value!;
        AssertCommitted(before, after, applied.Value!, review.Value!.Projection);
        Assert.AreEqual(0, after.Document.AuxiliaryState.CharacterCreationFinalizationArchive!
            .State.CharacterCreationAttributesDraft!.PlayerChosenAttributeIds!.Count);
        var replay = Service(context, cold, owners).Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void Completion_apply_native_atomic_faults_preserve_or_recover_one_exact_commit(
        bool afterRename, bool knownPlayerChoices)
    {
        // Exercise the maximum admitted choice-set width in the same real saved
        // history that is already close to the continuation transport bound.
        string[]? chosenIds = knownPlayerChoices
            ? ["AGI", "BOD", "CHA", "DEP", "EDG", "ESS", "INT", "LOG", "MAG", "REA", "RES", "STR", "WIL"]
            : null;
        using var context = CharacterCreationCompletionReviewServiceTests.CreateSavedContext(chosenIds);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe
        {
            ThrowAt = afterRename ? FileWorkspaceStoreFaultStage.AfterTargetReplaced : FileWorkspaceStoreFaultStage.AfterTempFileFlushed
        };
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(chosenIds,
            inputs.Skills.Attributes.PendingDraft!.PlayerChosenAttributeIds));
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "completion-native-fault");
        var before = store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        probe.Armed = true;
        var first = service.Apply(owners.Capture(), command);
        Assert.AreEqual(1, probe.Throws);
        Assert.AreEqual(0, owners.ActiveLeases);
        if (!afterRename)
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, first.Outcome);
            Assert.IsNull(first.Value);
            Assert.AreEqual(0, probe.Replacements);
            AssertUnchanged(context, files);
        }
        else
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, first.Outcome, string.Join(",", first.Blockers));
            Assert.IsNotNull(first.Value, "A replaced target must recover its exact persisted receipt.");
            Assert.AreEqual(1, probe.Replacements);
        }
        // The injector disarms after one fault. This is an explicit same-command
        // retry, not a generic retry loop or a new completion operation.
        var second = service.Apply(owners.Capture(), command);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, second.Outcome, string.Join(",", second.Blockers));
        Assert.AreEqual(1, probe.Replacements);
        if (afterRename) Assert.AreEqual(first.Value!.ReceiptDigest, second.Value!.ReceiptDigest);
        var after = new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Value!;
        AssertCommitted(before, after, second.Value!, review.Value!.Projection);
        // The exact original receipts/drafts survive as history, not invented
        // intermediate saved stages. Existing ordinary receipt replay stays valid.
        foreach (var replayCheck in context.ReplayChecks) replayCheck(new FileWorkspaceStore(context.Directory));
        var held = File.ReadAllBytes(probe.TargetPath!);
        var third = Service(context, new FileWorkspaceStore(context.Directory), owners).Apply(owners.Capture(), command);
        Assert.AreEqual(second.Value!.ReceiptDigest, third.Value!.ReceiptDigest);
        CollectionAssert.AreEqual(held, File.ReadAllBytes(probe.TargetPath!));
        if (afterRename)
        {
            // Retained ordinary choices/receipts are much larger than a fresh
            // prerequisite. They must fit the real wire too, not just local disk.
            const int maximumBytes = 512 * 1024;
            var exported = new WorkspaceContinuationExportService(new FileWorkspaceStore(context.Directory), owners)
                .Export(owners.Capture(), context.WorkspaceId);
            Assert.IsTrue(exported.Success, exported.Error);
            Assert.IsNotNull(exported.Value);
            byte[] wire = WorkspaceContinuationCodec.Encode(exported.Value, maximumBytes);
            Console.WriteLine($"Completion saved-history actual wire: bytes={wire.Length}, limit={maximumBytes}, knownPlayerChoices={knownPlayerChoices}.");
            Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(wire, maximumBytes, out var decoded));
            CollectionAssert.AreEqual(wire, WorkspaceContinuationCodec.Encode(decoded!, maximumBytes));
            Assert.AreEqual(after.Document.AuxiliaryStateDigest,
                decoded!.Snapshot.Workspace.Document.AuxiliaryStateDigest);
            Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(chosenIds,
                decoded.Snapshot.Workspace.Document.AuxiliaryState.CharacterCreationFinalizationArchive!
                    .State.CharacterCreationAttributesDraft!.PlayerChosenAttributeIds));
            CollectionAssert.AreEqual(held, File.ReadAllBytes(probe.TargetPath!));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_apply_cancellation_respects_the_native_atomic_commit_boundary(bool afterRename)
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var choices = UnsavedChoices(inputs);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "completion-cancel-boundary");
        var before = store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        using var cancellation = new CancellationTokenSource();
        if (afterRename) probe.AfterTargetReplaced = () => cancellation.Cancel();
        else probe.AfterTempFileFlushed = () => cancellation.Cancel();

        if (!afterRename)
        {
            try
            {
                service.Apply(owners.Capture(), command, cancellation.Token);
                Assert.Fail("Cancellation before the target replacement must propagate without committing.");
            }
            catch (OperationCanceledException error)
            {
                Assert.AreEqual(cancellation.Token, error.CancellationToken);
            }
            Assert.IsTrue(cancellation.IsCancellationRequested);
            Assert.AreEqual(0, probe.Replacements);
            Assert.AreEqual(0, owners.ActiveLeases);
            AssertUnchanged(context, files);
            return;
        }

        var applied = service.Apply(owners.Capture(), command, cancellation.Token);
        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, applied.Outcome, string.Join(",", applied.Blockers));
        Assert.IsNotNull(applied.Value, "Cancellation after replacement cannot relabel a durable commit as canceled.");
        Assert.AreEqual(1, probe.Replacements);
        Assert.AreEqual(0, owners.ActiveLeases);
        var cold = new FileWorkspaceStore(context.Directory);
        var after = cold.Get(context.WorkspaceId).Value!;
        AssertCommitted(before, after, applied.Value!, review.Value!.Projection);
        var committedFiles = Snapshot(context);
        var replay = Service(context, cold, owners).Apply(owners.Capture(), command, CancellationToken.None);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(applied.Value!.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.AreEqual(after.ContentRevision, cold.Get(context.WorkspaceId).Value!.ContentRevision);
        Assert.AreEqual(0, owners.ActiveLeases);
        AssertUnchanged(context, committedFiles);
    }

    [TestMethod]
    public void Completion_apply_refuses_stale_observation_missing_confirmation_and_owner_ABA_without_writing()
    {
        using var context = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = Service(context, context.Store, owners);
        var inputs = Inputs(context, context.Store, owners);
        var choices = UnsavedChoices(inputs);
        var stamp = owners.Capture();
        var review = service.Review(stamp, context.WorkspaceId, new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "completion-refusals");
        var files = Snapshot(context);
        foreach (var changed in new[]
        {
            command with { ExplicitlyConfirmed = false },
            command with { ProjectionDigest = new string('0', 64) },
            command with { CompletionReviewDigest = new string('0', 64) },
            command with { Proposal = command.Proposal with { InputsSnapshotDigest = new string('0', 64) } },
            command with { Proposal = command.Proposal with { Selections = command.Proposal.Selections with { TotalKarmaInvestment = 4 } } }
        })
        {
            Assert.IsNull(service.Apply(stamp, changed).Value);
            AssertUnchanged(context, files);
        }
        owners.Set(new OwnerScope("completion-other-owner"));
        Assert.IsNull(service.Apply(stamp, command).Value);
        owners.Set(OwnerScope.LocalSingleUser);
        Assert.AreNotEqual(stamp, owners.Capture());
        Assert.IsNull(service.Apply(stamp, command).Value, "Returning to the same owner name must not admit an old transition stamp.");
        Assert.AreEqual(0, owners.ActiveLeases);
        AssertUnchanged(context, files);
        MagicResonanceCompletionTests.SaveAttributes(context, [new("MAG", 1, 0), new("CHA", 1, 0)]);
        var changedFiles = Snapshot(context);
        var stale = service.Apply(owners.Capture(), command);
        Assert.IsNull(stale.Value);
        AssertUnchanged(context, changedFiles);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_apply_rejects_real_starting_cash_source_drift_after_review(bool changeBeforeRename)
    {
        using var context = ReadyContext.Create(true, amendSettings: _ => { });
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var probe = new AtomicWriteProbe();
        var store = new FileWorkspaceStore(context.Directory, probe);
        var service = Service(context, store, owners);
        var inputs = Inputs(context, store, owners);
        var choices = CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs);
        var review = service.Review(owners.Capture(), context.WorkspaceId,
            new(inputs.SnapshotDigest, choices), CashChoice(context, inputs, choices));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
        var command = Command(review.Value!, "completion-source-drift");
        string path = Path.Combine(context.Directory, "source", "data", "lifestyles.xml");
        var files = Snapshot(context);
        int sourceChanges = 0;
        void ChangeSource()
        {
            var source = XDocument.Load(path);
            source.Root!.Element("lifestyles")!.Elements("lifestyle").Single(row => row.Element("name")!.Value == "Street")
                .SetElementValue("multiplier", "999");
            source.Save(path);
            // Only the test-owned source changes. Do not include the transient
            // candidate file present at the pre-rename hook in the baseline.
            files[Path.GetRelativePath(context.Directory, path)] = File.ReadAllBytes(path);
            sourceChanges++;
        }
        if (changeBeforeRename) probe.AfterTempFileFlushed = ChangeSource;
        else ChangeSource();
        var denied = service.Apply(owners.Capture(), command);
        Assert.IsNull(denied.Value);
        Assert.AreEqual(1, sourceChanges);
        Assert.AreEqual(0, probe.Replacements);
        AssertUnchanged(context, files);
    }

    private static OwnerBoundCharacterCreationCompletionService Service(ReadyContext context, IWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners)
        => new(store, owners, context.Resolver, context.Queries);

    private static (FileWorkspaceStore Store, CharacterCreationAttributesServiceTests.AllocationOwners Owners,
        CharacterWorkspaceId Id) CreateLinkedPrerequisite(ReadyContext fixture, string directory)
    {
        var store = new FileWorkspaceStore(directory);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        owners.Set(new OwnerScope("completion-linked-owner"));
        var stamp = owners.Capture();
        var codec = new Sr5WorkspaceCodec(fixture.Queries,
            new XmlCharacterSectionQueries(new CharacterSectionService(fixture.Resolver)),
            new XmlCharacterMetadataCommands(new CharacterFileService()));
        var bootstrap = new OwnerBoundCharacterCreationBootstrapService(
            new CharacterCreationBootstrapService(store, new RulesetWorkspaceCodecResolver([codec]),
                fixture.Queries, fixture.Resolver), owners);
        Assert.IsTrue(CharacterCreationBootstrapProfiles.TryResolveCanonicalSettingsProfileId(
            CharacterCreationBuildMethods.Priority, out var settings));
        var created = bootstrap.Create(stamp, new(CharacterCreationBootstrapSchemas.RequestV1,
            CharacterCreationBootstrapStages.AwaitingFoundationSelection, RulesetDefaults.Sr5,
            "Completion linked owner", "Linked", CharacterCreationBuildMethods.Priority, settings));
        Assert.AreEqual(CharacterCreationBootstrapOutcomes.Success, created.Outcome, string.Join(",", created.Blockers));
        var id = created.Value!.WorkspaceId;
        var prerequisites = new OwnerBoundCharacterCreationPrerequisiteService(store, owners, fixture.Queries, fixture.Resolver);
        var loaded = prerequisites.Load(stamp, new(id));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var state = loaded.Value!;
        var ranks = CharacterCreationPrerequisiteServiceTests.Assign("A", "E", "B", "C", "D");
        var heritage = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Heritage
            && row.Rank == "A").HeritageOptions.First(row => row.IsEnabled && row.MetatypeName == "Human"
                && row.MetavariantSourceId is null);
        var talent = state.Authority.Options.Single(row => row.CategoryId == CharacterCreationPriorityCategoryIds.Talent
            && row.Rank == "E").TalentOptions.First(row => row.IsEnabled
                && row.Value.Equals("Mundane", StringComparison.OrdinalIgnoreCase));
        var preview = prerequisites.Preview(stamp, new(state.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, preview.Outcome, string.Join(",", preview.Blockers));
        var selected = prerequisites.Confirm(stamp, new(preview.Value!.Binding, ranks, preview.Value.PreviewDigest, true)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId });
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, selected.Outcome, string.Join(",", selected.Blockers));
        Assert.IsFalse(store.Get(id).Success);
        return (store, owners, id);
    }

    private static XmlLifeModulesCatalogService ContinuationCatalog()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
        }
        throw new DirectoryNotFoundException("Canonical Life Modules data is missing.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Completion_authority_size_histogram_uses_actual_inputs_without_an_apply(bool saved)
    {
        using var context = saved
            ? CharacterCreationCompletionReviewServiceTests.CreateSavedContext()
            : ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        if (!saved) MagicResonanceCompletionTests.SavePrerequisite(context, "Mystic Adept", "B");
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var inputs = Inputs(context, context.Store, owners);
        var choices = saved ? CharacterCreationCompletionReviewServiceTests.BaselineSelections(inputs) : UnsavedChoices(inputs);
        var cash = CashChoice(context, inputs, choices);
        var before = context.Store.Get(context.WorkspaceId).Value!;
        var files = Snapshot(context);
        CharacterCreationCompletionAuthority? authority;
        Assert.IsTrue(owners.TryAcquire(owners.Capture(), out var lease));
        using (lease)
        {
            Assert.IsTrue(CharacterCreationCompletionTransaction.TryAdmit(context.Store, owners.Current, before,
                context.Resolver, context.Queries, new(inputs.SnapshotDigest, choices), cash, CancellationToken.None,
                out authority, out _, out var blockers), string.Join(",", blockers));
        }
        Assert.IsNotNull(authority);
        Assert.AreEqual(0, owners.ActiveLeases);
        AssertUnchanged(context, files);
        // Measure exactly the proposed archive's fields; no receipt is created,
        // no Completion is applied and nothing is exported or persisted.
        var archive = new CharacterCreationFinalizationArchive(before.Document.AuxiliaryState)
            { CompletionAuthority = authority };
        if (saved) Assert.IsNotNull(archive.State.CharacterCreationLifestylesDraft);
        else Assert.IsNull(archive.State.CharacterCreationLifestylesDraft);
        MeasureAuthorityHistogram(saved ? "saved-lifestyle" : "unsaved", archive);
        AssertUnchanged(context, files);
    }

    private static void MeasureAuthorityHistogram(string fixture, CharacterCreationFinalizationArchive archive)
    {
        var options = new JsonSerializerOptions { IgnoreReadOnlyProperties = true, MaxDepth = 128 };
        JsonElement root = JsonSerializer.SerializeToElement(archive, options);
        var paths = new[]
        {
            "State", "CompletionAuthority", "CompletionAuthority.BootstrapSourceSnapshot", "CompletionAuthority.Inputs",
            "CompletionAuthority.BootstrapSourceSnapshot.SourceProfile",
            "CompletionAuthority.BootstrapSourceSnapshot.Metatypes",
            "CompletionAuthority.BootstrapSourceSnapshot.Prerequisite",
            "CompletionAuthority.BootstrapSourceSnapshot.Qualities",
            "CompletionAuthority.BootstrapSourceSnapshot.MagicResonance",
            "CompletionAuthority.Inputs.Skills", "CompletionAuthority.Inputs.Skills.Authority",
            "CompletionAuthority.Inputs.Skills.Attributes", "CompletionAuthority.Inputs.Skills.SavedSkillsState",
            "CompletionAuthority.Inputs.Magic", "CompletionAuthority.Inputs.Magic.Authority",
            "CompletionAuthority.Inputs.Magic.Attributes", "CompletionAuthority.Inputs.Magic.SelectedTalent",
            "CompletionAuthority.Inputs.Magic.SavedMagicState",
            "CompletionAuthority.Inputs.Lifestyles", "CompletionAuthority.Inputs.Lifestyles.Authority",
            "CompletionAuthority.Inputs.Lifestyles.Qualities.Authority",
            "CompletionAuthority.Inputs.Lifestyles.Qualities.Attributes",
            "CompletionAuthority.Inputs.Lifestyles.Qualities.SavedQualitiesState",
            "CompletionAuthority.Inputs.Lifestyles.Gear.State.Authority",
            "CompletionAuthority.Inputs.Lifestyles.Gear.Resources.Authority",
            "CompletionAuthority.Inputs.Contacts", "CompletionAuthority.Inputs.Contacts.Qualities.Authority",
            "CompletionAuthority.Inputs.Contacts.Qualities.Attributes",
            "CompletionAuthority.Inputs.Contacts.Qualities.SavedQualitiesState"
        };
        var seenSubtrees = new Dictionary<string, (string Path, int Compressed)>(StringComparer.Ordinal);
        foreach (string path in paths)
        {
            JsonElement value = root;
            bool present = true;
            foreach (string key in path.Split('.'))
            {
                if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))
                { present = false; break; }
            }
            if (!present || value.ValueKind == JsonValueKind.Null) continue;
            byte[] raw = Encoding.UTF8.GetBytes(value.GetRawText());
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(raw));
            // Aggregates already measured in 08: report their raw size only.
            // Compress each distinct concrete authority/history subtree once at
            // one fixed setting, never another quality/window tuning sweep.
            bool aggregate = path is "CompletionAuthority" or "CompletionAuthority.BootstrapSourceSnapshot"
                or "CompletionAuthority.Inputs" or "CompletionAuthority.Inputs.Skills"
                or "CompletionAuthority.Inputs.Magic" or "CompletionAuthority.Inputs.Lifestyles"
                or "CompletionAuthority.Inputs.Contacts";
            string sameAs = "-";
            int compressed = -1;
            if (seenSubtrees.TryGetValue(hash, out var previous))
            { sameAs = previous.Path; compressed = previous.Compressed; }
            else
            {
                if (!aggregate)
                {
                    byte[] destination = new byte[BrotliEncoder.GetMaxCompressedLength(raw.Length)];
                    Assert.IsTrue(BrotliEncoder.TryCompress(raw, destination, out compressed, quality: 9, window: 24));
                }
                seenSubtrees.Add(hash, (path, compressed));
            }
            Console.WriteLine($"Completion authority histogram: fixture={fixture} path={path} rawBytes={raw.Length} brotli9Window24Bytes={compressed} sameAs={sameAs}.");
        }

        var strings = new Dictionary<string, (int Count, long Bytes, Dictionary<string, int> Unique)>(StringComparer.Ordinal);
        Accumulate(root, "");
        foreach (var row in strings.OrderBy(item => item.Key, StringComparer.Ordinal))
            Console.WriteLine($"Completion authority strings: fixture={fixture} category={row.Key} occurrences={row.Value.Count} jsonValueBytes={row.Value.Bytes} uniqueValues={row.Value.Unique.Count} uniqueJsonValueBytes={row.Value.Unique.Values.Sum(value => (long)value)}.");
        Console.WriteLine($"Completion authority histogram total: fixture={fixture} archiveJsonBytes={Encoding.UTF8.GetByteCount(root.GetRawText())}.");

        void Accumulate(JsonElement value, string property)
        {
            if (value.ValueKind == JsonValueKind.Object)
            {
                foreach (var child in value.EnumerateObject()) Accumulate(child.Value, child.Name);
                return;
            }
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in value.EnumerateArray()) Accumulate(child, property);
                return;
            }
            if (value.ValueKind != JsonValueKind.String) return;
            string text = value.GetString()!;
            string category = text.AsSpan().TrimStart().StartsWith("<", StringComparison.Ordinal) ? "xml"
                : property.Contains("Digest", StringComparison.OrdinalIgnoreCase) ? "digest"
                : Guid.TryParse(text, out _) ? "guid" : "other-text";
            byte[] serialized = Encoding.UTF8.GetBytes(value.GetRawText());
            string hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(serialized));
            if (!strings.TryGetValue(category, out var bucket))
                bucket = (0, 0, new Dictionary<string, int>(StringComparer.Ordinal));
            bucket.Unique.TryAdd(hash, serialized.Length);
            strings[category] = (bucket.Count + 1, bucket.Bytes + serialized.Length, bucket.Unique);
        }
    }

    private static CharacterCreationCompletionReviewInputs Inputs(ReadyContext context, IWorkspaceStore store,
        CharacterCreationAttributesServiceTests.AllocationOwners owners)
    {
        var result = new OwnerBoundCharacterCreationCompletionReader(store, owners, context.Resolver, context.Queries)
            .LoadCompletionInputs(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static CharacterCreationCompletionApplyRequest Command(CharacterCreationCompletionApplyReview review, string key)
        => new(review.Projection.Binding, review.Proposal, review.Projection.ReviewDigest,
            review.Projection.StartingCash.Choice, review.Projection.ProjectionDigest, key, true);

    private static CharacterCreationStartingCashChoice CashChoice(ReadyContext context,
        CharacterCreationCompletionReviewInputs inputs, CharacterCreationCompletionSelections choices)
    {
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content);
        Assert.IsNotNull(source);
        CharacterCreationStartingNuyenSource? cash;
        if (choices.StartingLifestyleId is Guid id)
        {
            var home = choices.Lifestyles.Single(row => row.LifestyleId == id);
            var option = inputs.Lifestyles.Authority.LifestyleOptions.Single(row => row.OptionId == home.BaseLifestyleOptionId);
            Assert.IsTrue(source!.TryResolveCreationPriorityStartingNuyen(option.SourceId, out cash));
        }
        else Assert.IsTrue(source!.TryResolveCreationDefaultStartingNuyen(out cash));
        Assert.IsNotNull(cash);
        return new(cash!.AuthorityDigest, cash.Dice * 3);
    }

    private static CharacterCreationCompletionSelections UnsavedChoices(CharacterCreationCompletionReviewInputs inputs)
    {
        var native = inputs.Skills.Authority.KnowledgeSkills.First(row => row.CanBeNativeLanguage);
        var pistols = inputs.Skills.Authority.ActiveSkills.Single(row => row.Name == "Pistols");
        var guts = inputs.Lifestyles.Qualities.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("0d0cb0fc-c79d-418e-9216-8a197369e52d") && row.Rating == 1);
        var supplies = inputs.Lifestyles.Gear.State.Authority.Options.Single(row =>
            row.SourceId == Guid.Parse("76aca45d-0d4f-45a3-a532-c47ac0b26b89") && row.IsSelectable);
        var home = CharacterCreationCompletionReviewServiceTests.LowLifestyle(inputs.Lifestyles.Authority);
        return new([new("MAG", 1, 0)],
            [.. inputs.Skills.Allocations,
                new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true),
                new(pistols.SourceSkillId, CharacterCreationSkillKinds.Active, 2, null, false)],
            inputs.Skills.GroupAllocations,
            MagicResonanceCompletionTests.CompleteSelections(inputs.Magic, inputs.Magic.SelectedTalent.Magic + 1, 1),
            [guts.OptionId], 3, [new(supplies.OptionId, 1)],
            [new(Guid.Parse("0cfb6e5f-9019-40cb-b0f1-420d02c107c3"),
                new CharacterCreationContactIdentity("Street Medic", "Professional", "Seattle", "", "",
                    "Human", "Other", "Adult", "Professional", "Nuyen", "", "", ""), 1, 1)], [home], home.LifestyleId)
        { ChangeableAttributeIds = ["MAG"] };
    }

    private static void AssertCommitted(WorkspaceStoredDocument before, WorkspaceStoredDocument after,
        CharacterCreationFinalizationReceipt receipt, CharacterCreationCompletionFinalizationProjection projection)
    {
        Assert.AreEqual(before.ContentRevision + 1, after.ContentRevision);
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        Assert.AreEqual(before.ContentRevision, receipt.PreviousContentRevision);
        Assert.AreEqual(after.ContentRevision, receipt.ContentRevision);
        Assert.IsTrue(receipt.CharacterCreated);
        Assert.IsTrue(receipt.RequiresFreshCareerReopen);
        Assert.AreEqual(projection.CharacterXml, after.Document.Content);
        var xml = XDocument.Parse(after.Document.Content).Root!;
        Assert.AreEqual("true", xml.Element("created")!.Value.ToLowerInvariant());
        Assert.AreEqual(projection.KarmaRemaining, int.Parse(xml.Element("karma")!.Value, CultureInfo.InvariantCulture));
        Assert.AreEqual(projection.NuyenRemaining, decimal.Parse(xml.Element("nuyen")!.Value, CultureInfo.InvariantCulture));
        var auxiliary = after.Document.AuxiliaryState;
        Assert.AreEqual(1, auxiliary.CharacterCreationFinalizationReceipts!.Count);
        Assert.AreEqual(receipt.ReceiptDigest, auxiliary.CharacterCreationFinalizationReceipts.Single().Receipt.ReceiptDigest);
        Assert.IsNotNull(auxiliary.CharacterCreationFinalizationArchive);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(before.Document.AuxiliaryState,
            auxiliary.CharacterCreationFinalizationArchive!.State));
        Assert.AreEqual(before.Document.AuxiliaryStateDigest,
            WorkspaceDocumentAuxiliaryStateDigest.Compute(auxiliary.CharacterCreationFinalizationArchive.State));
        Assert.IsNull(auxiliary.CharacterCreationAttributesDraft);
        Assert.IsNull(auxiliary.CharacterCreationSkillsDraft);
        Assert.IsNull(auxiliary.CharacterCreationGearDraft);
        Assert.IsNull(auxiliary.CharacterCreationMagicResonanceDraft);
        Assert.IsNull(auxiliary.CharacterCreationResourcesDraft);
    }

    private static void AssertArchiveCorruptionRejected(WorkspaceStoredDocument after)
    {
        var auxiliary = after.Document.AuxiliaryState;
        var archive = auxiliary.CharacterCreationFinalizationArchive!;
        var entry = auxiliary.CharacterCreationFinalizationReceipts!.Single();
        var receipt = entry.Receipt;
        Assert.IsNotNull(archive.CompletionAuthority);
        Assert.AreEqual(archive.CompletionAuthority!.AuthorityDigest, receipt.CompletionAuthorityDigest);
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(
            after.Id, after.ContentRevision, archive, [entry]));
        Assert.IsFalse(WorkspaceAuxiliaryStateIntegrity.IsValidShape(after.Id, after.ContentRevision,
            auxiliary with { CharacterCreationFinalizationArchive = null }));
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(after.Id, after.ContentRevision,
            archive with { CompletionAuthority = null }, [entry]), "A completion marker cannot fall back to historical Priority validation.");

        string wrongMarker = receipt.CompletionAuthorityDigest![..^1]
            + (receipt.CompletionAuthorityDigest.EndsWith("b", StringComparison.Ordinal) ? "c" : "b");
        foreach (string? marker in new string?[] { null, wrongMarker })
        {
            var changed = receipt with { CompletionAuthorityDigest = marker };
            changed = changed with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(changed) };
            var ledger = new[] { entry with { Receipt = changed } };
            Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(after.Id, after.ContentRevision, ledger),
                "The negative must pass outer receipt hashing and reach archive semantics.");
            Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(after.Id, after.ContentRevision, archive, ledger));
        }
        var authority = archive.CompletionAuthority!;
        var different = authority with { Proposal = authority.Proposal with
            { Selections = authority.Proposal.Selections with { TotalKarmaInvestment = authority.Proposal.Selections.TotalKarmaInvestment + 1 } } };
        different = different with { AuthorityDigest = CharacterCreationCompletionTransaction.AuthorityDigest(different) };
        var rehashed = receipt with { CompletionAuthorityDigest = different.AuthorityDigest };
        rehashed = rehashed with { ReceiptDigest = CharacterCreationFinalizationDigest.ComputeReceiptDigest(rehashed) };
        var rehashedLedger = new[] { entry with { Receipt = rehashed } };
        Assert.IsTrue(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidLedger(after.Id, after.ContentRevision, rehashedLedger));
        Assert.IsFalse(CharacterCreationFinalizationReceiptLedgerIntegrity.IsValidArchive(after.Id, after.ContentRevision,
            archive with { CompletionAuthority = different }, rehashedLedger),
            "Rehashing altered accepted choices cannot recreate the original projected receipt.");
    }

    private static Dictionary<string, byte[]> Snapshot(ReadyContext context)
        => Directory.EnumerateFiles(context.Directory, "*", SearchOption.AllDirectories).ToDictionary(
            path => Path.GetRelativePath(context.Directory, path), File.ReadAllBytes, StringComparer.Ordinal);

    private static void AssertUnchanged(ReadyContext context, Dictionary<string, byte[]> before)
    {
        var after = Snapshot(context);
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var entry in before) CollectionAssert.AreEqual(entry.Value, after[entry.Key], entry.Key);
    }

    private sealed class AtomicWriteProbe : IFileWorkspaceStoreFaultInjector
    {
        public FileWorkspaceStoreFaultStage? ThrowAt { get; init; }
        public bool Armed { get; set; }
        public int Throws { get; private set; }
        public int Replacements { get; private set; }
        public string? TargetPath { get; private set; }
        public Action? AfterTempFileFlushed { get; set; }
        public Action? AfterTargetReplaced { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            TargetPath = targetPath;
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                var action = AfterTempFileFlushed;
                AfterTempFileFlushed = null;
                action?.Invoke();
            }
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced)
            {
                Replacements++;
                var action = AfterTargetReplaced;
                AfterTargetReplaced = null;
                action?.Invoke();
            }
            if (!Armed || stage != ThrowAt) return;
            Armed = false;
            Throws++;
            throw new IOException("Focused completion atomic-write fault.");
        }
    }
}
