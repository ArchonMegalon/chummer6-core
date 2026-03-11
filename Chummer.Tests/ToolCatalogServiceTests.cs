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
