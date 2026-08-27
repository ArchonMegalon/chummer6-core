using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationGearRules
{
    private const string Prefix = "sha256:";

    public static string ReceiptLedgerRootDigest { get; } = ComputeUtf8(
        "chummer.sr5.creation-gear-receipt-ledger.root.v1");

    public static string ComputeOptionDigest(CharacterCreationGearCatalogOption value) =>
        Compute(value with { OptionDigest = string.Empty });

    public static string ComputeAuthorityDigest(CharacterCreationGearAuthority value) =>
        Compute(value with { AuthorityDigest = string.Empty });

    public static string ComputeLineDigest(CharacterCreationGearLine value) =>
        Compute(value with { LineDigest = string.Empty });

    public static string ComputeContributionDigest(CharacterCreationGearFinalizationContribution value) =>
        Compute(value with { ContributionDigest = string.Empty });

    public static string ComputeDraftDigest(CharacterCreationGearDraft value) =>
        Compute(value with { DraftDigest = string.Empty });

    public static string ComputeStateDigest(CharacterCreationGearState value) =>
        Compute(value with { SnapshotDigest = string.Empty });

    public static string ComputePreviewDigest(CharacterCreationGearPreview value) =>
        Compute(value with { PreviewDigest = string.Empty });

    public static string ComputeReceiptDigest(CharacterCreationGearReceipt value) =>
        Compute(value with { ReceiptDigest = string.Empty });

    public static string ComputeIdempotencyKeyDigest(string value) => ComputeUtf8(value);

    public static string ComputeCommandDigest(CharacterCreationGearConfirmRequest value) =>
        Compute(new
        {
            Schema = "chummer.sr5.creation-gear.command.v1",
            value.Binding,
            value.Basket,
            value.PreviewDigest
        });

    public static bool TryProjectBasket(
        IReadOnlyList<CharacterCreationGearSelection>? requested,
        CharacterCreationGearAuthority? authority,
        decimal totalStartingNuyen,
        out CharacterCreationGearLine[] lines,
        out CharacterCreationGearBudget budget,
        out string[] blockers)
    {
        var findings = new List<string>();
        var projected = new List<CharacterCreationGearLine>();
        if (!IsValidAuthority(authority) || totalStartingNuyen < 0m)
        {
            findings.Add(CharacterCreationGearBlockers.AuthorityUnavailable);
        }
        else if (requested is null || requested.Count > authority!.MaximumBasketLines)
        {
            findings.Add(CharacterCreationGearBlockers.InvalidBasket);
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, CharacterCreationGearCatalogOption> catalog = authority.Options
                .ToDictionary(item => item.OptionId, StringComparer.Ordinal);
            foreach (CharacterCreationGearSelection? selection in requested)
            {
                if (selection is null || string.IsNullOrWhiteSpace(selection.OptionId))
                {
                    findings.Add(CharacterCreationGearBlockers.InvalidOption);
                    continue;
                }
                if (!seen.Add(selection.OptionId))
                {
                    findings.Add(CharacterCreationGearBlockers.DuplicateOption);
                    continue;
                }
                if (selection.Quantity is < 1 || selection.Quantity > authority.MaximumQuantityPerLine)
                {
                    findings.Add(CharacterCreationGearBlockers.InvalidQuantity);
                    continue;
                }
                if (!catalog.TryGetValue(selection.OptionId, out CharacterCreationGearCatalogOption? option)
                    || !IsValidOption(option))
                {
                    findings.Add(CharacterCreationGearBlockers.InvalidOption);
                    continue;
                }
                if (!option.IsSelectable)
                {
                    findings.AddRange(option.Blockers.Count == 0
                        ? [CharacterCreationGearBlockers.UnsupportedSemantics]
                        : option.Blockers);
                    continue;
                }
                decimal lineCost;
                try
                {
                    lineCost = checked(option.PackageCost * selection.Quantity / option.PackageQuantity);
                }
                catch (OverflowException)
                {
                    findings.Add(CharacterCreationGearBlockers.InvalidQuantity);
                    continue;
                }
                var candidate = new CharacterCreationGearLine(
                    option.OptionId,
                    option.SourceId,
                    option.Name,
                    option.Category,
                    selection.Quantity,
                    option.PackageQuantity,
                    option.PackageCost,
                    lineCost,
                    option.Availability,
                    option.Legality,
                    option.SourceBook,
                    option.Page,
                    option.SourceAnchorIds,
                    string.Empty);
                projected.Add(candidate with { LineDigest = ComputeLineDigest(candidate) });
            }
        }

        decimal basketCost = 0m;
        try
        {
            basketCost = projected.Aggregate(
                0m,
                (sum, item) => checked(sum + item.TotalCost));
        }
        catch (OverflowException)
        {
            findings.Add(CharacterCreationGearBlockers.InvalidBasket);
        }
        decimal remaining = totalStartingNuyen - basketCost;
        decimal overspend = Math.Max(0m, -remaining);
        if (overspend > 0m)
            findings.Add(CharacterCreationGearBlockers.InsufficientFunds);
        blockers = Normalize(findings);
        lines = projected.OrderBy(item => item.OptionId, StringComparer.Ordinal).ToArray();
        budget = new CharacterCreationGearBudget(
            totalStartingNuyen,
            basketCost,
            Math.Max(0m, remaining),
            overspend,
            blockers.Length == 0,
            blockers);
        return blockers.Length == 0;
    }

    public static bool IsValidAuthority(CharacterCreationGearAuthority? authority)
    {
        if (authority is null
            || !authority.IsAuthoritative
            || !string.Equals(authority.Schema, CharacterCreationGearSchemas.AuthorityV1, StringComparison.Ordinal)
            || !string.Equals(authority.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authority.SettingsProfileId)
            || authority.MaximumAvailability < 0
            || authority.MaximumBasketLines is < 1 or > 4096
            || authority.MaximumQuantityPerLine is < 1 or > 1_000_000
            || authority.Options is not { Count: > 0 and <= 65_536 }
            || authority.Options.Any(option => !IsValidOption(option))
            || authority.Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count()
               != authority.Options.Count
            || authority.Options.Select(option => option.SourceId).Distinct().Count()
               != authority.Options.Count
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

    public static bool IsValidOption(CharacterCreationGearCatalogOption? option) =>
        option is not null
        && !string.IsNullOrWhiteSpace(option.OptionId)
        && option.SourceId != Guid.Empty
        && !string.IsNullOrWhiteSpace(option.Name)
        && !string.IsNullOrWhiteSpace(option.Category)
        && option.PackageCost >= 0m
        && option.PackageQuantity > 0
        && option.Availability >= 0
        && option.Legality is CharacterCreationGearLegality.Legal
            or CharacterCreationGearLegality.Restricted
            or CharacterCreationGearLegality.Forbidden
        && (!option.IsSelectable
            || !string.IsNullOrWhiteSpace(option.SourceBook)
               && !string.IsNullOrWhiteSpace(option.Page))
        && (!option.IsSelectable
            || option.PricingIsExact && option.AvailabilityIsExact && option.Blockers.Count == 0)
        && option.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
        && option.SourceAnchorIds is { Count: > 0 }
        && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && IsCanonicalDigest(option.SourceNodeDigest)
        && IsCanonicalDigest(option.OptionDigest)
        && DigestsEqual(option.OptionDigest, ComputeOptionDigest(option));

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

    private static string[] Normalize(IEnumerable<string> blockers) => blockers
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.Ordinal)
        .OrderBy(item => item, StringComparer.Ordinal)
        .ToArray();

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
                throw new InvalidOperationException("Unsupported creation-gear JSON value kind.");
        }
    }
}
