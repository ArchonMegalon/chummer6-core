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
    public void Passive_values_derive_saved_ratings_and_quality_totals_without_equipping_purchases(string method)
    {
        using var fixture = new Fixture(method);
        var selection = KarmaSeed(method) with
        {
            Attributes = Spend(Spend(EmptyAttributes(), "Body", 1, 0), "Willpower", 1, 0),
            Qualities = new(["built-tough-2", "glass-jaw-1", "will-to-live-1"]),
            Karma = new([new("Body", 1)], [], 2),
            Gear = new([new(Guid.NewGuid(), "full-body-armor", 2)])
        };
        var request = fixture.Request(selection);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, request).Value);
        string bytes = Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!);
        var cold = new Sr6CreationFoundationService(new FileWorkspaceStore(fixture.Directory), fixture.Owner);
        var state = cold.Load(fixture.Stamp, fixture.Id).Value!;
        var values = state.DraftSummary!.PassiveValues!;
        Check("physical-monitor", 12, "8 + ceil(3 / 2) + 2 = 12");
        Check("stun-monitor", 8, "8 + ceil(2 / 2) - 1 = 8");
        Check("overflow", 8, "3 × 2 + 1 × 2 = 8");
        Check("initiative-base", 2, "1 + 1 = 2");
        Check("initiative-dice", 1, "1 + 0 = 1");
        Check("unarmored-defense-rating", 3, "3 + 0 + 0 = 3");
        Check("defense-dice-pool", 2, "1 + 1 + 0 = 2");
        Check("unarmed-attack-rating", 2, "1 + 1 = 2");
        Assert.IsEmpty(values.WarningIds);
        Assert.AreEqual(request.PreviewDigest, state.Selection!.PreviewDigest);
        Assert.IsFalse(state.DraftSummary.FinalizationAvailable);
        Assert.IsTrue(cold.Confirm(fixture.Stamp, request).Value!.Replayed);
        Assert.AreEqual(bytes, Sr6CreationFoundationIntegrity.Digest(fixture.Store.Get(fixture.Id).Value!));

        void Check(string id, int expected, string equation)
        {
            var row = values.Derived.Single(value => value.Id == id);
            Assert.AreEqual(expected, row.Value, id);
            Assert.AreEqual(equation, row.Calculation, id);
            StringAssert.StartsWith(row.SourceAnchorId, "sr6_core_de_2024:p");
        }
    }

    [TestMethod]
    [DataRow("human", 0, 0)]
    [DataRow("elf", 0, 0)]
    [DataRow("dwarf", 0, 0)]
    [DataRow("ork", 1, 0)]
    [DataRow("troll", 2, 1)]
    public void Passive_metatype_grants_and_upgrades_do_not_double_count(string metatype, int innate, int armor)
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PointBuy(metatype) with { Attributes = EmptyAttributes() };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(9 + innate, values.Derived.Single(row => row.Id == "physical-monitor").Value);
        Assert.AreEqual(1 + armor, values.Derived.Single(row => row.Id == "unarmored-defense-rating").Value);
        Assert.IsNull(values.Skills);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed with { Qualities = new(["built-tough-3"]) })).Value);
        values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(12, values.Derived.Single(row => row.Id == "physical-monitor").Value);
    }

    [TestMethod]
    public void Passive_powers_change_permanent_attributes_and_derived_values_without_changing_natural_history()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PowerSeed("PointBuy") with
        {
            Attributes = Spend(Spend(EmptyAttributes(), "Magic", 0, 2), "Body", 1, 0),
            Skills = new([]), AdeptPowers = new([new("improved-attribute-body", 1),
                new("improved-reflexes", 1), new("mystic-armor", 2), new("combat-sense", 1)])
        };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var state = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!;
        var values = state.DraftSummary!.PassiveValues!;
        Assert.AreEqual(2, state.DraftSummary.NaturalValues!.Attributes!.Single(row => row.AttributeId == "Body").Rating);
        Assert.AreEqual(new Sr6CreationPassiveAttributeValue("Body", 2, 1, 3), values.Attributes.Single(row => row.AttributeId == "Body"));
        Assert.AreEqual(new Sr6CreationPassiveAttributeValue("Reaction", 1, 1, 2), values.Attributes.Single(row => row.AttributeId == "Reaction"));
        foreach (var (id, expected) in new[] { ("physical-monitor", 10), ("initiative-base", 3), ("initiative-dice", 2),
                     ("unarmored-defense-rating", 5), ("defense-dice-pool", 4), ("unarmed-attack-rating", 3) })
            Assert.AreEqual(expected, values.Derived.Single(row => row.Id == id).Value, id);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed with { AdeptPowers = new([]) })).Value);
        values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(2, values.Attributes.Single(row => row.AttributeId == "Body").Rating);
        Assert.AreEqual(1, values.Derived.Single(row => row.Id == "initiative-dice").Value);
    }

    [TestMethod]
    public void Passive_projection_keeps_noncombat_skills_and_activated_powers_separate()
    {
        using var fixture = new Fixture("PointBuy");
        var seed = PowerSeed("PointBuy") with { Skills = new([new("Athletics", 3, ["Climbing"])]),
            AdeptPowers = new([new("improved-ability-athletics-noncombat", 2), new("adrenaline-boost", 2), new("attribute-boost-body", 2)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(new Sr6CreationPassiveSkillValue("Athletics", 3, 0, 2, 3, 5), values.Skills!.Single(row => row.SkillId == "Athletics"));
        Assert.AreEqual(1, values.Attributes.Single(row => row.AttributeId == "Body").Rating);
        Assert.AreEqual(2, values.Derived.Single(row => row.Id == "initiative-base").Value);
        Assert.AreEqual(1, values.Derived.Single(row => row.Id == "initiative-dice").Value);
        seed = seed with { AdeptPowers = new([new("improved-ability-athletics-all", 2)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(new Sr6CreationPassiveSkillValue("Athletics", 3, 2, 0, 5, 5), values.Skills!.Single(row => row.SkillId == "Athletics"));
    }

    [TestMethod]
    public void Passive_projection_does_not_guess_through_incompatible_reaction_effects_or_halve_unrolled_initiative()
    {
        using var fixture = new Fixture("PointBuy");
        Assert.IsNull(fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(PointBuy())).Value);
        Assert.IsNull(fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues);
        var seed = PowerSeed("PointBuy") with { Qualities = new(["combat-paralysis"]),
            AdeptPowers = new([new("improved-reflexes", 1), new("improved-attribute-reaction", 1)]) };
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed)).Value);
        var values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        CollectionAssert.AreEqual(new[] { "reaction-power-conflict", "combat-paralysis-rolled-total" }, values.WarningIds.ToArray());
        Assert.IsNull(values.Attributes.Single(row => row.AttributeId == "Reaction").Rating);
        foreach (string id in new[] { "initiative-base", "initiative-dice", "defense-dice-pool", "unarmed-attack-rating" })
        {
            var row = values.Derived.Single(value => value.Id == id);
            Assert.IsNull(row.Value);
            Assert.AreEqual(string.Empty, row.Calculation);
        }
        Assert.AreEqual(9, values.Derived.Single(row => row.Id == "physical-monitor").Value);
        Assert.IsNotNull(fixture.Service.Confirm(fixture.Stamp, fixture.Request(seed with { AdeptPowers = new([new("improved-reflexes", 1)]) })).Value);
        values = fixture.Service.Load(fixture.Stamp, fixture.Id).Value!.DraftSummary!.PassiveValues!;
        Assert.AreEqual(3, values.Derived.Single(row => row.Id == "initiative-base").Value, "Combat Paralysis applies to the rolled total, not the base rating.");
        CollectionAssert.AreEqual(new[] { "combat-paralysis-rolled-total" }, values.WarningIds.ToArray());
    }
}
