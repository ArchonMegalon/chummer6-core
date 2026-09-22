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
    public void Karma_is_cumulative_separate_from_pools_and_reopens_once(string method)
    {
        using var fixture = new Fixture(method);
        var seed = KarmaSeed(method) with { Karma = new([new("Body", 2)], [new("Athletics", 1)], 10) };
        var before = fixture.Store.Get(fixture.Id).Value!;
        var quote = fixture.Preview(seed);
        Assert.AreEqual(45, quote.Karma!.KarmaSpent); // Body 10+15, Athletics 10, cash 10
        Assert.AreEqual(5, quote.Karma.KarmaRemaining);
        CollectionAssert.AreEqual(new[] { 10, 15 }, quote.Karma.Attributes.Single().Steps.Select(row => row.KarmaCost).ToArray());
        Assert.AreEqual(20000, quote.Karma.AdditionalNuyen);
        Assert.AreEqual(quote.Budget.ResourcesNuyen + 20000, quote.Karma.ResourcesNuyen);
        Assert.AreEqual(0, quote.Karma.UnspentAboveCarryOver);
        Assert.AreEqual(1, quote.Attributes!.Values.Single(row => row.AttributeId == "Body").Value);
        Assert.AreEqual(3, Sr6CreationKarmaRules.AttributeRating(quote, "Body"));
        Assert.AreEqual(2, Sr6CreationKarmaRules.SkillRating(quote, "Athletics"));
        Assert.AreEqual(fixture.Preview(seed with { Karma = null }).PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.AreEqual(1, state.KarmaOptions!.Attributes.Single(row => row.Id == "Body").BaseRating);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
    }

    [TestMethod]
    [DataRow("Priority", "adept")]
    [DataRow("Priority", "mystic-adept")]
    [DataRow("SumtoTen", "mystic-adept")]
    [DataRow("PointBuy", "adept")]
    [DataRow("PointBuy", "mystic-adept")]
    public void Karma_magic_grants_power_points_without_rebuying_or_increasing_prior_grants(string method, string talent)
    {
        using var fixture = new Fixture(method);
        var seed = (method == "PointBuy" ? PointBuy(talent: talent) : Ranked("talent", "A") with { TalentId = talent }) with
        { Attributes = EmptyAttributes(), Skills = new([]), TalentAllocation = new(talent == "mystic-adept" || method == "PointBuy" ? 1 : 0) };
        var original = fixture.Preview(seed);
        var quote = fixture.Preview(seed with { Karma = new([new("Magic", 1)], [], 0) });
        Assert.AreEqual(original.TalentAllocation!.Magic + 1, quote.TalentAllocation!.Magic);
        Assert.AreEqual(original.TalentAllocation.PowerPointBudget + 1, quote.TalentAllocation.PowerPointBudget);
        Assert.AreEqual(original.TalentAllocation.PowerPointCharacterPointCost, quote.TalentAllocation.PowerPointCharacterPointCost);
        Assert.AreEqual(original.TalentAllocation.FreeSpellOrRitualSlots, quote.TalentAllocation.FreeSpellOrRitualSlots);
        Assert.AreEqual(original.TalentAllocation.SpellOrRitualLimit, quote.TalentAllocation.SpellOrRitualLimit);
        Assert.AreEqual(1, quote.Karma!.AdditionalPowerPoints);
        Assert.AreEqual(Sr6CreationTalentRules.Options(original)!.MaximumSelectedPowerPoints,
            Sr6CreationTalentRules.Options(quote)!.MaximumSelectedPowerPoints);
    }

    [TestMethod]
    public void Karma_final_ratings_drive_knowledge_and_adept_limits_without_changing_pool_costs()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PowerSeed("PointBuy") with { Skills = new([new("Athletics", 2, [])]),
            Knowledge = new("English", [new(Guid.NewGuid(), "Seattle"), new(Guid.NewGuid(), "Magic")], []),
            Karma = new([new("Logic", 1), new("Body", 2)], [new("Athletics", 1)], 0),
            AdeptPowers = new([new("improved-attribute-body", 2), new("improved-ability-athletics-noncombat", 2)]) };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(50, quote.Karma!.KarmaSpent);
        Assert.AreEqual(2, quote.Knowledge!.Logic);
        Assert.AreEqual(2, quote.Skills!.PointsSpent);
        Assert.AreEqual(12, quote.AdeptPowers!.QuarterPointsSpent);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = null }).Value);
    }

    [TestMethod]
    public void Karma_enforces_creation_caps_magic_access_aspects_and_exotic_identity()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        foreach (string id in new[] { "Magic", "Resonance" })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = new([new(id, 1)], [], 0) }).Value);
        foreach (string id in new[] { "Sorcery", "Tasking", "Astral", "ExoticWeapons" })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = new([], [new(id, 1)], 0) }).Value);
        var exotic = fixture.Preview(seed with { Karma = new([], [new("ExoticWeapons", 2, "Whip")], 0) });
        Assert.AreEqual(15, exotic.Karma!.KarmaSpent);
        Assert.AreEqual("Whip", exotic.Karma.Skills.Single().FirstExoticSpecialization);
        var capped = seed with { Attributes = Spend(Spend(EmptyAttributes(), "Body", 5, 0), "Agility", 4, 0),
            Karma = new([new("Agility", 1)], [], 0) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, capped).Blockers.ToArray(),
            Sr6CreationAttributeBlockers.MaximumCountExceeded);
        capped = seed with { Skills = new([new("Athletics", 6, []), new("Firearms", 5, [])]), Karma = new([], [new("Firearms", 1)], 0) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, capped).Blockers.ToArray(),
            Sr6CreationSkillBlockers.MaximumCountExceeded);
        var aspect = Ranked("talent", "A") with { TalentId = "aspected-magician", Attributes = EmptyAttributes(),
            Skills = new([], "Sorcery"), Karma = new([], [new("Conjuring", 1)], 0) };
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, aspect).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_rejects_malformed_overspent_and_missing_base_allocations()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = KarmaSeed("PointBuy");
        foreach (var karma in new Sr6CreationKarmaSelection[] {
            new(null!, [], 0), new([], null!, 0), new([], [], -1), new([], [], 51),
            new([null!], [], 0), new([new("Body", 0)], [], 0), new([new("Body", int.MaxValue)], [], 0),
            new([new("Body", 1), new("Body", 1)], [], 0), new([new("body", 1)], [], 0),
            new([], [new("Athletics", 1, "x")], 0), new([], [new("ExoticWeapons", 1, " ")], 0),
            new([new("Body", 2)], [new("Athletics", 2)], 1), new([], [new("Athletics", 6)], 0) })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Karma = karma }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Attributes = null, Karma = new([], [], 0) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Skills = null, Karma = new([], [], 0) }).Value);
        var draft = fixture.Preview(seed with { Karma = new([], [], 0) });
        Assert.AreEqual(45, draft.Karma!.UnspentAboveCarryOver);
        Assert.AreEqual(50, draft.Karma.KarmaRemaining); // no silent truncation to the carry-over cap
    }

    [TestMethod]
    public void Karma_is_detached_canonical_and_rehashed_forged_costs_fail_cold_load()
    {
        using var fixture = new Fixture();
        var rows = new List<Sr6CreationKarmaIncrease> { new("Body", 1), new("Agility", 1) };
        var quote = fixture.Preview(KarmaSeed("Priority") with { Karma = new(rows, [], 0) });
        rows.Clear();
        Assert.HasCount(2, quote.Selection.Karma!.Attributes);
        Assert.AreEqual(quote.PreviewDigest, fixture.Preview(quote.Selection with
            { Karma = new([new("Agility", 1), new("Body", 1)], [], 0) }).PreviewDigest);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(quote.Selection);
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = decision.Preview with { Karma = decision.Preview.Karma! with { KarmaSpent = 0 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }

    private static Sr6CreationFoundationSelection KarmaSeed(string method)
        => (method == "PointBuy" ? PointBuy() : Selection(method)) with
        { Attributes = EmptyAttributes(), Skills = new([new("Athletics", 1, [])]) };
}
