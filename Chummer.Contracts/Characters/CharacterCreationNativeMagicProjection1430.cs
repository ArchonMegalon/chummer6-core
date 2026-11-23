namespace Chummer.Contracts.Characters;

// Separate additive definition surface: V1/V2 observations and their wire bytes stay frozen.
public enum CharacterCreationNativeMagicSetting1430
{
    SpiritForceBasedOnTotalMagic, AllowInitiationInCreation, IgnoreArt, BoundSpiritExpression,
    RegisteredSpriteExpression, IgnoreComplexFormLimit, FreeSpiritPowerPointsMagic,
    IncreasedImprovedAbilityModifier, DicePenaltySustaining
}

public sealed record CharacterCreationNativeMagicDefinition1430(
    CharacterCreationNativeMagicSetting1430 Key, string Element, CharacterCreationSettingValueKind Kind);

public static class CharacterCreationNativeMagicDefinitions1430
{
    public static IReadOnlyList<CharacterCreationNativeMagicDefinition1430> All { get; } = Array.AsReadOnly(new CharacterCreationNativeMagicDefinition1430[]
    {
        new CharacterCreationNativeMagicDefinition1430(CharacterCreationNativeMagicSetting1430.SpiritForceBasedOnTotalMagic, "spiritforcebasedontotalmag", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.AllowInitiationInCreation, "allowinitiationincreatemode", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.IgnoreArt, "ignoreart", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.BoundSpiritExpression, "boundspiritexpression", CharacterCreationSettingValueKind.Expression),
        new(CharacterCreationNativeMagicSetting1430.RegisteredSpriteExpression, "registeredspriteexpression", CharacterCreationSettingValueKind.Expression),
        new(CharacterCreationNativeMagicSetting1430.IgnoreComplexFormLimit, "ignorecomplexformlimit", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.FreeSpiritPowerPointsMagic, "freespiritpowerpointsmag", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.IncreasedImprovedAbilityModifier, "increasedimprovedabilitymodifier", CharacterCreationSettingValueKind.Boolean),
        new(CharacterCreationNativeMagicSetting1430.DicePenaltySustaining, "dicepenaltysustaining", CharacterCreationSettingValueKind.Integer)
    });
}

public sealed record CharacterCreationNativeMagicPolicy1430(
    bool? SpiritForceBasedOnTotalMagic, bool? AllowInitiationInCreation, bool? IgnoreArt,
    string? BoundSpiritExpression, string? RegisteredSpriteExpression, bool? IgnoreComplexFormLimit,
    bool? FreeSpiritPowerPointsMagic, bool? IncreasedImprovedAbilityModifier, int? DicePenaltySustaining,
    string SettingsProfileId, string ProfileInputsDigest, string ProfileXmlDigest)
{
    public object? Value(string field) => field switch
    {
        "spiritforcebasedontotalmag" => SpiritForceBasedOnTotalMagic,
        "allowinitiationincreatemode" => AllowInitiationInCreation,
        "ignoreart" => IgnoreArt,
        "boundspiritexpression" => BoundSpiritExpression,
        "registeredspriteexpression" => RegisteredSpriteExpression,
        "ignorecomplexformlimit" => IgnoreComplexFormLimit,
        "freespiritpowerpointsmag" => FreeSpiritPowerPointsMagic,
        "increasedimprovedabilitymodifier" => IncreasedImprovedAbilityModifier,
        "dicepenaltysustaining" => DicePenaltySustaining,
        _ => null
    };
}

public enum CharacterCreationNativeMagicApplicability1430
{
    SettingAbsent, NoArtRequirementOperation, NoBoostedSkillPower, NotPlayerFreeSpirit, PlayerFreeSpirit
}

// Results describe this compiled graph and retained future controls. They are data,
// not evidence of private source execution, a completed selection prompt, or FullEditing.
public sealed record CharacterCreationNativeMagicProjection1430(
    CharacterCreationNativeMagicPolicy1430 Policy, bool Created, string GraphDigest,
    int? MaximumSpiritForce, bool? AddInitiationsAllowed, int? BoundSpiritLimit, int? RegisteredSpriteLimit,
    CharacterCreationNativeMagicApplicability1430 ArtApplicability,
    CharacterCreationNativeMagicApplicability1430 ImprovedAbilityApplicability,
    CharacterCreationNativeMagicApplicability1430 FreeSpiritApplicability,
    int? FreeSpiritPowerPoints, decimal? FreeSpiritPowerPointsRemaining,
    decimal? ComplexFormLimit, int? ComplexFormCount, bool? ComplexFormLimitAllowsNextAddition,
    int? SustainedPenaltyObjects, int? SustainingPenalty);
