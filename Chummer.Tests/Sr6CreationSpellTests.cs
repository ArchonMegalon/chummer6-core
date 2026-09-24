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
    public void Spells_and_rituals_share_one_creation_pool_save_and_cold_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var selection = SpellSeed(method) with { Spells = new(["spell-heal", "ritual-ward", "spell-increase-attribute"]) };
        var original = fixture.Store.Get(fixture.Id).Value!;
        var preview = fixture.Preview(selection);
        Assert.AreEqual(3, preview.Spells!.Spells.Count);
        Assert.AreEqual(method == "PointBuy" ? 6 : 8, preview.Spells.Limit);
        Assert.AreEqual(method == "PointBuy" ? 0 : 3, preview.Spells.FreeSlotsUsed);
        Assert.AreEqual(method == "PointBuy" ? 6 : 0, preview.Spells.CharacterPointCost);
        Assert.AreEqual("sorcery-and-enchanting", preview.Spells.UseId);
        if (method == "PointBuy")
        {
            Assert.AreEqual(24, preview.PointBuy!.PointsSpent);
            Assert.AreEqual(6, preview.PointBuy.SpellCost);
        }
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(preview.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.AreEqual(original.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
        Assert.HasCount(81, state.SpellOptions!);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
        Assert.IsNull(cold.Preview(fixture.Stamp, state.Binding, selection with { TalentAllocation = null }).Value);
    }

    [TestMethod]
    public void Every_core_spell_and_ritual_has_a_distinct_source_identity_without_synthetic_cast_variants()
    {
        using var fixture = new Fixture();
        var catalog = Sr6CreationSpellRules.Catalog();
        Assert.HasCount(81, catalog);
        Assert.AreEqual(73, catalog.Count(row => row.Kind == "spell"));
        Assert.AreEqual(8, catalog.Count(row => row.Kind == "ritual"));
        Assert.AreEqual(catalog.Count, catalog.Select(row => row.Id).Distinct().Count());
        foreach (var option in catalog)
        {
            var preview = fixture.Preview(SpellSeed("Priority") with { Spells = new([option.Id]) });
            Assert.AreEqual(option, preview.Spells!.Spells.Single());
            Assert.IsTrue(option.SourceAnchorId.StartsWith("sr6_core_de_2024:p", StringComparison.Ordinal));
        }
        // Attribute/element/trigger are casting choices, not separate purchases.
        Assert.IsFalse(catalog.Any(row => row.Id == "spell-increase-body" || row.Id.StartsWith("alchemy-", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Spell_aspects_have_distinct_access_without_duplicate_alchemy_costs(string method)
    {
        using var fixture = new Fixture(method);
        var seed = SpellSeed(method, "aspected-magician") with
        { Attributes = Spend(EmptyAttributes(), "Magic", 0, 1), Skills = new([], "Enchanting"), Spells = new(["spell-heal"]) };
        var quote = fixture.Preview(seed);
        Assert.AreEqual("enchanting", quote.Spells!.UseId);
        Assert.AreEqual(method == "PointBuy" ? 6 : 10, quote.Spells.Limit);
        Assert.AreEqual(method == "PointBuy" ? 2 : 0, quote.Spells.CharacterPointCost);
        Assert.AreEqual(73, Sr6CreationSpellRules.Options(quote)!.Count);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Spells = new(["ritual-ward"]) }).Value);
        var sorcerer = fixture.Preview(seed with { Skills = new([], "Sorcery"), Spells = new(["ritual-ward", "spell-heal"]) });
        Assert.AreEqual("sorcery", sorcerer.Spells!.UseId);
        Assert.AreEqual(81, Sr6CreationSpellRules.Options(sorcerer)!.Count);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with
            { Skills = new([], "Conjuring") }).Blockers.ToArray(), Sr6CreationSpellBlockers.TalentRequired);
        Assert.IsNull(Sr6CreationSpellRules.Options(fixture.Preview(seed with { Skills = new([], "Conjuring"), Spells = null })));
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("PointBuy")]
    public void Spell_purchases_respect_mystic_split_and_reject_reduced_limits_without_dropping_choices(string method)
    {
        using var fixture = new Fixture(method);
        var seed = SpellSeed(method, "mystic-adept") with { TalentAllocation = new(method == "PointBuy" ? 2 : 3),
            Spells = new(["spell-heal", "ritual-ward"]) };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(2, quote.Spells!.Limit);
        if (method == "PointBuy")
        {
            Assert.AreEqual(16, quote.PointBuy!.PowerPointCost);
            Assert.AreEqual(4, quote.PointBuy.SpellCost);
            Assert.AreEqual(38, quote.PointBuy.PointsSpent);
        }
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var state = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, state.Binding,
            seed with { TalentAllocation = new(method == "PointBuy" ? 3 : 4) }).Value);
        Assert.AreEqual(2, fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.Selection!.Spells!.Spells.Count);
    }

    [TestMethod]
    public void Spell_cp_exhaustion_and_invalid_inputs_are_rejected_before_mutation()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = SpellSeed("PointBuy") with { PointBuy = new(20, 20, 2, 0), Spells = new(["spell-heal"]) };
        Assert.AreEqual(100, fixture.Preview(seed).PointBuy!.PointsSpent);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { Spells = new(["spell-heal", "ritual-ward"]) }).Blockers.ToArray(), Sr6CreationPointBuyBlockers.BudgetExceeded);
        seed = SpellSeed("PointBuy");
        foreach (var ids in new string[][] { ["spell-heal", "spell-heal"], ["Spell-Heal"], ["spell-heal\n"],
                     [""], [null!], ["sr5-spell"], ["alchemy-heal"], Enumerable.Repeat("spell-heal", 13).ToArray() })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Spells = new(ids) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { Spells = new(null!) }).Value);
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with
            { Spells = new(Sr6CreationSpellRules.Catalog().Take(7).Select(row => row.Id).ToArray()) }).Blockers.ToArray(), Sr6CreationSpellBlockers.LimitExceeded);
        foreach (string talent in new[] { "mundane", "adept", "technomancer" })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, PointBuy(talent: talent) with
                { Attributes = EmptyAttributes(), TalentAllocation = new(0), Spells = new([]) }).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Spell_freeze_is_detached_canonical_and_redigested_forgery_is_not_authority()
    {
        using var fixture = new Fixture("PointBuy");
        var ids = new List<string> { "spell-heal", "ritual-ward" };
        var seed = SpellSeed("PointBuy") with { Spells = new(ids) };
        var quote = fixture.Preview(seed);
        ids.Add("spell-increase-attribute");
        Assert.HasCount(2, quote.Selection.Spells!.CatalogIds);
        Assert.AreEqual(quote.PreviewDigest, fixture.Preview(seed with { Spells = new(["ritual-ward", "spell-heal"]) }).PreviewDigest);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(quote.Selection);
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = decision.Preview with { Spells = decision.Preview.Spells! with { CharacterPointCost = 0 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }

    private static Sr6CreationFoundationSelection SpellSeed(string method, string talent = "magician")
        => (method == "PointBuy" ? PointBuy(talent: talent) with { PointBuy = new(0, 0, 2, 0) }
            : Ranked("talent", "A") with { TalentId = talent }) with
        { Attributes = Spend(EmptyAttributes(), "Magic", 0, 2), TalentAllocation = new(0) };
}
