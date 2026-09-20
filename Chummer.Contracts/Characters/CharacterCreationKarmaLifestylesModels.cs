namespace Chummer.Contracts.Characters;

/// <summary>
/// Source-priced pending lifestyles sharing the resource/gear nuyen pool. This
/// quote does not purchase a lifestyle, roll starting cash or finalize a runner.
/// The starting lifestyle is an explicit owned selection, never an arbitrary
/// catalog row offered as free starting money.
/// </summary>
public sealed record CharacterCreationKarmaLifestylesQuote(
    string Schema,
    CharacterCreationLifestylesAuthority Authority,
    string ResourcesQuoteDigest,
    string GearQuoteDigest,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lines,
    Guid? StartingLifestyleId,
    decimal LifestyleNuyenUsed,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_lifestyles_quote.v1";
    public bool CanSelect => Blockers is { Count: 0 } && Budget is { IsExact: true, Remaining: >= 0 };
}
