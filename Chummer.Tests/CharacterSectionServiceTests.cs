using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public class CharacterSectionServiceTests
{
    private const string ResolverVehicleModId = "f89a112e-600a-4278-8731-9b14cf3737c9";

    [TestMethod]
    public void Condition_monitor_preserves_career_editability_and_filled_tracks()
    {
        const string xml = """
            <character>
              <created>True</created>
              <physicalcm>11</physicalcm>
              <physicalcmfilled>4</physicalcmfilled>
              <physicalcmoverflow>3</physicalcmoverflow>
              <physicalcmthresholdoffset>1</physicalcmthresholdoffset>
              <physicalcmnaturalrecovery>7</physicalcmnaturalrecovery>
              <stuncm>10</stuncm>
              <stuncmfilled>2</stuncmfilled>
              <stuncmthresholdoffset>0</stuncmthresholdoffset>
              <stuncmnaturalrecovery>6</stuncmnaturalrecovery>
              <physicalcmiscorecm>False</physicalcmiscorecm>
              <stuncmismatrixcm>False</stuncmismatrixcm>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterConditionMonitorSection result = service.ParseConditionMonitor(xml);

        Assert.IsTrue(result.Created);
        Assert.AreEqual(11, result.PhysicalTrack);
        Assert.AreEqual(4, result.PhysicalFilled);
        Assert.AreEqual(3, result.PhysicalOverflow);
        Assert.AreEqual(10, result.StunTrack);
        Assert.AreEqual(2, result.StunFilled);
    }

    [TestMethod]
    public void Vehicle_condition_monitor_is_exact_only_when_saved_mod_body_effects_are_known()
    {
        const string xml = """
            <character>
              <created>True</created>
              <vehicles>
                <vehicle>
                  <guid>vehicle-clear</guid><name>Roadmaster</name><category>Groundcraft</category><body>5</body>
                  <pilot>3</pilot><physicalcmfilled>3</physicalcmfilled><matrixcmfilled>2</matrixcmfilled><mods />
                </vehicle>
                <vehicle>
                  <guid>vehicle-known</guid><name>Modified Van</name><category>Groundcraft</category><body>5</body>
                  <pilot>3</pilot><physicalcmfilled>2</physicalcmfilled><matrixcmfilled>1</matrixcmfilled>
                  <mods><mod><included>False</included><equipped>True</equipped><rating>2</rating><conditionmonitor>1</conditionmonitor><bonus><body>-Rating</body><devicerating>1</devicerating><matrixcmbonus>1</matrixcmbonus></bonus></mod></mods>
                  <gears><gear><guid>vehicle-gear</guid><name>Rigger module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></gears>
                </vehicle>
                <vehicle>
                  <guid>vehicle-unknown</guid><name>Unknown Mod</name><category>Groundcraft</category><body>5</body>
                  <pilot>3</pilot><physicalcmfilled>1</physicalcmfilled><matrixcmfilled>1</matrixcmfilled>
                  <mods><mod><included>False</included><equipped>True</equipped><rating>1</rating><conditionmonitor>0</conditionmonitor></mod></mods>
                </vehicle>
                <vehicle>
                  <guid>vehicle-overflow</guid><name>Overflow Mod</name><category>Groundcraft</category><body>5</body>
                  <physicalcmfilled>1</physicalcmfilled>
                  <mods><mod><included>False</included><equipped>True</equipped><rating>1</rating><conditionmonitor>0</conditionmonitor><wirelesson>True</wirelesson><bonus><body>2147483647</body></bonus><wirelessbonus><body>2147483647</body></wirelessbonus></mod></mods>
                </vehicle>
                <vehicle>
                  <guid>vehicle-malformed-body</guid><name>Malformed Body</name><category>Groundcraft</category><body>unknown</body>
                  <physicalcmfilled>1</physicalcmfilled><mods />
                </vehicle>
                <vehicle>
                  <guid>vehicle-malformed-mod</guid><name>Malformed Mod</name><category>Groundcraft</category><body>5</body>
                  <physicalcmfilled>1</physicalcmfilled>
                  <mods><mod><included>True</included><equipped>False</equipped><conditionmonitor>unknown</conditionmonitor></mod></mods>
                </vehicle>
                <vehicle>
                  <guid>vehicle-overclocked</guid><name>Overclocked Vehicle</name><category>Groundcraft</category><body>5</body><pilot>3</pilot>
                  <matrixcmfilled>1</matrixcmfilled><overclocked>Device Rating</overclocked><mods />
                </vehicle>
              </vehicles>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterVehiclesSection result = service.ParseVehicles(xml);

        CharacterVehicleSummary clear = result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-clear");
        Assert.IsTrue(clear.CareerEditable);
        Assert.IsTrue(clear.PhysicalConditionMaximumExact);
        Assert.AreEqual(15, clear.PhysicalConditionMaximum);
        Assert.AreEqual(3, clear.PhysicalDamage);
        Assert.IsTrue(clear.MatrixConditionMaximumExact);
        Assert.AreEqual(10, clear.MatrixConditionMaximum);
        Assert.AreEqual(2, clear.MatrixDamage);
        CharacterVehicleSummary known = result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-known");
        Assert.IsTrue(known.PhysicalConditionMaximumExact);
        Assert.AreEqual(15, known.PhysicalConditionMaximum);
        Assert.IsTrue(known.MatrixConditionMaximumExact);
        Assert.AreEqual(13, known.MatrixConditionMaximum);
        CharacterVehicleSummary unknown = result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-unknown");
        Assert.IsFalse(unknown.PhysicalConditionMaximumExact);
        Assert.AreEqual(0, unknown.PhysicalConditionMaximum);
        Assert.IsFalse(unknown.MatrixConditionMaximumExact);
        Assert.AreEqual(0, unknown.MatrixConditionMaximum);
        CharacterVehicleSummary overflow = result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-overflow");
        Assert.IsFalse(overflow.PhysicalConditionMaximumExact);
        Assert.AreEqual(0, overflow.PhysicalConditionMaximum);
        Assert.IsFalse(result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-malformed-body").PhysicalConditionMaximumExact);
        Assert.IsFalse(result.Vehicles.Single(vehicle => vehicle.Guid == "vehicle-malformed-mod").PhysicalConditionMaximumExact);
        CharacterVehicleSummary inactiveOverclock = result.Vehicles.Single(
            vehicle => vehicle.Guid == "vehicle-overclocked");
        Assert.IsTrue(inactiveOverclock.MatrixConditionMaximumExact);
        Assert.AreEqual(10, inactiveOverclock.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Source_context_resolves_blank_cyberware_grade_and_source_only_vehicle_mod_bonuses()
    {
        const string xml = """
            <character>
              <created>True</created>
              <cyberwares>
                <cyberware>
                  <guid>cyber-source</guid><name>Source-backed implant</name>
                  <grade>Standard</grade><improvementsource>Cyberware</improvementsource>
                  <matrixcmfilled>1</matrixcmfilled>
                </cyberware>
              </cyberwares>
              <vehicles>
                <vehicle>
                  <guid>vehicle-source</guid><name>Source-backed van</name><category>Groundcraft</category>
                  <body>4</body><pilot>4</pilot><physicalcmfilled>1</physicalcmfilled><matrixcmfilled>1</matrixcmfilled>
                  <mods>
                    <mod>
                      <sourceid>f89a112e-600a-4278-8731-9b14cf3737c9</sourceid><name>Gyro-Stabilization</name>
                      <included>False</included><equipped>True</equipped><rating>2</rating><conditionmonitor>1</conditionmonitor>
                    </mod>
                  </mods>
                </vehicle>
              </vehicles>
            </character>
            """;
        var service = new CharacterSectionService(new FixedSourceDataResolver());

        CharacterCyberwareSummary cyberware = service.ParseCyberwares(xml).Cyberwares.Single();
        CharacterVehicleSummary vehicle = service.ParseVehicles(xml).Vehicles.Single();

        Assert.IsTrue(cyberware.MatrixConditionMaximumExact);
        Assert.AreEqual(10, cyberware.MatrixConditionMaximum);
        Assert.IsTrue(vehicle.PhysicalConditionMaximumExact);
        Assert.AreEqual(17, vehicle.PhysicalConditionMaximum);
        Assert.IsTrue(vehicle.MatrixConditionMaximumExact);
        Assert.AreEqual(14, vehicle.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Gear_matrix_condition_monitor_flattens_stable_children_and_ignores_inactive_overclocking()
    {
        const string xml = """
            <character>
              <created>True</created>
              <gears>
                <gear>
                  <guid>gear-root</guid><name>Cyberdeck</name><rating>3</rating><devicerating>Rating</devicerating>
                  <matrixcmfilled>2</matrixcmfilled><matrixcmbonus>1</matrixcmbonus><equipped>True</equipped>
                  <children>
                    <gear>
                      <guid>gear-child</guid><name>Module</name><rating>1</rating><devicerating>2</devicerating>
                      <matrixcmfilled>1</matrixcmfilled><matrixcmbonus>2</matrixcmbonus><equipped>True</equipped>
                      <children><gear><guid>gear-grandchild</guid><name>Chip</name><matrixcmbonus>1</matrixcmbonus><equipped>True</equipped><children /></gear></children>
                    </gear>
                  </children>
                </gear>
                <gear>
                  <guid>gear-overclocked</guid><name>Overclocked Deck</name><rating>2</rating><devicerating>2</devicerating>
                  <matrixcmfilled>1</matrixcmfilled><matrixcmbonus>0</matrixcmbonus><overclocked>Device Rating</overclocked><children />
                </gear>
              </gears>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSection result = service.ParseGear(xml);

        Assert.HasCount(4, result.Gear);
        CharacterGearSummary root = result.Gear.Single(item => item.Guid == "gear-root");
        Assert.IsTrue(root.CareerEditable);
        Assert.AreEqual(14, root.MatrixConditionMaximum);
        Assert.AreEqual(2, root.MatrixDamage);
        Assert.AreEqual(1, root.ChildCount);
        CharacterGearSummary child = result.Gear.Single(item => item.Guid == "gear-child");
        Assert.AreEqual("gear-root", child.ParentGuid);
        Assert.AreEqual(1, child.Depth);
        Assert.AreEqual("Cyberdeck / Module", child.HierarchyPath);
        Assert.AreEqual(12, child.MatrixConditionMaximum);
        CharacterGearSummary grandchild = result.Gear.Single(item => item.Guid == "gear-grandchild");
        Assert.AreEqual("gear-child", grandchild.ParentGuid);
        Assert.AreEqual(9, grandchild.MatrixConditionMaximum);
        CharacterGearSummary inactiveOverclock = result.Gear.Single(item => item.Guid == "gear-overclocked");
        Assert.IsTrue(inactiveOverclock.MatrixConditionMaximumExact);
        Assert.AreEqual(9, inactiveOverclock.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Armor_matrix_condition_monitor_uses_saved_device_and_equipped_child_gear_bonuses()
    {
        const string xml = """
            <character>
              <created>True</created>
              <armors>
                <armor>
                  <guid>armor-exact</guid><name>Wireless armor</name><devicerating>3</devicerating>
                  <matrixcmfilled>2</matrixcmfilled><matrixcmbonus>1</matrixcmbonus>
                  <gears><gear><guid>armor-gear</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></gears>
                </armor>
                <armor><guid>armor-default</guid><name>Default device</name><matrixcmfilled>1</matrixcmfilled><matrixcmbonus>0</matrixcmbonus><children /></armor>
                <armor><guid>armor-overclocked</guid><name>Unproven overclock</name><devicerating>3</devicerating><matrixcmfilled>1</matrixcmfilled><matrixcmbonus>0</matrixcmbonus><overclocked>Device Rating</overclocked><children /></armor>
              </armors>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterArmorsSection result = service.ParseArmors(xml);

        CharacterArmorSummary exact = result.Armors.Single(item => item.Guid == "armor-exact");
        Assert.IsTrue(exact.CareerEditable);
        Assert.AreEqual(2, exact.MatrixDamage);
        Assert.IsTrue(exact.MatrixConditionMaximumExact);
        Assert.AreEqual(13, exact.MatrixConditionMaximum);
        Assert.AreEqual(9, result.Armors.Single(item => item.Guid == "armor-default").MatrixConditionMaximum);
        CharacterArmorSummary inactiveOverclock = result.Armors.Single(
            item => item.Guid == "armor-overclocked");
        Assert.IsTrue(inactiveOverclock.MatrixConditionMaximumExact);
        Assert.AreEqual(10, inactiveOverclock.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Weapon_matrix_condition_monitor_uses_saved_rating_and_fails_closed_for_attribute_overrides()
    {
        const string xml = """
            <character>
              <created>True</created>
              <weapons>
                <weapon><guid>weapon-exact</guid><name>Smartgun</name><rating>3</rating><devicerating>{Rating}</devicerating><matrixcmfilled>2</matrixcmfilled></weapon>
                <weapon><guid>weapon-default</guid><name>Default device</name><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-overclocked</guid><name>Unproven overclock</name><devicerating>3</devicerating><matrixcmfilled>1</matrixcmfilled><overclocked>Device Rating</overclocked></weapon>
                <weapon><guid>weapon-parented</guid><name>Gear-created weapon</name><devicerating>3</devicerating><matrixcmfilled>1</matrixcmfilled><parentid>gear-parent</parentid></weapon>
                <weapon><guid>weapon-expression</guid><name>Complex device</name><devicerating>Rating + 1</devicerating><matrixcmfilled>1</matrixcmfilled></weapon>
              </weapons>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterWeaponsSection result = service.ParseWeapons(xml);

        CharacterWeaponSummary exact = result.Weapons.Single(item => item.Guid == "weapon-exact");
        Assert.IsTrue(exact.CareerEditable);
        Assert.AreEqual(2, exact.MatrixDamage);
        Assert.IsTrue(exact.MatrixConditionMaximumExact);
        Assert.AreEqual(10, exact.MatrixConditionMaximum);
        Assert.AreEqual(9, result.Weapons.Single(item => item.Guid == "weapon-default").MatrixConditionMaximum);
        CharacterWeaponSummary inactiveOverclock = result.Weapons.Single(
            item => item.Guid == "weapon-overclocked");
        Assert.IsTrue(inactiveOverclock.MatrixConditionMaximumExact);
        Assert.AreEqual(10, inactiveOverclock.MatrixConditionMaximum);
        CharacterWeaponSummary staleParent = result.Weapons.Single(item => item.Guid == "weapon-parented");
        Assert.IsTrue(staleParent.MatrixConditionMaximumExact);
        Assert.AreEqual(10, staleParent.MatrixConditionMaximum);
        CharacterWeaponSummary expression = result.Weapons.Single(item => item.Guid == "weapon-expression");
        Assert.IsTrue(expression.MatrixConditionMaximumExact);
        Assert.AreEqual(9, expression.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Weapon_matrix_condition_monitor_delegates_to_exact_saved_parent_owner()
    {
        const string xml = """
            <character>
              <created>True</created>
              <gears>
                <gear><guid>gear-parent</guid><name>Deck</name><devicerating>4</devicerating><matrixcmfilled>5</matrixcmfilled><matrixcmbonus>1</matrixcmbonus><children><gear><guid>gear-module</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></children></gear>
                <gear><guid>duplicate-parent</guid><name>Duplicate one</name><devicerating>2</devicerating><matrixcmfilled>2</matrixcmfilled><children /></gear>
                <gear><guid>duplicate-parent</guid><name>Duplicate two</name><devicerating>3</devicerating><matrixcmfilled>3</matrixcmfilled><children /></gear>
              </gears>
              <armors>
                <armor><guid>armor-parent</guid><name>Armor deck</name><devicerating>3</devicerating><matrixcmfilled>4</matrixcmfilled><matrixcmbonus>1</matrixcmbonus><gears><gear><guid>armor-module</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></gears></armor>
              </armors>
              <cyberwares>
                <cyberware><guid>cyber-parent</guid><name>Implanted deck</name><devicerating>4</devicerating><matrixcmfilled>3</matrixcmfilled><gears><gear><guid>cyber-module</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></gears></cyberware>
              </cyberwares>
              <vehicles>
                <vehicle><guid>vehicle-parent</guid><name>Drone</name><pilot>4</pilot><matrixcmfilled>2</matrixcmfilled><mods><mod><guid>vehicle-module</guid><name>Matrix module</name><bonus><devicerating>2</devicerating><matrixcmbonus>1</matrixcmbonus></bonus></mod></mods></vehicle>
              </vehicles>
              <weapons>
                <weapon><guid>weapon-gear</guid><name>Gear child</name><parentid>gear-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-armor</guid><name>Armor child</name><parentid>armor-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-cyber</guid><name>Cyber child</name><parentid>cyber-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-vehicle</guid><name>Vehicle child</name><parentid>vehicle-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-parent</guid><name>Weapon parent</name><devicerating>6</devicerating><matrixcmfilled>6</matrixcmfilled></weapon>
                <weapon><guid>weapon-chain</guid><name>Weapon child</name><parentid>weapon-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-duplicate</guid><name>Ambiguous child</name><parentid>duplicate-parent</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-cycle-a</guid><name>Cycle A</name><parentid>weapon-cycle-b</parentid><matrixcmfilled>1</matrixcmfilled></weapon>
                <weapon><guid>weapon-cycle-b</guid><name>Cycle B</name><parentid>weapon-cycle-a</parentid><matrixcmfilled>2</matrixcmfilled></weapon>
              </weapons>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterWeaponsSection result = service.ParseWeapons(xml);

        AssertWeaponMatrix(result, "weapon-gear", damage: 5, maximum: 13);
        AssertWeaponMatrix(result, "weapon-armor", damage: 4, maximum: 13);
        AssertWeaponMatrix(result, "weapon-cyber", damage: 3, maximum: 12);
        AssertWeaponMatrix(result, "weapon-vehicle", damage: 2, maximum: 12);
        AssertWeaponMatrix(result, "weapon-chain", damage: 6, maximum: 11);
        Assert.IsFalse(result.Weapons.Single(item => item.Guid == "weapon-duplicate").MatrixConditionMaximumExact);
        Assert.IsFalse(result.Weapons.Single(item => item.Guid == "weapon-cycle-a").MatrixConditionMaximumExact);
        Assert.IsFalse(result.Weapons.Single(item => item.Guid == "weapon-cycle-b").MatrixConditionMaximumExact);

        static void AssertWeaponMatrix(
            CharacterWeaponsSection section,
            string guid,
            int damage,
            int maximum)
        {
            CharacterWeaponSummary weapon = section.Weapons.Single(item => item.Guid == guid);
            Assert.IsTrue(weapon.MatrixConditionMaximumExact);
            Assert.AreEqual(damage, weapon.MatrixDamage);
            Assert.AreEqual(maximum, weapon.MatrixConditionMaximum);
        }
    }

    [TestMethod]
    public void Cyberware_matrix_condition_monitor_uses_explicit_device_and_recursive_gear_bonuses()
    {
        const string xml = """
            <character>
              <created>True</created>
              <cyberwares>
                <cyberware>
                  <guid>cyber-root</guid><name>Implanted deck</name><rating>3</rating><devicerating>Rating</devicerating>
                  <matrixcmfilled>2</matrixcmfilled><matrixcmbonus>99</matrixcmbonus>
                  <gears><gear><guid>cyber-gear</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></gears>
                  <children>
                    <cyberware>
                      <guid>cyber-child</guid><name>Plugin</name><devicerating>2</devicerating><matrixcmfilled>1</matrixcmfilled>
                      <gears><gear><guid>child-gear</guid><name>Chip</name><equipped>True</equipped><matrixcmbonus>1</matrixcmbonus><children /></gear></gears>
                    </cyberware>
                  </children>
                </cyberware>
                <cyberware><guid>cyber-grade</guid><name>Grade fallback</name><grade>Standard</grade><matrixcmfilled>1</matrixcmfilled></cyberware>
                <cyberware><guid>cyber-overclocked</guid><name>Unproven overclock</name><devicerating>3</devicerating><matrixcmfilled>1</matrixcmfilled><overclocked>Device Rating</overclocked></cyberware>
              </cyberwares>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterCyberwaresSection result = service.ParseCyberwares(xml);

        CharacterCyberwareSummary root = result.Cyberwares.Single(item => item.Guid == "cyber-root");
        Assert.IsTrue(root.CareerEditable);
        Assert.AreEqual(2, root.MatrixDamage);
        Assert.IsTrue(root.MatrixConditionMaximumExact);
        Assert.AreEqual(13, root.MatrixConditionMaximum);
        CharacterCyberwareSummary child = result.Cyberwares.Single(item => item.Guid == "cyber-child");
        Assert.AreEqual("cyber-root", child.ParentGuid);
        Assert.AreEqual(10, child.MatrixConditionMaximum);
        Assert.IsFalse(result.Cyberwares.Single(item => item.Guid == "cyber-grade").MatrixConditionMaximumExact);
        CharacterCyberwareSummary inactiveOverclock = result.Cyberwares.Single(
            item => item.Guid == "cyber-overclocked");
        Assert.IsTrue(inactiveOverclock.MatrixConditionMaximumExact);
        Assert.AreEqual(10, inactiveOverclock.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Matrix_condition_monitors_apply_saved_active_career_overclocker_improvement()
    {
        const string xml = """
            <character>
              <created>True</created>
              <improvements>
                <improvement>
                  <improvementttype>Overclocker</improvementttype>
                  <condition>career</condition><enabled>1</enabled><addtorating>0</addtorating>
                </improvement>
                <improvement>
                  <improvementttype>Overclocker</improvementttype>
                  <condition>create</condition><enabled>1</enabled><addtorating>0</addtorating>
                </improvement>
                <improvement>
                  <improvementttype>Overclocker</improvementttype>
                  <enabled>0</enabled><addtorating>0</addtorating>
                </improvement>
              </improvements>
              <gears><gear><guid>gear</guid><name>Deck</name><devicerating>2</devicerating><matrixcmbonus>0</matrixcmbonus><overclocked>Device Rating</overclocked><children /></gear></gears>
              <armors><armor><guid>armor</guid><name>Armor</name><devicerating>2</devicerating><matrixcmbonus>0</matrixcmbonus><overclocked>Device Rating</overclocked><children /></armor></armors>
              <weapons><weapon><guid>weapon</guid><name>Weapon</name><devicerating>2</devicerating><overclocked>Device Rating</overclocked></weapon></weapons>
              <cyberwares><cyberware><guid>cyberware</guid><name>Ware</name><devicerating>2</devicerating><overclocked>Device Rating</overclocked></cyberware></cyberwares>
              <vehicles><vehicle><guid>vehicle</guid><name>Vehicle</name><category>Groundcraft</category><body>4</body><pilot>2</pilot><overclocked>Device Rating</overclocked><mods /></vehicle></vehicles>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSummary gear = service.ParseGear(xml).Gear.Single();
        CharacterArmorSummary armor = service.ParseArmors(xml).Armors.Single();
        CharacterWeaponSummary weapon = service.ParseWeapons(xml).Weapons.Single();
        CharacterCyberwareSummary cyberware = service.ParseCyberwares(xml).Cyberwares.Single();
        CharacterVehicleSummary vehicle = service.ParseVehicles(xml).Vehicles.Single();

        Assert.IsTrue(gear.MatrixConditionMaximumExact);
        Assert.AreEqual(10, gear.MatrixConditionMaximum);
        Assert.IsTrue(armor.MatrixConditionMaximumExact);
        Assert.AreEqual(10, armor.MatrixConditionMaximum);
        Assert.IsTrue(weapon.MatrixConditionMaximumExact);
        Assert.AreEqual(10, weapon.MatrixConditionMaximum);
        Assert.IsTrue(cyberware.MatrixConditionMaximumExact);
        Assert.AreEqual(10, cyberware.MatrixConditionMaximum);
        Assert.IsTrue(vehicle.MatrixConditionMaximumExact);
        Assert.AreEqual(10, vehicle.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Living_persona_matrix_monitor_applies_safe_saved_improvement_fragments()
    {
        const string xml = """
            <character>
              <created>True</created>
              <attributes><attribute><name>RES</name><totalvalue>3</totalvalue></attribute></attributes>
              <improvements>
                <improvement><improvedname>+2</improvedname><improvementttype>LivingPersonaDeviceRating</improvementttype><condition>career</condition><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvedname>+1</improvedname><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence0</unique><improvedname>+100</improvedname><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>0</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvementttype>Overclocker</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
              </improvements>
              <gears>
                <gear>
                  <guid>living-persona</guid><name>Living Persona</name><rating>3</rating><devicerating>{RES}</devicerating>
                  <canformpersona>Self</canformpersona><matrixcmbonus>1</matrixcmbonus><overclocked>Device Rating</overclocked>
                  <children><gear><guid>module</guid><name>Module</name><equipped>True</equipped><matrixcmbonus>2</matrixcmbonus><children /></gear></children>
                </gear>
              </gears>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSummary livingPersona = service.ParseGear(xml).Gear.Single(
            item => item.Guid == "living-persona");

        Assert.IsTrue(livingPersona.MatrixConditionMaximumExact);
        Assert.AreEqual(15, livingPersona.MatrixConditionMaximum);

        CharacterGearSummary malformedAttribute = service.ParseGear(xml.Replace(
            "<totalvalue>3</totalvalue>",
            "<totalvalue>invalid</totalvalue>",
            StringComparison.Ordinal)).Gear.Single(item => item.Guid == "living-persona");
        Assert.IsFalse(malformedAttribute.MatrixConditionMaximumExact);
        CharacterGearSummary duplicateAttribute = service.ParseGear(xml.Replace(
            "</attributes>",
            "<attribute><name>RES</name><totalvalue>3</totalvalue></attribute></attributes>",
            StringComparison.Ordinal)).Gear.Single(item => item.Guid == "living-persona");
        Assert.IsFalse(duplicateAttribute.MatrixConditionMaximumExact);
    }

    [TestMethod]
    public void Living_persona_matrix_monitor_applies_legacy_unique_precedence_and_custom_selection()
    {
        const string xml = """
            <character>
              <created>True</created>
              <improvements>
                <improvement><improvedname>+1</improvedname><val>4</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence0</unique><improvedname>+1</improvedname><val>6</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence0</unique><improvedname>+1</improvedname><val>8</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence-1</unique><improvedname>+1</improvedname><val>1</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>stack</unique><improvedname>+2</improvedname><val>3</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>stack</unique><improvedname>+2</improvedname><val>5</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvedname>+3</improvedname><val>1</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><custom>True</custom><unique>custom-stack</unique><improvedname>+3</improvedname><val>2</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><custom>True</custom><unique>custom-stack</unique><improvedname>+3</improvedname><val>7</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><custom>True</custom><improvedname>+4</improvedname><val>0</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvedname>+5</improvedname><val>10</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvedname>+5</improvedname><val>10</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence0</unique><improvedname>+5</improvedname><val>15</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence1</unique><improvedname>+1</improvedname><val>4</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence1</unique><improvedname>+1</improvedname><val>5</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><unique>precedence-1</unique><improvedname>+1</improvedname><val>1</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><improvedname>+2</improvedname><val>0</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><custom>True</custom><unique>boxes</unique><improvedname>+2</improvedname><val>1</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
                <improvement><custom>True</custom><unique>boxes</unique><improvedname>+2</improvedname><val>2</val><improvementttype>LivingPersonaMatrixCM</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement>
              </improvements>
              <gears><gear><guid>living-persona</guid><name>Living Persona</name><rating>4</rating><devicerating>Rating</devicerating><canformpersona>Self</canformpersona><matrixcmbonus>0</matrixcmbonus><children /></gear></gears>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSummary livingPersona = service.ParseGear(xml).Gear.Single();

        Assert.IsTrue(livingPersona.MatrixConditionMaximumExact);
        Assert.AreEqual(29, livingPersona.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Living_persona_matrix_monitor_fails_closed_for_malformed_selection_value()
    {
        const string xml = """
            <character>
              <created>True</created>
              <improvements><improvement><unique>precedence0</unique><improvedname>+2</improvedname><val>invalid</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement></improvements>
              <gears><gear><guid>living-persona</guid><name>Living Persona</name><rating>3</rating><devicerating>Rating</devicerating><canformpersona>Self</canformpersona><matrixcmbonus>0</matrixcmbonus><children /></gear></gears>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSummary livingPersona = service.ParseGear(xml).Gear.Single();

        Assert.IsFalse(livingPersona.MatrixConditionMaximumExact);
        Assert.AreEqual(0, livingPersona.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Living_persona_matrix_monitor_preserves_custom_unique_only_legacy_list_behavior()
    {
        const string xml = """
            <character>
              <created>True</created>
              <improvements><improvement><custom>True</custom><unique>custom-only</unique><improvedname>+4</improvedname><val>9</val><improvementttype>LivingPersonaDeviceRating</improvementttype><enabled>1</enabled><addtorating>0</addtorating></improvement></improvements>
              <gears><gear><guid>living-persona</guid><name>Living Persona</name><rating>4</rating><devicerating>Rating</devicerating><canformpersona>Self</canformpersona><matrixcmbonus>0</matrixcmbonus><children /></gear></gears>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterGearSummary livingPersona = service.ParseGear(xml).Gear.Single();

        Assert.IsTrue(livingPersona.MatrixConditionMaximumExact);
        Assert.AreEqual(10, livingPersona.MatrixConditionMaximum);
    }

    [TestMethod]
    public void Rating_expression_evaluator_supports_checked_integer_arithmetic_only()
    {
        Assert.IsTrue(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "Rating + (2 * 3) - 1",
            4,
            out int additive));
        Assert.AreEqual(9, additive);
        Assert.IsTrue(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "(Rating + 1) * 2",
            4,
            out int grouped));
        Assert.AreEqual(10, grouped);
        Assert.IsTrue(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "({Rating} + 1) * 2",
            4,
            out int braced));
        Assert.AreEqual(10, braced);
        Assert.IsFalse(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "{RES}",
            4,
            out _));
        IReadOnlyDictionary<string, int> attributes = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["RES"] = 5
        };
        Assert.IsTrue(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "{RES} + {Rating}",
            2,
            attributes,
            out int attributeBound));
        Assert.AreEqual(7, attributeBound);
        Assert.IsFalse(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "{RESBase}",
            2,
            attributes,
            out _));
        Assert.IsFalse(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "Rating / 2",
            4,
            out _));
        Assert.IsFalse(CharacterVehicleConditionMonitorCalculator.TryResolveRatingExpression(
            "9223372036854775807 * 2",
            0,
            out _));
    }


    [TestMethod]
    public void ParseAttributes_reads_core_attribute_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterAttributesSection section = service.ParseAttributes(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "BOD"));
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "AGI"));
        Assert.IsTrue(section.Attributes.All(attribute => attribute.TotalValue >= 0));
    }

    [TestMethod]
    public void ParseAttributeDetails_reads_attribute_bounds_and_values()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterAttributeDetailsSection section = service.ParseAttributeDetails(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Attributes.Any(attribute => attribute.Name == "BOD"));
        Assert.IsTrue(section.Attributes.All(attribute => attribute.MetatypeMax >= attribute.MetatypeMin));
    }

    [TestMethod]
    public void ParseInventory_extracts_item_counts_and_names()
    {
        string xml = File.ReadAllText(FindTestFilePath("Barrett.chum5"));
        var service = new CharacterSectionService();

        CharacterInventorySection section = service.ParseInventory(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.GearCount);
        Assert.IsGreaterThanOrEqualTo(0, section.WeaponCount);
        Assert.IsGreaterThanOrEqualTo(0, section.ArmorCount);
        Assert.IsGreaterThanOrEqualTo(0, section.CyberwareCount);
        Assert.IsGreaterThanOrEqualTo(0, section.VehicleCount);
        Assert.HasCount(section.GearCount, section.GearNames);
        Assert.HasCount(section.WeaponCount, section.WeaponNames);
        Assert.HasCount(section.ArmorCount, section.ArmorNames);
        Assert.HasCount(section.CyberwareCount, section.CyberwareNames);
        Assert.HasCount(section.VehicleCount, section.VehicleNames);
    }

    [TestMethod]
    public void ParseProfile_extracts_character_identity_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterProfileSection section = service.ParseProfile(xml);

        Assert.AreEqual("Troy Simmons", section.Name);
        Assert.AreEqual("BLUE", section.Alias);
        Assert.AreEqual("Ork", section.Metatype);
        Assert.AreEqual("SumtoTen", section.BuildMethod);
    }

    [TestMethod]
    public void ParseProfile_extracts_character_game_and_group_notes()
    {
        const string xml = "<character><name>Runner</name><notes>Character notes</notes><gamenotes>Game notes</gamenotes><groupnotes>Group notes</groupnotes></character>";
        var service = new CharacterSectionService();

        CharacterProfileSection section = service.ParseProfile(xml);

        Assert.AreEqual("Character notes", section.CharacterNotes);
        Assert.AreEqual("Game notes", section.GameNotes);
        Assert.AreEqual("Group notes", section.GroupNotes);
    }

    [TestMethod]
    public void ParseProgress_extracts_character_progress_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterProgressSection section = service.ParseProgress(xml);

        Assert.IsGreaterThan(0m, section.Nuyen);
        Assert.IsGreaterThanOrEqualTo(0m, section.Karma);
        Assert.IsGreaterThan(0m, section.TotalEssence);
    }

    [TestMethod]
    public void ParseProgress_preserves_manual_astral_and_wild_reputation()
    {
        const string xml = "<character><streetcred>11</streetcred><notoriety>12</notoriety><publicawareness>13</publicawareness><baseastralreputation>14</baseastralreputation><basewildreputation>15</basewildreputation></character>";
        var service = new CharacterSectionService();

        CharacterProgressSection section = service.ParseProgress(xml);

        Assert.AreEqual(11, section.StreetCred);
        Assert.AreEqual(12, section.Notoriety);
        Assert.AreEqual(13, section.PublicAwareness);
        Assert.AreEqual(14, section.AstralReputation);
        Assert.AreEqual(15, section.WildReputation);
    }

    [TestMethod]
    public void ParseRules_extracts_character_rules_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterRulesSection section = service.ParseRules(xml);

        Assert.AreEqual("SR5", section.GameEdition);
        Assert.AreEqual("default.xml", section.Settings);
        Assert.IsGreaterThan(0, section.BannedWareGrades.Count);
    }

    [TestMethod]
    public void ParseBuild_extracts_character_build_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterBuildSection section = service.ParseBuild(xml);

        Assert.AreEqual("SumtoTen", section.BuildMethod);
        Assert.AreEqual("C,2", section.PriorityMetatype);
        Assert.IsGreaterThan(0, section.TotalAttributes);
    }

    [TestMethod]
    public void ParseMovement_extracts_character_movement_fields()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterMovementSection section = service.ParseMovement(xml);

        Assert.AreEqual("2/1/0", section.Walk);
        Assert.AreEqual("4/0/0", section.Run);
        Assert.IsGreaterThanOrEqualTo(0, section.PhysicalCmFilled);
    }

    [TestMethod]
    public void ParseAwakening_extracts_magic_resonance_and_limits()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterAwakeningSection section = service.ParseAwakening(xml);

        Assert.IsFalse(section.MagEnabled);
        Assert.IsFalse(section.ResEnabled);
        Assert.IsFalse(section.DepEnabled);
        Assert.AreEqual("RES + WIL", section.StreamDrain);
    }

    [TestMethod]
    public void ParseGear_extracts_gear_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterGearSection section = service.ParseGear(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Gear.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseWeapons_extracts_weapon_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterWeaponsSection section = service.ParseWeapons(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Weapons.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
        Assert.IsTrue(section.Weapons.Any(item => !string.IsNullOrWhiteSpace(item.Damage)));
    }

    [TestMethod]
    public void ParseWeaponAccessories_extracts_weapon_accessory_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterWeaponAccessoriesSection section = service.ParseWeaponAccessories(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Accessories.Any(item => !string.IsNullOrWhiteSpace(item.WeaponName)));
    }

    [TestMethod]
    public void ParseArmors_extracts_armor_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterArmorsSection section = service.ParseArmors(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Armors.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseArmorMods_extracts_armor_mod_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterArmorModsSection section = service.ParseArmorMods(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.ArmorMods.Any(item => !string.IsNullOrWhiteSpace(item.ArmorName)));
    }

    [TestMethod]
    public void ParseCyberwares_extracts_cyberware_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterCyberwaresSection section = service.ParseCyberwares(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Cyberwares.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
        Assert.IsTrue(section.Cyberwares.Any(item => !string.IsNullOrWhiteSpace(item.Essence)));
    }

    [TestMethod]
    public void ParseCyberwares_preserves_modular_hierarchy_from_legacy_fixture()
    {
        string xml = File.ReadAllText(FindTestFilePath("SCSi.chum5"));
        var service = new CharacterSectionService();

        CharacterCyberwaresSection section = service.ParseCyberwares(xml);

        CharacterCyberwareSummary connector = section.Cyberwares.Single(item =>
            string.Equals(item.Name, "Modular Connector, Shoulder", StringComparison.Ordinal));
        CharacterCyberwareSummary arm = section.Cyberwares.Single(item =>
            string.Equals(item.Name, "Obvious Full Arm, Modular", StringComparison.Ordinal));
        CharacterCyberwareSummary weapon = section.Cyberwares.Single(item =>
            string.Equals(item.Name, "Custom Submachine Gun", StringComparison.Ordinal));

        Assert.AreEqual(string.Empty, connector.ParentGuid);
        Assert.AreEqual(string.Empty, connector.ParentName);
        Assert.AreEqual(0, connector.Depth);
        Assert.AreEqual("Right", connector.Location);
        Assert.IsTrue(connector.IsModular);
        Assert.IsGreaterThan(0, connector.ChildCount);

        Assert.AreEqual("Modular Connector, Shoulder", arm.ParentName);
        Assert.AreEqual(1, arm.Depth);
        Assert.AreEqual("shoulder", arm.MountSlot);
        StringAssert.Contains(arm.HierarchyPath, "Modular Connector, Shoulder");
        Assert.IsTrue(arm.IsModular);
        Assert.IsGreaterThan(0, arm.ChildCount);

        Assert.AreEqual("Obvious Full Arm, Modular", weapon.ParentName);
        Assert.AreEqual(2, weapon.Depth);
        StringAssert.Contains(weapon.HierarchyPath, "Obvious Full Arm, Modular");
    }

    [TestMethod]
    public void ParseVehicles_extracts_vehicle_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterVehiclesSection section = service.ParseVehicles(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Vehicles.Any(item => !string.IsNullOrWhiteSpace(item.Name)));
    }

    [TestMethod]
    public void ParseVehicleMods_extracts_vehicle_mod_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterVehicleModsSection section = service.ParseVehicleMods(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.VehicleMods.Any(item => !string.IsNullOrWhiteSpace(item.VehicleName)));
    }

    [TestMethod]
    public void ParseSkills_extracts_skill_entries_and_specializations()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterSkillsSection section = service.ParseSkills(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.KnowledgeCount);
        Assert.IsTrue(section.Skills.Any(skill => !string.IsNullOrWhiteSpace(skill.Suid) || !string.IsNullOrWhiteSpace(skill.Guid)));
        Assert.IsTrue(section.Skills.Any(skill => skill.Specializations.Count >= 0));
    }

    [TestMethod]
    public void ParseQualities_extracts_quality_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterQualitiesSection section = service.ParseQualities(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Qualities.Any(quality => !string.IsNullOrWhiteSpace(quality.Name)));
        Assert.HasCount(section.Count, section.Qualities);
    }

    [TestMethod]
    public void ParseContacts_extracts_contact_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterContactsSection section = service.ParseContacts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Contacts.Any(contact => !string.IsNullOrWhiteSpace(contact.Name)));
    }

    [TestMethod]
    public void ParseRelationship_contact_sections_split_contact_enemy_and_pet_entries()
    {
        const string xml = """
            <character>
              <contacts>
                <contact>
                  <name>Fixer</name>
                  <role>Broker</role>
                  <location>Seattle</location>
                  <connection>4</connection>
                  <loyalty>3</loyalty>
                  <type>Contact</type>
                </contact>
                <contact>
                  <name>Nemesis</name>
                  <role>Detective</role>
                  <location>Tacoma</location>
                  <connection>5</connection>
                  <loyalty>1</loyalty>
                  <type>Enemy</type>
                </contact>
                <contact>
                  <name>Wolfhound</name>
                  <role>Guard Dog</role>
                  <location>Redmond</location>
                  <connection>2</connection>
                  <loyalty>5</loyalty>
                  <type>Pet</type>
                </contact>
              </contacts>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterContactsSection relationships = service.ParseRelationships(xml);
        CharacterContactsSection contacts = service.ParseContacts(xml);
        CharacterContactsSection enemies = service.ParseEnemies(xml);
        CharacterContactsSection pets = service.ParsePets(xml);

        Assert.AreEqual(3, relationships.Count);
        Assert.AreEqual(1, contacts.Count);
        Assert.AreEqual(1, enemies.Count);
        Assert.AreEqual(1, pets.Count);
        Assert.AreEqual("Fixer", contacts.Contacts[0].Name);
        Assert.AreEqual("Nemesis", enemies.Contacts[0].Name);
        Assert.AreEqual("Wolfhound", pets.Contacts[0].Name);
    }

    [TestMethod]
    public void ParseSpellDefense_extracts_base_and_counterspelled_metrics()
    {
        const string xml = """
            <character>
              <currentcounterspellingdice>3</currentcounterspellingdice>
              <indirectdefenseresist>9</indirectdefenseresist>
              <indirectsoakresist>12</indirectsoakresist>
              <directmanaresist>8</directmanaresist>
              <directphysicalresist>7</directphysicalresist>
              <detectionspellresist>10</detectionspellresist>
              <decreasebodresist>11</decreasebodresist>
              <decreaseagiresist>10</decreaseagiresist>
              <decreaserearesist>9</decreaserearesist>
              <decreasestrresist>8</decreasestrresist>
              <decreasecharesist>7</decreasecharesist>
              <decreaseintresist>6</decreaseintresist>
              <decreaselogresist>5</decreaselogresist>
              <decreasewilresist>4</decreasewilresist>
              <illusionmanaresist>13</illusionmanaresist>
              <illusionphysicalresist>12</illusionphysicalresist>
              <manipulationmentalresist>11</manipulationmentalresist>
              <manipulationphysicalresist>10</manipulationphysicalresist>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterSpellDefenseSection section = service.ParseSpellDefense(xml);
        CharacterSpellDefenseMetricSummary indirectDodge = section.Metrics.Single(metric => string.Equals(metric.Id, "indirect-dodge", StringComparison.Ordinal));
        CharacterSpellDefenseMetricSummary illusionMana = section.Metrics.Single(metric => string.Equals(metric.Id, "illusion-mana", StringComparison.Ordinal));

        Assert.AreEqual(17, section.Count);
        Assert.AreEqual(3, section.CurrentCounterspellingDice);
        Assert.AreEqual(9, indirectDodge.BaseValue);
        Assert.AreEqual(12, indirectDodge.TotalValue);
        Assert.AreEqual("Dodge", indirectDodge.Formula);
        Assert.AreEqual(13, illusionMana.BaseValue);
        Assert.AreEqual(16, illusionMana.TotalValue);
    }

    [TestMethod]
    public void ParseSpells_extracts_spell_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin.chum5"));
        var service = new CharacterSectionService();

        CharacterSpellsSection section = service.ParseSpells(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Spells.Any(spell => !string.IsNullOrWhiteSpace(spell.Name)));
    }

    [TestMethod]
    public void ParsePowers_extracts_power_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterPowersSection section = service.ParsePowers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Powers.Any(power => !string.IsNullOrWhiteSpace(power.Name)));
    }

    [TestMethod]
    public void ParseComplexForms_extracts_complexform_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Rez0luti0n2.0.chum5"));
        var service = new CharacterSectionService();

        CharacterComplexFormsSection section = service.ParseComplexForms(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.ComplexForms.Any(form => !string.IsNullOrWhiteSpace(form.Name)));
    }

    [TestMethod]
    public void ParseSpirits_extracts_spirit_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Glessner.chum5"));
        var service = new CharacterSectionService();

        CharacterSpiritsSection section = service.ParseSpirits(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Spirits.Any(spirit => !string.IsNullOrWhiteSpace(spirit.Name)));
    }

    [TestMethod]
    public void ParseFoci_extracts_focus_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Gangerbean.chum5"));
        var service = new CharacterSectionService();

        CharacterFociSection section = service.ParseFoci(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Foci.Any(focus => !string.IsNullOrWhiteSpace(focus.Guid)));
    }

    [TestMethod]
    public void ParseAiPrograms_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterAiProgramsSection section = service.ParseAiPrograms(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.AiPrograms);
    }

    [TestMethod]
    public void ParseMartialArts_extracts_martial_art_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Apex Predator.chum5"));
        var service = new CharacterSectionService();

        CharacterMartialArtsSection section = service.ParseMartialArts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.MartialArts.Any(art => !string.IsNullOrWhiteSpace(art.Name)));
    }

    [TestMethod]
    public void ParseLimitModifiers_extracts_modifier_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterLimitModifiersSection section = service.ParseLimitModifiers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.LimitModifiers.Any(modifier => !string.IsNullOrWhiteSpace(modifier.Name)));
    }

    [TestMethod]
    public void ParseLifestyles_extracts_lifestyle_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("BLUE.chum5"));
        var service = new CharacterSectionService();

        CharacterLifestylesSection section = service.ParseLifestyles(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Lifestyles.Any(lifestyle => !string.IsNullOrWhiteSpace(lifestyle.Name)));
    }

    [TestMethod]
    public void ParseMetamagics_extracts_metamagic_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterMetamagicsSection section = service.ParseMetamagics(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Metamagics.Any(metamagic => !string.IsNullOrWhiteSpace(metamagic.Name)));
    }

    [TestMethod]
    public void ParseArts_extracts_art_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterArtsSection section = service.ParseArts(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Arts.Any(art => !string.IsNullOrWhiteSpace(art.Name)));
    }

    [TestMethod]
    public void ParseInitiationGrades_extracts_grade_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Munin_Career.chum5"));
        var service = new CharacterSectionService();

        CharacterInitiationGradesSection section = service.ParseInitiationGrades(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.InitiationGrades.Any(grade => grade.Grade >= 0));
    }

    [TestMethod]
    public void ParseCritterPowers_extracts_critter_power_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCritterPowersSection section = service.ParseCritterPowers(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.CritterPowers.Any(power => !string.IsNullOrWhiteSpace(power.Name)));
    }

    [TestMethod]
    public void ParseMentorSpirits_extracts_mentor_spirit_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterMentorSpiritsSection section = service.ParseMentorSpirits(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.MentorSpirits.Any(spirit => !string.IsNullOrWhiteSpace(spirit.Name)));
    }

    [TestMethod]
    public void ParseExpenses_extracts_expense_entries_and_totals()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterExpensesSection section = service.ParseExpenses(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.TotalKarma);
        Assert.IsGreaterThanOrEqualTo(0, section.TotalNuyen);
        Assert.HasCount(section.Count, section.Expenses);
    }

    [TestMethod]
    public void ParseSources_extracts_distinct_source_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterSourcesSection section = service.ParseSources(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Sources.Any(source => !string.IsNullOrWhiteSpace(source)));
        Assert.IsGreaterThanOrEqualTo(0, section.ReferencedSourceCount);
        Assert.IsNotNull(section.Sourcebooks);
        Assert.IsTrue(section.Sourcebooks!.All(sourcebook => !string.IsNullOrWhiteSpace(sourcebook.Code)));
    }

    [TestMethod]
    public void ParseSources_projects_sourcebook_selection_and_reference_mismatch_flags()
    {
        const string xml = """
                           <character>
                             <sources>
                               <source>sr5</source>
                               <source> rf </source>
                             </sources>
                             <weapons>
                               <weapon><source>SR5</source></weapon>
                               <weapon><source>sg</source></weapon>
                               <weapon><source>SG</source></weapon>
                             </weapons>
                           </character>
                           """;
        var service = new CharacterSectionService();

        CharacterSourcesSection section = service.ParseSources(xml);

        Assert.AreEqual(2, section.Count);
        Assert.AreEqual(2, section.ReferencedSourceCount);
        Assert.HasCount(3, section.Sourcebooks!);

        CharacterSourcebookSummary sr5 = section.Sourcebooks!.Single(source => source.Code == "SR5");
        CharacterSourcebookSummary rf = section.Sourcebooks!.Single(source => source.Code == "RF");
        CharacterSourcebookSummary sg = section.Sourcebooks!.Single(source => source.Code == "SG");

        Assert.AreEqual(1, sr5.ItemReferenceCount);
        Assert.IsTrue(sr5.SelectedForCharacter);
        Assert.IsFalse(sr5.MissingFromSelectedList);
        Assert.IsFalse(sr5.SelectionOnly);

        Assert.AreEqual(0, rf.ItemReferenceCount);
        Assert.IsTrue(rf.SelectedForCharacter);
        Assert.IsFalse(rf.MissingFromSelectedList);
        Assert.IsTrue(rf.SelectionOnly);

        Assert.AreEqual(2, sg.ItemReferenceCount);
        Assert.IsFalse(sg.SelectedForCharacter);
        Assert.IsTrue(sg.MissingFromSelectedList);
        Assert.IsFalse(sg.SelectionOnly);
    }

    [TestMethod]
    public void ParseGearLocations_extracts_location_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseGearLocations(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Locations.Any(location => !string.IsNullOrWhiteSpace(location.Name)));
    }

    [TestMethod]
    public void ParseArmorLocations_extracts_location_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseArmorLocations(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.Locations.Any(location => !string.IsNullOrWhiteSpace(location.Name)));
    }

    [TestMethod]
    public void ParseWeaponLocations_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseWeaponLocations(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Locations);
    }

    [TestMethod]
    public void ParseVehicleLocations_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterLocationsSection section = service.ParseVehicleLocations(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Locations);
    }

    [TestMethod]
    public void ParseCalendar_handles_empty_collection()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCalendarSection section = service.ParseCalendar(xml);

        Assert.IsGreaterThanOrEqualTo(0, section.Count);
        Assert.HasCount(section.Count, section.Entries);
    }

    [TestMethod]
    public void ParseImprovements_extracts_improvement_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Draught.chum5"));
        var service = new CharacterSectionService();

        CharacterImprovementsSection section = service.ParseImprovements(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsGreaterThanOrEqualTo(0, section.EnabledCount);
        Assert.HasCount(section.Count, section.Improvements);
    }

    [TestMethod]
    public void ParseImprovements_reads_modern_numeric_enabled_flags()
    {
        const string xml = """
            <character>
              <improvements>
                <improvement><improvedname>active</improvedname><improvementttype>Overclocker</improvementttype><rating>1</rating><enabled>1</enabled></improvement>
                <improvement><improvedname>inactive</improvedname><improvementttype>Overclocker</improvementttype><rating>1</rating><enabled>0</enabled></improvement>
              </improvements>
            </character>
            """;
        var service = new CharacterSectionService();

        CharacterImprovementsSection section = service.ParseImprovements(xml);

        Assert.AreEqual(2, section.Count);
        Assert.AreEqual(1, section.EnabledCount);
        Assert.IsTrue(section.Improvements.Single(item => item.ImprovedName == "active").Enabled);
        Assert.IsFalse(section.Improvements.Single(item => item.ImprovedName == "inactive").Enabled);
    }

    [TestMethod]
    public void ParseCustomDataDirectoryNames_extracts_directory_entries()
    {
        string xml = File.ReadAllText(FindTestFilePath("Mittens Chargen.chum5"));
        var service = new CharacterSectionService();

        CharacterCustomDataDirectoryNamesSection section = service.ParseCustomDataDirectoryNames(xml);

        Assert.IsGreaterThan(0, section.Count);
        Assert.IsTrue(section.DirectoryNames.Any(name => !string.IsNullOrWhiteSpace(name)));
    }

    [TestMethod]
    public void Editable_collection_sections_preserve_stable_ids_and_editable_values()
    {
        const string xml = """
<character>
  <gears><gear><guid>gear-1</guid><name>Gear</name><rating>2</rating><qty>3</qty><source>Core</source><notes>Note</notes><extra>Custom</extra><equipped>True</equipped><wirelesson>True</wirelesson><homenode>True</homenode></gear></gears>
  <weapons><weapon><guid>weapon-1</guid><name>Weapon</name><accessories><accessory><guid>accessory-1</guid><name>Accessory</name><rating>1</rating><notes>Nested</notes><wirelesson>True</wirelesson></accessory></accessories></weapon></weapons>
  <armors><armor><guid>armor-1</guid><name>Armor</name></armor></armors>
  <newskills><skills><skill><guid>skill-1</guid><suid>skill</suid><name>Skill</name><notes>Note</notes><extra>Custom</extra><specs /></skill></skills></newskills>
  <contacts><contact><guid>contact-1</guid><name>Contact</name><notes>Note</notes><extra>Custom</extra></contact></contacts>
  <vehicles><vehicle><guid>vehicle-1</guid><name>Vehicle</name></vehicle></vehicles>
  <qualities><quality><guid>quality-1</guid><name>Quality</name><notes>Note</notes><extra>Custom</extra></quality></qualities>
  <drugs><drug><guid>drug-1</guid><name>Drug</name><qty>1</qty></drug></drugs>
  <cyberwares><cyberware><guid>cyberware-1</guid><name>Cyberware</name><notes>Note</notes><extra>Custom</extra><wirelesson>True</wirelesson></cyberware></cyberwares>
  <spells><spell><guid>spell-1</guid><name>Spell</name></spell></spells>
  <powers><power><guid>power-1</guid><name>Power</name></power></powers>
  <complexforms><complexform><guid>complex-1</guid><name>Complex</name></complexform></complexforms>
  <aiprograms><program><guid>program-1</guid><name>Program</name></program></aiprograms>
  <initiationgrades><initiationgrade><guid>grade-1</guid><grade>1</grade><reward>Masking</reward></initiationgrade></initiationgrades>
  <spirits><spirit><guid>spirit-1</guid><name>Spirit</name></spirit></spirits>
  <critterpowers><critterpower><guid>critter-1</guid><name>Critter Power</name></critterpower></critterpowers>
</character>
""";
        var service = new CharacterSectionService();

        Assert.AreEqual("gear-1", service.ParseGear(xml).Gear.Single().Guid);
        Assert.AreEqual("Custom", service.ParseGear(xml).Gear.Single().CustomName);
        Assert.IsTrue(service.ParseGear(xml).Gear.Single().WirelessEnabled);
        Assert.AreEqual("weapon-1", service.ParseWeapons(xml).Weapons.Single().Guid);
        Assert.AreEqual("accessory-1", service.ParseWeaponAccessories(xml).Accessories.Single().AccessoryGuid);
        Assert.AreEqual("Nested", service.ParseWeaponAccessories(xml).Accessories.Single().Notes);
        Assert.AreEqual("armor-1", service.ParseArmors(xml).Armors.Single().Guid);
        Assert.AreEqual("skill-1", service.ParseSkills(xml).Skills.Single().Guid);
        Assert.AreEqual("contact-1", service.ParseContacts(xml).Contacts.Single().Guid);
        Assert.AreEqual("vehicle-1", service.ParseVehicles(xml).Vehicles.Single().Guid);
        Assert.AreEqual("quality-1", service.ParseQualities(xml).Qualities.Single().Guid);
        Assert.AreEqual("drug-1", service.ParseDrugs(xml).Drugs.Single().Guid);
        Assert.AreEqual("cyberware-1", service.ParseCyberwares(xml).Cyberwares.Single().Guid);
        Assert.AreEqual("spell-1", service.ParseSpells(xml).Spells.Single().Guid);
        Assert.AreEqual("power-1", service.ParsePowers(xml).Powers.Single().Guid);
        Assert.AreEqual("complex-1", service.ParseComplexForms(xml).ComplexForms.Single().Guid);
        Assert.AreEqual("program-1", service.ParseAiPrograms(xml).AiPrograms.Single().Guid);
        Assert.AreEqual("grade-1", service.ParseInitiationGrades(xml).InitiationGrades.Single().Guid);
        Assert.AreEqual("spirit-1", service.ParseSpirits(xml).Spirits.Single().Guid);
        Assert.AreEqual("critter-1", service.ParseCritterPowers(xml).CritterPowers.Single().Guid);
    }

    [TestMethod]
    public void ParseDrugs_extracts_drug_entries_from_xml_payload()
    {
        const string xml = "<character><drugs><drug><name>Jazz</name><category>Combat Drugs</category><source>SR5</source><rating>2</rating><qty>3</qty></drug></drugs></character>";
        var service = new CharacterSectionService();

        CharacterDrugsSection section = service.ParseDrugs(xml);

        Assert.HasCount(1, section.Drugs);
        Assert.AreEqual("Jazz", section.Drugs[0].Name);
        Assert.AreEqual(3m, section.Drugs[0].Quantity);
    }

    [TestMethod]
    public void ParseContacts_projects_all_direct_creation_fields_and_editability()
    {
        const string xml = """
<character>
  <created>False</created>
  <contacts>
    <contact>
      <guid>contact-create</guid>
      <name>Ms. Johnson</name>
      <role>Fixer</role>
      <location>Vienna</location>
      <connection>6</connection>
      <loyalty>5</loyalty>
      <metatype>Elf</metatype>
      <gender>Female</gender>
      <age>42</age>
      <contacttype>Professional</contacttype>
      <preferredpayment>Credstick</preferredpayment>
      <hobbiesvice>Urban exploration</hobbiesvice>
      <personallife>Private</personallife>
      <groupname>Night Market</groupname>
      <group>False</group>
      <free>True</free>
      <family>True</family>
      <blackmail>False</blackmail>
      <notes>Keep it discreet.</notes>
      <type>Contact</type>
    </contact>
  </contacts>
</character>
""";
        var service = new CharacterSectionService();

        CharacterContactSummary contact = service.ParseContacts(xml).Contacts.Single();

        Assert.AreEqual("contact-create", contact.Guid);
        Assert.AreEqual("Ms. Johnson", contact.Name);
        Assert.AreEqual("Fixer", contact.Role);
        Assert.AreEqual("Vienna", contact.Location);
        Assert.AreEqual("Elf", contact.Metatype);
        Assert.AreEqual("Female", contact.Gender);
        Assert.AreEqual("42", contact.Age);
        Assert.AreEqual("Professional", contact.ContactType);
        Assert.AreEqual("Credstick", contact.PreferredPayment);
        Assert.AreEqual("Urban exploration", contact.HobbiesVice);
        Assert.AreEqual("Private", contact.PersonalLife);
        Assert.AreEqual("Night Market", contact.GroupName);
        Assert.AreEqual(6, contact.Connection);
        Assert.AreEqual(6, contact.ConnectionMaximum);
        Assert.AreEqual(5, contact.Loyalty);
        Assert.IsFalse(contact.IsGroup);
        Assert.IsTrue(contact.Free);
        Assert.IsTrue(contact.Family);
        Assert.IsFalse(contact.Blackmail);
        Assert.IsTrue(contact.IdentityEditable);
        Assert.IsTrue(contact.ConnectionEditable);
        Assert.IsTrue(contact.LoyaltyEditable);
        Assert.IsTrue(contact.GroupEditable);
        Assert.IsTrue(contact.FreeEditable);
        Assert.IsTrue(contact.FamilyEditable);
        Assert.IsTrue(contact.BlackmailEditable);
        Assert.IsTrue(contact.CanDelete);
        Assert.IsTrue(contact.EditSemanticsExact);
    }

    [TestMethod]
    public void ParseContacts_projects_governed_link_snapshot_and_restores_saved_identity_after_unlink()
    {
        const string linkedXml = """
<character>
  <contacts>
    <contact>
      <guid>contact-linked</guid><name>Original contact</name><metatype>Human</metatype>
      <gender>Female</gender><age>38</age><type>Contact</type>
      <file>/private/linked-characters/contact-linked.chum5lz</file>
      <relative>linked-characters/contact-linked.chum5lz</relative>
      <chummercomplete><linkedcharacter>
        <displayname>Neon Fox.chum5lz</displayname><name>Neon Fox</name>
        <metatype>Elf (Dryad)</metatype><gender>Non-binary</gender><age>29</age>
      </linkedcharacter></chummercomplete>
    </contact>
  </contacts>
</character>
""";
        var service = new CharacterSectionService();

        CharacterContactSummary linked = service.ParseContacts(linkedXml).Contacts.Single();

        Assert.AreEqual("Neon Fox", linked.Name);
        Assert.AreEqual("Elf (Dryad)", linked.Metatype);
        Assert.AreEqual("Non-binary", linked.Gender);
        Assert.AreEqual("29", linked.Age);
        Assert.IsTrue(linked.LinkedCharacter?.IsLinked);
        Assert.IsTrue(linked.LinkedCharacter?.IdentityResolved);
        Assert.AreEqual("Neon Fox.chum5lz", linked.LinkedCharacter?.DisplayName);
        Assert.AreEqual(
            "linked-characters/contact-linked.chum5lz",
            linked.LinkedCharacter?.RelativeFileName);
        Assert.IsFalse(linked.IdentityEditable);

        XDocument unlinkedDocument = XDocument.Parse(linkedXml);
        XElement contact = unlinkedDocument.Root!.Element("contacts")!.Element("contact")!;
        contact.Element("file")!.Value = string.Empty;
        contact.Element("relative")!.Value = string.Empty;
        contact.Element("chummercomplete")!.Remove();
        CharacterContactSummary unlinked = service.ParseContacts(unlinkedDocument.ToString()).Contacts.Single();

        Assert.AreEqual("Original contact", unlinked.Name);
        Assert.AreEqual("Human", unlinked.Metatype);
        Assert.AreEqual("Female", unlinked.Gender);
        Assert.AreEqual("38", unlinked.Age);
        Assert.IsFalse(unlinked.LinkedCharacter?.IsLinked);
        Assert.IsFalse(unlinked.LinkedCharacter?.IdentityResolved);
        Assert.IsTrue(unlinked.IdentityEditable);
    }

    [TestMethod]
    public void ParseContacts_applies_career_link_readonly_enemy_and_improvement_rules()
    {
        const string xml = """
<character>
  <created>True</created>
  <improvements>
    <improvement><improvementttype>FriendsInHighPlaces</improvementttype><enabled>1</enabled><condition>career</condition></improvement>
    <improvement><improvementttype>ContactForceGroup</improvementttype><improvedname>contact-linked</improvedname><enabled>1</enabled></improvement>
    <improvement><improvementttype>ContactMakeFree</improvementttype><improvedname>contact-linked</improvedname><enabled>1</enabled></improvement>
    <improvement><improvementttype>ContactForcedLoyalty</improvementttype><improvedname>contact-linked</improvedname><val>4</val><enabled>1</enabled></improvement>
  </improvements>
  <contacts>
    <contact>
      <guid>contact-linked</guid><name>Linked</name><connection>11</connection><loyalty>2</loyalty>
      <file>linked.chum5</file><group>False</group><free>False</free><type>Contact</type>
    </contact>
    <contact>
      <guid>contact-readonly</guid><name>Read only</name><connection>3</connection><loyalty>3</loyalty>
      <group>True</group><readonly /><type>Contact</type>
    </contact>
    <contact>
      <guid>contact-enemy</guid><name>Enemy</name><connection>2</connection><loyalty>1</loyalty>
      <family>True</family><blackmail>True</blackmail><type>Enemy</type>
    </contact>
  </contacts>
</character>
""";
        var service = new CharacterSectionService();

        CharacterContactsSection section = service.ParseContacts(xml);
        CharacterContactSummary linked = section.Contacts.Single(contact => contact.Guid == "contact-linked");
        CharacterContactSummary readOnly = section.Contacts.Single(contact => contact.Guid == "contact-readonly");
        XElement character = XElement.Parse(xml);
        XElement enemyNode = character.Element("contacts")!.Elements("contact")
            .Single(contact => contact.Element("guid")!.Value == "contact-enemy");
        Assert.IsTrue(CharacterContactEditSemanticsResolver.TryResolve(character, enemyNode, out CharacterContactEditSemantics enemy));

        Assert.AreEqual(12, linked.ConnectionMaximum);
        Assert.AreEqual(11, linked.Connection);
        Assert.AreEqual(4, linked.Loyalty);
        Assert.IsFalse(linked.IdentityEditable);
        Assert.IsTrue(linked.ConnectionEditable);
        Assert.IsFalse(linked.LoyaltyEditable);
        Assert.IsFalse(linked.GroupEditable);
        Assert.IsTrue(linked.Free);
        Assert.IsFalse(linked.FreeEditable);
        Assert.IsTrue(linked.CanDelete);

        Assert.AreEqual(1, readOnly.Loyalty);
        Assert.IsFalse(readOnly.ConnectionEditable);
        Assert.IsFalse(readOnly.LoyaltyEditable);
        Assert.IsFalse(readOnly.GroupEditable);
        Assert.IsFalse(readOnly.CanDelete);

        Assert.IsTrue(enemy.Family);
        Assert.IsTrue(enemy.Blackmail);
        Assert.IsFalse(enemy.FamilyEditable);
        Assert.IsFalse(enemy.BlackmailEditable);
        Assert.IsFalse(enemy.FreeEditable);

        XElement unsupported = XElement.Parse("""
<character>
  <created>True</created>
  <improvements><improvement><improvementttype>ContactForcedLoyalty</improvementttype><improvedname>contact</improvedname><val>7</val></improvement></improvements>
  <contacts><contact><guid>contact</guid><name>Unsupported</name><connection>2</connection><loyalty>2</loyalty></contact></contacts>
</character>
""");
        Assert.IsFalse(CharacterContactEditSemanticsResolver.TryResolve(
            unsupported,
            unsupported.Element("contacts")!.Element("contact")!,
            out _));
    }

    [TestMethod]
    public void ParsePets_projects_only_pets_with_exact_direct_editability()
    {
        const string xml = """
<character>
  <contacts>
    <contact>
      <guid>pet-free</guid><name>Rex</name><metatype>Hell Hound</metatype>
      <notes>Likes synth-meat.</notes><type>Pet</type>
    </contact>
    <contact>
      <guid>pet-linked</guid><name>Linked critter</name><metatype>Barghest</metatype>
      <relative>linked-pet.chum5</relative><readonly /><type>Pet</type>
    </contact>
    <contact>
      <guid>contact</guid><name>Not a pet</name><type>Contact</type>
    </contact>
  </contacts>
</character>
""";
        var service = new CharacterSectionService();

        CharacterContactsSection section = service.ParsePets(xml);

        Assert.AreEqual(2, section.Count);
        CharacterContactSummary free = section.Contacts.Single(pet => pet.Guid == "pet-free");
        CharacterContactSummary linked = section.Contacts.Single(pet => pet.Guid == "pet-linked");
        Assert.AreEqual("Rex", free.Name);
        Assert.AreEqual("Hell Hound", free.Metatype);
        Assert.AreEqual("Likes synth-meat.", free.Notes);
        Assert.IsTrue(free.IdentityEditable);
        Assert.IsTrue(free.CanDelete);
        Assert.IsTrue(free.EditSemanticsExact);
        Assert.IsFalse(free.ConnectionEditable);
        Assert.IsFalse(free.LoyaltyEditable);
        Assert.IsFalse(linked.IdentityEditable);
        Assert.IsTrue(linked.CanDelete);
        Assert.IsTrue(linked.EditSemanticsExact);

        XElement wrongType = XElement.Parse("<contact><type>Contact</type></contact>");
        Assert.IsFalse(CharacterPetEditSemanticsResolver.TryResolve(wrongType, out _));
    }

    [TestMethod]
    public void ParseSpirits_projects_the_persisted_linked_runner_association()
    {
        const string xml = """
<character>
  <spirits>
    <spirit>
      <guid>spirit-linked</guid><name>Fire Spirit</name>
      <file>/app/private/linked-characters/fire-spirit.chum5lz</file>
      <relative>linked-characters/fire-spirit.chum5lz</relative>
      <chummercomplete><linkedcharacter>
        <displayname>Fire spirit runner.chum5lz</displayname>
        <name>Ember</name>
      </linkedcharacter></chummercomplete>
    </spirit>
    <spirit><guid>sprite-free</guid><name>Machine Sprite</name></spirit>
  </spirits>
</character>
""";

        CharacterSpiritsSection section = new CharacterSectionService().ParseSpirits(xml);

        CharacterLinkedAssociationSummary linked = section.Spirits
            .Single(spirit => spirit.Guid == "spirit-linked")
            .LinkedCharacter!;
        Assert.IsTrue(linked.IsLinked);
        Assert.IsTrue(linked.IdentityResolved);
        Assert.AreEqual("/app/private/linked-characters/fire-spirit.chum5lz", linked.FileName);
        Assert.AreEqual("linked-characters/fire-spirit.chum5lz", linked.RelativeFileName);
        Assert.AreEqual("Fire spirit runner.chum5lz", linked.DisplayName);

        CharacterLinkedAssociationSummary free = section.Spirits
            .Single(spirit => spirit.Guid == "sprite-free")
            .LinkedCharacter!;
        Assert.IsFalse(free.IsLinked);
        Assert.IsFalse(free.IdentityResolved);
        Assert.AreEqual(string.Empty, free.FileName);
    }

    [TestMethod]
    public void ParseSpirits_exposes_only_source_exact_force_ceilings()
    {
        const string xml = """
<character>
  <created>True</created>
  <magenabled>True</magenabled>
  <resenabled>True</resenabled>
  <attributes>
    <attribute><name>MAG</name><value>5</value><totalvalue>7</totalvalue></attribute>
    <attribute><name>RES</name><totalvalue>4</totalvalue></attribute>
  </attributes>
  <spirits>
    <spirit><guid>sprite-1</guid><name>Machine Sprite</name><type>Sprite</type><force>8</force><services>2</services><bound>True</bound></spirit>
    <spirit><guid>spirit-unknown</guid><name>Fire Spirit</name><type>Spirit</type><force>10</force><services>1</services><bound>False</bound></spirit>
  </spirits>
</character>
""";

        CharacterSpiritsSection section = new CharacterSectionService().ParseSpirits(xml);

        CharacterSpiritSummary sprite = section.Spirits.Single(spirit => spirit.Guid == "sprite-1");
        Assert.AreEqual("Sprite", sprite.EntityType);
        Assert.AreEqual(8, sprite.ForceMaximum);
        Assert.IsTrue(sprite.ForceMaximumExact);
        Assert.IsTrue(sprite.ForceEditable);

        CharacterSpiritSummary spirit = section.Spirits.Single(spirit => spirit.Guid == "spirit-unknown");
        Assert.AreEqual("Spirit", spirit.EntityType);
        Assert.IsFalse(spirit.ForceMaximumExact);
        Assert.IsFalse(spirit.ForceEditable);
    }

    [TestMethod]
    public void ParseSpirits_uses_a_persisted_total_magic_setting_when_available()
    {
        const string xml = """
<character>
  <created>True</created>
  <magenabled>True</magenabled>
  <spiritforcebasedontotalmag>True</spiritforcebasedontotalmag>
  <attributes><attribute><name>MAG</name><value>5</value><totalvalue>7</totalvalue></attribute></attributes>
  <spirits><spirit><guid>spirit-1</guid><name>Fire Spirit</name><type>Spirit</type><force>14</force></spirit></spirits>
</character>
""";

        CharacterSpiritSummary spirit = new CharacterSectionService()
            .ParseSpirits(xml)
            .Spirits.Single();

        Assert.AreEqual(14, spirit.ForceMaximum);
        Assert.IsTrue(spirit.ForceMaximumExact);
        Assert.IsTrue(spirit.ForceEditable);
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

    private sealed class FixedSourceDataResolver : ICharacterSourceDataResolver
    {
        private static readonly ICharacterSourceDataContext Context = new FixedSourceDataContext();

        public ICharacterSourceDataContext? TryCreateContext(string characterXml)
            => Context;
    }

    private sealed class FixedSourceDataContext : ICharacterSourceDataContext
    {
        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 4;
            return string.Equals(gradeName, "Standard", StringComparison.Ordinal)
                && string.Equals(improvementSource, "Cyberware", StringComparison.Ordinal);
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = new CharacterVehicleModSourceBonuses(
                BodyExpression: "Rating + 1",
                DeviceRatingExpression: "2",
                MatrixConditionExpression: "3",
                WirelessBodyExpression: string.Empty,
                WirelessDeviceRatingExpression: string.Empty,
                WirelessMatrixConditionExpression: string.Empty);
            return string.Equals(sourceId, ResolverVehicleModId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(name, "Gyro-Stabilization", StringComparison.Ordinal);
        }
    }
}
