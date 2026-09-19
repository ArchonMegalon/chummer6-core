namespace Chummer.Contracts.Characters;

/// <summary>
/// Profile-owned SR5 attribute spending policy, independent of Priority ranks.
/// Does not imply supported qualities, enabled special attributes, or a draft.
/// </summary>
public sealed record CharacterCreationAttributePolicy(
    string Schema,
    string SettingsProfileId,
    string BuildMethod,
    int KarmaAttribute,
    int MaxNumberMaxAttributesCreate,
    bool AlternateMetatypeAttributeKarma,
    bool ReverseAttributePriorityOrder,
    string RawProfileInputsDigest,
    IReadOnlyList<string> SourceAnchorIds,
    string AuthorityDigest)
{
    public const string SchemaV1 = "chummer.character_creation_attribute_policy.v1";
}
