namespace Chummer.Contracts.Characters;

/// <summary>
/// Identity projected by Chummer5 when a Contact or Pet is associated with another runner file.
/// </summary>
public sealed record CharacterLinkedDocument(
    string CharacterName,
    string Name,
    string Alias,
    string Metatype,
    string Metavariant,
    string Gender,
    string Age)
{
    public string DisplayMetatype => string.IsNullOrWhiteSpace(Metavariant)
        ? Metatype
        : $"{Metatype} ({Metavariant})";
}

public sealed record CharacterLinkedAssociationSummary(
    bool IsLinked,
    bool IdentityResolved,
    string FileName,
    string RelativeFileName,
    string DisplayName);
