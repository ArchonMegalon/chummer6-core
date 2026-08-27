namespace Chummer.Contracts.Characters;

public enum CharacterVehicleChassisKind
{
    Vehicle,
    Drone
}

public enum CharacterVehicleChassisPosture
{
    Stock,
    GmApprovedCustom
}

public enum CharacterVehicleWorkshopProjectionStatus
{
    Exact,
    Unsupported
}

public enum CharacterVehicleWorkshopLegality
{
    Legal,
    Restricted,
    Forbidden
}

public enum CharacterVehicleWeaponMountComponentKind
{
    Size,
    Visibility,
    Flexibility,
    Control
}

public enum CharacterVehicleWorkshopCommitStatus
{
    Blocked,
    Committed,
    Recovered,
    Undone
}

public readonly record struct CharacterVehicleChassisSourceId(Guid Value);

public readonly record struct CharacterVehicleModificationSourceId(Guid Value);

public readonly record struct CharacterVehicleWeaponMountComponentSourceId(Guid Value);

public readonly record struct CharacterVehicleFactoryGearProjectionId(Guid Value);

public readonly record struct CharacterVehicleFactoryGearSourceId(Guid Value);

public readonly record struct CharacterVehicleFactoryModificationInstructionId(Guid Value);

public readonly record struct CharacterVehicleFactoryModificationSourceId(Guid Value);

public readonly record struct CharacterVehicleInstanceId(Guid Value);

public readonly record struct CharacterVehicleModificationInstanceId(Guid Value);

public readonly record struct CharacterVehicleWeaponMountInstanceId(Guid Value);

public readonly record struct CharacterVehicleWeaponMountComponentInstanceId(Guid Value);

public readonly record struct CharacterVehicleFactoryGearInstanceId(Guid Value);

public readonly record struct CharacterVehicleFactoryModificationInstanceId(Guid Value);

public sealed record CharacterVehicleWorkshopAvailability(
    int Value,
    CharacterVehicleWorkshopLegality Legality,
    bool AddToParent);

/// <summary>
/// Exact content authority used to project the catalog. Every digest is lower-case SHA-256.
/// The profile is the saved character's settings identity, not a UI-selected fallback.
/// </summary>
public sealed record CharacterVehicleWorkshopSourceBinding(
    string RulesetId,
    string ProfileId,
    string SemanticsVersion,
    string ProfileDigest,
    string VehiclesDigest,
    string WeaponsDigest,
    string GearDigest,
    string OverlayDigest,
    bool MultiplyRestrictedCost,
    decimal RestrictedCostMultiplier,
    bool MultiplyForbiddenCost,
    decimal ForbiddenCostMultiplier,
    bool Exact);

public sealed record CharacterVehicleWorkshopChassisEntry(
    CharacterVehicleChassisSourceId SourceId,
    CharacterVehicleChassisKind Kind,
    CharacterVehicleChassisPosture Posture,
    string Name,
    string Category,
    int Handling,
    int OffRoadHandling,
    int Acceleration,
    int OffRoadAcceleration,
    int Speed,
    int OffRoadSpeed,
    int Pilot,
    int Body,
    int Seats,
    int Armor,
    int Sensor,
    int ModificationSlots,
    int ModificationCapacity,
    decimal Cost,
    CharacterVehicleWorkshopAvailability Availability,
    string SourceBook,
    string Page,
    string GmAuthorityDigest,
    CharacterVehicleWorkshopProjectionStatus ProjectionStatus,
    string UnsupportedReason,
    IReadOnlyList<CharacterVehicleWorkshopFactoryGearEntry> FactoryGears,
    IReadOnlyList<CharacterVehicleWorkshopFactoryModificationEntry> FactoryModifications);

/// <summary>
/// A factory-installed child projected from one vehicle instruction and one
/// effective gear.xml source row. ProjectionId is source-stable; the saved
/// instance identity is derived from it and the new vehicle identity at commit.
/// </summary>
public sealed record CharacterVehicleWorkshopFactoryGearEntry(
    CharacterVehicleFactoryGearProjectionId ProjectionId,
    CharacterVehicleChassisSourceId ChassisSourceId,
    int Ordinal,
    CharacterVehicleFactoryGearSourceId SourceId,
    string Name,
    string Category,
    string Capacity,
    string ArmorCapacity,
    string MinimumRating,
    string MaximumRating,
    int Rating,
    decimal Quantity,
    CharacterVehicleWorkshopAvailability Availability,
    string Weight,
    string SourceBook,
    string Page,
    bool ConsumeCapacity,
    string SourceNodeDigest,
    string InstructionNodeDigest,
    CharacterVehicleWorkshopProjectionStatus ProjectionStatus,
    string UnsupportedReason);

/// <summary>
/// One factory-installed vehicle modification projected from a chassis instruction
/// and its unique enabled effective vehicles.xml modification source row. The
/// instruction identity is source-stable; the saved instance identity is derived
/// from it and the new vehicle identity at commit.
/// </summary>
public sealed record CharacterVehicleWorkshopFactoryModificationEntry(
    CharacterVehicleFactoryModificationInstructionId InstructionId,
    CharacterVehicleChassisSourceId ChassisSourceId,
    int Ordinal,
    CharacterVehicleFactoryModificationSourceId SourceId,
    string Name,
    string Category,
    string Limit,
    string Slots,
    string Capacity,
    int Rating,
    string MaximumRating,
    string RatingLabel,
    int ConditionMonitor,
    CharacterVehicleWorkshopAvailability Availability,
    string Cost,
    string Extra,
    string SourceBook,
    string Page,
    string Subsystems,
    string WeaponMountCategories,
    decimal AmmoBonus,
    decimal AmmoBonusPercent,
    string AmmoReplace,
    bool UseOwnAttributesForWeapon,
    string SourceNodeDigest,
    string InstructionNodeDigest,
    CharacterVehicleWorkshopProjectionStatus ProjectionStatus,
    string UnsupportedReason);

public sealed record CharacterVehicleWorkshopModificationEntry(
    CharacterVehicleModificationSourceId SourceId,
    string Name,
    string Category,
    int MinimumRating,
    int MaximumRating,
    decimal BaseCost,
    decimal CostPerRating,
    int BaseSlots,
    int SlotsPerRating,
    int BaseCapacity,
    int CapacityPerRating,
    CharacterVehicleWorkshopAvailability Availability,
    string SourceBook,
    string Page,
    IReadOnlyList<CharacterVehicleChassisSourceId> AllowedChassis,
    CharacterVehicleWorkshopProjectionStatus ProjectionStatus,
    string UnsupportedReason);

public sealed record CharacterVehicleWeaponMountComponentEntry(
    CharacterVehicleWeaponMountComponentSourceId SourceId,
    CharacterVehicleWeaponMountComponentKind Kind,
    string Name,
    decimal Cost,
    int Slots,
    int Capacity,
    CharacterVehicleWorkshopAvailability Availability,
    string SourceBook,
    string Page,
    IReadOnlyList<CharacterVehicleChassisSourceId> AllowedChassis,
    IReadOnlyList<CharacterVehicleWeaponMountComponentSourceId> RequiredComponents,
    IReadOnlyList<CharacterVehicleWeaponMountComponentSourceId> ForbiddenComponents,
    CharacterVehicleWorkshopProjectionStatus ProjectionStatus,
    string UnsupportedReason);

/// <summary>
/// Typed output of source/profile projection. Unsupported rows are intentionally retained;
/// callers can show why a row cannot be quoted instead of silently hiding source content.
/// </summary>
public sealed record CharacterVehicleWorkshopCatalog(
    CharacterVehicleWorkshopSourceBinding Binding,
    IReadOnlyList<CharacterVehicleWorkshopChassisEntry> Chassis,
    IReadOnlyList<CharacterVehicleWorkshopModificationEntry> Modifications,
    IReadOnlyList<CharacterVehicleWeaponMountComponentEntry> WeaponMountComponents,
    string DeclaredCatalogDigest);

public sealed record CharacterVehicleWorkshopUnsupportedRow(
    string Kind,
    Guid SourceId,
    string Name,
    string Reason);

public sealed record CharacterVehicleWorkshopPreparation(
    bool Exact,
    IReadOnlyList<string> Blockers,
    long ContentRevision,
    string CharacterDigest,
    decimal AvailableNuyen,
    CharacterVehicleWorkshopSourceBinding Binding,
    string CatalogDigest,
    IReadOnlyList<CharacterVehicleWorkshopChassisEntry> Chassis,
    IReadOnlyList<CharacterVehicleWorkshopModificationEntry> Modifications,
    IReadOnlyList<CharacterVehicleWeaponMountComponentEntry> WeaponMountComponents,
    IReadOnlyList<CharacterVehicleWorkshopUnsupportedRow> UnsupportedRows);

public sealed record CharacterVehicleWorkshopModificationSelection(
    CharacterVehicleModificationSourceId SourceId,
    CharacterVehicleModificationInstanceId InstanceId,
    int Rating);

public sealed record CharacterVehicleWeaponMountComponentSelection(
    CharacterVehicleWeaponMountComponentSourceId SourceId,
    CharacterVehicleWeaponMountComponentInstanceId InstanceId);

public sealed record CharacterVehicleWeaponMountSelection(
    CharacterVehicleWeaponMountInstanceId InstanceId,
    IReadOnlyList<CharacterVehicleWeaponMountComponentSelection> Components);

public sealed record CharacterVehicleWorkshopSelection(
    CharacterVehicleChassisSourceId ChassisSourceId,
    CharacterVehicleInstanceId NewVehicleInstanceId,
    string CustomName,
    string GmAuthorityDigest,
    IReadOnlyList<CharacterVehicleWorkshopModificationSelection> Modifications,
    IReadOnlyList<CharacterVehicleWeaponMountSelection> WeaponMounts);

public sealed record CharacterVehicleWorkshopQuoteLine(
    string Kind,
    Guid SourceId,
    Guid InstanceId,
    string Name,
    int Rating,
    decimal Cost,
    int Slots,
    int Capacity,
    CharacterVehicleWorkshopAvailability Availability,
    bool Exact,
    string BlockReason);

public sealed record CharacterVehicleWorkshopQuote(
    bool Exact,
    IReadOnlyList<string> Blockers,
    CharacterVehicleChassisSourceId ChassisSourceId,
    CharacterVehicleInstanceId VehicleInstanceId,
    string DisplayName,
    CharacterVehicleChassisKind Kind,
    CharacterVehicleChassisPosture Posture,
    decimal TotalCost,
    decimal NuyenDelta,
    int SlotsUsed,
    int SlotsRemaining,
    int CapacityUsed,
    int CapacityRemaining,
    CharacterVehicleWorkshopAvailability Availability,
    IReadOnlyList<CharacterVehicleWorkshopQuoteLine> Lines,
    string QuoteDigest);

public sealed record CharacterVehicleWorkshopCommitCommand(
    long ExpectedContentRevision,
    string ExpectedCharacterDigest,
    string ExpectedCatalogDigest,
    string ExpectedQuoteDigest,
    string IdempotencyKey,
    Guid NewExpenseId,
    DateTimeOffset ExpenseDate,
    CharacterVehicleWorkshopSelection Selection);

public sealed record CharacterVehicleWorkshopCommitReceipt(
    long ContentRevision,
    string CharacterDigest,
    long PreviousContentRevision,
    string PreviousCharacterDigest,
    decimal PreviousAvailableNuyen,
    string CatalogDigest,
    string QuoteDigest,
    string IdempotencyKeyDigest,
    string CommandDigest,
    CharacterVehicleInstanceId VehicleInstanceId,
    Guid ExpenseId,
    decimal NuyenDelta,
    string VehicleXmlDigest,
    string ExpenseXmlDigest,
    bool UndoReady,
    string ReceiptDigest);

public sealed record CharacterVehicleWorkshopUndoCommand(
    CharacterVehicleWorkshopCommitReceipt? Receipt);

public sealed record CharacterVehicleWorkshopCommitResult(
    CharacterVehicleWorkshopCommitStatus Status,
    string BlockReason,
    long PreviousContentRevision,
    long NewContentRevision,
    string PreviousCharacterDigest,
    string NewCharacterDigest,
    string CharacterXml,
    CharacterVehicleInstanceId VehicleInstanceId,
    Guid ExpenseId,
    decimal NuyenDelta,
    CharacterVehicleWorkshopCommitReceipt? Receipt);
