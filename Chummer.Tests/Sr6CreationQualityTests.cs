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
    public void Quality_costs_change_customization_not_CP_and_survive_cold_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var seed = KarmaSeed(method) with { Qualities = new(["analytical-mind", "ar-vertigo"]), Karma = new([], [], 57) };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(3, quote.Qualities!.PositiveKarmaCost);
        Assert.AreEqual(10, quote.Qualities.NegativeKarmaBonus);
        Assert.AreEqual(57, quote.Karma!.KarmaBudget);
        Assert.AreEqual(114000, quote.Karma.AdditionalNuyen);
        Assert.AreEqual(fixture.Preview(seed with { Qualities = null, Karma = null }).PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = new([], [], 58) }).Value);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var loaded = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, loaded.Selection!.PreviewDigest);
        Assert.AreEqual(57, loaded.KarmaOptions!.KarmaBudget);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
        // Removing a bonus never silently drops already bought Karma improvements/cash.
        Assert.IsNull(cold.Preview(fixture.Stamp, loaded.Binding,
            seed with { Qualities = new(["analytical-mind"]) }).Value);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Exceptional_attribute_and_aptitude_raise_caps_not_ratings_and_remain_required()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { Qualities = new(["exceptional-Logic", "aptitude-Athletics"]) };
        var baseQuote = fixture.Preview(seed);
        Assert.AreEqual(1, baseQuote.Attributes!.Values.Single(row => row.AttributeId == "Logic").Value);
        Assert.AreEqual(7, baseQuote.Attributes.Values.Single(row => row.AttributeId == "Logic").Maximum);
        Assert.AreEqual(7, Sr6CreationSkillRules.Options(baseQuote).Single(row => row.SkillId == "Athletics").Maximum);
        var capped = seed with { Attributes = new(seed.Attributes!.Allocations.Select(row => row.AttributeId == "Logic"
            ? row with { AttributePoints = 6 } : row).ToArray()), Skills = new([new("Athletics", 7, [])]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(capped)).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, capped with { Qualities = null }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            capped with { Skills = new([new("Athletics", 7, []), new("Firearms", 6, [])]) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            capped with { Skills = new([new("Firearms", 7, [])]) }).Value);
        // Karma uses the same changed cap; five ranks cannot fit this adjusted budget.
        var karmaCap = seed with { Qualities = new(["aptitude-Athletics", "ar-vertigo", "bad-luck"]),
            Skills = new([new("Athletics", 6, [])]), Karma = new([], [new("Athletics", 1)], 0) };
        Assert.AreEqual(7, fixture.Preview(karmaCap).Karma!.Skills.Single().Rating);
        Assert.AreEqual(35, fixture.Preview(karmaCap).Karma!.KarmaSpent);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            karmaCap with { Karma = new([], [new("Athletics", 2)], 0) }).Value);
    }

    [TestMethod]
    public void Quality_count_bonus_cost_and_availability_limits_fail_without_writes()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        foreach (var ids in new string[][] {
            ["ar-vertigo", "astral-beacon", "bad-luck"],
            ["first-impression", "photographic-memory", "long-reach", "double-jointed", "catlike"],
            ["aptitude-Athletics", "aptitude-Firearms"], ["blandness", "distinctive-style"],
            ["human-looking"], ["unknown"], ["exceptional-Edge"], ["aptitude-unknown"]
        }) Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Qualities = new(ids) }).Value);
        var six = new[] { "analytical-mind", "ambidextrous", "hardening", "high-pain-tolerance", "insomnia", "shaky-hands" };
        Assert.HasCount(6, fixture.Preview(seed with { Qualities = new(six) }).Qualities!.Values);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Qualities = new([.. six, "bad-luck"]) }).Value);
        Assert.AreEqual(20, fixture.Preview(seed with { Qualities = new(["ar-vertigo", "bad-luck"]) }).Qualities!.NetKarmaBonus);
        var magical = Ranked("talent", "A") with { TalentId = "magician",
            Qualities = new(["implant-rejection"]) };
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, magical).Value);
        var dwarf = Ranked("heritage", "C") with { MetatypeId = "dwarf" };
        Assert.IsNotNull(fixture.Preview(dwarf));
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            dwarf with { Qualities = new(["toxin-resistance"]) }).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Quality_shape_is_bounded_detached_and_empty_is_legacy_compatible()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        Assert.AreEqual(fixture.Preview(seed).PreviewDigest,
            fixture.Preview(seed with { Qualities = new([]) }).PreviewDigest);
        foreach (var ids in new string[][] { null!, [null!], [""], [" ar-vertigo"], ["a\nb"],
            [new string('x', 81)], ["ar-vertigo", "ar-vertigo"] })
            Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeQualities(new(ids), out _));
        var values = new List<string> { "ar-vertigo", "analytical-mind" };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeQualities(new(values), out var frozen));
        string hash = Sr6CreationFoundationIntegrity.Digest(frozen);
        values.Reverse();
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeQualities(new(values), out var reordered));
        Assert.AreEqual(hash, Sr6CreationFoundationIntegrity.Digest(reordered));
        values.Clear();
        Assert.HasCount(2, frozen!.OptionIds);
    }

    [TestMethod]
    public void Rehashed_quality_cost_or_budget_is_rejected_on_load()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority") with { Qualities = new(["analytical-mind"]) };
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, fixture.Request(seed), out var candidate, out var decision));
        var quality = decision.Preview.Qualities!;
        foreach (var altered in new[] { quality with { CustomizationKarma = 99 },
            quality with { Values = [quality.Values.Single() with { KarmaCost = 0 }] } })
        {
            var quote = decision.Preview with { Qualities = altered };
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
