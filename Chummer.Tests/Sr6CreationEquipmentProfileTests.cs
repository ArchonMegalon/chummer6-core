using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("actioneer-suit", 2, 6)]
    [DataRow("chameleon-suit", 2, 4)]
    [DataRow("full-body-armor", 5, 10)]
    [DataRow("lined-coat", 3, 7)]
    [DataRow("synthleather-jacket", 1, 3)]
    [DataRow("armor-jacket", 4, 8)]
    [DataRow("armor-clothing", 2, 4)]
    [DataRow("armor-vest", 3, 6)]
    [DataRow("urban-explorer", 3, 6)]
    public void Equipment_profiles_retain_source_armor_without_equipping_or_changing_purchase_bytes(string catalogId, int defense, int capacity)
    {
        using var fixture = new Fixture();
        var choice = new Sr6CreationGearChoice(Guid.NewGuid(), catalogId, 2);
        var selection = KarmaSeed("Priority") with { Gear = new([choice]) };
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var stored = fixture.Store.Get(fixture.Id).Value!;
        string bytes = Sr6CreationFoundationIntegrity.Digest(stored);
        var state = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner).Load(fixture.Stamp, fixture.Id).Value!;
        var profile = state.DraftSummary!.Equipment.Single();
        Assert.AreEqual(choice.Id, profile.ItemId);
        Assert.AreEqual(2, profile.Quantity);
        Assert.IsTrue(profile.StatisticsAvailable);
        Assert.AreEqual(new Sr6CreationArmorProfile(defense, capacity), profile.Armor);
        Assert.IsNull(profile.Matrix);
        Assert.AreEqual(Sr6CreationFoundationRules.CoreSourceSha256, profile.SourceSha256);
        CollectionAssert.Contains(profile.SourceAnchorIds.ToArray(), "sr6_core_de_2024:p266");
        Assert.IsTrue(profile.Traits.Any(row => row.Id == "armor-does-not-stack"));
        if (catalogId == "chameleon-suit")
        {
            Assert.AreEqual(1, profile.Traits.Single(row => row.Id == "active-suit-stealth-edge").Value);
            Assert.AreEqual(2, profile.Traits.Single(row => row.Id == "wireless-suit-defense-bonus").Value);
        }
        if (catalogId == "urban-explorer") Assert.IsTrue(profile.Traits.Any(row => row.Id == "included-biomonitor"));
        var projection = fixture.Service.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!;
        var root = XDocument.Parse(projection.Document.Content).Root!;
        var armor = root.Element("armors")!.Element("armor")!;
        Assert.AreEqual("false", armor.Element("equipped")!.Value);
        Assert.AreEqual(defense, (int)armor.Element("sr6equipmentprofile")!.Element("armor")!.Element("defenseratingbonus")!);
        Assert.AreEqual(1, (int)root.Element("sr6creationprojection")!.Element("derived")!.Elements("stat")
            .Single(row => row.Attribute("id")!.Value == "unarmored-defense-rating").Element("value")!);
        Assert.AreEqual(request.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));
        Assert.IsTrue(fixture.Service.Confirm(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    [DataRow("meta-link", 1, 1, 0, 0)]
    [DataRow("sony-emperor", 2, 1, 1, 1)]
    [DataRow("renraku-sensei", 3, 2, 0, 1)]
    [DataRow("erika-elite", 4, 2, 1, 2)]
    [DataRow("hermes-ikon", 5, 3, 0, 2)]
    [DataRow("transys-avalon", 6, 3, 1, 3)]
    [DataRow("erika-mcd6", 1, 4, 3, 2)]
    [DataRow("spinrad-falcon", 2, 5, 4, 4)]
    [DataRow("mct360", 3, 6, 5, 6)]
    [DataRow("renraku-kitsune", 4, 7, 6, 8)]
    [DataRow("shiawase-cyber6", 5, 8, 7, 10)]
    [DataRow("fairlight-excalibur", 6, 9, 8, 12)]
    [DataRow("rcc-scrap", 1, 3, 2, -1)]
    [DataRow("allegiance-control-center", 2, 3, 3, -1)]
    [DataRow("essy-dronemaster", 3, 4, 4, -1)]
    [DataRow("horizon-overseer", 4, 5, 4, -1)]
    [DataRow("maersk-spider", 4, 4, 5, -1)]
    [DataRow("vulcan-liegelord", 5, 6, 5, -1)]
    [DataRow("proteus-poseidon", 5, 5, 6, -1)]
    [DataRow("transys-eidolon", 6, 6, 5, -1)]
    public void Equipment_profiles_preserve_matrix_attribute_pairs_zero_and_absent_values(string id, int rating, int first, int second, int programs)
    {
        using var fixture = new Fixture();
        var selection = KarmaSeed("Priority") with { Assignments = [new("heritage", "D"), new("talent", "E"),
            new("attributes", "B"), new("skills", "C"), new("resources", "A")],
            Gear = new([new(Guid.NewGuid(), id, 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var state = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!;
        var profile = state.DraftSummary!.Equipment.Single();
        var device = profile.Matrix!;
        Assert.IsTrue(profile.StatisticsAvailable);
        Assert.IsNull(profile.Armor);
        Assert.AreEqual(rating, device.DeviceRating);
        bool deck = state.Selection!.Gear!.Items.Single().Option.CategoryId == "cyberdeck";
        Assert.AreEqual(first, deck ? device.Attack : device.DataProcessing);
        Assert.AreEqual(second, deck ? device.Sleaze : device.Firewall);
        if (deck) { Assert.IsNull(device.DataProcessing); Assert.IsNull(device.Firewall); }
        else { Assert.IsNull(device.Attack); Assert.IsNull(device.Sleaze); }
        Assert.AreEqual(programs < 0 ? (int?)null : programs, device.ActiveProgramSlots);
        Assert.AreEqual(programs < 0 ? rating * 3 : deck ? (int?)null : first, device.MaximumSlaves);
        Assert.AreEqual(programs < 0 ? first : (int?)null, device.SharedProgramSlots);
        Assert.AreEqual(programs < 0 ? rating : (int?)null, device.NoiseReduction);
        var projection = fixture.Service.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!;
        CollectionAssert.DoesNotContain(projection.IncompleteDomains.ToArray(), "equipment-runtime-stats");
        var xml = XDocument.Parse(projection.Document.Content).Descendants("sr6equipmentprofile").Single().Element("matrix")!;
        Assert.AreEqual(second, (int)xml.Element(deck ? "sleaze" : "firewall")!);
        Assert.IsNull(xml.Element(deck ? "firewall" : "attack"), "Absent device attributes must not become zero.");
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Equipment_profiles_finalize_with_supported_basket_and_survive_cold_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var selection = KarmaSeed(method) with { Karma = new([], [], 2), Knowledge = new("German", [], []),
            Contacts = new([]), Gear = new([new(Guid.NewGuid(), "lined-coat", 1), new(Guid.NewGuid(), "meta-link", 1)]),
            Lifestyle = new("street", 1) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding).Value!;
        Assert.IsTrue(review.CanFinalize, string.Join(",", review.Blockers));
        var result = fixture.Service.ConfirmFinalization(fixture.Stamp, new(review.Binding, review.ReviewDigest, Guid.NewGuid(), true, true));
        Assert.IsNotNull(result.Value);
        var cold = new FileWorkspaceStore(fixture.Directory);
        var saved = cold.Get(fixture.Id).Value!;
        Assert.AreEqual(review.Document.Content, saved.Document.Content);
        Assert.AreEqual(2, XDocument.Parse(saved.Document.Content).Descendants("sr6equipmentprofile").Count());
        Assert.AreEqual(result.Value.Receipt, new Sr6CreationFoundationService(cold, fixture.Owner).LoadFinalization(fixture.Stamp, fixture.Id).Value);
    }

    [TestMethod]
    public void Equipment_profiles_do_not_bless_unsupported_or_unavailable_items()
    {
        using var fixture = new Fixture();
        var selection = KarmaSeed("Priority") with { Gear = new([new(Guid.NewGuid(), "lined-coat", 1),
            new(Guid.NewGuid(), "combat-knife", 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var summary = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!;
        Assert.IsFalse(summary.Equipment.Single(row => row.CatalogId == "combat-knife").StatisticsAvailable);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding).Value!;
        CollectionAssert.Contains(review.Blockers.ToArray(), "equipment-runtime-stats");
        foreach (string id in new[] { "ares-red-dog", "aztechnology-tlaloc" })
            CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                selection with { Gear = new([new(Guid.NewGuid(), id, 1)]) }).Blockers.ToArray(), Sr6CreationGearBlockers.AvailabilityExceeded);
    }
}
