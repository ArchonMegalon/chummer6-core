#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using Chummer.Application.Campaign;
using Chummer.Application.Content;
using Chummer.Contracts.Campaign;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class OppositionPacketContractServiceTests
{
    [TestMethod]
    public void ListOppositionPackets_projects_seeded_entries_and_packs()
    {
        DefaultOppositionPacketContractService service = new(new DefaultNpcVaultRegistryService());

        IReadOnlyList<OppositionPacketContract> packets = service.ListOppositionPackets(OwnerScope.LocalSingleUser, RulesetDefaults.Sr5);

        Assert.IsTrue(packets.Any(packet => packet.PacketId == "red-samurai" && packet.PacketKind == OppositionPacketKinds.NpcEntry));
        Assert.IsTrue(packets.Any(packet => packet.PacketId == "renraku-security" && packet.PacketKind == OppositionPacketKinds.NpcPack));
        CollectionAssert.AreEqual(
            packets
                .OrderBy(static packet => packet.RulesetId, StringComparer.Ordinal)
                .ThenBy(static packet => packet.Title, StringComparer.Ordinal)
                .Select(static packet => packet.PacketId)
                .ToArray(),
            packets.Select(static packet => packet.PacketId).ToArray());
    }

    [TestMethod]
    public void GetOppositionPacket_projects_seeded_pack_members_stats_and_bounded_loss_receipt()
    {
        DefaultOppositionPacketContractService service = new(new DefaultNpcVaultRegistryService());

        OppositionPacketContract? packet = service.GetOppositionPacket(OwnerScope.LocalSingleUser, "renraku-security", RulesetDefaults.Sr5);

        Assert.IsNotNull(packet);
        Assert.AreEqual(OppositionPacketKinds.NpcPack, packet.PacketKind);
        Assert.IsTrue(packet.Members.Count > 0);
        Assert.IsTrue(packet.PacketStats.Any(stat => stat.StatId == "combat.attack-dice.peak"));
        Assert.AreEqual(GmPrepPacketBoundedLossPostures.BoundedLoss, packet.BoundedLossReceipt.Posture);
        Assert.IsTrue(packet.BoundedLossReceipt.WarningCount > 0);
        Assert.IsTrue((packet.BoundedLossReceipt.RuleStatCount ?? 0) >= packet.PacketStats.Count);
    }

    [TestMethod]
    public void GetOppositionPacket_marks_mixed_runtime_pack_as_bounded_loss_and_unbound()
    {
        DefaultOppositionPacketContractService service = new(new StubNpcVaultRegistryService(
            entries:
            [
                CreateEntry("entry-a", RulesetDefaults.Sr5, "low", "sha256:a"),
                CreateEntry("entry-b", RulesetDefaults.Sr5, "high", "sha256:b")
            ],
            packs:
            [
                new NpcPackRegistryEntry(
                    new NpcPackManifest(
                        PackId: "mixed-pack",
                        Version: "1.0.0",
                        Title: "Mixed Pack",
                        Description: "Two runtimes.",
                        RulesetId: RulesetDefaults.Sr5,
                        Entries:
                        [
                            new NpcPackMemberReference("entry-a", 1),
                            new NpcPackMemberReference("entry-b", 1)
                        ]),
                    OwnerScope.LocalSingleUser,
                    NpcPublicationStatuses.Published,
                    DateTimeOffset.UtcNow)
            ]));

        OppositionPacketContract? packet = service.GetOppositionPacket(OwnerScope.LocalSingleUser, "mixed-pack", RulesetDefaults.Sr5);

        Assert.IsNotNull(packet);
        Assert.IsNull(packet.RuntimeFingerprint);
        Assert.AreEqual(GmPrepPacketBoundedLossPostures.BoundedLoss, packet.BoundedLossReceipt.Posture);
        Assert.IsTrue(packet.BoundedLossReceipt.Items.Any(item => item.Code == "runtime-fingerprint-mixed"));
    }

    [TestMethod]
    public void GetScenePacket_marks_missing_members_as_review_required()
    {
        DefaultOppositionPacketContractService service = new(new StubNpcVaultRegistryService(
            entries: [],
            encounterPacks:
            [
                new EncounterPackRegistryEntry(
                    new EncounterPackManifest(
                        EncounterPackId: "broken-scene",
                        Version: "1.0.0",
                        Title: "Broken Scene",
                        Description: "Missing governed entry.",
                        RulesetId: RulesetDefaults.Sr6,
                        Participants:
                        [
                            new EncounterPackParticipantReference("missing-entry", 1, "overwatch")
                        ],
                        Tags: ["checkpoint"]),
                    OwnerScope.LocalSingleUser,
                    NpcPublicationStatuses.Published,
                    DateTimeOffset.UtcNow)
            ]));

        ScenePacketContract? packet = service.GetScenePacket(OwnerScope.LocalSingleUser, "broken-scene", RulesetDefaults.Sr6);

        Assert.IsNotNull(packet);
        Assert.AreEqual(ScenePacketEngagementKinds.Checkpoint, packet.EngagementKind);
        Assert.AreEqual(GmPrepPacketBoundedLossPostures.ReviewRequired, packet.BoundedLossReceipt.Posture);
        Assert.AreEqual(1, packet.BoundedLossReceipt.ErrorCount);
        Assert.IsTrue(packet.BoundedLossReceipt.Items.Any(item => item.Code == "missing-entry:missing-entry"));
    }

    [TestMethod]
    public void GetOppositionPacket_marks_missing_pack_entries_as_review_required()
    {
        DefaultOppositionPacketContractService service = new(new StubNpcVaultRegistryService(
            entries:
            [
                CreateEntry("entry-a", RulesetDefaults.Sr5, "low", "sha256:a")
            ],
            packs:
            [
                new NpcPackRegistryEntry(
                    new NpcPackManifest(
                        PackId: "broken-pack",
                        Version: "1.0.0",
                        Title: "Broken Pack",
                        Description: "One entry is missing.",
                        RulesetId: RulesetDefaults.Sr5,
                        Entries:
                        [
                            new NpcPackMemberReference("entry-a", 1),
                            new NpcPackMemberReference("missing-entry", 2)
                        ]),
                    OwnerScope.LocalSingleUser,
                    NpcPublicationStatuses.Published,
                    DateTimeOffset.UtcNow)
            ]));

        OppositionPacketContract? packet = service.GetOppositionPacket(OwnerScope.LocalSingleUser, "broken-pack", RulesetDefaults.Sr5);

        Assert.IsNotNull(packet);
        Assert.AreEqual(OppositionPacketKinds.NpcPack, packet.PacketKind);
        Assert.AreEqual(GmPrepPacketBoundedLossPostures.ReviewRequired, packet.BoundedLossReceipt.Posture);
        Assert.IsTrue(packet.BoundedLossReceipt.Items.Any(item => item.Code == "missing-entry:missing-entry"));
        Assert.AreEqual(1, packet.Members.Count);
    }

    [TestMethod]
    public void ListScenePackets_orders_results_and_projects_runtime_fingerprint_when_members_resolve()
    {
        DefaultOppositionPacketContractService service = new(new StubNpcVaultRegistryService(
            entries:
            [
                CreateEntry("entry-a", RulesetDefaults.Sr6, "low", "sha256:scene"),
                CreateEntry("entry-b", RulesetDefaults.Sr6, "high", "sha256:scene")
            ],
            encounterPacks:
            [
                new EncounterPackRegistryEntry(
                    new EncounterPackManifest(
                        EncounterPackId: "z-scene",
                        Version: "1.0.0",
                        Title: "Zulu Scene",
                        Description: "Late alphabet.",
                        RulesetId: RulesetDefaults.Sr6,
                        Participants: [new EncounterPackParticipantReference("entry-a", 1, "scout")],
                        Tags: ["checkpoint"]),
                    OwnerScope.LocalSingleUser,
                    NpcPublicationStatuses.Published,
                    DateTimeOffset.UtcNow),
                new EncounterPackRegistryEntry(
                    new EncounterPackManifest(
                        EncounterPackId: "a-scene",
                        Version: "1.0.0",
                        Title: "Alpha Scene",
                        Description: "Early alphabet.",
                        RulesetId: RulesetDefaults.Sr6,
                        Participants:
                        [
                            new EncounterPackParticipantReference("entry-a", 1, "scout"),
                            new EncounterPackParticipantReference("entry-b", 2, "muscle")
                        ],
                        Tags: ["ambush"]),
                    OwnerScope.LocalSingleUser,
                    NpcPublicationStatuses.Published,
                    DateTimeOffset.UtcNow)
            ]));

        IReadOnlyList<ScenePacketContract> packets = service.ListScenePackets(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);
        ScenePacketContract? alpha = service.GetScenePacket(OwnerScope.LocalSingleUser, "a-scene", RulesetDefaults.Sr6);

        Assert.AreEqual("a-scene", packets[0].ScenePacketId);
        Assert.AreEqual("z-scene", packets[1].ScenePacketId);
        Assert.IsNotNull(alpha);
        Assert.AreEqual("sha256:scene", alpha.RuntimeFingerprint);
        Assert.AreEqual(ScenePacketEngagementKinds.General, alpha.EngagementKind);
        Assert.AreEqual(2, alpha.OppositionRoles.Count);
        Assert.IsTrue(alpha.PacketStats.Any(static stat => stat.StatId == "combat.attack-dice.peak"));
    }

    private static NpcEntryRegistryEntry CreateEntry(string entryId, string rulesetId, string threatTier, string runtimeFingerprint)
        => new(
            new NpcEntryManifest(
                EntryId: entryId,
                Version: "1.0.0",
                Title: entryId,
                Description: $"Entry {entryId}",
                RulesetId: rulesetId,
                ThreatTier: threatTier,
                RuntimeFingerprint: runtimeFingerprint),
            OwnerScope.LocalSingleUser,
            NpcPublicationStatuses.Published,
            DateTimeOffset.UtcNow);

    private sealed class StubNpcVaultRegistryService : INpcVaultRegistryService
    {
        private readonly IReadOnlyList<NpcEntryRegistryEntry> _entries;
        private readonly IReadOnlyList<NpcPackRegistryEntry> _packs;
        private readonly IReadOnlyList<EncounterPackRegistryEntry> _encounterPacks;

        public StubNpcVaultRegistryService(
            IReadOnlyList<NpcEntryRegistryEntry>? entries = null,
            IReadOnlyList<NpcPackRegistryEntry>? packs = null,
            IReadOnlyList<EncounterPackRegistryEntry>? encounterPacks = null)
        {
            _entries = entries ?? [];
            _packs = packs ?? [];
            _encounterPacks = encounterPacks ?? [];
        }

        public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null)
            => _entries.Where(entry => Matches(entry.Manifest.RulesetId, rulesetId)).ToArray();

        public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null)
            => _entries.FirstOrDefault(entry =>
                string.Equals(entry.Manifest.EntryId, entryId, StringComparison.Ordinal)
                && Matches(entry.Manifest.RulesetId, rulesetId));

        public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null)
            => _packs.Where(pack => Matches(pack.Manifest.RulesetId, rulesetId)).ToArray();

        public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null)
            => _packs.FirstOrDefault(pack =>
                string.Equals(pack.Manifest.PackId, packId, StringComparison.Ordinal)
                && Matches(pack.Manifest.RulesetId, rulesetId));

        public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null)
            => _encounterPacks.Where(pack => Matches(pack.Manifest.RulesetId, rulesetId)).ToArray();

        public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null)
            => _encounterPacks.FirstOrDefault(pack =>
                string.Equals(pack.Manifest.EncounterPackId, encounterPackId, StringComparison.Ordinal)
                && Matches(pack.Manifest.RulesetId, rulesetId));

        private static bool Matches(string entryRulesetId, string? rulesetId)
            => rulesetId is null || string.Equals(entryRulesetId, RulesetDefaults.NormalizeRequired(rulesetId), StringComparison.Ordinal);
    }
}
