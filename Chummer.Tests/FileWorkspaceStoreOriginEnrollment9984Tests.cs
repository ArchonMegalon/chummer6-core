using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Owners;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileWorkspaceStoreOriginEnrollment9984Tests
{
    [TestMethod]
    public void Origin9992_Windows_immutable_enrollment_custody_reopens_without_a_directory_callback()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Requires the ordinary Windows local-NTFS fixture.");
        string root = Path.Combine(Path.GetTempPath(), "origin9992-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string pin = Convert.ToBase64String(issuer.ExportSubjectPublicKeyInfo());
            var owners = new CharacterCreationAttributesServiceTests.AllocationOwners();
            owners.Set(new OwnerScope("install-account-v1:" + new string('a', 64)));
            var stamp = owners.Capture();
            Guid operation = Guid.NewGuid();
            var first = new FileWorkspaceStore.OriginTrustDomain(root, "synthetic-native-fixture", pin);
            var publicOrigin = first.ExportOwnerOrigin(owners, stamp);
            var request = first.PrepareOriginRequest(owners, stamp, "windows-fixture", operation);
            Assert.IsTrue(first.MarkOriginDispatch(owners, stamp, request, "begin"));
            var reopened = new FileWorkspaceStore.OriginTrustDomain(root, "synthetic-native-fixture", pin);
            Assert.AreEqual(publicOrigin, reopened.ExportOwnerOrigin(owners, stamp));
            Assert.AreEqual(request, reopened.PrepareOriginRequest(owners, stamp, "windows-fixture", operation));
            Assert.IsFalse(reopened.MarkOriginDispatch(owners, stamp, request, "begin"));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp.*", SearchOption.AllDirectories).Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    // Core storage/adoption test with a synthetic trusted issuer. The companion
    // Hub xUnit tests exercise the real V1/V2 authentication and possession flow.
    // Neither source test is a native Windows/Android or real account assertion.
    [TestMethod]
    public void Origin9984_Foreign_adoption_saved_reopen_and_reviewed_return_keep_signatures_and_source_branches()
    {
        using var f = new Fixture();
        var initial = f.Seed();
        f.Import(f.Windows, initial);
        f.Change(f.Windows, initial.Binding.WorkspaceId, "Windows before phone");
        var windows = f.Export(f.Windows, initial.Binding.WorkspaceId);
        var androidRestore = new WorkspacePrivateRuleSourceClosureRestoreService(f.Android, f.Owners, f.Queries);
        using (var refused = androidRestore.Review(f.Owners.Capture(), windows.ExportPrivateBytes(), windows.Binding))
            Assert.AreNotEqual(WorkspaceContinuationRestoreOutcome.Applied,
                androidRestore.Confirm(refused, true).Outcome);
        f.Authorize(f.AndroidTrust, "android", windows);
        f.Import(f.Android, windows);
        var originalHistory = f.Decode(windows).AuthenticatedHistory!;
        f.ReopenAndroid();
        CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(originalHistory),
            JsonSerializer.SerializeToUtf8Bytes(f.Decode(f.Export(f.Android, windows.Binding.WorkspaceId)).AuthenticatedHistory));
        f.Change(f.Android, windows.Binding.WorkspaceId, "Android return");
        var returned = f.Export(f.Android, windows.Binding.WorkspaceId);
        f.Authorize(f.WindowsTrust, "windows", returned);
        var restore = new WorkspacePrivateRuleSourceClosureRestoreService(f.Windows, f.Owners, f.Queries);
        using var review = restore.ReviewUpdate(f.Owners.Capture(), returned.ExportPrivateBytes(), returned.Binding, f.Resolver());
        Assert.IsNotNull(review.UpdatePreview, string.Join(",", review.Result.Blockers ?? []));
        var result = restore.Confirm(review, true);
        Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, string.Join(",", result.Blockers ?? []));
        Assert.IsTrue(restore.ListUpdateAlternatives(f.Owners.Capture(), windows.Binding.WorkspaceId).Any(a => a.Binding == windows.Binding));
        f.ReopenWindows();
        var final = f.Decode(f.Export(f.Windows, windows.Binding.WorkspaceId));
        var returnedHistory = f.Decode(returned).AuthenticatedHistory!;
        CollectionAssert.AreEqual(JsonSerializer.SerializeToUtf8Bytes(returnedHistory.Transitions),
            JsonSerializer.SerializeToUtf8Bytes(final.AuthenticatedHistory!.Transitions[..returnedHistory.Transitions.Length]));
        StringAssert.Contains(final.Snapshot.Workspace.Document.Content, "Android return");
        Assert.IsTrue(f.Decode(windows).Captures.Keys.All(final.Captures.ContainsKey));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "core-origin9984-" + Guid.NewGuid().ToString("N"));
        private readonly ECDsa _issuer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly Guid _epoch = Guid.NewGuid();
        internal readonly Owners Owners = new();
        internal readonly XmlCharacterFileQueries Queries = new(new CharacterFileService());
        internal FileWorkspaceStore Windows = null!, Android = null!;
        internal FileWorkspaceStore.OriginTrustDomain WindowsTrust = null!, AndroidTrust = null!;
        private string Sources => Path.Combine(_root, "sources");
        private readonly string _fixture = Environment.GetEnvironmentVariable("CHUMMER_AMEND9967_FIXTURE")
            ?? throw new AssertFailedException("Set CHUMMER_AMEND9967_FIXTURE to the pinned 9967 fixture.");
        internal Fixture()
        {
            Directory.CreateDirectory(_root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(_root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string source = Environment.GetEnvironmentVariable("CHUMMER_AMEND9976_BASE_SOURCES")
                ?? throw new AssertFailedException("Set CHUMMER_AMEND9976_BASE_SOURCES to frozen Core/Chummer.");
            CopyXml(Path.Combine(source, "data"), Path.Combine(Sources, "data"));
            CopyXml(Path.Combine(source, "lang"), Path.Combine(Sources, "lang"));
            CopyXml(Path.Combine(_fixture, "initialize", "data"), Path.Combine(Sources, "data"));
            CopyXml(Path.Combine(_fixture, "baseline", "customdata"), Path.Combine(Sources, "customdata"));
            ReopenWindows(); ReopenAndroid();
        }
        private FileWorkspaceStore.OriginTrustDomain Trust(string installation) => new(Path.Combine(_root, installation + "-trust"),
            "synthetic-core-issuer", Convert.ToBase64String(_issuer.ExportSubjectPublicKeyInfo()), _ => { });
        internal void ReopenWindows()
        { WindowsTrust = Trust("windows"); Windows = FileWorkspaceStore.CreateWithRetainedRuleSources(Path.Combine(_root, "windows"), Queries, WindowsTrust); }
        internal void ReopenAndroid()
        { AndroidTrust = Trust("android"); Android = FileWorkspaceStore.CreateWithRetainedRuleSources(Path.Combine(_root, "android"), Queries, AndroidTrust); }
        internal FileSystemCharacterSourceDataResolver Resolver() => new(new FileSystemContentOverlayCatalogService(Sources, Sources, null));
        internal WorkspacePrivateRuleSourceClosure Export(FileWorkspaceStore store, CharacterWorkspaceId id)
            => store.ExportCurrentRuleEnvironmentClosure(Owners, Owners.Capture(), id,
                WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, Resolver(), Chummer5aLegacyImportCompatibility.ProfileV2);
        internal WorkspacePrivateRuleSourceClosure.Decoded Decode(WorkspacePrivateRuleSourceClosure closure)
            => WorkspacePrivateRuleSourceClosure.DecodeForRestore(closure.ExportPrivateBytes(), closure.Binding, Queries, default);
        internal WorkspacePrivateRuleSourceClosure Seed()
        {
            var seed = FileWorkspaceStore.CreateWithRetainedRuleSources(Path.Combine(_root, "seed"), Queries, WindowsTrust);
            var created = seed.CreateWorkspaceDocument(Owners.Current,
                new WorkspaceDocument(FileWorkspaceStoreAppliedAmend9976Tests.CreatePriorityImport10101(Sources), "sr5"));
            Assert.IsTrue(created.Success);
            Assert.IsTrue(seed.SaveCheckpoint(Owners.Current, created.Entry!.Value.Id, created.Entry.Value.ContentRevision).Success);
            var d = Decode(Export(seed, created.Entry.Value.Id)); var saved = d.Snapshot.Workspace; var doc = saved.Document;
            var state = new WorkspaceHistoricalState(doc.Format.ToString(), JsonSerializer.Serialize(doc.PayloadEnvelope),
                JsonSerializer.Serialize(doc.AuxiliaryState), CharacterCareerMartialAcquisitionTransaction.SourceDigest(doc),
                WorkspaceDocumentAuxiliaryStateDigest.Compute(doc.AuxiliaryState), CharacterCareerMartialAcquisitionTransaction.Hash(doc.Content));
            var observation = new WorkspaceHistoricalObservation(1, WindowsTrust.OriginId, Owners.Current.NormalizedValue, false,
                saved.Id.Value, Guid.NewGuid().ToString("N"), "", saved.ContentRevision, saved.ContentRevision,
                saved.SavedRevision, saved.SavedRevision, state, state, d.CurrentEnvironment!.CaptureDigest, d.CurrentEnvironment.CaptureDigest, null);
            return WorkspacePrivateRuleSourceClosure.Create(d.Snapshot, d.Captures.Values.Select(c => c.ExportPrivateInputs()).ToArray(),
                d.Campaigns, WorkspacePrivateRuleSourceClosureBinding.MaximumBytes, default, d.CurrentEnvironment, new([WindowsTrust.Sign(observation)]));
        }
        internal void Import(FileWorkspaceStore store, WorkspacePrivateRuleSourceClosure closure)
        {
            var service = new WorkspacePrivateRuleSourceClosureRestoreService(store, Owners, Queries);
            using var review = service.Review(Owners.Capture(), closure.ExportPrivateBytes(), closure.Binding);
            // A refused review owns no bytes. Confirm would report ReviewConsumed
            // even on the first call; keep the actual admission blocker visible.
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Available, review.Result.Outcome,
                string.Join(",", review.Result.Blockers ?? []));
            var result = service.Confirm(review, true);
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.Applied, result.Outcome, string.Join(",", result.Blockers ?? []));
            Assert.AreEqual(WorkspaceContinuationRestoreOutcome.ReviewConsumed, service.Confirm(review, true).Outcome);
        }
        internal void Change(FileWorkspaceStore store, CharacterWorkspaceId id, string alias)
        {
            var saved = store.Get(Owners.Current, id).Value!;
            var xml = XElement.Parse(saved.Document.Content); xml.SetElementValue("alias", alias);
            var document = saved.Document with { State = saved.Document.State with { Payload = xml.ToString(SaveOptions.DisableFormatting) } };
            Assert.IsTrue(store.ReplaceWorkspaceDocumentAndCheckpoint(Owners.Current, id, saved.ContentRevision, document).Success);
        }
        internal void Authorize(FileWorkspaceStore.OriginTrustDomain domain, string installation, WorkspacePrivateRuleSourceClosure closure)
        {
            var request = domain.PrepareOriginRequest(Owners, Owners.Capture(), installation, Guid.NewGuid(),
                WorkspaceOriginProtocol.HistoryDigest(Decode(closure).AuthenticatedHistory!));
            WorkspaceOriginBinding Binding(FileWorkspaceStore.OriginTrustDomain key, string name)
            {
                var material = key.ExportOwnerOrigin(Owners, Owners.Capture());
                return new(material.OriginId, material.PublicKey, name, new string('1', 64), true);
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var snapshot = new WorkspaceOriginSnapshot(request, Owners.Current.NormalizedValue, _epoch, 1, now,
                now + WorkspaceOriginProtocol.LifetimeSeconds, false, [Binding(WindowsTrust, "windows"), Binding(AndroidTrust, "android")]);
            domain.AcceptOriginSnapshot(Owners, Owners.Capture(), new("synthetic-core-issuer", snapshot,
                Convert.ToBase64String(_issuer.SignData(WorkspaceOriginProtocol.Bytes("snapshot", snapshot),
                    HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))));
        }
        private static void CopyXml(string source, string target)
        {
            foreach (string file in Directory.EnumerateFiles(source, "*.xml", SearchOption.AllDirectories))
            { string path = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(file, path, true); }
        }
        public void Dispose() { _issuer.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    }
    private sealed class Owners : IOwnerContextLeaseAccessor
    {
        public OwnerScope Current => new("install-account-v1:" + new string('a', 64));
        private readonly object _gate = new(); private readonly string _issuer = Guid.NewGuid().ToString("N");
        public OwnerContextStamp Capture() => new(Current, _issuer, 0);
        public bool TryAcquire(OwnerContextStamp expected, out IOwnerContextLease? lease)
        { if (expected != Capture()) { lease = null; return false; } Monitor.Enter(_gate); lease = new Lease(_gate, expected); return true; }
        private sealed class Lease(object gate, OwnerContextStamp stamp) : IOwnerContextLease
        { public OwnerContextStamp Stamp => stamp; public void Dispose() => Monitor.Exit(gate); }
    }
}
