using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterLifestyleIncrementRulesTests
{
    private static readonly Guid LifestyleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [TestMethod]
    public void CreationDirectEntryUsesExactLegacyBounds()
    {
        CharacterLifestyleIncrementState state = State(careerMode: false, increments: 2);

        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state,
            CharacterLifestyleIncrementAction.SetCreation,
            0).Exact);
        Assert.AreEqual(
            100,
            CharacterLifestyleIncrementRules.Quote(
                state,
                CharacterLifestyleIncrementAction.SetCreation,
                100).UpdatedIncrements);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state,
            CharacterLifestyleIncrementAction.SetCreation,
            101).Exact);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state with { TotalIncrementCostExact = false },
            CharacterLifestyleIncrementAction.SetCreation,
            2).Exact);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state,
            CharacterLifestyleIncrementAction.IncreaseCareer).Exact);
    }

    [TestMethod]
    public void CareerIncreaseRequiresExactAffordableSavedTotalCost()
    {
        CharacterLifestyleIncrementState state = State(
            careerMode: true,
            increments: 4,
            nuyen: 8_000m,
            totalIncrementCost: 2_500m);
        CharacterLifestyleIncrementQuote quote = CharacterLifestyleIncrementRules.Quote(
            state,
            CharacterLifestyleIncrementAction.IncreaseCareer);

        Assert.IsTrue(quote.Exact);
        Assert.AreEqual(5, quote.UpdatedIncrements);
        Assert.AreEqual(-2_500m, quote.NuyenDelta);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state with { Nuyen = 2_499m },
            CharacterLifestyleIncrementAction.IncreaseCareer).Exact);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            state with { TotalIncrementCostExact = false },
            CharacterLifestyleIncrementAction.IncreaseCareer).Exact);
    }

    [TestMethod]
    public void CareerDecreasePreservesLegacyUnboundedNegativeBehavior()
    {
        CharacterLifestyleIncrementQuote quote = CharacterLifestyleIncrementRules.Quote(
            State(careerMode: true, increments: 0, nuyenExact: false),
            CharacterLifestyleIncrementAction.DecreaseCareer);

        Assert.IsTrue(quote.Exact);
        Assert.AreEqual(-1, quote.UpdatedIncrements);
        Assert.AreEqual(0m, quote.NuyenDelta);
        Assert.IsFalse(CharacterLifestyleIncrementRules.Quote(
            State(careerMode: true, increments: 0, totalCostExact: false),
            CharacterLifestyleIncrementAction.DecreaseCareer).Exact);
    }

    [TestMethod]
    public void ProjectionCarriesSavedCareerAuthorityAndDefaultsUnknownUnitToMonth()
    {
        const string xml = """
            <character><created>True</created><nuyen>8000</nuyen><lifestyles><lifestyle>
              <guid>11111111-1111-1111-1111-111111111111</guid>
              <name>Low</name><baselifestyle>Low</baselifestyle><months>4</months>
              <totalmonthlycost>2500.50</totalmonthlycost><increment>unexpected</increment>
            </lifestyle></lifestyles></character>
            """;

        CharacterLifestyleIncrementState state = new CharacterSectionService()
            .ParseLifestyles(xml)
            .Lifestyles.Single()
            .IncrementState!;

        Assert.AreEqual(LifestyleId, state.LifestyleId);
        Assert.AreEqual(4, state.Increments);
        Assert.AreEqual(CharacterLifestyleIncrementUnit.Month, state.Unit);
        Assert.IsTrue(state.CareerMode);
        Assert.IsTrue(state.NuyenExact);
        Assert.AreEqual(8_000m, state.Nuyen);
        Assert.IsTrue(state.TotalIncrementCostExact);
        Assert.AreEqual(2_500.50m, state.TotalIncrementCost);
        Assert.AreEqual("Low", state.DisplayName);
    }

    private static CharacterLifestyleIncrementState State(
        bool careerMode,
        int increments,
        decimal nuyen = 10_000m,
        decimal totalIncrementCost = 2_000m,
        bool nuyenExact = true,
        bool totalCostExact = true)
        => new(
            LifestyleId,
            increments,
            CharacterLifestyleIncrementUnit.Month,
            careerMode,
            nuyen,
            nuyenExact,
            totalIncrementCost,
            totalCostExact,
            "Low");
}
