using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Quality_level_source_resolves_real_SINner_tiers_and_highest_not_sum()
    {
        var (levels, qualities, _) = LoadQualityLevelSources();
        string[] expected = ["SINner (National)", "SINner (Criminal)",
            "SINner (Corporate Limited)", "SINner (Corporate)"];
        for (int tier = 1; tier <= expected.Length; tier++)
        {
            Assert.IsTrue(levels.TryResolveExact("SINner", tier, qualities, out var target));
            Assert.AreEqual(expected[tier - 1], target!.CanonicalName);
            Assert.IsTrue(qualities.TryGetDefinition(target, out var definition, out _));
            Assert.AreEqual(expected[tier - 1], definition!.Element("name")!.Value);
        }

        Assert.IsTrue(levels.TryResolveHighest("SINner", [1, 3, 2, 3], qualities,
            out int highest, out var winner));
        Assert.AreEqual(3, highest);
        Assert.AreEqual("SINner (Corporate Limited)", winner!.CanonicalName);
        Assert.IsTrue(levels.TryResolveHighest("SINner", [3, 2, 1, 3], qualities,
            out int reordered, out var same));
        Assert.AreEqual(highest, reordered);
        Assert.AreEqual(winner, same);
        Assert.IsFalse(levels.TryResolveHighest("SINner", [], qualities, out _, out _));
        Assert.IsFalse(levels.TryResolveHighest("SINner", [0, 3], qualities, out _, out _));
        Assert.IsFalse(levels.TryResolveHighest("SINner", [1, 5], qualities, out _, out _));
        Assert.IsFalse(levels.TryResolveExact("sinner", 1, qualities, out _));
    }

    [TestMethod]
    public void Quality_level_source_rejects_stale_ambiguous_and_unavailable_mappings()
    {
        var (levels, qualities, source) = LoadQualityLevelSources();
        string digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(source);
        Assert.IsFalse(CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(source + " ", digest, out _));
        foreach (int mode in Enumerable.Range(0, 6))
        {
            XDocument changed = XDocument.Parse(source);
            XElement groups = changed.Root!.Element("qualitygroups")!;
            XElement group = groups.Element("qualitygroup")!;
            XElement mappings = group.Element("levels")!;
            if (mode == 0) groups.Add(new XElement(group));
            if (mode == 1) mappings.Add(new XElement(mappings.Elements("level").First()));
            if (mode == 2) mappings.Elements("level").Last().Value = mappings.Elements("level").First().Value;
            if (mode == 3) mappings.Elements("level").First().SetAttributeValue("value", "01");
            if (mode == 4) group.Add(new XElement("name", "SINner"));
            if (mode == 5) mappings.Add(new XText("unparsed rule instruction"));
            string xml = changed.ToString(SaveOptions.DisableFormatting);
            Assert.IsFalse(CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(xml,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml), out _),
                "ambiguous/malformed mapping mode " + mode);
        }

        string qualityXml = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
        Assert.IsTrue(CharacterCreationFoundationQualitySourceAuthority.TryCreate(qualityXml,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualityXml),
            out var disabled, new HashSet<string> { "RF" }));
        Assert.IsFalse(levels.TryResolveExact("SINner", 1, disabled!, out _));

        string unknown = source.Replace("SINner (National)", "Missing Quality", StringComparison.Ordinal);
        Assert.IsTrue(CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(unknown,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(unknown), out var unresolved));
        Assert.IsFalse(unresolved!.TryResolveHighest("SINner", [1, 4], qualities, out _, out _),
            "An unknown lower contribution must not be hidden by a valid higher tier.");
    }

    [TestMethod]
    public void Quality_level_compiler_binds_contribution_without_granting_partial_write_authority()
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, qualities, source) = LoadQualityLevelSources();
            var fixture = CreateGroupFixture(directory, "<qualitylevel group=\"SINner\">1</qualitylevel>", "");
            var result = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5,
                fixture.Ledger, fixture.Module, fixture.Version, qualitySourceAuthority: qualities,
                qualityLevelSourceAuthority: levels);
            Assert.AreEqual(CharacterCreationFoundationEffectCompilationStatuses.Supported,
                result.Effects.Single().CompilationStatus);
            Assert.AreEqual("SINner (National)", result.Effects.Single().TargetBinding!.CanonicalName);
            CollectionAssert.Contains(result.Effects.Single().SourceAnchorIds.ToArray(),
                "qualitylevels.xml#qualitygroup:SINner:level:1");
            CollectionAssert.Contains(result.Effects.Single().SourceAnchorIds.ToArray(),
                "qualities.xml#quality:9ac85feb-ae1e-4996-8514-3570d411e1d5");
            Assert.IsFalse(result.IsCompleteLedgerSupported);
            CollectionAssert.Contains(result.Blockers.ToArray(),
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);

            string qualitiesXml = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
            var plan = CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(fixture.WorkspaceId,
                RulesetDefaults.Sr5, fixture.Ledger, fixture.Module, fixture.Version,
                fixture.EffectiveSourceXml, fixture.SourceDigest, "Chocolate",
                qualitiesSourceXml: qualitiesXml,
                qualitiesSourceDigest: CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualitiesXml),
                qualityLevelsSourceXml: source, qualityLevelsSourceDigest: levels.SourceDigest);
            Assert.IsFalse(plan.IsReady, "Group reconciliation and instance text are still required.");
            Assert.IsNull(plan.Plan);
            CollectionAssert.Contains(plan.Blockers.ToArray(),
                CharacterCreationFoundationBlockers.FinalizationRuntimeAuthorityRequired);

            string changed = source.Replace("SINner (Corporate)</level>", "Other Quality</level>", StringComparison.Ordinal);
            Assert.IsTrue(CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(changed,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(changed), out var changedAuthority));
            var recompiled = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5,
                fixture.Ledger, fixture.Module, fixture.Version, qualitySourceAuthority: qualities,
                qualityLevelSourceAuthority: changedAuthority);
            Assert.AreNotEqual(result.CompilerRuntimeDigest, recompiled.CompilerRuntimeDigest);
            Assert.AreNotEqual(result.CompilationDigest, recompiled.CompilationDigest);
            Assert.AreEqual(result.Effects.Single().TargetBinding, recompiled.Effects.Single().TargetBinding);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Quality_level_effect_rejects_expressions_unknown_tiers_and_projection_tampering()
    {
        string directory = CreateTempDirectory();
        try
        {
            var (levels, qualities, _) = LoadQualityLevelSources();
            string[] rejected = ["0", "-1", "5", "1.5", "01", "Rating", "1+1", "$LEVEL", "{BOD}"];
            for (int i = 0; i < rejected.Length; i++)
            {
                var fixture = CreateGroupFixture(Path.Combine(directory, i.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    "<qualitylevel group=\"SINner\">" + rejected[i] + "</qualitylevel>", "");
                Assert.IsFalse(levels.TryResolveEffect(fixture.Ledger.ProjectedEffects.Single(), qualities, out _), rejected[i]);
            }
            var valid = CreateGroupFixture(Path.Combine(directory, "valid"),
                "<qualitylevel group=\"SINner\">1</qualitylevel>", "").Ledger.ProjectedEffects.Single();
            Assert.IsTrue(levels.TryResolveEffect(valid, qualities, out _));
            Assert.IsFalse(levels.TryResolveEffect(valid with { TargetId = "4" }, qualities, out _));
            Assert.IsFalse(levels.TryResolveEffect(valid with { AfterValue = "4" }, qualities, out _));
            Assert.IsFalse(levels.TryResolveEffect(valid with { BudgetDelta = 1 }, qualities, out _));
            Assert.IsFalse(levels.TryResolveEffect(valid with { Parameters = new Dictionary<string, string> { ["@group"] = "TrustFund" } }, qualities, out _));
            Assert.IsFalse(levels.TryResolveEffect(valid with { SourceAnchorIds = [] }, qualities, out _));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static (CharacterCreationFoundationQualityLevelSourceAuthority Levels,
        CharacterCreationFoundationQualitySourceAuthority Qualities, string Source) LoadQualityLevelSources()
    {
        string data = Path.Combine(FindCoreRoot(), "Chummer", "data");
        string source = File.ReadAllText(Path.Combine(data, "qualitylevels.xml"));
        string qualities = File.ReadAllText(Path.Combine(data, "qualities.xml"));
        Assert.IsTrue(CharacterCreationFoundationQualityLevelSourceAuthority.TryCreate(source,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(source), out var levels));
        Assert.IsTrue(CharacterCreationFoundationQualitySourceAuthority.TryCreate(qualities,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualities), out var qualityAuthority,
            new HashSet<string> { "SR5" }));
        return (levels!, qualityAuthority!, source);
    }
}
