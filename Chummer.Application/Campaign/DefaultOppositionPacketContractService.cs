using Chummer.Application.Content;
using Chummer.Contracts.Campaign;
using Chummer.Contracts.Content;
using Chummer.Contracts.Owners;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Campaign;

public sealed class DefaultOppositionPacketContractService : IOppositionPacketContractService
{
    private static readonly RulesetGasUsage EmptyGasUsage = new(0, 0, 0);

    private readonly INpcVaultRegistryService _npcVaultRegistryService;

    public DefaultOppositionPacketContractService(INpcVaultRegistryService npcVaultRegistryService)
    {
        _npcVaultRegistryService = npcVaultRegistryService ?? throw new ArgumentNullException(nameof(npcVaultRegistryService));
    }

    public IReadOnlyList<OppositionPacketContract> ListOppositionPackets(OwnerScope owner, string? rulesetId = null)
    {
        List<OppositionPacketContract> packets =
        [
            .._npcVaultRegistryService.ListEntries(owner, rulesetId).Select(BuildOppositionPacket),
            .._npcVaultRegistryService.ListPacks(owner, rulesetId).Select(entry => BuildOppositionPacket(owner, entry))
        ];

        return packets
            .OrderBy(static packet => packet.RulesetId, StringComparer.Ordinal)
            .ThenBy(static packet => packet.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public OppositionPacketContract? GetOppositionPacket(OwnerScope owner, string packetId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packetId);

        foreach (string? candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            NpcEntryRegistryEntry? entry = _npcVaultRegistryService.GetEntry(owner, packetId, candidateRulesetId);
            if (entry is not null)
            {
                return BuildOppositionPacket(entry);
            }

            NpcPackRegistryEntry? pack = _npcVaultRegistryService.GetPack(owner, packetId, candidateRulesetId);
            if (pack is not null)
            {
                return BuildOppositionPacket(owner, pack);
            }
        }

        return null;
    }

    public IReadOnlyList<ScenePacketContract> ListScenePackets(OwnerScope owner, string? rulesetId = null)
    {
        return _npcVaultRegistryService.ListEncounterPacks(owner, rulesetId)
            .Select(entry => BuildScenePacket(owner, entry))
            .OrderBy(static packet => packet.RulesetId, StringComparer.Ordinal)
            .ThenBy(static packet => packet.Title, StringComparer.Ordinal)
            .ToArray();
    }

    public ScenePacketContract? GetScenePacket(OwnerScope owner, string scenePacketId, string? rulesetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenePacketId);

        foreach (string? candidateRulesetId in EnumerateRulesetIds(rulesetId))
        {
            EncounterPackRegistryEntry? entry = _npcVaultRegistryService.GetEncounterPack(owner, scenePacketId, candidateRulesetId);
            if (entry is not null)
            {
                return BuildScenePacket(owner, entry);
            }
        }

        return null;
    }

    private OppositionPacketContract BuildOppositionPacket(NpcEntryRegistryEntry entry)
    {
        OppositionPacketMemberContract member = BuildMemberFromEntry(entry, role: "opposition", quantity: 1);
        IReadOnlyList<GmPrepPacketRuleStat> packetStats = BuildPacketStats(
            packetId: entry.Manifest.EntryId,
            packetKind: OppositionPacketKinds.NpcEntry,
            rulesetId: entry.Manifest.RulesetId,
            threatTier: entry.Manifest.ThreatTier,
            runtimeFingerprint: entry.Manifest.RuntimeFingerprint,
            sourceStats: member.Stats);
        GmPrepPacketBoundedLossReceipt receipt = CreateEntryReceipt(entry, member.Stats);

        return new OppositionPacketContract(
            PacketId: entry.Manifest.EntryId,
            PacketKind: OppositionPacketKinds.NpcEntry,
            Version: entry.Manifest.Version,
            Title: entry.Manifest.Title,
            Description: entry.Manifest.Description,
            RulesetId: entry.Manifest.RulesetId,
            Visibility: entry.Manifest.Visibility,
            TrustTier: entry.Manifest.TrustTier,
            Members: [member],
            PacketStats: packetStats,
            BoundedLossReceipt: receipt,
            ThreatTier: entry.Manifest.ThreatTier,
            Faction: entry.Manifest.Faction,
            RuntimeFingerprint: entry.Manifest.RuntimeFingerprint,
            Tags: entry.Manifest.Tags ?? []);
    }

    private OppositionPacketContract BuildOppositionPacket(OwnerScope owner, NpcPackRegistryEntry pack)
    {
        List<OppositionPacketMemberContract> members = [];
        List<GmPrepPacketBoundedLossItem> receiptItems = [];

        foreach (NpcPackMemberReference member in pack.Manifest.Entries)
        {
            NpcEntryRegistryEntry? entry = _npcVaultRegistryService.GetEntry(owner, member.EntryId, pack.Manifest.RulesetId);
            if (entry is null)
            {
                receiptItems.Add(new GmPrepPacketBoundedLossItem(
                    Code: $"missing-entry:{member.EntryId}",
                    Severity: GmPrepPacketBoundedLossSeverities.Error,
                    Summary: $"Packet member '{member.EntryId}' is referenced but the governed NPC entry is missing.",
                    MissingField: "entryId",
                    NextSafeAction: "Restore the missing NPC entry before promoting this pack."));
                continue;
            }

            members.Add(BuildMemberFromEntry(entry, role: "opposition", quantity: member.Quantity));
            receiptItems.AddRange(CreateEntryReceipt(entry, members[^1].Stats).Items.Select(item => item with
            {
                Code = $"{member.EntryId}:{item.Code}",
                Summary = $"{entry.Manifest.Title}: {item.Summary}"
            }));
        }

        string? runtimeFingerprint = ResolveRuntimeFingerprint(members, receiptItems);
        IReadOnlyList<GmPrepPacketRuleStat> packetStats = BuildPacketStats(
            packetId: pack.Manifest.PackId,
            packetKind: OppositionPacketKinds.NpcPack,
            rulesetId: pack.Manifest.RulesetId,
            threatTier: ResolveThreatTier(members.Select(static member => member.ThreatTier)),
            runtimeFingerprint: runtimeFingerprint,
            sourceStats: members.SelectMany(static member => member.Stats));
        GmPrepPacketBoundedLossReceipt receipt = CreateAggregateReceipt(
            receiptId: $"prep-receipt:{pack.Manifest.PackId}",
            packetId: pack.Manifest.PackId,
            packetKind: OppositionPacketKinds.NpcPack,
            rulesetId: pack.Manifest.RulesetId,
            title: pack.Manifest.Title,
            reviewSummary: "governed roster review",
            items: receiptItems,
            sourceStats: members.SelectMany(static member => member.Stats));

        return new OppositionPacketContract(
            PacketId: pack.Manifest.PackId,
            PacketKind: OppositionPacketKinds.NpcPack,
            Version: pack.Manifest.Version,
            Title: pack.Manifest.Title,
            Description: pack.Manifest.Description,
            RulesetId: pack.Manifest.RulesetId,
            Visibility: pack.Manifest.Visibility,
            TrustTier: pack.Manifest.TrustTier,
            Members: members,
            PacketStats: packetStats,
            BoundedLossReceipt: receipt,
            RuntimeFingerprint: runtimeFingerprint,
            Tags: pack.Manifest.Tags ?? []);
    }

    private ScenePacketContract BuildScenePacket(OwnerScope owner, EncounterPackRegistryEntry encounter)
    {
        List<ScenePacketRoleContract> roles = [];
        List<GmPrepPacketBoundedLossItem> receiptItems = [];

        foreach (EncounterPackParticipantReference participant in encounter.Manifest.Participants)
        {
            NpcEntryRegistryEntry? entry = _npcVaultRegistryService.GetEntry(owner, participant.EntryId, encounter.Manifest.RulesetId);
            if (entry is null)
            {
                receiptItems.Add(new GmPrepPacketBoundedLossItem(
                    Code: $"missing-entry:{participant.EntryId}",
                    Severity: GmPrepPacketBoundedLossSeverities.Error,
                    Summary: $"Scene role '{participant.Role ?? participant.EntryId}' references missing NPC entry '{participant.EntryId}'.",
                    MissingField: "entryId",
                    NextSafeAction: "Restore the missing NPC entry before staging this scene packet."));
                continue;
            }

            OppositionPacketMemberContract member = BuildMemberFromEntry(entry, participant.Role ?? "opposition", participant.Quantity);
            roles.Add(new ScenePacketRoleContract(
                RoleId: string.IsNullOrWhiteSpace(participant.Role) ? participant.EntryId : participant.Role!,
                Label: BuildRoleLabel(entry.Manifest.Title, participant.Role),
                Quantity: Math.Max(1, participant.Quantity),
                SpotlightStats: member.Stats,
                SourceEntryId: entry.Manifest.EntryId,
                SourcePacketMemberId: member.MemberId,
                TacticalSummary: BuildTacticalSummary(entry, participant.Role)));

            receiptItems.AddRange(CreateEntryReceipt(entry, member.Stats).Items.Select(item => item with
            {
                Code = $"{participant.EntryId}:{item.Code}",
                Summary = $"{entry.Manifest.Title}: {item.Summary}"
            }));
        }

        string engagementKind = ResolveEngagementKind(encounter.Manifest.Tags);
        string? runtimeFingerprint = ResolveRuntimeFingerprint(roles, receiptItems);
        IReadOnlyList<GmPrepPacketRuleStat> packetStats = BuildPacketStats(
            packetId: encounter.Manifest.EncounterPackId,
            packetKind: ScenePacketKinds.EncounterPack,
            rulesetId: encounter.Manifest.RulesetId,
            threatTier: ResolveThreatTier(roles.Select(static role => role.SpotlightStats.FirstOrDefault()?.RulesAnchor.ThreatTier)),
            runtimeFingerprint: runtimeFingerprint,
            sourceStats: roles.SelectMany(static role => role.SpotlightStats));
        GmPrepPacketBoundedLossReceipt receipt = CreateAggregateReceipt(
            receiptId: $"prep-receipt:{encounter.Manifest.EncounterPackId}",
            packetId: encounter.Manifest.EncounterPackId,
            packetKind: ScenePacketKinds.EncounterPack,
            rulesetId: encounter.Manifest.RulesetId,
            title: encounter.Manifest.Title,
            reviewSummary: "scene packet review",
            items: receiptItems,
            sourceStats: roles.SelectMany(static role => role.SpotlightStats));

        return new ScenePacketContract(
            ScenePacketId: encounter.Manifest.EncounterPackId,
            SceneKind: ScenePacketKinds.EncounterPack,
            Version: encounter.Manifest.Version,
            Title: encounter.Manifest.Title,
            Description: encounter.Manifest.Description,
            RulesetId: encounter.Manifest.RulesetId,
            EngagementKind: engagementKind,
            Visibility: encounter.Manifest.Visibility,
            TrustTier: encounter.Manifest.TrustTier,
            OppositionRoles: roles,
            PacketStats: packetStats,
            BoundedLossReceipt: receipt,
            RuntimeFingerprint: runtimeFingerprint,
            SourceEncounterPackId: encounter.Manifest.EncounterPackId,
            OpeningSummary: BuildOpeningSummary(encounter.Manifest.Title, engagementKind),
            EscalationSummary: BuildEscalationSummary(encounter.Manifest.Title, engagementKind),
            Tags: encounter.Manifest.Tags ?? []);
    }

    private static OppositionPacketMemberContract BuildMemberFromEntry(NpcEntryRegistryEntry entry, string role, int quantity)
    {
        IReadOnlyList<string> tags = entry.Manifest.Tags ?? [];
        StatProfile profile = ResolveStatProfile(entry.Manifest.RulesetId, entry.Manifest.ThreatTier, role, tags);

        return new OppositionPacketMemberContract(
            MemberId: entry.Manifest.EntryId,
            Label: entry.Manifest.Title,
            Role: string.IsNullOrWhiteSpace(role) ? "opposition" : role.Trim(),
            Quantity: Math.Max(1, quantity),
            Stats: BuildStats(entry, role, profile),
            SourceEntryId: entry.Manifest.EntryId,
            ThreatTier: entry.Manifest.ThreatTier,
            Faction: entry.Manifest.Faction,
            Tags: tags);
    }

    private static IReadOnlyList<GmPrepPacketRuleStat> BuildStats(NpcEntryRegistryEntry entry, string role, StatProfile profile)
    {
        List<GmPrepPacketRuleStat> stats =
        [
            CreateStat(entry, role, "combat.attack-dice", "Attack Dice", GmPrepPacketStatCategories.Combat, profile.AttackDice),
            CreateStat(entry, role, "combat.defense-dice", "Defense Dice", GmPrepPacketStatCategories.Combat, profile.DefenseDice),
            CreateStat(entry, role, "combat.soak-dice", "Soak Dice", GmPrepPacketStatCategories.Combat, profile.SoakDice),
            CreateStat(entry, role, "combat.initiative", "Initiative", GmPrepPacketStatCategories.Combat, profile.Initiative, unit: GmPrepPacketStatUnits.Rating),
            CreateStat(entry, role, "awareness.perception-dice", "Perception Dice", GmPrepPacketStatCategories.Awareness, profile.PerceptionDice)
        ];

        if (profile.MagicDice.HasValue)
        {
            stats.Add(CreateStat(entry, role, "magic.spellcasting-dice", "Spellcasting Dice", GmPrepPacketStatCategories.Magic, profile.MagicDice.Value));
        }

        if (profile.MatrixDice.HasValue)
        {
            stats.Add(CreateStat(entry, role, "matrix.operations-dice", "Matrix Operations Dice", GmPrepPacketStatCategories.Matrix, profile.MatrixDice.Value));
        }

        if (profile.MobilityRating.HasValue)
        {
            stats.Add(CreateStat(entry, role, "mobility.rating", "Mobility Rating", GmPrepPacketStatCategories.Mobility, profile.MobilityRating.Value, unit: GmPrepPacketStatUnits.Rating));
        }

        return stats;
    }

    private static GmPrepPacketRuleStat CreateStat(
        NpcEntryRegistryEntry entry,
        string role,
        string statId,
        string label,
        string category,
        int value,
        string unit = GmPrepPacketStatUnits.DicePool)
    {
        RulesetCapabilityValue capabilityValue = RulesetCapabilityBridge.FromObject(value);
        string runtimeFingerprint = string.IsNullOrWhiteSpace(entry.Manifest.RuntimeFingerprint)
            ? "runtime:unbound"
            : entry.Manifest.RuntimeFingerprint!;
        string rulePointer = $"{entry.Manifest.RulesetId}#gm-prep.{NormalizeToken(statId)}.{NormalizeToken(entry.Manifest.ThreatTier)}";
        string capabilityDescriptorPointer = $"gm-prep.packet/{entry.Manifest.EntryId}/{NormalizeToken(statId)}";
        IReadOnlyList<RulesetEvidencePointer> evidence =
        [
            new(
                Kind: RulesetEvidencePointerKinds.RuntimeLock,
                Pointer: runtimeFingerprint),
            new(
                Kind: RulesetEvidencePointerKinds.RuleReference,
                Pointer: rulePointer,
                RuleId: statId),
            new(
                Kind: RulesetEvidencePointerKinds.CapabilityDescriptor,
                Pointer: capabilityDescriptorPointer)
        ];

        RulesetTraceStep step = new(
            ProviderId: "gm-prep.packet.contracts",
            CapabilityId: statId,
            PackId: entry.Manifest.EntryId,
            ExplanationKey: $"gm-prep.packet.stat.{NormalizeToken(statId)}",
            ExplanationParameters:
            [
                new("entryId", RulesetCapabilityBridge.FromObject(entry.Manifest.EntryId)),
                new("rulesetId", RulesetCapabilityBridge.FromObject(entry.Manifest.RulesetId)),
                new("threatTier", RulesetCapabilityBridge.FromObject(entry.Manifest.ThreatTier)),
                new("role", RulesetCapabilityBridge.FromObject(role)),
                new("value", capabilityValue)
            ],
            Category: category,
            Modifier: null,
            Certain: false,
            RuleId: statId,
            Evidence: evidence);

        RulesetExplainTrace explain = new(
            TargetKey: statId,
            FinalValue: capabilityValue,
            SummaryKey: "gm-prep.packet.stat.summary",
            SummaryParameters:
            [
                new("label", RulesetCapabilityBridge.FromObject(label)),
                new("entryId", RulesetCapabilityBridge.FromObject(entry.Manifest.EntryId)),
                new("threatTier", RulesetCapabilityBridge.FromObject(entry.Manifest.ThreatTier))
            ],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: "gm-prep.packet.contracts",
                    CapabilityId: statId,
                    PackId: entry.Manifest.EntryId,
                    Success: true,
                    Steps: [step],
                    GasUsage: EmptyGasUsage,
                    Evidence: evidence)
            ],
            AggregateGasUsage: EmptyGasUsage,
            RuntimeFingerprint: string.IsNullOrWhiteSpace(entry.Manifest.RuntimeFingerprint) ? null : entry.Manifest.RuntimeFingerprint,
            ProfileId: $"{entry.Manifest.RulesetId}.gm-prep",
            Evidence: evidence);

        return new GmPrepPacketRuleStat(
            StatId: statId,
            Label: label,
            Category: category,
            Unit: unit,
            ValueSummary: $"{value} {unit}",
            Value: capabilityValue,
            RulesAnchor: new GmPrepPacketRulesAnchor(
                RulesetId: entry.Manifest.RulesetId,
                SourceEntryId: entry.Manifest.EntryId,
                RulePointer: rulePointer,
                CapabilityDescriptorPointer: capabilityDescriptorPointer,
                ThreatTier: entry.Manifest.ThreatTier,
                RuntimeFingerprint: string.IsNullOrWhiteSpace(entry.Manifest.RuntimeFingerprint) ? null : entry.Manifest.RuntimeFingerprint),
            ExplainTrace: explain);
    }

    private static IReadOnlyList<GmPrepPacketRuleStat> BuildPacketStats(
        string packetId,
        string packetKind,
        string rulesetId,
        string? threatTier,
        string? runtimeFingerprint,
        IEnumerable<GmPrepPacketRuleStat> sourceStats)
    {
        List<GmPrepPacketRuleStat> sourceList = sourceStats.ToList();
        List<GmPrepPacketRuleStat> packetStats = [];

        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "combat.attack-dice", "Peak Attack Dice", GmPrepPacketStatCategories.Combat);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "combat.defense-dice", "Peak Defense Dice", GmPrepPacketStatCategories.Combat);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "combat.initiative", "Peak Initiative", GmPrepPacketStatCategories.Combat, GmPrepPacketStatUnits.Rating);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "awareness.perception-dice", "Peak Perception Dice", GmPrepPacketStatCategories.Awareness);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "magic.spellcasting-dice", "Peak Spellcasting Dice", GmPrepPacketStatCategories.Magic);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "matrix.operations-dice", "Peak Matrix Operations Dice", GmPrepPacketStatCategories.Matrix);
        AddAggregateStat(packetStats, packetId, packetKind, rulesetId, threatTier, runtimeFingerprint, sourceList, "mobility.rating", "Peak Mobility Rating", GmPrepPacketStatCategories.Mobility, GmPrepPacketStatUnits.Rating);

        return packetStats;
    }

    private static void AddAggregateStat(
        List<GmPrepPacketRuleStat> packetStats,
        string packetId,
        string packetKind,
        string rulesetId,
        string? threatTier,
        string? runtimeFingerprint,
        IReadOnlyList<GmPrepPacketRuleStat> sourceStats,
        string sourceStatId,
        string label,
        string category,
        string unit = GmPrepPacketStatUnits.DicePool)
    {
        List<GmPrepPacketRuleStat> matches = sourceStats
            .Where(stat => string.Equals(stat.StatId, sourceStatId, StringComparison.Ordinal) && stat.Value.IntegerValue is long)
            .ToList();
        if (matches.Count == 0)
        {
            return;
        }

        long value = matches.Max(static stat => stat.Value.IntegerValue ?? 0L);
        string statId = $"{sourceStatId}.peak";
        string rulePointer = $"{rulesetId}#gm-prep.packet-summary.{NormalizeToken(sourceStatId)}.peak";
        string capabilityDescriptorPointer = $"gm-prep.packet-summary/{packetId}/{NormalizeToken(sourceStatId)}";
        List<string> sourceEntryIds = matches
            .Select(static stat => stat.RulesAnchor.SourceEntryId)
            .Where(static entryId => !string.IsNullOrWhiteSpace(entryId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        List<RulesetEvidencePointer> evidence = [];
        if (!string.IsNullOrWhiteSpace(runtimeFingerprint))
        {
            evidence.Add(new RulesetEvidencePointer(
                Kind: RulesetEvidencePointerKinds.RuntimeLock,
                Pointer: runtimeFingerprint!));
        }

        evidence.Add(new RulesetEvidencePointer(
            Kind: RulesetEvidencePointerKinds.RuleReference,
            Pointer: rulePointer,
            RuleId: statId));
        evidence.Add(new RulesetEvidencePointer(
            Kind: RulesetEvidencePointerKinds.CapabilityDescriptor,
            Pointer: capabilityDescriptorPointer));
        evidence.AddRange(matches.Select(match => new RulesetEvidencePointer(
            Kind: RulesetEvidencePointerKinds.RuleReference,
            Pointer: match.RulesAnchor.RulePointer,
            RuleId: match.StatId)));
        RulesetCapabilityValue capabilityValue = RulesetCapabilityBridge.FromObject(value);
        RulesetTraceStep step = new(
            ProviderId: "gm-prep.packet.contracts.aggregate",
            CapabilityId: statId,
            PackId: packetId,
            ExplanationKey: $"gm-prep.packet.summary.{NormalizeToken(sourceStatId)}",
            ExplanationParameters:
            [
                new("packetId", RulesetCapabilityBridge.FromObject(packetId)),
                new("packetKind", RulesetCapabilityBridge.FromObject(packetKind)),
                new("rulesetId", RulesetCapabilityBridge.FromObject(rulesetId)),
                new("sourceStatId", RulesetCapabilityBridge.FromObject(sourceStatId)),
                new("value", capabilityValue)
            ],
            Category: category,
            Modifier: null,
            Certain: false,
            RuleId: statId,
            Evidence: evidence);
        RulesetExplainTrace explain = new(
            TargetKey: statId,
            FinalValue: capabilityValue,
            SummaryKey: "gm-prep.packet.summary.peak",
            SummaryParameters:
            [
                new("label", RulesetCapabilityBridge.FromObject(label)),
                new("packetId", RulesetCapabilityBridge.FromObject(packetId)),
                new("packetKind", RulesetCapabilityBridge.FromObject(packetKind))
            ],
            Providers:
            [
                new RulesetProviderTrace(
                    ProviderId: "gm-prep.packet.contracts.aggregate",
                    CapabilityId: statId,
                    PackId: packetId,
                    Success: true,
                    Steps: [step],
                    GasUsage: EmptyGasUsage,
                    Evidence: evidence)
            ],
            AggregateGasUsage: EmptyGasUsage,
            RuntimeFingerprint: runtimeFingerprint,
            ProfileId: $"{rulesetId}.gm-prep.packet-summary",
            Evidence: evidence);

        packetStats.Add(new GmPrepPacketRuleStat(
            StatId: statId,
            Label: label,
            Category: category,
            Unit: unit,
            ValueSummary: $"{value} {unit}",
            Value: capabilityValue,
            RulesAnchor: new GmPrepPacketRulesAnchor(
                RulesetId: rulesetId,
                SourceEntryId: sourceEntryIds.FirstOrDefault() ?? packetId,
                RulePointer: rulePointer,
                CapabilityDescriptorPointer: capabilityDescriptorPointer,
                ThreatTier: threatTier,
                RuntimeFingerprint: runtimeFingerprint,
                SourcePacketId: packetId,
                SourceEntryIds: sourceEntryIds),
            ExplainTrace: explain));
    }

    private static GmPrepPacketBoundedLossReceipt CreateEntryReceipt(NpcEntryRegistryEntry entry, IReadOnlyList<GmPrepPacketRuleStat> stats)
    {
        List<GmPrepPacketBoundedLossItem> items =
        [
            new(
                Code: "exact-loadout-not-carried",
                Severity: GmPrepPacketBoundedLossSeverities.Warning,
                Summary: "The canonical packet keeps threat-tier and rules-backed stat posture, but exact gear and authored action sequencing remain scenario-owned.",
                MissingField: "gearLoadout",
                NextSafeAction: "Open the source NPC entry before final encounter balancing."),
            new(
                Code: "tactics-remain-authored",
                Severity: GmPrepPacketBoundedLossSeverities.Warning,
                Summary: "Opening tactics, spell order, and matrix scripts are intentionally left as GM-authored prep instead of hidden auto-play truth.",
                MissingField: "tacticalScript",
                NextSafeAction: "Attach a scene note or briefing artifact for table-specific tactics.")
        ];

        if (string.IsNullOrWhiteSpace(entry.Manifest.RuntimeFingerprint))
        {
            items.Add(new GmPrepPacketBoundedLossItem(
                Code: "runtime-fingerprint-missing",
                Severity: GmPrepPacketBoundedLossSeverities.Warning,
                Summary: "The packet is rules-backed, but the source entry does not yet pin a runtime fingerprint.",
                MissingField: "runtimeFingerprint",
                NextSafeAction: "Bind the packet to a promoted runtime before publication."));
        }

        return new GmPrepPacketBoundedLossReceipt(
            ReceiptId: $"prep-receipt:{entry.Manifest.EntryId}",
            Posture: GmPrepPacketBoundedLossPostures.BoundedLoss,
            Summary: $"{entry.Manifest.Title} keeps deterministic stat anchors while leaving exact authored tactics and loadout detail reviewable.",
            NextSafeAction: "Inspect the source entry and attach scenario-specific notes before final GM handoff.",
            ItemCount: items.Count,
            WarningCount: items.Count(item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Warning, StringComparison.Ordinal)),
            ErrorCount: items.Count(item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Error, StringComparison.Ordinal)),
            Items: items,
            PacketId: entry.Manifest.EntryId,
            PacketKind: OppositionPacketKinds.NpcEntry,
            RulesetId: entry.Manifest.RulesetId,
            RuleStatCount: stats.Count,
            RuntimeBoundStatCount: stats.Count(static stat => !string.IsNullOrWhiteSpace(stat.RulesAnchor.RuntimeFingerprint)));
    }

    private static GmPrepPacketBoundedLossReceipt CreateAggregateReceipt(
        string receiptId,
        string packetId,
        string packetKind,
        string rulesetId,
        string title,
        string reviewSummary,
        IReadOnlyList<GmPrepPacketBoundedLossItem> items,
        IEnumerable<GmPrepPacketRuleStat> sourceStats)
    {
        IReadOnlyList<GmPrepPacketBoundedLossItem> effectiveItems = items.Count == 0
            ? [new GmPrepPacketBoundedLossItem(
                Code: "packet-grounded",
                Severity: GmPrepPacketBoundedLossSeverities.Info,
                Summary: "The packet is fully backed by governed member entries.",
                NextSafeAction: "Bind the packet into the target campaign or event lane.")]
            : items;

        string posture = effectiveItems.Any(static item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Error, StringComparison.Ordinal))
            ? GmPrepPacketBoundedLossPostures.ReviewRequired
            : effectiveItems.Any(static item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Warning, StringComparison.Ordinal))
                ? GmPrepPacketBoundedLossPostures.BoundedLoss
                : GmPrepPacketBoundedLossPostures.None;
        int warningCount = effectiveItems.Count(item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Warning, StringComparison.Ordinal));
        int errorCount = effectiveItems.Count(item => string.Equals(item.Severity, GmPrepPacketBoundedLossSeverities.Error, StringComparison.Ordinal));
        List<GmPrepPacketRuleStat> stats = sourceStats.ToList();

        return new GmPrepPacketBoundedLossReceipt(
            ReceiptId: receiptId,
            Posture: posture,
            Summary: $"{title} keeps governed opposition membership, with {reviewSummary} remaining explicit instead of hidden in automation.",
            NextSafeAction: posture == GmPrepPacketBoundedLossPostures.ReviewRequired
                ? "Resolve missing governed member data before promoting this packet."
                : "Review any scenario-specific tactics before final handoff.",
            ItemCount: effectiveItems.Count,
            WarningCount: warningCount,
            ErrorCount: errorCount,
            Items: effectiveItems,
            PacketId: packetId,
            PacketKind: packetKind,
            RulesetId: rulesetId,
            RuleStatCount: stats.Count,
            RuntimeBoundStatCount: stats.Count(static stat => !string.IsNullOrWhiteSpace(stat.RulesAnchor.RuntimeFingerprint)));
    }

    private static IEnumerable<string?> EnumerateRulesetIds(string? rulesetId)
    {
        string? normalized = RulesetDefaults.NormalizeOptional(rulesetId);
        if (normalized is not null)
        {
            yield return normalized;
        }

        yield return null;
    }

    private static string? ResolveRuntimeFingerprint(
        IEnumerable<OppositionPacketMemberContract> members,
        List<GmPrepPacketBoundedLossItem> receiptItems)
        => ResolveRuntimeFingerprint(
            members.SelectMany(static member => member.Stats).Select(static stat => stat.ExplainTrace.RuntimeFingerprint),
            receiptItems);

    private static string? ResolveRuntimeFingerprint(
        IEnumerable<ScenePacketRoleContract> roles,
        List<GmPrepPacketBoundedLossItem> receiptItems)
        => ResolveRuntimeFingerprint(
            roles.SelectMany(static role => role.SpotlightStats).Select(static stat => stat.ExplainTrace.RuntimeFingerprint),
            receiptItems);

    private static string? ResolveRuntimeFingerprint(
        IEnumerable<string?> runtimeFingerprints,
        List<GmPrepPacketBoundedLossItem> receiptItems)
    {
        List<string> distinctFingerprints = runtimeFingerprints
            .Where(static fingerprint => !string.IsNullOrWhiteSpace(fingerprint))
            .Select(static fingerprint => fingerprint!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctFingerprints.Count <= 1)
        {
            return distinctFingerprints.FirstOrDefault();
        }

        receiptItems.Add(new GmPrepPacketBoundedLossItem(
            Code: "runtime-fingerprint-mixed",
            Severity: GmPrepPacketBoundedLossSeverities.Warning,
            Summary: "The packet merges governed entries from multiple runtime fingerprints, so packet-level stats stay unbound until one promoted runtime is chosen.",
            MissingField: "runtimeFingerprint",
            NextSafeAction: "Normalize the packet onto one promoted runtime before publishing packet-level trust."));
        return null;
    }

    private static string BuildRoleLabel(string title, string? role)
        => string.IsNullOrWhiteSpace(role)
            ? title
            : $"{title} ({role!.Trim()})";

    private static string? ResolveThreatTier(IEnumerable<string?> threatTiers)
    {
        string[] orderedThreatTiers = ["low", "medium", "high"];
        string? normalized = threatTiers
            .Where(static threatTier => !string.IsNullOrWhiteSpace(threatTier))
            .Select(static threatTier => threatTier!.Trim().ToLowerInvariant())
            .OrderByDescending(threatTier => Array.IndexOf(orderedThreatTiers, threatTier))
            .FirstOrDefault();
        return normalized;
    }

    private static string BuildTacticalSummary(NpcEntryRegistryEntry entry, string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return $"{entry.Manifest.Title} keeps its governed threat posture and can be staged directly into the scene.";
        }

        return $"{entry.Manifest.Title} fills the '{role!.Trim()}' lane while keeping rules-backed stat anchors attached to the packet.";
    }

    private static string ResolveEngagementKind(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return ScenePacketEngagementKinds.General;
        }

        HashSet<string> tagSet = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (tagSet.Contains("checkpoint"))
        {
            return ScenePacketEngagementKinds.Checkpoint;
        }

        if (tagSet.Contains("chase"))
        {
            return ScenePacketEngagementKinds.Chase;
        }

        if (tagSet.Contains("ritual"))
        {
            return ScenePacketEngagementKinds.Ritual;
        }

        if (tagSet.Contains("smash-grab") || tagSet.Contains("smash-and-grab"))
        {
            return ScenePacketEngagementKinds.SmashAndGrab;
        }

        return ScenePacketEngagementKinds.General;
    }

    private static string BuildOpeningSummary(string title, string engagementKind)
        => engagementKind switch
        {
            ScenePacketEngagementKinds.Checkpoint => $"{title} opens with overt access control, line-of-sight pressure, and one obvious flashpoint for negotiation or breach.",
            ScenePacketEngagementKinds.Chase => $"{title} opens in motion, so initiative and mobility anchors matter before static cover does.",
            ScenePacketEngagementKinds.Ritual => $"{title} opens with overwatch and ritual pressure already active, pushing the table toward fast interruption choices.",
            ScenePacketEngagementKinds.SmashAndGrab => $"{title} opens on a fast objective clock where opposition pressure should accelerate once the score is visible.",
            _ => $"{title} opens as a governed scene packet with reusable opposition roles and explicit prep posture."
        };

    private static string BuildEscalationSummary(string title, string engagementKind)
        => engagementKind switch
        {
            ScenePacketEngagementKinds.Checkpoint => $"{title} escalates by tightening lanes, raising alarm pressure, and committing the support role.",
            ScenePacketEngagementKinds.Chase => $"{title} escalates through route compression, harder pursuit checks, and faster support intervention.",
            ScenePacketEngagementKinds.Ritual => $"{title} escalates when the ritual or astral lane survives long enough to feed stronger magical effects.",
            ScenePacketEngagementKinds.SmashAndGrab => $"{title} escalates as the objective is touched, forcing the table to trade speed against exposure.",
            _ => $"{title} escalates by turning its tagged pressure lanes into explicit GM choices rather than hidden automation."
        };

    private static StatProfile ResolveStatProfile(string rulesetId, string threatTier, string role, IReadOnlyList<string> tags)
    {
        string normalizedRulesetId = RulesetDefaults.NormalizeRequired(rulesetId);
        string normalizedThreatTier = string.IsNullOrWhiteSpace(threatTier)
            ? "medium"
            : threatTier.Trim().ToLowerInvariant();
        string normalizedRole = string.IsNullOrWhiteSpace(role)
            ? "opposition"
            : role.Trim().ToLowerInvariant();
        HashSet<string> tagSet = tags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .Select(static tag => tag.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        StatProfile profile = normalizedRulesetId switch
        {
            RulesetDefaults.Sr6 => normalizedThreatTier switch
            {
                "high" => new StatProfile(AttackDice: 12, DefenseDice: 10, SoakDice: 9, Initiative: 14, PerceptionDice: 8),
                "low" => new StatProfile(AttackDice: 6, DefenseDice: 5, SoakDice: 5, Initiative: 8, PerceptionDice: 5),
                _ => new StatProfile(AttackDice: 9, DefenseDice: 7, SoakDice: 7, Initiative: 11, PerceptionDice: 6)
            },
            _ => normalizedThreatTier switch
            {
                "high" => new StatProfile(AttackDice: 14, DefenseDice: 11, SoakDice: 13, Initiative: 18, PerceptionDice: 10),
                "low" => new StatProfile(AttackDice: 7, DefenseDice: 6, SoakDice: 6, Initiative: 9, PerceptionDice: 6),
                _ => new StatProfile(AttackDice: 10, DefenseDice: 8, SoakDice: 9, Initiative: 13, PerceptionDice: 7)
            }
        };

        if (tagSet.Contains("matrix") || normalizedRole.Contains("matrix", StringComparison.Ordinal))
        {
            profile = profile with { MatrixDice = Math.Max(profile.AttackDice, normalizedRulesetId == RulesetDefaults.Sr6 ? 10 : 12), AttackDice = Math.Max(4, profile.AttackDice - 1) };
        }

        if (tagSet.Contains("magic") || tagSet.Contains("ritual") || normalizedRole.Contains("overwatch", StringComparison.Ordinal))
        {
            profile = profile with { MagicDice = Math.Max(profile.AttackDice, normalizedRulesetId == RulesetDefaults.Sr6 ? 11 : 13), PerceptionDice = profile.PerceptionDice + 1 };
        }

        if (tagSet.Contains("vehicle") || tagSet.Contains("chase") || normalizedRole.Contains("vanguard", StringComparison.Ordinal))
        {
            profile = profile with { MobilityRating = normalizedRulesetId == RulesetDefaults.Sr6 ? 8 : 6, Initiative = profile.Initiative + 2 };
        }

        if (tagSet.Contains("support") || normalizedRole.Contains("support", StringComparison.Ordinal))
        {
            profile = profile with { DefenseDice = profile.DefenseDice + 1, AttackDice = Math.Max(4, profile.AttackDice - 1) };
        }

        if (normalizedRole.Contains("lead", StringComparison.Ordinal))
        {
            profile = profile with { DefenseDice = profile.DefenseDice + 1 };
        }

        return profile;
    }

    private static string NormalizeToken(string value)
        => value.Trim().ToLowerInvariant().Replace('.', '-').Replace('_', '-').Replace(' ', '-');

    private sealed record StatProfile(
        int AttackDice,
        int DefenseDice,
        int SoakDice,
        int Initiative,
        int PerceptionDice,
        int? MagicDice = null,
        int? MatrixDice = null,
        int? MobilityRating = null);
}
