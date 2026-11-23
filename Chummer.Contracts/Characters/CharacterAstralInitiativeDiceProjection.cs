namespace Chummer.Contracts.Characters;

/// <summary>Derived from a native candidate and its retained settings. This is
/// neither a saved character field nor authority to certify source coverage.</summary>
public sealed record CharacterAstralInitiativeDiceProjection(
    int Dice, int Minimum, int Maximum, bool Created, string SettingsProfileId,
    string ProfileInputsDigest, string ProfileXmlDigest, string GraphDigest);
