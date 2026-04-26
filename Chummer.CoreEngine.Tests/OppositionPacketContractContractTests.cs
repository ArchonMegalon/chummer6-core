using Chummer.Application.Campaign;
using Chummer.Application.Content;
using Chummer.Contracts.Campaign;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

internal static class OppositionPacketContractContractTests
{
    public static void Run()
    {
        DefaultOppositionPacketContractService service = new(new DefaultNpcVaultRegistryService());

        OppositionPacketContract? entryPacket = service.GetOppositionPacket(OwnerScope.LocalSingleUser, "red-samurai", RulesetDefaults.Sr5);
        AssertEx.True(entryPacket is not null, "Seeded NPC entries should project into opposition packet contracts.");
        AssertEx.Equal(OppositionPacketKinds.NpcEntry, entryPacket!.PacketKind, "Seeded entry packets should keep the npc-entry kind.");
        AssertEx.Equal("high", entryPacket.ThreatTier, "Seeded entry packets should preserve threat tier.");
        AssertEx.Equal("Renraku", entryPacket.Faction, "Seeded entry packets should preserve faction.");
        AssertEx.Equal("sha256:core", entryPacket.RuntimeFingerprint, "Seeded entry packets should preserve runtime fingerprint.");
        AssertEx.Equal(GmPrepPacketBoundedLossPostures.BoundedLoss, entryPacket.BoundedLossReceipt.Posture, "Seeded entry packets should surface bounded-loss posture.");
        AssertEx.Equal(2, entryPacket.BoundedLossReceipt.WarningCount, "Seeded entry packets should summarize warning posture directly on the receipt.");
        AssertEx.Equal(entryPacket.BoundedLossReceipt.Items.Count, entryPacket.BoundedLossReceipt.ItemCount, "Entry packet receipts should report item counts without re-counting the list.");
        AssertEx.Equal(entryPacket.PacketId, entryPacket.BoundedLossReceipt.PacketId, "Entry packet receipts should keep the packet id explicit.");
        AssertEx.Equal(OppositionPacketKinds.NpcEntry, entryPacket.BoundedLossReceipt.PacketKind, "Entry packet receipts should keep the packet kind explicit.");
        AssertEx.Equal(RulesetDefaults.Sr5, entryPacket.BoundedLossReceipt.RulesetId, "Entry packet receipts should keep the ruleset id explicit.");
        AssertEx.Equal(entryPacket.Members.Single().Stats.Count, entryPacket.BoundedLossReceipt.RuleStatCount, "Entry packet receipts should summarize grounded rule-stat counts.");
        AssertEx.Equal(entryPacket.Members.Single().Stats.Count, entryPacket.BoundedLossReceipt.RuntimeBoundStatCount, "Entry packet receipts should summarize runtime-bound stat counts.");
        AssertEx.True(
            entryPacket.BoundedLossReceipt.Items.Any(item => string.Equals(item.Code, "exact-loadout-not-carried", StringComparison.Ordinal)),
            "Seeded entry packets should make exact loadout loss explicit.");
        AssertEx.True(
            entryPacket.PacketStats.Any(stat => string.Equals(stat.StatId, "combat.attack-dice.peak", StringComparison.Ordinal) && stat.Value.IntegerValue == 14L),
            "Entry packets should publish packet-level peak attack stats.");
        AssertEx.True(
            entryPacket.PacketStats.All(stat => string.Equals(stat.RulesAnchor.SourcePacketId, entryPacket.PacketId, StringComparison.Ordinal)),
            "Entry packet packet-stats should keep their packet anchor explicit.");

        OppositionPacketMemberContract entryMember = entryPacket.Members.Single();
        GmPrepPacketRuleStat attack = entryMember.Stats.Single(stat => string.Equals(stat.StatId, "combat.attack-dice", StringComparison.Ordinal));
        AssertEx.Equal(14L, attack.Value.IntegerValue, "SR5 high-tier packets should expose deterministic attack dice.");
        AssertEx.Equal("red-samurai", attack.RulesAnchor.SourceEntryId, "Packet stats should keep a direct governed source entry anchor.");
        AssertEx.Equal("sr5#gm-prep.combat-attack-dice.high", attack.RulesAnchor.RulePointer, "Packet stats should expose their rule pointer without requiring explain-trace parsing.");
        AssertEx.Equal("gm-prep.packet/red-samurai/combat-attack-dice", attack.RulesAnchor.CapabilityDescriptorPointer, "Packet stats should expose their capability descriptor pointer directly.");
        AssertEx.Equal("sha256:core", attack.RulesAnchor.RuntimeFingerprint, "Packet stats should keep runtime fingerprints on the direct rules anchor.");
        AssertEx.Equal("sha256:core", attack.ExplainTrace.RuntimeFingerprint, "Packet stats should keep runtime fingerprints in explain traces.");
        AssertEx.Equal("gm-prep.packet.stat.summary", attack.ExplainTrace.SummaryKey, "Packet stats should use the governed explain summary key.");
        AssertEx.True(
            attack.ExplainTrace.Evidence is not null
            && attack.ExplainTrace.Evidence.Any(pointer =>
                string.Equals(pointer.Kind, RulesetEvidencePointerKinds.RuleReference, StringComparison.Ordinal)
                && pointer.Pointer.Contains("combat-attack-dice", StringComparison.Ordinal)),
            "Packet stat traces should carry rule-reference evidence.");

        OppositionPacketContract? packPacket = service.GetOppositionPacket(OwnerScope.LocalSingleUser, "renraku-security", RulesetDefaults.Sr5);
        AssertEx.True(packPacket is not null, "Seeded NPC packs should project into opposition packet contracts.");
        AssertEx.Equal(OppositionPacketKinds.NpcPack, packPacket!.PacketKind, "Seeded pack packets should keep the npc-pack kind.");
        AssertEx.Equal(2, packPacket.Members.Count, "Seeded pack packets should preserve member composition.");
        AssertEx.Equal(GmPrepPacketBoundedLossPostures.BoundedLoss, packPacket.BoundedLossReceipt.Posture, "Seeded pack packets should roll bounded-loss posture forward.");
        AssertEx.Equal(packPacket.BoundedLossReceipt.Items.Count, packPacket.BoundedLossReceipt.ItemCount, "Pack receipts should summarize total bounded-loss items.");
        AssertEx.Equal(4, packPacket.BoundedLossReceipt.WarningCount, "Pack receipts should aggregate member warning counts.");
        AssertEx.Equal(packPacket.PacketId, packPacket.BoundedLossReceipt.PacketId, "Pack receipts should keep the pack id explicit.");
        AssertEx.Equal(OppositionPacketKinds.NpcPack, packPacket.BoundedLossReceipt.PacketKind, "Pack receipts should keep the npc-pack kind explicit.");
        AssertEx.Equal(11, packPacket.BoundedLossReceipt.RuleStatCount, "Pack receipts should count grounded member stats across the packet.");
        AssertEx.Equal(11, packPacket.BoundedLossReceipt.RuntimeBoundStatCount, "Pack receipts should count runtime-bound member stats across the packet.");
        AssertEx.True(
            packPacket.BoundedLossReceipt.Items.Any(item => item.Code.StartsWith("red-samurai:", StringComparison.Ordinal)),
            "Seeded pack packets should keep member-scoped bounded-loss items.");
        AssertEx.True(
            packPacket.PacketStats.Any(stat => string.Equals(stat.StatId, "combat.attack-dice.peak", StringComparison.Ordinal) && stat.Value.IntegerValue == 14L),
            "Pack packets should publish packet-level peak attack stats.");
        AssertEx.True(
            packPacket.PacketStats.Any(stat => string.Equals(stat.StatId, "matrix.operations-dice.peak", StringComparison.Ordinal) && stat.Value.IntegerValue == 12L),
            "Pack packets should publish packet-level peak matrix stats.");
        AssertEx.True(
            packPacket.PacketStats.All(stat => string.Equals(stat.RulesAnchor.SourcePacketId, packPacket.PacketId, StringComparison.Ordinal)),
            "Pack packet-stats should keep their source packet id explicit.");

        ScenePacketContract? scenePacket = service.GetScenePacket(OwnerScope.LocalSingleUser, "renraku-checkpoint", RulesetDefaults.Sr5);
        AssertEx.True(scenePacket is not null, "Seeded encounter packets should project into scene packet contracts.");
        AssertEx.Equal(ScenePacketKinds.EncounterPack, scenePacket!.SceneKind, "Seeded scene packets should keep the encounter-pack kind.");
        AssertEx.Equal(ScenePacketEngagementKinds.Checkpoint, scenePacket.EngagementKind, "Checkpoint tags should map to checkpoint engagement kind.");
        AssertEx.Equal(GmPrepPacketBoundedLossPostures.BoundedLoss, scenePacket.BoundedLossReceipt.Posture, "Scene packets should keep bounded-loss posture explicit.");
        AssertEx.Equal(scenePacket.BoundedLossReceipt.Items.Count, scenePacket.BoundedLossReceipt.ItemCount, "Scene receipts should summarize total bounded-loss items.");
        AssertEx.Equal(4, scenePacket.BoundedLossReceipt.WarningCount, "Scene receipts should aggregate warning posture across governed roles.");
        AssertEx.Equal(scenePacket.ScenePacketId, scenePacket.BoundedLossReceipt.PacketId, "Scene receipts should keep the scene packet id explicit.");
        AssertEx.Equal(ScenePacketKinds.EncounterPack, scenePacket.BoundedLossReceipt.PacketKind, "Scene receipts should keep the encounter-pack kind explicit.");
        AssertEx.Equal(11, scenePacket.BoundedLossReceipt.RuleStatCount, "Scene receipts should count spotlight stats across governed roles.");
        AssertEx.Equal(11, scenePacket.BoundedLossReceipt.RuntimeBoundStatCount, "Scene receipts should count runtime-bound spotlight stats.");
        AssertEx.True(
            !string.IsNullOrWhiteSpace(scenePacket.OpeningSummary) && scenePacket.OpeningSummary.Contains("access control", StringComparison.Ordinal),
            "Checkpoint scenes should advertise their opening summary.");
        AssertEx.True(
            !string.IsNullOrWhiteSpace(scenePacket.EscalationSummary) && scenePacket.EscalationSummary.Contains("alarm pressure", StringComparison.Ordinal),
            "Checkpoint scenes should advertise their escalation summary.");
        AssertEx.True(
            scenePacket.OppositionRoles.Any(role => string.Equals(role.RoleId, "lead", StringComparison.Ordinal) && role.Quantity == 2),
            "Scene packets should preserve lead roles and quantities.");
        AssertEx.True(
            scenePacket.OppositionRoles.Any(role => string.Equals(role.RoleId, "matrix-support", StringComparison.Ordinal) && role.Quantity == 1),
            "Scene packets should preserve matrix-support roles and quantities.");
        AssertEx.True(
            scenePacket.OppositionRoles.SelectMany(role => role.SpotlightStats).Any(stat => string.Equals(stat.StatId, "matrix.operations-dice", StringComparison.Ordinal)),
            "Scene packets should carry spotlight matrix stats for support roles.");
        AssertEx.True(
            scenePacket.PacketStats.Any(stat => string.Equals(stat.StatId, "combat.defense-dice.peak", StringComparison.Ordinal) && stat.Value.IntegerValue == 12L),
            "Scene packets should publish packet-level peak defense stats.");
        AssertEx.True(
            scenePacket.PacketStats.Any(stat => string.Equals(stat.StatId, "matrix.operations-dice.peak", StringComparison.Ordinal) && stat.Value.IntegerValue == 12L),
            "Scene packets should publish packet-level peak matrix stats.");
        AssertEx.True(
            scenePacket.PacketStats.All(stat => string.Equals(stat.RulesAnchor.SourcePacketId, scenePacket.ScenePacketId, StringComparison.Ordinal)),
            "Scene packet-stats should keep their source packet id explicit.");

        IReadOnlyList<ScenePacketContract> sr6Scenes = service.ListScenePackets(OwnerScope.LocalSingleUser, RulesetDefaults.Sr6);
        AssertEx.Equal(1, sr6Scenes.Count, "Ruleset filtering should keep scene packet lists deterministic.");
        AssertEx.Equal("ancients-smash-and-grab", sr6Scenes[0].ScenePacketId, "Ruleset filtering should return the seeded SR6 scene.");

        DefaultOppositionPacketContractService missingDataService = new(new StubNpcVaultRegistryService());

        OppositionPacketContract? reviewRequiredPack = missingDataService.GetOppositionPacket(OwnerScope.LocalSingleUser, "broken-pack", RulesetDefaults.Sr5);
        AssertEx.True(reviewRequiredPack is not null, "Packs with partial governed data should still project into opposition packet contracts.");
        AssertEx.Equal(
            GmPrepPacketBoundedLossPostures.ReviewRequired,
            reviewRequiredPack!.BoundedLossReceipt.Posture,
            "Missing governed pack members should escalate bounded-loss posture to review-required.");
        AssertEx.Equal(1, reviewRequiredPack.BoundedLossReceipt.ErrorCount, "Review-required pack receipts should summarize missing governed members as errors.");
        AssertEx.Equal(5, reviewRequiredPack.BoundedLossReceipt.RuleStatCount, "Review-required pack receipts should still count surviving grounded member stats.");
        AssertEx.Equal(5, reviewRequiredPack.BoundedLossReceipt.RuntimeBoundStatCount, "Review-required pack receipts should still count surviving runtime-bound member stats.");
        AssertEx.True(
            reviewRequiredPack.BoundedLossReceipt.Items.Any(item =>
                string.Equals(item.Code, "missing-entry:missing-scout", StringComparison.Ordinal)
                && string.Equals(item.MissingField, "entryId", StringComparison.Ordinal)),
            "Review-required pack receipts should call out missing governed member entries.");
        AssertEx.Equal(1, reviewRequiredPack.Members.Count, "Present governed members should remain available even when sibling members are missing.");

        ScenePacketContract? reviewRequiredScene = missingDataService.GetScenePacket(OwnerScope.LocalSingleUser, "broken-scene", RulesetDefaults.Sr5);
        AssertEx.True(reviewRequiredScene is not null, "Scenes with partial governed data should still project into scene packet contracts.");
        AssertEx.Equal(
            GmPrepPacketBoundedLossPostures.ReviewRequired,
            reviewRequiredScene!.BoundedLossReceipt.Posture,
            "Missing governed scene participants should escalate bounded-loss posture to review-required.");
        AssertEx.Equal(1, reviewRequiredScene.BoundedLossReceipt.ErrorCount, "Review-required scene receipts should summarize missing governed participants as errors.");
        AssertEx.Equal(5, reviewRequiredScene.BoundedLossReceipt.RuleStatCount, "Review-required scene receipts should still count surviving spotlight stats.");
        AssertEx.Equal(5, reviewRequiredScene.BoundedLossReceipt.RuntimeBoundStatCount, "Review-required scene receipts should still count surviving runtime-bound spotlight stats.");
        AssertEx.True(
            reviewRequiredScene.BoundedLossReceipt.Items.Any(item =>
                string.Equals(item.Code, "missing-entry:missing-scout", StringComparison.Ordinal)
                && string.Equals(item.MissingField, "entryId", StringComparison.Ordinal)),
            "Review-required scene receipts should call out missing governed participant entries.");

        OppositionPacketContract? runtimeUnboundPacket = missingDataService.GetOppositionPacket(OwnerScope.LocalSingleUser, "runtime-unbound-guard", RulesetDefaults.Sr5);
        AssertEx.True(runtimeUnboundPacket is not null, "Entries without runtime fingerprints should still project into opposition packet contracts.");
        AssertEx.True(runtimeUnboundPacket!.RuntimeFingerprint is null, "Entries without runtime fingerprints should stay explicitly unbound at the packet level.");
        AssertEx.Equal(
            GmPrepPacketBoundedLossPostures.BoundedLoss,
            runtimeUnboundPacket.BoundedLossReceipt.Posture,
            "Missing runtime fingerprints should stay bounded-loss instead of pretending the packet is fully grounded.");
        AssertEx.Equal(runtimeUnboundPacket.Members.Single().Stats.Count, runtimeUnboundPacket.BoundedLossReceipt.RuleStatCount, "Runtime-unbound packets should still count grounded stats.");
        AssertEx.Equal(0, runtimeUnboundPacket.BoundedLossReceipt.RuntimeBoundStatCount, "Runtime-unbound packets should keep runtime-bound stat counts honest.");
        AssertEx.True(
            runtimeUnboundPacket.BoundedLossReceipt.Items.Any(item => string.Equals(item.Code, "runtime-fingerprint-missing", StringComparison.Ordinal)),
            "Entries without runtime fingerprints should emit an explicit bounded-loss item.");
        AssertEx.True(
            runtimeUnboundPacket.Members.Single().Stats.All(stat => stat.RulesAnchor.RuntimeFingerprint is null),
            "Direct rules anchors should stay honest when runtime fingerprints are not yet bound.");
        AssertEx.True(
            runtimeUnboundPacket.Members.Single().Stats.All(stat => stat.ExplainTrace.RuntimeFingerprint is null),
            "Explain traces should stay honest when runtime fingerprints are not yet bound.");
        AssertEx.True(
            runtimeUnboundPacket.PacketStats.All(stat => stat.RulesAnchor.RuntimeFingerprint is null && stat.ExplainTrace.RuntimeFingerprint is null),
            "Packet-level aggregate stats should stay honest when runtime fingerprints are not yet bound.");
    }

    private sealed class StubNpcVaultRegistryService : INpcVaultRegistryService
    {
        private static readonly OwnerScope SystemOwner = new("system");
        private static readonly DateTimeOffset SeededAt = DateTimeOffset.Parse("2026-04-24T00:00:00+00:00");

        private readonly NpcEntryRegistryEntry[] _entries =
        [
            BuildEntry(
                entryId: "fallback-guard",
                title: "Fallback Guard",
                description: "Seeded guard for bounded-loss receipt verification.",
                threatTier: "medium",
                runtimeFingerprint: "sha256:bounded-proof",
                tags: ["corporate", "support"]),
            BuildEntry(
                entryId: "runtime-unbound-guard",
                title: "Runtime Unbound Guard",
                description: "Seeded guard without a runtime fingerprint so the receipt must stay explicit.",
                threatTier: "low",
                runtimeFingerprint: null,
                tags: ["checkpoint"])
        ];

        private readonly NpcPackRegistryEntry[] _packs =
        [
            new(
                Manifest: new NpcPackManifest(
                    PackId: "broken-pack",
                    Version: "1.0.0",
                    Title: "Broken Pack",
                    Description: "Pack with one missing governed member to verify review-required posture.",
                    RulesetId: RulesetDefaults.Sr5,
                    Entries:
                    [
                        new NpcPackMemberReference("fallback-guard", 1),
                        new NpcPackMemberReference("missing-scout", 1)
                    ],
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: ["checkpoint"]),
                Owner: SystemOwner,
                PublicationStatus: NpcPublicationStatuses.Published,
                UpdatedAtUtc: SeededAt)
        ];

        private readonly EncounterPackRegistryEntry[] _encounters =
        [
            new(
                Manifest: new EncounterPackManifest(
                    EncounterPackId: "broken-scene",
                    Version: "1.0.0",
                    Title: "Broken Scene",
                    Description: "Scene with one missing governed participant to verify review-required posture.",
                    RulesetId: RulesetDefaults.Sr5,
                    Participants:
                    [
                        new EncounterPackParticipantReference("fallback-guard", 1, "lead"),
                        new EncounterPackParticipantReference("missing-scout", 1, "support")
                    ],
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: ["checkpoint"]),
                Owner: SystemOwner,
                PublicationStatus: NpcPublicationStatuses.Published,
                UpdatedAtUtc: SeededAt)
        ];

        public IReadOnlyList<NpcEntryRegistryEntry> ListEntries(OwnerScope owner, string? rulesetId = null)
            => FilterByRuleset(_entries, rulesetId);

        public NpcEntryRegistryEntry? GetEntry(OwnerScope owner, string entryId, string? rulesetId = null)
            => ListEntries(owner, rulesetId)
                .FirstOrDefault(entry => string.Equals(entry.Manifest.EntryId, entryId, StringComparison.Ordinal));

        public IReadOnlyList<NpcPackRegistryEntry> ListPacks(OwnerScope owner, string? rulesetId = null)
            => FilterByRuleset(_packs, rulesetId);

        public NpcPackRegistryEntry? GetPack(OwnerScope owner, string packId, string? rulesetId = null)
            => ListPacks(owner, rulesetId)
                .FirstOrDefault(entry => string.Equals(entry.Manifest.PackId, packId, StringComparison.Ordinal));

        public IReadOnlyList<EncounterPackRegistryEntry> ListEncounterPacks(OwnerScope owner, string? rulesetId = null)
            => FilterByRuleset(_encounters, rulesetId);

        public EncounterPackRegistryEntry? GetEncounterPack(OwnerScope owner, string encounterPackId, string? rulesetId = null)
            => ListEncounterPacks(owner, rulesetId)
                .FirstOrDefault(entry => string.Equals(entry.Manifest.EncounterPackId, encounterPackId, StringComparison.Ordinal));

        private static NpcEntryRegistryEntry BuildEntry(
            string entryId,
            string title,
            string description,
            string threatTier,
            string? runtimeFingerprint,
            IReadOnlyList<string> tags)
            => new(
                Manifest: new NpcEntryManifest(
                    EntryId: entryId,
                    Version: "1.0.0",
                    Title: title,
                    Description: description,
                    RulesetId: RulesetDefaults.Sr5,
                    ThreatTier: threatTier,
                    Faction: "Test Faction",
                    RuntimeFingerprint: runtimeFingerprint,
                    SessionReady: true,
                    GmBoardReady: true,
                    Visibility: ArtifactVisibilityModes.Public,
                    TrustTier: ArtifactTrustTiers.Curated,
                    Tags: tags),
                Owner: SystemOwner,
                PublicationStatus: NpcPublicationStatuses.Published,
                UpdatedAtUtc: SeededAt);

        private static T[] FilterByRuleset<T>(IEnumerable<T> entries, string? rulesetId) where T : class
        {
            string? normalizedRulesetId = RulesetDefaults.NormalizeOptional(rulesetId);
            if (normalizedRulesetId is null)
            {
                return entries.ToArray();
            }

            return entries.Where(entry => entry switch
            {
                NpcEntryRegistryEntry npcEntry => string.Equals(npcEntry.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal),
                NpcPackRegistryEntry npcPack => string.Equals(npcPack.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal),
                EncounterPackRegistryEntry encounter => string.Equals(encounter.Manifest.RulesetId, normalizedRulesetId, StringComparison.Ordinal),
                _ => false
            }).ToArray();
        }
    }
}
