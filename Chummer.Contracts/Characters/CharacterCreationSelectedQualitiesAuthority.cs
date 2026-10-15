namespace Chummer.Contracts.Characters;

/// <summary>
/// Bounded historical pricing evidence for two explicit selections, not a full
/// catalogue, a saved draft, or live source/write authority. The provenance
/// digests identify the admitted full source; they are not hashes of Options.
/// WitnessDigest independently binds this selected representation. The caller
/// must bind the two ID sets to the real original history and accepted proposal.
/// </summary>
public sealed record CharacterCreationSelectedQualitiesAuthority(
    string Schema,
    string ProvenanceAuthorityDigest,
    string RulesetId,
    string SettingsProfileId,
    string SourceDigest,
    string ProfileDigest,
    string GmPolicyDigest,
    string RuntimeDigest,
    int QualityKarmaLimit,
    bool MayExceedPositiveQualityLimit,
    bool MayExceedNegativeQualityLimit,
    int MetagenicLimit,
    CharacterCreationQualityCostPolicy? CostPolicy,
    IReadOnlyList<string> OriginalOptionIds,
    IReadOnlyList<string> ProposedOptionIds,
    IReadOnlyList<CharacterCreationQualityCatalogOption> Options,
    IReadOnlyList<CharacterCreationGrantedQuality> GrantedQualities,
    IReadOnlyList<string> SourceAnchorIds,
    string WitnessDigest)
{
    public const string SchemaV1 = "chummer.sr5.creation-selected-qualities-authority.v1";
}
