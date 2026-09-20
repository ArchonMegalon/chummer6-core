using System.Text.Json.Serialization;

namespace Chummer.Contracts.Characters;

public sealed record CharacterCreationKarmaLifestyleEffects(
    string AttributesQuoteDigest,
    string QualitiesQuoteDigest,
    IReadOnlyList<CharacterCreationTalentQualitySource> RacialSources,
    CharacterCreationTalentQualitySource? TalentSource);

/// <summary>
/// Source-priced pending lifestyles sharing the resource/gear nuyen pool. This
/// quote does not purchase a lifestyle, roll starting cash or finalize a runner.
/// The starting lifestyle is an explicit owned selection, never an arbitrary
/// catalog row offered as free starting money.
/// ProjectionAuthority omits unrelated quality rows; SourceAuthorityDigest
/// binds the complete catalog that must be re-admitted before persistence.
/// </summary>
public sealed record CharacterCreationKarmaLifestylesQuote(
    string Schema,
    string SourceAuthorityDigest,
    CharacterCreationLifestylesAuthority ProjectionAuthority,
    string ResourcesQuoteDigest,
    string GearQuoteDigest,
    IReadOnlyList<CharacterCreationLifestyleProjection> Lines,
    Guid? StartingLifestyleId,
    decimal LifestyleNuyenUsed,
    CharacterCreationLifestyleBudget Budget,
    IReadOnlyList<string> Blockers,
    string QuoteDigest,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CharacterCreationKarmaLifestyleEffects? Effects = null)
{
    public const string SchemaV1 = "chummer.character_creation_karma_lifestyles_quote.v1";
    public bool CanSelect => Blockers is { Count: 0 } && Budget is { IsExact: true, Remaining: >= 0 };
}
