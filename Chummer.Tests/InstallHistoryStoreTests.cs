#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class InstallHistoryStoreTests
{
    [TestMethod]
    public void File_rulepack_install_history_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRulePackInstallHistoryStore store = new(stateDirectory);
            RulePackInstallHistoryRecord record = new(
                PackId: "house-rules",
                Version: "1.0.0",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Install,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:runtime"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00")));

            store.Append(new OwnerScope("alice"), record);

            IReadOnlyList<RulePackInstallHistoryRecord> reloaded = store.GetHistory(new OwnerScope("alice"), "house-rules", "1.0.0", RulesetDefaults.Sr5);
            IReadOnlyList<RulePackInstallHistoryRecord> hiddenFromBob = store.GetHistory(new OwnerScope("bob"), "house-rules", "1.0.0", RulesetDefaults.Sr5);

            Assert.HasCount(1, reloaded);
            Assert.AreEqual(ArtifactInstallHistoryOperations.Install, reloaded[0].Entry.Operation);
            Assert.AreEqual("workspace-1", reloaded[0].Entry.Install.InstalledTargetId);
            Assert.IsEmpty(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_ruleprofile_install_history_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuleProfileInstallHistoryStore store = new(stateDirectory);
            RuleProfileInstallHistoryRecord record = new(
                ProfileId: "official.sr5.core",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Pinned,
                        InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:05:00+00:00")));

            store.Append(new OwnerScope("alice"), record);

            IReadOnlyList<RuleProfileInstallHistoryRecord> reloaded = store.GetHistory(new OwnerScope("alice"), "official.sr5.core", RulesetDefaults.Sr5);
            IReadOnlyList<RuleProfileInstallHistoryRecord> hiddenFromBob = store.GetHistory(new OwnerScope("bob"), "official.sr5.core", RulesetDefaults.Sr5);

            Assert.HasCount(1, reloaded);
            Assert.AreEqual(ArtifactInstallHistoryOperations.Pin, reloaded[0].Entry.Operation);
            Assert.AreEqual("workspace-1", reloaded[0].Entry.Install.InstalledTargetId);
            Assert.IsEmpty(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_runtime_lock_install_history_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuntimeLockInstallHistoryStore store = new(stateDirectory);
            RuntimeLockInstallHistoryRecord record = new(
                LockId: "sha256:core",
                RulesetId: RulesetDefaults.Sr5,
                Entry: new ArtifactInstallHistoryEntry(
                    Operation: ArtifactInstallHistoryOperations.Pin,
                    Install: new ArtifactInstallState(
                        ArtifactInstallStates.Installed,
                        InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                        InstalledTargetId: "workspace-1",
                        RuntimeFingerprint: "sha256:core"),
                    AppliedAtUtc: DateTimeOffset.Parse("2026-03-06T12:10:00+00:00")));

            store.Append(new OwnerScope("alice"), record);

            IReadOnlyList<RuntimeLockInstallHistoryRecord> reloaded = store.GetHistory(new OwnerScope("alice"), "sha256:core", RulesetDefaults.Sr5);
            IReadOnlyList<RuntimeLockInstallHistoryRecord> hiddenFromBob = store.GetHistory(new OwnerScope("bob"), "sha256:core", RulesetDefaults.Sr5);

            Assert.HasCount(1, reloaded);
            Assert.AreEqual(ArtifactInstallHistoryOperations.Pin, reloaded[0].Entry.Operation);
            Assert.AreEqual("workspace-1", reloaded[0].Entry.Install.InstalledTargetId);
            Assert.IsEmpty(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-install-history-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
