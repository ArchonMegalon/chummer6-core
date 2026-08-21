using Chummer.Contracts.Characters;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGroupNameRulesTests
{
    [TestMethod]
    public void TryValidate_PreservesExactSingleLineTextAndEmptyValue()
    {
        Assert.IsTrue(CharacterGroupNameRules.TryValidate("  Hermetic Circle  ", out string exact));
        Assert.AreEqual("  Hermetic Circle  ", exact);
        Assert.IsTrue(CharacterGroupNameRules.TryValidate(string.Empty, out string empty));
        Assert.AreEqual(string.Empty, empty);
    }

    [TestMethod]
    public void TryValidate_RejectsValuesTheLegacySingleLineControlCannotSubmit()
    {
        Assert.IsFalse(CharacterGroupNameRules.TryValidate(null, out _));
        Assert.IsFalse(CharacterGroupNameRules.TryValidate("one\ntwo", out _));
        Assert.IsFalse(CharacterGroupNameRules.TryValidate("one\rtwo", out _));
        Assert.IsFalse(CharacterGroupNameRules.TryValidate("one\0two", out _));
        Assert.IsFalse(CharacterGroupNameRules.TryValidate(
            new string('x', CharacterGroupNameRules.MaximumLength + 1),
            out _));
    }
}
