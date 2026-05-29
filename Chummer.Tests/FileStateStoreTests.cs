#nullable enable annotations

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Hub;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Session;
using Chummer.Infrastructure.Files;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class FileStateStoreTests
{
    [TestMethod]
    public void File_hub_publisher_store_normalizes_persists_and_scopes_records_by_owner()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubPublisherStore store = new(stateDirectory.Path);

        HubPublisherRecord persisted = store.Upsert(
            new OwnerScope(" Alice "),
            new HubPublisherRecord(
                PublisherId: " ShadowOps ",
                OwnerId: "ignored",
                DisplayName: "  Shadow Ops  ",
                Slug: " ShadowOps ",
                VerificationState: " Verified ",
                CreatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 0, 0, TimeSpan.Zero),
                UpdatedAtUtc: new DateTimeOffset(2026, 5, 20, 10, 5, 0, TimeSpan.Zero),
                Description: "  Campaign tools  ",
                WebsiteUrl: " https://example.test/shadowops "));

        HubPublisherRecord? loaded = store.Get(new OwnerScope("alice"), " shadowops ");
        HubPublisherRecord[] bobRecords = store.List(new OwnerScope("bob")).ToArray();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("shadowops", persisted.PublisherId);
        Assert.AreEqual("alice", persisted.OwnerId);
        Assert.AreEqual("Shadow Ops", persisted.DisplayName);
        Assert.AreEqual("shadowops", persisted.Slug);
        Assert.AreEqual("verified", persisted.VerificationState);
        Assert.AreEqual("Campaign tools", persisted.Description);
        Assert.AreEqual("https://example.test/shadowops", persisted.WebsiteUrl);
        Assert.AreEqual(persisted, loaded);
        Assert.IsEmpty(bobRecords);
    }

    [TestMethod]
    public void File_hub_publisher_store_replaces_existing_record_instead_of_appending_duplicate()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileHubPublisherStore store = new(stateDirectory.Path);
        OwnerScope owner = new("alice");
        DateTimeOffset createdAtUtc = new(2026, 5, 20, 11, 0, 0, TimeSpan.Zero);

        store.Upsert(
            owner,
            new HubPublisherRecord(
                PublisherId: "shadowops",
                OwnerId: owner.NormalizedValue,
                DisplayName: "Shadow Ops",
                Slug: "shadowops",
                VerificationState: HubPublisherVerificationStates.Unverified,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: createdAtUtc,
                Description: "First",
                WebsiteUrl: "https://example.test/first"));
        HubPublisherRecord updated = store.Upsert(
            owner,
            new HubPublisherRecord(
                PublisherId: "shadowops",
                OwnerId: "ignored",
                DisplayName: "Shadow Ops Updated",
                Slug: "shadowops",
                VerificationState: HubPublisherVerificationStates.Verified,
                CreatedAtUtc: createdAtUtc,
                UpdatedAtUtc: createdAtUtc.AddMinutes(10),
                Description: "Second",
                WebsiteUrl: "https://example.test/second"));

        HubPublisherRecord[] records = store.List(owner).ToArray();

        Assert.HasCount(1, records);
        Assert.AreEqual("Shadow Ops Updated", records[0].DisplayName);
        Assert.AreEqual(HubPublisherVerificationStates.Verified, records[0].VerificationState);
        Assert.AreEqual("Second", records[0].Description);
        Assert.AreEqual(updated, records[0]);
    }

    [TestMethod]
    public void File_session_profile_selection_store_normalizes_persists_and_scopes_bindings_by_owner()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileSessionProfileSelectionStore store = new(stateDirectory.Path);
        DateTimeOffset selectedAtUtc = new(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);

        SessionProfileBinding persisted = store.Upsert(
            new OwnerScope(" Alice "),
            new SessionProfileBinding(
                CharacterId: " char-1 ",
                ProfileId: " campaign.sr5.ready ",
                RulesetId: " SR5 ",
                RuntimeFingerprint: " runtime-campaign-sr5-ready ",
                SelectedAtUtc: selectedAtUtc));

        SessionProfileBinding? loaded = store.Get(new OwnerScope("alice"), " char-1 ");
        SessionProfileBinding[] bobBindings = store.List(new OwnerScope("bob")).ToArray();

        Assert.IsNotNull(loaded);
        Assert.AreEqual("char-1", persisted.CharacterId);
        Assert.AreEqual("campaign.sr5.ready", persisted.ProfileId);
        Assert.AreEqual(RulesetDefaults.Sr5, persisted.RulesetId);
        Assert.AreEqual("runtime-campaign-sr5-ready", persisted.RuntimeFingerprint);
        Assert.AreEqual(persisted, loaded);
        Assert.IsEmpty(bobBindings);
    }

    [TestMethod]
    public void File_session_runtime_bundle_store_roundtrips_and_replaces_character_bound_records()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileSessionRuntimeBundleStore store = new(stateDirectory.Path);
        OwnerScope owner = new("alice");
        DateTimeOffset issuedAtUtc = new(2026, 5, 20, 13, 0, 0, TimeSpan.Zero);

        SessionRuntimeBundleRecord first = CreateRuntimeBundleRecord(
            characterId: " char-1 ",
            profileId: " campaign.sr5.ready ",
            rulesetId: " SR5 ",
            bundleId: "bundle-1",
            runtimeFingerprint: "runtime-1",
            signature: "sig-1",
            issuedAtUtc: issuedAtUtc);
        SessionRuntimeBundleRecord updated = CreateRuntimeBundleRecord(
            characterId: "char-1",
            profileId: "campaign.sr5.ready",
            rulesetId: RulesetDefaults.Sr5,
            bundleId: "bundle-2",
            runtimeFingerprint: "runtime-2",
            signature: "sig-2",
            issuedAtUtc: issuedAtUtc.AddMinutes(10));

        SessionRuntimeBundleRecord persistedFirst = store.Upsert(owner, first);
        SessionRuntimeBundleRecord persistedUpdated = store.Upsert(owner, updated);
        SessionRuntimeBundleRecord? loaded = store.Get(owner, " char-1 ");

        Assert.AreEqual("char-1", persistedFirst.CharacterId);
        Assert.AreEqual("campaign.sr5.ready", persistedFirst.ProfileId);
        Assert.AreEqual(RulesetDefaults.Sr5, persistedFirst.RulesetId);
        Assert.IsNotNull(loaded);
        Assert.AreEqual("bundle-2", loaded.Receipt.Bundle.BundleId);
        Assert.AreEqual("runtime-2", loaded.Receipt.Bundle.BaseCharacterVersion.RuntimeFingerprint);
        Assert.AreEqual("char-1", loaded.CharacterId);
        Assert.AreEqual("campaign.sr5.ready", loaded.ProfileId);
        Assert.AreEqual(RulesetDefaults.Sr5, loaded.RulesetId);
        Assert.AreEqual(SessionRuntimeBundleIssueOutcomes.Issued, loaded.Receipt.Outcome);
        Assert.AreEqual(SessionRuntimeBundleDeliveryModes.Inline, loaded.Receipt.DeliveryMode);
        Assert.AreEqual("sig-2", loaded.Receipt.SignatureEnvelope.Signature);
        Assert.HasCount(1, loaded.Receipt.Diagnostics);
    }

    [TestMethod]
    public void File_session_stores_return_empty_or_null_when_owner_state_file_is_missing()
    {
        using TemporaryStateDirectory stateDirectory = new();
        FileSessionProfileSelectionStore profileStore = new(stateDirectory.Path);
        FileSessionRuntimeBundleStore bundleStore = new(stateDirectory.Path);

        Assert.IsEmpty(profileStore.List(new OwnerScope("missing")));
        Assert.IsNull(profileStore.Get(new OwnerScope("missing"), "char-1"));
        Assert.IsNull(bundleStore.Get(new OwnerScope("missing"), "char-1"));
    }

    private static SessionRuntimeBundleRecord CreateRuntimeBundleRecord(
        string characterId,
        string profileId,
        string rulesetId,
        string bundleId,
        string runtimeFingerprint,
        string signature,
        DateTimeOffset issuedAtUtc)
    {
        SessionRuntimeBundleIssueReceipt receipt = new(
            Outcome: SessionRuntimeBundleIssueOutcomes.Issued,
            Bundle: new SessionRuntimeBundle(
                BundleId: bundleId,
                BaseCharacterVersion: new CharacterVersionReference(
                    CharacterId: characterId.Trim(),
                    VersionId: "ver-1",
                    RulesetId: RulesetDefaults.NormalizeRequired(rulesetId),
                    RuntimeFingerprint: runtimeFingerprint),
                EngineApiVersion: "1.0.0",
                SignedAtUtc: issuedAtUtc,
                Signature: signature,
                QuickActions: [],
                Trackers: [],
                ReducerBindings: new Dictionary<string, string>()),
            SignatureEnvelope: new SessionRuntimeBundleSignatureEnvelope(
                BundleId: bundleId,
                KeyId: "key-1",
                Signature: signature,
                SignedAtUtc: issuedAtUtc,
                ExpiresAtUtc: issuedAtUtc.AddDays(7)),
            DeliveryMode: SessionRuntimeBundleDeliveryModes.Inline,
            Diagnostics:
            [
                new SessionRuntimeBundleTrustDiagnostic(
                    SessionRuntimeBundleTrustStates.Trusted,
                    "Trusted bundle",
                    "key-1",
                    runtimeFingerprint)
            ]);

        return new SessionRuntimeBundleRecord(
            CharacterId: characterId,
            ProfileId: profileId,
            RulesetId: rulesetId,
            Receipt: receipt,
            IssuedAtUtc: issuedAtUtc);
    }

    private sealed class TemporaryStateDirectory : IDisposable
    {
        public TemporaryStateDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chummer-file-state-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
