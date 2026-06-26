#nullable enable annotations

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chummer.Rulesets.Sr5;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Chummer.Tests;

[TestClass]
public sealed class Sr5RuleAuthorityRegistryTests
{
    [TestMethod]
    public void Generated_registry_excludes_shell_catalog_metadata_from_rule_authority()
    {
        string json = File.ReadAllText(FindRepoPath(
            ".codex-studio",
            "published",
            "SR5_RULE_AUTHORITY_REGISTRY.generated.json"));

        Sr5RuleFactRegistry loaded = Sr5RuleFactRegistry.Load(json);
        using JsonDocument registry = JsonDocument.Parse(json);

        Assert.AreEqual(Sr5RuleFactRegistry.ReadyVerdict, loaded.FinalVerdict);
        Assert.AreEqual("sr5", loaded.Ruleset);
        Assert.IsTrue(loaded.RuleFactCount >= 100);
        Assert.IsTrue(loaded.RuleFacts.Any(fact => fact.Provider == "SR5DiceProvider"));
        Assert.IsTrue(loaded.RuleFacts.All(fact => !string.IsNullOrWhiteSpace(fact.SourceRef)));
        AssertExcludedShellCatalog(registry.RootElement);
    }

    [TestMethod]
    public void Runtime_rulefact_registry_excludes_shell_catalog_metadata_from_rule_authority()
    {
        string json = File.ReadAllText(FindRepoPath(
            ".codex-studio",
            "published",
            "rule-authority",
            "SR5_RULEFACT_REGISTRY.generated.json"));

        Sr5RuleFactRegistry loaded = Sr5RuleFactRegistry.Load(json);
        using JsonDocument registry = JsonDocument.Parse(json);

        Assert.AreEqual("pass", registry.RootElement.GetProperty("status").GetString());
        Assert.IsTrue(loaded.RuleFactCount >= 100);
        AssertExcludedShellCatalog(registry.RootElement);

        JsonElement excluded = registry.RootElement.GetProperty("excluded_inputs").EnumerateArray().Single();
        Assert.AreEqual("Chummer.Rulesets.Sr5/Sr5ShellCatalogs.cs", excluded.GetProperty("path").GetString());
        StringAssert.Contains(excluded.GetProperty("reason").GetString(), "UI/workbench metadata");
    }

    [TestMethod]
    public void Generated_registry_rejects_rulefacts_with_mismatched_ruleset()
    {
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR5_RULE_AUTHORITY_REGISTRY.generated.json"));
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
        string json = File.ReadAllText(FindRepoPath(".codex-studio", "published", "SR5_RULE_AUTHORITY_REGISTRY.generated.json"));
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
            Sr5RuleFactRegistry.Load(registry.ToJsonString());
        }
        catch (InvalidOperationException ex)
        {
            return ex;
        }

        throw new AssertFailedException("Expected Sr5RuleFactRegistry.Load to reject the malformed registry.");
    }

    private static void AssertExcludedShellCatalog(JsonElement registry)
    {
        string[] requiredProviders = registry.GetProperty("required_providers")
            .EnumerateArray()
            .Select(provider => provider.GetString() ?? string.Empty)
            .ToArray();
        string[] implementedProviders = registry.GetProperty("implemented_providers")
            .EnumerateArray()
            .Select(provider => provider.GetString() ?? string.Empty)
            .ToArray();
        string[] families = registry.GetProperty("rulefact_families")
            .EnumerateArray()
            .Select(family => family.GetString() ?? string.Empty)
            .ToArray();

        CollectionAssert.DoesNotContain(requiredProviders, "SR5ShellCatalogProvider");
        CollectionAssert.DoesNotContain(implementedProviders, "SR5ShellCatalogProvider");
        CollectionAssert.DoesNotContain(families, "shell_commands");
        CollectionAssert.DoesNotContain(families, "navigation_tabs");
        CollectionAssert.DoesNotContain(families, "workspace_actions");

        foreach (JsonElement fact in registry.GetProperty("rulefacts").EnumerateArray())
        {
            string id = fact.GetProperty("id").GetString() ?? string.Empty;
            string provider = fact.GetProperty("provider").GetString() ?? string.Empty;

            Assert.AreNotEqual("SR5ShellCatalogProvider", provider);
            Assert.IsFalse(id.StartsWith("sr5.shell.command.", StringComparison.Ordinal), id);
            Assert.IsFalse(id.StartsWith("sr5.navigation.tab.", StringComparison.Ordinal), id);
            Assert.IsFalse(id.StartsWith("sr5.workspace_action.", StringComparison.Ordinal), id);
        }
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
