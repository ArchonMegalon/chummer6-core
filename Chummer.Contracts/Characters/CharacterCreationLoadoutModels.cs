namespace Chummer.Contracts.Characters;

/// <summary>Distinct source and saved-instance kinds; never aliases for generic Gear.</summary>
public static class CharacterCreationLoadoutKinds
{
    public const string Weapon = "weapon";
    public const string Armor = "armor";
}

public static class CharacterCreationLoadoutSchemas
{
    public const string AuthorityV1 = "chummer.sr5.creation-loadout.authority.v1";
    public const string RulesV1 = "chummer.sr5.creation-loadout.rules.v1";
    public const string RuntimeV1 = "chummer.sr5.creation-loadout.runtime.v1";
}

public static class CharacterCreationLoadoutBlockers
{
    public const string AuthorityUnavailable = "creation-loadout-authority-unavailable";
    public const string SourceDisabled = "creation-loadout-source-disabled";
    public const string AvailabilityExceeded = "creation-loadout-availability-exceeded";
    public const string UnsupportedSemantics = "creation-loadout-unsupported-semantics";
    public const string InvalidBasket = "creation-loadout-invalid-basket";
    public const string InvalidOption = "creation-loadout-invalid-option";
    public const string InvalidQuantity = "creation-loadout-invalid-quantity";
    public const string DuplicateOption = "creation-loadout-duplicate-option";
}

public sealed record CharacterCreationWeaponSelection(string OptionId, int Quantity = 1);
public sealed record CharacterCreationArmorSelection(string OptionId, int Quantity = 1);

public sealed record CharacterCreationLoadoutSelections(
    IReadOnlyList<CharacterCreationWeaponSelection> Weapons,
    IReadOnlyList<CharacterCreationArmorSelection> Armor)
{
    public static CharacterCreationLoadoutSelections Empty { get; } = new([], []);
}

public sealed record CharacterCreationLoadoutCatalogOption(
    string OptionId,
    string Kind,
    Guid SourceId,
    string Name,
    string Category,
    decimal UnitCost,
    int Availability,
    string Legality,
    string SourceBook,
    string Page,
    bool IsSelectable,
    bool PricingIsExact,
    bool AvailabilityIsExact,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceNodeXml,
    string SourceNodeDigest,
    string SourceCategoryXml,
    string SourceCategoryDigest,
    string OptionDigest);

public sealed record CharacterCreationLoadoutAuthority(
    string Schema,
    string RulesetId,
    string SettingsProfileId,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine,
    IReadOnlyList<CharacterCreationLoadoutCatalogOption> Options,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    string AuthorityDigest)
{
    public static CharacterCreationLoadoutAuthority Unavailable { get; } = new(
        CharacterCreationLoadoutSchemas.AuthorityV1, string.Empty, string.Empty, 0, 0, 0,
        [], [], [CharacterCreationLoadoutBlockers.AuthorityUnavailable], false,
        string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
}

public sealed record CharacterCreationLoadoutLine(
    string OptionId,
    string Kind,
    Guid SourceId,
    string Name,
    string Category,
    int Quantity,
    decimal UnitCost,
    decimal TotalCost,
    int Availability,
    string Legality,
    string SourceBook,
    string Page,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceNodeXml,
    string SourceNodeDigest,
    string SourceCategoryXml,
    string SourceCategoryDigest,
    string LineDigest);

/// <summary>Historical selected closure; never substitutes for live source admission.</summary>
public sealed record CharacterCreationSelectedLoadoutAuthority(
    string Schema,
    string AuthorityDigest,
    string RulesetId,
    string SettingsProfileId,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine,
    CharacterCreationLoadoutSelections OriginalSelections,
    CharacterCreationLoadoutSelections ProposedSelections,
    IReadOnlyList<CharacterCreationLoadoutCatalogOption> Options,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    string WitnessDigest)
{
    public const string SchemaV1 = "chummer.sr5.creation-loadout.selected-authority.v1";
}
