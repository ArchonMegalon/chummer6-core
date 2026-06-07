using System.Text.Json;
using System.Text.Json.Serialization;

namespace Chummer.Rulesets.Sr4;

public sealed record Sr4RuleFactRegistry(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("ruleset")] string Ruleset,
    [property: JsonPropertyName("book_profile")] string BookProfile,
    [property: JsonPropertyName("final_verdict")] string FinalVerdict,
    [property: JsonPropertyName("rulefact_count")] int RuleFactCount,
    [property: JsonPropertyName("required_providers")] IReadOnlyList<string> RequiredProviders,
    [property: JsonPropertyName("implemented_providers")] IReadOnlyList<string> ImplementedProviders,
    [property: JsonPropertyName("missing_profile_status")] IReadOnlyList<string> MissingProfileStatus,
    [property: JsonPropertyName("missing_implemented_providers")] IReadOnlyList<string> MissingImplementedProviders,
    [property: JsonPropertyName("rulefacts")] IReadOnlyList<Sr4RuleFact> RuleFacts)
{
    public const string ExpectedSchema = "sr4-rule-authority-public-registry-v2";
    public const string LegacySchema = "sr4-rulefact-registry-v1";
    public const string NotReadyVerdict = "NOT_READY";
    public const string ReadyVerdict = "SR4_RULE_AUTHORITY_READY";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static Sr4RuleFactRegistry Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("SR4 RuleFact registry JSON is required.", nameof(json));
        }

        Sr4RuleFactRegistry? registry = JsonSerializer.Deserialize<Sr4RuleFactRegistry>(json, SerializerOptions);
        if (registry is null)
        {
            throw new InvalidOperationException("SR4 RuleFact registry could not be parsed.");
        }

        registry = registry with
        {
            RequiredProviders = registry.RequiredProviders ?? Array.Empty<string>(),
            ImplementedProviders = registry.ImplementedProviders ?? Array.Empty<string>(),
            MissingProfileStatus = registry.MissingProfileStatus ?? Array.Empty<string>(),
            MissingImplementedProviders = registry.MissingImplementedProviders ?? Array.Empty<string>(),
            RuleFacts = registry.RuleFacts ?? Array.Empty<Sr4RuleFact>(),
        };

        registry.Validate();
        return registry;
    }

    public void Validate()
    {
        if (!string.Equals(Schema, ExpectedSchema, StringComparison.Ordinal)
            && !string.Equals(Schema, LegacySchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported SR4 RuleFact registry schema '{Schema}'.");
        }

        if (!string.Equals(Ruleset, "sr4", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"SR4 RuleFact registry ruleset must be 'sr4', got '{Ruleset}'.");
        }

        if (RuleFacts.Count != RuleFactCount)
        {
            throw new InvalidOperationException("SR4 RuleFact registry count does not match the facts array.");
        }

        if (RuleFacts.Count == 0)
        {
            throw new InvalidOperationException("SR4 RuleFact registry must contain at least one seed fact.");
        }

        string[] duplicateIds = RuleFacts
            .GroupBy(fact => fact.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException($"SR4 RuleFact registry contains duplicate ids: {string.Join(", ", duplicateIds)}.");
        }

        string[] missingProviders = RuleFacts
            .Where(fact => string.IsNullOrWhiteSpace(fact.Provider))
            .Select(fact => fact.Id)
            .ToArray();
        if (missingProviders.Length > 0)
        {
            throw new InvalidOperationException($"SR4 RuleFacts without providers: {string.Join(", ", missingProviders)}.");
        }

        string[] mismatchedRulesets = RuleFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.Ruleset)
                && !string.Equals(fact.Ruleset, Ruleset, StringComparison.Ordinal))
            .Select(fact => fact.Id)
            .ToArray();
        if (mismatchedRulesets.Length > 0)
        {
            throw new InvalidOperationException($"SR4 RuleFacts with mismatched rulesets: {string.Join(", ", mismatchedRulesets)}.");
        }

        string[] mismatchedBookProfiles = RuleFacts
            .Where(fact => !string.IsNullOrWhiteSpace(fact.BookProfile)
                && !string.Equals(fact.BookProfile, BookProfile, StringComparison.Ordinal))
            .Select(fact => fact.Id)
            .ToArray();
        if (mismatchedBookProfiles.Length > 0)
        {
            throw new InvalidOperationException($"SR4 RuleFacts with mismatched book profiles: {string.Join(", ", mismatchedBookProfiles)}.");
        }

        string[] missingSourceRefs = RuleFacts
            .Where(fact => string.IsNullOrWhiteSpace(fact.SourceRef))
            .Select(fact => fact.Id)
            .ToArray();
        if (string.Equals(Schema, LegacySchema, StringComparison.Ordinal)
            && missingSourceRefs.Length > 0)
        {
            throw new InvalidOperationException($"SR4 RuleFacts without source references: {string.Join(", ", missingSourceRefs)}.");
        }
    }
}

public sealed record Sr4RuleFact(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("ruleset")] string Ruleset,
    [property: JsonPropertyName("book_profile")] string BookProfile,
    [property: JsonPropertyName("provider")] string Provider,
    [property: JsonPropertyName("source_ref")] string SourceRef,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("seed_file")] string SeedFile,
    [property: JsonPropertyName("fact")] JsonElement Fact);
