namespace Chummer.Contracts.Characters;

/// <summary>Commitment to a separately retained catalogue and filter inputs.
/// This is input evidence; it grants neither rule coverage nor mutation authority.</summary>
public sealed record CharacterCreationSourcebookSelectionAuthority(
    string Schema, string CatalogueInputsDigest, string CatalogueXmlDigest, string FilterInputsDigest)
{
    public const string SchemaV1 = "chummer.creation-sourcebook-authority.v1";
    public const string Unavailable = "creation-sourcebooks-authority-unavailable";
    public const string Invalid = "creation-sourcebooks-authority-invalid";

    public bool IsValid() => Schema == SchemaV1
        && CharacterCreationFinalizationDigest.IsCanonical(CatalogueInputsDigest)
        && CharacterCreationFinalizationDigest.IsCanonical(CatalogueXmlDigest)
        && CharacterCreationFinalizationDigest.IsCanonical(FilterInputsDigest);
}
