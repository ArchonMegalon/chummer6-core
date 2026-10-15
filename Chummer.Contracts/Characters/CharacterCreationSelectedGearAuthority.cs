namespace Chummer.Contracts.Characters;

/// <summary>
/// Exact original/proposed Gear source closure for historical repricing, not a
/// complete catalog or current source/owner authority. ProvenanceAuthorityDigest
/// identifies the complete admitted catalog; WitnessDigest hashes THIS payload.
/// The caller must bind both selection sets to the actual original and proposal.
/// No ordinary Gear draft or receipt is manufactured from this value.
/// </summary>
public sealed record CharacterCreationSelectedGearAuthority(
    string Schema,
    string ProvenanceAuthorityDigest,
    string RulesetId,
    string SettingsProfileId,
    int MaximumAvailability,
    int MaximumBasketLines,
    int MaximumQuantityPerLine,
    IReadOnlyList<string> OriginalOptionIds,
    IReadOnlyList<string> ProposedOptionIds,
    IReadOnlyList<CharacterCreationGearCatalogOption> Options,
    IReadOnlyList<string> SourceAnchorIds,
    string SourceDigest,
    string ProfileDigest,
    string RulesDigest,
    string RuntimeDigest,
    string WitnessDigest)
{
    public const string SchemaV1 = "chummer.creation-selected-gear-authority.v1";
}
