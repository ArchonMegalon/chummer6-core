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
    public void Weapon_profiles_cover_each_existing_catalog_purchase_without_rewriting_its_ledger()
    {
        var catalog = Sr6CreationGearRules.Catalog("human").Where(row => row.CategoryId is "melee" or "projectile" or "firearm" or "launcher").ToArray();
        Assert.AreEqual(82, catalog.Length);
        foreach (var option in catalog)
        {
            using var fixture = new Fixture();
            var selection = KarmaSeed("Priority") with
            {
                Assignments = [new("heritage", "D"), new("talent", "E"), new("attributes", "B"), new("skills", "C"), new("resources", "A")],
                Gear = new([new(Guid.NewGuid(), option.Id, 2)])
            };
            var request = fixture.Request(selection);
            Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value, option.Id);
            string before = Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!);
            var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
            var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
            var profile = state.DraftSummary!.Equipment.Single();
            Assert.IsTrue(profile.StatisticsAvailable, option.Id);
            Assert.AreEqual(2, profile.Quantity);
            Assert.IsNotNull(profile.Weapon, option.Id);
            Assert.IsNull(profile.Armor);
            Assert.IsNull(profile.Matrix);
            var projection = cold.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!;
            CollectionAssert.DoesNotContain(projection.IncompleteDomains.ToArray(), "equipment-runtime-stats", option.Id);
            var root = XDocument.Parse(projection.Document.Content).Root!;
            var weapon = root.Element("weapons")!.Element("weapon")!;
            Assert.AreEqual("false", weapon.Element("equipped")!.Value);
            var xml = weapon.Element("sr6equipmentprofile")!.Element("weapon")!;
            Assert.AreEqual("false", xml.Element("ammunitionincluded")!.Value);
            Assert.AreEqual(profile.Weapon.Attacks.Count, xml.Elements("attack").Count());
            foreach (var attack in profile.Weapon.Attacks)
            {
                Assert.IsTrue(profile.SourceAnchorIds.Contains(attack.SourceAnchorId));
                Assert.AreEqual(5, xml.Elements("attack").Single(row => row.Attribute("id")!.Value == attack.Id)
                    .Element("attackratings")!.Elements("range").Count());
                Assert.IsTrue(attack.Magazines.All(row => row.Capacity > 0));
                if (attack.SkillId == "ExoticWeapons") Assert.IsNotNull(attack.RequiredWeaponSpecialization);
            }
            Assert.AreEqual(before, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!), option.Id);
            Assert.AreEqual(request.PreviewDigest, state.Selection!.PreviewDigest);
            Assert.IsTrue(fixture.Service.Confirm(fixture.Stamp, request).Value!.Replayed);
        }
    }

    [TestMethod]
    public void Weapon_profiles_keep_melee_and_thrown_attacks_and_whip_attribute_separate()
    {
        using var fixture = new Fixture();
        var selection = KarmaSeed("Priority") with { Gear = new([new(Guid.NewGuid(), "combat-knife", 1),
            new(Guid.NewGuid(), "bullwhip", 1), new(Guid.NewGuid(), "shock-gloves", 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var profiles = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.Equipment;
        var knife = profiles.Single(row => row.CatalogId == "combat-knife").Weapon!;
        Assert.AreEqual("Strength", knife.Attacks[0].AttackRatingAttribute);
        Assert.AreEqual(3, knife.Attacks[0].DamageValue);
        Assert.IsNull(knife.Attacks[0].AttackRatings.Near);
        Assert.AreEqual("Athletics", knife.Attacks[1].SkillId);
        Assert.IsNull(knife.Attacks[1].AttackRatingAttribute);
        Assert.AreEqual(new Sr6CreationWeaponAttackRatings(8, 2, null, null, null), knife.Attacks[1].AttackRatings);
        Assert.AreEqual(20, knife.Attacks[1].MaximumRangeMeters);
        var whip = profiles.Single(row => row.CatalogId == "bullwhip").Weapon!.Attacks.Single();
        Assert.AreEqual("Reaction", whip.AttackRatingAttribute);
        Assert.AreEqual("ExoticWeapons", whip.SkillId);
        Assert.AreEqual(1, whip.DamageValue);
        var shock = profiles.Single(row => row.CatalogId == "shock-gloves");
        Assert.IsTrue(shock.Weapon!.Attacks.Single().Electrical);
        Assert.AreEqual("stun", shock.Weapon.Attacks.Single().DamageKind);
        Assert.AreEqual(10, shock.Traits.Single(row => row.Id == "charge-capacity").Value);
    }

    [TestMethod]
    public void Weapon_profiles_keep_underbarrels_payloads_feeds_and_accessory_conditions_separate()
    {
        using var fixture = new Fixture();
        string[] ids = ["yamaha-raiden", "ares-alpha", "mossberg-cmdt", "ingram-valiant", "ares-super-squirt",
            "parashield-dart-pistol", "ares-antioch-ii", "ares-light-fire-75", "rpk-hmg"];
        var selection = KarmaSeed("Priority") with { Gear = new(ids.Select(id => new Sr6CreationGearChoice(Guid.NewGuid(), id, 1)).ToArray()) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var profiles = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.Equipment;
        Sr6CreationWeaponProfile Weapon(string id) => profiles.Single(row => row.CatalogId == id).Weapon!;
        var raiden = Weapon("yamaha-raiden");
        Assert.AreEqual(3, raiden.Attacks.Count);
        Assert.AreEqual(new Sr6CreationWeaponAttackRatings(4, 11, 7, 1, null), raiden.Attacks[1].AttackRatings);
        Assert.AreEqual("grenade", raiden.Attacks[1].PayloadKind);
        Assert.IsNull(raiden.Attacks[1].DamageValue);
        Assert.AreEqual(4, raiden.Attacks[2].DamageValue);
        Assert.AreEqual(new Sr6CreationWeaponMagazine(2, "break-action"), raiden.Attacks[2].Magazines.Single());
        Assert.AreEqual(2, Weapon("ares-alpha").Attacks.Count);
        CollectionAssert.AreEqual(new[] { new Sr6CreationWeaponMagazine(10, "clip"), new Sr6CreationWeaponMagazine(24, "drum") },
            Weapon("mossberg-cmdt").Attacks.Single().Magazines.ToArray());
        Assert.AreEqual(new Sr6CreationWeaponMagazine(100, "belt"), Weapon("ingram-valiant").Attacks.Single().Magazines[1]);
        Assert.AreEqual(0, Weapon("ares-super-squirt").Attacks.Single().DamageValue);
        Assert.AreEqual("contact-toxin", Weapon("ares-super-squirt").Attacks.Single().PayloadKind);
        Assert.AreEqual(1, Weapon("parashield-dart-pistol").Attacks.Single().DamageValue);
        Assert.AreEqual("injection-toxin", Weapon("parashield-dart-pistol").Attacks.Single().PayloadKind);
        Assert.IsNull(Weapon("ares-antioch-ii").Attacks.Single().AttackRatings.Close);
        Assert.AreEqual(5, Weapon("rpk-hmg").MinimumCarryStrength);
        Assert.AreEqual(10, Weapon("ares-light-fire-75").Attacks.Single().AttackRatings.Close, "Do not activate the extra smartgun point.");
        Assert.AreEqual(1, profiles.Single(row => row.CatalogId == "ares-light-fire-75").Traits.Single(row => row.Id == "active-smartgun-additional-rating").Value);
        CollectionAssert.Contains(raiden.IncludedAccessoryIds.ToArray(), "smartgun");
        var xml = XDocument.Parse(fixture.Service.ProjectCharacter(fixture.Stamp, fixture.Binding).Value!.Document.Content);
        Assert.AreEqual(ids.Length, xml.Root!.Element("weapons")!.Elements("weapon").Count(), "Underbarrels are not separate purchases.");
        Assert.IsFalse(xml.Descendants("range").Where(row => row.Attribute("available")!.Value == "false").Any(row => row.Attribute("base") is not null));
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Weapon_profiles_finalize_once_and_preserve_exact_character_on_cold_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var selection = KarmaSeed(method) with { Karma = new([], [], 3), Knowledge = new("German", [], []), Contacts = new([]),
            Gear = new([new(Guid.NewGuid(), "lined-coat", 1), new(Guid.NewGuid(), "combat-knife", 1), new(Guid.NewGuid(), "yamaha-raiden", 1)]),
            Lifestyle = new("street", 1) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(selection)).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding).Value!;
        Assert.IsTrue(review.CanFinalize, string.Join(",", review.Blockers));
        var request = new Sr6CreationFinalizationRequest(review.Binding, review.ReviewDigest, Guid.NewGuid(), true, true);
        var result = fixture.Service.ConfirmFinalization(fixture.Stamp, request);
        Assert.IsNotNull(result.Value);
        var coldStore = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(coldStore, fixture.Owner);
        Assert.AreEqual(review.Document.Content, coldStore.Get(fixture.Id).Value!.Document.Content);
        Assert.AreEqual(result.Value.Receipt, cold.LoadFinalization(fixture.Stamp, fixture.Id).Value);
        Assert.IsNotNull(coldStore.Get(fixture.Id).Value!.Document.AuxiliaryState.Sr6CreationFinalizationArchive);
        Assert.IsTrue(cold.ConfirmFinalization(fixture.Stamp, request).Value!.Replayed);
    }
}
