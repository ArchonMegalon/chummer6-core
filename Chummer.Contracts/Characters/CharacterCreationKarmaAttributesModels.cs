namespace Chummer.Contracts.Characters;

/// <summary>Karma-only purchase. No Priority points or implicit free grants.</summary>
public sealed record CharacterCreationKarmaAttributeAllocation(string AttributeId, int KarmaLevels);

public sealed record CharacterCreationKarmaAttributesQuote(
    string Schema,
    CharacterCreationAttributePolicy Policy,
    IReadOnlyList<CharacterCreationKarmaAttributeAllocation> Allocations,
    IReadOnlyList<CharacterCreationAttributeProjection> Attributes,
    decimal KarmaUsed,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_attributes_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
