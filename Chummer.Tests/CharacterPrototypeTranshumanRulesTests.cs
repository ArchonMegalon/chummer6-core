using System.Xml.Linq;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterPrototypeTranshumanRulesTests
{
    private static readonly Guid ParentId = Guid.Parse("81111111-8111-8111-8111-811111111111");
    private static readonly Guid ChildId = Guid.Parse("82222222-8222-8222-8222-822222222222");

    [TestMethod]
    public void Creation_top_level_bioware_projects_exact_allowance_and_hierarchy()
    {
        XElement character = XElement.Parse(ValidCharacterXml());
        XElement selected = character.Element("cyberwares")!.Element("cyberware")!;

        Assert.IsTrue(CharacterPrototypeTranshumanRules.TryProject(character, selected, out CharacterPrototypeTranshumanSemantics state));
        Assert.AreEqual(ParentId, state.CyberwareId);
        Assert.IsFalse(state.PrototypeTranshuman);
        Assert.AreEqual(1.25m, state.EssenceAllowance);
        CollectionAssert.AreEqual(
            new[]
            {
                new CharacterPrototypeTranshumanNodeState(ParentId, false),
                new CharacterPrototypeTranshumanNodeState(ChildId, true)
            },
            state.Hierarchy.ToArray());
    }

    [TestMethod]
    public void Parser_exposes_semantics_only_on_the_eligible_top_level_bioware()
    {
        CharacterCyberwareSummary[] cyberware = new CharacterSectionService()
            .ParseCyberwares(ValidCharacterXml())
            .Cyberwares
            .ToArray();

        Assert.AreEqual(2, cyberware.Length);
        Assert.IsNotNull(cyberware[0].PrototypeTranshumanSemantics);
        Assert.IsNull(cyberware[1].PrototypeTranshumanSemantics);
    }

    [TestMethod]
    public void Career_child_cyberware_and_missing_improvement_fail_closed()
    {
        XElement character = XElement.Parse(ValidCharacterXml());
        XElement parent = character.Element("cyberwares")!.Element("cyberware")!;
        XElement child = parent.Element("children")!.Element("cyberware")!;

        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, child, out _));
        character.Element("created")!.Value = "True";
        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, parent, out _));
        character.Element("created")!.Value = "False";
        character.Element("improvements")!.Remove();
        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, parent, out _));
    }

    [TestMethod]
    public void Duplicate_identity_malformed_flags_and_non_bioware_fail_closed()
    {
        XElement character = XElement.Parse(ValidCharacterXml());
        XElement parent = character.Element("cyberwares")!.Element("cyberware")!;
        XElement child = parent.Element("children")!.Element("cyberware")!;

        child.Element("guid")!.Value = ParentId.ToString("D");
        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, parent, out _));
        child.Element("guid")!.Value = ChildId.ToString("D");
        child.Element("prototypetranshuman")!.Value = "not-a-bool";
        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, parent, out _));
        child.Element("prototypetranshuman")!.Value = "True";
        parent.Element("improvementsource")!.Value = "Cyberware";
        Assert.IsFalse(CharacterPrototypeTranshumanRules.TryProject(character, parent, out _));
    }

    [TestMethod]
    public void Expected_semantics_match_every_descendant_and_allowance()
    {
        XElement character = XElement.Parse(ValidCharacterXml());
        XElement parent = character.Element("cyberwares")!.Element("cyberware")!;
        Assert.IsTrue(CharacterPrototypeTranshumanRules.TryProject(character, parent, out CharacterPrototypeTranshumanSemantics expected));
        Assert.IsTrue(CharacterPrototypeTranshumanRules.Matches(expected, expected));

        parent.Element("children")!.Element("cyberware")!.Element("prototypetranshuman")!.Value = "False";
        Assert.IsTrue(CharacterPrototypeTranshumanRules.TryProject(character, parent, out CharacterPrototypeTranshumanSemantics changed));
        Assert.IsFalse(CharacterPrototypeTranshumanRules.Matches(expected, changed));
    }

    private static string ValidCharacterXml() => $$"""
<character>
  <created>False</created>
  <improvements>
    <improvement><improvementttype>PrototypeTranshuman</improvementttype><val>1.25</val><enabled>1</enabled></improvement>
  </improvements>
  <cyberwares>
    <cyberware>
      <guid>{{ParentId:D}}</guid><name>Nephritic Screen</name><improvementsource>Bioware</improvementsource><prototypetranshuman>False</prototypetranshuman>
      <children><cyberware><guid>{{ChildId:D}}</guid><name>Child Option</name><improvementsource>Bioware</improvementsource><prototypetranshuman>True</prototypetranshuman></cyberware></children>
    </cyberware>
  </cyberwares>
</character>
""";
}
