namespace Chummer.Contracts.Characters;

/// <summary>Core-owned, per-item catalog statistics. Not an equipped loadout or a combat action.</summary>
public sealed record Sr6CreationEquipmentProfile(
    Guid ItemId, string CatalogId, string SourceName, int Quantity,
    Sr6CreationArmorProfile? Armor, Sr6CreationMatrixDeviceProfile? Matrix,
    IReadOnlyList<Sr6CreationEquipmentTrait> Traits,
    IReadOnlyList<string> SourceAnchorIds, string SourceSha256)
{
    public bool StatisticsAvailable => Armor is not null || Matrix is not null;
}

public sealed record Sr6CreationArmorProfile(int DefenseRatingBonus, int ModificationCapacity);

/// <summary>Null means this device supplies no catalog value for that field, never a zero rating.</summary>
public sealed record Sr6CreationMatrixDeviceProfile(
    int DeviceRating, int? Attack, int? Sleaze, int? DataProcessing, int? Firewall,
    int? ActiveProgramSlots, int? MaximumSlaves, int? NoiseReduction, int? SharedProgramSlots);

/// <summary>Conditional effects and included components, retained but not automatically activated.</summary>
public sealed record Sr6CreationEquipmentTrait(string Id, int? Value, string SourceAnchorId);
