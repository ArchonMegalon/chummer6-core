using Chummer.Contracts.Content;
using Chummer.Contracts.Rulesets;

namespace Chummer.Contracts.Campaign;

public static class OppositionPacketKinds
{
    public const string NpcEntry = "npc-entry";
    public const string NpcPack = "npc-pack";
}

public static class ScenePacketKinds
{
    public const string EncounterPack = "encounter-pack";
}

public static class GmPrepPacketStatCategories
{
    public const string Combat = "combat";
    public const string Awareness = "awareness";
    public const string Magic = "magic";
    public const string Matrix = "matrix";
    public const string Mobility = "mobility";
}

public static class GmPrepPacketStatUnits
{
    public const string DicePool = "dice-pool";
    public const string Rating = "rating";
}

public static class GmPrepPacketBoundedLossPostures
{
    public const string None = "none";
    public const string BoundedLoss = "bounded-loss";
    public const string ReviewRequired = "review-required";
    public const string Blocked = "blocked";
}

public static class GmPrepPacketBoundedLossSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Error = "error";
}

public static class ScenePacketEngagementKinds
{
    public const string General = "general";
    public const string Checkpoint = "checkpoint";
    public const string Chase = "chase";
    public const string Ritual = "ritual";
    public const string SmashAndGrab = "smash-and-grab";
}

public sealed record GmPrepPacketBoundedLossItem(
    string Code,
    string Severity,
    string Summary,
    string? MissingField = null,
    string? NextSafeAction = null);

public sealed record GmPrepPacketBoundedLossReceipt(
    string ReceiptId,
    string Posture,
    string Summary,
    string NextSafeAction,
    int ItemCount,
    int WarningCount,
    int ErrorCount,
    IReadOnlyList<GmPrepPacketBoundedLossItem> Items,
    string? PacketId = null,
    string? PacketKind = null,
    string? RulesetId = null,
    int? RuleStatCount = null,
    int? RuntimeBoundStatCount = null);

public sealed record GmPrepPacketRulesAnchor(
    string RulesetId,
    string SourceEntryId,
    string RulePointer,
    string CapabilityDescriptorPointer,
    string? ThreatTier = null,
    string? RuntimeFingerprint = null,
    string? SourcePacketId = null,
    IReadOnlyList<string>? SourceEntryIds = null);

public sealed record GmPrepPacketRuleStat(
    string StatId,
    string Label,
    string Category,
    string Unit,
    string ValueSummary,
    RulesetCapabilityValue Value,
    GmPrepPacketRulesAnchor RulesAnchor,
    RulesetExplainTrace ExplainTrace);

public sealed record OppositionPacketMemberContract(
    string MemberId,
    string Label,
    string Role,
    int Quantity,
    IReadOnlyList<GmPrepPacketRuleStat> Stats,
    string? SourceEntryId = null,
    string? ThreatTier = null,
    string? Faction = null,
    IReadOnlyList<string>? Tags = null);

public sealed record OppositionPacketContract(
    string PacketId,
    string PacketKind,
    string Version,
    string Title,
    string Description,
    string RulesetId,
    string Visibility,
    string TrustTier,
    IReadOnlyList<OppositionPacketMemberContract> Members,
    IReadOnlyList<GmPrepPacketRuleStat> PacketStats,
    GmPrepPacketBoundedLossReceipt BoundedLossReceipt,
    string? ThreatTier = null,
    string? Faction = null,
    string? RuntimeFingerprint = null,
    IReadOnlyList<string>? Tags = null);

public sealed record ScenePacketRoleContract(
    string RoleId,
    string Label,
    int Quantity,
    IReadOnlyList<GmPrepPacketRuleStat> SpotlightStats,
    string? SourceEntryId = null,
    string? SourcePacketMemberId = null,
    string? TacticalSummary = null);

public sealed record ScenePacketContract(
    string ScenePacketId,
    string SceneKind,
    string Version,
    string Title,
    string Description,
    string RulesetId,
    string EngagementKind,
    string Visibility,
    string TrustTier,
    IReadOnlyList<ScenePacketRoleContract> OppositionRoles,
    IReadOnlyList<GmPrepPacketRuleStat> PacketStats,
    GmPrepPacketBoundedLossReceipt BoundedLossReceipt,
    string? RuntimeFingerprint = null,
    string? SourceEncounterPackId = null,
    string? OpeningSummary = null,
    string? EscalationSummary = null,
    IReadOnlyList<string>? Tags = null);
