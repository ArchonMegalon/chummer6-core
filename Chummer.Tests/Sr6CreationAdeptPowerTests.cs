using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("Priority", "adept")]
    [DataRow("SumtoTen", "adept")]
    [DataRow("PointBuy", "adept")]
    [DataRow("Priority", "mystic-adept")]
    [DataRow("SumtoTen", "mystic-adept")]
    [DataRow("PointBuy", "mystic-adept")]
    public void Adept_power_quarters_save_reopen_and_replay(string method, string talent)
    {
        using var fixture = new Fixture(method);
        var seed = PowerSeed(method, talent) with { AdeptPowers = new([new("astral-perception", 1), new("mystic-armor", 1)]) };
        var before = fixture.Store.Get(fixture.Id).Value!;
        var quote = fixture.Preview(seed);
        Assert.AreEqual(5, quote.AdeptPowers!.QuarterPointsSpent);
        Assert.AreEqual(method != "PointBuy" && talent == "adept" ? 24 : 12, quote.AdeptPowers.QuarterPointBudget);
        Assert.AreEqual(fixture.Preview(seed with { AdeptPowers = null }).PointBuy?.PointsSpent, quote.PointBuy?.PointsSpent);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.HasCount(51, state.AdeptPowerOptions!);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Every_core_power_and_variant_can_be_selected_with_its_actual_prerequisites()
    {
        using var fixture = new Fixture("PointBuy");
        var skills = Sr6CreationSkillIds.Ordered.Where(id => id is not ("Sorcery" or "Conjuring" or "Enchanting" or "Tasking"))
            .Select(id => new Sr6CreationSkillSpend(id, 1, id == "ExoticWeapons" ? ["Whip"] : [])).ToArray();
        var seed = PowerSeed("PointBuy") with { PointBuy = new(0, 3, 2, 0), Skills = new(skills),
            AdeptPowers = new([new("astral-perception", 1)]) };
        var options = Sr6CreationAdeptPowerRules.Options(fixture.Preview(seed))!;
        Assert.HasCount(51, options);
        Assert.AreEqual(options.Count, options.Select(row => row.Id).Distinct().Count());
        foreach (var option in options)
        {
            var choices = option.Id == "astral-perception" ? seed.AdeptPowers.Choices
                : new Sr6CreationAdeptPowerChoice[] { new("astral-perception", 1), new(option.Id, 1) };
            var quote = fixture.Preview(seed with { AdeptPowers = new(choices) });
            Assert.AreEqual(option, quote.AdeptPowers!.Powers.Single(row => row.Option.Id == option.Id).Option);
        }
        Assert.IsFalse(options.Any(row => row.SubjectId is "Sorcery" or "Conjuring" or "Enchanting" or "Tasking"));
    }

    [TestMethod]
    public void Improved_abilities_use_natural_ratings_and_distinguish_combat_scope()
    {
        using var fixture = new Fixture();
        var seed = PowerSeed("Priority") with { Skills = new([new("Athletics", 3, []), new("Firearms", 1, [])]) };
        var normal = fixture.Preview(seed with { AdeptPowers = new([new("improved-ability-athletics-noncombat", 2)]) });
        Assert.AreEqual(4, normal.AdeptPowers!.QuarterPointsSpent);
        Assert.AreEqual("noncombat", normal.AdeptPowers.Powers.Single().Option.UseId);
        var all = fixture.Preview(seed with { AdeptPowers = new([new("improved-ability-athletics-all", 2)]) });
        Assert.AreEqual(8, all.AdeptPowers!.QuarterPointsSpent);
        foreach (var choices in new Sr6CreationAdeptPowerChoice[][] {
            [new("improved-ability-athletics-all", 3)], [new("improved-ability-firearms-all", 2)],
            [new("improved-ability-perception-all", 1)],
            [new("improved-ability-athletics-all", 1), new("improved-ability-athletics-noncombat", 1)] })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { AdeptPowers = new(choices) }).Value);
        var physical = Sr6CreationAdeptPowerRules.Options(fixture.Preview(seed))!.Single(row => row.Id == "improved-attribute-agility");
        Assert.AreEqual(1, physical.MaximumRating); // natural Agility 1 -> ceiling 2, increase 1
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { AdeptPowers = new([new(physical.Id, 2)]) }).Value);
    }

    [TestMethod]
    public void Astral_skill_requires_a_valid_paid_power_in_the_same_atomic_draft()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PowerSeed("PointBuy") with { Skills = new([new("Astral", 1, [])]),
            AdeptPowers = new([new("astral-perception", 1), new("improved-ability-astral-noncombat", 1)]) };
        var quote = fixture.Preview(seed);
        Assert.IsTrue(Sr6CreationSkillRules.Options(quote).Single(row => row.SkillId == "Astral").Available);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { AdeptPowers = new([]) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { TalentAllocation = new(0) }).Value);
        Assert.AreEqual(6, fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.Selection!.AdeptPowers!.QuarterPointsSpent);
    }

    [TestMethod]
    public void Powers_reject_reduced_budgets_rating_caps_and_malformed_inputs_without_writes()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PowerSeed("PointBuy");
        foreach (var choices in new Sr6CreationAdeptPowerChoice[][] {
            [new("astral-perception", 2)], [new("mystic-armor", 4)], [new("improved-reflexes", 4)],
            [new("astral-perception", 1), new("astral-perception", 1)], [new("unknown", 1)],
            [new("mystic-armor", 0)], [new("mystic-armor", 7)], [new("Mystic-Armor", 1)],
            [new(null!, 1)], [null!], Enumerable.Repeat(new Sr6CreationAdeptPowerChoice("kinesics", 1), 25).ToArray() })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { AdeptPowers = new(choices) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { AdeptPowers = new(null!) }).Value);
        var choicesToSave = seed with { AdeptPowers = new([new("astral-perception", 1), new("mystic-armor", 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(choicesToSave)).Value);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            choicesToSave with { TalentAllocation = new(1) }).Blockers.ToArray(), Sr6CreationAdeptPowerBlockers.BudgetExceeded);
        foreach (string talent in new[] { "mundane", "magician", "aspected-magician", "technomancer" })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, PointBuy(talent: talent) with
                { Attributes = EmptyAttributes(), TalentAllocation = new(0), AdeptPowers = new([]) }).Value);
        Assert.AreEqual(2L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
        using var priority = new Fixture();
        Assert.IsNull(priority.Service.Preview(priority.Stamp, priority.Binding,
            PowerSeed("Priority") with { AdeptPowers = new([new("improved-reflexes", 5)]) }).Value);
        Assert.HasCount(1, priority.Preview(PowerSeed("Priority") with
            { AdeptPowers = new([new("improved-reflexes", 4)]) }).AdeptPowers!.WarningIds);
    }

    [TestMethod]
    public void Power_selections_are_frozen_and_rehashed_saved_costs_are_recomputed()
    {
        using var fixture = new Fixture("PointBuy");
        var rows = new List<Sr6CreationAdeptPowerChoice> { new("mystic-armor", 1), new("astral-perception", 1) };
        var quote = fixture.Preview(PowerSeed("PointBuy") with { AdeptPowers = new(rows) });
        rows.Add(new("kinesics", 1));
        Assert.HasCount(2, quote.Selection.AdeptPowers!.Choices);
        Assert.AreEqual(quote.PreviewDigest, fixture.Preview(PowerSeed("PointBuy") with
            { AdeptPowers = new([new("astral-perception", 1), new("mystic-armor", 1)]) }).PreviewDigest);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(quote.Selection);
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = decision.Preview with { AdeptPowers = decision.Preview.AdeptPowers! with { QuarterPointsSpent = 0 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }

    private static Sr6CreationFoundationSelection PowerSeed(string method, string talent = "adept")
        => SpellSeed(method, talent) with { TalentAllocation = new(method == "PointBuy" || talent == "mystic-adept" ? 3 : 0) };
}
