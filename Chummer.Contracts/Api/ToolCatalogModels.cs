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
    string ReferenceLaneReceipt = "",
    int SourcebooksWithSnippets = 0,
    int SourcebooksMissingSnippets = 0,
    int ReferenceCoveragePercent = 0,
    string ReferenceSourceLanePosture = "missing",
    int SourcebooksWithGovernedReferenceSources = 0,
    int SourcebooksWithStaleReferenceSources = 0,
    int SourcebooksMissingReferenceSources = 0,
    string ReferenceSourceLaneReceipt = "",
    string SettingsLanePosture = "missing",
    int SettingsProfileCount = 0,
    int SettingsProfilesWithSourceToggles = 0,
    int DistinctSourcebookToggles = 0,
    string SourceToggleLanePosture = "missing",
    string SourceToggleLaneReceipt = "",
    string SourceSelectionLaneReceipt = "",
    string SettingsLaneReceipt = "",
    int SourcebookToggleCoveragePercent = 0,
    string CustomDataLanePosture = "missing",
    string CustomDataLaneReceipt = "",
    string CustomDataAuthoringLaneReceipt = "",
    int SettingsProfilesWithCustomDataDirectories = 0,
    int DistinctCustomDataDirectoryCount = 0,
    string XmlBridgePosture = "missing",
    string XmlBridgeLaneReceipt = "",
    int EnabledDataOverlayCount = 0,
    string TranslatorLanePosture = "missing",
    string TranslatorLaneReceipt = "",
    string TranslatorBridgePosture = "missing",
    int TranslatorLanguageCount = 0,
    int EnabledLanguageOverlayCount = 0,
    string Sr6SupplementLanePosture = "missing",
    string Sr6DesignerToolsPosture = "missing",
    int Sr6DesignerFamiliesAvailable = 0,
    int Sr6DesignerFamiliesExpected = 0,
    string HouseRuleLanePosture = "missing",
    int HouseRuleOverlayCount = 0,
    string OnlineStorageLanePosture = "missing",
    string OnlineStorageReceiptPosture = "missing",
    string OnlineStorageLaneReceipt = "",
    int OnlineStorageReceiptsCovered = 0,
    int OnlineStorageReceiptsExpected = 2,
    int OnlineStorageCoveragePercent = 0,
    string ImportOracleLanePosture = "missing",
    string ImportOracleReceiptPosture = "missing",
    int LegacyChummer4FixtureCount = 0,
    int LegacyChummer5FixtureCount = 0,
    int HeroLabFixtureCount = 0,
    string AdjacentSr6OracleReceiptPosture = "missing",
    int AdjacentSr6OracleSourcesCovered = 0,
    int AdjacentSr6OracleSourcesExpected = 2,
    int ImportOracleSourcesCovered = 0,
    int ImportOracleSourcesExpected = 4,
    int ImportOracleCoveragePercent = 0,
    IReadOnlyList<string>? ImportOracleMissingSources = null,
    string ImportOracleLaneReceipt = "",
    string AdjacentSr6OracleLaneReceipt = "",
    string Sr6SuccessorLaneReceipt = "",
    CustomDataXmlBridgeDeterministicReceipt? CustomDataXmlBridgeDeterministicReceipt = null,
    TranslatorLaneDeterministicReceipt? TranslatorDeterministicReceipt = null,
    ImportOracleLaneDeterministicReceipt? ImportOracleDeterministicReceipt = null,
    Sr6SuccessorLaneDeterministicReceipt? Sr6SuccessorDeterministicReceipt = null);

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
    string ReferenceUrl = "",
    string ReferenceSnapshot = "",
    string ReferenceSnapshotPosture = "missing");

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

public sealed record CustomDataXmlBridgeDeterministicReceipt(
    string ParityFamilyId,
    string CustomDataLanePosture,
    string CustomDataLaneReceipt,
    string CustomDataAuthoringLaneReceipt,
    int SettingsProfilesWithCustomDataDirectories,
    int DistinctCustomDataDirectoryCount,
    string XmlBridgePosture,
    string XmlBridgeLaneReceipt,
    int EnabledDataOverlayCount);

public sealed record TranslatorLaneDeterministicReceipt(
    string ParityRouteId,
    string TranslatorLanePosture,
    string TranslatorLaneReceipt,
    string TranslatorBridgePosture,
    int TranslatorLanguageCount,
    int EnabledLanguageOverlayCount);

public sealed record ImportOracleLaneDeterministicReceipt(
    string ParityFamilyId,
    string ImportOracleLanePosture,
    string ImportOracleLaneReceipt,
    string ImportOracleReceiptPosture,
    int LegacyChummer4FixtureCount,
    int LegacyChummer5FixtureCount,
    int HeroLabFixtureCount,
    string AdjacentSr6OracleReceiptPosture,
    int AdjacentSr6OracleSourcesCovered,
    int AdjacentSr6OracleSourcesExpected,
    int ImportOracleSourcesCovered,
    int ImportOracleSourcesExpected,
    int ImportOracleCoveragePercent,
    IReadOnlyList<string> ImportOracleMissingSources,
    string AdjacentSr6OracleLaneReceipt);

public sealed record Sr6SuccessorLaneDeterministicReceipt(
    string ParityFamilyId,
    string Sr6SupplementLanePosture,
    string Sr6SuccessorLaneReceipt,
    string Sr6DesignerToolsPosture,
    int Sr6DesignerFamiliesAvailable,
    int Sr6DesignerFamiliesExpected,
    string HouseRuleLanePosture,
    int HouseRuleOverlayCount,
    string OnlineStorageLanePosture,
    string OnlineStorageReceiptPosture,
    int OnlineStorageReceiptsCovered,
    int OnlineStorageReceiptsExpected,
    int OnlineStorageCoveragePercent);
