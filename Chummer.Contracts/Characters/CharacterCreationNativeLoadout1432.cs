namespace Chummer.Contracts.Characters;

// Additive definitions: do not extend the frozen V1/V2 observation registries.
public enum CharacterCreationNativeLoadoutSetting1432
{
    UnarmedImprovementsApplyToWeapons, DroneArmorMultiplierEnabled, DroneArmorMultiplier,
    NoArmorEncumbrance, LimbCount, EnforceCapacity, DroneMods, DroneModsMaximumPilot
}

public sealed record CharacterCreationNativeLoadoutDefinition1432(
    CharacterCreationNativeLoadoutSetting1432 Key, string Element, CharacterCreationSettingValueKind Kind);

public static class CharacterCreationNativeLoadoutDefinitions1432
{
    public static IReadOnlyList<CharacterCreationNativeLoadoutDefinition1432> All { get; } =
        Array.AsReadOnly(new[]
        {
            D(CharacterCreationNativeLoadoutSetting1432.UnarmedImprovementsApplyToWeapons, "unarmedimprovementsapplytoweapons"),
            D(CharacterCreationNativeLoadoutSetting1432.DroneArmorMultiplierEnabled, "dronearmormultiplierenabled"),
            D(CharacterCreationNativeLoadoutSetting1432.DroneArmorMultiplier, "dronearmorflatnumber", CharacterCreationSettingValueKind.Integer),
            D(CharacterCreationNativeLoadoutSetting1432.NoArmorEncumbrance, "noarmorencumbrance"),
            D(CharacterCreationNativeLoadoutSetting1432.LimbCount, "limbcount", CharacterCreationSettingValueKind.Integer),
            D(CharacterCreationNativeLoadoutSetting1432.EnforceCapacity, "enforcecapacity"),
            D(CharacterCreationNativeLoadoutSetting1432.DroneMods, "dronemods"),
            D(CharacterCreationNativeLoadoutSetting1432.DroneModsMaximumPilot, "dronemodsmaximumpilot")
        });

    private static CharacterCreationNativeLoadoutDefinition1432 D(CharacterCreationNativeLoadoutSetting1432 key,
        string element, CharacterCreationSettingValueKind kind = CharacterCreationSettingValueKind.Boolean)
        => new(key, element, kind);
}

public sealed record CharacterCreationNativeLoadoutPolicy1432(
    bool? UnarmedImprovementsApplyToWeapons, bool? DroneArmorMultiplierEnabled, int? DroneArmorMultiplier,
    bool? NoArmorEncumbrance, int? LimbCount, bool? EnforceCapacity, bool? DroneMods, bool? DroneModsMaximumPilot)
{
    public object? Value(string field) => field switch
    {
        "unarmedimprovementsapplytoweapons" => UnarmedImprovementsApplyToWeapons,
        "dronearmormultiplierenabled" => DroneArmorMultiplierEnabled,
        "dronearmorflatnumber" => DroneArmorMultiplier,
        "noarmorencumbrance" => NoArmorEncumbrance,
        "limbcount" => LimbCount,
        "enforcecapacity" => EnforceCapacity,
        "dronemods" => DroneMods,
        "dronemodsmaximumpilot" => DroneModsMaximumPilot,
        _ => null
    };
}

public enum CharacterCreationArmorEncumbranceBranch1432
{
    Unsupported, DisabledBySetting, NoEquippedArmor, AllEquippedArmorExempt, SourceProvenStockArmor
}

public enum CharacterCreationCapacityBranch1432
{
    Unsupported, DisabledBySetting, NoCapacityTargets, SourceProvenStockCapacity, SourceProvenRetainedFocusCapacity
}

/// <summary>Exact native property applicability for this graph. Unsupported branches
/// retain policy but confer no setting coverage. No attack, vehicle modification,
/// cyberlimb average or general populated capacity calculation is claimed.</summary>
public sealed record CharacterCreationNativeLoadoutProjection1432(
    CharacterCreationNativeLoadoutPolicy1432 Policy, bool Created, bool NoWeapons, bool NoVehicles,
    bool NoCyberware, int? TotalLimbCount, CharacterCreationArmorEncumbranceBranch1432 ArmorEncumbranceBranch,
    CharacterCreationCapacityBranch1432 CapacityBranch, string SettingsProfileId, string ProfileInputsDigest,
    string ProfileXmlDigest, string GraphDigest)
{
    // Absent on historical paths, preserving their serialized digest inputs.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationStockLoadoutProjection3478? StockLoadout3478 { get; init; }
    public int? ArmorEncumbrance => ArmorEncumbranceBranch switch
    {
        CharacterCreationArmorEncumbranceBranch1432.Unsupported => null,
        CharacterCreationArmorEncumbranceBranch1432.SourceProvenStockArmor => StockLoadout3478?.ArmorEncumbrance,
        _ => 0
    };
    public bool Supports(string field) => Policy.Value(field) is not null && (field switch
    {
        "unarmedimprovementsapplytoweapons" => NoWeapons || StockLoadout3478 is { NoUnarmedWeaponTargets: true },
        "dronearmormultiplierenabled" or "dronearmorflatnumber" or "dronemods" or "dronemodsmaximumpilot" => NoVehicles,
        "noarmorencumbrance" => ArmorEncumbranceBranch != CharacterCreationArmorEncumbranceBranch1432.Unsupported,
        "limbcount" => NoCyberware && TotalLimbCount.HasValue,
        "enforcecapacity" => CapacityBranch != CharacterCreationCapacityBranch1432.Unsupported,
        _ => false
    });
}

// Output evidence only. Instance/remaining-capacity rows are committed by the
// CapacityDigest; consumers mint this only after replaying the private proof.
public sealed record CharacterCreationStockLoadoutProjection3478(
    int ArmorEncumbrance, int ArmorOwners, int WeaponOwners, int CapacityTargets,
    decimal ArmorCapacityRemaining, string CapacityDigest, bool CapacityComplete,
    bool NoUnarmedWeaponTargets, string SourceAuthorityDigest);
