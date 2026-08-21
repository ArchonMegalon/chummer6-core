using Chummer.Contracts.Characters;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterTraditionNameRulesTests
{
    [TestMethod]
    public void Identity_MatchesTheExactChummer5CustomMagicalTradition()
    {
        Assert.AreEqual(
            Guid.Parse("616ba093-306c-45fc-8f41-0b98c8cccb46"),
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId);
    }

    [TestMethod]
    public void TryValidate_PreservesExactSingleLineTextAndEmptyValue()
    {
        Assert.IsTrue(CharacterTraditionNameRules.TryValidate("  Vienna Hermetic  ", out string exact));
        Assert.AreEqual("  Vienna Hermetic  ", exact);
        Assert.IsTrue(CharacterTraditionNameRules.TryValidate(string.Empty, out string empty));
        Assert.AreEqual(string.Empty, empty);
    }

    [TestMethod]
    public void TryValidate_RejectsValuesTheLegacySingleLineControlCannotSubmit()
    {
        Assert.IsFalse(CharacterTraditionNameRules.TryValidate(null, out _));
        Assert.IsFalse(CharacterTraditionNameRules.TryValidate("one\ntwo", out _));
        Assert.IsFalse(CharacterTraditionNameRules.TryValidate("one\rtwo", out _));
        Assert.IsFalse(CharacterTraditionNameRules.TryValidate("one\0two", out _));
        Assert.IsFalse(CharacterTraditionNameRules.TryValidate(
            new string('x', CharacterTraditionNameRules.MaximumLength + 1),
            out _));
    }
}
