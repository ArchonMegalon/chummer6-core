#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6RuleFactRegistryTests
{
    [TestMethod]
    public void Generated_registry_loads_with_not_ready_verdict()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));

        Sr6RuleFactRegistry registry = Sr6RuleFactRegistry.Load(json);

        Assert.AreEqual(Sr6RuleFactRegistry.ExpectedSchema, registry.Schema);
        Assert.AreEqual("sr6", registry.Ruleset);
        Assert.AreEqual(Sr6RuleFactRegistry.NotReadyVerdict, registry.FinalVerdict);
        Assert.IsTrue(registry.RuleFactCount >= 5);
        Assert.IsTrue(registry.RuleFacts.Any(fact => fact.Provider == "Sr6DiceProvider"));
        Assert.IsTrue(registry.RuleFacts.All(fact => fact.SourceRef.StartsWith("sr6_core_2019:", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Generated_registry_keeps_sr6_claim_bounded()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));

        Sr6RuleFactRegistry registry = Sr6RuleFactRegistry.Load(json);

        CollectionAssert.Contains(registry.RequiredProviders.ToArray(), "Sr6ExplainReceiptProvider");
        CollectionAssert.Contains(registry.ImplementedProviders.ToArray(), "Sr6DiceProvider");
        Assert.IsFalse(registry.MissingImplementedProviders.Count == 0 && registry.FinalVerdict == "SR6_RULE_AUTHORITY_READY");
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
