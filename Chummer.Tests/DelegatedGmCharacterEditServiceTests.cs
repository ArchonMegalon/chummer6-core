using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    [TestMethod]
    public void Campaign_owner_cannot_substitute_for_character_owner_consent()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new()
        {
            GrantedByCharacterOwnerIdOverride = "campaign-owner@example.com"
        };
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);

        DelegatedGmCharacterEditResult result = service.Execute(Command());

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Denied, result.Outcome);
        Assert.AreEqual("campaign_delegation_denied", result.ErrorCode);
        WorkspaceStoredDocument current = store.Get(CharacterOwner, CharacterId).Value!;
        Assert.AreEqual(1L, current.ContentRevision);
        StringAssert.Contains(current.Document.Content, "Original notes");
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
    public void Delegated_ledger_allows_revision_gaps_created_by_owner_edits()
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            FileWorkspaceStore store = new(stateDirectory);
            WorkspaceStoreMutationResult created = store.CreateWorkspaceDocument(
                CharacterOwner,
                CharacterId,
                Document("Original notes"));
            Assert.IsTrue(created.Success, created.Error);
            DelegatedGmCharacterEditService service = CreateService(store, new RecordingAuthorizer());
            DelegatedGmCharacterEditResult first = service.Execute(Command());
            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, first.Outcome, first.Error);
            WorkspaceStoreMutationResult ownerEdit = store.ReplaceWorkspaceDocument(
                CharacterOwner,
                CharacterId,
                expectedContentRevision: 2,
                Document("Owner revision between delegated edits"));
            Assert.IsTrue(ownerEdit.Success, ownerEdit.Error);

            DelegatedGmCharacterEditResult second = service.Execute(Command(
                expectedRevision: 3,
                idempotencyKey: "edit-key-after-owner-gap",
                reason: "Correct a note after the owner revision"));

            Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, second.Outcome, second.Error);
            WorkspaceStoreReadResult restarted = new FileWorkspaceStore(stateDirectory)
                .Get(CharacterOwner, CharacterId);
            Assert.AreEqual(WorkspaceOperationOutcome.Success, restarted.Outcome, restarted.Error);
            Assert.AreEqual(4L, restarted.Value?.ContentRevision);
        }
        finally
        {
            Directory.Delete(stateDirectory, recursive: true);
        }
    }

    [DataTestMethod]
    [DataRow("wrong-owner")]
    [DataRow("wrong-workspace")]
    [DataRow("future-revision")]
    [DataRow("overlapping-revision")]
    [DataRow("reversed-order")]
    [DataRow("duplicate-idempotency-key")]
    [DataRow("duplicate-receipt-id")]
    [DataRow("authority-binding")]
    [DataRow("character-owner-consent")]
    [DataRow("authority-revision-rollback")]
    [DataRow("applied-time-rollback")]
    public void Persisted_ledger_tampering_blocks_the_entire_workspace_read(string mutation)
    {
        string stateDirectory = CreateTempStateDirectory();
        try
        {
            string workspacePath = CreatePersistedLedgerWithTwoEdits(stateDirectory);
            JsonObject record = JsonNode.Parse(File.ReadAllText(workspacePath))?.AsObject()
                ?? throw new AssertFailedException("Persisted workspace record was not valid JSON.");
            JsonArray ledger = record["DelegatedGmCharacterEdits"]?.AsArray()
                ?? throw new AssertFailedException("Persisted delegated-edit ledger was missing.");
            Assert.HasCount(2, ledger);
            JsonObject first = ledger[0]?.AsObject()
                ?? throw new AssertFailedException("First persisted ledger entry was missing.");
            JsonObject second = ledger[1]?.AsObject()
                ?? throw new AssertFailedException("Second persisted ledger entry was missing.");
            JsonObject firstReceipt = first["Receipt"]?.AsObject()
                ?? throw new AssertFailedException("First persisted receipt was missing.");
            JsonObject secondReceipt = second["Receipt"]?.AsObject()
                ?? throw new AssertFailedException("Second persisted receipt was missing.");

            switch (mutation)
            {
                case "wrong-owner":
                    secondReceipt["CharacterOwnerId"] = "attacker@example.com";
                    break;
                case "wrong-workspace":
                    secondReceipt["CharacterId"] = new JsonObject { ["Value"] = "runner-two" };
                    break;
                case "future-revision":
                    secondReceipt["PreviousRevision"] = 3;
                    secondReceipt["NewRevision"] = 4;
                    break;
                case "overlapping-revision":
                    secondReceipt["PreviousRevision"] = 1;
                    secondReceipt["NewRevision"] = 2;
                    break;
                case "reversed-order":
                    JsonNode firstClone = first.DeepClone();
                    JsonNode secondClone = second.DeepClone();
                    ledger.Clear();
                    ledger.Add(secondClone);
                    ledger.Add(firstClone);
                    break;
                case "duplicate-idempotency-key":
                    second["IdempotencyKeySha256"] = first["IdempotencyKeySha256"]?.DeepClone();
                    secondReceipt["IdempotencyKeySha256"] =
                        firstReceipt["IdempotencyKeySha256"]?.DeepClone();
                    break;
                case "duplicate-receipt-id":
                    secondReceipt["ReceiptId"] = firstReceipt["ReceiptId"]?.DeepClone();
                    break;
                case "authority-binding":
                    secondReceipt["CampaignId"] = "campaign-two";
                    break;
                case "character-owner-consent":
                    secondReceipt["GrantedByCharacterOwnerId"] = "campaign-owner@example.com";
                    break;
                case "authority-revision-rollback":
                    secondReceipt["AuthorityRevision"] = 6;
                    break;
                case "applied-time-rollback":
                    secondReceipt["AppliedAtUtc"] = FixedNow.AddMinutes(-1);
                    break;
                default:
                    throw new AssertFailedException($"Unknown ledger mutation '{mutation}'.");
            }

            File.WriteAllText(workspacePath, record.ToJsonString());
            FileWorkspaceStore restarted = new(stateDirectory);

            WorkspaceStoreReadResult read = restarted.Get(CharacterOwner, CharacterId);

            Assert.AreEqual(WorkspaceOperationOutcome.Corrupt, read.Outcome, mutation);
            Assert.AreEqual(0, restarted.List(CharacterOwner).Count, mutation);
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
    public void Revocation_is_rechecked_before_an_idempotent_replay_is_disclosed()
    {
        InMemoryWorkspaceStore store = CreateStoreWithCharacter();
        RecordingAuthorizer authorizer = new();
        DelegatedGmCharacterEditService service = CreateService(store, authorizer);
        DelegatedGmCharacterEditCommand command = Command();
        DelegatedGmCharacterEditResult applied = service.Execute(command);
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, applied.Outcome);

        authorizer.Authorized = false;
        DelegatedGmCharacterEditResult replay = service.Execute(command);

        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Denied, replay.Outcome);
        Assert.IsNull(replay.Receipt);
        Assert.AreEqual(2L, store.Get(CharacterOwner, CharacterId).Value?.ContentRevision);
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
        Assert.AreEqual(CharacterOwner.NormalizedValue, receipt.GrantedByCharacterOwnerId);
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

    private static string CreatePersistedLedgerWithTwoEdits(string stateDirectory)
    {
        FileWorkspaceStore store = new(stateDirectory);
        WorkspaceStoreMutationResult created = store.CreateWorkspaceDocument(
            CharacterOwner,
            CharacterId,
            Document("Original notes"));
        Assert.IsTrue(created.Success, created.Error);
        DelegatedGmCharacterEditService service = CreateService(store, new RecordingAuthorizer());
        DelegatedGmCharacterEditResult first = service.Execute(Command());
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, first.Outcome, first.Error);
        DelegatedGmCharacterEditResult second = service.Execute(Command(
            expectedRevision: 2,
            idempotencyKey: "edit-key-two",
            reason: "Correct a second campaign-visible note"));
        Assert.AreEqual(DelegatedGmCharacterEditOutcome.Applied, second.Outcome, second.Error);

        return Directory.EnumerateFiles(
                stateDirectory,
                $"{CharacterId.Value}.json",
                SearchOption.AllDirectories)
            .Single();
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

        public bool Authorized { get; set; } = true;

        public string Role { get; init; } = DelegatedGmCharacterEditContract.GameMasterRole;

        public string? CampaignIdOverride { get; init; }

        public string? GrantedByCharacterOwnerIdOverride { get; init; }

        public CampaignGmCharacterEditAuthorization Authorize(
            CampaignGmCharacterEditAuthorizationRequest request)
        {
            CallCount++;
            return new CampaignGmCharacterEditAuthorization(
                Authorized: Authorized,
                CampaignId: CampaignIdOverride ?? request.CampaignId,
                ActorId: request.ActorId,
                Role: Role,
                Scope: DelegatedGmCharacterEditContract.CharacterEditScope,
                CharacterOwner: request.CharacterOwner,
                CharacterId: request.CharacterId,
                DelegationId: "delegation-one",
                GrantedByCampaignOwnerId: "campaign-owner@example.com",
                GrantedByCharacterOwnerId: GrantedByCharacterOwnerIdOverride
                    ?? request.CharacterOwner.NormalizedValue,
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
