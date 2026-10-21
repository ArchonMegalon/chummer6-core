using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

public sealed partial class CharacterCreationEffectivePolicyTests
{
    [TestMethod]
    public void Persistence418_real_draft_commit_reopens_without_the_live_source_tree()
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var command = DraftCommand418(context);
        var receipt = CommitDraft418(context, store, command);
        var manifest = Manifest418(context);
        Assert.AreEqual(1, manifest["References"]!.AsArray().Count);
        Assert.AreEqual(receipt.ReceiptDigest, manifest["Uses"]![0]!["ReceiptDigest"]!.GetValue<string>());
        Assert.AreEqual(command.Review.Review.Request.RuleSourceCaptureDigest,
            manifest["Uses"]![0]!["CaptureDigest"]!.GetValue<string>());
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var reopened = cold.Get(context.WorkspaceId);
        Assert.IsTrue(reopened.Success);
        Assert.AreEqual(receipt.ReceiptDigest,
            reopened.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Receipt.ReceiptDigest);
        Assert.IsFalse(JsonSerializer.Serialize(reopened.Value).Contains("RetainedRuleSources", StringComparison.Ordinal));
        Assert.IsFalse(JsonSerializer.Serialize(receipt).Contains("PrivateInputs", StringComparison.Ordinal));
        var replay = cold.CommitCompletionDraft(OwnerScope.LocalSingleUser, command, context.Resolver, context.Queries);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(receipt.ReceiptDigest, replay.Value!.ReceiptDigest);
        Assert.IsFalse(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Success,
            "A legacy constructor has no implicit cold-source capability.");
        Assert.IsFalse(cold.ReadContinuation(context.WorkspaceId).Success,
            "Portable continuation must not drop the private source dependency.");
    }

    [TestMethod]
    public void Persistence418_later_replacement_and_checkpoint_preserve_the_exact_manifest()
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        CommitDraft418(context, store, DraftCommand418(context));
        string manifest = Manifest418(context).ToJsonString();
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var saved = store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(store.ReplaceWorkspaceDocument(context.WorkspaceId, saved.ContentRevision, saved.Document).Success);
        saved = store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(store.SaveCheckpoint(context.WorkspaceId, saved.ContentRevision).Success);
        saved = store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(store.ReplaceWorkspaceDocumentAndCheckpoint(context.WorkspaceId, saved.ContentRevision, saved.Document).Success);
        Assert.AreEqual(manifest, Manifest418(context).ToJsonString());
        Assert.IsTrue(FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries)
            .Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Persistence418_successive_real_drafts_recover_their_original_prefixes()
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var first = CommitDraft418(context, store, DraftCommand418(context));
        var next = DraftCommand418(context, store) with { IdempotencyKey = "persistence418-second-draft" };
        var second = CommitDraft418(context, store, next);
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId);
        Assert.IsTrue(cold.Success);
        CollectionAssert.AreEqual(new[] { first.ReceiptDigest, second.ReceiptDigest },
            cold.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Select(row => row.Receipt.ReceiptDigest).ToArray());
        Assert.AreEqual(2, Manifest418(context)["Uses"]!.AsArray().Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistence418_real_finalization_retains_its_source_and_original_draft_baselines(bool saveDraftFirst)
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        CharacterCreationCompletionDraftReceipt? draft = saveDraftFirst
            ? CommitDraft418(context, store, DraftCommand418(context)) : null;
        CharacterCreationCompletionApplyRequest command;
        // Keep the existing synchronous identity memo alive across the ordinary
        // source review. Every store Get independently restores its sidecars.
        using (CharacterCreationCompletionDraftTransaction.EnterValidationOperation())
        {
            var proposal = DraftCommand418(context, store).Review.Review.Request;
            var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
            var service = new OwnerBoundCharacterCreationCompletionService(store, owners, context.Resolver, context.Queries);
            var cash = service.LoadStartingCash(owners.Capture(), context.WorkspaceId, proposal);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, cash.Outcome, string.Join(",", cash.Blockers));
            var review = service.Review(owners.Capture(), context.WorkspaceId, proposal,
                new(cash.Value!.AuthorityDigest, cash.Value.Dice * 3));
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, review.Outcome, string.Join(",", review.Blockers));
            command = Command(review.Value!);
        }
        var result = store.CommitCompletion(command, context.Resolver, context.Queries);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var saved = cold.Get(context.WorkspaceId);
        Assert.IsTrue(saved.Success);
        Assert.AreEqual(result.Value!.ReceiptDigest,
            saved.Value!.Document.AuxiliaryState.CharacterCreationFinalizationReceipts!.Single().Receipt.ReceiptDigest);
        var archivedDrafts = saved.Value.Document.AuxiliaryState.CharacterCreationFinalizationArchive!.State.CharacterCreationCompletionDrafts;
        if (draft is null) Assert.IsNull(archivedDrafts);
        else Assert.AreEqual(draft.ReceiptDigest, archivedDrafts!.Single().Receipt.ReceiptDigest);
        Assert.AreEqual(saveDraftFirst ? 2 : 1, Manifest418(context)["Uses"]!.AsArray().Count);
        var replay = cold.CommitCompletion(command, context.Resolver, context.Queries);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, replay.Outcome, string.Join(",", replay.Blockers));
        Assert.AreEqual(result.Value.ReceiptDigest, replay.Value!.ReceiptDigest);
    }

    [TestMethod]
    public void Persistence418_final_live_source_fence_remains_after_publication_and_record_flush()
    {
        using var context = RuleContext410();
        var command = DraftCommand418(context);
        byte[] before = File.ReadAllBytes(Record418(context));
        var fault = new RecordAction418(() => Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true));
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var result = store.CommitCompletionDraft(OwnerScope.LocalSingleUser, command, context.Resolver, context.Queries);
        Assert.IsTrue(fault.Called);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    public void Persistence418_capture_callback_is_successful_live_admission_only_and_does_not_persist()
    {
        using var context = RuleContext410();
        var command = DraftCommand418(context);
        byte[] before = File.ReadAllBytes(Record418(context));
        CharacterCreationRuleSourceExecutionContext? captured = null;
        var original = context.Store.Get(context.WorkspaceId).Value!;
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.TryBuild(context.Store, OwnerScope.LocalSingleUser,
            original, context.Resolver, context.Queries, command, default, out _, out _, value => captured = value));
        Assert.IsNotNull(captured);
        captured = null;
        Assert.IsFalse(CharacterCreationCompletionDraftTransaction.TryBuild(context.Store, OwnerScope.LocalSingleUser,
            original, context.Resolver, context.Queries, command with { ExplicitlyConfirmed = false }, default,
            out _, out _, value => captured = value));
        Assert.IsNull(captured);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    public void Persistence418_legacy_draft_commit_and_cold_reopen_keep_schema_four()
    {
        using var context = ReadyContext.Create(false);
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        var service = new OwnerBoundCharacterCreationCompletionService(context.Store, owners, context.Resolver, context.Queries);
        var inputs = Load(context);
        var reviewed = service.ReviewDraft(owners.Capture(), context.WorkspaceId,
            Request(inputs, CharacterCreationPolicyRequirement.None));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, reviewed.Outcome, string.Join(",", reviewed.Blockers));
        var result = service.ApplyDraft(owners.Capture(), new(CharacterCreationCompletionDraftApplyRequest.SchemaV1,
            reviewed.Value!, "persistence418-legacy-draft", true));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        Assert.IsTrue(new FileWorkspaceStore(context.Directory).Get(context.WorkspaceId).Success);
        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        Assert.AreEqual(4, record["RecordSchemaVersion"]!.GetValue<int>());
        Assert.IsNull(record["RetainedRuleSources"]);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistence418_cached_Get_revalidates_missing_or_same_length_tampered_sidecars(bool missing)
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        CommitDraft418(context, store, DraftCommand418(context));
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
        Assert.IsTrue(store.Get(context.WorkspaceId).Success);
        Assert.IsTrue(store.CachedReadValidationCount > 0);
        byte[] record = File.ReadAllBytes(Record418(context));
        string artifact = Artifact418(context);
        if (missing) File.Delete(artifact);
        else
        {
            var time = File.GetLastWriteTimeUtc(artifact);
            byte[] bytes = File.ReadAllBytes(artifact);
            bytes[^1] ^= 1;
            File.WriteAllBytes(artifact, bytes);
            File.SetLastWriteTimeUtc(artifact, time);
        }
        Assert.IsFalse(store.Get(context.WorkspaceId).Success);
        CollectionAssert.AreEqual(record, File.ReadAllBytes(Record418(context)));
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("incarnation")]
    [DataRow("unconsumed")]
    [DataRow("history-binding")]
    [DataRow("downgrade")]
    public void Persistence418_private_manifest_cannot_rebind_or_invent_source_authority(string mutation)
    {
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        CommitDraft418(context, store, DraftCommand418(context));
        var record = JsonNode.Parse(File.ReadAllText(Record418(context)))!;
        var manifest = record["RetainedRuleSources"]!;
        if (mutation == "owner") manifest["OwnerId"] = "foreign-owner";
        else if (mutation == "incarnation")
        {
            string incarnation = Guid.NewGuid().ToString("N");
            record["LocalHistory"]!["IncarnationId"] = incarnation;
            manifest["IncarnationId"] = incarnation;
            manifest["References"]![0]!["IncarnationId"] = incarnation;
        }
        else if (mutation == "unconsumed")
        {
            var claim = manifest["References"]![0]!.DeepClone();
            claim["CaptureDigest"] = CharacterCreationFinalizationDigest.Compute("unconsumed-capture");
            manifest["References"]!.AsArray().Add(claim);
        }
        else if (mutation == "history-binding")
            manifest["Uses"]![0]!["ReceiptDigest"] = CharacterCreationFinalizationDigest.Compute("unconsumed-receipt");
        else record["RecordSchemaVersion"] = 4;
        File.WriteAllText(Record418(context), record.ToJsonString());
        Assert.IsFalse(store.Get(context.WorkspaceId).Success);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Persistence418_record_fault_reconciles_only_the_persisted_receipt_and_manifest(bool afterRename)
    {
        using var context = RuleContext410();
        var fault = new RecordFault418(afterRename ? FileWorkspaceStoreFaultStage.AfterTargetReplaced
            : FileWorkspaceStoreFaultStage.AfterTempFileFlushed);
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries, fault);
        var command = DraftCommand418(context);
        byte[] before = File.ReadAllBytes(Record418(context));
        var result = store.CommitCompletionDraft(OwnerScope.LocalSingleUser, command, context.Resolver, context.Queries);
        Assert.AreEqual(1, fault.Failures);
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries).Get(context.WorkspaceId);
        Assert.IsTrue(cold.Success);
        if (afterRename)
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
            Assert.AreEqual(result.Value!.ReceiptDigest,
                cold.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single().Receipt.ReceiptDigest);
            Assert.IsTrue(File.Exists(Artifact418(context)));
        }
        else
        {
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
            Assert.IsNull(cold.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts);
            Assert.AreEqual(1, Directory.EnumerateFiles(Path.Combine(context.Directory, "workspaces", ".retained-rule-sources"),
                "*.bin", SearchOption.AllDirectories).Count(), "Publication orphans are retained.");
        }
    }

    [TestMethod]
    public void Persistence418_legacy_and_root_only_constructors_fail_closed_for_new_source_receipts()
    {
        using var context = RuleContext410();
        var command = DraftCommand418(context);
        byte[] before = File.ReadAllBytes(Record418(context));
        foreach (var store in new[] { new FileWorkspaceStore(context.Directory),
                     FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory) })
        {
            var result = store.CommitCompletionDraft(OwnerScope.LocalSingleUser, command, context.Resolver, context.Queries);
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Blocked, result.Outcome);
            Assert.IsTrue(store.Get(context.WorkspaceId).Success, "Legacy history remains readable.");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(Record418(context)));
        }
    }

    // Extend the 416 fixture's existing two-process protocol with a real draft
    // receipt. No aggregate is promoted to Complete: the source410 fixture is
    // deliberately scoped and still has unresolved mechanics obligations.
    internal static void PublishPersistentDraftProcessFixture418(string root)
    {
        Assert.IsFalse(Directory.Exists(root));
        using var context = RuleContext410();
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(context.Directory, context.Queries);
        var receipt = CommitDraft418(context, store, DraftCommand418(context));
        Directory.Delete(Path.Combine(context.Directory, "source"), recursive: true);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var stream = new FileStream(Path.Combine(context.Directory, "draft-process-fixture.json"), options))
        {
            JsonSerializer.Serialize(stream, new DraftProcessFixture418(Environment.ProcessId, context.WorkspaceId, receipt.ReceiptDigest));
            stream.Flush(flushToDisk: true);
        }
        Directory.Move(context.Directory, root);
    }

    internal static void RecoverPersistentDraftProcessFixture418(string root)
    {
        using var stream = File.OpenRead(Path.Combine(root, "draft-process-fixture.json"));
        Assert.IsTrue(stream.Length <= 4096);
        var proof = JsonSerializer.Deserialize<DraftProcessFixture418>(stream)!;
        Assert.AreNotEqual(proof.ProcessId, Environment.ProcessId);
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "source")));
        var queries = new XmlCharacterFileQueries(new CharacterFileService());
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(root, queries);
        var saved = store.Get(proof.WorkspaceId);
        Assert.IsTrue(saved.Success);
        var draft = saved.Value!.Document.AuxiliaryState.CharacterCreationCompletionDrafts!.Single();
        Assert.AreEqual(proof.ReceiptDigest, draft.Receipt.ReceiptDigest);
        Assert.AreEqual(5, draft.Baseline.Review.Review.Skills!.Skills.Single(row => row.Name == "Pistols").Rating);
        Assert.IsFalse(draft.Baseline.Review.Review.CompleteAssessment!.SatisfiesComplete);
    }

    public sealed record DraftProcessFixture418(int ProcessId, CharacterWorkspaceId WorkspaceId, string ReceiptDigest);

    private static CharacterCreationCompletionDraftApplyRequest DraftCommand418(ReadyContext context, FileWorkspaceStore? store = null)
    {
        using var history = CharacterCreationCompletionDraftTransaction.EnterValidationOperation();
        store ??= context.Store;
        var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
        IOwnerBoundCharacterCreationCompletionReader reader = new OwnerBoundCharacterCreationCompletionReader(
            store, owners, context.Resolver, context.Queries);
        var loaded = reader.LoadCompletionInputsWithRuleSourceExecution(owners.Capture(), new(context.WorkspaceId));
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, loaded.Outcome, string.Join(",", loaded.Blockers));
        var prepared = loaded.Value!;
        var request = RuleRequest410(prepared.Inputs);
        var original = store.Get(context.WorkspaceId).Value!;
        var bootstrap = prepared.Inputs.OriginalCharacter!.BootstrapSourceSnapshot
            .Deserialize<CharacterCreationCompletionBootstrapSnapshot>()!;
        var evaluated = CharacterCreationSelectedCompletionEvaluator.ReviewedCompletion.Evaluate(
            prepared.Inputs, request, default, prepared.Execution);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, evaluated.Outcome, string.Join(",", evaluated.Blockers));
        var captured = evaluated.Value!.Capture(original, bootstrap, default);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, captured.Outcome, string.Join(",", captured.Blockers));
        var witness = captured.Value!;
        string authority = CharacterCreationFinalizationDigest.Compute(new
        {
            Schema = CharacterCreationCompletionDraftReview.SchemaV1,
            prepared.Inputs.SnapshotDigest, BootstrapDigest = bootstrap.SnapshotDigest, witness.WitnessDigest
        });
        var binding = new CharacterCreationFinalizationBinding(original.Id, original.ContentRevision, original.SavedRevision,
            CharacterCreationFinalizationDigest.ComputeUtf8(original.Document.Content), original.Document.AuxiliaryStateDigest,
            prepared.Inputs.Skills.PrerequisiteDraft.BuildMethod, authority);
        var review = new CharacterCreationCompletionDraftReview(CharacterCreationCompletionDraftReview.SchemaV1,
            binding, evaluated.Value.ReviewSnapshot, witness.WitnessDigest, string.Empty)
        {
            OwnerScopeDigest = CharacterCreationFinalizationDigest.Compute(OwnerScope.LocalSingleUser),
            OwnerStampDigest = CharacterCreationFinalizationDigest.Compute("persistence418-live-review")
        };
        review = review with { DraftReviewDigest = CharacterCreationCompletionDraftReviewRules.ComputeDigest(review) };
        Assert.IsFalse(review.Review.CompleteAssessment!.SatisfiesComplete);
        var command = new CharacterCreationCompletionDraftApplyRequest(
            CharacterCreationCompletionDraftApplyRequest.SchemaV1, review, "persistence418-real-draft", true);
        Assert.IsTrue(CharacterCreationCompletionDraftTransaction.IsConfirmed(command));
        return command;
    }

    private static CharacterCreationCompletionDraftReceipt CommitDraft418(ReadyContext context, FileWorkspaceStore store,
        CharacterCreationCompletionDraftApplyRequest command)
    {
        var result = store.CommitCompletionDraft(OwnerScope.LocalSingleUser, command, context.Resolver, context.Queries);
        Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, result.Outcome, string.Join(",", result.Blockers));
        return result.Value!;
    }

    private static string Record418(ReadyContext context)
        => Path.Combine(context.Directory, "workspaces", context.WorkspaceId.Value + ".json");

    private static JsonNode Manifest418(ReadyContext context)
        => JsonNode.Parse(File.ReadAllText(Record418(context)))!["RetainedRuleSources"]!;

    private static string Artifact418(ReadyContext context)
    {
        var reference = Manifest418(context)["References"]![0]!;
        return Path.Combine(context.Directory, "workspaces", ".retained-rule-sources", context.WorkspaceId.Value,
            reference["IncarnationId"]!.GetValue<string>(), reference["CaptureDigest"]!.GetValue<string>()[7..] + ".bin");
    }

    private sealed class RecordFault418(FileWorkspaceStoreFaultStage failAt) : IFileWorkspaceStoreFaultInjector
    {
        internal int Failures { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != failAt || !targetPath.EndsWith(".json", StringComparison.Ordinal)) return;
            Failures++;
            throw new IOException("Synthetic record acknowledgement fault.");
        }
    }

    private sealed class RecordAction418(Action action) : IFileWorkspaceStoreFaultInjector
    {
        internal bool Called { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (Called || stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed
                || !targetPath.EndsWith(".json", StringComparison.Ordinal)) return;
            Called = true;
            action();
        }
    }
}
