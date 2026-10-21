using System.Reflection;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class CharacterCreationLifestyleInactiveEffectsTests
{
    [TestMethod]
    [DataRow("LifestyleCost", "", "-50")]
    [DataRow("BasicLifestyleCost", "", "-50")]
    [DataRow("LifestyleCost", "Metatype", "20")]
    [DataRow("BasicLifestyleCost", "Metatype", "20")]
    public void Valid_inactive_costs_preserve_creation_trust_fund_without_extra_costs(string type, string source, string value)
    {
        foreach (var activation in InactiveActivations())
        {
            XElement[] effects =
            [
                Effect("TrustFund", "1", condition: "create"),
                Effect("TrustFund", "2", enabled: "0"),
                Effect("TrustFund", "3", condition: "career"),
                Effect(type, value, activation.Enabled, activation.Condition, source)
            ];
            string scenario = $"{type} ({source}): enabled={activation.Enabled}, condition={activation.Condition}";
            Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolve(effects, 0,
                out int level, out var blockers), scenario + ": " + string.Join(',', blockers));
            Assert.AreEqual(1, level, scenario);
            Assert.AreEqual(0, blockers.Length, scenario);
            Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts(effects, 0,
                out level, out decimal percentage, out blockers), scenario + ": " + string.Join(',', blockers));
            Assert.AreEqual(1, level, scenario);
            Assert.AreEqual(0m, percentage, scenario);
            Assert.AreEqual(0, blockers.Length, scenario);
            Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(effects, [], 0,
                out level, out percentage, out var costs, out blockers), scenario + ": " + string.Join(',', blockers));
            Assert.AreEqual(1, level, scenario);
            Assert.AreEqual(0m, percentage, scenario);
            Assert.IsNull(costs, scenario);
            Assert.AreEqual(0, blockers.Length, scenario);
        }
    }

    [TestMethod]
    public void Inactive_trust_fund_effects_do_not_replace_existing_authority()
    {
        foreach (var activation in InactiveActivations())
        {
            var effect = Effect("TrustFund", "2", activation.Enabled, activation.Condition);
            Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts([effect], 1,
                out int level, out decimal percentage, out var blockers), string.Join(',', blockers));
            Assert.AreEqual(1, level);
            Assert.AreEqual(0m, percentage);
            Assert.AreEqual(0, blockers.Length);
        }
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("1")]
    [DataRow("2")]
    [DataRow(null)]
    public void Active_metatype_costs_are_retained_alongside_inactive_effects(string? enabled)
    {
        var active = Effect("LifestyleCost", "20", source: "Metatype");
        if (enabled is null) active.Element("enabled")!.Remove();
        else active.SetElementValue("enabled", enabled);
        XElement[] effects =
        [
            Effect("TrustFund", "1", condition: "create"), active,
            Effect("LifestyleCost", "-50", enabled: "0"),
            Effect("BasicLifestyleCost", "-25", condition: "career")
        ];
        Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts(effects, 0,
            out int level, out decimal percentage, out var blockers), string.Join(',', blockers));
        Assert.AreEqual(1, level);
        Assert.AreEqual(20m, percentage);
        Assert.AreEqual(0, blockers.Length);
        Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(effects, [], 0,
            out level, out percentage, out var costs, out blockers), string.Join(',', blockers));
        Assert.AreEqual(1, level);
        Assert.AreEqual(20m, percentage);
        Assert.IsNull(costs);
        Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolve(effects, 0, out level, out blockers));
        Assert.AreEqual(1, level);
        CollectionAssert.Contains(blockers, CharacterCreationLifestylesBlockers.UnsupportedSemantics);
    }

    [TestMethod]
    [DataRow("LifestyleCost")]
    [DataRow("BasicLifestyleCost")]
    public void Active_unsupported_and_unbacked_costs_still_block_creation(string type)
    {
        foreach (string enabled in new[] { "", "1", "2" })
        foreach (string condition in new[] { "", "create", "once" })
        foreach (string source in new[] { "", "Quality" })
            AssertUnsupported(Effect(type, "-50", enabled, condition, source));

        AssertUnsupported(Effect(type, "-50", source: "Metatype"));
        AssertUnsupported(Effect(type, "20", condition: "create", source: "Metatype"));
        AssertUnsupported(Effect(type, "20", condition: "once", source: "Metatype"));
        var named = Effect(type, "20", source: "Metatype");
        named.Add(new XElement("improvedname", "Low"));
        AssertUnsupported(named);
        var unique = Effect(type, "20", source: "Metatype");
        unique.Add(new XElement("unique", "exclusive"));
        AssertUnsupported(unique);
    }

    [TestMethod]
    [DataRow("LifestyleCost")]
    [DataRow("BasicLifestyleCost")]
    public void Invalid_activation_is_not_treated_as_inactive(string type)
    {
        foreach (string enabled in new[] { "true", "false", "invalid", "-1", "1.5", "2147483648" })
        foreach (string condition in new[] { "", "create", "career" })
            AssertUnsupported(Effect(type, "20", enabled, condition, "Metatype"));

        foreach (string condition in new[] { "unknown", "Career", "create,career" })
        foreach (string enabled in new[] { "0", "1" })
            AssertUnsupported(Effect(type, "20", enabled, condition, "Metatype"));
    }

    [TestMethod]
    [DataRow("LifestyleCost", "0", "")]
    [DataRow("LifestyleCost", "1", "career")]
    [DataRow("LifestyleCost", "1", "")]
    [DataRow("BasicLifestyleCost", "0", "")]
    [DataRow("BasicLifestyleCost", "1", "career")]
    [DataRow("BasicLifestyleCost", "1", "")]
    public void Inactivity_cannot_hide_duplicate_namespaced_or_malformed_cost_xml(string type, string enabled, string condition)
    {
        Action<XElement>[] changes =
        [
            effect => effect.Add(new XElement("val", "100")),
            effect => effect.Add(new XElement("enabled", "0")),
            effect => effect.Add(new XElement("condition", "career")),
            effect => effect.Add(new XElement("improvementsource", "Quality")),
            effect => effect.AddFirst(new XElement("improvementttype", "Unrelated")),
            effect => effect.Element("val")!.ReplaceNodes(new XElement("amount", "20")),
            effect => effect.Element("enabled")!.ReplaceNodes(new XElement("flag", "0")),
            effect => effect.Name = "other",
            effect => effect.Name = XName.Get("improvement", "urn:foreign"),
            effect => effect.Add(new XAttribute("enabled", "0")),
            effect => effect.Element("val")!.Add(new XAttribute("value", "20")),
            effect => effect.Element("improvementttype")!.Name = XName.Get("improvementttype", "urn:foreign"),
            effect => effect.Element("enabled")!.Name = XName.Get("enabled", "urn:foreign"),
            effect => effect.Element("condition")!.Name = XName.Get("condition", "urn:foreign")
        ];
        foreach (var change in changes)
        {
            var effect = Effect(type, "20", enabled, condition, "Metatype");
            change(effect);
            AssertUnsupported(effect);
        }
    }

    [TestMethod]
    [DataRow("lifestylecost")]
    [DataRow("basiclifestylecost")]
    public void Inactive_effects_do_not_waive_exact_purchased_quality_costs(string sourceElement)
    {
        var selection = QualityCostSelection(sourceElement);
        var (effects, instances) = QualityGraph(selection);
        Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryProjectPurchasedQualityCosts([selection], out var expected));
        Assert.IsNotNull(expected);
        Assert.AreEqual(2, expected.Occurrences.Count);
        Assert.IsTrue(expected.Occurrences.All(occurrence => occurrence.Percentage == -10m));
        XElement[] inactive =
        [
            Effect("TrustFund", "1", condition: "create"),
            Effect("TrustFund", "2", enabled: "0"),
            Effect("LifestyleCost", "-50", condition: "career"),
            Effect("BasicLifestyleCost", "-25", enabled: "0")
        ];
        Assert.IsTrue(CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(effects.Concat(inactive), instances, 0,
            out int level, out decimal percentage, out var actual, out var blockers), string.Join(',', blockers));
        Assert.AreEqual(1, level);
        Assert.AreEqual(0m, percentage);
        Assert.AreEqual(0, blockers.Length);
        Assert.IsTrue(CharacterCreationFoundationDraftLedgerIntegrity.CanonicallyEquals(expected, actual));

        foreach (var change in new (string Name, string Value)[]
        {
            ("enabled", "0"), ("condition", "career"), ("condition", "unknown"), ("val", "-20"),
            ("sourcename", "11111111-2222-4333-8444-555555555555"), ("improvementsource", "Metatype")
        })
        {
            var altered = effects.Select(effect => new XElement(effect)).ToArray();
            altered[0].SetElementValue(change.Name, change.Value);
            AssertSourceRejected(altered.Concat(inactive), instances, change.Name);
        }
        AssertSourceRejected(effects.Skip(1).Concat(inactive), instances, "missing occurrence");
        AssertSourceRejected(effects.Concat(effects.Take(1)).Concat(inactive), instances, "extra occurrence");
        AssertSourceRejected(effects.Concat(inactive), [], "unbacked owner");
        AssertSourceRejected(effects.Concat(inactive), instances.Concat(instances).ToArray(), "duplicate owner");
        var alteredSelection = selection with
        {
            SourceNodeDigest = CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("altered-source")
        };
        AssertSourceRejected(effects.Concat(inactive), [new(instances[0].Quality, 1, alteredSelection)], "altered source");

        foreach (var activation in InactiveActivations())
        {
            string type = sourceElement == "lifestylecost" ? "LifestyleCost" : "BasicLifestyleCost";
            var orphan = Effect(type, "-10", activation.Enabled, activation.Condition, "Quality");
            AssertSourceRejected([orphan], [], "inactive quality effect still requires a source");
        }
    }

    private static void AssertUnsupported(XElement effect)
    {
        string scenario = effect.ToString(SaveOptions.DisableFormatting);
        Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolve([effect], 0, out _, out var blockers), scenario);
        CollectionAssert.Contains(blockers, CharacterCreationLifestylesBlockers.UnsupportedSemantics, scenario);
        Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolveMetatypeCosts([effect], 0,
            out _, out _, out blockers), scenario);
        CollectionAssert.Contains(blockers, CharacterCreationLifestylesBlockers.UnsupportedSemantics, scenario);
        AssertSourceRejected([effect], [], scenario);
    }

    private static void AssertSourceRejected(IEnumerable<XElement> effects,
        IReadOnlyList<CharacterCreationLifestyleQualityInstance> instances, string scenario)
    {
        Assert.IsFalse(CharacterCreationLifestyleImprovementRules.TryResolveSourceBackedCosts(effects, instances, 0,
            out _, out _, out var costs, out var blockers), scenario);
        Assert.IsNull(costs, scenario);
        CollectionAssert.Contains(blockers, CharacterCreationLifestylesBlockers.UnsupportedSemantics, scenario);
    }

    private static (string Enabled, string Condition)[] InactiveActivations() =>
    [
        ("0", ""), ("0", "create"), ("0", "once"), ("0", "career"),
        ("", "career"), ("1", "career"), ("2", "career")
    ];

    private static XElement Effect(string type, string value, string enabled = "1", string condition = "", string source = "")
        => new("improvement", new XElement("improvementttype", type), new XElement("val", value),
            new XElement("enabled", enabled), new XElement("condition", condition), new XElement("improvementsource", source));

    private static CharacterCreationQualitySelection QualityCostSelection(string sourceElement)
    {
        Guid id = Guid.Parse("22222222-3333-4444-8555-666666666666");
        string effect = $"<{sourceElement}>-10</{sourceElement}>";
        string xml = $"<quality><id>{id:D}</id><name>Inactive cost fixture</name><karma>-10</karma><category>Negative</category>"
            + $"<bonus>{effect}</bonus><firstlevelbonus>{effect}</firstlevelbonus><source>RF</source><page>157</page></quality>";
        return new($"quality:{id:D}:1", id, id.ToString("D"), "Inactive cost fixture",
            CharacterCreationQualityType.Negative, 1, -10, false, true, true, false, null, null,
            [$"qualities.xml#quality:{id:D}"], xml, CharacterCreationQualitiesRules.ComputeSourceNodeDigest(xml),
            CharacterCreationLifestylesRules.ComputeIdempotencyKeyDigest("inactive-cost-fixture-option"));
    }

    private static (XElement[] Effects, CharacterCreationLifestyleQualityInstance[] Instances) QualityGraph(
        CharacterCreationQualitySelection selection)
    {
        var method = typeof(CharacterCreationLegacySourceProjector).GetMethod("TryBuildQualityGraph",
            BindingFlags.NonPublic | BindingFlags.Static, null,
            [typeof(CharacterCreationQualitySelection), typeof(string), typeof(XElement[]).MakeByRefType(), typeof(XElement[]).MakeByRefType()], null)!;
        object?[] arguments = [selection, selection.SourceNodeDigest, null, null];
        Assert.AreEqual(true, method.Invoke(null, arguments));
        var qualities = (XElement[])arguments[2]!;
        return ((XElement[])arguments[3]!, [new(qualities.Single(), 1, selection)]);
    }
}
