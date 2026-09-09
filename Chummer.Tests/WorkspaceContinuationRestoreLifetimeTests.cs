using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceContinuationRestoreLifetimeTests
{
    private const int MaximumBytes = 16 * 1024 * 1024;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Expiry_at_confirmation_or_after_temp_flush_preserves_the_existing_runner(bool afterTempFlush)
    {
        using RestoreFixture fixture = new();
        var before = fixture.CaptureRunner();
        bool flushed = false;
        bool replaced = false;
        fixture.Fault.Callback = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                flushed = true;
                if (afterTempFlush) fixture.Clock.UtcNow = fixture.Review.ExpiresAtUtc;
            }
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) replaced = true;
        };
        if (!afterTempFlush) fixture.Clock.UtcNow = fixture.Review.ExpiresAtUtc;

        var result = fixture.Service.Confirm(fixture.Review, explicitlyConfirmed: true);

        Assert.AreEqual(afterTempFlush, flushed);
        Assert.IsFalse(replaced);
        fixture.AssertDenied(result, afterTempFlush
            ? WorkspaceContinuationRestoreOutcome.Conflict : WorkspaceContinuationRestoreOutcome.ReviewExpired, before);
    }

    [TestMethod]
    [DataRow("before-confirm")]
    [DataRow("after-temp-flush")]
    [DataRow("after-target-replaced")]
    public void Cancellation_respects_the_atomic_commit_boundary(string cancellationStage)
    {
        using RestoreFixture fixture = new();
        using CancellationTokenSource cancellation = new();
        var before = fixture.CaptureRunner();
        bool flushed = false;
        bool replaced = false;
        fixture.Fault.Callback = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                flushed = true;
                if (cancellationStage == "after-temp-flush") cancellation.Cancel();
            }
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced)
            {
                replaced = true;
                if (cancellationStage == "after-target-replaced") cancellation.Cancel();
            }
        };
        if (cancellationStage == "before-confirm") cancellation.Cancel();

        var result = fixture.Service.Confirm(fixture.Review, explicitlyConfirmed: true,
            cancellationToken: cancellation.Token);

        Assert.IsTrue(cancellation.IsCancellationRequested);
        Assert.AreEqual(cancellationStage != "before-confirm", flushed);
        Assert.AreEqual(cancellationStage == "after-target-replaced", replaced);
        if (cancellationStage == "after-target-replaced")
        {
            fixture.AssertApplied(result);
            var recovered = fixture.Service.Recover(fixture.Owner.Capture(), fixture.Id,
                fixture.Review.OperationId, fixture.Review.AdmissionDigest!);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Recovered, recovered.Outcome, Describe(recovered));
            Assert.AreEqual(result.Receipt, recovered.Receipt,
                "Cancellation after the known rename cannot erase the local recovery proof.");
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed,
                fixture.Service.Confirm(fixture.Review, true).Outcome);
        }
        else fixture.AssertDenied(result, WorkspaceContinuationRestoreOutcome.Canceled, before);
    }

    [TestMethod]
    public void Caller_byte_array_mutation_after_review_cannot_change_the_admitted_snapshot()
    {
        using RestoreFixture fixture = new();
        Array.Fill(fixture.CandidateBytes, (byte)' ');
        Assert.IsFalse(WorkspaceContinuationCodec.TryDecodeCandidate(fixture.CandidateBytes,
            MaximumBytes, out _), "The caller's original buffer is now deliberately unusable.");

        var result = fixture.Service.Confirm(fixture.Review, explicitlyConfirmed: true);

        fixture.AssertApplied(result);
        Assert.AreEqual(fixture.Exported.SnapshotDigest, fixture.Review.SnapshotDigest);
    }

    [TestMethod]
    public void Expiry_during_slot_rotation_is_rechecked_before_the_runner_replace()
    {
        using RestoreFixture fixture = new();
        var before = fixture.CaptureRunner();
        string slotPath = fixture.RecordPath + ".slot";
        string reviewedSlot = File.ReadAllText(slotPath);
        Assert.AreEqual(fixture.Review.Result.Target!.SlotGeneration, reviewedSlot);
        bool observedRotation = false;
        bool flushed = false;
        bool replaced = false;
        fixture.Fault.Callback = stage =>
        {
            if (stage == FileWorkspaceStoreFaultStage.AfterTempFileFlushed) flushed = true;
            if (stage == FileWorkspaceStoreFaultStage.AfterTargetReplaced) replaced = true;
        };
        fixture.Clock.ObserveUtcNow = () =>
        {
            if (File.ReadAllText(slotPath) == reviewedSlot) return fixture.Clock.UtcNow;
            observedRotation = true;
            return fixture.Review.ExpiresAtUtc;
        };

        var result = fixture.Service.Confirm(fixture.Review, explicitlyConfirmed: true);
        fixture.Clock.ObserveUtcNow = null;

        Assert.IsTrue(flushed);
        Assert.AreNotEqual(reviewedSlot, File.ReadAllText(slotPath),
            "The slot rotation is the real intervening durable operation, not a simulated runner write.");
        Assert.IsTrue(observedRotation, "Admission lifetime must be observed after the slot's durable rotation.");
        Assert.IsFalse(replaced);
        fixture.AssertDenied(result, WorkspaceContinuationRestoreOutcome.Conflict, before);
    }

    private static string Describe(WorkspaceContinuationRestoreResult result) => JsonSerializer.Serialize(result);

    // Reuses the existing real bootstrap fixture and the same non-reentrant
    // owner/fault contracts as WorkspaceContinuationRestoreTests. Only the clock
    // and cancellation timing are controlled; candidate evaluation is real.
    private sealed class RestoreFixture : IDisposable
    {
        private readonly ReadyContext _source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        private readonly string _directory = Directory.CreateTempSubdirectory("chummer-restore-lifetime-").FullName;
        public CharacterWorkspaceId Id => _source.WorkspaceId;
        public string RecordPath => Path.Combine(_directory, "workspaces", Id.Value + ".json");
        public TestOwner Owner { get; } = new();
        public TestClock Clock { get; } = new();
        public RestoreFault Fault { get; } = new();
        public WorkspaceContinuationExport Exported { get; }
        public byte[] CandidateBytes { get; }
        public WorkspaceContinuationRestoreService Service { get; }
        public WorkspaceContinuationRestoreReview Review { get; }

        public RestoreFixture()
        {
            var original = _source.Store.Get(Id).Value!;
            Assert.IsTrue(_source.Store.ReplaceWorkspaceDocument(Id, original.ContentRevision, original.Document).Success);
            var exported = new WorkspaceContinuationExportService(_source.Store, Owner).Export(Owner.Capture(), Id);
            Assert.IsTrue(exported.Success, exported.Error);
            Assert.IsNotNull(exported.Value);
            Exported = exported.Value;
            CandidateBytes = WorkspaceContinuationCodec.Encode(Exported, MaximumBytes);
            FileWorkspaceStore target = new(_directory, Fault);
            Assert.IsTrue(target.CreateWorkspaceDocument(Id, new WorkspaceDocument(
                "<character><name>Existing lifetime target</name><metatype>Human</metatype><created>False</created></character>",
                "sr5")).Success);
            Service = new(target, Owner, new ObservedResolver(_source.Resolver, Owner), _source.Queries,
                Catalog(), MaximumBytes, Clock, TimeSpan.FromMinutes(5));
            Review = Service.Review(Owner.Capture(), CandidateBytes);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, Review.Result.Outcome, Describe(Review.Result));
            Assert.IsTrue(Review.Result.Target!.Exists);
            Assert.IsTrue(Exported.Snapshot.Workspace.ContentRevision > Review.Result.Target.ContentRevision);
        }

        public (byte[] Bytes, DateTime Timestamp) CaptureRunner() =>
            (File.ReadAllBytes(RecordPath), File.GetLastWriteTimeUtc(RecordPath));

        public void AssertDenied(WorkspaceContinuationRestoreResult result, WorkspaceContinuationRestoreOutcome outcome,
            (byte[] Bytes, DateTime Timestamp) before)
        {
            Assert.AreEqual(outcome, result.Outcome, Describe(result));
            Assert.IsNull(result.Receipt);
            Assert.AreEqual(0, Owner.ActiveLeases);
            CollectionAssert.AreEqual(before.Bytes, File.ReadAllBytes(RecordPath));
            Assert.AreEqual(before.Timestamp, File.GetLastWriteTimeUtc(RecordPath));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, Service.Confirm(Review, true).Outcome);
            var recovered = Service.Recover(Owner.Capture(), Id, Review.OperationId, Review.AdmissionDigest!);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing, recovered.Outcome, Describe(recovered));
            CollectionAssert.AreEqual(before.Bytes, File.ReadAllBytes(RecordPath));
            Assert.AreEqual(before.Timestamp, File.GetLastWriteTimeUtc(RecordPath));
        }

        public void AssertApplied(WorkspaceContinuationRestoreResult result)
        {
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, Describe(result));
            Assert.IsNotNull(result.Receipt);
            Assert.IsFalse(result.ReopenRequired);
            Assert.AreEqual(0, Owner.ActiveLeases);
            var cold = new FileWorkspaceStore(_directory).ReadContinuation(Id);
            Assert.IsTrue(cold.Success, cold.Error);
            Assert.IsNotNull(cold.Value);
            Assert.AreEqual(Exported.SnapshotDigest, WorkspaceContinuationSnapshotDigest.Compute(cold.Value));
            Assert.AreEqual(JsonSerializer.Serialize(Exported.Snapshot), JsonSerializer.Serialize(cold.Value));
            var stored = new FileWorkspaceStore(_directory).Get(Id);
            Assert.IsTrue(stored.Success, stored.Error);
            Assert.AreEqual(result.Receipt, stored.Value!.LocalHistory!.LastRestore);
        }

        public void Dispose()
        {
            Review.Dispose();
            _source.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static XmlLifeModulesCatalogService Catalog()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, "Chummer", "data", "lifemodules.xml");
            if (File.Exists(path)) return new(path);
        }
        throw new DirectoryNotFoundException("Canonical Life Modules data is missing.");
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        public Func<DateTimeOffset>? ObserveUtcNow { get; set; }
        public override DateTimeOffset GetUtcNow() => ObserveUtcNow?.Invoke() ?? UtcNow;
    }

    private sealed class RestoreFault : IFileWorkspaceStoreFaultInjector
    {
        public Action<FileWorkspaceStoreFaultStage>? Callback { get; set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string temporaryPath) => Callback?.Invoke(stage);
    }

    private sealed class ObservedResolver(ICharacterSourceDataResolver inner, TestOwner owner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            Assert.AreEqual(1, owner.ActiveLeases, "Source evaluation must retain the actual owner lease.");
            return inner.TryCreateContext(xml);
        }
    }

    private sealed class TestOwner : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        public int ActiveLeases { get; private set; }
        public OwnerScope Current => OwnerScope.LocalSingleUser;
        public OwnerContextStamp Capture() { lock (_gate) return new(Current, _issuer, 0); }
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases, "Restore must not assume reentrant owner admission.");
            if (!expected.IsValid || expected != new OwnerContextStamp(Current, _issuer, 0))
            {
                Monitor.Exit(_gate); lease = null; return false;
            }
            ActiveLeases++;
            lease = new Lease(this, expected);
            return true;
        }

        private sealed class Lease(TestOwner owner, OwnerContextStamp stamp) : IOwnerContextLease
        {
            private bool _disposed;
            public OwnerContextStamp Stamp => !_disposed ? stamp : throw new ObjectDisposedException(nameof(Lease));
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true; owner.ActiveLeases--; Monitor.Exit(owner._gate);
            }
        }
    }
}
