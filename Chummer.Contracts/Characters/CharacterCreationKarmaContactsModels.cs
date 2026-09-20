namespace Chummer.Contracts.Characters;

/// <summary>Settings-owned free contact allowance and group-contact multiplier.
/// Neither field authorizes a free contact or invents a separate Karma pool.</summary>
public sealed record CharacterCreationKarmaContactsPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string ContactPointsExpression,
    int GroupContactKarmaMultiplier,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_contacts_policy.v1";
}

/// <summary>Complete pending contact, not a partial edit or already saved XML.</summary>
public sealed record CharacterCreationKarmaContactSelection(
    Guid ContactId,
    CharacterCreationContactIdentity Identity,
    int Connection,
    int Loyalty,
    bool IsGroup = false,
    bool Free = false,
    bool Family = false,
    bool Blackmail = false);

public sealed record CharacterCreationKarmaContactLine(
    CharacterCreationKarmaContactSelection Selection,
    int PointCost,
    bool UsesHighPlacesPool);

/// <summary>Read-only shared-budget contribution. Group costs include their
/// incremental positive-quality excess cost, avoiding a second quality pool.</summary>
public sealed record CharacterCreationKarmaContactsQuote(
    string Schema,
    CharacterCreationKarmaContactsPolicy Policy,
    string AttributesDigest,
    string QualitiesDigest,
    IReadOnlyList<CharacterCreationTalentQualitySource> RacialSources,
    CharacterCreationTalentQualitySource? TalentSource,
    IReadOnlyList<CharacterCreationKarmaContactLine> Lines,
    int ContactPoints,
    int ContactPointsUsed,
    int HighPlacesPoints,
    int HighPlacesPointsUsed,
    int GroupContactKarma,
    CharacterCreationQualityCostTotals CombinedQualityCosts,
    int KarmaUsed,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_contacts_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
