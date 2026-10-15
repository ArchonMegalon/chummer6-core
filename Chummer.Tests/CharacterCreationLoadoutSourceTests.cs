using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Files;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ReadyContext = Chummer.Tests.CharacterCreationFinalizationServiceTests.ReadyContext;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationLoadoutSourceTests
{
    private const string WeaponId = "weapon:676660a2-3320-4b4a-acaf-f645f2ba6cd7";
    private const string ArmorId = "armor:36a4cd30-c32c-44d0-847a-0c15fb51072a";

    [TestMethod]
    public void Effective_real_catalog_admits_plain_firearm_and_armor_with_distinct_source_types()
    {
        using var context = ReadyContext.Create(true);
        var source = context.Resolver.TryCreateContext(context.Store.Get(context.WorkspaceId).Value!.Document.Content)!;
        Assert.IsTrue(source.TryResolveCreationLoadoutAuthority(out var authority));
        Assert.IsTrue(CharacterCreationLoadoutRules.IsValidAuthority(authority));
        var weapon = authority.Options.Single(option => option.OptionId == WeaponId);
        var armor = authority.Options.Single(option => option.OptionId == ArmorId);
        Assert.IsTrue(weapon.IsSelectable, string.Join(',', weapon.Blockers));
        Assert.IsTrue(armor.IsSelectable, string.Join(',', armor.Blockers));
        Assert.AreEqual(320m, weapon.UnitCost);
        Assert.AreEqual(1000m, armor.UnitCost);
        Assert.AreEqual("gun", (string?)XElement.Parse(weapon.SourceCategoryXml).Attribute("type"));
        Assert.IsTrue(authority.Options.Any(option => !option.IsSelectable
            && option.Blockers.Contains(CharacterCreationLoadoutBlockers.UnsupportedSemantics)));
    }

    [TestMethod]
    public void Source_bound_quote_materializes_real_unloaded_weapon_and_armor_graphs_deterministically()
    {
        var authority = Authority();
        var selections = new CharacterCreationLoadoutSelections([new(WeaponId, 2)], [new(ArmorId)]);
        Assert.IsTrue(CharacterCreationLoadoutRules.TryProjectBasket(selections, authority, out var lines,
            out decimal cost, out var blockers), string.Join(',', blockers));
        Assert.AreEqual(1640m, cost);
        string review = CharacterCreationLoadoutRules.Compute("exact-review");
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines, review, out var weapons, out var armor));
        Assert.AreEqual(2, weapons.Length);
        Assert.AreEqual(1, armor.Length);
        Assert.AreEqual("weapon", weapons[0].Name.LocalName);
        Assert.AreEqual("7P", weapons[0].Element("damage")!.Value);
        Assert.AreEqual("-", weapons[0].Element("ap")!.Value);
        Assert.AreEqual("SA", weapons[0].Element("mode")!.Value);
        Assert.AreEqual("11(c)", weapons[0].Element("ammo")!.Value);
        Assert.AreEqual("gun", weapons[0].Element("weapontype")!.Value);
        Assert.AreEqual("Barrel/Top", weapons[0].Element("weaponslots")!.Value);
        Assert.IsNull(weapons[0].Element("clips"), "Buying a firearm must not claim that ammunition was purchased or loaded.");
        Assert.AreEqual("armor", armor[0].Name.LocalName);
        Assert.AreEqual("12", armor[0].Element("armor")!.Value);
        Assert.AreEqual("12", armor[0].Element("armorcapacity")!.Value);
        Assert.IsTrue(weapons.Concat(armor).All(node => node.Element("qty") is null && node.Element("gears") is null));
        Assert.AreEqual(3, weapons.Concat(armor).Select(node => node.Element("guid")!.Value).Distinct().Count());
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines, review, out var coldWeapons, out var coldArmor));
        Assert.AreEqual(string.Concat(weapons.Select(node => node.ToString())), string.Concat(coldWeapons.Select(node => node.ToString())));
        Assert.AreEqual(armor[0].ToString(), coldArmor[0].ToString());
    }

    [TestMethod]
    [DataRow(null, "False")]
    [DataRow("False", "False")]
    [DataRow("True", "True")]
    public void Source_stolen_flag_keeps_armor_creation_default_without_changing_weapon_semantics(
        string? sourceFlag, string expectedWeaponFlag)
    {
        var authority = Authority(sourceFlag);
        Assert.IsTrue(authority.Options.All(option => option.IsSelectable),
            "Armor must remain available; an ignored source flag is not an unsupported purchase choice.");
        var selections = new CharacterCreationLoadoutSelections([new(WeaponId)], [new(ArmorId)]);
        Assert.IsTrue(CharacterCreationLoadoutRules.TryProjectBasket(selections, authority, out var lines,
            out decimal cost, out var blockers), string.Join(',', blockers));
        Assert.AreEqual(1320m, cost);
        string review = CharacterCreationLoadoutRules.Compute("source-stolen-creation-parity");
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryBuildGraph(lines, review, out var weapons, out var armor));
        Assert.AreEqual(expectedWeaponFlag, weapons.Single().Element("stolen")!.Value);
        Assert.AreEqual("False", armor.Single().Element("stolen")!.Value,
            "Armor.Create ignores source stolen and begins with ordinary, non-stolen ownership.");

        Assert.IsTrue(CharacterCreationLoadoutRules.TryCaptureSelectedAuthority(authority,
            CharacterCreationLoadoutSelections.Empty, selections, out var witness));
        var cold = JsonSerializer.Deserialize<CharacterCreationSelectedLoadoutAuthority>(JsonSerializer.Serialize(witness))!;
        Assert.IsTrue(CharacterCreationLoadoutRules.TryProjectSelectedBasket(selections, cold,
            out var coldLines, out decimal coldCost, out _));
        Assert.AreEqual(cost, coldCost);
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryBuildGraph(coldLines, review,
            out var coldWeapons, out var coldArmor));
        Assert.AreEqual(weapons.Single().ToString(), coldWeapons.Single().ToString());
        Assert.AreEqual(armor.Single().ToString(), coldArmor.Single().ToString());
    }

    [TestMethod]
    public void Unsupported_source_children_expressions_category_semantics_and_disabled_books_are_not_buyable()
    {
        foreach (XElement unsupported in new[]
                 {
                     new XElement("accessories", new XElement("accessory", new XElement("name", "Laser Sight"))),
                     new XElement("required", new XElement("quality", "Restricted Gear")),
                     new XElement("bonus", new XElement("skill", "Pistols")),
                     new XElement("selecttext"), new XElement("unknownmechanic", "1")
                 })
        {
            XElement row = Weapon(); row.Add(unsupported);
            Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Weapon,
                row, WeaponCategory(), true, 12, out var option));
            Assert.IsFalse(option!.IsSelectable, unsupported.Name.LocalName);
        }
        foreach (string price in new[] { "Variable(100-500)", "Rating * 100", "-1", "1,000" })
        {
            XElement row = Weapon(); row.SetElementValue("cost", price);
            Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Weapon,
                row, WeaponCategory(), true, 12, out var option));
            Assert.IsFalse(option!.IsSelectable, price);
        }
        var category = WeaponCategory(); category.SetAttributeValue("type", "energy");
        Assert.IsFalse(CharacterCreationLoadoutRules.IsSourceProjectable(CharacterCreationLoadoutKinds.Weapon,
            Weapon().ToString(), category.ToString()));
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Weapon,
            Weapon(), WeaponCategory(), false, 12, out var disabled));
        Assert.IsFalse(disabled!.IsSelectable);
        CollectionAssert.Contains(disabled.Blockers.ToArray(), CharacterCreationLoadoutBlockers.SourceDisabled);
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Weapon,
            Weapon(), WeaponCategory(), true, 3, out var unavailable));
        CollectionAssert.Contains(unavailable!.Blockers.ToArray(), CharacterCreationLoadoutBlockers.AvailabilityExceeded);
    }

    [TestMethod]
    public void Rehashed_price_and_kind_tampering_do_not_substitute_for_the_captured_source()
    {
        var authority = Authority();
        var option = authority.Options.Single(item => item.Kind == CharacterCreationLoadoutKinds.Weapon) with { UnitCost = 1m };
        option = option with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(option) };
        Assert.IsFalse(CharacterCreationLoadoutRules.IsValidOption(option));
        var selections = new CharacterCreationLoadoutSelections([new(ArmorId)], []);
        Assert.IsFalse(CharacterCreationLoadoutRules.TryProjectBasket(selections, authority, out _, out _, out _));
        Assert.IsFalse(CharacterCreationLoadoutRules.TryProjectBasket(new([], [new(ArmorId, 2)]), authority, out _, out _, out _));
        Assert.IsFalse(CharacterCreationLoadoutRules.TryProjectBasket(new([new(WeaponId, 21)], []), authority, out _, out _, out _));
        Assert.IsFalse(CharacterCreationLoadoutRules.TryProjectBasket(null, authority, out _, out _, out _));
        Assert.IsFalse(CharacterCreationLoadoutRules.IsSourceProjectable(CharacterCreationLoadoutKinds.Weapon,
            "<!DOCTYPE weapon [<!ENTITY x SYSTEM 'file:///nonexistent'>]><weapon>&x;</weapon>", WeaponCategory().ToString()));
    }

    [TestMethod]
    public void Selected_history_preserves_source_categories_and_reprices_after_json_round_trip()
    {
        var authority = Authority();
        var selected = new CharacterCreationLoadoutSelections([new(WeaponId)], [new(ArmorId)]);
        Assert.IsTrue(CharacterCreationLoadoutRules.TryCaptureSelectedAuthority(authority,
            CharacterCreationLoadoutSelections.Empty, selected, out var witness));
        var cold = JsonSerializer.Deserialize<CharacterCreationSelectedLoadoutAuthority>(JsonSerializer.Serialize(witness))!;
        Assert.IsTrue(CharacterCreationLoadoutRules.IsValidSelectedAuthority(cold));
        Assert.IsTrue(CharacterCreationLoadoutRules.TryProjectSelectedBasket(selected, cold, out _, out decimal cost, out _));
        Assert.AreEqual(1320m, cost);
        var changed = cold.Options[0] with { UnitCost = cold.Options[0].UnitCost + 1m };
        changed = changed with { OptionDigest = CharacterCreationLoadoutRules.ComputeOptionDigest(changed) };
        cold = cold with { Options = [changed, cold.Options[1]] };
        cold = cold with { WitnessDigest = CharacterCreationLoadoutRules.ComputeSelectedAuthorityDigest(cold) };
        Assert.IsFalse(CharacterCreationLoadoutRules.IsValidSelectedAuthority(cold));
    }

    [TestMethod]
    [DataRow("override_weapons.xml", "Light Pistols", "type", "energy")]
    [DataRow("custom_weapons.xml", "Light Pistols", "type", "energy")]
    [DataRow("amend_weapons.xml", "Light Pistols", "type", "energy")]
    [DataRow("override_armor.xml", "Armor", "blackmarket", "Other")]
    [DataRow("custom_armor.xml", "Armor", "blackmarket", "Other")]
    [DataRow("amend_armor.xml", "Armor", "blackmarket", "Other")]
    public void Selected_custom_category_mutations_are_rejected_instead_of_using_stale_base_metadata(
        string fileName, string category, string attribute, string value)
    {
        var custom = new XElement("chummer", new XElement("categories",
            new XElement("category", new XAttribute(attribute, value), category)));
        var authority = ResolveFixture(out bool available, fileName, custom);
        Assert.IsFalse(available);
        Assert.IsFalse(authority.IsAuthoritative);
        Assert.AreEqual(0, authority.Options.Count);

        // An installed but unselected directory does not change this character's policy.
        var unselected = ResolveFixture(out available, fileName, custom, enableCustom: false);
        Assert.IsTrue(available);
        Assert.IsTrue(unselected.Options.All(option => option.IsSelectable));
    }

    [TestMethod]
    public void Empty_category_removal_is_rejected_but_row_only_custom_price_amendment_is_admitted()
    {
        ResolveFixture(out bool available, "amend_weapons.xml", new XElement("chummer",
            new XElement("categories", new XAttribute("amendoperation", "remove"))));
        Assert.IsFalse(available);

        var authority = ResolveFixture(out available, "amend_weapons.xml", new XElement("chummer",
            new XElement("weapons", new XElement("weapon", new XElement("id", WeaponId[7..]),
                new XElement("cost", "325")))));
        Assert.IsTrue(available);
        var weapon = authority.Options.Single(option => option.OptionId == WeaponId);
        Assert.IsTrue(weapon.IsSelectable);
        Assert.AreEqual(325m, weapon.UnitCost);
        Assert.AreEqual("gun", (string?)XElement.Parse(weapon.SourceCategoryXml).Attribute("type"));
    }

    [TestMethod]
    [DataRow(true, false, "4R", false)]
    [DataRow(false, true, "4F", false)]
    [DataRow(true, true, "4", true)]
    [DataRow(false, true, "4R", true)]
    [DataRow(true, false, "4F", true)]
    public void Unsupported_profile_price_multiplier_disables_only_affected_legality(
        bool multiplyRestricted, bool multiplyForbidden, string weaponAvailability, bool weaponSelectable)
    {
        var baseline = ResolveFixture(out bool baselineAvailable);
        Assert.IsTrue(baselineAvailable);
        var authority = ResolveFixture(out bool available, amendProfile: profile =>
        {
            profile.SetElementValue("multiplyrestrictedcost", multiplyRestricted);
            profile.SetElementValue("multiplyforbiddencost", multiplyForbidden);
        }, weaponAvailability: weaponAvailability);
        Assert.IsTrue(available);
        Assert.IsTrue(CharacterCreationLoadoutRules.IsValidAuthority(authority));
        Assert.AreNotEqual(baseline.RulesDigest, authority.RulesDigest);
        var weapon = authority.Options.Single(option => option.OptionId == WeaponId);
        Assert.AreEqual(weaponSelectable, weapon.IsSelectable);
        Assert.AreEqual(weaponSelectable, weapon.PricingIsExact);
        if (!weaponSelectable)
            CollectionAssert.Contains(weapon.Blockers.ToArray(), CharacterCreationLoadoutBlockers.UnsupportedSemantics);
        var armor = authority.Options.Single(option => option.OptionId == ArmorId);
        Assert.IsTrue(armor.IsSelectable);
        Assert.IsTrue(armor.PricingIsExact);
        Assert.IsTrue(CharacterCreationLoadoutRules.TryProjectBasket(new([], [new(ArmorId)]), authority,
            out _, out decimal cost, out _));
        Assert.AreEqual(1000m, cost);
    }

    private static CharacterCreationLoadoutAuthority ResolveFixture(out bool available,
        string? customFileName = null, XElement? customContent = null, bool enableCustom = true,
        Action<XElement>? amendProfile = null, string weaponAvailability = "4R")
    {
        string root = Path.Combine(Path.GetTempPath(), $"chummer-loadout-source-{Guid.NewGuid():N}");
        const string profileId = "e7aef934-912f-43d0-a555-50ebc2ffca08";
        const string customName = "Loadout Test Rules";
        Directory.CreateDirectory(Path.Combine(root, "data"));
        try
        {
            var profile = new XElement("setting", new XElement("id", profileId),
                new XElement("buildmethod", "Priority"), new XElement("availability", "12"),
                new XElement("multiplyrestrictedcost", "False"), new XElement("multiplyforbiddencost", "False"),
                new XElement("books", new XElement("book", "SR5")));
            var character = new XElement("character", new XElement("settings", profileId));
            if (customFileName is not null)
            {
                string customRoot = Path.Combine(root, "customdata", customName);
                Directory.CreateDirectory(customRoot);
                customContent!.Save(Path.Combine(customRoot, customFileName));
                if (enableCustom)
                {
                    profile.Add(new XElement("customdatadirectorynames", new XElement("customdatadirectoryname",
                        new XElement("directoryname", customName), new XElement("order", "0"), new XElement("enabled", "True"))));
                    character.Add(new XElement("customdatadirectorynames", new XElement("directoryname", customName)));
                }
            }
            amendProfile?.Invoke(profile);
            new XElement("chummer", new XElement("settings", profile)).Save(Path.Combine(root, "data", "settings.xml"));
            XElement weapon = Weapon(); weapon.SetElementValue("avail", weaponAvailability);
            new XElement("chummer", new XElement("categories", WeaponCategory()), new XElement("weapons", weapon))
                .Save(Path.Combine(root, "data", "weapons.xml"));
            new XElement("chummer", new XElement("categories", new XElement("category",
                    new XAttribute("blackmarket", "Armor"), "Armor")), new XElement("armors", Armor()))
                .Save(Path.Combine(root, "data", "armor.xml"));
            var resolver = new FileSystemCharacterSourceDataResolver(new FileSystemContentOverlayCatalogService(root, root, null));
            var context = resolver.TryCreateContext(character.ToString());
            Assert.IsNotNull(context);
            available = context.TryResolveCreationLoadoutAuthority(out var authority);
            return authority;
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static CharacterCreationLoadoutAuthority Authority(string? sourceStolen = null)
    {
        XElement weaponSource = Weapon();
        XElement armorSource = Armor();
        if (sourceStolen is not null)
        {
            weaponSource.SetElementValue("stolen", sourceStolen);
            armorSource.SetElementValue("stolen", sourceStolen);
        }
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Weapon,
            weaponSource, WeaponCategory(), true, 12, out var weapon));
        Assert.IsTrue(CharacterCreationLoadoutSourceProjector.TryCreateOption(CharacterCreationLoadoutKinds.Armor,
            armorSource, new XElement("category", new XAttribute("blackmarket", "Armor"), "Armor"), true, 12, out var armor));
        string digest = CharacterCreationLoadoutRules.Compute("fixture-source-profile-rules-runtime");
        var authority = new CharacterCreationLoadoutAuthority(CharacterCreationLoadoutSchemas.AuthorityV1,
            "sr5", "fixture-profile", 12, 128, 20, [weapon!, armor!], ["weapons.xml", "armor.xml"], [], true,
            digest, digest, digest, digest, string.Empty);
        return authority with { AuthorityDigest = CharacterCreationLoadoutRules.ComputeAuthorityDigest(authority) };
    }

    // Exact bounded core-rulebook rows; the first test resolves the effective repository source too.
    private static XElement Weapon() => XElement.Parse("""
        <weapon><id>676660a2-3320-4b4a-acaf-f645f2ba6cd7</id><name>Colt America L36</name>
        <category>Light Pistols</category><type>Ranged</type><conceal>-2</conceal><accuracy>7</accuracy>
        <reach>0</reach><damage>7P</damage><ap>-</ap><mode>SA</mode><rc>0</rc><ammo>11(c)</ammo>
        <avail>4R</avail><cost>320</cost><source>SR5</source><page>425</page>
        <accessorymounts><mount>Barrel</mount><mount>Top</mount></accessorymounts><spec>Semi-Automatics</spec></weapon>
        """);
    private static XElement WeaponCategory() => new("category", new XAttribute("blackmarket", "Weapons"),
        new XAttribute("type", "gun"), new XAttribute("gunneryspec", "Ballistic"), "Light Pistols");
    private static XElement Armor() => XElement.Parse("""
        <armor><id>36a4cd30-c32c-44d0-847a-0c15fb51072a</id><name>Armor Jacket</name><category>Armor</category>
        <armor>12</armor><armorcapacity>12</armorcapacity><avail>2</avail><cost>1000</cost><source>SR5</source><page>437</page></armor>
        """);
}
