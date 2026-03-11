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
public class RuleProfileApplicationServiceTests
{
    [TestMethod]
    public void Default_application_service_previews_runtime_lock_and_changes_for_registered_profile()
    {
        DefaultRuleProfileApplicationService service = new(
            new RuleProfileRegistryServiceStub(CreateProfileEntry(includeRulePack: true)),
            new RuntimeLockInstallServiceStub(CreateInstallReceipt("workspace-1")),
            new RuleProfileInstallStateStoreStub(),
            new RuleProfileInstallHistoryStoreStub());

        RuleProfilePreviewReceipt? preview = service.Preview(
            OwnerScope.LocalSingleUser,
            "official.sr5.core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Workspace, "workspace-1"));

        Assert.IsNotNull(preview);
        Assert.AreEqual("official.sr5.core", preview.ProfileId);
        Assert.AreEqual("workspace-1", preview.Target.TargetId);
        Assert.AreEqual("runtime-lock-sha256", preview.RuntimeLock.RuntimeFingerprint);
        Assert.IsTrue(preview.RequiresConfirmation);
        Assert.IsTrue(preview.Changes.Any(change => string.Equals(change.Kind, RuleProfilePreviewChangeKinds.RulePackSelectionChanged, StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Default_application_service_applies_profile_and_persists_install_state_and_history()
    {
        RuntimeLockInstallServiceStub runtimeLockInstallService = new(CreateInstallReceipt("character-1"));
        RuleProfileInstallStateStoreStub installStateStore = new();
        RuleProfileInstallHistoryStoreStub installHistoryStore = new();
        DefaultRuleProfileApplicationService service = new(
            new RuleProfileRegistryServiceStub(CreateProfileEntry(includeRulePack: false)),
            runtimeLockInstallService,
            installStateStore,
            installHistoryStore);

        RuleProfileApplyReceipt? receipt = service.Apply(
            OwnerScope.LocalSingleUser,
            "official.sr5.core",
            new RuleProfileApplyTarget(RuleProfileApplyTargetKinds.Character, "character-1"));

        Assert.IsNotNull(receipt);
        Assert.AreEqual(RuleProfileApplyOutcomes.Applied, receipt.Outcome);
        Assert.IsNull(receipt.DeferredReason);
        Assert.IsNotNull(receipt.Preview);
        Assert.IsNotNull(receipt.InstallReceipt);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Installed, receipt.InstallReceipt.Outcome);
        Assert.AreEqual("character-1", receipt.Target.TargetId);
        Assert.AreEqual("runtime-lock-sha256", runtimeLockInstallService.ApplyCalls[0].LockId);
        Assert.HasCount(1, installStateStore.Upserts);
        Assert.AreEqual(ArtifactInstallStates.Pinned, installStateStore.Upserts[0].Install.State);
        Assert.AreEqual(RuleProfileApplyTargetKinds.Character, installStateStore.Upserts[0].Install.InstalledTargetKind);
        Assert.AreEqual("character-1", installStateStore.Upserts[0].Install.InstalledTargetId);
        Assert.AreEqual("runtime-lock-sha256", installStateStore.Upserts[0].Install.RuntimeFingerprint);
        Assert.HasCount(1, installHistoryStore.Appends);
        Assert.AreEqual(ArtifactInstallHistoryOperations.Pin, installHistoryStore.Appends[0].Entry.Operation);
    }

    private static RuntimeLockInstallReceipt CreateInstallReceipt(string targetId) => new(
        TargetKind: RuleProfileApplyTargetKinds.Character,
        TargetId: targetId,
        Outcome: RuntimeLockInstallOutcomes.Installed,
        RuntimeLock: new ResolvedRuntimeLock(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles: [],
            RulePacks: [],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256"),
        InstalledAtUtc: DateTimeOffset.Parse("2026-03-06T12:15:00+00:00"),
        RebindNotices: []);

    private static RuleProfileRegistryEntry CreateProfileEntry(bool includeRulePack)
    {
        List<RuleProfilePackSelection> rulePacks = [];
        if (includeRulePack)
        {
            rulePacks.Add(new RuleProfilePackSelection(
                new ArtifactVersionReference("house-rules", "1.0.0"),
                Required: true,
                EnabledByDefault: true));
        }

        return new RuleProfileRegistryEntry(
            new RuleProfileManifest(
                ProfileId: "official.sr5.core",
                Title: "Official SR5 Core",
                Description: "Curated runtime.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.General,
                CatalogKind: RuleProfileCatalogKinds.Official,
                RulePacks: rulePacks,
                DefaultToggles: [],
                RuntimeLock: new ResolvedRuntimeLock(
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
                    RulePacks: includeRulePack ? [new ArtifactVersionReference("house-rules", "1.0.0")] : [],
                    ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                    EngineApiVersion: "rulepack-v1",
                    RuntimeFingerprint: "runtime-lock-sha256"),
                UpdateChannel: RuleProfileUpdateChannels.Stable),
            new RuleProfilePublicationMetadata(
                OwnerId: "system",
                Visibility: includeRulePack ? ArtifactVisibilityModes.LocalOnly : ArtifactVisibilityModes.Public,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares: []),
            new ArtifactInstallState(ArtifactInstallStates.Available));
    }

    private sealed class RuleProfileRegistryServiceStub : IRuleProfileRegistryService
    {
        private readonly RuleProfileRegistryEntry _entry;

        public RuleProfileRegistryServiceStub(RuleProfileRegistryEntry entry)
        {
            _entry = entry;
        }

        public IReadOnlyList<RuleProfileRegistryEntry> List(OwnerScope owner, string? rulesetId = null) => [_entry];

        public RuleProfileRegistryEntry? Get(OwnerScope owner, string profileId, string? rulesetId = null)
        {
            return string.Equals(profileId, _entry.Manifest.ProfileId, StringComparison.Ordinal)
                ? _entry
                : null;
        }
    }

    private sealed class RuntimeLockInstallServiceStub : IRuntimeLockInstallService
    {
        private readonly RuntimeLockInstallReceipt _receipt;

        public RuntimeLockInstallServiceStub(RuntimeLockInstallReceipt receipt)
        {
            _receipt = receipt;
        }

        public List<(string LockId, RuleProfileApplyTarget Target, string? RulesetId)> ApplyCalls { get; } = [];

        public RuntimeLockInstallPreviewReceipt? Preview(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null)
            => null;

        public RuntimeLockInstallReceipt? Apply(OwnerScope owner, string lockId, RuleProfileApplyTarget target, string? rulesetId = null)
        {
            ApplyCalls.Add((lockId, target, rulesetId));
            return _receipt with
            {
                TargetKind = target.TargetKind,
                TargetId = target.TargetId
            };
        }
    }

    private sealed class RuleProfileInstallStateStoreStub : IRuleProfileInstallStateStore
    {
        public List<RuleProfileInstallRecord> Upserts { get; } = [];

        public IReadOnlyList<RuleProfileInstallRecord> List(OwnerScope owner, string? rulesetId = null) => Upserts;

        public RuleProfileInstallRecord? Get(OwnerScope owner, string profileId, string rulesetId)
        {
            return Upserts.LastOrDefault(record =>
                string.Equals(record.ProfileId, profileId, StringComparison.Ordinal)
                && string.Equals(record.RulesetId, rulesetId, StringComparison.Ordinal));
        }

        public RuleProfileInstallRecord Upsert(OwnerScope owner, RuleProfileInstallRecord record)
        {
            Upserts.Add(record);
            return record;
        }
    }

    private sealed class RuleProfileInstallHistoryStoreStub : IRuleProfileInstallHistoryStore
    {
        public List<RuleProfileInstallHistoryRecord> Appends { get; } = [];

        public IReadOnlyList<RuleProfileInstallHistoryRecord> List(OwnerScope owner, string? rulesetId = null) => Appends;

        public IReadOnlyList<RuleProfileInstallHistoryRecord> GetHistory(OwnerScope owner, string profileId, string rulesetId) => Appends;

        public RuleProfileInstallHistoryRecord Append(OwnerScope owner, RuleProfileInstallHistoryRecord record)
        {
            Appends.Add(record);
            return record;
        }
    }
}
