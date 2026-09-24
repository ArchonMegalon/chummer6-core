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
    public void Gear_basket_uses_resource_and_karma_cash_preserves_pools_and_reopens(string method)
    {
        using var fixture = new Fixture(method);
        var id = Guid.NewGuid();
        var seed = (method == "PointBuy" ? PointBuy() : Selection(method)) with
        {
            Attributes = EmptyAttributes(), Skills = new([]), Karma = new([], [], 2),
            Gear = new([new(id, "lined-coat", 1), new(Guid.NewGuid(), "renraku-sensei", 1),
                new(Guid.NewGuid(), "credstick-standard", 2)])
        };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(1910m, quote.Gear!.SpentNuyen);
        Assert.AreEqual(quote.Budget.ResourcesNuyen + 4000m, quote.Gear.ResourcesNuyen);
        Assert.AreEqual(quote.Gear.ResourcesNuyen - 1910m, quote.Gear.RemainingNuyen);
        Assert.AreEqual(Math.Max(0m, quote.Gear.RemainingNuyen - 5000m), quote.Gear.UnspentAboveCarryOver);
        var without = fixture.Preview(seed with { Gear = null });
        Assert.AreEqual(without.Karma!.KarmaSpent, quote.Karma!.KarmaSpent);
        Assert.AreEqual(without.PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(2L, reopened.Binding.SavedRevision);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        seed = seed with { Gear = new([new(id, "lined-coat", 2)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(id, reopened.Selection!.Gear!.Items.Single().Choice.Id);
        Assert.AreEqual(1800m, reopened.Selection.Gear.SpentNuyen);
        Assert.AreEqual(3L, reopened.Binding.SavedRevision);
    }

    [TestMethod]
    [DataRow("human", 900, 185, 5)]
    [DataRow("elf", 900, 185, 5)]
    [DataRow("ork", 900, 185, 5)]
    [DataRow("dwarf", 990, 185, 5)]
    [DataRow("troll", 990, 203.5, 5.5)]
    public void Gear_size_costs_use_sr6_decimal_prices(string metatype, double armor, double weapon, double stick)
    {
        var catalog = Sr6CreationGearRules.Catalog(metatype);
        Assert.AreEqual((decimal)armor, catalog.Single(row => row.Id == "lined-coat").UnitPrice);
        Assert.AreEqual((decimal)weapon, catalog.Single(row => row.Id == "forearm-snap-blades").UnitPrice);
        Assert.AreEqual((decimal)stick, catalog.Single(row => row.Id == "credstick-standard").UnitPrice);
        Assert.AreEqual(catalog.Count, catalog.Select(row => row.Id).Distinct().Count());
        Assert.IsTrue(catalog.All(row => row.BasePrice > 0 && row.SourceAnchorId.StartsWith("sr6_core_de_2024:", StringComparison.Ordinal)));
        Assert.IsTrue(catalog.Single(row => row.Id == "transys-eidolon").Available);
        Assert.IsFalse(catalog.Single(row => row.Id == "ares-red-dog").Available);
        Assert.IsFalse(catalog.Single(row => row.Id == "aztechnology-tlaloc").Available);
        Assert.AreEqual(1500m, catalog.Single(row => row.Id == "medkit-6").BasePrice);
        Assert.AreEqual(1440m, catalog.Single(row => row.Id == "tranq-patch-12").BasePrice);
    }

    [TestMethod]
    public void Gear_illegal_items_are_warnings_not_invented_creation_bans_but_availability_and_budget_are_enforced()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PointBuy() with { Attributes = EmptyAttributes(), Skills = new([]), Karma = new([], [], 1),
            Gear = new([new(Guid.NewGuid(), "monofilament-whip", 1)]) };
        var quote = fixture.Preview(seed);
        Assert.IsTrue(quote.Gear!.RestrictedItemsNeedGmReview);
        Assert.AreEqual(1300m, quote.Gear.SpentNuyen);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = null }).Blockers.ToArray(),
            Sr6CreationGearBlockers.BudgetExceeded);
        foreach (var (item, reason) in new[] { ("ares-red-dog", Sr6CreationGearBlockers.AvailabilityExceeded),
                     ("sr5-weapon", Sr6CreationGearBlockers.CatalogUnavailable), ("fairlight-excalibur", Sr6CreationGearBlockers.BudgetExceeded) })
        {
            var result = fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                seed with { Gear = new([new(Guid.NewGuid(), item, 1)]) });
            CollectionAssert.Contains(result.Blockers.ToArray(), reason);
        }
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        Assert.AreEqual(0m, fixture.Preview(seed with { Gear = new([]) }).Gear!.SpentNuyen);
    }

    [TestMethod]
    public void Gear_shapes_reject_aliasing_duplicates_overflow_and_malformed_identifiers()
    {
        var row = new Sr6CreationGearChoice(Guid.NewGuid(), "lined-coat", 1);
        foreach (var selection in new Sr6CreationGearSelection[] { new(null!), new([null!]),
                     new([row with { Id = Guid.Empty }]), new([row, row]), new([row with { CatalogId = "" }]),
                     new([row with { CatalogId = "\ud800" }]), new([row with { CatalogId = " lined-coat" }]),
                     new([row with { Quantity = 0 }]), new([row with { Quantity = -1 }]), new([row with { Quantity = int.MaxValue }]),
                     new(Enumerable.Range(0, 129).Select(_ => row with { Id = Guid.NewGuid() }).ToArray()) })
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeGear(selection, out _));
        Sr6CreationGearChoice[] rows = [row, row with { Id = Guid.NewGuid() }];
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeGear(new(rows), out var frozen));
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeGear(new(rows.Reverse().ToArray()), out var reverse));
        Assert.AreEqual(Sr6CreationFoundationIntegrity.Digest(frozen), Sr6CreationFoundationIntegrity.Digest(reverse));
        rows[0] = row with { Quantity = 50 };
        Assert.IsTrue(frozen!.Items.All(item => item.Quantity == 1));
    }

    [TestMethod]
    public void Gear_rehashed_price_forgery_is_rejected_on_cold_load()
    {
        using var fixture = new Fixture();
        var quote = fixture.Preview(Selection() with { Gear = new([new(Guid.NewGuid(), "lined-coat", 1)]) });
        var request = new Sr6CreationFoundationConfirmRequest(quote.Binding, quote.Selection, quote.PreviewDigest, Guid.NewGuid(), true);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var item = quote.Gear!.Items.Single();
        var forgedQuote = quote with { Gear = quote.Gear with { SpentNuyen = 0, RemainingNuyen = quote.Gear.ResourcesNuyen,
            Items = [item with { TotalPrice = 0, Option = item.Option with { UnitPrice = 0 } }] } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
