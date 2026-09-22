using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class Sr6CreationFoundationTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Karma_specialty_on_existing_mystic_adept_preserves_all_domain_anchors(bool withKnowledge)
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PointBuy(talent: "mystic-adept") with
        {
            PointBuy = new(0, 0, 2, 0),
            Attributes = new(EmptyAttributes().Allocations.Select(row => row.AttributeId == "Magic"
                ? row with { AdjustmentPoints = 2 } : row).ToArray()),
            Skills = new([new("Astral", 1, [])]),
            TalentAllocation = new(2),
            Spells = new(["ritual-ward", "spell-heal"]),
            AdeptPowers = new([new("astral-perception", 1), new("mystic-armor", 2)]),
            Knowledge = withKnowledge ? new("English", [new(Guid.NewGuid(), "Seattle")], []) : null
        };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        seed = seed with { Karma = new([new("Body", 1), new("Magic", 1)], [new("Astral", 1)], 5) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var before = fixture.Store.Get(fixture.Id).Value!;
        seed = seed with { Karma = seed.Karma! with { Specializations = [new("Astral", "AstralCombat")] } };
        var request = fixture.Request(seed);
        var quote = fixture.Preview(seed);
        Assert.HasCount(withKnowledge ? 10 : 9, quote.SourceAnchorIds);
        Assert.AreEqual(50, quote.Karma!.KarmaSpent);
        var committed = fixture.Service.Confirm(fixture.Stamp, request);
        Assert.IsNotNull(committed.Value, string.Join(",", committed.Blockers));
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var reopened = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, reopened.Selection!.PreviewDigest);
        Assert.AreEqual(4L, reopened.Binding.ContentRevision);
        Assert.AreEqual(4L, reopened.Binding.SavedRevision);
        Assert.HasCount(3, store.Get(fixture.Id).Value!.Document.AuxiliaryState.Sr6CreationFoundationDecisions!);
        Assert.AreEqual(before.Document.Content, store.Get(fixture.Id).Value!.Document.Content);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        var saved = store.Get(fixture.Id).Value!;
        var last = saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions!.Last();
        var oversized = last.Preview with { SourceAnchorIds = Enumerable.Range(0,
            Sr6CreationFoundationIntegrity.MaximumSourceAnchors + 1).Select(i => "forged:" + i).ToArray() };
        oversized = oversized with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(oversized) };
        var forged = last with { Preview = oversized, Command = last.Command with { PreviewDigest = oversized.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        Assert.IsFalse(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, saved.ContentRevision,
            saved.Document.AuxiliaryState with { Sr6CreationFoundationDecisions =
                [.. saved.Document.AuxiliaryState.Sr6CreationFoundationDecisions.Take(2), forged] }));
    }

    [TestMethod]
    [DataRow("Priority")]
    [DataRow("SumtoTen")]
    [DataRow("PointBuy")]
    public void Karma_specializations_charge_once_preserve_pools_and_reopen(string method)
    {
        using var fixture = new Fixture(method);
        var seed = KarmaSeed(method) with { Karma = new([], [new("ExoticWeapons", 1, "Whip")], 0)
            { Specializations = [new("Athletics", "Climbing"), new("ExoticWeapons", "Monowhip")] } };
        var quote = fixture.Preview(seed);
        Assert.AreEqual(15, quote.Karma!.KarmaSpent);
        Assert.AreEqual(1, quote.Skills!.PointsSpent);
        var specialties = quote.Karma.Specializations!;
        Assert.HasCount(2, specialties);
        Assert.AreEqual(2, specialties.Single(row => row.SkillId == "Athletics").DicePoolBonus);
        Assert.AreEqual(0, specialties.Single(row => row.SkillId == "ExoticWeapons").DicePoolBonus);
        Assert.IsTrue(specialties.All(row => row.KarmaCost == 5 && row.NeedsGmReview));
        CollectionAssert.Contains(quote.SourceAnchorIds.ToArray(), Sr6CreationKarmaRules.SpecializationSourceAnchor);
        var request = fixture.Request(seed);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        var store = new FileWorkspaceStore(fixture.Directory);
        var cold = new Sr6CreationFoundationService(store, fixture.Owner);
        var loaded = cold.Load(fixture.Stamp, fixture.Id).Value!;
        Assert.AreEqual(quote.PreviewDigest, loaded.Selection!.PreviewDigest);
        Assert.IsFalse(loaded.KarmaSpecializationOptions!.ExpertiseAvailable);
        Assert.AreEqual(5, loaded.KarmaSpecializationOptions.KarmaCost);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(2L, store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_specialties_enforce_ratings_access_combined_creation_limit_and_budget()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        Sr6CreationFoundationSelection Buy(string skill, string subject) => seed with
            { Karma = new([], [], 0) { Specializations = [new(skill, subject)] } };
        foreach (var bad in new[] {
            Buy("Firearms", "Pistols"), Buy("Sorcery", "Spells"), Buy("Astral", "Combat"),
            Buy("Athletics", "Climbing") with { Skills = new([new("Athletics", 1, ["Swimming"])]) },
            Buy("Athletics", "Climbing") with { Karma = new([], [], 46) { Specializations = [new("Athletics", "Climbing")] } },
            Buy("Athletics", "Climbing") with { Karma = new([], [], 0) { Specializations = [new("Athletics", "Climbing"), new("Athletics", "Swimming")] } },
            Buy("ExoticWeapons", "WHIP") with { Skills = new([new("ExoticWeapons", 1, ["Whip"])]) },
            Buy("ExoticWeapons", "WHIP") with { Karma = new([], [new("ExoticWeapons", 1, "Whip")], 0) { Specializations = [new("ExoticWeapons", "WHIP")] } }
        }) Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding, bad).Value);
        var raised = Buy("Firearms", "Pistols") with { Karma = new([], [new("Firearms", 1)], 0)
            { Specializations = [new("Firearms", "Pistols")] } };
        Assert.AreEqual(10, fixture.Preview(raised).Karma!.KarmaSpent);
        var exotic = Buy("ExoticWeapons", "Monowhip") with { Skills = new([new("ExoticWeapons", 1, ["Whip"])]),
            Karma = new([], [], 0) { Specializations = [new("ExoticWeapons", "Monowhip"), new("ExoticWeapons", "Laser")] } };
        Assert.AreEqual(10, fixture.Preview(exotic).Karma!.KarmaSpent);
        Assert.AreEqual(1L, fixture.Store.Get(fixture.Id).Value!.ContentRevision);
    }

    [TestMethod]
    public void Karma_specialty_shape_is_bounded_detached_canonical_and_null_compatible()
    {
        using var fixture = new Fixture();
        var seed = KarmaSeed("Priority");
        foreach (var rows in new Sr6CreationKarmaSpecialization[][] {
            [null!], [new("Athletics", "")], [new("Athletics", " Climbing")], [new("Athletics", "x\ny")],
            [new("Athletics", new string('x', 81))], [new("athletics", "Climbing")], [new("Expertise", "Climbing")],
            [new("Athletics", "Climbing"), new("Athletics", "CLIMBING")],
            Enumerable.Range(0, 65).Select(i => new Sr6CreationKarmaSpecialization("ExoticWeapons", "Weapon" + i)).ToArray()
        }) Assert.IsFalse(Sr6CreationFoundationIntegrity.TryFreezeKarma(new([], [], 0) { Specializations = rows }, out _));
        var old = fixture.Preview(seed with { Karma = new([], [], 0) });
        var empty = fixture.Preview(seed with { Karma = new([], [], 0) { Specializations = [] } });
        Assert.AreEqual(old.PreviewDigest, empty.PreviewDigest);
        Assert.IsNull(empty.Karma!.Specializations);
        var purchases = new List<Sr6CreationKarmaSpecialization> { new("Athletics", "Climbing"), new("ExoticWeapons", "Laser") };
        var selection = seed with { Skills = new([new("Athletics", 1, []), new("ExoticWeapons", 1, ["Whip"])]),
            Karma = new([], [], 0) { Specializations = purchases } };
        var quote = fixture.Preview(selection);
        purchases.Reverse();
        Assert.AreEqual(quote.PreviewDigest, fixture.Preview(selection).PreviewDigest);
        purchases.Clear();
        Assert.HasCount(2, quote.Selection.Karma!.Specializations!);
        Assert.IsNull(fixture.Service.Preview(fixture.Stamp, fixture.Binding,
            quote.Selection with { Skills = new([]) }).Value);
    }

    [TestMethod]
    public void Rehashed_Karma_specialty_bonus_cannot_be_imported_as_authoritative()
    {
        using var fixture = new Fixture();
        var selection = KarmaSeed("Priority") with { Karma = new([], [], 0) { Specializations = [new("Athletics", "Climbing")] } };
        var saved = fixture.Store.Get(fixture.Id).Value!;
        var request = fixture.Request(selection);
        Assert.IsTrue(Sr6CreationFoundationRules.TryBuild(saved, request, out var candidate, out var decision));
        var karma = decision.Preview.Karma!;
        var forgedQuote = decision.Preview with { Karma = karma with
            { Specializations = [karma.Specializations!.Single() with { DicePoolBonus = 3 }] } };
        forgedQuote = forgedQuote with { PreviewDigest = Sr6CreationFoundationIntegrity.PreviewDigest(forgedQuote) };
        var forged = decision with { Preview = forgedQuote, Command = request with { PreviewDigest = forgedQuote.PreviewDigest } };
        forged = forged with { DecisionDigest = Sr6CreationFoundationIntegrity.DecisionDigest(forged) };
        var state = candidate.AuxiliaryState with { Sr6CreationFoundationDecisions = [forged] };
        Assert.IsTrue(Sr6CreationFoundationIntegrity.IsValidLedger(fixture.Id, 2, state));
        Assert.IsNull(Sr6CreationFoundationRules.Load(saved with { ContentRevision = 2, SavedRevision = 2,
            Document = candidate with { State = candidate.State with { AuxiliaryState = state } } }).Value);
    }

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
