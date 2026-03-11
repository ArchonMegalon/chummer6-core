#nullable enable annotations

using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Chummer.Tests;

[TestClass]
public class CharacterApplicationPortsTests
{
    [TestMethod]
    public void File_queries_parse_summary_from_xml()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>15</karma><nuyen>2500</nuyen><created>True</created></character>";

        XmlCharacterFileQueries queries = new(new CharacterFileService());
        CharacterFileSummary summary = queries.ParseSummary(new CharacterDocument(xml));

        Assert.AreEqual("Neo", summary.Name);
        Assert.AreEqual("The One", summary.Alias);
        Assert.AreEqual(15m, summary.Karma);
    }

    [TestMethod]
    public void Metadata_commands_update_xml_and_return_summary()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><createdversion>1.0</createdversion><appversion>1.0</appversion><karma>15</karma><nuyen>2500</nuyen><created>True</created></character>";

        XmlCharacterMetadataCommands commands = new(new CharacterFileService());
        UpdateCharacterMetadataResult result = commands.UpdateMetadata(new UpdateCharacterMetadataCommand(
            Document: new CharacterDocument(xml),
            Update: new CharacterMetadataUpdate(
                Name: "Updated",
                Alias: "Alias",
                Notes: "Hello")));

        Assert.AreEqual("Updated", result.Summary.Name);
        Assert.AreEqual("Alias", result.Summary.Alias);
        StringAssert.Contains(result.UpdatedDocument.Content, "<notes>Hello</notes>");
    }

    [TestMethod]
    public void Section_queries_route_to_expected_section_parser()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><created>True</created><adept>False</adept><magician>False</magician><technomancer>False</technomancer><ai>False</ai></character>";

        XmlCharacterSectionQueries queries = new(new CharacterSectionService());
        object section = queries.ParseSection("profile", new CharacterDocument(xml));

        Assert.IsInstanceOfType<CharacterProfileSection>(section);
        CharacterProfileSection profile = (CharacterProfileSection)section;
        Assert.AreEqual("Neo", profile.Name);
    }

    [TestMethod]
    public void Feature_slice_queries_delegate_to_character_section_service()
    {
        const string xml = "<character><name>Neo</name><alias>The One</alias><metatype>Human</metatype><buildmethod>Priority</buildmethod><created>True</created><adept>False</adept><magician>False</magician><technomancer>False</technomancer><ai>False</ai></character>";

        ICharacterSectionService sectionService = new CharacterSectionService();
        XmlCharacterOverviewQueries overview = new(sectionService);
        XmlCharacterStatsQueries stats = new(sectionService);
        XmlCharacterInventoryQueries inventory = new(sectionService);
        XmlCharacterMagicResonanceQueries magic = new(sectionService);
        XmlCharacterSocialNarrativeQueries social = new(sectionService);
        CharacterDocument document = new(xml);

        Assert.IsNotNull(overview.ParseProfile(document));
        Assert.IsNotNull(stats.ParseAttributes(document));
        Assert.IsNotNull(inventory.ParseInventory(document));
        Assert.IsNotNull(magic.ParseSpells(document));
        Assert.IsNotNull(social.ParseQualities(document));
    }

    [TestMethod]
    public void Section_queries_parse_profile_for_blue_sample_character()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        ICharacterSectionService sectionService = new CharacterSectionService();

        XmlCharacterSectionQueries queries = new(
            new XmlCharacterOverviewQueries(sectionService),
            new XmlCharacterStatsQueries(sectionService),
            new XmlCharacterInventoryQueries(sectionService),
            new XmlCharacterMagicResonanceQueries(sectionService),
            new XmlCharacterSocialNarrativeQueries(sectionService));

        object section = queries.ParseSection("profile", new CharacterDocument(xml));
        Assert.IsInstanceOfType<CharacterProfileSection>(section);
    }

    private static string FindTestFilePath(string fileName)
    {
        string? root = Environment.GetEnvironmentVariable("CHUMMER_REPO_ROOT");
        string[] candidates =
        {
            Path.Combine(Directory.GetCurrentDirectory(), "Chummer.Tests", "TestFiles", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "TestFiles", fileName),
            Path.Combine(AppContext.BaseDirectory, "TestFiles", fileName),
            Path.Combine("/src", "Chummer.Tests", "TestFiles", fileName),
            string.IsNullOrWhiteSpace(root) ? string.Empty : Path.Combine(root, "Chummer.Tests", "TestFiles", fileName)
        };

        string? match = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
        if (match is null)
            throw new FileNotFoundException("Could not locate test file.", fileName);

        return match;
    }
}
