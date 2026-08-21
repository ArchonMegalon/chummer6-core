using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterLifestyleNameProjectionTests
{
    [TestMethod]
    public void ParseLifestyles_ProjectsStableIdentityAndExactCustomName()
    {
        const string xml = """
            <character><lifestyles><lifestyle>
              <guid>11111111-1111-1111-1111-111111111111</guid>
              <name>Low</name><baselifestyle>Low</baselifestyle>
              <extra>Safehouse</extra><notes>Preserved notes</notes><notesColor>#123456</notesColor>
              <source>SR5</source><cost>2000</cost><months>3</months>
            </lifestyle></lifestyles></character>
            """;

        CharacterLifestyleSummary lifestyle = new CharacterSectionService()
            .ParseLifestyles(xml)
            .Lifestyles
            .Single();

        Assert.AreEqual("11111111-1111-1111-1111-111111111111", lifestyle.Guid);
        Assert.AreEqual("Safehouse", lifestyle.CustomName);
        Assert.AreEqual("Preserved notes", lifestyle.Notes);
        Assert.AreEqual("#123456", lifestyle.NotesColor);
        Assert.AreEqual("Low", lifestyle.Name);
    }
}
