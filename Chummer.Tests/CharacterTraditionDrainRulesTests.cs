using Chummer.Contracts.Characters;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterTraditionDrainRulesTests
{
    private static readonly Guid TraditionId = Guid.Parse("d87f03c0-8820-4f5f-8362-c05bcbacb64d");
    private static readonly string[] SourceExpressions =
        ["{WIL} + {CHA}", "{WIL} + {INT}", "{WIL} + {LOG}"];

    [TestMethod]
    public void CustomMagicalTradition_AllowsExactSourceExpressionsAndBlank()
    {
        Assert.IsTrue(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId,
            CharacterTraditionNameRules.CustomMagicalTraditionSourceId,
            "MAG",
            adeptEnabled: false,
            magicianEnabled: true,
            "{WIL} + {CHA}",
            SourceExpressions,
            out CharacterTraditionDrainSemantics semantics));
        CollectionAssert.AreEqual(
            new[] { string.Empty, "{WIL} + {CHA}", "{WIL} + {INT}", "{WIL} + {LOG}" },
            semantics.AllowedExpressions.ToArray());
        Assert.IsTrue(CharacterTraditionDrainRules.TryValidateRequestedExpression(
            "{WIL} + {LOG}", semantics.AllowedExpressions, out string validated));
        Assert.AreEqual("{WIL} + {LOG}", validated);
        Assert.IsTrue(CharacterTraditionDrainRules.TryValidateRequestedExpression(
            string.Empty, semantics.AllowedExpressions, out _));
    }

    [TestMethod]
    public void EmptyPublishedTradition_IsSelectableExactlyOnce()
    {
        Guid published = Guid.Parse("19320625-bc1a-492f-8904-da6a847e5700");
        Assert.IsTrue(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId, published, "MAG", false, true, string.Empty, SourceExpressions, out _));
        Assert.IsFalse(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId, published, "MAG", false, true, "{WIL} + {LOG}", SourceExpressions, out _));
    }

    [TestMethod]
    public void AdeptOnlyAndUntrustedSourceData_FailClosed()
    {
        Guid custom = CharacterTraditionNameRules.CustomMagicalTraditionSourceId;
        Assert.IsFalse(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId, custom, "MAG", true, false, string.Empty, SourceExpressions, out _));
        Assert.IsFalse(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId, custom, "RES", false, true, string.Empty, SourceExpressions, out _));
        Assert.IsFalse(CharacterTraditionDrainRules.TryCreateSemantics(
            TraditionId, custom, "MAG", false, true, string.Empty,
            ["{WIL} + {CHA}", "{WIL} + {CHA}"], out _));
        Assert.IsFalse(CharacterTraditionDrainRules.TryValidateRequestedExpression(
            "{WIL} + {BOD}", [string.Empty, .. SourceExpressions], out _));
    }
}
