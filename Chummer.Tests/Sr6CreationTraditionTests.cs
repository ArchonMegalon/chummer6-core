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
    public void Tradition_selection_resolves_magical_tradition_and_materializes(string method)
    {
        using var fixture = new Fixture(method);
        var seed = SpellSeed(method, "mystic-adept") with
        {
            TalentAllocation = new(method == "PointBuy" ? 2 : 3),
            Spells = new(["spell-heal"]),
            Tradition = new("shinto"),
        };
        var preview = fixture.Preview(seed);
        Assert.IsNotNull(preview.Tradition);
        Assert.AreEqual("shinto", preview.Tradition.Tradition.Id);
        Assert.AreEqual("Shinto", preview.Tradition.Tradition.Name);
        Assert.IsNotNull(preview.Tradition.AuthorityDigest);
        CollectionAssert.Contains(preview.SourceAnchorIds.ToArray(), Sr6CreationTraditionRules.SourceAnchor);

        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        Assert.IsFalse(review.Value.Blockers.Any(row => row.Contains("magical-tradition")),
            string.Join(",", review.Value.Blockers));

        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(preview.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.IsNotNull(state.Selection.Selection.Tradition);
        Assert.AreEqual("shinto", state.Selection.Selection.Tradition.TraditionId);
        Assert.IsNotNull(state.TraditionOptions);
        Assert.IsTrue(state.TraditionOptions.Any(row => row.Id == "shinto"));
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
    }

    [TestMethod]
    public void Tradition_guard_retained_when_unresolved()
    {
        using var fixture = new Fixture("Priority");
        var seed = PowerSeed("Priority", "mystic-adept") with
        {
            Spells = new(["spell-heal"]),
            AdeptPowers = new([new("astral-perception", 1)]),
        };
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var review = fixture.Service.ReviewFinalization(fixture.Stamp, fixture.Binding);
        Assert.IsNotNull(review.Value, string.Join(",", review.Blockers));
        // Controller9659 guard: unresolved tradition still blocks finalization.
        Assert.IsTrue(review.Value.Blockers.Any(row => row.Contains("magical-tradition")),
            string.Join(",", review.Value.Blockers));
        Assert.IsFalse(review.Value.CanFinalize);
    }

    [TestMethod]
    public void Tradition_selection_rejects_unknown_and_mundane_talent()
    {
        using var fixture = new Fixture("PointBuy");
        var mundane = fixture.Preview(PointBuy(talent: "mundane") with { Attributes = EmptyAttributes() });
        // Mundane talent: no tradition options offered.
        Assert.IsNull(Sr6CreationTraditionRules.Options(mundane));

        var magical = fixture.Preview(SpellSeed("PointBuy"));
        var options = Sr6CreationTraditionRules.Options(magical)!;
        Assert.HasCount(12, options);
        Assert.AreEqual(options.Count, options.Select(row => row.Id).Distinct().Count());

        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            SpellSeed("PointBuy") with { Tradition = new("not-a-tradition") }).Value);
    }

    [TestMethod]
    public void Tradition_freeze_rejects_oversized_and_noncanonical_ids()
    {
        Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeTradition(new("A"), out _));
        Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeTradition(new("Shinto"), out _));
        Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeTradition(new("has space"), out _));
        Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeTradition(new(new string('a', 101)), out _));
        Assert.IsTrue(Sr6CreationFoundationIntegrity.TryFreezeTradition(new("chaos-magic"), out var frozen));
        Assert.AreEqual("chaos-magic", frozen!.TraditionId);
    }

    [TestMethod]
    public void Tradition_digest_is_stable_and_catalog_is_unique()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = SpellSeed("PointBuy") with { Tradition = new("hermetic") };
        var first = fixture.Preview(seed);
        var second = fixture.Preview(seed);
        Assert.IsNotNull(first.Tradition);
        Assert.AreEqual(first.Tradition!.AuthorityDigest, second.Tradition!.AuthorityDigest);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(seed),
            System.Text.Json.JsonSerializer.Serialize(second.Selection));
        var catalog = Sr6CreationTraditionRules.Catalog();
        Assert.AreEqual(catalog.Count, catalog.Select(row => row.Id).Distinct().Count());
        // Digest must vary with the selected tradition.
        var other = fixture.Preview(seed with { Tradition = new("shinto") });
        Assert.AreNotEqual(first.Tradition.AuthorityDigest, other.Tradition!.AuthorityDigest);
    }
}
