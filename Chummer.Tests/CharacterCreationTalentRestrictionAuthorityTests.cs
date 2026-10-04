using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationTalentRestrictionAuthorityTests
{
    private const string ApprenticeId = "c1d4d7ec-9ebb-4e85-ae72-8155b0f27478";
    private const string AirId = "380a4860-e5b7-4d07-9b8f-24951c1d656a";

    [TestMethod]
    public void Apprentice_categories_are_real_source_choices_not_defaults_or_display_labels()
    {
        var (quality, categories, spirits) = Sources();
        categories.Element("category")!.SetAttributeValue("translate", "Kampf");
        spirits.Single(row => row.Element("id")!.Value == AirId).Add(new XElement("translate", "Luftgeist"));
        Assert.IsTrue(Project(quality, categories, spirits, out var catalog));
        Assert.IsNotNull(catalog);
        CollectionAssert.AreEqual(categories.Elements().Select(row => row.Value).ToArray(),
            catalog.SpellCategories.Select(row => row.Value).ToArray());
        Assert.AreEqual("Kampf", catalog.SpellCategories.Single(row => row.Value == "Combat").Label);
        Assert.AreEqual("Luftgeist", catalog.SpiritCategories.Single(row => row.SourceId == AirId).Label);
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog, null, out _));
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog, new("Kampf", AirId), out _));
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog, new("Combat", "Luftgeist"), out _));
        Assert.IsTrue(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog, new("Combat", AirId), out var plan));
        Assert.AreEqual("Spirit of Air", plan!.Spirit.Name);
    }

    [TestMethod]
    public void Apprentice_materializes_both_restrictions_and_both_fixed_unlocks_without_changing_source()
    {
        var (quality, categories, spirits) = Sources();
        Assert.IsTrue(Project(quality, categories, spirits, out var catalog));
        Assert.IsTrue(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog!, new("Combat", AirId), out var plan));
        var option = CharacterCreationKarmaTalentAuthority.Project(XElement.Parse(quality.CanonicalSourceXml), 1, true)!;
        Assert.IsFalse(option.IsEnabled, "Without the typed source catalog, prompts must remain blocked.");
        Assert.IsTrue(CharacterCreationKarmaTalentAuthority.Project(XElement.Parse(quality.CanonicalSourceXml), 1, true, catalog)!.IsEnabled);
        Assert.IsTrue(CharacterCreationLifeModuleTalentAuthority.TryProjectUnlockChoices(option, out var choices));
        Assert.IsEmpty(choices, "Sorcery and Conjuring are both granted, not a choose-one prompt.");
        var flags = new HashSet<string>(StringComparer.Ordinal);
        var improvements = new List<XElement>();
        string original = quality.CanonicalSourceXml;
        string extra = CharacterCreationAwakenedLegacyProjector.CompileBonus(XElement.Parse(original).Element("bonus"),
            string.Empty, [], quality, "11111111-1111-1111-1111-111111111111", flags, improvements, [], plan);
        Assert.AreEqual(original, quality.CanonicalSourceXml);
        Assert.AreEqual("Spirit of Air", extra);
        CollectionAssert.AreEquivalent(new[] { "magenabled", "magician" }, flags.ToArray());
        CollectionAssert.AreEqual(new[] { "Sorcery", "Conjuring" }, improvements.Where(row =>
            row.Element("improvementttype")!.Value == "SpecialSkills").Select(row => row.Element("improvedname")!.Value).ToArray());
        Assert.AreEqual("Combat", improvements.Single(row => row.Element("improvementttype")!.Value == "LimitSpellCategory")
            .Element("improvedname")!.Value);
        Assert.AreEqual("Spirit of Air", improvements.Single(row => row.Element("improvementttype")!.Value == "LimitSpiritCategory")
            .Element("improvedname")!.Value);
        Assert.IsTrue(CharacterCreationLegacySourceProjector.TryBuildGrantedQualityInstance(quality,
            "11111111-1111-1111-1111-111111111111", extra, "Selected", out var saved));
        Assert.AreEqual(ApprenticeId, saved.Element("sourceid")!.Value);
        Assert.AreEqual(extra, saved.Element("extra")!.Value);
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("wrong-quality")]
    [DataRow("changed-bonus")]
    [DataRow("namespaced-effect")]
    public void Restriction_materializer_rejects_missing_stale_or_retargeted_answers(string mutation)
    {
        var (quality, categories, spirits) = Sources();
        Assert.IsTrue(Project(quality, categories, spirits, out var catalog));
        Assert.IsTrue(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, catalog!, new("Combat", AirId), out var plan));
        var bonus = XElement.Parse(quality.CanonicalSourceXml).Element("bonus")!;
        if (mutation == "missing") plan = null;
        if (mutation == "wrong-quality") quality = quality with { SourceId = "11111111-1111-1111-1111-111111111111" };
        if (mutation == "changed-bonus") bonus.Element("limitspellcategory")!.Value = "Health";
        if (mutation == "namespaced-effect") bonus.Element("limitspellcategory")!.Name = XName.Get("limitspellcategory", "urn:foreign");
        Assert.ThrowsExactly<InvalidDataException>(() => CharacterCreationAwakenedLegacyProjector.CompileBonus(
            bonus, string.Empty, [], quality, "11111111-1111-1111-1111-111111111111", [], [], [], plan));
    }

    [TestMethod]
    [DataRow("duplicate-category")]
    [DataRow("duplicate-spirit-id")]
    [DataRow("duplicate-spirit-name")]
    [DataRow("duplicate-name-field")]
    [DataRow("namespaced-category")]
    [DataRow("unknown-category-attribute")]
    [DataRow("nested-category")]
    [DataRow("invalid-spirit-id")]
    [DataRow("unknown-prompt")]
    public void Malformed_or_ambiguous_effective_sources_do_not_offer_choices(string mutation)
    {
        var (quality, categories, spirits) = Sources();
        if (mutation == "duplicate-category") categories.Add(new XElement(categories.Element("category")!));
        if (mutation == "duplicate-spirit-id") spirits[1].Element("id")!.Value = spirits[0].Element("id")!.Value;
        if (mutation == "duplicate-spirit-name") spirits[1].Element("name")!.Value = spirits[0].Element("name")!.Value;
        if (mutation == "duplicate-name-field") spirits[0].Add(new XElement(spirits[0].Element("name")!));
        if (mutation == "namespaced-category") categories.Element("category")!.Name = XName.Get("category", "urn:foreign");
        if (mutation == "unknown-category-attribute") categories.Element("category")!.SetAttributeValue("unknown", "1");
        if (mutation == "nested-category") categories.Element("category")!.Add(new XElement("unknown", "1"));
        if (mutation == "invalid-spirit-id") spirits[0].Element("id")!.Value = "friendly-name";
        if (mutation == "unknown-prompt")
        {
            var row = XElement.Parse(quality.CanonicalSourceXml);
            row.Element("bonus")!.Element("limitspellcategory")!.SetAttributeValue("unknown", "1");
            quality = Quality(row);
        }
        Assert.IsFalse(Project(quality, categories, spirits, out var catalog));
        Assert.IsNull(catalog);
    }

    [TestMethod]
    public void Catalog_binds_both_effective_files_and_rejects_stale_quality_or_modified_choices()
    {
        var (quality, categories, spirits) = Sources();
        Assert.IsTrue(Project(quality, categories, spirits, out var original));
        Assert.IsTrue(CharacterCreationTalentRestrictionAuthority.TryProjectCatalog(quality, categories, spirits,
            Digest("changed-spells"), Digest("spirits"), out var spellsChanged));
        Assert.IsTrue(CharacterCreationTalentRestrictionAuthority.TryProjectCatalog(quality, categories, spirits,
            Digest("spells"), Digest("changed-spirits"), out var spiritsChanged));
        Assert.AreNotEqual(original!.AuthorityDigest, spellsChanged!.AuthorityDigest);
        Assert.AreNotEqual(original.AuthorityDigest, spiritsChanged!.AuthorityDigest);
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryChoose(quality, original with
            { SpellCategories = [new("invented", "invented", Digest("invented"))] }, new("invented", AirId), out _));
        var row = XElement.Parse(quality.CanonicalSourceXml);
        row.Element("karma")!.Value = "16";
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryChoose(Quality(row), original, new("Combat", AirId), out _));
        Assert.IsFalse(CharacterCreationTalentRestrictionAuthority.TryProjectCatalog(quality, categories, spirits,
            "unbound", Digest("spirits"), out _));
    }

    [TestMethod]
    [DataRow("Sorcery,Conjuring,Enchanting", null, true, "Sorcery,Conjuring,Enchanting")]
    [DataRow("Sorcery", null, true, "Sorcery")]
    [DataRow("Sorcery", "Conjuring", true, "")]
    [DataRow("Sorcery", "Conjuring,Enchanting", true, "Conjuring,Enchanting")]
    [DataRow("Sorcery,Conjuring", "Conjuring,Enchanting", false, "")]
    [DataRow("Sorcery,Sorcery", null, false, "")]
    [DataRow("Invalid", "Conjuring", false, "")]
    public void Fixed_unlocks_accumulate_but_multiple_choice_prompts_are_not_collapsed(
        string first, string? second, bool valid, string expected)
    {
        var (quality, _, _) = Sources();
        var row = XElement.Parse(quality.CanonicalSourceXml);
        row.Element("bonus")!.Elements("unlockskills").Remove();
        row.Element("bonus")!.Add(new XElement("unlockskills", first));
        if (second is not null) row.Element("bonus")!.Add(new XElement("unlockskills", second));
        var option = CharacterCreationKarmaTalentAuthority.Project(row, 1, true)!;
        Assert.AreEqual(valid, CharacterCreationLifeModuleTalentAuthority.TryProjectUnlockChoices(option, out var choices));
        Assert.AreEqual(expected, string.Join(',', choices));
    }

    private static bool Project(CharacterCreationTalentQualitySource quality, XElement categories, XElement[] spirits,
        out CharacterCreationTalentRestrictionCatalog? catalog)
        => CharacterCreationTalentRestrictionAuthority.TryProjectCatalog(quality, categories, spirits,
            Digest("spells"), Digest("spirits"), out catalog);

    private static (CharacterCreationTalentQualitySource, XElement, XElement[]) Sources()
    {
        var root = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Chummer/data/qualities.xml"))) root = root.Parent;
        Assert.IsNotNull(root);
        string data = Path.Combine(root.FullName, "Chummer/data");
        var quality = XDocument.Load(Path.Combine(data, "qualities.xml")).Root!.Element("qualities")!.Elements("quality")
            .Single(row => row.Element("id")?.Value == ApprenticeId);
        var categories = XDocument.Load(Path.Combine(data, "spells.xml")).Root!.Element("categories")!;
        var spirits = XDocument.Load(Path.Combine(data, "traditions.xml")).Root!.Element("spirits")!.Elements("spirit").ToArray();
        return (Quality(quality), categories, spirits);
    }

    private static CharacterCreationTalentQualitySource Quality(XElement row)
    {
        string xml = row.ToString(SaveOptions.DisableFormatting), input = Digest("qualities"), id = row.Element("id")!.Value;
        return new(id, string.Empty, id, row.Element("name")!.Value, row.Element("source")!.Value,
            row.Element("page")!.Value, input, CharacterCreationTalentQualitySourceRules.ComputeSourceNodeDigest(input, id, xml),
            xml, CharacterCreationMagicResonanceDigest.ComputeUtf8(xml), [$"qualities.xml#quality:{id}"]);
    }

    private static string Digest(string text) => CharacterCreationMagicResonanceDigest.ComputeUtf8(text);
}
