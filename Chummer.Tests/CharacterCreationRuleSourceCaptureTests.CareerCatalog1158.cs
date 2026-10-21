using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Application.Content;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationRuleSourceCaptureTests
{
    private const string PowerId1158 = "a1000000-0000-0000-0000-000000000001";
    private const string OtherPowerId1158 = "b1000000-0000-0000-0000-000000000002";

    [TestMethod]
    public void CareerCatalog1158_complete_rows_match_exact_sources_and_results_are_detached()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("powers.xml"), PowerDocument1158(
            PowerRow1158(OtherPowerId1158, "Other"), PowerRow1158(PowerId1158, "First")));
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        var expected = AssertPowerCatalog1158(context, PowerId1158, OtherPowerId1158);
        Assert.IsTrue(context.TryResolveCareerPowerCatalog(out var first));
        Assert.IsTrue(((ICollection<CharacterCareerPowerSource>)first).IsReadOnly);
        var detached = first.ToArray();
        var xml = XElement.Parse(detached[0].RawSourceXml);
        xml.Element("name")!.Value = "Changed";
        detached[0] = detached[0] with { RawSourceXml = xml.ToString() };
        Assert.IsTrue(context.TryResolveCareerPowerCatalog(out var again));
        Assert.AreNotSame(first, again);
        CollectionAssert.AreEqual(expected, again.ToArray());
    }

    [TestMethod]
    public void CareerCatalog1158_disabled_books_and_nonselected_containers_match_exact_semantics()
    {
        using var fixture = new Fixture();
        string disabled = PowerRow1158(OtherPowerId1158, "Other", "CF");
        // The duplicate of a disabled row in a second container is not an
        // enabled target. A unique row there also must not enter the catalog.
        File.WriteAllText(fixture.Data("powers.xml"), "<chummer><powers>"
            + PowerRow1158(PowerId1158, "First") + disabled + "</powers><powers>"
            + disabled + PowerRow1158(Guid.NewGuid().ToString("D"), "Hidden") + "</powers></chummer>");
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        AssertPowerCatalog1158(context, PowerId1158);
        Assert.IsFalse(context.TryResolveCareerPowerSource(Guid.Parse(OtherPowerId1158), out _));
    }

    [TestMethod]
    [DataRow("same-container")]
    [DataRow("other-container")]
    [DataRow("duplicate-child")]
    [DataRow("empty-id")]
    [DataRow("duplicate-disabled")]
    public void CareerCatalog1158_ambiguous_or_malformed_identities_still_reject(string kind)
    {
        using var fixture = new Fixture();
        string row = PowerRow1158(PowerId1158, "First", kind == "duplicate-disabled" ? "CF" : "SR5");
        string duplicate = row.Replace(PowerId1158, PowerId1158.ToUpperInvariant(), StringComparison.Ordinal);
        string xml = kind switch
        {
            "other-container" => "<chummer><powers>" + row + "</powers><powers>" + duplicate + "</powers></chummer>",
            "duplicate-child" => PowerDocument1158(row.Replace("</id>", "</id><id>" + PowerId1158 + "</id>", StringComparison.Ordinal)),
            "empty-id" => PowerDocument1158(row.Replace(PowerId1158, Guid.Empty.ToString("D"), StringComparison.Ordinal)),
            _ => PowerDocument1158(row, duplicate)
        };
        File.WriteAllText(fixture.Data("powers.xml"), xml);
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        Assert.IsFalse(context.TryResolveCareerPowerCatalog(out var rejected));
        Assert.AreEqual(0, rejected.Count);
        Assert.IsFalse(context.TryResolveCareerPowerSource(Guid.Parse(PowerId1158), out _));
    }

    [TestMethod]
    public void CareerCatalog1158_selected_override_custom_and_amend_keep_exact_xml_and_digests()
    {
        using var fixture = new Fixture(selected: true);
        File.WriteAllText(fixture.Data("powers.xml"), PowerDocument1158(PowerRow1158(PowerId1158, "First")));
        File.WriteAllText(fixture.Custom("First", "override_powers.xml"), PowerDocument1158(PowerRow1158(PowerId1158, "Override")));
        File.WriteAllText(fixture.Custom("First", "custom_powers.xml"), PowerDocument1158(PowerRow1158(OtherPowerId1158, "Added")));
        File.WriteAllText(fixture.Custom("Second", "amend_powers.xml"),
            $"<chummer><powers><power><id>{PowerId1158}</id><points amendoperation=\"replace\">0.75</points></power></powers></chummer>");
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        var rows = AssertPowerCatalog1158(context, PowerId1158, OtherPowerId1158);
        var amended = XElement.Parse(rows.Single(row => row.SourceId == Guid.Parse(PowerId1158)).RawSourceXml);
        Assert.AreEqual("Override", amended.Element("name")!.Value);
        Assert.AreEqual("0.75", amended.Element("points")!.Value);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CareerCatalog1158_overlay_merge_and_replace_keep_exact_authority(bool replace)
    {
        using var fixture = new Fixture();
        string row = PowerRow1158(PowerId1158, "First");
        // A replacement clears ambiguity in the earlier raw contributor.
        File.WriteAllText(fixture.Data("powers.xml"), replace ? PowerDocument1158(row, row) : PowerDocument1158(row));
        string overlay = AddPowerOverlay1158(fixture, replace);
        File.WriteAllText(overlay, PowerDocument1158(PowerRow1158(PowerId1158, "Overlay"), PowerRow1158(OtherPowerId1158, "Other")));
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        var rows = AssertPowerCatalog1158(context, PowerId1158, OtherPowerId1158);
        Assert.AreEqual("Overlay", rows.Single(row => row.SourceId == Guid.Parse(PowerId1158)).Name);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CareerCatalog1158_raw_duplicates_collapsed_by_merge_or_custom_still_reject(bool custom)
    {
        using var fixture = new Fixture(selected: custom);
        string row = PowerRow1158(PowerId1158, "First");
        File.WriteAllText(fixture.Data("powers.xml"), PowerDocument1158(row));
        string path = custom ? fixture.Custom("First", "override_powers.xml") : AddPowerOverlay1158(fixture, false);
        File.WriteAllText(path, PowerDocument1158(row, row));
        var context = fixture.Resolver().TryCreateContext(fixture.Character)!;
        Assert.IsFalse(context.TryResolveCareerPowerCatalog(out var rejected));
        Assert.AreEqual(0, rejected.Count);
        Assert.IsFalse(context.TryResolveCareerPowerSource(Guid.Parse(PowerId1158), out _));
    }

    [TestMethod]
    [DataRow("powers.xml")]
    [DataRow("settings.xml")]
    public void CareerCatalog1158_observed_source_or_profile_ABA_stays_rejected(string file)
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("powers.xml"), PowerDocument1158(PowerRow1158(PowerId1158, "First")));
        var context = fixture.Resolver(weakIdentity: true).TryCreateContext(fixture.Character)!;
        var expected = AssertPowerCatalog1158(context, PowerId1158);
        string path = fixture.Data(file);
        byte[] original = File.ReadAllBytes(path);
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        ReplaceSameLength(path, file == "powers.xml" ? "First" : "<book>SR5</book>",
            file == "powers.xml" ? "Other" : "<book>SR4</book>");
        Assert.IsFalse(context.TryResolveCareerPowerCatalog(out _));
        Assert.IsFalse(context.TryResolveCareerPowerSource(Guid.Parse(PowerId1158), out _));
        File.WriteAllBytes(path, original);
        File.SetLastWriteTimeUtc(path, timestamp);
        Assert.IsFalse(context.TryResolveCareerPowerCatalog(out _));
        Assert.IsFalse(context.TryResolveCareerPowerSource(Guid.Parse(PowerId1158), out _));
        var fresh = fixture.Resolver(weakIdentity: true).TryCreateContext(fixture.Character)!;
        CollectionAssert.AreEqual(expected, AssertPowerCatalog1158(fresh, PowerId1158));
    }

    [TestMethod]
    public void CareerCatalog1158_selected_profiles_do_not_share_book_or_digest_authority()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Data("powers.xml"), PowerDocument1158(
            PowerRow1158(PowerId1158, "First"), PowerRow1158(OtherPowerId1158, "Other", "CF")));
        var settings = XDocument.Load(fixture.Data("settings.xml"));
        var other = new XElement(settings.Root!.Element("settings")!.Element("setting")!);
        string otherId = Guid.NewGuid().ToString("D");
        other.Element("id")!.Value = otherId;
        other.Element("books")!.Element("book")!.Value = "CF";
        settings.Root.Element("settings")!.Add(other);
        settings.Save(fixture.Data("settings.xml"));
        using var reads = fixture.Resolver().CreateValidatedReadScope();
        var first = AssertPowerCatalog1158(reads.TryCreateContext(fixture.Character)!, PowerId1158).Single();
        var second = AssertPowerCatalog1158(reads.TryCreateContext(
            fixture.Character.Replace(ProfileId, otherId, StringComparison.Ordinal))!, OtherPowerId1158).Single();
        Assert.AreNotEqual(first.ProfileInputsDigest, second.ProfileInputsDigest);
        Assert.AreEqual(first.SourceInputsDigest, second.SourceInputsDigest);
    }

    private static CharacterCareerPowerSource[] AssertPowerCatalog1158(ICharacterSourceDataContext context, params string[] ids)
    {
        Assert.IsNotNull(context);
        var expected = ids.Select(id =>
        {
            Assert.IsTrue(context.TryResolveCareerPowerSource(Guid.Parse(id), out var source));
            Assert.IsNotNull(source);
            return source;
        }).OrderBy(row => row!.SourceId).ToArray();
        Assert.IsTrue(context.TryResolveCareerPowerCatalog(out var actual));
        CollectionAssert.AreEqual(expected, actual.ToArray());
        return actual.ToArray();
    }

    private static string PowerDocument1158(params string[] rows) => "<chummer><powers>" + string.Concat(rows) + "</powers></chummer>";
    private static string PowerRow1158(string id, string name, string book = "SR5") =>
        $"<power><id>{id}</id><name>{name}</name><points>0.5</points><levels>True</levels>"
        + $"<source>{book}</source><page>1</page><!--retain--><bonus><attribute><name>BOD</name><val>1</val></attribute></bonus></power>";

    private static string AddPowerOverlay1158(Fixture fixture, bool replace)
    {
        string data = Path.Combine(fixture.Root, "overlay", "data");
        Directory.CreateDirectory(data);
        fixture.Catalog.Value = fixture.Catalog.Value with
        {
            Overlays = [new("powers", "Powers", Path.GetDirectoryName(data)!, data, string.Empty, 1, true,
                replace ? ContentOverlayModes.ReplaceFile : ContentOverlayModes.MergeCatalog, "fixture")]
        };
        return Path.Combine(data, replace ? "powers.xml" : "powers.fragment.xml");
    }
}
