#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Content;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RuntimeLockInstallServiceTests
{
    [TestMethod]
    public void Default_install_service_previews_runtime_lock_pin_and_session_replay_requirements()
    {
        DefaultRuntimeLockInstallService service = new(
            new RuntimeLockRegistryServiceStub(CreateEntry(ArtifactInstallStates.Available, RuntimeLockCatalogKinds.Published)),
            new RuntimeLockInstallHistoryStoreStub());

        RuntimeLockInstallPreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            "sha256:core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.SessionLedger, "session-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual("sha256:core", preview.LockId);
        Assert.IsTrue(preview.RequiresConfirmation);
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.Kind, RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned, StringComparison.Ordinal)));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.Kind, RuntimeLockInstallPreviewChangeKinds.SessionReplayRequired, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Default_install_service_persists_owner_saved_runtime_lock_and_history()
    {
        RuntimeLockInstallHistoryStoreStub historyStore = new();
        RuntimeLockRegistryServiceStub registry = new(CreateEntry(ArtifactInstallStates.Available, RuntimeLockCatalogKinds.Published));
        DefaultRuntimeLockInstallService service = new(
            registry,
            historyStore);

        RuntimeLockInstallReceipt? receipt = service.Apply(
            new OwnerScope("alice"),
            "sha256:core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(receipt);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Installed, receipt.Outcome);
        Assert.AreEqual("workspace-1", receipt.TargetId);
        Assert.HasCount(1, registry.Upserts);
        Assert.AreEqual(RuntimeLockCatalogKinds.Saved, registry.Upserts[0].CatalogKind);
        Assert.AreEqual("alice", registry.Upserts[0].Owner.NormalizedValue);
        Assert.AreEqual(ArtifactInstallStates.Pinned, registry.Upserts[0].Install.State);
        Assert.AreEqual("workspace-1", registry.Upserts[0].Install.InstalledTargetId);
        Assert.HasCount(1, historyStore.Appends);
        Assert.AreEqual(ArtifactInstallHistoryOperations.Pin, historyStore.Appends[0].Entry.Operation);
    }

    [TestMethod]
    public void Default_install_service_returns_already_installed_when_runtime_lock_is_already_pinned_to_target()
    {
        DefaultRuntimeLockInstallService service = new(
            new RuntimeLockRegistryServiceStub(
                CreateEntry(
                    ArtifactInstallStates.Pinned,
                    RuntimeLockCatalogKinds.Saved,
                    installedTargetKind: RuleProfileApplyTargetKinds.Workspace,
                    installedTargetId: "workspace-1")),
            new RuntimeLockInstallHistoryStoreStub());

        RuntimeLockInstallReceipt? receipt = service.Apply(
            OwnerScope.LocalSingleUser,
            "sha256:core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(receipt);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Unchanged, receipt.Outcome);
    }

    private static RuntimeLockRegistryEntry CreateEntry(string state, string catalogKind, string? installedTargetKind = null, string? installedTargetId = null)
    {
        return new RuntimeLockRegistryEntry(
            LockId: "sha256:core",
            Owner: new OwnerScope("system"),
            Title: "Official SR5 Core Runtime",
            Visibility: ArtifactVisibilityModes.Public,
            CatalogKind: catalogKind,
            RuntimeLock: new ResolvedRuntimeLock(
                RulesetId: RulesetDefaults.Sr5,
                ContentBundles:
                [
                    new ContentBundleDescriptor(
                        BundleId: "official.sr5.base",
                        RulesetId: RulesetDefaults.Sr5,
                        Version: "schema-5",
                        Title: "SR5 Base Content",
                        Description: "Base content.",
                        AssetPaths: ["data/", "lang/"])
                ],
                RulePacks: [],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                EngineApiVersion: "rulepack-v1",
                RuntimeFingerprint: "sha256:core"),
            UpdatedAtUtc: DateTimeOffset.Parse("2026-03-06T12:00:00+00:00"),
            Install: new ArtifactInstallState(state, InstalledTargetKind: installedTargetKind, InstalledTargetId: installedTargetId, RuntimeFingerprint: "sha256:core"));
    }

    private sealed class RuntimeLockRegistryServiceStub : IRuntimeLockRegistryService
    {
        private readonly RuntimeLockRegistryEntry _entry;
        public List<RuntimeLockRegistryEntry> Upserts { get; } = [];

        public RuntimeLockRegistryServiceStub(RuntimeLockRegistryEntry entry)
        {
            _entry = entry;
        }

        public RuntimeLockRegistryPage List(OwnerScope owner, string? rulesetId = null) => new([_entry], 1);

        public RuntimeLockRegistryEntry? Get(OwnerScope owner, string lockId, string? rulesetId = null)
        {
            return string.Equals(lockId, _entry.LockId, StringComparison.Ordinal) ? _entry : null;
        }

        public RuntimeLockRegistryEntry Upsert(OwnerScope owner, string lockId, RuntimeLockSaveRequest request)
        {
            RuntimeLockRegistryEntry entry = new(
                LockId: lockId,
                Owner: owner,
                Title: request.Title,
                Visibility: request.Visibility,
                CatalogKind: RuntimeLockCatalogKinds.Saved,
                RuntimeLock: request.RuntimeLock,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                Description: request.Description,
                Install: request.Install ?? new ArtifactInstallState(ArtifactInstallStates.Available, RuntimeFingerprint: request.RuntimeLock.RuntimeFingerprint));
            Upserts.Add(entry);
            return entry;
        }
    }

    private sealed class RuntimeLockInstallHistoryStoreStub : IRuntimeLockInstallHistoryStore
    {
        public List<RuntimeLockInstallHistoryRecord> Appends { get; } = [];

        public IReadOnlyList<RuntimeLockInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => Appends;

        public IReadOnlyList<RuntimeLockInstallHistoryRecord> GetHistory(OwnerScope owner, string lockId, string rulesetId) => Appends;

        public RuntimeLockInstallHistoryRecord Append(OwnerScope owner, RuntimeLockInstallHistoryRecord record)
        {
            Appends.Add(record);
            return record;
        }
    }
}
