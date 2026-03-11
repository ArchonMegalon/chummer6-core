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
public class RuntimeLockStoreTests
{
    [TestMethod]
    public void File_runtime_lock_store_persists_owner_scoped_entries()
    {
        string stateDirectory = CreateTempDirectory();

        try
        {
            FileRuntimeLockStore store = new(stateDirectory);
            RuntimeLockRegistryEntry entry = new(
                LockId: "sha256:alice-runtime",
                Owner: new OwnerScope("alice"),
                Title: "Alice Runtime",
                Visibility: ArtifactVisibilityModes.Private,
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                RuntimeLock: CreateRuntimeLock("sha256:alice-runtime"),
                UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
                Description: "Saved runtime lock.",
                Install: new ArtifactInstallState(
                    ArtifactInstallStates.Pinned,
                    InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                    InstalledTargetId: "workspace-1"));

            store.Upsert(new OwnerScope("alice"), entry);

            RuntimeLockRegistryEntry? reloaded = store.Get(new OwnerScope("alice"), "sha256:alice-runtime", RulesetDefaults.Sr5);
            RuntimeLockRegistryEntry? hiddenFromBob = store.Get(new OwnerScope("bob"), "sha256:alice-runtime", RulesetDefaults.Sr5);

            Assert.IsNotNull(reloaded);
            Assert.AreEqual("Alice Runtime", reloaded.Title);
            Assert.AreEqual(RuntimeLockCatalogKinds.Saved, reloaded.CatalogKind);
            Assert.AreEqual(ArtifactInstallStates.Pinned, reloaded.Install.State);
            Assert.AreEqual("workspace-1", reloaded.Install.InstalledTargetId);
            Assert.AreEqual("sha256:alice-runtime", reloaded.Install.RuntimeFingerprint);
            Assert.IsNull(hiddenFromBob);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    private static ResolvedRuntimeLock CreateRuntimeLock(string fingerprint)
    {
        return new ResolvedRuntimeLock(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "official.sr5.base",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "schema-1",
                    Title: "SR5 Base",
                    Description: "Built-in base content.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: fingerprint);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"chummer-runtime-lock-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
