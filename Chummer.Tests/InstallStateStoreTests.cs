#nullable enable annotations

using System;
using System.IO;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class InstallStateStoreTests
{
    [TestMethod]
    public void File_rulepack_install_state_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRulePackInstallStateStore store = new(stateDirectory);
            RulePackInstallRecord record = new(
                PackId: "house-rules",
                Version: "overlay-v1",
                RulesetId: RulesetDefaults.Sr5,
                Install: new ArtifactInstallState(
                    State: ArtifactInstallStates.Pinned,
                    InstalledTargetKind: RuleProfileApplyTargetKinds.Workspace,
                    InstalledTargetId: "workspace-1",
                    RuntimeFingerprint: "sha256:runtime"));

            store.Upsert(new OwnerScope("alice"), record);

            RulePackInstallRecord? reloaded = store.Get(new OwnerScope("alice"), "house-rules", "overlay-v1", RulesetDefaults.Sr5);
            RulePackInstallRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), "house-rules", "overlay-v1", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(ArtifactInstallStates.Pinned, reloaded.Install.State);
            Assert.AreEqual("workspace-1", reloaded.Install.InstalledTargetId);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void File_ruleprofile_install_state_store_persists_owner_scoped_records()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuleProfileInstallStateStore store = new(stateDirectory);
            RuleProfileInstallRecord record = new(
                ProfileId: "official.sr5.core",
                RulesetId: RulesetDefaults.Sr5,
                Install: new ArtifactInstallState(
                    State: ArtifactInstallStates.Installed,
                    InstalledTargetKind: RuleProfileApplyTargetKinds.GlobalDefaults,
                    InstalledTargetId: "local-single-user",
                    RuntimeFingerprint: "sha256:core"));

            store.Upsert(new OwnerScope("alice"), record);

            RuleProfileInstallRecord? reloaded = store.Get(new OwnerScope("alice"), "official.sr5.core", RulesetDefaults.Sr5);
            RuleProfileInstallRecord? hiddenFromBob = store.Get(new OwnerScope("bob"), "official.sr5.core", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual(ArtifactInstallStates.Installed, reloaded.Install.State);
            Assert.AreEqual("local-single-user", reloaded.Install.InstalledTargetId);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-install-state-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
