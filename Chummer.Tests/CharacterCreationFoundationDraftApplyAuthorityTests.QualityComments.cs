using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Contracts.Rulesets;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Street_kid_quality_comments_preserve_Bad_Rep_without_granting_commented_Enemy()
    {
        string root = FindCoreRoot();
        var catalog = new XmlLifeModulesCatalogService(Path.Combine(root, "Chummer", "data", "lifemodules.xml"));
        var module = catalog.GetOptionProjections(null, ["RF"])
            .Single(item => item.ModuleId == "21c3cb79-e0d9-49b8-9ad3-9232bab4f12b");
        var projection = module.Effects.Single(item => item.TargetId == "addqualities");
        Assert.IsTrue(XElement.Parse(projection.RawXml).Nodes().OfType<XComment>().Any());
        StringAssert.Contains(projection.RawXml, "<addquality>Enemy</addquality>");
        string qualities = File.ReadAllText(Path.Combine(root, "Chummer", "data", "qualities.xml"));
        string digest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualities);
        Assert.IsTrue(CharacterCreationFoundationQualitySourceAuthority.TryCreate(qualities, digest, out var authority));
        var draft = CreateCompilerDraft(module, catalog.GetAuthority().RawXmlDigest, "street-kid-comments");
        var compilation = CharacterCreationFoundationEffectCompiler.Compile(RulesetDefaults.Sr5,
            draft, module, null, qualitySourceAuthority: authority);
        Assert.AreEqual(CharacterCreationFoundationEffectCompilationStatuses.Supported,
            compilation.Effects.Single(item => item.EffectKind == "addqualities").CompilationStatus);
        Assert.HasCount(1, compilation.DependentQualities);
        Assert.AreEqual("Bad Rep", compilation.DependentQualities[0].TargetBinding.CanonicalName);

        string directory = CreateTempDirectory();
        try
        {
            var fixture = CreateQualityCommentFixture(directory, projection.RawXml);
            var plan = BuildQualityCommentPlan(fixture, qualities, digest);
            Assert.IsTrue(plan.IsReady, string.Join(",", plan.Blockers));
            Assert.HasCount(1, plan.Plan!.DependentQualityXml);
            Assert.AreEqual("Bad Rep", XElement.Parse(plan.Plan.DependentQualityXml[0]).Element("name")!.Value);
            var notoriety = plan.Plan.ImprovementXml.Select(XElement.Parse)
                .Single(item => item.Element("improvementttype")!.Value == "Notoriety");
            Assert.AreEqual("3", notoriety.Element("val")!.Value);
            Assert.AreEqual(plan.Plan.PlanDigest, BuildQualityCommentPlan(fixture, qualities, digest).Plan!.PlanDigest);
            Assert.AreEqual(projection.RawXml, fixture.Ledger.ProjectedEffects.Single().RawXml,
                "Comments remain in the source authority; they are not normalized out of its digest.");
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [DataRow("<!--only a comment-->")]
    [DataRow("<addquality>Bad Rep</addquality><?unsupported instruction?>")]
    [DataRow("<addquality>Bad Rep</addquality>unexpected text")]
    [DataRow("<addquality>Bad Rep</addquality><unknown />")]
    [DataRow("<addquality>Bad Rep</addquality><addquality>Unknown quality</addquality>")]
    [DataRow("<addquality>Bad<!--not a container comment--> Rep</addquality>")]
    public void Quality_container_comments_do_not_relax_executable_node_validation(string content)
    {
        string directory = CreateTempDirectory();
        try
        {
            string qualities = File.ReadAllText(Path.Combine(FindCoreRoot(), "Chummer", "data", "qualities.xml"));
            var fixture = CreateQualityCommentFixture(directory, "<addqualities>" + content + "</addqualities>");
            var plan = BuildQualityCommentPlan(fixture, qualities,
                CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(qualities));
            Assert.IsFalse(plan.IsReady, content);
            Assert.IsNull(plan.Plan, "An invalid active node must not yield a partial quality plan.");
        }
        finally { Directory.Delete(directory, true); }
    }

    private static LevelWritePlanFixture CreateQualityCommentFixture(string directory, string bonus)
        => CreateLevelWritePlanFixture(directory, "quality-comments", TirModuleId, "Quality comments",
            "Teen Years", 50, "148", "13c614ba-e4c0-45e0-a203-3ba08e02f6ef", "Quality comments variation",
            string.Empty, bonus);

    private static CharacterCreationFoundationEffectWritePlanResult BuildQualityCommentPlan(
        LevelWritePlanFixture fixture, string qualities, string digest)
        => CharacterCreationFoundationLifeModuleQualityWritePlanner.Build(fixture.WorkspaceId,
            RulesetDefaults.Sr5, fixture.Ledger, fixture.Module, fixture.Version, fixture.EffectiveSourceXml,
            fixture.SourceDigest, "Chocolate", qualitiesSourceXml: qualities, qualitiesSourceDigest: digest);
}
