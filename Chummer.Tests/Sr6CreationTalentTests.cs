using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow("Priority", "magician", "Magic", 8, 0, 0)]
    [DataRow("Priority", "technomancer", "Resonance", 0, 8, 0)]
    [DataRow("Priority", "adept", "Magic", 0, 0, 6)]
    [DataRow("Priority", "mystic-adept", "Magic", 8, 0, 0)]
    [DataRow("SumtoTen", "magician", "Magic", 8, 0, 0)]
    [DataRow("SumtoTen", "technomancer", "Resonance", 0, 8, 0)]
    [DataRow("SumtoTen", "adept", "Magic", 0, 0, 6)]
    [DataRow("SumtoTen", "mystic-adept", "Magic", 8, 0, 0)]
    public void Talent_free_slots_use_priority_base_but_adept_power_uses_final_magic(
        string method, string talent, string attribute, int spells, int forms, int power)
    {
        using var fixture = new Fixture(method);
        var selection = Ranked("talent", "A") with { TalentId = talent,
            Attributes = Spend(EmptyAttributes(), attribute, 0, 2), TalentAllocation = new(0) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(spells, quote.TalentAllocation!.FreeSpellOrRitualSlots);
        Assert.AreEqual(forms, quote.TalentAllocation.FreeComplexFormSlots);
        Assert.AreEqual(power, quote.TalentAllocation.PowerPointBudget);
        Assert.AreEqual(0, quote.TalentAllocation.PowerPointCharacterPointCost);
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), Sr6CreationTalentRules.SourceAnchor);
    }

    [TestMethod]
    public void Mystic_priority_split_cannot_spend_adjusted_magic_twice()
    {
        using var fixture = new Fixture();
        var selection = Ranked("talent", "A") with { TalentId = "mystic-adept",
            Attributes = Spend(EmptyAttributes(), "Magic", 0, 2), TalentAllocation = new(3) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(6, quote.TalentAllocation!.Magic);
        Assert.AreEqual(3, quote.TalentAllocation.PowerPointBudget);
        Assert.AreEqual(2, quote.TalentAllocation.FreeSpellOrRitualSlots);
        var invalid = fixture.Service.Preview(fixture.Stamp, fixture.Binding, selection with { TalentAllocation = new(5) });
        CollectionAssert.Contains(invalid.Blockers.ToArray(), Sr6CreationTalentBlockers.PowerPointLimit);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    [DataRow("Sorcery", 10, 0)]
    [DataRow("Enchanting", 0, 10)]
    [DataRow("Conjuring", 0, 0)]
    public void Aspected_talent_uses_one_exact_skill_aspect(string aspect, int spells, int alchemy)
    {
        using var fixture = new Fixture();
        var selection = Ranked("talent", "A") with { TalentId = "aspected-magician",
            Attributes = Spend(EmptyAttributes(), "Magic", 0, 1), Skills = new([], aspect), TalentAllocation = new(0) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(6, quote.TalentAllocation!.Magic);
        Assert.AreEqual(spells, quote.TalentAllocation.FreeSpellOrRitualSlots);
        Assert.AreEqual(alchemy, quote.TalentAllocation.FreeAlchemicalSpellSlots);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, selection with { Skills = null }).Value);
    }

    [TestMethod]
    [DataRow("adept", 8, 0)]
    [DataRow("mystic-adept", 16, 2)]
    public void Point_buy_purchases_power_points_from_cp_and_limits_spells_after_split(string talent, int cost, int spellLimit)
    {
        using var fixture = new Fixture("PointBuy");
        var selection = PointBuy(talent: talent) with { PointBuy = new(0, 0, 2, 0),
            Attributes = Spend(EmptyAttributes(), "Magic", 0, 2), TalentAllocation = new(2) };
        var quote = fixture.Preview(selection);
        Assert.AreEqual(3, quote.TalentAllocation!.Magic);
        Assert.AreEqual(2, quote.TalentAllocation.PowerPointBudget);
        Assert.AreEqual(cost, quote.TalentAllocation.PowerPointCharacterPointCost);
        Assert.AreEqual(spellLimit, quote.TalentAllocation.SpellOrRitualLimit);
        Assert.AreEqual(0, quote.TalentAllocation.FreeSpellOrRitualSlots);
        Assert.AreEqual(2, quote.TalentAllocation.CharacterPointsPerSpellOrForm);
        Assert.AreEqual(18 + cost, quote.PointBuy!.PointsSpent);
        Assert.AreEqual(82 - cost, quote.PointBuy.PointsRemaining);
        Assert.AreEqual(cost, quote.PointBuy.PowerPointCost);
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        Assert.AreEqual(quote.PreviewDigest, cold.Load(fixture.Stamp, fixture.Id).Value!.Selection!.PreviewDigest);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.IsNull(cold.Preview(fixture.Stamp, cold.Load(fixture.Stamp, fixture.Id).Value!.Binding,
            selection with { Attributes = EmptyAttributes() }).Value, "Lowering Magic must not silently discard purchased powers.");
    }

    [TestMethod]
    public void Point_buy_form_cap_uses_final_resonance_without_free_grants()
    {
        using var fixture = new Fixture("PointBuy");
        var quote = fixture.Preview(PointBuy(talent: "technomancer") with { PointBuy = new(0, 0, 4, 0),
            Attributes = Spend(EmptyAttributes(), "Resonance", 0, 5), TalentAllocation = new(0) });
        Assert.AreEqual(12, quote.TalentAllocation!.ComplexFormLimit);
        Assert.AreEqual(0, quote.TalentAllocation.FreeComplexFormSlots);
        Assert.AreEqual(26, quote.PointBuy!.PointsSpent);
    }

    [TestMethod]
    public void Talent_purchase_rejects_missing_attributes_wrong_kind_invalid_input_and_cp_overspend()
    {
        using var fixture = new Fixture("PointBuy");
        var selection = PointBuy(talent: "adept") with { TalentAllocation = new(1) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, selection).Blockers.ToArray(),
            Sr6CreationTalentBlockers.AttributesRequired);
        foreach (int points in new[] { -1, 7, int.MaxValue })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
                selection with { Attributes = EmptyAttributes(), TalentAllocation = new(points) }).Value);
        var full = selection with { PointBuy = new(20, 20, 0, 10), Attributes = EmptyAttributes() };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, full).Blockers.ToArray(),
            Sr6CreationPointBuyBlockers.BudgetExceeded);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            selection with { TalentId = "magician", Attributes = EmptyAttributes() }).Value);
        var exact = fixture.Preview(full with { PointBuy = new(20, 20, 0, 6) });
        Assert.AreEqual(100, exact.PointBuy!.PointsSpent);
        Assert.IsTrue(exact.PointBuy.AllCharacterPointsSpent);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Redigested_talent_entitlement_forgery_is_recomputed_on_load()
    {
        using var fixture = new Fixture();
        var request = fixture.Request(Selection() with { Attributes = EmptyAttributes(), TalentAllocation = new(0) });
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var quote = decision.Preview with { TalentAllocation = decision.Preview.TalentAllocation! with { FreeComplexFormSlots = 12 } };
        quote = quote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(quote) };
        var forged = decision with { Preview = quote, Command = request with { PreviewDigest = quote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }
}
