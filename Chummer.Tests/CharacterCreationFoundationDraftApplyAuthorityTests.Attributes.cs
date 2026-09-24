using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using Chummer.Application.Characters;
using Chummer.Contracts.Characters;
using Chummer.Infrastructure.Workspaces;
using Chummer.Infrastructure.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

public sealed partial class CharacterCreationFoundationDraftApplyAuthorityTests
{
    [TestMethod]
    public void Life_module_attributes_preview_real_grants_and_paid_levels_without_committing_or_choosing_talent()
    {
        string directory = CreateTempDirectory();
        try
        {
            var fixture = SeedFullGraph(directory);
            var (_, baseline) = BuildFullGraph(fixture.Store, fixture.Id);
            Assert.IsNotNull(baseline.AttributeQuote, string.Join(", ", baseline.FinalizationBlocked));
            Assert.HasCount(9, baseline.AttributeQuote.Attributes);
            Assert.IsFalse(baseline.AttributeQuote.Attributes.Any(row => row.AttributeId is "MAG" or "RES" or "ESS"));
            Assert.AreEqual(0m, baseline.AttributeQuote.KarmaUsed);
            var sources = baseline.ModuleSequence!.Occurrences.SelectMany(row => row.Compilation.Effects)
                .Where(effect => effect.EffectKind == "attributelevel").ToArray();
            foreach (var row in baseline.AttributeQuote.Attributes)
            {
                long granted = sources.Where(effect => effect.TargetId == row.AttributeId)
                    .Sum(effect => (long)CharacterCreationFoundationEffectCompiler.ParseLegacyAttributeLevelValue(
                        effect.Parameters.GetValueOrDefault("val")));
                Assert.AreEqual(granted, row.ModuleLevels);
                Assert.AreEqual((int)Math.Min(granted, row.Maximum - row.Minimum), row.AppliedModuleLevels);
                Assert.IsNotEmpty(row.SourceAnchorIds);
            }
            var service = CreateService(fixture.Store);
            var prompt = baseline.ModuleSequence.QualityLevels.Single().InstancePrompt!;
            var purchase = baseline.AttributeQuote.Attributes.First(row => row.Current < row.Maximum && row.ModuleLevels > 0);
            var request = QualityInstanceRequest(service, fixture.Id,
                new Dictionary<string, string> { [prompt.PromptId] = "Renraku" }) with
                { AttributePurchases = [new(purchase.AttributeId, 1)] };
            var preview = service.PreviewFinalization(request).Value!;
            var quote = preview.AttributeQuote!;
            Assert.IsNotNull(quote);
            var changed = quote.Attributes.Single(row => row.AttributeId == purchase.AttributeId);
            Assert.AreEqual(purchase.Current + 1, changed.Current);
            int costBase = quote.Policy.AlternateMetatypeAttributeKarma ? purchase.AppliedModuleLevels + 1 : purchase.Current;
            Assert.AreEqual((costBase + 1) * quote.Policy.KarmaAttribute, changed.KarmaCost);
            Assert.AreNotEqual(baseline.PreviewDigest, preview.PreviewDigest);
            Assert.AreEqual(CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(quote with { QuoteDigest = string.Empty }), quote.QuoteDigest);
            var omitted = service.ConfirmFinalization(new(request.Binding, request.DraftRevision, request.DraftDigest,
                preview.PreviewDigest, true) { QualityInstanceValues = request.QualityInstanceValues });
            CollectionAssert.Contains(omitted.Blockers.ToArray(), CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch);
            var exact = service.ConfirmFinalization(new(request.Binding, request.DraftRevision, request.DraftDigest,
                preview.PreviewDigest, true) { QualityInstanceValues = request.QualityInstanceValues, AttributePurchases = request.AttributePurchases });
            Assert.IsFalse(exact.Blockers.Contains(CharacterCreationFoundationBlockers.FinalizationPreviewDigestMismatch));
            Assert.IsFalse(preview.CanApply);
            var reopened = CreateService(new FileWorkspaceStore(directory)).PreviewFinalization(request).Value!;
            Assert.AreEqual(JsonSerializer.Serialize(quote), JsonSerializer.Serialize(reopened.AttributeQuote));
            CollectionAssert.AreEqual(fixture.Before, File.ReadAllBytes(WorkspacePath(directory, fixture.Id)));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow(1, 1, false, false, 5, 25)]
    [DataRow(1, 1, true, false, 5, 15)]
    [DataRow(1, 1, true, true, 5, 15)]
    [DataRow(20, 0, false, false, 8, 0)]
    [DataRow(-2, 1, false, false, 4, 20)]
    [DataRow(-2, 1, true, false, 4, 10)]
    public void Life_module_attributes_match_legacy_freebase_cap_and_cost_order(int grant, int bought,
        bool alternate, bool reverse, int expectedCurrent, int expectedCost)
    {
        // Deliberately synthetic bound compiler output tests the arithmetic;
        // the real-source service path is exercised independently above.
        var fixture = AttributeMathFixture(grant, "CHA");
        var policy = fixture.Policy with { AlternateMetatypeAttributeKarma = alternate, ReverseAttributePriorityOrder = reverse };
        policy = policy with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(policy) };
        var result = CharacterCreationLifeModuleAttributeRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial, policy, [new("CHA", bought)]);
        Assert.IsNotNull(result.Quote, string.Join(", ", result.Blockers));
        var row = result.Quote.Attributes.Single(item => item.AttributeId == "CHA");
        Assert.AreEqual(expectedCurrent, row.Current);
        Assert.AreEqual(expectedCost, row.KarmaCost);
        Assert.AreEqual((long)grant, row.ModuleLevels);
    }

    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("negative")]
    [DataRow("magic")]
    [DataRow("overflow")]
    [DataRow("wrong-method")]
    [DataRow("policy-digest")]
    [DataRow("policy-profile")]
    [DataRow("policy-inputs")]
    [DataRow("effect-digest")]
    [DataRow("cost-modifier")]
    [DataRow("conditional-grant")]
    [DataRow("unique-grant")]
    public void Life_module_attributes_reject_unresolved_or_changed_inputs(string fault)
    {
        var fixture = AttributeMathFixture(1, "CHA");
        IReadOnlyList<CharacterCreationLifeModuleAttributePurchase> purchases = fault switch
        {
            "duplicate" => [new("CHA", 1), new("CHA", 1)],
            "negative" => [new("CHA", -1)],
            "magic" => [new("MAG", 1)],
            "overflow" => [new("CHA", int.MaxValue)],
            _ => []
        };
        var policy = fixture.Policy;
        var effects = fixture.Effects;
        var racial = fixture.Racial;
        if (fault == "wrong-method")
        {
            policy = policy with { BuildMethod = CharacterCreationBuildMethods.Karma };
            policy = policy with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(policy) };
        }
        if (fault == "policy-digest") policy = policy with { KarmaAttribute = 1 };
        if (fault is "policy-profile" or "policy-inputs")
        {
            policy = fault == "policy-profile" ? policy with { SettingsProfileId = "different-profile" }
                : policy with { RawProfileInputsDigest = "sha256:" + new string('b', 64) };
            policy = policy with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(policy) };
        }
        if (fault == "effect-digest") effects = effects with { ImprovementXml = [] };
        if (fault is "cost-modifier" or "conditional-grant" or "unique-grant")
        {
            var xml = XElement.Parse(effects.ImprovementXml.Single());
            if (fault == "cost-modifier") xml.Element("improvementttype")!.Value = "AttributeKarmaCostMultiplier";
            else if (fault == "unique-grant") xml.Add(new XElement("unique", "group0"));
            else xml.Element("condition")!.Value = "create";
            effects = effects with { ImprovementXml = [xml.ToString(SaveOptions.DisableFormatting)] };
            (effects, racial) = SealAttributeMathPlans(effects, racial);
        }
        var result = CharacterCreationLifeModuleAttributeRules.Evaluate(fixture.Xml, effects, racial, policy, purchases);
        Assert.IsNull(result.Quote);
        Assert.IsNotEmpty(result.Blockers);
    }

    [TestMethod]
    public void Life_module_attributes_block_extra_paid_levels_at_cap_and_count_normal_maxima_only()
    {
        var fixture = AttributeMathFixture(20, "CHA");
        var result = CharacterCreationLifeModuleAttributeRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial, fixture.Policy, [new("CHA", 1)]);
        Assert.IsNotNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationAttributesBlockers.AllocationInvalid);
        var policy = fixture.Policy with { MaxNumberMaxAttributesCreate = 0 };
        policy = policy with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(policy) };
        result = CharacterCreationLifeModuleAttributeRules.Evaluate(fixture.Xml, fixture.Effects, fixture.Racial, policy, []);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationAttributesBlockers.MaximumAttributeCountExceeded);
        var edge = AttributeMathFixture(20, "EDG");
        result = CharacterCreationLifeModuleAttributeRules.Evaluate(edge.Xml, edge.Effects, edge.Racial, policy, []);
        Assert.IsEmpty(result.Blockers, "Maximum normal-attribute count excludes Edge.");
    }

    [TestMethod]
    public void Life_module_attributes_sum_distinct_grants_before_clamping_or_charging()
    {
        var fixture = AttributeMathFixture(7, "CHA");
        var negative = XElement.Parse(fixture.Effects.ImprovementXml.Single());
        negative.Element("val")!.Value = "-4";
        var effects = fixture.Effects with { ImprovementXml = [fixture.Effects.ImprovementXml.Single(), negative.ToString(SaveOptions.DisableFormatting)] };
        var (combined, racial) = SealAttributeMathPlans(effects, fixture.Racial);
        var policy = fixture.Policy with { AlternateMetatypeAttributeKarma = false };
        policy = policy with { AuthorityDigest = CharacterCreationAttributePolicyAuthority.ComputeDigest(policy) };
        var result = CharacterCreationLifeModuleAttributeRules.Evaluate(fixture.Xml, combined, racial, policy, [new("CHA", 1)]);
        Assert.IsNotNull(result.Quote);
        var row = result.Quote.Attributes.Single(item => item.AttributeId == "CHA");
        Assert.AreEqual(3L, row.ModuleLevels);
        Assert.AreEqual(3, row.AppliedModuleLevels);
        Assert.AreEqual(7, row.Current);
        Assert.AreEqual(35, row.KarmaCost);
    }

    [TestMethod]
    [DataRow("qualities")]
    [DataRow("improvements")]
    [DataRow("attributes")]
    public void Life_module_attributes_do_not_ignore_existing_saved_mechanics(string container)
    {
        var fixture = AttributeMathFixture(1, "CHA");
        var root = XElement.Parse(fixture.Xml);
        root.Add(container == "attributes"
            ? new XElement(container, new XElement("attribute", new XElement("name", "CHA"), new XElement("karma", "1")))
            : new XElement(container, new XElement(container == "qualities" ? "quality" : "improvement")));
        string xml = root.ToString(SaveOptions.DisableFormatting);
        var effects = fixture.Effects with { RawCharacterXmlDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml) };
        var (bound, racial) = SealAttributeMathPlans(effects, fixture.Racial);
        var result = CharacterCreationLifeModuleAttributeRules.Evaluate(xml, bound, racial, fixture.Policy, []);
        Assert.IsNull(result.Quote);
        CollectionAssert.Contains(result.Blockers.ToArray(), CharacterCreationAttributesBlockers.LegacyAttributeStateRequiresImport);
    }

    private static (string Xml, CharacterCreationFoundationSequenceWritePlan Effects,
        CharacterCreationLifeModuleMetatypeWritePlan Racial, CharacterCreationAttributePolicy Policy) AttributeMathFixture(int grant, string id)
    {
        string xml = CharacterXml("Elf");
        var context = new FileSystemCharacterSourceDataResolver(CreateOverlays()).TryCreateContext(xml)!;
        Assert.IsTrue(context.TryResolveCreationMetatypeCatalog(out var catalog));
        Assert.IsTrue(context.TryResolveCreationAttributePolicy(out var policy));
        var metatype = catalog.Options.Single(item => item.OptionId == ElfId);
        string digest = "sha256:" + new string('a', 64);
        string improvement = new XElement("improvement", new XElement("improvementttype", "Attributelevel"),
            new XElement("improvedname", id), new XElement("val", grant.ToString(CultureInfo.InvariantCulture)),
            new XElement("condition", ""), new XElement("enabled", "1")).ToString(SaveOptions.DisableFormatting);
        var effects = new CharacterCreationFoundationSequenceWritePlan("life-module-full-effect-graph/v1", new("attribute-math"),
            1, digest, CharacterCreationFoundationDraftLedgerIntegrity.ComputeRawCharacterXmlDigest(xml), digest,
            digest, digest, digest, [], [], [improvement], [], 0, 0, 0, "");
        var racial = new CharacterCreationLifeModuleMetatypeWritePlan("life-module-racial-contribution/v1", digest,
            "", metatype, catalog.SourceContext, [], [], [], [], [], "");
        (effects, racial) = SealAttributeMathPlans(effects, racial);
        return (xml, effects, racial, policy!);
    }

    private static (CharacterCreationFoundationSequenceWritePlan, CharacterCreationLifeModuleMetatypeWritePlan) SealAttributeMathPlans(
        CharacterCreationFoundationSequenceWritePlan effects, CharacterCreationLifeModuleMetatypeWritePlan racial)
    {
        effects = effects with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(effects with { PlanDigest = string.Empty }) };
        racial = racial with { EffectPlanDigest = effects.PlanDigest, PlanDigest = "" };
        racial = racial with { PlanDigest = CharacterCreationFoundationDraftLedgerIntegrity.ComputeCanonicalDigest(racial) };
        return (effects, racial);
    }
}
