#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Chummer.Rulesets.Sr6;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr6RuleFactRegistryTests
{
    [TestMethod]
    public void Generated_registry_loads_with_ready_verdict()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));

        Sr6RuleFactRegistry registry = Sr6RuleFactRegistry.Load(json);

        Assert.AreEqual(Sr6RuleFactRegistry.ExpectedSchema, registry.Schema);
        Assert.AreEqual("sr6", registry.Ruleset);
        Assert.AreEqual(Sr6RuleFactRegistry.ReadyVerdict, registry.FinalVerdict);
        Assert.IsTrue(registry.RuleFactCount >= 5);
        Assert.IsTrue(registry.RuleFacts.Any(fact => fact.Provider == "SR6DiceProvider"));
        Assert.IsTrue(registry.RuleFacts.All(fact => string.IsNullOrWhiteSpace(fact.SourceRef)));
    }

    [TestMethod]
    public void Generated_registry_requires_operator_gold_receipt_for_ready_claim()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));

        Sr6RuleFactRegistry registry = Sr6RuleFactRegistry.Load(json);

        CollectionAssert.Contains(registry.RuleFacts.Select(fact => fact.Provider).Distinct().ToArray(), "SR6ExplainReceiptProvider");
        CollectionAssert.Contains(registry.RuleFacts.Select(fact => fact.Provider).Distinct().ToArray(), "SR6DiceProvider");
        Assert.AreEqual(0, registry.MissingImplementedProviders.Count);
        Assert.AreEqual(Sr6RuleFactRegistry.ReadyVerdict, registry.FinalVerdict);

        string receiptJson = File.ReadAllText(FindRepoPath(".codex-studio", "published", "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json"));
        StringAssert.Contains(receiptJson, "\"final_verdict\": \"FULL_RULE_AUTHORITY_READY\"");
        StringAssert.Contains(receiptJson, "\"sourcebook_text_committed\": false");
    }

    [TestMethod]
    public void Generated_registry_rejects_rulefacts_with_mismatched_ruleset()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));
        JsonObject registry = JsonNode.Parse(json)!.AsObject();
        JsonArray ruleFacts = registry["rulefacts"]!.AsArray();
        JsonObject firstFact = ruleFacts[0]!.AsObject();

        firstFact["ruleset"] = "sr4";

        InvalidOperationException ex = CaptureLoadFailure(registry);
        StringAssert.Contains(ex.Message, "mismatched rulesets");
    }

    [TestMethod]
    public void Generated_registry_rejects_rulefacts_with_mismatched_book_profile()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR6_RULEFACT_REGISTRY.generated.json"));
        JsonObject registry = JsonNode.Parse(json)!.AsObject();
        JsonArray ruleFacts = registry["rulefacts"]!.AsArray();
        JsonObject firstFact = ruleFacts[0]!.AsObject();

        registry["book_profile"] = "core";
        firstFact["book_profile"] = "runnerhub";

        InvalidOperationException ex = CaptureLoadFailure(registry);
        StringAssert.Contains(ex.Message, "mismatched book profiles");
    }

    private static InvalidOperationException CaptureLoadFailure(JsonObject registry)
    {
        try
        {
            Sr6RuleFactRegistry.Load(registry.ToJsonString());
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }

        throw new AssertFailedException("Expected Sr6RuleFactRegistry.Load to reject the malformed registry.");
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
