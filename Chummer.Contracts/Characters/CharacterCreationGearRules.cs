using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Chummer.Contracts.Characters;

public static class CharacterCreationGearRules
{
    private const string Prefix = "sha256:";
    public const int MaximumSourceNodeLength = 1_000_000;

    public static string ReceiptLedgerRootDigest { get; } = ComputeUtf8(
        "chummer.sr5.creation-gear-receipt-ledger.root.v1");

    public static string ComputeSourceNodeDigest(string sourceNodeXml) => ComputeDirect(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("Schema", "chummer.sr5.creation-gear.source-node.v1");
        writer.WriteString("Xml", sourceNodeXml ?? string.Empty);
        writer.WriteEndObject();
    });

    public static string ComputeOptionDigest(CharacterCreationGearCatalogOption value) =>
        ComputeDirect(writer => WriteOption(value, writer, string.Empty));

    public static string ComputeAuthorityDigest(CharacterCreationGearAuthority value) =>
        ComputeDirect(writer =>
        {
            writer.WriteStartObject();
            writer.WriteString("AuthorityDigest", string.Empty);
            WriteStrings(writer, "Blockers", value.Blockers);
            writer.WriteBoolean("IsAuthoritative", value.IsAuthoritative);
            writer.WriteNumber("MaximumAvailability", value.MaximumAvailability);
            writer.WriteNumber("MaximumBasketLines", value.MaximumBasketLines);
            writer.WriteNumber("MaximumQuantityPerLine", value.MaximumQuantityPerLine);
            writer.WritePropertyName("Options");
            if (value.Options is null) writer.WriteNullValue();
            else
            {
                writer.WriteStartArray();
                foreach (var option in value.Options)
                {
                    if (option is null) writer.WriteNullValue();
                    else WriteOption(option, writer, option.OptionDigest);
                }
                writer.WriteEndArray();
            }
            writer.WriteString("ProfileDigest", value.ProfileDigest);
            writer.WriteString("RulesDigest", value.RulesDigest);
            writer.WriteString("RulesetId", value.RulesetId);
            writer.WriteString("RuntimeDigest", value.RuntimeDigest);
            writer.WriteString("Schema", value.Schema);
            writer.WriteString("SettingsProfileId", value.SettingsProfileId);
            WriteStrings(writer, "SourceAnchorIds", value.SourceAnchorIds);
            writer.WriteString("SourceDigest", value.SourceDigest);
            writer.WriteEndObject();
        });

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
        => TryProjectBasketCore(requested, authority?.Options, authority?.MaximumBasketLines ?? 0,
            authority?.MaximumQuantityPerLine ?? 0, totalStartingNuyen, IsValidAuthority(authority),
            out lines, out budget, out blockers);

    // Shared pricing kernel. The ordinary and selected-history entry points
    // have separate validators; no public bypass or fabricated full catalog.
    private static bool TryProjectBasketCore(
        IReadOnlyList<CharacterCreationGearSelection>? requested,
        IReadOnlyList<CharacterCreationGearCatalogOption>? options,
        int maximumBasketLines,
        int maximumQuantityPerLine,
        decimal totalStartingNuyen,
        bool authorityValid,
        out CharacterCreationGearLine[] lines,
        out CharacterCreationGearBudget budget,
        out string[] blockers)
    {
        var findings = new List<string>();
        var projected = new List<CharacterCreationGearLine>();
        if (!authorityValid || totalStartingNuyen < 0m)
        {
            findings.Add(CharacterCreationGearBlockers.AuthorityUnavailable);
        }
        else if (requested is null || requested.Count > maximumBasketLines)
        {
            findings.Add(CharacterCreationGearBlockers.InvalidBasket);
        }
        else
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, CharacterCreationGearCatalogOption> catalog = options!
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
                if (selection.Quantity is < 1 || selection.Quantity > maximumQuantityPerLine)
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
                    option.SourceNodeXml,
                    option.SourceNodeDigest,
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

    public static string ComputeSelectedAuthorityDigest(CharacterCreationSelectedGearAuthority witness)
        => CharacterCreationFinalizationDigest.Compute(witness with { WitnessDigest = string.Empty });

    /// <summary>Capture only after validating the complete original catalog,
    /// including duplicate or malformed rows outside the requested closure.</summary>
    public static bool TryCaptureSelectedAuthority(CharacterCreationGearAuthority authority,
        IReadOnlyList<string> originalOptionIds, IReadOnlyList<string> proposedOptionIds,
        out CharacterCreationSelectedGearAuthority? witness)
    {
        witness = null;
        if (authority is null || !ValidSelectedIds(originalOptionIds, authority.MaximumBasketLines)
            || !ValidSelectedIds(proposedOptionIds, authority.MaximumBasketLines)) return false;
        try
        {
            // Own the exact values validated and retained, not caller-mutated
            // collections whose digest was checked before the copy.
            authority = JsonSerializer.Deserialize<CharacterCreationGearAuthority>(JsonSerializer.SerializeToUtf8Bytes(authority))!;
            string[] original = originalOptionIds.Order(StringComparer.Ordinal).ToArray();
            string[] proposed = proposedOptionIds.Order(StringComparer.Ordinal).ToArray();
            if (!IsValidAuthority(authority) || !ValidSelectedIds(original, authority.MaximumBasketLines)
                || !ValidSelectedIds(proposed, authority.MaximumBasketLines)) return false;
            var required = original.Concat(proposed).ToHashSet(StringComparer.Ordinal);
            var options = authority.Options.Where(option => required.Contains(option.OptionId))
                .OrderBy(option => option.OptionId, StringComparer.Ordinal).ToArray();
            if (options.Length != required.Count) return false;
            var candidate = new CharacterCreationSelectedGearAuthority(CharacterCreationSelectedGearAuthority.SchemaV1,
                authority.AuthorityDigest, authority.RulesetId, authority.SettingsProfileId,
                authority.MaximumAvailability, authority.MaximumBasketLines, authority.MaximumQuantityPerLine,
                original, proposed, options, authority.SourceAnchorIds, authority.SourceDigest,
                authority.ProfileDigest, authority.RulesDigest, authority.RuntimeDigest, string.Empty);
            candidate = candidate with { WitnessDigest = ComputeSelectedAuthorityDigest(candidate) };
            if (!IsValidSelectedAuthority(candidate)) return false;
            witness = candidate;
            return true;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        { return false; }
    }

    /// <summary>Checks only self-consistency of a historical witness, never
    /// whether the original catalog is currently admitted for a live mutation.</summary>
    public static bool IsValidSelectedAuthority(CharacterCreationSelectedGearAuthority? witness)
    {
        if (witness is null || witness.Schema != CharacterCreationSelectedGearAuthority.SchemaV1
            || !string.Equals(witness.RulesetId, "sr5", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(witness.SettingsProfileId)
            || witness.MaximumAvailability < 0 || witness.MaximumBasketLines is < 1 or > 4096
            || witness.MaximumQuantityPerLine is < 1 or > 1_000_000
            || !ValidSelectedIds(witness.OriginalOptionIds, witness.MaximumBasketLines)
            || !ValidSelectedIds(witness.ProposedOptionIds, witness.MaximumBasketLines)
            || !witness.OriginalOptionIds.SequenceEqual(witness.OriginalOptionIds.Order(StringComparer.Ordinal))
            || !witness.ProposedOptionIds.SequenceEqual(witness.ProposedOptionIds.Order(StringComparer.Ordinal))
            || witness.Options is null || witness.Options.Count > 8192
            || witness.Options.Any(option => !IsValidOption(option))
            || witness.Options.Select(option => option.OptionId).Distinct(StringComparer.Ordinal).Count() != witness.Options.Count
            || witness.Options.Select(option => option.SourceId).Distinct().Count() != witness.Options.Count
            || !witness.Options.Select(option => option.OptionId).SequenceEqual(
                witness.OriginalOptionIds.Concat(witness.ProposedOptionIds).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            || witness.SourceAnchorIds is not { Count: > 0 } || witness.SourceAnchorIds.Any(string.IsNullOrWhiteSpace)
            || !IsCanonicalDigest(witness.ProvenanceAuthorityDigest) || !IsCanonicalDigest(witness.SourceDigest)
            || !IsCanonicalDigest(witness.ProfileDigest) || !IsCanonicalDigest(witness.RulesDigest)
            || !IsCanonicalDigest(witness.RuntimeDigest) || !IsCanonicalDigest(witness.WitnessDigest)) return false;
        return DigestsEqual(witness.WitnessDigest, ComputeSelectedAuthorityDigest(witness));
    }

    /// <summary>Same exact pricing as ordinary Gear. This distinct entry point
    /// admits an empty retained set without inventing a sentinel catalog option.</summary>
    public static bool TryProjectSelectedBasket(IReadOnlyList<CharacterCreationGearSelection>? requested,
        CharacterCreationSelectedGearAuthority? witness, decimal totalStartingNuyen,
        out CharacterCreationGearLine[] lines, out CharacterCreationGearBudget budget, out string[] blockers)
        => TryProjectBasketCore(requested, witness?.Options, witness?.MaximumBasketLines ?? 0,
            witness?.MaximumQuantityPerLine ?? 0, totalStartingNuyen, IsValidSelectedAuthority(witness),
            out lines, out budget, out blockers);

    private static bool ValidSelectedIds(IReadOnlyList<string>? ids, int maximum)
        => maximum is >= 1 and <= 4096 && ids is not null && ids.Count <= maximum
            && ids.All(id => !string.IsNullOrWhiteSpace(id))
            && ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;

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
            || authority.Blockers is not { Count: 0 }
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
        && option.Blockers is not null
        && (!option.IsSelectable
            || !string.IsNullOrWhiteSpace(option.SourceBook)
               && !string.IsNullOrWhiteSpace(option.Page))
        && (!option.IsSelectable
            || option.PricingIsExact && option.AvailabilityIsExact && option.Blockers.Count == 0)
        && option.Blockers.All(blocker => !string.IsNullOrWhiteSpace(blocker))
        && option.SourceAnchorIds is { Count: > 0 }
        && option.SourceAnchorIds.All(anchor => !string.IsNullOrWhiteSpace(anchor))
        && option.SourceNodeXml is { Length: > 0 and <= MaximumSourceNodeLength }
        && IsCanonicalDigest(option.SourceNodeDigest)
        && DigestsEqual(option.SourceNodeDigest, ComputeSourceNodeDigest(option.SourceNodeXml))
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

    // Emit exactly the existing ordinal canonical JSON. Catalog hot paths need
    // no intermediate JSON DOM, decoded XML strings or per-property sorting.
    // The generic Compute implementation remains the compatibility oracle.
    private static string ComputeDirect(Action<Utf8JsonWriter> write)
    {
        using var buffer = new DirectHashBufferWriter();
        using (Utf8JsonWriter writer = new(buffer)) write(writer);
        return Prefix + buffer.GetDigest();
    }

    // Catalogs are revalidated, not cached. Hash the unchanged canonical bytes
    // as the writer commits them instead of allocating a whole catalog copy
    // for every source, option and authority check during finalization.
    private sealed class DirectHashBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private byte[] _buffer = ArrayPool<byte>.Shared.Rent(4096);

        public void Advance(int count)
        {
            if ((uint)count > (uint)_buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            _hash.AppendData(_buffer.AsSpan(0, count));
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer;
        }

        private void EnsureCapacity(int sizeHint)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
            if (sizeHint <= _buffer.Length) return;
            byte[] replacement = ArrayPool<byte>.Shared.Rent(sizeHint);
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
            _buffer = replacement;
        }

        public string GetDigest() => Convert.ToHexStringLower(_hash.GetHashAndReset());

        public void Dispose()
        {
            _hash.Dispose();
            ArrayPool<byte>.Shared.Return(_buffer, clearArray: true);
        }
    }

    private static void WriteOption(CharacterCreationGearCatalogOption value, Utf8JsonWriter writer, string? digest)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Availability", value.Availability);
        writer.WriteBoolean("AvailabilityIsExact", value.AvailabilityIsExact);
        WriteStrings(writer, "Blockers", value.Blockers);
        writer.WriteString("Category", value.Category);
        writer.WriteBoolean("IsSelectable", value.IsSelectable);
        writer.WriteString("Legality", value.Legality);
        writer.WriteString("Name", value.Name);
        writer.WriteString("OptionDigest", digest);
        writer.WriteString("OptionId", value.OptionId);
        writer.WriteNumber("PackageCost", value.PackageCost);
        writer.WriteNumber("PackageQuantity", value.PackageQuantity);
        writer.WriteString("Page", value.Page);
        writer.WriteBoolean("PricingIsExact", value.PricingIsExact);
        WriteStrings(writer, "SourceAnchorIds", value.SourceAnchorIds);
        writer.WriteString("SourceBook", value.SourceBook);
        writer.WriteString("SourceId", value.SourceId);
        writer.WriteString("SourceNodeDigest", value.SourceNodeDigest);
        writer.WriteString("SourceNodeXml", value.SourceNodeXml);
        writer.WriteEndObject();
    }

    private static void WriteStrings(Utf8JsonWriter writer, string name, IReadOnlyList<string>? values)
    {
        writer.WritePropertyName(name);
        if (values is null) writer.WriteNullValue();
        else
        {
            writer.WriteStartArray();
            foreach (string? value in values) writer.WriteStringValue(value);
            writer.WriteEndArray();
        }
    }

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
