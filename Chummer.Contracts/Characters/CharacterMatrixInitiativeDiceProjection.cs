namespace Chummer.Contracts.Characters;

/// <summary>The four explicit SR5 VR settings, without inferred source defaults.</summary>
public sealed record CharacterMatrixInitiativePolicy(
    int MinimumColdDice, int MaximumColdDice, int MinimumHotDice, int MaximumHotDice)
{
    public int Value(CharacterCreationObservedSetting key) => key switch
    {
        CharacterCreationObservedSetting.MinimumColdSimInitiativeDice => MinimumColdDice,
        CharacterCreationObservedSetting.MaximumColdSimInitiativeDice => MaximumColdDice,
        CharacterCreationObservedSetting.MinimumHotSimInitiativeDice => MinimumHotDice,
        CharacterCreationObservedSetting.MaximumHotSimInitiativeDice => MaximumHotDice,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };
}

/// <summary>Inputs actually read by the native branch. AI reads the general initiative
/// cap; non-AI does not. The original improvements and active device remain inspectable.</summary>
public sealed record CharacterMatrixInitiativeSourceInputs(bool DepthEnabled,
    int? BodyMetatypeMaximum, int? GeneralMaximumDice, string ImprovementsXml, string? ActiveCommlinkXml);

/// <summary>Derived cold/hot dice. A public projection cannot certify execution.
/// IsAI selects Character.MatrixInitiativeDice for BOTH VR modes; their separate
/// caps remain retained policy and are not applied on that branch.</summary>
public sealed record CharacterMatrixInitiativeDiceProjection(
    int ColdDice, int HotDice, bool IsAI, decimal MatrixDiceValue, int MatrixDiceAdjustment,
    decimal? MatrixDiceAddValue, int? MatrixDiceAddAdjustment,
    CharacterMatrixInitiativePolicy Policy, CharacterMatrixInitiativeSourceInputs SourceInputs,
    bool Created, string SettingsProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string GraphDigest);
