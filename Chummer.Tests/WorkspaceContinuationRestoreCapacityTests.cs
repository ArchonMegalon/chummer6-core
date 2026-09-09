using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.LifeModules;
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
public sealed class WorkspaceContinuationRestoreCapacityTests
{
    private const int MaximumBytes = 16 * 1024 * 1024;
    private static readonly OwnerScope Owner = new("restore-capacity-owner");

    [TestMethod]
    [DataRow(4096, false)]
    [DataRow(4096, true)]
    [DataRow(4097, false)]
    [DataRow(4097, true)]
    public void Restore_GM_capacity_matches_cold_read_limit_without_committing_an_unreadable_record(
        int receiptCount, bool existingTarget)
    {
        using ReadyContext source = ReadyContext.CreateUnprepared(CharacterCreationBuildMethods.Priority);
        var original = source.Store.ReadContinuation(source.WorkspaceId);
        Assert.IsTrue(original.Success, original.Error);
        string sourceBefore = JsonSerializer.Serialize(source.Store.Get(source.WorkspaceId).Value);
        WorkspaceContinuationSnapshot snapshot = WithUnverifiedHistory(original.Value!, receiptCount);
        var ledger = snapshot.DelegatedGmCharacterEdits.Select(receipt =>
            new DelegatedGmCharacterEditLedgerEntry(receipt.IdempotencyKeySha256, receipt.CommandSha256, receipt)).ToArray();

        // These synthetic entries are explicitly unverified portable history.
        // They obey all actual identity, hash, chronological and auxiliary-state
        // invariants; this test must fail on store capacity, not a malformed DTO.
        Assert.HasCount(receiptCount, ledger);
        Assert.IsTrue(DelegatedGmCharacterEditLedgerValidator.IsValidLedger(Owner, source.WorkspaceId,
            snapshot.Workspace.ContentRevision, ledger));
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(Owner, snapshot));
        string digest = WorkspaceContinuationSnapshotDigest.Compute(snapshot);
        byte[] bytes = WorkspaceContinuationCodec.Encode(new(snapshot, digest), MaximumBytes);
        Assert.IsTrue(bytes.Length < MaximumBytes);
        Assert.IsTrue(WorkspaceContinuationCodec.TryDecodeCandidate(bytes, MaximumBytes, out var decoded));
        Assert.AreEqual(digest, decoded!.SnapshotDigest);
        Assert.AreEqual(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(decoded.Snapshot));
        Assert.IsTrue(WorkspaceContinuationHistoryIntegrity.TryValidate(Owner, decoded.Snapshot));
        CollectionAssert.AreEqual(bytes, WorkspaceContinuationCodec.Encode(decoded, MaximumBytes));

        string directory = Directory.CreateTempSubdirectory("chummer-restore-capacity-").FullName;
        try
        {
            FileWorkspaceStore target = new(directory);
            if (existingTarget)
                Assert.IsTrue(target.CreateWorkspaceDocument(Owner, source.WorkspaceId,
                    new WorkspaceDocument("<character><name>Original target</name><metatype>Human</metatype><created>False</created></character>", "sr5")).Success);
            TestOwner owner = new();
            var service = new WorkspaceContinuationRestoreService(target, owner,
                new ObservedResolver(source.Resolver, owner), source.Queries, Catalog(), MaximumBytes);
            using var review = service.Review(owner.Capture(), bytes);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
                JsonSerializer.Serialize(review.Result));
            Assert.AreEqual(existingTarget, review.Result.Target!.Exists);
            Assert.AreNotEqual(Guid.Empty, review.OperationId);
            var durableBefore = CaptureDurableFiles(directory);
            string targetBefore = JsonSerializer.Serialize(target.Get(Owner, source.WorkspaceId));

            var result = service.Confirm(review, explicitlyConfirmed: true);

            Assert.AreEqual(0, owner.ActiveLeases);
            Assert.AreEqual(sourceBefore, JsonSerializer.Serialize(source.Store.Get(source.WorkspaceId).Value));
            if (receiptCount == 4096)
            {
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome,
                    JsonSerializer.Serialize(result));
                Assert.IsFalse(result.ReopenRequired, "An accepted capacity-boundary record must remain readable.");
                var cold = new FileWorkspaceStore(directory).ReadContinuation(Owner, source.WorkspaceId);
                Assert.IsTrue(cold.Success, cold.Error);
                Assert.HasCount(receiptCount, cold.Value!.DelegatedGmCharacterEdits);
                Assert.AreEqual(JsonSerializer.Serialize(snapshot), JsonSerializer.Serialize(cold.Value));
                Assert.AreEqual(digest, WorkspaceContinuationSnapshotDigest.Compute(cold.Value));
                var stored = new FileWorkspaceStore(directory).Get(Owner, source.WorkspaceId);
                Assert.IsTrue(stored.Success, stored.Error);
                Assert.AreEqual(result.Receipt, stored.Value!.LocalHistory!.LastRestore);
                Assert.AreEqual(snapshot.Workspace.ContentRevision, stored.Value.LocalHistory.ImportedThroughRevision);
                foreach (var receipt in new[] { ledger[0], ledger[^1] })
                {
                    var replay = target.LookupDelegatedGmCharacterEdit(Owner, source.WorkspaceId,
                        receipt.IdempotencyKeySha256, receipt.CommandSha256);
                    Assert.AreEqual(DelegatedGmCharacterEditStoreOutcome.IdempotencyConflict, replay.Outcome);
                    Assert.IsNull(replay.Receipt);
                }
            }
            else
            {
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Rejected, result.Outcome,
                    "An otherwise valid oversized ledger must be rejected before any target rename. "
                    + JsonSerializer.Serialize(result));
                Assert.IsNull(result.Receipt);
                AssertDurableFilesUnchanged(directory, durableBefore);
                Assert.AreEqual(targetBefore, JsonSerializer.Serialize(target.Get(Owner, source.WorkspaceId)));
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.RecoveryProofMissing,
                    service.Recover(owner.Capture(), source.WorkspaceId, review.OperationId, review.AdmissionDigest!).Outcome);
                Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, service.Confirm(review, true).Outcome);
                AssertDurableFilesUnchanged(directory, durableBefore);
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static WorkspaceContinuationSnapshot WithUnverifiedHistory(WorkspaceContinuationSnapshot source, int count)
    {
        const string delegation = "synthetic-historical-delegation";
        const string authority = "synthetic-historical-authority";
        const string historicalValue = "Historical note";
        long baseRevision = source.Workspace.ContentRevision;
        var receipts = Enumerable.Range(0, count).Select(index =>
        {
            string key = Hash("synthetic-unverified-key-" + index);
            string command = Hash("synthetic-unverified-command-" + index);
            string receiptId = "gm-edit-" + Hash(command + "\n" + delegation + "\n" + authority + "\n" + key)[..24];
            return new DelegatedGmCharacterEditAuditReceipt(DelegatedGmCharacterEditContract.Name,
                receiptId, "synthetic-historical-campaign", delegation, "historical-campaign-owner",
                Owner.NormalizedValue, authority, 7, "historical-gm@example.com",
                DelegatedGmCharacterEditContract.GameMasterRole, Owner.NormalizedValue, source.Workspace.Id,
                "Unverified historical edit", key, command, baseRevision + index, baseRevision + index + 1,
                new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero),
                [new(DelegatedGmCharacterPatchOperationKind.Replace, DelegatedGmCharacterEditContract.ProfileNotesPath,
                    Hash(historicalValue), historicalValue.Length)]);
        }).ToArray();
        return source with
        {
            OwnerId = Owner.NormalizedValue,
            DelegatedGmCharacterEdits = receipts,
            DelegatedGmHistorySegmentStarts = [],
            // A later owner revision preserves the genuine bootstrap payload;
            // no latest-GM value commitment is falsely claimed for that payload.
            Workspace = source.Workspace with { ContentRevision = baseRevision + count + 1 }
        };
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static Dictionary<string, (byte[] Bytes, DateTime Timestamp)> CaptureDurableFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.Ordinal) || path.EndsWith(".slot", StringComparison.Ordinal))
            .ToDictionary(path => path, path => (File.ReadAllBytes(path), File.GetLastWriteTimeUtc(path)), StringComparer.Ordinal);

    private static void AssertDurableFilesUnchanged(string directory,
        Dictionary<string, (byte[] Bytes, DateTime Timestamp)> expected)
    {
        var actual = CaptureDurableFiles(directory);
        CollectionAssert.AreEquivalent(expected.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var (path, previous) in expected)
        {
            CollectionAssert.AreEqual(previous.Bytes, actual[path].Bytes, path);
            Assert.AreEqual(previous.Timestamp, actual[path].Timestamp, path);
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

    private sealed class ObservedResolver(ICharacterSourceDataResolver inner, TestOwner owner) : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext? TryCreateContext(string xml)
        {
            Assert.AreEqual(1, owner.ActiveLeases);
            return inner.TryCreateContext(xml);
        }
    }

    private sealed class TestOwner : IOwnerContextLeaseAccessor
    {
        private readonly object _gate = new();
        private readonly string _issuer = Guid.NewGuid().ToString("N");
        public OwnerScope Current => Owner;
        public int ActiveLeases { get; private set; }
        public OwnerContextStamp Capture() => new(Owner, _issuer, 0);
        public bool TryAcquire(OwnerContextStamp expected, [NotNullWhen(true)] out IOwnerContextLease? lease)
        {
            Monitor.Enter(_gate);
            Assert.AreEqual(0, ActiveLeases, "Owner leases must not nest.");
            if (!expected.IsValid || expected != Capture())
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
