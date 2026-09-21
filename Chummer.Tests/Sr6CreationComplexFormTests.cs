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
    public void Complex_forms_use_edition_catalog_and_correct_budget_then_cold_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var selection = FormSeed(method) with { ComplexForms = new([
            new("editor"), new("diffusion-firewall"), new("emulate-toolbox")]) };
        var before = fixture.Store.Get(fixture.Id).Value!;
        var preview = fixture.Preview(selection);
        Assert.AreEqual(3, preview.ComplexForms!.Forms.Count);
        Assert.AreEqual(method == "PointBuy" ? 6 : 8, preview.ComplexForms.Limit);
        Assert.AreEqual(method == "PointBuy" ? 0 : 3, preview.ComplexForms.FreeSlotsUsed);
        Assert.AreEqual(method == "PointBuy" ? 6 : 0, preview.ComplexForms.CharacterPointCost);
        if (method == "PointBuy")
        {
            Assert.AreEqual(24, preview.PointBuy!.PointsSpent);
            Assert.AreEqual(76, preview.PointBuy.PointsRemaining);
            Assert.AreEqual(6, preview.PointBuy.ComplexFormCost);
        }
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var coldStore = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(coldStore, fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(preview.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.AreEqual(before.Document.Content, coldStore.Get(fixture.Id).Value!.Document.Content);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, coldStore.Get(fixture.Id).Value!.ContentRevision);
        Assert.HasCount(71, state.ComplexFormOptions!);
        Assert.IsNull(cold.Preview(fixture.Stamp, state.Binding, selection with
            { TalentAllocation = null }).Value, "No free forms from an absent talent budget.");
    }

    [TestMethod]
    public void Complex_form_variants_have_distinct_source_identities_and_subject_validation()
    {
        using var fixture = new Fixture();
        var seed = FormSeed("Priority");
        var catalog = Sr6CreationComplexFormRules.Catalog();
        Assert.AreEqual(catalog.Count, catalog.Select(row => row.Id).Distinct().Count());
        // Every option goes through real foundation validation, including the
        // closed action/program variants and GM-reviewed weapon/drone subjects.
        foreach (var option in catalog)
        {
            var choice = new Sr6CreationComplexFormChoice(option.Id, option.SubjectKind is null ? null : "GM model");
            var preview = fixture.Preview(seed with { ComplexForms = new([choice]) });
            var value = preview.ComplexForms!.Forms.Single();
            Assert.AreEqual(option.SourceName, value.SourceName);
            Assert.AreEqual(option.SourceAnchorId, value.SourceAnchorId);
            Assert.AreEqual(option.SubjectKind is not null, value.SubjectNeedsGmReview);
        }
        var choices = new Sr6CreationComplexFormChoice[] { new("diffusion-firewall"), new("diffusion-attack"),
            new("emulate-overclock-matrix-search"), new("emulate-overclock-hide"),
            new("emulate-autosoft-targeting", "Ares Alpha"), new("emulate-autosoft-targeting", "HK-227") };
        Assert.AreEqual(6, fixture.Preview(seed with { ComplexForms = new(choices) }).ComplexForms!.Forms.Count);
        Assert.AreEqual(fixture.Preview(seed with { ComplexForms = new(choices) }).PreviewDigest,
            fixture.Preview(seed with { ComplexForms = new(choices.Reverse().ToArray()) }).PreviewDigest);
        foreach (var bad in new Sr6CreationComplexFormChoice[][] {
            [new("editor"), new("editor")], [new("editor", "fake variant")],
            [new("emulate-autosoft-targeting")], [new("emulate-autosoft-targeting", " ")],
            [new("emulate-autosoft-targeting", "Ares Alpha"), new("emulate-autosoft-targeting", "ares alpha")],
            [new("sr5-form")], [new("Editor")], [new("editor\n")],
            [new("emulate-autosoft-targeting", "e\u0301")], [null!] })
            Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { ComplexForms = new(bad) }).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, seed with { ComplexForms = new(null!) }).Value);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Complex_form_caps_cp_and_talent_are_checked_without_silent_loss()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = FormSeed("PointBuy");
        var choices = Sr6CreationComplexFormRules.Catalog().Take(7)
            .Select(row => new Sr6CreationComplexFormChoice(row.Id)).ToArray();
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            seed with { ComplexForms = new(choices) }).Blockers.ToArray(), Sr6CreationComplexFormBlockers.LimitExceeded);
        var full = seed with { PointBuy = new(20, 20, 2, 2), ComplexForms = new([new("editor")]) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, full).Blockers.ToArray(),
            Sr6CreationPointBuyBlockers.BudgetExceeded);
        var exact = fixture.Preview(full with { PointBuy = new(20, 20, 2, 0) });
        Assert.AreEqual(100, exact.PointBuy!.PointsSpent);
        Assert.IsTrue(exact.PointBuy.AllCharacterPointsSpent);
        Assert.AreEqual(2, exact.PointBuy.ComplexFormCost);
        var savedChoice = seed with { ComplexForms = new(choices.Take(3).ToArray()) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(savedChoice)).Value);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, savedChoice with { Attributes = EmptyAttributes() }).Value);
        Assert.AreEqual(3, fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.Selection!.ComplexForms!.Forms.Count);
        var mundane = PointBuy() with { Attributes = EmptyAttributes(), TalentAllocation = new(0), ComplexForms = new([]) };
        CollectionAssert.Contains(fixture.Service.Preview(fixture.Stamp, fixture.Binding, mundane).Blockers.ToArray(),
            Sr6CreationComplexFormBlockers.TalentRequired);
    }

    [TestMethod]
    public void Complex_form_freeze_detaches_lists_and_load_rejects_redigested_cost_forgery()
    {
        using var fixture = new Fixture("PointBuy");
        var mutable = new List<Sr6CreationComplexFormChoice> { new("editor") };
        var seed = FormSeed("PointBuy") with { ComplexForms = new(mutable) };
        var quote = fixture.Preview(seed);
        mutable.Add(new("cleaner"));
        Assert.AreEqual(1, quote.Selection.ComplexForms!.Choices.Count);
        var request = fixture.Request(quote.Selection);
        var saved = fixture.Store.Get(fixture.Id).Value!;
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var forgedQuote = decision.Preview with { ComplexForms = decision.Preview.ComplexForms! with { CharacterPointCost = 0 } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }

    private static Sr6CreationFoundationSelection FormSeed(string method)
        => (method == "PointBuy" ? PointBuy(talent: "technomancer") with { PointBuy = new(0, 0, 2, 0) }
            : Ranked("talent", "A") with { TalentId = "technomancer" }) with
        { Attributes = Spend(EmptyAttributes(), "Resonance", 0, 2), TalentAllocation = new(0) };
}
