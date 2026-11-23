using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

/// <summary>Source-bound SR5 sheet reads. Null is unknown, never a zero or a purchased rating.</summary>
public sealed record CharacterSheetDerivedSection(
    IReadOnlyList<CharacterSheetAttributeValue> Attributes,
    IReadOnlyList<CharacterSheetSkillValue> Skills,
    CharacterPhysicalInitiativeProjection? PhysicalInitiative,
    int? DefensePool,
    int? PhysicalLimit,
    int? MentalLimit,
    int? SocialLimit,
    decimal? Essence,
    string? UnavailableReason)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterInitiativeModeOption>? InitiativeModes { get; init; }
    public IReadOnlyList<CharacterSheetGrantedTrait> GrantedTraits { get; init; } = [];
    public IReadOnlyList<CharacterSheetSkillUnlock> UnlockedSkills { get; init; } = [];
    // Absent on older records. Omit null to preserve their durable JSON/digest shape.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterSheetRuleModifier>? RuleModifiers { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DefenseUnavailableReason { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterSustainedSpellPreparationState? SustainedSpellPreparation { get; init; }
    public string? AuthorityDigest { get; init; }
    public CharacterSheetConditionValues? Condition { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CharacterSheetMovementValues? Movement { get; init; }
    // Source/instance-bound reads, not permission to acquire or use an owner. Null
    // preserves old durable record shapes; an unavailable sheet exposes none.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterSheetVehicleValue>? Vehicles { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterSheetComplexFormValue>? ComplexForms { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<CharacterSheetMatrixDeviceValue>? MatrixDevices { get; init; }
    public IReadOnlyList<CharacterSheetConditionalLimitValue> OwnedConditionalLimits { get; init; } = [];
    public IReadOnlyDictionary<string, int> OtherPoolsAndModifiers { get; init; } = new Dictionary<string, int>();
    public static CharacterSheetDerivedSection Unavailable(string reason) =>
        new([], [], null, null, null, null, null, null, reason);
}

/// <summary>Unmodified, current chassis values. Control mode, skills, noise, sensor,
/// gunnery and jumped-in bonuses are separate action consumers, never implied here.</summary>
public sealed record CharacterSheetVehicleValue(Guid OwnerId, Guid SourceId, string Name,
    CharacterVehicleChassisKind Kind, int Handling, int OffRoadHandling, int Body, int Armor,
    int PhysicalConditionMaximum, string SourceDigest);

/// <summary>Learned source-owned form. Threading level, fading and test pools require action context.</summary>
public sealed record CharacterSheetComplexFormValue(Guid OwnerId, Guid SourceId, string Name,
    string Target, string Duration, string FadingExpression, string SourceDigest);

/// <summary>Current source-bound gear persona, including native factory children.
/// No optional programs, overclocking, noise or action bonuses are applied.</summary>
public sealed record CharacterSheetMatrixDeviceValue(Guid OwnerId, Guid SourceId, string Name,
    bool Active, int Attack, int Sleaze, int DataProcessing, int Firewall, int DeviceRating,
    int ProgramLimit, string SourceDigest);

public sealed record CharacterSheetAttributeValue(string Name, int Total)
{
    /// <summary>Native Value before augmented modifiers, distinct from allocation points.</summary>
    public int? Unaugmented { get; init; }
    public int? AllocatedBase { get; init; }
    public int? AllocatedKarma { get; init; }
    public int? AugmentedModifier { get; init; }
}

/// <summary>Effective BOD/WIL feed monitors and recovery; recovery modifiers are not wounds.</summary>
public sealed record CharacterSheetConditionValues(int PhysicalMaximum, int StunMaximum, int WoundThreshold,
    int PhysicalThresholdOffset, int StunThresholdOffset, int WoundModifier,
    int PhysicalRecoveryPool, int StunRecoveryPool, int OverflowModifier)
{
    // Native CM offsets depend on both current tracks; the saved offsets alone cannot drive an overlay.
    public decimal ThresholdOffset { get; init; }
    public decimal SharedThresholdOffset { get; init; }
    public bool IgnorePhysical { get; init; }
    public bool IgnoreStun { get; init; }
    public CharacterSheetConditionValues WithDamage(int physical, int stun)
    {
        if (physical < 0 || stun < 0 || physical > PhysicalMaximum || stun > StunMaximum || WoundThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(physical));
        int Offset(int other, bool ignore)
        {
            decimal value = ignore ? ThresholdOffset + SharedThresholdOffset
                : Math.Max(ThresholdOffset, ThresholdOffset + SharedThresholdOffset - Math.Max(other - WoundThreshold - ThresholdOffset, 0));
            return checked((int)(value >= 0 ? decimal.Ceiling(value) : decimal.Floor(value)));
        }
        int p = IgnorePhysical ? int.MaxValue : Offset(stun, IgnoreStun);
        int s = IgnoreStun ? int.MaxValue : Offset(physical, IgnorePhysical);
        int wounds = checked((IgnorePhysical ? 0 : Math.Min(0, p - physical) / WoundThreshold)
            + (IgnoreStun ? 0 : Math.Min(0, s - stun) / WoundThreshold));
        return this with { PhysicalThresholdOffset = p, StunThresholdOffset = s, WoundModifier = wounds };
    }
}

/// <summary>Source-owned grant identity. Action resolution is not implied by a sheet trait.</summary>
public sealed record CharacterSheetGrantedTrait(string Kind, Guid SourceId, string Name, string SourceBook, string? ActionUnavailableReason = null)
{
    public string? MechanicsUnavailableReason { get; init; }
    public Guid OwnerId { get; init; }
    public Guid ParentMetatypeId { get; init; }
    public Guid SelectedMetatypeId { get; init; }
    public string Selection { get; init; } = "";
    public int Rating { get; init; }
}

/// <summary>A source-owned skill availability grant, not a rating or test bonus.</summary>
public sealed record CharacterSheetSkillUnlock(Guid OwnerId, Guid SourceId, string Name);

public sealed record CharacterSheetSkillValue(
    string Guid, string Name, string Attribute, int? EffectiveRating, int? DicePool,
    string? UnavailableReason, bool IsNativeLanguage = false)
{
    public int? LearnedRating { get; init; }
    /// <summary>Situational alternative pools; never already included in DicePool.</summary>
    public IReadOnlyList<CharacterSheetSpecializationValue> Specializations { get; init; } = [];
}

public sealed record CharacterSheetSpecializationValue(string Name, int Bonus, int DicePool);

public sealed record CharacterSheetConditionalLimitValue(string Limit, int Modifier, string Condition, bool Enabled);

/// <summary>Source-owned, scoped rules, not additions to every dice pool or permission to spend karma.
/// Movement replacements select the greatest active replacement; sprint values are meters per hit.
/// Conditional skill entries require a contextual action resolver; affected sheet pools remain unavailable.</summary>
public sealed record CharacterSheetRuleModifier(Guid OwnerId, CharacterSheetRuleModifierKind Kind,
    string Target, decimal Value, string Condition = "", string Exclude = "", bool AppliesToRating = false,
    string? ActionUnavailableReason = null)
{
    // Present only for portable source-only contributions; never a saved legacy row.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? SourceId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceDigest { get; init; }
}

public enum CharacterSheetRuleModifierKind
{
    AdditionalLimb,
    WalkMultiplierReplacement,
    RunMultiplierReplacement,
    SprintMetersPerHitReplacement,
    SprintMetersPerHitAddition,
    SkillCategoryDefaultBlocked,
    SkillCategoryKarmaCostPercent,
    SkillCategorySpecializationKarmaCostPercent,
    SkillGroupCategoryKarmaCostPercent,
    ConditionalSkill,
    ConditionalSkillGroup,
    ConditionalSkillCategory,
    ConditionalSkillAttribute,
    UnresolvedDefenseTest,
    // Already included once in DefensePool and the raw play defense basis.
    // Consumers must not add this explanatory contribution to either again.
    SourceDefenseTestPenalty
}

/// <summary>Final source-bound SR5 rates. A null availability is unknown; false is a proven zero mode.
/// Walk/run are metres per Combat Turn; sprint is additional metres per hit, never a distance total.</summary>
public sealed record CharacterSheetMovementMode(bool? IsAvailable, decimal? WalkMetersPerTurn,
    decimal? RunMetersPerTurn, decimal? SprintMetersPerHit, string? UnavailableReason);

public sealed record CharacterSheetMovementValues(CharacterSheetMovementMode Ground,
    CharacterSheetMovementMode Swim, CharacterSheetMovementMode Fly)
{
    public static CharacterSheetMovementValues Unavailable(string reason)
    {
        var unknown = new CharacterSheetMovementMode(null, null, null, null, reason);
        return new(unknown, unknown, unknown);
    }
}
