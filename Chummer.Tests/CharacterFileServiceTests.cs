using System;
using System.IO;
using System.Linq;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CharacterFileServiceTests
{
    [TestMethod]
    public void ParseSummaryFromXml_reads_expected_values_from_sample_character()
    {
        string xml = File.ReadAllText(FindTestFilePath("Barrett.chum5"));
        var service = new CharacterFileService();

        CharacterFileSummary summary = service.ParseSummaryFromXml(xml);

        Assert.AreEqual("Moa", summary.Name);
        Assert.AreEqual("Barrett", summary.Alias);
        Assert.AreEqual("Human", summary.Metatype);
        Assert.AreEqual("SumtoTen", summary.BuildMethod);
        Assert.IsFalse(summary.Created);
        Assert.AreEqual(3m, summary.Karma);
        Assert.AreEqual(617.50m, summary.Nuyen);
    }

    [TestMethod]
    public void ValidateXml_reports_errors_for_missing_required_nodes()
    {
        const string xml = "<character><name>Test</name><karma>not-a-number</karma></character>";
        var service = new CharacterFileService();

        CharacterValidationResult validation = service.ValidateXml(xml);

        Assert.IsFalse(validation.IsValid);
        Assert.IsGreaterThanOrEqualTo(1, validation.Issues.Count);
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "MissingRequiredNode"));
        Assert.IsTrue(validation.Issues.Any(issue => issue.Code == "InvalidDecimal"));
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority)]
    [DataRow(CharacterCreationBuildMethods.SumToTen)]
    public void ValidateXml_accepts_omitted_metatype_only_for_pending_typed_priority_setup(
        string buildMethod)
    {
        var service = new CharacterFileService();

        CharacterValidationResult validation = service.ValidateXml(
            CreateValidationXml(buildMethod, created: false, metatypeElement: null));

        Assert.IsTrue(validation.IsValid);
        Assert.AreEqual(0, validation.Issues.Count);
    }

    [DataTestMethod]
    [DataRow(CharacterCreationBuildMethods.Priority, true, null)]
    [DataRow(CharacterCreationBuildMethods.Karma, false, null)]
    [DataRow("priority", false, null)]
    [DataRow(CharacterCreationBuildMethods.Priority, false, "<metatype />")]
    public void ValidateXml_rejects_missing_or_empty_metatype_outside_exact_pending_typed_setup(
        string buildMethod,
        bool created,
        string? metatypeElement)
    {
        var service = new CharacterFileService();

        CharacterValidationResult validation = service.ValidateXml(
            CreateValidationXml(buildMethod, created, metatypeElement));

        Assert.IsFalse(validation.IsValid);
        CharacterValidationIssue issue = validation.Issues.Single(item =>
            string.Equals(item.Code, "MissingRequiredNode", StringComparison.Ordinal)
            && string.Equals(item.Path, "/character/metatype", StringComparison.Ordinal));
        Assert.AreEqual("Required node 'metatype' is missing or empty.", issue.Message);
    }

    [TestMethod]
    public void ParseSummaryFromXml_defaults_missing_numeric_and_created_fields_for_minimal_imports()
    {
        const string xml = "<character><name>Minimal Runner</name></character>";
        var service = new CharacterFileService();

        CharacterFileSummary summary = service.ParseSummaryFromXml(xml);

        Assert.AreEqual("Minimal Runner", summary.Name);
        Assert.AreEqual(0m, summary.Karma);
        Assert.AreEqual(0m, summary.Nuyen);
        Assert.IsFalse(summary.Created);
    }

    [TestMethod]
    public void ApplyMetadataUpdate_updates_name_alias_and_all_notes_nodes()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterFileService();

        string updatedXml = service.ApplyMetadataUpdate(xml, new CharacterMetadataUpdate(
            Name: "Updated Name",
            Alias: "Updated Alias",
            Notes: "Updated Notes")
        {
            GameNotes = "Updated Game Notes",
            GroupNotes = "Updated Group Notes"
        });

        CharacterFileSummary summary = service.ParseSummaryFromXml(updatedXml);
        CharacterValidationResult validation = service.ValidateXml(updatedXml);

        Assert.AreEqual("Updated Name", summary.Name);
        Assert.AreEqual("Updated Alias", summary.Alias);
        Assert.IsTrue(updatedXml.Contains("<notes>Updated Notes</notes>", StringComparison.Ordinal));
        Assert.IsTrue(updatedXml.Contains("<gamenotes>Updated Game Notes</gamenotes>", StringComparison.Ordinal));
        Assert.IsTrue(updatedXml.Contains("<groupnotes>Updated Group Notes</groupnotes>", StringComparison.Ordinal));
        Assert.IsTrue(validation.IsValid);
    }

    [TestMethod]
    public void ApplyMetadataUpdate_preserves_unmentioned_long_notes()
    {
        const string xml = "<character><name>Runner</name><notes>Character</notes><gamenotes>Game</gamenotes><groupnotes>Group</groupnotes></character>";
        var service = new CharacterFileService();

        string updatedXml = service.ApplyMetadataUpdate(xml, new CharacterMetadataUpdate(
            Name: null,
            Alias: "Alias",
            Notes: null));

        Assert.IsTrue(updatedXml.Contains("<notes>Character</notes>", StringComparison.Ordinal));
        Assert.IsTrue(updatedXml.Contains("<gamenotes>Game</gamenotes>", StringComparison.Ordinal));
        Assert.IsTrue(updatedXml.Contains("<groupnotes>Group</groupnotes>", StringComparison.Ordinal));
    }

    private static string FindTestFilePath(string fileName)
    {
        DirectoryInfo current = new(AppDomain.CurrentDomain.BaseDirectory);
        while (true)
        {
            string candidate = Path.Combine(current.FullName, "Chummer.Tests", "TestFiles", fileName);
            if (File.Exists(candidate))
                return candidate;

            if (current.Parent == null)
                break;

            current = current.Parent;
        }

        throw new FileNotFoundException("Could not locate test character file.", fileName);
    }

    private static string CreateValidationXml(
        string buildMethod,
        bool created,
        string? metatypeElement)
        => $"""
           <character>
             <name>Pending Runner</name>
             {metatypeElement}
             <buildmethod>{buildMethod}</buildmethod>
             <createdversion>5.225.0</createdversion>
             <appversion>5.225.0</appversion>
             <karma>0</karma>
             <nuyen>0</nuyen>
             <created>{created}</created>
           </character>
           """;
}
