namespace Chummer.Contracts.Characters;

/// <summary>Retained source inputs for replay validation, never a caller-issued write grant.</summary>
public sealed record CharacterCreationKarmaFinalizationAuthority(
    string Schema,
    string RawCharacterXml,
    CharacterCreationKarmaMetatypeQuote Foundation,
    CharacterCreationKarmaFinalizationBudgetQuote Finances,
    IReadOnlyList<CharacterCreationTalentQualitySource> RacialSources,
    CharacterCreationLifestylesAuthority Lifestyles,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_finalization_authority.v1";
}

/// <summary>The dice result is chosen once and bound to the explicitly reviewed plan.</summary>
public sealed record CharacterCreationKarmaFinalizationConfirmRequest(
    CharacterCreationFinalizationConfirmRequest Confirmation,
    int DiceTotal);
