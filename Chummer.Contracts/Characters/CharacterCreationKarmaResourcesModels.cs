namespace Chummer.Contracts.Characters;

/// <summary>Profile-owned Karma funding, not final starting cash or purchase permission.
/// The schema distinguishes Karma creation from Life Modules.</summary>
public sealed record CharacterCreationKarmaResourcesPolicy(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string FundingExpression,
    decimal MaximumKarmaInvestment,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_resources_policy.v1";
    public const string LifeModulesSchemaV1 = "chummer.character_creation_life_module_resources_policy.v1";
}

/// <summary>
/// Pending Karma-funded cash only. Qualities, other cash grants, purchases and
/// carryover must be composed by their owners before finalizing the runner.
/// </summary>
public sealed record CharacterCreationKarmaResourcesQuote(
    string Schema,
    CharacterCreationKarmaResourcesPolicy Policy,
    string AttributesDigest,
    decimal KarmaAvailable,
    decimal KarmaInvestment,
    decimal NuyenFromKarma,
    IReadOnlyList<string> Blockers,
    string QuoteDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_resources_quote.v1";
    public bool CanSelect => Blockers.Count == 0;
}
