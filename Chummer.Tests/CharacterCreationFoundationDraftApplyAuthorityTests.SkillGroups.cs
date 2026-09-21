using System.Globalization;
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
    public void Skill_group_level_plans_keep_version_order_and_group_identity_without_per_skill_expansion()
    {
        string directory = CreateTempDirectory();
        try
        {
            string skills = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "skills.xml"));
            string digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(skills);
            LevelWritePlanFixture fixture = CreateGroupFixture(directory,
                "<skillgrouplevel><name>Close Combat</name><val>2</val></skillgrouplevel>",
                "<skillgrouplevel><name>Outdoors</name></skillgrouplevel>"
                + "<skillgrouplevel><name>Close Combat</name><val>1</val></skillgrouplevel>");
            var first = BuildGroupPlan(fixture, skills, digest);
            Assert.IsTrue(first.IsReady, string.Join(",", first.Blockers));
            var plan = first.Plan!;
            Assert.HasCount(3, plan.ImprovementXml);
            XElement[] improvements = plan.ImprovementXml.Select(XElement.Parse).ToArray();
            CollectionAssert.AreEqual(new[] { "Close Combat", "Outdoors", "Close Combat" },
                improvements.Select(item => item.Element("improvedname")!.Value).ToArray());
            CollectionAssert.AreEqual(new[] { "2", "1", "1" },
                improvements.Select(item => item.Element("val")!.Value).ToArray());
            Assert.IsTrue(improvements.All(item => item.Element("improvementttype")!.Value == "SkillGroupLevel"
                && item.Element("sourcename")!.Value == plan.QualityId));
            CollectionAssert.AreEqual(new[] { "version", "module", "module" },
                plan.EffectProvenance.Select(item => item.SourcePhase).ToArray());
            Assert.IsTrue(plan.EffectProvenance.All(item => item.TargetBinding!.TargetKind == "skill-group"
                && item.TargetBinding.SourceDigest == digest));
            Assert.AreNotEqual(plan.InstructionDigests[0], plan.InstructionDigests[2]);
            Assert.AreEqual(plan.PlanDigest, BuildGroupPlan(fixture, skills, digest).Plan!.PlanDigest);
            Assert.AreEqual("LifeModule", XElement.Parse(plan.QualityXml).Element("qualitytype")!.Value);
            Assert.IsFalse(fixture.Ledger.CharacterEffectsApplied);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Skill_group_level_matches_legacy_int32_default_and_does_not_consume_pending_prompts()
    {
        string directory = CreateTempDirectory();
        try
        {
            string skills = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "skills.xml"));
            string digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(skills);
            (string? Raw, int Expected)[] cases = [(null, 1), ("2.00", 2), ("-1", -1), ("0", 0),
                ("invalid", 1), ("2147483648", 1), ("2.5", 1)];
            for (int i = 0; i < cases.Length; i++)
            {
                var fixture = CreateGroupFixture(Path.Combine(directory, i.ToString(CultureInfo.InvariantCulture)),
                    "<skillgrouplevel><name>Outdoors</name>"
                    + (cases[i].Raw is null ? "" : "<val>" + cases[i].Raw + "</val>") + "</skillgrouplevel>", "");
                var result = BuildGroupPlan(fixture, skills, digest);
                Assert.IsTrue(result.IsReady, string.Join(",", result.Blockers));
                Assert.AreEqual(cases[i].Expected.ToString(CultureInfo.InvariantCulture),
                    XElement.Parse(result.Plan!.ImprovementXml.Single()).Element("val")!.Value);
            }
            string[] rejected = [
                "<skillgrouplevel><selectskillgroup /><val>2</val></skillgrouplevel>",
                "<skillgrouplevel><name>Outdoors</name><spec>Forest</spec></skillgrouplevel>",
                "<skillgrouplevel><name>Outdoors</name><name>Athletics</name></skillgrouplevel>",
                "<skillgrouplevel><name>Unknown Group</name></skillgrouplevel>",
                "<skillgrouplevel><name>outdoors</name></skillgrouplevel>",
                "<skillgrouplevel><name> Outdoors </name></skillgrouplevel>",
                "<skillgrouplevel><name>$GROUP</name></skillgrouplevel>",
                "<skillgrouplevel rating=\"2\"><name>Outdoors</name></skillgrouplevel>"
            ];
            for (int i = 0; i < rejected.Length; i++)
            {
                var fixture = CreateGroupFixture(Path.Combine(directory, "reject-" + i),
                    "<attributelevel><name>BOD</name></attributelevel>", rejected[i]);
                var result = BuildGroupPlan(fixture, skills, digest);
                Assert.IsFalse(result.IsReady, rejected[i]);
                Assert.IsNull(result.Plan, "Do not emit a partial BOD plan when the group is unresolved.");
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Skill_group_level_rejects_stale_ambiguous_or_memberless_catalog_and_tampered_projection()
    {
        string directory = CreateTempDirectory();
        try
        {
            string skills = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "skills.xml"));
            string digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(skills);
            var fixture = CreateGroupFixture(directory,
                "<skillgrouplevel><name>Outdoors</name><val>2</val></skillgrouplevel>", "");
            Assert.IsFalse(BuildGroupPlan(fixture, skills + " ", digest).IsReady);
            Assert.IsFalse(CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(fixture.WorkspaceId,
                RulesetDefaults.Sr5, fixture.Ledger, fixture.Module, fixture.Version,
                fixture.EffectiveSourceXml, fixture.SourceDigest, "Chocolate").IsReady);

            for (int mode = 0; mode < 4; mode++)
            {
                XDocument mutated = XDocument.Parse(skills);
                XElement groups = mutated.Root!.Element("skillgroups")!;
                if (mode == 0) groups.Add(new XElement("name", "Outdoors"));
                if (mode == 1) groups.Remove();
                if (mode == 2)
                    foreach (XElement skill in mutated.Root.Element("skills")!.Elements("skill")
                        .Where(item => item.Element("skillgroup")?.Value == "Outdoors"))
                        skill.Element("skillgroup")!.Value = string.Empty;
                if (mode == 3)
                {
                    XElement duplicate = new(mutated.Root.Element("skills")!.Elements("skill")
                        .First(item => item.Element("skillgroup")?.Value == "Outdoors"));
                    duplicate.Element("id")!.Value = Guid.NewGuid().ToString("D");
                    mutated.Root.Element("skills")!.Add(duplicate);
                }
                string xml = mutated.ToString(SaveOptions.DisableFormatting);
                Assert.IsFalse(BuildGroupPlan(fixture, xml,
                    CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml)).IsReady,
                    "Invalid group catalog mode " + mode);
            }
            var effects = fixture.Ledger.ProjectedEffects.ToArray();
            effects[0] = effects[0] with { TargetId = "Athletics" };
            var tampered = fixture.Ledger with { ProjectedEffects = effects };
            tampered = tampered with { DraftDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeDigest(tampered) };
            Assert.IsFalse(BuildGroupPlan(fixture with { Ledger = tampered }, skills, digest).IsReady);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    public void Real_Military_Brat_and_Rural_modules_compile_their_literal_group_grants()
    {
        string root = FindCoreRoot();
        var catalog = new XmlLifeModulesCatalogService(Path.Combine(root, "Chummer", "data", "lifemodules.xml"));
        string skills = File.ReadAllText(Path.Combine(root, "Chummer", "data", "skills.xml"));
        Assert.IsTrue(CharacterCreationFoundationSkillSourceAuthority.TryCreate(skills,
            CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(skills), out var authority));
        foreach ((string id, string group, string value) in new[] {
            ("7acbd745-50d4-4fb1-839c-65a6f07e1e50", "Close Combat", "2"),
            ("0f415a05-5a6f-4279-9fb4-c43e0435bab5", "Outdoors", "1") })
        {
            LifeModuleLegalOptionDto module = catalog.GetOptionProjections(null, ["RF"]).Single(item => item.ModuleId == id);
            var draft = CreateCompilerDraft(module, catalog.GetAuthority().RawXmlDigest, "source-group-" + id);
            var compilation = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5,
                draft, module, null, authority);
            var effect = compilation.Effects.Single(item => item.EffectKind == "skillgrouplevel");
            Assert.AreEqual(CharacterCreationFoundationEffectCompilationStatuses.Supported, effect.CompilationStatus);
            Assert.AreEqual(group, effect.TargetBinding!.CanonicalName);
            Assert.AreEqual(value, effect.Parameters["val"]);
            Assert.IsFalse(compilation.IsCompleteLedgerSupported, "No partial character-application authority.");
        }
    }

    private static LevelWritePlanFixture CreateGroupFixture(string directory, string versionBonus, string moduleBonus)
        => CreateLevelWritePlanFixture(directory, "skill-group-plan", TirModuleId, "Group fixture",
            "Nationality", 15, "65", "13c614ba-e4c0-45e0-a203-3ba08e02f6ef", "Group variation",
            versionBonus, moduleBonus);

    private static CharacterCreationFoundationEffectWritePlanResult BuildGroupPlan(LevelWritePlanFixture fixture,
        string skills, string digest)
        => CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(fixture.WorkspaceId,
            RulesetDefaults.Sr5, fixture.Ledger, fixture.Module, fixture.Version, fixture.EffectiveSourceXml,
            fixture.SourceDigest, "Chocolate", skills, digest);
}
