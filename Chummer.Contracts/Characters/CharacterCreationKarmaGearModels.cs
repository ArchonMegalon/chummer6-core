namespace Chummer.Contracts.Characters;

/// <summary>Source and limits of a Karma basket, without retaining the entire gear catalog.</summary>
public sealed record CharacterCreationKarmaGearBasis(
    string SettingsProfileId,
    string ProfileDigest,
    string SourceDigest,
    string RulesDigest,
    string RuntimeDigest,
    string AuthorityDigest,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine);

/// <summary>
/// Pending purchases against the exact Karma-funded resources quote. No Priority
/// resources draft, applied equipment, or final starting-cash claim is implied.
/// </summary>
public sealed record CharacterCreationKarmaGearQuote(
    string Schema,
    CharacterCreationKarmaGearBasis Basis,
    string ResourcesQuoteDigest,
    IReadOnlyList<CharacterCreationGearLine> Lines,
    CharacterCreationGearBudget Budget,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_gear_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
