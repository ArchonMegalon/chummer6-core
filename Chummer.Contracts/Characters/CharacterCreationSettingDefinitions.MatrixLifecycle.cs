namespace Chummer.Contracts.Characters;

public static partial class CharacterCreationSettingDefinitions
{
    // Additive native projection vocabulary. Frozen V1/V2 observations retain
    // their original opaque occurrence identities and serialized bytes.
    public static IReadOnlyList<CharacterCreationSettingDefinition> MatrixLifecycle { get; } = Array.AsReadOnly(new[]
    {
        D(CharacterCreationObservedSetting.MinimumColdSimInitiativeDice, "mincoldsiminitiativedice"),
        D(CharacterCreationObservedSetting.MaximumColdSimInitiativeDice, "maxcoldsiminitiativedice"),
        D(CharacterCreationObservedSetting.MinimumHotSimInitiativeDice, "minhotsiminitiativedice"),
        D(CharacterCreationObservedSetting.MaximumHotSimInitiativeDice, "maxhotsiminitiativedice")
    });
}
