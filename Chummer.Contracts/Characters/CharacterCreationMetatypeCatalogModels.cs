namespace Chummer.Contracts.Characters;

public static class CharacterCreationMetatypeCatalogSchemas
{
    public const string CatalogV1 = "chummer.character_creation_metatype_catalog.v1";
}

public static class CharacterCreationMetatypeCatalogBlockers
{
    public const string AuthorityUnavailable = "metatype-catalog-authority-unavailable";
    public const string BaseEntryDuplicate = "metatype-base-entry-duplicate";
    public const string BaseEntryInvalid = "metatype-base-entry-invalid";
    public const string BaseEntryMissing = "metatype-base-entry-missing";
    public const string CustomDataDrift = "metatype-custom-data-drift";
    public const string CustomDataUnsupported = "metatype-custom-data-unsupported";
    public const string MetavariantUnsupported = "metatype-metavariant-unsupported";
    public const string MetatypesSourceDrift = "metatype-source-inputs-drift";
    public const string OverlayUnsupported = "metatype-overlay-unsupported";
    public const string ProfileBuildMethodUnsupported = "metatype-profile-build-method-unsupported";
    public const string ProfileDroneModsInvalid = "metatype-profile-drone-mods-invalid";
    public const string ProfileInitiativeFallbackInvalid = "metatype-profile-initiative-fallback-invalid";
    public const string ProfileKarmaModeInvalid = "metatype-profile-karma-mode-invalid";
    public const string ProfileKarmaMultiplierInvalid = "metatype-profile-karma-multiplier-invalid";
    public const string ProfileSettingsDrift = "metatype-profile-settings-drift";
    public const string ProfileUnsupported = "metatype-profile-unsupported";
    public const string SelectorSemanticsUnsupported = "metatype-selector-semantics-unsupported";
    public const string SourceDisabled = "metatype-source-disabled";
    public const string SpecialSemanticsUnsupported = "metatype-special-semantics-unsupported";
    public const string UnknownSemantics = "metatype-unknown-semantics";
}

public static class CharacterCreationMetatypeQualityPolarities
{
    public const string Positive = "positive";
    public const string Negative = "negative";
}

public sealed record CharacterCreationMetatypeAttributeProjection(
    string AttributeId,
    int Minimum,
    int Maximum,
    int AugmentedMaximum);

public sealed record CharacterCreationMetatypeInitiativeProjection(
    int Minimum,
    int Maximum,
    int AugmentedMaximum,
    int MinimumDiceFallback);

public sealed record CharacterCreationMetatypeMovementRate(
    decimal Ground,
    decimal Swim,
    decimal Fly);

public sealed record CharacterCreationMetatypeMovementProjection(
    CharacterCreationMetatypeMovementRate Walk,
    CharacterCreationMetatypeMovementRate Run,
    CharacterCreationMetatypeMovementRate Sprint)
{
    public bool IsSpecial { get; init; }

    public static CharacterCreationMetatypeMovementProjection Unavailable { get; } = new(
        new CharacterCreationMetatypeMovementRate(0m, 0m, 0m),
        new CharacterCreationMetatypeMovementRate(0m, 0m, 0m),
        new CharacterCreationMetatypeMovementRate(0m, 0m, 0m));

    public static CharacterCreationMetatypeMovementProjection Special { get; } = Unavailable with
    {
        IsSpecial = true
    };
}

public sealed record CharacterCreationMetatypeGrantedQualityProjection(
    string Name,
    string Polarity,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationMetatypeExcludedChoice(
    string OptionId,
    string Label,
    string SourceBook,
    int? SourcePage,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationMetatypeOptionProjection(
    string OptionId,
    string Label,
    string Category,
    string SourceBook,
    int SourcePage,
    int BaseKarma,
    int KarmaCost,
    IReadOnlyList<CharacterCreationMetatypeAttributeProjection> Attributes,
    CharacterCreationMetatypeInitiativeProjection Initiative,
    CharacterCreationMetatypeMovementProjection Movement,
    IReadOnlyList<CharacterCreationMetatypeGrantedQualityProjection> GrantedQualities,
    IReadOnlyList<CharacterCreationMetatypeExcludedChoice> ExcludedMetavariants,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationMetatypeSourceContextAuthority(
    string SettingsProfileId,
    string RawMetatypesXmlDigest,
    string EffectiveMetatypesInputsDigest,
    string RawProfileInputsDigest,
    string SelectedCustomDataInputsDigest,
    string AuthorityDigest,
    int? MetatypeKarmaMultiplier,
    int? MinimumInitiativeDiceFallback,
    bool? DroneMods,
    IReadOnlyList<string> EnabledSourcebooks,
    IReadOnlyList<string> SourceAnchorIds,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative)
{
    public static CharacterCreationMetatypeSourceContextAuthority Unavailable { get; } = new(
        SettingsProfileId: string.Empty,
        RawMetatypesXmlDigest: string.Empty,
        EffectiveMetatypesInputsDigest: string.Empty,
        RawProfileInputsDigest: string.Empty,
        SelectedCustomDataInputsDigest: string.Empty,
        AuthorityDigest: string.Empty,
        MetatypeKarmaMultiplier: null,
        MinimumInitiativeDiceFallback: null,
        DroneMods: null,
        EnabledSourcebooks: Array.Empty<string>(),
        SourceAnchorIds: Array.Empty<string>(),
        Blockers: [CharacterCreationMetatypeCatalogBlockers.AuthorityUnavailable],
        IsAuthoritative: false);
}

public sealed record CharacterCreationMetatypeCatalogAuthority(
    string Schema,
    CharacterCreationMetatypeSourceContextAuthority SourceContext,
    IReadOnlyList<CharacterCreationMetatypeOptionProjection> Options,
    IReadOnlyList<string> Blockers,
    bool IsAuthoritative)
{
    public static CharacterCreationMetatypeCatalogAuthority Unavailable { get; } = new(
        Schema: CharacterCreationMetatypeCatalogSchemas.CatalogV1,
        SourceContext: CharacterCreationMetatypeSourceContextAuthority.Unavailable,
        Options: Array.Empty<CharacterCreationMetatypeOptionProjection>(),
        Blockers: [CharacterCreationMetatypeCatalogBlockers.AuthorityUnavailable],
        IsAuthoritative: false);
}
