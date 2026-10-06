using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    [DataRow("<nuyenmaxbp>30</nuyenmaxbp>", true)]
    [DataRow("<nuyenmaxbp>Rating</nuyenmaxbp>", false)]
    [DataRow("<nuyenmaxbp hidden=\"true\">30</nuyenmaxbp>", false)]
    [DataRow("<nuyenmaxbp><val>30</val></nuyenmaxbp>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus><condition>Meeting people the first time</condition></skillcategory>", true)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus></skillcategory>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus><condition /></skillcategory>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>Rating</bonus><condition>First meeting</condition></skillcategory>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus><condition>First meeting</condition><condition>Always</condition></skillcategory>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus><condition>First meeting</condition><applytorating>true</applytorating></skillcategory>", false)]
    [DataRow("<skillcategory><name>Social Active</name><bonus>2</bonus><condition xmlns=\"foreign\">First meeting</condition></skillcategory>", false)]
    public void Corporate_dependent_bonuses_keep_exact_literal_and_conditional_shape(string xml, bool supported)
        => Assert.AreEqual(supported, CharacterCreationFoundationEffectCompiler.TryValidateDependentBonusEffect(XElement.Parse(xml)));

    [TestMethod]
    [DataRow(30, 10, 40)]
    [DataRow(-30, 10, 0)]
    [DataRow(30, int.MaxValue, int.MaxValue)]
    public void Corporate_resource_cap_bonus_clamps_and_never_becomes_free_cash(int bonus, int policyMaximum, int expected)
    {
        var fixture = SkillMathFixture(("NuyenMaxBP", "", bonus));
        var policy = ResourcePolicy(fixture) with { MaximumKarmaInvestment = policyMaximum };
        policy = policy with { AuthorityDigest = CharacterCreationKarmaResourcesRules.ComputePolicyDigest(policy) };
        var quote = QuoteLifeResources(fixture, policy, 0m).Quote!;
        Assert.IsNotNull(quote);
        Assert.AreEqual((decimal)expected, quote.MaximumKarmaInvestment);
        Assert.AreEqual((decimal)policyMaximum, quote.Policy.MaximumKarmaInvestment);
        Assert.AreEqual(0m, quote.NuyenFromKarma);
        Assert.AreEqual(quote.KarmaBeforeResources, quote.KarmaAfterResources);
    }

    [TestMethod]
    [DataRow("<chargenonly />", true)]
    [DataRow("<chargenonly>False</chargenonly>", false)]
    [DataRow("<chargenonly hidden=\"true\" />", false)]
    [DataRow("<chargenonly><hidden /></chargenonly>", false)]
    [DataRow("<chargenonly /><chargenonly />", false)]
    public void Corporate_chargen_only_marker_does_not_admit_new_effect_or_condition_shapes(string marker, bool supported)
    {
        var doc = XDocument.Load(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
        var quality = new XElement(doc.Root!.Element("qualities")!.Elements("quality")
            .Single(row => row.Element("name")?.Value == "Born Rich"));
        quality.Element("chargenonly")!.Remove();
        foreach (var node in XElement.Parse("<markers>" + marker + "</markers>").Elements()) quality.Add(new XElement(node));
        Assert.AreEqual(supported, CharacterCreationFoundationEffectCompiler.TryInspectDependentQuality(quality,
            out _, out _, out bool bonusSupported));
        if (supported) Assert.IsTrue(bonusSupported);
    }

    [TestMethod]
    [DataRow("", true)]
    [DataRow("tier", false)]
    [DataRow("group", false)]
    [DataRow("unknown", false)]
    [DataRow("nested", false)]
    [DataRow("attribute", false)]
    [DataRow("projection", false)]
    public void Corporate_ignored_nested_level_is_explicit_exact_and_never_an_extra_grant(string tamper, bool accepted)
    {
        string directory = CreateTempDirectory();
        try
        {
            string nested = "<qualitylevel group=\"SINner\">3</qualitylevel>";
            if (tamper == "tier") nested = nested.Replace(">3<", ">4<", StringComparison.Ordinal);
            if (tamper == "group") nested = nested.Replace("SINner", "TrustFund", StringComparison.Ordinal);
            if (tamper == "unknown") nested += "<unknown>2</unknown>";
            if (tamper == "nested") nested = "<container>" + nested + "</container>";
            if (tamper == "attribute") nested = nested.Replace("group=", "hidden=\"true\" group=", StringComparison.Ordinal);
            var fixture = CreateGroupFixture(directory, "<addqualities><addquality>Born Rich</addquality>"
                + "<addquality>Privileged Family Name</addquality>" + nested + "</addqualities>", "");
            string qualityXml = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
            Assert.IsTrue(CharacterCreationFoundationQualitySourceAuthority.TryCreate(qualityXml,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualityXml), out var qualities));
            var ledger = fixture.Ledger;
            if (tamper == "projection") ledger = ledger with { ProjectedEffects = [ledger.ProjectedEffects.Single() with
                { Parameters = new Dictionary<string, string> { ["addquality"] = "Born Rich|Privileged Family Name", ["qualitylevel"] = "4" } }] };
            string before = JsonSerializer.Serialize(ledger);
            var result = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5, ledger,
                fixture.Module, fixture.Version, qualitySourceAuthority: qualities);
            var effect = result.Effects.Single();
            Assert.AreEqual(before, JsonSerializer.Serialize(ledger));
            Assert.AreEqual(accepted, effect.IgnoredSourceMetadata.ContainsKey("legacy-ignored-nested-qualitylevel"));
            Assert.IsFalse(result.Effects.Any(row => row.EffectKind == "qualitylevel"));
            if (accepted)
            {
                Assert.AreEqual(nested, effect.IgnoredSourceMetadata["legacy-ignored-nested-qualitylevel"]);
                Assert.HasCount(2, result.DependentQualities);
                Assert.IsTrue(ledger.ProjectedEffects.Single().RawXml.Contains(nested, StringComparison.Ordinal));
                Assert.AreEqual(CharacterCreationFoundationEffectSourcePhases.Version, effect.SourcePhase);
            }
            else Assert.AreEqual(CharacterCreationFoundationEffectCompilationStatuses.Unsupported, effect.CompilationStatus);
            Assert.IsFalse(result.IsCompleteLedgerSupported, "An isolated module cannot bypass full-graph requirements.");
        }
        finally { Directory.Delete(directory, true); }
    }
}
