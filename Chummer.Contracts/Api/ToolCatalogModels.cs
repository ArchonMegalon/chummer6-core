namespace Chummer.Contracts.Api;

public sealed record MasterIndexFileEntry(
    string File,
    string Root,
    int ElementCount);

public sealed record MasterIndexResponse(
    int Count,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<MasterIndexFileEntry> Files,
    string ReferenceLanePosture,
    int SourcebookCount,
    IReadOnlyList<MasterIndexSourcebookEntry> Sourcebooks,
    int SourcebooksWithSnippets = 0,
    int SourcebooksMissingSnippets = 0,
    int ReferenceCoveragePercent = 0,
    string SettingsLanePosture = "missing",
    int SettingsProfileCount = 0,
    int SettingsProfilesWithSourceToggles = 0,
    int DistinctSourcebookToggles = 0,
    string SourceToggleLanePosture = "missing",
    int SourcebookToggleCoveragePercent = 0,
    string CustomDataLanePosture = "missing",
    int SettingsProfilesWithCustomDataDirectories = 0,
    int DistinctCustomDataDirectoryCount = 0,
    string XmlBridgePosture = "missing",
    int EnabledDataOverlayCount = 0,
    string Sr6SupplementLanePosture = "missing",
    string Sr6DesignerToolsPosture = "missing",
    int Sr6DesignerFamiliesAvailable = 0,
    int Sr6DesignerFamiliesExpected = 0,
    string HouseRuleLanePosture = "missing",
    int HouseRuleOverlayCount = 0);

public sealed record MasterIndexSourcebookEntry(
    string Id,
    string Code,
    string Name,
    bool Permanent,
    string ReferencePosture,
    int RuleSnippetCount,
    IReadOnlyList<MasterIndexRuleSnippetEntry> RuleSnippets,
    string ReferenceSourcePosture = "missing",
    string LocalPdfPath = "",
    string ReferenceUrl = "");

public sealed record MasterIndexRuleSnippetEntry(
    string Language,
    int Page,
    string Snippet,
    string Provenance);

public sealed record TranslatorLanguageEntry(
    string Code,
    string Name);

public sealed record TranslatorLanguagesResponse(
    int Count,
    IReadOnlyList<TranslatorLanguageEntry> Languages,
    string TranslatorBridgePosture = "missing",
    int EnabledLanguageOverlayCount = 0);
