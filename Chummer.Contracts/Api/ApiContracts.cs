namespace Chummer.Contracts.Api;

public sealed record CharacterXmlRequest(string Xml);

public sealed record DiceRollRequest(string? Expression);

public sealed record RosterEntry(string Name, string Alias, string Metatype, string LastOpenedUtc);

public sealed record CharacterMetadataRequest(
    string Xml,
    string? Name,
    string? Alias,
    string? Notes);
