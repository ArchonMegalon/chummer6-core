#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Assets;
using Chummer.Contracts.Campaign;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Content;
using Chummer.Contracts.Diagnostics;
using Chummer.Contracts.Journal;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Presentation;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Contracts.Trackers;
using Chummer.Contracts.Workspaces;
using Chummer.Desktop.Runtime;
using Chummer.Infrastructure.Xml;
using Chummer.Presentation;
using Chummer.Rulesets.Sr4;
using Chummer.Rulesets.Sr5;
using Chummer.Rulesets.Sr6;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class RulesetSeamContractsTests
{
    [TestMethod]
    public void Workspace_models_keep_native_format_defaults_but_require_explicit_ruleset_inputs()
    {
        WorkspaceDocument workspaceDocument = new("<character />", RulesetId: RulesetDefaults.Sr5);
        WorkspaceImportDocument importDocument = new("<character />", RulesetId: RulesetDefaults.Sr5);
        WorkspaceSaveReceipt saveReceipt = new(new CharacterWorkspaceId("ws-1"), DocumentLength: 128, RulesetId: RulesetDefaults.Sr5);
        WorkspaceDownloadReceipt downloadReceipt = new(
            new CharacterWorkspaceId("ws-1"),
            WorkspaceDocumentFormat.NativeXml,
            ContentBase64: "PGNoYXJhY3RlciAvPg==",
            FileName: "ws-1.chum5",
            DocumentLength: 128,
            RulesetId: RulesetDefaults.Sr5);
        WorkspaceListItem listItem = new(
            new CharacterWorkspaceId("ws-1"),
            Summary: new Chummer.Contracts.Characters.CharacterFileSummary(
                Name: "Test",
                Alias: "Alias",
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 0m,
                Nuyen: 0m,
                Created: true),
            LastUpdatedUtc: DateTimeOffset.UtcNow,
            RulesetId: RulesetDefaults.Sr5);
        WorkspacePayloadEnvelope envelope = new(RulesetDefaults.Sr5, SchemaVersion: 1, PayloadKind: "workspace", Payload: "{}");

        Assert.AreEqual(RulesetDefaults.Sr5, workspaceDocument.State.RulesetId);
        Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, workspaceDocument.Format);
        Assert.AreEqual(1, workspaceDocument.State.SchemaVersion);
        Assert.AreEqual("workspace", workspaceDocument.State.PayloadKind);
        Assert.AreEqual("<character />", workspaceDocument.State.Payload);
        Assert.AreEqual(RulesetDefaults.Sr5, workspaceDocument.PayloadEnvelope.RulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, importDocument.RulesetId);
        Assert.AreEqual(WorkspaceDocumentFormat.NativeXml, importDocument.Format);
        Assert.AreEqual(RulesetDefaults.Sr5, saveReceipt.RulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, downloadReceipt.RulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, listItem.RulesetId);
        Assert.AreEqual(RulesetDefaults.Sr5, envelope.RulesetId);
    }

    [TestMethod]
    public void Artifact_taxonomy_distinguishes_rulepacks_buildkits_content_bundles_and_runtime_locks()
    {
        ContentBundleDescriptor contentBundle = new(
            BundleId: "sr5-core",
            RulesetId: RulesetDefaults.Sr5,
            Version: "2026.03.06",
            Title: "SR5 Core Bundle",
            Description: "Official base data.",
            AssetPaths: ["data/", "lang/"]);
        RulePackManifest rulePack = new(
            PackId: "house-rules",
            Version: "1.2.0",
            Title: "House Rules",
            Author: "GM",
            Description: "Campaign-specific runtime changes.",
            Targets: [RulesetDefaults.Sr5],
            EngineApiVersion: "rulepack-v1",
            DependsOn: [],
            ConflictsWith: [],
            Visibility: ArtifactVisibilityModes.Shared,
            TrustTier: ArtifactTrustTiers.Private,
            Assets:
            [
                new RulePackAssetDescriptor(
                    Kind: RulePackAssetKinds.Xml,
                    Mode: RulePackAssetModes.MergeCatalog,
                    RelativePath: "data/qualities.xml",
                    Checksum: "sha256:abc")
            ],
            Capabilities:
            [
                new RulePackCapabilityDescriptor(
                    CapabilityId: RulePackCapabilityIds.ValidateCharacter,
                    AssetKind: RulePackAssetKinds.Lua,
                    AssetMode: RulePackAssetModes.AddProvider,
                    Explainable: true,
                    SessionSafe: false)
            ],
            ExecutionPolicies:
            [
                new RulePackExecutionPolicyHint(
                    Environment: RulePackExecutionEnvironments.HostedServer,
                    PolicyMode: RulePackExecutionPolicyModes.ReviewRequired,
                    MinimumTrustTier: ArtifactTrustTiers.Curated,
                    AllowedAssetModes: [RulePackAssetModes.AddProvider, RulePackAssetModes.WrapProvider])
            ]);
        BuildKitManifest buildKit = new(
            BuildKitId: "street-sam-starter",
            Version: "1.0.0",
            Title: "Street Sam Starter",
            Description: "Chargen starter kit.",
            Targets: [RulesetDefaults.Sr5],
            RuntimeRequirements:
            [
                new BuildKitRuntimeRequirement(
                    RulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprints: ["runtime-lock-sha256"],
                    RequiredRulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")])
            ],
            Prompts:
            [
                new BuildKitPromptDescriptor(
                    PromptId: "weapon-focus",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: "Preferred Combat Focus",
                    Options:
                    [
                        new BuildKitPromptOption("melee", "Melee"),
                        new BuildKitPromptOption("ranged", "Ranged")
                    ],
                    Required: true)
            ],
            Actions:
            [
                new BuildKitActionDescriptor(
                    ActionId: "grant-starting-bundle",
                    Kind: BuildKitActionKinds.AddBundle,
                    TargetId: "starter/street-sam",
                    PromptId: "weapon-focus",
                    Notes: "Apply the matching starter bundle.")
            ],
            Visibility: ArtifactVisibilityModes.Shared,
            TrustTier: ArtifactTrustTiers.Curated);
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles: [contentBundle],
            RulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["validate.character"] = "house-rules/validate.character"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");

        Assert.AreEqual("street-sam-starter", buildKit.BuildKitId);
        Assert.AreEqual(BuildKitPromptKinds.Choice, buildKit.Prompts[0].Kind);
        Assert.AreEqual(BuildKitActionKinds.AddBundle, buildKit.Actions[0].Kind);
        Assert.AreEqual("runtime-lock-sha256", buildKit.RuntimeRequirements[0].RequiredRuntimeFingerprints[0]);
        Assert.AreEqual("house-rules", rulePack.PackId);
        Assert.AreEqual("sr5-core", contentBundle.BundleId);
        Assert.AreEqual("runtime-lock-sha256", runtimeLock.RuntimeFingerprint);
        Assert.AreEqual(RulePackAssetModes.MergeCatalog, rulePack.Assets[0].Mode);
        Assert.AreEqual(RulePackAssetKinds.Xml, rulePack.Assets[0].Kind);
        Assert.AreEqual(RulePackCapabilityIds.ValidateCharacter, rulePack.Capabilities[0].CapabilityId);
        Assert.AreEqual(RulePackExecutionEnvironments.HostedServer, rulePack.ExecutionPolicies[0].Environment);
        Assert.AreEqual(ArtifactVisibilityModes.Shared, buildKit.Visibility);
        Assert.AreEqual(ArtifactTrustTiers.Private, rulePack.TrustTier);
    }

    [TestMethod]
    public void Ruleprofile_taxonomy_distinguishes_curated_install_targets_from_rulepacks_and_runtime_locks()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core Bundle",
                    Description: "Official base data.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        RuleProfileManifest profile = new(
            ProfileId: "official.sr5.core",
            Title: "Official SR5 Core",
            Description: "Curated default runtime profile.",
            RulesetId: RulesetDefaults.Sr5,
            Audience: RuleProfileAudienceKinds.General,
            CatalogKind: RuleProfileCatalogKinds.Official,
            RulePacks:
            [
                new RuleProfilePackSelection(
                    RulePack: new ArtifactVersionReference("house-rules", "1.2.0"),
                    Required: true,
                    EnabledByDefault: true)
            ],
            DefaultToggles:
            [
                new RuleProfileDefaultToggle(
                    ToggleId: "creation.street-scum",
                    Value: "false",
                    Label: "Street Scum")
            ],
            RuntimeLock: runtimeLock,
            UpdateChannel: RuleProfileUpdateChannels.Stable,
            Notes: "Default install target.");
        RuleProfileRegistryEntry entry = new(
            profile,
            new RuleProfilePublicationMetadata(
                OwnerId: "system",
                Visibility: ArtifactVisibilityModes.Public,
                PublicationStatus: RuleProfilePublicationStatuses.Published,
                Review: new RulePackReviewDecision(RulePackReviewStates.NotRequired),
                Shares:
                [
                    new RulePackShareGrant(
                        SubjectKind: RulePackShareSubjectKinds.PublicCatalog,
                        SubjectId: "profiles",
                        AccessLevel: RulePackShareAccessLevels.Install)
                ]),
            new ArtifactInstallState(ArtifactInstallStates.Available));

        Assert.AreEqual("official.sr5.core", entry.Manifest.ProfileId);
        Assert.AreEqual(RuleProfileCatalogKinds.Official, entry.Manifest.CatalogKind);
        Assert.AreEqual(RuleProfileAudienceKinds.General, entry.Manifest.Audience);
        Assert.AreEqual("house-rules", entry.Manifest.RulePacks[0].RulePack.Id);
        Assert.AreEqual("runtime-lock-sha256", entry.Manifest.RuntimeLock.RuntimeFingerprint);
        Assert.AreEqual(RuleProfileUpdateChannels.Stable, entry.Manifest.UpdateChannel);
        Assert.AreEqual(RuleProfilePublicationStatuses.Published, entry.Publication.PublicationStatus);
    }

    [TestMethod]
    public void Ruleprofile_application_contracts_define_preview_and_applied_install_receipts()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core Bundle",
                    Description: "Official base data.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");
        RuleProfilePreviewReceipt preview = new(
            ProfileId: "official.sr5.core",
            Target: target,
            RuntimeLock: runtimeLock,
            Changes:
            [
                new RuleProfilePreviewItem(
                    Kind: RuleProfilePreviewChangeKinds.RuntimeLockPinned,
                    Summary: "Pin runtime.",
                    SubjectId: runtimeLock.RuntimeFingerprint),
                new RuleProfilePreviewItem(
                    Kind: RuleProfilePreviewChangeKinds.RulePackSelectionChanged,
                    Summary: "Apply rulepack selection.",
                    SubjectId: "official.sr5.core",
                    RequiresConfirmation: true)
            ],
            Warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Trust,
                    Severity: RuntimeInspectorWarningSeverityLevels.Info,
                    Message: "Local-only profile.")
            ],
            RequiresConfirmation: true);
        RuleProfileApplyReceipt receipt = new(
            ProfileId: "official.sr5.core",
            Target: target,
            Outcome: RuleProfileApplyOutcomes.Applied,
            Preview: preview,
            InstallReceipt: new RuntimeLockInstallReceipt(
                TargetKind: target.TargetKind,
                TargetId: target.TargetId,
                Outcome: RuntimeLockInstallOutcomes.Installed,
                RuntimeLock: runtimeLock,
                InstalledAtUtc: DateTimeOffset.Parse("2026-03-06T12:15:00+00:00"),
                RebindNotices: []));

        Assert.AreEqual(RuleProfileApplyTargetKinds.Workspace, preview.Target.TargetKind);
        Assert.AreEqual(RuleProfilePreviewChangeKinds.RulePackSelectionChanged, preview.Changes[1].Kind);
        Assert.IsTrue(preview.RequiresConfirmation);
        Assert.AreEqual(RuleProfileApplyOutcomes.Applied, receipt.Outcome);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Installed, receipt.InstallReceipt?.Outcome);
        Assert.IsNotNull(receipt.Preview.RuntimeLock);
    }

    [TestMethod]
    public void Session_taxonomy_distinguishes_ledger_snapshot_and_runtime_bundle()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core Bundle",
                    Description: "Official base data.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["session.quick-actions"] = "house-rules/session.quick-actions"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: runtimeLock.RuntimeFingerprint);
        CharacterVersion characterVersion = new(
            Reference: baseCharacterVersion,
            RuntimeLock: runtimeLock,
            PayloadEnvelope: new WorkspacePayloadEnvelope(
                RulesetDefaults.Sr5,
                SchemaVersion: 1,
                PayloadKind: "workspace",
                Payload: "<character />"),
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Summary: new CharacterFileSummary(
                Name: "Prime Runner",
                Alias: "Cipher",
                Metatype: "Human",
                BuildMethod: "Priority",
                CreatedVersion: "1.0",
                AppVersion: "1.0",
                Karma: 0m,
                Nuyen: 0m,
                Created: true));
        SessionEventEnvelope sessionEvent = new(
            EventId: "evt-1",
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            DeviceId: "device-1",
            ActorId: "user-1",
            Sequence: 1,
            EventType: SessionEventTypes.TrackerIncrement,
            Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["trackerId"] = RulesetCapabilityBridge.FromObject("stun"),
                ["amount"] = RulesetCapabilityBridge.FromObject(1)
            },
            CreatedAtUtc: DateTimeOffset.UtcNow);
        SessionLedger ledger = new(
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            Events: [sessionEvent],
            BaselineSnapshotId: "snap-0",
            NextSequence: 2);
        SessionOverlaySnapshot snapshot = new(
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            Trackers:
            [
                new TrackerSnapshot(
                    Definition: new TrackerDefinition(
                        TrackerId: "stun",
                        Category: TrackerCategories.Condition,
                        Label: "Stun",
                        DefaultValue: 0,
                        MinimumValue: 0,
                        MaximumValue: 10,
                        Thresholds:
                        [
                            new TrackerThresholdDefinition(
                                ThresholdId: "healthy",
                                Value: 3,
                                Label: "Healthy",
                                Status: "ok")
                        ]),
                    CurrentValue: 1,
                    ThresholdState: "healthy")
            ],
            ActiveEffects:
            [
                new SessionEffectState(
                    EffectId: "wounded",
                    Label: "Wounded",
                    IsActive: true,
                    SourceEventId: sessionEvent.EventId)
            ],
            PinnedQuickActions:
            [
                new SessionQuickActionPin(
                    ActionId: "second-wind",
                    Label: "Second Wind",
                    CapabilityId: "session.quick-actions")
            ],
            Notes: ["Took stun from suppressive fire."],
            SyncState: new SessionSyncState(
                Status: SessionSyncStatuses.PendingSync,
                PendingEventCount: 1,
                LastSyncedAtUtc: null));
        SessionRuntimeBundle runtimeBundle = new(
            BundleId: "session-bundle-1",
            BaseCharacterVersion: baseCharacterVersion,
            EngineApiVersion: "session-runtime-v1",
            SignedAtUtc: DateTimeOffset.UtcNow,
            Signature: "sig-1",
            QuickActions:
            [
                new SessionQuickActionPin(
                    ActionId: "second-wind",
                    Label: "Second Wind",
                    CapabilityId: "session.quick-actions")
            ],
            Trackers:
            [
                new TrackerDefinition(
                    TrackerId: "stun",
                    Category: TrackerCategories.Condition,
                    Label: "Stun",
                    DefaultValue: 0,
                    MinimumValue: 0,
                    MaximumValue: 10,
                    Thresholds:
                    [
                        new TrackerThresholdDefinition("healthy", 3, "Healthy", "ok"),
                        new TrackerThresholdDefinition("wounded", 6, "Wounded", "warn"),
                        new TrackerThresholdDefinition("critical", 9, "Critical", "critical")
                    ])
            ],
            ReducerBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tracker.increment"] = "session-runtime/stun.increment"
            });

        Assert.AreEqual("charv-1", characterVersion.Reference.VersionId);
        Assert.AreEqual("runtime-lock-sha256", characterVersion.RuntimeLock.RuntimeFingerprint);
        Assert.AreEqual("evt-1", ledger.Events[0].EventId);
        Assert.AreEqual(SessionEventTypes.TrackerIncrement, ledger.Events[0].EventType);
        Assert.AreEqual("overlay-1", snapshot.OverlayId);
        Assert.AreEqual("char-1", snapshot.BaseCharacterVersion.CharacterId);
        Assert.AreEqual(SessionSyncStatuses.PendingSync, snapshot.SyncState.Status);
        Assert.AreEqual(TrackerCategories.Condition, snapshot.Trackers[0].Definition.Category);
        Assert.AreEqual("session-bundle-1", runtimeBundle.BundleId);
        Assert.AreEqual("runtime-lock-sha256", runtimeBundle.BaseCharacterVersion.RuntimeFingerprint);
        Assert.AreEqual(10, runtimeBundle.Trackers[0].MaximumValue);
        Assert.AreEqual("session-runtime/stun.increment", runtimeBundle.ReducerBindings["tracker.increment"]);
    }

    [TestMethod]
    public void Linked_asset_taxonomy_distinguishes_contact_assets_from_character_sections()
    {
        LinkedAssetReference assetReference = new(
            AssetId: "contact-1",
            VersionId: "contactv-1",
            AssetType: "contact",
            Visibility: LinkedAssetVisibilityModes.Private);
        ContactAsset contactAsset = new(
            Reference: assetReference,
            Name: "Nines",
            Role: "Fixer",
            Location: "Seattle",
            Connection: 4,
            Loyalty: 3,
            Notes: "Reusable campaign contact.");
        CharacterContactLink contactLink = new(
            CharacterId: "char-1",
            Contact: assetReference,
            Overrides: new ContactLinkOverride(
                DisplayName: "Nines (Runner Team)",
                Loyalty: 4),
            IsFavorite: true);
        CharacterContactsSection contactsSection = new(
            Count: 1,
            Contacts:
            [
                new CharacterContactSummary(
                    Name: "Nines",
                    Role: "Fixer",
                    Location: "Seattle",
                    Connection: 4,
                    Loyalty: 3)
            ]);

        Assert.AreEqual("contact", contactAsset.Reference.AssetType);
        Assert.AreEqual("contact-1", contactLink.Contact.AssetId);
        Assert.IsTrue(contactLink.IsFavorite);
        Assert.AreEqual("Nines (Runner Team)", contactLink.Overrides.DisplayName);
        Assert.AreEqual(1, contactsSection.Count);
        Assert.AreEqual("Nines", contactsSection.Contacts[0].Name);
    }

    [TestMethod]
    public void Design_token_contracts_define_shared_renderer_preferences()
    {
        DesignTokenSet designTokens = new(
            Theme: ThemeModes.Dark,
            TypographyScale: TypographyScales.Large,
            Density: DensityModes.Comfortable,
            Contrast: ContrastModes.High,
            TouchTargetSize: TouchTargetModes.Large);

        Assert.AreEqual(ThemeModes.Dark, designTokens.Theme);
        Assert.AreEqual(TypographyScales.Large, designTokens.TypographyScale);
        Assert.AreEqual(DensityModes.Comfortable, designTokens.Density);
        Assert.AreEqual(ContrastModes.High, designTokens.Contrast);
        Assert.AreEqual(TouchTargetModes.Large, designTokens.TouchTargetSize);
    }

    [TestMethod]
    public void Session_sync_contracts_define_batch_replay_and_conflict_vocabulary()
    {
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-sha256");
        SessionEventEnvelope sessionEvent = new(
            EventId: "evt-1",
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            DeviceId: "device-1",
            ActorId: "user-1",
            Sequence: 1,
            EventType: SessionEventTypes.TrackerIncrement,
            Payload: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
            {
                ["trackerId"] = RulesetCapabilityBridge.FromObject("stun"),
                ["amount"] = RulesetCapabilityBridge.FromObject(1)
            },
            CreatedAtUtc: DateTimeOffset.UtcNow);
        SessionSyncBatch batch = new(
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            Events: [sessionEvent],
            ClientCursor: "cursor-1");
        SessionReplayReceipt replay = new(
            AppliedCharacterVersion: baseCharacterVersion,
            AcceptedEventCount: 1,
            ReplayedEventCount: 1,
            RuntimeRebindRequired: false,
            ManualResolutionRequired: false);
        SessionSyncReceipt receipt = new(
            OverlayId: "overlay-1",
            Replay: replay,
            PendingEvents:
            [
                new SessionPendingEventState(
                    EventId: "evt-1",
                    Sequence: 1,
                    Status: SessionSyncStatuses.Synced,
                    CreatedAtUtc: sessionEvent.CreatedAtUtc)
            ],
            Conflicts:
            [
                new SessionConflictDiagnostic(
                    EventId: "evt-2",
                    Kind: SessionConflictKinds.RuntimeFingerprintMismatch,
                    Message: "Runtime lock changed before replay.",
                    RequiresManualResolution: true,
                    ConflictingEventId: "evt-server-1")
            ],
            ServerCursor: "cursor-2");

        Assert.AreEqual("overlay-1", batch.OverlayId);
        Assert.AreEqual("cursor-1", batch.ClientCursor);
        Assert.AreEqual(1, receipt.Replay.AcceptedEventCount);
        Assert.AreEqual(SessionSyncStatuses.Synced, receipt.PendingEvents[0].Status);
        Assert.AreEqual(SessionConflictKinds.RuntimeFingerprintMismatch, receipt.Conflicts[0].Kind);
        Assert.IsTrue(receipt.Conflicts[0].RequiresManualResolution);
        Assert.AreEqual("cursor-2", receipt.ServerCursor);
    }

    [TestMethod]
    public void Declarative_rule_override_contracts_distinguish_packable_rule_changes_from_rulepack_assets_and_buildkits()
    {
        DeclarativeRuleOverrideSet overrideSet = new(
            SetId: "street-scum-overrides",
            RulesetId: RulesetDefaults.Sr5,
            Overrides:
            [
                new DeclarativeRuleOverride(
                    OverrideId: "street-scum-starting-nuyen",
                    Mode: DeclarativeRuleOverrideModes.ModifyCap,
                    Target: new DeclarativeRuleTarget(
                        TargetKind: DeclarativeRuleTargetKinds.Cap,
                        TargetId: "starting-nuyen",
                        Path: "creation.starting-nuyen",
                        Scope: "chargen"),
                    Value: new DeclarativeRuleValue(
                        ValueKind: DeclarativeRuleValueKinds.Number,
                        Value: "6000"),
                    Conditions:
                    [
                        new DeclarativeRuleCondition(
                            Field: "creationProfile",
                            Operator: DeclarativeRuleConditionOperators.EqualTo,
                            Value: "street-scum",
                            ValueKind: DeclarativeRuleValueKinds.String)
                    ],
                    CapabilityIds: [RulePackCapabilityIds.CreationProfile])
            ]);
        RulePackAssetDescriptor asset = new(
            Kind: RulePackAssetKinds.DeclarativeRules,
            Mode: RulePackAssetModes.ModifyCap,
            RelativePath: "rules/street-scum.json",
            Checksum: "sha256:def");
        BuildKitActionDescriptor buildKitAction = new(
            ActionId: "grant-starting-bundle",
            Kind: BuildKitActionKinds.AddBundle,
            TargetId: "starter/street-scum");

        Assert.AreEqual("street-scum-overrides", overrideSet.SetId);
        Assert.AreEqual(DeclarativeRuleOverrideModes.ModifyCap, overrideSet.Overrides[0].Mode);
        Assert.AreEqual(DeclarativeRuleTargetKinds.Cap, overrideSet.Overrides[0].Target.TargetKind);
        Assert.AreEqual(DeclarativeRuleConditionOperators.EqualTo, overrideSet.Overrides[0].Conditions[0].Operator);
        Assert.AreEqual(DeclarativeRuleValueKinds.Number, overrideSet.Overrides[0].Value.ValueKind);
        Assert.AreEqual(RulePackAssetKinds.DeclarativeRules, asset.Kind);
        Assert.AreEqual(DeclarativeRuleOverrideModes.ModifyCap, asset.Mode);
        Assert.AreEqual(BuildKitActionKinds.AddBundle, buildKitAction.Kind);
        Assert.AreNotEqual(buildKitAction.Kind, overrideSet.Overrides[0].Mode);
    }

    [TestMethod]
    public void Workflow_surface_contracts_define_legacy_workbench_workflows_and_shell_regions()
    {
        WorkflowDefinition libraryShell = new(
            WorkflowId: WorkflowDefinitionIds.LibraryShell,
            Title: "Library Shell",
            SurfaceIds: ["shell.menu", "shell.workspace-left", "shell.section"],
            RequiresOpenWorkspace: false);
        WorkflowDefinition sessionDashboard = new(
            WorkflowId: WorkflowDefinitionIds.SessionDashboard,
            Title: "Session Dashboard",
            SurfaceIds: ["session.summary", "session.quick-actions"],
            RequiresOpenWorkspace: true,
            MobileOptimized: true);
        WorkflowSurfaceDefinition shellMenu = new(
            SurfaceId: "shell.menu",
            WorkflowId: WorkflowDefinitionIds.LibraryShell,
            Kind: WorkflowSurfaceKinds.ShellRegion,
            RegionId: ShellRegionIds.MenuBar,
            LayoutToken: WorkflowLayoutTokens.ShellFrame,
            ActionIds: ["file", "tools"],
            RendererHints: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["desktopControl"] = "ShellMenuBarControl"
            });
        WorkflowSurfaceDefinition careerSection = new(
            SurfaceId: "career.section",
            WorkflowId: WorkflowDefinitionIds.CareerWorkbench,
            Kind: WorkflowSurfaceKinds.Workbench,
            RegionId: ShellRegionIds.SectionPane,
            LayoutToken: WorkflowLayoutTokens.CareerWorkbench,
            ActionIds: ["summary", "validate", "metadata"]);

        Assert.AreEqual(WorkflowDefinitionIds.LibraryShell, libraryShell.WorkflowId);
        Assert.AreEqual(WorkflowDefinitionIds.SessionDashboard, sessionDashboard.WorkflowId);
        Assert.IsTrue(sessionDashboard.MobileOptimized);
        Assert.AreEqual(WorkflowSurfaceKinds.ShellRegion, shellMenu.Kind);
        Assert.AreEqual(ShellRegionIds.MenuBar, shellMenu.RegionId);
        Assert.AreEqual(WorkflowLayoutTokens.CareerWorkbench, careerSection.LayoutToken);
        Assert.AreEqual(WorkflowDefinitionIds.CareerWorkbench, careerSection.WorkflowId);
        Assert.AreEqual("ShellMenuBarControl", shellMenu.RendererHints!["desktopControl"]);
        CollectionAssert.Contains(libraryShell.SurfaceIds.ToList(), "shell.workspace-left");
    }

    [TestMethod]
    public void Shadow_regression_contracts_define_corpus_baseline_diff_and_waiver_vocabulary()
    {
        ShadowRegressionFixtureDescriptor fixture = new(
            FixtureId: "fuzzy-chargen",
            RulesetId: RulesetDefaults.Sr5,
            FixtureKind: ShadowRegressionFixtureKinds.CharacterFile,
            RelativePath: "Chummer.Tests/TestFiles/Fuzzy-chargen.chum5",
            RulePacks: [new ArtifactVersionReference("house-rules", "1.2.0")],
            LegacyOracle: true);
        ShadowRegressionCorpusDescriptor corpus = new(
            CorpusId: "legacy-sr5-corpus",
            RulesetId: RulesetDefaults.Sr5,
            Fixtures: [fixture]);
        ShadowRegressionMetricBaseline baseline = new(
            FixtureId: fixture.FixtureId,
            MetricKind: ShadowRegressionMetricKinds.DerivedStats,
            SubjectId: "body",
            ExpectedValueJson: "{\"value\":6}");
        ShadowRegressionRunReceipt run = new(
            RunId: "run-1",
            CorpusId: corpus.CorpusId,
            Baselines: [baseline],
            Diffs:
            [
                new ShadowRegressionDiff(
                    FixtureId: fixture.FixtureId,
                    MetricKind: ShadowRegressionMetricKinds.Validation,
                    DiffKind: ShadowRegressionDiffKinds.ValueMismatch,
                    Severity: ShadowRegressionSeverityLevels.Error,
                    SubjectId: "validation.summary",
                    ExpectedValueJson: "{\"warnings\":0}",
                    ActualValueJson: "{\"warnings\":1}",
                    Reason: "New validation warning introduced.",
                    WaiverId: "waiver-1",
                    Explain: new ShadowRegressionExplainReference(
                        TraceId: "trace-1",
                        SubjectId: "validation.summary",
                        ProviderId: "sr5/validate.character",
                        PackId: "house-rules"))
            ],
            AppliedWaivers:
            [
                new ShadowRegressionWaiver(
                    WaiverId: "waiver-1",
                    FixtureId: fixture.FixtureId,
                    MetricKind: ShadowRegressionMetricKinds.Validation,
                    SubjectId: "validation.summary",
                    Reason: "Known legacy discrepancy under review.",
                    DecisionId: "MIG-011")
            ],
            ComparedFixtureCount: 1,
            ComparedMetricCount: 6);

        Assert.AreEqual(ShadowRegressionFixtureKinds.CharacterFile, fixture.FixtureKind);
        Assert.IsTrue(fixture.LegacyOracle);
        Assert.AreEqual("legacy-sr5-corpus", corpus.CorpusId);
        Assert.AreEqual(ShadowRegressionMetricKinds.DerivedStats, baseline.MetricKind);
        Assert.AreEqual(ShadowRegressionDiffKinds.ValueMismatch, run.Diffs[0].DiffKind);
        Assert.AreEqual(ShadowRegressionSeverityLevels.Error, run.Diffs[0].Severity);
        Assert.AreEqual("trace-1", run.Diffs[0].Explain!.TraceId);
        Assert.AreEqual("waiver-1", run.AppliedWaivers[0].WaiverId);
        Assert.AreEqual(6, run.ComparedMetricCount);
    }

    [TestMethod]
    public void Rulepack_compiler_contracts_define_resolution_diagnostics_and_runtime_lock_receipts()
    {
        RulePackCompilerRequest request = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core Bundle",
                    Description: "Official base data.",
                    AssetPaths: ["data/", "lang/"])
            ],
            SelectedRulePacks:
            [
                new ArtifactVersionReference("official-errata", "1.0.0"),
                new ArtifactVersionReference("house-rules", "1.2.0")
            ],
            EngineApiVersion: "rulepack-v1",
            Environment: RulePackExecutionEnvironments.HostedServer,
            MinimumTrustTier: ArtifactTrustTiers.Curated);
        RulePackResolutionResult resolution = new(
            RulesetId: request.RulesetId,
            RequestedRulePacks: request.SelectedRulePacks,
            ResolvedRulePacks: request.SelectedRulePacks,
            Diagnostics:
            [
                new RulePackResolutionDiagnostic(
                    Kind: RulePackResolutionDiagnosticKinds.ReviewRequired,
                    Severity: RulePackResolutionSeverityLevels.Warning,
                    SubjectId: "house-rules",
                    Message: "Private pack requires hosted review.",
                    RelatedPackId: "house-rules")
            ],
            RequiresReview: true);
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: request.RulesetId,
            ContentBundles: request.ContentBundles,
            RulePacks: resolution.ResolvedRulePacks,
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["validate.character"] = "house-rules/validate.character"
            },
            EngineApiVersion: request.EngineApiVersion,
            RuntimeFingerprint: "runtime-lock-sha256");
        RulePackCompileReceipt receipt = new(
            Status: RulePackCompileStatuses.CompiledWithReview,
            Request: request,
            Resolution: resolution,
            RuntimeLock: runtimeLock,
            CompiledAtUtc: DateTimeOffset.UtcNow);

        Assert.AreEqual(RulePackExecutionEnvironments.HostedServer, request.Environment);
        Assert.AreEqual(ArtifactTrustTiers.Curated, request.MinimumTrustTier);
        Assert.AreEqual(RulePackResolutionDiagnosticKinds.ReviewRequired, resolution.Diagnostics[0].Kind);
        Assert.AreEqual(RulePackResolutionSeverityLevels.Warning, resolution.Diagnostics[0].Severity);
        Assert.IsTrue(resolution.RequiresReview);
        Assert.AreEqual(RulePackCompileStatuses.CompiledWithReview, receipt.Status);
        Assert.AreEqual("runtime-lock-sha256", receipt.RuntimeLock!.RuntimeFingerprint);
        Assert.AreEqual("house-rules/validate.character", receipt.RuntimeLock.ProviderBindings["validate.character"]);
    }

    [TestMethod]
    public void Session_merge_and_rebind_contracts_define_event_family_policies_and_replay_outcomes()
    {
        SessionMergePolicy trackerPolicy = new(
            Family: SessionMergeFamilies.Tracker,
            Mode: SessionMergePolicyModes.Additive,
            EventTypes: [SessionEventTypes.TrackerIncrement, SessionEventTypes.TrackerDecrement]);
        SessionMergePolicy notesPolicy = new(
            Family: SessionMergeFamilies.Notes,
            Mode: SessionMergePolicyModes.ConflictMarker,
            EventTypes: [SessionEventTypes.NoteAppend, SessionEventTypes.NoteReplace]);
        CharacterVersionReference priorVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-v1");
        CharacterVersionReference appliedVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-2",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-v2");
        SessionRebindReceipt rebind = new(
            PriorCharacterVersion: priorVersion,
            AppliedCharacterVersion: appliedVersion,
            Outcome: SessionRebindOutcomes.ManualResolutionRequired,
            Diagnostics:
            [
                new SessionRebindDiagnostic(
                    Family: SessionMergeFamilies.Selection,
                    Outcome: SessionRebindOutcomes.ReboundToNewRuntime,
                    Message: "Selection events replayed against the new runtime.",
                    PriorRuntimeFingerprint: priorVersion.RuntimeFingerprint,
                    NewRuntimeFingerprint: appliedVersion.RuntimeFingerprint),
                new SessionRebindDiagnostic(
                    Family: SessionMergeFamilies.Notes,
                    Outcome: SessionRebindOutcomes.ManualResolutionRequired,
                    Message: "Concurrent note replacements need conflict markers.",
                    PriorRuntimeFingerprint: priorVersion.RuntimeFingerprint,
                    NewRuntimeFingerprint: appliedVersion.RuntimeFingerprint)
            ],
            RuntimeFingerprintChanged: true,
            BaseCharacterChanged: true);

        Assert.AreEqual(SessionMergeFamilies.Tracker, trackerPolicy.Family);
        Assert.AreEqual(SessionMergePolicyModes.Additive, trackerPolicy.Mode);
        Assert.AreEqual(SessionMergePolicyModes.ConflictMarker, notesPolicy.Mode);
        Assert.AreEqual(SessionRebindOutcomes.ManualResolutionRequired, rebind.Outcome);
        Assert.IsTrue(rebind.RuntimeFingerprintChanged);
        Assert.IsTrue(rebind.BaseCharacterChanged);
        Assert.AreEqual(SessionMergeFamilies.Notes, rebind.Diagnostics[1].Family);
        Assert.AreEqual("runtime-lock-v2", rebind.Diagnostics[0].NewRuntimeFingerprint);
    }

    [TestMethod]
    public void Session_lifecycle_contracts_define_snapshot_compaction_and_bundle_refresh_receipts()
    {
        CharacterVersionReference version = new(
            CharacterId: "char-1",
            VersionId: "charv-2",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-v2");
        SessionSnapshotBaseline baseline = new(
            SnapshotId: "snap-42",
            OverlayId: "overlay-1",
            BaseCharacterVersion: version,
            ThroughSequence: 42,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            CompactedEventCount: 40);
        SessionCompactionReceipt compaction = new(
            OverlayId: "overlay-1",
            Mode: SessionCompactionModes.IncrementalSnapshot,
            Baseline: baseline,
            NextSequence: 43,
            RetainedPendingEventCount: 2);
        SessionRuntimeBundleRefreshReceipt refresh = new(
            PreviousBundleId: "bundle-1",
            CurrentBundleId: "bundle-2",
            Outcome: SessionRuntimeBundleRefreshOutcomes.Refreshed,
            BaseCharacterVersion: version,
            RuntimeFingerprint: version.RuntimeFingerprint,
            RefreshedAtUtc: DateTimeOffset.UtcNow,
            SignatureChanged: true);

        Assert.AreEqual(SessionCompactionModes.IncrementalSnapshot, compaction.Mode);
        Assert.AreEqual("snap-42", compaction.Baseline.SnapshotId);
        Assert.AreEqual(40, compaction.Baseline.CompactedEventCount);
        Assert.IsTrue(compaction.PendingEventsRetained);
        Assert.AreEqual(SessionRuntimeBundleRefreshOutcomes.Refreshed, refresh.Outcome);
        Assert.AreEqual("bundle-2", refresh.CurrentBundleId);
        Assert.IsTrue(refresh.SignatureChanged);
    }

    [TestMethod]
    public void Buildkit_application_contracts_define_prompt_resolution_validation_and_apply_receipts()
    {
        CharacterVersionReference version = new(
            CharacterId: "char-1",
            VersionId: "charv-3",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-v3");
        BuildKitPromptResolution promptResolution = new(
            PromptId: "weapon-focus",
            OptionId: "melee");
        BuildKitValidationIssue validationIssue = new(
            Kind: BuildKitValidationIssueKinds.MissingRulePack,
            Message: "Required campaign pack is not installed.",
            ActionId: "grant-starting-bundle");
        BuildKitApplicationReceipt receipt = new(
            Status: BuildKitApplicationStatuses.PartiallyApplied,
            BuildKitId: "street-sam-starter",
            WorkspaceId: "ws-1",
            ResolvedPrompts: [promptResolution],
            AppliedActions:
            [
                new BuildKitAppliedAction(
                    ActionId: "grant-starting-bundle",
                    Kind: BuildKitActionKinds.AddBundle,
                    TargetId: "starter/street-sam",
                    Outcome: BuildKitAppliedActionOutcomes.Applied),
                new BuildKitAppliedAction(
                    ActionId: "queue-career-upgrade",
                    Kind: BuildKitActionKinds.QueueCareerUpdate,
                    TargetId: "career/street-sam",
                    Outcome: BuildKitAppliedActionOutcomes.Blocked)
            ],
            Issues: [validationIssue],
            ResultingCharacterVersion: version);

        Assert.AreEqual(BuildKitApplicationStatuses.PartiallyApplied, receipt.Status);
        Assert.AreEqual("weapon-focus", receipt.ResolvedPrompts[0].PromptId);
        Assert.AreEqual(BuildKitValidationIssueKinds.MissingRulePack, receipt.Issues[0].Kind);
        Assert.AreEqual(BuildKitAppliedActionOutcomes.Applied, receipt.AppliedActions[0].Outcome);
        Assert.AreEqual(BuildKitAppliedActionOutcomes.Blocked, receipt.AppliedActions[1].Outcome);
        Assert.AreEqual("charv-3", receipt.ResultingCharacterVersion!.VersionId);
    }

    [TestMethod]
    public void Rulepack_registry_contracts_define_publication_review_share_and_fork_vocabulary()
    {
        RulePackManifest manifest = new(
            PackId: "house-rules",
            Version: "1.2.0",
            Title: "House Rules",
            Author: "GM",
            Description: "Campaign runtime changes.",
            Targets: [RulesetDefaults.Sr5],
            EngineApiVersion: "rulepack-v1",
            DependsOn: [],
            ConflictsWith: [],
            Visibility: ArtifactVisibilityModes.CampaignShared,
            TrustTier: ArtifactTrustTiers.Private,
            Assets:
            [
                new RulePackAssetDescriptor(
                    Kind: RulePackAssetKinds.DeclarativeRules,
                    Mode: RulePackAssetModes.ModifyCap,
                    RelativePath: "rules/house.json",
                    Checksum: "sha256:abc")
            ],
            Capabilities: [],
            ExecutionPolicies: []);
        RulePackPublicationMetadata publication = new(
            OwnerId: "user-1",
            Visibility: ArtifactVisibilityModes.CampaignShared,
            PublicationStatus: RulePackPublicationStatuses.Published,
            Review: new RulePackReviewDecision(
                State: RulePackReviewStates.PendingReview),
            Shares:
            [
                new RulePackShareGrant(
                    SubjectKind: RulePackShareSubjectKinds.Campaign,
                    SubjectId: "campaign-7",
                    AccessLevel: RulePackShareAccessLevels.Install),
                new RulePackShareGrant(
                    SubjectKind: RulePackShareSubjectKinds.User,
                    SubjectId: "user-2",
                    AccessLevel: RulePackShareAccessLevels.Fork)
            ],
            ForkLineage: new RulePackForkLineage(
                RootPackId: "official-errata",
                ParentPackId: "official-errata",
                ParentVersion: "1.0.0",
                IsFork: true),
            PublishedAtUtc: DateTimeOffset.UtcNow);
        RulePackRegistryEntry entry = new(manifest, publication, new ArtifactInstallState(ArtifactInstallStates.Installed));
        RulePackPublicationReceipt receipt = new(
            PackId: manifest.PackId,
            Version: manifest.Version,
            PublicationStatus: publication.PublicationStatus,
            Visibility: publication.Visibility,
            ReviewState: publication.Review.State,
            Shares: publication.Shares,
            ForkLineage: publication.ForkLineage);

        Assert.AreEqual(RulePackPublicationStatuses.Published, entry.Publication.PublicationStatus);
        Assert.AreEqual(RulePackReviewStates.PendingReview, entry.Publication.Review.State);
        Assert.AreEqual(RulePackShareSubjectKinds.Campaign, entry.Publication.Shares[0].SubjectKind);
        Assert.AreEqual(RulePackShareAccessLevels.Fork, entry.Publication.Shares[1].AccessLevel);
        Assert.IsTrue(entry.Publication.ForkLineage!.IsFork);
        Assert.AreEqual("official-errata", receipt.ForkLineage!.RootPackId);
        Assert.AreEqual(ArtifactVisibilityModes.CampaignShared, receipt.Visibility);
    }

    [TestMethod]
    public void Linked_asset_library_contracts_define_registry_share_and_transfer_receipts()
    {
        LinkedAssetReference assetReference = new(
            AssetId: "contact-1",
            VersionId: "contactv-2",
            AssetType: "contact",
            Visibility: LinkedAssetVisibilityModes.CampaignShared);
        LinkedAssetLibraryEntry entry = new(
            OwnerId: "user-1",
            Asset: assetReference,
            Shares:
            [
                new LinkedAssetShareGrant(
                    SubjectKind: LinkedAssetShareSubjectKinds.Campaign,
                    SubjectId: "campaign-7",
                    AccessLevel: LinkedAssetShareAccessLevels.Link),
                new LinkedAssetShareGrant(
                    SubjectKind: LinkedAssetShareSubjectKinds.User,
                    SubjectId: "user-2",
                    AccessLevel: LinkedAssetShareAccessLevels.View)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        LinkedAssetImportReceipt importReceipt = new(
            AssetId: assetReference.AssetId,
            VersionId: assetReference.VersionId,
            AssetType: assetReference.AssetType,
            Format: LinkedAssetTransferFormats.Bundle,
            ImportedCount: 1);
        LinkedAssetExportReceipt exportReceipt = new(
            AssetId: assetReference.AssetId,
            VersionId: assetReference.VersionId,
            AssetType: assetReference.AssetType,
            Format: LinkedAssetTransferFormats.Json,
            FileName: "contact-1.json",
            DocumentLength: 256);

        Assert.AreEqual("user-1", entry.OwnerId);
        Assert.AreEqual(LinkedAssetShareSubjectKinds.Campaign, entry.Shares[0].SubjectKind);
        Assert.AreEqual(LinkedAssetShareAccessLevels.Link, entry.Shares[0].AccessLevel);
        Assert.AreEqual(LinkedAssetTransferFormats.Bundle, importReceipt.Format);
        Assert.AreEqual(LinkedAssetTransferFormats.Json, exportReceipt.Format);
        Assert.AreEqual("contact-1.json", exportReceipt.FileName);
    }

    [TestMethod]
    public void Session_projection_contracts_define_dashboard_cards_groups_banners_and_explain_entries()
    {
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-sha256");
        TrackerSnapshot trackerSnapshot = new(
            Definition: new TrackerDefinition(
                TrackerId: "stun",
                Category: TrackerCategories.Condition,
                Label: "Stun",
                DefaultValue: 0,
                MinimumValue: 0,
                MaximumValue: 10,
                Thresholds:
                [
                    new TrackerThresholdDefinition(
                        ThresholdId: "stun-warning",
                        Value: 8,
                        Label: "Warning",
                        Status: "warning")
                ]),
            CurrentValue: 3,
            ThresholdState: null);
        SessionOverlaySnapshot overlay = new(
            OverlayId: "overlay-1",
            BaseCharacterVersion: baseCharacterVersion,
            Trackers: [trackerSnapshot],
            ActiveEffects:
            [
                new SessionEffectState(
                    EffectId: "effect-1",
                    Label: "Jazz",
                    IsActive: true)
            ],
            PinnedQuickActions:
            [
                new SessionQuickActionPin(
                    ActionId: "fire-weapon",
                    Label: "Fire Weapon",
                    CapabilityId: "session.quick-actions")
            ],
            Notes: ["Take cover."],
            SyncState: new SessionSyncState(
                Status: SessionSyncStatuses.PendingSync,
                PendingEventCount: 2,
                LastSyncedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-5),
                WasReplayed: false,
                RuntimeFingerprintMismatch: false));
        SessionRuntimeBundle runtimeBundle = new(
            BundleId: "bundle-1",
            BaseCharacterVersion: baseCharacterVersion,
            EngineApiVersion: "rulepack-v1",
            SignedAtUtc: DateTimeOffset.UtcNow,
            Signature: "sig-1",
            QuickActions: overlay.PinnedQuickActions,
            Trackers: [trackerSnapshot.Definition],
            ReducerBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tracker.increment"] = "sr5-core/reducers/tracker.increment"
            });
        SessionTrackerGroup trackerGroup = new(
            GroupId: "condition-trackers",
            Label: "Condition",
            Trackers: [trackerSnapshot],
            ExplainEntryId: "explain-1");
        SessionQuickActionGroup quickActionGroup = new(
            GroupId: "combat-actions",
            Label: "Combat",
            Actions:
            [
                new SessionQuickActionDescriptor(
                    ActionId: "fire-weapon",
                    Label: "Fire Weapon",
                    CapabilityId: "session.quick-actions",
                    IsPinned: true,
                    IsEnabled: true,
                    ExplainEntryId: "explain-2")
            ]);
        SessionSyncBanner syncBanner = new(
            BannerId: "sync-status",
            Status: SessionSyncBannerStates.PendingSync,
            Message: "2 events are waiting to sync.",
            PendingEventCount: 2,
            RequiresAttention: true,
            ExplainEntryId: "explain-3");
        SessionDashboardProjection projection = new(
            OverlayId: overlay.OverlayId,
            BaseCharacterVersion: baseCharacterVersion,
            RuntimeFingerprint: baseCharacterVersion.RuntimeFingerprint,
            Overlay: overlay,
            RuntimeBundle: runtimeBundle,
            Sections:
            [
                new SessionDashboardSection(
                    SectionId: "trackers",
                    Kind: SessionDashboardSectionKinds.Trackers,
                    Title: "Trackers",
                    CardIds: ["trackers-card"]),
                new SessionDashboardSection(
                    SectionId: "actions",
                    Kind: SessionDashboardSectionKinds.QuickActions,
                    Title: "Quick Actions",
                    CardIds: ["actions-card"])
            ],
            Cards:
            [
                new SessionDashboardCard(
                    CardId: "trackers-card",
                    Kind: SessionDashboardCardKinds.TrackerGroup,
                    Title: "Condition Tracks",
                    PrimaryValue: "Stun 3/10",
                    GroupId: trackerGroup.GroupId,
                    ExplainEntryId: "explain-1",
                    IsInteractive: true),
                new SessionDashboardCard(
                    CardId: "actions-card",
                    Kind: SessionDashboardCardKinds.QuickActionGroup,
                    Title: "Quick Actions",
                    PrimaryValue: "1 pinned",
                    GroupId: quickActionGroup.GroupId,
                    ExplainEntryId: "explain-2",
                    IsInteractive: true)
            ],
            TrackerGroups: [trackerGroup],
            QuickActionGroups: [quickActionGroup],
            ExplainEntries:
            [
                new SessionExplainEntry(
                    EntryId: "explain-1",
                    Kind: SessionExplainEntryKinds.TrackerThreshold,
                    TitleKey: "session.explain.entry.tracker-threshold.title",
                    TitleParameters: [],
                    SummaryKey: "session.explain.entry.tracker-threshold.summary",
                    SummaryParameters: [new RulesetExplainParameter("threshold", RulesetCapabilityBridge.FromObject(8))],
                    Fragments:
                    [
                        new SessionExplainFragment(
                            FragmentKey: "session.explain.fragment.tracker-threshold",
                            Parameters: [new RulesetExplainParameter("threshold", RulesetCapabilityBridge.FromObject(8))])
                    ],
                    ProviderId: "sr5-core/tracker-threshold",
                    PackId: "sr5-core",
                    GasUsed: 18),
                new SessionExplainEntry(
                    EntryId: "explain-2",
                    Kind: SessionExplainEntryKinds.QuickActionAvailability,
                    TitleKey: "session.explain.entry.quick-action.title",
                    TitleParameters: [new RulesetExplainParameter("actionId", RulesetCapabilityBridge.FromObject("fire-weapon"))],
                    SummaryKey: "session.explain.entry.quick-action.summary",
                    SummaryParameters: [new RulesetExplainParameter("phase", RulesetCapabilityBridge.FromObject("open"))],
                    Fragments:
                    [
                        new SessionExplainFragment(
                            FragmentKey: "session.explain.fragment.quick-action.phase",
                            Parameters: [new RulesetExplainParameter("phase", RulesetCapabilityBridge.FromObject("open"))])
                    ],
                    ProviderId: "sr5-core/quick-action",
                    PackId: "sr5-core",
                    GasUsed: 22)
            ],
            SyncBanner: syncBanner);

        Assert.AreEqual(SessionDashboardSectionKinds.Trackers, projection.Sections[0].Kind);
        Assert.AreEqual(SessionDashboardCardKinds.TrackerGroup, projection.Cards[0].Kind);
        Assert.AreEqual("condition-trackers", projection.Cards[0].GroupId);
        Assert.AreEqual("combat-actions", projection.QuickActionGroups[0].GroupId);
        Assert.AreEqual(SessionSyncBannerStates.PendingSync, projection.SyncBanner!.Status);
        Assert.AreEqual(SessionExplainEntryKinds.QuickActionAvailability, projection.ExplainEntries[1].Kind);
        Assert.AreEqual("sr5-core", projection.ExplainEntries[0].PackId);
    }

    [TestMethod]
    public void Portal_identity_contracts_define_account_session_owner_and_binding_vocabulary()
    {
        PortalAccountProfile account = new(
            OwnerId: "user-1",
            Email: "runner@example.com",
            DisplayName: "Cipher",
            Status: PortalAccountStatuses.Active,
            CreatedAtUtc: DateTimeOffset.UtcNow.AddDays(-30),
            ConfirmedAtUtc: DateTimeOffset.UtcNow.AddDays(-29),
            PreferredRulesetId: RulesetDefaults.Sr5,
            TimeZone: "Europe/Vienna");
        PortalOwnerDescriptor owner = new(
            Scope: new OwnerScope(account.OwnerId),
            Kind: PortalOwnerKinds.Account,
            IsAuthenticated: true,
            ActorId: account.OwnerId,
            DisplayName: account.DisplayName);
        PortalSessionDescriptor session = new(
            SessionId: "session-1",
            Owner: owner.Scope,
            Mode: PortalSessionModes.InteractiveWeb,
            IssuedAtUtc: DateTimeOffset.UtcNow.AddMinutes(-15),
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddHours(8),
            IsPersistent: true,
            DeviceId: "device-1");
        PortalAuthenticationReceipt receipt = new(
            Owner: owner,
            Account: account,
            Session: session,
            Identities:
            [
                new PortalIdentityBinding(
                    ProviderKind: PortalIdentityProviderKinds.Password,
                    SubjectId: "password:user-1",
                    Email: account.Email,
                    EmailVerified: true,
                    LinkedAtUtc: DateTimeOffset.UtcNow.AddDays(-29)),
                new PortalIdentityBinding(
                    ProviderKind: PortalIdentityProviderKinds.PortalBridge,
                    SubjectId: "portal-bridge:user-1",
                    Email: account.Email)
            ]);

        Assert.AreEqual(PortalAccountStatuses.Active, receipt.Account.Status);
        Assert.AreEqual(PortalOwnerKinds.Account, receipt.Owner.Kind);
        Assert.AreEqual(PortalSessionModes.InteractiveWeb, receipt.Session.Mode);
        Assert.AreEqual(PortalIdentityProviderKinds.Password, receipt.Identities[0].ProviderKind);
        Assert.IsTrue(receipt.Owner.IsAuthenticated);
        Assert.AreEqual(RulesetDefaults.Sr5, receipt.Account.PreferredRulesetId);
    }

    [TestMethod]
    public void Owner_repository_contracts_define_scope_filter_page_and_receipt_vocabulary()
    {
        OwnerRepositoryQuery query = new(
            ScopeMode: OwnerRepositoryScopeModes.SharedWithMe,
            AssetKind: OwnerRepositoryAssetKinds.RulePack,
            Search: "errata",
            CampaignId: "campaign-7",
            Visibility: ArtifactVisibilityModes.CampaignShared,
            SortMode: OwnerRepositorySortModes.UpdatedDesc,
            Offset: 0,
            Limit: 25);
        OwnerRepositoryEntry entry = new(
            AssetKind: OwnerRepositoryAssetKinds.RulePack,
            AssetId: "official-errata",
            Title: "Official Errata",
            Owner: new OwnerScope("user-2"),
            Visibility: ArtifactVisibilityModes.CampaignShared,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            VersionId: "1.0.0",
            Summary: "Campaign-approved rules corrections.",
            CanEdit: false,
            CanShare: true);
        OwnerRepositoryPage page = new(
            ScopeMode: query.ScopeMode,
            AssetKind: query.AssetKind,
            Entries: [entry],
            TotalCount: 1,
            ContinuationToken: null);
        OwnerRepositoryQueryReceipt receipt = new(
            Query: query,
            ReturnedCount: page.Entries.Count,
            TotalCount: page.TotalCount,
            ContinuationToken: page.ContinuationToken);

        Assert.AreEqual(OwnerRepositoryScopeModes.SharedWithMe, receipt.Query.ScopeMode);
        Assert.AreEqual(OwnerRepositoryAssetKinds.RulePack, page.AssetKind);
        Assert.AreEqual(OwnerRepositorySortModes.UpdatedDesc, receipt.Query.SortMode);
        Assert.AreEqual(ArtifactVisibilityModes.CampaignShared, page.Entries[0].Visibility);
        Assert.IsFalse(page.Entries[0].CanEdit);
        Assert.IsTrue(page.Entries[0].CanShare);
    }

    [TestMethod]
    public void Owner_repository_mutation_contracts_define_create_share_fork_archive_and_delete_receipts()
    {
        OwnerScope actor = new("user-1");
        OwnerRepositoryMutationReceipt createReceipt = new(
            MutationKind: OwnerRepositoryMutationKinds.Create,
            Status: OwnerRepositoryMutationStatuses.Applied,
            AssetKind: OwnerRepositoryAssetKinds.BuildKit,
            AssetId: "street-sam-starter",
            Actor: actor,
            AppliedAtUtc: DateTimeOffset.UtcNow,
            VersionId: "1.0.0",
            RequiresReindex: true);
        OwnerRepositoryShareReceipt shareReceipt = new(
            AssetKind: OwnerRepositoryAssetKinds.RulePack,
            AssetId: "official-errata",
            Actor: actor,
            Grants:
            [
                new OwnerRepositoryShareGrant(
                    SubjectKind: RulePackShareSubjectKinds.Campaign,
                    SubjectId: "campaign-7",
                    AccessLevel: OwnerRepositoryShareAccessLevels.Install),
                new OwnerRepositoryShareGrant(
                    SubjectKind: RulePackShareSubjectKinds.User,
                    SubjectId: "user-2",
                    AccessLevel: OwnerRepositoryShareAccessLevels.Fork)
            ],
            Status: OwnerRepositoryMutationStatuses.Applied,
            SharedAtUtc: DateTimeOffset.UtcNow);
        OwnerRepositoryForkReceipt forkReceipt = new(
            AssetKind: OwnerRepositoryAssetKinds.RulePack,
            SourceAssetId: "official-errata",
            SourceVersionId: "1.0.0",
            ForkedAssetId: "official-errata-user-1",
            Actor: actor,
            Status: OwnerRepositoryMutationStatuses.Applied,
            ForkedAtUtc: DateTimeOffset.UtcNow);
        OwnerRepositoryArchiveReceipt archiveReceipt = new(
            AssetKind: OwnerRepositoryAssetKinds.LinkedAsset,
            AssetId: "contact-1",
            Mode: OwnerRepositoryArchiveModes.RetainHistory,
            Actor: actor,
            Status: OwnerRepositoryMutationStatuses.Applied,
            ArchivedAtUtc: DateTimeOffset.UtcNow,
            RetainsHistory: true);
        OwnerRepositoryMutationReceipt deleteReceipt = new(
            MutationKind: OwnerRepositoryMutationKinds.Delete,
            Status: OwnerRepositoryMutationStatuses.Applied,
            AssetKind: OwnerRepositoryAssetKinds.SessionLedger,
            AssetId: "overlay-1",
            Actor: actor,
            AppliedAtUtc: DateTimeOffset.UtcNow,
            Message: "User requested ledger removal.");

        Assert.AreEqual(OwnerRepositoryMutationKinds.Create, createReceipt.MutationKind);
        Assert.IsTrue(createReceipt.RequiresReindex);
        Assert.AreEqual(OwnerRepositoryShareAccessLevels.Install, shareReceipt.Grants[0].AccessLevel);
        Assert.AreEqual(OwnerRepositoryMutationStatuses.Applied, forkReceipt.Status);
        Assert.AreEqual(OwnerRepositoryArchiveModes.RetainHistory, archiveReceipt.Mode);
        Assert.AreEqual(OwnerRepositoryMutationKinds.Delete, deleteReceipt.MutationKind);
    }

    [TestMethod]
    public void Runtime_lock_install_contracts_define_target_pin_and_rebind_vocabulary()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core",
                    Description: "Official base rules.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("official-errata", "1.0.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["validate.character"] = "official-errata/validate.character"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        RuntimeLockPin pin = new(
            TargetKind: RuntimeLockTargetKinds.Workspace,
            TargetId: "workspace-1",
            PinMode: RuntimeLockPinModes.Required,
            RuntimeLock: new RuntimeLockReference(
                RuntimeFingerprint: runtimeLock.RuntimeFingerprint,
                RulesetId: runtimeLock.RulesetId,
                EngineApiVersion: runtimeLock.EngineApiVersion),
            RulePacks: runtimeLock.RulePacks);
        RuntimeLockInstallReceipt receipt = new(
            TargetKind: RuntimeLockTargetKinds.SessionLedger,
            TargetId: "overlay-1",
            Outcome: RuntimeLockInstallOutcomes.Rebound,
            RuntimeLock: runtimeLock,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            RebindNotices:
            [
                new RuntimeLockRebindNotice(
                    Reason: RuntimeLockRebindReasons.RulePackSelectionChanged,
                    PriorRuntimeFingerprint: "runtime-lock-old",
                    CurrentRuntimeFingerprint: runtimeLock.RuntimeFingerprint,
                    SessionSafe: true)
            ],
            RequiresSessionReplay: true);

        Assert.AreEqual(RuntimeLockTargetKinds.Workspace, pin.TargetKind);
        Assert.AreEqual(RuntimeLockPinModes.Required, pin.PinMode);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Rebound, receipt.Outcome);
        Assert.AreEqual(RuntimeLockRebindReasons.RulePackSelectionChanged, receipt.RebindNotices[0].Reason);
        Assert.IsTrue(receipt.RebindNotices[0].SessionSafe);
        Assert.IsTrue(receipt.RequiresSessionReplay);
    }

    [TestMethod]
    public void Runtime_lock_registry_contracts_define_catalog_compatibility_and_install_candidate_vocabulary()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03.06",
                    Title: "SR5 Core",
                    Description: "Official base rules.",
                    AssetPaths: ["data/", "lang/"])
            ],
            RulePacks: [new ArtifactVersionReference("official-errata", "1.0.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["session.quick-actions"] = "official-errata/session.quick-actions"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        RuntimeLockRegistryEntry entry = new(
            LockId: "runtime-lock-1",
            Owner: new OwnerScope("user-1"),
            Title: "Campaign Runtime",
            Visibility: ArtifactVisibilityModes.CampaignShared,
            CatalogKind: RuntimeLockCatalogKinds.Published,
            RuntimeLock: runtimeLock,
            UpdatedAtUtc: DateTimeOffset.UtcNow,
            Description: "Shared runtime for campaign seven.",
            Install: new ArtifactInstallState(
                ArtifactInstallStates.Pinned,
                InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                InstalledTargetId: "workspace-1",
                RuntimeFingerprint: runtimeLock.RuntimeFingerprint));
        RuntimeLockInstallCandidate candidate = new(
            TargetKind: RuntimeLockTargetKinds.CharacterVersion,
            TargetId: "charv-1",
            Entry: entry,
            Diagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.Compatible,
                    Message: "Exact runtime match."),
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "Quick-action providers changed.",
                    RequiredRulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprint: runtimeLock.RuntimeFingerprint)
            ],
            CanInstall: true);
        RuntimeLockRegistryPage page = new(
            Entries: [entry],
            TotalCount: 1,
            ContinuationToken: null);

        Assert.AreEqual(RuntimeLockCatalogKinds.Published, entry.CatalogKind);
        Assert.AreEqual(RuntimeLockCompatibilityStates.Compatible, candidate.Diagnostics[0].State);
        Assert.AreEqual(RuntimeLockCompatibilityStates.RebindRequired, candidate.Diagnostics[1].State);
        Assert.AreEqual(RuntimeLockTargetKinds.CharacterVersion, candidate.TargetKind);
        Assert.IsTrue(candidate.CanInstall);
        Assert.AreEqual(ArtifactInstallStates.Pinned, entry.Install.State);
        Assert.AreEqual(1, page.TotalCount);
    }

    [TestMethod]
    public void Install_history_contracts_define_append_only_operation_and_history_vocabulary()
    {
        ArtifactInstallHistoryEntry historyEntry = new(
            Operation: ArtifactInstallHistoryOperations.Pin,
            Install: new ArtifactInstallState(
                ArtifactInstallStates.Pinned,
                InstalledTargetKind: RuntimeLockTargetKinds.Workspace,
                InstalledTargetId: "workspace-1",
                RuntimeFingerprint: "sha256:core"),
            AppliedAtUtc: DateTimeOffset.UtcNow,
            Notes: "Pinned for campaign workspace.");
        RulePackInstallHistoryRecord rulePackHistory = new(
            PackId: "house-rules",
            Version: "1.0.0",
            RulesetId: RulesetDefaults.Sr5,
            Entry: historyEntry);
        RuleProfileInstallHistoryRecord ruleProfileHistory = new(
            ProfileId: "official.sr5.core",
            RulesetId: RulesetDefaults.Sr5,
            Entry: historyEntry);
        RuntimeLockInstallHistoryRecord runtimeLockHistory = new(
            LockId: "sha256:core",
            RulesetId: RulesetDefaults.Sr5,
            Entry: historyEntry);

        Assert.AreEqual(ArtifactInstallHistoryOperations.Pin, historyEntry.Operation);
        Assert.AreEqual(ArtifactInstallStates.Pinned, historyEntry.Install.State);
        Assert.AreEqual("house-rules", rulePackHistory.PackId);
        Assert.AreEqual("official.sr5.core", ruleProfileHistory.ProfileId);
        Assert.AreEqual("sha256:core", runtimeLockHistory.LockId);
    }

    [TestMethod]
    public void Owner_backed_manifest_store_contracts_define_registry_asset_persistence_vocabulary()
    {
        ResolvedRuntimeLock runtimeLock = new(
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
            RulePacks: [new ArtifactVersionReference("house-rules", "1.0.0")],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "sha256:runtime");
        RulePackManifestRecord rulePackManifest = new(
            new RulePackManifest(
                PackId: "house-rules",
                Version: "1.0.0",
                Title: "House Rules",
                Author: "alice",
                Description: "Owner-backed manifest.",
                Targets: [RulesetDefaults.Sr5],
                EngineApiVersion: "rulepack-v1",
                DependsOn: [],
                ConflictsWith: [],
                Visibility: ArtifactVisibilityModes.Private,
                TrustTier: ArtifactTrustTiers.Private,
                Assets: [],
                Capabilities: [],
                ExecutionPolicies: []));
        RuleProfileManifestRecord ruleProfileManifest = new(
            new RuleProfileManifest(
                ProfileId: "campaign.seattle.runtime",
                Title: "Seattle Runtime",
                Description: "Owner-backed profile.",
                RulesetId: RulesetDefaults.Sr5,
                Audience: RuleProfileAudienceKinds.Campaign,
                CatalogKind: RuleProfileCatalogKinds.Personal,
                RulePacks:
                [
                    new RuleProfilePackSelection(
                        RulePack: new ArtifactVersionReference("house-rules", "1.0.0"),
                        Required: true,
                        EnabledByDefault: true)
                ],
                DefaultToggles: [],
                RuntimeLock: runtimeLock,
                UpdateChannel: RuleProfileUpdateChannels.CampaignPinned));

        Assert.AreEqual("house-rules", rulePackManifest.Manifest.PackId);
        Assert.AreEqual(RulesetDefaults.Sr5, rulePackManifest.Manifest.Targets[0]);
        Assert.AreEqual("campaign.seattle.runtime", ruleProfileManifest.Manifest.ProfileId);
        Assert.AreEqual("sha256:runtime", ruleProfileManifest.Manifest.RuntimeLock.RuntimeFingerprint);
    }

    [TestMethod]
    public void Rulepack_and_runtime_lock_install_contracts_define_preview_and_apply_vocabulary()
    {
        RuleProfileApplyTarget target = new(RuleProfileApplyTargetKinds.Workspace, "workspace-1");
        RulePackInstallPreviewReceipt rulePackPreview = new(
            PackId: "house-rules",
            RulesetId: RulesetDefaults.Sr5,
            Target: target,
            Changes:
            [
                new RulePackInstallPreviewItem(
                    Kind: RulePackInstallPreviewChangeKinds.InstallStateChanged,
                    Summary: "Install rulepack.",
                    SubjectId: "house-rules")
            ],
            Warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Trust,
                    Severity: RuntimeInspectorWarningSeverityLevels.Info,
                    Message: "Local-only pack.",
                    SubjectId: "house-rules")
            ]);
        RulePackInstallReceipt rulePackReceipt = new(
            PackId: "house-rules",
            RulesetId: RulesetDefaults.Sr5,
            Target: target,
            Outcome: RulePackInstallOutcomes.Applied,
            Install: new ArtifactInstallState(ArtifactInstallStates.Installed),
            Preview: rulePackPreview);
        RuntimeLockInstallPreviewReceipt runtimeLockPreview = new(
            LockId: "sha256:core",
            Target: target,
            RuntimeLock: new ResolvedRuntimeLock(
                RulesetId: RulesetDefaults.Sr5,
                ContentBundles: [],
                RulePacks: [],
                ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal),
                EngineApiVersion: "rulepack-v1",
                RuntimeFingerprint: "sha256:core"),
            Changes:
            [
                new RuntimeLockInstallPreviewItem(
                    Kind: RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned,
                Summary: "Pin runtime lock.",
                SubjectId: "sha256:core")
            ],
            Warnings: []);
        RuntimeLockInstallReceipt runtimeLockReceipt = new(
            TargetKind: target.TargetKind,
            TargetId: target.TargetId,
            Outcome: RuntimeLockInstallOutcomes.Installed,
            RuntimeLock: runtimeLockPreview.RuntimeLock,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            RebindNotices: []);

        Assert.AreEqual(RulePackInstallPreviewChangeKinds.InstallStateChanged, rulePackPreview.Changes[0].Kind);
        Assert.AreEqual(RulePackInstallOutcomes.Applied, rulePackReceipt.Outcome);
        Assert.AreEqual(RuntimeLockInstallPreviewChangeKinds.RuntimeLockPinned, runtimeLockPreview.Changes[0].Kind);
        Assert.AreEqual(RuntimeLockInstallOutcomes.Installed, runtimeLockReceipt.Outcome);
    }

    [TestMethod]
    public void Session_runtime_bundle_issue_contracts_define_issue_rotation_and_trust_vocabulary()
    {
        CharacterVersionReference baseCharacterVersion = new(
            CharacterId: "char-1",
            VersionId: "charv-1",
            RulesetId: RulesetDefaults.Sr5,
            RuntimeFingerprint: "runtime-lock-sha256");
        SessionRuntimeBundle bundle = new(
            BundleId: "bundle-1",
            BaseCharacterVersion: baseCharacterVersion,
            EngineApiVersion: "rulepack-v1",
            SignedAtUtc: DateTimeOffset.UtcNow,
            Signature: "sig-1",
            QuickActions:
            [
                new SessionQuickActionPin(
                    ActionId: "fire-weapon",
                    Label: "Fire Weapon",
                    CapabilityId: "session.quick-actions")
            ],
            Trackers:
            [
                new TrackerDefinition(
                    TrackerId: "stun",
                    Category: TrackerCategories.Condition,
                    Label: "Stun",
                    DefaultValue: 0,
                    MinimumValue: 0,
                    MaximumValue: 10,
                    Thresholds: [])
            ],
            ReducerBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["tracker.increment"] = "sr5-core/reducers/tracker.increment"
            });
        SessionRuntimeBundleIssueReceipt receipt = new(
            Outcome: SessionRuntimeBundleIssueOutcomes.Rotated,
            Bundle: bundle,
            SignatureEnvelope: new SessionRuntimeBundleSignatureEnvelope(
                BundleId: bundle.BundleId,
                KeyId: "key-1",
                Signature: bundle.Signature,
                SignedAtUtc: bundle.SignedAtUtc,
                ExpiresAtUtc: bundle.SignedAtUtc.AddDays(7)),
            DeliveryMode: SessionRuntimeBundleDeliveryModes.Cached,
            Diagnostics:
            [
                new SessionRuntimeBundleTrustDiagnostic(
                    State: SessionRuntimeBundleTrustStates.Trusted,
                    Message: "Signature verified.",
                    KeyId: "key-1",
                    RuntimeFingerprint: baseCharacterVersion.RuntimeFingerprint),
                new SessionRuntimeBundleTrustDiagnostic(
                    State: SessionRuntimeBundleTrustStates.ExpiringSoon,
                    Message: "Rotate before the next session.",
                    KeyId: "key-1")
            ]);
        SessionRuntimeBundleRotationNotice rotation = new(
            PreviousBundleId: "bundle-0",
            CurrentBundleId: bundle.BundleId,
            Reason: SessionRuntimeBundleRotationReasons.RuntimeFingerprintChanged,
            RotatedAtUtc: DateTimeOffset.UtcNow,
            RequiresClientReload: true);

        Assert.AreEqual(SessionRuntimeBundleIssueOutcomes.Rotated, receipt.Outcome);
        Assert.AreEqual(SessionRuntimeBundleDeliveryModes.Cached, receipt.DeliveryMode);
        Assert.AreEqual(SessionRuntimeBundleTrustStates.Trusted, receipt.Diagnostics[0].State);
        Assert.AreEqual(SessionRuntimeBundleRotationReasons.RuntimeFingerprintChanged, rotation.Reason);
        Assert.IsTrue(rotation.RequiresClientReload);
    }

    [TestMethod]
    public void Browse_query_contracts_define_query_facet_sort_preset_result_and_disable_reason_vocabulary()
    {
        BrowseQuery query = new(
            QueryText: "smartgun",
            FacetSelections: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["category"] = ["weapon"],
                ["availability"] = ["12R"]
            },
            SortId: "name",
            SortDirection: BrowseSortDirections.Ascending,
            Offset: 0,
            Limit: 25);
        DisableReason disableReason = new(
            ReasonId: "requires-cyberware",
            Summary: "Requires an implanted smartlink.",
            ExplainEntryId: "explain-smartlink",
            IsBlocking: true);
        BrowseResultPage page = new(
            Query: query,
            Items:
            [
                new BrowseResultItem(
                    ItemId: "weapon-ares-alpha",
                    Title: "Ares Alpha",
                    ColumnValues: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["availability"] = "12R",
                        ["damage"] = "11P"
                    },
                    FacetValues: ["weapon", "smartgun"],
                    IsSelectable: false,
                    DisableReasonId: disableReason.ReasonId)
            ],
            Columns:
            [
                new BrowseColumnDefinition(
                    ColumnId: "name",
                    Label: "Name",
                    ValueKind: BrowseValueKinds.Text,
                    IsPrimary: true),
                new BrowseColumnDefinition(
                    ColumnId: "availability",
                    Label: "Availability",
                    ValueKind: BrowseValueKinds.Availability)
            ],
            Facets:
            [
                new FacetDefinition(
                    FacetId: "category",
                    Label: "Category",
                    Kind: BrowseFacetKinds.MultiSelect,
                    Options:
                    [
                        new FacetOptionDefinition(
                            Value: "weapon",
                            Label: "Weapons",
                            Count: 42,
                            Selected: true)
                    ])
            ],
            Sorts:
            [
                new SortDefinition(
                    SortId: "name",
                    Label: "Name",
                    Direction: BrowseSortDirections.Ascending,
                    IsDefault: true)
            ],
            ViewPresets:
            [
                new ViewPreset(
                    PresetId: "favorite-weapons",
                    Label: "Favorite Weapons",
                    Query: query,
                    Shared: false)
            ],
            DisableReasons: [disableReason],
            TotalCount: 1,
            ContinuationToken: null);
        SelectionResult selection = new(
            ItemId: page.Items[0].ItemId,
            Title: page.Items[0].Title,
            SelectedFacetValues: page.Items[0].FacetValues,
            PresetId: page.ViewPresets[0].PresetId,
            DisableReasonId: page.Items[0].DisableReasonId);

        Assert.AreEqual(BrowseFacetKinds.MultiSelect, page.Facets[0].Kind);
        Assert.AreEqual(BrowseSortDirections.Ascending, page.Sorts[0].Direction);
        Assert.AreEqual(BrowseValueKinds.Availability, page.Columns[1].ValueKind);
        Assert.AreEqual("requires-cyberware", page.DisableReasons[0].ReasonId);
        Assert.AreEqual("favorite-weapons", selection.PresetId);
        Assert.AreEqual("weapon-ares-alpha", selection.ItemId);
    }

    [TestMethod]
    public void Journal_contracts_define_structured_notes_ledger_and_timeline_vocabulary()
    {
        OwnerScope owner = new("user-1");
        NoteDocument note = new(
            NoteId: "note-1",
            Owner: owner,
            ScopeKind: JournalScopeKinds.Character,
            ScopeId: "char-1",
            Title: "Street Contacts",
            Blocks:
            [
                new NoteBlock(
                    BlockId: "block-1",
                    Kind: NoteBlockKinds.Paragraph,
                    Content: "Met with the fixer in Tacoma.",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        LedgerEntry ledgerEntry = new(
            EntryId: "ledger-1",
            Owner: owner,
            ScopeKind: JournalScopeKinds.Character,
            ScopeId: "char-1",
            Kind: LedgerEntryKinds.Nuyen,
            Amount: -2500m,
            Currency: "nuyen",
            Label: "Ares Alpha",
            OccurredAtUtc: DateTimeOffset.UtcNow,
            NoteId: note.NoteId);
        TimelineEvent timelineEvent = new(
            EventId: "timeline-1",
            Owner: owner,
            ScopeKind: JournalScopeKinds.Character,
            ScopeId: "char-1",
            Kind: TimelineEventKinds.Training,
            Title: "Longarms 4 -> 5",
            StartsAtUtc: DateTimeOffset.UtcNow.AddDays(1),
            EndsAtUtc: DateTimeOffset.UtcNow.AddDays(10),
            NoteId: note.NoteId,
            LedgerEntryId: ledgerEntry.EntryId);
        JournalProjection projection = new(
            ScopeKind: JournalScopeKinds.Character,
            ScopeId: "char-1",
            Notes: [note],
            LedgerEntries: [ledgerEntry],
            TimelineEvents: [timelineEvent]);

        Assert.AreEqual(JournalScopeKinds.Character, projection.ScopeKind);
        Assert.AreEqual(NoteBlockKinds.Paragraph, projection.Notes[0].Blocks[0].Kind);
        Assert.AreEqual(LedgerEntryKinds.Nuyen, projection.LedgerEntries[0].Kind);
        Assert.AreEqual(TimelineEventKinds.Training, projection.TimelineEvents[0].Kind);
        Assert.AreEqual(note.NoteId, projection.LedgerEntries[0].NoteId);
        Assert.AreEqual(ledgerEntry.EntryId, projection.TimelineEvents[0].LedgerEntryId);
    }

    [TestMethod]
    public void Runtime_inspector_contracts_define_projection_warning_and_migration_preview_vocabulary()
    {
        ResolvedRuntimeLock runtimeLock = new(
            RulesetId: RulesetDefaults.Sr5,
            ContentBundles:
            [
                new ContentBundleDescriptor(
                    BundleId: "sr5-core",
                    RulesetId: RulesetDefaults.Sr5,
                    Version: "2026.03",
                    Title: "SR5 Core",
                    Description: "Base SR5 content.",
                    AssetPaths: ["data/qualities.xml"])
            ],
            RulePacks:
            [
                new ArtifactVersionReference(
                    Id: "house-rules",
                    Version: "1.2.0")
            ],
            ProviderBindings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["validate.character"] = "house-rules/validate.character"
            },
            EngineApiVersion: "rulepack-v1",
            RuntimeFingerprint: "runtime-lock-sha256");
        RuntimeInspectorProjection projection = new(
            TargetKind: RuntimeInspectorTargetKinds.CharacterVersion,
            TargetId: "charv-1",
            RuntimeLock: runtimeLock,
            Install: new ArtifactInstallState(
                ArtifactInstallStates.Pinned,
                InstalledTargetKind: RuntimeLockTargetKinds.CharacterVersion,
                InstalledTargetId: "charv-1",
                RuntimeFingerprint: runtimeLock.RuntimeFingerprint),
            ResolvedRulePacks:
            [
                new RuntimeInspectorRulePackEntry(
                    RulePack: runtimeLock.RulePacks[0],
                    Title: "House Rules",
                    Visibility: ArtifactVisibilityModes.Private,
                    TrustTier: ArtifactTrustTiers.Private,
                    CapabilityIds: [RulePackCapabilityIds.ValidateCharacter])
            ],
            ProviderBindings:
            [
                new RuntimeInspectorProviderBinding(
                    CapabilityId: RulePackCapabilityIds.ValidateCharacter,
                    ProviderId: "house-rules/validate.character",
                    PackId: "house-rules",
                    SourceAssetPath: "lua/validate.lua")
            ],
            CompatibilityDiagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "The runtime lock requires rebind before install.",
                    RequiredRuntimeFingerprint: "runtime-lock-next")
            ],
            Warnings:
            [
                new RuntimeInspectorWarning(
                    Kind: RuntimeInspectorWarningKinds.Compatibility,
                    Severity: RuntimeInspectorWarningSeverityLevels.Warning,
                    Message: "A runtime rebind is required before save.",
                    SubjectId: "house-rules",
                    ExplainEntryId: "explain-runtime-1")
            ],
            MigrationPreview:
            [
                new RuntimeMigrationPreviewItem(
                    Kind: RuntimeMigrationPreviewChangeKinds.ProviderRebound,
                    Summary: "Validation provider will be rebound to the curated pack.",
                    SubjectId: RulePackCapabilityIds.ValidateCharacter,
                    BeforeValue: "house-rules/validate.character",
                    AfterValue: "curated-pack/validate.character",
                    RequiresRebind: true)
            ],
            GeneratedAtUtc: DateTimeOffset.UtcNow);

        Assert.AreEqual(RuntimeInspectorTargetKinds.CharacterVersion, projection.TargetKind);
        Assert.AreEqual("runtime-lock-sha256", projection.RuntimeLock.RuntimeFingerprint);
        Assert.AreEqual(ArtifactInstallStates.Pinned, projection.Install.State);
        Assert.AreEqual(ArtifactTrustTiers.Private, projection.ResolvedRulePacks[0].TrustTier);
        Assert.AreEqual("house-rules/validate.character", projection.ProviderBindings[0].ProviderId);
        Assert.AreEqual(RuntimeInspectorWarningKinds.Compatibility, projection.Warnings[0].Kind);
        Assert.AreEqual(RuntimeMigrationPreviewChangeKinds.ProviderRebound, projection.MigrationPreview[0].Kind);
        Assert.IsTrue(projection.MigrationPreview[0].RequiresRebind);
    }

    [TestMethod]
    public void Rulepack_workbench_contracts_define_library_inspector_graph_validation_and_override_vocabulary()
    {
        RulePackManifest manifest = new(
            PackId: "house-rules",
            Version: "1.2.0",
            Title: "House Rules",
            Author: "GM",
            Description: "Campaign RulePack",
            Targets: [RulesetDefaults.Sr5],
            EngineApiVersion: "rulepack-v1",
            DependsOn: [],
            ConflictsWith: [],
            Visibility: ArtifactVisibilityModes.Private,
            TrustTier: ArtifactTrustTiers.Private,
            Assets:
            [
                new RulePackAssetDescriptor(
                    Kind: RulePackAssetKinds.DeclarativeRules,
                    Mode: DeclarativeRuleOverrideModes.OverrideThreshold,
                    RelativePath: "rules/thresholds.json",
                    Checksum: "sha256-1")
            ],
            Capabilities:
            [
                new RulePackCapabilityDescriptor(
                    CapabilityId: RulePackCapabilityIds.ValidateCharacter,
                    AssetKind: RulePackAssetKinds.Lua,
                    AssetMode: RulePackAssetModes.WrapProvider,
                    Explainable: true)
            ],
            ExecutionPolicies: []);
        RulePackLibraryProjection library = new(
            Items:
            [
                new RulePackWorkbenchListItem(
                    RulePack: new ArtifactVersionReference(
                        Id: manifest.PackId,
                        Version: manifest.Version),
                    Title: manifest.Title,
                    InstallState: RulePackInstallStates.InstalledEnabled,
                    Visibility: manifest.Visibility,
                    TrustTier: manifest.TrustTier,
                    Targets: manifest.Targets,
                    DiagnosticCount: 1)
            ],
            SelectedPackId: manifest.PackId);
        RulePackInspectorProjection inspector = new(
            Manifest: manifest,
            InstallState: RulePackInstallStates.ReviewRequired,
            Diagnostics:
            [
                new RulePackValidationIssue(
                    Kind: RulePackValidationIssueKinds.DeclarativeOverride,
                    Severity: RulePackResolutionSeverityLevels.Warning,
                    Message: "Threshold override needs review.",
                    AssetPath: "rules/thresholds.json",
                    SubjectId: "wound-threshold",
                    ExplainEntryId: "rulepack-validate-1")
            ],
            CompatibilityDiagnostics:
            [
                new RuntimeLockCompatibilityDiagnostic(
                    State: RuntimeLockCompatibilityStates.RebindRequired,
                    Message: "Install requires runtime rebind.")
            ],
            IsInstalled: true);
        RulePackDependencyGraphProjection graph = new(
            PackId: manifest.PackId,
            Nodes:
            [
                new RulePackDependencyNode(
                    NodeId: "house-rules",
                    Label: "House Rules",
                    Version: "1.2.0",
                    Visibility: ArtifactVisibilityModes.Private,
                    TrustTier: ArtifactTrustTiers.Private,
                    IsSelected: true),
                new RulePackDependencyNode(
                    NodeId: "official-errata",
                    Label: "Official Errata",
                    Version: "2026.03",
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Official)
            ],
            Edges:
            [
                new RulePackDependencyEdge(
                    FromNodeId: "house-rules",
                    ToNodeId: "official-errata",
                    Kind: RulePackDependencyEdgeKinds.DependsOn,
                    Message: "Requires base errata.")
            ]);
        RulePackValidationPanelProjection validationPanel = new(
            PackId: manifest.PackId,
            Version: manifest.Version,
            Issues: inspector.Diagnostics,
            HasBlockingIssues: false);
        DeclarativeOverrideEditorProjection overrideEditor = new(
            PackId: manifest.PackId,
            Version: manifest.Version,
            Overrides:
            [
                new DeclarativeOverrideDraft(
                    OverrideId: "wound-threshold",
                    Mode: DeclarativeRuleOverrideModes.OverrideThreshold,
                    TargetId: "stun-track",
                    Value: "10")
            ],
            SupportedModes:
            [
                DeclarativeRuleOverrideModes.SetConstant,
                DeclarativeRuleOverrideModes.OverrideThreshold,
                DeclarativeRuleOverrideModes.ReplaceCreationProfile
            ]);

        Assert.AreEqual(manifest.PackId, library.SelectedPackId);
        Assert.AreEqual(RulePackInstallStates.InstalledEnabled, library.Items[0].InstallState);
        Assert.AreEqual(RulePackValidationIssueKinds.DeclarativeOverride, inspector.Diagnostics[0].Kind);
        Assert.AreEqual(RulePackDependencyEdgeKinds.DependsOn, graph.Edges[0].Kind);
        Assert.AreEqual(DeclarativeRuleOverrideModes.OverrideThreshold, overrideEditor.Overrides[0].Mode);
        Assert.IsFalse(validationPanel.HasBlockingIssues);
    }

    [TestMethod]
    public void Campaign_and_gm_board_contracts_define_party_roster_tracker_board_and_round_marker_vocabulary()
    {
        OwnerScope owner = new("user-1");
        TrackerDefinition trackerDefinition = new(
            TrackerId: "stun",
            Category: TrackerCategories.Condition,
            Label: "Stun",
            DefaultValue: 0,
            MinimumValue: 0,
            MaximumValue: 10,
            Thresholds: []);
        NoteDocument note = new(
            NoteId: "note-1",
            Owner: owner,
            ScopeKind: JournalScopeKinds.Campaign,
            ScopeId: "campaign-7",
            Title: "Round Notes",
            Blocks:
            [
                new NoteBlock(
                    BlockId: "block-1",
                    Kind: NoteBlockKinds.Paragraph,
                    Content: "Suppressive fire on the east stairwell.",
                    CreatedAtUtc: DateTimeOffset.UtcNow)
            ],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
        GmBoardProjection projection = new(
            Campaign: new CampaignDescriptor(
                Owner: owner,
                CampaignId: "campaign-7",
                Title: "Seattle Nights",
                Visibility: ArtifactVisibilityModes.CampaignShared,
                Description: "Weekly SR5 game."),
            Roster:
            [
                new PartyRosterEntry(
                    ParticipantId: "participant-1",
                    DisplayName: "Razor",
                    Role: CampaignParticipantRoles.Player,
                    CharacterId: "char-1",
                    OverlayId: "overlay-1",
                    IsConnected: true)
            ],
            InitiativeOrder:
            [
                new InitiativeOrderEntry(
                    ParticipantId: "participant-1",
                    Label: "Razor",
                    Order: 1,
                    PassLabel: "Pass 1",
                    IsCurrentTurn: true)
            ],
            Participants:
            [
                new ParticipantSessionTile(
                    ParticipantId: "participant-1",
                    CharacterId: "char-1",
                    DisplayName: "Razor",
                    Role: CampaignParticipantRoles.Player,
                    Trackers:
                    [
                        new TrackerSnapshot(
                            Definition: trackerDefinition,
                            CurrentValue: 3,
                            ThresholdState: "wounded")
                    ],
                    ActiveEffects: ["Blinded"],
                    SyncBanner: new SessionSyncBanner(
                        BannerId: "sync-1",
                        Status: SessionSyncBannerStates.PendingSync,
                        Message: "Waiting for reconnect.",
                        PendingEventCount: 2,
                        RequiresAttention: true),
                    ExplainEntryId: "explain-participant-1",
                    RequiresAttention: true)
            ],
            TrackerBoard:
            [
                new GmTrackerBoardTile(
                    TileId: "tracker-1",
                    ParticipantId: "participant-1",
                    Label: "Razor Conditions",
                    Trackers:
                    [
                        new TrackerSnapshot(
                            Definition: trackerDefinition,
                            CurrentValue: 3,
                            ThresholdState: "wounded")
                    ],
                    ExplainEntryId: "explain-tracker-1")
            ],
            RoundMarkers:
            [
                new CombatRoundMarker(
                    MarkerId: "round-3",
                    RoundNumber: 3,
                    Label: "Round 3",
                    State: CombatRoundMarkerStates.Active,
                    StartedAtUtc: DateTimeOffset.UtcNow)
            ],
            Notes: [note],
            ActiveRoundMarkerId: "round-3");

        Assert.AreEqual(CampaignParticipantRoles.Player, projection.Roster[0].Role);
        Assert.IsTrue(projection.Roster[0].IsConnected);
        Assert.AreEqual(1, projection.InitiativeOrder[0].Order);
        Assert.AreEqual(SessionSyncBannerStates.PendingSync, projection.Participants[0].SyncBanner!.Status);
        Assert.AreEqual("Blinded", projection.Participants[0].ActiveEffects[0]);
        Assert.AreEqual(CombatRoundMarkerStates.Active, projection.RoundMarkers[0].State);
        Assert.AreEqual(JournalScopeKinds.Campaign, projection.Notes[0].ScopeKind);
    }

    [TestMethod]
    public void Buildkit_workbench_contracts_define_library_inspector_prompt_and_apply_preview_vocabulary()
    {
        BuildKitManifest manifest = new(
            BuildKitId: "street-sam-starter",
            Version: "1.0.0",
            Title: "Street Samurai Starter",
            Description: "Starter package for a street samurai.",
            Targets: [RulesetDefaults.Sr5],
            RuntimeRequirements:
            [
                new BuildKitRuntimeRequirement(
                    RulesetId: RulesetDefaults.Sr5,
                    RequiredRuntimeFingerprints: ["runtime-lock-sha256"],
                    RequiredRulePacks: [])
            ],
            Prompts:
            [
                new BuildKitPromptDescriptor(
                    PromptId: "weapon-package",
                    Kind: BuildKitPromptKinds.Choice,
                    Label: "Weapon Package",
                    Options:
                    [
                        new BuildKitPromptOption(
                            OptionId: "arsenal",
                            Label: "Arsenal")
                    ],
                    Required: true)
            ],
            Actions:
            [
                new BuildKitActionDescriptor(
                    ActionId: "add-arsenal",
                    Kind: BuildKitActionKinds.AddBundle,
                    TargetId: "bundle/arsenal")
            ],
            Visibility: ArtifactVisibilityModes.Private,
            TrustTier: ArtifactTrustTiers.Private);
        BuildKitValidationReceipt validation = new(
            BuildKitId: manifest.BuildKitId,
            IsValid: true,
            ResolvedPrompts:
            [
                new BuildKitPromptResolution(
                    PromptId: "weapon-package",
                    OptionId: "arsenal")
            ],
            Issues: []);
        BuildKitLibraryProjection library = new(
            Items:
            [
                new BuildKitLibraryItem(
                    BuildKit: new ArtifactVersionReference(
                        Id: manifest.BuildKitId,
                        Version: manifest.Version),
                    Title: manifest.Title,
                    AvailabilityState: BuildKitAvailabilityStates.Available,
                    Visibility: manifest.Visibility,
                    TrustTier: manifest.TrustTier,
                    Targets: manifest.Targets)
            ],
            SelectedBuildKitId: manifest.BuildKitId);
        BuildKitInspectorProjection inspector = new(
            Manifest: manifest,
            AvailabilityState: BuildKitAvailabilityStates.Installed,
            Issues: [],
            CanApply: true,
            RuntimeFingerprint: "runtime-lock-sha256");
        BuildKitApplyPreviewProjection preview = new(
            BuildKitId: manifest.BuildKitId,
            WorkspaceId: "ws-1",
            Prompts:
            [
                new BuildKitPromptPreview(
                    Prompt: manifest.Prompts[0],
                    CurrentSelections: validation.ResolvedPrompts,
                    Issues: validation.Issues)
            ],
            Changes:
            [
                new BuildKitPreviewChange(
                    Kind: BuildKitPreviewChangeKinds.BundleAdded,
                    Summary: "Adds the Arsenal starter bundle.",
                    ActionId: "add-arsenal",
                    TargetId: "bundle/arsenal",
                    PromptId: "weapon-package")
            ],
            Validation: validation,
            RequiresConfirmation: true);

        Assert.AreEqual(manifest.BuildKitId, library.SelectedBuildKitId);
        Assert.AreEqual(BuildKitAvailabilityStates.Installed, inspector.AvailabilityState);
        Assert.AreEqual(BuildKitPromptKinds.Choice, preview.Prompts[0].Prompt.Kind);
        Assert.AreEqual(BuildKitPreviewChangeKinds.BundleAdded, preview.Changes[0].Kind);
        Assert.IsTrue(preview.Validation.IsValid);
        Assert.IsTrue(preview.RequiresConfirmation);
    }

    [TestMethod]
    public void Journal_panel_contracts_define_notes_ledger_and_timeline_panel_vocabulary()
    {
        JournalPanelProjection projection = new(
            ScopeKind: JournalScopeKinds.Character,
            ScopeId: "char-1",
            Sections:
            [
                new JournalPanelSection(
                    SectionId: JournalPanelSurfaceIds.NotesPanel,
                    Kind: JournalPanelSectionKinds.Notes,
                    Title: "Notes",
                    ItemCount: 2),
                new JournalPanelSection(
                    SectionId: JournalPanelSurfaceIds.LedgerPanel,
                    Kind: JournalPanelSectionKinds.Ledger,
                    Title: "Ledger",
                    ItemCount: 1),
                new JournalPanelSection(
                    SectionId: JournalPanelSurfaceIds.TimelinePanel,
                    Kind: JournalPanelSectionKinds.Timeline,
                    Title: "Timeline",
                    ItemCount: 1)
            ],
            Notes:
            [
                new NoteListItem(
                    NoteId: "note-1",
                    Title: "Tacoma Meeting",
                    ScopeKind: JournalScopeKinds.Character,
                    BlockCount: 2,
                    UpdatedAtUtc: DateTimeOffset.UtcNow)
            ],
            LedgerEntries:
            [
                new LedgerEntryView(
                    EntryId: "ledger-1",
                    Kind: LedgerEntryKinds.Nuyen,
                    Label: "Ares Alpha",
                    Amount: -2500m,
                    Currency: "nuyen",
                    OccurredAtUtc: DateTimeOffset.UtcNow,
                    NoteId: "note-1")
            ],
            TimelineEvents:
            [
                new TimelineEventView(
                    EventId: "timeline-1",
                    Kind: TimelineEventKinds.Training,
                    Title: "Longarms 4 -> 5",
                    StartsAtUtc: DateTimeOffset.UtcNow.AddDays(1),
                    EndsAtUtc: DateTimeOffset.UtcNow.AddDays(10),
                    NoteId: "note-1",
                    LedgerEntryId: "ledger-1")
            ]);

        Assert.AreEqual(JournalPanelSectionKinds.Notes, projection.Sections[0].Kind);
        Assert.AreEqual(JournalPanelSurfaceIds.LedgerPanel, projection.Sections[1].SectionId);
        Assert.AreEqual(LedgerEntryKinds.Nuyen, projection.LedgerEntries[0].Kind);
        Assert.AreEqual(TimelineEventKinds.Training, projection.TimelineEvents[0].Kind);
        Assert.AreEqual("note-1", projection.TimelineEvents[0].NoteId);
    }

    [TestMethod]
    public void Browse_workspace_contracts_define_renderer_neutral_workspace_and_selection_dialog_vocabulary()
    {
        BrowseQuery query = new(
            QueryText: "smartgun",
            FacetSelections: new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["category"] = ["weapon"]
            },
            SortId: "name");
        BrowseResultPage results = new(
            Query: query,
            Items:
            [
                new BrowseResultItem(
                    ItemId: "weapon-ares-alpha",
                    Title: "Ares Alpha",
                    ColumnValues: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["availability"] = "12R"
                    },
                    FacetValues: ["weapon"])
            ],
            Columns:
            [
                new BrowseColumnDefinition(
                    ColumnId: "name",
                    Label: "Name",
                    ValueKind: BrowseValueKinds.Text,
                    IsPrimary: true)
            ],
            Facets:
            [
                new FacetDefinition(
                    FacetId: "category",
                    Label: "Category",
                    Kind: BrowseFacetKinds.MultiSelect,
                    Options:
                    [
                        new FacetOptionDefinition(
                            Value: "weapon",
                            Label: "Weapons",
                            Count: 42,
                            Selected: true)
                    ])
            ],
            Sorts:
            [
                new SortDefinition(
                    SortId: "name",
                    Label: "Name",
                    Direction: BrowseSortDirections.Ascending,
                    IsDefault: true)
            ],
            ViewPresets: [],
            DisableReasons: [],
            TotalCount: 1);
        BrowseWorkspaceProjection workspace = new(
            WorkspaceId: "browse-gear",
            WorkflowId: WorkflowDefinitionIds.SelectionDialog,
            Results: results,
            Sections:
            [
                new BrowseWorkspaceSection(
                    SectionId: BrowseWorkspaceSurfaceIds.FacetPanel,
                    Kind: BrowseWorkspaceSectionKinds.Facets,
                    Title: "Facets"),
                new BrowseWorkspaceSection(
                    SectionId: BrowseWorkspaceSurfaceIds.ResultGrid,
                    Kind: BrowseWorkspaceSectionKinds.Results,
                    Title: "Results"),
                new BrowseWorkspaceSection(
                    SectionId: BrowseWorkspaceSurfaceIds.DetailPane,
                    Kind: BrowseWorkspaceSectionKinds.Detail,
                    Title: "Detail")
            ],
            SelectedItems:
            [
                new SelectionSummaryItem(
                    ItemId: "weapon-ares-alpha",
                    Title: "Ares Alpha",
                    Detail: "12R")
            ],
            ActiveDetail: new BrowseItemDetail(
                ItemId: "weapon-ares-alpha",
                Title: "Ares Alpha",
                SummaryLines: ["11P", "AP -2"],
                ExplainEntryId: "explain-weapon-1"),
            ActiveSurfaceId: BrowseWorkspaceSurfaceIds.ResultGrid);
        SelectionDialogProjection dialog = new(
            DialogId: "dialog-gear",
            Title: "Select Gear",
            Mode: SelectionDialogModes.SingleSelect,
            Workspace: workspace,
            CanConfirm: true,
            ConfirmActionId: "select",
            CancelActionId: "cancel");

        Assert.AreEqual(WorkflowDefinitionIds.SelectionDialog, workspace.WorkflowId);
        Assert.AreEqual(BrowseWorkspaceSurfaceIds.ResultGrid, workspace.ActiveSurfaceId);
        Assert.AreEqual(BrowseWorkspaceSectionKinds.Results, workspace.Sections[1].Kind);
        Assert.AreEqual("weapon-ares-alpha", dialog.Workspace.SelectedItems[0].ItemId);
        Assert.AreEqual(SelectionDialogModes.SingleSelect, dialog.Mode);
        Assert.IsTrue(dialog.CanConfirm);
    }

    [TestMethod]
    public void Presentation_catalogs_support_ruleset_filtering_without_changing_sr5_defaults()
    {
        IReadOnlyList<AppCommandDefinition> sr5Commands = AppCommandCatalog.ForRuleset(null);
        IReadOnlyList<NavigationTabDefinition> sr5Tabs = NavigationTabCatalog.ForRuleset(RulesetDefaults.Sr5);
        IReadOnlyList<WorkspaceSurfaceActionDefinition> sr5Actions = WorkspaceSurfaceActionCatalog.ForRuleset(string.Empty);

        Assert.IsGreaterThan(0, sr5Commands.Count);
        Assert.IsGreaterThan(0, sr5Tabs.Count);
        Assert.IsGreaterThan(0, sr5Actions.Count);

        Assert.IsFalse(AppCommandCatalog.ForRuleset("sr6").Any());
        Assert.IsFalse(AppCommandCatalog.ForRuleset("sr4").Any());
        Assert.IsFalse(NavigationTabCatalog.ForRuleset("sr6").Any());
        Assert.IsFalse(NavigationTabCatalog.ForRuleset("sr4").Any());
        Assert.IsFalse(WorkspaceSurfaceActionCatalog.ForRuleset("sr6").Any());
        Assert.IsFalse(WorkspaceSurfaceActionCatalog.ForRuleset("sr4").Any());
        Assert.IsFalse(WorkspaceSurfaceActionCatalog.ForTab("tab-info", "sr6").Any());
        Assert.IsFalse(WorkspaceSurfaceActionCatalog.ForTab("tab-info", "sr4").Any());
    }

    [TestMethod]
    public void Ruleset_plugin_contracts_are_declared_for_serializer_shell_catalog_capability_rule_and_script_hosts()
    {
        Assert.IsTrue(typeof(IRulesetPlugin).IsInterface);
        Assert.IsTrue(typeof(IRulesetPluginRegistry).IsInterface);
        Assert.IsTrue(typeof(IRulesetSelectionPolicy).IsInterface);
        Assert.IsTrue(typeof(IRulesetShellCatalogResolver).IsInterface);
        Assert.IsTrue(typeof(IRulesetSerializer).IsInterface);
        Assert.IsTrue(typeof(IRulesetShellDefinitionProvider).IsInterface);
        Assert.IsTrue(typeof(IRulesetCatalogProvider).IsInterface);
        Assert.IsTrue(typeof(IRulesetCapabilityHost).IsInterface);
        Assert.IsTrue(typeof(IRulesetRuleHost).IsInterface);
        Assert.IsTrue(typeof(IRulesetScriptHost).IsInterface);
    }

    [TestMethod]
    public void Ruleset_explain_contracts_capture_provider_traces_and_gas_usage()
    {
        RulesetGasBudget gasBudget = new(
            ProviderInstructionLimit: 5000,
            RequestInstructionLimit: 20000,
            MemoryBytesLimit: 1_048_576,
            WallClockLimit: TimeSpan.FromSeconds(1));
        RulesetExecutionOptions options = new(
            Explain: true,
            GasBudget: gasBudget);
        RulesetExplainTrace explainTrace = new(
            TargetKey: "derive.stat.body",
            FinalValue: RulesetCapabilityBridge.FromObject(3),
            SummaryKey: "ruleset.explain.summary.derive.stat.body",
            SummaryParameters: [new RulesetExplainParameter("value", RulesetCapabilityBridge.FromObject(3))],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: "sr5/derive.stat.body",
                    CapabilityId: RulePackCapabilityIds.DeriveStat,
                    PackId: "house-rules",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            ProviderId: "sr5/derive.stat.body",
                            CapabilityId: RulePackCapabilityIds.DeriveStat,
                            PackId: "sr5-core",
                            ExplanationKey: "ruleset.explain.step.derive.stat.base",
                            ExplanationParameters:
                            [
                                new RulesetExplainParameter("metatype", RulesetCapabilityBridge.FromObject("human")),
                                new RulesetExplainParameter("value", RulesetCapabilityBridge.FromObject(3))
                            ],
                            Category: "base",
                            Modifier: 3m,
                            Certain: true)
                    ],
                    GasUsage: new RulesetGasUsage(
                        ProviderInstructionsConsumed: 120,
                        RequestInstructionsConsumed: 120,
                        PeakMemoryBytes: 4096))
            ],
            AggregateGasUsage: new RulesetGasUsage(
                ProviderInstructionsConsumed: 120,
                RequestInstructionsConsumed: 120,
                PeakMemoryBytes: 4096));
        RulesetRuleEvaluationRequest ruleRequest = new(
            RuleId: "derive.stat.body",
            Inputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["metatype"] = "human"
            },
            Options: options);
        RulesetRuleEvaluationResult ruleResult = new(
            Success: true,
            Outputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["value"] = 3
            },
            Messages: ["ok"],
            Explain: explainTrace);
        RulesetExplainTrace scriptExplainTrace = new(
            TargetKey: "session.quick-actions",
            FinalValue: RulesetCapabilityBridge.FromObject(2),
            SummaryKey: "ruleset.explain.summary.session.quick-actions",
            SummaryParameters: [new RulesetExplainParameter("quickActions", RulesetCapabilityBridge.FromObject(2))],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: "sr5/session.quick-actions",
                    CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                    PackId: "house-rules",
                    Success: true,
                    Steps:
                    [
                        new RulesetTraceStep(
                            ProviderId: "sr5/session.quick-actions",
                            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
                            PackId: "house-rules",
                            ExplanationKey: "ruleset.explain.step.session.quick-actions.pinned",
                            ExplanationParameters:
                            [
                                new RulesetExplainParameter("quickActions", RulesetCapabilityBridge.FromObject(2))
                            ],
                            Category: "availability",
                            Modifier: 2m,
                            Certain: true)
                    ],
                    GasUsage: new RulesetGasUsage(
                        ProviderInstructionsConsumed: 80,
                        RequestInstructionsConsumed: 80,
                        PeakMemoryBytes: 2048))
            ],
            AggregateGasUsage: new RulesetGasUsage(
                ProviderInstructionsConsumed: 80,
                RequestInstructionsConsumed: 80,
                PeakMemoryBytes: 2048));
        RulesetScriptExecutionRequest scriptRequest = new(
            ScriptId: "sr5/session.quick-actions",
            ScriptSource: "-- compiled provider reference",
            Inputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeFingerprint"] = "runtime-lock-sha256"
            },
            Options: options);
        RulesetScriptExecutionResult scriptResult = new(
            Success: true,
            Error: null,
            Outputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["quickActions"] = 2
            },
            Explain: scriptExplainTrace);

        Assert.IsTrue(ruleRequest.Options?.Explain);
        Assert.AreEqual(5000, ruleRequest.Options?.GasBudget?.ProviderInstructionLimit);
        Assert.AreEqual(RulePackCapabilityIds.DeriveStat, ruleResult.Explain?.Providers[0].CapabilityId);
        Assert.AreEqual(120, ruleResult.Explain?.AggregateGasUsage.RequestInstructionsConsumed);
        Assert.AreEqual("derive.stat", ruleResult.Explain?.SubjectId);
        Assert.IsTrue(scriptRequest.Options?.Explain);
        Assert.AreEqual("sr5/session.quick-actions", scriptResult.Explain?.Providers[0].ProviderId);
        Assert.AreEqual("house-rules", scriptResult.Explain?.Providers[0].PackId);
    }

    [TestMethod]
    public void Ruleset_capability_contracts_bridge_legacy_rule_and_script_requests_through_typed_values()
    {
        RulesetExecutionOptions options = new(
            Explain: true,
            GasBudget: new RulesetGasBudget(100, 500, 1024, TimeSpan.FromSeconds(1)));
        RulesetRuleEvaluationRequest ruleRequest = new(
            RuleId: RulePackCapabilityIds.DeriveStat,
            Inputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["metatype"] = "human",
                ["body"] = 3
            },
            Options: options);
        RulesetCapabilityInvocationRequest typedRuleRequest = RulesetCapabilityBridge.FromRuleRequest(ruleRequest);

        Assert.AreEqual(RulesetCapabilityInvocationKinds.Rule, typedRuleRequest.InvocationKind);
        Assert.AreEqual(RulePackCapabilityIds.DeriveStat, typedRuleRequest.CapabilityId);
        Assert.AreEqual(RulesetCapabilityValueKinds.String, typedRuleRequest.Arguments.Single(argument => argument.Name == "metatype").Value.Kind);
        Assert.AreEqual(3L, typedRuleRequest.Arguments.Single(argument => argument.Name == "body").Value.IntegerValue);
        Assert.IsTrue(typedRuleRequest.Options?.Explain);

        RulesetCapabilityInvocationResult typedRuleResult = new(
            Success: true,
            Output: new RulesetCapabilityValue(
                RulesetCapabilityValueKinds.Object,
                Properties: new Dictionary<string, RulesetCapabilityValue>(StringComparer.Ordinal)
                {
                    ["value"] = new(RulesetCapabilityValueKinds.Integer, IntegerValue: 5)
                }),
            Diagnostics:
            [
                new RulesetCapabilityDiagnostic("rule.ok", "rule ok", MessageKey: "rule.ok")
            ]);
        RulesetRuleEvaluationResult bridgedRuleResult = RulesetCapabilityBridge.ToRuleResult(typedRuleResult);

        Assert.IsTrue(bridgedRuleResult.Success);
        Assert.AreEqual(5L, bridgedRuleResult.Outputs["value"]);
        CollectionAssert.Contains(bridgedRuleResult.Messages.ToArray(), "rule ok");

        RulesetScriptExecutionRequest scriptRequest = new(
            ScriptId: RulePackCapabilityIds.SessionQuickActions,
            ScriptSource: "-- compiled provider reference",
            Inputs: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["runtimeFingerprint"] = "runtime-lock-sha256"
            },
            Options: options);
        RulesetCapabilityInvocationRequest typedScriptRequest = RulesetCapabilityBridge.FromScriptRequest(scriptRequest);

        Assert.AreEqual(RulesetCapabilityInvocationKinds.Script, typedScriptRequest.InvocationKind);
        Assert.AreEqual("-- compiled provider reference", typedScriptRequest.Source);
        Assert.AreEqual("runtime-lock-sha256", typedScriptRequest.Arguments.Single().Value.StringValue);

        RulesetCapabilityInvocationResult typedScriptResult = new(
            Success: false,
            Output: null,
            Diagnostics:
            [
                new RulesetCapabilityDiagnostic("script.fail", "script failed", RulesetCapabilityDiagnosticSeverities.Error, MessageKey: "script.fail")
            ]);
        RulesetScriptExecutionResult bridgedScriptResult = RulesetCapabilityBridge.ToScriptResult(typedScriptResult);

        Assert.IsFalse(bridgedScriptResult.Success);
        Assert.AreEqual("script failed", bridgedScriptResult.Error);
        Assert.IsEmpty(bridgedScriptResult.Outputs);
    }

    [TestMethod]
    public void Ruleset_capability_descriptor_contracts_define_invocation_kind_session_safety_and_gas_policy()
    {
        RulesetCapabilityDescriptor descriptor = new(
            CapabilityId: RulePackCapabilityIds.SessionQuickActions,
            InvocationKind: RulesetCapabilityInvocationKinds.Script,
            Title: "Session Quick Actions",
            Explainable: true,
            SessionSafe: true,
            DefaultGasBudget: new RulesetGasBudget(1_000, 5_000, 1_048_576, TimeSpan.FromSeconds(1)),
            MaximumGasBudget: new RulesetGasBudget(5_000, 20_000, 4_194_304, TimeSpan.FromSeconds(2)),
            TitleKey: "ruleset.capability.session.quick-actions.title",
            TitleParameters: []);

        Assert.AreEqual(RulePackCapabilityIds.SessionQuickActions, descriptor.CapabilityId);
        Assert.AreEqual(RulesetCapabilityInvocationKinds.Script, descriptor.InvocationKind);
        Assert.IsTrue(descriptor.Explainable);
        Assert.IsTrue(descriptor.SessionSafe);
        Assert.AreEqual(1_000, descriptor.DefaultGasBudget.ProviderInstructionLimit);
        Assert.AreEqual(20_000, descriptor.MaximumGasBudget?.RequestInstructionLimit);
        Assert.AreEqual("ruleset.capability.session.quick-actions.title", descriptor.TitleKey);
        Assert.AreEqual("ruleset.capability.session.quick-actions.title", RulesetCapabilityDescriptorLocalization.ResolveTitleKey(descriptor));
        Assert.AreEqual(0, RulesetCapabilityDescriptorLocalization.ResolveTitleParameters(descriptor).Count);
    }

    [TestMethod]
    public void Ruleset_defaults_expose_sr4_sr5_and_sr6_ids()
    {
        Assert.AreEqual(string.Empty, RulesetId.Default.NormalizedValue);
        Assert.AreEqual("sr4", new RulesetId(RulesetDefaults.Sr4).NormalizedValue);
        Assert.AreEqual("sr5", new RulesetId(RulesetDefaults.Sr5).NormalizedValue);
        Assert.AreEqual("sr6", new RulesetId(RulesetDefaults.Sr6).NormalizedValue);
    }

    [TestMethod]
    public void Ruleset_defaults_only_expose_explicit_normalization_helpers()
    {
        Assert.IsNull(typeof(RulesetDefaults).GetMethod("Normalize", [typeof(string)]));
        Assert.IsNotNull(typeof(RulesetDefaults).GetMethod(nameof(RulesetDefaults.NormalizeOptional)));
        Assert.IsNotNull(typeof(RulesetDefaults).GetMethod(nameof(RulesetDefaults.NormalizeRequired)));
        Assert.IsNull(typeof(RulesetDefaults).GetMethod("NormalizeOrDefault", [typeof(string), typeof(string)]));
        Assert.IsNull(RulesetDefaults.NormalizeOptional(" "));
        Assert.AreEqual(RulesetDefaults.Sr4, RulesetDefaults.NormalizeRequired(" SR4 "));
        Assert.AreEqual(
            RulesetDefaults.Sr6,
            RulesetDefaults.NormalizeOptional(null) ?? RulesetDefaults.NormalizeRequired(RulesetDefaults.Sr6));
    }

    [TestMethod]
    public void Ruleset_workspace_codecs_require_explicit_ruleset_id_for_wrap_import()
    {
        IRulesetWorkspaceCodec[] codecs =
        [
            new Sr4WorkspaceCodec(),
            new Sr5WorkspaceCodec(
                new XmlCharacterFileQueries(new CharacterFileService()),
                new XmlCharacterSectionQueries(new CharacterSectionService()),
                new XmlCharacterMetadataCommands(new CharacterFileService())),
            new Sr6WorkspaceCodec()
        ];

        foreach (IRulesetWorkspaceCodec codec in codecs)
        {
            Assert.ThrowsExactly<ArgumentException>(() => codec.WrapImport(
                string.Empty,
                new WorkspaceImportDocument("<character />", string.Empty)));
        }
    }

    [TestMethod]
    public void Sr4_ruleset_registration_is_opt_in()
    {
        ServiceCollection services = new();
        services.AddSr4Ruleset();

        using ServiceProvider provider = services.BuildServiceProvider();
        IRulesetPlugin[] plugins = provider.GetServices<IRulesetPlugin>().ToArray();
        IRulesetWorkspaceCodec[] codecs = provider.GetServices<IRulesetWorkspaceCodec>().ToArray();

        Assert.IsTrue(plugins.Any(plugin => string.Equals(plugin.Id.NormalizedValue, RulesetDefaults.Sr4, StringComparison.Ordinal)));
        Assert.IsTrue(codecs.Any(codec => string.Equals(codec.RulesetId, RulesetDefaults.Sr4, StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task Sr5_plugin_adapters_expose_existing_shell_catalogs_without_behavior_change()
    {
        Sr5RulesetPlugin plugin = new();

        Assert.AreEqual(RulesetDefaults.Sr5, plugin.Id.NormalizedValue);
        Assert.AreEqual("Shadowrun 5", plugin.DisplayName);
        Assert.AreEqual(RulesetDefaults.Sr5, plugin.Serializer.RulesetId.NormalizedValue);
        Assert.AreEqual(1, plugin.Serializer.SchemaVersion);

        WorkspacePayloadEnvelope envelope = plugin.Serializer.Wrap("workspace", "{}");
        Assert.AreEqual(RulesetDefaults.Sr5, envelope.RulesetId);
        Assert.AreEqual("workspace", envelope.PayloadKind);
        Assert.AreEqual("{}", envelope.Payload);

        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetCommands().Count);
        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetNavigationTabs().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowDefinitions().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowSurfaces().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkspaceActions().Count);
        Assert.IsTrue(plugin.CapabilityDescriptors.GetCapabilityDescriptors().Any(descriptor => string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal)));
        Assert.IsTrue(plugin.CapabilityDescriptors.GetCapabilityDescriptors().Any(static descriptor => descriptor.SessionSafe));
        Assert.IsTrue(plugin.CapabilityDescriptors.GetCapabilityDescriptors().All(static descriptor => !string.IsNullOrWhiteSpace(descriptor.TitleKey)));

        RulesetCapabilityInvocationResult capabilityResult = await plugin.Capabilities.InvokeAsync(
            new RulesetCapabilityInvocationRequest(
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                Arguments:
                [
                    new RulesetCapabilityArgument("karma", RulesetCapabilityBridge.FromObject(12))
                ]),
            CancellationToken.None);
        Assert.IsTrue(capabilityResult.Success);
        Assert.AreEqual(12L, capabilityResult.Output?.Properties?["karma"].IntegerValue);

        RulesetRuleEvaluationResult ruleResult = await plugin.Rules.EvaluateAsync(
            new RulesetRuleEvaluationRequest(
                RuleId: "sr5.noop",
                Inputs: new Dictionary<string, object?> { ["karma"] = 12 }),
            CancellationToken.None);
        Assert.IsTrue(ruleResult.Success);
        Assert.IsTrue(ruleResult.Outputs.ContainsKey("karma"));

        RulesetScriptExecutionResult scriptResult = await plugin.Scripts.ExecuteAsync(
            new RulesetScriptExecutionRequest(
                ScriptId: "sr5.noop",
                ScriptSource: "-- noop",
                Inputs: new Dictionary<string, object?> { ["nuyen"] = 5000 }),
            CancellationToken.None);
        Assert.IsTrue(scriptResult.Success);
        Assert.AreEqual("noop", scriptResult.Outputs["mode"]);
    }

    [TestMethod]
    public async Task Sr6_plugin_skeleton_exposes_independent_catalogs_and_codec_contracts()
    {
        Sr6RulesetPlugin plugin = new();
        Sr6WorkspaceCodec codec = new();

        Assert.AreEqual(RulesetDefaults.Sr6, plugin.Id.NormalizedValue);
        Assert.AreEqual("Shadowrun 6", plugin.DisplayName);
        Assert.AreEqual(RulesetDefaults.Sr6, plugin.Serializer.RulesetId.NormalizedValue);
        Assert.AreEqual(Sr6WorkspaceCodec.SchemaVersion, plugin.Serializer.SchemaVersion);
        Assert.AreEqual(RulesetDefaults.Sr6, codec.RulesetId);
        Assert.AreEqual(Sr6WorkspaceCodec.Sr6PayloadKind, codec.PayloadKind);

        WorkspacePayloadEnvelope wrapped = codec.WrapImport(
            RulesetDefaults.Sr6,
            new WorkspaceImportDocument("<character><name>Switchback</name><alias>Ghost</alias></character>", RulesetDefaults.Sr6));
        CharacterFileSummary summary = codec.ParseSummary(wrapped);

        Assert.AreEqual(RulesetDefaults.Sr6, wrapped.RulesetId);
        Assert.AreEqual("Switchback", summary.Name);
        Assert.AreEqual("Ghost", summary.Alias);
        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetCommands().Count);
        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetNavigationTabs().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowDefinitions().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowSurfaces().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkspaceActions().Count);
        Assert.IsTrue(plugin.CapabilityDescriptors.GetCapabilityDescriptors().Any(descriptor => string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal)));

        RulesetCapabilityInvocationResult capabilityResult = await plugin.Capabilities.InvokeAsync(
            new RulesetCapabilityInvocationRequest(
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                Arguments:
                [
                    new RulesetCapabilityArgument("edge", RulesetCapabilityBridge.FromObject(2))
                ]),
            CancellationToken.None);
        Assert.IsFalse(capabilityResult.Success);
        CollectionAssert.Contains(
            capabilityResult.Diagnostics.Select(static diagnostic => diagnostic.Message).ToArray(),
            "SR6 rules engine is not implemented; this ruleset remains experimental.");
        Assert.AreEqual("sr6.rule.experimental", capabilityResult.Diagnostics[0].MessageKey);

        WorkspaceDownloadReceipt download = codec.BuildDownload(
            new CharacterWorkspaceId("ws-sr6"),
            wrapped,
            WorkspaceDocumentFormat.NativeXml);
        Assert.AreEqual("ws-sr6.chum6", download.FileName);

        RulesetRuleEvaluationResult ruleResult = await plugin.Rules.EvaluateAsync(
            new RulesetRuleEvaluationRequest(
                RuleId: "sr6.noop",
                Inputs: new Dictionary<string, object?> { ["edge"] = 2 }),
            CancellationToken.None);
        Assert.IsFalse(ruleResult.Success);
        Assert.IsEmpty(ruleResult.Outputs);
        CollectionAssert.Contains(ruleResult.Messages.ToArray(), "SR6 rules engine is not implemented; this ruleset remains experimental.");

        RulesetScriptExecutionResult scriptResult = await plugin.Scripts.ExecuteAsync(
            new RulesetScriptExecutionRequest(
                ScriptId: "sr6.noop",
                ScriptSource: "// noop",
                Inputs: new Dictionary<string, object?> { ["essence"] = 5.8m }),
            CancellationToken.None);
        Assert.IsFalse(scriptResult.Success);
        Assert.IsEmpty(scriptResult.Outputs);
        StringAssert.Contains(scriptResult.Error, "SR6 script host is not implemented");
        Assert.AreEqual(
            "ruleset.capability.session.quick-actions.title",
            plugin.CapabilityDescriptors.GetCapabilityDescriptors()
                .Single(descriptor => string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal))
                .TitleKey);
    }

    [TestMethod]
    public async Task Sr4_plugin_scaffold_exposes_independent_catalogs_and_codec_contracts()
    {
        Sr4RulesetPlugin plugin = new();
        Sr4WorkspaceCodec codec = new();

        Assert.AreEqual(RulesetDefaults.Sr4, plugin.Id.NormalizedValue);
        Assert.AreEqual("Shadowrun 4", plugin.DisplayName);
        Assert.AreEqual(RulesetDefaults.Sr4, plugin.Serializer.RulesetId.NormalizedValue);
        Assert.AreEqual(Sr4WorkspaceCodec.SchemaVersion, plugin.Serializer.SchemaVersion);
        Assert.AreEqual(RulesetDefaults.Sr4, codec.RulesetId);
        Assert.AreEqual(Sr4WorkspaceCodec.Sr4PayloadKind, codec.PayloadKind);

        WorkspacePayloadEnvelope wrapped = codec.WrapImport(
            RulesetDefaults.Sr4,
            new WorkspaceImportDocument("<character><name>Ghost</name><alias>Switchback</alias></character>", RulesetDefaults.Sr4));
        CharacterFileSummary summary = codec.ParseSummary(wrapped);

        Assert.AreEqual(RulesetDefaults.Sr4, wrapped.RulesetId);
        Assert.AreEqual("Ghost", summary.Name);
        Assert.AreEqual("Switchback", summary.Alias);
        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetCommands().Count);
        Assert.IsGreaterThan(0, plugin.ShellDefinitions.GetNavigationTabs().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowDefinitions().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkflowSurfaces().Count);
        Assert.IsGreaterThan(0, plugin.Catalogs.GetWorkspaceActions().Count);
        Assert.IsTrue(plugin.CapabilityDescriptors.GetCapabilityDescriptors().Any(descriptor => string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.DeriveStat, StringComparison.Ordinal)));

        RulesetCapabilityInvocationResult capabilityResult = await plugin.Capabilities.InvokeAsync(
            new RulesetCapabilityInvocationRequest(
                CapabilityId: RulePackCapabilityIds.DeriveStat,
                InvocationKind: RulesetCapabilityInvocationKinds.Rule,
                Arguments:
                [
                    new RulesetCapabilityArgument("essence", RulesetCapabilityBridge.FromObject(5.5m))
                ]),
            CancellationToken.None);
        Assert.IsFalse(capabilityResult.Success);
        CollectionAssert.Contains(
            capabilityResult.Diagnostics.Select(static diagnostic => diagnostic.Message).ToArray(),
            "SR4 rules engine is not implemented; this ruleset remains experimental.");
        Assert.AreEqual("sr4.rule.experimental", capabilityResult.Diagnostics[0].MessageKey);

        WorkspaceDownloadReceipt download = codec.BuildDownload(
            new CharacterWorkspaceId("ws-sr4"),
            wrapped,
            WorkspaceDocumentFormat.NativeXml);
        Assert.AreEqual("ws-sr4.chum4", download.FileName);

        RulesetRuleEvaluationResult ruleResult = await plugin.Rules.EvaluateAsync(
            new RulesetRuleEvaluationRequest(
                RuleId: "sr4.noop",
                Inputs: new Dictionary<string, object?> { ["essence"] = 5.5m }),
            CancellationToken.None);
        Assert.IsFalse(ruleResult.Success);
        Assert.IsEmpty(ruleResult.Outputs);
        CollectionAssert.Contains(ruleResult.Messages.ToArray(), "SR4 rules engine is not implemented; this ruleset remains experimental.");

        RulesetScriptExecutionResult scriptResult = await plugin.Scripts.ExecuteAsync(
            new RulesetScriptExecutionRequest(
                ScriptId: "sr4.noop",
                ScriptSource: "-- noop",
                Inputs: new Dictionary<string, object?> { ["karma"] = 9 }),
            CancellationToken.None);
        Assert.IsFalse(scriptResult.Success);
        Assert.IsEmpty(scriptResult.Outputs);
        StringAssert.Contains(scriptResult.Error, "SR4 script host is not implemented");
        Assert.AreEqual(
            "ruleset.capability.session.quick-actions.title",
            plugin.CapabilityDescriptors.GetCapabilityDescriptors()
                .Single(descriptor => string.Equals(descriptor.CapabilityId, RulePackCapabilityIds.SessionQuickActions, StringComparison.Ordinal))
                .TitleKey);
    }

    [TestMethod]
    public void Session_api_contracts_define_operation_and_placeholder_receipt_vocabulary()
    {
        SessionNotImplementedReceipt receipt = new(
            Error: "session_not_implemented",
            Operation: SessionApiOperations.SyncCharacterLedger,
            Message: "The dedicated session/mobile API seam exists, but this operation is not implemented yet.",
            CharacterId: "char-7",
            OwnerId: "owner-3");

        Assert.AreEqual(SessionApiOperations.SyncCharacterLedger, receipt.Operation);
        Assert.AreEqual("session_not_implemented", receipt.Error);
        Assert.AreEqual("char-7", receipt.CharacterId);
        Assert.AreEqual("owner-3", receipt.OwnerId);
    }

    [TestMethod]
    public async Task Dedicated_session_client_keeps_mobile_boundary_separate_from_workbench_client()
    {
        InProcessSessionClient sessionClient = new();
        SessionApiResult<SessionCharacterCatalog> listResult = await sessionClient.ListCharactersAsync(CancellationToken.None);
        SessionApiResult<RulePackCatalog> rulePackResult = await sessionClient.ListRulePacksAsync(CancellationToken.None);

        Assert.IsFalse(listResult.IsImplemented);
        Assert.IsNotNull(listResult.NotImplemented);
        Assert.AreEqual(SessionApiOperations.ListCharacters, listResult.NotImplemented.Operation);
        Assert.AreEqual("session_not_implemented", listResult.NotImplemented.Error);
        Assert.IsFalse(rulePackResult.IsImplemented);
        Assert.IsNotNull(rulePackResult.NotImplemented);
        Assert.AreEqual(SessionApiOperations.ListRulePacks, rulePackResult.NotImplemented.Operation);
    }
}
