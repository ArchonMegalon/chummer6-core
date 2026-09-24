using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    private static Sr6CreationFoundationSelection PointBuy(string metatype = "human", string talent = "mundane")
        => new(metatype, talent, []) { PointBuy = new(0, 0, 0, 0) };

    [TestMethod]
    [DataRow("human")]
    [DataRow("elf")]
    [DataRow("dwarf")]
    [DataRow("ork")]
    [DataRow("troll")]
    public void Point_buy_has_own_free_pools_without_priority_or_metatype_cp_cost(string metatype)
    {
        using var fixture = new Fixture("PointBuy");
        var state = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(Sr6CreationPointBuyRules.Limits(), state.PointBuyLimits);
        Assert.IsTrue(state.Metatypes.All(row => row.AllowedRanks.Count == 0));
        var quote = fixture.Preview(PointBuy(metatype));
        Assert.AreEqual(new Sr6CreationPriorityBudget(4, 12, 0, 1, null), quote.Budget);
        Assert.AreEqual(100, quote.PointBuy!.PointsRemaining);
        Assert.AreEqual(50, quote.PointBuy.CustomizationKarma);
        Assert.IsFalse(quote.PointBuy.AllCharacterPointsSpent);
        Assert.AreEqual(0, quote.PointBuy.FreeSpells + quote.PointBuy.FreeComplexForms + quote.PointBuy.FreePowerPoints);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), Sr6CreationPointBuyRules.SourceAnchor);
    }

    [TestMethod]
    [DataRow("mundane", 0, 0, 0)]
    [DataRow("magician", 10, 1, 0)]
    [DataRow("aspected-magician", 10, 2, 0)]
    [DataRow("adept", 10, 1, 0)]
    [DataRow("mystic-adept", 10, 1, 0)]
    [DataRow("technomancer", 10, 0, 1)]
    public void Point_buy_talent_cost_and_base_rating_are_not_priority_grants(string talent, int cost, int magic, int resonance)
    {
        using var fixture = new Fixture("PointBuy");
        var quote = fixture.Preview(PointBuy(talent: talent));
        Assert.AreEqual(cost, quote.PointBuy!.TalentCost);
        Assert.AreEqual(cost, quote.PointBuy.PointsSpent);
        Assert.AreEqual(magic, quote.BaseMagic);
        Assert.AreEqual(resonance, quote.BaseResonance);
        Assert.AreEqual(0, quote.PointBuy.FreeSpells + quote.PointBuy.FreeComplexForms + quote.PointBuy.FreePowerPoints);
    }

    [TestMethod]
    public void Point_buy_complete_pool_purchase_and_allocations_save_and_reopen_atomically()
    {
        using var fixture = new Fixture("PointBuy");
        var attributes = Spend(Spend(Spend(EmptyAttributes(), "Logic", 2, 0), "Resonance", 0, 5), "Edge", 0, 2);
        var selection = PointBuy(talent: "technomancer") with { PointBuy = new(16, 16, 6, 2), Attributes = attributes,
            Skills = new([new("Tasking", 5, ["Compiling"])]), Knowledge = new("German", [new(Guid.NewGuid(), "Matrix history")], []) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(new Sr6CreationPriorityBudget(20, 28, 30000, 7, null), quote.Budget);
        Assert.AreEqual(100, quote.PointBuy!.PointsSpent);
        Assert.IsTrue(quote.PointBuy.AllCharacterPointsSpent);
        Assert.AreEqual(0, quote.PointBuy.PointsRemaining);
        Assert.AreEqual(6, quote.Attributes!.Values.Single(row => row.AttributeId == "Resonance").Value);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), Sr6CreationAttributeRules.SourceAnchor);
        Assert.AreEqual(6, quote.Skills!.PointsSpent);
        Assert.AreEqual(3, quote.Knowledge!.Logic);
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(2L, reopened.Binding.SavedRevision);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        var smaller = selection with { PointBuy = new(16, 16, 0, 2) };
        CollectionAssert.Contains(cold.Preview(fixture.Stamp, reopened.Binding, smaller).Blockers.ToArray(),
            Sr6CreationAttributeBlockers.AdjustmentBudgetExceeded);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Point_buy_rejects_caps_negative_overflow_budget_and_priority_mixing_without_writes()
    {
        using var fixture = new Fixture("PointBuy");
        foreach (var invalid in new Sr6CreationPointBuySelection[] {
            new(-1, 0, 0, 0), new(21, 0, 0, 0), new(0, 21, 0, 0), new(0, 0, 13, 0), new(0, 0, 0, 31),
            new(int.MaxValue, 0, 0, 0), new(0, 0, 0, int.MaxValue) })
        {
            var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding, PointBuy() with { PointBuy = invalid });
            Assert.IsNull(result.Value);
        }
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            PointBuy() with { PointBuy = new(20, 20, 12, 30) }).Blockers.ToArray(), Sr6CreationPointBuyBlockers.BudgetExceeded);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, Selection()).Value);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            PointBuy() with { MetatypeId = "" }).Blockers.ToArray(), Sr6CreationPointBuyBlockers.InvalidSelection);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, PointBuy() with { Assignments = Selection().Assignments }).Value);
        using var priority = new Fixture();
        Assert.IsNull(priority.Service.Preview(priority.Stamp, priority.Binding, PointBuy()).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        var cap = fixture.Preview(PointBuy() with { PointBuy = new(0, 0, 0, 30) });
        Assert.AreEqual(450000, cap.Budget.ResourcesNuyen);
        Assert.AreEqual(30, cap.PointBuy!.PointsSpent);
    }

    [TestMethod]
    public void Point_buy_adjustment_rules_do_not_inherit_priority_reduced_maximum_exception()
    {
        using var fixture = new Fixture("PointBuy");
        var quote = fixture.Preview(PointBuy("dwarf"));
        var options = Sr6CreationAttributeRules.Options(quote);
        Assert.IsFalse(options.Single(row => row.AttributeId == "Reaction").AllowsAdjustmentPoints);
        Assert.IsTrue(options.Single(row => row.AttributeId == "Body").AllowsAdjustmentPoints);
        Assert.IsTrue(options.Single(row => row.AttributeId == "Edge").AllowsAdjustmentPoints);
        var invalid = PointBuy("dwarf") with { Attributes = Spend(EmptyAttributes(), "Reaction", 0, 1) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, invalid).Blockers.ToArray(),
            Sr6CreationAttributeBlockers.PointKindUnavailable);
        var valid = fixture.Preview(PointBuy("dwarf") with { Attributes = Spend(EmptyAttributes(), "Body", 0, 1) });
        Assert.AreEqual(2, valid.Attributes!.Values.Single(row => row.AttributeId == "Body").Value);
    }

    [TestMethod]
    public void Point_buy_projection_is_recomputed_even_after_forged_digest_resealing()
    {
        using var fixture = new Fixture("PointBuy");
        var request = fixture.Request(PointBuy());
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var quote = decision.Preview with { PointBuy = decision.Preview.PointBuy! with { PointsRemaining = 1000 } };
        quote = quote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(quote) };
        var forged = decision with { Preview = quote, Command = request with { PreviewDigest = quote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
