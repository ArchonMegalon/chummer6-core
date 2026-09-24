using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Free_quality_offsets_are_improvements_not_direct_karma_awards_and_reject_unresolved_values()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = CreateGroupFixture(directory,
                "<freepositivequalities>5</freepositivequalities>",
                "<freenegativequalities>-14</freenegativequalities><freenegativequalities>-2.5</freenegativequalities>");
            CharacterCreationFoundationEffectWritePlanResult Build(LevelWritePlanFixture current)
                => CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(current.WorkspaceId,
                    RulesetDefaults.Sr5, current.Ledger, current.Module, current.Version,
                    current.EffectiveSourceXml, current.SourceDigest, "Chocolate");
            var result = Build(fixture);
            Assert.IsTrue(result.IsReady, string.Join(",", result.Blockers));
            XElement[] improvements = result.Plan!.ImprovementXml.Select(XElement.Parse).ToArray();
            CollectionAssert.AreEqual(new[] { "FreePositiveQualities", "FreeNegativeQualities", "FreeNegativeQualities" },
                improvements.Select(item => item.Element("improvementttype")!.Value).ToArray());
            CollectionAssert.AreEqual(new[] { "5", "-14", "-2.5" },
                improvements.Select(item => item.Element("val")!.Value).ToArray());
            Assert.IsTrue(improvements.All(item => item.Element("improvedname")!.Value == string.Empty
                && item.Element("sourcename")!.Value == result.Plan.QualityId));
            Assert.IsTrue(result.Plan.EffectProvenance.All(item => item.TargetBinding!.SourceDigest == fixture.SourceDigest));
            Assert.IsFalse(fixture.Ledger.CharacterEffectsApplied);
            Assert.AreEqual(result.Plan.PlanDigest, Build(fixture).Plan!.PlanDigest);

            string[] rejected = [
                "<freenegativequalities>Rating + 5</freenegativequalities>",
                "<freenegativequalities />", "<freenegativequalities>$NQ</freenegativequalities>",
                "<freenegativequalities><val>-14</val></freenegativequalities>",
                "<freenegativequalities select=\"yes\">-14</freenegativequalities>",
                "<freenegativequalities xmlns=\"foreign\">-14</freenegativequalities>"
            ];
            for (int i = 0; i < rejected.Length; i++)
            {
                var bad = CreateGroupFixture(Path.Combine(directory, "bad-" + i),
                    "<attributelevel><name>STR</name></attributelevel>", rejected[i]);
                Assert.IsNull(Build(bad).Plan, "No partial grant for " + rejected[i]);
            }
            var effects = fixture.Ledger.ProjectedEffects.ToArray();
            effects[1] = effects[1] with { BudgetDelta = 14m };
            var changed = fixture.Ledger with { ProjectedEffects = effects };
            changed = changed with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(changed) };
            Assert.IsFalse(Build(fixture with { Ledger = changed }).IsReady);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Real_Military_Brat_has_a_complete_static_quality_graph_including_group_offset_and_Uncouth()
    {
        string root = FindCoreRoot();
        string catalogPath = Path.Combine(root, "Chummer", "data", "lifemodules.xml");
        var catalog = new XmlLifeModulesCatalogService(catalogPath);
        const string moduleId = "7acbd745-50d4-4fb1-839c-65a6f07e1e50";
        LifeModuleLegalOptionDto module = catalog.GetOptionProjections(null, ["RF"]).Single(item => item.ModuleId == moduleId);
        var draft = CreateCompilerDraft(module, catalog.GetAuthority().RawXmlDigest, "military-brat-graph");
        XElement source = XDocument.Load(catalogPath).Root!.Element("modules")!.Elements("module")
            .Single(item => item.Element("id")?.Value == moduleId);
        string skills = File.ReadAllText(Path.Combine(root, "Chummer", "data", "skills.xml"));
        string qualities = File.ReadAllText(Path.Combine(root, "Chummer", "data", "qualities.xml"));
        var result = CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(draft.WorkspaceId,
            RulesetDefaults.Sr5, draft, module, null, source.ToString(SaveOptions.DisableFormatting),
            draft.SourceDigest, "Chocolate", skills,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(skills), qualities,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualities));
        Assert.IsTrue(result.IsReady, string.Join(",", result.Blockers));
        var plan = result.Plan!;
        XElement quality = XElement.Parse(plan.QualityXml);
        Assert.AreEqual("Military Brat", quality.Element("name")!.Value);
        Assert.AreEqual("40", quality.Element("bp")!.Value);
        XElement[] improvements = plan.ImprovementXml.Select(XElement.Parse).ToArray();
        XElement group = improvements.Single(item => item.Element("improvementttype")!.Value == "SkillGroupLevel");
        Assert.AreEqual("Close Combat", group.Element("improvedname")!.Value);
        Assert.AreEqual("2", group.Element("val")!.Value);
        XElement offset = improvements.Single(item => item.Element("improvementttype")!.Value == "FreeNegativeQualities");
        Assert.AreEqual("-14", offset.Element("val")!.Value);
        XElement dependent = XElement.Parse(plan.DependentQualityXml.Single());
        Assert.AreEqual("Uncouth", dependent.Element("name")!.Value);
        // Legacy addqualities grants the quality for free. Its printed Karma
        // value is not a second budget award; the module has an explicit offset.
        Assert.AreEqual("0", dependent.Element("bp")!.Value);
        Assert.AreEqual("False", dependent.Element("contributetolimit")!.Value);
        string dependentId = dependent.Element("guid")!.Value;
        Assert.AreEqual(4, improvements.Count(item => item.Element("sourcename")!.Value == dependentId));
        Assert.AreEqual(9, improvements.Count(item => item.Element("sourcename")!.Value == plan.QualityId));
        XElement ownership = improvements.Single(item => item.Element("improvementttype")!.Value == "SpecificQuality");
        Assert.AreEqual(dependentId, ownership.Element("improvedname")!.Value);
        Assert.AreEqual(plan.QualityId, ownership.Element("sourcename")!.Value);
        Assert.AreEqual(0, plan.SelectionBindings.Count);
        Assert.AreEqual(0, plan.SelectionConsumers.Count);
        Assert.IsFalse(draft.CharacterEffectsApplied, "A plan is not an atomic character write.");
    }
}
