using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationResourcesRules
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } = ComputeUtf8(
        "chummer.sr5.creation-resources-receipt-ledger.root.v1");

    public static string ComputeAuthorityDigest(CharacterCreationResourcesAuthority value) =>
        Compute(value with { AuthorityDigest = string.Empty });

    public static string ComputePriorityOptionDigest(CharacterCreationResourcePriorityOption value) =>
        Compute(value with { OptionDigest = string.Empty });

    public static string ComputeAllocationOptionDigest(CharacterCreationResourceAllocationOption value) =>
        Compute(value with { OptionDigest = string.Empty });

    public static string ComputeContributionDigest(
        CharacterCreationResourcesFinalizationContribution value) =>
        Compute(value with { ContributionDigest = string.Empty });

    public static string ComputeDraftDigest(CharacterCreationResourcesDraft value) =>
        Compute(value with { DraftDigest = string.Empty });

    public static string ComputeStateDigest(CharacterCreationResourcesState value) =>
        Compute(value with { SnapshotDigest = string.Empty });

    public static string ComputePreviewDigest(CharacterCreationResourcesPreview value) =>
        Compute(value with { PreviewDigest = string.Empty });

    public static string ComputeReceiptDigest(CharacterCreationResourcesReceipt value) =>
        Compute(value with { ReceiptDigest = string.Empty });

    public static string ComputeIdempotencyKeyDigest(string value) => ComputeUtf8(value);

    public static string ComputeCommandDigest(CharacterCreationResourcesConfirmRequest request) =>
        Compute(new
        {
            Schema = "chummer.sr5.creation-resources.command.v1",
            request.Binding,
            request.OptionId,
            request.PreviewDigest
        });

    public static bool IsCanonicalDigest(string? value)
    {
        if (value is not { Length: 71 } || !value.StartsWith(Prefix, StringComparison.Ordinal))
            return false;
        return value.AsSpan(Prefix.Length).ToArray().All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
    }

    public static bool DigestsEqual(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public static bool IsValidAuthority(CharacterCreationResourcesAuthority? authority)
    {
        if (authority is null
            || !authority.IsAuthoritative
            || !string.Equals(authority.Schema, CharacterCreationResourcesSchemas.AuthorityV1, StringComparison.Ordinal)
            || !string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || authority.BuildMethod is not (CharacterCreationBuildMethods.Priority
                or CharacterCreationBuildMethods.SumToTen)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || authority.KarmaToNuyenRate <= 0m
            || authority.MaximumKarmaInvestment < 0
            || authority.NuyenCarryover < 0m
            || authority.MaximumAvailability < 0
            || authority.UnrestrictedNuyen
            || authority.PriorityOptions is not { Count: > 0 and <= 64 }
            || authority.PriorityOptions.Any(option => !IsValidPriorityOption(option))
            || authority.PriorityOptions.Select(option => option.SourceId).Distinct(StringComparer.Ordinal).Count()
               != authority.PriorityOptions.Count
            || authority.SourceAnchorIds is not { Count: > 0 }
            || authority.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || authority.Blockers.Count != 0
            || !IsCanonicalDigest(authority.SourceDigest)
            || !IsCanonicalDigest(authority.ProfileDigest)
            || !IsCanonicalDigest(authority.RulesDigest)
            || !IsCanonicalDigest(authority.RuntimeDigest)
            || !IsCanonicalDigest(authority.AuthorityDigest))
            return false;
        return DigestsEqual(authority.AuthorityDigest, ComputeAuthorityDigest(authority));
    }

    public static bool IsValidPriorityOption(CharacterCreationResourcePriorityOption? option) =>
        option is not null
        && Guid.TryParseExact(option.SourceId, "D", out Guid sourceId)
        && sourceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(option.Rank)
        && option.BasePriorityNuyen >= 0m
        && IsCanonicalDigest(option.SourceNodeDigest)
        && option.SourceAnchorIds is { Count: > 0 }
        && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && IsCanonicalDigest(option.OptionDigest)
        && DigestsEqual(option.OptionDigest, ComputePriorityOptionDigest(option));

    public static string Compute<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
            WriteCanonical(root, writer);
        return Prefix + Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    public static string ComputeUtf8(string value) => Prefix
        + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty)));

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in element.EnumerateArray())
                    WriteCanonical(item, writer);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("Unsupported creation-resources JSON value kind.");
        }
    }
}
