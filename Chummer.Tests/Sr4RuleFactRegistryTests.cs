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
    public void Generated_registry_loads_with_not_ready_verdict()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        Assert.AreEqual(Sr4RuleFactRegistry.ExpectedSchema, registry.Schema);
        Assert.AreEqual("sr4", registry.Ruleset);
        Assert.AreEqual(Sr4RuleFactRegistry.NotReadyVerdict, registry.FinalVerdict);
        Assert.IsTrue(registry.RuleFactCount >= 5);
        Assert.IsTrue(registry.RuleFacts.Any(fact => fact.Provider == "Sr4DiceProvider"));
        Assert.IsTrue(registry.RuleFacts.All(fact => fact.SourceRef.StartsWith("sr4a_core_2009:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generated_registry_keeps_sr4_claim_bounded()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        CollectionAssert.Contains(registry.RequiredProviders.ToArray(), "Sr4ExplainReceiptProvider");
        CollectionAssert.Contains(registry.ImplementedProviders.ToArray(), "Sr4DiceProvider");
        Assert.IsFalse(registry.MissingImplementedProviders.Count == 0 && registry.FinalVerdict == "SR4_RULE_AUTHORITY_READY");
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
