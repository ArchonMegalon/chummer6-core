namespace Chummer.Contracts.Characters;

/// <summary>Pending lifestyles priced after gear purchases, using the same
/// creation funds. Starting cash is neither rolled nor made spendable here.</summary>
public sealed record CharacterCreationLifeModuleLifestylesQuote(
    string SourceAuthorityDigest,
    CharacterCreationLifestylesAuthority ProjectionAuthority,
    string ResourcesQuoteDigest,
    string GearQuoteDigest,
    IReadOnlyList<CharacterCreationLifestyleConfiguration> Selection,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lines,
    Guid? StartingLifestyleId,
    decimal LifestyleNuyenUsed,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SelectionRequired = "creation-life-module-lifestyles-selection-required";
    public const string StartingLifestyleRequired = "creation-life-module-starting-lifestyle-required";
}
