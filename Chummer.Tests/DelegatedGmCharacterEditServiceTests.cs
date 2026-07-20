using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using Chummer.Application.Characters;
using Chummer.Application.Workspaces;
using Chummer.Contracts.Api;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Chummer.Contracts.Workspaces;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class DelegatedGmCharacterEditServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 7, 20, 8, 30, 0, TimeSpan.Zero);
    private static readonly OwnerScope CharacterOwner = new("runner-owner@example.com");
    private static readonly CharacterWorkspaceId CharacterId = new("runner-one");

    [TestMethod]
    public void Player_role_is_denied_without_reading_or_mutating_owner_state()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new() { Role = "player" };
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);

        DelegatedGmCharacterEditResult result = service.Execute(Command());

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Denied, result.Outcome);
        Assert.AreEqual("campaign_delegation_denied", result.ErrorCode);
        Assert.AreEqual(1L, store.Get(CharacterOwner, CharacterId).Value?.ContentRevision);
        StringAssert.Contains(
            store.Get(CharacterOwner, CharacterId).Value?.Document.Content ?? string.Empty,
            "Original notes");
    }

    [TestMethod]
    public void Foreign_campaign_authorization_is_denied_as_a_confused_deputy_attempt()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new() { CampaignIdOverride = "campaign-foreign" };
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);

        DelegatedGmCharacterEditResult result = service.Execute(Command());

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Denied, result.Outcome);
        Assert.AreEqual(1L, store.Get(CharacterOwner, CharacterId).Value?.ContentRevision);
    }

    [DataTestMethod]
    [DataRow("/owner/id")]
    [DataRow("/account/email")]
    [DataRow("/auth/token")]
    [DataRow("/provenance/source")]
    [DataRow("/private-contact/phone")]
    public void Sensitive_identity_and_private_contact_fields_are_forbidden(string path)
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        DelegatedGmCharacterEditService service = CreateService(store, new RecordingAuthorizer());
        DelegatedGmCharacterEditCommand command = Command(
            operations:
            [
                new DelegatedGmCharacterPatchOperation(
                    DelegatedGmCharacterPatchOperationKind.Replace,
                    path,
                    "attacker-value")
            ]);

        DelegatedGmCharacterEditResult result = service.Execute(command);

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Forbidden, result.Outcome);
        Assert.AreEqual("forbidden_character_field", result.ErrorCode);
        Assert.AreEqual(1L, store.Get(CharacterOwner, CharacterId).Value?.ContentRevision);
    }

    [TestMethod]
    public void Stale_expected_revision_is_rejected_without_clobbering_owner_cas_winner()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        WorkspaceStoreMutationResult ownerWinner = store.ReplaceWorkspaceDocument(
            CharacterOwner,
            CharacterId,
            expectedContentRevision: 1,
            Document("Owner winner"));
        Assert.IsTrue(ownerWinner.Success, ownerWinner.Error);
        DelegatedGmCharacterEditService service = CreateService(store, new RecordingAuthorizer());

        DelegatedGmCharacterEditResult result = service.Execute(Command(expectedRevision: 1));

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Conflict, result.Outcome);
        Assert.AreEqual("stale_revision", result.ErrorCode);
        WorkspaceStoredDocument current = store.Get(CharacterOwner, CharacterId).Value!;
        Assert.AreEqual(2L, current.ContentRevision);
        StringAssert.Contains(current.Document.Content, "Owner winner");
        Assert.IsFalse(current.Document.Content.Contains("GM-visible note", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Idempotent_replay_survives_restart_and_a_later_owner_edit()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore firstStore = new(stateDirectory);
            WorkspaceStoreMutationResult created = firstStore.CreateWorkspaceDocument(
                CharacterOwner,
                CharacterId,
                Document("Original notes"));
            Assert.IsTrue(created.Success, created.Error);
            DelegatedGmCharacterEditCommand command = Command();
            DelegatedGmCharacterEditResult applied = CreateService(
                firstStore,
                new RecordingAuthorizer()).Execute(command);
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome);

            WorkspaceStoreMutationResult ownerEdit = firstStore.ReplaceWorkspaceDocument(
                CharacterOwner,
                CharacterId,
                expectedContentRevision: 2,
                Document("Later owner edit"));
            Assert.IsTrue(ownerEdit.Success, ownerEdit.Error);

            FileWorkspaceStore restartedStore = new(stateDirectory);
            DelegatedGmCharacterEditResult replayed = CreateService(
                restartedStore,
                new RecordingAuthorizer()).Execute(command);

            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Replayed, replayed.Outcome);
            Assert.AreEqual(applied.Receipt?.ReceiptId, replayed.Receipt?.ReceiptId);
            Assert.AreEqual(applied.Receipt?.CommandSha256, replayed.Receipt?.CommandSha256);
            WorkspaceStoredDocument current = restartedStore.Get(CharacterOwner, CharacterId).Value!;
            Assert.AreEqual(3L, current.ContentRevision);
            StringAssert.Contains(current.Document.Content, "Later owner edit");
            Assert.IsFalse(current.Document.Content.Contains("GM-visible note", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void Reusing_an_idempotency_key_for_a_different_command_is_rejected()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        DelegatedGmCharacterEditService service = CreateService(store, new RecordingAuthorizer());
        DelegatedGmCharacterEditCommand first = Command(idempotencyKey: "edit-key-reused");
        DelegatedGmCharacterEditResult applied = service.Execute(first);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome);
        DelegatedGmCharacterEditCommand changed = Command(
            idempotencyKey: "edit-key-reused",
            operations:
            [
                new DelegatedGmCharacterPatchOperation(
                    DelegatedGmCharacterPatchOperationKind.Replace,
                    DelegatedGmCharacterEditContract.ProfileNotesPath,
                    "Different note")
            ]);

        DelegatedGmCharacterEditResult result = service.Execute(changed);

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Conflict, result.Outcome);
        Assert.AreEqual("idempotency_key_reused", result.ErrorCode);
        WorkspaceStoredDocument current = store.Get(CharacterOwner, CharacterId).Value!;
        Assert.AreEqual(2L, current.ContentRevision);
        StringAssert.Contains(current.Document.Content, "GM-visible note");
        Assert.IsFalse(current.Document.Content.Contains("Different note", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Audit_receipt_is_complete_bound_and_does_not_duplicate_patch_values()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new();
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);
        const string privateNote = "GM-visible note";

        DelegatedGmCharacterEditResult result = service.Execute(Command());

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, result.Outcome);
        DelegatedGmCharacterEditAuditReceipt receipt = result.Receipt!;
        Assert.AreEqual(DelegatedGmCharacterEditContract.Name, receipt.Contract);
        StringAssert.StartsWith(receipt.ReceiptId, "gm-edit-");
        Assert.AreEqual("campaign-one", receipt.CampaignId);
        Assert.AreEqual("delegation-one", receipt.DelegationId);
        Assert.AreEqual("campaign-owner@example.com", receipt.GrantedByCampaignOwnerId);
        Assert.AreEqual("authority-receipt-one", receipt.AuthorityReceiptId);
        Assert.AreEqual(7L, receipt.AuthorityRevision);
        Assert.AreEqual("gm@example.com", receipt.ActorId);
        Assert.AreEqual(DelegatedGmCharacterEditContract.GameMasterRole, receipt.ActorRole);
        Assert.AreEqual(CharacterOwner.NormalizedValue, receipt.CharacterOwnerId);
        Assert.AreEqual(CharacterId, receipt.CharacterId);
        Assert.AreEqual("Correct a campaign-visible note", receipt.Reason);
        Assert.AreEqual(64, receipt.IdempotencyKeySha256.Length);
        Assert.AreEqual(64, receipt.CommandSha256.Length);
        Assert.AreEqual(1L, receipt.PreviousRevision);
        Assert.AreEqual(2L, receipt.NewRevision);
        Assert.AreEqual(FixedNow, receipt.AppliedAtUtc);
        Assert.HasCount(1, receipt.Operations);
        Assert.AreEqual(DelegatedGmCharacterEditContract.ProfileNotesPath, receipt.Operations[0].Path);
        Assert.AreEqual(64, receipt.Operations[0].ValueSha256.Length);
        Assert.AreEqual(privateNote.Length, receipt.Operations[0].ValueLength);
        Assert.IsFalse(JsonSerializer.Serialize(receipt).Contains(privateNote, StringComparison.Ordinal));
    }

    [TestMethod]
    public void Blank_reason_is_rejected_before_campaign_authority_or_state_access()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new();
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);

        DelegatedGmCharacterEditResult result = service.Execute(Command(reason: "   "));

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Invalid, result.Outcome);
        Assert.AreEqual("reason_required", result.ErrorCode);
        Assert.AreEqual(0, authorizer.CallCount);
        Assert.AreEqual(1L, store.Get(CharacterOwner, CharacterId).Value?.ContentRevision);
    }

    [TestMethod]
    public void Local_single_user_labels_cannot_enter_the_delegated_owner_lane()
    {
        InMemoryWorkspaceStore store = new();
        WorkspaceStoreMutationResult localCreated = store.CreateWorkspaceDocument(
            CharacterId,
            Document("Local original"));
        Assert.IsTrue(localCreated.Success, localCreated.Error);
        OwnerScope[] sentinelOwners =
        [
            OwnerScope.LocalSingleUser,
            new OwnerScope("local-single-user"),
            new OwnerScope(" LOCAL-SINGLE-USER ")
        ];

        foreach (OwnerScope sentinel in sentinelOwners)
        {
            DelegatedGmCharacterEditCommand command = Command(characterOwner: sentinel);
            DelegatedGmCharacterEditResult result = CreateService(
                store,
                new RecordingAuthorizer()).Execute(command);
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Invalid, result.Outcome);
        }

        WorkspaceStoredDocument current = store.Get(CharacterId).Value!;
        Assert.AreEqual(1L, current.ContentRevision);
        StringAssert.Contains(current.Document.Content, "Local original");
    }

    private static InMemoryWorkspaceStore CreateStoreWithCharacter()
    {
        InMemoryWorkspaceStore store = new();
        WorkspaceStoreMutationResult created = store.CreateWorkspaceDocument(
            CharacterOwner,
            CharacterId,
            Document("Original notes"));
        Assert.IsTrue(created.Success, created.Error);
        return store;
    }

    private static DelegatedGmCharacterEditService CreateService(
        IWorkspaceStore store,
        RecordingAuthorizer authorizer)
    {
        return new DelegatedGmCharacterEditService(
            store,
            new RulesetWorkspaceCodecResolver([CreateSr5Codec()]),
            authorizer,
            new FixedTimeProvider(FixedNow));
    }

    private static DelegatedGmCharacterEditCommand Command(
        long expectedRevision = 1,
        string idempotencyKey = "edit-key-one",
        string reason = "Correct a campaign-visible note",
        OwnerScope? characterOwner = null,
        IReadOnlyList<DelegatedGmCharacterPatchOperation>? operations = null)
    {
        return new DelegatedGmCharacterEditCommand(
            CampaignId: "campaign-one",
            ActorId: "gm@example.com",
            CharacterOwner: characterOwner ?? CharacterOwner,
            CharacterId: CharacterId,
            ExpectedRevision: expectedRevision,
            IdempotencyKey: idempotencyKey,
            Reason: reason,
            Operations: operations ??
            [
                new DelegatedGmCharacterPatchOperation(
                    DelegatedGmCharacterPatchOperationKind.Replace,
                    DelegatedGmCharacterEditContract.ProfileNotesPath,
                    "GM-visible note")
            ]);
    }

    private static WorkspaceDocument Document(string notes)
    {
        return new WorkspaceDocument(
            CharacterXml(notes),
            RulesetDefaults.Sr5);
    }

    private static string CharacterXml(string notes)
    {
        return $"<character><name>Runner One</name><alias>One</alias><notes>{notes}</notes><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>0</karma><nuyen>0</nuyen><created>True</created></character>";
    }

    private static Sr5WorkspaceCodec CreateSr5Codec()
    {
        CharacterFileService characterFileService = new();
        XmlCharacterFileQueries fileQueries = new(characterFileService);
        XmlCharacterSectionQueries sectionQueries = new(new CharacterSectionService());
        XmlCharacterMetadataCommands metadataCommands = new(characterFileService);
        return new Sr5WorkspaceCodec(fileQueries, sectionQueries, metadataCommands);
    }

    private static string CreateTempStateDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "chummer-delegated-gm-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class RecordingAuthorizer : ICampaignGmCharacterEditAuthorizer
    {
        public int CallCount { get; private set; }

        public string Role { get; init; } = DelegatedGmCharacterEditContract.GameMasterRole;

        public string? CampaignIdOverride { get; init; }

        public CampaignGmCharacterEditAuthorization Authorize(
            CampaignGmCharacterEditAuthorizationRequest request)
        {
            CallCount++;
            return new CampaignGmCharacterEditAuthorization(
                Authorized: true,
                CampaignId: CampaignIdOverride ?? request.CampaignId,
                ActorId: request.ActorId,
                Role: Role,
                Scope: DelegatedGmCharacterEditContract.CharacterEditScope,
                CharacterOwner: request.CharacterOwner,
                CharacterId: request.CharacterId,
                DelegationId: "delegation-one",
                GrantedByCampaignOwnerId: "campaign-owner@example.com",
                AuthorityReceiptId: "authority-receipt-one",
                AuthorityRevision: 7,
                ValidFromUtc: FixedNow.AddMinutes(-5),
                ExpiresAtUtc: FixedNow.AddHours(1),
                AllowedPatchPaths:
                [
                    DelegatedGmCharacterEditContract.ProfileNamePath,
                    DelegatedGmCharacterEditContract.ProfileAliasPath,
                    DelegatedGmCharacterEditContract.ProfileNotesPath
                ]);
        }
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }
}
