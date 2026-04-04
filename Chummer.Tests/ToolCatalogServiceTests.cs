using System;
using System.IO;
using System.Linq;
using Chummer.Contracts.Api;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class ToolCatalogServiceTests
{
    [TestMethod]
    public void Master_index_reads_xml_files_and_tolerates_invalid_documents()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "valid.xml"), "<chummer><item /><item /></chummer>");
            File.WriteAllText(Path.Combine(dataDir, "broken.xml"), "<chummer>");

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual(2, response.Count);
            Assert.HasCount(2, response.Files);
            Assert.IsTrue(response.Files.Any(file => file.File == "valid.xml" && file.Root == "chummer" && file.ElementCount >= 3));
            Assert.IsTrue(response.Files.Any(file => file.File == "broken.xml" && file.Root == string.Empty && file.ElementCount == 0));
            Assert.AreEqual("missing", response.ReferenceLanePosture);
            Assert.AreEqual(0, response.SourcebookCount);
            Assert.HasCount(0, response.Sourcebooks);
            Assert.AreEqual(0, response.SourcebooksWithSnippets);
            Assert.AreEqual(0, response.SourcebooksMissingSnippets);
            Assert.AreEqual(0, response.ReferenceCoveragePercent);
            Assert.AreEqual("missing", response.ReferenceSourceLanePosture);
            Assert.AreEqual(0, response.SourcebooksWithGovernedReferenceSources);
            Assert.AreEqual(0, response.SourcebooksWithStaleReferenceSources);
            Assert.AreEqual(0, response.SourcebooksMissingReferenceSources);
            Assert.AreEqual("missing", response.SettingsLanePosture);
            Assert.AreEqual(0, response.SettingsProfileCount);
            Assert.AreEqual(0, response.SettingsProfilesWithSourceToggles);
            Assert.AreEqual(0, response.DistinctSourcebookToggles);
            Assert.AreEqual("missing", response.SourceToggleLanePosture);
            Assert.AreEqual(0, response.SourcebookToggleCoveragePercent);
            Assert.AreEqual("missing", response.CustomDataLanePosture);
            Assert.AreEqual(0, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(0, response.DistinctCustomDataDirectoryCount);
            Assert.AreEqual("missing", response.XmlBridgePosture);
            Assert.AreEqual(0, response.EnabledDataOverlayCount);
            Assert.AreEqual("missing", response.Sr6SupplementLanePosture);
            Assert.AreEqual("missing", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(0, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
            Assert.AreEqual("missing", response.HouseRuleLanePosture);
            Assert.AreEqual(0, response.HouseRuleOverlayCount);
            Assert.AreEqual("missing", response.OnlineStorageLanePosture);
            Assert.AreEqual("missing", response.OnlineStorageReceiptPosture);
            Assert.AreEqual(0, response.OnlineStorageReceiptsCovered);
            Assert.AreEqual(2, response.OnlineStorageReceiptsExpected);
            Assert.AreEqual(0, response.OnlineStorageCoveragePercent);
            Assert.AreEqual("missing", response.ImportOracleLanePosture);
            Assert.AreEqual("missing", response.ImportOracleReceiptPosture);
            Assert.AreEqual(0, response.LegacyChummer4FixtureCount);
            Assert.AreEqual(0, response.LegacyChummer5FixtureCount);
            Assert.AreEqual(0, response.HeroLabFixtureCount);
            Assert.AreEqual(0, response.ImportOracleSourcesCovered);
            Assert.AreEqual(4, response.ImportOracleSourcesExpected);
            Assert.AreEqual(0, response.ImportOracleCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_merge_catalog_fragment_merges_into_canonical_file()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "qualities.xml"),
                "<chummer><qualities><quality><id>base</id><name>Base</name></quality></qualities></chummer>");

            string amendsRoot = Path.Combine(root, "Amends");
            string overlayData = Path.Combine(amendsRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(
                Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"merge-pack\",\n  \"priority\": 100,\n  \"enabled\": true,\n  \"mode\": \"merge-catalog\"\n}");
            File.WriteAllText(
                Path.Combine(overlayData, "qualities.test-amend.xml"),
                "<chummer><qualities><quality><id>addon</id><name>Addon</name></quality></qualities></chummer>");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual(1, response.Count);
            Assert.HasCount(1, response.Files);
            Assert.AreEqual("qualities.xml", response.Files[0].File);
            Assert.AreEqual("chummer", response.Files[0].Root);
            Assert.IsGreaterThanOrEqualTo(7, response.Files[0].ElementCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_merge_catalog_fragment_replaces_entry_with_matching_id_key()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "qualities.xml"),
                "<chummer><qualities><quality><id>base</id><name>Base</name></quality></qualities></chummer>");

            string amendsRoot = Path.Combine(root, "Amends");
            string overlayData = Path.Combine(amendsRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(
                Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"merge-pack\",\n  \"priority\": 100,\n  \"enabled\": true,\n  \"mode\": \"merge-catalog\"\n}");
            File.WriteAllText(
                Path.Combine(overlayData, "qualities.test-amend.xml"),
                "<chummer><qualities><quality><id>base</id><name>Base Overlay</name></quality><quality><id>addon</id><name>Addon</name></quality></qualities></chummer>");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            MasterIndexResponse response = service.GetMasterIndex();

            MasterIndexFileEntry qualities = response.Files.Single(file => file.File == "qualities.xml");
            Assert.AreEqual(1, response.Count);
            Assert.HasCount(1, response.Files);
            Assert.AreEqual("chummer", qualities.Root);
            Assert.AreEqual(8, qualities.ElementCount, "Merge-catalog should replace the matching id entry instead of appending duplicates.");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_projects_sourcebook_metadata_and_rule_snippets_from_books_catalog()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-sr5</id>
                      <name>Shadowrun 5th Edition</name>
                      <code>SR5</code>
                      <pdf>Shadowrun5-Core.pdf</pdf>
                      <url>https://example.test/sourcebooks/shadowrun5</url>
                      <permanent />
                      <matches>
                        <match>
                          <language>en-us</language>
                          <text>Welcome to Shadowrun, Fifth Edition.</text>
                          <page>8</page>
                        </match>
                      </matches>
                    </book>
                    <book>
                      <id>book-rf</id>
                      <name>Run Faster</name>
                      <code>RF</code>
                    </book>
                  </books>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("stale", response.ReferenceLanePosture);
            Assert.AreEqual(2, response.SourcebookCount);
            Assert.HasCount(2, response.Sourcebooks);
            Assert.AreEqual(1, response.SourcebooksWithSnippets);
            Assert.AreEqual(1, response.SourcebooksMissingSnippets);
            Assert.AreEqual(50, response.ReferenceCoveragePercent);
            Assert.AreEqual("stale", response.ReferenceSourceLanePosture);
            Assert.AreEqual(1, response.SourcebooksWithGovernedReferenceSources);
            Assert.AreEqual(0, response.SourcebooksWithStaleReferenceSources);
            Assert.AreEqual(1, response.SourcebooksMissingReferenceSources);

            MasterIndexSourcebookEntry rf = response.Sourcebooks.Single(sourcebook => sourcebook.Code == "RF");
            Assert.AreEqual("book-rf", rf.Id);
            Assert.AreEqual("Run Faster", rf.Name);
            Assert.IsFalse(rf.Permanent);
            Assert.AreEqual("no-snippets", rf.ReferencePosture);
            Assert.AreEqual(0, rf.RuleSnippetCount);
            Assert.AreEqual("missing", rf.ReferenceSourcePosture);
            Assert.AreEqual(string.Empty, rf.LocalPdfPath);
            Assert.AreEqual(string.Empty, rf.ReferenceUrl);

            MasterIndexSourcebookEntry sr5 = response.Sourcebooks.Single(sourcebook => sourcebook.Code == "SR5");
            Assert.AreEqual("book-sr5", sr5.Id);
            Assert.AreEqual("Shadowrun 5th Edition", sr5.Name);
            Assert.IsTrue(sr5.Permanent);
            Assert.AreEqual("matched-snippets", sr5.ReferencePosture);
            Assert.AreEqual(1, sr5.RuleSnippetCount);
            Assert.AreEqual("en-us", sr5.RuleSnippets[0].Language);
            Assert.AreEqual(8, sr5.RuleSnippets[0].Page);
            Assert.AreEqual("Welcome to Shadowrun, Fifth Edition.", sr5.RuleSnippets[0].Snippet);
            Assert.AreEqual("books.xml", sr5.RuleSnippets[0].Provenance);
            Assert.AreEqual("governed", sr5.ReferenceSourcePosture);
            Assert.AreEqual("Shadowrun5-Core.pdf", sr5.LocalPdfPath);
            Assert.AreEqual("https://example.test/sourcebooks/shadowrun5", sr5.ReferenceUrl);
            Assert.AreEqual("missing", response.SettingsLanePosture);
            Assert.AreEqual(0, response.SettingsProfileCount);
            Assert.AreEqual(0, response.SettingsProfilesWithSourceToggles);
            Assert.AreEqual(0, response.DistinctSourcebookToggles);
            Assert.AreEqual("missing", response.SourceToggleLanePosture);
            Assert.AreEqual(0, response.SourcebookToggleCoveragePercent);
            Assert.AreEqual("missing", response.CustomDataLanePosture);
            Assert.AreEqual(0, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(0, response.DistinctCustomDataDirectoryCount);
            Assert.AreEqual("missing", response.XmlBridgePosture);
            Assert.AreEqual(0, response.EnabledDataOverlayCount);
            Assert.AreEqual("stale", response.Sr6SupplementLanePosture);
            Assert.AreEqual("missing", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(0, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
            Assert.AreEqual("missing", response.HouseRuleLanePosture);
            Assert.AreEqual(0, response.HouseRuleOverlayCount);
            Assert.AreEqual("missing", response.OnlineStorageLanePosture);
            Assert.AreEqual("missing", response.OnlineStorageReceiptPosture);
            Assert.AreEqual(0, response.OnlineStorageReceiptsCovered);
            Assert.AreEqual(2, response.OnlineStorageReceiptsExpected);
            Assert.AreEqual(0, response.OnlineStorageCoveragePercent);
            Assert.AreEqual("missing", response.ImportOracleLanePosture);
            Assert.AreEqual("missing", response.ImportOracleReceiptPosture);
            Assert.AreEqual(0, response.LegacyChummer4FixtureCount);
            Assert.AreEqual(0, response.LegacyChummer5FixtureCount);
            Assert.AreEqual(0, response.HeroLabFixtureCount);
            Assert.AreEqual(0, response.ImportOracleSourcesCovered);
            Assert.AreEqual(4, response.ImportOracleSourcesExpected);
            Assert.AreEqual(0, response.ImportOracleCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Translator_languages_reads_name_when_present_and_falls_back_to_code()
    {
        string root = CreateTempDirectory();
        try
        {
            string langDir = Path.Combine(root, "lang");
            Directory.CreateDirectory(langDir);
            File.WriteAllText(Path.Combine(langDir, "en-us.xml"), "<chummer><name>English</name></chummer>");
            File.WriteAllText(Path.Combine(langDir, "fr-fr.xml"), "<chummer><metadata /></chummer>");

            var service = new XmlToolCatalogService(root);
            TranslatorLanguagesResponse response = service.GetTranslatorLanguages();

            Assert.AreEqual(2, response.Count);
            Assert.HasCount(2, response.Languages);
            Assert.IsTrue(response.Languages.Any(language => language.Code == "en-us" && language.Name == "English"));
            Assert.IsTrue(response.Languages.Any(language => language.Code == "fr-fr" && language.Name == "fr-fr"));
            Assert.AreEqual("missing", response.TranslatorBridgePosture);
            Assert.AreEqual(0, response.EnabledLanguageOverlayCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Translator_languages_merge_catalog_fragment_uses_canonical_language_code()
    {
        string root = CreateTempDirectory();
        try
        {
            string amendsRoot = Path.Combine(root, "Amends");
            string overlayLang = Path.Combine(amendsRoot, "lang");
            Directory.CreateDirectory(overlayLang);
            File.WriteAllText(
                Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"merge-lang\",\n  \"priority\": 100,\n  \"enabled\": true,\n  \"mode\": \"merge-catalog\"\n}");
            File.WriteAllText(
                Path.Combine(overlayLang, "en-us.test-amend.xml"),
                "<chummer><name>English Overlay</name></chummer>");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            TranslatorLanguagesResponse response = service.GetTranslatorLanguages();

            Assert.AreEqual(1, response.Count);
            Assert.HasCount(1, response.Languages);
            Assert.AreEqual("en-us", response.Languages[0].Code);
            Assert.AreEqual("English Overlay", response.Languages[0].Name);
            Assert.AreEqual("governed", response.TranslatorBridgePosture);
            Assert.AreEqual(1, response.EnabledLanguageOverlayCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Translator_languages_ignores_fragment_overlay_file_when_canonical_language_exists()
    {
        string root = CreateTempDirectory();
        try
        {
            string baseLang = Path.Combine(root, "lang");
            Directory.CreateDirectory(baseLang);
            File.WriteAllText(Path.Combine(baseLang, "en-us.xml"), "<chummer><name>English</name></chummer>");

            string amendsRoot = Path.Combine(root, "Amends");
            string overlayLang = Path.Combine(amendsRoot, "lang");
            Directory.CreateDirectory(overlayLang);
            File.WriteAllText(Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"local-test-amend\",\n  \"name\": \"Local Test Amend\",\n  \"priority\": 100,\n  \"enabled\": true\n}");
            File.WriteAllText(Path.Combine(overlayLang, "en-us.test-amend.xml"), "<chummer><strings /></chummer>");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            TranslatorLanguagesResponse response = service.GetTranslatorLanguages();

            Assert.AreEqual(1, response.Count);
            Assert.HasCount(1, response.Languages);
            Assert.AreEqual("en-us", response.Languages[0].Code);
            Assert.AreEqual("English", response.Languages[0].Name);
            Assert.AreEqual("governed", response.TranslatorBridgePosture);
            Assert.AreEqual(1, response.EnabledLanguageOverlayCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_xml_bridge_when_enabled_data_overlay_exists()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "skills.xml"), "<chummer><skills /></chummer>");

            string amendsRoot = Path.Combine(root, "Amends");
            string overlayData = Path.Combine(amendsRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(Path.Combine(overlayData, "skills.xml"), "<chummer><skills><skill /></skills></chummer>");
            File.WriteAllText(Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"local-data-bridge\",\n  \"priority\": 100,\n  \"enabled\": true\n}");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.XmlBridgePosture);
            Assert.AreEqual(1, response.EnabledDataOverlayCount);
            Assert.AreEqual("missing", response.HouseRuleLanePosture);
            Assert.AreEqual(0, response.HouseRuleOverlayCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_reference_lane_when_all_sourcebooks_have_snippets()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-sr5</id>
                      <name>Shadowrun 5th Edition</name>
                      <code>SR5</code>
                      <matches>
                        <match>
                          <language>en-us</language>
                          <text>Core rule excerpt.</text>
                          <page>10</page>
                        </match>
                      </matches>
                    </book>
                    <book>
                      <id>book-rf</id>
                      <name>Run Faster</name>
                      <code>RF</code>
                      <matches>
                        <match>
                          <language>en-us</language>
                          <text>Expanded character options.</text>
                          <page>20</page>
                        </match>
                      </matches>
                    </book>
                  </books>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.ReferenceLanePosture);
            Assert.AreEqual(2, response.SourcebookCount);
            Assert.IsTrue(response.Sourcebooks.All(sourcebook => sourcebook.RuleSnippetCount > 0));
            Assert.AreEqual(2, response.SourcebooksWithSnippets);
            Assert.AreEqual(0, response.SourcebooksMissingSnippets);
            Assert.AreEqual(100, response.ReferenceCoveragePercent);
            Assert.AreEqual("governed", response.Sr6SupplementLanePosture);
            Assert.AreEqual("missing", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(0, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_reference_source_posture_for_invalid_pdf_or_url_targets()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-invalid-reference</id>
                      <name>Broken Reference Entry</name>
                      <code>BRE</code>
                      <pdf>BrokenReference.txt</pdf>
                      <url>ftp://example.test/broken-reference</url>
                      <matches>
                        <match>
                          <language>en-us</language>
                          <text>Reference snippet remains available.</text>
                          <page>12</page>
                        </match>
                      </matches>
                    </book>
                  </books>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual(1, response.Sourcebooks.Count);
            MasterIndexSourcebookEntry sourcebook = response.Sourcebooks[0];
            Assert.AreEqual("stale", sourcebook.ReferenceSourcePosture);
            Assert.AreEqual("BrokenReference.txt", sourcebook.LocalPdfPath);
            Assert.AreEqual("ftp://example.test/broken-reference", sourcebook.ReferenceUrl);
            Assert.AreEqual("matched-snippets", sourcebook.ReferencePosture);
            Assert.AreEqual(1, sourcebook.RuleSnippetCount);
            Assert.AreEqual("stale", response.ReferenceSourceLanePosture);
            Assert.AreEqual(0, response.SourcebooksWithGovernedReferenceSources);
            Assert.AreEqual(1, response.SourcebooksWithStaleReferenceSources);
            Assert.AreEqual(0, response.SourcebooksMissingReferenceSources);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_reference_source_lane_when_all_sourcebooks_have_valid_pdf_or_url_targets()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-sr5</id>
                      <name>Shadowrun 5th Edition</name>
                      <code>SR5</code>
                      <pdf>Shadowrun5-Core.pdf</pdf>
                    </book>
                    <book>
                      <id>book-rf</id>
                      <name>Run Faster</name>
                      <code>RF</code>
                      <url>https://example.test/sourcebooks/run-faster</url>
                    </book>
                  </books>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.ReferenceSourceLanePosture);
            Assert.AreEqual(2, response.SourcebooksWithGovernedReferenceSources);
            Assert.AreEqual(0, response.SourcebooksWithStaleReferenceSources);
            Assert.AreEqual(0, response.SourcebooksMissingReferenceSources);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_projects_settings_profile_and_source_toggle_posture_from_settings_catalog()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-sr5</id>
                      <name>Shadowrun 5th Edition</name>
                      <code>SR5</code>
                    </book>
                    <book>
                      <id>book-rf</id>
                      <name>Run Faster</name>
                      <code>RF</code>
                    </book>
                  </books>
                </chummer>
                """);
            File.WriteAllText(
                Path.Combine(dataDir, "settings.xml"),
                """
                <chummer>
                  <settings>
                    <setting>
                      <name>Standard</name>
                      <books>
                        <book>SR5</book>
                      </books>
                    </setting>
                    <setting>
                      <name>Expanded</name>
                      <books>
                        <book>SR5</book>
                        <book>RF</book>
                      </books>
                    </setting>
                  </settings>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.SettingsLanePosture);
            Assert.AreEqual(2, response.SettingsProfileCount);
            Assert.AreEqual(2, response.SettingsProfilesWithSourceToggles);
            Assert.AreEqual(2, response.DistinctSourcebookToggles);
            Assert.AreEqual("governed", response.SourceToggleLanePosture);
            Assert.AreEqual(100, response.SourcebookToggleCoveragePercent);
            Assert.AreEqual("missing", response.CustomDataLanePosture);
            Assert.AreEqual(0, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(0, response.DistinctCustomDataDirectoryCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_source_toggle_posture_when_settings_reference_unknown_sourcebook_codes()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "books.xml"),
                """
                <chummer>
                  <books>
                    <book>
                      <id>book-sr5</id>
                      <name>Shadowrun 5th Edition</name>
                      <code>SR5</code>
                    </book>
                  </books>
                </chummer>
                """);
            File.WriteAllText(
                Path.Combine(dataDir, "settings.xml"),
                """
                <chummer>
                  <settings>
                    <setting>
                      <name>Custom</name>
                      <books>
                        <book>SR5</book>
                        <book>UNKNOWN</book>
                      </books>
                    </setting>
                  </settings>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.SettingsLanePosture);
            Assert.AreEqual(1, response.SettingsProfileCount);
            Assert.AreEqual(1, response.SettingsProfilesWithSourceToggles);
            Assert.AreEqual(2, response.DistinctSourcebookToggles);
            Assert.AreEqual("stale", response.SourceToggleLanePosture);
            Assert.AreEqual(100, response.SourcebookToggleCoveragePercent);
            Assert.AreEqual("missing", response.CustomDataLanePosture);
            Assert.AreEqual(0, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(0, response.DistinctCustomDataDirectoryCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_custom_data_lane_when_settings_reference_custom_data_without_enabled_overlay()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "settings.xml"),
                """
                <chummer>
                  <settings>
                    <setting>
                      <name>Custom Data Test</name>
                      <customdatadirectorynames>
                        <customdatadirectoryname>
                          <directoryname>German Data Changes</directoryname>
                          <enabled>True</enabled>
                        </customdatadirectoryname>
                      </customdatadirectorynames>
                    </setting>
                  </settings>
                </chummer>
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual(1, response.SettingsProfileCount);
            Assert.AreEqual(1, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(1, response.DistinctCustomDataDirectoryCount);
            Assert.AreEqual("stale", response.CustomDataLanePosture);
            Assert.AreEqual(0, response.EnabledDataOverlayCount);
            Assert.AreEqual("missing", response.XmlBridgePosture);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_custom_data_lane_when_settings_reference_custom_data_with_enabled_overlay()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(
                Path.Combine(dataDir, "settings.xml"),
                """
                <chummer>
                  <settings>
                    <setting>
                      <name>Custom Data Test</name>
                      <customdatadirectorynames>
                        <customdatadirectoryname>
                          <directoryname>German Data Changes</directoryname>
                          <enabled>True</enabled>
                        </customdatadirectoryname>
                        <customdatadirectoryname>
                          <directoryname>Sum-to-Ten Improved</directoryname>
                          <enabled>True</enabled>
                        </customdatadirectoryname>
                      </customdatadirectorynames>
                    </setting>
                  </settings>
                </chummer>
                """);
            string amendsRoot = Path.Combine(root, "Amends");
            string overlayData = Path.Combine(amendsRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(Path.Combine(overlayData, "skills.xml"), "<chummer><skills><skill /></skills></chummer>");
            File.WriteAllText(
                Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"custom-data-overlay\",\n  \"priority\": 100,\n  \"enabled\": true\n}");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual(1, response.SettingsProfileCount);
            Assert.AreEqual(1, response.SettingsProfilesWithCustomDataDirectories);
            Assert.AreEqual(2, response.DistinctCustomDataDirectoryCount);
            Assert.AreEqual("governed", response.CustomDataLanePosture);
            Assert.AreEqual(1, response.EnabledDataOverlayCount);
            Assert.AreEqual("governed", response.XmlBridgePosture);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_sr6_designer_tool_posture_from_catalog_coverage()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "spells.xml"), "<chummer><spells /></chummer>");
            File.WriteAllText(Path.Combine(dataDir, "vehicles.xml"), "<chummer><vehicles /></chummer>");
            File.WriteAllText(Path.Combine(dataDir, "programs.xml"), "<chummer><programs /></chummer>");
            File.WriteAllText(Path.Combine(dataDir, "drugcomponents.xml"), "<chummer><drugcomponents /></chummer>");
            File.WriteAllText(Path.Combine(dataDir, "qualities.xml"), "<chummer><qualities /></chummer>");

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_sr6_designer_tool_posture_when_catalog_coverage_is_partial()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "spells.xml"), "<chummer><spells /></chummer>");

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("stale", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(1, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_house_rule_lane_when_house_rule_overlay_exists()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "skills.xml"), "<chummer><skills /></chummer>");

            string amendsRoot = Path.Combine(root, "Amends");
            string overlayData = Path.Combine(amendsRoot, "data");
            Directory.CreateDirectory(overlayData);
            File.WriteAllText(Path.Combine(overlayData, "qualities.xml"), "<chummer><qualities><quality /></qualities></chummer>");
            File.WriteAllText(
                Path.Combine(amendsRoot, "manifest.json"),
                "{\n  \"id\": \"house-rules\",\n  \"name\": \"House Rules\",\n  \"priority\": 120,\n  \"enabled\": true,\n  \"mode\": \"merge-catalog\"\n}");

            var overlays = new FileSystemContentOverlayCatalogService(root, root, amendsRoot);
            var service = new XmlToolCatalogService(overlays);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.HouseRuleLanePosture);
            Assert.AreEqual(1, response.HouseRuleOverlayCount);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_online_storage_lane_when_hub_and_mobile_release_receipts_cover_restore_journey()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "books.xml"), "<chummer><books /></chummer>");

            string hubPublishedDir = Path.Combine(root, "chummer.run-services", ".codex-studio", "published");
            Directory.CreateDirectory(hubPublishedDir);
            File.WriteAllText(
                Path.Combine(hubPublishedDir, "HUB_LOCAL_RELEASE_PROOF.generated.json"),
                """
                {
                  "status": "passed",
                  "journeys_passed": [
                    "install_claim_restore_continue",
                    "campaign_session_recover_recap"
                  ]
                }
                """);

            string mobilePublishedDir = Path.Combine(root, "chummer-play", ".codex-studio", "published");
            Directory.CreateDirectory(mobilePublishedDir);
            File.WriteAllText(
                Path.Combine(mobilePublishedDir, "MOBILE_LOCAL_RELEASE_PROOF.generated.json"),
                """
                {
                  "status": "passed",
                  "journeys_passed": [
                    "install_claim_restore_continue"
                  ]
                }
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.OnlineStorageLanePosture);
            Assert.AreEqual("governed", response.OnlineStorageReceiptPosture);
            Assert.AreEqual(2, response.OnlineStorageReceiptsCovered);
            Assert.AreEqual(2, response.OnlineStorageReceiptsExpected);
            Assert.AreEqual(100, response.OnlineStorageCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_online_storage_lane_when_only_one_receipt_covers_restore_journey()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "books.xml"), "<chummer><books /></chummer>");

            string hubPublishedDir = Path.Combine(root, "chummer.run-services", ".codex-studio", "published");
            Directory.CreateDirectory(hubPublishedDir);
            File.WriteAllText(
                Path.Combine(hubPublishedDir, "HUB_LOCAL_RELEASE_PROOF.generated.json"),
                """
                {
                  "status": "passed",
                  "journeys_passed": [
                    "install_claim_restore_continue"
                  ]
                }
                """);

            string mobilePublishedDir = Path.Combine(root, "chummer-play", ".codex-studio", "published");
            Directory.CreateDirectory(mobilePublishedDir);
            File.WriteAllText(
                Path.Combine(mobilePublishedDir, "MOBILE_LOCAL_RELEASE_PROOF.generated.json"),
                """
                {
                  "status": "failed",
                  "journeys_passed": []
                }
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("stale", response.OnlineStorageLanePosture);
            Assert.AreEqual("stale", response.OnlineStorageReceiptPosture);
            Assert.AreEqual(1, response.OnlineStorageReceiptsCovered);
            Assert.AreEqual(2, response.OnlineStorageReceiptsExpected);
            Assert.AreEqual(50, response.OnlineStorageCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_governed_import_oracle_lane_when_fixture_families_and_certification_are_present()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "books.xml"), "<chummer><books /></chummer>");

            string sr4Dir = Path.Combine(root, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4");
            Directory.CreateDirectory(sr4Dir);
            File.WriteAllText(Path.Combine(sr4Dir, "sample.chum4"), "<character><name>SR4 Sample</name></character>");

            string sr5Dir = Path.Combine(root, "Chummer.Tests", "TestFiles");
            Directory.CreateDirectory(sr5Dir);
            File.WriteAllText(Path.Combine(sr5Dir, "sample.chum5"), "<character><name>SR5 Sample</name></character>");

            string heroLabDir = Path.Combine(root, "Chummer.CoreEngine.Tests", "Fixtures", "HeroLab", "Sr5");
            Directory.CreateDirectory(heroLabDir);
            File.WriteAllText(Path.Combine(heroLabDir, "sample.por"), "<portfolio />");

            string certificationDir = Path.Combine(root, ".codex-studio", "published");
            Directory.CreateDirectory(certificationDir);
            File.WriteAllText(
                Path.Combine(certificationDir, "IMPORT_PARITY_CERTIFICATION.generated.json"),
                """
                {
                  "status": "passed",
                  "notes": "SR4/SR5/SR6 import parity is proven."
                }
                """);

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("governed", response.ImportOracleLanePosture);
            Assert.AreEqual("governed", response.ImportOracleReceiptPosture);
            Assert.AreEqual(1, response.LegacyChummer4FixtureCount);
            Assert.AreEqual(1, response.LegacyChummer5FixtureCount);
            Assert.AreEqual(1, response.HeroLabFixtureCount);
            Assert.AreEqual(4, response.ImportOracleSourcesCovered);
            Assert.AreEqual(4, response.ImportOracleSourcesExpected);
            Assert.AreEqual(100, response.ImportOracleCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [TestMethod]
    public void Master_index_reports_stale_import_oracle_lane_when_certification_receipt_is_missing()
    {
        string root = CreateTempDirectory();
        try
        {
            string dataDir = Path.Combine(root, "data");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, "books.xml"), "<chummer><books /></chummer>");

            string sr4Dir = Path.Combine(root, "Chummer.CoreEngine.Tests", "Fixtures", "Sr4");
            Directory.CreateDirectory(sr4Dir);
            File.WriteAllText(Path.Combine(sr4Dir, "sample.chum4"), "<character><name>SR4 Sample</name></character>");

            string sr5Dir = Path.Combine(root, "Chummer.Tests", "TestFiles");
            Directory.CreateDirectory(sr5Dir);
            File.WriteAllText(Path.Combine(sr5Dir, "sample.chum5"), "<character><name>SR5 Sample</name></character>");

            string heroLabDir = Path.Combine(root, "Chummer.CoreEngine.Tests", "Fixtures", "HeroLab", "Sr5");
            Directory.CreateDirectory(heroLabDir);
            File.WriteAllText(Path.Combine(heroLabDir, "sample.por"), "<portfolio />");

            var service = new XmlToolCatalogService(root);
            MasterIndexResponse response = service.GetMasterIndex();

            Assert.AreEqual("stale", response.ImportOracleLanePosture);
            Assert.AreEqual("missing", response.ImportOracleReceiptPosture);
            Assert.AreEqual(1, response.LegacyChummer4FixtureCount);
            Assert.AreEqual(1, response.LegacyChummer5FixtureCount);
            Assert.AreEqual(1, response.HeroLabFixtureCount);
            Assert.AreEqual(3, response.ImportOracleSourcesCovered);
            Assert.AreEqual(4, response.ImportOracleSourcesExpected);
            Assert.AreEqual(75, response.ImportOracleCoveragePercent);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "chummer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }
}
