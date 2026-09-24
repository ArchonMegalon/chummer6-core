using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Lifestyle_shares_gear_cash_preserves_old_decisions_and_cold_reopens(string method)
    {
        using var fixture = new Fixture(method);
        var seed = (method == "PointBuy" ? PointBuy() : Selection(method)) with
        {
            Attributes = EmptyAttributes(), Skills = new([]), Karma = new([], [], 5),
            Gear = new([new(Guid.NewGuid(), "lined-coat", 2)])
        };
        var first = fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value!;
        string original = Sr6CreationFoundationIntegrity.Digest(first.Decision);
        seed = seed with { Lifestyle = new("low", 2) };
        var quote = fixture.Preview(seed);
        var cash = quote.Lifestyle!;
        Assert.AreEqual(4000m, cash.LifestyleSpentNuyen);
        Assert.AreEqual(1800m, cash.GearSpentNuyen);
        Assert.AreEqual(quote.Karma!.ResourcesNuyen, cash.ResourcesNuyen);
        Assert.AreEqual(cash.ResourcesNuyen - 5800m, cash.RemainingNuyen);
        Assert.AreEqual(Math.Min(5000m, cash.RemainingNuyen), cash.ProjectedStartingNuyen);
        Assert.AreEqual(Math.Max(0m, cash.RemainingNuyen - 5000m), cash.UnspentAboveCarryOver);
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(first.Decision.Preview.Gear),
            Sr6CreationFoundationIntegrity.Digest(quote.Gear), "Existing gear quote remains a gear-only projection.");
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(3L, reopened.Binding.SavedRevision);
        Assert.AreEqual(original, Sr6CreationFoundationIntegrity.Digest(store.Get(fixture.Id).Value!
            .Document.AuxiliaryState.Sr6CreationFoundationDecisions![0]));
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(6, reopened.LifestyleOptions!.Count);
        // Replacing, not accumulating, prepaid months; no automatic cash discard.
        seed = seed with { Lifestyle = new("street", 1) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        Assert.AreEqual(cash.ResourcesNuyen - 1800m, cold.Load(fixture.Stamp, fixture.Id).Value!.Selection!.Lifestyle!.RemainingNuyen);
    }

    [TestMethod]
    public void Lifestyle_budget_rechecks_gear_edits_cash_reductions_and_exact_carry_over()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PointBuy() with { Attributes = EmptyAttributes(), Skills = new([]), Karma = new([], [], 5),
            Lifestyle = new("middle", 1) };
        var quote = fixture.Preview(seed).Lifestyle!;
        Assert.AreEqual(5000m, quote.RemainingNuyen);
        Assert.AreEqual(5000m, quote.ProjectedStartingNuyen);
        Assert.AreEqual(0m, quote.UnspentAboveCarryOver);
        Assert.AreEqual(0m, fixture.Preview(seed with { Lifestyle = new("middle", 2) }).Lifestyle!.RemainingNuyen);
        foreach (var bad in new[] { seed with { Lifestyle = new("luxury", 1) },
                     seed with { Karma = new([], [], 2) },
                     seed with { Gear = new([new(Guid.NewGuid(), "lined-coat", 6)]) } })
            CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, bad).Blockers.ToArray(),
                Sr6CreationLifestyleBlockers.BudgetExceeded);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        Assert.AreEqual(2000m, fixture.Preview(seed with { Lifestyle = new("low", 1) }).Lifestyle!.Option.MonthlyNuyen);
        Assert.AreEqual(500m, fixture.Preview(seed with { Lifestyle = new("squatter", 1) }).Lifestyle!.Option.MonthlyNuyen);
        // The book allows arbitrary whole-month prepayment, not an invented six-month rule.
        Assert.AreEqual(3500m, fixture.Preview(seed with { Lifestyle = new("squatter", 7) }).Lifestyle!.LifestyleSpentNuyen);
    }

    [TestMethod]
    public void Lifestyle_shapes_catalog_and_large_month_counts_fail_closed_without_overflow()
    {
        using var fixture = new Fixture();
        foreach (var choice in new[] { new Sr6CreationLifestyleSelection("low", 0), new("low", -1),
                     new("", 1), new(" low", 1), new("\ud800", 1), new(null!, 1) })
        {
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeLifestyle(choice, out _));
            CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                Selection() with { Lifestyle = choice }).Blockers.ToArray(), Sr6CreationLifestyleBlockers.InvalidSelection);
        }
        foreach (string id in new[] { "Low", "hospitalized", "permanent", "sr5-low" })
            CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                Selection() with { Lifestyle = new(id, 1) }).Blockers.ToArray(), Sr6CreationLifestyleBlockers.Unavailable);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            Selection() with { Lifestyle = new("luxury", int.MaxValue) }).Blockers.ToArray(), Sr6CreationLifestyleBlockers.BudgetExceeded);
        CollectionAssert.AreEqual(new decimal[] { 0, 500, 2000, 5000, 10000, 100000 },
            Sr6CreationLifestyleRules.Catalog().Select(row => row.MonthlyNuyen).ToArray());
    }

    [TestMethod]
    public void Lifestyle_rehashed_price_forgery_is_rejected_on_cold_load()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Lifestyle = new("low", 1) });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = quote with { Lifestyle = quote.Lifestyle! with { LifestyleSpentNuyen = 0,
            Option = quote.Lifestyle.Option with { MonthlyNuyen = 0 } } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
