#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Chummer.Rulesets.Sr4;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr4RuleFactRegistryTests
{
    [TestMethod]
    public void Generated_registry_loads_with_current_public_verdict()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        Assert.AreEqual(Sr4RuleFactRegistry.ExpectedSchema, registry.Schema);
        Assert.AreEqual("sr4", registry.Ruleset);
        Assert.AreEqual(Sr4RuleFactRegistry.NotReadyVerdict, registry.FinalVerdict);
        Assert.IsTrue(registry.RuleFactCount >= 100);
        Assert.IsTrue(registry.RuleFacts.Any(fact => fact.Provider == "Sr4DiceProvider"));
        Assert.IsTrue(registry.RuleFacts.All(fact => !string.IsNullOrWhiteSpace(fact.SourceRef)));
    }

    [TestMethod]
    public void Generated_registry_tracks_non_ready_public_rule_authority_truth()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));

        Sr4RuleFactRegistry registry = Sr4RuleFactRegistry.Load(json);

        CollectionAssert.Contains(registry.ImplementedProviders.ToArray(), "Sr4ExplainReceiptProvider");
        CollectionAssert.Contains(registry.RuleFacts.Select(fact => fact.Provider).Distinct().ToArray(), "Sr4DiceProvider");
        Assert.AreEqual(0, registry.MissingImplementedProviders.Count);
        Assert.AreEqual(Sr4RuleFactRegistry.NotReadyVerdict, registry.FinalVerdict);

        string receiptJson = File.ReadAllText(FindRepoPath(".codex-studio", "published", "OPERATOR_PROMOTED_RULE_AUTHORITY_GOLD.generated.json"));
        StringAssert.Contains(receiptJson, "\"final_verdict\": \"NOT_READY\"");
        StringAssert.Contains(receiptJson, "\"sourcebook_text_committed\": false");
    }

    [TestMethod]
    public void Generated_registry_rejects_rulefacts_with_mismatched_ruleset_or_book_profile()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));
        JsonObject registry = JsonNode.Parse(json)!.AsObject();
        JsonArray ruleFacts = registry["rulefacts"]!.AsArray();
        JsonObject firstFact = ruleFacts[0]!.AsObject();

        firstFact["ruleset"] = "sr6";

        InvalidOperationException ex = CaptureLoadFailure(registry);
        StringAssert.Contains(ex.Message, "mismatched rulesets");
    }

    [TestMethod]
    public void Generated_registry_rejects_rulefacts_with_mismatched_book_profile()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR4_RULEFACT_REGISTRY.generated.json"));
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
            Sr4RuleFactRegistry.Load(registry.ToJsonString());
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }

        throw new AssertFailedException("Expected Sr4RuleFactRegistry.Load to reject the malformed registry.");
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
