using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileWorkspaceStoreRetainedRuleSourceTests
{
    private static readonly OwnerScope Owner = new("retained-rule-source-test");
    private static readonly CharacterWorkspaceId WorkspaceId = new("11111111111111111111111111111111");
    private const string ProfileId = "22222222-2222-2222-2222-222222222222";
    private const string FixtureVariable = "CHUMMER_RETAINED_SOURCES_FIXTURE";
    private static ICharacterFileQueries Queries() => new XmlCharacterFileQueries(new CharacterFileService());

    [TestMethod]
    public void Immutable_roundtrip_recovers_private_settings_and_custom_rules_without_original_sources()
    {
        using var fixture = new Fixture();
        byte[] expected = fixture.Execution.ExportPrivateInputs();
        Assert.IsTrue(expected.AsSpan().IndexOf(File.ReadAllBytes(fixture.SettingsPath)) >= 0);
        Assert.IsTrue(expected.AsSpan().IndexOf(File.ReadAllBytes(fixture.CustomPath)) >= 0);
        var reference = fixture.Publish();
        Directory.Delete(fixture.SourceRoot, recursive: true);
        Assert.IsNull(fixture.Resolver.TryCreateContext(Fixture.Character));
        // A new store object is useful coverage, but is NOT the fresh-process test below.
        var cold = FileWorkspaceStore.CreateWithRetainedRuleSources(fixture.StateRoot);
        AssertRecovered(cold, reference, fixture.PolicyDigest, expected);
        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(fixture.Artifact(reference)));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.GetDirectoryName(fixture.Artifact(reference))!));
        }
    }

    [TestMethod]
    public void Identical_existing_artifact_is_validated_and_never_overwritten()
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        string path = fixture.Artifact(reference);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        timestamp = File.GetLastWriteTimeUtc(path);
        Assert.AreEqual(reference, fixture.Publish());
        Assert.AreEqual(timestamp, File.GetLastWriteTimeUtc(path));
        CollectionAssert.AreEqual(fixture.Execution.ExportPrivateInputs(), File.ReadAllBytes(path));
    }

    [TestMethod]
    public void Cached_read_and_recovery_revalidate_same_length_tampering()
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        fixture.Store.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), [reference], scope =>
        {
            Assert.IsTrue(scope.TryRecoverRuleSources(reference.CaptureDigest, out _));
            string path = fixture.Artifact(reference);
            var timestamp = File.GetLastWriteTimeUtc(path);
            byte[] bytes = File.ReadAllBytes(path);
            bytes[^1] ^= 1;
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, timestamp);
            Assert.IsFalse(scope.ValidateRetainedRuleSources());
            Assert.IsFalse(scope.TryRecoverRuleSources(reference.CaptureDigest, out var rejected));
            Assert.IsNull(rejected);
            ExpectRejected(() => scope.PrepareAndPublish(fixture.Execution));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(path));
            return true;
        });
    }

    [TestMethod]
    public void Matching_digest_and_length_do_not_authorize_a_non_capture_payload()
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        byte[] malformed = new byte[checked((int)reference.ByteLength)];
        var forged = reference with { CaptureDigest = RetainedCharacterRuleSourceInputs.Digest(malformed) };
        File.WriteAllBytes(fixture.Artifact(forged), malformed);
        ExpectRejected(() => fixture.Recover(forged));
        fixture.Recover(reference);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("truncated")]
    [DataRow("oversized")]
    public void Missing_truncated_and_oversized_artifacts_fail_closed(string damage)
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        string path = fixture.Artifact(reference);
        if (damage == "missing") File.Delete(path);
        else
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
            stream.SetLength(damage == "truncated" ? reference.ByteLength - 1
                : (long)RetainedCharacterRuleSourceInputs.MaximumArchiveBytes + 1);
        }
        ExpectRejected(() => fixture.Recover(reference));
    }

    [TestMethod]
    [DataRow("owner")]
    [DataRow("workspace")]
    [DataRow("incarnation")]
    [DataRow("schema")]
    [DataRow("length")]
    [DataRow("digest-path")]
    [DataRow("workspace-path")]
    public void Foreign_or_malformed_references_never_select_a_file(string mismatch)
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        var changed = mismatch switch
        {
            "owner" => reference with { OwnerBinding = OwnerScopedStatePath.BuildOwnerComponent(new("foreign")) },
            "workspace" => reference with { WorkspaceId = "other-workspace" },
            "incarnation" => reference with { IncarnationId = Guid.NewGuid().ToString("N") },
            "schema" => reference with { SchemaVersion = 2 },
            "length" => reference with { ByteLength = (long)RetainedCharacterRuleSourceInputs.MaximumArchiveBytes + 1 },
            "digest-path" => reference with { CaptureDigest = "sha256:../../outside" },
            _ => reference with { WorkspaceId = "../outside" }
        };
        ExpectRejected(() => fixture.Recover(changed));
        fixture.Recover(reference);
    }

    [TestMethod]
    public void Real_foreign_owner_and_recreated_workspace_cannot_recover_the_old_reference()
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        var foreign = new OwnerScope("foreign");
        Assert.IsTrue(fixture.Store.CreateWorkspaceDocument(foreign, WorkspaceId,
            new(Fixture.Character, RulesetDefaults.Sr5)).Success);
        ExpectRejected(() => fixture.Store.WithRetainedRuleSources(foreign, WorkspaceId, Queries(), [reference], _ => true));
        var prior = fixture.Store.Get(Owner, WorkspaceId).Value!;
        Assert.IsTrue(fixture.Store.Delete(Owner, WorkspaceId, prior.ContentRevision).Success);
        Assert.IsTrue(fixture.Store.CreateWorkspaceDocument(Owner, WorkspaceId,
            new(Fixture.Character, RulesetDefaults.Sr5)).Success);
        ExpectRejected(() => fixture.Recover(reference));
        Assert.IsTrue(File.Exists(fixture.Artifact(reference)), "Old-incarnation artifacts are retained, not collected by age.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Symlinked_artifact_or_directory_is_rejected(bool directory)
    {
        if (OperatingSystem.IsWindows()) { Assert.Inconclusive("Requires a symlink-capable test account."); return; }
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        string path = directory ? Path.GetDirectoryName(fixture.Artifact(reference))! : fixture.Artifact(reference);
        string moved = Path.Combine(fixture.Root, "outside-owner-scope");
        if (directory)
        {
            Directory.Move(path, moved);
            Directory.CreateSymbolicLink(path, moved);
        }
        else
        {
            File.Move(path, moved);
            File.CreateSymbolicLink(path, moved);
        }
        try
        {
            ExpectRejected(() => fixture.Recover(reference));
            ExpectRejected(() => fixture.Publish());
        }
        finally
        {
            if (directory) { Directory.Delete(path); Directory.Move(moved, path); }
            else { File.Delete(path); File.Move(moved, path); }
        }
    }

    [TestMethod]
    public void Failure_after_byte_flush_leaves_record_unchanged_and_no_published_reference()
    {
        var fault = new PublicationFault();
        using var fixture = new Fixture(fault: fault);
        byte[] before = File.ReadAllBytes(fixture.RecordPath);
        fault.Stage = FileWorkspaceStoreFaultStage.AfterTempFileFlushed;
        ExpectRejected(() => fixture.Publish());
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.RecordPath));
        Assert.IsFalse(Directory.EnumerateFiles(fixture.StateRoot, "*.bin", SearchOption.AllDirectories).Any());
        Assert.AreEqual(1, Directory.EnumerateFiles(fixture.StateRoot, ".pending-*", SearchOption.AllDirectories).Count());
        fault.Stage = null;
        var reference = fixture.Publish();
        fixture.Recover(reference);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.RecordPath));
    }

    [TestMethod]
    public void Post_rename_uncertainty_is_reconciled_by_identity_without_record_mutation()
    {
        var fault = new PublicationFault();
        using var fixture = new Fixture(fault: fault);
        byte[] before = File.ReadAllBytes(fixture.RecordPath);
        fault.Stage = FileWorkspaceStoreFaultStage.AfterTargetReplaced;
        var reference = fixture.Publish();
        Assert.AreEqual(1, fault.Failures);
        fixture.Recover(reference);
        CollectionAssert.AreEqual(before, File.ReadAllBytes(fixture.RecordPath));
        Assert.AreEqual(1, Directory.EnumerateFiles(fixture.StateRoot, "*.bin", SearchOption.AllDirectories).Count());
    }

    [TestMethod]
    public void Recovery_port_expires_with_lease_and_unlisted_digests_are_not_admitted()
    {
        using var fixture = new Fixture();
        var reference = fixture.Publish();
        ICharacterCreationRuleSourceRecovery? escaped = null;
        fixture.Store.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), [], scope =>
        {
            escaped = scope;
            Assert.IsFalse(scope.TryRecoverRuleSources(reference.CaptureDigest, out _));
            return true;
        });
        Assert.IsFalse(escaped!.ValidateRetainedRuleSources());
        Assert.IsFalse(escaped.TryRecoverRuleSources(reference.CaptureDigest, out _));
        var supplied = new List<CharacterCreationRetainedRuleSourceReference> { reference };
        fixture.Store.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), supplied, scope =>
        {
            supplied.Clear();
            Assert.IsTrue(scope.TryRecoverRuleSources(reference.CaptureDigest, out var restored));
            byte[] mutated = restored!.ExportPrivateInputs();
            Array.Fill(mutated, (byte)0);
            Assert.IsTrue(scope.ValidateRetainedRuleSources());
            return true;
        });
    }

    [TestMethod]
    public void Old_store_constructors_and_implicit_roots_do_not_enable_retention()
    {
        using var fixture = new Fixture();
        var disabled = new FileWorkspaceStore(fixture.StateRoot);
        ExpectRejected(() => disabled.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), [], _ => true));
        ExpectRejected(() => FileWorkspaceStore.CreateWithRetainedRuleSources(null!));
        ExpectRejected(() => FileWorkspaceStore.CreateWithRetainedRuleSources("relative-root"));
    }

    // Controller runs these in TWO separate test-host invocations with the same private unused
    // CHUMMER_RETAINED_SOURCES_FIXTURE absolute directory. No second in-memory instance counts.
    // This fixture manifest is test transport only, NOT the controller's future record manifest.
    [TestMethod]
    public void RetainedSource_Phase1_publish_private_fixture()
    {
        string? root = Environment.GetEnvironmentVariable(FixtureVariable);
        if (string.IsNullOrWhiteSpace(root)) { Assert.Inconclusive("Requires the controller's private subprocess fixture directory."); return; }
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsFalse(Directory.Exists(root), "Never overwrite a subprocess fixture.");
        using var fixture = new Fixture(root, retain: true);
        var reference = fixture.Publish();
        Directory.Delete(fixture.SourceRoot, recursive: true);
        var receipt = new ProcessFixture(Environment.ProcessId, reference, fixture.PolicyDigest);
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var stream = new FileStream(Path.Combine(root, "process-fixture.json"), options);
        JsonSerializer.Serialize(stream, receipt);
        stream.Flush(flushToDisk: true);
        CharacterCreationEffectivePolicyTests.PublishPersistentDraftProcessFixture418(Path.Combine(root, "finish-draft"));
        CharacterCreationEffectivePolicyTests.PublishCommittedCampaignProcessFixture528(Path.Combine(root, "finish-campaign"));
    }

    [TestMethod]
    public void RetainedSource_Phase2_recover_private_fixture_in_fresh_process()
    {
        string? root = Environment.GetEnvironmentVariable(FixtureVariable);
        if (string.IsNullOrWhiteSpace(root)) { Assert.Inconclusive("Requires phase 1 in a different process."); return; }
        Assert.IsTrue(Path.IsPathFullyQualified(root));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "source")));
        using var stream = new FileStream(Path.Combine(root, "process-fixture.json"), FileMode.Open, FileAccess.Read);
        Assert.IsTrue(stream.Length <= 4096);
        var receipt = JsonSerializer.Deserialize<ProcessFixture>(stream)!;
        Assert.AreNotEqual(receipt.ProcessId, Environment.ProcessId, "A new object in the same process is insufficient.");
        var store = FileWorkspaceStore.CreateWithRetainedRuleSources(Path.Combine(root, "state"));
        AssertRecovered(store, receipt.Reference, receipt.PolicyDigest);
        CharacterCreationEffectivePolicyTests.RecoverPersistentDraftProcessFixture418(Path.Combine(root, "finish-draft"));
        CharacterCreationEffectivePolicyTests.RecoverCommittedCampaignProcessFixture528(Path.Combine(root, "finish-campaign"));
    }

    private static void AssertRecovered(FileWorkspaceStore store, CharacterCreationRetainedRuleSourceReference reference,
        string policyDigest, byte[]? expected = null)
        => store.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), [reference], scope =>
        {
            Assert.IsTrue(scope.ValidateRetainedRuleSources());
            Assert.IsTrue(scope.TryRecoverRuleSources(reference.CaptureDigest, out var execution));
            byte[] bytes = execution!.ExportPrivateInputs();
            if (expected is not null) CollectionAssert.AreEqual(expected, bytes);
            Assert.AreEqual(reference.ByteLength, bytes.LongLength);
            Assert.IsTrue(FileSystemCharacterSourceDataResolver.TryRestoreRuleSourceCapture(bytes, reference.CaptureDigest, out var capture));
            Assert.AreEqual(policyDigest, capture!.EffectivePolicyDigest);
            Assert.IsTrue(capture.MatchesOriginalCharacter(Fixture.Character));
            var context = capture.CreateReplayResolver().TryCreateContext(Fixture.Character);
            Assert.IsNotNull(context);
            Assert.IsTrue(context.TryResolveCreationEffectivePolicy(out var policy));
            Assert.AreEqual(policyDigest, policy!.PolicyDigest);
            return true;
        });

    private static void ExpectRejected(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { rejected = true; }
        Assert.IsTrue(rejected, "Invalid retained inputs must fail closed.");
    }

    public sealed record ProcessFixture(int ProcessId, CharacterCreationRetainedRuleSourceReference Reference, string PolicyDigest);

    private sealed class PublicationFault : IFileWorkspaceStoreFaultInjector
    {
        internal FileWorkspaceStoreFaultStage? Stage { get; set; }
        internal int Failures { get; private set; }
        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != Stage) return;
            Failures++;
            throw new IOException("Synthetic private publication failure.");
        }
    }

    private sealed class Fixture : IDisposable
    {
        internal const string Character = "<character><settings>" + ProfileId
            + "</settings><buildmethod>Priority</buildmethod><created>False</created>"
            + "<customdatadirectorynames><directoryname>Private</directoryname></customdatadirectorynames></character>";
        private readonly bool _retain;
        internal string Root { get; }
        internal string StateRoot => Path.Combine(Root, "state");
        internal string SourceRoot => Path.Combine(Root, "source");
        internal string SettingsPath => Path.Combine(SourceRoot, "data", "settings.xml");
        internal string CustomPath => Path.Combine(SourceRoot, "customdata", "Private", "amend_settings.xml");
        internal string RecordPath => Path.Combine(OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(StateRoot, Owner),
            "workspaces", WorkspaceId.Value + ".json");
        internal FileWorkspaceStore Store { get; }
        internal FileSystemCharacterSourceDataResolver Resolver { get; }
        internal CharacterCreationRuleSourceExecutionContext Execution { get; }
        internal string PolicyDigest { get; }

        internal Fixture(string? root = null, bool retain = false, PublicationFault? fault = null)
        {
            Root = root ?? Path.Combine(Path.GetTempPath(), "chummer-retained-sources-" + Guid.NewGuid().ToString("N"));
            _retain = retain;
            if (OperatingSystem.IsWindows()) Directory.CreateDirectory(Root);
            else Directory.CreateDirectory(Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(CustomPath)!);
            File.WriteAllText(SettingsPath, $"<chummer><settings><setting><id>{ProfileId}</id><buildmethod>Priority</buildmethod>"
                + "<buildpoints>25</buildpoints><priorityarray>ABCDE</priorityarray><prioritytable>Standard</prioritytable><sumtoten>10</sumtoten>"
                + "<books><book>SR5</book></books><nuyenformat>#,0.#</nuyenformat><availability>12</availability><nuyenperbpwftm>2000</nuyenperbpwftm>"
                + "<maxnumbermaxattributescreate>1</maxnumbermaxattributescreate><karmacost><karmaattribute>5</karmaattribute></karmacost>"
                + "<nuyenmaxbp>10</nuyenmaxbp><knowledgepointsexpression>({INTUnaug} + {LOGUnaug}) * 2</knowledgepointsexpression>"
                + "<customdatadirectorynames><customdatadirectoryname><directoryname>Private</directoryname><order>0</order>"
                + "<enabled>True</enabled></customdatadirectoryname></customdatadirectorynames></setting></settings></chummer>");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(CustomPath)!, "manifest.xml"),
                "<manifest><guid>33333333-3333-3333-3333-333333333333</guid><version>1.0</version></manifest>");
            File.WriteAllText(CustomPath, $"<chummer><settings><setting><id>{ProfileId}</id>"
                + "<nuyenformat amendoperation=\"append\">#</nuyenformat></setting></settings></chummer>");
            Resolver = new(new Catalog(new(Path.Combine(SourceRoot, "data"), string.Empty, [])));
            Assert.IsTrue(Resolver.TryCaptureRuleSources(Character, out var capture));
            PolicyDigest = capture!.EffectivePolicyDigest;
            Execution = new(capture, Queries());
            Store = FileWorkspaceStore.CreateWithRetainedRuleSources(StateRoot,
                (IFileWorkspaceStoreFaultInjector?)fault ?? FileWorkspaceStoreFaultInjector.None);
            Assert.IsTrue(Store.CreateWorkspaceDocument(Owner, WorkspaceId, new(Character, RulesetDefaults.Sr5)).Success);
        }

        internal CharacterCreationRetainedRuleSourceReference Publish()
            => Store.WithRetainedRuleSources(Owner, WorkspaceId, Queries(), [], scope =>
            {
                var publication = scope.PrepareAndPublish(Execution);
                Assert.IsTrue(publication.CanCommitReference, "Directory flush failed; reconcile before committing a reference.");
                Assert.AreEqual(OperatingSystem.IsLinux() ? FileWorkspaceStore.RetainedRuleSourceDurability.FileAndDirectoriesFlushed
                    : FileWorkspaceStore.RetainedRuleSourceDurability.ProcessRestartOnly, publication.Durability);
                return publication.Reference;
            });

        internal void Recover(CharacterCreationRetainedRuleSourceReference reference)
            => AssertRecovered(Store, reference, PolicyDigest);

        internal string Artifact(CharacterCreationRetainedRuleSourceReference reference)
            => Path.Combine(Path.GetDirectoryName(RecordPath)!, ".retained-rule-sources", WorkspaceId.Value,
                reference.IncarnationId, reference.CaptureDigest[7..] + ".bin");

        public void Dispose() { if (!_retain && Directory.Exists(Root)) Directory.Delete(Root, recursive: true); }
    }

    private sealed class Catalog(ContentOverlayCatalog catalog) : IContentOverlayCatalogService
    {
        public ContentOverlayCatalog GetCatalog() => catalog;
        public IReadOnlyList<string> GetDataDirectories() => [catalog.BaseDataPath];
        public IReadOnlyList<string> GetLanguageDirectories() => [];
        public string ResolveDataFile(string fileName) => Path.Combine(catalog.BaseDataPath, fileName);
    }
}
