using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.BuildGhost;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Explain;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Owners;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
[SupportedOSPlatform("linux")]
public sealed class PrivateWorkspaceRuleRuntimeTests
{
    private const int MaximumBytes = 512 * 1024;
    private const string SettingsId = "223a11ff-80e0-428b-89a9-6ef1c243b8b6";
    private const string SourceId = "50000000-0000-0000-0000-000000000001";
    private const string SubjectId = "70000000-0000-0000-0000-000000000001";
    private const string SecondSubjectId = "70000000-0000-0000-0000-000000000002";
    private const string QualityName = "Grounded Quality";
    private const string PrivateMarker = "PRIVATE-CONTINUATION-SENTINEL";
    private static readonly OwnerScope Owner = new("private-owner-a");
    private static readonly CharacterWorkspaceId WorkspaceId = new("private-quality-explain");
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly UnixFileMode PrivateDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private static readonly UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    [TestInitialize]
    public void Require_supported_native_scratch_platform()
    {
        if (!OperatingSystem.IsLinux())
            Assert.Inconclusive("The request-private scratch allocator intentionally supports Linux only.");
    }

    [TestMethod]
    public void Real_export_codec_restore_and_query_preserve_core_revisions_sources_and_original_store()
    {
        using Fixture fixture = new();
        byte[] carrierBefore = fixture.Carrier.ToArray();
        FileTree sourceBefore = FileTree.Capture(fixture.SourceRoot);
        FileTree scratchBefore = FileTree.Capture(fixture.ScratchRoot);
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        string child = fixture.SingleScratchChild();
        Assert.AreEqual(child, runtime.ScratchDirectoryPath);
        CollectionAssert.AreEquivalent(new[] { "OwnerStamp", "WorkspaceId", "ContentRevision", "SavedRevision", "RestoreReceipt" },
            typeof(PrivateWorkspaceRuleRuntime).GetProperties().Select(property => property.Name).ToArray());
        OwnerContextStamp stamp = runtime.OwnerStamp;
        Assert.AreEqual(Owner, stamp.Owner);
        Assert.IsFalse(stamp.Owner.IsLocalSingleUser);
        Assert.IsTrue(stamp.IsValid);
        Assert.AreNotEqual(fixture.SourceOwner.Capture().AuthorityInstanceId, stamp.AuthorityInstanceId);
        Assert.AreEqual(WorkspaceId, runtime.WorkspaceId);
        Assert.AreEqual(2L, runtime.ContentRevision);
        Assert.AreEqual(1L, runtime.SavedRevision);

        WorkspaceContinuationRestoreReceipt receipt = runtime.RestoreReceipt;
        Assert.AreNotEqual(Guid.Empty, receipt.OperationId);
        Assert.AreEqual(fixture.Exported.SnapshotDigest, receipt.SnapshotDigest);
        Assert.AreEqual(2L, receipt.ContentRevision);
        Assert.AreEqual(1L, receipt.SavedRevision);
        Assert.AreEqual(Now, receipt.RestoredAtUtc);
        Assert.AreEqual(64, receipt.AdmissionDigest.Length);
        Assert.IsTrue(receipt.AdmissionDigest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'));
        Assert.IsTrue(CharacterCreationQualitiesRules.IsCanonicalDigest(receipt.SourceDigest));
        Assert.IsFalse(string.IsNullOrWhiteSpace(receipt.IncarnationId));

        // Test-side cold observation uses another real accessor/store, never a writable
        // capability exposed by the runtime. Compare the complete exported carrier.
        FileWorkspaceStore coldStore = new(child);
        using RequestOwnerContextAccessor coldOwner = new(Owner);
        WorkspaceContinuationExport cold = Export(coldStore, coldOwner);
        Assert.AreEqual(fixture.Exported.SnapshotDigest, cold.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(fixture.Exported.Snapshot), JsonSerializer.Serialize(cold.Snapshot));
        WorkspaceStoredDocument restored = coldStore.Get(Owner, WorkspaceId).Value!;
        Assert.AreEqual(receipt, restored.LocalHistory!.LastRestore);
        Assert.AreEqual(receipt.IncarnationId, restored.LocalHistory.IncarnationId);
        Assert.AreEqual(2L, restored.LocalHistory.ImportedThroughRevision);
        Assert.AreEqual(receipt.SnapshotDigest, restored.LocalHistory.ImportedSnapshotDigest);

        FileTree queryBefore = FileTree.Capture(child);
        WorkspaceRuleQuestionRequest request = Request();
        WorkspaceRuleQuestionResult result = runtime.Resolve(stamp, request);
        AssertResolved(result);
        WorkspaceRuleQuestionBinding binding = result.Binding!;
        Assert.AreEqual(Owner.Value, binding.OwnerId);
        Assert.IsFalse(binding.TrustedLocalOwner);
        Assert.AreEqual(stamp.AuthorityInstanceId, binding.OwnerAuthorityInstanceId);
        Assert.AreEqual(stamp.TransitionRevision, binding.OwnerTransitionRevision);
        Assert.AreEqual(WorkspaceId, binding.WorkspaceId);
        Assert.AreEqual(2L, binding.ContentRevision);
        Assert.AreEqual(1L, binding.SavedRevision);
        Assert.AreEqual(CharacterCreationBootstrapActivationIntegrity.ComputeDocumentDigest(restored.Document),
            binding.WorkspaceDocumentDigest);
        ICharacterSourceDataContext context = fixture.Sources.TryCreateContext(restored.Document.Content)!;
        Assert.IsTrue(context.TryResolveCreationSourceProfile(out CharacterCreationSourceProfileAuthority profile));
        Assert.IsTrue(context.TryResolveQualityLevelSource(SourceId, QualityName, out CharacterQualityLevelSource source));
        Assert.AreEqual(profile.RawProfileInputsDigest, binding.SourceProfileDigest);
        Assert.AreEqual(source.SourceNodeDigest, binding.SourceNodeDigest);
        Assert.AreEqual(SettingsId, binding.SettingsProfileId);
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeEngineFingerprint(binding.ExecutingModules), binding.EngineFingerprint);
        foreach (Type type in new[]
                 {
                     typeof(WorkspaceRuleQuestionService), typeof(Sr5WorkspaceCodec),
                     typeof(FileSystemCharacterSourceDataResolver), typeof(FileWorkspaceStore),
                     typeof(RequestOwnerContextAccessor)
                 })
        {
            WorkspaceRuleExecutingModule module = binding.ExecutingModules.Single(item => item.ImplementationType == type.FullName);
            Assert.AreEqual(type.Assembly.GetName().Name, module.AssemblyName);
            Assert.AreEqual(type.Module.ModuleVersionId, module.ModuleVersionId);
        }
        Assert.AreEqual(JsonSerializer.Serialize(result), JsonSerializer.Serialize(runtime.Resolve(stamp, request)));
        queryBefore.AssertUnchanged(child);
        sourceBefore.AssertUnchanged(fixture.SourceRoot);
        CollectionAssert.AreEqual(carrierBefore, fixture.Carrier);
        runtime.Dispose();
        scratchBefore.AssertUnchanged(fixture.ScratchRoot);
    }

    [TestMethod]
    public void Same_owner_runtimes_are_distinct_and_cannot_admit_each_others_stamp_or_delete_each_others_state()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime first = fixture.Create();
        string firstChild = fixture.SingleScratchChild();
        using PrivateWorkspaceRuleRuntime second = fixture.Create();
        string secondChild = Directory.GetDirectories(fixture.ScratchRoot).Single(path => path != firstChild);
        OwnerContextStamp firstStamp = first.OwnerStamp;
        OwnerContextStamp secondStamp = second.OwnerStamp;
        Assert.AreEqual(firstStamp.Owner, secondStamp.Owner);
        Assert.AreNotEqual(firstStamp.AuthorityInstanceId, secondStamp.AuthorityInstanceId);
        Assert.AreNotEqual(firstChild, secondChild);
        AssertUnresolved(second.Resolve(firstStamp, Request()));
        AssertUnresolved(first.Resolve(secondStamp, Request()));
        first.Dispose();
        Assert.IsFalse(Directory.Exists(firstChild));
        Assert.IsTrue(Directory.Exists(secondChild));
        AssertResolved(second.Resolve(secondStamp, Request()));
        second.Dispose();
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Full_owner_stamp_rejection_preserves_canonical_unresolved_results_and_the_valid_runtime()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        FileTree before = FileTree.Capture(runtime.ScratchDirectoryPath);
        foreach (OwnerContextStamp invalid in new[]
                 {
                     stamp with { Owner = new OwnerScope("another-owner") },
                     stamp with { TransitionRevision = stamp.TransitionRevision + 1 },
                     default
                 })
        {
            WorkspaceRuleQuestionResult result = runtime.Resolve(invalid, Request());
            Assert.AreEqual(WorkspaceRuleQuestionSchemas.ResultV1, result.Schema);
            Assert.AreEqual(WorkspaceRuleQuestionStatuses.Unresolved, result.Status);
            AssertUnresolved(result);
            Assert.AreEqual(stamp, runtime.OwnerStamp);
        }
        before.AssertUnchanged(runtime.ScratchDirectoryPath);
        AssertResolved(runtime.Resolve(stamp, Request()));
    }

    [TestMethod]
    public void Closed_runtime_rejects_every_observation_and_query_and_disposal_is_idempotent()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        runtime.Dispose();
        AssertClosed(runtime, stamp);
        runtime.Dispose();
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Query_cannot_substitute_workspace_or_core_revision_metadata()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        string child = fixture.SingleScratchChild();
        FileTree before = FileTree.Capture(child);
        foreach (WorkspaceRuleQuestionRequest request in new[]
                 {
                     Request() with { WorkspaceId = new("another-workspace") },
                     Request() with { ExpectedContentRevision = 1 },
                     Request() with { ExpectedContentRevision = 3 }
                 })
            Assert.ThrowsExactly<ArgumentException>(() => runtime.Resolve(stamp, request));
        before.AssertUnchanged(child);
        AssertResolved(runtime.Resolve(stamp, Request()));
    }

    [TestMethod]
    public void Missing_explicit_confirmation_fails_without_scratch_allocation_or_private_details()
    {
        using Fixture fixture = new();
        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Factory.Create(Owner, fixture.Carrier, explicitlyConfirmed: false));
        AssertSafeFailure(failure);
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void A_valid_full_carrier_for_another_owner_is_not_relabelled_or_restored()
    {
        using Fixture fixture = new();
        byte[] before = fixture.Carrier.ToArray();
        InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Factory.Create(new("another-owner"), fixture.Carrier, explicitlyConfirmed: true));
        AssertSafeFailure(failure);
        CollectionAssert.AreEqual(before, fixture.Carrier);
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Malformed_or_digest_damaged_full_carriers_fail_closed_and_leave_no_private_child()
    {
        using Fixture fixture = new();
        byte[][] candidates =
        [
            [], "null"u8.ToArray(), "{}"u8.ToArray(), "[]"u8.ToArray(),
            Encoding.UTF8.GetBytes("{\"Snapshot\":\"" + PrivateMarker + "\"}"),
            fixture.Carrier[..^1],
            Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(fixture.Carrier).Replace(PrivateMarker, "CHANGED-PRIVATE-CONTINUATION", StringComparison.Ordinal))
        ];
        string? safeMessage = null;
        foreach (byte[] candidate in candidates)
        {
            byte[] before = candidate.ToArray();
            InvalidOperationException failure = Assert.ThrowsExactly<InvalidOperationException>(() =>
                fixture.Factory.Create(Owner, candidate, explicitlyConfirmed: true));
            AssertSafeFailure(failure);
            safeMessage ??= failure.Message;
            Assert.AreEqual(safeMessage, failure.Message);
            CollectionAssert.AreEqual(before, candidate);
            fixture.AssertScratchEmpty();
        }
    }

    [TestMethod]
    public void Oversized_carrier_is_rejected_without_allocation_or_mutating_the_callers_buffer()
    {
        using Fixture fixture = new();
        byte[] candidate = new byte[MaximumBytes + 1];
        Array.Fill(candidate, (byte)' ');
        fixture.Carrier.CopyTo(candidate, 0);
        byte[] before = candidate.ToArray();
        AssertSafeFailure(Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Factory.Create(Owner, candidate, explicitlyConfirmed: true)));
        CollectionAssert.AreEqual(before, candidate);
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Exact_wire_byte_boundary_accepts_the_real_carrier_without_changing_its_caller_buffer()
    {
        using Fixture fixture = new();
        byte[] candidate = new byte[MaximumBytes];
        Array.Fill(candidate, (byte)' ');
        fixture.Carrier.CopyTo(candidate, 0);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(candidate, MaximumBytes, out _));
        byte[] before = candidate.ToArray();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Factory.Create(Owner, candidate, explicitlyConfirmed: true);
        AssertResolved(runtime.Resolve(runtime.OwnerStamp, Request()));
        CollectionAssert.AreEqual(before, candidate);
        runtime.Dispose();
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Invalid_and_hidden_trusted_local_owners_are_rejected_before_allocation()
    {
        using Fixture fixture = new();
        foreach (OwnerScope owner in new[]
                 {
                     default, OwnerScope.LocalSingleUser, new OwnerScope("local-single-user"),
                     OwnerScope.LocalSingleUser with { Value = Owner.Value },
                     new OwnerScope("PRIVATE-OWNER-A"), new OwnerScope("private-owner-a ")
                 })
        {
            Assert.ThrowsExactly<ArgumentException>(() => fixture.Factory.Create(owner, fixture.Carrier, true));
            fixture.AssertScratchEmpty();
        }
    }

    [TestMethod]
    public void Already_canceled_creation_has_no_scratch_effect_and_does_not_mutate_the_carrier()
    {
        using Fixture fixture = new();
        using CancellationTokenSource canceled = new();
        canceled.Cancel();
        byte[] before = fixture.Carrier.ToArray();
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            fixture.Factory.Create(Owner, fixture.Carrier, true, canceled.Token));
        CollectionAssert.AreEqual(before, fixture.Carrier);
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Cancellation_observed_after_real_scratch_allocation_cleans_the_private_state()
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        bool observedAllocation = false;
        CallbackClock clock = new(() =>
        {
            observedAllocation |= Directory.GetDirectories(fixture.ScratchRoot).Length == 1;
            cancellation.Cancel();
            return Now;
        });
        PrivateWorkspaceRuleRuntimeFactory factory = fixture.CreateFactory(clock: clock);
        Assert.ThrowsExactly<OperationCanceledException>(() =>
            factory.Create(Owner, fixture.Carrier, true, cancellation.Token));
        Assert.IsTrue(observedAllocation, "The actual restore clock must observe an allocated private store, not a preflight denial.");
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void A_codec_valid_but_ambiguous_character_is_rejected_by_real_review_and_scratch_is_cleaned()
    {
        using Fixture fixture = new();
        WorkspaceStoredDocument original = fixture.ReadSource();
        WorkspaceDocument ambiguous = original.Document with
        {
            State = original.Document.State with
            {
                Payload = original.Document.Content.Replace("<created>False</created>",
                    "<created>False</created><created>False</created>", StringComparison.Ordinal)
            }
        };
        Assert.IsTrue(fixture.SourceStore.ReplaceWorkspaceDocument(Owner, WorkspaceId, original.ContentRevision, ambiguous).Success);
        byte[] carrier = WorkspaceContinuationCodec.Encode(Export(fixture.SourceStore, fixture.SourceOwner), MaximumBytes);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(carrier, MaximumBytes, out _));
        FileTree sourceBefore = FileTree.Capture(fixture.SourceRoot);
        AssertSafeFailure(Assert.ThrowsExactly<InvalidOperationException>(() =>
            fixture.Factory.Create(Owner, carrier, true)));
        sourceBefore.AssertUnchanged(fixture.SourceRoot);
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Factory_requires_an_explicit_existing_absolute_directory_and_never_creates_the_parent()
    {
        using Fixture fixture = new();
        string missing = Path.Combine(fixture.Root, "missing-provisioned-root");
        string notDirectory = Path.Combine(fixture.Root, "root-is-a-file");
        File.WriteAllText(notDirectory, PrivateMarker);
        foreach (string root in new[] { "", " ", "relative-private-root", missing, notDirectory, fixture.ScratchRoot + Path.DirectorySeparatorChar })
            Assert.ThrowsExactly<ArgumentException>(() => fixture.CreateFactory(root));
        Assert.IsFalse(Directory.Exists(missing));
        Assert.AreEqual(PrivateMarker, File.ReadAllText(notDirectory));
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Factory_rejects_a_linked_root_or_linked_ancestor_without_touching_the_real_target()
    {
        using Fixture fixture = new();
        string direct = Path.Combine(fixture.Root, "linked-root");
        string parent = Path.Combine(fixture.Root, "linked-parent");
        FileTree before = FileTree.Capture(fixture.ScratchRoot);
        Directory.CreateSymbolicLink(direct, fixture.ScratchRoot);
        Directory.CreateSymbolicLink(parent, fixture.Root);
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => fixture.CreateFactory(direct));
            Assert.ThrowsExactly<ArgumentException>(() => fixture.CreateFactory(Path.Combine(parent, "scratch")));
            before.AssertUnchanged(fixture.ScratchRoot);
        }
        finally
        {
            Directory.Delete(direct);
            Directory.Delete(parent);
        }
    }

    [TestMethod]
    public void Factory_rejects_nonprivate_root_permissions_instead_of_repairing_the_provisioned_root()
    {
        using Fixture fixture = new();
        UnixFileMode shared = PrivateDirectoryMode | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        File.SetUnixFileMode(fixture.ScratchRoot, shared);
        try
        {
            Assert.ThrowsExactly<ArgumentException>(() => fixture.CreateFactory());
            Assert.AreEqual(shared, File.GetUnixFileMode(fixture.ScratchRoot));
            fixture.AssertScratchEmpty();
        }
        finally
        {
            File.SetUnixFileMode(fixture.ScratchRoot, PrivateDirectoryMode);
        }
    }

    [TestMethod]
    public void Scratch_child_is_opaque_private_and_contains_only_private_files_under_the_explicit_root()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        string child = fixture.SingleScratchChild();
        Assert.AreEqual(fixture.ScratchRoot, Path.GetDirectoryName(child));
        string component = Path.GetFileName(child);
        Assert.IsFalse(component.Contains(Owner.Value, StringComparison.Ordinal));
        Assert.IsFalse(component.Contains(WorkspaceId.Value, StringComparison.Ordinal));
        foreach (string directory in Directory.GetDirectories(child, "*", SearchOption.AllDirectories).Prepend(child))
            Assert.AreEqual(PrivateDirectoryMode, File.GetUnixFileMode(directory), directory);
        string[] files = Directory.GetFiles(child, "*", SearchOption.AllDirectories);
        Assert.IsTrue(files.Length > 0);
        foreach (string file in files)
            Assert.AreEqual(PrivateFileMode, File.GetUnixFileMode(file), file);
        runtime.Dispose();
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Cleanup_rejects_a_substituted_child_link_and_never_deletes_the_unrelated_target()
    {
        using Fixture fixture = new();
        PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        string child = fixture.SingleScratchChild();
        string parked = Path.Combine(fixture.Root, "parked-owned-child");
        string unrelated = Path.Combine(fixture.Root, "unrelated-cleanup-target");
        Directory.CreateDirectory(unrelated, PrivateDirectoryMode);
        string sentinel = Path.Combine(unrelated, "sentinel");
        File.WriteAllText(sentinel, PrivateMarker);
        Directory.Move(child, parked);
        Directory.CreateSymbolicLink(child, unrelated);
        try
        {
            AssertSafeFailure(Assert.ThrowsExactly<IOException>(runtime.Dispose));
            AssertClosed(runtime, stamp);
            Assert.AreEqual(PrivateMarker, File.ReadAllText(sentinel));
            Assert.IsTrue(Directory.Exists(parked));
        }
        finally
        {
            Directory.Delete(child);
            Directory.Move(parked, child);
            runtime.Dispose();
        }
        Assert.AreEqual(PrivateMarker, File.ReadAllText(sentinel));
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Filesystem_blocked_query_and_queued_query_are_discarded_while_every_disposer_waits_for_real_cleanup()
    {
        using Fixture fixture = new();
        PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        string child = runtime.ScratchDirectoryPath;
        FileStream held = fixture.HoldWorkspaceLock(child);
        Worker? query = null;
        Worker? queued = null;
        Worker? firstDisposer = null;
        Worker? secondDisposer = null;
        try
        {
            query = new Worker(() => Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.Resolve(stamp, Request())));
            query.WaitUntilBlocked();
            queued = new Worker(() => Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.Resolve(stamp, Request())));
            queued.WaitUntilBlocked();
            firstDisposer = new Worker(runtime.Dispose);
            firstDisposer.WaitUntilBlocked();
            secondDisposer = new Worker(runtime.Dispose);
            secondDisposer.WaitUntilBlocked();
            AssertClosed(runtime, stamp);
            Assert.IsFalse(query.IsCompleted);
            Assert.IsFalse(queued.IsCompleted);
            Assert.IsFalse(firstDisposer.IsCompleted);
            Assert.IsFalse(secondDisposer.IsCompleted);
            Assert.IsTrue(Directory.Exists(child), "No cleanup may unlink the store while its real query lease is live.");
        }
        finally
        {
            held.Dispose();
            JoinAll(query, queued, firstDisposer, secondDisposer);
            runtime.Dispose();
        }
        fixture.AssertScratchEmpty();
    }

    [TestMethod]
    public void Cancellation_during_a_real_filesystem_blocked_query_discards_the_result_without_ending_the_runtime()
    {
        using Fixture fixture = new();
        using PrivateWorkspaceRuleRuntime runtime = fixture.Create();
        using CancellationTokenSource cancellation = new();
        OwnerContextStamp stamp = runtime.OwnerStamp;
        string child = runtime.ScratchDirectoryPath;
        FileStream held = fixture.HoldWorkspaceLock(child);
        Worker? query = null;
        try
        {
            query = new Worker(() => Assert.ThrowsExactly<OperationCanceledException>(() =>
                runtime.Resolve(stamp, Request(), cancellation.Token)));
            query.WaitUntilBlocked();
            cancellation.Cancel();
            Assert.IsFalse(query.IsCompleted, "Cancellation cannot preempt the synchronous filesystem operation.");
        }
        finally
        {
            held.Dispose();
            JoinAll(query);
        }
        Assert.AreEqual(stamp, runtime.OwnerStamp);
        AssertResolved(runtime.Resolve(stamp, Request()));
        runtime.Dispose();
        fixture.AssertScratchEmpty();
    }

    private static WorkspaceRuleQuestionRequest Request() => new(
        WorkspaceId, 2, "sr5", WorkspaceRuleQuestionIntents.QualityLevel, SubjectId, "en");

    private static WorkspaceContinuationExport Export(FileWorkspaceStore store, RequestOwnerContextAccessor owner)
    {
        var result = new WorkspaceContinuationExportService(store, owner).Export(owner.Capture(), WorkspaceId);
        Assert.IsTrue(result.Success, result.Error);
        Assert.IsNotNull(result.Value);
        return result.Value;
    }

    private static void AssertResolved(WorkspaceRuleQuestionResult result)
    {
        Assert.IsTrue(result.Resolved, result.FailureReason);
        Assert.AreEqual(2, result.Level);
        Assert.AreEqual(3, result.MaximumLevel);
        Assert.IsNotNull(result.Binding);
        WorkspaceRuleSourceAnchor anchor = result.SourceAnchors.Single();
        Assert.AreEqual("SG", anchor.SourceBook);
        Assert.AreEqual(224, anchor.Page);
        Assert.AreEqual(SourceId, anchor.QualitySourceId);
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), result.ResultDigest);
        Assert.AreEqual(WorkspaceRuleQuestionIntents.QualityLevelRuleId, result.Explanation.RuleId);
    }

    private static void AssertUnresolved(WorkspaceRuleQuestionResult result)
    {
        Assert.IsFalse(result.Resolved);
        Assert.IsNull(result.Binding);
        Assert.IsNull(result.Level);
        Assert.IsNull(result.MaximumLevel);
        Assert.HasCount(0, result.SourceAnchors);
        Assert.HasCount(0, result.Explanation.SourceAnchorIds);
        Assert.AreEqual(WorkspaceRuleQuestionIntegrity.ComputeResultDigest(result), result.ResultDigest);
    }

    private static void AssertClosed(PrivateWorkspaceRuleRuntime runtime, OwnerContextStamp previous)
    {
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = runtime.OwnerStamp);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = runtime.WorkspaceId);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = runtime.ContentRevision);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = runtime.SavedRevision);
        Assert.ThrowsExactly<ObjectDisposedException>(() => _ = runtime.RestoreReceipt);
        Assert.ThrowsExactly<ObjectDisposedException>(() => runtime.Resolve(previous, Request()));
    }

    private static void AssertSafeFailure(Exception failure)
    {
        Assert.IsNull(failure.InnerException);
        Assert.IsFalse(failure.Message.Contains(PrivateMarker, StringComparison.Ordinal));
        Assert.IsFalse(failure.Message.Contains(Owner.Value, StringComparison.Ordinal));
        Assert.IsFalse(failure.Message.Contains("<character>", StringComparison.Ordinal));
    }

    private static void JoinAll(params Worker?[] workers)
    {
        List<Exception> failures = [];
        foreach (Worker? worker in workers)
        {
            if (worker is null) continue;
            try { worker.Join(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException(failures);
    }

    private sealed class Worker
    {
        private readonly Thread _thread;
        private int _started;
        private ExceptionDispatchInfo? _failure;
        private int _completed;
        public bool IsCompleted => Volatile.Read(ref _completed) != 0;

        public Worker(Action work)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    Volatile.Write(ref _started, 1);
                    work();
                }
                catch (Exception failure) { _failure = ExceptionDispatchInfo.Capture(failure); }
                finally { Volatile.Write(ref _completed, 1); }
            }) { IsBackground = true };
            _thread.Start();
            Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref _started) != 0, Bound), "The bounded worker did not start.");
        }

        public void WaitUntilBlocked()
        {
            Assert.IsTrue(SpinWait.SpinUntil(() => IsCompleted
                || (_thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Bound), "The real operation did not reach its bounded wait.");
            if (IsCompleted)
            {
                Join();
                Assert.Fail("The operation completed before reaching the required real lock boundary.");
            }
        }

        public void Join()
        {
            Assert.IsTrue(_thread.Join(Bound), "The bounded worker failed to finish after releasing the real filesystem lock.");
            _failure?.Throw();
        }
    }

    private sealed class CallbackClock(Func<DateTimeOffset> read) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => read();
    }

    private sealed class FileTree(string[] directories, FileObservation[] files)
    {
        public static FileTree Capture(string root) => new(
            Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(root, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray(),
            Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FileObservation(Path.GetRelativePath(root, path), File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path))).ToArray());

        public void AssertUnchanged(string root)
        {
            FileTree current = Capture(root);
            CollectionAssert.AreEqual(directories, current.Directories);
            CollectionAssert.AreEqual(files.Select(file => file.Path).ToArray(), current.Files.Select(file => file.Path).ToArray());
            for (int index = 0; index < files.Length; index++)
            {
                CollectionAssert.AreEqual(files[index].Bytes, current.Files[index].Bytes, files[index].Path);
                Assert.AreEqual(files[index].LastWriteUtc, current.Files[index].LastWriteUtc, files[index].Path);
            }
        }

        private string[] Directories => directories;
        private FileObservation[] Files => files;
    }

    private sealed record FileObservation(string Path, byte[] Bytes, DateTime LastWriteUtc);

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("chummer-private-rule-runtime-tests-").FullName;
        public string SourceRoot { get; }
        public string ScratchRoot { get; }
        public RequestOwnerContextAccessor SourceOwner { get; } = new(Owner);
        public FileWorkspaceStore SourceStore { get; }
        public FileSystemCharacterSourceDataResolver Sources { get; }
        public Sr5WorkspaceCodec Codec { get; }
        public WorkspaceContinuationExport Exported { get; }
        public byte[] Carrier { get; }
        public PrivateWorkspaceRuleRuntimeFactory Factory { get; }
        private string Sentinel => Path.Combine(ScratchRoot, "unrelated-sentinel");

        public Fixture()
        {
            SourceRoot = Path.Combine(Root, "source-store");
            ScratchRoot = Path.Combine(Root, "scratch");
            try
            {
                File.SetUnixFileMode(Root, PrivateDirectoryMode);
                Directory.CreateDirectory(ScratchRoot, PrivateDirectoryMode);
                File.WriteAllText(Sentinel, PrivateMarker);
                string data = Path.Combine(Root, "data");
                Directory.CreateDirectory(data, PrivateDirectoryMode);
                File.WriteAllText(Path.Combine(data, "settings.xml"), $"<chummer><settings><setting><id>{SettingsId}</id>"
                    + "<buildmethod>Priority</buildmethod><buildpoints>25</buildpoints><books><book>SR5</book><book>SG</book></books>"
                    + "<customdatadirectorynames/></setting></settings></chummer>");
                File.WriteAllText(Path.Combine(data, "books.xml"), "<chummer><books><book><code>SR5</code><name>Core Rulebook</name></book>"
                    + "<book><code>SG</code><name>Street Grimoire</name></book></books></chummer>");
                File.WriteAllText(Path.Combine(data, "qualities.xml"), $"<chummer><qualities><quality><id>{SourceId}</id><name>{QualityName}</name>"
                    + "<category>Positive</category><limit>3</limit><source>SG</source><page>224</page></quality></qualities></chummer>");
                File.WriteAllText(Path.Combine(data, "lifemodules.xml"),
                    "<chummer><stages><stage order=\"0\">Nationality</stage></stages><modules/></chummer>");
                Sources = new(new FileSystemContentOverlayCatalogService(Root, Root, null));
                CharacterFileService files = new();
                Codec = new(new XmlCharacterFileQueries(files),
                    new XmlCharacterSectionQueries(new CharacterSectionService(Sources)), new XmlCharacterMetadataCommands(files));
                string xml = "<character><name>Grounded Runner</name><alias>Before</alias><metatype>Human</metatype>"
                    + $"<settings>{SettingsId}</settings><buildmethod>Priority</buildmethod><createdversion>5.225.0</createdversion>"
                    + $"<appversion>5.225.0</appversion><created>False</created><karma>0</karma><nuyen>0</nuyen><notes>{PrivateMarker}</notes><qualities>"
                    + Quality(SubjectId) + Quality(SecondSubjectId) + "</qualities></character>";
                WorkspacePayloadEnvelope envelope = new("sr5", Sr5WorkspaceCodec.SchemaVersion, Sr5WorkspaceCodec.Sr5PayloadKind, xml);
                Assert.IsTrue(Codec.Validate(envelope).IsValid);
                SourceStore = new(SourceRoot);
                Assert.IsTrue(SourceStore.CreateWorkspaceDocument(Owner, WorkspaceId, new WorkspaceDocument(envelope)).Success);
                Assert.IsTrue(SourceStore.SaveCheckpoint(Owner, WorkspaceId, 1).Success);
                WorkspaceDocument changed = new(envelope with
                {
                    Payload = xml.Replace("<alias>Before</alias>", "<alias>Grounded</alias>", StringComparison.Ordinal)
                });
                Assert.IsTrue(SourceStore.ReplaceWorkspaceDocument(Owner, WorkspaceId, 1, changed).Success);
                Assert.AreEqual(2L, ReadSource().ContentRevision);
                Assert.AreEqual(1L, ReadSource().SavedRevision);
                Exported = Export(SourceStore, SourceOwner);
                Carrier = WorkspaceContinuationCodec.Encode(Exported, MaximumBytes);
                Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(Carrier, MaximumBytes, out _));
                Factory = CreateFactory();
            }
            catch
            {
                SourceOwner.Dispose();
                Directory.Delete(Root, recursive: true);
                throw;
            }
        }

        public PrivateWorkspaceRuleRuntimeFactory CreateFactory(string? scratchRoot = null, TimeProvider? clock = null) =>
            new(scratchRoot ?? ScratchRoot, Root, Root, null, clock ?? new CallbackClock(() => Now));

        public PrivateWorkspaceRuleRuntime Create() => Factory.Create(Owner, Carrier, explicitlyConfirmed: true);

        public string SingleScratchChild() => Directory.GetDirectories(ScratchRoot).Single();

        public void AssertScratchEmpty()
        {
            Assert.HasCount(0, Directory.GetDirectories(ScratchRoot));
            CollectionAssert.AreEqual(new[] { Sentinel }, Directory.GetFiles(ScratchRoot));
            Assert.AreEqual(PrivateMarker, File.ReadAllText(Sentinel));
        }

        public WorkspaceStoredDocument ReadSource()
        {
            WorkspaceStoreReadResult read = SourceStore.Get(Owner, WorkspaceId);
            Assert.IsTrue(read.Success, read.Error);
            Assert.IsNotNull(read.Value);
            return read.Value;
        }

        public FileStream HoldWorkspaceLock(string child)
        {
            string record = Directory.GetFiles(child, WorkspaceId.Value + ".json", SearchOption.AllDirectories).Single();
            return new FileStream(record + ".lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        public void Dispose()
        {
            SourceOwner.Dispose();
            Directory.Delete(Root, recursive: true);
        }

        private static string Quality(string id) => $"<quality><guid>{id}</guid><sourceid>{SourceId}</sourceid><name>{QualityName}</name>"
            + "<qualitytype>Positive</qualitytype><qualitysource>Selected</qualitysource><bp>0</bp><extra/><sourcename/>"
            + "<notes/><source>SR5</source><page>111</page><bonus/></quality>";
    }
}
