namespace Chummer.Contracts.Characters;

public sealed record UpdateCharacterMetadataCommand(
    CharacterDocument Document,
    CharacterMetadataUpdate Update);

public sealed record UpdateCharacterMetadataResult(
    CharacterDocument UpdatedDocument,
    CharacterFileSummary Summary);
