using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterSpiritNameChoiceRulesTests
{
    private static readonly Guid TargetId = Guid.Parse("81111111-8111-8111-8111-811111111111");

    [TestMethod]
    public void LimitsFilterBaseBeforeEnabledEntityImprovementsAreAppended()
    {
        Assert.IsTrue(CharacterSpiritNameChoiceRules.TryProject(
            TargetId,
            [TargetId],
            "Spirit",
            "Spirit of Fire",
            ["Spirit of Fire", "Spirit of Water", "Spirit of Fire"],
            ["Spirit of Fire"],
            magicEnabled: true,
            resonanceEnabled: true,
            ["Guardian Spirit"],
            ["Companion Sprite"],
            out CharacterSpiritNameChoiceState? state));

        Assert.IsNotNull(state);
        CollectionAssert.AreEqual(
            new[] { "Spirit of Fire", "Guardian Spirit", "Companion Sprite" },
            state.AllowedNames.ToArray());
        Assert.IsTrue(CharacterSpiritNameChoiceRules.CanSet(state, "Guardian Spirit"));
        Assert.IsFalse(CharacterSpiritNameChoiceRules.CanSet(state, "Spirit of Water"));
    }

    [TestMethod]
    public void ParserProjectsCustomTraditionAndStableSpiritIdentity()
    {
        const string xml = """
<character>
  <created>False</created><magenabled>True</magenabled><resenabled>False</resenabled>
  <tradition><guid>82222222-8222-8222-8222-822222222222</guid><traditiontype>MAG</traditiontype><sourceid>616ba093-306c-45fc-8f41-0b98c8cccb46</sourceid><name>Custom</name><spiritcombat>Spirit of Fire</spiritcombat><spiritdetection>Spirit of Water</spiritdetection><spirithealth>Spirit of Earth</spirithealth><spiritillusion>Spirit of Man</spiritillusion><spiritmanipulation>Spirit of Air</spiritmanipulation><spirits /></tradition>
  <improvements><improvement><improvementttype>LimitSpiritCategory</improvementttype><improvedname>Spirit of Fire</improvedname><enabled>1</enabled></improvement><improvement><improvementttype>AddSpirit</improvementttype><improvedname>Guardian Spirit</improvedname><enabled>True</enabled></improvement></improvements>
  <spirits><spirit><guid>81111111-8111-8111-8111-811111111111</guid><name>Spirit of Fire</name><type>Spirit</type><force>4</force><services>1</services><bound>True</bound><fettered>False</fettered></spirit></spirits>
</character>
""";

        CharacterSpiritNameChoiceState? state = new CharacterSectionService()
            .ParseSpirits(xml)
            .Spirits.Single()
            .NameChoiceSemantics;

        Assert.IsNotNull(state);
        Assert.AreEqual(TargetId, state.SpiritId);
        CollectionAssert.AreEqual(
            new[] { "Spirit of Fire", "Guardian Spirit" },
            state.AllowedNames.ToArray());
    }

    [TestMethod]
    public void AllMarkerUsesExactCatalogOrFailsClosed()
    {
        const string xml = """
<character>
  <magenabled>False</magenabled><resenabled>True</resenabled>
  <tradition><guid>83333333-8333-8333-8333-833333333333</guid><traditiontype>RES</traditiontype><sourceid>7a3ecfbe-616e-425d-b204-329de37ffdbb</sourceid><name>Default</name><spirits><spirit>All</spirit></spirits></tradition>
  <improvements />
  <spirits><spirit><guid>81111111-8111-8111-8111-811111111111</guid><name>Machine Sprite</name><type>Sprite</type></spirit></spirits>
</character>
""";
        Assert.IsNull(new CharacterSectionService().ParseSpirits(xml).Spirits.Single().NameChoiceSemantics);

        CharacterSpiritNameChoiceState? state = new CharacterSectionService(new CatalogResolver())
            .ParseSpirits(xml)
            .Spirits.Single()
            .NameChoiceSemantics;
        Assert.IsNotNull(state);
        CollectionAssert.AreEqual(
            new[] { "Machine Sprite", "Courier Sprite" },
            state.AllowedNames.ToArray());
    }

    private sealed class CatalogResolver : ICharacterSourceDataResolver, ICharacterSourceDataContext
    {
        public ICharacterSourceDataContext TryCreateContext(string characterXml) => this;

        public bool TryResolveSpiritCatalogNames(string entityType, out IReadOnlyList<string> names)
        {
            names = ["Machine Sprite", "Courier Sprite"];
            return string.Equals(entityType, "Sprite", StringComparison.Ordinal);
        }

        public bool TryResolveTraditionSpiritNames(
            string entityType,
            string sourceId,
            out IReadOnlyList<string> names)
        {
            names = ["All"];
            return string.Equals(entityType, "Sprite", StringComparison.Ordinal)
                && string.Equals(sourceId, "7a3ecfbe-616e-425d-b204-329de37ffdbb", StringComparison.Ordinal);
        }

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 0;
            return false;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }
    }
}
