namespace Chummer.Contracts.Characters;

/// <summary>Core-owned, per-item catalog statistics. Not an equipped loadout or a combat action.</summary>
public sealed record Sr6CreationEquipmentProfile(
    Guid ItemId, string CatalogId, string SourceName, int Quantity,
    Sr6CreationArmorProfile? Armor, Sr6CreationMatrixDeviceProfile? Matrix,
    IReadOnlyList<Sr6CreationEquipmentTrait> Traits,
    IReadOnlyList<string> SourceAnchorIds, string SourceSha256)
{
    public Sr6CreationWeaponProfile? Weapon { get; init; }
    public bool StatisticsAvailable => Armor is not null || Matrix is not null || Weapon is not null;
}

public sealed record Sr6CreationArmorProfile(int DefenseRatingBonus, int ModificationCapacity);

/// <summary>Null means this device supplies no catalog value for that field, never a zero rating.</summary>
public sealed record Sr6CreationMatrixDeviceProfile(
    int DeviceRating, int? Attack, int? Sleaze, int? DataProcessing, int? Firewall,
    int? ActiveProgramSlots, int? MaximumSlaves, int? NoiseReduction, int? SharedProgramSlots);

/// <summary>Conditional effects and included components, retained but not automatically activated.</summary>
public sealed record Sr6CreationEquipmentTrait(string Id, int? Value, string SourceAnchorId);

/// <summary>Printed, per-weapon statistics, not an attack resolution or a loaded/equipped weapon.</summary>
public sealed record Sr6CreationWeaponProfile(
    IReadOnlyList<Sr6CreationWeaponAttackProfile> Attacks,
    IReadOnlyList<string> IncludedAccessoryIds,
    int? MinimumCarryStrength,
    IReadOnlyList<Sr6CreationEquipmentTrait> Traits);

/// <summary>
/// A dash in the book is null, not zero. The attribute is added only to this attack's
/// rating, not damage. Payload damage is unresolved until actual ammunition is selected.
/// </summary>
public sealed record Sr6CreationWeaponAttackProfile(
    string Id, string SkillId, string? RequiredWeaponSpecialization,
    int? DamageValue, string DamageKind, bool Electrical, string? PayloadKind,
    Sr6CreationWeaponAttackRatings AttackRatings, string? AttackRatingAttribute,
    IReadOnlyList<string> FireModes, IReadOnlyList<Sr6CreationWeaponMagazine> Magazines,
    int? MaximumRangeMeters, string SourceAnchorId);

public sealed record Sr6CreationWeaponAttackRatings(int? Close, int? Near, int? Medium, int? Far, int? Extreme);

/// <summary>Alternative feed/capacity options. Does not grant ammunition, spares, or a loaded magazine.</summary>
public sealed record Sr6CreationWeaponMagazine(int Capacity, string FeedType);
