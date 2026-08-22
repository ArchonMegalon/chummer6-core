using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chummer.Contracts.Characters;
using Chummer.Contracts.LifeModules;
using Chummer.Contracts.Workspaces;

namespace Chummer.Application.Characters;

internal static class CharacterCreationFoundationDraftLedgerIntegrity
{
    private const string Sha256Prefix = "sha256:";

    public static string ComputeRawCharacterXmlDigest(string xml)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(xml ?? string.Empty);
        return Sha256Prefix + Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    public static string ComputeDigest(CharacterCreationFoundationDraftLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        return Sha256Prefix + ComputeCanonicalSha256(ledger with { DraftDigest = string.Empty });
    }

    public static bool IsCanonicalDigest(string? value)
    {
        if (value is not { Length: 71 }
            || !value.StartsWith(Sha256Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in value.AsSpan(Sha256Prefix.Length))
        {
            bool isDigit = character is >= '0' and <= '9';
            bool isLowerHex = character is >= 'a' and <= 'f';
            if (!isDigit && !isLowerHex)
                return false;
        }

        return true;
    }

    public static bool IsValidPending(
        CharacterCreationFoundationDraftLedger? ledger,
        CharacterWorkspaceId workspaceId,
        long persistedContentRevision,
        string rawCharacterXmlDigest,
        string sourceDigest)
    {
        if (ledger is null
            || !string.Equals(
                ledger.Schema,
                CharacterCreationFoundationSchemas.DraftLedgerV1,
                StringComparison.Ordinal)
            || ledger.WorkspaceId != workspaceId
            || ledger.DraftRevision <= 0
            || ledger.BaseContentRevision <= 0
            || ledger.BaseContentRevision >= persistedContentRevision
            || !IsCanonicalDigest(ledger.BaseRawCharacterXmlDigest)
            || !IsCanonicalDigest(ledger.SourceDigest)
            || !FixedTimeEquals(ledger.BaseRawCharacterXmlDigest, rawCharacterXmlDigest)
            || !FixedTimeEquals(ledger.SourceDigest, sourceDigest)
            || !IsNormalizedNonEmpty(ledger.RequestedMetatype)
            || ledger.Selection is null
            || !IsNormalizedNonEmpty(ledger.Selection.ModuleId)
            || (ledger.Selection.VersionId is not null
                && !IsNormalizedNonEmpty(ledger.Selection.VersionId))
            || ledger.RequirementEvaluations is null
            || ledger.ProjectedEffects is null
            || ledger.FollowUpValues is null
            || ledger.SourceAnchorIds is null
            || !string.Equals(
                ledger.CompilationStatus,
                CharacterCreationFoundationDraftStatuses.PendingFinalization,
                StringComparison.Ordinal)
            || ledger.CharacterEffectsApplied
            || !IsCanonicalDigest(ledger.DraftDigest)
            || !FixedTimeEquals(ledger.DraftDigest, ComputeDigest(ledger)))
        {
            return false;
        }

        return ledger.RequirementEvaluations.All(IsStructurallyValid)
               && ledger.ProjectedEffects.All(IsStructurallyValid)
               && ledger.FollowUpValues.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null)
               && ledger.SourceAnchorIds.All(IsNormalizedNonEmpty);
    }

    public static bool HasSameLogicalPayload(
        CharacterCreationFoundationDraftLedger left,
        CharacterCreationFoundationDraftLedger right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        CharacterCreationFoundationDraftLedger normalizedLeft = left with
        {
            DraftRevision = 0,
            BaseContentRevision = 0,
            DraftDigest = string.Empty
        };
        CharacterCreationFoundationDraftLedger normalizedRight = right with
        {
            DraftRevision = 0,
            BaseContentRevision = 0,
            DraftDigest = string.Empty
        };
        return FixedTimeEquals(
            ComputeCanonicalSha256(normalizedLeft),
            ComputeCanonicalSha256(normalizedRight));
    }

    private static bool IsStructurallyValid(LifeModuleRequirementProjectionDto? requirement)
    {
        return requirement is not null
               && IsNormalizedNonEmpty(requirement.RequirementId)
               && requirement.DisableReasonArguments is not null
               && requirement.SourceAnchorIds is not null
               && IsNormalizedNonEmpty(requirement.Operator)
               && IsNormalizedNonEmpty(requirement.SubjectKind)
               && requirement.AcceptedValues is not null
               && requirement.RawXml is not null
               && requirement.DisableReasonArguments.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null)
               && requirement.SourceAnchorIds.All(IsNormalizedNonEmpty)
               && requirement.AcceptedValues.All(value => value is not null);
    }

    private static bool IsStructurallyValid(LifeModuleEffectProjectionDto? effect)
    {
        return effect is not null
               && IsNormalizedNonEmpty(effect.EffectId)
               && IsNormalizedNonEmpty(effect.Domain)
               && IsNormalizedNonEmpty(effect.TargetId)
               && effect.SourceAnchorIds is not null
               && effect.Parameters is not null
               && effect.RawXml is not null
               && effect.SourceAnchorIds.All(IsNormalizedNonEmpty)
               && effect.Parameters.All(item =>
                   IsNormalizedNonEmpty(item.Key) && item.Value is not null);
    }

    private static bool IsNormalizedNonEmpty(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
               && string.Equals(value, value.Trim(), StringComparison.Ordinal);
    }

    private static string ComputeCanonicalSha256<T>(T value)
    {
        JsonElement root = JsonSerializer.SerializeToElement(value);
        ArrayBufferWriter<byte> buffer = new();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(root, writer);
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }

    private static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in element
                             .EnumerateObject()
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
                throw new InvalidOperationException("Unsupported foundation-draft JSON value kind.");
        }
    }

    private static bool FixedTimeEquals(string? left, string? right)
    {
        byte[] leftBytes = Encoding.UTF8.GetBytes(left ?? string.Empty);
        byte[] rightBytes = Encoding.UTF8.GetBytes(right ?? string.Empty);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
