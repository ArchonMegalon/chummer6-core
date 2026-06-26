using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Rulesets.Sr5;

public sealed record Sr5RuleFactRegistry(
    [property: JsonPropertyName("schema")] string? Schema,
    [property: JsonPropertyName("ruleset")] string? Ruleset,
    [property: JsonPropertyName("book_profile")] string BookProfile,
    [property: JsonPropertyName("final_verdict")] string FinalVerdict,
    [property: JsonPropertyName("rulefact_count")] int RuleFactCount,
    [property: JsonPropertyName("required_providers")] IReadOnlyList<string> RequiredProviders,
    [property: JsonPropertyName("implemented_providers")] IReadOnlyList<string> ImplementedProviders,
    [property: JsonPropertyName("missing_profile_status")] IReadOnlyList<string> MissingProfileStatus,
    [property: JsonPropertyName("missing_implemented_providers")] IReadOnlyList<string> MissingImplementedProviders,
    [property: JsonPropertyName("rulefacts")] IReadOnlyList<Sr5RuleFact> RuleFacts)
{
    public const string ExpectedSchema = "sr5-rule-authority-public-registry-v2";
    public const string LegacySchema = "sr5-rulefact-registry-v1";
    public const string NotReadyVerdict = "NOT_READY";
    public const string ReadyVerdict = "SR5_RULE_AUTHORITY_READY";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Sr5RuleFactRegistry Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("SR5 RuleFact registry JSON is required.", nameof(json));
        }

        Sr5RuleFactRegistry? registry = JsonSerializer.Deserialize<Sr5RuleFactRegistry>(json, SerializerOptions);
        if (registry is null)
        {
            throw new InvalidOperationException("SR5 RuleFact registry could not be parsed.");
        }

        registry = registry with
        {
            Schema = string.IsNullOrWhiteSpace(registry.Schema) ? ExpectedSchema : registry.Schema,
            Ruleset = string.IsNullOrWhiteSpace(registry.Ruleset) ? "sr5" : registry.Ruleset,
            RequiredProviders = registry.RequiredProviders ?? Array.Empty<string>(),
            ImplementedProviders = registry.ImplementedProviders ?? Array.Empty<string>(),
            MissingProfileStatus = registry.MissingProfileStatus ?? Array.Empty<string>(),
            MissingImplementedProviders = registry.MissingImplementedProviders ?? Array.Empty<string>(),
            RuleFacts = registry.RuleFacts ?? Array.Empty<Sr5RuleFact>(),
        };

        registry.Validate();
        return registry;
    }

    public void Validate()
    {
        if (!string.Equals(Schema, ExpectedSchema, StringComparison.Ordinal)
            && !string.Equals(Schema, LegacySchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported SR5 RuleFact registry schema '{Schema}'.");
        }

        if (!string.Equals(Ruleset, "sr5", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"SR5 RuleFact registry ruleset must be 'sr5', got '{Ruleset}'.");
        }

        if (RuleFacts.Count != RuleFactCount)
        {
            throw new InvalidOperationException("SR5 RuleFact registry count does not match the facts array.");
        }

        if (RuleFacts.Count == 0)
        {
            throw new InvalidOperationException("SR5 RuleFact registry must contain at least one seed fact.");
        }

        string[] duplicateIds = RuleFacts
            .GroupBy(static fact => fact.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"SR5 RuleFact registry contains duplicate ids: {string.Join(", ", duplicateIds)}.");
        }

        string[] missingProviders = RuleFacts
            .Where(static fact => string.IsNullOrWhiteSpace(fact.Provider))
            .Select(static fact => fact.Id)
            .ToArray();
        if (missingProviders.Length > 0)
        {
            throw new InvalidOperationException($"SR5 RuleFacts without providers: {string.Join(", ", missingProviders)}.");
        }

        string[] mismatchedRulesets = RuleFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Ruleset)
                && !string.Equals(fact.Ruleset, Ruleset, StringComparison.Ordinal))
            .Select(static fact => fact.Id)
            .ToArray();
        if (mismatchedRulesets.Length > 0)
        {
            throw new InvalidOperationException($"SR5 RuleFacts with mismatched rulesets: {string.Join(", ", mismatchedRulesets)}.");
        }

        string[] mismatchedBookProfiles = RuleFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.BookProfile)
                && !string.Equals(fact.BookProfile, BookProfile, StringComparison.Ordinal))
            .Select(static fact => fact.Id)
            .ToArray();
        if (mismatchedBookProfiles.Length > 0)
        {
            throw new InvalidOperationException($"SR5 RuleFacts with mismatched book profiles: {string.Join(", ", mismatchedBookProfiles)}.");
        }

        string[] missingSourceRefs = RuleFacts
            .Where(static fact => string.IsNullOrWhiteSpace(fact.SourceRef))
            .Select(static fact => fact.Id)
            .ToArray();
        if (missingSourceRefs.Length > 0)
        {
            throw new InvalidOperationException($"SR5 RuleFacts without source references: {string.Join(", ", missingSourceRefs)}.");
        }
    }
}

public sealed record Sr5RuleFact(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ruleset")] string Ruleset,
    [property: JsonPropertyName("book_profile")] string BookProfile,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("source_ref")] string SourceRef,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("seed_file")] string SeedFile,
    [property: JsonPropertyName("fact")] JsonElement Fact);
