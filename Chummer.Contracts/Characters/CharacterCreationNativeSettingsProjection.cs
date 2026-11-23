namespace Chummer.Contracts.Characters;

public enum CharacterLicenseSuggestionSource { GeneralLicenseCatalog, OwnedRestrictedItems }

/// <summary>Retained SR5 settings for creation/Career handoff, not an executed license
/// selection or weapon damage result. SelectItem's RESTRICTED prompt permits free
/// text in either mode; this option changes its suggestions, not ownership legality.
/// Weapon damage applies the optional +2 while evaluating a character's weapon.
/// No attack, damage total, license choice or FullEditing capability is asserted.</summary>
public sealed record CharacterCreationNativeSettingsProjection(
    bool? LicenseRestricted, bool? MoreLethalGameplay, bool Created,
    string SettingsProfileId, string ProfileInputsDigest, string ProfileXmlDigest, string GraphDigest)
{
    public CharacterLicenseSuggestionSource? LicenseSuggestions => LicenseRestricted is { } enabled
        ? enabled ? CharacterLicenseSuggestionSource.OwnedRestrictedItems
            : CharacterLicenseSuggestionSource.GeneralLicenseCatalog : null;
    public int? WeaponDamageAdjustmentWhenEvaluated => MoreLethalGameplay is { } enabled ? enabled ? 2 : 0 : null;
}
