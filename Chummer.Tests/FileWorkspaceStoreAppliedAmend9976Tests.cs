using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Owners9976 = Chummer.Tests.CharacterCreationAttributesServiceTests.AllocationOwners;

namespace Chummer.Tests;

[TestClass]
public sealed class FileWorkspaceStoreAppliedAmend9976Tests
{
    [DataTestMethod]
    [DataRow(false, 80)]
    [DataRow(true, 50)]
    public void AppliedAmend9976_Stored_author_review_confirm_reopen_retains_original(bool reverse, int expectedCost)
    {
        using var f = new SavedRunner(reverse, authenticatedHistory: true);
        var original = f.Export();
        var before = f.Store.Get(f.Id).Value!;
        Assert.AreEqual(10m, f.Quote());
        f.StageAmend();
        // Ambient edit cannot silently refresh an ordinary export.
        CollectionAssert.AreEqual(original.ExportPrivateBytes(), f.Export().ExportPrivateBytes());
        var candidate = f.Store.AuthorCurrentRuleEnvironmentUpdate(f.Owners, f.Owners.Capture(),
            original.Binding, f.Resolver());
        Assert.AreEqual(original.Binding.ContinuationDigest, candidate.Binding.ContinuationDigest);
        Assert.AreNotEqual(original.Binding.CurrentEnvironmentDigest, candidate.Binding.CurrentEnvironmentDigest);
        Assert.AreEqual(before.Document.Content, candidate.ExportContinuation().Snapshot.Workspace.Document.Content);
        using var review = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview, string.Join(",", review.Result.Blockers ?? []));
        Assert.IsTrue(review.UpdatePreview.NewLocalRevision is long newRevision && newRevision > before.ContentRevision);
        var result = f.Service.Confirm(review, true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, string.Join(",", result.Blockers ?? []));
        Assert.AreEqual(WorkspaceRuleUpdateState.CommittedUpdate, result.RuleUpdate?.State);
        var alternatives = f.Service.ListUpdateAlternatives(f.Owners.Capture(), f.Id);
        var originalAlternative = alternatives.Single(row => row.Binding == original.Binding);
        Assert.AreEqual((decimal)expectedCost, f.Quote());
        var after = f.Store.Get(f.Id).Value!;
        Assert.AreEqual(before.Document.Content, after.Document.Content);
        Assert.AreEqual(JsonSerializer.Serialize(before.Document.AuxiliaryState), JsonSerializer.Serialize(after.Document.AuxiliaryState));
        Assert.AreEqual(after.ContentRevision, after.SavedRevision);
        var updated = f.Export();
        // Verify the previous authenticated prefix remains byte-for-byte intact.
        var oldDecoded = WorkspacePrivateRuleSourceClosure.DecodeForRestore(original.ExportPrivateBytes(), original.Binding, f.Queries, default);
        var newDecoded = WorkspacePrivateRuleSourceClosure.DecodeForRestore(updated.ExportPrivateBytes(), updated.Binding, f.Queries, default);
        var oldHistory = oldDecoded.AuthenticatedHistory!.Transitions;
        var newHistory = newDecoded.AuthenticatedHistory!.Transitions;
        Assert.AreEqual(oldHistory.Length + 1, newHistory.Length);
        CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(oldHistory),
            JsonSerializer.SerializeToUtf8Bytes(newHistory[..^1]));
        Assert.IsTrue(oldDecoded.Captures.Keys.All(newDecoded.Captures.ContainsKey));
        CheckTransport(updated);
        CheckHistoryFramingRefusals(updated, f.Queries);
        Directory.Delete(f.Sources, true);
        f.Reopen(); // Reconstruct store + trust domain; all source reads must be retained.
        Assert.AreEqual((decimal)expectedCost, f.Quote());
        CollectionAssert.AreEqual(updated.ExportPrivateBytes(), f.Export().ExportPrivateBytes());
        // The retained alternative is executable and can be explicitly restored.
        using var restore = f.Service.ReviewUpdateAlternative(f.Owners.Capture(), f.Id,
            originalAlternative.OperationId, f.Resolver());
        Assert.IsNotNull(restore.UpdatePreview, string.Join(",", restore.Result.Blockers ?? []));
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(restore, true).Outcome);
        f.Reopen();
        Assert.AreEqual(10m, f.Quote());
        Assert.AreEqual(before.Document.Content, f.Store.Get(f.Id).Value!.Document.Content);
    }

    [TestMethod]
    public void AppliedAmend9976_Refuses_stale_source_owner_cancel_expiry_and_baseline()
    {
        using var f = new SavedRunner(false);
        var baseline = f.Export();
        f.StageAmend();
        using (var canceled = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver()))
        {
            Assert.IsNotNull(canceled.UpdatePreview);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Canceled, f.Service.Confirm(canceled, false).Outcome);
        }
        using (var canceledToken = new CancellationTokenSource())
        {
            canceledToken.Cancel();
            using var refused = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver(), canceledToken.Token);
            Assert.IsNull(refused.UpdatePreview);
        }
        using (var changedSource = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver()))
        {
            Assert.IsNotNull(changedSource.UpdatePreview);
            string path = Path.Combine(f.Sources, "customdata", "Second", "amend_gear.xml");
            string bytes = File.ReadAllText(path);
            File.WriteAllText(path, bytes.Replace(">80<", ">81<", StringComparison.Ordinal));
            var result = f.Service.Confirm(changedSource, true);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Conflict, result.Outcome);
            Assert.AreEqual(WorkspaceRuleUpdateState.NoMutation, result.RuleUpdate?.State);
            File.WriteAllText(path, bytes);
        }
        var owner = f.Owners.Capture();
        using (var wrongOwner = f.Service.ReviewAuthoredUpdate(owner, baseline.Binding, f.Resolver()))
        {
            Assert.IsNotNull(wrongOwner.UpdatePreview);
            f.Owners.Set(new OwnerScope("other-owner9976"));
            using var refused = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
            Assert.IsNull(refused.UpdatePreview);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Unavailable, f.Service.Confirm(wrongOwner, true).Outcome);
            f.Owners.Set(OwnerScope.LocalSingleUser);
        }
        var clock = new ReviewClock();
        var timed = new WorkspacePrivateRuleSourceClosureRestoreService(f.Store, f.Owners, f.Queries, clock);
        using (var expired = timed.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver()))
        {
            Assert.IsNotNull(expired.UpdatePreview);
            clock.Now += TimeSpan.FromHours(1);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewExpired, timed.Confirm(expired, true).Outcome);
        }
        CollectionAssert.AreEqual(baseline.ExportPrivateBytes(), f.Export().ExportPrivateBytes());
        Assert.AreEqual(0, f.Service.ListUpdateAlternatives(f.Owners.Capture(), f.Id).Count);
        using var stale = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
        using var current = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
        Assert.IsNotNull(stale.UpdatePreview);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(current, true).Outcome);
        var accepted = f.Export();
        Assert.AreNotEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(stale, true).Outcome);
        using var staleBaseline = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
        Assert.IsNull(staleBaseline.UpdatePreview);
        CollectionAssert.AreEqual(accepted.ExportPrivateBytes(), f.Export().ExportPrivateBytes());
    }

    [TestMethod]
    public void AppliedAmend9976_Native_probe_failure_refuses_before_authoring_or_publication()
    {
        using var f = new SavedRunner(false);
        var baseline = f.Export();
        f.StageAmend();
        foreach (bool loader in new[] { false, true })
        {
            var store = f.CreateFaultingStore(new ProbeFailure(loader));
            var service = new WorkspacePrivateRuleSourceClosureRestoreService(store, f.Owners, f.Queries);
            using var review = service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
            Assert.IsNull(review.UpdatePreview);
            CollectionAssert.Contains(review.Result.Blockers!.ToArray(), "rule-update-durability-unavailable");
            Assert.AreEqual(0, service.ListUpdateAlternatives(f.Owners.Capture(), f.Id).Count);
            CollectionAssert.AreEqual(baseline.ExportPrivateBytes(), f.Export().ExportPrivateBytes());
        }
    }

    [TestMethod]
    public void AppliedAmend9976_Post_replace_flush_failure_is_unknown_and_recovered_without_replay()
    {
        using var f = new SavedRunner(false);
        var baseline = f.Export(); f.StageAmend();
        var store = f.CreateFaultingStore(new RecordFlushFailure());
        var service = new WorkspacePrivateRuleSourceClosureRestoreService(store, f.Owners, f.Queries);
        using var review = service.ReviewAuthoredUpdate(f.Owners.Capture(), baseline.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        var attempted = service.Confirm(review, true);
        Assert.AreNotEqual(WorkspaceContinuationRestoreOutcome.Applied, attempted.Outcome);
        Assert.AreEqual(WorkspaceRuleUpdateState.Unknown, attempted.RuleUpdate?.State);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, service.Confirm(review, true).Outcome);
        f.Reopen();
        var recovered = f.Service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!);
        Assert.AreEqual(WorkspaceRuleUpdateState.CommittedUpdate, recovered.RuleUpdate?.State);
        Assert.AreEqual(80m, f.Quote());
        Assert.IsTrue(f.Service.ListUpdateAlternatives(f.Owners.Capture(), f.Id).Any(row => row.Binding == baseline.Binding));
    }

    private static void RequireOrdinaryWindows9981()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Native ordinary-user Windows test; not executed on this OS.");
            return;
        }
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        Assert.IsFalse(new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator), "Run this acceptance WITHOUT elevation.");
    }

    [DataTestMethod]
    [DataRow(nameof(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateJournalPublished), false)]
    [DataRow(nameof(FileWorkspaceStoreFaultStage.AfterWindowsRuleUpdateJournalPublished), false)]
    [DataRow(nameof(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateCommitPublished), true)]
    [DataRow(nameof(FileWorkspaceStoreFaultStage.AfterWindowsRuleUpdateCommitPublished), true)]
    public void OrdinaryWindows9981_Publication_boundary_keeps_original_and_recovers_same_operation(
        string stageName, bool adopted)
    {
        RequireOrdinaryWindows9981();
        using var f = new SavedRunner(false);
        var original = f.Export();
        f.StageAmend();
        var fault = new StageFault9981(Enum.Parse<FileWorkspaceStoreFaultStage>(stageName));
        var store = f.CreateFaultingStore(fault);
        var service = new WorkspacePrivateRuleSourceClosureRestoreService(store, f.Owners, f.Queries);
        using var review = service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        var result = service.Confirm(review, true);
        Assert.IsTrue(fault.Hit);
        Assert.AreNotEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome);
        Assert.AreEqual(WorkspaceRuleUpdateState.Unknown, result.RuleUpdate?.State);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, service.Confirm(review, true).Outcome);
        f.Reopen();
        var recovered = f.Service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!);
        Assert.AreEqual(adopted ? WorkspaceRuleUpdateState.CommittedUpdate : WorkspaceRuleUpdateState.Unknown, recovered.RuleUpdate?.State);
        Assert.AreEqual(adopted ? 80m : 10m, f.Quote());
        Assert.IsTrue(f.Service.ListUpdateAlternatives(f.Owners.Capture(), f.Id)
            .Any(value => value.Binding == original.Binding));
    }

    [TestMethod]
    public void OrdinaryWindows9981_Durable_journal_repairs_lost_rename_and_dependencies_without_new_history()
    {
        RequireOrdinaryWindows9981();
        using var f = new SavedRunner(false, authenticatedHistory: true);
        string path = Directory.GetFiles(f.State, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
        byte[] before = File.ReadAllBytes(path);
        DateTime beforeTime = File.GetLastWriteTimeUtc(path);
        var original = f.Export();
        f.StageAmend();
        using var review = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(review, true).Outcome);
        byte[] published = File.ReadAllBytes(path);
        DateTime publishedTime = File.GetLastWriteTimeUtc(path);
        var exported = f.Export().ExportPrivateBytes();
        // Model loss of the final ReplaceFile namespace update, NOT a power-cut test.
        File.WriteAllBytes(path, before);
        File.SetLastWriteTimeUtc(path, beforeTime);
        foreach (string capture in Directory.GetFiles(f.State, "*.bin", SearchOption.AllDirectories)) File.Delete(capture);
        Directory.Delete(f.Sources, true);
        f.Reopen();
        Assert.AreEqual(80m, f.Quote()); // ordinary read performs same-operation redo first
        CollectionAssert.AreEqual(published, File.ReadAllBytes(path));
        Assert.AreEqual(publishedTime, File.GetLastWriteTimeUtc(path));
        CollectionAssert.AreEqual(exported, f.Export().ExportPrivateBytes());
        Assert.AreEqual(WorkspaceRuleUpdateState.CommittedUpdate,
            f.Service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!).RuleUpdate?.State);
        var saved = f.Store.Get(f.Id).Value!;
        Assert.IsTrue(f.Store.ReplaceWorkspaceDocumentAndCheckpoint(f.Id, saved.ContentRevision, saved.Document).Success);
        byte[] later = File.ReadAllBytes(path);
        f.Reopen();
        Assert.IsTrue(f.Store.Get(f.Id).Success);
        CollectionAssert.AreEqual(later, File.ReadAllBytes(path)); // never rewind a later save
        using var old = f.Service.ReviewUpdateAlternative(f.Owners.Capture(), f.Id, review.OperationId, f.Resolver());
        Assert.IsNotNull(old.UpdatePreview);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(old, true).Outcome);
        f.Reopen();
        Assert.AreEqual(10m, f.Quote());
    }

    [TestMethod]
    public void OrdinaryWindows9981_Recovery_failure_missing_target_and_changed_slot_never_recreate()
    {
        RequireOrdinaryWindows9981();
        using var f = new SavedRunner(false);
        string path = Directory.GetFiles(f.State, f.Id.Value + ".json", SearchOption.AllDirectories).Single();
        byte[] before = File.ReadAllBytes(path);
        DateTime beforeTime = File.GetLastWriteTimeUtc(path);
        var original = f.Export();
        f.StageAmend();
        using var review = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(review, true).Outcome);
        byte[] published = File.ReadAllBytes(path);
        DateTime publishedTime = File.GetLastWriteTimeUtc(path);
        byte[] publishedExport = f.Export().ExportPrivateBytes();
        File.WriteAllBytes(path, before);
        File.SetLastWriteTimeUtc(path, beforeTime);
        byte[]? slot = File.Exists(path + ".slot") ? File.ReadAllBytes(path + ".slot") : null;
        string temporaryPattern = Path.GetFileName(path) + ".tmp.*";
        var recoveryTemporaries = new List<string>();
        var fault = new StageFault9981(FileWorkspaceStoreFaultStage.BeforeWindowsRuleUpdateRecoveryReplace,
            (_, temporary) => recoveryTemporaries.Add(temporary));
        var store = f.CreateFaultingStore(fault);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern).Length);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.IsFalse(store.Get(f.Id).Success);
            Assert.IsTrue(fault.Hit);
            Assert.AreEqual(attempt + 1, recoveryTemporaries.Count);
            Assert.IsFalse(File.Exists(recoveryTemporaries[attempt]));
            Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern).Length);
            CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
            Assert.AreEqual(beforeTime, File.GetLastWriteTimeUtc(path));
            Assert.AreEqual(slot is not null, File.Exists(path + ".slot"));
            if (slot is not null) CollectionAssert.AreEqual(slot, File.ReadAllBytes(path + ".slot"));
        }
        File.WriteAllText(path + ".slot", Guid.NewGuid().ToString("N"));
        f.Reopen();
        Assert.IsFalse(f.Store.Get(f.Id).Success);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(path));
        Assert.AreEqual(beforeTime, File.GetLastWriteTimeUtc(path));
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern).Length);
        if (slot is null) File.Delete(path + ".slot"); else File.WriteAllBytes(path + ".slot", slot);
        f.Reopen();
        Assert.IsTrue(f.Store.Get(f.Id).Success);
        CollectionAssert.AreEqual(published, File.ReadAllBytes(path));
        Assert.AreEqual(publishedTime, File.GetLastWriteTimeUtc(path));
        CollectionAssert.AreEqual(publishedExport, f.Export().ExportPrivateBytes());
        Assert.AreEqual(WorkspaceRuleUpdateState.CommittedUpdate,
            f.Service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!).RuleUpdate?.State);
        Assert.AreEqual(0, Directory.GetFiles(Path.GetDirectoryName(path)!, temporaryPattern).Length);
        File.Delete(path);
        f.Reopen();
        Assert.IsFalse(f.Store.Get(f.Id).Success);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(WorkspaceRuleUpdateState.Unknown,
            f.Service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!).RuleUpdate?.State);
    }

    [TestMethod]
    public void OrdinaryWindows9981_Final_target_deletion_and_ancestor_substitution_are_refused()
    {
        RequireOrdinaryWindows9981();
        using var f = new SavedRunner(false);
        var original = f.Export();
        f.StageAmend();
        bool namespaceRefused = false;
        string? removed = null;
        var fault = new StageFault9981(FileWorkspaceStoreFaultStage.AfterWindowsRuleUpdateJournalPublished, (path, _) =>
        {
            string directory = Path.GetDirectoryName(path)!;
            try { Directory.Move(directory, directory + ".moved9981"); }
            catch (IOException) { namespaceRefused = true; }
            Assert.IsTrue(namespaceRefused, "Ancestor must remain pinned across publication.");
            File.Delete(path);
            removed = path;
        }, throwAfter: false);
        var store = f.CreateFaultingStore(fault);
        var service = new WorkspacePrivateRuleSourceClosureRestoreService(store, f.Owners, f.Queries);
        using var review = service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        Assert.AreNotEqual(WorkspaceContinuationRestoreOutcome.Applied, service.Confirm(review, true).Outcome);
        Assert.IsTrue(fault.Hit);
        Assert.IsNotNull(removed);
        Assert.IsFalse(File.Exists(removed));
        Assert.AreEqual(WorkspaceRuleUpdateState.Unknown,
            service.Recover(f.Owners.Capture(), f.Id, review.OperationId, review.AdmissionDigest!).RuleUpdate?.State);
        Assert.IsFalse(File.Exists(removed));
    }

    [TestMethod]
    public void OrdinaryWindows9981_Journal_reparse_and_tampered_commit_are_refused()
    {
        RequireOrdinaryWindows9981();
        using var f = new SavedRunner(false);
        var original = f.Export();
        f.StageAmend();
        using var review = f.Service.ReviewAuthoredUpdate(f.Owners.Capture(), original.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, f.Service.Confirm(review, true).Outcome);
        string ready = Directory.GetFiles(f.State, review.OperationId.ToString("N") + ".ready", SearchOption.AllDirectories).Single();
        string commit = Path.ChangeExtension(ready, ".commit");
        byte[] marker = File.ReadAllBytes(commit);
        File.WriteAllText(commit, new string('0', 64));
        f.Reopen();
        Assert.IsFalse(f.Store.Get(f.Id).Success);
        File.WriteAllBytes(commit, marker);
        string outside = Path.Combine(f.State, "ready-outside9981");
        File.Move(ready, outside);
        try { File.CreateSymbolicLink(ready, outside); }
        catch (Exception error) when (error is UnauthorizedAccessException || error is IOException && (error.HResult & 0xffff) == 1314)
        {
            File.Move(outside, ready);
            Assert.Inconclusive("Tampered marker refusal exercised; this ordinary token cannot create a test symlink. Native reparse acceptance remains unexecuted.");
        }
        try
        {
            f.Reopen();
            Assert.IsFalse(f.Store.Get(f.Id).Success);
        }
        finally { File.Delete(ready); File.Move(outside, ready); }
    }

    private sealed class StageFault9981(FileWorkspaceStoreFaultStage selected,
        Action<string, string>? callback = null, bool throwAfter = true) : IFileWorkspaceStoreFaultInjector
    {
        internal bool Hit { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != selected) return;
            Hit = true;
            callback?.Invoke(targetPath, tempPath);
            if (throwAfter) throw new IOException("test-only Windows journal boundary failure");
        }
    }

    private sealed class RecordFlushFailure : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.BeforeRuleUpdateRecordDirectorySync)
                throw new IOException("test-only post-publication flush fault");
        }
    }

    private sealed class ProbeFailure(bool loader) : IFileWorkspaceStoreFaultInjector
    {
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == FileWorkspaceStoreFaultStage.BeforeRuleUpdateNativeProbe)
            {
                if (loader) throw new EntryPointNotFoundException("test-only native loader fault");
                throw new IOException("test-only required flush fault");
            }
        }
    }
    private sealed class ReviewClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void CheckTransport(WorkspacePrivateRuleSourceClosure closure)
    {
        byte[] bytes = closure.ExportPrivateBytes();
        var verifier = new WorkspaceRuleEnvironmentTransportVerifier();
        // Deliberately split lengths/tokens; checkpoint through actual JSON state.
        for (int i = 0; i < bytes.Length; i += 16383)
        {
            verifier.Append(bytes.AsSpan(i, Math.Min(16383, bytes.Length - i)));
            verifier = JsonSerializer.Deserialize<WorkspaceRuleEnvironmentTransportVerifier>(JsonSerializer.Serialize(verifier))!;
        }
        verifier.Complete(new(WorkspaceRuleEnvironmentTransfer.SchemaV1, closure.Binding, bytes.Length));
    }
    private static void CheckHistoryFramingRefusals(WorkspacePrivateRuleSourceClosure closure, ICharacterFileQueries queries)
    {
        byte[] bytes = closure.ExportPrivateBytes();
        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        void SkipBlock() { int length = reader.ReadInt32(); stream.Position += length; }
        SkipBlock(); SkipBlock(); int archives = reader.ReadInt32();
        for (int i = 0; i < archives; i++) { SkipBlock(); SkipBlock(); }
        SkipBlock(); SkipBlock(); int historyOffset = checked((int)stream.Position);
        // The history-free V2 prefix is still supported with its own exact digest.
        byte[] prefix = bytes[..historyOffset];
        var prefixBinding = closure.Binding with { ClosureDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(prefix)) };
        var plain = new WorkspaceRuleEnvironmentTransportVerifier(); plain.Append(prefix);
        plain.Complete(new(WorkspaceRuleEnvironmentTransfer.SchemaV1, prefixBinding, prefix.Length));
        // Custody observes framing/digest, never claims canonical/signature admission.
        // Core rejects even semantically identical history with noncanonical bytes.
        int historyLength = BitConverter.ToInt32(bytes, historyOffset);
        byte[] noncanonical = [.. prefix, .. BitConverter.GetBytes(historyLength + 1), (byte)' ', .. bytes[(historyOffset + 4)..]];
        var noncanonicalBinding = closure.Binding with
        { ClosureDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(noncanonical)) };
        var custody = new WorkspaceRuleEnvironmentTransportVerifier(); custody.Append(noncanonical);
        custody.Complete(new(WorkspaceRuleEnvironmentTransfer.SchemaV1, noncanonicalBinding, noncanonical.Length));
        Assert.ThrowsExactly<InvalidOperationException>(() => WorkspacePrivateRuleSourceClosure.Import(noncanonical,
            noncanonicalBinding, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, queries));
        foreach (byte[] bad in new byte[][] { bytes[..^1], [.. bytes, (byte)0], [.. prefix, (byte)1],
            [.. prefix, .. BitConverter.GetBytes(WorkspaceAuthenticatedHistory.MaximumBytes + 1)] })
        {
            Assert.ThrowsExactly<InvalidDataException>(() =>
            {
                var v = new WorkspaceRuleEnvironmentTransportVerifier(); v.Append(bad);
                var binding = closure.Binding with { ClosureDigest = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bad)) };
                v.Complete(new(WorkspaceRuleEnvironmentTransfer.SchemaV1, binding, bad.Length));
            });
        }
    }

    // The mundane Priority path from ReadyContext uses A/B/C/D/E once each and
    // real creation-authority, draft and finalization services. Keep this path here:
    // ReadyContext belongs to the separate Racial test project, not this assembly.
    // Import the whole finalized .chum5 payload, never relabel Soma's allocations
    // or transplant native creation receipts to a different workspace id.
    internal static string CreatePriorityImport10101(string sources)
    {
        string directory = Path.Combine(Path.GetTempPath(), "chummer-priority-import10101-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(sources, sources, null));
            var queries = new XmlCharacterFileQueries(new CharacterFileService());
            var store = new FileWorkspaceStore(directory);
            // ReadyContext.BootstrapPersistedShapeFixture is the established
            // selected-custom-source entry. Bind the pre-decision shape before
            // any draft exists; ordinary empty-profile bootstrap has no pack selection.
            var character = XElement.Parse($"""
                <character>
                  <name>Priority Runner</name><alias>Priority10101</alias><buildmethod>Priority</buildmethod>
                  <createdversion>5.225.0</createdversion><appversion>5.225.0</appversion>
                  <karma>0</karma><nuyen>0</nuyen><created>False</created><gameedition>SR5</gameedition>
                  <settings>{CharacterCreationBootstrapProfiles.PrioritySettingsProfileId}</settings>
                  <{CharacterCreationBootstrapXml.MarkerElement}>
                    <{CharacterCreationBootstrapXml.SchemaElement}>{CharacterCreationBootstrapSchemas.MarkerV1}</{CharacterCreationBootstrapXml.SchemaElement}>
                    <{CharacterCreationBootstrapXml.StageElement}>{CharacterCreationBootstrapStages.AwaitingFoundationSelection}</{CharacterCreationBootstrapXml.StageElement}>
                  </{CharacterCreationBootstrapXml.MarkerElement}>
                  <streetcred>0</streetcred><notoriety>0</notoriety><publicawareness>0</publicawareness><burntstreetcred>0</burntstreetcred>
                  <expenses /><improvements /><contacts />
                  <customdatadirectorynames><directoryname>Seed9967</directoryname><directoryname>First</directoryname><directoryname>Second</directoryname></customdatadirectorynames>
                </character>
                """);
            var id = new CharacterWorkspaceId(Guid.NewGuid().ToString("N"));
            var document = new WorkspaceDocument(new WorkspacePayloadEnvelope("sr5", 1,
                "sr5/chum5-xml", character.ToString(SaveOptions.DisableFormatting)));
            Assert.IsTrue(CharacterCreationBootstrapAuthority.TryPrepareBinding(id, document, resolver,
                out var binding, out _, out var blockers), string.Join(",", blockers));
            document = document with { State = document.State with
            { AuxiliaryState = new WorkspaceDocumentAuxiliaryState(CharacterCreationBootstrapBinding: binding) } };
            var created = ((ICharacterCreationBootstrapAtomicCreateCapability)store)
                .CreateCharacterCreationBootstrapWorkspaceDocument(id, document);
            Assert.IsTrue(created.Success, created.Error);
            var prerequisites = new CharacterCreationPrerequisiteService(store, queries, resolver);
            var prerequisite = prerequisites.Load(new(id)).Value!;
            var ranks = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [CharacterCreationPriorityCategoryIds.Heritage] = "A",
                [CharacterCreationPriorityCategoryIds.Attributes] = "B",
                [CharacterCreationPriorityCategoryIds.Skills] = "C",
                [CharacterCreationPriorityCategoryIds.Resources] = "D",
                [CharacterCreationPriorityCategoryIds.Talent] = "E"
            };
            var heritage = prerequisite.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Heritage && x.Rank == "A")
                .HeritageOptions.First(x => x.IsEnabled && x.MetavariantSourceId is null && x.MetatypeName == "Human");
            var talent = prerequisite.Authority.Options.Single(x => x.CategoryId == CharacterCreationPriorityCategoryIds.Talent && x.Rank == "E")
                .TalentOptions.First(x => x.IsEnabled && string.Equals(x.Value, CharacterCreationMagicResonanceKinds.Mundane, StringComparison.OrdinalIgnoreCase)
                    && x.Magic is null && x.Resonance is null && x.Depth is null && x.ActiveSkillGrant is null && x.SkillGroupGrant is null);
            var foundation = prerequisites.Preview(new(prerequisite.Binding, ranks)
            { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, prerequisites.Confirm(new(foundation.Binding,
                ranks, foundation.PreviewDigest, true) { HeritageSelectionId = heritage.SelectionId, TalentSelectionId = talent.SelectionId }).Outcome);
            var attributes = new CharacterCreationAttributesService(store, resolver);
            var attributePreview = attributes.Preview(new(attributes.Load(new(id)).Value!.Binding, [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, attributes.Confirm(new(attributePreview.Binding,
                [], attributePreview.PreviewDigest, true)).Outcome);
            var skills = new CharacterCreationSkillsService(store, resolver);
            var skillState = skills.Load(new(id)).Value!;
            var native = skillState.Authority.KnowledgeSkills.First(x => x.CanBeNativeLanguage);
            CharacterCreationSkillAllocation[] allocations = [new(native.SourceSkillId, CharacterCreationSkillKinds.Knowledge, null, null, true)];
            var skillPreview = skills.Preview(new(skillState.Binding, allocations, [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, skills.Confirm(new(skillPreview.Binding,
                allocations, [], skillPreview.PreviewDigest, "skills-import10101", true)).Outcome);
            var qualities = new CharacterCreationQualitiesService(store, resolver, prerequisites, attributes);
            var qualityPreview = qualities.Preview(new(qualities.Load(new(id)).Value!.Binding, [])).Value!;
            Assert.AreEqual(CharacterCreationFoundationOutcomes.Success, qualities.Confirm(new(qualityPreview.Binding,
                [], qualityPreview.PreviewDigest, "qualities-import10101", Guid.NewGuid(), true)).Outcome);
            var resources = new CharacterCreationResourcesService(store, resolver);
            var resourceState = resources.Load(new(id)).Value!;
            var zeroKarma = resourceState.Options.Single(x => x.IsEnabled && x.KarmaInvestment == 0);
            var resourcePreview = resources.Preview(new(resourceState.Binding, zeroKarma.OptionId)).Value!;
            Assert.AreEqual(CharacterCreationResourcesOutcomes.Applied, resources.Confirm(new(resourcePreview.Binding,
                zeroKarma.OptionId, resourcePreview.PreviewDigest, "resources-import10101", true)).Outcome);
            var gear = new CharacterCreationGearService(store, resolver);
            var gearPreview = gear.Preview(new(gear.Load(new(id)).Value!.Binding, [])).Value!;
            Assert.AreEqual(CharacterCreationGearOutcomes.Applied, gear.Confirm(new(gearPreview.Binding,
                [], gearPreview.PreviewDigest, "gear-import10101", true)).Outcome);
            var finalizer = new CharacterCreationFinalizationService(store, queries, prerequisites, attributes, skills, qualities,
                new CharacterCreationMagicResonanceService(store, resolver), resources, gear, resolver);
            var state = finalizer.Load(new(id)).Value!;
            Assert.IsTrue(state.CanReview, string.Join(",", state.Blockers));
            Assert.IsTrue(state.Steps.All(step => step.IsComplete));
            Assert.AreEqual(CharacterCreationBuildMethods.Priority, state.Binding.BuildMethod);
            var source = resolver.TryCreateContext(store.Get(id).Value!.Document.Content);
            Assert.IsNotNull(source);
            Assert.IsTrue(source!.TryResolveCreationDefaultStartingNuyen(out var cash));
            Assert.IsNotNull(cash);
            var reviewed = finalizer.Review(new(state.Binding) { StartingCash = new(cash!.AuthorityDigest, cash.Dice) });
            Assert.IsNotNull(reviewed.Value, string.Join(",", reviewed.Blockers));
            var review = reviewed.Value!;
            Assert.IsTrue(review.CanConfirm, string.Join(",", review.Blockers));
            Assert.IsNotNull(review.Plan);
            var applied = finalizer.Confirm(new(state.Binding, review.PreviewDigest,
                review.Plan!.PlanDigest, "priority-import10101", true) { StartingCash = review.Plan.StartingCash });
            Assert.AreEqual(CharacterCreationFinalizationOutcomes.Applied, applied.Outcome, string.Join(",", applied.Blockers));
            string xml = store.Get(id).Value!.Document.Content;
            character = XElement.Parse(xml);
            Assert.AreEqual("True", character.Element("created")!.Value);
            Assert.AreEqual(CharacterCreationBuildMethods.Priority, character.Element("buildmethod")!.Value);
            Assert.AreEqual(CharacterCreationBootstrapProfiles.PrioritySettingsProfileId, character.Element("settings")!.Value);
            CollectionAssert.AreEqual(new[] { "Seed9967", "First", "Second" },
                character.Element("customdatadirectorynames")!.Elements("directoryname").Select(x => x.Value).ToArray());
            return xml;
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class SavedRunner : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "chummer-amend9976-" + Guid.NewGuid().ToString("N"));
        private readonly string _fixture = Environment.GetEnvironmentVariable("CHUMMER_AMEND9967_FIXTURE")
            ?? throw new AssertFailedException("Set CHUMMER_AMEND9967_FIXTURE to the pinned 9967 fixture.");
        internal string Sources => Path.Combine(_root, "selected");
        internal string State => Path.Combine(_root, "state");
        internal readonly Owners9976 Owners = new();
        internal readonly XmlCharacterFileQueries Queries = new(new CharacterFileService());
        internal CharacterWorkspaceId Id { get; }
        internal FileWorkspaceStore Store { get; private set; } = null!;
        internal WorkspacePrivateRuleSourceClosureRestoreService Service { get; private set; } = null!;
        internal string Trust => Path.Combine(_root, "trust");

        internal SavedRunner(bool reverse, bool authenticatedHistory = false)
        {
            if (OperatingSystem.IsWindows()) RequireOrdinaryWindows9981();
            Directory.CreateDirectory(_root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string source = Environment.GetEnvironmentVariable("CHUMMER_AMEND9976_BASE_SOURCES")
                ?? throw new AssertFailedException("Set CHUMMER_AMEND9976_BASE_SOURCES to frozen Core/Chummer (complete data/lang).");
            CopyXml(Path.Combine(source, "data"), Path.Combine(Sources, "data"));
            CopyXml(Path.Combine(source, "lang"), Path.Combine(Sources, "lang"));
            CopyXml(Path.Combine(_fixture, "initialize", "data"), Path.Combine(Sources, "data"));
            CopyXml(Path.Combine(_fixture, "baseline", "customdata"), Path.Combine(Sources, "customdata"));
            string xml = CreatePriorityImport10101(Sources);
            if (reverse)
            {
                var character = XElement.Parse(xml);
                character.Element("customdatadirectorynames")!.ReplaceNodes(new XElement("directoryname", "Seed9967"),
                    new XElement("directoryname", "Second"), new XElement("directoryname", "First"));
                xml = character.ToString(SaveOptions.DisableFormatting);
                string settingsPath = Path.Combine(Sources, "data", "settings-all-sources.xml");
                var settings = XDocument.Load(settingsPath);
                foreach (var row in settings.Descendants("customdatadirectoryname"))
                    if (row.Element("directoryname")?.Value is "First" or "Second")
                        row.Element("order")!.Value = row.Element("directoryname")!.Value == "First" ? "2" : "1";
                settings.Save(settingsPath);
            }
            var aux = WorkspaceDocumentAuxiliaryState.Empty with
            { ExtensionData = new Dictionary<string, JsonElement> { ["fixtureUnknown9976"] = JsonSerializer.SerializeToElement(new { keep = "unchanged" }) } };
            Reopen();
            var document = new WorkspaceDocument(xml, "sr5");
            var created = Store.CreateWorkspaceDocument(document);
            Assert.IsTrue(created.Success, created.Error); Id = created.Entry!.Value.Id;
            Assert.IsTrue(Store.SaveCheckpoint(Id, created.Entry.Value.ContentRevision).Success);
            // Generic creation deliberately rejects auxiliary state. Seed the unknown
            // portable field through the supported full-continuation review/restore,
            // which validates the owner, complete history, source and target fences.
            var read = Store.ReadContinuation(Id);
            Assert.IsTrue(read.Success, read.Error);
            var snapshotWithAux = read.Value! with
            {
                Workspace = read.Value!.Workspace with
                {
                    Document = document with { State = document.State with { AuxiliaryState = aux } }
                }
            };
            byte[] bootstrapBytes = WorkspaceContinuationCodec.Encode(new(snapshotWithAux,
                WorkspaceContinuationSnapshotDigest.Compute(snapshotWithAux)),
                WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes);
            Directory.Delete(State, true); Reopen();
            var bootstrap = new WorkspaceContinuationRestoreService(Store, Owners, Resolver(), Queries,
                new XmlLifeModulesCatalogService(Path.Combine(Sources, "data", "lifemodules.xml")),
                WorkspacePrivateRuleSourceClosureBinding.MaximumContinuationBytes);
            try
            {
                using var bootstrapReview = bootstrap.Review(Owners.Capture(), bootstrapBytes);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, bootstrapReview.Result.Outcome,
                    string.Join(",", bootstrapReview.Result.Blockers ?? []));
                var bootstrapResult = bootstrap.Confirm(bootstrapReview, true);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, bootstrapResult.Outcome,
                    string.Join(",", bootstrapResult.Blockers ?? []));
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, bootstrap.Confirm(bootstrapReview, true).Outcome);
            }
            finally { CryptographicOperations.ZeroMemory(bootstrapBytes); }
            Assert.AreEqual(JsonSerializer.Serialize(aux), JsonSerializer.Serialize(Store.Get(Id).Value!.Document.AuxiliaryState));
            var closure = Export();
            // A test-owned, genuinely signed history prefix is installed through
            // Core's normal import in a fresh store; no persisted record is edited.
            if (authenticatedHistory)
            {
                var d = WorkspacePrivateRuleSourceClosure.DecodeForRestore(closure.ExportPrivateBytes(), closure.Binding, Queries, default);
                var snapshot = d.Snapshot.Workspace;
                var doc = snapshot.Document;
                var state = new WorkspaceHistoricalState(doc.Format.ToString(), JsonSerializer.Serialize(doc.PayloadEnvelope),
                    JsonSerializer.Serialize(doc.AuxiliaryState), CharacterCareerMartialAcquisitionTransaction.SourceDigest(doc),
                    WorkspaceDocumentAuxiliaryStateDigest.Compute(doc.AuxiliaryState), CharacterCareerMartialAcquisitionTransaction.Hash(doc.Content));
                var trust = new FileWorkspaceStore.OriginTrustDomain(Trust);
                var observation = new WorkspaceHistoricalObservation(1, trust.OriginId, Owners.Current.NormalizedValue, true,
                    Id.Value, Guid.NewGuid().ToString("N"), "", snapshot.ContentRevision, snapshot.ContentRevision,
                    snapshot.SavedRevision, snapshot.SavedRevision, state, state,
                    d.CurrentEnvironment!.CaptureDigest, d.CurrentEnvironment.CaptureDigest, null);
                closure = WorkspacePrivateRuleSourceClosure.Create(d.Snapshot, d.Captures.Values.Select(c => c.ExportPrivateInputs()).ToArray(),
                    d.Campaigns, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, default, d.CurrentEnvironment,
                    new([trust.Sign(observation)]));
                Directory.Delete(State, true); Reopen();
                using var imported = Service.Review(Owners.Capture(), closure.ExportPrivateBytes(), closure.Binding);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, imported.Result.Outcome,
                    string.Join(",", imported.Result.Blockers ?? []));
                var result = Service.Confirm(imported, true);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, string.Join(",", result.Blockers ?? []));
            }
            else
            {
                var bridge = new WorkspaceRoamingRuleEnvironmentBridge(Store, Owners, Resolver(), Queries);
                using var exported = bridge.Export(Owners.Capture(), Id);
                using var review = bridge.ReviewLocalCapture(Owners.Capture(), exported);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
                    string.Join(",", review.Result.Blockers ?? []));
                var result = bridge.ConfirmLocalCapture(review);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, string.Join(",", result.Blockers ?? []));
            }
        }
        internal FileSystemCharacterSourceDataResolver Resolver() => new(new FileSystemContentOverlayCatalogService(Sources, Sources, null));
        internal void StageAmend() => CopyXml(Path.Combine(_fixture, "amended", "customdata"), Path.Combine(Sources, "customdata"));
        internal WorkspacePrivateRuleSourceClosure Export() => Store.ExportCurrentRuleEnvironmentClosure(Owners, Owners.Capture(), Id,
            WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, Resolver(), Chummer5aLegacyImportCompatibility.ProfileV2);
        // Exercise the existing production constructor with the same faults, queries and custody.
        // The public factory cannot combine fault injection with an explicit trust domain.
        internal FileWorkspaceStore CreateFaultingStore(IFileWorkspaceStoreFaultInjector fault)
        {
            Assert.IsTrue(Path.IsPathFullyQualified(State));
            var constructor = typeof(FileWorkspaceStore).GetConstructor(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                binder: null,
                types: [typeof(string), typeof(IFileWorkspaceStoreFaultInjector), typeof(ICharacterFileQueries),
                    typeof(FileWorkspaceStore.OriginTrustDomain)],
                modifiers: null);
            Assert.IsNotNull(constructor, "The retained-source production constructor must exist.");
            var trust = new FileWorkspaceStore.OriginTrustDomain(Trust);
            var store = (FileWorkspaceStore)constructor!.Invoke([State, fault, Queries, trust]);
            Assert.AreSame(trust, store.WorkspaceOriginTrust);
            return store;
        }
        internal void Reopen()
        {
            Store = FileWorkspaceStore.CreateWithRetainedRuleSources(State, Queries, new FileWorkspaceStore.OriginTrustDomain(Trust));
            Service = new(Store, Owners, Queries, reviewLifetime: TimeSpan.FromMinutes(30));
        }
        internal decimal Quote() => Store.WithCurrentRuleEnvironment(Owners, Owners.Capture(), Id, (saved, resolver) =>
        {
            var gear = Guid.Parse("22222222-2222-2222-2222-222222222222");
            Assert.IsTrue(CharacterCareerGearPurchaseTransaction.TryRead(Owners.Current, saved, gear, resolver,
                CharacterCareerGearPurchaseTransaction.Hash("amend9976-store-fixture"), out var snapshot, out var error), error);
            var request = new CharacterCareerGearPurchaseRequest(Id, Guid.NewGuid(), gear, 0, 1m,
                new DateTime(2026, 11, 23, 12, 0, 0, DateTimeKind.Unspecified));
            Assert.IsTrue(CharacterCareerGearPurchaseTransaction.TryPreview(snapshot!, request, out var preview, out error), error);
            return preview!.Quote.TotalCost;
        });
        public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
        private static void CopyXml(string source, string destination)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*.xml", SearchOption.AllDirectories))
            {
                string target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true);
            }
        }
    }
}
