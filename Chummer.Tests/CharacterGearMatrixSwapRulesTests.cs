using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterGearMatrixSwapRulesTests
{
    [TestMethod]
    public void SleazeUsesTypedSharedRawSwapAuthorityInBothPhases()
    {
        var identity = new CharacterGearMatrixSwapIdentity([Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]);
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, false, true, "Deck", "7", "{Rating}", "5", "4", out var creation));
        Assert.AreEqual(CharacterGearMatrixSwapPhase.Creation, creation.Phase);
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryValidateMutation(creation, creation.Revision,
            CharacterGearMatrixAttribute.Sleaze, CharacterGearMatrixAttribute.DataProcessing));
        Assert.IsFalse(CharacterGearMatrixSwapRules.TryValidateMutation(creation, creation.Revision,
            CharacterGearMatrixAttribute.Sleaze, CharacterGearMatrixAttribute.Sleaze));
        Assert.AreEqual("{Rating}", CharacterGearMatrixSwapRules.Read(creation, CharacterGearMatrixAttribute.Sleaze));
        Assert.AreEqual("dataprocessing", CharacterGearMatrixSwapRules.ElementName(CharacterGearMatrixAttribute.DataProcessing));
        Assert.IsTrue(CharacterGearMatrixSwapRules.TryCreateState(
            identity, true, true, "Deck", "7", "{Rating}", "5", "4", out var career));
        Assert.AreEqual(CharacterGearMatrixSwapPhase.Career, career.Phase);
        Assert.AreNotEqual(creation.Revision, career.Revision);
    }
}
