namespace Chummer.Contracts.Characters;

/// <summary>Source-bound pending talent selection. Does not grant Priority ratings or apply effects.</summary>
public sealed record CharacterCreationKarmaTalentOption(
    string OptionId,
    string Name,
    int KarmaCost,
    string? EnabledAttribute,
    string SourceNodeXml,
    string SourceNodeDigest,
    bool IsEnabled,
    IReadOnlyList<string> Blockers,
    IReadOnlyList<string> SourceAnchorIds);

public sealed record CharacterCreationKarmaTalentCatalog(
    string Schema,
    string SettingsProfileId,
    string RawProfileInputsDigest,
    string SourceInputsDigest,
    int KarmaQuality,
    IReadOnlyList<CharacterCreationKarmaTalentOption> Options,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_karma_talent_catalog.v1";
    public const string MundaneOptionId = "mundane";
    public const string UnsupportedSource = "creation-karma-talent-source-unsupported";
    public const string SourceDisabled = "creation-karma-talent-source-disabled";
}
