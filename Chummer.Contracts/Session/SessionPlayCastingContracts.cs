using System.Text.Json.Serialization;

namespace Chummer.Contracts.Session;

// Service-issued source capability. Never accepted as command authority.
public sealed record SessionPlayCastingChoice(string CapabilityId, Guid SpellId, Guid SourceId,
    string DisplayName, string SourceBook, string SourcePage, string SourceDigest,
    string DefenseAttribute, string DamageType, string DrainExpression, int DrainAdjustment,
    int Magic, int RawCastingPool, int DrainResistancePool)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IndirectKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Element { get; init; }
}
public sealed record SessionPlayCastingAuthority(string Digest, IReadOnlyList<SessionPlayCastingChoice> Choices,
    IReadOnlyList<string> Blockers)
{
    // Omitted from old authority bytes. Adoption adds this capability without
    // changing the direct choices or their original blocked-source evidence.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayIndirectCapability? Indirect { get; init; }
}
public sealed record SessionPlayCastingOption(SessionPlayCastingChoice Choice, int CastingPool);
public sealed record SessionPlayCastingProjection(string AuthorityDigest, IReadOnlyList<SessionPlayCastingOption> Choices,
    IReadOnlyList<string> Blockers, IReadOnlyList<SessionPlayCastOutcome> History)
{
    public const string Coverage = "Ordinary instantaneous Direct Combat (one living target) or adopted SR5 Core-book non-elemental or source-bound acid/fire/electricity Indirect Combat and the specifically reviewed SG cold spells (single target or area). Physical clear line of fire/sight, player-declared ordinary living targets only. Player-entered rolls only. No Edge, reagents, reckless/multiple casting, counterspelling, situational modifiers or glitches. Records caster in-track drain only. Indirect target resistance results are declared calculations, not authenticated NPC updates. Elemental reviews include target-specific protection and declared equipment/effect follow-up. Target monitors and equipment are never written; ongoing turn/movement actions, other supplemental spells, knockdown and overflow remain open. Canonical character and PDF are unchanged.";
}
public sealed record SessionPlayCastOutcome(string EventId, Guid SpellId, string DisplayName, int Force,
    int CastingPool, int CastingHits, int LimitedHits, int OppositionPool, int OppositionHits, int NetHits,
    string TargetDamageType, int DrainValue, int DrainResistancePool, int DrainHits, string DrainTrack,
    int DrainBoxes, int ConditionBefore, int ConditionAfter, string Provenance)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayIndirectOutcome? Indirect { get; init; }
}
public sealed record SessionPlayCastingSetup(string CapabilityId, string DisplayName, int Force, int Magic,
    int CastingPool, int Limit, int DrainValue, int DrainResistancePool, string DefenseAttribute)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IndirectKind { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Element { get; init; }
}
public sealed record SessionPlayCastingReview(SessionPatchRequest Request, string PayloadDigest,
    SessionPlayCastingSetup Setup, SessionPlayCastOutcome? Outcome);

// Explicit capability review for an immutable older origin; never an origin replacement.
public sealed record SessionPlayMagicUpgradeOffer(
    SessionPlayCastingAuthority? Casting, SessionPlaySustainingAuthority? Sustaining,
    IReadOnlyDictionary<string, Chummer.Contracts.Rulesets.RulesetCapabilityValue> Payload);

public sealed record SessionPlayIndirectCapability(IReadOnlyList<SessionPlayCastingChoice> Choices,
    IReadOnlyList<string> ReplacedBlockers)
{
    // Absent in every legacy authority/receipt. Explicit adoption is required.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayElementalCapability? Elemental { get; init; }
}
public sealed record SessionPlayElementalCapability(IReadOnlyList<SessionPlayCastingChoice> Choices,
    IReadOnlyList<string> ReplacedBlockers);
public sealed record SessionPlayIndirectInput(string Aim, bool OrdinaryTargetsConfirmed,
    IReadOnlyList<int> ScatterDice, IReadOnlyList<SessionPlayIndirectTargetInput> Targets);
public sealed record SessionPlayIndirectTargetInput(string Label, int Reaction, int Intuition,
    int DefenseHits, int Body, int Armor, int SoakHits)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayElementalTargetInput? Elemental { get; init; }
}
public sealed record SessionPlayIndirectTargetResult(string Label, string Resolution, int DefensePool, int DefenseHits,
    int NetHits, int DamageValue, int AdjustedArmor, int SoakPool, int SoakHits, string DamageType, int Boxes)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayElementalTargetResult? Elemental { get; init; }
}
public sealed record SessionPlayIndirectOutcome(string Kind, string Aim, int DamageValue, int ArmorPenetration,
    int RadiusMeters, int ScatterMeters, int? ScatterDirectionTotal, IReadOnlyList<SessionPlayIndirectTargetResult> Targets)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SessionPlayIceSheetResult? IceSheet { get; init; }
}
public sealed record SessionPlayIceSheetResult(int CrossingThreshold, int RadiusMeters, string AimReference,
    IReadOnlyList<SessionPlayTableInputRequirement> RequiredTableInputs);
public sealed record SessionPlayTableInputRequirement(string Key, string ValueType, string Purpose);
public sealed record SessionPlayIndirectUpgradeOffer(SessionPlayIndirectCapability Capability,
    IReadOnlyDictionary<string, Chummer.Contracts.Rulesets.RulesetCapabilityValue> Payload);

// Declarations from the table, never credentials or authority over another actor.
public sealed record SessionPlayElementalTargetInput(int ProtectionRating, bool EquipmentComplete,
    IReadOnlyList<SessionPlayElementalItemInput> Items, int? ElectricalTurnsRemaining,
    int? InitiativeScore, string? AcidSmoke)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<SessionPlayColdItemInput>? ColdItems { get; init; }
}
public sealed record SessionPlayColdItemInput(string Label, int Armor, int Hits, string RollOutcome);
public sealed record SessionPlayElementalItemInput(string Label, int Armor, int FireResistance,
    int IgnitionHits, int ExistingFireDamage);
public sealed record SessionPlayElementalTargetResult(string Element, int ProtectionRating,
    IReadOnlyList<SessionPlayElementalItemResult> Items, SessionPlayElectricalResult? Electricity,
    string? AcidSmoke, IReadOnlyList<string> TableFollowUp);
public sealed record SessionPlayElementalItemResult(string Label, string Resolution, int ArmorBefore,
    int ArmorAfter, int? TestPool, int? TestHits, int? Threshold, int? FireDamage, int? NextTurnFireDamage);
public sealed record SessionPlayElectricalResult(int ActionAndDefensePenalty, int TurnsRemaining,
    int InitiativeLossNow, int InitiativeLossNextTurn);
