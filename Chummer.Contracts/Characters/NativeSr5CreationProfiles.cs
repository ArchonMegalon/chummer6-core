namespace Chummer.Contracts.Characters;

/// <summary>
/// Named native SR5 creation presets. These are new source identities: earlier
/// all-source and restricted profiles retain their original files and meaning.
/// The selected package uses a native extension, not upstream Chummer5a semantics.
/// </summary>
public static class NativeSr5CreationProfiles
{
    public const string SettingsFile = "settings-native-sr5.xml";
    public const string PackageDirectory = "Native SR5 Lifestyle Mechanics v1";
    public const string PackageId = "de19af38-7eea-4d98-a709-1f5d47ab2714";
    public const string PackageVersion = "1.0";
    public const string PackageSelectionKey = PackageId + ">" + PackageVersion;
    public const string Priority = "451e06ce-378e-5a66-a711-91f07fd182cf";
    public const string SumToTen = "0083432e-627d-55b4-a284-571432768054";
    public const string Karma = "81de7064-2530-5deb-a83e-aae71f517c1b";
    // A separate file/identity keeps every previously bound preset byte-stable.
    public const string KarmaQualityPrompts = "8abc02b8-41b4-5491-9907-8ccdd65e2806";
    public const string KarmaQualityPromptsSettingsFile = "settings-native-sr5-karma-prompts.xml";
    public const string KarmaQualityPromptElement = "chummerkarmaqualityprompts";
    public const string KarmaQualityPromptCapability = "chummer.karma-quality-source-text.v1";
    public const string LifeModules = "e7b7f73d-eb72-50e8-91df-e0b2fa7e6a13";

    public static bool IsNativeProfile(string? profileId)
        => profileId is Priority or SumToTen or Karma or KarmaQualityPrompts or LifeModules;

    public static bool TryResolve(string? buildMethod, out string profileId)
    {
        profileId = buildMethod switch
        {
            CharacterCreationBuildMethods.Priority => Priority,
            CharacterCreationBuildMethods.SumToTen => SumToTen,
            CharacterCreationBuildMethods.Karma => KarmaQualityPrompts,
            CharacterCreationBuildMethods.LifeModules => LifeModules,
            _ => string.Empty
        };
        return profileId.Length != 0;
    }
}
