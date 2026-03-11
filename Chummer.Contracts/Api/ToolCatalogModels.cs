namespace Chummer.Contracts.Api;

public sealed record MasterIndexFileEntry(
    string File,
    string Root,
    int ElementCount);

public sealed record MasterIndexResponse(
    int Count,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<MasterIndexFileEntry> Files);

public sealed record TranslatorLanguageEntry(
    string Code,
    string Name);

public sealed record TranslatorLanguagesResponse(
    int Count,
    IReadOnlyList<TranslatorLanguageEntry> Languages);
