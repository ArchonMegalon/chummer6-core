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

    [TestMethod]
    public void ApplyMetadataUpdate_updates_name_alias_and_notes_nodes()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterFileService();

        string updatedXml = service.ApplyMetadataUpdate(xml, new CharacterMetadataUpdate(
            Name: "Updated Name",
            Alias: "Updated Alias",
            Notes: "Updated Notes"));

        CharacterFileSummary summary = service.ParseSummaryFromXml(updatedXml);
        CharacterValidationResult validation = service.ValidateXml(updatedXml);

        Assert.AreEqual("Updated Name", summary.Name);
        Assert.AreEqual("Updated Alias", summary.Alias);
        Assert.IsTrue(updatedXml.Contains("<notes>Updated Notes</notes>", StringComparison.Ordinal));
        Assert.IsTrue(validation.IsValid);
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
}
