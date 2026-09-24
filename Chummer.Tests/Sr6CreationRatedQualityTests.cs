using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("focused-concentration", 3, 12)]
    [DataRow("built-tough", 4, 4)]
    [DataRow("will-to-live", 3, 8)]
    [DataRow("dependents", 3, -4)]
    public void Rated_qualities_have_exact_total_cost_and_one_family(string family, int maximum, int perLevel)
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        for (int level = 1; level <= maximum; level++)
        {
            var quote = fixture.Preview(seed with { Qualities = new([family + "-" + level]) });
            var row = quote.Qualities!.Values.Single();
            Assert.AreEqual(new Sr6CreationQualityRating(level, 0, level, perLevel), row.Rating);
            Assert.AreEqual(level * perLevel, row.KarmaCost);
            Assert.AreEqual(50 - level * perLevel, quote.Qualities.CustomizationKarma);
            Assert.HasCount(1, quote.Qualities.Values);
        }
        foreach (var ids in new string[][] { [family + "-0"], [family + "-" + (maximum + 1)],
            [family + "-1", family + "-2"], [family + "-01"] })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Qualities = new(ids) }).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("Priority", "human", 0)]
    [DataRow("Priority", "ork", 1)]
    [DataRow("Priority", "troll", 2)]
    [DataRow("SumtoTen", "human", 0)]
    [DataRow("SumtoTen", "ork", 1)]
    [DataRow("SumtoTen", "troll", 2)]
    [DataRow("PointBuy", "human", 0)]
    [DataRow("PointBuy", "ork", 1)]
    [DataRow("PointBuy", "troll", 2)]
    public void Built_tough_charges_only_above_metatype_grant_and_cold_reopens(string method, string metatype, int innate)
    {
        using var fixture = new Fixture(method);
        var seed = KarmaSeed(method) with { MetatypeId = metatype, Qualities = new(["built-tough-3"]) };
        var quote = fixture.Preview(seed);
        var row = quote.Qualities!.Values.Single();
        Assert.AreEqual(new Sr6CreationQualityRating(3, innate, 3 - innate, 4), row.Rating);
        Assert.AreEqual((3 - innate) * 4, row.KarmaCost);
        Assert.AreEqual(innate > 0, quote.SourceAnchorIds.Contains(Sr6CreationQualityRules.MetatypeUpgradeSourceAnchor));
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        for (int level = 1; level <= innate; level++)
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                seed with { Qualities = new(["built-tough-" + level]) }).Value);
        var upgrade = seed with { Qualities = new(["built-tough-4"]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(upgrade)).Value);
        var history = fixture.Store.Get(fixture.Id).Value!.Document.AuxiliaryState.Sr6CreationFoundationDecisions!;
        Assert.HasCount(2, history);
        Assert.AreEqual(3, history[0].Preview.Qualities!.Values.Single().Rating!.Total);
        Assert.AreEqual(4, history[1].Preview.Qualities!.Values.Single().Rating!.Total);
        Assert.AreEqual((4 - innate) * 4, history[1].Preview.Qualities!.PositiveKarmaCost);
    }

    [TestMethod]
    public void Metatype_upgrade_counts_as_one_choice_and_bonus_removal_preserves_purchases()
    {
        using var fixture = new Fixture();
        var ids = new[] { "built-tough-3", "analytical-mind", "ambidextrous", "hardening", "insomnia", "shaky-hands" };
        var seed = KarmaSeed("Priority") with { MetatypeId = "troll", Qualities = new(ids) };
        Assert.HasCount(6, fixture.Preview(seed).Qualities!.Values);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Qualities = new([.. ids, "bad-luck"]) }).Value);
        seed = seed with { Qualities = new(["dependents-3"]), Karma = new([], [], 62) };
        Assert.AreEqual(62, fixture.Preview(seed).Karma!.KarmaBudget);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Qualities = new(["dependents-2"]) }).Value);
        Assert.AreEqual(before.Document.AuxiliaryStateDigest, fixture.Store.Get(fixture.Id).Value!.Document.AuxiliaryStateDigest);
    }

    [TestMethod]
    public void Glass_jaw_validates_final_willpower_and_two_box_floor_not_a_fixed_rank_cap()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { Qualities = new(["glass-jaw-8", "focused-concentration-1"]) };
        var missing = fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Attributes = null });
        CollectionAssert.Contains(missing.Blockers.ToArray(), Sr6CreationQualityBlockers.AttributesRequired);
        var tooSmall = fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed);
        CollectionAssert.Contains(tooSmall.Blockers.ToArray(), Sr6CreationQualityBlockers.RatingUnavailable);
        seed = seed with { Karma = new([new("Willpower", 2)], [], 0) };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(3, quote.Karma!.Attributes.Single().Rating);
        Assert.AreEqual(25, quote.Karma.KarmaSpent);
        Assert.AreEqual(70, quote.Karma.KarmaBudget);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = null }).Value);
        // Maximum supported natural Willpower eight permits exactly ten removed boxes.
        seed = KarmaSeed("Priority") with { MetatypeId = "dwarf", Attributes = Spend(EmptyAttributes(), "Willpower", 7, 0),
            Qualities = new(["exceptional-Willpower", "built-tough-3", "glass-jaw-10"]) };
        Assert.AreEqual(8, fixture.Preview(seed).Attributes!.Values.Single(row => row.AttributeId == "Willpower").Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Attributes = Spend(EmptyAttributes(), "Willpower", 5, 0) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Qualities = new(["exceptional-Willpower", "built-tough-3", "glass-jaw-11"]) }).Value);
    }

    [TestMethod]
    public void Rehashed_innate_rating_or_purchase_delta_never_authorizes_a_saved_quality()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { MetatypeId = "troll", Qualities = new(["built-tough-3"]) };
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, fixture.Request(seed), out var candidate, out var decision));
        var quality = decision.Preview.Qualities!;
        var row = quality.Values.Single();
        foreach (var rating in new[] { row.Rating! with { Innate = 3 }, row.Rating! with { Purchased = 0 }, row.Rating! with { Total = 4 } })
        {
            var quote = decision.Preview with { Qualities = quality with { Values = [row with { Rating = rating }] } };
            quote = quote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(quote) };
            var forged = decision with { Preview = quote, Command = decision.Command with { PreviewDigest = quote.PreviewDigest } };
            forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
            var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
            Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
            Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
                Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
        }
    }
}
