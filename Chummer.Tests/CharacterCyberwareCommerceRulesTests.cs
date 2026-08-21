using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;

namespace Chummer.Tests;

[TestClass]
public class CharacterCyberwareCommerceRulesTests
{
    private const string TargetId = "71111111-1111-1111-1111-111111111111";
    private const string SourceId = "eb9e691a-8002-4138-ac8d-d9714d398b1e";

    [TestMethod]
    public void Source_backed_simple_career_ware_quotes_upgrade_sale_and_essence_hole_exactly()
    {
        CharacterCyberwareCommerceSemantics semantics = ParseSemantics("[1]");

        Assert.IsTrue(semantics.UpgradeExact, semantics.UpgradeBlockReason);
        Assert.IsTrue(semantics.SellExact, semantics.SellBlockReason);
        CharacterCyberwareCommerceQuote upgrade = CharacterCyberwareCommerceRules.QuoteUpgrade(
            semantics,
            CommerceSourceDataContext.AlphawareId,
            rating: 3,
            refundPercentage: 50m,
            freeCost: false);
        Assert.IsTrue(upgrade.Exact, upgrade.BlockReason);
        Assert.AreEqual(2_000m, upgrade.CurrentTotalCost);
        Assert.AreEqual(3_600m, upgrade.NewTotalCost);
        Assert.AreEqual(1_000m, upgrade.SaleCredit);
        Assert.AreEqual(2_600m, upgrade.NetNuyenCost);
        Assert.AreEqual(-2_600m, upgrade.NuyenDelta);
        Assert.AreEqual(0.10m, upgrade.CurrentEssence);
        Assert.AreEqual(0.08m, upgrade.NewEssence);
        Assert.AreEqual(-0.02m, upgrade.EssenceDelta);
        Assert.AreEqual(12, upgrade.NewEssenceHoleRating);
        Assert.IsTrue(upgrade.RatingReplayRequired);
        Assert.IsTrue(upgrade.GradeReplayRequired);
        Assert.AreEqual(64, upgrade.QuoteDigest.Length);

        CharacterCyberwareCommerceQuote sale = CharacterCyberwareCommerceRules.QuoteSale(semantics, 50m);
        Assert.IsTrue(sale.Exact, sale.BlockReason);
        Assert.AreEqual(1_000m, sale.NuyenDelta);
        Assert.AreEqual(-0.10m, sale.EssenceDelta);
        Assert.AreEqual(20, sale.NewEssenceHoleRating);
        Assert.AreEqual(64, sale.QuoteDigest.Length);
    }

    [TestMethod]
    public void Free_cost_and_refund_precision_preserve_legacy_bounds()
    {
        CharacterCyberwareCommerceSemantics semantics = ParseSemantics("[1]");
        CharacterCyberwareCommerceQuote free = CharacterCyberwareCommerceRules.QuoteUpgrade(
            semantics,
            CommerceSourceDataContext.StandardId,
            rating: 3,
            refundPercentage: 9_999.99m,
            freeCost: true);

        Assert.IsTrue(free.Exact, free.BlockReason);
        Assert.AreEqual(0m, free.NetNuyenCost);
        Assert.AreEqual(0m, free.NuyenDelta);
        Assert.IsFalse(CharacterCyberwareCommerceRules.TryNormalizeRefundPercentage(10_000m, out _));
        Assert.IsFalse(CharacterCyberwareCommerceRules.TryNormalizeRefundPercentage(50.001m, out _));
        Assert.IsTrue(CharacterCyberwareCommerceRules.TryNormalizeRefundPercentage(0m, out decimal zero));
        Assert.AreEqual(0m, zero);
    }

    [TestMethod]
    public void Linked_capacity_child_is_unconditionally_refused()
    {
        CharacterCyberwareCommerceSemantics semantics = ParseSemantics("[*]", nested: true);

        Assert.IsFalse(semantics.UpgradeExact);
        Assert.IsFalse(semantics.SellExact);
        StringAssert.Contains(semantics.UpgradeBlockReason, "Capacity=[*]");
        Assert.IsFalse(CharacterCyberwareCommerceRules.QuoteSale(semantics, 50m).Exact);
    }

    [TestMethod]
    public void Simple_parented_child_with_unit_cost_multiplier_has_exact_sale_authority()
    {
        CharacterCyberwareCommerceSemantics semantics = ParseSemantics("[1]", nested: true);

        Assert.IsTrue(semantics.SellExact, semantics.SellBlockReason);
        CharacterCyberwareCommerceQuote quote = CharacterCyberwareCommerceRules.QuoteSale(semantics, 50m);
        Assert.IsTrue(quote.Exact, quote.BlockReason);
        Assert.AreEqual(1_000m, quote.NuyenDelta);
        Assert.AreEqual(10, quote.NewEssenceHoleRating);
    }

    [TestMethod]
    public void Saved_improvements_fail_closed_instead_of_skipping_replay()
    {
        CharacterCyberwareCommerceSemantics semantics = ParseSemantics(
            "[1]",
            improvement: "<improvements><improvement><improvementttype>CyberwareEssCost</improvementttype></improvement></improvements>");

        Assert.IsFalse(semantics.UpgradeExact);
        Assert.IsFalse(semantics.SellExact);
        StringAssert.Contains(semantics.UpgradeBlockReason, "improvement");
    }

    private static CharacterCyberwareCommerceSemantics ParseSemantics(
        string capacity,
        bool nested = false,
        string improvement = "")
    {
        string target = $"""
            <cyberware>
              <guid>{TargetId}</guid><sourceid>{SourceId}</sourceid><name>Data Lock</name>
              <improvementsource>Cyberware</improvementsource><grade>Standard</grade><rating>2</rating>
              <minrating>1</minrating><maxrating>12</maxrating><cost>Rating * 1000</cost><ess>0.1</ess>
              <capacity>{capacity}</capacity><discountedcost>False</discountedcost>
              <addtoparentess>False</addtoparentess><children />
            </cyberware>
            """;
        string ware = nested
            ? $"""
              <cyberware>
                <guid>72222222-2222-2222-2222-222222222222</guid><sourceid>{SourceId}</sourceid><name>Simple parent</name>
                <improvementsource>Cyberware</improvementsource><grade>Standard</grade><rating>1</rating>
                <cost>1000</cost><ess>0.1</ess><capacity>4</capacity><childcostmultiplier>1</childcostmultiplier>
                <children>{target}</children>
              </cyberware>
              """
            : target;
        string xml = $"""
            <character>
              <created>True</created><nuyen>10000</nuyen>{improvement}
              <cyberwares>
                {ware}
                <cyberware>
                  <guid>73333333-3333-3333-3333-333333333333</guid>
                  <sourceid>b57eadaa-7c3b-4b80-8d79-cbbd922c1196</sourceid>
                  <name>Essence Hole</name><rating>10</rating>
                </cyberware>
              </cyberwares>
            </character>
            """;

        CharacterCyberwareSummary selected = new CharacterSectionService(new CommerceSourceDataResolver())
            .ParseCyberwares(xml)
            .Cyberwares
            .Single(item => item.Guid == TargetId);
        return selected.CommerceSemantics!;
    }

    private sealed class CommerceSourceDataResolver : ICharacterSourceDataResolver
    {
        public ICharacterSourceDataContext TryCreateContext(string characterXml)
            => new CommerceSourceDataContext();
    }

    private sealed class CommerceSourceDataContext : ICharacterSourceDataContext
    {
        public const string StandardId = "23382221-fd16-44ec-8da7-9b935ed2c1ee";
        public const string AlphawareId = "75da0ff2-4137-4990-85e6-331977564712";

        public bool TryResolveCyberwareGradeDeviceRating(
            string gradeName,
            string improvementSource,
            out int deviceRating)
        {
            deviceRating = 2;
            return true;
        }

        public bool TryResolveVehicleModBonuses(
            string sourceId,
            string name,
            out CharacterVehicleModSourceBonuses bonuses)
        {
            bonuses = CharacterVehicleModSourceBonuses.Empty;
            return false;
        }

        public bool TryResolveCyberwareCommerceSource(
            string sourceId,
            string name,
            string improvementSource,
            out CharacterCyberwareCommerceSource source)
        {
            source = new CharacterCyberwareCommerceSource(
                sourceId,
                name,
                Source: "SR5",
                MinimumRatingExpression: "1",
                MaximumRatingExpression: "12",
                CostExpression: "Rating * 1000",
                EssenceExpression: "0.1",
                CapacityExpression: "[1]",
                ForcedGrade: string.Empty,
                BannedGrades: Array.Empty<string>(),
                Grades:
                [
                    new CharacterCyberwareCommerceGradeSource(StandardId, "Standard", 1m, 1m, "SR5", false),
                    new CharacterCyberwareCommerceGradeSource(AlphawareId, "Alphaware", 1.2m, 0.8m, "SR5", false)
                ],
                EssenceDecimals: 2,
                DoNotRoundEssenceInternally: false,
                EssenceModifierPostExpression: "{Modifier}",
                SourceEntryUsesGeneratedOrImprovementSemantics: false);
            return string.Equals(improvementSource, "Cyberware", StringComparison.Ordinal)
                && Guid.TryParse(sourceId, out _);
        }
    }
}
