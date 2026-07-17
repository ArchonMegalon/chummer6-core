using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Workspaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class WorkspaceStoreHardeningTests
{
    private static readonly TimeSpan CoordinationTimeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public void File_workspace_store_applies_restrictive_unix_permissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("permissions"));
            string targetPath = GetTargetPath(stateDirectory, id);

            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(stateDirectory));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(Path.Combine(stateDirectory, "workspaces")));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(targetPath));
            Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(targetPath + ".lock"));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_rejects_a_symbolic_link_state_root()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string container = CreateTempStateDirectory();
        string actualDirectory = Path.Combine(container, "actual");
        string linkedDirectory = Path.Combine(container, "linked");
        Directory.CreateDirectory(actualDirectory);
        Directory.CreateSymbolicLink(linkedDirectory, actualDirectory);

        try
        {
            AssertThrows<IOException>(() => _ = new FileWorkspaceStore(linkedDirectory));
        }
        finally
        {
            if (Directory.Exists(linkedDirectory))
            {
                Directory.Delete(linkedDirectory);
            }

            DeleteDirectory(container);
        }
    }

    [TestMethod]
    public void File_workspace_store_refuses_target_symbolic_links_without_touching_the_victim()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string targetPath = GetTargetPath(stateDirectory, id);
            string victimPath = Path.Combine(stateDirectory, "victim.json");
            File.WriteAllText(victimPath, "victim");
            File.Delete(targetPath);
            File.CreateSymbolicLink(targetPath, victimPath);

            Assert.IsFalse(store.TryGet(id, out _));
            AssertThrows<IOException>(() => store.Save(id, Document("replacement")));
            Assert.IsFalse(store.Delete(id));
            Assert.AreEqual("victim", File.ReadAllText(victimPath));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_refuses_lock_symbolic_links_without_touching_the_victim()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string lockPath = GetTargetPath(stateDirectory, id) + ".lock";
            string victimPath = Path.Combine(stateDirectory, "lock-victim");
            File.WriteAllText(victimPath, "victim");
            File.Delete(lockPath);
            File.CreateSymbolicLink(lockPath, victimPath);

            Assert.IsFalse(store.TryGet(id, out _));
            AssertThrows<IOException>(() => store.Save(id, Document("replacement")));
            Assert.IsFalse(store.Delete(id));
            Assert.AreEqual("victim", File.ReadAllText(victimPath));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_refuses_stale_temp_symbolic_links_without_touching_the_victim()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string targetPath = GetTargetPath(stateDirectory, id);
            string tempPath = targetPath + ".tmp.malicious";
            string victimPath = Path.Combine(stateDirectory, "temp-victim");
            File.WriteAllText(victimPath, "victim");
            File.CreateSymbolicLink(tempPath, victimPath);

            AssertThrows<IOException>(() => store.Save(id, Document("replacement")));
            Assert.AreEqual("victim", File.ReadAllText(victimPath));
            Assert.IsTrue(File.Exists(tempPath));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_removes_stale_regular_temps_in_deterministic_recovery_path()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string targetPath = GetTargetPath(stateDirectory, id);
            string laterTemp = targetPath + ".tmp.z";
            string earlierTemp = targetPath + ".tmp.a";
            File.WriteAllText(laterTemp, "later");
            File.WriteAllText(earlierTemp, "earlier");

            Assert.IsTrue(store.TryGet(id, out WorkspaceDocument loaded));

            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "original");
            Assert.IsFalse(File.Exists(earlierTemp));
            Assert.IsFalse(File.Exists(laterTemp));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_failure_before_replace_preserves_old_target_and_cleans_temp()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore normalStore = new(stateDirectory);
            CharacterWorkspaceId id = normalStore.Create(Document("original"));
            FileWorkspaceStore failingStore = new(
                stateDirectory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTempFileFlushed));

            AssertThrows<InjectedWorkspaceStoreException>(() => failingStore.Save(id, Document("replacement")));

            Assert.IsTrue(normalStore.TryGet(id, out WorkspaceDocument loaded));
            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "original");
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_failure_after_replace_leaves_complete_new_target()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore normalStore = new(stateDirectory);
            CharacterWorkspaceId id = normalStore.Create(Document("original"));
            FileWorkspaceStore failingStore = new(
                stateDirectory,
                new ThrowingFaultInjector(FileWorkspaceStoreFaultStage.AfterTargetReplaced));

            WorkspaceStoreMutationResult result = failingStore.ReplaceWorkspaceDocument(
                id,
                expectedContentRevision: 1,
                Document("replacement"));

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(2L, result.Entry?.ContentRevision);
            Assert.AreEqual(0L, result.Entry?.SavedRevision);
            Assert.IsTrue(normalStore.TryGet(id, out WorkspaceDocument loaded));
            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "replacement");
            Assert.AreEqual(2L, normalStore.Get(id).Value?.ContentRevision);
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_save_cannot_resurrect_a_deleted_workspace()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));

            Assert.IsTrue(store.Delete(id));
            AssertThrows<FileNotFoundException>(() => store.Save(id, Document("resurrected")));
            Assert.IsFalse(store.TryGet(id, out _));
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_serializes_two_store_instances_and_releases_gate_registry()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore creator = new(stateDirectory);
            CharacterWorkspaceId id = creator.Create(Document("original"));
            BlockingFaultInjector blocker = new();
            FileWorkspaceStore firstStore = new(stateDirectory, blocker);
            FileWorkspaceStore secondStore = new(stateDirectory);

            Task firstSave = Task.Run(() => firstStore.Save(id, Document("first")));
            Assert.IsTrue(blocker.WaitUntilBlocked(), "The first save did not reach the injected crash boundary.");
            ManualResetEventSlim secondStarted = new(initialState: false);
            Task secondSave = Task.Run(() =>
            {
                secondStarted.Set();
                secondStore.Save(id, Document("second"));
            });
            Assert.IsTrue(secondStarted.Wait(CoordinationTimeout), "The second save did not start.");
            await Task.Delay(250);
            bool secondWasBlocked = !secondSave.IsCompleted;
            blocker.Release();

            await Task.WhenAll(firstSave, secondSave);

            Assert.IsTrue(secondWasBlocked, "The second store bypassed the per-workspace in-process gate.");
            Assert.IsTrue(creator.TryGet(id, out WorkspaceDocument loaded));
            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "second");
            AssertNoTemps(stateDirectory, id);
            Assert.AreEqual(0, FileWorkspaceStore.ActiveGateCount);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_serializes_save_then_delete_across_two_store_instances()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore creator = new(stateDirectory);
            CharacterWorkspaceId id = creator.Create(Document("original"));
            BlockingFaultInjector blocker = new();
            FileWorkspaceStore savingStore = new(stateDirectory, blocker);
            FileWorkspaceStore deletingStore = new(stateDirectory);

            Task save = Task.Run(() => savingStore.Save(id, Document("saved")));
            Assert.IsTrue(blocker.WaitUntilBlocked(), "The save did not reach the injected crash boundary.");
            ManualResetEventSlim deleteStarted = new(initialState: false);
            Task<bool> delete = Task.Run(() =>
            {
                deleteStarted.Set();
                return deletingStore.Delete(id);
            });
            Assert.IsTrue(deleteStarted.Wait(CoordinationTimeout), "Delete did not start.");
            await Task.Delay(250);
            bool deleteWasBlocked = !delete.IsCompleted;
            blocker.Release();

            await save;
            Assert.IsTrue(await delete);

            Assert.IsTrue(deleteWasBlocked, "Delete bypassed the active save lease.");
            Assert.IsFalse(creator.TryGet(id, out _));
            AssertNoTemps(stateDirectory, id);
            Assert.AreEqual(0, FileWorkspaceStore.ActiveGateCount);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_serializes_read_behind_an_active_save()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore creator = new(stateDirectory);
            CharacterWorkspaceId id = creator.Create(Document("original"));
            BlockingFaultInjector blocker = new();
            FileWorkspaceStore savingStore = new(stateDirectory, blocker);
            FileWorkspaceStore readingStore = new(stateDirectory);

            Task save = Task.Run(() => savingStore.Save(id, Document("saved")));
            Assert.IsTrue(blocker.WaitUntilBlocked(), "The save did not reach the injected crash boundary.");
            ManualResetEventSlim readStarted = new(initialState: false);
            Task<(bool Found, WorkspaceDocument Document)> read = Task.Run(() =>
            {
                readStarted.Set();
                bool found = readingStore.TryGet(id, out WorkspaceDocument document);
                return (found, document);
            });
            Assert.IsTrue(readStarted.Wait(CoordinationTimeout), "Read did not start.");
            await Task.Delay(250);
            bool readWasBlocked = !read.IsCompleted;
            blocker.Release();

            await save;
            (bool found, WorkspaceDocument loaded) = await read;

            Assert.IsTrue(readWasBlocked, "Read bypassed the active save lease.");
            Assert.IsTrue(found);
            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "saved");
            AssertNoTemps(stateDirectory, id);
            Assert.AreEqual(0, FileWorkspaceStore.ActiveGateCount);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_serializes_saves_across_processes()
    {
        string stateDirectory = CreateTempStateDirectory();
        Process? holder = null;
        Process? contender = null;
        string releaseFile = Path.Combine(stateDirectory, "release");
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string readyFile = Path.Combine(stateDirectory, "ready");
            holder = StartHost("save-block", stateDirectory, id.Value, "first", readyFile, releaseFile);
            await WaitForSignalAsync(holder, readyFile);

            string contenderStartedFile = Path.Combine(stateDirectory, "contender-started");
            contender = StartHost("save-signal", stateDirectory, id.Value, "second", contenderStartedFile);
            await WaitForSignalAsync(contender, contenderStartedFile);
            await Task.Delay(250);
            bool contenderWasBlocked = !contender.HasExited;
            File.WriteAllText(releaseFile, "release");

            await AssertProcessExitAsync(holder, expectedExitCode: 0);
            await AssertProcessExitAsync(contender, expectedExitCode: 0);

            Assert.IsTrue(contenderWasBlocked, "The contender process bypassed the workspace lock file.");
            Assert.IsTrue(store.TryGet(id, out WorkspaceDocument loaded));
            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "second");
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            ReleaseAndStop(releaseFile, contender, holder);
            contender?.Dispose();
            holder?.Dispose();
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_serializes_save_then_delete_across_processes()
    {
        string stateDirectory = CreateTempStateDirectory();
        Process? holder = null;
        Process? contender = null;
        string releaseFile = Path.Combine(stateDirectory, "release");
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string readyFile = Path.Combine(stateDirectory, "ready");
            holder = StartHost("save-block", stateDirectory, id.Value, "saved", readyFile, releaseFile);
            await WaitForSignalAsync(holder, readyFile);

            string contenderStartedFile = Path.Combine(stateDirectory, "contender-started");
            contender = StartHost("delete-signal", stateDirectory, id.Value, contenderStartedFile);
            await WaitForSignalAsync(contender, contenderStartedFile);
            await Task.Delay(250);
            bool contenderWasBlocked = !contender.HasExited;
            File.WriteAllText(releaseFile, "release");

            await AssertProcessExitAsync(holder, expectedExitCode: 0);
            await AssertProcessExitAsync(contender, expectedExitCode: 0);

            Assert.IsTrue(contenderWasBlocked, "The delete process bypassed the active save lease.");
            Assert.IsFalse(store.TryGet(id, out _));
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            ReleaseAndStop(releaseFile, contender, holder);
            contender?.Dispose();
            holder?.Dispose();
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_recovers_stale_temp_after_process_crash()
    {
        string stateDirectory = CreateTempStateDirectory();
        Process? holder = null;
        string releaseFile = Path.Combine(stateDirectory, "never-release");
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            CharacterWorkspaceId id = store.Create(Document("original"));
            string readyFile = Path.Combine(stateDirectory, "ready");
            holder = StartHost("save-block", stateDirectory, id.Value, "crashing", readyFile, releaseFile);
            await WaitForSignalAsync(holder, readyFile);

            holder.Kill(entireProcessTree: true);
            await holder.WaitForExitAsync();
            Assert.IsTrue(EnumerateTemps(stateDirectory, id).Any(), "The killed writer did not leave its flushed temp file.");

            Assert.IsTrue(store.TryGet(id, out WorkspaceDocument loaded));

            StringAssert.Contains(loaded.PayloadEnvelope.Payload, "original");
            AssertNoTemps(stateDirectory, id);
        }
        finally
        {
            ReleaseAndStop(releaseFile, holder);
            holder?.Dispose();
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void Workspace_owner_state_paths_use_contained_collision_resistant_ascii_components()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            OwnerScope[] owners =
            [
                new("."),
                new(".."),
                new("../outside"),
                new(@"..\outside"),
                new("%2e%2e"),
                new("name."),
                new("name "),
                new("CON"),
                new("NUL.txt"),
                new("COM1"),
                new("LPT9.txt"),
                new("slash/name"),
                new(@"backslash\name")
            ];
            string ownersDirectory = Path.GetFullPath(Path.Combine(stateDirectory, "owners"));
            string[] paths = owners
                .Select(owner => OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(stateDirectory, owner))
                .ToArray();

            foreach (string path in paths)
            {
                Assert.AreEqual(ownersDirectory, Path.GetDirectoryName(path));
                string component = Path.GetFileName(path);
                StringAssert.StartsWith(component, "owner-v1-");
                Assert.IsTrue(
                    component.All(character =>
                        character is >= 'a' and <= 'z'
                        or >= 'A' and <= 'Z'
                        or >= '0' and <= '9'
                        or '-' or '_'),
                    $"Owner component was not portable ASCII: {component}");
            }

            Assert.AreEqual(paths.Length, paths.Distinct(StringComparer.Ordinal).Count());
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void Workspace_owner_state_paths_preserve_ordinal_nfc_and_nfd_identity()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            OwnerScope nfc = new("\u00e9@example.com");
            OwnerScope nfd = new("e\u0301@example.com");

            string nfcPath = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(stateDirectory, nfc);
            string nfdPath = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(stateDirectory, nfd);

            Assert.AreNotEqual(nfc.NormalizedValue, nfd.NormalizedValue);
            Assert.AreNotEqual(nfcPath, nfdPath);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void Owner_scoped_sentinel_values_cannot_reach_local_state_in_either_store()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            AssertOwnerSentinelsCannotReachLocalState(new FileWorkspaceStore(stateDirectory));
            AssertOwnerSentinelsCannotReachLocalState(new InMemoryWorkspaceStore());
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_rejects_a_symbolic_link_in_the_configured_root_ancestry()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string container = CreateTempStateDirectory();
        string actualParent = Path.Combine(container, "actual-parent");
        string linkedParent = Path.Combine(container, "linked-parent");
        Directory.CreateDirectory(actualParent);
        Directory.CreateSymbolicLink(linkedParent, actualParent);
        try
        {
            AssertThrows<IOException>(() =>
                _ = new FileWorkspaceStore(Path.Combine(linkedParent, "state")));
        }
        finally
        {
            if (Directory.Exists(linkedParent))
            {
                Directory.Delete(linkedParent);
            }

            DeleteDirectory(container);
        }
    }

    [TestMethod]
    public void Dot_and_dotdot_owners_cannot_alias_local_workspace_state()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            WorkspaceStoreMutationResult local = store.CreateWorkspaceDocument(Document("local"));
            WorkspaceStoreMutationResult dot = store.CreateWorkspaceDocument(new OwnerScope("."), Document("dot"));
            WorkspaceStoreMutationResult dotDot = store.CreateWorkspaceDocument(new OwnerScope(".."), Document("dotdot"));

            Assert.IsTrue(local.Success, local.Error);
            Assert.IsTrue(dot.Success, dot.Error);
            Assert.IsTrue(dotDot.Success, dotDot.Error);
            Assert.AreNotEqual(dot.Entry?.Id, dotDot.Entry?.Id);
            Assert.IsTrue(store.Get(local.Entry!.Value.Id).Success);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Missing,
                store.Get(new OwnerScope(".."), local.Entry.Value.Id).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Missing,
                store.Get(new OwnerScope("."), local.Entry.Value.Id).Outcome);
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_migrates_only_the_contained_legacy_workspace_subtree()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            OwnerScope owner = new("legacy.user@example.com");
            CharacterWorkspaceId id = new("legacyownerworkspace");
            string ownersDirectory = Path.Combine(stateDirectory, "owners");
            string legacyDirectory = Path.Combine(
                ownersDirectory,
                Uri.EscapeDataString(owner.NormalizedValue));
            string legacyWorkspaceDirectory = Path.Combine(legacyDirectory, "workspaces");
            Directory.CreateDirectory(legacyWorkspaceDirectory);
            string legacyNonWorkspaceState = Path.Combine(legacyDirectory, "desktop-state.json");
            File.WriteAllText(legacyNonWorkspaceState, "{\"theme\":\"dark\"}");
            File.WriteAllText(
                Path.Combine(legacyWorkspaceDirectory, $"{id.Value}.json"),
                """
                {
                  "Content": "<character><name>Legacy owner</name></character>",
                  "Format": "NativeXml",
                  "RulesetId": "SR5"
                }
                """);

            WorkspaceStoreReadResult result = store.Get(owner, id);
            string canonicalDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(stateDirectory, owner);

            Assert.IsTrue(result.Success, result.Error);
            Assert.AreEqual(1L, result.Value?.ContentRevision);
            Assert.AreEqual(1L, result.Value?.SavedRevision);
            Assert.IsTrue(Directory.Exists(canonicalDirectory));
            Assert.IsTrue(Directory.Exists(legacyDirectory));
            Assert.IsFalse(Directory.Exists(legacyWorkspaceDirectory));
            Assert.AreEqual("{\"theme\":\"dark\"}", File.ReadAllText(legacyNonWorkspaceState));
            StringAssert.Contains(result.Value?.Document.Content ?? string.Empty, "Legacy owner");
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_allows_legacy_non_workspace_state_beside_canonical_owner_directory()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            OwnerScope owner = new("shared.state.user@example.com");
            WorkspaceStoreMutationResult created = store.CreateWorkspaceDocument(
                owner,
                Document("canonical workspace"));
            Assert.IsTrue(created.Success, created.Error);

            string legacyDirectory = Path.Combine(
                stateDirectory,
                "owners",
                Uri.EscapeDataString(owner.NormalizedValue));
            Directory.CreateDirectory(legacyDirectory);
            string legacyNonWorkspaceState = Path.Combine(legacyDirectory, "install-linking.json");
            File.WriteAllText(legacyNonWorkspaceState, "{\"linked\":true}");

            WorkspaceStoreReadResult read = store.Get(owner, created.Entry!.Value.Id);
            var listed = store.List(owner);

            Assert.IsTrue(read.Success, read.Error);
            Assert.HasCount(1, listed);
            Assert.AreEqual(created.Entry.Value.Id, listed[0].Id);
            Assert.AreEqual("{\"linked\":true}", File.ReadAllText(legacyNonWorkspaceState));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void File_workspace_store_fails_closed_when_legacy_and_canonical_workspace_directories_both_exist()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            OwnerScope owner = new("dual.user@example.com");
            CharacterWorkspaceId id = new("dualownerworkspace");
            string ownersDirectory = Path.Combine(stateDirectory, "owners");
            string legacyDirectory = Path.Combine(
                ownersDirectory,
                Uri.EscapeDataString(owner.NormalizedValue));
            string canonicalDirectory = OwnerScopedStatePath.ResolveWorkspaceOwnerDirectory(stateDirectory, owner);
            Directory.CreateDirectory(Path.Combine(legacyDirectory, "workspaces"));
            Directory.CreateDirectory(Path.Combine(canonicalDirectory, "workspaces"));

            WorkspaceStoreReadResult result = store.Get(owner, id);

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, result.Outcome);
            Assert.IsTrue(Directory.Exists(legacyDirectory));
            Assert.IsTrue(Directory.Exists(canonicalDirectory));
        }
        finally
        {
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_times_out_in_process_gate_and_releases_waiter_reference()
    {
        string stateDirectory = CreateTempStateDirectory();
        BlockingFaultInjector blocker = new();
        try
        {
            FileWorkspaceStore creator = new(stateDirectory);
            WorkspaceStoreMutationResult created = creator.CreateWorkspaceDocument(Document("original"));
            Assert.IsTrue(created.Success, created.Error);
            FileWorkspaceStore holder = new(stateDirectory, blocker);
            FileWorkspaceStore contender = new(
                stateDirectory,
                NoOpFaultInjector.Instance,
                TimeSpan.FromMilliseconds(250));
            Task<WorkspaceStoreMutationResult> heldMutation = Task.Run(() =>
                holder.ReplaceWorkspaceDocument(
                    created.Entry!.Value.Id,
                    expectedContentRevision: 1,
                    Document("held")));
            Assert.IsTrue(blocker.WaitUntilBlocked(), "The holder did not reach the injected boundary.");

            Stopwatch elapsed = Stopwatch.StartNew();
            WorkspaceStoreReadResult timedOut = contender.Get(created.Entry!.Value.Id);
            elapsed.Stop();

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, timedOut.Outcome);
            Assert.IsGreaterThanOrEqualTo(150d, elapsed.Elapsed.TotalMilliseconds);
            Assert.IsLessThan(2000d, elapsed.Elapsed.TotalMilliseconds);
            Assert.AreEqual(1, FileWorkspaceStore.ActiveGateCount);

            blocker.Release();
            WorkspaceStoreMutationResult completed = await heldMutation;
            Assert.IsTrue(completed.Success, completed.Error);
            Assert.AreEqual(0, FileWorkspaceStore.ActiveGateCount);
        }
        finally
        {
            blocker.Release();
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public async Task File_workspace_store_times_out_cross_process_lease_with_the_same_budget()
    {
        string stateDirectory = CreateTempStateDirectory();
        Process? holder = null;
        string releaseFile = Path.Combine(stateDirectory, "release");
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            WorkspaceStoreMutationResult created = store.CreateWorkspaceDocument(Document("original"));
            Assert.IsTrue(created.Success, created.Error);
            string readyFile = Path.Combine(stateDirectory, "ready");
            holder = StartHost(
                "save-block",
                stateDirectory,
                created.Entry!.Value.Id.Value,
                "held",
                readyFile,
                releaseFile);
            await WaitForSignalAsync(holder, readyFile);
            FileWorkspaceStore contender = new(
                stateDirectory,
                NoOpFaultInjector.Instance,
                TimeSpan.FromMilliseconds(250));

            Stopwatch elapsed = Stopwatch.StartNew();
            WorkspaceStoreReadResult timedOut = contender.Get(created.Entry.Value.Id);
            elapsed.Stop();

            Assert.AreEqual(WorkspaceOperationOutcome.Unavailable, timedOut.Outcome);
            Assert.IsGreaterThanOrEqualTo(150d, elapsed.Elapsed.TotalMilliseconds);
            Assert.IsLessThan(2000d, elapsed.Elapsed.TotalMilliseconds);
            Assert.AreEqual(0, FileWorkspaceStore.ActiveGateCount);

            File.WriteAllText(releaseFile, "release");
            await AssertProcessExitAsync(holder, expectedExitCode: 0);
        }
        finally
        {
            ReleaseAndStop(releaseFile, holder);
            holder?.Dispose();
            DeleteDirectory(stateDirectory);
        }
    }

    [TestMethod]
    public void Workspace_store_process_test_host_is_excluded_from_pack_publish_and_test_output()
    {
        string repositoryRoot = ResolveRepositoryRoot();
        string projectPath = Path.Combine(
            repositoryRoot,
            "Chummer.WorkspaceStore.TestHost",
            "Chummer.WorkspaceStore.TestHost.csproj");
        XDocument project = XDocument.Load(projectPath);

        Assert.AreEqual(
            "false",
            project.Descendants("IsPackable").Single().Value,
            ignoreCase: true);
        Assert.AreEqual(
            "false",
            project.Descendants("IsPublishable").Single().Value,
            ignoreCase: true);
        Assert.IsFalse(File.Exists(Path.Combine(
            AppContext.BaseDirectory,
            "Chummer.WorkspaceStore.TestHost.dll")));
    }

    private static void AssertOwnerSentinelsCannotReachLocalState(IWorkspaceStore store)
    {
        WorkspaceStoreMutationResult local = store.CreateWorkspaceDocument(Document("local"));
        Assert.IsTrue(local.Success, local.Error);
        CharacterWorkspaceId localId = local.Entry!.Value.Id;
        OwnerScope[] invalidOwners =
        [
            default,
            new OwnerScope(string.Empty),
            new OwnerScope("   "),
            OwnerScope.LocalSingleUser,
            new OwnerScope(" LOCAL-SINGLE-USER ")
        ];

        foreach (OwnerScope invalidOwner in invalidOwners)
        {
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.Get(invalidOwner, localId).Outcome);
            Assert.AreEqual(0, store.List(invalidOwner).Count);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.CreateWorkspaceDocument(invalidOwner, Document("scoped")).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.CreateWorkspaceDocument(
                    invalidOwner,
                    new CharacterWorkspaceId("scopedid"),
                    Document("scoped")).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.ReplaceWorkspaceDocument(invalidOwner, localId, 1, Document("replacement")).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.SaveCheckpoint(invalidOwner, localId, 1).Outcome);
            Assert.AreEqual(
                WorkspaceOperationOutcome.Unavailable,
                store.Delete(invalidOwner, localId, 1).Outcome);
        }

        WorkspaceStoreReadResult localRead = store.Get(localId);
        Assert.IsTrue(localRead.Success, localRead.Error);
        Assert.AreEqual(1L, localRead.Value?.ContentRevision);
        StringAssert.Contains(localRead.Value?.Document.Content ?? string.Empty, "local");
    }

    private static WorkspaceDocument Document(string name)
    {
        return new WorkspaceDocument(
            $"<character><name>{name}</name></character>",
            RulesetId: RulesetDefaults.Sr5);
    }

    private static string CreateTempStateDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string GetTargetPath(string stateDirectory, CharacterWorkspaceId id)
    {
        return Path.Combine(stateDirectory, "workspaces", $"{id.Value}.json");
    }

    private static string[] EnumerateTemps(string stateDirectory, CharacterWorkspaceId id)
    {
        return Directory.GetFiles(
            Path.Combine(stateDirectory, "workspaces"),
            $"{id.Value}.json.tmp.*",
            SearchOption.TopDirectoryOnly);
    }

    private static void AssertNoTemps(string stateDirectory, CharacterWorkspaceId id)
    {
        Assert.HasCount(0, EnumerateTemps(stateDirectory, id));
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            Assert.Fail($"Expected {typeof(TException).Name}.");
        }
        catch (TException)
        {
        }
    }

    private static Process StartHost(params string[] arguments)
    {
        string hostPath = ResolveHostPath();
        Assert.IsTrue(File.Exists(hostPath), $"Workspace-store test host was not built: {hostPath}");
        ProcessStartInfo startInfo = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(hostPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start workspace-store test host.");
    }

    private static string ResolveHostPath()
    {
        string repositoryRoot = ResolveRepositoryRoot();

        DirectoryInfo? configurationDirectory = new(AppContext.BaseDirectory);
        while (configurationDirectory.Parent is not null
               && !string.Equals(configurationDirectory.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            configurationDirectory = configurationDirectory.Parent;
        }

        string configuration = configurationDirectory.Name;
        string frameworkDirectory = Path.Combine(
            repositoryRoot,
            "Chummer.WorkspaceStore.TestHost",
            "bin",
            configuration,
            "net10.0");
        string directPath = Path.Combine(frameworkDirectory, "Chummer.WorkspaceStore.TestHost.dll");
        string runtimePath = Path.Combine(
            frameworkDirectory,
            new DirectoryInfo(AppContext.BaseDirectory).Name,
            "Chummer.WorkspaceStore.TestHost.dll");
        return File.Exists(directPath) ? directPath : runtimePath;
    }

    private static string ResolveRepositoryRoot()
    {
        DirectoryInfo? repositoryRoot = new(AppContext.BaseDirectory);
        while (repositoryRoot is not null
               && !Directory.Exists(Path.Combine(repositoryRoot.FullName, "Chummer.Infrastructure")))
        {
            repositoryRoot = repositoryRoot.Parent;
        }

        return repositoryRoot?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve the chummer-core-engine root.");
    }

    private static async Task WaitForSignalAsync(Process process, string signalFile)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!File.Exists(signalFile))
        {
            if (process.HasExited)
            {
                string error = await process.StandardError.ReadToEndAsync();
                Assert.Fail($"Workspace-store test host exited before signaling readiness: {error}");
            }

            if (stopwatch.Elapsed >= CoordinationTimeout)
            {
                Assert.Fail("Timed out waiting for workspace-store test host readiness.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task AssertProcessExitAsync(Process process, int expectedExitCode)
    {
        using CancellationTokenSource timeout = new(CoordinationTimeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            Assert.Fail("Workspace-store test host did not exit before the timeout.");
        }

        string error = await process.StandardError.ReadToEndAsync();
        Assert.AreEqual(expectedExitCode, process.ExitCode, error);
    }

    private static void ReleaseAndStop(string releaseFile, params Process?[] processes)
    {
        try
        {
            if (!File.Exists(releaseFile))
            {
                File.WriteAllText(releaseFile, "release");
            }
        }
        catch (IOException)
        {
        }

        foreach (Process? process in processes)
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ThrowingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly FileWorkspaceStoreFaultStage _stage;

        public ThrowingFaultInjector(FileWorkspaceStoreFaultStage stage)
        {
            _stage = stage;
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage == _stage)
            {
                throw new InjectedWorkspaceStoreException();
            }
        }
    }

    private sealed class BlockingFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        private readonly ManualResetEventSlim _blocked = new(initialState: false);
        private readonly ManualResetEventSlim _release = new(initialState: false);

        public bool WaitUntilBlocked()
        {
            return _blocked.Wait(CoordinationTimeout);
        }

        public void Release()
        {
            _release.Set();
        }

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
            if (stage != FileWorkspaceStoreFaultStage.AfterTempFileFlushed)
            {
                return;
            }

            _blocked.Set();
            if (!_release.Wait(CoordinationTimeout))
            {
                throw new TimeoutException("Timed out waiting for the in-process test release signal.");
            }
        }
    }

    private sealed class NoOpFaultInjector : IFileWorkspaceStoreFaultInjector
    {
        public static NoOpFaultInjector Instance { get; } = new();

        public void OnStage(FileWorkspaceStoreFaultStage stage, string targetPath, string tempPath)
        {
        }
    }

    private sealed class InjectedWorkspaceStoreException : Exception
    {
    }
}
