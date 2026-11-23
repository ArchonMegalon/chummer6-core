namespace Chummer.Contracts.Characters;

// Separate from the frozen V1/V2 observation vocabulary. These four source
// occurrences remain Opaque until private execution of their exact source leaf.
public enum CharacterCreationNativeCompatibilitySetting1430
{
    NoSingleArmorEncumbrance, ExtendAnyDetectionSpell,
    ProfileAllowSkillDiceRolling, AllowTechnomancerSchooling
}

public sealed record CharacterCreationNativeCompatibilityDefinition1430(
    CharacterCreationNativeCompatibilitySetting1430 Key, string Element);

public static class CharacterCreationNativeCompatibilityDefinitions1430
{
    public static IReadOnlyList<CharacterCreationNativeCompatibilityDefinition1430> All { get; }
        = Array.AsReadOnly(new CharacterCreationNativeCompatibilityDefinition1430[]
        {
            new CharacterCreationNativeCompatibilityDefinition1430(CharacterCreationNativeCompatibilitySetting1430.NoSingleArmorEncumbrance, "nosinglearmorencumbrance"),
            new(CharacterCreationNativeCompatibilitySetting1430.ExtendAnyDetectionSpell, "extendanydetectionspell"),
            new(CharacterCreationNativeCompatibilitySetting1430.ProfileAllowSkillDiceRolling, "allowskilldicerolling"),
            new(CharacterCreationNativeCompatibilitySetting1430.AllowTechnomancerSchooling, "allowtechnomancerschooling")
        });
}

public sealed record CharacterCreationNativeCompatibilityPolicy1430(
    bool? NoSingleArmorEncumbrance, bool? ExtendAnyDetectionSpell,
    bool? ProfileAllowSkillDiceRolling, bool? AllowTechnomancerSchooling);

/// <summary>The native spell-selection control for an actually compiled standard
/// spell. This does not select a custom extension or evaluate spell drain.</summary>
public sealed record CharacterCreationSpellExtensionControl1430(
    string SourceId, string SpellDigest, string Category, bool HasExtendedArea,
    bool ListedByExtensionPreference, bool CustomExtensionAvailable,
    bool SelectedExtended, bool SelectedCustomExtended);

/// <summary>Source-version-bounded retained settings and executed applicability.
/// NoSingleArmorEncumbrance is stored without a runtime accessor in the reviewed
/// source. ProfileAllowSkillDiceRolling never controls GlobalSettings or dice UI.
/// SchoolingControlEnabled is null outside the native RESEnabled UI branch;
/// no initiation/submersion purchase or discount is evaluated by this result.</summary>
public sealed record CharacterCreationNativeCompatibilityProjection1430(
    string SourceVersion, CharacterCreationNativeCompatibilityPolicy1430 Policy,
    IReadOnlyList<CharacterCreationSpellExtensionControl1430> Spells,
    bool ResonanceEnabled, bool? SchoolingControlEnabled, bool Created,
    string SettingsProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string GraphDigest);

public sealed partial record CharacterCreationCompletionFinalizationProjection
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public CharacterCreationNativeCompatibilityProjection1430? NativeCompatibility1430 { get; init; }
}
