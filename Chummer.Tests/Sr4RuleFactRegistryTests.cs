#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using Chummer.Rulesets.Sr4;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr4RuleFactRegistryTests
{
    [TestMethod]
    public void Generated_registry_loads_with_ready_verdict()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        Assert.AreEqual(Sr4RuleFactRegistry.ExpectedSchema, registry.Schema);
        Assert.AreEqual("sr4", registry.Ruleset);
        Assert.AreEqual(Sr4RuleFactRegistry.ReadyVerdict, registry.FinalVerdict);
        Assert.IsTrue(registry.RuleFactCount >= 5);
        Assert.IsTrue(registry.RuleFacts.Any(fact => fact.Provider == "Sr4DiceProvider"));
        Assert.IsTrue(registry.RuleFacts.All(fact => fact.SourceRef.StartsWith("sr4a_core_2009:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generated_registry_requires_operator_gold_receipt_for_ready_claim()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        CollectionAssert.Contains(registry.RequiredProviders.ToArray(), "Sr4ExplainReceiptProvider");
        CollectionAssert.Contains(registry.ImplementedProviders.ToArray(), "Sr4DiceProvider");
        Assert.AreEqual(0, registry.MissingImplementedProviders.Count);
        Assert.AreEqual(Sr4RuleFactRegistry.ReadyVerdict, registry.FinalVerdict);

        string receiptJson = File.ReadAllText(FindRepoPath(".codex-studio", "published", "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json"));
        StringAssert.Contains(receiptJson, "\"final_verdict\": \"FULL_RULE_AUTHORITY_READY\"");
        StringAssert.Contains(receiptJson, "\"sourcebook_text_committed\": false");
    }

    private static string FindRepoPath(params string[] segments)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            string candidate = Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new FileNotFoundException($"Could not find repo file '{Path.Combine(segments)}'.");
    }
}
