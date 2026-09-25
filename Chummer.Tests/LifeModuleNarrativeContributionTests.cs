using System.Text.Json;
using Chummer.Application.LifeModules;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class LifeModuleNarrativeContributionTests
{
    private static LifeModuleDecisionAuthorityChoice Choice(string label = "Childhood") => new(
        "choice", label, "RF", "66", "command",
        new(40, "40", true, [], [], ["module:childhood"], "preview"), ["module:childhood"], [], true);

    private static LifeModuleEffectContribution Row(string kind, string name, decimal? amount) => new(
        "effect:" + kind, kind, "target:" + name, name, amount, "private-stack-selection",
        new Dictionary<string, string> { ["name"] = "Salish", ["group"] = "SINner" },
        ["effect:" + kind], CharacterCreationFoundationEffectCompilationStatuses.Supported, null);

    [TestMethod]
    [DataRow("en-US", "not final character ratings", "Knowledge-skill pool")]
    [DataRow("de-DE", "keine endgültigen Charakterwerte", "Wissensfertigkeiten-Pool")]
    [DataRow("es-ES", "no valores finales del personaje", "Reserva de conocimientos")]
    public void Narrative_facts_preserve_contributions_without_inventing_named_knowledge_or_final_ratings(
        string locale, string caveat, string poolLabel)
    {
        LifeModuleEffectContribution[] rows = [
            Row("attributelevel", "LOG", 1), Row("skilllevel", "Survival", 2),
            Row("skillgrouplevel", "Athletics", -1), Row("knowledgeskilllevel", "FreeKnowledgeSkills", 1),
            Row("qualitylevel", "SINner (National)", 1), Row("addqualities", "Distinctive Style", null),
            Row("freepositivequalities", "pool", 3), Row("freenegativequalities", "pool", 4),
            Row("pushtext", "raw-selection", null),
            Row("skilllevel", "NotGranted", 999) with
            { CompilationStatus = CharacterCreationFoundationEffectCompilationStatuses.Unsupported, Blocker = "unsupported" }
        ];
        var fact = CharacterCreationFoundationLifeModuleDecisionAuthority.CreateContributionFact(
            "life-module:childhood:1", "decision", Choice(), locale, rows);
        Assert.AreEqual("accepted-life-module-contributions", fact.FactKind);
        Assert.AreEqual("decision", fact.AcceptedDecisionId);
        StringAssert.Contains(fact.LocalizedSummary, caveat);
        StringAssert.Contains(fact.LocalizedSummary, poolLabel + ": +1");
        StringAssert.Contains(fact.LocalizedSummary, "LOG: +1");
        StringAssert.Contains(fact.LocalizedSummary, "Survival: +2");
        StringAssert.Contains(fact.LocalizedSummary, "Athletics: -1");
        StringAssert.Contains(fact.LocalizedSummary, "SINner (National)");
        StringAssert.Contains(fact.LocalizedSummary, "Distinctive Style");
        StringAssert.Contains(fact.LocalizedSummary, "40 Karma");
        foreach (string excluded in new[] { "Salish", "999", "NotGranted", "raw-selection", "private-stack-selection" })
            Assert.IsFalse(fact.LocalizedSummary.Contains(excluded), excluded);
        CollectionAssert.Contains(fact.SourceAnchorIds.ToArray(), "effect:skilllevel");
        Assert.AreEqual(JsonSerializer.Serialize(fact), JsonSerializer.Serialize(
            CharacterCreationFoundationLifeModuleDecisionAuthority.CreateContributionFact(
                "life-module:childhood:1", "decision", Choice(), locale, rows)));
        Assert.AreNotEqual(fact.FactId, CharacterCreationFoundationLifeModuleDecisionAuthority.CreateContributionFact(
            "life-module:childhood:2", "decision-2", Choice(), locale, rows).FactId);
    }

    [TestMethod]
    public void Missing_or_oversized_contributions_do_not_assert_partial_rewards()
    {
        foreach (var rows in new IReadOnlyList<LifeModuleEffectContribution>?[]
        {
            null, [Row("skilllevel", new string('x', 2100), 2)]
        })
        {
            var fact = CharacterCreationFoundationLifeModuleDecisionAuthority.CreateContributionFact(
                "module:1", "decision", Choice(), "en-US", rows);
            Assert.IsTrue(fact.LocalizedSummary.Length <= 2048);
            StringAssert.Contains(fact.LocalizedSummary, "Mechanical contributions unavailable; do not infer rewards.");
            Assert.IsFalse(fact.LocalizedSummary.Contains("+2"));
        }
    }
}
