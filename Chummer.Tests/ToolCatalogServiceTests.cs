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
            Assert.AreEqual("missing", response.XmlBridgePosture);
            Assert.AreEqual(0, response.EnabledDataOverlayCount);
            Assert.AreEqual("missing", response.Sr6SupplementLanePosture);
            Assert.AreEqual("missing", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(0, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
            Assert.AreEqual("missing", response.HouseRuleLanePosture);
            Assert.AreEqual(0, response.HouseRuleOverlayCount);
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

            MasterIndexSourcebookEntry rf = response.Sourcebooks.Single(sourcebook => sourcebook.Code == "RF");
            Assert.AreEqual("book-rf", rf.Id);
            Assert.AreEqual("Run Faster", rf.Name);
            Assert.IsFalse(rf.Permanent);
            Assert.AreEqual("no-snippets", rf.ReferencePosture);
            Assert.AreEqual(0, rf.RuleSnippetCount);

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
            Assert.AreEqual("missing", response.XmlBridgePosture);
            Assert.AreEqual(0, response.EnabledDataOverlayCount);
            Assert.AreEqual("stale", response.Sr6SupplementLanePosture);
            Assert.AreEqual("missing", response.Sr6DesignerToolsPosture);
            Assert.AreEqual(0, response.Sr6DesignerFamiliesAvailable);
            Assert.AreEqual(5, response.Sr6DesignerFamiliesExpected);
            Assert.AreEqual("missing", response.HouseRuleLanePosture);
            Assert.AreEqual(0, response.HouseRuleOverlayCount);
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
