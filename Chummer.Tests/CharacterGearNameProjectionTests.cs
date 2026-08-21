using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearNameProjectionTests
{
    [TestMethod]
    public void ParseGear_ProjectsExactTopLevelAndNestedGearNames()
    {
        const string xml = """
            <character>
              <created>True</created>
              <gears>
                <gear>
                  <guid>11111111-1111-1111-1111-111111111111</guid>
                  <name>Commlink</name>
                  <gearname>Primary link</gearname>
                  <children>
                    <gear>
                      <guid>22222222-2222-2222-2222-222222222222</guid>
                      <name>Module</name>
                      <gearname>Hidden module</gearname>
                    </gear>
                  </children>
                </gear>
              </gears>
            </character>
            """;

        CharacterGearSection section = new CharacterSectionService().ParseGear(xml);

        Assert.AreEqual(
            "Primary link",
            section.Gear.Single(item => item.Guid.StartsWith("11111111", StringComparison.Ordinal)).GearName);
        Assert.AreEqual(
            "Hidden module",
            section.Gear.Single(item => item.Guid.StartsWith("22222222", StringComparison.Ordinal)).GearName);
    }
}
