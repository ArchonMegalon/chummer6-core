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
public class RulePackInstallServiceTests
{
    [TestMethod]
    public void Default_install_service_previews_rulepack_changes_and_runtime_warnings()
    {
        DefaultRulePackInstallService service = new(
            new RulePackRegistryServiceStub(CreateEntry(ArtifactInstallStates.Available)),
            new RulePackInstallStateStoreStub(),
            new RulePackInstallHistoryStoreStub());

        RulePackInstallPreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            "house-rules",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.SessionLedger, "session-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(preview);
        Assert.AreEqual("house-rules", preview.PackId);
        Assert.IsTrue(preview.RequiresConfirmation);
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.Kind, RulePackInstallPreviewChangeKinds.RuntimeReviewRequired, StringComparison.Ordinal)));
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.Kind, RulePackInstallPreviewChangeKinds.SessionReplayRequired, StringComparison.Ordinal)));
        Assert.IsTrue(preview.Warnings.Any(warning => string.Equals(warning.Kind, RuntimeInspectorWarningKinds.Trust, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Default_install_service_persists_install_state_and_history_for_new_rulepack_installs()
    {
        RulePackInstallStateStoreStub installStateStore = new();
        RulePackInstallHistoryStoreStub installHistoryStore = new();
        DefaultRulePackInstallService service = new(
            new RulePackRegistryServiceStub(CreateEntry(ArtifactInstallStates.Available)),
            installStateStore,
            installHistoryStore);

        RulePackInstallReceipt? receipt = service.Apply(
            new OwnerScope("alice"),
            "house-rules",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(receipt);
        Assert.AreEqual(RulePackInstallOutcomes.Applied, receipt.Outcome);
        Assert.AreEqual(ArtifactInstallStates.Installed, receipt.Install.State);
        Assert.AreEqual("workspace-1", receipt.Install.InstalledTargetId);
        Assert.HasCount(1, installStateStore.Upserts);
        Assert.HasCount(1, installHistoryStore.Appends);
        Assert.AreEqual(ArtifactInstallHistoryOperations.Install, installHistoryStore.Appends[0].Entry.Operation);
    }

    [TestMethod]
    public void Default_install_service_returns_already_installed_when_target_matches_existing_state()
    {
        RulePackRegistryEntry entry = CreateEntry(
            ArtifactInstallStates.Installed,
            installedTargetKind: RuleProfileApplyTargetKinds.Workspace,
            installedTargetId: "workspace-1");
        RulePackInstallStateStoreStub installStateStore = new();
        RulePackInstallHistoryStoreStub installHistoryStore = new();
        DefaultRulePackInstallService service = new(
            new RulePackRegistryServiceStub(entry),
            installStateStore,
            installHistoryStore);

        RulePackInstallReceipt? receipt = service.Apply(
            OwnerScope.LocalSingleUser,
            "house-rules",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"),
            RulesetDefaults.Sr5);

        Assert.IsNotNull(receipt);
        Assert.AreEqual(RulePackInstallOutcomes.AlreadyInstalled, receipt.Outcome);
        Assert.IsEmpty(installStateStore.Upserts);
        Assert.IsEmpty(installHistoryStore.Appends);
    }

    private static RulePackRegistryEntry CreateEntry(string state, string? installedTargetKind = null, string? installedTargetId = null)
    {
        return new RulePackRegistryEntry(
            new RulePackManifest(
                PackId: "house-rules",
                Version: "1.0.0",
                Title: "House Rules",
                Author: "GM",
                Description: "Campaign overlay.",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "rulepack-v1",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: ArtifactVisibilityModes.LocalOnly,
                TrustTier: ArtifactTrustTiers.LocalOnly,
                Assets: [],
                Capabilities:
                [
                    new RulePackCapabilityDescriptor(
                        CapabilityId: RulePackCapabilityIds.ValidateCharacter,
                        AssetKind: RulePackAssetKinds.Lua,
                        AssetMode: RulePackAssetModes.AddProvider,
                        Explainable: true)
                ],
                ExecutionPolicies: []),
            new RulePackPublicationMetadata(
                OwnerId: "alice",
                Visibility: ArtifactVisibilityModes.LocalOnly,
                PublicationStatus: RulePackPublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(state, InstalledTargetKind: installedTargetKind, InstalledTargetId: installedTargetId));
    }

    private sealed class RulePackRegistryServiceStub : IRulePackRegistryService
    {
        private readonly RulePackRegistryEntry _entry;

        public RulePackRegistryServiceStub(RulePackRegistryEntry entry)
        {
            _entry = entry;
        }

        public IReadOnlyList<RulePackRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => [_entry];

        public RulePackRegistryEntry? Get(OwnerScope owner, string packId, string? rulesetId = null)
        {
            return string.Equals(packId, _entry.Manifest.PackId, StringComparison.Ordinal) ? _entry : null;
        }
    }

    private sealed class RulePackInstallStateStoreStub : IRulePackInstallStateStore
    {
        public List<RulePackInstallRecord> Upserts { get; } = [];

        public IReadOnlyList<RulePackInstallRecord> List(OwnerScope owner, string? rulesetId = null) => Upserts;

        public RulePackInstallRecord? Get(OwnerScope owner, string packId, string version, string rulesetId)
        {
            return Upserts.LastOrDefault(record =>
                string.Equals(record.PackId, packId, StringComparison.Ordinal)
                && string.Equals(record.Version, version, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RulePackInstallRecord Upsert(OwnerScope owner, RulePackInstallRecord record)
        {
            Upserts.Add(record);
            return record;
        }
    }

    private sealed class RulePackInstallHistoryStoreStub : IRulePackInstallHistoryStore
    {
        public List<RulePackInstallHistoryRecord> Appends { get; } = [];

        public IReadOnlyList<RulePackInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => Appends;

        public IReadOnlyList<RulePackInstallHistoryRecord> GetHistory(OwnerScope owner, string packId, string version, string rulesetId) => Appends;

        public RulePackInstallHistoryRecord Append(OwnerScope owner, RulePackInstallHistoryRecord record)
        {
            Appends.Add(record);
            return record;
        }
    }
}
