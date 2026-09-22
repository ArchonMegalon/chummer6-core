using System.Globalization;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Life_module_quality_costs_compose_tiers_free_grants_racial_and_talent_once()
    {
        var fixture = SkillMathFixture(("FreeNegativeQualities", "", -5m), ("FreeNegativeQualities", "", -5m),
            ("FreePositiveQualities", "", 2m));
        var bound = QualityCostPlans(fixture,
            [QualityCostRow("SINner", -5, "QualityLevelImprovement"), QualityCostRow("Free grant", 0, "Improvement"),
             QualityCostRow("Module owner", 100, "LifeModule")],
            [QualityCostRow("Racial", 10, "Metatype")], [QualityCostRow("Talent", 15, "Selected")], 15);
        var quote = bound.Quote();
        Assert.IsNotNull(quote);
        Assert.AreEqual(-10m, quote.FreeNegativeQualities, "Repeated module offsets sum even when their quality-tier winner does not.");
        Assert.AreEqual(13, quote.Costs.PositiveKarmaSpent); // Talent fifteen, free positive two.
        Assert.AreEqual(-5, quote.Costs.NegativeKarmaGranted); // -5 quality, -10 cumulative offset.
        Assert.AreEqual(18, quote.Costs.NetKarmaSpent);
        Assert.AreEqual(3m, quote.KarmaAdjustmentAfterTalent);
        Assert.HasCount(4, quote.Lines, "LifeModule owner already charged outside this quote.");
        Assert.IsFalse(quote.Lines.Single(row => row.Name == "Racial").CountsAgainstKarma);
        Assert.IsEmpty(quote.Blockers);
    }

    [TestMethod]
    public void Life_module_quality_costs_apply_house_multiplier_after_origin_admission()
    {
        var fixture = SkillMathFixture();
        var bound = QualityCostPlans(fixture, [QualityCostRow("Tier", 15, "QualityLevelImprovement")],
            [], [QualityCostRow("Talent", 10, "Selected")], 10);
        bound = bound with { Policy = bound.Policy with { Costs = new(2, true, false), QualityKarmaLimit = 25 } };
        bound = bound with { Policy = SealQualityPolicy(bound.Policy) };
        var catalog = bound.Talent.Catalog with { KarmaQuality = 2 };
        catalog = catalog with { AuthorityDigest = CharacterCreationLifeModuleTalentAuthority.ComputeDigest(catalog) };
        var talent = bound.Talent with { Catalog = catalog, Talent = bound.Talent.Talent with { KarmaCost = 20 }, PlanDigest = string.Empty };
        talent = talent with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(talent) };
        bound = bound with { Talent = talent };
        var quote = bound.Quote()!;
        Assert.AreEqual(75, quote.Costs.PositiveKarmaSpent);
        Assert.AreEqual(55m, quote.KarmaAdjustmentAfterTalent);
        CollectionAssert.Contains(quote.Blockers.ToArray(), CharacterCreationQualitiesBlockers.PositiveLimitExceeded);
    }

    [TestMethod]
    public void Life_module_quality_costs_preserve_contribution_flags_and_mentor_exemption()
    {
        var bound = QualityCostPlans(SkillMathFixture(),
            [QualityCostRow("Limit only", 7, "QualityLevelImprovement", karma: false),
             QualityCostRow("Cap exempt", -3, "QualityLevelImprovement", limit: false),
             QualityCostRow("Mentor Spirit", 5, "QualityLevelImprovement"),
             QualityCostRow("The Beast's Way", 0, "Improvement")], [], [], 0);
        var quote = bound.Quote()!;
        Assert.AreEqual(7, quote.Costs.PositiveLimitKarma);
        Assert.AreEqual(0, quote.Costs.PositiveKarmaSpent);
        Assert.AreEqual(0, quote.Costs.NegativeLimitKarma);
        Assert.AreEqual(3, quote.Costs.NegativeKarmaGranted);
        Assert.IsFalse(quote.Lines.Single(row => row.Name == "Mentor Spirit").CountsAgainstKarma);
    }

    [TestMethod]
    public void Life_module_quality_costs_keep_metagenic_balance_outside_ordinary_costs()
    {
        var bound = QualityCostPlans(SkillMathFixture(),
            [QualityCostRow("Metagenic positive", 6, "QualityLevelImprovement", metagenic: true),
             QualityCostRow("Metagenic negative", -5, "QualityLevelImprovement", metagenic: true)], [], [], 0);
        bound = bound with { Policy = SealQualityPolicy(bound.Policy with { MetagenicLimit = 10 }) };
        var quote = bound.Quote()!;
        Assert.AreEqual(0, quote.Costs.NetKarmaSpent);
        Assert.AreEqual(1, quote.MetagenicBalanceKarma);
        Assert.AreEqual(1m, quote.KarmaAdjustmentAfterTalent);
        Assert.IsEmpty(quote.Blockers);
    }

    [TestMethod]
    public void Life_module_quality_costs_use_free_negative_offsets_in_metagenic_balance_only_when_enabled()
    {
        var bound = QualityCostPlans(SkillMathFixture(("FreeNegativeQualities", "", -1m)),
            [QualityCostRow("Metagenic positive", 6, "QualityLevelImprovement", metagenic: true),
             QualityCostRow("Metagenic negative", -5, "QualityLevelImprovement", metagenic: true)], [], [], 0);
        var ordinary = bound.Quote()!;
        Assert.AreEqual(2, ordinary.Costs.NetKarmaSpent);
        Assert.IsFalse(ordinary.Lines.Any(row => row.CountsAgainstMetagenicLimit));
        Assert.IsEmpty(ordinary.Blockers, "No SURGE limit means no separate metagenic admission gate.");
        bound = bound with { Policy = SealQualityPolicy(bound.Policy with { MetagenicLimit = 10 }) };
        var surge = bound.Quote()!;
        Assert.AreEqual(4, surge.Costs.MetagenicNegativeKarma);
        Assert.AreEqual(0, surge.MetagenicBalanceKarma, "A difference of two is illegal, not a one-Karma surcharge.");
        CollectionAssert.Contains(surge.Blockers.ToArray(), CharacterCreationQualitiesBlockers.MetagenicImbalanced);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("malformed")]
    [DataRow("wrong-type")]
    [DataRow("unknown-origin")]
    [DataRow("plan-digest")]
    [DataRow("wrong-schema")]
    [DataRow("wrong-profile")]
    [DataRow("wrong-source")]
    [DataRow("wrong-multiplier")]
    [DataRow("policy-digest")]
    [DataRow("conditional")]
    [DataRow("unknown-modifier")]
    public void Life_module_quality_costs_reject_ambiguous_or_stale_composition(string fault)
    {
        var fixture = SkillMathFixture(("FreePositiveQualities", "", 1m));
        var row = QualityCostRow("Tier", 5, "QualityLevelImprovement");
        if (fault == "malformed") row.Add(new XElement("bp", 999));
        if (fault == "wrong-type") row.Element("qualitytype")!.Value = "Negative";
        if (fault == "unknown-origin") row.Element("qualitysource")!.Value = "Unknown";
        var bound = QualityCostPlans(fixture, fault == "duplicate" ? [row, row] : [row], [], [], 0);
        if (fault is "conditional" or "unknown-modifier")
        {
            var improvement = XElement.Parse(bound.Effects.ImprovementXml.Single());
            improvement.Element(fault == "conditional" ? "condition" : "improvementttype")!.Value
                = fault == "conditional" ? "career" : "FreeQuality";
            bound = QualityCostPlans(fixture with { Effects = fixture.Effects with
                { ImprovementXml = [improvement.ToString(SaveOptions.DisableFormatting)] } }, [row], [], [], 0);
        }
        if (fault == "plan-digest") bound = bound with { Effects = bound.Effects with { ModuleKarmaCost = 999 } };
        if (fault == "policy-digest") bound = bound with { Policy = bound.Policy with { QualityKarmaLimit = 999 } };
        if (fault == "wrong-schema") bound = bound with { Policy = SealQualityPolicy(bound.Policy with { Schema = CharacterCreationKarmaQualitiesPolicy.SchemaV1 }) };
        if (fault == "wrong-profile") bound = bound with { Policy = SealQualityPolicy(bound.Policy with { SettingsProfileId = "other" }) };
        if (fault == "wrong-source") bound = bound with { Policy = SealQualityPolicy(bound.Policy with
            { SourceInputsDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest("other-source") }) };
        if (fault == "wrong-multiplier") bound = bound with { Policy = SealQualityPolicy(bound.Policy with { Costs = new(3, false, false) }) };
        Assert.IsNull(bound.Quote(), fault);
    }

    [TestMethod]
    public void Life_module_quality_policy_is_method_owned_and_rechecked_after_funding()
    {
        var fixture = SkillMathFixture();
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        var policy = LifeQualityPolicy(fixture);
        Assert.IsFalse(CharacterCreationKarmaQualitiesRules.IsValidPolicy(policy));
        Assert.IsFalse(context.TryResolveCreationKarmaQualities(out _));
        Assert.IsFalse(context.TryResolveCreationQualitiesAuthority(out _));
        var skills = CharacterCreationLifeModuleSkillsRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, null, context).Quote!;
        var changing = new QualityPolicyDriftContext(context);
        var result = CharacterCreationLifeModuleResourcesRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial,
            fixture.Talent, fixture.Attributes, skills, 1000m, 1m, changing);
        Assert.AreEqual(2, changing.Reads);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationFoundationBlockers.SourceDigestConflict);
    }

    private static CharacterCreationKarmaQualitiesPolicy LifeQualityPolicy(SkillMathBinding fixture)
    {
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(fixture.Xml)!;
        Assert.IsTrue(context.TryResolveCreationLifeModuleQualitiesPolicy(out var policy));
        return policy!;
    }

    private static CharacterCreationKarmaQualitiesPolicy SealQualityPolicy(CharacterCreationKarmaQualitiesPolicy policy)
        => policy with { AuthorityDigest = CharacterCreationKarmaQualitiesRules.PolicyDigest(policy) };

    private static XElement QualityCostRow(string name, int cost, string origin,
        bool limit = true, bool karma = true, bool metagenic = false)
        => new("quality", new XElement("name", name), new XElement("guid", Guid.NewGuid().ToString("D")),
            new XElement("sourceid", Guid.NewGuid().ToString("D")), new XElement("bp", cost.ToString(CultureInfo.InvariantCulture)),
            new XElement("qualitytype", origin == "LifeModule" ? "LifeModule" : cost < 0 ? "Negative" : "Positive"),
            new XElement("qualitysource", origin), new XElement("contributetolimit", limit),
            new XElement("contributetobp", karma), new XElement("metagenic", metagenic));

    private static QualityCostBinding QualityCostPlans(SkillMathBinding fixture, XElement[] modules, XElement[] racial,
        XElement[] talent, int talentCost)
    {
        var (effects, metatype) = SealAttributeMathPlans(
            fixture.Effects with { QualityXml = modules.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray() },
            fixture.Racial with { QualityXml = racial.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray() });
        var gifted = fixture.Talent with { EffectPlanDigest = effects.PlanDigest, MetatypePlanDigest = metatype.PlanDigest,
            Talent = fixture.Talent.Talent with { KarmaCost = talentCost },
            QualityXml = talent.Select(row => row.ToString(SaveOptions.DisableFormatting)).ToArray(), PlanDigest = string.Empty };
        gifted = gifted with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(gifted) };
        return new(effects, metatype, gifted, LifeQualityPolicy(fixture));
    }

    private sealed record QualityCostBinding(CharacterCreationFoundationSequenceWritePlan Effects,
        CharacterCreationLifeModuleMetatypeWritePlan Racial, CharacterCreationLifeModuleTalentWritePlan Talent,
        CharacterCreationKarmaQualitiesPolicy Policy)
    {
        internal CharacterCreationLifeModuleQualityCostsQuote? Quote()
            => CharacterCreationLifeModuleQualityCostsRules.Quote(Effects, Racial, Talent, Policy);
    }

    private sealed class QualityPolicyDriftContext(ICharacterSourceDataContext inner) : ICharacterSourceDataContext
    {
        internal int Reads { get; private set; }
        public bool TryResolveCyberwareGradeDeviceRating(string sourceId, string grade, out int rating)
            => inner.TryResolveCyberwareGradeDeviceRating(sourceId, grade, out rating);
        public bool TryResolveVehicleModBonuses(string sourceId, string grade, out CharacterVehicleModSourceBonuses bonuses)
            => inner.TryResolveVehicleModBonuses(sourceId, grade, out bonuses);
        public bool TryResolveCreationSkillsCatalog(out CharacterCreationSkillsCatalog? catalog)
            => inner.TryResolveCreationSkillsCatalog(out catalog);
        public bool TryResolveCreationLifeModuleSkillsPolicy(out CharacterCreationKarmaSkillsPolicy? policy)
            => inner.TryResolveCreationLifeModuleSkillsPolicy(out policy);
        public bool TryResolveCreationLifeModuleResourcesPolicy(out CharacterCreationKarmaResourcesPolicy? policy)
            => inner.TryResolveCreationLifeModuleResourcesPolicy(out policy);
        public bool TryResolveCreationLifeModuleQualitiesPolicy(out CharacterCreationKarmaQualitiesPolicy? policy)
        {
            bool found = inner.TryResolveCreationLifeModuleQualitiesPolicy(out policy);
            if (++Reads == 2 && policy is not null) policy = SealQualityPolicy(policy with { Costs = new(3, false, false) });
            return found;
        }
    }
}
