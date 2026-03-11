using System.Text.Json;
using Chummer.Contracts.Rulesets;

namespace Chummer.Application.Workspaces;

public static class WorkspaceRulesetDetection
{
    public static string? Detect(string? payloadKind, string? payload)
    {
        string? normalizedPayloadKind = RulesetDefaults.NormalizeOptional(payloadKind);
        if (normalizedPayloadKind is not null)
        {
            if (MatchesRulesetPayloadKind(normalizedPayloadKind, RulesetDefaults.Sr4))
            {
                return RulesetDefaults.Sr4;
            }

            if (MatchesRulesetPayloadKind(normalizedPayloadKind, RulesetDefaults.Sr5))
            {
                return RulesetDefaults.Sr5;
            }

            if (MatchesRulesetPayloadKind(normalizedPayloadKind, RulesetDefaults.Sr6))
            {
                return RulesetDefaults.Sr6;
            }
        }

        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        if (TryDetectJsonRulesetId(payload, out string? jsonRulesetId))
        {
            return jsonRulesetId;
        }

        if (ContainsAny(payload, ">SR4<", ">SR4A<", ">Shadowrun 4<", ">Shadowrun 4A<", ">Shadowrun 4th Edition<", ">Shadowrun Fourth Edition<"))
        {
            return RulesetDefaults.Sr4;
        }

        if (ContainsAny(payload, ">SR5<", ">Shadowrun 5<", ">Shadowrun 5th Edition<"))
        {
            return RulesetDefaults.Sr5;
        }

        if (ContainsAny(payload, ">SR6<", ">Shadowrun 6<", ">Shadowrun 6th Edition<"))
        {
            return RulesetDefaults.Sr6;
        }

        return null;
    }

    private static bool MatchesRulesetPayloadKind(string payloadKind, string rulesetId)
    {
        return string.Equals(payloadKind, rulesetId, StringComparison.Ordinal)
            || payloadKind.StartsWith($"{rulesetId}/", StringComparison.Ordinal);
    }

    private static bool ContainsAny(string payload, params string[] markers)
    {
        return markers.Any(marker => payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool TryDetectJsonRulesetId(string payload, out string? rulesetId)
    {
        rulesetId = null;
        ReadOnlySpan<char> trimmed = payload.AsSpan().TrimStart();
        if (trimmed.IsEmpty || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return TryFindRulesetId(document.RootElement, out rulesetId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryFindRulesetId(JsonElement element, out string? rulesetId)
    {
        rulesetId = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if ((property.NameEquals("rulesetId") || property.NameEquals("RulesetId"))
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        string? normalized = RulesetDefaults.NormalizeOptional(property.Value.GetString());
                        if (normalized is not null)
                        {
                            rulesetId = normalized;
                            return true;
                        }
                    }

                    if (TryFindRulesetId(property.Value, out rulesetId))
                    {
                        return true;
                    }
                }

                return false;

            case JsonValueKind.Array:
                foreach (JsonElement child in element.EnumerateArray())
                {
                    if (TryFindRulesetId(child, out rulesetId))
                    {
                        return true;
                    }
                }

                return false;

            default:
                return false;
        }
    }
}
